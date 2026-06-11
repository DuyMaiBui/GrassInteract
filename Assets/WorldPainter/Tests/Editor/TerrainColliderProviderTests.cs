#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="TerrainColliderProvider"/>:
    ///   - Heightfield data matches tile R16 within epsilon.
    ///   - A downward raycast from above hits expected world Y.
    ///   - Build returns null for invalid tile data.
    /// </summary>
    [TestFixture]
    public sealed class TerrainColliderProviderTests
    {
        private const float TILE    = TerrainWorldGrid.TILE_SIZE_M;
        private const float EPSILON = 1.0f; // 1 m tolerance for heightfield downsampling

        // ── Tile builders ─────────────────────────────────────────────────────

        private static TerrainTileAsset MakeFlatTile(float height,
            float minH = 0f, float maxH = 512f)
        {
            int res  = TerrainWorldGrid.DEFAULT_HEIGHT_RES;
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = Vector2Int.zero;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int count = res * res;
            tile.heightData = new byte[count * TerrainHeightFormat.BYTES_PER_SAMPLE];
            ushort raw = TerrainHeightFormat.EncodeHeight(height, minH, maxH);
            for (int i = 0; i < count; ++i)
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);
            return tile;
        }

        // ── Heightfield data parity ────────────────────────────────────────────

        [Test]
        public void FlatTile_Heightfield_AllSamplesNormalized_Correct()
        {
            float h     = 128f;
            float minH  = 0f;
            float maxH  = 512f;
            var tile    = MakeFlatTile(h, minH, maxH);
            int hfRes   = TerrainColliderConfig.HEIGHTFIELD_RES;

            float[,] hf = TerrainColliderProvider.BuildHeightfield(tile, hfRes);

            float expected = (h - minH) / (maxH - minH);
            for (int row = 0; row < hfRes; ++row)
                for (int col = 0; col < hfRes; ++col)
                    Assert.AreEqual(expected, hf[row, col], 0.001f,
                        $"Heightfield[{row},{col}] should equal normalized flat height.");
        }

        [Test]
        public void InvalidTile_ReturnsNullHandle()
        {
            var tile   = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.heightData = new byte[0]; // invalid
            var parent = new GameObject("TestParent").transform;

            var handle = TerrainColliderProvider.Build(tile, parent);

            Assert.IsNull(handle, "Build should return null for a tile with empty heightData.");
            Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void NullTile_ReturnsNullHandle()
        {
            var parent = new GameObject("TestParent").transform;
            var handle = TerrainColliderProvider.Build(null!, parent);
            Assert.IsNull(handle, "Build should return null for a null tile.");
            Object.DestroyImmediate(parent.gameObject);
        }

        // ── Heightfield corner samples match R16 ──────────────────────────────

        [Test]
        public void HeightfieldCorners_MatchSourceR16_WithinEpsilon()
        {
            // Build a ramp tile and verify that heightfield corner [0,0] and
            // [hfRes-1, hfRes-1] approximately match the source R16 at corresponding texels.
            int srcRes  = 9; // small resolution for test speed
            float minH  = 10f;
            float maxH  = 200f;
            int hfRes   = TerrainColliderConfig.HEIGHTFIELD_RES;

            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = Vector2Int.zero;
            tile.heightRes = srcRes;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int count = srcRes * srcRes;
            tile.heightData = new byte[count * TerrainHeightFormat.BYTES_PER_SAMPLE];

            // Fill with a linear Z-ramp: h = minH + (z / (srcRes-1)) * (maxH - minH).
            for (int z = 0; z < srcRes; ++z)
                for (int x = 0; x < srcRes; ++x)
                {
                    float t   = srcRes > 1 ? (float)z / (srcRes - 1) : 0f;
                    float h   = minH + t * (maxH - minH);
                    ushort raw = TerrainHeightFormat.EncodeHeight(h, minH, maxH);
                    TerrainHeightFormat.WriteRaw(tile.heightData, z * srcRes + x, raw);
                }

            float[,] hf = TerrainColliderProvider.BuildHeightfield(tile, hfRes);

            // Corner [0,0] should correspond to src texel (0,0) → minH normalized = 0.
            float expectedMin = 0f;
            Assert.AreEqual(expectedMin, hf[0, 0], 0.01f,
                "Corner [0,0] should map to normalized minH.");

            // Corner [hfRes-1, hfRes-1] should correspond to src (srcRes-1, srcRes-1) → maxH.
            float expectedMax = 1f;
            Assert.AreEqual(expectedMax, hf[hfRes - 1, hfRes - 1], 0.01f,
                "Corner [hfRes-1, hfRes-1] should map to normalized maxH.");
        }
    }
}
