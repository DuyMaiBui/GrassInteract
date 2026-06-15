#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Pure function: camera world position + a LOD range (metres) → set of tileCoords whose
    /// nearest edge is within that range, i.e. the tiles that should have a physics collider.
    ///
    /// Unlike <see cref="TerrainResidencyRing"/> (a fixed Chebyshev tile-unit ring used by streaming),
    /// this ring is metric: the collider footprint is derived from a chosen WorldPainter LOD band's
    /// distance (e.g. 256 m ≈ a 3×3 ring around the camera tile), so collider coverage tracks the
    /// renderer's LOD configuration rather than a separate magic radius.
    ///
    /// Stateless and deterministic — safe to call from tests without a Unity scene.
    /// </summary>
    public static class TerrainColliderRing
    {
        /// <summary>
        /// Euclidean XZ distance from the camera to the nearest point of the tile's world footprint.
        /// Returns 0 when the camera is inside the tile's XZ bounds.
        /// </summary>
        public static float NearestEdgeDistance(Vector3 cameraWorldPos, Vector2Int tileCoord)
        {
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(tileCoord);
            float minX = origin.x;
            float maxX = origin.x + TerrainWorldGrid.TILE_SIZE_M;
            float minZ = origin.y;
            float maxZ = origin.y + TerrainWorldGrid.TILE_SIZE_M;

            float clampedX = Mathf.Clamp(cameraWorldPos.x, minX, maxX);
            float clampedZ = Mathf.Clamp(cameraWorldPos.z, minZ, maxZ);

            float dx = cameraWorldPos.x - clampedX;
            float dz = cameraWorldPos.z - clampedZ;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Returns the set of tileCoords whose nearest edge is within <paramref name="lodRangeMetres"/>
        /// of the camera. Empty when the range is ≤ 0 (no bands configured / colliders disabled).
        /// </summary>
        public static HashSet<Vector2Int> ComputeDesired(Vector3 cameraWorldPos, float lodRangeMetres)
        {
            var result = new HashSet<Vector2Int>();
            if (lodRangeMetres <= 0f) return result;

            Vector2Int centre = TerrainWorldGrid.WorldToTileCoord(cameraWorldPos.x, cameraWorldPos.z);
            // +1 guards the sub-tile camera position (the nearest edge can reach one extra tile
            // when the camera sits near a tile boundary). NearestEdgeDistance does the exact test.
            int searchTiles = Mathf.CeilToInt(lodRangeMetres / TerrainWorldGrid.TILE_SIZE_M) + 1;

            for (int dz = -searchTiles; dz <= searchTiles; ++dz)
                for (int dx = -searchTiles; dx <= searchTiles; ++dx)
                {
                    var coord = new Vector2Int(centre.x + dx, centre.y + dz);
                    if (NearestEdgeDistance(cameraWorldPos, coord) <= lodRangeMetres)
                        result.Add(coord);
                }

            return result;
        }
    }
}
