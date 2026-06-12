#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Builds a Unity <c>TerrainData</c> heightfield proxy from a <see cref="TerrainTileAsset"/>'s
    /// R16 height data, then attaches a <c>Terrain</c> + <c>TerrainCollider</c> to a
    /// provided <see cref="GameObject"/> so physics raycasts land on the tile.
    ///
    /// Resolution mapping: R16 (heightRes × heightRes) is downsampled to
    /// <see cref="TerrainColliderConfig.HEIGHTFIELD_RES"/> × HEIGHTFIELD_RES using
    /// nearest-neighbour to stay cheap. The TerrainCollider heightfield is cheaper
    /// to cook than a MeshCollider on the same geometry.
    ///
    /// The caller owns the lifetime of both the GameObject and TerrainData;
    /// call <see cref="Release"/> to destroy both when the tile is evicted.
    /// </summary>
    public sealed class TerrainColliderProvider
    {
        // ── Result handle ─────────────────────────────────────────────────────

        /// <summary>
        /// Result of a successful <see cref="Build"/> call.
        /// Contains the TerrainData (owned by this handle) and the host GameObject.
        /// </summary>
        public sealed class Handle
        {
            internal TerrainData   TerrainData { get; }
            internal GameObject    Host        { get; }
            internal Vector2Int    TileCoord   { get; }

            internal Handle(TerrainData td, GameObject host, Vector2Int coord)
            {
                this.TerrainData = td;
                this.Host        = host;
                this.TileCoord   = coord;
            }

            /// <summary>
            /// Destroy the host GameObject and the TerrainData asset.
            /// Call this when the tile is evicted from the collider ring.
            /// </summary>
            public void Release()
            {
                if (this.Host != null)
                    Object.Destroy(this.Host);
                if (this.TerrainData != null)
                    Object.Destroy(this.TerrainData);
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a heightfield collider for <paramref name="tile"/> and attach it to a
        /// new child of <paramref name="parent"/>.
        /// Returns null if the tile's height data is invalid.
        /// </summary>
        public static Handle? Build(TerrainTileAsset tile, Transform parent)
        {
            if (tile == null || !tile.IsHeightValid)
                return null;

            int hfRes = TerrainColliderConfig.HEIGHTFIELD_RES;

            // Build Unity TerrainData with the downsampled heightfield.
            var td = new TerrainData();
            td.heightmapResolution = hfRes; // sets (hfRes × hfRes) internal grid
            td.size = new Vector3(TerrainWorldGrid.TILE_SIZE_M,
                                  tile.maxHeight - tile.minHeight,
                                  TerrainWorldGrid.TILE_SIZE_M);

            float[,] heights = BuildHeightfield(tile, hfRes);
            td.SetHeights(0, 0, heights);

            // Create host GameObject at the tile's world origin.
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
            var host = new GameObject($"TerrainCollider_{tile.tileCoord.x}_{tile.tileCoord.y}");
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.position = new Vector3(origin.x, tile.minHeight, origin.y);

            // Attach Terrain + TerrainCollider (the cheapest heightfield collider in Unity).
            var terrain   = host.AddComponent<Terrain>();
            terrain.terrainData = td;
            terrain.enabled = false; // collider-only; no rendering

            var col = host.AddComponent<TerrainCollider>();
            col.terrainData = td;

            return new Handle(td, host, tile.tileCoord);
        }

        // ── Heightfield resampling ────────────────────────────────────────────

        /// <summary>
        /// Downsample the tile's R16 data to a (hfRes × hfRes) Unity normalized heightfield
        /// [0,1] using nearest-neighbour. Unity expects heights[row, col] where row = Z, col = X.
        /// </summary>
        public static float[,] BuildHeightfield(TerrainTileAsset tile, int hfRes)
        {
            float[,] heights = new float[hfRes, hfRes];
            float heightRange = tile.maxHeight - tile.minHeight;
            if (heightRange <= 0f) return heights;

            int srcRes = tile.heightRes;

            for (int row = 0; row < hfRes; ++row)
            {
                for (int col = 0; col < hfRes; ++col)
                {
                    // Map hfRes grid → srcRes grid via nearest-neighbour.
                    int sx = Mathf.Clamp(Mathf.RoundToInt((float)col / (hfRes - 1) * (srcRes - 1)),
                                         0, srcRes - 1);
                    int sz = Mathf.Clamp(Mathf.RoundToInt((float)row / (hfRes - 1) * (srcRes - 1)),
                                         0, srcRes - 1);

                    int texelIndex = sz * srcRes + sx;
                    ushort raw = TerrainHeightFormat.ReadRaw(tile.heightData, texelIndex);
                    float worldY = TerrainHeightFormat.DecodeHeight(raw, tile.minHeight, tile.maxHeight);

                    // Unity stores heights normalized [0,1] relative to TerrainData.size.y.
                    heights[row, col] = (worldY - tile.minHeight) / heightRange;
                }
            }

            return heights;
        }
    }
}
