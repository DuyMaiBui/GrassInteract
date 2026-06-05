# Brainstorm — Scatter Layer Editor Rewrite (UIToolkit, R6)

Date: 2026-06-05
Iteration: R6.6 (final)
Predecessors: `brainstorm-scatter-layer-placement-split-20260604.md`, `brainstorm-scatter-brush-config-refactor-20260604.md`, `brainstorm-authored-instance-editor-20260604.md`

---

## Problem statement

`ScatterLayer` base class currently exposes every concern in one inspector — placement, rendering, wind, trample, bounds, GPU chunking, colliders — for both the procedural Density and the authored Instance subclasses. The result:

- Instance layers display fields that are meaningless to them (`fieldBounds`, `seed`, `scaleRange`, `slopeRange`, splat mask, random orientation ranges).
- Density layers display per-layer collider config that procedural scatter never honours.
- New layers are created via the default Odin array UI — type picker missing, sub-assets (density texture, authored sidecar, material) not auto-created.
- Layer list is a flat array with no visual identity.
- Editor logic spreads across Odin attributes that don't accommodate the rich tile/scene-overlay UX the team wants.
- The `ScatterKind` enum (Grass/Mesh) duplicates information that the deform flags + material already encode.

## Requirements

1. Move every field to the type that genuinely needs it (Density vs Instance). Base class keeps only shared infrastructure.
2. Delete `ScatterKind`; route rendering pipeline via existing `InteractsWithDeform`.
3. Collapse `grassMaterial` + `meshMaterial` into a single `material` field.
4. New-layer flow: header buttons `+ Density` / `+ Instance`; auto-create R8 density texture (white-filled, 512×512), empty `AuthoredInstancesData`, default material assignment.
5. Layer list = square LOD0-preview tiles, click-to-select, drag-reorder, dup/delete/rename, validation badge.
6. Instance layer editing: reorderable record list + scene-view TRS gizmos + drag-prefab-to-import + inspector-focus-on-scene-click + scene-overlay Mode toolbar.
7. Per-record collider on Instance (override mesh, convex, scale, opt-in).
8. Runtime pooling + frustum culling for Instance colliders.
9. Editor intelligence: inline validation badges + auto-fix buttons + sub-asset naming convention.
10. LOD section: visual distance bar + 3 LOD cards with mesh preview and clamped sliders.
11. **Pure UIToolkit editor.** Strip all Odin attributes from runtime types. Full branded GrassInteract USS.
12. Clean break — delete legacy assets and migration scripts.

## Final design (R6.6)

### §1 — Field ownership

| Field | Base | Density | Instance |
|---|:-:|:-:|:-:|
| `affectedByWind`, `affectedByInteractors` (both default `true`) | ✅ | | |
| `material` (single field; rendering pipeline = `InteractsWithDeform`) | ✅ | | |
| `shadowCastingMode` | ✅ | | |
| Wind block (Sine/Perlin + tunables) | ✅ | | |
| Trample (`bendStrength`, `flatten`, `recoveryRate`) | ✅ | | |
| Bounds (`maxBladeHeight`, `bendHeadroom`) | ✅ | | |
| `chunkSize`, `lods[]`, `groundSnapMask` | ✅ | | |
| `fieldBounds`, `seed`, `scaleRange` | | ✅ | |
| `slopeRange`, `splatLayerIndex`, `splatThreshold` | | ✅ | |
| `rotationOffsetEuler`, `randomPitchRange`, `randomRollRange`, `alignToNormal` | | ✅ | |
| `densityMap`, `targetInstances` | | ✅ | |
| `authoredInstances`, `placeSpacing` | | | ✅ |
| `generateColliders`, `colliderMesh`, `colliderConvex` (deleted from base) | — | (none — density doesn't collide) | (per-record + layer defaults) |
| ~~`kind`~~ | **REMOVED** | | |

Engine route: `layer.InteractsWithDeform == true` → grass pipeline (`GrassCpuEngine` / `GrassGpuEngine`); `false` → mesh-prop pipeline (`MeshScatterEngine`).

### §2 — `AuthoredInstanceRecord` schema

```csharp
public struct AuthoredInstanceRecord
{
    Vector3    position;
    Quaternion rotation;
    float      scale;            // uniform
    Mesh?      colliderOverride; // null = use layer default
    float      colliderScale;    // 1 = match instance scale
    bool       generateCollider; // per-record opt-in
    bool       colliderConvex;
}
```

### §3 — Instance editing surfaces

| Surface | Behaviour |
|---|---|
| Reorderable record list | Inspector list with drag, dup, delete, focus-in-scene, expandable collider sub-row. |
| Scene-view TRS gizmo | Drag handles for translate / rotate / scale, snap-to-ground on translate, optional align-to-normal toggle. |
| Drag-prefab → list | Drop a GameObject or Prefab, enumerate transforms, bake records, mesh collider source → record's `colliderOverride`. |
| Click-instance-in-scene | Inspector list scrolls + highlights matching row; activates TRS gizmo. |
| Scene-view Mode toolbar | Select / Place / Erase mode; Move/Rotate/Scale tool; Snap toggles; Place spacing + Erase brush radius. |
| Place mode (world raycast) | LMB drops a record at the `groundSnapMask` raycast hit; Shift-drag spaces along path by `placeSpacing`. **Not** a texture-paint operation. |

### §4 — Layer creation flow

Inspector header on `TerrainScatterConfig`:

```
[ + Density Layer ]   [ + Instance Layer ]
```

On click:
- Create `ScriptableObject` subclass as sub-asset, name `Layer_<Density|Instance>_<idx>`.
- **Density:** generate `512×512 R8 readable Texture2D` filled white (full density), save as sub-asset `Density_<layerName>`, assign to `densityMap`.
- **Instance:** create empty `AuthoredInstancesData` sub-asset `Authored_<layerName>`, assign.
- Both: assign kit-shipped `Default_Material.mat`.
- Both: prefill `lods[0]` with kit-shipped default mesh.
- Postprocessor renames sub-assets when layer is renamed / reordered.

### §5 — Layer list

Square tile grid (`LayerTileGrid` UIToolkit ScrollView with CSS Grid layout).

Per tile: LOD0 thumbnail (`AssetPreview.GetAssetPreview`) · kind icon (🌿 / 📦) · layer name · live instance count · validation status dot (red/yellow/green) · drag-handle on edge · right-click context menu (Duplicate / Rename / Delete / Re-bake preview).

Tile metrics: 96×128 px · 8 px gap · 6 px radius · 2 px border (3 px accent blue when selected).

Empty 4th-slot `+ Add Layer` tile opens a quick-add popover with both type choices.

### §6/§11 — Pure UIToolkit editor

All custom editors return UIToolkit root via `CreateInspectorGUI()`. Reusable components:

| Component | Role |
|---|---|
| `LayerTileGrid` | ScrollView + CSS Grid, hosts tiles, manages selection. |
| `LayerTile` | LOD0 thumbnail + name + count + badge + context menu. |
| `RecordList` | Virtualized `ListView` with drag-reorder + drag-drop import. |
| `RecordRow` | Inline TRS + expandable collider sub-row. |
| `ValidationBadge` | Status dot + hover popover listing errors + auto-fix buttons. |
| `AutoFixButton` | Bound to a single fix lambda. |
| `ModeToolbar` | Scene-view `Overlay` content: Select/Place/Erase + Move/Rotate/Scale + snap toggles. |
| `MeshPreview` | Cached LOD0 thumbnail, refreshes when mesh changes. |
| `DensityTextureField` | Density-map field with preview + Paint + Auto-fix. |
| `DensityPaintWindow` | Standalone EditorWindow for painting the density texture. |
| `LodDistanceBar` | Horizontal segmented bar with draggable switch handles + camera preview cursor. |
| `LodCard` | Per-LOD card with mesh picker + tris/verts readout + clamped slider. |

Binding: `BindableElement.BindProperty(SerializedProperty)`. Conditional visibility via property-change listeners toggling `style.display`. Validation runs per-frame against per-layer rules. Scene-view UI via Unity `Overlay` API.

### §7 — USS theming (branded)

- Palette: green `#5DBB46` (grass), brown `#8A6240` (mesh), blue `#3D8BFF` (selection), red `#E14B4B` / yellow `#E0A526` / green `#3FB75A` (status), surface `#2A2A2A` (dark) / `#E6E6E6` (light).
- Typography: Unity default font; 12 px body / 13 px section headers / 11 px tile metadata; 600 weight on section headers.
- Spacing: 8 px section padding, 4 px field gap, 12 px between sections.
- Stylesheets: `GrassInteract.uss` (tokens + base), `LayerTile.uss`, `RecordRow.uss`, `ValidationBadge.uss`, `ModeToolbar.uss`, `Sections.uss`, `LodSection.uss`.
- Light + dark variants via root class swap.
- Icons: 16×16 monochrome in `Editor/UI/Icons/`.

### §8 — Migration

Clean break. Delete legacy `Demo/TerrainScatterConfig.asset` + sub-assets, `ScatterAssetMigrator.cs`, `MigrateScatterLayerTypes.cs`, `ScatterFieldRebuildLayerHarness.cs` (if migration-only). New rewrite of `ScatterFieldEditor` + `TerrainScatterConfigEditor`. User re-authors test assets via the new create flow.

### §9 — Instance runtime

- `InstanceColliderPool` MonoBehaviour: prewarms a pool of `MeshCollider` GameObjects, cap configurable per layer.
- `InstanceFrustumCuller`: per-frame test of record positions vs `Camera.main` frustum, activates colliders only within `cullDistance`.

### §10 — LOD section (visualized)

- **Distance bar:** segmented horizontal element (3 segments by default, one per LOD), proportional widths, draggable switch handles between segments. Camera-distance preview cursor (vertical line) slides along bar to visualize active LOD at a given distance.
- **LOD cards:** 3 stacked cards (default), each with 64×64 mesh thumbnail, mesh picker, tris/verts readout, clamped `Max Distance` slider, ping + remove. Cards colour-bordered to match bar segments.
- **Auto-generate:** button decimates LOD0 to LOD1 (50%) and LOD2 (25%) as sub-assets.
- **Variants:** 1-LOD, 2-LOD, N>3-LOD supported automatically.

### §11 — Wireframes

Eight panels drawn (config inspector + tile grid, density editor, instance editor with record list + selected-record sub-panel + defaults, scene overlay, density paint window, empty state, validation popover, quick-add popover) + a rewritten LOD section. See conversation transcript R6.5 + R6.6 for ASCII frames; component map at the end of R6.5 ties every wireframe element to its UIToolkit component.

## Files affected

**Modified:** `ScatterLayer.cs`, `DensityScatterLayer.cs`, `InstanceScatterLayer.cs`, `AuthoredInstancesData.cs`, `TerrainScatterConfig.cs`, `MeshScatterEngine.cs`, `ScatterAssetPostprocessor.cs`.

**Replaced (full rewrite):** `ScatterFieldEditor.cs`, `TerrainScatterConfigEditor.cs`, `ScatterBrush.cs`.

**New (editor):**
- Editors: `DensityScatterLayerEditor.cs`, `InstanceScatterLayerEditor.cs`.
- Components: `LayerTileGrid.cs`, `LayerTile.cs`, `LayerInspectorPanel.cs`, `RecordList.cs`, `RecordRow.cs`, `RecordDetailPanel.cs`, `ValidationBadge.cs`, `AutoFixButton.cs`, `ModeToolbar.cs`, `MeshPreview.cs`, `DensityTextureField.cs`, `InstancePlacementOverlay.cs`, `DensityPaintWindow.cs`, `LodDistanceBar.cs`, `LodCard.cs`, `EmptyLayersState.cs`, `QuickAddPopover.cs`, `ValidationPopover.cs`.
- UXML: `TerrainScatterConfigHeader.uxml`, `LayerTile.uxml`, `RecordRow.uxml`, `RecordDetailPanel.uxml`, `DensityLayer.uxml`, `InstanceLayer.uxml`, `ModeToolbar.uxml`, `KindAndDeformSection.uxml`, `DensityMapSection.uxml`, `PlacementSection.uxml`, `OrientationSection.uxml`, `RenderingSection.uxml`, `WindSection.uxml`, `TrampleSection.uxml`, `BoundsAndGpuSection.uxml`, `LodSection.uxml`, `AuthoredInstancesSection.uxml`, `DefaultsSection.uxml`, `EmptyLayersState.uxml`, `ValidationPopover.uxml`, `QuickAddPopover.uxml`, `DensityPaintWindow.uxml`.
- Styles: `GrassInteract.uss` (root tokens + base), `LayerTile.uss`, `RecordRow.uss`, `ValidationBadge.uss`, `ModeToolbar.uss`, `Sections.uss`, `LodSection.uss`, `DensityPaintWindow.uss`.
- Icons folder: `Editor/UI/Icons/` (~10 svg/png 16×16).

**New (runtime):** `InstanceColliderPool.cs`, `InstanceFrustumCuller.cs`.

**New (assets):** `Default_Material.mat`, default LOD0 fallback meshes (grass + mesh), kit-shipped white R8 texture template.

**Deleted:** `ScatterAssetMigrator.cs`, `MigrateScatterLayerTypes.cs`, `Demo/TerrainScatterConfig.asset` + sub-assets, all Odin attribute usings in runtime types.

## Risks

- **UIToolkit learning curve** — mitigate with a short `EDITOR-UI-GUIDE.md` in the kit.
- **No Odin attribute UI** — `[ShowIf]` / `[BoxGroup]` logic moves to controller `.cs`. More code but more controllable.
- **Drag-drop import** depth limit + collider-source rule must be documented.
- **Per-instance MeshCollider pool** — frustum culling is mandatory; cap pool size to avoid spike on entry.
- **Clean-break asset deletion** — user must save scene work; flag in changelog.
- **Scene-view gizmo + brush mode conflict** — clear Mode toolbar switch prevents overlap.
- **LOD slider clamping** — switch distances cannot cross; enforce on UI + on runtime validation.

## Success metrics

- Density inspector shows 0 fields that don't affect procedural scatter.
- Instance inspector shows 0 fields that don't affect authored records.
- New layer creation = 1 click → renderable layer with all sub-assets in place.
- Layer list legible at a glance (kind icon + thumbnail + count + status).
- Place-mode authoring takes ≤ 3 clicks for a 10-instance prop placement.
- Per-instance collider can be tuned without leaving inspector.
- All Odin attributes removed from runtime types (verified by grep).
- Inspector renders identically in light and dark editor themes.

## Next steps

1. Run `/t1k:plan` to phase the rewrite (suggested phases: runtime types cleanup → editor components catalog → density editor → instance editor + scene overlay → LOD section → runtime pooling + culling → polish + docs).
2. Each phase carries its own validation criteria and file ownership.
