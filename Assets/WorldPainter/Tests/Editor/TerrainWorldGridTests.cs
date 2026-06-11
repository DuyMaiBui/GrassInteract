#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for TerrainWorldGrid: world↔tile coordinate + texel UV mapping.
    /// Verifies round-trip correctness, boundary consistency (shared-edge convention),
    /// and out-of-bounds clamping.
    /// </summary>
    [TestFixture]
    public class TerrainWorldGridTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M; // 256
        private const float EPSILON = 0.0001f;

        // ── WorldToTileCoord ──────────────────────────────────────────────────

        [Test]
        public void WorldToTileCoord_Origin_Returns00()
        {
            var coord = TerrainWorldGrid.WorldToTileCoord(0f, 0f);
            Assert.AreEqual(Vector2Int.zero, coord);
        }

        [Test]
        public void WorldToTileCoord_InsideTile00_Returns00()
        {
            var coord = TerrainWorldGrid.WorldToTileCoord(TILE * 0.5f, TILE * 0.5f);
            Assert.AreEqual(Vector2Int.zero, coord);
        }

        [Test]
        public void WorldToTileCoord_ExactlyOneTileX_Returns10()
        {
            // worldX = TILE → floor(TILE/TILE) = 1 → tile (1, 0).
            var coord = TerrainWorldGrid.WorldToTileCoord(TILE, 0f);
            Assert.AreEqual(new Vector2Int(1, 0), coord);
        }

        [Test]
        public void WorldToTileCoord_Negative_ReturnsNegativeTile()
        {
            var coord = TerrainWorldGrid.WorldToTileCoord(-1f, -1f);
            Assert.AreEqual(new Vector2Int(-1, -1), coord);
        }

        // ── TileOriginWorld ───────────────────────────────────────────────────

        [Test]
        public void TileOriginWorld_00_ReturnsZero()
        {
            var origin = TerrainWorldGrid.TileOriginWorld(Vector2Int.zero);
            Assert.AreEqual(Vector2.zero, origin);
        }

        [Test]
        public void TileOriginWorld_11_ReturnsTileSize()
        {
            var origin = TerrainWorldGrid.TileOriginWorld(new Vector2Int(1, 1));
            Assert.AreEqual(new Vector2(TILE, TILE), origin);
        }

        // ── WorldToTexelUV ────────────────────────────────────────────────────

        [Test]
        public void WorldToTexelUV_MinCorner_Returns00()
        {
            var uv = TerrainWorldGrid.WorldToTexelUV(0f, 0f, Vector2Int.zero);
            Assert.AreEqual(0f, uv.x, EPSILON);
            Assert.AreEqual(0f, uv.y, EPSILON);
        }

        [Test]
        public void WorldToTexelUV_MaxCorner_Returns11()
        {
            var uv = TerrainWorldGrid.WorldToTexelUV(TILE, TILE, Vector2Int.zero);
            Assert.AreEqual(1f, uv.x, EPSILON);
            Assert.AreEqual(1f, uv.y, EPSILON);
        }

        [Test]
        public void WorldToTexelUV_Centre_Returns0505()
        {
            var uv = TerrainWorldGrid.WorldToTexelUV(TILE * 0.5f, TILE * 0.5f, Vector2Int.zero);
            Assert.AreEqual(0.5f, uv.x, EPSILON);
            Assert.AreEqual(0.5f, uv.y, EPSILON);
        }

        // ── Shared-edge boundary consistency ─────────────────────────────────

        [Test]
        public void SharedEdge_BoundaryPoint_EquivalentFromBothTiles()
        {
            // A point exactly on the X boundary between tile (0,0) and tile (1,0).
            float boundaryX = TILE; // = 256
            float worldZ = TILE * 0.5f;

            // From tile (0,0): UV.x should be 1.0
            var uvTile0 = TerrainWorldGrid.WorldToTexelUV(boundaryX, worldZ, new Vector2Int(0, 0));
            // From tile (1,0): UV.x should be 0.0
            var uvTile1 = TerrainWorldGrid.WorldToTexelUV(boundaryX, worldZ, new Vector2Int(1, 0));

            Assert.AreEqual(1f, uvTile0.x, EPSILON,
                "Boundary point UV from tile(0,0) should be 1.0 (shared-edge)");
            Assert.AreEqual(0f, uvTile1.x, EPSILON,
                "Boundary point UV from tile(1,0) should be 0.0 (shared-edge)");

            // Both UVs must map to the same texel index when clamped to [0, heightRes-1].
            int res = TerrainWorldGrid.DEFAULT_HEIGHT_RES;
            int texelFromTile0 = TerrainWorldGrid.UVToTexelCoord(uvTile0.x, res);
            int texelFromTile1 = TerrainWorldGrid.UVToTexelCoord(uvTile1.x, res);

            Assert.AreEqual(res - 1, texelFromTile0, "UV=1 → last texel (shared)");
            Assert.AreEqual(0, texelFromTile1, "UV=0 → first texel (shared)");
        }

        // ── UVToTexelCoord ────────────────────────────────────────────────────

        [Test]
        public void UVToTexelCoord_UV0_Returns0()
        {
            Assert.AreEqual(0, TerrainWorldGrid.UVToTexelCoord(0f, 257));
        }

        [Test]
        public void UVToTexelCoord_UV1_ReturnsLastTexel()
        {
            Assert.AreEqual(256, TerrainWorldGrid.UVToTexelCoord(1f, 257));
        }

        [Test]
        public void UVToTexelCoord_OutOfRange_Clamped()
        {
            // UV beyond [0,1] should clamp.
            Assert.AreEqual(0,   TerrainWorldGrid.UVToTexelCoord(-0.5f, 257));
            Assert.AreEqual(256, TerrainWorldGrid.UVToTexelCoord(1.5f,  257));
        }

        // ── Round-trip world → tileCoord → origin → UV → tileCoord ──────────

        [Test]
        public void RoundTrip_WorldToTileAndBack()
        {
            float worldX = 512.7f;
            float worldZ = 131.2f;

            Vector2Int coord = TerrainWorldGrid.WorldToTileCoord(worldX, worldZ);
            Vector2 origin   = TerrainWorldGrid.TileOriginWorld(coord);
            Vector2 uv       = TerrainWorldGrid.WorldToTexelUV(worldX, worldZ, coord);

            // Reconstruct world XZ from origin + UV.
            float reconstructedX = origin.x + uv.x * TILE;
            float reconstructedZ = origin.y + uv.y * TILE;

            Assert.AreEqual(worldX, reconstructedX, EPSILON);
            Assert.AreEqual(worldZ, reconstructedZ, EPSILON);
        }
    }
}
