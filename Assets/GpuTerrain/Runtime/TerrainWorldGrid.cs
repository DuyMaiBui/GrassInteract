#nullable enable
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// World XZ ↔ tile coordinate ↔ texel UV mapping for the terrain tile grid.
    ///
    /// ── Tile layout ──────────────────────────────────────────────────────────
    /// Each tile covers a square region of TILE_SIZE_M × TILE_SIZE_M metres in world XZ.
    /// Tile (0,0) has its min corner at world origin (0, 0).
    /// Tile (tx, tz) min corner = (tx * TILE_SIZE_M, tz * TILE_SIZE_M) in world XZ.
    ///
    /// ── 1-texel shared-edge (skirt/overlap) convention ───────────────────────
    /// Each tile's height texture stores (heightRes × heightRes) texels covering
    /// [tileMinX, tileMinX + TILE_SIZE_M] in X and [tileMinZ, tileMinZ + TILE_SIZE_M] in Z.
    ///
    /// The LAST row/column of texels coincides with the FIRST row/column of the
    /// adjacent tile — i.e. adjacent tiles share the boundary edge texel. This means:
    ///   - A tile of heightRes=R covers R-1 "cell intervals" across TILE_SIZE_M metres.
    ///   - Texel UV step = TILE_SIZE_M / (heightRes - 1) metres per texel.
    ///   - At a tile boundary (worldX == tileMaxX), UV = 1.0 on the current tile AND
    ///     UV = 0.0 on the adjacent tile — both evaluate to the same height (shared texel).
    ///
    /// WHY: Phase 1 CDLOD vertex morph blends between LOD levels at tile edges.
    ///      Phase 2 normal reconstruction reads adjacent texels across tile boundaries.
    ///      A shared edge texel makes both seamless — no seam gap, no duplicate samples.
    ///
    /// Implication for authoring (Phase 5): when a brush stroke crosses a tile boundary,
    /// the shared edge texel must be written identically into both neighbouring tiles.
    /// </summary>
    public static class TerrainWorldGrid
    {
        // ── Grid constants ────────────────────────────────────────────────────

        /// <summary>Side length of one square terrain tile in world metres.</summary>
        public const float TILE_SIZE_M = 256f;

        /// <summary>
        /// Default height texture resolution per tile edge (texels).
        /// Actual step = TILE_SIZE_M / (DEFAULT_HEIGHT_RES - 1) = 256 / 255 ≈ 1.004 m/texel.
        /// This can be overridden per-tile via TerrainTileAsset.heightRes.
        /// </summary>
        public const int DEFAULT_HEIGHT_RES = 257; // power-of-2 + 1 for exact shared-edge alignment

        /// <summary>
        /// Default splat texture resolution per tile edge (texels).
        /// Lower resolution than height; phase 2 bilinear-samples this at fragment.
        /// </summary>
        public const int DEFAULT_SPLAT_RES = 512;

        // ── Coordinate mapping ────────────────────────────────────────────────

        /// <summary>
        /// Convert world XZ to tile coordinates (integer tile index).
        /// Floors to the containing tile; does NOT clamp — caller must validate.
        /// </summary>
        /// <param name="worldX">World-space X.</param>
        /// <param name="worldZ">World-space Z.</param>
        /// <returns>Integer tile coordinate; may be negative for worlds below origin.</returns>
        public static Vector2Int WorldToTileCoord(float worldX, float worldZ)
        {
            int tx = Mathf.FloorToInt(worldX / TILE_SIZE_M);
            int tz = Mathf.FloorToInt(worldZ / TILE_SIZE_M);
            return new Vector2Int(tx, tz);
        }

        /// <summary>
        /// World-space XZ position of the min corner (bottom-left) of the tile at tileCoord.
        /// </summary>
        public static Vector2 TileOriginWorld(Vector2Int tileCoord)
        {
            return new Vector2(tileCoord.x * TILE_SIZE_M, tileCoord.y * TILE_SIZE_M);
        }

        /// <summary>
        /// Convert world XZ to a texel UV within the tile that contains the point.
        /// UV is in [0, 1]; 0 = tile min edge, 1 = tile max edge (shared with adjacent tile).
        ///
        /// The UV is NOT clamped — points outside [0,1] are on an adjacent tile.
        /// Phase 1/2 should use the containing tile's UV; for cross-boundary reads
        /// the caller must resolve the adjacent tile.
        /// </summary>
        /// <param name="worldX">World-space X.</param>
        /// <param name="worldZ">World-space Z.</param>
        /// <param name="tileCoord">The tile to compute UV within.</param>
        /// <returns>UV in [0,1] (unclamped).</returns>
        public static Vector2 WorldToTexelUV(float worldX, float worldZ, Vector2Int tileCoord)
        {
            Vector2 origin = TileOriginWorld(tileCoord);
            float u = (worldX - origin.x) / TILE_SIZE_M;
            float v = (worldZ - origin.y) / TILE_SIZE_M;
            return new Vector2(u, v);
        }

        /// <summary>
        /// Convert a texel UV in [0,1] to an integer texel coordinate within a tile,
        /// clamped to [0, heightRes-1].
        ///
        /// Uses the shared-edge convention: UV=1.0 maps to texel index (heightRes-1),
        /// which is the last texel — shared with the adjacent tile's texel 0.
        /// </summary>
        /// <param name="uv">UV in [0,1].</param>
        /// <param name="heightRes">Tile height resolution (texel count per edge).</param>
        /// <returns>Clamped integer texel coordinate in [0, heightRes-1].</returns>
        public static int UVToTexelCoord(float uv, int heightRes)
        {
            // UV=0 → texel 0; UV=1 → texel (heightRes-1): shared-edge texel.
            float fCoord = uv * (heightRes - 1);
            return Mathf.Clamp(Mathf.FloorToInt(fCoord), 0, heightRes - 1);
        }

        /// <summary>
        /// Convert a texel UV in [0,1] to a continuous (fractional) texel coordinate.
        /// Used by bilinear sampling in TerrainHeightSampleCpu.
        /// </summary>
        /// <param name="uv">UV in [0,1].</param>
        /// <param name="heightRes">Tile height resolution.</param>
        /// <returns>Continuous texel coordinate in [0, heightRes-1].</returns>
        public static float UVToTexelF(float uv, int heightRes)
        {
            return uv * (heightRes - 1);
        }
    }
}
