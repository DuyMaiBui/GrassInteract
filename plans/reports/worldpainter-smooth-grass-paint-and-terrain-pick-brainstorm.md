# WorldPainter — Smooth Grass Paint + Click-Terrain-to-Select (Brainstorm)

**Date:** 2026-06-17 · **Status:** approved, → /t1k:plan

## Problem statement

Two WorldPainter editor UX asks:

1. **Smooth continuous grass paint on drag.** Prior version's drag painting is laggy/steppy. Want a *buffer* so painting is smooth.
2. **Click the terrain visual in the Scene view to select the WorldPainter object.** Terrain is GPU-drawn with no selectable GameObject, so native picking can't hit it.

## Scout findings (current architecture)

- Scene input lives in `WorldPainterSculptTool.OnSceneGui` (subscribed to `SceneView.duringSceneGui`), not an EditorTool. MouseDown/Drag/Up wired in `WorldPainterSculptTool.cs:307-324`.
- Continuous-stroke math already exists + unit-tested: `WorldPainterStroke.Advance` / `CountStamps` (spacing-interpolated stamps, mouse-speed-independent).
- Per drag stamp currently does heavy inline GPU work: `TerrainPaintTargetResolver.Resolve` → per-tile RT `GetOrCreate` → density compute dispatch (`DensityDispatch.Run`) → async writeback (`densityEncoder.RequestAsync`, 0.15s throttle). `DensityBrushTools.cs`, `WorldPainterSculptTool.Stroke.cs`.
- `RebuildScatterPreview()` (the grass-visibility step) is called **only** in `HandleMouseUp` (Stroke.cs:93-97), never mid-drag → no live feedback during a stroke; coupling rebuild to per-stamp events in the prior version caused the stutter.
- Grass data: per-tile R8 density `Texture2D` in `GrassLayer.densityTiles`; per-tile transient adapter `GrassTileScatterLayer` feeds scatter engines.
- Terrain render: `Graphics.RenderMeshIndirect` (`GpuTerrainEngine.cs:352`). **No selectable GameObject**; only hidden `HideAndDontSave` collider hosts via `TerrainColliderStreamer` (physics only).
- Brush already does a CPU-authoritative analytic terrain raycast in painting space: `TryGetBrushWorldPoint` / `TerrainHeightSampleCpu` (no collider needed, exact on visible surface).

## Feature 1 — Buffered stamp pipeline (decouple input from GPU)

Root cause: high-frequency mouse events are coupled directly to expensive GPU work (compute dispatch + scatter rebuild). Fix = buffer + drain at fixed cadence.

```
MouseDrag (high freq, cheap)        Drain tick (~15 Hz)
  stroke.Advance(...)                 pop ALL pending stamps
    └─ enqueue stampPos ──► buffer    batched density compute dispatch
                                      coalesced writeback request
MouseUp                               ONE scatter rebuild, scoped to touched tiles
  drain remaining synchronously       SceneView.Repaint
  + final full rebuild
```

Pieces:
1. **`StrokeStampBuffer`** — `HandleMouseDrag` stops dispatching inline; only enqueues interpolated stamp positions from `stroke.Advance` (O(1), zero GPU). Mouse sampling stays smooth regardless of GPU load.
2. **Throttled drain** — time-gated step (~15 Hz, from `OnSceneGui`/`EditorApplication.update`) pops the whole batch, dispatches density compute together, one coalesced writeback, **one** `RebuildScatterPreview` per drain (not per stamp).
3. **Scoped rebuild** — rebuild only tiles touched this drain (via per-tile `GrassTileScatterLayer`) instead of all scatter engines. Biggest perf win; fall back to throttled global rebuild if per-tile scoping isn't cleanly separable.

Cadence fixed at ~15 Hz (not exposed as config — chosen). Reuses `WorldPainterStroke`, `RebuildScatterPreview`, density encoder. New: buffer + drain scheduler only.

Verify: dragging a long stroke shows continuous live grass under cursor with no stutter; final result identical to per-stamp; full stroke persists on mouseup; one Ctrl+Z reverts whole stroke (existing Undo group preserved).

## Feature 2 — Click terrain → select WorldPainter

Approach: **analytic-raycast pick in the scene-GUI hook, gated.**
- Active only when **a WorldPainter exists AND paint mode is OFF AND the View/no-tool is active** (default-tool-only — won't fight Move/Rotate/Scale gizmo drags; suppressed mid-paint).
- On left-click: defer to `HandleUtility.PickGameObject` first (real objects keep priority); if nothing else hit, run existing analytic terrain raycast (`TryGetBrushWorldPoint`/`TerrainHeightSampleCpu`). On terrain hit → `Selection.activeGameObject = painter.gameObject`, consume click.
- Reuses the brush's CPU terrain raycast — works without a collider, exact on visible surface.

Rejected: hidden `MeshCollider` proxy (redundant with `TerrainColliderStreamer`, heavy for GPU terrain); repurposing streamed collider hosts (`HideAndDontSave` + streamed → fragile).

## Risks / considerations

- Drain scheduling in edit mode must not leak (`EditorApplication.update` subscription lifecycle tied to stroke / tool teardown).
- Scoped per-tile rebuild must still flush straddled-neighbour tiles (existing seam-sync registration on stroke begin).
- Feature 2 click-consume must not swallow legitimate empty-space clicks (deselect) — only consume on confirmed terrain hit.
- Both features are editor-only; no runtime/build impact.

## Decisions (from user)

- F1 live feedback during drag: **yes**. Buffer/drain at fixed ~15 Hz (not configurable).
- F2 pick scope: **default/no-tool only**, gated to exists-&-not-painting, defer to native pick first.
- Next: **/t1k:plan** then implement.

## Success criteria

- Grass drag paint feels continuous and lag-free across long strokes and multi-tile spans.
- Final painted density identical to the per-stamp path; undo = one step per stroke.
- Clicking GPU terrain (no other object under cursor) selects the WorldPainter GameObject only when not painting and on the default tool; empty-space and gizmo clicks unaffected.

## Next steps

1. `/t1k:plan` — phased plan: (P1) StrokeStampBuffer + drain scheduler, (P2) scoped scatter rebuild, (P3) terrain-pick selection, (P4) EditMode tests + manual verify.
2. Implement per plan.
