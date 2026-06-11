# Phase 6 — Delete Old Terrain Authoring (GATE)

**Effort:** M · **Blocked by:** P5 · **Compiles at boundary:** YES

## Goal
Delete the superseded old terrain authoring path (plan §3.2 group 1). Migrate the straddle tests (E3/E4) so NO coverage is silently dropped.

## DELETE set (each: pre-delete ref check from P2 → repoint KEEP callers → `git rm`)
`GpuTerrainRendererEditor.cs`, `GpuTerrainRendererEditor.Sculpt.cs`, `TerrainSculptTool.cs`, `TerrainSculptTool.Stroke.cs`, `TerrainBrushStroke.cs`, `TerrainBrushPreview.cs`, `TerrainSculptState.cs`, `TerrainSculptConfig.cs`, `TerrainSculptUndo.cs` (from `_pending-delete/`). Verify `WorldPainterSculptTool.Density.cs` superseded-by-`DensityPaintGPU`-fold-in before deleting it (it may already be the kept density path — KEEP if so).

## Straddle-test handling (MANDATORY — no silent drop)
- **E4 `TerrainSculptUndoTests`** (`new TerrainSculptUndo()`): if `WorldPainterUndo` provides the equivalent bounded-ring/evict behavior, re-point the 11 tests at `WorldPainterUndo`; only drop a test whose exact behavior is gone. Document per-test.
- **E4 `TerrainSculptRtWritebackTests`** (CPU encode/decode mirror): the writeback path `TerrainSculptRtWriteback` is KEEP (still used by `WorldPainterDensityEncoder`) → tests stay green unchanged; verify.
- **E4 `TerrainBrushPreviewTests`** (`TerrainBrushPreview.CreateUnitDisc`): if WorldPainter's brush-disc geometry is the kept path, move the disc-geometry helper into the KEEP brush code and re-point; else keep `CreateUnitDisc` as a KEEP utility.
- **E3 `DensityBrushMathTests`** (`DensityPaintGPU.ComputeStampPositions`): handled in P7 with the `DensityPaintGPU` delete — OR port `ComputeStampPositions` math into the WorldPainter stamping path here if cleaner; re-point the 8 tests, then P7 deletes the class.

## Steps
1. Pin instance.
2. Per DELETE target: confirm P2 caller list, repoint KEEP callers, migrate straddle tests, `git rm` file+meta.
3. Batch all deletes + test repoints, then `refresh_unity(force)` → `read_console` → `run_tests`.

## Verification — GATE
`read_console` clean (no dangling refs). `run_tests` green. Count = 301 minus ONLY itemized, justified obsolete tests (list them in the commit). No silent delta.

## Rollback
`git reset --hard <P5 sha>`.

## Escalation
This is a destructive phase — cook surfaces E3/E4 to user before executing (see plan §0).
