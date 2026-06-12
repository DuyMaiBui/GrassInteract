# Phase 4 — In-scene tile creation (factory + ghost-quad overlay)

**Effort:** M · **Wave:** D (PARALLEL fan-out) · **Depends on:** P1 (lifecycle API), P3 (validation builder deleted) · **Blocks:** P9

## Goal

Create + grow tiles in the **current scene** with no scene switch. A factory creates the `WorldMapAsset` + first tile + a `WorldPainter` GameObject in the current scene. A SceneView ghost-quad overlay grows tiles at open N/E/S/W edges (signed coords). Replaces the deleted `TerrainValidationSceneBuilder` scene-hijack path.

## File-ownership group (this phase = ONE concurrent subagent in WAVE D)

**G4.1 — Factory + overlay (Editor)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldMapAssetFactory.cs` *(new)*:
  - Save dialog **once** → create `WorldMapAsset.asset`.
  - `WorldMapAssetLifecycle.AddTile(map, (0,0))` → first `Tile_0_0`.
  - Find a `WorldPainter` in the **current** scene (or add one GameObject) and assign `map`. **No `EditorSceneManager.NewScene`.**
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterNeighborGrowOverlay.cs` *(new)*:
  - SceneView `Handles` translucent clickable quad outlines at open N/E/S/W edges of existing tiles (uses `WorldMapAsset.HasOpenNeighbor` from P1).
  - **Click** → `AddTile` at that coord → `WorldPainter` rebuild.
  - **Shift-click** → create + select the new tile for immediate sculpt.
- `Assets/WorldPainter/Editor/Inspector/WorldPainterInspector.cs` *(edit — small, isolated)*: empty-state **"Create World Map"** button → calls factory. (Only WAVE-D group touching this file.)

## Non-overlap proof (WAVE D safety)

- P4 owns `WorldMapAssetFactory.cs`, `WorldPainterNeighborGrowOverlay.cs` (both new) + `WorldPainterInspector.cs`.
- P5 owns palette/card files; P7 owns prop card/emitter; P8 owns `Runtime/Terrain` bake/streaming. **`WorldPainterInspector.cs` is touched ONLY by P4** in WAVE D — P5's palette mounts via its own palette-view files, not the inspector root. If P5 must add a palette mount point, it adds a single-line call that P4 reserves a stub for (coordinated in P1's inspector layout, NOT edited concurrently). If unavoidable, demote P5's inspector edit to WAVE E behind P4.

## Parallelizable vs sequential

**Parallel** with P5/P7/P8 (WAVE D). Internally sequential (factory before overlay, since overlay assumes a map exists).

## Verification

1. **Compile:** `read_console` + `run_tests` in one pass.
2. **Manual MCP smoke** (also re-validated in P9): open an arbitrary scene → click "Create World Map" → assert a `WorldMapAsset` with `Tile_0_0` exists AND the original scene is still loaded (no switch) AND a `WorldPainter` is in it.
3. **New test:** `WorldMapAssetFactoryTests.cs` — factory creates map+tile+assigns to a WorldPainter without calling any scene API (assert current scene path unchanged).
4. Ghost-quad: enter the overlay, click an open edge, assert a new tile sub-asset appears at the expected signed coord (incl. a negative-direction grow).

## Success criteria (maps to design success criteria 2 & 3)

- "Create World Map" produces one `WorldMapAsset` + `Tile_0_0` + a `WorldPainter` in the **current** scene — **no scene switch**.
- Ghost-quad click grows tiles in any of 4 directions with **signed coords** (negative grow works).
- Shift-click creates + selects for sculpt.
- Project compiles; tests green.
