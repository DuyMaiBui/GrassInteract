# WorldPainter — Splat Albedo + Per-Tile Grass Density + Play-Mode Render

**Date:** 2026-06-13
**Branch:** `feat/worldpainter-ssot-consolidation`
**Mode:** plan-first (user-selected), then implement with tests.

## Scope (locked with user)

| # | Bug | Decision |
|---|-----|----------|
| 1 | "Create new splat" shows no albedo/normal texture | Create **albedo only** as a sub-asset; **skip normals** entirely for now |
| 2 | Painting a grass layer across 2 tiles uses ONE global density texture, not one per tile | **Per-tile texture, eager** — allocate one density `Texture2D` sub-asset per existing tile at variant-creation; route paint + runtime per tile |
| 3 | Pressing Play shows no grass | Build + submit the unified surface (grass) engines in **play mode** |

## Confirmed root causes (file:line)

1. **Splat:** `WorldMapAssetLifecycle.AddSplatLayer` (`WorldMapAssetLifecycle.cs:226-245`) creates the `SplatLayer` + an **empty** `TerrainLayerSet` sub-asset and never creates any albedo texture. `TerrainLayerSet` (`TerrainLayerSet.cs:27`) holds only `layerAlbedos[]`; with none assigned, `BuildArray()` returns null → blank. (No normal field exists anywhere — repo-wide grep = 0 hits.)
2. **Grass density:** `GrassVariant.densityMap` (`GrassLayer.cs:24`) is ONE field-level texture. Paint resolves it globally (`BrushToolTargets.cs:62`, `DensityBrushTools.cs:40`) into ONE RT (`WorldPainterSculptTool.Density.cs:32`); runtime samples it in field space (`DensityPlacement.cs:68-69`, `GrassFieldSpace.cs`). `ctx.Tile` is available (`BrushToolContext.cs:28`) but ignored by the density path. Per-tile storage/grid math already exist (`TileDensityChannel` byte[], `TerrainWorldGrid` `TILE_SIZE_M=256`, `WorldToTileCoord`/`TileOriginWorld`).
3. **Play-mode grass:** `RebuildScatter`/`RebuildSurfaceLayers` are called only in `OnBeginCameraRenderingEdit`, guarded by `if (Application.isPlaying) return;` (`WorldPainter.Render.cs:93,99-106`). Play-mode `LateUpdate` (`WorldPainter.cs:35-43`) builds neither, and never calls `SubmitSurfaceLayers`.

## Constraints

- **FROZEN engines** (`WorldPainter.Scatter.cs` header): `GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`, `IGrassEngine`, `IScatterPlacement`, `GrassScatter`. We only EXTEND via the editable `GrassVariantScatterLayer` adapter + `RebuildSurfaceLayers`. No engine internals touched.
- Verify loop: edit → `refresh_unity(force, scripts)` → `read_console(errors)` → `run_tests(EditMode, WorldPainter.Tests)`. `execute_code` is broken on this machine — use `Debug.Log` + `read_console`.

---

## Phase 1 — Splat albedo sub-asset (self-contained)

**Edit `WorldMapAssetLifecycle.AddSplatLayer`:** after creating the `TerrainLayerSet`, create one blank albedo `Texture2D` sub-asset (`{layer.name}_Albedo0`, RGBA32, white) and assign it to `layerSet.layerAlbedos[0]` (via `SerializedObject`, mirroring `AssignDensityMap`). Add a tiny editor setter on `TerrainLayerSet` OR set the private array through `SerializedObject` ("layerAlbedos").
**Edit `RemoveSurfaceLayer` (splat branch):** also remove albedo sub-assets owned by the set (path-guarded, like the grass density cleanup).
- **Verify:** Add Splat → set's `ActiveLayerCount ≥ 1`, albedo sub-asset is a child of the map, terrain renders the albedo.
- **Tests:** extend `WorldPainterSplatLayerTests` — post-`AddSplatLayer`, albedo count ≥ 1 and the texture's asset path == map path.

## Phase 2 — Per-tile grass density: data model + lifecycle (eager)

**Data model (`GrassLayer.cs`):** replace `GrassVariant.densityMap` (single) with a per-tile collection — `[Serializable] struct TileDensityTexture { Vector2Int coord; Texture2D tex; }` + `TileDensityTexture[] densityTiles`. Add lookup `Texture2D? GetTileDensity(Vector2Int)`.
**Lifecycle (`WorldMapAssetLifecycle.cs`):**
- `AddGrassVariant`: for **every existing tile**, create a density `Texture2D` sub-asset (`{layer.name}#{i}@{coordName}_Density`) and populate the variant's `densityTiles`.
- `AddTile`: for **every existing grass variant**, create that tile's density texture (mirrors the existing per-tile channel allocation).
- `RemoveSurfaceLayer` / `RemoveTile`: clean up the per-tile density textures.
- **Migration:** the existing demo `WorldMap.asset` has single-`densityMap` variants → those become orphaned. Plan: one-time migration in lifecycle (if `densityTiles` empty but a legacy `densityMap` exists, seed each tile texture from it), then drop the legacy field. Demo asset re-saved on first edit.
- **Tests:** `WorldMapLayerAllocTests` — 2 tiles + AddGrassVariant ⇒ 2 density sub-assets/variant; AddTile-after-variant ⇒ new texture; removal ⇒ 0 orphans.

## Phase 3 — Per-tile grass density: editor paint routing

- `BrushToolTargets`: add `ResolveGrassVariantDensityForTile(painter, coord)` → the per-tile texture.
- `DensityBrushTools.DensityDispatch.Run`: use `ctx.Tile.tileCoord` to pick the per-tile texture; allocate a **coord-keyed** density RT (extend the `TileRtCache` pattern); brush already binds tile-space `centerUV/radiusUV` in `BindAndDispatch`. Writeback to the per-tile texture.
- `WorldPainterSculptTool.Density` + `WorldPainterDensityEncoder`: replace the single `densityRT`/`activeDensityMap` with a coord→(RT,target) cache; persist per tile.
- **Verify:** paint a stroke straddling both tiles → each tile's texture changes independently, no cross-tile bleed at the shared UV.
- **Tests:** extend `DensityBrushMathTests` / add a per-tile writeback test.

## Phase 4 — Per-tile grass density: runtime sampling

- `GrassVariantScatterLayer.Create`: tile-aware overload `(layer, variantIndex, tileCoord, tileDensityTex)`; override `FieldBounds → (TILE_SIZE_M, TILE_SIZE_M)`.
- `WorldPainter.SurfaceLayers.RebuildSurfaceLayers`: iterate variants × **tiles**; per (variant, tile) build an adapter at the tile origin (`TileOriginWorld + half tile`) with that tile's density texture; one engine per (variant, tile). Scale `TargetInstances` per tile.
- `DensityPlacement`/`GrassFieldSpace`: per-tile field rectangle at tile origin (already parameterized by origin+bounds — just feed tile values).
- **Verify:** edit-preview grass scatters correctly on BOTH tiles.

## Phase 5 — Play-mode grass render

- Build the surface/scatter engines in play mode: call `RebuildScatter` + `RebuildSurfaceLayers` once on play start (in `TryBuild` or first `LateUpdate`), and add `SubmitSurfaceLayers(null)` to `LateUpdate` (`WorldPainter.cs:35-43`).
- **Verify:** press Play → grass renders.

## Phase 6 — Verify + review + finalize

- One compile + full `WorldPainter.Tests` EditMode pass (all green; currently 378).
- `t1k-code-reviewer` (root-cause not symptom; no frozen-engine edits; pattern-consistent).
- `t1k-docs-manager` if warranted; update `GrassLayer`/`TerrainLayerSet` XML docs.
- Commit per phase via `t1k-git-manager` (conventional scope `feat/fix(worldpainter)`).

## Status: COMPLETE (2026-06-13)

| Phase | Commit | Tests |
|---|---|---|
| 1 — splat albedo sub-asset | `d78d6e6` | 380/380 |
| 2-4 — per-tile grass density (data model + lifecycle + paint + runtime) | `09d6a75` | 385/385 |
| 5 — play-mode grass build + submit | `4640be4` | 385/385 |
| 6 — per-target density writeback queue + review cleanups | `79d8755` | 385/385 |

Independent code review (separate agent): **SHIP**, no blockers, frozen engines untouched, cleanup symmetric. Branch `feat/worldpainter-ssot-consolidation`, **not pushed**.

**Decisions applied:** albedo-only (no normals); per-tile density eager; reset grass (no `[FormerlySerializedAs]` migration — verified zero existing `GrassVariant` assets used the old `densityMap`); per-tile `TargetInstances`.

**Manual confirmation still owed by user (cannot be asserted in EditMode tests / `execute_code` is broken on this machine):**
1. Add Splat → albedo slot is populated + terrain renders the layer.
2. Paint a stroke across both tiles → two independent per-tile density textures (no cross-tile bleed).
3. Press Play → grass renders.

## Risks / open items

- **Data migration** of the existing demo `WorldMap.asset` (Phase 2) — handled by the one-time seed-from-legacy path; the demo will be re-saved.
- **Engine count growth:** variants × tiles engines. Acceptable for the current 2-tile demo; note as a follow-up if tile counts grow large (could pool/merge).
- Per-tile `TargetInstances` scaling formula (total ÷ tileCount, or per-tile constant) — will use per-tile = layer target (density-gated), matching current visual expectation; flag if you prefer total-split.
