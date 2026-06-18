# Grass Paint — Defer Scatter Rebuild to Mouse-Up (Brainstorm)

Date: 2026-06-17 · Status: design approved, ready for /t1k:plan

## Problem

Painting grass lags while dragging the brush. Each ~15 Hz drain tick during a
stroke runs a **full** grass scatter rebuild: `BuildGrassLayerEngines()`
iterates `map.EnumerateTiles()` and re-runs CPU scatter
(`DensityPlacement.Build()` — per-blade density sample + raycast/terrain-snap +
matrix build) for **every tile with painted density**, not just tiles under the
brush. Once a large area is painted, every tick re-scatters the whole map → stall.

Existing throttles (already present, kept): 15 Hz drain cadence
(`DRAIN_INTERVAL_SEC = 1/15`), deferred-dispose (2-frame engine survival to kill
flicker), and the per-frame coalescing rebuild scheduler. None of these reduce
*per-rebuild* cost — they only cap frequency. The cost per rebuild is the issue.

## Decisions (user-approved)

- **Approach:** Defer all grass-blade scatter to **mouse-up**. No blade rebuild
  during drag.
- **Drag preview:** Density-only is acceptable → **add a density heatmap overlay**
  (none exists today; blades were the only painted-area feedback).

## Key feasibility finding

There is currently **no** density visualization independent of the grass engine.
During a stroke the only feedback is the brush-ring gizmo
(`TerrainBrushPreview.cs`) + the grass blades themselves (scatter-engine
`RenderMeshIndirect`, which only exists after a rebuild). `FlushAllDensityRTs()`
commits the painted density RT → Texture2D but never displays it. So removing
the in-drag rebuild *requires* a new overlay or the user paints semi-blind.

## Approach comparison

| Approach | Drag cost | Live feedback | Code | Verdict |
|---|---|---|---|---|
| **Defer to mouse-up + density overlay** | ~0 scatter | heatmap | remove call + 1 overlay | **chosen** |
| Dirty-tile only rebuild | scales w/ brush | live blades | new dirty-tracking | strong alt, more code |
| Adaptive drain throttle | still full rebuild | live blades (slow) | trivial | masks, doesn't fix |
| Defer, brush-ring only | ~0 | ring only (blind) | trivial | rejected — paints blind |

## Chosen design

**1. Remove blade rebuild from the drag path**
`WorldPainterSculptTool.Stroke.cs` → `PreviewActiveScatter()` currently does
`FlushAllDensityRTs()` + `painter.RebuildGrassLayerDeferred(layer)`.
→ Keep the density flush (cheap GPU, that's the paint data); **drop the
`RebuildGrassLayerDeferred` call** during drag. Stamp buffering + density RT
painting continue unchanged.

**2. Single rebuild on release**
`HandleMouseUp()` already calls `RebuildGrassLayerDeferred(grassLayer)` →
becomes the one rebuild per stroke. No new code.

**3. Density heatmap overlay (new, drag-only)**
Render the live painted density RT as a tinted SceneView overlay during the
stroke (e.g. density→color ramp on the active layer's tiles), torn down on
mouse-up when real blades return. Scope: ~1 editor shader/material + a SceneView
draw pass keyed off the existing per-tile `densityRtCache`. Self-contained,
unit-testable material setup.

**Unchanged:** 15 Hz drain, deferred-dispose, rebuild scheduler, end-of-stroke
rebuild.

## Risks

- Mouse-up may show a one-time hitch on a large painted map (single full
  re-scatter) — acceptable vs 15×/sec stalls. If noticeable, dirty-tile rebuild
  (alt approach) is the follow-up lever.
- Overlay must read the *in-progress* density RT (pre-flush) for true live
  feedback, or flush-then-display each tick (cheaper than scatter, still GPU).
- Overlay teardown must be reliable on mouse-up / tool-exit / undo so it doesn't
  linger over the real blades.

## Success criteria

- Dragging a large grass stroke holds smooth editor framerate (no per-tick CPU
  scatter); profiler shows scatter `Build()` absent during drag, present once on
  release.
- Painted area is visible live via the heatmap overlay during the drag.
- Final blades match what the overlay implied; overlay fully removed post-stroke.

## Files in scope

- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Stroke.cs` (remove in-drag rebuild)
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Density.cs` (density RT access for overlay)
- New: density-overlay editor draw + shader/material
- `Assets/WorldPainter/Editor/Brush/TerrainBrushPreview.cs` (overlay may live alongside brush preview draw)

## Next step

`/t1k:plan` to phase this (Phase 1: remove in-drag rebuild + verify deferred
mouse-up rebuild; Phase 2: density heatmap overlay + teardown; Phase 3: profile
+ EditMode test).
