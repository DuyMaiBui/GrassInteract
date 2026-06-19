# Phase F2 — Window shell + exclusive tabs

Effort: **M** · Blocked by: F1 · Blocks: F3, F4, F6.

## Goal

NEW UI Toolkit `EditorWindow` at `Tools/Mesh Atlas/UV Editor` with an exclusive tab bar (Combine · Mesh Select · UV Edit · Paint) over a shared session-state model. Only one tab's content is visible at a time; shared state (combined mesh, element selection, placed images, paint output) survives tab switches. The Combine tab hosts F1's UI.

## File ownership

Create:
- `Editor/UI/UVEditorWindow.cs` — `[MenuItem("Tools/Mesh Atlas/UV Editor")]`; `CreateGUI()` builds the tab bar + a content `VisualElement`; switching a tab calls `tab.Detach()` on the old and `tab.Attach(root, session)` on the new. Holds the single `UVEditorSession`. ≤150 lines.
- `Editor/UI/UVEditorSession.cs` — shared session model: `Mesh CombinedMesh`, `List<UvIsland> Islands`, `UvSelection Selection` (added F3), `List<PlacedImage> PlacedImages` (added F4), `Texture2D PaintOutput` (added F6), output folder/baseName. Plain editor-only class; raises a simple `event Action Changed` so tabs refresh. ≤120 lines. (F3/F4/F6 ADD fields here — sequenced, never concurrent edits to the same region.)
- `Editor/UI/Tabs/IUVEditorTab.cs` — interface: `string Title`, `void Attach(VisualElement parent, UVEditorSession session)`, `void Detach()`. ≤30 lines.
- `Editor/UI/Tabs/CombineTab.cs` — hosts F1: mesh drop-area (UI Toolkit drag-drop / object fields), "Combine + Separate" button → calls `UVEditorPipeline`, writes result into `session.CombinedMesh` + `session.Islands`, log label. ≤180 lines.
- `Editor/UI/UVEditor.uss` — shared styles (tab bar, active-tab highlight, canvas frame). ≤120 lines.

Edit: none of F1's files (CombineTab CALLS `UVEditorPipeline`).

## Reuse map

- F1 `UVEditorPipeline` — called by `CombineTab`.
- UI Toolkit `Toolbar` / `ToolbarToggle` (or plain `Button`s) for the tab bar — `UnityEditor.UIElements` / `UnityEngine.UIElements`, editor-shipped.

## Design notes

- Tabs are lazy: a tab builds its content on first `Attach`. Detach hides (or removes) it. Mesh-Select's `PreviewRenderUtility` (F3) MUST be disposed on window `OnDisable` — the session/tab owns a `Cleanup()` the window calls.
- Stub tabs (MeshSelect/UvEdit/Paint) ship as empty `IUVEditorTab` placeholders in F2 so the bar is complete; F3/F4/F6 fill them. Each placeholder file is OWNED by its later phase — F2 creates only `CombineTab` + the interface; F3/F4/F6 create their own tab files. (F2's tab bar references them by interface, instantiated via a small registry list the later phases append to — no F2 edit needed if the bar enumerates a `List<IUVEditorTab>` the window builds; later phases add their `new XTab()` to that list. Document this seam.)

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| `PreviewRenderUtility` leaked across tab switches/window reopen (F3 fallout, but seam defined here) | 3 | 3 | 9 | Define `IUVEditorTab.Detach()` + window `OnDisable` cleanup contract NOW; F3 implements dispose. |
| Session field additions by F3/F4/F6 collide | 2 | 2 | 4 | Sequence the phases; each adds its own clearly-commented field block. |
| UI Toolkit drag-drop of mesh assets unfamiliar/janky | 2 | 2 | 4 | Fall back to `ObjectField`(s) + "Add" if DnD is flaky; report's "drag-drop" is satisfied by either. |

No score ≥ 15.

## Verify gate

- Compile: fresh `MeshAtlas.Editor.dll` mtime + 0 console errors (`refresh_unity(force, all)` for the new files).
- Manual: `Tools/Mesh Atlas/UV Editor` opens; tab bar shows 4 tabs; clicking each switches content with the active tab highlighted; the Combine tab reproduces F1 (combine 3 meshes → prefab) and leaves `session.CombinedMesh`/`session.Islands` populated (verify by switching to a stub tab and back — state persists). Existing `Combine & Bake` window still opens and works (regression).
