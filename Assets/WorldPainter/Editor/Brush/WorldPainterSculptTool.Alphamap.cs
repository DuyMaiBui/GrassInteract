#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Phase 2b: per-tile alphamap RT lifecycle for the TerrainPalette brush.
    /// Direct port of the density partial — manages per-(coord, alphamap-index)
    /// RenderTextures used by the <c>PaintAlphamap</c> compute kernel. Each entry
    /// is seeded from the matching <see cref="TerrainTileAsset.Alphamaps"/> texture
    /// so brush edits start from the saved state.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // key  : (coord, alphamap-index)  →  RT + bound target Texture2D
        // value: (RT, committed target Texture2D)
        private readonly Dictionary<(Vector2Int coord, int alphaIdx), (RenderTexture rt, Texture2D target)>
            alphamapRtCache = new();

        /// <summary>
        /// Returns (or allocates) the alphamap RT for <paramref name="alphamapTex"/> at
        /// <paramref name="coord"/>:<paramref name="alphamapIndex"/>. Seeds the RT from
        /// the committed texture on first allocation so edits continue from the saved state
        /// (matches the density-RT contract).
        /// </summary>
        internal RenderTexture? GetOrCreateAlphamapRT(Texture2D alphamapTex, Vector2Int coord, int alphamapIndex)
        {
            var key = (coord, alphamapIndex);
            if (this.alphamapRtCache.TryGetValue(key, out var cached))
            {
                if (ReferenceEquals(cached.target, alphamapTex))
                    return cached.rt;

                // Target swapped out (e.g. tile alphamaps reallocated) — drop the stale RT.
                this.ReleaseAlphamapRT(key);
            }

            int res = TerrainSculptConfig.BRUSH_RT_RES;
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
            {
                name = $"WorldPainterAlphamapRT_{alphamapTex.name}",
                enableRandomWrite = true,
            };
            rt.Create();

            // Seed from the committed alphamap so brush edits start from the saved state.
            Graphics.Blit(alphamapTex, rt);

            this.alphamapRtCache[key] = (rt, alphamapTex);
            return rt;
        }

        /// <summary>Sync-flush every cached alphamap RT into its target texture (call on mouse-up).</summary>
        internal void FlushAllAlphamapRTs()
        {
            foreach (var kv in this.alphamapRtCache)
                this.alphamapEncoder.ExecuteSync(kv.Value.target, kv.Value.rt);
        }

        /// <summary>
        /// Restore the committed Texture2D alphamap binding on every tile/slot we live-bound
        /// during the stroke. Mirrors EndSculptPreview for height — runs after the sync flush
        /// so the shader picks up the just-written final pixels.
        /// </summary>
        internal void RestoreAllAlphamapBindings(WorldPainter? painter)
        {
            if (painter == null) return;
            foreach (var kv in this.alphamapRtCache)
                painter.EndAlphamapPreview(kv.Key.coord, kv.Key.alphaIdx);
        }

        internal void ReleaseAlphamapRT((Vector2Int coord, int alphaIdx) key)
        {
            if (!this.alphamapRtCache.TryGetValue(key, out var entry)) return;
            if (RenderTexture.active == entry.rt) RenderTexture.active = null;
            entry.rt.Release();
            Object.DestroyImmediate(entry.rt);
            this.alphamapRtCache.Remove(key);
        }

        internal void ReleaseAllAlphamapRTs()
        {
            foreach (var kv in this.alphamapRtCache)
            {
                if (RenderTexture.active == kv.Value.rt) RenderTexture.active = null;
                kv.Value.rt.Release();
                Object.DestroyImmediate(kv.Value.rt);
            }
            this.alphamapRtCache.Clear();
        }
    }
}
