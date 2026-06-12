# Phase 6 — Tile-agnostic world-space brush + seam sync

**Effort:** L · **Wave:** E (after P5) · **Depends on:** P1 (tiles/channels), P5 (active-paint-layer API) · **Blocks:** P9

## Goal

A purely **world-space** brush: a stroke writes height/splat/density to **every overlapped tile** and **syncs shared edges** (paints across seams). Brush **only affects existing tiles** (creation is the P4 ghost quads — never the brush). One **unified always-visible thumbnail strip** of stamps shared across terrain + scatter modes; size/strength/falloff sliders below. Brush import = drag grayscale texture → saved to Editor `Resources`, loaded on inspector activation. Per-tile RT cache + undo stay per-tile under the hood.

## File-ownership group (this phase = ONE subagent in WAVE E)

**G6.1 — World-space stroke + seam sync (Editor/Brush)**
- `Assets/WorldPainter/Editor/Brush/WorldPainterStroke.cs` *(edit)* — convert stroke origin to world space; resolve ALL overlapped tiles per stamp (not a single selected tile).
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Stroke.cs` *(edit)* — multi-tile dispatch; remove any "selected tile" precondition.
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Density.cs` *(edit)* — density mode writes to active layer's per-tile R8 channel (active layer from P5 `WorldPainterState`).
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Kernels.cs` *(edit)* — kernel dispatch per overlapped tile.
- `Assets/WorldPainter/Editor/Brush/WorldPainterStampMath.cs` *(edit)* — world→tile-texel mapping across tile boundaries.
- `Assets/WorldPainter/Editor/Brush/TerrainSculptRtWriteback.cs` *(edit)* — **seam sync:** after writeback, copy the shared edge row/column so both tiles' edge texels are byte-identical (uses `TerrainWorldGrid` 1-texel shared-edge convention).
- `Assets/WorldPainter/Editor/Brush/TileRtCache.cs` *(edit)* + `WorldPainterUndo*.cs` *(edit)* — stay per-tile; record all overlapped tiles in one undo group.

**G6.2 — Unified brush strip + import (Editor)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterBrushDock.cs` *(edit)* — always-visible thumbnail strip; selected stamp highlighted; size/strength/falloff sliders; Height/Splat/Density mode toggle.
- `Assets/WorldPainter/Editor/Brush/TerrainBrushPreview.cs` *(edit)* — stamp thumbnails.
- Brush import: drag grayscale texture → save to `Assets/WorldPainter/Editor/Resources/Brushes/` → load on inspector activation (shared across all maps; NOT nested in `WorldMapAsset`).

## Parallelizable vs sequential

**Held to WAVE E** (not parallel with WAVE D) because density-mode painting consumes the active-paint-layer API delivered by P5. Within the phase, G6.1 and G6.2 touch disjoint files and MAY fan out to 2 subagents if desired (Brush/* vs BrushDock+preview), but the simplest path is one subagent.

## Verification

1. **Compile:** `read_console` + `run_tests` in one pass.
2. **High-risk mitigation test (seam sync):** `WorldPainterSeamSyncTests.cs` — paint a height stroke centered on the shared edge of `Tile_0_0` and `Tile_1_0`; assert the shared column texels of both tiles are **byte-identical** after writeback. Repeat for splat + density modes.
3. **Existing tests stay green:** `WorldPainterStrokeTests`, `TerrainBrushMathTests`, `TerrainBrushPreviewTests`, `TerrainSculptRtWritebackTests`, `DensityBrushMathTests`.
4. **No-create guard test:** brushing over an empty (no-tile) coord does nothing (assert no tile created).
5. Brush import: drag a `.png` → assert saved under Editor `Resources/Brushes` and appears in the strip on next inspector activation.

## Success criteria (maps to design success criteria 4 & 7-partial)

- World-space brush paints height/splat/density across tile seams **without selecting a tile**; shared edges stay seamless (byte-identical, test-proven).
- Brush only affects existing tiles (no creation).
- Unified always-visible thumbnail strip with size/strength/falloff; mode toggle.
- Brush import saves grayscale stamp to Editor Resources, loaded on activation.
- Per-tile RT cache + undo intact.
