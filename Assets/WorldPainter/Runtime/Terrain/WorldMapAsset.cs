#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Grid constants used by <see cref="WorldMapAsset"/>. Complements the existing
    /// <see cref="WorldGrid"/> serializable struct (which holds tileSizeM/heightRes/splatRes)
    /// with the density-channel resolution that is new in Phase 1.
    ///
    /// Naming: <c>WorldGrid</c> is already defined in WorldPainter.Data.cs as a serializable
    /// struct used by the <see cref="WorldPainter"/> MonoBehaviour. This static class holds the
    /// <em>constants</em> that callers of <see cref="WorldMapAsset"/> need without duplicating
    /// the serializable struct.
    /// </summary>
    public static class WorldMapGrid
    {
        /// <summary>Side length of one square terrain tile in world metres.</summary>
        public const float TILE_SIZE_M = TerrainWorldGrid.TILE_SIZE_M;

        /// <summary>Height texture resolution per tile edge (texels, includes shared edge).</summary>
        public const int HEIGHT_RES = TerrainWorldGrid.DEFAULT_HEIGHT_RES; // 257

        /// <summary>Splat texture resolution per tile edge (texels).</summary>
        public const int SPLAT_RES = TerrainWorldGrid.DEFAULT_SPLAT_RES; // 512

        /// <summary>Density map resolution per tile edge (texels, R8 channel).</summary>
        public const int DENSITY_RES = 256;
    }

    /// <summary>
    /// One self-contained WorldPainter map container. All tiles and layers are nested as
    /// sub-assets via <c>AssetDatabase.AddObjectToAsset</c> in editor-only lifecycle code.
    ///
    /// â”€â”€ Tile keying â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// Tiles are keyed by signed <see cref="Vector2Int"/> (unbounded N/E/S/W).
    /// Serialized as two parallel lists (coords + tiles); dictionary rebuilt on OnEnable/
    /// OnAfterDeserialize. Editor lifecycle (<see cref="WorldMapAssetLifecycle"/>) is the
    /// ONLY add/remove path.
    ///
    /// â”€â”€ Layer list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <see cref="Layers"/> holds <see cref="DensityScatterLayer"/> and
    /// <see cref="InstanceScatterLayer"/> defs (map-level); per-tile density channels
    /// are allocated/freed by the lifecycle editor class.
    ///
    /// â”€â”€ TerrainLayerSet â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// Splat texture references are NOT nested â€” they live in a referenced
    /// <see cref="TerrainLayerSet"/> asset (separate file).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/World Map", fileName = "WorldMap")]
    public sealed class WorldMapAsset : ScriptableObject, ISerializationCallbackReceiver
    {
        // â”€â”€ Inline grid â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Grid dimensions for this map. Uses the <see cref="WorldGrid"/> serializable struct
        /// defined in WorldPainter.Data.cs (tileSizeM / heightRes / splatRes).
        /// Density-channel resolution is fixed at <see cref="WorldMapGrid.DENSITY_RES"/>.
        /// </summary>
        [SerializeField] private WorldGrid grid = WorldGrid.Default;

        /// <summary>Read-only access to the grid dimensions.</summary>
        public WorldGrid Grid => this.grid;

        // â”€â”€ Tile storage (serialized as parallel lists for signed Vector2Int support) â”€â”€

        [SerializeField] private List<Vector2Int> tileCoords = new();
        [SerializeField] private List<TerrainTileAsset> tileAssets = new();

        /// <summary>Runtime lookup dictionary â€” rebuilt from parallel lists on enable/deserialize.</summary>
        [NonSerialized]
        private Dictionary<Vector2Int, TerrainTileAsset> tileDict = new();

        // â”€â”€ Layer list â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Tooltip("Map-level scatter layer defs (DensityScatterLayer + InstanceScatterLayer sub-assets).")]
        [SerializeField] private List<ScatterLayer> layers = new();

        // ── Surface layer list (unified splat + grass — WorldPainterLayer) ──────

        [Tooltip("Unified surface layers (SplatLayer + GrassLayer sub-assets). Separate from the " +
                 "legacy 'layers' list; iterated by WorldPainter.SurfaceLayers, NOT the frozen RebuildScatter.")]
        [SerializeField] private List<WorldPainterLayer> surfaceLayers = new();

        // â”€â”€ Splat texture set (referenced, not nested) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Tooltip("Splat texture set (TerrainLayerSet) referenced by this map. Not a sub-asset.")]
        [SerializeField] private TerrainLayerSet? splatSet;

        /// <summary>Splat texture set for this map (not nested as sub-asset).</summary>
        public TerrainLayerSet? SplatSet => this.splatSet;

        // â”€â”€ ISerializationCallbackReceiver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Called by Unity before serialization. Dict â†’ parallel lists.</summary>
        public void OnBeforeSerialize()
        {
            // Lists are the canonical serialized form; no sync needed from dict on serialize
            // because all mutations go through the lifecycle API which keeps lists+dict in sync.
        }

        /// <summary>Called by Unity after deserialization. Rebuilds the lookup dict from parallel lists.</summary>
        public void OnAfterDeserialize()
        {
            this.RebuildDict();
        }

        private void OnEnable()
        {
            this.RebuildDict();
        }

        // â”€â”€ Tile lookup API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Returns the tile at <paramref name="coord"/>, or null if absent.</summary>
        public TerrainTileAsset? GetTile(Vector2Int coord)
        {
            this.tileDict.TryGetValue(coord, out var tile);
            return tile;
        }

        /// <summary>Enumerates all tile coordinates (order matches serialization).</summary>
        public IEnumerable<Vector2Int> EnumerateTileCoords()
        {
            return this.tileCoords;
        }

        /// <summary>Enumerates all tile assets (order matches serialization).</summary>
        public IEnumerable<TerrainTileAsset> EnumerateTiles()
        {
            return this.tileAssets;
        }

        /// <summary>Total number of tiles currently in this map.</summary>
        public int TileCount => this.tileCoords.Count;

        // â”€â”€ Layer API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Read-only list of scatter layer defs (density + instance).</summary>
        public IReadOnlyList<ScatterLayer> Layers => this.layers;

        /// <summary>Read-only list of unified surface layers (splat + grass).</summary>
        public IReadOnlyList<WorldPainterLayer> SurfaceLayers => this.surfaceLayers;

        // â”€â”€ Neighbor query (used by P4 ghost quads) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly Vector2Int[] NEIGHBORS = {
            new Vector2Int(1,  0),   // East
            new Vector2Int(-1, 0),   // West
            new Vector2Int(0,  1),   // North
            new Vector2Int(0, -1),   // South
        };

        /// <summary>
        /// Returns true if <paramref name="coord"/> has at least one NSEW neighbor slot that
        /// is NOT occupied by a tile. Fills <paramref name="openEdges"/> with the open neighbor
        /// coordinates (at most 4 entries). Returns false if coord is not in the map at all.
        /// </summary>
        public bool HasOpenNeighbor(Vector2Int coord, out Vector2Int[] openEdges)
        {
            if (!this.tileDict.ContainsKey(coord))
            {
                openEdges = Array.Empty<Vector2Int>();
                return false;
            }

            var open = new List<Vector2Int>(4);
            foreach (var delta in NEIGHBORS)
            {
                var neighbor = coord + delta;
                if (!this.tileDict.ContainsKey(neighbor))
                    open.Add(neighbor);
            }

            openEdges = open.ToArray();
            return open.Count > 0;
        }

        // â”€â”€ Internal mutation API (called ONLY by WorldMapAssetLifecycle) â”€â”€â”€â”€â”€

        /// <summary>
        /// Registers a newly created tile sub-asset. Called by
        /// <see cref="WorldMapAssetLifecycle"/> AFTER <c>AddObjectToAsset</c>.
        /// </summary>
        internal void RegisterTile(Vector2Int coord, TerrainTileAsset tile)
        {
            // Guard: avoid duplicates (should never happen via lifecycle API).
            if (this.tileDict.ContainsKey(coord))
                throw new InvalidOperationException(
                    $"[WorldMapAsset] Tile at {coord} already registered. Use lifecycle API only.");

            this.tileCoords.Add(coord);
            this.tileAssets.Add(tile);
            this.tileDict[coord] = tile;
        }

        /// <summary>
        /// Unregisters a tile by coord. Called by <see cref="WorldMapAssetLifecycle"/> BEFORE
        /// <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>.
        /// </summary>
        internal void UnregisterTile(Vector2Int coord)
        {
            int index = this.tileCoords.IndexOf(coord);
            if (index < 0) return;

            this.tileCoords.RemoveAt(index);
            this.tileAssets.RemoveAt(index);
            this.tileDict.Remove(coord);
        }

        /// <summary>
        /// Appends a layer def sub-asset. Called by <see cref="WorldMapAssetLifecycle"/> AFTER
        /// <c>AddObjectToAsset</c>.
        /// </summary>
        internal void RegisterLayer(ScatterLayer layer)
        {
            if (this.layers.Contains(layer))
                throw new InvalidOperationException(
                    $"[WorldMapAsset] Layer '{layer.name}' already registered.");
            this.layers.Add(layer);
        }

        /// <summary>
        /// Removes a layer def from the list. Called by <see cref="WorldMapAssetLifecycle"/> BEFORE
        /// <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>.
        /// </summary>
        internal void UnregisterLayer(ScatterLayer layer)
        {
            this.layers.Remove(layer);
        }

        /// <summary>
        /// Appends a unified surface-layer sub-asset. Called by <see cref="WorldMapAssetLifecycle"/>
        /// AFTER <c>AddObjectToAsset</c>.
        /// </summary>
        internal void RegisterSurfaceLayer(WorldPainterLayer layer)
        {
            if (this.surfaceLayers.Contains(layer))
                throw new InvalidOperationException(
                    $"[WorldMapAsset] Surface layer '{layer.name}' already registered.");
            this.surfaceLayers.Add(layer);
        }

        /// <summary>
        /// Removes a unified surface-layer from the list. Called by <see cref="WorldMapAssetLifecycle"/>
        /// BEFORE <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>.
        /// </summary>
        internal void UnregisterSurfaceLayer(WorldPainterLayer layer)
        {
            this.surfaceLayers.Remove(layer);
        }

        /// <summary>
        /// Assigns the map-level splat texture set (the one GpuTerrainEngine binds). Called by
        /// <see cref="WorldMapAssetLifecycle"/> when a <c>SplatLayer</c> is added/removed.
        /// </summary>
        internal void SetSplatSet(TerrainLayerSet? set) => this.splatSet = set;

        // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RebuildDict()
        {
            this.tileDict = new Dictionary<Vector2Int, TerrainTileAsset>(this.tileCoords.Count);
            int count = Mathf.Min(this.tileCoords.Count, this.tileAssets.Count);
            for (int i = 0; i < count; i++)
            {
                var coord = this.tileCoords[i];
                var tile  = this.tileAssets[i];
                if (tile != null)
                    this.tileDict[coord] = tile;
            }
        }
    }
}
