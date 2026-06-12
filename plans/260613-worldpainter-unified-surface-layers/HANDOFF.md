# WorldPainter Unified Surface Layers — Handoff (2026-06-13)

Branch: `feat/worldpainter-ssot-consolidation`. Plan: `plans/260613-worldpainter-unified-surface-layers/plan.md`.

## Done (committed)

**`f9207e3`** — Phase 0: unified data model (additive, zero frozen edits, 378/378 tests green).
- `Assets/WorldPainter/Runtime/Surface/WorldPainterLayer.cs` — abstract base (`LayerKind` enum, `DisplayName`, `Kind`, `PaletteCount`).
- `.../Surface/SplatLayer.cs` — wraps `TerrainLayerSet` (albedo-palette SSOT).
- `.../Surface/GrassLayer.cs` — shared scatter config + `GrassVariant[] palette` (each variant = own R8 density channel, wired in Phase 2/3).
- `WorldMapAsset.cs` — `SurfaceLayers` list + `Register/UnregisterSurfaceLayer` (separate from frozen `Layers`).

## NEXT: Phase 1 — splat blend (code half; visual half waits on user textures)

The splat shader (`TerrainPatch.shader` + `TerrainSplat.hlsl`) ALREADY blends 4 albedos by RGBA weights.
The only gaps are: (a) no `TerrainLayerSet` assigned to `WorldMapAsset.splatSet`, (b) `GpuTerrainEngine` never binds the splat properties. Exact recipe:

1. **`GpuTerrainEngine.cs`** (EDITABLE, not frozen):
   - Add property-ID statics using `TerrainShadingConfig.PROPERTY_SPLAT_TEX` (`_SplatTex`),
     `PROPERTY_LAYER_ARRAY` (`_LayerAlbedoArray`), and the layer-tiling property
     (check `TerrainShadingConfig` for a `PROPERTY_LAYER_TILING`; add the const if missing → `_LayerTiling`).
   - In `Build()` after line 186 (the `_HeightTex` bind): `if (gpuRes.SplatTexture != null) patchMaterial.SetTexture(ID_SplatTex, gpuRes.SplatTexture);` (per-tile, like height).
   - Add `internal void BindSplatLayers(Texture2DArray? layerArray, float layerTiling)` → sets `_LayerAlbedoArray` + `_LayerTiling` on `this.patchMaterial`. (Textures persist on the material clone → bind once in/after Build; no Submit rebind needed, unlike buffers.)

2. **NEW `Assets/WorldPainter/Runtime/Terrain/TerrainLayerSetBinder.cs`** — the lifetime manager the doc at `TerrainLayerSet.cs:56` references but that does NOT yet exist. Builds + caches the `Texture2DArray` from a `TerrainLayerSet` (`BuildArray()`), exposes `Array` + `Tiling` (`set.LayerTiling`), `Dispose()` releases the array. ONE per map (the array is map-level, shared across all tile engines — do NOT call BuildArray per-tile or it leaks N copies).

3. **`WorldPainter.Render.cs`** (EDITABLE): build a `TerrainLayerSetBinder` once from `this.map.SplatSet`
   (cache as a field; rebuild on map change; dispose alongside engines in the teardown path).
   After `engine.Build(...)` in BOTH `BuildOneTileAsset` (line ~194) and `BuildOneTile` (line ~239),
   call `engine.BindSplatLayers(this.splatBinder?.Array, this.splatBinder?.Tiling ?? TerrainShadingConfig.DEFAULT_LAYER_TILING)`.

4. **`TerrainStreamingManager.cs:225`** (`OnTileLoaded`) — check if it has a `WorldMapAsset`/`splatSet` ref;
   if yes wire the same `BindSplatLayers`; if not, leave it (graceful → shader `_BaseColor` fallback, unchanged behaviour).

**Verify (code):** `refresh_unity(force, all)` → `read_console(errors)` → `run_tests(EditMode, WorldPainter.Tests)` (378 green).
**Verify (visual, needs user textures):** build a `TerrainLayerSet` from the user-provided ground albedos,
assign to demo `WorldMap.splatSet`, paint ≥2 splat channels, confirm blended per-layer albedos (not flat `_BaseColor`).

## Then Phase 2 (grass variants), 3 (painting/persistence), 4 (reauthor demo) — see plan.md.

## Env / gotchas
- **`refresh_unity(scope=scripts)` does NOT import NEW `.cs` files** (`refresh_triggered=false`) → new types show CS0246.
  Use `scope=all` whenever new files were added. (scope=scripts only recompiles already-imported files.)
- `mcp__UnityMCP__execute_code` BROKEN here → use `Debug.Log` + `read_console`. Unity 6000.3.13f1.
- MCP bridge drops ("Client handler exited") on every domain reload — normal; wait + retry, never restart the editor.
- Pre-existing uncommitted demo-asset changes (`WorldMap.asset`, `WorldPainterDemo.unity`, `Props 1*.asset`) are NOT part of this work — leave them.
- FROZEN: `WorldPainter.Scatter.cs` + engines (`GrassGpuEngine`/`GrassCpuEngine`/`InstancedPropEngine`/`InstanceBatchPool`/`IGrassEngine`/`IScatterPlacement`). A NEW partial may CALL their private helpers (same class) but never edit the files.
