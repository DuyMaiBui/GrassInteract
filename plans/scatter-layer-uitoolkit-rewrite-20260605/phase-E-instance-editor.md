# Phase E -- InstanceScatterLayerEditor (authored-record inspector)

- Effort: L
- Parallel-safe with: D (after C). Different files, no shared props.
- Blocks: G (scene overlay reads from the same selected-record state)

## Scope

Build the UIToolkit inspector for InstanceScatterLayer. Reorderable record list + virtualized rows + expandable per-record collider sub-row + drag-prefab/GameObject drop zone + selected-record detail panel (TRS + per-record collider config) [renderer override is GONE per D1 strict-V2] + Defaults section (layer-default collider mesh + convex + placeSpacing). Wire selection state out to the scene overlay (G consumes; E exposes via a session-scoped EditorPref or a static `InstanceSelectionService` introduced here).

Per brainstorm: drag-prefab depth-limited to 1024 transforms with warning.

## File ownership

- NEW: `Editor/InstanceScatterLayerEditor.cs`
- NEW: `Editor/InstanceSelectionService.cs` -- static class holding `currentLayer` + `selectedRecordIndex` + `OnSelectionChanged` event. Reused by G's scene overlay.
- Fill stubs from B: `Editor/UI/Components/RecordList.cs`, `RecordRow.cs`, `RecordDetailPanel.cs`
- NEW UXML: `Editor/UI/UXML/InstanceLayer.uxml` (root), `AuthoredInstancesSection.uxml`, `DefaultsSection.uxml`, `RecordRow.uxml`, `RecordDetailPanel.uxml`

## Pre-conditions

- Phase C merged.
- Phase A's AuthoredInstancesData V2 schema (scaleMultiplier on ColliderOverrideData) is live.
- Default_LOD0_Prop + Default_Material exist.

## Step-by-step tasks

### E.1 -- InstanceScatterLayerEditor.cs

1. `[CustomEditor(typeof(InstanceScatterLayer))]`.
2. `CreateInspectorGUI()` returns `InstanceScatterLayerPanel`.
3. Panel loads `InstanceLayer.uxml`. Sections in order:
   - Header (rename + kind icon).
   - KindAndDeform (same as D.3 but kind label = "Instance (authored records)").
   - AuthoredInstancesSection (record list + drop zone + detail panel).
   - RenderingSection (material, shadowCastingMode -- same as D.7).
   - DefaultsSection (defaultColliderMesh, defaultColliderConvex, placeSpacing).
   - WindSection / TrampleSection / BoundsAndGpuSection -- reused from D? Yes -- factor these into shared UXML in B's scaffold IF not already (note for E to coordinate with D: if D shipped them inline, E will duplicate them; both sub-tasks should share the same per-section UXML files, owned by B's scaffold from the start. This was already specified in B's file ownership.)
   - LodSection slot (F fills).

### E.2 -- AuthoredInstancesSection (record list + drop + detail)

1. UXML layout: left = RecordList (40% width), right = RecordDetailPanel (60% width), top = DropZone strip.
2. DropZone: a styled VisualElement with "Drop a prefab or GameObject here to import records" label. Handles `DragUpdatedEvent` (sets DragAndDrop.visualMode = Copy) and `DragPerformEvent` (enumerate dragged objects).

### E.3 -- RecordList (virtualized ListView)

1. `RecordList : VisualElement` wrapping a `ListView`.
2. Bound to `layer.AuthoredInstances.WorkingList` via a SerializedProperty proxy. Since AuthoredInstancesData stores records in a byte blob, the editor uses a "live view" pattern: read records from `WorkingList` on bind, render rows; on edits, write back through `SetRecord` + mark dirty + force PackBlob on save (via SerializedObject.ApplyModifiedProperties indirect through `EditorUtility.SetDirty`).
3. ListView config: `fixedItemHeight = 28`, `reorderable = true`, `reorderMode = ListViewReorderMode.Animated`.
4. Row template: a `RecordRow`.
5. Selection change -> call `InstanceSelectionService.Select(layer, idx)` -> RecordDetailPanel updates AND scene overlay (G) updates.

### E.4 -- RecordRow (compact + expandable collider sub-row)

1. UXML: row root = horizontal layout, 28 px tall when collapsed.
2. Contents: index label, position Vector3Field (compact), small "expand" button (caret), delete button (trash icon), duplicate button, ping-in-scene button.
3. Expanded state: appends a sub-row beneath with the per-record collider override toggle group (generateCollider Toggle, colliderConvex Toggle, colliderOverride ObjectField (Mesh; null = use layer default), colliderScale FloatField (default 1.0)).
4. Expand state is per-row local; not persisted across reloads.
5. Edits write through `AuthoredInstancesData.SetRecord` (the per-record collider fields are now inline on `InstanceRecord` per D1 strict-V2 -- no separate `SetColliderOverride` call exists).

### E.5 -- RecordDetailPanel (right pane for the selected record)

1. Subscribes to `InstanceSelectionService.OnSelectionChanged`.
2. Shows the FULL record: `Vector3 position`, `Quaternion rotation` (as EulerField), `float scale` (uniform; FloatField). Per D1 strict-V2 §2 the record carries no Vector3 scale and no RendererOverride.
3. Per-record collider sub-section (always visible in the detail panel, even when the toggle is off, so the user can edit fields then enable): Toggle `generateCollider`, ObjectField `colliderOverride` (Mesh; null = use layer default), FloatField `colliderScale` (default 1), Toggle `colliderConvex`.
4. The sub-section is the SAME field set as RecordRow expanded -- redundant by design (panel is always-visible vs. row expands on demand). Edits write through `AuthoredInstancesData.SetRecord` with `Undo.RegisterCompleteObjectUndo` per edit group.
5. "Focus in scene" button: pings the record's position via SceneView.lastActiveSceneView.LookAt.
6. **Explicitly NOT present** (removed per D1 strict-V2): material override, shadowMode override, any renderer-related field. The whole record renders with the layer's single `material`.

### E.6 -- DropZone import flow

1. On DragPerform: enumerate `DragAndDrop.objectReferences`.
2. For each GameObject / prefab in the drop:
   - Walk its Transform hierarchy depth-first, gathering up to 1024 transforms total (across all dropped roots).
   - If exceeded: `EditorUtility.DisplayDialog("Import limit", "Drop contains more than 1024 transforms; only the first 1024 are imported.", "OK")`.
   - For each transform: build an InstanceRecord with position + rotation from the local-to-world matrix; uniform `float scale` from the average of the matrix lossy-scale XYZ (log one notice per import if any transform was non-uniform). If the transform has a MeshCollider, populate per-record collider fields inline: `colliderOverride = collider.sharedMesh` (via `EnsureObjectRef`), `colliderConvex = collider.convex`, `colliderScale = 1f`, `generateCollider = true`. Otherwise `generateCollider = false`.
   - Append via `AuthoredInstancesData.AddRecord`.
3. After import: `EditorUtility.SetDirty(layer.AuthoredInstances)` + `AssetDatabase.SaveAssets()` + Undo register.
4. Document the depth limit in EDITOR-UI-GUIDE.md (Phase I).

### E.7 -- DefaultsSection

1. ObjectField defaultColliderMesh.
2. Toggle defaultColliderConvex.
3. Slider placeSpacing (0.05..5).
4. Helper text: "When a record's per-record collider is set but its mesh override is null, this default mesh is used."

### E.8 -- InstanceSelectionService

1. `internal static class InstanceSelectionService` (Editor asmdef).
2. Fields: `static InstanceScatterLayer? CurrentLayer`, `static int SelectedRecordIndex = -1`.
3. Event: `static event Action<InstanceScatterLayer?, int>? OnSelectionChanged`.
4. API: `void Select(InstanceScatterLayer? layer, int idx)`, `void Clear()`.
5. Reused by Phase G scene overlay (G subscribes; E publishes).

## Validation criteria

1. Compile clean.
2. Create an empty InstanceScatterLayer; record list is empty + drop zone visible.
3. Drag a simple prefab (e.g. a cube) into the drop zone -- a record is added; select it in the list; detail panel shows position/rot/scale.
4. Edit position in detail panel -> record updates -> blob re-packs on save -> reload Unity -> record persists.
5. Mark per-record `generateCollider = true` -> sub-row appears with mesh + convex + scale fields.
6. Drop a prefab containing > 1024 transforms -> warning dialog appears; first 1024 imported.
7. Light + dark theme both render cleanly.
8. Selection change publishes to InstanceSelectionService (verify with a debug log subscriber).
9. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| ListView reorder breaks blob ordering | 4 | 4 | **16** | Reorder writes through `WorkingList` mutation; the blob is repacked on Apply (PackBlob walks `workingList` in current order). EditMode test: reorder 5 records 10 times, save+reload, asserts order preserved. Add to A.0's roundtrip suite. |
| Drag-prefab on deeply nested prefab variants confuses transform walk | 3 | 3 | 9 | Walk uses `PrefabUtility.GetCorrespondingObjectFromSource` to resolve prefab content correctly; document in EDITOR-UI-GUIDE.md. |
| RecordRow's compact Vector3Field collapses on narrow inspector widths | 3 | 2 | 6 | USS sets `min-width: 200px` on the row; horizontal scrollbar appears when inspector is narrower. |
| InstanceSelectionService leaks across domain reload | 3 | 2 | 6 | `[InitializeOnLoadMethod]` resets on reload; not persisted to EditorPrefs (single-session). |

## Effort: L

Estimate 6-8 hours. Most complex of the editor phases -- record list virtualization + drag-drop + selection service + override sub-rows.
