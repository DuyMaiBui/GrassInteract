---
title: ScatterLayer → DensityScatterLayer + InstanceScatterLayer (placement polymorphism)
date: 2026-06-04
status: design-approved
follows: brainstorm-authored-instance-editor-20260604.md (P1–P5 shipped; this is the cleanup refactor)
scope: refactor — placement axis split; rendering axis (Grass/Mesh) untouched
---

# ScatterLayer Placement Split — Brainstorm Report

## Problem Statement

After P1–P5 of the Authored Instance Editor cook, `ScatterLayer` carries TWO orthogonal axes on a single concrete `ScriptableObject`:

1. **Rendering kind** — `ScatterKind { Grass, Mesh }` enum (already a field).
2. **Placement mode** — `HasAuthoredInstances : bool` flag added in P1; switches between procedural RNG scatter and authored sidecar.

The placement-mode flag is leaking complexity into the data class:
- `targetInstances` is `[System.Obsolete] + [FormerlySerializedAs] + [HideIf] + [InfoBox(Warning)] + [SerializeField] private` plus an accessor wrapped in `#pragma warning disable 0618` (5 attribute layers + 1 pragma block for one deprecated field).
- `Validate(out string error)` has a branching authored-vs-density tail; introduced a private `ValidateAuthoredAndCommon` helper.
- 4 fields (`densityMap`, `targetInstances`, `authoredInstances`, `placeSpacing`) are mutually-exclusive but all live on the same SO.
- Editor UI hides fields via `[HideIf]` for the wrong mode.
- 5 consumers branch on `HasAuthoredInstances` to pick procedural-vs-authored paths.

This is a flag-switched union-type anti-pattern. The fix is type polymorphism.

## Requirements

| # | Requirement |
|---|---|
| R1 | Split placement axis only — rendering (`ScatterKind`) stays as enum. |
| R2 | Two concrete `ScatterLayer` subclasses: `DensityScatterLayer`, `InstanceScatterLayer`. |
| R3 | Placement logic moves to `IScatterPlacement` strategy interface (`DensityPlacement`, `InstancePlacement`). |
| R4 | Existing demo `ScatterLayer.asset` sub-asset migrates cleanly via one-shot menu. |
| R5 | Engine signature `IGrassEngine.Build(ScatterLayer, …)` unchanged — callers still see the base type. |
| R6 | Eliminate `HasAuthoredInstances` bool and `targetInstances` `[Obsolete]` wrapper. |
| R7 | `ScatterFieldRebuildLayerHarness` still PASS after migration. |
| R8 | Demo renders visually identical to pre-refactor baseline (`phase-5-before.png`). |

## Approaches Considered

### A. Keep `ScatterLayer` concrete; switch on enum
Replace `HasAuthoredInstances` bool with `PlacementMode { Density, Instance }` enum, keep all fields on the same SO.

- ✔ Smallest diff.
- ✖ Doesn't solve the union-type problem; just renames the flag. Density-only / Instance-only fields still both serialized.
- ✖ Rejected.

### B. Abstract base + 2 concrete SOs + IScatterPlacement strategy — **CHOSEN**
`ScatterLayer` becomes abstract; `DensityScatterLayer` and `InstanceScatterLayer` are concrete subclasses with mode-specific fields. Placement logic lives in `IScatterPlacement` implementations the layer hands out.

- ✔ Eliminates all 4 mutually-exclusive fields' co-existence on one type.
- ✔ Kills `[Obsolete]` + `[FormerlySerializedAs]` + `[HideIf]` + `[InfoBox]` + `#pragma 0618` stack on `targetInstances`.
- ✔ Future extensibility: adding a third placement (Poisson, Stream) is one new SO subclass + one new strategy class.
- ✖ Asset migration required for existing demo asset.

### C. Composition — mixin SOs (PlacementConfig, WindConfig, LODConfig, ColliderConfig) referenced by each layer
Break the 26 shared fields into orthogonal mixin SOs.

- ✔ Maximum reusability across kits.
- ✖ Massive consumer churn (every `layer.X` read becomes `layer.WindConfig.X`).
- ✖ Over-engineered for a project-local refactor.
- ✖ Rejected.

### D. 4-way cross-product (DensityGrass / InstanceGrass / DensityMesh / InstanceMesh)
Split BOTH axes into concrete types.

- ✔ No enum anywhere; pure Liskov.
- ✖ 4 types to maintain; `ScatterField.Rebuild` has 4 dispatch paths.
- ✖ User explicitly chose "just placement" — Option 1 of the design questions.
- ✖ Rejected.

**Decision:** Approach **B**. Confirmed by user via AskUserQuestion across 4 design decisions:
1. Polymorphism scope: just placement.
2. Naming: `DensityScatterLayer` + `InstanceScatterLayer`.
3. Migration: one-shot menu (not `[MovedFrom]`).
4. Engine API: `IScatterPlacement` strategy.

## Recommended Solution

### Type hierarchy

```
ScatterLayer : ScriptableObject (abstract)
  ├── 26 shared serialized fields
  │     kind enum, fieldBounds, scaleRange, seed, groundSnapMask, slopeRange,
  │     splatLayerIndex, splatThreshold, rotationOffsetEuler, randomPitchRange,
  │     randomRollRange, alignToNormal, grassMaterial, shadowCastingMode,
  │     deformMode, wind* (8 fields), bendStrength, flatten, recoveryRate,
  │     maxBladeHeight, bendHeadroom, chunkSize, lods[], meshMaterial,
  │     generateColliders, colliderMesh, colliderConvex
  ├── abstract IScatterPlacement CreatePlacement()
  └── abstract bool Validate(out string error)

DensityScatterLayer : ScatterLayer  [CreateAssetMenu]
  ├── Texture2D    densityMap       (required; null fails Validate)
  ├── int          targetInstances  (range [1, ∞); lives here only, no [Obsolete])
  └── override CreatePlacement() => new DensityPlacement(this)
       Validate: density-map readable + uncompressed checks + common-tail

InstanceScatterLayer : ScatterLayer [CreateAssetMenu]
  ├── AuthoredInstancesData? authoredInstances  (sub-asset created on demand)
  ├── float                  placeSpacing       (range 0.05–5)
  └── override CreatePlacement() => new InstancePlacement(this)
       Validate: common-tail only (density map optional / unused)
```

### Strategy interface + implementations

```csharp
public interface IScatterPlacement {
    GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler);
}

internal sealed class DensityPlacement : IScatterPlacement {
    private readonly DensityScatterLayer layer;
    public DensityPlacement(DensityScatterLayer layer) => this.layer = layer;
    public GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler) {
        // body lifted from current GrassScatter.Build procedural path
    }
}

internal sealed class InstancePlacement : IScatterPlacement {
    private readonly InstanceScatterLayer layer;
    public InstancePlacement(InstanceScatterLayer layer) => this.layer = layer;
    public GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler) {
        // body lifted from current GrassScatter.BuildFromAuthored
    }
}
```

### `GrassScatter` façade

```csharp
public static class GrassScatter {
    public static GrassScatterResult Build(ScatterLayer layer, Vector3 origin,
        InstanceBatchPool pool, ISurfaceSampler sampler)
        => layer.CreatePlacement().Build(origin, pool, sampler);

    public static void ReturnSlabs(GrassScatterResult? result, InstanceBatchPool pool) { … }
    // BuildFieldBounds stays as internal static helper used by both placements
}
```

### Migration menu

`Tools / GrassInteract / Migrate / ScatterLayer Assets → Typed`

For each legacy `ScatterLayer.asset` sub-asset under every `TerrainScatterConfig`:

1. Inspect `HasAuthoredInstances` on legacy data → pick target type (`InstanceScatterLayer` if true, else `DensityScatterLayer`).
2. `ScriptableObject.CreateInstance<T>()`.
3. `EditorJsonUtility.ToJson(oldLayer)` → `FromJsonOverwrite(newLayer)`. Field names match exactly (only their declaring class changes), so the JSON round-trip is a direct copy.
4. Per-asset backup `.json` written to `plans/reports/scatter-layer-migration-backup/<asset-guid>.json` before any destructive op.
5. `AssetDatabase.AddObjectToAsset(newLayer, config)`, swap entry in `TerrainScatterConfig.layers`, `AssetDatabase.RemoveObjectFromAsset(oldLayer)`, `AssetDatabase.SaveAssets()`.
6. Re-running on already-typed assets is a no-op.

### Consumer updates

| File | Change |
|---|---|
| `Runtime/GrassScatter.cs` | Shrinks to façade |
| `Runtime/ScatterField.cs` | `layer.HasAuthoredInstances` reads → `layer is InstanceScatterLayer`. ~5 sites. |
| `Editor/TerrainScatterConfigEditor.cs` | Layer-tab UI varies by concrete type; toolbar hidden on `DensityScatterLayer`. |
| `Editor/ScatterBrush.cs` | Place/Erase/EditBrush methods take `InstanceScatterLayer`. |
| `Editor/InstancePickingService.cs` | API tightens to `InstanceScatterLayer`. |
| `Editor/InstanceSelectionOverlay.cs` | Same. |
| `Editor/ScatterBakeToAuthored.cs` | Reads `DensityScatterLayer`, CREATES `InstanceScatterLayer` sub-asset, swaps config entry. Replaces "flip flag on same SO". |
| `Runtime/MeshScatterEngine.cs` / `GrassGpuEngine.cs` / `GrassCpuEngine.cs` | Signatures unchanged; ~95% of `layer.X` reads are shared-base fields, no touch. |

## Out of Scope

- `kind` (Grass/Mesh) enum stays — not a placement concern.
- P4b (`overrideMask` byte slot in `ChunkedInstanceBuffer`) — independent; can land before/after/parallel.
- Additional placement types (Poisson disk, runtime stream) — design supports them but not shipped.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Migration menu loses field data | 3 | 5 | **15** | EditorJsonUtility exact-name round-trip; per-asset JSON backup before delete; dry-run flag |
| `TerrainScatterConfig.layers` ref swap leaves orphan sub-asset | 3 | 3 | 9 | `AssetDatabase.RemoveObjectFromAsset` after entry swap |
| Legacy `GrassLayer : ScatterLayer` alias still in use | 2 | 3 | 6 | Pre-refactor grep; memory says retired in earlier refactor; verify |
| 101 `layer.X` reads need type-tightening | 4 | 2 | 8 | ~95% are shared base fields → no change; only `HasAuthoredInstances` reads need `is`-pattern |
| `ScatterFieldRebuildLayerHarness` regresses | 3 | 3 | 9 | Run after each refactor step |
| `IScatterPlacement` allocates per Rebuild | 3 | 2 | 6 | One new object per layer per Rebuild (not per frame). Negligible. Cache on layer if profiling shows hot. |
| `[CreateAssetMenu]` adds new asset types but doesn't break existing | 1 | 2 | 2 | Standard Unity behavior |
| Demo `ScatterLayer.asset` MonoScript GUID refers to soon-abstract type | 4 | 5 | **20** | The migration menu MUST run BEFORE the existing `ScatterLayer` class becomes abstract. R3 sequence: (a) keep current concrete class temporarily, (b) add new subclasses + migration menu, (c) run menu on demo, (d) verify, (e) THEN promote `ScatterLayer` to abstract in R5. Order is load-bearing. |

**Highest risk = Demo asset MonoScript GUID (score 20).** Mitigation: phasing is sequenced so the existing concrete `ScatterLayer` is kept alive until the demo asset has been migrated to a new typed sub-asset and verified rendering. Only then does `ScatterLayer` become abstract.

## Success Metrics

| Metric | Target | Verified in |
|---|---|---|
| Compile clean after each phase | 0 errors | R1–R6 |
| Demo asset migrates without data loss | All 26 shared + 2 density-specific fields equal pre/post | R3 |
| Demo renders visually identical | Screenshot diff vs `phase-5-before.png` | R3 + R6 |
| `ScatterFieldRebuildLayerHarness` PASS | No `[Parity]` ERROR | R6 |
| `HasAuthoredInstances` references | 0 in repo | R5 |
| `[Obsolete] targetInstances` references | 0 in repo | R5 |
| `#pragma warning disable 0618` blocks | 0 in `ScatterLayer.cs` | R5 |

## Phases (proposed for `/t1k:plan`)

| # | Phase | Effort | Owns |
|---|---|---|---|
| R1 | Add `IScatterPlacement` + `DensityPlacement` + `InstancePlacement` (mirror existing bodies; not wired) | S | NEW: `Runtime/IScatterPlacement.cs`, `Runtime/DensityPlacement.cs`, `Runtime/InstancePlacement.cs` |
| R2 | Add `DensityScatterLayer` + `InstanceScatterLayer` concrete subclasses (alongside still-concrete `ScatterLayer` for backward compat) | S | NEW: `Runtime/DensityScatterLayer.cs`, `Runtime/InstanceScatterLayer.cs` |
| R3 | Migration menu + run on demo asset; verify load + render | S | NEW: `Editor/MigrateScatterLayerTypes.cs` |
| R4 | Type-tighten consumers; replace `HasAuthoredInstances` reads with `is InstanceScatterLayer`; type-narrow editor APIs | M | ~8 file edits |
| R5 | Rewire `GrassScatter.Build` to façade; promote `ScatterLayer` to abstract; delete `targetInstances` + `HasAuthoredInstances` + `[Obsolete]` stack | S | `Runtime/GrassScatter.cs`, `Runtime/ScatterLayer.cs` |
| R6 | Verification: compile + harness + screenshot vs baseline | S | — |

**Critical path:** R1 → R2 → R3 → R4 → R5 → R6. R3 cannot start until R1+R2 ship the new types. R5 cannot start until R3 has migrated the demo (else `ScatterLayer.asset` becomes unloadable).

## Next Steps

Hand off to `/t1k:plan` to expand R1–R6 into ordered tasks with file-ownership boundaries and verification gates.
