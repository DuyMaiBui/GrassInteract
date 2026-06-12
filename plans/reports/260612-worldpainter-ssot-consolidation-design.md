# WorldPainter SSOT Consolidation — Design Report

**Date:** 2026-06-12
**Type:** Brainstorm design (approved, all decisions resolved)
**Scope:** Make WorldPainter the single source of truth for map render + authoring; one self-contained `WorldMapAsset` container; in-scene tile creation; tile-agnostic painting; unified brush + layer palette; dual-mode prop placement.

---

## Problem statement

Three problems in the current architecture:

1. **Duplicate renderers.** `GpuTerrainRenderer` (standalone) and `WorldPainter.Render.cs` (which *"mirrors GpuTerrainRenderer exactly"*) both drive terrain — an SSOT violation. `ScatterField` is a separate sibling MonoBehaviour for grass/props.
2. **Scene hijack on tile creation.** "Create 1x1 tile" calls `TerrainValidationSceneBuilder.CreateValidationScene()` → `EditorSceneManager.NewScene(..., NewSceneMode.Single)` (`TerrainValidationSceneBuilder.cs:205`) which unloads the user's current scene. Not wanted — user wants to author in the current scene.
3. **Fragmented data + per-tile editing friction.** Every piece is a separate `.asset` (`TerrainTileAsset`, `ScatterLayer`, `AuthoredInstancesData`, `TerrainLayerSet`); no `AddObjectToAsset` anywhere. User must select individual tiles to paint — unwanted friction.

## Goal

- `WorldPainter` = sole renderer (terrain + scatter).
- One self-contained `WorldMapAsset` ScriptableObject; tile + layers nested as sub-assets.
- Create/grow tiles in the current scene, no scene switch.
- Tile-agnostic, world-space painting; pick a layer from a palette, never select a tile.

---

## Final design (all decisions resolved)

### Data — `WorldMapAsset` (the one container)

- New SO. Everything nested via `AssetDatabase.AddObjectToAsset`, **including heavy tile bytes** (accepted multi-MB tradeoff; coarse git diffs known).
- Tiles keyed by **signed `Vector2Int`**, unbounded N/E/S/W from origin (0,0); negatives allowed.
- Per-tile (`TerrainTileAsset`) holds: R16 height, RGBA32 splat, `(layerId → R8 256×256 density)` channel list, and **per-tile prop TRS buckets**.
- Nested sub-assets: tiles, `DensityScatterLayer` / `InstanceScatterLayer` defs, prop `AuthoredInstancesData` (per-tile bucket), `TerrainLayerSet` (splat textures referenced, not nested).
- **Brush stamps are NOT nested** — editor-global, stored in an Editor `Resources` folder, loaded when the inspector activates (shared across all maps).

```
WorldMapAsset.asset
├── (inline) WorldGrid           tileSize=256, heightRes=257, splatRes=512
├── Tile_0_0  (TerrainTileAsset)  R16 height + RGBA32 splat + density channels + prop bucket
├── Tile_1_0  (TerrainTileAsset)
├── Meadow_*  (DensityScatterLayer)   map-level def; coverage painted per-tile
├── Prop_*    (InstanceScatterLayer)
│     └── Prop_*_Instances (AuthoredInstancesData, per-tile buckets)
└── SplatSet  (TerrainLayerSet)
```

### Renderer — `WorldPainter` is sole renderer

- Keep `WorldPainter.Render.cs` terrain path. **Absorb** `ScatterField` orchestration into new `WorldPainter.Scatter.cs` partial (Rebuild / StepAll / SubmitAll / engine selection). **Keep** the engines: `GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`.
- `WorldPainter` reads tiles + layers from the referenced `WorldMapAsset` (replaces inline lists).
- `LateUpdate` → SubmitTerrain + StepScatter + SubmitScatter.

### Deletes (no migration converter)

- **Delete:** `GpuTerrainRenderer.cs`, `ScatterField.cs`, `GpuTerrainScatterGround.cs`, `TerrainValidationSceneBuilder.cs` + the validation scene, **and all stale/legacy files** tied to these.
- Repoint the sculpt-tool seam accessors + `WorldPainterMigration` to WorldPainter (or delete if legacy-only).
- **No migration tool.** Current authored data is disposable. Stand up a **fresh demo scene via Unity MCP**.

### Tile creation — in current scene, no switch

- New `WorldMapAssetFactory`: creates `WorldMapAsset` (save dialog once) + first `Tile_0_0` sub-asset; finds/adds a `WorldPainter` GameObject in the **current** scene and assigns the map. No `NewScene`.
- **SceneView ghost-quad overlay** (`WorldPainterNeighborGrowOverlay`, Handles): translucent clickable tile outlines at open N/E/S/W edges of existing tiles. Click → create tile sub-asset at that coord → rebuild. Shift-click → create + select for immediate sculpt.
- "Create World Map" entry = **inspector empty-state button only**.

### Layers — map-level def + per-tile density

- Activating a grass/scatter layer: create the `ScatterLayer` sub-asset, append to the map layer list, and **allocate an empty R8 256×256 density channel on every existing tile**. New tiles auto-allocate channels for all active layers.
- Remove layer → `RemoveObjectFromAsset` + `DestroyImmediate` the sub-asset and free each tile's matching channel (disciplined remove paths to avoid orphan sub-assets).
- `DensityScatterLayer` (meadow/grass) = density-painted via the per-tile channel. `InstanceScatterLayer` (props) = explicit TRS records, **bucketed per-tile** so they bake/stream with the tile.

### Authoring UX — tile-agnostic

- **No tile selection.** Brush is purely world-space; a stroke writes height/splat/density to every overlapped tile and **syncs shared edges** (paints across seams). Brush **only affects existing tiles** (create is via ghost quads).
- **Brush dock:** one **unified** palette shared across terrain + scatter modes — an **always-visible thumbnail strip** of stamps, selected one highlighted, size/strength/falloff sliders below. Brush = shape; layer palette = what.
- **Brush import:** drag a grayscale texture into the brush palette → saved to Editor `Resources`, loaded on inspector activation.
- **Layer palette:** 3 sections (Splat / Meadow / Prop), each a square-thumbnail grid + its **own `+`**. Grass/prop squares render LOD0 mesh+material via a custom **`PreviewRenderUtility`**; splat squares show albedo. Click a square = active paint layer.
- **Brush modes:** Height / Splat / Density toggle. Per-tile RT cache (`TileRtCache`) + `WorldPainterUndo` stay per-tile under the hood.

### Prop placement — dual workflow

- Two sub-modes per prop layer, switched by an **explicit toggle UI + shortcut key**:
  - **Scatter (brush):** drag brush → randomly places prop instances in footprint (density, jitter, random yaw, scale range, ground-snap).
  - **Transform (select):** click an instance → move/rotate/scale gizmo for fine edits.
- **Anchor config (per-layer only):** pivot offset, ground-snap, align-to-surface-normal — layer-wide, no per-instance override.
- **Inspector preview:** square LOD0 mesh preview + live instance count/stats + in-scene per-instance gizmos (no heavy list).

### Runtime / bake

- **Editor Play** reads the container directly (fast iteration).
- **Bake step** → emits **one `TerrainTileAsset` per tile** (standalone) for player builds. `TerrainStreamingManager` streams those + GPU residency by camera proximity.
- Rationale: nesting everything is great for authoring but Unity loads a whole asset at once (no lazy sub-asset load); the bake gives true per-tile streaming at runtime while the container stays the editor SSOT.

---

## Risks / caveats

- **Single-asset size & VCS:** ~1.1 MB/tile; large maps → multi-MB asset, coarse git diffs, wider merge-conflict blast radius. Accepted. Mitigation if it bites: split bulk byte[] to sub-files later.
- **Sub-asset lifecycle:** every tile/layer removal must `RemoveObjectFromAsset` + `DestroyImmediate` or orphan sub-assets accumulate.
- **Cross-tile seam sync:** brush writing two tiles must keep shared edge row/column identical — needs careful texel mapping (`TerrainWorldGrid` 1-texel shared-edge convention).
- **Editor-only RAM:** whole container resident in editor; bake is the runtime escape hatch.

## Alternatives considered (rejected per user)

- Keep `ScatterField` as an internal helper (chose full absorb).
- Tiles-only / external texture-sets (chose everything self-contained).
- Split bulk bytes now / Addressables or raw-bytes bake (chose nest-all + SO-per-tile bake).
- Migration converter (chose delete-stale + fresh demo via Unity MCP).

## Success criteria

1. `GpuTerrainRenderer`, `ScatterField`, `GpuTerrainScatterGround`, validation-scene builder deleted; project compiles; WorldPainter renders terrain + grass + props alone.
2. "Create World Map" produces one `WorldMapAsset` with a `Tile_0_0` sub-asset and a WorldPainter in the current scene — no scene switch.
3. Ghost-quad click grows tiles in any of 4 directions (signed coords).
4. World-space brush paints height/splat/density across tile seams without selecting a tile; shared edges stay seamless.
5. Layer palette: 3 sections with `+`, LOD0 previews; activating a layer allocates per-tile density channels.
6. Prop layer: brush-scatter + transform-edit both work; per-layer anchor; inspector LOD0 + count + gizmos.
7. Bake emits per-tile `TerrainTileAsset`s; streaming works in a player build.
8. Fresh demo scene built via Unity MCP renders correctly.

## Next step

Run `/t1k:plan` — phased, structured for **parallel `/t1k:cook` subagent execution** (independent file groups fanned out where ownership doesn't conflict).
