# Phase 7 — Delete Legacy Scatter Authoring + Demo Scene (GATE)

**Effort:** M · **Blocked by:** P6 · **Compiles at boundary:** YES

## Goal
Delete the legacy scatter AUTHORING (ScatterStudio window + gizmos/brush-library/tools) and `GrassInteractDemo.unity` per locked-#2 — while KEEPING the `ScatterField` runtime engine host and its cluster (E1) and the static `GrassScatter` builder (E2).

## DELETE set (each: P2 ref check → repoint → `git rm`)
**Authoring:** `ScatterStudioWindow.cs`, `ScatterStudio/*` (`AnchorPreviewPanel`, `BrushLibraryView`, `DensityPaintGPU` (after E3 test repoint), `DensityPaintPanel`, `InstanceGhostPreview`, `InstancePanel`, `LayerPanelView`, `LayerRailView`, `LodDistanceBar`, `ScatterBrushPreview`), `ScatterGizmos.cs`, `ScatterBrushLibrary.cs`, `ScatterBrushLibraryProvider.cs`, `ScatterDensityOverlay.cs`, `ScatterFieldEditorTick.cs`, `ScatterFieldLookup.cs`, `ScatterRebuildScheduler.cs`, `ScatterAuthoringState.cs`, `DensityPaintTool.cs`, `DensityMapFactory.cs`, `InstancePlacementTool.cs`, `TerrainScatterConfigEditor.cs` (verify NOT the sole editor for a KEPT config; if WorldPainter inspector supersedes it, delete).
**Non-.cs:** `ScatterStudio.uss`, `ScatterStudio.uxml`, `ScatterStudioLight.uss`, `DensityPaintBrush.shader`.
**Demo:** `GrassInteract/Demo/GrassInteractDemo.unity` + meta; scene-only assets (`DensityMap.*`, `GrassInteractDemo.mat`, `GrassInteractGround.mat`, `GrassInteractIndirectMat.mat`, `New Material.mat`, `ScatterPropRock.mat`, `GrassInteractDemoEffector.cs`) — `git rm` if the demo scene was their only consumer (verify each). `TerrainScatterConfig.asset` — KEEP if WorldPainter migration/runtime reads it; else delete.

## KEEP discipline (E1 — do NOT delete)
`ScatterField`, `IGrassEngine`/`GrassGpuEngine`/`GrassCpuEngine`, `GrassRenderer`, `GrassBendSimulator`, `GrassFieldSpace`, `Chunked*Buffer`, `InstancedPropEngine`, `Instance*`/`Density*`Placement, `GrassScatter`(static)+`Result`, `TerrainScatterConfig`(type), `GrassInteractor*`/`GrassTrail*`. The 4 KEEP→ScatterField refs (`WorldPainter.cs`, `GpuTerrainScatterGround`, `WorldPainterMigration`, `WorldPainterScatterLayerCard`) stay intact.

## Steps
1. Pin instance.
2. Per DELETE target: confirm caller list (all callers must be DELETE-too or already repointed), `git rm` file+meta.
3. Batch deletes → `refresh_unity(force)` → `read_console` → `run_tests`.

## Verification — GATE
`read_console` clean. `run_tests` green (count = post-P6 baseline). `ScatterField` + grass cluster still compile and are referenced by WorldPainter runtime. No `*.unity` references a deleted script (grep scenes for deleted GUIDs).

## Rollback
`git reset --hard <P6 sha>`.

## Escalation
Destructive — cook surfaces E1 (ScatterField KEEP) + E5 (TerrainValidation.unity KEEP) to user before executing.
