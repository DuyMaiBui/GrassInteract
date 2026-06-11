#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Maps a world-space brush (center + radius) to the set of affected tile coordinates.
    ///
    /// Uses Phase 0 <see cref="TerrainWorldGrid"/> for world→tile mapping.
    /// For cross-tile strokes the brush can overlap up to 4 neighbouring tiles —
    /// all tiles whose world AABB intersects the brush circle are returned.
    ///
    /// Phase 3 resident-set is optional: if non-null only tiles that are currently
    /// resident are returned (avoids loading new tiles mid-stroke).
    /// </summary>
    public static class TerrainPaintTargetResolver
    {
        /// <summary>
        /// Collect all tile coordinates whose world extent overlaps the brush circle.
        /// Results are appended to <paramref name="results"/> (not cleared).
        /// </summary>
        /// <param name="worldCenter">Brush center in world XZ.</param>
        /// <param name="radiusM">Brush radius in world metres.</param>
        /// <param name="residencySet">
        /// Optional Phase 3 residency set. When non-null, only resident tiles are included.
        /// Pass null to return all geometrically overlapping tiles.
        /// </param>
        /// <param name="results">Output list; entries appended (caller clears if needed).</param>
        public static void Resolve(
            Vector2 worldCenter,
            float radiusM,
            TerrainTileResidencySet? residencySet,
            List<Vector2Int> results)
        {
            // Compute AABB of the brush circle in world XZ.
            float minX = worldCenter.x - radiusM;
            float maxX = worldCenter.x + radiusM;
            float minZ = worldCenter.y - radiusM;
            float maxZ = worldCenter.y + radiusM;

            // Tile coord range that can possibly overlap.
            int tileMinX = Mathf.FloorToInt(minX / TerrainWorldGrid.TILE_SIZE_M);
            int tileMaxX = Mathf.FloorToInt(maxX / TerrainWorldGrid.TILE_SIZE_M);
            int tileMinZ = Mathf.FloorToInt(minZ / TerrainWorldGrid.TILE_SIZE_M);
            int tileMaxZ = Mathf.FloorToInt(maxZ / TerrainWorldGrid.TILE_SIZE_M);

            for (int tz = tileMinZ; tz <= tileMaxZ; ++tz)
            {
                for (int tx = tileMinX; tx <= tileMaxX; ++tx)
                {
                    var coord = new Vector2Int(tx, tz);
                    if (residencySet != null && !residencySet.Contains(coord))
                        continue;
                    results.Add(coord);
                }
            }
        }

        /// <summary>
        /// Compute brush center UV and radius UV within a specific tile.
        /// UV = 0 at tile min corner, 1 at tile max corner.
        /// </summary>
        /// <param name="worldCenter">Brush center XZ in world space.</param>
        /// <param name="radiusM">Brush radius in metres.</param>
        /// <param name="tileCoord">Target tile coordinate.</param>
        /// <param name="centerUV">Output: brush center UV within the tile.</param>
        /// <param name="radiusUV">Output: brush radius in UV space.</param>
        public static void WorldBrushToTileUV(
            Vector2 worldCenter,
            float radiusM,
            Vector2Int tileCoord,
            out Vector2 centerUV,
            out float radiusUV)
        {
            centerUV = TerrainWorldGrid.WorldToTexelUV(worldCenter.x, worldCenter.y, tileCoord);
            radiusUV = radiusM / TerrainWorldGrid.TILE_SIZE_M;
        }
    }
}
