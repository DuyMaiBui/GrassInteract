# Phase 8 — Consolidate Parallel Classes + Split Oversized Files (GATE)

**Effort:** L · **Blocked by:** P7 · **Compiles at boundary:** YES

## Goal
Now the old path is gone, consolidate the transitional parallel classes (plan §3.3) and split oversized files (§3.4). SOLID + DRY per `.claude/rules/code-conventions-unity.md`.

## Consolidation (§3.3)
- **`GpuTerrainRenderer` (runtime) ↔ `WorldPainter.Render.cs`** — `WorldPainter.Render` "mirrors GpuTerrainRenderer exactly". Ref-check the KEPT `TerrainValidation.unity` scene + `TerrainTileAssetEditor` + `TerrainValidationSceneBuilder` first. Options: (a) keep `GpuTerrainRenderer` as the standalone scene-facing renderer + have `WorldPainter.Render` delegate to it (no dup); (b) migrate the scene to WorldPainter + delete `GpuTerrainRenderer`. Pick the one that removes duplication without breaking the scene; record the choice. **If scene migration is required, do it with verified GUIDs (git mv discipline) — flag as a sub-decision if ambiguous.**
- Verify `TerrainSculptRtWriteback`, `TerrainPaintTargetResolver`, `TileRtCache`, `BrushFalloffLut` are single-owner KEEP utilities (no dead duplicate after P6).

## Oversized splits (§3.4 — split by responsibility, each new partial ≤200)
`WorldPainterSculptTool.Stroke.cs` (349 → stroke-path / per-stamp dispatch / kernel-select), `WorldPainterUndo.cs` (269 → ring / Unity-Undo bridge), `WorldPainterLayerStackView.Mutations.cs` (256), `WorldPainterSculptTool.cs` (247), `WorldPainterLayerStackView.cs` (246), `WorldPainterBrushDock.cs` (243), `WorldPainterLodPreviewPanel.cs` (231), `WorldPainterMigration.cs` (230), `WorldPainterBiomePaletteView.cs` (222), `WorldPainterInspector.cs`/`WorldPainterLodBandRuler.cs` (212 — only if clean seam). Splits are partial-class additions (same type, new file) → `.meta` GUID of the original stays; new partials get fresh metas (Unity-generated).

## Steps
1. Pin instance.
2. Batch all consolidation + splits (per `ai-velocity-batch-compile-unity.md` — implement all, verify once).
3. `refresh_unity(force)` → `read_console` → `run_tests`.

## Verification — GATE
`run_tests` green. No file >200 lines without a documented justification. No duplicated render-submit path. `wc -l` on every touched file ≤200.

## Rollback
`git reset --hard <P7 sha>`.

## Risk
R6 (scene-facing `GpuTerrainRenderer` consolidation breaks `TerrainValidation.unity`) — ref-check before consolidating; preserve GUIDs.
