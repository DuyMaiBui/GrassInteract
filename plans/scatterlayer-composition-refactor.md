# ScatterLayer Composition Refactor Plan

## Objective
Decouple `DensityScatterLayer` and `InstanceScatterLayer` from shared inheritance by extracting shared config into composable structs. `ScatterLayer` becomes a thin ScriptableObject base for serialization only.

## Design Decisions
- **Approach**: Composition over inheritance (Option 1)
- **Base class**: `ScatterLayer` stays as abstract `ScriptableObject` with zero fields — purely for Unity list serialization in `TerrainScatterConfig`
- **Config structs**: 4 shared serializable structs carry all data previously in the base class
- **Independence**: `DensityScatterLayer` and `InstanceScatterLayer` compose only the structs they need; no shared state through inheritance

---

## Phase 1: Extract Config Structs (no engine changes)

**Files to create:**
- `ScatterRenderConfig.cs` — material, shadowCastingMode, lods[]
- `ScatterWindConfig.cs` — windMode, direction, strength, frequency, noiseScale, gustScale, rippleScale, gustSpeed, rippleSpeed, rippleWeight
- `ScatterDeformConfig.cs` — affectedByWind, affectedByInteractors, bendStrength, flatten, recoveryRate
- `ScatterBoundsConfig.cs` — maxBladeHeight, bendHeadroom, chunkSize
- `ScatterPlacementConfig.cs` — groundSnapMask

**Pattern:** Each struct is `[Serializable]` with `[SerializeField] private` fields and public read-only accessors. Preserves Odin `[BoxGroup]` attributes via the concrete layer's field declarations.

**Verify**: Compilation passes with new files added.

---

## Phase 2: Strip ScatterLayer Base

**File to modify:** `ScatterLayer.cs`

**Changes:**
- Remove ALL serialized fields
- Remove ALL virtual properties (FieldBounds, ScaleRange, Seed, SlopeRange, SplatLayerIndex, SplatThreshold, RotationOffsetEuler, RandomPitchRange, RandomRollRange, AlignToNormal, IsOriented, DensityMap, AuthoredInstances, PlaceSpacing)
- Keep: `abstract CreatePlacement()`, `virtual Validate(out string error)` (empty pass-through), `name` (inherited from ScriptableObject)
- Keep: `ScatterLod` struct (used by both types)
- Add: abstract accessors for composed configs that engines need:
  - `abstract ScatterRenderConfig Render { get; }`
  - `abstract ScatterWindConfig Wind { get; }`
  - `abstract ScatterDeformConfig Deform { get; }`
  - `abstract ScatterBoundsConfig Bounds { get; }`
  - `abstract ScatterPlacementConfig Placement { get; }`

**Verify**: Compilation passes (concrete types will be broken until Phase 3).

---

## Phase 3: Refactor DensityScatterLayer

**File to modify:** `DensityScatterLayer.cs`

**Changes:**
- Compose `[SerializeField] private ScatterRenderConfig render`, `wind`, `deform`, `bounds`, `placement`
- Keep density-specific fields: densityMap, targetInstances
- Keep procedural placement fields: fieldBounds, scaleRange, seed, slopeRange, splatLayerIndex, splatThreshold, rotationOffsetEuler, randomPitchRange, randomRollRange, alignToNormal
- Implement abstract base accessors: `override Render => render`, etc.
- Override `CreatePlacement()` → `new DensityPlacement(this)`
- Override `Validate()` → density-specific checks + base
- Add: `IProceduralPlacementConfig` interface implementation (new interface for placement fields)

**Verify**: Compilation passes.

---

## Phase 4: Refactor InstanceScatterLayer

**File to modify:** `InstanceScatterLayer.cs`

**Changes:**
- Compose `[SerializeField] private ScatterRenderConfig render`, `wind`, `deform`, `bounds`, `placement`
- Keep instance-specific fields: authoredInstances, placeSpacing, defaultColliderMesh, defaultColliderConvex, poolColliders, poolCap, cullColliders, cullDistance, defaultColliderScale
- Implement abstract base accessors: `override Render => render`, etc.
- Override `CreatePlacement()` → `new InstancePlacement(this)`
- Override `Validate()` → instance-specific checks + base

**Verify**: Compilation passes.

---

## Phase 5: Update Placements to Use Interfaces

**Files to modify:** `DensityPlacement.cs`, `InstancePlacement.cs`

**Changes:**
- `DensityPlacement` constructor: accept `IProceduralPlacementConfig` + `IBoundsConfig` + `IDeformConfig` instead of typed `DensityScatterLayer`
- `InstancePlacement` constructor: accept `IInstancePlacementConfig` + `IBoundsConfig` instead of typed `InstanceScatterLayer`
- Extract thin interfaces from the concrete layer types:
  - `IProceduralPlacementConfig` — FieldBounds, ScaleRange, Seed, SlopeRange, SplatLayerIndex, SplatThreshold, RotationOffsetEuler, RandomPitchRange, RandomRollRange, AlignToNormal, IsOriented
  - `IInstancePlacementConfig` — AuthoredInstances, PlaceSpacing

**Verify**: Compilation passes.

---

## Phase 6: Update Engines to Read from Config Structs

**Files to modify:**
- `MeshScatterEngine.cs` — replace `layer.LodMeshes` with `layer.Render.LodMeshes`, `layer.Material` with `layer.Render.Material`, etc.
- `GrassCpuEngine.cs` — same pattern
- `GrassGpuEngine.cs` — same pattern
- `ScatterField.cs` — `layer.GroundSnapMask` → `layer.Placement.GroundSnapMask`

**Pattern:** All engine code that reads `layer.Property` becomes `layer.ConfigCategory.Property`.

**Verify**: Compilation passes.

---

## Phase 7: Validation

1. Run Unity compilation via MCP
2. Verify no errors in console
3. Verify existing asset compatibility (FormerlySerializedAs paths still resolve)
4. Confirm no behavioral changes (scatter result byte-identical for same inputs)

---

## Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| Unity serialization breaks for existing assets | 3 | 5 | 15 | Preserve FormerlySerializedAs; test asset load |
| Engine accessor typo causes runtime null | 2 | 4 | 8 | Compiler catches most; thorough grep for remaining `layer.` accessors |
| Odin BoxGroup lost on refactored fields | 2 | 2 | 4 | Re-apply BoxGroup attributes on composed struct fields in concrete types |

## Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Config structs | S (~1h) | Pure data, no dependencies |
| Phase 2: Strip base | S (~30m) | Remove fields, add abstract accessors |
| Phase 3: Density refactor | S (~1h) | Compose + implement |
| Phase 4: Instance refactor | S (~1h) | Compose + implement |
| Phase 5: Placement interfaces | S (~1h) | Extract interfaces, update constructors |
| Phase 6: Engine updates | M (~2h) | Bulk accessor rename across 3 engines |
| Phase 7: Validation | S (~1h) | Compile + runtime test |
| Total | M (~1d) | Sequential dependencies: 1→2→3,4→5→6→7 |
