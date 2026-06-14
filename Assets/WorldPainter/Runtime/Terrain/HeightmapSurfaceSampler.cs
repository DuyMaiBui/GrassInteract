#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    /// <summary>
    /// <see cref="ISurfaceSampler"/> implementation over the custom heightmap terrain.
    ///
    /// <see cref="TrySample"/> delegates to <see cref="TerrainHeightSampleCpu"/> — the SAME SSOT
    /// decode path that the Phase 1 GPU VTF shader mirrors — so CPU scatter placement and
    /// GPU rendering return identical heights (risk-20 mitigation: no floating scatter).
    ///
    /// Resolves the tile via <see cref="TerrainWorldGrid.WorldToTileCoord"/>;
    /// derives the surface normal by central difference (SSOT with Phase 2);
    /// populates <see cref="SurfaceHit.SplatWeights"/> from the Phase 0 RGBA32 splat data.
    /// Returns <c>false</c> off-grid or when the tile is not registered/loaded.
    ///
    /// Lives in WorldPainter (not WorldPainter) — one-way reference: WorldPainter → WorldPainter.
    /// </summary>
    public sealed class HeightmapSurfaceSampler : ISurfaceSampler
    {
        // ── State ─────────────────────────────────────────────────────────────

        // Tile lookup delegate: coord → tile asset (or null = not resident).
        private readonly System.Func<Vector2Int, TerrainTileAsset?> tileResolver;

        // Step used for central-difference normal estimation (world metres).
        private const float NORMAL_STEP_M = 0.5f;

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Construct with a single tile (single-tile / demo mode).
        /// </summary>
        public HeightmapSurfaceSampler(TerrainTileAsset tile)
        {
            this.tileResolver = coord => coord == tile.tileCoord ? tile : null;
        }

        /// <summary>
        /// Construct with a resolver delegate (streaming mode — TerrainStreamingManager).
        /// The delegate is called per-sample; it must be thread-safe or run on main thread only.
        /// </summary>
        public HeightmapSurfaceSampler(System.Func<Vector2Int, TerrainTileAsset?> tileResolver)
        {
            this.tileResolver = tileResolver;
        }

        // ── ISurfaceSampler ───────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool TrySample(float worldX, float worldZ, out SurfaceHit hit)
        {
            hit = default;

            // Resolve the tile for this world position.
            Vector2Int coord = TerrainWorldGrid.WorldToTileCoord(worldX, worldZ);
            TerrainTileAsset? tile = this.tileResolver(coord);
            if (tile == null || !tile.IsHeightValid)
                return false;

            // Sample height at the query point (SSOT decode — matches GPU VTF).
            if (!TerrainHeightSampleCpu.TrySample(tile, worldX, worldZ, out float worldY))
                return false;

            hit.Y = worldY;

            // Central-difference normal (SSOT with Phase 2 normal reconstruction).
            hit.Normal = this.ComputeNormal(tile, worldX, worldZ, worldY);
            hit.SlopeDeg = Vector3.Angle(hit.Normal, Vector3.up);

            // (Phase 3 cleanup — SampleSplat removed alongside tile.splatData; SplatWeights stays null.)
            hit.SplatWeights = null;

            return true;
        }

        // ── Normal computation ────────────────────────────────────────────────

        private Vector3 ComputeNormal(TerrainTileAsset tile, float wx, float wz, float wy)
        {
            // Central difference along X and Z; fall back to sampled height when off-tile.
            float hPx = SampleOrFallback(tile, wx + NORMAL_STEP_M, wz, wy);
            float hNx = SampleOrFallback(tile, wx - NORMAL_STEP_M, wz, wy);
            float hPz = SampleOrFallback(tile, wx, wz + NORMAL_STEP_M, wy);
            float hNz = SampleOrFallback(tile, wx, wz - NORMAL_STEP_M, wy);

            // Tangent vectors in world space; cross product gives normal.
            var tangentX = new Vector3(NORMAL_STEP_M * 2f, hPx - hNx, 0f);
            var tangentZ = new Vector3(0f, hPz - hNz, NORMAL_STEP_M * 2f);
            return Vector3.Cross(tangentZ, tangentX).normalized;
        }

        private static float SampleOrFallback(TerrainTileAsset tile,
            float wx, float wz, float fallback)
        {
            // Stay on the same tile; no cross-tile sampling for normal (acceptable for 0.5m step).
            if (TerrainHeightSampleCpu.TrySample(tile, wx, wz, out float h))
                return h;
            return fallback;
        }

    }
}
