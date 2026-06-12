#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// The ONLY path for adding and removing tiles and layers in a <see cref="WorldMapAsset"/>.
    ///
    /// Every mutation follows the pattern:
    ///   1. <c>AssetDatabase.AddObjectToAsset</c> / <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>
    ///   2. <c>EditorUtility.SetDirty(map)</c>
    ///   3. <c>AssetDatabase.SaveAssets()</c>
    ///
    /// Sign-safe sub-asset naming uses underscores for the coordinate separator, with "n" prefix
    /// for negative axes (e.g. Tile_n1_0 for (-1,0)).
    ///
    /// Per-tile density channels are allocated when layers are added and freed when removed.
    /// </summary>
    public static class WorldMapAssetLifecycle
    {
        // â”€â”€ Naming helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Builds the sub-asset name for a tile at <paramref name="coord"/>.
        /// Sign-safe: negative coordinates use "n" prefix (e.g. Tile_n1_0 for (-1,0)).
        /// </summary>
        public static string TileSubAssetName(Vector2Int coord)
        {
            string xStr = coord.x < 0 ? $"n{-coord.x}" : $"{coord.x}";
            string yStr = coord.y < 0 ? $"n{-coord.y}" : $"{coord.y}";
            return $"Tile_{xStr}_{yStr}";
        }

        /// <summary>
        /// Builds the sub-asset name for a scatter layer def sub-asset.
        /// </summary>
        public static string LayerSubAssetName(string layerBaseName)
        {
            return $"Layer_{layerBaseName}";
        }

        // â”€â”€ Tile lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Creates a new <see cref="TerrainTileAsset"/> sub-asset at <paramref name="coord"/> inside
        /// <paramref name="map"/>. No-op if a tile already exists at that coord.
        ///
        /// After this call: <c>map.GetTile(coord)</c> is non-null; the asset file is saved.
        /// </summary>
        /// <param name="map">The container map (must be a saved asset â€” <c>AssetDatabase.GetAssetPath</c> must return non-empty).</param>
        /// <param name="coord">Signed tile coordinate (negatives allowed).</param>
        /// <returns>The newly created tile, or the existing tile if coord was already occupied.</returns>
        public static TerrainTileAsset AddTile(WorldMapAsset map, Vector2Int coord)
        {
            var existing = map.GetTile(coord);
            if (existing != null) return existing;

            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.name      = TileSubAssetName(coord);
            tile.tileCoord = coord;

            // Seed a valid flat tile (zero-filled = flat at minHeight). Without this the tile's
            // heightData stays Array.Empty<byte>(), so TerrainTileAsset.IsHeightValid is false and
            // WorldPainter.BuildOneTileAsset silently skips it — the tile never renders.
            // splatData is left empty (optional in TerrainTileGpuResources.Upload; lazily
            // allocated by the splat brush on first paint).
            tile.heightData = new byte[tile.ExpectedHeightBytes];

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(tile, mapPath);

            // Allocate density channels for all existing layers.
            foreach (var layer in map.Layers)
                tile.AllocateDensityChannel(layer.name);

            map.RegisterTile(coord, tile);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();

            return tile;
        }

        /// <summary>
        /// Removes the <see cref="TerrainTileAsset"/> at <paramref name="coord"/> from <paramref name="map"/>.
        /// Calls <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c> to eliminate orphan sub-assets.
        /// No-op if no tile exists at coord.
        /// </summary>
        public static void RemoveTile(WorldMapAsset map, Vector2Int coord)
        {
            var tile = map.GetTile(coord);
            if (tile == null) return;

            map.UnregisterTile(coord);
            AssetDatabase.RemoveObjectFromAsset(tile);
            Object.DestroyImmediate(tile, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        // â”€â”€ Layer lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Adds a <see cref="DensityScatterLayer"/> def sub-asset to <paramref name="map"/> and allocates
        /// a density channel on every existing tile.
        /// </summary>
        /// <param name="map">The container map.</param>
        /// <param name="layerName">Unique base name for this layer (used as layerId).</param>
        /// <returns>The newly created layer def.</returns>
        public static DensityScatterLayer AddDensityLayer(WorldMapAsset map, string layerName)
        {
            var layer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            layer.name = LayerSubAssetName(layerName);

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(layer, mapPath);
            map.RegisterLayer(layer);

            // Allocate a per-tile density channel for all existing tiles.
            AllocateDensityChannelOnAllTiles(map, layer.name);

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            return layer;
        }

        /// <summary>
        /// Adds an <see cref="InstanceScatterLayer"/> def sub-asset to <paramref name="map"/>.
        /// Instance layers use per-tile <see cref="TilePropBucket"/>s (not density channels).
        /// </summary>
        /// <param name="map">The container map.</param>
        /// <param name="layerName">Unique base name for this layer (used as layerId).</param>
        /// <returns>The newly created layer def.</returns>
        public static InstanceScatterLayer AddInstanceLayer(WorldMapAsset map, string layerName)
        {
            var layer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            layer.name = LayerSubAssetName(layerName);

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(layer, mapPath);
            map.RegisterLayer(layer);

            // Allocate a prop bucket on all existing tiles.
            AllocatePropBucketOnAllTiles(map, layer.name);

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            return layer;
        }

        /// <summary>
        /// Removes a scatter layer def sub-asset from <paramref name="map"/> and frees per-tile channels/buckets.
        /// Works for both <see cref="DensityScatterLayer"/> and <see cref="InstanceScatterLayer"/>.
        /// </summary>
        public static void RemoveLayer(WorldMapAsset map, ScatterLayer layer)
        {
            string layerId = layer.name;
            bool isDensity = layer is DensityScatterLayer;

            // Free per-tile resources before removing the layer def.
            if (isDensity)
                FreeDensityChannelOnAllTiles(map, layerId);
            else
                FreePropBucketOnAllTiles(map, layerId);

            map.UnregisterLayer(layer);
            AssetDatabase.RemoveObjectFromAsset(layer);
            Object.DestroyImmediate(layer, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        // â”€â”€ Per-tile channel/bucket allocation helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void AllocateDensityChannelOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.AllocateDensityChannel(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void FreeDensityChannelOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.FreeDensityChannel(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void AllocatePropBucketOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.AllocatePropBucket(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void FreePropBucketOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.FreePropBucket(layerId);
                EditorUtility.SetDirty(tile);
            }
        }
    }
}
