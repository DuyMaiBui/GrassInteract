# Phase 8 — Consolidate Parallel Classes + Split Oversized Files + Retarget Builder Path (GATE)

**Effort:** L · **Blocked by:** P7 · **Compiles at boundary:** YES

## Goal
Now the old path + both demo scenes are gone, consolidate the transitional parallel classes (plan §3.3), split oversized files (§3.4), and retarget the KEPT `TerrainValidationSceneBuilder` generator output to the new tree. SOLID + DRY per `.claude/rules/code-conventions-unity.md`.

## Consolidation (§3.3)
- **`GpuTerrainRenderer` (runtime) ↔ `WorldPainter.Render.cs`** — `WorldPainter.Render` "mirrors GpuTerrainRenderer exactly". With `TerrainValidation.unity` now DELETED (P7), NO checked-in scene references `GpuTerrainRenderer` — its only remaining consumers are `WorldPainter.Render`, `TerrainTileAssetEditor`, and `TerrainValidationSceneBuilder` (which *constructs* one at generation time). Consolidation is therefore freer than originally assumed. Ref-check those 3 consumers, then either: (a) keep `GpuTerrainRenderer` as the runtime component the builder instantiates + have `WorldPainter.Render` delegate to it (zero dup); or (b) fold it fully into `WorldPainter.Render` and update the builder + `TerrainTileAssetEditor`. Pick the option that removes duplication without breaking the builder; record the choice.
- Verify `TerrainSculptRtWriteback`, `TerrainPaintTargetResolver`, `TileRtCache`, `BrushFalloffLut` are single-owner KEEP utilities (no dead duplicate after P6).

## Retarget `TerrainValidationSceneBuilder` generator path (E5 follow-up)
The builder hardcodes `private const string DEMO_DIR = "Assets/GpuTerrain/Demo"` and writes `TileA/TileB/ValidationLayerSet/TerrainValidation.unity` there. Post-merge that path no longer exists. Retarget `DEMO_DIR` to a generated-output location under the new tree (e.g. `Assets/WorldPainter/Generated` or a transient temp path) so the kept "Create 1×1 tile" coach-marks button writes somewhere valid. Keep the output OUT of the committed tree (the regenerated scene is a user convenience, not a checked-in asset) — gitignore the output dir or write to a temp scene. Confirm `WorldPainterCoachMarks` still compiles against the builder API.

## Oversized splits (§3.4 — split by responsibility, each new partial ≤200)
`WorldPainterSculptTool.Stroke.cs` (349 → stroke-path / per-stamp dispatch / kernel-select), `WorldPainterUndo.cs` (269 → ring / Unity-Undo bridge), `WorldPainterLayerStackView.Mutations.cs` (256), `WorldPainterSculptTool.cs` (247), `WorldPainterLayerStackView.cs` (246), `WorldPainterBrushDock.cs` (243), `WorldPainterLodPreviewPanel.cs` (231), `WorldPainterMigration.cs` (230), `WorldPainterBiomePaletteView.cs` (222), `WorldPainterInspector.cs`/`WorldPainterLodBandRuler.cs` (212 — only if clean seam), `TerrainValidationSceneBuilder.cs` (253 — split tile-gen vs scene-gen only if a clean seam, alongside the path retarget). Splits are partial-class additions (same type, new file) → original `.meta` GUID stays; new partials get Unity-generated metas.

## Steps
1. Pin instance.
2. Batch all consolidation + splits + builder-path retarget (per `ai-velocity-batch-compile-unity.md` — implement all, verify once).
3. `refresh_unity(force)` → `read_console` → `run_tests`.

## Verification — GATE
`run_tests` green. No file >200 lines without documented justification (`wc -l`). No duplicated render-submit path. `TerrainValidationSceneBuilder` compiles and its `DEMO_DIR` points at a valid new-tree/temp path.

## Rollback
`git reset --hard <P7 sha>`.

## Risk
R6 — the builder-path retarget keeps the coach-marks "Create 1×1 tile" button alive after the scene sweep; smoke-checked at P10 (SC8).
