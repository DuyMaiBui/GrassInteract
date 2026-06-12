#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Pure function: camera world position → set of tileCoords that should be resident.
    ///
    /// Uses a Chebyshev (Chessboard) ring of <see cref="TerrainStreamingConfig.RING_RADIUS"/>
    /// tile units, so a 5×5 block of tiles surrounds the camera at radius 2.
    ///
    /// Load ring: RING_RADIUS.
    /// Evict threshold: RING_RADIUS + HYSTERESIS_TILES — avoids boundary thrash.
    ///
    /// Stateless and deterministic — safe to call from tests without Unity context.
    /// </summary>
    public static class TerrainResidencyRing
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the set of tileCoords that should be loaded for the given camera position.
        /// Uses RING_RADIUS from TerrainStreamingConfig.
        /// </summary>
        public static HashSet<Vector2Int> ComputeDesired(Vector3 cameraWorldPos)
        {
            return ComputeDesired(cameraWorldPos, TerrainStreamingConfig.RING_RADIUS);
        }

        /// <summary>
        /// Returns the set of tileCoords that should be loaded, with an explicit radius.
        /// The set size is always (2*radius+1)².
        /// </summary>
        public static HashSet<Vector2Int> ComputeDesired(Vector3 cameraWorldPos, int radius)
        {
            Vector2Int centre = TerrainWorldGrid.WorldToTileCoord(cameraWorldPos.x, cameraWorldPos.z);
            var result = new HashSet<Vector2Int>();
            for (int dz = -radius; dz <= radius; ++dz)
                for (int dx = -radius; dx <= radius; ++dx)
                    result.Add(new Vector2Int(centre.x + dx, centre.y + dz));
            return result;
        }

        /// <summary>
        /// Returns true if the given tileCoord falls within the eviction threshold
        /// (RING_RADIUS + HYSTERESIS_TILES) for the camera's current tile.
        /// A tile is kept resident as long as this returns true.
        /// </summary>
        public static bool IsWithinEvictThreshold(Vector2Int tileCoord, Vector3 cameraWorldPos)
        {
            int evictRadius = TerrainStreamingConfig.RING_RADIUS +
                              TerrainStreamingConfig.HYSTERESIS_TILES;
            Vector2Int centre = TerrainWorldGrid.WorldToTileCoord(cameraWorldPos.x, cameraWorldPos.z);
            int dx = Mathf.Abs(tileCoord.x - centre.x);
            int dz = Mathf.Abs(tileCoord.y - centre.y);
            // Chebyshev distance
            return Mathf.Max(dx, dz) <= evictRadius;
        }
    }
}
