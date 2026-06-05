# Phase C -- TerrainScatterConfig editor + tile grid + layer creation flow

- Effort: M
- Parallel-safe: No (depends on B)
- Blocks: D, E (they plug into LayerInspectorPanel created here)

## Scope

Replace `TerrainScatterConfigEditor` and `ScatterFieldEditor` with pure UIToolkit. Fill the LayerTileGrid + LayerTile + LayerInspectorPanel + EmptyLayersState + QuickAddPopover components scaffolded in B. Wire the +Density / +Instance creation flow with auto sub-asset generation (calls into `TerrainScatterConfig.CreateDensityLayer` / `CreateInstanceLayer` from A.5 with Default_Material + LOD0 mesh references from B). Extend ScatterAssetPostprocessor with the sub-asset naming-convention method (appends to A's extension point).

## File ownership

- Full rewrite: `Editor/TerrainScatterConfigEditor.cs`
- Full rewrite: `Editor/ScatterFieldEditor.cs`
- Append-only: `Editor/ScatterAssetPostprocessor.cs` (add `ApplyNamingConvention` method body; A left it empty)
- Fill stubs from B: `Editor/UI/Components/LayerTileGrid.cs`, `LayerTile.cs`, `LayerInspectorPanel.cs`, `EmptyLayersState.cs`, `QuickAddPopover.cs`
- NEW UXML: `Editor/UI/UXML/TerrainScatterConfigHeader.uxml`, `LayerTile.uxml`, `EmptyLayersState.uxml`, `QuickAddPopover.uxml`

## Pre-conditions

- Phase B merged + compile-clean.
- Editor/Defaults/ seed assets exist (Default_Material, Default_LOD0_Grass, Default_LOD0_Prop).
- DensityScatterLayerEditor and InstanceScatterLayerEditor do NOT yet exist -- LayerInspectorPanel falls back to a placeholder "No editor yet" panel for unknown layer types in C. D and E later replace the placeholder when registering their UIToolkit inspectors via [CustomEditor].

## Step-by-step tasks

### C.1 -- TerrainScatterConfigEditor.cs full rewrite

1. `[CustomEditor(typeof(TerrainScatterConfig))]` -- `public override VisualElement CreateInspectorGUI()`.
2. Root: a BindablePanel subclass `TerrainScatterConfigPanel`.
3. Load `TerrainScatterConfigHeader.uxml` from `UI/UXML/`; clone into root.
4. Header contents: project icon + asset name (label bound to `target.name`), two big buttons `+ Density Layer` and `+ Instance Layer`, plus a small `Tools` dropdown (Re-bake all previews, etc -- can be empty for now).
5. Below header: a `LayerTileGrid` instance bound to `serializedObject.FindProperty("layers")`.
6. Below grid: a `LayerInspectorPanel` slot that swaps content based on grid selection (initially shows EmptyLayersState if zero layers OR a "Pick a layer" placeholder if >0 layers but none selected).

### C.2 -- LayerTileGrid (CSS Grid via ScrollView)

1. `LayerTileGrid : VisualElement`.
2. Bound to a SerializedProperty (array of layers).
3. Inner ScrollView with `flex-direction: row; flex-wrap: wrap;`.
4. Per layer: one `LayerTile` instance. Plus one `EmptyLayersState`-style "+ Add Layer" placeholder tile at the end that opens `QuickAddPopover`.
5. Drag-reorder: handle inside each LayerTile catches `PointerDownEvent`, raises an event the grid listens to; on drop, swaps array elements via `SerializedProperty.MoveArrayElement`.
6. Selection state: grid raises `OnSelectionChanged(int layerIndex)`; LayerInspectorPanel subscribes.
7. Right-click context menu on a tile: Duplicate / Rename / Delete / Re-bake preview -- uses `ContextualMenuManipulator`.

### C.3 -- LayerTile (UXML + behavior)

1. UXML `LayerTile.uxml`: root has `class="gi-layer-tile"`. Children: MeshPreview (LOD0 thumbnail), kind icon overlay (grass leaf or prop cube), name TextField (label by default, editable on F2/double-click), instance count label, ValidationBadge, drag-handle on right edge.
2. Tile dimensions enforced by `LayerTile.uss`: width 96, height 128, border 2, radius 6, gap implicit via parent grid.
3. Selected state: add `class="gi-selected"` -> 3px accent-blue border.
4. Kind icon resolved by GetType() check: `layer is DensityScatterLayer` -> grass icon; `layer is InstanceScatterLayer` -> mesh icon.
5. ValidationBadge: every frame (or via SerializedObject change event) runs `layer.Validate(out string err)`; sets Ok/Error accordingly. Click on badge opens `ValidationPopover` with the error text + any registered auto-fix buttons (e.g. "Assign default material" if material is null -> sets layer.material = Editor/Defaults/Default_Material.mat).

### C.4 -- Layer creation flow

**D2 seed model:** the kit ships Editor/Defaults/ assets as SEEDS, never as the runtime payload. Two seed-handling patterns are used in C.4:

| Seed | Pattern | Reason |
|---|---|---|
| Default_Material.mat | **Reference in-place** -- the layer's `material` field points at the kit asset | Materials are typically shared; user can swap with their own without touching the seed. |
| Default_LOD0_Grass.mesh / Default_LOD0_Prop.mesh | **Reference in-place** for lods[0] initially; user can swap | Meshes are also shareable; in-place ref keeps the project Assets/ tree tidy. |
| Default_DensityMap_512_white.png | **Copy into user space** as the layer's density-map sub-asset | The user paints into this -- it MUST be writable + per-layer; never shared. (CreateDensityLayer generates a fresh 512x512 R8 texture in-code; the seed is referenced only as a template for default size/format.) |

1. `+ Density Layer` click handler:
   - `Undo.RegisterCompleteObjectUndo(config, "Create Density Layer");`
   - Resolve default material seed (REFERENCE): `AssetDatabase.LoadAssetAtPath<Material>("Assets/GrassInteract/Editor/Defaults/Default_Material.mat")`.
   - Resolve default LOD0 mesh seed (REFERENCE): `AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GrassInteract/Editor/Defaults/Default_LOD0_Grass.mesh")`.
   - Call `config.CreateDensityLayer(name: $"Layer_Density_{nextIndex}", defaultMaterial, defaultLod0Mesh)` -- which internally generates the white-filled 512x512 R8 density texture as a NEW sub-asset (in-code, not copied from the seed PNG; the seed exists only as a format template documented in EDITOR-UI-GUIDE).
   - `AssetDatabase.SaveAssets()`.
   - Trigger grid refresh + select the new tile.
2. `+ Instance Layer` click handler: same flow but `CreateInstanceLayer` + default prop mesh seed (REFERENCE).
3. Validation: if the parent config has not been saved to disk (no `AssetPath`), block + show dialog "Save the TerrainScatterConfig asset before adding layers".
4. Validation: if any seed asset cannot be loaded (kit installation broken), show dialog "Editor/Defaults/Default_*.* missing -- reinstall the GrassInteract editor" + abort.

### C.5 -- EmptyLayersState + QuickAddPopover

1. `EmptyLayersState`: shown when `config.Layers.Count == 0`. Contents: friendly placeholder graphic + duplicated +Density / +Instance buttons + a tip "Click + Density to create your first scatter layer".
2. `QuickAddPopover`: a tiny PopupWindow opened by the "+ Add Layer" trailing tile. Two buttons: Density / Instance -- delegates to same handler as C.4.

### C.6 -- LayerInspectorPanel slot wiring

1. `LayerInspectorPanel : VisualElement` -- displays the inspector of the currently selected layer.
2. On grid `OnSelectionChanged(int idx)`: clear children; resolve `layer = config.Layers[idx]`; use `UnityEditor.Editor.CreateEditor(layer)` to get the layer's custom editor (D/E will register their own; in C this returns a placeholder until D/E land).
3. Call `editor.CreateInspectorGUI()` and add the returned VisualElement.
4. On grid selection cleared: show "Pick a layer" placeholder.

### C.7 -- ScatterFieldEditor.cs full rewrite

1. Much simpler than Config -- ScatterField (MonoBehaviour) just references a config + has runtime status info.
2. `CreateInspectorGUI()` returns a BindablePanel with: ConfigField (bound to `serializedObject.FindProperty("config")`), a TerrainField (existing field), and a "Rebuild" button that calls `field.Rebuild()`.
3. Add a runtime status box (visible in Play mode only) showing per-layer instance counts + engine routing (Grass / Mesh).

### C.8 -- ScatterAssetPostprocessor.ApplyNamingConvention

1. Fill the stub method A.8 left open.
2. On every config reimport, walk all layers + their sub-assets. For each ScatterLayer named `Layer_<Density|Instance>_<n>`, ensure: the density-map sub-asset is named `Density_<layerName>`, the authored-instances sub-asset is named `Authored_<layerName>`. Use `AssetDatabase.RenameAsset` only on mismatch (avoids unnecessary reimports).
3. Also auto-renumber on reorder so layer index suffixes stay in order (use the layer's index within `config.Layers`).
4. Schedule via `EditorApplication.delayCall` to avoid mutating AssetDatabase during the postprocessor callback (same pattern as the existing rebuild scheduling).

## Validation criteria

1. Compile clean: `refresh_unity` + `read_console` zero errors.
2. Smoke test "create from empty": open a freshly created TerrainScatterConfig, click `+ Density Layer` -> a tile appears, sub-assets exist (Layer_Density_0 SO + Density_Layer_Density_0 texture), material is Default_Material, LOD0 mesh is Default_LOD0_Grass. Same for `+ Instance Layer`.
3. Drag-reorder: drag tile 2 to position 0 -- order changes, sub-asset names renumber after delayCall fires.
4. Light + dark theme: both render without contrast violations.
5. Validation badge: temporarily un-assign material on a layer; badge turns red; click reveals "Assign default material" auto-fix; click fixes.
6. Right-click context menu: Duplicate creates `Layer_Density_0_copy`; Delete removes layer + cascade-deletes its sub-assets.
7. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| ScriptableObject.CreateInstance + AddObjectToAsset wired in wrong order causes "asset not found" | 3 | 4 | 12 | Always set `layer.hideFlags = HideFlags.None` BEFORE AddObjectToAsset; only flip to HideInHierarchy AFTER (existing CreateLayer already does this). Validate in C.4 unit smoke. |
| Sub-asset cascade-delete misses density texture | 3 | 3 | 9 | Context-menu Delete uses `AssetDatabase.RemoveObjectFromAsset` for EVERY sub-asset belonging to the layer (walk objectRefs + density map). Add a smoke: delete a layer, confirm `AssetDatabase.LoadAllAssetsAtPath` no longer lists those sub-assets. |
| CreateEditor produces a stale editor across config reimports | 2 | 3 | 6 | LayerInspectorPanel disposes the previous Editor with `Object.DestroyImmediate(editor)` before creating a new one. |
| TerrainScatterConfigHeader.uxml drag-drop into root fails on light skin | 2 | 2 | 4 | Phase I theme audit catches; mitigate by using flex layout with explicit min-heights. |

## Effort: M

Estimate 4-6 hours. Most code-volume of the editor phases is here; lots of UI wiring + serialization plumbing.
