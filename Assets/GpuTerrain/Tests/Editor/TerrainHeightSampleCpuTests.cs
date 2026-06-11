#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for TerrainHeightSampleCpu bilinear sampling.
    /// Uses analytic height fields (flat, linear ramps) where the expected
    /// interpolated value is known exactly.
    /// </summary>
    [TestFixture]
    public class TerrainHeightSampleCpuTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M;
        private const float EPSILON = 0.05f; // ≈ half a quantisation step for 512m range at 16-bit

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Build a flat tile where every texel encodes the same height.</summary>
        private static TerrainTileAsset MakeFlatTile(float flatHeight,
            float minH = 0f, float maxH = 512f,
            Vector2Int? coord = null, int res = TerrainWorldGrid.DEFAULT_HEIGHT_RES)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = coord ?? Vector2Int.zero;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int texelCount = res * res;
            tile.heightData = new byte[texelCount * TerrainHeightFormat.BYTES_PER_SAMPLE];
            ushort raw = TerrainHeightFormat.EncodeHeight(flatHeight, minH, maxH);
            for (int i = 0; i < texelCount; ++i)
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);
            return tile;
        }

        /// <summary>
        /// Build a tile with a linear ramp in X: height = minH + (worldX / TILE) * (maxH - minH).
        /// </summary>
        private static TerrainTileAsset MakeXRampTile(float minH = 0f, float maxH = 512f,
            int res = TerrainWorldGrid.DEFAULT_HEIGHT_RES)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = Vector2Int.zero;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int texelCount = res * res;
            tile.heightData = new byte[texelCount * TerrainHeightFormat.BYTES_PER_SAMPLE];
            for (int z = 0; z < res; ++z)
            {
                for (int x = 0; x < res; ++x)
                {
                    // Texel at (x, z): world height scales linearly with x-texel index.
                    // x=0 → minH, x=res-1 → maxH (shared-edge convention).
                    float t = (res > 1) ? (float)x / (res - 1) : 0f;
                    float h = minH + t * (maxH - minH);
                    ushort raw = TerrainHeightFormat.EncodeHeight(h, minH, maxH);
                    TerrainHeightFormat.WriteRaw(tile.heightData, z * res + x, raw);
                }
            }
            return tile;
        }

        // ── Flat tile tests ───────────────────────────────────────────────────

        [Test]
        public void FlatTile_CentrePoint_ReturnsCorrectHeight()
        {
            float expected = 100f;
            var tile = MakeFlatTile(expected);
            bool ok = TerrainHeightSampleCpu.TrySample(tile, TILE * 0.5f, TILE * 0.5f, out float h);
            Assert.IsTrue(ok, "TrySample should return true for centre of tile");
            Assert.AreEqual(expected, h, EPSILON);
        }

        [Test]
        public void FlatTile_CornerMin_ReturnsCorrectHeight()
        {
            float expected = 50f;
            var tile = MakeFlatTile(expected);
            bool ok = TerrainHeightSampleCpu.TrySample(tile, 0f, 0f, out float h);
            Assert.IsTrue(ok);
            Assert.AreEqual(expected, h, EPSILON);
        }

        [Test]
        public void FlatTile_CornerMax_ReturnsCorrectHeight()
        {
            // UV=1 is the shared boundary texel; still part of tile(0,0).
            float expected = 200f;
            var tile = MakeFlatTile(expected);
            // worldX = TILE exactly → WorldToTileCoord returns tile (1,0), not (0,0).
            // Use TILE - epsilon to stay within tile (0,0).
            bool ok = TerrainHeightSampleCpu.TrySample(tile, TILE - 0.001f, TILE - 0.001f, out float h);
            Assert.IsTrue(ok);
            Assert.AreEqual(expected, h, EPSILON);
        }

        // ── Ramp tile tests ───────────────────────────────────────────────────

        [Test]
        public void XRampTile_MinCorner_ReturnsMinHeight()
        {
            float minH = 0f, maxH = 256f;
            var tile = MakeXRampTile(minH, maxH);
            bool ok = TerrainHeightSampleCpu.TrySample(tile, 0f, 0f, out float h);
            Assert.IsTrue(ok);
            Assert.AreEqual(minH, h, EPSILON);
        }

        [Test]
        public void XRampTile_MaxCornerX_ReturnsMaxHeight()
        {
            float minH = 0f, maxH = 256f;
            var tile = MakeXRampTile(minH, maxH);
            // Use TILE - epsilon so we stay within tile(0,0).
            bool ok = TerrainHeightSampleCpu.TrySample(tile, TILE - 0.001f, TILE * 0.5f, out float h);
            Assert.IsTrue(ok);
            // At X = TILE - epsilon, UV ≈ 1 → height ≈ maxH.
            Assert.AreEqual(maxH, h, 1f); // 1m tolerance at boundary
        }

        [Test]
        public void XRampTile_QuarterX_ReturnsQuarterHeight()
        {
            float minH = 0f, maxH = 400f;
            var tile = MakeXRampTile(minH, maxH);
            float quarterX = TILE * 0.25f;
            bool ok = TerrainHeightSampleCpu.TrySample(tile, quarterX, TILE * 0.5f, out float h);
            Assert.IsTrue(ok);
            float expected = maxH * 0.25f;
            Assert.AreEqual(expected, h, 2f); // 2m tolerance (quantisation + bilinear rounding)
        }

        [Test]
        public void XRampTile_HalfX_ReturnsMidHeight()
        {
            float minH = 0f, maxH = 512f;
            var tile = MakeXRampTile(minH, maxH);
            bool ok = TerrainHeightSampleCpu.TrySample(tile, TILE * 0.5f, TILE * 0.5f, out float h);
            Assert.IsTrue(ok);
            float expected = 256f;
            Assert.AreEqual(expected, h, 1f);
        }

        // ── Out-of-bounds / wrong tile tests ─────────────────────────────────

        [Test]
        public void WrongTile_ReturnsFalse()
        {
            var tile = MakeFlatTile(100f);
            // worldX = TILE + 1 is in tile (1,0), not (0,0).
            bool ok = TerrainHeightSampleCpu.TrySample(tile, TILE + 1f, 0f, out float h);
            Assert.IsFalse(ok, "Sample outside tile should return false");
        }

        [Test]
        public void NullTile_ReturnsFalse()
        {
            TerrainTileAsset? nullTile = null;
            bool ok = TerrainHeightSampleCpu.TrySample(nullTile!, 0f, 0f, out float h);
            Assert.IsFalse(ok);
        }
    }
}
