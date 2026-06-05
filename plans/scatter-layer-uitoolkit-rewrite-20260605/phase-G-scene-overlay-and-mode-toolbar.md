# Phase G -- Scene overlay (Mode toolbar + Place/Erase + TRS gizmo + click-to-focus)

- Effort: L
- Parallel-safe with: F (after E).
- Blocks: I

## Scope

Scene-view UI for InstanceScatterLayer authoring. Adds an `Overlay` (Unity Scene-View Overlay API) with Mode toolbar (Select/Place/Erase + W/E/R + snap toggles + Place spacing + Erase brush radius). Mode-conditional behavior: Select mode shows TRS gizmo on the selected record + click-instance-in-scene focuses the inspector row; Place mode raycasts via `layer.GroundSnapMask` and drops records on click / Shift-drag (spaced by `layer.PlaceSpacing`); Erase mode shows a brush footprint + removes records inside on click/drag.

Rewrites the legacy `ScatterBrush.cs` -- old IMGUI brush is replaced by this overlay + scene tooling.

## File ownership

- Full rewrite: `Editor/ScatterBrush.cs` -- now a thin static helper that hosts the SceneView.duringSceneGui callback registration. Most logic moves to InstancePlacementOverlay + ModeToolbar.
- Fill stub from B: `Editor/UI/Components/ModeToolbar.cs`
- Fill USS: `Editor/UI/USS/ModeToolbar.uss`
- NEW: `Editor/UI/InstancePlacementOverlay.cs` (the Scene-View Overlay)
- NEW UXML: `Editor/UI/UXML/ModeToolbar.uxml`
- Consumes (from E): `Editor/InstanceSelectionService.cs`

## Pre-conditions

- Phase E merged (InstanceSelectionService + RecordList scroll-to-row API).
- Phase A's per-record collider field model in place.

## Step-by-step tasks

### G.1 -- InstancePlacementOverlay class

1. `[Overlay(typeof(SceneView), overlayId, "Scatter Placement")]` -- standard Unity overlay attribute.
2. `public override VisualElement CreatePanelContent()` returns ModeToolbar instance.
3. Static state: `Mode = Select | Place | Erase`, `Tool = Move | Rotate | Scale`, `SnapTranslate`, `SnapRotate`, `SnapScale`, `PlaceSpacing`, `EraseBrushRadius`.
4. Persist Mode/Tool/Snap state in EditorPrefs under keys `GrassInteract.Overlay.Mode` etc.
5. Subscribe to `SceneView.duringSceneGui` from `[InitializeOnLoadMethod]` static ctor; route per-mode handlers.

### G.2 -- ModeToolbar UXML + behavior

1. UXML: row of segmented buttons -- [Select][Place][Erase], then [Move][Rotate][Scale], then snap toggles, then context-sensitive fields (PlaceSpacing visible when Place, EraseBrushRadius visible when Erase).
2. Tooltip per button matches keyboard shortcut (W = Move, E = Rotate, R = Scale).
3. Click handlers write to InstancePlacementOverlay state + repaint SceneView.

### G.3 -- Select mode

1. Active when `InstancePlacementOverlay.Mode == Select`.
2. Reads `InstanceSelectionService.CurrentLayer` + `SelectedRecordIndex`.
3. Draws a TRS Handle at the record's world position (using `Handles.PositionHandle / RotationHandle / ScaleHandle` per `Tool`).
4. On Handle change: read new value, write back via `AuthoredInstancesData.SetRecord` + Undo register.
5. Click-instance-in-scene: every SceneView frame raycast under mouse against an in-memory list of record AABBs (computed from layer's records + LOD0 mesh bounds at record's scale). On click: find closest hit, `InstanceSelectionService.Select(layer, idx)`. The inspector's RecordList scrolls + highlights via its OnSelectionChanged subscription.
6. Snap-to-ground toggle: when on + translating, raycast against `layer.GroundSnapMask` to snap Y.

### G.4 -- Place mode

1. Active when Mode == Place.
2. Every SceneView frame: raycast under mouse against `layer.GroundSnapMask` (use existing `RaycastSurfaceSampler` or direct `Physics.Raycast`).
3. Draw ghost preview at hit point (mesh = layer.lods[0].mesh; thin wireframe via `Handles.DrawWireDisc` + `Graphics.DrawMeshNow` in `Handles.DrawingScope`).
4. On LMB click: snapshot for Undo; add record at hit.position with default rotation (or aligned-to-normal if layer.AlignToNormal). Scale defaults to (1,1,1) -- record picks up `layer.ScaleRange` randomization NO -- per brainstorm, Place mode is authoring; user can edit scale per-record after.
5. Shift-LMB drag: continuous placement; new record only when distance from last placed point >= `layer.PlaceSpacing`.
6. Pressing W/E/R while in Place mode does NOT switch tool (overlay owns the input); pressing Escape or clicking Select mode returns control.

### G.5 -- Erase mode

1. Active when Mode == Erase.
2. Every SceneView frame: draw a brush disc at mouse position with radius `EraseBrushRadius` on the ground.
3. Hover preview: highlight records that fall inside the disc (e.g. tint red via DrawWireMesh).
4. LMB click: remove every highlighted record via `AuthoredInstancesData.RemoveAt` -- one Undo group per click (multi-record delete batched).
5. LMB drag: continuous erase as the brush moves.

### G.6 -- ScatterBrush.cs rewrite

1. Old class held IMGUI brush state + drew gizmos. New form: thin static helper that initialises overlay state on domain load via `[InitializeOnLoadMethod]` + holds shared helpers (raycast utility wrappers that other systems can reuse).
2. Delete every IMGUI Handles.BeginGUI call -- overlay UI lives in UIToolkit ModeToolbar now.
3. KEEP any utility that other code references (grep before deleting).

### G.7 -- Domain-reload state restore

1. After domain reload, the `[Overlay]` API restores layout but our STATIC state (Mode, Tool, snap) is lost. Use `[InitializeOnLoadMethod]` to read from EditorPrefs on load.
2. Test: enter Place mode, save scene, trigger domain reload (touch a .cs file) -- Place mode still active.

## Validation criteria

1. Compile clean.
2. Select a ScatterField with an InstanceScatterLayer; overlay appears at default position in scene view.
3. Toggle Mode = Place; click on terrain -> a record is added; tile-grid layer's instance count increments; inspector record list adds an entry.
4. Shift-drag in Place mode: records spaced by `placeSpacing` -- not bunched.
5. Toggle Mode = Erase; hover over a cluster of records -> they highlight red; click -> they vanish + Undo registers.
6. Toggle Mode = Select; click on a record in the scene -> inspector's RecordList scrolls + highlights the row; the TRS Handle appears.
7. CTRL+Z undoes last Place/Erase/move group.
8. Domain reload mid-Place mode: mode is preserved.
9. Light + dark theme: overlay toolbar renders cleanly.
10. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Overlay attribute API differences between Unity versions (6000.3.x baseline) | 2 | 4 | 8 | Test against the project's actual Unity version; if API diverges, fall back to `EditorWindow`-as-overlay pattern. Document in EDITOR-UI-GUIDE.md. |
| Place mode consumes input that the user wanted for Unity's own tools | 3 | 3 | 9 | Only consume Event in Place/Erase modes when mouse is over the scene view AND overlay says active. In Select mode, Unity's default tools (W/E/R) still work. |
| Erase brush hover preview re-computes record-in-disc list every frame -> stall on 10K records | 3 | 3 | 9 | Throttle to 10Hz via timeSinceStartup; precompute record XY positions once per scene-gui session and re-use until selection changes. |
| Undo group not closed on click -- multi-record erase fragments into N undos | 3 | 3 | 9 | Wrap erase op in `Undo.IncrementCurrentGroup()` + `Undo.SetCurrentGroupName` + `Undo.CollapseUndoOperations`. |
| Raycast against per-record AABBs is slow when records are thousands | 3 | 3 | 9 | Build a flat KD-tree-like XZ-cell grid on InstanceSelectionService.Select(); only test cells under cursor. |

## Effort: L

Estimate 6-8 hours. Overlay + 3 modes + per-mode input handling + click-to-focus + domain reload. Heaviest input/UX complexity.
