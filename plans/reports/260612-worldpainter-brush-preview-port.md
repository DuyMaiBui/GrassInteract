# Brainstorm Report — Port MegaWorld brush preview to WorldPainter (+ full square brush)

Date: 2026-06-12
Author: brainstorm session (t1k-brainstorm)
Status: design approved → plan requested

## Problem statement

WorldPainter's editor sculpt/paint brush cursor is currently a filled tessellated
disc mesh (481 verts) drawn via `Graphics.DrawMeshNow` + a custom `BrushDecal`
shader + a procedurally generated 64×64 texture (`TerrainBrushPreview.cs`). The
user wants to adopt the cleaner brush-preview technique from the
`Assets/VladislavTsurikov` (MegaWorld) library, and extend the brush with a
square shape.

## Source technique (MegaWorld / VladislavTsurikov)

- Outline drawn with `Handles.DrawAAPolyLine` twice per loop: black @ 8px then
  color @ 4px, with `Handles.zTest = Always` (visible through geometry). No mesh,
  no shader, no texture.
  - `MegaWorld.Core/Runtime/Common/Utility/Repaint/BrushVisualisation.cs` → `DrawLoop`
- Each perimeter point is generated in world space then **raycast down the surface
  normal** (`Physics.Raycast`) to conform to terrain. Adaptive resolution 16–128
  from world-space perimeter length.
  - `CircleBrushVisualisation.cs`, `SquareBrushVisualisation.cs` (OBB edges)
- Entry: `MouseMove.OnRepaint` → tool `OnRepaint` → `VisualisationUtility.DrawCircleHandles(boxArea)`.

## Key adaptation for WorldPainter

WorldPainter's GPU/CDLOD terrain has **no collider**, so MegaWorld's per-point
`Physics.Raycast` conform won't hit anything. WorldPainter already supplies a
`HeightFn` (CPU heightmap sampler) to the preview. **Conform each perimeter point
via `HeightFn` instead of `Physics.Raycast`** — same technique, correct height source.

## Decisions (from user)

| Decision | Choice |
|---|---|
| Visual style | Outline ring + faint fill |
| Integration | Replace current `TerrainBrushPreview` (delete shader/texture/mesh) |
| Shapes | Circle + Square |
| Square scope | **Full square brush** — preview shape AND GPU stamp mask match |

## Design

### Part A — Preview rendering (pure Handles, replaces `TerrainBrushPreview`)
- Ring: build N perimeter points (circle = adaptive segments; square = sampled
  along 4 edges), conform Y via `HeightFn` + existing lift offset, close loop,
  draw `DrawAAPolyLine` black-then-color (MegaWorld double stroke).
- Faint fill: `Handles.DrawAAConvexPolygon` over the same conformed perimeter
  points at low alpha, `zTest = Always`.
- Delete `BrushDecal.shader` (+`.meta`), procedural disc texture, 481-vert mesh,
  `Graphics.DrawMeshNow` path.
- `Set(...)` gains a `shape` argument; caller `WorldPainterSculptTool.cs:180`
  passes `brush.shape`.
- Tradeoff: convex-poly fill is flat between perimeter points (screen-space fan),
  so on a very bumpy/large brush the interior fill won't hug every dip — but the
  ring conforms point-by-point, the fill is faint, and `zTest=Always` ⇒ no
  clipping. Standard cheap look. (Interior-hug mesh fill rejected as it
  contradicts the "delete the mesh" decision.)

### Part B — Full square brush
- `WorldPainterState.BrushSettings`: add `BrushShape shape = Circle` + enum
  `BrushShape { Circle, Square }`.
- `BrushMask.hlsl`: add `int _BrushShape` uniform; distance = Euclidean
  `length()` for circle, Chebyshev `max(|du.x|,|du.y|)` for square, before the
  falloff-LUT sample. Same falloff curve, square iso-contours.
- `WorldPainterSculptTool.Kernels.cs`: `SetInt("_BrushShape", (int)brush.shape)`
  in `BindAndDispatch` (set every dispatch so it can't leak across paths).
- `TerrainBrushMathTests.cs`: mirror Chebyshev branch in CPU reference + add
  square coverage (preserves CPU↔GPU parity invariant).
- `WorldPainterBrushDock.cs`: Circle/Square UI toggle.
- `WorldPainterBiomeStamp.cs`: pass shape through (shares `brushCompute`).
- No change to `TerrainPaintTargetResolver` or undo extents — both AABB-based; a
  square of half-extent = radius has the same AABB as the circle, so affected-tile
  resolution and undo regions already cover it.

### Files touched
| File | Change |
|---|---|
| `Editor/Brush/TerrainBrushPreview.cs` | Rewrite → Handles ring + convex-poly fill, circle+square |
| `Shaders/BrushDecal.shader` (+`.meta`) | Delete |
| `Editor/Brush/WorldPainterSculptTool.cs:180` | Pass `brush.shape` to `Set` |
| `Editor/WorldPainter/WorldPainterState.cs` | `BrushShape` enum + `shape` field |
| `Shaders/BrushMask.hlsl` | Chebyshev branch on `_BrushShape` |
| `Editor/Brush/WorldPainterSculptTool.Kernels.cs` | Bind `_BrushShape` |
| `Tests/Editor/TerrainBrushMathTests.cs` | CPU square mirror + test |
| `Editor/WorldPainter/WorldPainterBrushDock.cs` | Circle/Square UI toggle |
| `Editor/WorldPainter/WorldPainterBiomeStamp.cs` | Pass shape through |

## Risks
- CPU/GPU parity for square distance metric (`BrushMask.hlsl` vs CPU test) —
  caught by `TerrainBrushMathTests`.
- `_BrushShape` leaking between dispatch paths (biome shares `brushCompute`) —
  mitigated by setting it every dispatch.
- Deleting `BrushDecal.shader` — grep confirms `TerrainBrushPreview` is the only
  referencer; safe after rewrite.

## Validation criteria
- Compile clean; `TerrainBrushMathTests` green (circle + square parity).
- Circle preview ring conforms to terrain (no float/clip) at varied zoom & size.
- Square preview matches the square edited region (cursor == affected area).
- `BrushDecal.shader` removed with zero dangling references.

## Next step
`/t1k:plan` — phased implementation plan (preview rewrite, square stamp + parity,
tests, UI), file ownership + per-phase verify gates.
