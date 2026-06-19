# Phase F6 — Paint tab

Effort: **M** · Blocked by: F2 only (independent of F3/F4/F5) → parallelizable off the shell.

## Goal

Create a blank texture (size + fill color), paint with a simple brush (color / size / opacity + eraser) onto a RenderTexture, and save a PNG. The painted texture is usable as a UV-Edit image (feeds `session` so F4 can import it). v1 = simple brush only — NO layers, NO blend modes.

## File ownership

Create:
- `Editor/Paint/PaintCanvas.cs` — owns a `RenderTexture` (created at chosen size, cleared to fill color); applies a brush stamp at a point; readback to `Texture2D` on demand. Owns dispose. ≤170 lines.
- `Editor/Paint/BrushStroke.cs` — PURE stamp math: given center, radius, opacity, color (or eraser), compute the affected pixel region + per-pixel alpha falloff; `Stamp(Color[] pixels, int w, int h, ...)` accumulates opacity (clamped). Unit-testable. ≤120 lines.
- `Editor/Paint/PaintTextureWriter.cs` — encode the painted `Texture2D` to PNG + import with correct importer settings (sRGB color, mip on) — mirror `AtlasAssetWriter.WriteTexture`'s importer config (do NOT duplicate the whole writer; a small focused encoder is fine, but cite the convention). ≤90 lines.
- `Editor/UI/Tabs/PaintTab.cs` — `IUVEditorTab`: new-texture controls (size dropdown + fill color), brush controls (color/size/opacity + eraser toggle), the paint surface (drag to paint), Save PNG button, "Use in UV-Edit" button (writes to session). ≤180 lines.

Test (create):
- `Tests/Editor/BrushStrokeTests.cs` — a stamp at center covers the expected radius; opacity accumulates and clamps to 1; eraser reduces alpha toward 0; falloff is monotonic from center to edge; a stamp outside bounds is clipped (no out-of-range write).

Edit: `Editor/UI/UVEditorSession.cs` — ADD `Texture2D PaintOutput` (F6's block) so F4 can import it.

## Reuse map

- PNG encode + importer settings convention from `AtlasAssetWriter.WriteTexture` (mirror, don't duplicate the mesh/material writer).
- The painted texture becomes a `PlacedImage` source in F4 (no new coupling — F4 imports any `Texture2D`).

## Design notes

- Painting onto a `RenderTexture` keeps it cheap; brush math is CPU-pure (`BrushStroke`) so it's testable, then applied via `SetPixels`/`Apply` on a readback buffer OR a GL blit of a brush quad. Prefer the pure-CPU path for v1 simplicity (report says "simple brush"); document the choice.
- Paint surface can be an `IMGUIContainer` or a `VisualElement` with pointer events — either is fine; the brush math is framework-agnostic.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Brush stamp writes out of bounds / wrong falloff | 2 | 3 | 6 | Pure `BrushStroke` unit-tested (clip + falloff + accumulate). |
| RenderTexture/Texture2D leak on tab switch | 3 | 2 | 6 | `PaintCanvas.Dispose()` in tab `Detach`/window `OnDisable`. |
| PNG importer settings drift from atlas convention | 2 | 2 | 4 | Mirror `AtlasAssetWriter.WriteTexture` importer config; cite it. |

No score ≥ 15.

## Verify gate

- Unit: `BrushStrokeTests` GREEN (fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort).
- Manual: Paint tab → new 512 texture, fill white; paint strokes (color/size/opacity), erase part; Save PNG (verify file on disk + correct sRGB); "Use in UV-Edit" → switch to UV-Edit → the painted texture imports as a `PlacedImage`.
