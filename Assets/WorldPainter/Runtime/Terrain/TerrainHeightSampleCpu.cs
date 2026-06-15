#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// CPU bilinear height sampler for a <see cref="TerrainTileAsset"/>.
    ///
    /// Implements the same decode math as the Phase 1 GPU VTF shader so CPU
    /// samples (Phase 4 HeightmapSurfaceSampler.TrySample, Phase 5 sculpt preview)
    /// and GPU renders return identical heights.
    ///
    /// ── SSOT decode path (mirrors HLSL) ──────────────────────────────────────
    /// 1. WorldXZ → texel UV via TerrainWorldGrid.WorldToTexelUV.
    /// 2. UV → continuous texel coordinate via TerrainWorldGrid.UVToTexelF.
    /// 3. Gather 4 neighbouring R16 samples; decode each via TerrainHeightFormat.DecodeHeight.
    /// 4. Bilinear interpolation with fractional part of the texel coordinate.
    ///
    /// The GPU does the same in a single Tex2D.SampleLevel call; CPU does it
    /// explicitly so the math is transparent and testable.
    /// </summary>
    public static class TerrainHeightSampleCpu
    {
        /// <summary>
        /// Sample the height at world XZ from a tile's R16 heightData.
        /// Returns false if the world position is outside the tile's bounds.
        /// </summary>
        /// <param name="tile">Source tile asset (must be height-valid).</param>
        /// <param name="worldX">World-space X coordinate.</param>
        /// <param name="worldZ">World-space Z coordinate.</param>
        /// <param name="worldY">Output: interpolated world-space Y height.</param>
        /// <returns>True if the position is within the tile and was sampled; false otherwise.</returns>
        public static bool TrySample(TerrainTileAsset tile, float worldX, float worldZ,
            out float worldY)
        {
            worldY = 0f;

            if (tile == null || !tile.IsHeightValid) return false;

            // ── Step 1: world → UV within the tile ───────────────────────────
            Vector2Int coord = TerrainWorldGrid.WorldToTileCoord(worldX, worldZ);
            if (coord != tile.tileCoord) return false; // wrong tile

            Vector2 uv = TerrainWorldGrid.WorldToTexelUV(worldX, worldZ, tile.tileCoord);
            // Guard: UV must be in [0, 1] (point is inside this tile).
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return false;

            // ── Step 2: UV → continuous texel coordinate ─────────────────────
            float tf_x = TerrainWorldGrid.UVToTexelF(uv.x, tile.heightRes);
            float tf_z = TerrainWorldGrid.UVToTexelF(uv.y, tile.heightRes);

            int x0 = Mathf.Clamp(Mathf.FloorToInt(tf_x), 0, tile.heightRes - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(tf_z), 0, tile.heightRes - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, tile.heightRes - 1);
            int z1 = Mathf.Clamp(z0 + 1, 0, tile.heightRes - 1);

            float fx = tf_x - x0; // fractional [0,1)
            float fz = tf_z - z0;

            // ── Step 3: gather 4 R16 samples, decode each (SSOT decode) ──────
            float h00 = SampleDecoded(tile, x0, z0);
            float h10 = SampleDecoded(tile, x1, z0);
            float h01 = SampleDecoded(tile, x0, z1);
            float h11 = SampleDecoded(tile, x1, z1);

            // ── Step 4: bilinear interpolation ────────────────────────────────
            float h0 = Mathf.LerpUnclamped(h00, h10, fx);
            float h1 = Mathf.LerpUnclamped(h01, h11, fx);
            worldY = Mathf.LerpUnclamped(h0, h1, fz);

            return true;
        }

        /// <summary>
        /// Sample directly at the tile containing worldXZ, without requiring the
        /// caller to know tileCoord. Returns false if worldXZ falls outside the tile.
        /// </summary>
        public static bool TrySampleWorld(TerrainTileAsset tile, float worldX, float worldZ,
            out float worldY)
        {
            return TrySample(tile, worldX, worldZ, out worldY);
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Read and decode the height at texel (x, z) using the SSOT decode formula.
        /// Row-major layout: index = z * heightRes + x.
        /// </summary>
        private static float SampleDecoded(TerrainTileAsset tile, int x, int z)
        {
            int texelIndex = z * tile.heightRes + x;
            ushort raw = TerrainHeightFormat.ReadRaw(tile.heightData, texelIndex);
            return TerrainHeightFormat.DecodeHeight(raw, tile.minHeight, tile.maxHeight);
        }
    }
}
