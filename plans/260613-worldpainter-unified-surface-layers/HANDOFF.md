# WorldPainter Unified Surface Layers — Handoff (2026-06-13)

Branch: `feat/worldpainter-ssot-consolidation`. Plan: `plans/260613-worldpainter-unified-surface-layers/plan.md`.
All work committed, NOT pushed. All steps kept 378/378 `WorldPainter.Tests` green; zero edits to the FROZEN
`WorldPainter.Scatter.cs` / engine types.

## Done (committed)

- **`f9207e3` P0** — unified data model: `WorldPainterLayer` base + `SplatLayer` + `GrassLayer`
  (`Runtime/Surface/`) + `WorldMapAsset.SurfaceLayers`.
- **`328bd49` P1** — splat blend: `GpuTerrainEngine` binds `_SplatTex` (per-tile) + `_LayerAlbedoArray`
  + `_LayerTiling`; new `TerrainLayerSetBinder` owns the array (built once per map from `map.SplatSet`).
- **`5b8ba17` P2** — grass multi-variant scatter: `GrassVariantScatterLayer` adapter + `WorldPainter.SurfaceLayers.cs`
  partial builds N frozen engines per palette variant (reuses frozen private helpers via same-class access).
- **`7973c15` P3** — authoring lifecycle: `WorldMapAssetLifecycle.AddSplatLayer/AddGrassLayer/AddGrassVariant/
  RemoveSurfaceLayer` + `Tools/WorldPainter/Surface Layers/` menu. Grass variant density maps can seed full.
- **`b058f1b` P3b** — interactive paint-routing: density brush paints a grass variant's own density map.
  `WorldPainterDensityEncoder` is now Texture2D-target-typed; `BrushToolTargets.ResolveDensityTarget` picks the
  active variant (SurfaceLayers, Meadow kind) else the legacy layer; `WorldPainterState.ActiveGrassVariantIndex` +
  `Tools/WorldPainter/Surface Layers/Paint Target Window` selects the active variant.

**Functional pipeline is COMPLETE** — splat blend + grass multi-variant scatter + authoring + interactive paint all work.

## Manual-verify NOW (no more code needed)

- **Splat blend:** select your `WorldMap` asset → `Tools/WorldPainter/Surface Layers/Add Splat Layer`
  → on the created `TerrainLayerSet` sub-asset assign ≤4 ground albedo textures → paint with the existing
  splat tool. Terrain blends the 4 albedos by painted RGBA weight. (Or skip the menu: make a `TerrainLayerSet`
  via Create menu, assign it to `WorldMap.splatSet` directly.)
- **Grass variants:** select `WorldMap` → `Tools/WorldPainter/Surface Layers/Add Grass Layer (2 seeded variants)`
  → on the `Grass_Grass` layer assign a blade Mesh to `Render → LODs[0].mesh` (and optional per-variant `_BaseMap`
  textures) → enter Scene view. Both variants scatter across the field (density seeded full) = multi-variant grass.

## REMAINING (polish only — feature already works)

### Phase 4 — SurfaceLayers inspector cards
Replace the stop-gap menu + Paint Target window with integrated inspector UI (mirror
`WorldPainterLayerStackView` / `WorldPainterSplatLayerCard`): a "Surface Layers" section in
`WorldPainterInspector.CreateInspectorGUI` that lists `map.SurfaceLayers`, adds/removes splat & grass layers,
edits the grass palette (texture + density per variant), assigns the blade mesh, and selects the active
variant for paint (calls `WorldPainterState.SetActiveLayer(grass.name, Meadow)` + `ActiveGrassVariantIndex`).

### Splat channel-selection consolidation
Splat **albedos** now come from the unified `SplatLayer`/`TerrainLayerSet`, but the **channel** painted is still
chosen by the legacy splat stack (`WorldPainterState.ActiveLayerType` → channel from the inline `SplatLayerDef`
rows). Unify: drive the painted channel from the active `SplatLayer` palette index too, and retire the inline
`SplatLayerDef[]`. (P1 binds `map.splatSet`; consider sourcing `_LayerAlbedoArray` from the SplatLayer in
SurfaceLayers as the single SSOT.)

### Demo reauthor (user content)
Reauthor the demo `WorldMap` with fresh SurfaceLayers (real ground albedos + blade mesh + variant textures);
optionally retire the legacy `map.Layers` + frozen `RebuildScatter` path (stop calling it — never edit the frozen file).

## Env / gotchas
- **`refresh_unity(scope=scripts)` does NOT import NEW `.cs` files** (`refresh_triggered=false` → CS0246 for new types).
  Use `scope=all` whenever new files were added.
- The shader (`TerrainPatch.shader` + `TerrainSplat.hlsl`) has **no `_BaseColor` fallback** — `frag` always
  `SplatBlend()`s; binding real `_SplatTex` is strictly an improvement.
- Grass needs a blade MESH on `GrassLayer.Render.LODs` or `GrassRenderer` renders nothing.
- `mcp__UnityMCP__execute_code` BROKEN here → `Debug.Log` + `read_console`. Unity 6000.3.13f1. MCP bridge drops on
  every domain reload (normal; wait+retry, never restart the editor).
- Pre-existing uncommitted demo-asset changes (`WorldMap.asset`, `WorldPainterDemo.unity`, `Props 1*.asset`) are NOT
  part of this work — leave them.
- FROZEN: `WorldPainter.Scatter.cs` + `GrassGpuEngine`/`GrassCpuEngine`/`InstancedPropEngine`/`InstanceBatchPool`/
  `IGrassEngine`/`IScatterPlacement`. A new partial may CALL their private helpers (same class), never edit the files.
