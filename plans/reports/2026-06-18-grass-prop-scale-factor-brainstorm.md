# Brainstorm: Per-Layer Render-Time Scale Factor (no repaint)

**Date:** 2026-06-18
**Feature:** A scale multiplier on GrassLayer/PropLayer to resize scattered grass & props without re-scattering ("repainting").

## Problem statement
`GrassLayer.scaleRange` and `PropLayer` scale are sampled per-instance and **quantized into the GPU buffer at bake time** (ChunkedBladeBuffer / ChunkedInstanceBuffer). Any scale change forces a full scatter → bake → upload rebuild. Goal: resize a whole layer's scatter instantly.

## Key finding (data flow)
Buffers store **normalized [0,1]** scale. Vertex shaders reconstruct world scale as:
```
worldScale = (packed/65535) * _ScaleMax2
```
`_ScaleMax2` is a **per-layer, per-material** uniform (set via `SetLodFloat`, re-applied every frame by `ApplyEngineOwnedUniforms`/`Submit`). Therefore a *separate* multiplier uniform applied after decode gives instant render-time scaling with **no re-quantization, no rebuild, no precision loss**:
```
worldScale = (packed/65535) * _ScaleMax2 * _ScaleFactor   // _ScaleFactor default 1.0
```
(The initial scout concluded "no render-time scaling" — but only because it considered mutating `_ScaleMax2` itself, which would break the 16-bit quantization range. A separate factor sidesteps that.)

## Decisions (user-confirmed)
- **Factor type:** uniform float (single multiplier).
- **Colliders:** factor rescales prop colliders too (grass has none).
- **Surface:** editor slider with live Scene preview; no play mode, no rebuild.
- **Scope:** per-layer only (no global world multiplier).

## Approaches evaluated
- **A. Render-time `_ScaleFactor` uniform (CHOSEN).** Add a uniform multiplied at decode; update cull margin + worldBounds CPU-side. No repaint. Minimal code, reuses existing per-material/per-frame uniform discipline.
- **B. Bake-time multiplier + re-quantize + rebuild.** This *is* a repaint — fails the core requirement. Rejected.
- **C. Per-instance multiplier buffer.** Overkill for a single factor (YAGNI). Rejected.

## Implementation outline
1. **Data** — `scaleFactor` field + accessor on `GrassLayer.cs`, `PropLayer.cs`; surfaced via `GrassTileScatterLayer` / `PropLayerScatterLayer` adapters. (camelCase, `this.` prefix per code-conventions-unity.)
2. **Shaders** — declare `float _ScaleFactor`; append `* _ScaleFactor` at both decode sites in `GrassInteractIndirect.shader` (~L656/793) and `ScatterInstanced.shader` (~L300/390).
3. **Engines** — snapshot `scaleFactorVal`; push via per-material `SetLodFloat(ID_ScaleFactor,…)` (NOT SetGlobal — multi-layer correctness). Add `SetScaleFactor(float)` that updates uniform + multiplies `bladeCullMargin` and `worldBounds` extents by the factor (structural lockstep; no scatter/bake).
4. **Prop colliders** — apply factor to per-instance collider local scale in `BuildColliderRuntime` (transform multiply, no scatter rebuild).
5. **Editor** — `scaleFactor` slider routed to the live `SetScaleFactor` path **instead of** `WorldPainterRebuildScheduler.Mark*Dirty` (this is what makes it repaint-free). `scaleRange` still triggers rebuild (re-randomizes).

## Risks / mitigations
- Cull-bounds desync → pops: `SetScaleFactor` updates uniform + margin + worldBounds atomically.
- Precision: factor applied after decode, never re-quantized; clamp `[0.1, 5]`.
- Bend/wind headroom: `BendHeadroom` additive/unscaled; factor multiplies `MaxBladeHeight*ScaleMax` term only.
- Collider cost: per-instance transform multiply; debounce on slider drag-release if needed.

## Out of scope (extensible later via same seam)
Non-uniform Vector3 factor, global world multiplier, runtime gameplay API.

## Relevant files (from scout)
- `Runtime/Surface/GrassLayer.cs` (scaleRange L43/90), `Runtime/Surface/PropLayer.cs` (override L67-71)
- `Runtime/Scatter/GrassGpuEngine.cs` (cull margin L262-263, worldBounds L214)
- `Runtime/Scatter/InstancedPropEngine.cs` (`SetLodFloat ID_ScaleMax2` L321, cull margin L240-241, colliders L202, per-frame re-apply L268-286)
- `Runtime/Scatter/ChunkedBladeBuffer.cs` / `ChunkedInstanceBuffer.cs` (16-bit scale pack)
- `GrassInteractIndirect.shader` / `ScatterInstanced.shader` (decode sites)
- `Editor/Inspector/GrassLayerEditor.cs` (L141), `Editor/Inspector/PropLayerEditor.cs`

## Next step
Hand to `/t1k:plan` for phased implementation plan with file ownership + tests.
