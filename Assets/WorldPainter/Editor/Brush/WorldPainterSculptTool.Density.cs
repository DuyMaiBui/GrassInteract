#nullable enable
using System.Collections.Generic;
using WorldPainter;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Density RT lifecycle half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// Manages per-tile <see cref="RenderTexture"/>s used by the <c>PaintDensity</c> compute
    /// kernel. Each tile coord gets its own RT seeded from that tile's committed density
    /// <see cref="Texture2D"/>. RTs are released on <see cref="TeardownActiveStroke"/>.
    ///
    /// For the legacy DensityScatterLayer (no tile), coord == null is used as the dict key
    /// via a sentinel Vector2Int.
    ///
    /// Density writeback is driven by <see cref="WorldPainterDensityEncoder"/> on the same
    /// 0.15s async pipeline + mouse-up sync flush as <see cref="TerrainSculptRtWriteback"/>.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // ── Coord-keyed density RT cache ──────────────────────────────────────
        // key: tile coord (or LEGACY_COORD for the single-map legacy path)
        // value: (RT, committed target Texture2D)

        private static readonly Vector2Int LEGACY_COORD = new Vector2Int(int.MinValue, int.MinValue);

        private readonly Dictionary<Vector2Int, (RenderTexture rt, Texture2D target)>
            densityRtCache = new();

        // ── Legacy single-RT references (kept for TeardownActiveStroke compat) ──
        // These now forward to the legacy dict entry when non-null.

        internal RenderTexture? densityRT        => this.GetCachedRT(LEGACY_COORD);
        internal Texture2D?     activeDensityMap => this.GetCachedTarget(LEGACY_COORD);

        private RenderTexture? GetCachedRT(Vector2Int coord)
            => this.densityRtCache.TryGetValue(coord, out var e) ? e.rt : null;

        private Texture2D? GetCachedTarget(Vector2Int coord)
            => this.densityRtCache.TryGetValue(coord, out var e) ? e.target : null;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns (or allocates) the density RT for <paramref name="densityMap"/> at
        /// <paramref name="coord"/>. Pass coord = null for the legacy single-map path
        /// (stored under <see cref="LEGACY_COORD"/>).
        /// </summary>
        internal RenderTexture? GetOrCreateDensityRT(Texture2D densityMap, Vector2Int? coord)
        {
            Vector2Int key = coord ?? LEGACY_COORD;

            if (this.densityRtCache.TryGetValue(key, out var cached))
            {
                // Reuse existing RT if the same texture is still active for this coord.
                if (ReferenceEquals(cached.target, densityMap))
                    return cached.rt;

                // Target changed — release old RT for this coord.
                this.ReleaseDensityRT(key);
            }

            int res = TerrainSculptConfig.BRUSH_RT_RES;
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
            {
                name = $"WorldPainterDensityRT_{densityMap.name}",
                enableRandomWrite = true,
            };
            rt.Create();

            // Seed from the committed density map so edits continue from the saved state.
            Graphics.Blit(densityMap, rt);

            this.densityRtCache[key] = (rt, densityMap);
            return rt;
        }

        // ── Flush all per-tile RTs on stroke-end (sync) ───────────────────────

        internal void FlushAllDensityRTs()
        {
            // Persist ONLY tiles actually painted this stroke. Seam-aware Smooth seeds a straddled
            // neighbour's density RT as a READ-ONLY blur source (DensityDispatch.BindDensityNeighbors)
            // — those coords never enter strokeTouchedCoords. Flushing them would rewrite an unpainted
            // neighbour with a lossy resampled round-trip and no undo entry, so skip them. LEGACY_COORD
            // (single-map path) is always persisted since it has no per-tile coord to match.
            foreach (var kv in this.densityRtCache)
                if (kv.Key == LEGACY_COORD || this.strokeTouchedCoords.Contains(kv.Key))
                    this.densityEncoder.ExecuteSync(kv.Value.target, kv.Value.rt);
        }

        // ── Release ───────────────────────────────────────────────────────────

        internal void ReleaseDensityRT(Vector2Int key)
        {
            if (!this.densityRtCache.TryGetValue(key, out var entry)) return;
            if (RenderTexture.active == entry.rt) RenderTexture.active = null;
            entry.rt.Release();
            Object.DestroyImmediate(entry.rt);
            this.densityRtCache.Remove(key);
        }

        internal void ReleaseAllDensityRTs()
        {
            foreach (var kv in this.densityRtCache)
            {
                if (RenderTexture.active == kv.Value.rt) RenderTexture.active = null;
                kv.Value.rt.Release();
                Object.DestroyImmediate(kv.Value.rt);
            }
            this.densityRtCache.Clear();
        }

        // ── Legacy compat shims (called by TeardownActiveStroke) ─────────────

        internal void ReleaseDensityRT()       => this.ReleaseAllDensityRTs();
    }
}
