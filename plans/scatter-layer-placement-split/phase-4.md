---
phase: R4
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: M (~1 day)
unity instance: GrassInteract@de203215 (port 6403)
depends on: R3 complete (demo asset migrated + verified)
---

# R4 - Type-Tighten Consumers + Move Type-Specific Fields

## Goal

Replace every HasAuthoredInstances read with `is InstanceScatterLayer` pattern matching. Narrow editor APIs that should only ever accept authored data (ScatterBrush, InstancePickingService, InstanceSelectionOverlay, ScatterBakeToAuthored) to take InstanceScatterLayer rather than the base. Move type-specific serialized fields DOWN the hierarchy:

- densityMap + targetInstances -> DensityScatterLayer
- authoredInstances + placeSpacing -> InstanceScatterLayer
- 26 shared fields stay on base ScatterLayer.

R4 is the largest phase (~8 file edits). ScatterLayer STAYS CONCRETE; the Obsolete-stack on base stays in place. R5 promotes to abstract + deletes the stack.

## Scope

**IN:**
- Field migration on DensityScatterLayer.cs (add densityMap + targetInstances, declared identical to current base copies).
- Field migration on InstanceScatterLayer.cs (add authoredInstances + placeSpacing).
- Type-tighten ~8 consumers; replace HasAuthoredInstances reads with `is InstanceScatterLayer`.
- ScatterField.cs, TerrainScatterConfigEditor.cs, ScatterBrush.cs, InstancePickingService.cs, InstanceSelectionOverlay.cs, ScatterBakeToAuthored.cs API narrowing.
- Update DensityPlacement constructor to take DensityScatterLayer (was ScatterLayer in R1). Same for InstancePlacement -> InstanceScatterLayer.

**OUT:**
- Removing HasAuthoredInstances accessor from base (R5 - last consumer migrated here).
- Removing targetInstances Obsolete stack from base (R5).
- Promoting ScatterLayer to abstract (R5).
- Removing densityMap, authoredInstances, placeSpacing from base (R5 - hard deletion).
- Wiring GrassScatter.Build to facade (R5).
- Engines (MeshScatterEngine, GrassGpuEngine, GrassCpuEngine) - signatures unchanged; ~95% of layer.X reads hit base, no touch needed.

## File Ownership

| File | Action | Notes |
|---|---|---|
| Runtime/DensityScatterLayer.cs | extend | Add `[SerializeField] private Texture2D? densityMap;` plus `[SerializeField, Range(1, 100000)] private int targetInstances = 1000;` plus public accessors matching base names. Add Validate override (density-map readable + uncompressed checks + base.Validate tail). |
| Runtime/InstanceScatterLayer.cs | extend | Add `[SerializeField] private AuthoredInstancesData? authoredInstances;` plus `[SerializeField, Range(0.05f, 5f)] private float placeSpacing = 0.5f;` plus public accessors. Validate override. |
| Runtime/DensityPlacement.cs | edit | Change ctor param + field type from ScatterLayer to DensityScatterLayer. Body uses this.layer.densityMap etc directly without casts. |
| Runtime/InstancePlacement.cs | edit | Change ctor param + field type from ScatterLayer to InstanceScatterLayer. Body uses this.layer.authoredInstances. |
| Runtime/ScatterField.cs | edit | ~5 sites: layer.HasAuthoredInstances -> layer is InstanceScatterLayer. |
| Editor/TerrainScatterConfigEditor.cs | edit | Layer-tab UI dispatches on layer switch DensityScatterLayer / InstanceScatterLayer / default. Toolbar hidden when type is DensityScatterLayer. |
| Editor/ScatterBrush.cs | edit | Place/Erase/EditBrush methods take InstanceScatterLayer (not base). Call sites adjusted to upcast/check via is. |
| Editor/InstancePickingService.cs | edit | Constructor + public API tighten to InstanceScatterLayer. |
| Editor/InstanceSelectionOverlay.cs | edit | Same. |
| Editor/ScatterBakeToAuthored.cs | edit | Reads DensityScatterLayer, CREATES new InstanceScatterLayer sub-asset (via ScriptableObject.CreateInstance + AddObjectToAsset), swaps the config.layers entry. Replaces the P5 design that flipped HasAuthoredInstances on the same SO. |
| Runtime/ScatterLayer.cs | NO edit | Base stays concrete; Obsolete stack stays; densityMap, authoredInstances, placeSpacing fields stay (until R5). The migrated demo asset already lives on DensityScatterLayer, so base fields are dead-but-serialized; harmless. |

## Step-by-Step Tasks

1. **Read all consumer files first** to map exact HasAuthoredInstances usage sites (~5 in ScatterField + ~3 in Editor).
2. **Promote fields:**
   - On DensityScatterLayer: declare densityMap + targetInstances with IDENTICAL field names + attributes as base copies (so EditorJsonUtility round-trip from R3 already populated them - field-name match is the load-bearing property). Add public accessors with same names + signatures.
   - On InstanceScatterLayer: declare authoredInstances + placeSpacing identically.
   - Add public override bool Validate(out string error) on each subclass with the corresponding tail.
3. **Tighten placement strategies:**
   - DensityPlacement: change field type to DensityScatterLayer. Body unchanged.
   - InstancePlacement: change field type to InstanceScatterLayer. Body unchanged.
4. **Replace HasAuthoredInstances reads** in ScatterField.cs:
   - `if (layer.HasAuthoredInstances)` -> `if (layer is InstanceScatterLayer)` for boolean checks.
   - Where authored-specific data is then accessed: pattern-match: `if (layer is InstanceScatterLayer instLayer) { use instLayer.authoredInstances; }`.
5. **Tighten editor APIs (one file at a time, verify compile mentally after each):**
   - ScatterBrush.cs: change method signatures from `(ScatterLayer layer, ...)` to `(InstanceScatterLayer layer, ...)`. Update callers in TerrainScatterConfigEditor.cs to do `if (layer is InstanceScatterLayer instLayer) brush.Place(instLayer, ...);`.
   - InstancePickingService.cs: same pattern.
   - InstanceSelectionOverlay.cs: same pattern.
6. **Type-aware editor UI in TerrainScatterConfigEditor.cs:**
   - Replace `if (layer.HasAuthoredInstances) DrawToolbar();` with `if (layer is InstanceScatterLayer) DrawToolbar();`.
   - Brainstorm prescribes a switch expression on the layer concrete type for clearer dispatch.
7. **Update ScatterBakeToAuthored.cs:**
   - Now takes DensityScatterLayer as source.
   - Instead of flipping a flag, creates a NEW InstanceScatterLayer via ScriptableObject.CreateInstance.
   - Copies the 26 shared fields via EditorJsonUtility round-trip (same pattern as R3 migration menu).
   - Populates authoredInstances + placeSpacing.
   - AssetDatabase.AddObjectToAsset(newInstanceLayer, configPath); swap config.layers entry; AssetDatabase.RemoveObjectFromAsset(oldDensityLayer); SaveAssets.
8. **Compile sanity** via main-loop compile check after EACH file group (placements, ScatterField, Editor batch). Subagent does not run refresh_unity itself - that is verification gate territory - but should mentally walk the diff.
9. **Write phase-4-report.md:** list of every site changed (file + line), every HasAuthoredInstances read replaced with `is InstanceScatterLayer`, and the count of layer.X reads that touched type-specific fields and needed pattern matching.

## Verification Gate

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` - FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=30)` - 0 NEW project errors.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` - no `[Parity] ERROR`.
5. Screenshot game-view -> `plans/scatter-layer-placement-split/screenshots/phase-4-render.png` - visual parity vs baseline.
6. **Asset-presence:** demo TerrainScatterConfig sub-asset still DensityScatterLayer (R3 result preserved); densityMap + targetInstances fields read non-default values on the new type (confirms field-name migration carried).
7. **Grep gate:** `grep -rn "HasAuthoredInstances" Assets/` returns ONLY the accessor declaration on base ScatterLayer.cs (no consumer reads remain). Consumers are migrated; the base accessor itself is deleted in R5.

## Exit Criteria

- All ~8 consumer files compile.
- HasAuthoredInstances reads removed from every consumer (only base accessor declaration remains).
- DensityScatterLayer + InstanceScatterLayer carry their type-specific fields populated from R3 migration.
- Demo renders byte-identical to baseline.
- Parity harness PASS.
- phase-4-report.md written with per-site change list.

## Rollback Plan

Per-file revert via git is unavailable (no git in this repo). Use plan-local backup strategy:
1. Before any edit, copy each target file to plans/scatter-layer-placement-split/backups/r4-pre/RELATIVE-PATH.bak.
2. On failure, restore from .bak files.
3. R3-migrated asset itself does NOT need rollback - field-name match on the new type means the asset is still valid; just consumer code regresses.

## Anti-Stall Guard Reminders

- **First MCP call = set_active_instance unity_instance="GrassInteract@de203215".**
- **No progress narration.** Read -> edit -> next.
- **150K commit checkpoint.** R4 is the biggest phase. If you reach ~150K mid-phase, STOP, write phase-4-report.md (in-progress + which files done, which pending), exit. Main-loop resumes with fresh context AND can spawn a follow-up R4-continuation teammate.
- **Read first, edit second.** Read all ~8 target files before any edit; HasAuthoredInstances pattern matching needs you to see all call shapes at once.
- **One file at a time after the read pass.** Smallest blast radius if you hit a compile error.
- **No Unity restart, no Assets/Reimport All.** refresh_unity only.
- **ScatterLayer stays concrete after R4.** Do NOT promote to abstract; do NOT delete the Obsolete stack. That is R5 work.
- **Edits-only-in-subagent.** Verification gate = main-loop responsibility.
