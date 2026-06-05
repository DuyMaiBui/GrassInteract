# Phase 0 Baseline — grass-cpu-bend refactor

Captured 2026-06-02 against live editor `GrassInteract@de203215`, demo scene `GrassInteractDemo.unity`.

## Reference sweep (doomed symbols) + disposition

Regex: `GrassTrampleMap|GrassChunk|ChunkGrid|GrassInteractDeform|TrampleUpdate|_GrassTrample|_GrassWind|_GrassBend|_GrassFlatten|BindGlobals|_GrassFieldRect` across `Assets/GrassInteract/**/*.{cs,shader,hlsl}`.

| File | Refs | Phase-4 disposition |
|---|---|---|
| `Runtime/GrassTrampleMap.cs` | self (class + globals + TrampleUpdate.Find) | **DELETE** |
| `Runtime/GrassChunk.cs` | self (class) | **DELETE** |
| `Runtime/ChunkGrid.cs` | self (class + GrassChunk) | **DELETE** |
| `Shaders/GrassInteractDeform.hlsl` | self (all `_Grass*` globals + trample sample) | **DELETE** |
| `Shaders/TrampleUpdate.shader` | self (`_GrassFieldRect`, `_GrassFlatten`) | **DELETE** |
| `Shaders/GrassInteractInstanced.shader` | `#include GrassInteractDeform.hlsl` ×3 passes + SSOT mirror comment | REWORK (Phase 1 — drop include, dumb shader) |
| `Runtime/GrassInteractField.cs` | 7 PropertyToID globals + `ChunkGrid.Build`/`ReturnSlabs` + `GrassChunk` + `BindGlobals` call + black-trample seed | REWORK (Phase 2) |
| `Runtime/GrassRenderer.cs` | `GrassChunk[]` param + per-chunk loop | REWORK (Phase 2) |
| `Runtime/GrassFieldSpace.cs` | `FieldRectId`/`_GrassFieldRect` + `BindGlobals` + doc refs to ChunkGrid/GrassTrampleMap/Deform | REWORK (Phase 2 — drop BindGlobals + FieldRectId; keep WorldToUv/UvToWorld/MinXZ/SizeXZ) |
| `Runtime/GrassLODConfig.cs` | tooltip mentions GrassTrampleMap | REWORK (Phase 2 — add `recoveryRate`; repurpose wind/bend tooltips to "C#") |
| `Runtime/GrassInteractor.cs` | `GrassTrampleMap.Register/Unregister/HasActiveInstance` (OnEnable/OnDisable/Update) | REWORK (Phase 3 — static registry; retarget warning) |
| `Runtime/GrassLayer.cs` | doc-comment "ChunkGrid turns this into instances" | comment-only (Phase 4) |
| `Editor/GrassInteractDemoBuilder.cs` | creates `GrassTrampleMap` GO + `ConfigureTrampleMap` | REWORK (Phase 4 — remove) |
| `Editor/GrassPainterWindow.cs` | doc-comment mentions `ChunkGrid.Build`; calls `field.Rebuild()` | **KEEP** (verified: no `BindGlobals`, no deleted-type usage; only `GrassFieldSpace` ctor + `WorldToUv` + `Rebuild`) — Phase 4 may touch the doc-comment only |
| `Demo/GrassInteractDemo.unity` | has a `GrassTrampleMap` component | REGENERATE via builder (Phase 4 — NOT hand-YAML) |

**GrassPainterWindow safety verdict:** confirmed safe — its only `GrassFieldSpace` usage is ctor + `WorldToUv` (both KEPT), and it calls `field.Rebuild()` (KEPT). No reference to any deleted type and it does NOT call `BindGlobals`. No code change required beyond the optional doc-comment.

## Blade pivot

`Editor/GrassBladeMeshBuilder.cs`: `BLADE_HEIGHT = 1.0`; vertex loop sets `y = t * BLADE_HEIGHT` with `t = r/segments` ∈ [0,1], base row (r=0) at **y = 0**. **Pivot at y=0 confirmed — rigid lean about base needs NO offset bake.**

## Render baseline (deterministic placement reference)

Demo layer `GrassInteractDemoLayer`, field origin `(0,0,0)`, seed/bounds/scale per the layer asset.
Computed by calling `ChunkGrid.Build` on the live demo layer (read-only; no asset modified).

| Metric | Value |
|---|---|
| keptBlades | **2074** (density-map rejection keeps ~10% of the 20k target — this is the painted-density reality, not 20k) |
| chunkCount | 4 |
| slabCount | 5 |
| checksum_tx (Σ m03·1) | **895.935021** |
| checksum_ty (Σ m13·7) | **0.000000** (all blades on the flat y=0 ground) |
| checksum_tz (Σ m23·13) | **-792.635855** |
| checksum_scaleSum (Σ ‖col0‖) | **2067.991800** (avg scale ≈ 0.997, consistent with 0.7–1.3 uniform) |

**Drift-check method for Phase 2:** ChunkGrid emits chunk-ordered output while GrassScatter emits flat draw-order output, so the first-N matrices will NOT line up by index. The robust invariant is **order-independent**: GrassScatter must reproduce `keptBlades = 2074` and the three translation checksums + scaleSum **byte-identically** (same seeded rng draw order → same kept set → same sums). If count or any checksum differs, the draw order drifted — fix before proceeding.

Render state: grass renders in BOTH Game and Scene view in edit mode (current working state, pre-refactor). No new console errors after the read-only build (throwaway build used a fresh `InstanceBatchPool`, mutated no asset).

## Gate status: PASS
- Sweep hit-list complete with per-file disposition ✓
- Pivot at y=0 confirmed ✓
- Render baseline (count + checksums) captured ✓
- No `Assets/` file modified (read-only `execute_code`; no throwaway logs left in source) ✓
