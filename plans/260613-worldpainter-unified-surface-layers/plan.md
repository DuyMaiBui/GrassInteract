# WorldPainter — Unified Surface Layers (grass + splat)

**Branch:** `feat/worldpainter-ssot-consolidation`
**Created:** 2026-06-13
**Status:** P0 ✅ `f9207e3` · P1 ✅ `328bd49` · P2 ✅ `5b8ba17` · P3 ✅ `7973c15` · P3b ✅ `b058f1b` — all 378/378 green.
**Functional pipeline COMPLETE** (data model + splat blend + grass multi-variant scatter + authoring + interactive paint).
Remaining = polish: **Phase 4** SurfaceLayers inspector cards (replace the menu + Surface Paint window), splat
channel-selection consolidation (still uses the legacy stack), demo reauthor. See HANDOFF.md.

## Goal

One unified layer architecture for grass (density scatter) and splat (terrain
blend): a single SSOT config per layer + a palette of multiple textures, painted
via **one RGBA control texture per tile** (no separate per-texture control maps).

## Locked design (confirmed with user)

- **`WorldPainterLayer`** abstract ScriptableObject → `SplatLayer` + `GrassLayer`,
  stored in a NEW `WorldMapAsset.SurfaceLayers` list (NOT the frozen `Layers`).
- Each tile: **one RGBA control texture per system**, 4 channels = up to 4 palette slots.
- **SplatLayer**: palette ≤4 albedos. Channel = blend WEIGHT. Existing
  `TerrainPatch.shader` + `TerrainSplat.hlsl` already blend 4 albedos by RGBA weight
  (`_SplatTex` × `_LayerAlbedoArray`) — the gap is the missing asset + binding.
- **GrassLayer**: shared `{ lodMeshes, material, weight, wind, bend, bounds, placement }`;
  palette `{ texture }`. Each variant owns its OWN per-tile **R8 density channel**
  (the existing `TileDensityChannel` mechanism, keyed by `layerId#variantIdx`),
  painted independently, and scatters via its OWN frozen-engine instance.
- **Splat cap = 4** (one RGBA control texture per tile → `MAX_SPLAT_LAYERS`). Grass
  variant count is NOT RGBA-bound (separate R8 channels) — keep a sane palette cap.

## Existing infra to REUSE (do not reinvent — confirmed by deep-read)

- `TerrainLayerSet` (SO): `Texture2D[] layerAlbedos` + `float layerTiling` + `BuildArray()`
  → `Texture2DArray` (truncates at `MAX_SPLAT_LAYERS=4`). **This IS the splat palette store.**
- `WorldMapAsset.splatSet : TerrainLayerSet?` (line 84) — the reference to assign (currently null).
- `TerrainTileGpuResources` (EDITABLE): exposes `HeightTexture` + `SplatTexture` (RGBA32, already uploaded).
- `GpuTerrainEngine.Build()` (EDITABLE, ~line 183-186): binds `_HeightTex` only — add `_SplatTex`,
  `_LayerAlbedoArray`, `_LayerTiling` here (re-bind in `Submit()` for domain-reload safety).
- `TerrainTileAsset.TileDensityChannel` (R8 256², keyed by `layerId`); `WorldMapAssetLifecycle`
  `AddDensityLayer` + `AllocateDensityChannelOnAllTiles(map, layerId)` — reuse for per-variant channels.
- Redundancy to retire: inline `SplatLayerDef[]` on the WorldPainter MonoBehaviour duplicates
  `TerrainLayerSet`. Consolidate to TerrainLayerSet as the SSOT (the asset-based one).
- `DensityScatterLayer.cs` is the template for `GrassLayer` (compose the 5 config structs).

## Hard constraints

- **FROZEN:** `Assets/WorldPainter/Runtime/WorldPainter.Scatter.cs` and the engine
  types (`GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`,
  `IGrassEngine`, `IScatterPlacement`). NEVER edit these files.
- **Unlock:** `WorldPainter.Scatter.cs` is `public sealed partial class WorldPainter`.
  A NEW partial file (same class) can CALL its private helpers
  (`SelectAndBuildScatterEngine`, `ResolveScatterInfra`, tier probe) and `new` the
  frozen engine types — instantiation/method-calls are allowed, only editing the
  files is not. This is how N-engines-per-variant happens with zero frozen edits.
- **Reauthor from scratch** — no migration of legacy `map.Layers` / inline
  `SplatLayerDef`. Build fresh `SurfaceLayers` in the demo.
- `mcp__UnityMCP__execute_code` is BROKEN on this machine — use `Debug.Log` +
  `read_console`. Unity 6000.3.13f1.

## Phases

### Phase 0 — Unified data model (additive) ✅ DONE (f9207e3)
- `WorldPainterLayer` abstract SO (kind, palette, shared config).
- `SplatLayer : WorldPainterLayer` — palette `{ albedo, normal, tiling }` ≤4.
- `GrassLayer : WorldPainterLayer` — shared scatter config + palette `{ texture }` ≤4.
- `WorldMapAsset.SurfaceLayers` (new list) + accessor.
- Verify: compiles; SO assets createable; inspector shows palette.

### Phase 1 — Splat blend (fixes the actual bug)
- Assign a `TerrainLayerSet` to `WorldMapAsset.splatSet`; SplatLayer is the SSOT palette
  (consolidate the inline `SplatLayerDef[]` away).
- In `GpuTerrainEngine.Build()` (editable): add `SetTexture(_SplatTex, gpuRes.SplatTexture)`,
  `SetTexture(_LayerAlbedoArray, splatSet.BuildArray())`, `SetFloat(_LayerTiling, splatSet.LayerTiling)`;
  re-bind in `Submit()`. Add the 3 `Shader.PropertyToID` constants.
- Verify: painting splat channels shows blended per-layer albedos (not the `_BaseColor` fallback).

### Phase 2 — Grass multi-variant scatter (N engines, no frozen edits)
- New `GrassVariantScatterLayer : ScatterLayer` runtime adapter wrapping `(GrassLayer, variantIdx)`:
  exposes `Render` with the variant's material+texture, shared `Wind/Deform/Bounds/Placement`,
  and the variant's density channel as its density source. Frozen engines build against it unchanged.
- New partial `WorldPainter.SurfaceLayers.cs` (same class → may CALL frozen private helpers
  `SelectAndBuildScatterEngine`/`ResolveScatterInfra` and `new` frozen engine types): per GrassLayer
  build N engines (one per variant adapter) into a parallel engine list; add Step/Submit.
- Redirect editable lifecycle call sites (`WorldPainter.Render.cs`, play-mode update)
  from `RebuildScatter` → `RebuildSurfaceLayers`.
- Verify: paint a variant's density → that texture scatters; variants mix.

### Phase 3 — Painting / persistence
- Brush writes the active SurfaceLayer's control: splat weight channel (existing path) or the
  active grass variant's R8 density channel (reuse `TileDensityChannel` via lifecycle allocation).
- Verify: paint → persists in tile sub-asset → reloads.

### Phase 4 — Reauthor demo + retire frozen path
- Build fresh SurfaceLayers in demo WorldMapAsset; paint a sample.
- Stop calling frozen `RebuildScatter` (left dead, untouched).
- Verify: full demo scene renders grass + blended splat.

## Verify loop (every phase)
edit → `refresh_unity(force, scripts)` → `read_console(errors)` →
`run_tests(EditMode, WorldPainter.Tests)` (all 378 must stay green).
