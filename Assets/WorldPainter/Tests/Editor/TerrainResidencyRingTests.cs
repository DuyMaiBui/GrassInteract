#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for TerrainResidencyRing:
    ///   - Desired set size is always (2*radius+1)².
    ///   - Camera tile is always in the desired set.
    ///   - Corner and edge coords are present for the given radius.
    ///   - Radius 0 returns exactly 1 tile.
    ///   - Deterministic: two calls with the same position return equal sets.
    ///   - IsWithinEvictThreshold respects RING_RADIUS + HYSTERESIS_TILES.
    /// </summary>
    [TestFixture]
    public sealed class TerrainResidencyRingTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M; // 256

        // ── Desired set size ──────────────────────────────────────────────────

        [Test]
        public void ComputeDesired_DefaultRadius_Returns5x5Set()
        {
            var pos  = new Vector3(TILE * 2.5f, 0f, TILE * 2.5f); // tile (2,2)
            var set  = TerrainResidencyRing.ComputeDesired(pos);
            int expected = (2 * TerrainStreamingConfig.RING_RADIUS + 1);
            expected *= expected; // 5*5 = 25
            Assert.AreEqual(expected, set.Count,
                $"Expected {expected} tiles for RING_RADIUS={TerrainStreamingConfig.RING_RADIUS}.");
        }

        [Test]
        public void ComputeDesired_Radius0_ReturnsSingleTile()
        {
            var pos = new Vector3(TILE * 1.5f, 0f, TILE * 1.5f); // tile (1,1)
            var set = TerrainResidencyRing.ComputeDesired(pos, radius: 0);
            Assert.AreEqual(1, set.Count, "Radius 0 should return exactly 1 tile.");
        }

        [Test]
        public void ComputeDesired_Radius1_Returns3x3Set()
        {
            var pos = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f); // tile (0,0)
            var set = TerrainResidencyRing.ComputeDesired(pos, radius: 1);
            Assert.AreEqual(9, set.Count, "Radius 1 should return 9 tiles (3×3).");
        }

        // ── Camera tile membership ────────────────────────────────────────────

        [Test]
        public void ComputeDesired_CameraTile_AlwaysIncluded()
        {
            var pos    = new Vector3(TILE * 3.7f, 10f, TILE * 5.1f);
            var set    = TerrainResidencyRing.ComputeDesired(pos);
            var centre = TerrainWorldGrid.WorldToTileCoord(pos.x, pos.z);
            Assert.IsTrue(set.Contains(centre),
                $"Camera tile {centre} must always be in desired set.");
        }

        // ── Corner membership ─────────────────────────────────────────────────

        [Test]
        public void ComputeDesired_CornerCoords_ArePresent()
        {
            int r   = TerrainStreamingConfig.RING_RADIUS;
            var pos = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f); // tile (5,5)
            var set = TerrainResidencyRing.ComputeDesired(pos);
            var c   = TerrainWorldGrid.WorldToTileCoord(pos.x, pos.z); // (5,5)

            Assert.IsTrue(set.Contains(new Vector2Int(c.x - r, c.y - r)), "SW corner missing.");
            Assert.IsTrue(set.Contains(new Vector2Int(c.x + r, c.y - r)), "SE corner missing.");
            Assert.IsTrue(set.Contains(new Vector2Int(c.x - r, c.y + r)), "NW corner missing.");
            Assert.IsTrue(set.Contains(new Vector2Int(c.x + r, c.y + r)), "NE corner missing.");
        }

        // ── Determinism ───────────────────────────────────────────────────────

        [Test]
        public void ComputeDesired_SamePosition_ReturnsSameSet()
        {
            var pos  = new Vector3(TILE * 2.5f, 5f, TILE * 7.5f);
            var setA = TerrainResidencyRing.ComputeDesired(pos);
            var setB = TerrainResidencyRing.ComputeDesired(pos);
            Assert.IsTrue(setA.SetEquals(setB), "Same position must produce equal desired sets.");
        }

        // ── Camera move shifts ring ───────────────────────────────────────────

        [Test]
        public void ComputeDesired_CameraMoveOneTile_ShiftsSetByOne()
        {
            var posA = new Vector3(TILE * 2.5f, 0f, TILE * 2.5f); // tile (2,2)
            var posB = new Vector3(TILE * 3.5f, 0f, TILE * 2.5f); // tile (3,2)
            var setA = TerrainResidencyRing.ComputeDesired(posA);
            var setB = TerrainResidencyRing.ComputeDesired(posB);

            // A-only coords: left column of ring A not in ring B
            var onlyA = new HashSet<Vector2Int>(setA);
            onlyA.ExceptWith(setB);
            // B-only coords: right column of ring B not in ring A
            var onlyB = new HashSet<Vector2Int>(setB);
            onlyB.ExceptWith(setA);

            int diameter = 2 * TerrainStreamingConfig.RING_RADIUS + 1;
            Assert.AreEqual(diameter, onlyA.Count, $"Moving one tile X should drop {diameter} coords.");
            Assert.AreEqual(diameter, onlyB.Count, $"Moving one tile X should add {diameter} coords.");
        }

        // ── Evict threshold ───────────────────────────────────────────────────

        [Test]
        public void IsWithinEvictThreshold_TileAtRadius_ReturnsTrue()
        {
            var pos    = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f); // centre (5,5)
            var centre = TerrainWorldGrid.WorldToTileCoord(pos.x, pos.z);
            int r      = TerrainStreamingConfig.RING_RADIUS;
            var edge   = new Vector2Int(centre.x + r, centre.y);

            Assert.IsTrue(TerrainResidencyRing.IsWithinEvictThreshold(edge, pos),
                "Tile exactly at RING_RADIUS should be within evict threshold (has hysteresis).");
        }

        [Test]
        public void IsWithinEvictThreshold_TileAtRadiusPlusHysteresis_ReturnsTrue()
        {
            var pos    = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f);
            var centre = TerrainWorldGrid.WorldToTileCoord(pos.x, pos.z);
            int margin = TerrainStreamingConfig.RING_RADIUS +
                         TerrainStreamingConfig.HYSTERESIS_TILES;
            var tile   = new Vector2Int(centre.x + margin, centre.y);

            Assert.IsTrue(TerrainResidencyRing.IsWithinEvictThreshold(tile, pos),
                "Tile at RING_RADIUS + HYSTERESIS should still be within evict threshold.");
        }

        [Test]
        public void IsWithinEvictThreshold_TileBeyondHysteresis_ReturnsFalse()
        {
            var pos    = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f);
            var centre = TerrainWorldGrid.WorldToTileCoord(pos.x, pos.z);
            int beyond = TerrainStreamingConfig.RING_RADIUS +
                         TerrainStreamingConfig.HYSTERESIS_TILES + 1;
            var tile   = new Vector2Int(centre.x + beyond, centre.y);

            Assert.IsFalse(TerrainResidencyRing.IsWithinEvictThreshold(tile, pos),
                "Tile beyond hysteresis threshold should return false (evict it).");
        }
    }
}
