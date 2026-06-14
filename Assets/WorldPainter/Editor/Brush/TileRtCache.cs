#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Small per-coord RenderTexture cache for a single brush stroke.
    ///
    /// Manages one RFloat height RT per tile coord. Created lazily on first touch;
    /// seeded from the tile's committed height texture at creation. Capped at
    /// <see cref="MAX_ENTRIES"/> (maximum tiles a brush circle can overlap). All RTs
    /// released by <see cref="ReleaseAll"/>.
    ///
    /// Splat / alphamap RTs are owned by their respective per-encoder caches
    /// (<see cref="WorldPainterSculptTool"/>.alphamapRtCache) — Phase 3 cleanup
    /// dropped this class' old splatRT dictionary.
    /// </summary>
    internal sealed class TileRtCache
    {
        internal const int MAX_ENTRIES = 4;

        private readonly Dictionary<Vector2Int, RenderTexture> heightRTs =
            new Dictionary<Vector2Int, RenderTexture>(MAX_ENTRIES);

        internal int Count => this.heightRTs.Count;

        internal bool TryGet(Vector2Int coord, out RenderTexture heightRT)
        {
            return this.heightRTs.TryGetValue(coord, out heightRT!);
        }

        /// <summary>
        /// Get or create the height RT for coord. Seeds from gpu.HeightTexture on creation.
        /// Returns false if the cap would be exceeded and this is a new coord.
        /// </summary>
        internal bool GetOrCreate(Vector2Int coord, TerrainTileGpuResources gpu,
            out RenderTexture heightRT)
        {
            bool existing = this.heightRTs.TryGetValue(coord, out heightRT!);
            if (existing) return true;

            if (this.heightRTs.Count >= MAX_ENTRIES)
            {
                Debug.LogWarning(
                    $"[TileRtCache] Cap ({MAX_ENTRIES}) reached; skipping coord {coord}.");
                return false;
            }

            int res = TerrainSculptConfig.BRUSH_RT_RES;

            heightRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
                { name = $"TerrainSculptHeightRT_{coord}", enableRandomWrite = true };
            heightRT.Create();
            this.heightRTs[coord] = heightRT;

            // Seed from committed texture so brush edits start from current state.
            if (gpu.HeightTexture != null)
                Graphics.Blit(gpu.HeightTexture, heightRT);

            return true;
        }

        /// <summary>Release and destroy all cached RTs.</summary>
        internal void ReleaseAll()
        {
            foreach (var kv in this.heightRTs) { kv.Value.Release(); Object.DestroyImmediate(kv.Value); }
            this.heightRTs.Clear();
        }
    }
}
