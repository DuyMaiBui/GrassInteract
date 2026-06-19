# Plan: Interactive UV Editor (MeshAtlas.UVEditor)

Source of truth: `plans/reports/2026-06-18-mesh-atlas-uv-editor-brainstorm.md` (design approved, decisions locked — not re-litigated here).
Target assembly: `MeshAtlas.Editor` (Editor-only, no external refs) under `Assets/MeshAtlas/`. Tests: `MeshAtlas.Tests` (NUnit EditMode).
Conventions: `this.` prefix mandatory; private fields camelCase (no underscore); public PascalCase; one responsibility per file (~≤200 lines).

## What we are building

ONE UI Toolkit (UIElements) `EditorWindow` at menu `Tools/Mesh Atlas/UV Editor` with **exclusive tabs** — Combine · Mesh Select · UV Edit · Paint — over a **shared session-state model**. Output is always a NEW combined asset (mesh + atlas PNG(s) + URP/Lit material + prefab); source assets are never mutated. The existing batch `AtlasBakerWindow` (`Tools/Mesh Atlas/Combine & Bake`) is untouched → zero regression surface.

## Phases (implementation order is locked by the report)

- **Phase F1 — Combine + auto-separate UV islands (vertical slice, FIRST)** | Effort: L
  - Scope: drag-drop meshes → `MeshCombiner`; NEW `UvIslandFinder` (union-find over UV0 triangle adjacency); pack islands into 0..1 with padding via `AtlasPacker` (keep aspect, no overlap); rewrite combined-mesh UVs; bake source textures + write mesh/material/prefab via `MapBaker`/`AtlasAssetWriter`. Produces a usable prefab with NO window shell (driven by a temporary EditorWindow or a unit-tested pipeline entry).
  - Owns (new): `Editor/Islands/UvIslandFinder.cs`, `Editor/Islands/UvIsland.cs`, `Editor/Islands/IslandPacker.cs`, `Editor/UVEditorPipeline.cs`; (tests) `Tests/Editor/UvIslandFinderTests.cs`, `Tests/Editor/IslandPackerTests.cs`.
- **Phase F2 — Window shell + exclusive tabs** | Effort: M
  - Scope: NEW `UVEditorWindow` (UI Toolkit), tab bar, shared session-state model; Combine tab hosts F1's UI.
  - Owns (new): `Editor/UI/UVEditorWindow.cs`, `Editor/UI/UVEditorSession.cs`, `Editor/UI/Tabs/CombineTab.cs`, `Editor/UI/Tabs/IUVEditorTab.cs`, `Editor/UI/UVEditor.uss`.
- **Phase F3 — Mesh-Select tab** | Effort: L
  - Scope: `PreviewRenderUtility` 3D viewport (orbit/zoom), render combined mesh, vert/edge/face select-mode toggle, click-pick via ray, store selection in shared `UvSelection`.
  - Owns (new): `Editor/UI/Tabs/MeshSelectTab.cs`, `Editor/Mesh/MeshPreviewViewport.cs`, `Editor/Mesh/MeshElementPicker.cs`, `Editor/Mesh/UvSelection.cs`; (tests) `Tests/Editor/MeshElementPickerTests.cs`.
- **Phase F4 — UV-Edit tab** | Effort: L
  - Scope: 0..1 `Painter2D` canvas (pan/zoom), draw UV islands + highlight islands matching mesh selection, import image(s) → NEW `PlacedImage`, click-select + arbitrary-angle rotate/scale/translate/tint via NEW `AffineUVRemapper`, island→image assignment.
  - Owns (new): `Editor/UI/Tabs/UvEditTab.cs`, `Editor/Canvas/UvCanvasElement.cs`, `Editor/Canvas/CanvasController.cs`, `Editor/Canvas/TransformGizmo.cs`, `Editor/Canvas/CanvasTransform.cs`, `Editor/Packing/AffineUVRemapper.cs`, `Editor/Canvas/PlacedImage.cs`; (tests) `Tests/Editor/AffineUVRemapperTests.cs`, `Tests/Editor/CanvasTransformTests.cs`.
- **Phase F5 — Bake/write for edited layout** | Effort: M
  - Scope: bake placed (rotated + tinted) images into atlas via NEW `RotatedAtlasBlit` (GL/material quad blit — `Graphics.DrawTexture` cannot rotate) + `ScalarFold` tint + `EdgeDilation`; `AtlasAssetWriter` → new asset.
  - Owns (new): `Editor/Baking/RotatedAtlasBlit.cs`, `Editor/Baking/PlacedImageBaker.cs`; (edit) `Editor/UI/Tabs/UvEditTab.cs` (wire Bake button); (tests) `Tests/Editor/RotatedAtlasBlitMathTests.cs`.
- **Phase F6 — Paint tab** | Effort: M
  - Scope: create blank texture (size + fill), simple brush (color/size/opacity + eraser) painting to a RenderTexture, save PNG; painted texture usable as a UV-Edit image. v1 = simple brush only.
  - Owns (new): `Editor/UI/Tabs/PaintTab.cs`, `Editor/Paint/PaintCanvas.cs`, `Editor/Paint/BrushStroke.cs`, `Editor/Paint/PaintTextureWriter.cs`; (tests) `Tests/Editor/BrushStrokeTests.cs`.
- **Enhancements phase (after F1–F6)** | Effort: M
  - Scope: undo/redo, snap + numeric fields, flip H/V, fit-to-canvas, auto-arrange (AtlasPacker on placed images). Detailed in `phase-enhancements.md`.

## Feasibility

- Reuse check: `MeshCombiner`, `AtlasPacker`, `AtlasAssetWriter`(+`WriteExisting`), `MapBaker`, `ScalarFold`, `EdgeDilation`, `UrpLitMaterialFactory`, `BakeInput`, `CombineItem`/`CombineItemBuilder`, `UvRangeInspector` — all reused as-is. NEW only where the report names a type.
- Complexity: moderate→complex. Pure-math new code (island-find, affine remap, rotated-blit math, packing) is the low-risk, high-test core. Interactive UI Toolkit canvas + 3D viewport picking is the complex, manual-verify surface.
- Dependency seam: UI Toolkit (`UnityEngine.UIElements`) and `PreviewRenderUtility` (`UnityEditor`) ship with the editor — no asmdef reference change, no third-party dependency, so `library-third-party-decoupling` does not apply.

## Dependency graph

- F1 → F2 (Combine tab hosts F1 UI; F1's pipeline is independently testable first).
- F2 → F3, F4, F6 (all tabs need the shell + session model).
- F3 → F4 (mesh selection drives island highlight in UV-Edit; F4 can build canvas first, wire highlight after F3).
- F4 → F5 (F5 bakes the `PlacedImage` layout F4 produces).
- Enhancements → after F1–F6 (depends on canvas + bake being complete).
- Parallel-safe: F1's pure-math files (`UvIslandFinder`, `IslandPacker`) and F4's `AffineUVRemapper` have NO shared files and NO runtime dependency on each other — they can be authored in parallel. F5's `RotatedAtlasBlit` math can be authored in parallel with F4 UI (it only depends on the affine matrix contract).
- File-ownership: no two phases create the same file. F5 is the only phase that EDITS a file owned by an earlier phase (`UvEditTab.cs`, F4) — sequence F5 strictly after F4.
- Critical path: F1 → F2 → F4 → F5.

## Risk Assessment (aggregate — per-phase tables in phase files)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Rotated bake blit (`RotatedAtlasBlit`) does not match `AffineUVRemapper` exactly → textures misalign on the mesh | 4 | 5 | 20 | HIGH. Pair the two: define ONE shared affine contract (`AffineUVRemapper` UV→atlas matrix); `RotatedAtlasBlit` consumes the SAME matrix to place the quad. Unit-test corner-mapping parity (UV corners → atlas pixels) before any visual bake. Mirror `MapBaker.ToScreenRect` Y-flip convention exactly. |
| UI Toolkit canvas gizmo math wrong under combined pan/zoom (screen↔UV round-trip drifts) | 4 | 4 | 16 | HIGH. Isolate ALL pan/zoom/screen↔UV math into pure `CanvasTransform` (no VisualElement deps) and unit-test the round-trip (`ScreenToUv(UvToScreen(p)) == p`) across zoom/pan combinations. Gizmo reads only `CanvasTransform`. |
| 3D viewport element picking (`PreviewRenderUtility` ray vs vert/edge/face) mis-picks or is unstable | 3 | 3 | 9 | Pure ray-vs-element math in `MeshElementPicker` (unit-tested); viewport only feeds it a ray. Tolerance in screen space, nearest-hit wins. |
| Island packing produces overlap / aspect distortion | 2 | 4 | 8 | `IslandPacker` reuses proven `AtlasPacker` (already gutters every rect); unit-test no-overlap + aspect-preserved on a multi-island fixture. |
| `run_tests` MCP silently drops `MeshAtlas.Tests` (reports total:0) → false "all green" | 3 | 3 | 9 | Compile signal = fresh `Library/ScriptAssemblies/*.dll` mtime + 0 console errors; `run_tests` is best-effort only. Stated in every phase's verify gate. |
| New `.cs` files not imported (phantom CS0246 on same-namespace sibling) | 3 | 2 | 6 | After writing brand-new files, `refresh_unity(force, all)` — not `scope=scripts` (project memory: new files need force/all). |

No score ≥ 15 lacks a documented mitigation. The two ≥15 risks (rotated-blit parity, canvas gizmo math) are mandated to be unit-test-gated BEFORE their interactive surfaces are wired.

## Backwards compatibility

Purely additive. New window, new files, new menu item. `AtlasBakerWindow` and all existing backend signatures are untouched (we only CALL them). No migration path needed. Existing tests must still pass after every phase (regression gate).

## Test matrix (pass/fail per phase)

| Phase | Pure-math unit tests (NUnit EditMode) | Manual verify |
|-------|----------------------------------------|---------------|
| F1 | `UvIslandFinderTests` (connectivity, island count), `IslandPackerTests` (no overlap, aspect kept, fits 0..1) | Combine 3 meshes → islands non-overlapping, prefab renders in scene |
| F2 | — (shell) | Window opens; 4 tabs switch; session state survives tab switch |
| F3 | `MeshElementPickerTests` (ray→nearest vert/edge/face) | Orbit/zoom; click picks correct element; selection persists to UV-Edit |
| F4 | `AffineUVRemapperTests` (identity/translate/scale parity w/ `UVRemapper` + rotation correctness), `CanvasTransformTests` (screen↔UV round-trip) | Import image; rotate/scale/move/tint; mesh-selected island highlights |
| F5 | `RotatedAtlasBlitMathTests` (quad corners == affine-remapped UV corners, Y-flip matches `MapBaker`) | Bake rotated+tinted layout → texture aligns on mesh in prefab |
| F6 | `BrushStrokeTests` (stamp coverage / opacity accumulation math) | Paint strokes + eraser; save PNG; reload as UV-Edit image |

Compile gate (all phases): fresh `Library/ScriptAssemblies/MeshAtlas.Editor.dll` + `MeshAtlas.Tests.dll` mtime advances AND `read_console` shows 0 errors. `run_tests` best-effort (may report total:0 — do not trust a 0 as pass).

## Rollback plan

Every phase is additive new files (except F5's edit to `UvEditTab.cs`). Rollback = delete the phase's new files (+ `.meta`) and revert the single F5 edit. No earlier phase or the existing batch window is affected, so rollback never cascades. Git: each phase commits independently on a feature branch; `git revert` of a phase commit is clean because file ownership does not overlap.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| F1 Combine + island separate | L | No blockers; vertical slice. Pure-math heavy → front-load tests. |
| F2 Window shell + tabs | M | Blocked by F1 (hosts F1 UI). |
| F3 Mesh-Select tab | L | Blocked by F2. Viewport picking is the risk. |
| F4 UV-Edit tab | L | Blocked by F2; highlight wiring blocked by F3. Canvas gizmo = highest UI risk. |
| F5 Bake edited layout | M | Blocked by F4. Rotated-blit parity = highest overall risk. |
| F6 Paint tab | M | Blocked by F2 only (independent of F3/F4/F5). |
| Enhancements | M | After F1–F6. |
| **Total** | **~3L + 3M + 1M** | Critical path: **F1 → F2 → F4 → F5**. F6 parallelizable off F2; F3 parallelizable off F2. |

## Phase files

- `phase-f1-combine-islands.md`
- `phase-f2-window-shell.md`
- `phase-f3-mesh-select.md`
- `phase-f4-uv-edit.md`
- `phase-f5-bake-edited.md`
- `phase-f6-paint.md`
- `phase-enhancements.md`
