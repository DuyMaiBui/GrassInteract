# Phase 1 — `WorldMapAsset` container (data SSOT)

**Effort:** L · **Wave:** A (sequential foundation) · **Depends on:** — · **Blocks:** P2, P4, P5, P6, P7, P8

## Goal

Introduce the one self-contained `WorldMapAsset` ScriptableObject. Everything nests as sub-assets via `AssetDatabase.AddObjectToAsset` — including heavy tile bytes. Tiles keyed by **signed `Vector2Int`** (unbounded N/E/S/W from origin `(0,0)`, negatives allowed). Provide a disciplined remove path and a lookup API. This phase is **pure data + lifecycle**; no renderer/brush wiring yet.

## Container layout (verbatim from design)

```
WorldMapAsset.asset
├── (inline) WorldGrid           tileSize=256, heightRes=257, splatRes=512
├── Tile_0_0  (TerrainTileAsset)  R16 height + RGBA32 splat + density channels + prop bucket
├── Tile_1_0  (TerrainTileAsset)
├── Meadow_*  (DensityScatterLayer)   map-level def; coverage painted per-tile
├── Prop_*    (InstanceScatterLayer)
│     └── Prop_*_Instances (AuthoredInstancesData, per-tile buckets)
└── SplatSet  (TerrainLayerSet)         (splat textures referenced, not nested)
```

Per-tile (`TerrainTileAsset`) holds: R16 height, RGBA32 splat, `(layerId → R8 256×256 density)` channel list, and **per-tile prop TRS buckets**. Brush stamps are **NOT** nested (editor-global Resources — see P6).

## File-ownership group (single group — no fan-out this phase)

**G1.1 — Container + lifecycle (Runtime)**
- `Assets/WorldPainter/Runtime/Terrain/WorldMapAsset.cs` *(new)* — the SO; inline `WorldGrid`; signed `Vector2Int`→tile dictionary (serialized as parallel lists, rebuilt to dict on enable); lookup API.
- `Assets/WorldPainter/Runtime/Terrain/TerrainTileAsset.cs` *(extend)* — add `(layerId → R8 256×256 density)` channel list + per-tile prop TRS bucket fields. Keep existing R16/RGBA32.
- `Assets/WorldPainter/Runtime/Scatter/AuthoredInstancesData.cs` *(extend)* — per-tile bucket keying (`Vector2Int coord` → TRS records).

**G1.2 — Sub-asset lifecycle (Editor)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldMapAssetLifecycle.cs` *(new)* — the ONLY add/remove path:
  - `AddTile(map, Vector2Int coord)` → `AddObjectToAsset` a `TerrainTileAsset` named `Tile_{x}_{y}` (signed; use `_n` or sign-safe naming for negatives, e.g. `Tile_-1_0`).
  - `RemoveTile(map, coord)` → `RemoveObjectFromAsset` + `DestroyImmediate` the tile sub-asset; free its channels.
  - `AddLayer` / `RemoveLayer` (def sub-asset + per-tile channel alloc/free — alloc API consumed by P5).
  - `AddObjectToAsset` then `EditorUtility.SetDirty(map)` + `AssetDatabase.SaveAssets()`.

## Lookup API (on `WorldMapAsset`)

- `TerrainTileAsset? GetTile(Vector2Int coord)`
- `IEnumerable<Vector2Int> EnumerateTileCoords()`
- `IEnumerable<TerrainTileAsset> EnumerateTiles()`
- `IReadOnlyList<ScatterLayer> Layers { get; }` (Density + Instance defs)
- `bool HasOpenNeighbor(Vector2Int coord, out Vector2Int[] openEdges)` — for P4 ghost quads.

## Parallelizable vs sequential

**Sequential, single group.** G1.1 and G1.2 are edited by one subagent in sequence (G1.2 depends on G1.1's types). No fan-out — this is the foundation everyone waits on.

## AddObjectToAsset spike (do FIRST — mitigates the net-new risk)

Before building the full API, write the smallest round-trip: create map → `AddObjectToAsset(tile)` → `SaveAssets` → reimport → assert the tile sub-asset is still present and bytes intact. `AddObjectToAsset` is net-new in this project (no prior use) — prove save/dirty/reimport ordering before scaling.

## Verification

1. **Compile:** `read_console` clean + `run_tests` (EditMode) in one pass; poll `WorldPainter*.dll` mtime.
2. **New EditMode tests** in `Assets/WorldPainter/Tests/Editor/`:
   - `WorldMapAssetTests.cs` — add 3 tiles incl. a negative coord `(-1,0)`; `GetTile` returns each; enumerate returns 3.
   - `WorldMapAssetLifecycleTests.cs` — **orphan guard (high-risk mitigation):** add tile+layer then remove; assert `AssetDatabase.LoadAllAssetsAtPath` returns only the root SO (zero orphan sub-assets); assert per-tile channel freed on layer remove.
   - Round-trip persistence test: sub-asset survives `SaveAssets` + reimport.

## Success criteria

- `WorldMapAsset` compiles; signed `Vector2Int` keys work for negatives.
- One `WorldMapAsset.asset` can hold N tile sub-assets + layer defs + per-tile prop buckets, all nested.
- Add→remove cycle leaves **zero orphan sub-assets** (test-proven).
- Lookup API (`GetTile`, enumerate, `HasOpenNeighbor`) returns correct results.
- All pre-existing EditMode tests stay green.
