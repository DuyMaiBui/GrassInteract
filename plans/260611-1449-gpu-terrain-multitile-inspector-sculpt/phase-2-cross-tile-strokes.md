# Phase 2 — Cross-tile strokes (per-intersected-tile dispatch)

**Effort: M** · **Blocked by:** P1 (needs the renderer `tiles` list + per-tile engine seam + coord lookup from P1). **Blocks:** nothing.

## Goal

When a brush circle overlaps a tile border, the stroke affects **all** intersected tiles in one stroke (today P1 resolves a single tile under the cursor center). Use the existing `TerrainPaintTargetResolver.Resolve` (already returns every tile a brush circle overlaps) to dispatch per intersected tile, with per-tile undo pushes. Honors the 1-texel shared-edge convention (`TerrainWorldGrid` class doc): the boundary texel is written into both neighbouring tiles so there is no seam gap.

## Tasks → files

### T1 — `TerrainSculptTool`: resolve + dispatch per intersected tile
**Owns:** `Editor/TerrainSculptTool.cs`

- Per stroke step (down/drag), replace the single-tile resolve with:
  1. `TerrainPaintTargetResolver.Resolve(worldCenterXZ, TerrainSculptState.BrushSize, residencySet: null, results)` — `null` because sculpt operates on the renderer's explicit `tiles` list, NOT the streaming resident set (out of scope per P1).
  2. For each resolved `coord`: skip if `renderer.EngineForCoord(coord) == null` (not in `tiles`); else seed that tile's working RT from its current height, `BeginSculptPreview`, `Dispatch` with that tile's `centerUV`/`radiusUV` (already computed per-tile by `TerrainPaintTargetResolver.WorldBrushToTileUV` in `TerrainBrushStroke.Dispatch`), throttled writeback → commit per tile.
  3. On mouse-up: final writeback + `EndSculptPreview` for **every** tile touched during the stroke (track the set).
- Working-RT management: either (a) one shared working RT re-seeded per tile per dispatch, or (b) a small per-coord RT cache for the duration of the stroke. Prefer (b) for cross-border drags so live preview shows on both tiles simultaneously; cap the cache to the ≤4 tiles a circle can overlap.

### T2 — Per-tile undo
**Owns:** `Editor/TerrainSculptTool.cs` (+ `Editor/TerrainBrushStroke.cs` only if the begin/end-stroke signature must take a tile set)

- `TerrainSculptUndo` is already keyed by `tileCoord` (`undo.Push(tile)` / `CanUndo(coord)` / `Pop(tile)`). Push one undo snapshot per affected tile at stroke begin. The `GpuTerrainRendererEditor` Undo button (P1 T6) pops the last-stroked tile; for cross-tile, pop each tile touched by the last stroke (track the stroke's tile set in `TerrainSculptState` or the editor).
- Do NOT change `TerrainSculptUndo` internals — it already supports per-coord depth.

## Verification (ONE compile + test gate)

1. **Compile gate:** `refresh_unity(force, scripts)` + `read_console` — zero errors.
2. **Test gate:** `run_tests` (EditMode) — zero failures.
3. **In-editor manual gate:**
   - In `Demo/TerrainValidation.unity`, position the brush so its circle straddles the border between the two tiles. One stroke deforms **both** tiles; the shared-edge texel matches on both sides (no visible seam gap at the border).
   - Undo reverts both affected tiles in one action.

## Risk assessment (P2)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| Seam mismatch at shared-edge texel (border texel differs between the two tiles) | 3 | 3 | 9 | Both tiles dispatch from the SAME world brush center; `WorldToTexelUV` maps the shared world point to UV=1 on one tile and UV=0 on the neighbour → same height by the shared-edge convention. Manual border-seam check confirms. |
| Per-coord RT cache leaks RTs across strokes | 2 | 3 | 6 | Clear + release the cache on mouse-up and `OnWillBeDeactivated`; cap ≤4 entries. |
| Undo desync (some tiles reverted, some not) | 2 | 3 | 6 | Push all affected-tile snapshots atomically at stroke begin; pop the full tracked set on Undo. |
| Throttled writeback for N tiles stalls the editor | 2 | 2 | 4 | Reuse the existing 0.15 s async throttle per tile; ≤4 tiles per stroke. |

No P2 risk scores ≥15 — no mandatory pre-phase mitigation gate.

## Rollback (P2)

P2 is additive on top of P1's single-tile resolve. Revert the P2 commit → the tool falls back to P1's single-tile-under-cursor behavior, which is fully functional. No data-format change.

## Timeline (P2)

| Task | Effort | Notes |
|---|---|---|
| T1 — resolve + per-tile dispatch | M | Reuses `Resolve` + `WorldBrushToTileUV`; per-coord RT cache |
| T2 — per-tile undo | S | `TerrainSculptUndo` already per-coord |
| **P2 total** | **M** | Critical path: T1 → T2; ONE gate at end |
```
```
