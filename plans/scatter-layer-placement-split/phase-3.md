---
phase: R3
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: S (~0.5 day)
unity instance: GrassInteract@de203215 (port 6403)
depends on: R1 + R2 complete
risk: highest in plan (mitigates score-20 MonoScript GUID risk)
---

# R3 — Migration Menu + Run on Demo Asset + Verify

## Goal

Build the one-shot migration menu that walks every TerrainScatterConfig and converts each legacy-base-type ScatterLayer sub-asset into the correct concrete subclass (DensityScatterLayer or InstanceScatterLayer). Then RUN it on the demo asset and verify visual parity vs baseline. This phase fully retires the score-20 MonoScript-GUID risk before R5 promotes the base to abstract.

R3 is the most critical phase in the plan. If R3 verification fails, R5 cannot proceed.

## Scope

**IN:**
- New file `Editor/MigrateScatterLayerTypes.cs` - menu item + migration logic + per-asset JSON backup.
- New directory `plans/scatter-layer-placement-split/backups/` for per-asset backup .json snapshots.
- Run the menu on the demo asset. The demo currently has HasAuthoredInstances == false per brainstorm scout, so its sub-asset migrates to DensityScatterLayer.
- Verification that demo loads + renders identical to baseline screenshot.

**OUT:**
- Removing the legacy CreateAssetMenu from base ScatterLayer (R5).
- Removing the targetInstances Obsolete stack (R5).
- Promoting ScatterLayer to abstract (R5).
- Type-tightening consumers (R4).
- Touching any runtime code.

## File Ownership

| File | Action | Notes |
|---|---|---|
| Editor/MigrateScatterLayerTypes.cs | CREATE | MenuItem at Tools/GrassInteract/Migrate/ScatterLayer Assets -> Typed. Walks AssetDatabase.FindAssets t:TerrainScatterConfig, inspects each sub-asset, migrates if exact type == base ScatterLayer (NOT a subclass). |
| plans/scatter-layer-placement-split/backups/ | CREATE (dir) | Holds per-asset config-guid + sub-asset-name .json backup before any destructive op. |
| Runtime/ScatterLayer.cs | NO edit | Stays concrete through R3. |

## Step-by-Step Tasks

1. **Read Runtime/ScatterLayer.cs** - confirm HasAuthoredInstances accessor still exists (needed to pick target subclass type).
2. **Read Runtime/DensityScatterLayer.cs + Runtime/InstanceScatterLayer.cs** - confirm both exist from R2.
3. **Locate demo asset path** - typically under Assets/Resources/ or Assets/Demos/. Use AssetDatabase.FindAssets with filter t:TerrainScatterConfig.
4. **Author Editor/MigrateScatterLayerTypes.cs:**
   - `#nullable enable`.
   - `using UnityEngine; using UnityEditor; using System.IO;`
   - `[MenuItem("Tools/GrassInteract/Migrate/ScatterLayer Assets -> Typed")]` static method.
   - Steps inside the menu method (per brainstorm):
     1. Find all TerrainScatterConfig GUIDs via AssetDatabase.FindAssets.
     2. For each config, load AssetDatabase.LoadAllAssetsAtPath(path). Filter to entries where obj.GetType() == typeof(ScatterLayer) EXACTLY (not subclass - already-migrated entries are subclasses and a no-op).
     3. For each legacy entry:
        a. Compute target type: oldLayer.HasAuthoredInstances ? typeof(InstanceScatterLayer) : typeof(DensityScatterLayer).
        b. Backup: write EditorJsonUtility.ToJson(oldLayer, prettyPrint:true) to plans/scatter-layer-placement-split/backups/CONFIG-GUID-SUBASSET-NAME.json.
        c. var newLayer = ScriptableObject.CreateInstance(targetType);
        d. var json = EditorJsonUtility.ToJson(oldLayer); EditorJsonUtility.FromJsonOverwrite(json, newLayer); - exact-name round-trip; copies all 26 shared fields + density-specific (or instance-specific) tail.
        e. newLayer.name = oldLayer.name;
        f. AssetDatabase.AddObjectToAsset(newLayer, configPath);
        g. Find index in config.layers -> overwrite with newLayer.
        h. EditorUtility.SetDirty(config);
        i. AssetDatabase.RemoveObjectFromAsset(oldLayer);
     4. AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
     5. End-of-run tally via Debug.Log (migrated: N, skipped-already-typed: M, configs-processed: K).
   - Provide a `[MenuItem("Tools/GrassInteract/Migrate/ScatterLayer Assets -> Typed (Dry Run)")]` sibling for dry-run that logs intended swaps without mutating.
5. **Pre-migration probe:** before running menu, capture a screenshot of demo via UnityMCP screenshot_editor to plans/scatter-layer-placement-split/screenshots/phase-3-pre.png. This becomes the visual-parity anchor.
6. **Run dry-run menu first** to confirm expected target counts.
7. **Run real migration menu** on demo. Capture console output.
8. **Re-open demo scene** (or force re-serialization via AssetDatabase.Refresh) to confirm migrated asset loads cleanly. No errors in console.
9. **Visual parity:** screenshot demo -> plans/scatter-layer-placement-split/screenshots/phase-3-render.png. Compare vs plans/authored-instance-scatter-editor/screenshots/phase-5-before.png - must be byte-identical (procedural seed unchanged; same fields).
10. **Asset-presence inspection:** in Unity Editor, expand demo TerrainScatterConfig asset header - confirm sub-asset row icon/type label reads DensityScatterLayer, NOT ScatterLayer.
11. **Write phase-3-report.md:** migration counts, backup file list, demo asset before/after sub-asset type, screenshot diff result.

## Verification Gate

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` - FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=30)` - 0 NEW project errors. Migration Debug.Log lines are allowed.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` - no `[Parity] ERROR`.
5. Screenshot game-view -> `plans/scatter-layer-placement-split/screenshots/phase-3-render.png` - visual parity vs `plans/authored-instance-scatter-editor/screenshots/phase-5-before.png`.
6. **Asset-presence (MANDATORY for R3):** demo TerrainScatterConfig carries DensityScatterLayer sub-asset (not legacy ScatterLayer). Verify by reading the .asset YAML via Read tool - look for the m_Script field guid; it must match the MonoScript GUID of DensityScatterLayer.cs, NOT the legacy ScatterLayer.cs MonoScript GUID.

## Exit Criteria

- Migration menu compiles + runs cleanly on demo.
- Demo asset sub-asset type changed from ScatterLayer to DensityScatterLayer.
- Per-asset .json backup exists under plans/scatter-layer-placement-split/backups/.
- Demo renders visually identical to baseline.
- Parity harness PASS.
- All 26 shared + density-specific fields preserved byte-equal pre/post (verify by diffing the backup .json against EditorJsonUtility.ToJson(newLayer) post-migration - manual or scripted).
- phase-3-report.md written.

## Rollback Plan

For each migrated asset:
1. Read backup plans/scatter-layer-placement-split/backups/CONFIG-GUID-SUBASSET-NAME.json.
2. var revivedLayer = ScriptableObject.CreateInstance<ScatterLayer>(); EditorJsonUtility.FromJsonOverwrite(json, revivedLayer);
3. Swap config entry back to revived legacy-base instance; RemoveObjectFromAsset(newLayer); AddObjectToAsset(revivedLayer, configPath).

Backup files are NOT cleaned up - keep them through R6 in case R4/R5 reveal a hidden field-loss bug.

## Anti-Stall Guard Reminders

- **First MCP call = set_active_instance unity_instance="GrassInteract@de203215".** Migrating against the wrong instance corrupts a different demo asset.
- **No progress narration.** Read -> edit -> next.
- **150K commit checkpoint.** If you reach ~150K mid-phase, write phase-3-report.md (in-progress + which configs migrated, which pending) and exit. R3 is the most critical phase - partial migration is recoverable via backups, but only if state is documented.
- **Dry-run first, real migration second.** Never run the real migration without dry-run output reviewed.
- **No Unity restart, no Assets/Reimport All.** refresh_unity only.
- **ScatterLayer stays concrete after R3.** Do NOT add abstract keyword. That is R5 work.
- **Edits-only-in-subagent.** Verification gate = main-loop responsibility AFTER your report writes.
