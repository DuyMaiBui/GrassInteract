#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Builds a Unity <c>TerrainData</c> heightfield proxy from a <see cref="TerrainTileAsset"/>'s
    /// R16 height data, then attaches a <c>Terrain</c> + <c>TerrainCollider</c> to a
    /// provided <see cref="GameObject"/> so physics raycasts land on the tile.
    ///
    /// Resolution mapping: R16 (heightRes × heightRes) is downsampled to the caller's
    /// hfRes × hfRes (validate via <see cref="NearestValidHeightfieldRes"/>) using
    /// nearest-neighbour to stay cheap. The TerrainCollider heightfield is cheaper
    /// to cook than a MeshCollider on the same geometry.
    ///
    /// The caller owns the lifetime of both the GameObject and TerrainData;
    /// call <see cref="Release"/> to destroy both when the tile is evicted.
    /// </summary>
    public sealed class TerrainColliderProvider
    {
        /// <summary>
        /// Name prefix for cooked collider host GameObjects. Used by the streamer to reconcile
        /// orphaned hosts after a domain reload (which resets the streamer's live map but not the hosts).
        /// </summary>
        public const string HOST_PREFIX = "TerrainCollider_";

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
            /// Edit-mode safe: the streamer is <c>[ExecuteAlways]</c> and evicts during edit-mode
            /// render ticks, where <c>Object.Destroy</c> is illegal — use DestroyImmediate there.
            /// </summary>
            public void Release()
            {
                Destroy(this.Host);
                Destroy(this.TerrainData);
            }

            /// <summary>
            /// Toggle host visibility for debugging: visible → <c>DontSave</c> (shows + selectable
            /// in the Hierarchy, still never serialized); hidden → <c>HideAndDontSave</c>.
            /// </summary>
            internal void SetVisible(bool visible)
            {
                if (this.Host != null)
                    this.Host.hideFlags = visible ? HideFlags.DontSave : HideFlags.HideAndDontSave;
            }

            private static void Destroy(Object? o)
            {
                if (o == null) return;
                if (Application.isPlaying) Object.Destroy(o);
                else                       Object.DestroyImmediate(o);
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a heightfield collider for <paramref name="tile"/> and attach it to a
        /// new child of <paramref name="parent"/> at heightfield resolution <paramref name="hfRes"/>.
        /// Returns null if the tile's height data is invalid.
        /// <paramref name="hfRes"/> must be (2^n + 1) — callers should pre-validate via
        /// <see cref="NearestValidHeightfieldRes"/>.
        /// </summary>
        public static Handle? Build(TerrainTileAsset tile, Transform parent, int hfRes,
                                    bool debugVisible = false)
        {
            if (tile == null || !tile.IsHeightValid)
                return null;

            // Build Unity TerrainData with the downsampled heightfield.
            var td = new TerrainData();
            td.heightmapResolution = hfRes; // sets (hfRes × hfRes) internal grid
            td.size = new Vector3(TerrainWorldGrid.TILE_SIZE_M,
                                  tile.maxHeight - tile.minHeight,
                                  TerrainWorldGrid.TILE_SIZE_M);

            float[,] heights = BuildHeightfield(tile, hfRes);
            td.SetHeights(0, 0, heights);

            // Create host GameObject at the tile's world origin. HideAndDontSave: the host is a
            // transient runtime/edit-mode object — never serialize it into the scene (it would
            // accumulate on save and orphan across reloads) and keep it out of the hierarchy.
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
            var host = new GameObject($"{HOST_PREFIX}{tile.tileCoord.x}_{tile.tileCoord.y}");
            host.hideFlags = debugVisible ? HideFlags.DontSave : HideFlags.HideAndDontSave;
            host.transform.SetParent(parent, worldPositionStays: false);
            host.transform.localPosition = new Vector3(origin.x, tile.minHeight, origin.y);

            // Attach Terrain + TerrainCollider (the cheapest heightfield collider in Unity).
            var terrain   = host.AddComponent<Terrain>();
            terrain.terrainData = td;
            terrain.enabled = false; // collider-only; no rendering

            var col = host.AddComponent<TerrainCollider>();
            col.terrainData = td;

            return new Handle(td, host, tile.tileCoord);
        }

        // ── Heightfield resolution validation ─────────────────────────────────

        /// <summary>
        /// Rounds <paramref name="requested"/> UP to the nearest valid Unity heightmap resolution
        /// (2^n + 1, minimum 33) and logs a warning when it had to adjust. Unity's
        /// <c>TerrainData.heightmapResolution</c> throws if the value is not (2^n + 1).
        /// </summary>
        public static int NearestValidHeightfieldRes(int requested)
        {
            const int MIN_RES = 33;   // 2^5  + 1
            const int MAX_RES = 4097; // 2^12 + 1 (Unity's max heightmap resolution)
            int res = MIN_RES;
            while (res < requested && res < MAX_RES)
                res = (res - 1) * 2 + 1; // 33 → 65 → 129 → 257 → … → 4097

            if (res != requested)
                WpLog.Warning($"[TerrainColliderProvider] heightfieldRes {requested} is not " +
                                 $"(2^n + 1); using {res}.");
            return res;
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
