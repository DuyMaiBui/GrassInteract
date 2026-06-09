# Scatter Studio — TerrainScatterConfig UI/UX Redesign (Design Doc)

**Date:** 2026-06-09
**Branch:** feat/grass-authoring-toolchain
**Status:** Design approved (doc-only; no plan/implementation yet)
**Scope:** Replace the IMGUI-based TerrainScatterConfig authoring experience with a modern, themed UI Toolkit window.

---

## Problem statement

The grass/terrain scatter authoring system (`TerrainScatterConfig` + layers + brush stamps + density maps + authored instances) is fully IMGUI, with friction at every "create" step and a split between layer inspectors and scene-overlay tools:

- Manual sub-asset creation/wiring for layers, density maps, brush stamps, `AuthoredInstancesData`.
- Density map requires a hand-made R8/readable/uncompressed texture before painting.
- Brush stamps require manual PNG + R8 import + sub-asset + list wiring; no visual picker.
- Layer config lives in the Inspector; paint/place settings live in a separate scene-overlay `EditorTool` HUD — no unified surface.
- No density visualization, no collider indicators, no scatter-brush placement, no multi-select.

**Goal:** one-click creation, visual brush library, polished density painting + instance placement, in a single modern themed window.

## Approved decisions

| Decision | Choice |
|---|---|
| UI technology | **UI Toolkit unified window** ("Scatter Studio"), UXML + USS + C# controller |
| Friction priorities | **All four**: one-click create · brush library · density polish · instance polish |
| Polish bar | **Full themed studio** — custom USS theme, icons, thumbnails, color-coded layers, transitions, drag-reorder |
| Window vs inspector | **Replace custom inspectors entirely** — window is the only authoring path; Unity default inspector remains for raw field access |
| Brush stamp scope | **Add shared global library** (project-wide) + keep per-config stamps |
| Next step | **Write design doc only** (this file); implementation deferred |

---

## Architecture

### Window
- `ScatterStudioWindow : EditorWindow` — opened via `Tools/GrassInteract/Scatter Studio`.
- Built from UXML (layout) + USS (theme) + a C# controller binding to `SerializedObject` of the active `TerrainScatterConfig` / `ScatterField`.
- Selection-aware with a **field picker** in the header; follows active `ScatterField`/`TerrainScatterConfig`.
- All edits via `SerializedObject` binding → **undo/redo, multi-edit, prefab overrides for free**.
- All mutations still route through existing `ScatterRebuildScheduler.MarkDirty` (reuse the 0.15s debounce — do NOT add a parallel rebuild path; SSOT).

### Inspector replacement
- **Remove** the custom `[CustomEditor]` bodies for `ScatterField`, `DensityScatterLayer`, `InstanceScatterLayer` (or reduce them to a single "Open in Scatter Studio" button + Unity default inspector for raw fields).
- No duplicate authoring UI to maintain — the window is canonical.

### SSOT for tool state
- New `ScatterAuthoringState : ScriptableSingleton<ScatterAuthoringState>` = single source for active brush/stamp/mode/size/opacity/falloff/flow.
- Replaces the scattered `EditorPrefs` reads currently duplicated in `DensityPaintTool` and `InstancePlacementTool`.
- One-time migration: read existing `EditorPrefs` keys into the singleton on first load so current user settings survive.

### Scene interaction (kept as EditorTools, driven by the window)
- `DensityPaintTool` / `InstancePlacementTool` remain the Handles-native scene overlays (UI Toolkit cannot draw scene Handles).
- Their **settings move into the window**; the tools read `ScatterAuthoringState`.
- Window "Paint"/"Place" entry calls `ToolManager.SetActiveTool(...)`. Scene HUD shrinks to brush cursor + mode label only.

---

## Layout (3 zones)

```
┌─ Scatter Studio ──────────────────────────────────────┐
│ Header: [Field ▾]  ◉ Preview  ◉ Colliders  [Rebuild]   │
├───────────────┬───────────────────────────────────────┤
│ LAYERS        │  SELECTED LAYER                         │
│ ▸ Grass (D)   │   ▸ Render  ▸ Wind  ▸ Deform           │
│ ▸ Rocks (I)   │   ▸ Bounds  ▸ Placement  ▸ (type)      │
│ [+ Density]   │   (Instance: per-instance panel here)  │
│ [+ Instance]  ├───────────────────────────────────────┤
│ drag-reorder  │  BRUSH LIBRARY      [global | config]   │
│ color chips   │  [▦][▦][▦] [+ New Stamp]               │
│               │  Paint: ◉size ─o─flow  [P][E][S]  ▥heat │
└───────────────┴───────────────────────────────────────┘
```

- **Left rail — Layers:** color-coded chips (Density=green, Instance=amber), drag-reorder, enable toggle, mini thumbnail (density heatmap / instance count), `+ Density` / `+ Instance` / remove.
- **Center — Selected layer:** card-style foldout sections via `PropertyField` (Render · Wind · Deform · Bounds · Placement · type-specific). Instance layers host the per-instance editor here.
- **Right/bottom dock — Brush + Paint:** thumbnail brush library with **global/config tab toggle**, active-brush settings, icon mode toggle group (Paint/Erase/Smooth), density heatmap preview.

---

## Feature designs

### One-click creation
- `+ Density` / `+ Instance`: create layer sub-asset → `AssetDatabase.AddObjectToAsset` → append `config.layers`; instance layers auto-spawn `AuthoredInstancesData`.
- **`DensityMapFactory.CreateBlank(size, owner)`** — builds a blank texture and force-sets R8 / readable / uncompressed import settings; created on demand when a density layer first needs a map. No hand-made PNGs.
- `+ New Stamp` — from a dragged texture or "paint your own"; auto-creates `BrushStamp` and forces R8/readable on `shape`.

### Brush stamp library (shared global + per-config)
- New **`ScatterBrushLibrary`** project asset (e.g. one per project under a settings folder) holding shared `BrushStamp`s, usable by any config.
- Per-config stamps remain on `TerrainScatterConfig.brushStamps`.
- Library picker = thumbnail grid with a **global | config** tab; "None (procedural)" tile; inline rename; drag-reorder.
- Active stamp writes `ScatterAuthoringState.StampRef` (resolves to either source) — `DensityPaintTool` reads it.

### Density-painting polish
- Scene **density overlay** (heatmap projected onto the field — decal/quad/gizmo) toggle; live heatmap thumbnail in window.
- Icon-based Paint/Erase/Smooth toggle group (replaces dropdown).
- Visual brush library replaces the StampIndex dropdown.

### Instance-placement polish
- **Scatter-brush place** mode (place many within radius respecting `placeSpacing`) alongside single-click.
- **Collider indicators** — colored dot on instances with collider config.
- **Multi-select + batch edit** (transform/collider).
- **Manual scale-range override** — small new runtime field on `InstanceScatterLayer`, decoupled from the auto-computed min/max (auto remains the default).
- Per-instance panel lives in the window dock (moved out of scene overlay).

### Theming (full studio)
- USS theme with pro/light skin variants via `EditorGUIUtility.isProSkin` class toggle.
- Rounded section cards, layer color chips, built-in/editor icons, hover/active transitions, drag-reorder animation.

---

## Reuse / library quality
- Window + controllers live in the existing Editor asmdef. **No third-party deps.**
- Only minimal runtime additions: `InstanceScatterLayer` scale-range override field; `ScatterBrushLibrary` (editor-or-runtime asset — keep editor-only if possible).
- Reuse `GrassFieldSpace` (world↔UV SSOT), `ScatterRebuildScheduler`, `ScatterFieldLookup`, `ScatterGizmos` unchanged.

## Risks
- **UI Toolkit ⇄ IMGUI Handles interop** for painting — mitigated: strokes stay in the EditorTool, settings in the window via `ScatterAuthoringState`.
- **Density scene-overlay rendering** (decal/quad) — moderate effort; safely deferrable to phase 3.
- **`EditorPrefs → ScatterAuthoringState` migration** — must not drop current user settings (one-time import).
- **Drag-reorder of sub-assets + undo correctness** — verify `Undo.RegisterCompleteObjectUndo` + serialized list reorder.
- **Inspector removal** — ensure nothing external depends on the removed `[CustomEditor]` behaviors before deleting.

## Phasing (for the eventual plan)
1. **Window shell** — field picker, layer list (create/remove/reorder/select), data-bound layer panel. Eliminates manual sub-asset wiring + replaces layer inspectors.
2. **Brush library + state** — `ScatterAuthoringState`, `ScatterBrushLibrary` (global+config), thumbnails, create/import/rename, wire `DensityPaintTool`, `DensityMapFactory` auto-create.
3. **Density polish** — scene overlay/heatmap, icon mode toggles.
4. **Instance polish** — scatter brush, collider indicators, multi-select, scale override, in-window per-instance panel.
5. **Theming pass** — USS theme, icons, animations, pro/light skins.

## Success criteria
- A new layer + density map + first paint stroke achievable with **zero manual asset creation** (no Assets > Create, no manual import settings).
- Brush stamp created and used **without leaving the window**.
- All current authoring capabilities preserved (paint, place, select/transform, erase, collider config, preview).
- Undo/redo works across all window edits.
- No regression in live re-scatter (still debounced via `ScatterRebuildScheduler`).

## Out of scope (this design)
- Runtime/GPU scatter pipeline changes.
- New placement strategies (density/instance only).
- The implementation itself (deferred per "doc only").

---

## Key existing files (reference)
- Config: `Assets/GrassInteract/Runtime/TerrainScatterConfig.cs`, `ScatterLayer.cs`, `DensityScatterLayer.cs`, `InstanceScatterLayer.cs`, `BrushStamp.cs`, `AuthoredInstancesData.cs`
- Space/placement: `GrassFieldSpace.cs`, `DensityPlacement.cs`, `InstancePlacement.cs`, `ScatterField.cs`
- Editors (to be replaced): `Editor/ScatterFieldEditor.cs`, `Editor/DensityScatterLayerEditor.cs`, `Editor/InstanceScatterLayerEditor.cs`
- Tools (kept, driven by window): `Editor/DensityPaintTool.cs`, `Editor/InstancePlacementTool.cs`
- Infra (reused): `Editor/ScatterRebuildScheduler.cs`, `Editor/ScatterFieldLookup.cs`, `Editor/ScatterGizmos.cs`
