#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
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
        private const int   HF_RES  = 65;   // 2^6 + 1 (was TerrainColliderConfig.HEIGHTFIELD_RES)

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
            int hfRes   = HF_RES;

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

            var handle = TerrainColliderProvider.Build(tile, parent, HF_RES);

            Assert.IsNull(handle, "Build should return null for a tile with empty heightData.");
            Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void NullTile_ReturnsNullHandle()
        {
            var parent = new GameObject("TestParent").transform;
            var handle = TerrainColliderProvider.Build(null!, parent, HF_RES);
            Assert.IsNull(handle, "Build should return null for a null tile.");
            Object.DestroyImmediate(parent.gameObject);
        }

        // ── Cooked collider is physically hittable (runtime smoke) ────────────

        [Test]
        public void CookedCollider_DownwardRaycast_HitsAtExpectedHeight()
        {
            // Flat tile at height 50 in [0,100] → terrain surface at world y≈50 over tile (0,0).
            var tile   = MakeFlatTile(50f, 0f, 100f);
            var parent = new GameObject("RaycastSmokeParent").transform;
            var handle = TerrainColliderProvider.Build(tile, parent, HF_RES);
            Assert.IsNotNull(handle, "Build should cook a collider for a valid tile.");

            Physics.SyncTransforms();
            bool ok = Physics.Raycast(new Vector3(TILE * 0.5f, 1000f, TILE * 0.5f),
                                      Vector3.down, out RaycastHit hit, 2000f);

            Assert.IsTrue(ok, "Downward ray over the cooked tile should hit its TerrainCollider.");
            Assert.IsInstanceOf<TerrainCollider>(hit.collider, "Ray should hit the cooked TerrainCollider.");
            Assert.AreEqual(50f, hit.point.y, EPSILON, "Hit height should match the flat tile height.");

            handle!.Release();
            Object.DestroyImmediate(parent.gameObject);
            Object.DestroyImmediate(tile);
        }

        [Test]
        public void NearestValidHeightfieldRes_RoundsUpToPow2Plus1()
        {
            Assert.AreEqual(65,  TerrainColliderProvider.NearestValidHeightfieldRes(65),  "65 is already valid.");
            Assert.AreEqual(129, TerrainColliderProvider.NearestValidHeightfieldRes(100), "100 rounds up to 129.");
            Assert.AreEqual(33,  TerrainColliderProvider.NearestValidHeightfieldRes(10),  "Below-min clamps to 33.");
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
            int hfRes   = HF_RES;

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
