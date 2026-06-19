# Phase F4 — UV-Edit tab

Effort: **L** · Blocked by: F2 (island-highlight wiring blocked by F3) · Blocks: F5.

## Goal

A 0..1 UV canvas (UI Toolkit `Painter2D` via `generateVisualContent`) with pan/zoom that draws UV islands, highlights islands matching the mesh selection (F3), imports image(s) as `PlacedImage`s, and lets the user click-select a placed image and apply **arbitrary-angle rotate / scale / translate / tint** via `AffineUVRemapper` (2×3 matrix). Supports island→image-region assignment. This produces the `PlacedImage` layout that F5 bakes.

## File ownership

Create:
- `Editor/Packing/AffineUVRemapper.cs` — PURE: a 2×3 affine UV transform (`float a,b,c,d,tx,ty`). `static AffineUVRemapper Identity`, `FromTRS(Vector2 translate, float rotationDeg, Vector2 scale, Vector2 pivot)`, `Vector2 Apply(Vector2 uv)`, `Compose`. **The translate+scale degenerate case MUST equal `UVRemapper.Remap`** (proven by test) so it is a true superset, not a parallel impl. ≤140 lines.
- `Editor/Canvas/CanvasTransform.cs` — PURE: pan (`Vector2`) + zoom (`float`) mapping `Vector2 UvToScreen(Vector2)` / `Vector2 ScreenToUv(Vector2)` over a viewport rect. NO VisualElement deps. ≤90 lines.
- `Editor/Canvas/PlacedImage.cs` — model: `Texture2D Texture`, `AffineUVRemapper Transform` (placement in UV space), `Color Tint`, optional assigned island id. ≤70 lines.
- `Editor/Canvas/UvCanvasElement.cs` — custom `VisualElement` with `generateVisualContent`: draws the 0..1 grid, each `PlacedImage` quad (using its affine transform via `CanvasTransform`), the UV island wireframe (highlight selected), and the active gizmo. Reads `CanvasController` for state. ≤200 lines.
- `Editor/Canvas/CanvasController.cs` — pan/zoom input (wheel/drag), hit-testing (which `PlacedImage`/gizmo handle is under the cursor via `CanvasTransform`), selection state, applies gizmo drag → updates the selected `PlacedImage.Transform`. ≤200 lines.
- `Editor/Canvas/TransformGizmo.cs` — move/rotate/scale handles: given a selected `PlacedImage` + `CanvasTransform`, computes handle screen positions, hit-tests a handle, converts a drag delta into a translate/rotate/scale update on the affine matrix. PURE math where possible (handle layout + delta→affine), drawing delegated to canvas. ≤180 lines.
- `Editor/UI/Tabs/UvEditTab.cs` — `IUVEditorTab`: hosts `UvCanvasElement` + an import-image button (→ `PlacedImage`) + tint color field + island-assignment control + (F5 adds) Bake button. ≤180 lines.

Test (create):
- `Tests/Editor/AffineUVRemapperTests.cs` — identity == passthrough; translate-only & scale-only == `UVRemapper.Remap` for the equivalent `subRect` (parity); 90°/45°/arbitrary rotation maps known UV corners to expected points; compose associativity.
- `Tests/Editor/CanvasTransformTests.cs` — `ScreenToUv(UvToScreen(p)) ≈ p` across {zoom: 0.5,1,4} × {pan: (0,0),(0.3,-0.2)}; viewport-edge mapping correct.

Edit: `Editor/UI/UVEditorSession.cs` — ADD `List<PlacedImage> PlacedImages` (F4's block).

## Reuse map

- `UVRemapper.Remap` — the parity baseline `AffineUVRemapper` must match in the no-rotation case (the report: "existing `UVRemapper` translate+scale is the degenerate case").
- `session.Islands` (F1) + `session.Selection` (F3) → island highlight.
- `Painter2D` / `generateVisualContent` (`UnityEngine.UIElements`) — editor-shipped.

## Design notes — the two ≥-risk seams

1. **Affine contract is shared with F5.** `AffineUVRemapper` is THE single definition of how a placed image's UV-space transform maps source-image UVs → atlas UVs. F5's `RotatedAtlasBlit` consumes the SAME matrix to place its quad. Lock the matrix convention (column/row order, degrees CCW, pivot in UV space) in this file's doc comment; F5 cites it.
2. **All screen↔UV math is in `CanvasTransform` (pure).** Gizmo + hit-test + draw ALL go through it; nothing recomputes pan/zoom inline. Round-trip is unit-gated before the gizmo is wired.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Gizmo math wrong under combined pan/zoom (drift/jump) | 4 | 4 | 16 | HIGH. Pure `CanvasTransform` round-trip unit-tested first; gizmo reads only it; test `delta→affine` for translate/rotate/scale in isolation. |
| `AffineUVRemapper` diverges from `UVRemapper` in degenerate case → F1/F4 inconsistency | 3 | 4 | 12 | Parity unit test (translate+scale == `UVRemapper.Remap`) is a hard gate. |
| `generateVisualContent` redraw cost / flicker on large island counts | 2 | 2 | 4 | Mark dirty only on state change (`MarkDirtyRepaint`); batch island wireframe. |
| Rotation produces atlas overlap (manual placement) | 3 | 3 | 9 | v1 allows it (user-driven); Enhancements auto-arrange mitigates; warn on overlap at bake (F5). |

Score ≥ 15: gizmo math (16) — mitigation mandated (pure `CanvasTransform` + tests) BEFORE wiring the interactive gizmo.

## Verify gate

- Unit: `AffineUVRemapperTests` + `CanvasTransformTests` GREEN (fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort).
- Manual: import an image → appears on canvas; click-select → gizmo shows; rotate (arbitrary angle) / scale / translate / change tint all update the quad live and stay aligned under pan+zoom; mesh-selected island (from F3) highlights; assign island→image. State persists on tab switch.
