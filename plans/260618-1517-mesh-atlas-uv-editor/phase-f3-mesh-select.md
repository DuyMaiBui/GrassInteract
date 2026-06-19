# Phase F3 — Mesh-Select tab

Effort: **L** · Blocked by: F2 · Blocks: F4 (selection drives UV-island highlight).

## Goal

A 3D viewport (`PreviewRenderUtility`) inside the Mesh-Select tab that renders `session.CombinedMesh`, supports orbit + zoom, exposes a vert/edge/face select-mode toggle, and lets the user click-pick an element. The picked element(s) are stored in `session.Selection` (`UvSelection`) so F4 can highlight the matching UV island(s).

## File ownership

Create:
- `Editor/Mesh/UvSelection.cs` — model: `enum SelectMode { Vertex, Edge, Face }`, current mode, `HashSet<int> SelectedVertices`, derived selected triangle/island ids. Pure editor-only. ≤90 lines.
- `Editor/Mesh/MeshPreviewViewport.cs` — wraps `PreviewRenderUtility`: `BeginPreview`/`Render`/`EndAndDrawPreview` into an `IMGUIContainer` (UIElements hosts IMGUI for the 3D preview — `PreviewRenderUtility` is IMGUI-based), camera orbit (yaw/pitch from drag delta) + zoom (scroll). Owns dispose (`Cleanup()` called by tab `Detach`/window `OnDisable`). ≤180 lines.
- `Editor/Mesh/MeshElementPicker.cs` — PURE: `static int PickVertex(Mesh, Ray, Matrix4x4 model, float screenTol, ...)`, `PickFace(...)` (Möller–Trumbore ray-triangle), `PickEdge(...)`. Nearest-hit wins; screen-space tolerance for vert/edge. NO PreviewRenderUtility dependency — viewport feeds it a world ray. ≤180 lines.
- `Editor/UI/Tabs/MeshSelectTab.cs` — `IUVEditorTab`: hosts the viewport `IMGUIContainer` + a mode toolbar (Vertex/Edge/Face); on click, builds a ray from the preview camera + mouse, calls `MeshElementPicker`, writes `session.Selection`, raises `session.Changed`. ≤180 lines.

Test (create):
- `Tests/Editor/MeshElementPickerTests.cs` — known mesh (unit quad/cube) + a ray aimed at a known vertex/face → picker returns that vertex index / triangle; a near-miss ray within tolerance still hits; a far ray misses (returns -1). Möller–Trumbore correctness on a single triangle (hit inside, miss outside).

Edit: `Editor/UI/UVEditorSession.cs` — ADD `UvSelection Selection` field + init (F3's clearly-commented block).

## Reuse map

- `PreviewRenderUtility` (`UnityEditor`) — editor-shipped, no new ref.
- `session.CombinedMesh` from F1/F2.
- No existing MeshAtlas backend reuse (this is new viewport surface) — the report lists no reuse here.

## Design notes

- `PreviewRenderUtility` is IMGUI; embed via `IMGUIContainer` inside the UIElements tab. This is the sanctioned bridge — document it (the report says "PreviewRenderUtility 3D viewport"; UIElements hosts it through IMGUIContainer).
- Picking math lives in the PURE `MeshElementPicker` so it is unit-testable WITHOUT a live preview. The tab only constructs the ray (camera → mouse) and passes it in. This keeps the risky part testable.
- Selection is element-level but F4 needs island-level highlight: `UvSelection` exposes selected vertices; F4 maps vertices→islands (an island owns vertex indices). Define that mapping in F3's `UvSelection` (it has `session.Islands`).

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| `PreviewRenderUtility` leaked (not disposed) → editor warning spam / GPU leak | 3 | 3 | 9 | `MeshPreviewViewport.Cleanup()` in tab `Detach` + window `OnDisable`; never recreate without disposing. |
| Element picking mis-picks under orbit (ray construction wrong) | 3 | 3 | 9 | Pure `MeshElementPicker` unit-tested; nearest-hit + screen-space tol; verify ray from `PreviewRenderUtility` camera matrices. |
| IMGUI-in-UIElements input quirks (scroll/drag eaten) | 2 | 3 | 6 | `IMGUIContainer` is the documented host; handle `Event.current` inside the IMGUI callback only. |

No score ≥ 15.

## Verify gate

- Unit: `MeshElementPickerTests` GREEN (fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort).
- Manual: open UV Editor → Mesh-Select tab on a combined mesh; orbit + zoom work; toggle Vertex/Edge/Face; click picks the correct element (highlighted); switch to UV-Edit (F4) and back → selection persists in `session.Selection`. No `PreviewRenderUtility` leak warning on window close.
