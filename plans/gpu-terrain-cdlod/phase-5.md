# Phase 5 — Sculpt + Paint Editor Tool

**Effort:** L · **Blocks:** nothing (authoring) · **Blocked by:** Phase 0 (R16 format + upload), Phase 1 (live preview); cross-tile needs Phase 3

## Goal

A Scatter-Studio-style editor window that replaces Unity's terrain editor: GPU brush height sculpt
and 4-layer splat paint that render into a `RenderTexture`, write back into Phase 0's R16 tile format,
re-upload via the Phase 0 GPU path for live preview, with per-tile snapshot undo and bounded undo
memory. No editor stalls.

## Feasibility

- **Reuse check:** writes back into Phase 0's `TerrainTileAsset` R16 format and re-uploads via
  `TerrainTileGpuResources` (Phase 0). Live preview is the Phase 1 renderer. The GPU brush compute,
  RT→R16 readback, undo snapshots, and editor window are NEW. Cross-tile strokes use the Phase 3
  residency set. Editor-window structure can reference the existing scatter editor tooling layout
  under `Assets/GrassInteract/Editor/` as a UI pattern.
- **Complexity:** complex — GPU readback writeback correctness + undo memory + editor UX.

## File ownership (new files)

```
Assets/GpuTerrain/
  Editor/
    TerrainSculptWindow.cs          (EditorWindow: brush UI, mode toggle sculpt/paint, layer picker)        ≤200
    TerrainBrushController.cs        (mouse→world brush ray, applies brush to active tile RT each stroke)     ≤200
    TerrainSculptRtWriteback.cs      (RT → R16/splat bytes readback → TerrainTileAsset; re-upload via Phase 0) ≤200
    TerrainSculptUndo.cs            (per-tile snapshot diff stack; bounded depth; Undo registration)          ≤200
    TerrainPaintTargetResolver.cs    (world brush → affected tileCoords via Phase 0 grid / Phase 3 resident set) ≤150
    TerrainSculptConfig.cs          (named consts: UNDO_DEPTH, BRUSH_RT_RES, default brush size/strength)      ≤100
  Shaders/
    TerrainBrush.compute             (height sculpt + splat paint kernels: raise/lower/smooth/flatten/paint)   ≤200
  Tests/Editor/
    TerrainSculptRtWritebackTests.cs (RT→R16 round-trip: write known RT → R16 bytes → decode equals RT)
    TerrainSculptUndoTests.cs        (snapshot push/pop restores prior R16; bounded depth evicts oldest)
    TerrainBrushMathTests.cs         (brush falloff/strength math; affected-texel set for a given radius)
```

## Tasks

1. **`TerrainSculptConfig`** — named consts: `UNDO_DEPTH`, `BRUSH_RT_RES`, default brush size/strength,
   smooth kernel radius. No magic numbers in the window or compute dispatch.
   - *Verify:* referenced by window + writeback + undo; no inline literals.
2. **`TerrainBrush.compute`** — kernels: `RaiseLower`, `Smooth`, `Flatten` (height RT), `PaintSplat`
   (splat RT, writes the active layer's weight with falloff). Operate on the resident tile's RT.
   - *Verify:* dispatch a raise brush at a known UV; the RT center texel rises by the expected amount; falloff matches `TerrainBrushMathTests` reference.
3. **`TerrainBrushController`** — mouse position → world brush ray → tile UV; applies the selected
   compute kernel to the active tile RT each drag sample; throttle to avoid per-pixel dispatch storms.
   - *Verify:* dragging in the Scene view modifies the live preview (Phase 1 renderer) in real time, no stall.
4. **`TerrainPaintTargetResolver`** — map a world brush (center + radius) to affected `tileCoord`s via
   Phase 0 `TerrainWorldGrid`; for cross-tile strokes use the Phase 3 resident set (single-tile if Phase 3 absent).
   - *Verify:* a brush straddling a tile boundary returns both tile coords; edits both seamlessly (skirt convention from Phase 0).
5. **`TerrainSculptRtWriteback`** — async-readback the height RT → R16 bytes (SSOT encode from Phase 0
   `TerrainHeightFormat`) and splat RT → RGBA bytes; write into the `TerrainTileAsset`; re-upload via
   Phase 0 `TerrainTileGpuResources` for live preview; mark the asset dirty (`AssetDatabase`).
   - *Verify:* `TerrainSculptRtWritebackTests` — write a known RT, read back to R16, decode equals the RT within R16 quantisation epsilon (round-trip).
6. **`TerrainSculptUndo`** — per-tile snapshot (or diff) pushed before each stroke begins; bounded
   `UNDO_DEPTH` (oldest evicted); integrate with Unity `Undo` so Ctrl+Z restores. Diff-based to bound memory.
   - *Verify:* `TerrainSculptUndoTests` — push N+1 snapshots with `UNDO_DEPTH=N` evicts the oldest; pop restores the exact prior R16; memory bounded.
7. **`TerrainSculptWindow`** — `EditorWindow`: brush size/strength sliders, mode toggle (sculpt/paint),
   layer picker (≤4, the Phase 2 cap), undo/redo buttons, active-tile readout. Scatter-Studio-style layout.
   - *Verify:* window opens, brushes apply, undo works, saved asset reloads with the sculpted heightmap (round-trip through Phase 0 import).

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| RT→R16 writeback drift (encode ≠ Phase 0 decode) → preview ≠ saved asset | 4 | 4 | 16 | Writeback uses Phase 0 `TerrainHeightFormat` encode SSOT; round-trip unit test asserts decode==RT within quantisation epsilon. |
| Undo memory growth (full snapshots, unbounded) | 4 | 3 | 12 | Diff-based snapshots + bounded `UNDO_DEPTH`; oldest evicted; memory-bound unit test. |
| Editor stall on per-drag-sample synchronous GPU readback | 3 | 4 | 12 | Operate on the GPU RT during the stroke; async readback to R16 only on stroke END (mouse-up), not per-sample. |
| Cross-tile stroke seams (edit one tile, neighbour stale) | 3 | 3 | 9 | `TerrainPaintTargetResolver` returns all affected tiles; Phase 0 skirt convention keeps the shared edge consistent. |
| Sculpt corrupts a tile asset (bad writeback) with no recovery | 2 | 4 | 8 | Undo snapshot taken BEFORE the stroke; AssetDatabase dirty + explicit Save; round-trip test gates writeback correctness. |

**Score ≥ 15:** writeback drift (16). Mitigation (Phase 0 encode SSOT + round-trip test) MUST pass
before the tool writes to a real asset.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| TerrainSculptConfig | S | consts SSOT |
| TerrainBrush.compute | M | sculpt + paint kernels |
| TerrainBrushMath + tests | S | falloff reference |
| TerrainBrushController | M | mouse→UV→dispatch |
| TerrainPaintTargetResolver | S | cross-tile mapping |
| TerrainSculptRtWriteback + tests | M | round-trip correctness — highest risk |
| TerrainSculptUndo + tests | M | bounded snapshot stack |
| TerrainSculptWindow | M | editor UX |
| **Total** | **L** | Critical path: Brush.compute → Controller → Writeback → Undo → Window |

## Test strategy

EditMode NUnit mirroring `DensityBrushMathTests` (existing scatter brush tests):
- `TerrainBrushMathTests` — falloff/strength, affected-texel set per radius.
- `TerrainSculptRtWritebackTests` — RT→R16→decode round-trip within quantisation epsilon.
- `TerrainSculptUndoTests` — bounded depth eviction + exact restore.
- **Editor round-trip gate** (manual) — sculpt → save → reload shows the same heightmap, no stall.
</content>
