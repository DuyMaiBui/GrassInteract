# Interactive UV Editor (MeshAtlas.UVEditor) — Brainstorm

Date: 2026-06-18 · Status: design approved, ready for /t1k:plan

## Problem statement

Existing `MeshAtlas` tool (Tools/Mesh Atlas/Combine & Bake) is batch + selection-based: numeric `RectField` UV
assignment, no canvas, no drag-drop, no image transforms, no UV-over-texture visualization. User wants a
ProBuilder-style interactive UV editor window: drag/drop meshes to combine, drag/drop images to author UVs,
transform images (move/rotate/scale), see mesh UV mapping over the texture and reassign it.

## Locked decisions (revised 2026-06-18 after dual-panel/tabs spec)

| Decision | Choice |
|---|---|
| Window | ONE editor window `Tools/Mesh Atlas/UV Editor`; **exclusive tabs** (one feature screen at a time) |
| Tabs | **Combine · Mesh Select · UV Edit · Paint** |
| Cross-tab sync | Shared session state (combined mesh, element selection, placed images) persists across tab switches; mesh element selection drives UV island highlight |
| Mesh panel | **Full 3D viewport** (PreviewRenderUtility, orbit/zoom) with **vertex/edge/face picking** (ProBuilder-style) |
| First feature | **Combine multiple meshes + auto-separate UV islands so none overlap** |
| UV separate | **Auto-pack islands** (union-find detect → AtlasPacker pack into 0..1 + padding, keep aspect, no overlap) |
| Image edit | Import image(s); click-select; **rotate (arbitrary) / scale / translate / tint color** |
| Paint | **Simple brush v1** — create blank texture (size+fill), brush color/size/opacity + eraser, save PNG |
| Output | Always a **new combined asset** (mesh + atlas PNG(s) + URP/Lit material + prefab); sources untouched |
| UI framework | **UI Toolkit (UIElements)** |
| Integration | New window, shared MeshAtlas backend; existing batch window untouched |
| Rotation | **Arbitrary angle from the start** (full affine UV math + rotated bake blit) |
| UV selection | **True connected-component islands** (adjacency / union-find) |
| Enhancements (post-F6) | Undo/redo · Snap + numeric fields · Flip H/V + fit-to-canvas · Auto-arrange (packer) |

## Tabs (exclusive, shared state)

- **Combine** — drag-drop multiple meshes; combine + auto-separate UV islands; bake source textures → new asset.
- **Mesh Select** — 3D viewport of combined mesh; vert/edge/face pick; selection stored in shared state.
- **UV Edit** — 0..1 canvas; draw islands (highlight those matching mesh selection); import images; affine
  transform (rotate/scale/translate) + tint; assign island→image region; bake → new asset.
- **Paint** — create blank texture; simple brush; output PNG usable as a UV-Edit image.

## Reuse vs new code

Reused as-is: `MeshCombiner`, `AtlasPacker`, `AtlasAssetWriter` (+ `WriteExisting`), `UrpLitMaterialFactory`,
`RendererCollector`, `MapBaker` + `EdgeDilation`, `UvRangeInspector`, `BakeOptions`, `ScalarFold` (tint reuse for
per-image color).

New code:
- `UVEditorWindow` — UI Toolkit shell, mode toolbar, intake panel, output controls, log.
- `UvCanvasElement` — custom `generateVisualContent` (Painter2D): grid, image quads, UV wireframe, gizmo.
- `CanvasController` — pan/zoom, hit-testing, selection state.
- `TransformGizmo` — move/rotate/scale handles.
- `AffineUVRemapper` — extends `UVRemapper` (translate+scale only) to a 2×3 affine matrix for rotation.
- `RotatedAtlasBlit` — bake-side rotated/tinted texture blit (Graphics.DrawTexture can't rotate → GL/material blit).
- `UvIslandFinder` — connected-component (union-find) grouping of UV triangles.
- `PlacedImage` (texture, affine xform, tint), `UvSelection` — editor-only state models.

## Technical realities

1. Rotation = affine (2×3), not Rect → `AffineUVRemapper` + rotated bake blit. Chosen up front.
2. UV islands = adjacency analysis (`UvIslandFinder`). Chosen up front (best unwrap UX).
3. UI Toolkit canvas (VisualElement + Painter2D + manipulators) is where most work lives.
4. Per-image tint reuses `ScalarFold.FoldAlbedo` (already multiplies a tint into atlas pixels).

## Feature implementation order (revised)

- **F1 Combine + auto-separate UV islands (FIRST)** — drag-drop multiple meshes → `MeshCombiner`; `UvIslandFinder`
  (union-find) detects islands across submeshes; pack into 0..1 + padding via `AtlasPacker` (keep aspect, no
  overlap); rewrite combined UVs; bake source textures + write mesh/material/prefab. Verify: 3 meshes combined,
  islands non-overlapping, prefab renders.
- **F2 Window shell + exclusive tabs** — `UVEditorWindow` (UI Toolkit), tab bar Combine/Mesh-Select/UV-Edit/Paint,
  shared session state across tabs. Combine tab hosts F1.
- **F3 Mesh-Select tab** — `PreviewRenderUtility` 3D viewport (orbit/zoom), render combined mesh, vert/edge/face
  select-mode toggle, click-pick via ray/handle, store selection in shared state.
- **F4 UV-Edit tab** — 0..1 `Painter2D` canvas (pan/zoom), draw islands + highlight mesh-selected islands, import
  images → `PlacedImage`, click-select + rotate(arbitrary)/scale/translate/tint via `AffineUVRemapper`,
  island→image assignment.
- **F5 Bake/write for edited layout** — bake placed (rotated+tinted) images via `RotatedAtlasBlit` + `ScalarFold`
  tint + `EdgeDilation`; `AtlasAssetWriter` → mesh + atlas + material + prefab.
- **F6 Paint tab** — create blank texture (size+fill), brush color/size/opacity + eraser → RenderTexture → save
  PNG; painted texture usable in UV-Edit.
- **Enhancements (after F1–F6)** — undo/redo, snap + numeric fields, flip H/V, fit-to-canvas, auto-arrange.

## Evaluation

- Reusability: backend math (`AffineUVRemapper`, `UvIslandFinder`, packer) are pure + unit-testable like existing
  MeshAtlas helpers; canvas is tool-specific.
- Maintainability: new window isolated; zero edits to working batch window → no regression risk.
- Testability: affine remap, island-find, rotated-blit-math unit-testable in `MeshAtlas.Tests`; canvas interaction
  is manual-verify.

## Risks

- UI Toolkit canvas interaction (gizmo math under zoom/pan) is the main effort + jank risk.
- Rotated bake blit must match the affine UV remap exactly or textures misalign — needs a paired unit/visual test.
- Arbitrary rotation can produce atlas overlap; auto-arrange (P5) mitigates but manual placement can still overlap.

## Next step

Run /t1k:plan to phase this into an implementation plan (P1–P5, file ownership, tests).
