# Phase 3 (C) — Density Paint: terrain-like `EditorTool`

Effort: **L** · Blocked by: Phase 2 (live re-scatter feedback) · Parallel-safe with Phase 4, Phase 5

## Goal

A Terrain-tool-style density brush (`DensityPaintTool : EditorTool`) active for `DensityScatterLayer`.
Raycast the surface, write the R8 `densityMap`, and re-scatter live through the Phase 2 scheduler.
Brush disc + falloff gizmo via `Handles` (replaces the deleted `BrushCursor.shader`).

## Reuse check

`DensityScatterLayer.DensityMap` (readable R8) + `Validate` (readable + uncompressed checks) already exist.
`BrushStamp` + `TerrainScatterConfig.brushStamps` exist (currently inert) — the overlay's stamp picker
consumes them. `ScatterRebuildScheduler.MarkDirty` (Phase 2) drives the live re-scatter. `ScatterGizmos`
(Phase 5) draws the brush disc.

## File ownership

### Created (all under `Assets/GrassInteract/Editor/`)
- `DensityPaintTool.cs` — `[EditorTool("Density Paint", typeof(DensityScatterLayer))]` (or typed for the `ScatterField` + selected layer; confirm the active-context object during cook):
  - `OnToolGUI(EditorWindow)`: handle mouse down/drag/up in the SceneView; `HandleUtility.GUIPointToWorldRay` → raycast (Physics raycast against `GroundSnapMask`, or terrain sampler if `boundTerrain` set — reuse `ScatterField` sampler choice).
  - Modes: **Paint** / **Erase** / **Smooth** (enum, exposed in overlay).
  - Write path: convert hit UV → density-map pixel; apply brush kernel (size/opacity/falloff/flow) to a CPU pixel buffer (`GetPixels`/`SetPixels` on the R8 texture), `Apply()` on stroke step.
  - Undo: `Undo.RegisterCompleteObjectUndo(densityMap, "Paint Density")` at stroke START (one undo entry per stroke, not per pixel).
  - Stroke END (mouse up) → `ScatterRebuildScheduler.MarkDirty(field, layerIdx)` → debounced live re-scatter. (Optionally MarkDirty on drag throttled by the same debounce.)
  - Guard: reuse `DensityScatterLayer.Validate` — if map not readable/R8, show an overlay warning and disable painting (errors-over-silent-fallback).
- `DensityPaintToolOverlay.cs` (or an `[Overlay]` panel inline in the tool) — plain IMGUI/UI-Toolkit, NO Odin:
  - Sliders: brush **size**, **opacity**, **falloff**, **flow**.
  - Mode selector: Paint / Erase / Smooth.
  - `BrushStamp` picker bound to `TerrainScatterConfig.brushStamps` (reuse existing brush library; stamp modulates the kernel).

### Consumed (do not create)
- `ScatterGizmos.cs` (Phase 5) — `BrushDisc`, falloff ring, normal helpers.
- `ScatterRebuildScheduler.cs` (Phase 2) — `MarkDirty`.

## Constraints

- Unity `EditorTool` + `Overlays` + `Handles` only; plain IMGUI/UI-Toolkit panel. NO Odin.
- Unity-stdlib only (Texture2D R8, Physics raycast, Undo, Handles, EditorTool). Genre-neutral name `DensityPaintTool`.
- Brush disc gizmo comes from the shared `ScatterGizmos` — no per-tool gizmo copy.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|:-:|:-:|:-:|------------|
| Per-pixel Undo or per-drag re-scatter stalls editor on 50k field | 4 | 3 | 12 | One Undo entry per stroke (`RegisterCompleteObjectUndo` at stroke start); MarkDirty on stroke end (debounced), not per pixel. |
| `densityMap` not readable / compressed → silent paint no-op | 3 | 3 | 9 | Reuse `Validate`; surface a clear overlay error and disable the brush — never silently swallow. |
| Raycast UV→pixel mapping wrong (paint offset from cursor) | 3 | 3 | 9 | Derive UV from the same sampler/terrain mapping `ScatterField` uses; visual brush disc gizmo confirms alignment during manual test. |
| EditorTool context object mismatch (tool not activating for layer) | 3 | 2 | 6 | Confirm the selectable context (ScatterField vs layer SO) during cook; mirror Phase 4's tool activation target. |

## Success criteria (manually validated in-editor — no automated editor-UI test)

- Activating Density Paint shows the brush-disc + falloff gizmo tracking the cursor on the surface.
- Painting writes the R8 `densityMap` (verify via the texture asset / a re-scatter density change).
- Stroke end re-scatters the layer live within ~150 ms (Phase 2 debounce), no manual rebuild.
- Paint / Erase / Smooth modes each visibly change density. Undo (Ctrl+Z) reverts a whole stroke in one step.
- Unreadable/compressed density map → clear overlay warning, brush disabled (no silent failure).
