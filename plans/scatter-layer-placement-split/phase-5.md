---
phase: R5
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: S (~0.5 day)
unity instance: GrassInteract@de203215 (port 6403)
depends on: R4 complete (consumers tightened; demo asset already on DensityScatterLayer since R3)
---

# R5 - Promote ScatterLayer to abstract + Facade + Delete Obsolete Stack

## Goal

Make ScatterLayer abstract. Rewire GrassScatter.Build to delegate via the strategy facade: `layer.CreatePlacement().Build(...)`. Remove every trace of the placement-mode flag from the base: HasAuthoredInstances accessor + private field, targetInstances [Obsolete] + [FormerlySerializedAs] + [HideIf] + [InfoBox] + #pragma 0618 stack, densityMap/authoredInstances/placeSpacing fields (moved to subclasses in R4). Remove the [CreateAssetMenu] on the base (cannot CreateAsset an abstract type anyway).

This is safe NOW because:
- R3 migrated the demo asset to DensityScatterLayer (no asset still uses base MonoScript GUID).
- R4 migrated all consumer reads off HasAuthoredInstances + moved type-specific fields down.

R5 is the cleanup harvest of all the deprecation tech debt accumulated in P1-P5.

## Scope

**IN:**
- Promote `public class ScatterLayer : ScriptableObject` to `public abstract class ScatterLayer : ScriptableObject`.
- Add `public abstract IScatterPlacement CreatePlacement();` if previously a virtual stub; otherwise add fresh.
- Delete:
  - `HasAuthoredInstances` accessor + backing field on base.
  - `targetInstances` declaration + [Obsolete] + [FormerlySerializedAs] + [HideIf] + [InfoBox] attributes on base.
  - `#pragma warning disable 0618` + matching restore on base (no more 0618 references).
  - `densityMap`, `authoredInstances`, `placeSpacing` fields on base (already mirrored on subclasses in R4).
  - Remove `[CreateAssetMenu]` on base (abstract types cannot be created).
- Rewire `GrassScatter.Build(ScatterLayer layer, ...)` to: `return layer.CreatePlacement().Build(origin, pool, sampler);`. Shrinks to a one-line facade. Keep `BuildFieldBounds` + `ReturnSlabs` helper static methods that both placements share.
- Remove the procedural and authored branches from GrassScatter.cs - their bodies now live in the placement strategies created in R1 and consumed by the facade.

**OUT:**
- Engine signature changes (none - IGrassEngine.Build still takes ScatterLayer).
- Touching subclasses (R4 finished them).
- Touching editor APIs (R4 finished them).
- Removing per-asset backups (kept through R6 + 1 cycle).

## File Ownership

| File | Action | Notes |
|---|---|---|
| Runtime/ScatterLayer.cs | edit | Abstract promotion + member deletions (HasAuthoredInstances, targetInstances + entire attribute stack, densityMap, authoredInstances, placeSpacing). Add `public abstract IScatterPlacement CreatePlacement();`. Remove [CreateAssetMenu]. Remove `#pragma 0618` block. |
| Runtime/GrassScatter.cs | edit | Shrinks to facade. `Build(...) => layer.CreatePlacement().Build(origin, pool, sampler);`. Keep static helpers used by both placements (BuildFieldBounds, ReturnSlabs). Delete procedural + authored branch bodies (now in DensityPlacement / InstancePlacement from R1). |
| All other files | NO edit | R4 already tightened consumers; engines unchanged. |

## Step-by-Step Tasks

1. **Pre-flight asset-presence check (READ-ONLY):**
   - Read demo TerrainScatterConfig `.asset` YAML. Confirm the sub-asset `m_Script:` guid is DensityScatterLayer.cs MonoScript GUID, NOT base ScatterLayer.cs.
   - If FAILED: STOP IMMEDIATELY. R3 did not migrate completely. Do NOT proceed. Write phase-5-report.md noting the failure and exit.
2. **Backup runtime files before edit:**
   - Copy Runtime/ScatterLayer.cs -> plans/scatter-layer-placement-split/backups/r5-pre/ScatterLayer.cs.bak.
   - Copy Runtime/GrassScatter.cs -> plans/scatter-layer-placement-split/backups/r5-pre/GrassScatter.cs.bak.
3. **Edit Runtime/ScatterLayer.cs:**
   - Promote: `public class ScatterLayer : ScriptableObject` -> `public abstract class ScatterLayer : ScriptableObject`.
   - Remove `[CreateAssetMenu(...)]` line from base.
   - Add `public abstract IScatterPlacement CreatePlacement();` (replace any prior virtual stub from R2 if present).
   - Delete `HasAuthoredInstances` accessor + backing field.
   - Delete `targetInstances` field + every attribute stacked on it ([Obsolete], [FormerlySerializedAs], [HideIf], [InfoBox], [SerializeField]).
   - Delete the `#pragma warning disable 0618` and matching `#pragma warning restore 0618` block.
   - Delete `densityMap` field + accessor.
   - Delete `authoredInstances` field + accessor.
   - Delete `placeSpacing` field + accessor.
   - Delete `ValidateAuthoredAndCommon` private helper (its tail logic now lives in DensityScatterLayer.Validate / InstanceScatterLayer.Validate overrides).
   - Simplify base Validate to the truly-shared part; subclasses override and call base.Validate.
4. **Edit Runtime/GrassScatter.cs:**
   - Replace the Build method body with `return layer.CreatePlacement().Build(origin, pool, sampler);`.
   - Remove BuildFromAuthored (its body is now InstancePlacement.Build).
   - Keep BuildFieldBounds + ReturnSlabs as internal static helpers (both placements call them).
   - Update any unused usings.
5. **Confirm engines unchanged:** read MeshScatterEngine.cs / GrassGpuEngine.cs / GrassCpuEngine.cs - they take `ScatterLayer` parameter; that still works because they consume shared base fields only. No edits.
6. **Confirm subclass CreatePlacement overrides exist:**
   - DensityScatterLayer.CreatePlacement -> new DensityPlacement(this).
   - InstanceScatterLayer.CreatePlacement -> new InstancePlacement(this).
   - If missing (R2 left them as virtual on base only), add the overrides now. Subclass file is OWNED by R4 but additive override is acceptable in R5 since R4 did not finalize CreatePlacement wiring.
7. **Grep gate (write to phase-5-report.md):**
   - `grep -rn "HasAuthoredInstances" Assets/ Packages/` -> 0 hits.
   - `grep -rn "\[Obsolete\].*targetInstances" Assets/` -> 0 hits.
   - `grep -rn "FormerlySerializedAs.*targetInstances" Assets/` -> 0 hits.
   - `grep -rn "pragma warning disable 0618" Assets/` -> 0 hits in ScatterLayer.cs (other files OK if pre-existing).
   - `grep -rn "BuildFromAuthored" Assets/` -> 0 hits (now part of InstancePlacement body).
8. **Write phase-5-report.md:** confirmation of grep results, diff summary of ScatterLayer.cs + GrassScatter.cs, asset-presence pre-check result.

## Verification Gate

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` - FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=30)` - 0 NEW project errors.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` - no `[Parity] ERROR`.
5. Screenshot game-view -> `plans/scatter-layer-placement-split/screenshots/phase-5-render.png` - visual parity vs baseline.
6. **Asset-presence:** demo TerrainScatterConfig sub-asset reads as DensityScatterLayer; no error loading the asset (would error if base became abstract but asset still pointed at base GUID - the load-bearing R3 result protects against this).
7. **Grep gate** (re-run from main-loop): all 5 greps from Step 7 above return 0 hits.

## Exit Criteria

- ScatterLayer is abstract; compiles cleanly.
- GrassScatter.Build is a one-line facade.
- All Obsolete-stack references purged.
- Demo renders byte-identical to baseline.
- Parity harness PASS.
- Grep gate clean.
- phase-5-report.md written.

## Rollback Plan

R5 is the highest-blast-radius edit phase (abstract promotion = unloadable asset if anything is missed).
1. If verification gate fails:
   - Read backups plans/scatter-layer-placement-split/backups/r5-pre/ScatterLayer.cs.bak + GrassScatter.cs.bak.
   - Restore both files exactly.
   - refresh_unity, confirm demo loads again.
   - File phase-5-report.md with failure root cause; loop back to R5 with corrective brief.
2. If demo asset itself becomes unloadable (Unity logs MonoScript GUID mismatch):
   - Restore backups as above AND restore demo asset from plans/scatter-layer-placement-split/backups/CONFIG-GUID-SUBASSET-NAME.json (R3 backup) via the rollback procedure in phase-3.md.

## Anti-Stall Guard Reminders

- **First MCP call = set_active_instance unity_instance="GrassInteract@de203215".**
- **Pre-flight asset-presence check is GATING.** If demo asset is not on DensityScatterLayer, STOP and report. Do not edit anything.
- **Backup before edit.** Both ScatterLayer.cs + GrassScatter.cs to plans/scatter-layer-placement-split/backups/r5-pre/ BEFORE the first character is changed.
- **No progress narration.** Read -> edit -> next.
- **150K commit checkpoint.** R5 is smaller than R4 but the abstract promotion is the riskiest single edit; if you reach ~150K, STOP and write report.
- **No Unity restart, no Assets/Reimport All.** refresh_unity only.
- **Edits-only-in-subagent.** Verification gate = main-loop responsibility.
