# Phase 7 — Delete Legacy Scatter Authoring + BOTH Demo Scenes (GATE)

**Effort:** M · **Blocked by:** P6 · **Compiles at boundary:** YES

## Goal
Delete the legacy scatter AUTHORING (ScatterStudio window + gizmos/brush-library/tools) AND sweep **both** old-path demo scenes — while KEEPING the `ScatterField` runtime engine host + cluster (E1), the static `GrassScatter` builder (E2), and `TerrainValidationSceneBuilder` (E5 sub-decision).

## DELETE set (each: P2 ref check → repoint → `git rm`)

**Authoring `.cs`:** `ScatterStudioWindow.cs`, `ScatterStudio/*` (`AnchorPreviewPanel`, `BrushLibraryView`, `DensityPaintGPU` (delete AFTER E3 test repoint), `DensityPaintPanel`, `InstanceGhostPreview`, `InstancePanel`, `LayerPanelView`, `LayerRailView`, `LodDistanceBar`, `ScatterBrushPreview`), `ScatterGizmos.cs`, `ScatterBrushLibrary.cs`, `ScatterBrushLibraryProvider.cs`, `ScatterDensityOverlay.cs`, `ScatterFieldEditorTick.cs`, `ScatterFieldLookup.cs`, `ScatterRebuildScheduler.cs`, `ScatterAuthoringState.cs`, `DensityPaintTool.cs`, `DensityMapFactory.cs`, `InstancePlacementTool.cs`, `TerrainScatterConfigEditor.cs` (verify NOT the sole editor for a KEPT config; if WorldPainter inspector supersedes it, delete).

**Non-.cs (deleted-window assets):** `ScatterStudio.uss`, `ScatterStudio.uxml`, `ScatterStudioLight.uss`, `DensityPaintBrush.shader`.

**Demo scene sweep — FULL old-path scene removal (locked-#2 + E5):**
1. **`GrassInteract/Demo/GrassInteractDemo.unity`** + meta + scene-only assets (`DensityMap.*`, `GrassInteractDemo.mat`, `GrassInteractGround.mat`, `GrassInteractIndirectMat.mat`, `New Material.mat`, `ScatterPropRock.mat`, `GrassInteractDemoEffector.cs`). `TerrainScatterConfig.asset` — KEEP if WorldPainter migration/runtime reads it (ref-check); else delete.
2. **`GpuTerrain/Demo/TerrainValidation.unity`** + meta + its 4 generated assets `TileA_0_0.asset`, `TileB_1_0.asset`, `ValidationLayerSet.asset`, `TerrainPatch_Validation.mat` (E5). **Pre-delete ref check (done in planning, re-confirm):** NOT in `EditorBuildSettings` (`m_Scenes: []`); NO test loads it (`grep` tests for `OpenScene`/`TerrainValidation` → empty); the 4 assets are referenced ONLY by `TerrainValidationSceneBuilder`. Safe to `git rm` scene + 4 assets + metas.

## KEEP discipline — do NOT delete
- **E1:** `ScatterField`, `IGrassEngine`/`GrassGpuEngine`/`GrassCpuEngine`, `GrassRenderer`, `GrassBendSimulator`, `GrassFieldSpace`, `Chunked*Buffer`, `InstancedPropEngine`, `Instance*`/`Density*`Placement, `GrassScatter`(static)+`Result`, `TerrainScatterConfig`(type), `GrassInteractor*`/`GrassTrail*`. The 4 KEEP→ScatterField refs (`WorldPainter.cs`, `GpuTerrainScatterGround`, `WorldPainterMigration`, `WorldPainterScatterLayerCard`) stay intact.
- **E5 sub-decision:** `TerrainValidationSceneBuilder.cs` is **KEPT** — `WorldPainterCoachMarks.BuildNoTilesEmptyState()` wires the shipped "Create 1×1 tile" button to `TerrainValidationSceneBuilder.CreateValidationScene`. Deleting the *pre-baked scene* does NOT break the builder (it regenerates on click). Do NOT delete the builder. (Its hardcoded `Assets/GpuTerrain/Demo` output path is retargeted in P8.)

## Steps
1. Pin instance.
2. Per DELETE target: confirm caller list (all callers must be DELETE-too or already repointed — e.g. `WorldPainterCoachMarks` keeps its `TerrainValidationSceneBuilder` ref), `git rm` file/asset + meta.
3. Batch deletes → `refresh_unity(force)` → `read_console` → `run_tests`.

## Verification — GATE
`read_console` clean (no dangling refs; no missing-script GUID warnings from the deleted scenes). `run_tests` green (count = post-P6 baseline). `ScatterField` + grass cluster + `TerrainValidationSceneBuilder` still compile. `grep` no surviving `*.unity`/`*.asset` references a deleted GUID. `find Assets -name '*.unity'` → no `TerrainValidation.unity`, no `GrassInteractDemo.unity`.

## Rollback
`git reset --hard <P6 sha>`.

## Note
E1–E5 are RESOLVED (plan §0). This phase executes the confirmed decisions — no further user gating required.
