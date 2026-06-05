# Phase F -- LOD section (distance bar + cards) + DensityPaintWindow

- Effort: M
- Parallel-safe with: G (after D + E).
- Blocks: I

## Scope

Two independent deliverables that conveniently share an owner because both are "polish" content surfaces:

1. **LOD section** that injects into the `lod-section-slot` of both DensityLayer.uxml and InstanceLayer.uxml. Horizontal distance bar with draggable switch handles + 3 colour-coded LOD cards (mesh thumbnail, mesh picker, tris/verts readout, clamped distance slider, ping + remove buttons). Auto-generate button decimates LOD0 -> LOD1 (50%) + LOD2 (25%).
2. **DensityPaintWindow** EditorWindow per D3 default: working byte[] buffer with per-stroke Undo, commit on Apply/Close, Undo stack capped at 32 strokes.

## File ownership

- Fill stubs from B: `Editor/UI/Components/LodDistanceBar.cs`, `LodCard.cs`
- NEW: `Editor/UI/Components/LodSection.cs` (composite that owns LodDistanceBar + N cards + auto-generate button)
- NEW: `Editor/UI/UXML/LodSection.uxml`
- Fill USS: `Editor/UI/USS/LodSection.uss`
- NEW: `Editor/UI/DensityPaintWindow.cs`
- NEW: `Editor/UI/UXML/DensityPaintWindow.uxml`
- Fill USS: `Editor/UI/USS/DensityPaintWindow.uss`

## Pre-conditions

- Phases D + E merged (their UXML has a `lod-section-slot` element).
- DensityTextureField (from D) has a Paint button click handler stub waiting to be wired.
- Editor/Defaults/ seed meshes exist (Phase B): `Assets/GrassInteract/Editor/Defaults/Default_LOD0_Grass.mesh` (for Density layers) and `Assets/GrassInteract/Editor/Defaults/Default_LOD0_Prop.mesh` (for Instance layers). Both are in-place-reference seeds per D2 -- LodCard's mesh picker assigns them by reference; users may swap with their own meshes.
- D3 (DensityPaintWindow undo) is UNCHANGED from the initial plan: working byte[] buffer, per-stroke Undo entries, cap 32 (oldest collapses into "Paint baseline"). No orchestrator overrides.

## Step-by-step tasks

### F.1 -- LodSection composite

1. `LodSection : VisualElement` -- takes a `SerializedProperty lodsProp` (array of ScatterLod).
2. Top: `LodDistanceBar` (horizontal segmented bar).
3. Middle: N `LodCard`s (one per LOD entry).
4. Bottom: buttons `Auto-generate (decimate LOD0)` and `Add LOD` / `Remove last LOD` (lods array size manipulation).

### F.2 -- LodDistanceBar

1. `LodDistanceBar : VisualElement` -- horizontal segmented bar, height 28 px.
2. Segments: one per LOD. Segment widths proportional to `lods[i].maxDistance` minus prior LOD's distance. Last segment fills remainder.
3. Segment colours per brainstorm: 3 distinct (e.g. blue, green, orange) matching the corresponding LodCard border.
4. Draggable handles BETWEEN segments: a small grabbable bar; PointerDownEvent -> capture; PointerMoveEvent -> compute new value clamped between (prev_handle + 0.5m) and (next_handle - 0.5m) per Risk mitigation.
5. Camera preview cursor: a vertical line that slides along the bar based on `SceneView.lastActiveSceneView.camera`'s distance to the layer field origin. Updates on `EditorApplication.update` (throttled to 30Hz).
6. Resizes responsively to inspector width.

### F.3 -- LodCard

1. `LodCard : VisualElement` -- one card per LOD entry.
2. UXML: 64x64 MeshPreview (from B reusable), ObjectField mesh, two read-only labels (tris / verts -- read from the mesh on assign), Slider maxDistance (clamp range per F.2), ping button, remove button.
3. Card border colour: matches the corresponding bar segment.
4. Mesh assign change triggers UpdateTrisVertsLabels + LodDistanceBar.MarkDirty (recompute segment widths).
5. Remove button: SerializedProperty.DeleteArrayElementAtIndex on lods array; parent LodSection re-renders.

### F.4 -- Auto-generate (decimate) + LOD0 seed fallback

1. **LOD0 source resolution.** Read `source = lods[0].mesh`. If null, fall back to the kit seed:
   - For `DensityScatterLayer`: `AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GrassInteract/Editor/Defaults/Default_LOD0_Grass.mesh")`.
   - For `InstanceScatterLayer`: `AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GrassInteract/Editor/Defaults/Default_LOD0_Prop.mesh")`.
   - If the seed itself is missing (broken install), show `EditorUtility.DisplayDialog("Editor/Defaults/Default_LOD0_*.mesh missing -- reinstall the GrassInteract editor")` and abort. Same diagnostic as Phase C.4.4.
2. Call `MeshUtility.SimplifyMesh(source, 0.5f)` -> save as new sub-asset under the layer's parent config. Assign as `lods[1].mesh` with `maxDistance = 30`.
3. Same for LOD2 at 0.25 factor with `maxDistance = 60`.
4. **Same fallback path also feeds LodCard's "Reset to default" context-menu entry**: an item that overwrites a LodCard's mesh with the appropriate Editor/Defaults seed in one click. Useful when a user has accidentally cleared a mesh and wants the seed back.
5. Note: Unity 6 doesn't ship a built-in simplifier; use UnityMeshSimplifier OpenUPM package OR provide a stub that opens a dialog "Install UnityMeshSimplifier" with a link. Acceptable for F: ship the dialog-stub; decimation lib install becomes a Phase I doc task.


### F.5 -- DensityPaintWindow

1. `DensityPaintWindow : EditorWindow`. Static `Open(Texture2D? target)` opens + sets the target.
2. UXML root: top toolbar (Brush Size slider, Hardness slider, Tools: Paint/Erase/Fill/Clear/Invert), centre = preview canvas (zoomable, pannable), right = info panel (texture size, % white, % black).
3. Working buffer: on Open, copy `target.GetPixels32()` into `byte[] workingBuffer` (R channel only -- per-byte). Display via a derived RenderTexture that re-reads workingBuffer each repaint (or a managed Texture2D refreshed per stroke).
4. Per-stroke: capture a snapshot of the affected rect BEFORE applying the stroke; push onto Undo stack (capped 32; oldest collapse into "Paint baseline" entry per D3).
5. Apply button: `target.SetPixels32(...)` from workingBuffer, `target.Apply(...)`, `EditorUtility.SetDirty(target)`, `AssetDatabase.SaveAssets()`.
6. Close button: prompt "Apply changes before close?" with Yes/No/Cancel if there are unsaved strokes.
7. CTRL+Z handler: pop Undo stack, restore affected rect.

### F.6 -- Wire DensityTextureField Paint button (D's stub)

1. D.4 left the Paint button click handler as a DisplayDialog stub. F replaces with `DensityPaintWindow.Open(layer.DensityMap)`.

## Validation criteria

1. Compile clean.
2. LodSection: open a layer with 3 LODs -- bar shows 3 segments with proportional widths. Drag the second handle right -- bar updates; LOD2 distance value increases.
3. Drag handle 1 PAST handle 2 -- clamp prevents crossing; values stay ordered.
4. LodCard: assign a new mesh -- tris/verts labels update; preview thumbnail refreshes.
5. Auto-generate (decimate): if UnityMeshSimplifier present, LOD1/LOD2 sub-assets are created. Otherwise dialog appears.
6. DensityPaintWindow: paint a stroke -- preview updates; close without Apply -> dialog appears -> Cancel -> stays open.
7. CTRL+Z during painting -- last stroke undoes.
8. Apply -> close window -> reopen the layer -- density map shows painted content.
9. Light + dark theme verified.
10. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| LOD switch handles cross each other on drag | 3 | 2 | 6 | Clamp logic in F.2 step 4 -- min = prev + 0.5m; max = next - 0.5m. |
| DensityPaintWindow large Undo entries (512x512 R8 stroke ~256KB) bloat memory | 3 | 3 | 9 | Store only the affected rect (xywh + byte[] of that rect) -- not the whole buffer. 32-stroke cap further bounds memory. |
| UnityMeshSimplifier absent -> Auto-generate silently no-ops | 4 | 2 | 8 | Detect via reflection: `Type.GetType("UnityMeshSimplifier.MeshSimplifier, UnityMeshSimplifier")`. If null, show install dialog. |
| RenderTexture refresh on every PointerMove drops frame rate on large textures | 3 | 3 | 9 | Throttle preview-texture upload to 30Hz via `EditorApplication.timeSinceStartup`. |
| Apply-on-close with unsaved strokes silently lost | 2 | 4 | 8 | Window's hasUnsavedChanges + showModified Title prefix; Close intercepted via OnDestroy that checks dirty flag. |

## Effort: M

Estimate 4-6 hours. LodSection is moderate; DensityPaintWindow is the bigger chunk with stroke + Undo + RT plumbing.
