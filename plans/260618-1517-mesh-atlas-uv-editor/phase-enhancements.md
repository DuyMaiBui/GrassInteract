# Phase Enhancements — post-F1–F6 polish

Effort: **M** (each item S–M) · Blocked by: F1–F6 complete. Independent items — parallelizable, ship incrementally.

These are the locked-in enhancements from the report's "Enhancements (after F1–F6)" row. Each is additive; none blocks another except auto-arrange (needs the canvas + placed images, i.e. F4).

## Items

### E1 — Undo/Redo (Effort: M)
- Scope: undo/redo for canvas edits (placed-image transforms, tint, island assignment) and combine/island operations.
- Owns (new): `Editor/UI/UVEditorUndo.cs` — a small command-stack OR integrate Unity's `Undo.RegisterCompleteObjectUndo` on a serializable session backing object. Prefer Unity `Undo` if the session is made a `ScriptableObject`; otherwise a typed command stack.
- Edit: `CanvasController.cs`, `UvEditTab.cs` (push undo entries on mutation).
- Test: command-stack push/pop/redo ordering (`Tests/Editor/UVEditorUndoTests.cs`) if a custom stack is used.
- Risk: low (L2/I2). Mitigation: choose ONE mechanism (Unity Undo vs custom) up front; don't mix.

### E2 — Snap + numeric fields (Effort: S)
- Scope: snap translate/rotate/scale to increments; numeric entry fields for exact transform values on the selected `PlacedImage`.
- Owns (new): `Editor/Canvas/SnapSettings.cs` (pure: `Snap(value, increment)`).
- Edit: `UvEditTab.cs` (numeric fields), `CanvasController.cs`/`TransformGizmo.cs` (apply snap).
- Test: `Tests/Editor/SnapSettingsTests.cs` (round-to-increment correctness, zero-increment = no snap).
- Risk: low (L1/I2).

### E3 — Flip H/V + fit-to-canvas (Effort: S)
- Scope: flip a placed image horizontally/vertically (negative scale on the affine matrix); fit-to-canvas frames the 0..1 region in the viewport.
- Owns: none new (operations on `AffineUVRemapper` + `CanvasTransform`).
- Edit: `UvEditTab.cs` (buttons), `CanvasController.cs` (fit), `AffineUVRemapper.cs` ONLY IF a flip helper is added (additive static).
- Test: extend `AffineUVRemapperTests` (flip = negative-scale, double-flip = identity); `CanvasTransformTests` (fit frames 0..1).
- Risk: low (L2/I2). Flip via negative scale must keep winding sane at bake → verify in F5's blit.

### E4 — Auto-arrange (packer on placed images) (Effort: M)
- Scope: one-click re-pack of all placed images into non-overlapping 0..1 regions via `AtlasPacker` — mitigates the manual-overlap risk from F4.
- Owns (new): `Editor/Canvas/PlacedImageArranger.cs` — pure: feed each `PlacedImage`'s current bounding size to `AtlasPacker.Pack`, write the resulting sub-rect back as a translate+scale affine (preserving any user rotation as a separate compose, or arrange axis-aligned bounds). ≤120 lines.
- Edit: `UvEditTab.cs` (Auto-arrange button).
- Test: `Tests/Editor/PlacedImageArrangerTests.cs` (N images → non-overlapping, all inside 0..1).
- Risk: low-moderate (L2/I3). Rotation+arrange interaction: arrange the rotated AABB, keep rotation; document.

## Aggregate Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Undo mechanism mixing (Unity Undo + custom stack) causes desync | 2 | 3 | 6 | Pick one mechanism; document in E1. |
| Auto-arrange loses user rotation | 2 | 3 | 6 | Arrange AABB, re-compose rotation; unit-test. |
| Snap math off-by-one at zero increment | 1 | 2 | 2 | `Snap` returns input when increment ≤ 0. |

No score ≥ 15.

## Verify gate

- Unit: `SnapSettingsTests`, `PlacedImageArrangerTests`, undo-stack tests, extended `AffineUVRemapperTests` GREEN (fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort).
- Manual: undo/redo a transform; numeric field sets exact value; snap clamps to increment; flip H/V mirrors correctly and bakes correct; fit-to-canvas frames 0..1; auto-arrange packs placed images with no overlap and they still bake aligned (re-run F5 verify).
