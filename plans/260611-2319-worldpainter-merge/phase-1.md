# Phase 1 — Inventory + Dependency Map (deliverable only, no code)

**Effort:** M · **Blocked by:** — · **Compiles at boundary:** n/a (no code change)

## Goal
Produce the authoritative per-file classification table (KEEP-rehome / DELETE / MERGE) and the complete cross-reference map. This is the contract every later phase reads. NO files move in this phase.

## Deliverable
Append to this file (or a sibling `inventory.md`) a table of all 183 `.cs` + 18 non-`.cs` assets:

| Path (rel Assets/) | Asm | Class | Target folder | Notes / cross-refs |
|---|---|---|---|---|

Seed classification (from planning scout — verify, don't trust blindly):

**KEEP-rehome runtime (GpuTerrain/Runtime):** all `Terrain*`, `Cdlod*`, `GpuTerrainEngine`, `GpuTerrainRenderer`, `HeightmapSurfaceSampler`, `GpuTerrainScatterGround`, `WorldPainter*.cs`, `WorldPainterImpostorLod`, `BiomePreset`.
**KEEP-rehome runtime (GrassInteract/Runtime):** ALL of it per E1/E2 — `ScatterField`, `GrassScatter`(static), `GrassScatterResult`, `IGrassEngine`+`Grass*Engine`, `GrassRenderer`, `GrassBendSimulator`, `GrassFieldSpace`, `Chunked*Buffer`, `InstancedPropEngine`, `Instance*`, `Density*`, `Scatter*Config`, `ScatterLayer`/`DensityScatterLayer`/`InstanceScatterLayer`, `ScatterLod`, `AuthoredInstancesData`, `ISurfaceSampler`, `RaycastSurfaceSampler`, `TerrainSurfaceSampler`, `LodCullMath`, `BrushStamp`, `GrassInteractor*`, `GrassTrail*`, `GrassTierProbe`.
**KEEP-rehome editor:** all `WorldPainter*` + `WorldPainter/*` + `BrushFalloffLut`, `TerrainPaintTargetResolver`, `TileRtCache`, `TerrainSculptRtWriteback`, `TerrainTileAssetEditor`, `TerrainTileImporter`, `TerrainValidationSceneBuilder`, assembly-info.
**KEEP-rehome tests:** all that don't exclusively test a DELETE target (see E3/E4 for the 4 straddles).
**DELETE:** §3.2 of plan.md.

## Steps
1. `set_active_instance("GrassInteract@de203215")`, verify path.
2. For every `.cs`, record asm + class + every inbound reference (`grep -rln "\bTypeName\b"`).
3. Flag each DELETE target's caller set (this seeds P2's ref-check).
4. Mark the 4 straddle tests (E3 density, E4 sculpt undo/writeback/preview) and the E1/E2 KEEP reclassifications explicitly.

## Verification
Table covers 183 `.cs` + 18 assets, every row classified, every DELETE target has a caller list. No compile (no code touched).

## Rollback
n/a (document only).
