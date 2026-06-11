# Phase 2 — Splat: Multi-Layer Painting + Palette Swatches

**Effort:** M · **Blocked by:** P1 (gate passed) · **Blocks:** P5

## Goal

Complete splat painting on the unified brush: multiple splat layers (≤4 → RGBA32) painted through the shared `BrushMask.hlsl` mask, with palette swatches in the layer stack and per-layer blend priority via drag-reorder.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterSplatPaletteView.cs` | albedo/normal/tiling swatch row + add/remove splat layer (≤4) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterSplatLayerCard.cs` | selected splat layer card (albedo thumb, tiling, blend priority) | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Shaders/TerrainBrush.compute` | splat-blend kernel: write the active layer's channel via `BrushMask.hlsl` weight; normalize across ≤4 channels |
| `Assets/GpuTerrain/Runtime/WorldPainter.Data.cs` | `splatLayers : List<{name, albedo, normal, tiling}>` materialized + active-layer index |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLayerStackView.cs` | splat rows show albedo swatch chip; reorder = blend priority |
| `Assets/GpuTerrain/Editor/TerrainSculptRtWriteback.cs` | splat RT → `TerrainTileAsset.splatData` commit through `WorldPainter` refs (path already exists for sculpt; confirm splat encoder still correct post-refactor) |

### Reuse unchanged (cite)
`TerrainTileAsset.splatData` (RGBA32 SSOT), `TerrainLayerSet.cs`, `TerrainSplatWeightTests` contract, `BrushMask.hlsl` (P1), `TerrainPaintTargetResolver` (multi-tile), `ScatterStudio.uss` swatch tokens.

## Tasks (each with verify-check)

1. **Splat layer model** — materialize `splatLayers` (≤4) in Tier-A; enforce the 4-channel RGBA32 cap with a clear error (design §7.2). → verify: 5th add is blocked with surfaced message (not silent).
2. **Splat-blend kernel over `BrushMask.hlsl`** — active layer's weight accumulates into its channel; channels renormalize to sum≤1. → verify: `run_tests` — `TerrainSplatWeightTests` (5) green; painting layer B reduces overlapping A weight.
3. **Palette swatches** — swatch row in stack + selected-layer card (albedo thumb via cached `AssetPreview`, tiling field). → verify: swatch reflects albedo; tiling edits update material binding.
4. **Blend priority by reorder** — drag-reorder of splat rows sets channel order; `Undo.RecordObject`. → verify: reorder changes overlap winner; undo restores.
5. **Multi-tile splat strokes** — splat strokes resolve across tile borders via `TerrainPaintTargetResolver` (P1 path). → verify: a stroke crossing the 2-tile validation seam paints both tiles consistently.

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Channel renormalization diverges from existing splat math | 3 | 4 | 12 | `TerrainSplatWeightTests` is the gate; keep encode/decode in `TerrainHeightFormat`/splat SSOT |
| >4 layers silently dropped | 2 | 4 | 8 | Hard cap with surfaced error (errors-over-fallbacks rule) |
| Splat writeback regressed by P1 refactor | 2 | 4 | 8 | `TerrainSculptRtWritebackTests` (8) re-run; confirm splat encoder path |

## Test plan

- `run_tests`: `TerrainSplatWeightTests` (5), `TerrainSculptRtWritebackTests` (8), `TerrainLayerSetTests` (6) green.
- New: cap-enforcement test (5th layer rejected), reorder→priority math.
- Manual: cross-seam splat stroke; palette swatch visual.
