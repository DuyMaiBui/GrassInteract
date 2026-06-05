# Phase B -- Editor foundations (UI asmdef, USS root, components scaffold, default assets)

- Effort: M
- Parallel-safe: No (depends on A compile-clean)
- Blocks: C, D, E, F, G all import B's components and USS.

## Scope

Stand up the entire UIToolkit infrastructure that the per-editor phases will fill in. This phase delivers EMPTY-BUT-COMPILING component classes, the USS token system with light/dark variants, the asmdef boundary, the kit-shipped default assets in `Editor/Defaults/`, and a `BindablePanel` base class that wires SerializedObject lifetime correctly.

**D2 note:** `Editor/Defaults/` holds editor-only SEED assets (under the Editor/ asmdef -- NOT shipped at runtime). Phase C's layer-creation flow loads them via `AssetDatabase.LoadAssetAtPath` and either references them in-place (the user's layer points at the seed) OR copies them into the user's TerrainScatterConfig as sub-assets (so the user can mutate without affecting other configs). Whichever pattern, the seed is the source -- never the runtime payload.

Per Library-Quality Mandate: every reusable surface goes here so per-editor phases don't reinvent.

## File ownership

NEW files (every one created in this phase):

- Editor folder structure:
  - `Assets/GrassInteract/Editor/UI/` (folder)
  - `Assets/GrassInteract/Editor/UI/Components/` (folder)
  - `Assets/GrassInteract/Editor/UI/UXML/` (folder)
  - `Assets/GrassInteract/Editor/UI/USS/` (folder)
  - `Assets/GrassInteract/Editor/UI/Icons/` (folder)
- Components (C# stubs; bodies filled by C/D/E/F/G):
  - `Editor/UI/Components/BindablePanel.cs` (FILLED in B; base for all panels)
  - `Editor/UI/Components/LayerTileGrid.cs` (stub + USS hookup; C fills behavior)
  - `Editor/UI/Components/LayerTile.cs` (stub; C fills)
  - `Editor/UI/Components/LayerInspectorPanel.cs` (stub; C fills)
  - `Editor/UI/Components/RecordList.cs` (stub; E fills)
  - `Editor/UI/Components/RecordRow.cs` (stub; E fills)
  - `Editor/UI/Components/RecordDetailPanel.cs` (stub; E fills)
  - `Editor/UI/Components/ValidationBadge.cs` (FILLED in B; reusable)
  - `Editor/UI/Components/AutoFixButton.cs` (FILLED in B; reusable)
  - `Editor/UI/Components/MeshPreview.cs` (FILLED in B; reusable)
  - `Editor/UI/Components/DensityTextureField.cs` (stub; D fills)
  - `Editor/UI/Components/LodDistanceBar.cs` (stub; F fills)
  - `Editor/UI/Components/LodCard.cs` (stub; F fills)
  - `Editor/UI/Components/ModeToolbar.cs` (stub; G fills)
  - `Editor/UI/Components/EmptyLayersState.cs` (stub; C fills)
  - `Editor/UI/Components/QuickAddPopover.cs` (stub; C fills)
  - `Editor/UI/Components/ValidationPopover.cs` (stub; C/D/E fill)

- USS (FILLED in B):
  - `Editor/UI/USS/GrassInteract.uss` -- root tokens, base + utility classes, font sizes, spacing.
  - `Editor/UI/USS/GrassInteract.Dark.uss` -- dark-variant overrides (surface, fg colours).
  - `Editor/UI/USS/GrassInteract.Light.uss` -- light-variant overrides.
  - `Editor/UI/USS/LayerTile.uss` -- tile metrics (96x128, 8px gap, 6px radius, 2px border, 3px accent on select).
  - `Editor/UI/USS/RecordRow.uss` -- empty placeholder (E fills).
  - `Editor/UI/USS/ValidationBadge.uss` -- dot + popover anchor styles.
  - `Editor/UI/USS/ModeToolbar.uss` -- empty placeholder (G fills).
  - `Editor/UI/USS/Sections.uss` -- section header styles (13px / 600 weight), 8px section padding, 12px between sections.
  - `Editor/UI/USS/LodSection.uss` -- empty placeholder (F fills).
  - `Editor/UI/USS/DensityPaintWindow.uss` -- empty placeholder (F fills).

- Default assets:
  - `Assets/GrassInteract/Editor/Defaults/` (folder)
  - `Editor/Defaults/Default_Material.mat` -- using the existing `Demo/GrassInteractIndirectMat.mat` shader (or `GrassInteract/IndirectGrass`); copy material settings; this is the SHARED default.
  - `Editor/Defaults/Default_LOD0_Grass.mesh` -- copy of `Meshes/GrassBlade_LOD0.asset` renamed (re-share by reference; do NOT duplicate the mesh, expose as ref-target).
  - `Editor/Defaults/Default_LOD0_Prop.mesh` -- a 0.5 m cube mesh built by Tools menu (generated in B via a small editor script `Editor/Defaults/BuildDefaultPropMesh.cs` that runs once and self-deletes).
  - `Editor/Defaults/Default_DensityMap_512_white.png` -- 512x512 R8 white-filled PNG (SHAPE/FORMAT seed; CreateDensityLayer generates a fresh writable copy per layer) (Read/Write enabled, uncompressed); used as a TEMPLATE only (CreateDensityLayer generates 512x512 in code).

- Asmdef (modify):
  - `Editor/GrassInteract.Editor.asmdef` -- KEEP existing. UI/ is a sub-folder so the same asmdef covers it. NO new asmdef needed.

- Icons:
  - `Editor/UI/Icons/grass.png`, `mesh.png`, `gear.png`, `add.png`, `remove.png`, `duplicate.png`, `rename.png`, `paint.png`, `move.png`, `rotate.png`, `scale.png` -- 16x16 monochrome PNGs.
  - Source: derive from Unity built-in editor icons via screenshot or generate as flat shapes. Acceptable: simple geometric stand-ins (B is foundations, icons can be improved in I).

## Pre-conditions

- Phase A merged + compile-clean.
- `Editor/Defaults/` folder does NOT yet exist.
- `Editor/UI/` folder does NOT yet exist.
- Branch created off A's merge commit.

## Step-by-step tasks

### B.1 -- Folder + asset scaffolding

1. Create folder tree: `Editor/UI/Components/`, `Editor/UI/UXML/`, `Editor/UI/USS/`, `Editor/UI/Icons/`, `Editor/Defaults/`.
2. Move `Meshes/GrassBlade_LOD0.asset` reference into `Editor/Defaults/Default_LOD0_Grass.mesh` by AssetDatabase.CopyAsset (cheap; original kept for back-compat).
3. Create `Editor/Defaults/Default_Material.mat` by `AssetDatabase.CopyAsset("Assets/GrassInteract/Demo/GrassInteractIndirectMat.mat", "Assets/GrassInteract/Editor/Defaults/Default_Material.mat")` -- avoids re-authoring shader fields.
4. Create `Editor/Defaults/Default_DensityMap_512_white.png` -- 512x512 R8 white. Acceptable to import a pre-made file or generate via one-shot editor script `Tools > GrassInteract > Build Default Density Template`.
5. Create `Editor/Defaults/Default_LOD0_Prop.mesh` -- 0.5m cube via Tools menu `Tools > GrassInteract > Build Default Prop Mesh`. ScatterPropMeshBuilder.cs already does prop meshes; reuse.

### B.2 -- USS root tokens + variants

1. `GrassInteract.uss` (REQUIRED contents):
   - `:root` block declaring CSS variables: `--gi-grass: #5DBB46`, `--gi-mesh: #8A6240`, `--gi-select: #3D8BFF`, `--gi-status-error: #E14B4B`, `--gi-status-warn: #E0A526`, `--gi-status-ok: #3FB75A`, `--gi-spacing-section: 8px`, `--gi-spacing-field: 4px`, `--gi-spacing-between-sections: 12px`, `--gi-radius: 6px`, `--gi-border: 2px`.
   - Two state classes: `.gi-light` and `.gi-dark` set `--gi-surface` and `--gi-fg` differently.
   - Base section class `.gi-section { padding: var(--gi-spacing-section); margin-bottom: var(--gi-spacing-between-sections); }`.
   - Section header: `.gi-section-header { font-size: 13px; -unity-font-style: bold; }`.
   - Body text: `.gi-body { font-size: 12px; }`.
2. `GrassInteract.Dark.uss`: sets `--gi-surface: #2A2A2A`, `--gi-fg: #DDD`.
3. `GrassInteract.Light.uss`: sets `--gi-surface: #E6E6E6`, `--gi-fg: #222`.
4. BindablePanel's constructor applies the right variant class based on `EditorGUIUtility.isProSkin`.

### B.3 -- BindablePanel base class (FILLED)

1. `BindablePanel : VisualElement` -- abstract base for every panel inspector returns.
2. Holds a `SerializedObject so` reference; calls `so.Update()` in `RegisterCallback<AttachToPanelEvent>` and `so.ApplyModifiedProperties()` on `DetachFromPanel`.
3. Constructor takes the SerializedObject; loads `GrassInteract.uss` + the active theme variant + any panel-specific stylesheet (subclasses can `AddStyle("LayerTile.uss")`).
4. Provides `protected void BindAll()` helper that calls `this.Bind(this.so)` (UIToolkit binding API) on the root.
5. Subscribes to `Undo.undoRedoPerformed` to refresh bindings; unsubscribes on detach.

### B.4 -- ValidationBadge + AutoFixButton + MeshPreview (FILLED reusables)

1. `ValidationBadge : VisualElement` -- a coloured circle + click-to-popover behavior. API: `void SetStatus(StatusKind k, string tooltip, IEnumerable<(string label, Action fix)> autoFixes)`. StatusKind: Ok/Warn/Error/Unknown -> CSS class swap.
2. `AutoFixButton : Button` -- thin wrapper around `Button` with a fixed text label, dispatches a delegate.
3. `MeshPreview : VisualElement` -- 64x64 (configurable) Image fed by `AssetPreview.GetAssetPreview(mesh)`; subscribes to `AssetPreviewUpdated` to refresh; null-safe (shows placeholder square when mesh is null). Reused by LayerTile + LodCard + RecordRow.

### B.5 -- Component stubs (compile-only)

For each of these classes, create the file with `: VisualElement` (or appropriate base), an empty constructor, a TODO comment naming the owning phase. Compiling is sufficient -- no behavior yet:

- LayerTileGrid (C will fill)
- LayerTile (C)
- LayerInspectorPanel (C)
- RecordList (E)
- RecordRow (E)
- RecordDetailPanel (E)
- DensityTextureField (D)
- LodDistanceBar (F)
- LodCard (F)
- ModeToolbar (G)
- EmptyLayersState (C)
- QuickAddPopover (C)
- ValidationPopover (C/D/E)

### B.6 -- Icon set

1. For each of the 11 icons in the list, drop a 16x16 PNG into `Editor/UI/Icons/`. Simple flat shapes acceptable for B; Phase I may polish.
2. Mark each as `TextureImporter.alphaIsTransparency = true`, `mipmapEnabled = false`, `filterMode = Point`, `wrapMode = Clamp`.
3. Provide a small `Editor/UI/IconLoader.cs` (NEW; reusable) that exposes `static Texture2D Get(string name)` with cached lookup of `Editor/UI/Icons/<name>.png`. Used by tiles, toolbar, record rows.

## Validation criteria

1. **Compile clean**: `refresh_unity` + `read_console` ZERO errors. Asmdef still passes.
2. **USS lint**: open `GrassInteract.uss` in Unity, no Console warnings about unknown CSS properties.
3. **Default asset smoke**: load `Editor/Defaults/Default_Material.mat` via `AssetDatabase.LoadAssetAtPath` -- not null. Same for the LOD0 grass + prop meshes + density texture template.
4. **BindablePanel sanity**: write a one-shot scratch test `EditorWindow` that opens a panel bound to any SerializedObject -- no NullReferenceException on attach/detach.
5. **Theme switch**: in Editor Settings, toggle Pro/Personal skin, reopen the scratch window -- the variant class should swap (verify by inspecting in UIToolkit Debugger).
6. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| AssetPreview returns null on first call (cache cold) | 4 | 2 | 8 | MeshPreview subscribes to `EditorApplication.update` once, polls AssetPreview until non-null, then unsubscribes. Standard pattern. |
| USS variables not supported by Unity's older UIToolkit subset | 3 | 3 | 9 | If `--var` syntax fails (Unity 6 should support it; verify in 6000.3.x), fallback: per-class colour declarations + light/dark override classes. |
| Icon source rights/licensing for built-in editor icons | 2 | 2 | 4 | Use flat-shape originals OR public-domain icon set; document in EDITOR-UI-GUIDE.md. |
| BindablePanel double-applies Undo (subscribed twice) | 3 | 3 | 9 | Use named-method handler (not lambda) per code-conventions-unity.md, and unsubscribe deterministically in DetachFromPanel. |

## Effort: M

Estimate 3-5 hours. Heavy in scaffolding (lots of small files) but no decision-making; mechanical.
