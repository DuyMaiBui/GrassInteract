#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for the collider ring derivation used by <see cref="TerrainColliderStreamer"/>:
    ///   - Near-ring membership uses the LOD-band nearest-edge metric (<see cref="TerrainColliderRing"/>).
    ///   - Cook amortisation caps per-tick builds at MAX_COOKS_PER_FRAME.
    ///   - Camera moves change the desired set (eviction trigger).
    ///
    /// These exercise the pure ring math + the stateless BuildHeightfield; the live streamer
    /// wiring is covered by <see cref="TerrainColliderStreamerWiringTests"/>.
    /// </summary>
    [TestFixture]
    public sealed class TerrainColliderStreamerDiffTests
    {
        private const float TILE         = TerrainWorldGrid.TILE_SIZE_M;
        private const float LOD_RANGE_M  = 256f; // band 3 default → 3×3 around a tile centre
        private const int   HF_RES       = 65;

        // ── Near-ring membership (nearest-edge metric) ────────────────────────

        [Test]
        public void ColliderRing_CentreTile_IsInDesiredSet()
        {
            var camPos  = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f); // tile centre
            var desired = TerrainColliderRing.ComputeDesired(camPos, LOD_RANGE_M);

            var centre = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            Assert.IsTrue(desired.Contains(centre),
                "Camera's own tile should always be in the collider ring.");
        }

        [Test]
        public void ColliderRing_AdjacentTile_IncludedAt256m()
        {
            var camPos  = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainColliderRing.ComputeDesired(camPos, LOD_RANGE_M);

            var centre = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            var edge   = new Vector2Int(centre.x + 1, centre.y); // nearest edge 128 m < 256 m

            Assert.IsTrue(desired.Contains(edge),
                "Edge-adjacent tile (nearest edge 128 m) should be in the 256 m ring.");
        }

        [Test]
        public void ColliderRing_TwoTilesOut_IsExcludedAt256m()
        {
            var camPos  = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainColliderRing.ComputeDesired(camPos, LOD_RANGE_M);

            var centre = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            var far    = new Vector2Int(centre.x + 2, centre.y); // nearest edge 384 m > 256 m

            Assert.IsFalse(desired.Contains(far),
                "Tile two out (nearest edge 384 m) should be outside the 256 m ring.");
        }

        [Test]
        public void ColliderRing_256m_AtTileCentre_IsFull3x3()
        {
            var camPos  = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f); // tile centre
            var desired = TerrainColliderRing.ComputeDesired(camPos, LOD_RANGE_M);

            Assert.AreEqual(9, desired.Count,
                "256 m band from a tile centre should cover exactly the 3×3 ring (9 tiles).");
        }

        [Test]
        public void ColliderRing_Band0_32m_IsOwnTileOnly()
        {
            var camPos  = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainColliderRing.ComputeDesired(camPos, 32f); // adjacent edge 128 m > 32 m

            Assert.AreEqual(1, desired.Count,
                "32 m band should cook only the camera's own tile.");
        }

        [Test]
        public void ColliderRing_LargerRange_IsSuperset()
        {
            var camPos = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var near   = TerrainColliderRing.ComputeDesired(camPos, 32f);
            var far    = TerrainColliderRing.ComputeDesired(camPos, 256f);

            Assert.IsTrue(far.IsSupersetOf(near),
                "A larger range must include every tile a smaller range includes (monotonic).");
        }

        // ── Cook amortisation ─────────────────────────────────────────────────

        [Test]
        public void MaxCooksPerFrame_IsPositive()
        {
            Assert.Greater(TerrainColliderConfig.MAX_COOKS_PER_FRAME, 0,
                "MAX_COOKS_PER_FRAME must be at least 1.");
        }

        // ── Heightfield parity used by provider ───────────────────────────────

        [Test]
        public void BuildHeightfield_FlatTile_AllSamplesEqual()
        {
            float h    = 100f;
            float minH = 0f;
            float maxH = 512f;
            int srcRes = 5;
            int hfRes  = HF_RES;

            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = Vector2Int.zero;
            tile.heightRes = srcRes;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            tile.heightData = new byte[srcRes * srcRes * TerrainHeightFormat.BYTES_PER_SAMPLE];
            ushort raw = TerrainHeightFormat.EncodeHeight(h, minH, maxH);
            for (int i = 0; i < srcRes * srcRes; ++i)
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);

            float[,] hf = TerrainColliderProvider.BuildHeightfield(tile, hfRes);
            float expected = (h - minH) / (maxH - minH);

            for (int row = 0; row < hfRes; ++row)
                for (int col = 0; col < hfRes; ++col)
                    Assert.AreEqual(expected, hf[row, col], 0.001f,
                        "All heightfield cells should be the same for a flat tile.");
        }

        // ── Eviction tracking (no live Unity Physics needed) ──────────────────

        [Test]
        public void DesiredSet_ExcludesPreviousTiles_OnCameraMove()
        {
            // When the camera moves one tile, the trailing column of the old ring is no longer
            // desired — this is the eviction trigger in the streamer.
            var posA = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f);
            var posB = new Vector3(TILE * 6.5f, 0f, TILE * 5.5f);

            var desiredA = TerrainColliderRing.ComputeDesired(posA, LOD_RANGE_M);
            var desiredB = TerrainColliderRing.ComputeDesired(posB, LOD_RANGE_M);

            var toEvict = new HashSet<Vector2Int>(desiredA);
            toEvict.ExceptWith(desiredB);

            Assert.Greater(toEvict.Count, 0,
                "After the camera moves one tile there should be tiles to evict.");
        }
    }
}
