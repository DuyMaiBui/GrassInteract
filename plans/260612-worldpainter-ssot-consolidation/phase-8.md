# Phase 8 — Bake → per-tile `TerrainTileAsset` + streaming

**Effort:** M · **Wave:** D (PARALLEL fan-out) · **Depends on:** P1 (container + per-tile data), P2 (WorldPainter reads container) · **Blocks:** P9

## Goal

**Editor Play** reads the container directly (fast iteration). A **bake step** emits **one standalone `TerrainTileAsset` per tile** for player builds; `TerrainStreamingManager` streams those baked tiles + GPU residency by camera proximity. Rationale (design): nesting is great for authoring but Unity loads a whole asset at once — the bake gives true per-tile streaming at runtime while the container stays the editor SSOT.

## File-ownership group (this phase = ONE concurrent subagent in WAVE D)

**G8.1 — Bake step (Editor + new WorldPainter partial)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldMapBaker.cs` *(new)* — iterate `WorldMapAsset.EnumerateTiles()`; for each tile write a standalone `TerrainTileAsset.asset` (R16 height + RGBA32 splat + density channels + prop bucket) to a bake output folder; emit a tile manifest (coord → asset path).
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterIncrementalBake.cs` *(edit)* — wire incremental bake to emit per-tile assets (only dirty tiles re-baked).
- `Assets/WorldPainter/Runtime/WorldPainter.Bake.cs` *(new partial — owned solely by P8; does NOT touch frozen P1/P2 partials)* — runtime hook: if a bake manifest is assigned, hand tiles to `TerrainStreamingManager`; else read container directly (editor Play).

**G8.2 — Streaming consumes baked tiles (Runtime/Terrain — disjoint from all other waves)**
- `Assets/WorldPainter/Runtime/Terrain/TerrainStreamingManager.cs` *(edit)* — stream baked per-tile `TerrainTileAsset`s by camera proximity.
- `Assets/WorldPainter/Runtime/Terrain/TerrainTileResidencySet.cs` *(edit)* — GPU residency for baked tiles.
- `Assets/WorldPainter/Runtime/Terrain/TerrainTileLoader.cs` *(edit)* — load standalone baked tiles (not sub-assets).

## Non-overlap proof (WAVE D safety)

- P8 owns `Runtime/Terrain/TerrainStreaming*`, `TerrainTileResidencySet`, `TerrainTileLoader`, + new `WorldMapBaker.cs`, `WorldPainter.Bake.cs`, `WorldPainterIncrementalBake.cs`.
- P4 (factory/overlay), P5 (palette), P7 (prop) touch none of these. Fully disjoint.
- `WorldPainter.Bake.cs` is a NEW partial — does not edit the frozen `WorldPainter.cs`/`.Data`/`.Render`/`.Scatter` partials.

## Parallelizable vs sequential

**Parallel** with P4/P5/P7. Internally sequential (baker before streaming-consumes-baked).

## Verification

1. **Compile:** `read_console` + `run_tests` in one pass.
2. **Existing tests stay green:** `TerrainTileResidencySetTests`, `TerrainResidencyDiffTests`, `TerrainResidencyRingTests`, `TerrainTileLoaderTests`, `TerrainStreaming*` coverage.
3. **New test:** `WorldMapBakerTests.cs` — bake a 2-tile map → assert 2 standalone `TerrainTileAsset.asset`s emitted with byte-identical height/splat to the container tiles + a manifest mapping coords to paths.
4. **Streaming test:** residency set includes/evicts baked tiles by simulated camera proximity (extend existing residency tests).
5. Play-mode (P9): bake → enter Play in a build-like path → baked tiles stream.

## Success criteria (maps to design success criterion 7)

- Bake emits one standalone `TerrainTileAsset` per tile + a manifest.
- Incremental bake re-bakes only dirty tiles.
- `TerrainStreamingManager` streams baked tiles + GPU residency by proximity.
- Editor Play still reads the container directly (no bake required for iteration).
- Project compiles; tests green.
