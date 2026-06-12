#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Small per-coord RenderTexture cache for a single brush stroke.
    ///
    /// Manages one RFloat height RT and one ARGBFloat splat RT per tile coord.
    /// Created lazily on first touch; seeded from the tile's committed Texture2D at creation.
    /// Capped at <see cref="MAX_ENTRIES"/> (maximum tiles a brush circle can overlap).
    /// All RTs released by <see cref="ReleaseAll"/>.
    /// </summary>
    internal sealed class TileRtCache
    {
        internal const int MAX_ENTRIES = 4;

        private readonly Dictionary<Vector2Int, RenderTexture> heightRTs =
            new Dictionary<Vector2Int, RenderTexture>(MAX_ENTRIES);
        private readonly Dictionary<Vector2Int, RenderTexture> splatRTs =
            new Dictionary<Vector2Int, RenderTexture>(MAX_ENTRIES);

        internal int Count => this.heightRTs.Count;

        internal bool TryGet(Vector2Int coord,
            out RenderTexture heightRT, out RenderTexture splatRT)
        {
            if (this.heightRTs.TryGetValue(coord, out heightRT!) &&
                this.splatRTs.TryGetValue(coord, out splatRT!))
                return true;
            splatRT = null!;
            return false;
        }

        /// <summary>
        /// Get or create RTs for coord. Seeds height RT from gpu.HeightTexture on creation.
        /// Returns false if the cap would be exceeded and this is a new coord.
        /// </summary>
        internal bool GetOrCreate(Vector2Int coord, TerrainTileGpuResources gpu,
            out RenderTexture heightRT, out RenderTexture splatRT)
        {
            bool existing = this.heightRTs.TryGetValue(coord, out heightRT!);

            if (!existing)
            {
                if (this.heightRTs.Count >= MAX_ENTRIES)
                {
                    Debug.LogWarning(
                        $"[TileRtCache] Cap ({MAX_ENTRIES}) reached; skipping coord {coord}.");
                    splatRT = null!;
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
            }

            if (!this.splatRTs.TryGetValue(coord, out splatRT!))
            {
                int res = TerrainSculptConfig.BRUSH_RT_RES;
                splatRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
                    { name = $"TerrainSculptSplatRT_{coord}", enableRandomWrite = true };
                splatRT.Create();
                this.splatRTs[coord] = splatRT;
            }

            return true;
        }

        /// <summary>Release and destroy all cached RTs.</summary>
        internal void ReleaseAll()
        {
            foreach (var kv in this.heightRTs) { kv.Value.Release(); Object.DestroyImmediate(kv.Value); }
            this.heightRTs.Clear();
            foreach (var kv in this.splatRTs)  { kv.Value.Release(); Object.DestroyImmediate(kv.Value); }
            this.splatRTs.Clear();
        }
    }
}
