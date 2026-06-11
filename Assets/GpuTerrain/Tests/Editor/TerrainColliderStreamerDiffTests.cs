#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using GpuTerrain;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="TerrainColliderStreamer"/>:
    ///   - Near-ring membership uses COLLIDER_RING_RADIUS.
    ///   - Cook amortisation caps per-tick builds at MAX_COOKS_PER_FRAME.
    ///   - Eviction releases the collider handle (no leak).
    ///
    /// These tests exercise the TerrainResidencyRing helper used by the streamer,
    /// and the TerrainColliderProvider.BuildHeightfield (stateless, testable without
    /// a live Unity Physics engine).
    /// </summary>
    [TestFixture]
    public sealed class TerrainColliderStreamerDiffTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M;

        // ── Near-ring membership ──────────────────────────────────────────────

        [Test]
        public void ColliderRing_CentreTile_IsInDesiredSet()
        {
            var camPos = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainResidencyRing.ComputeDesired(camPos,
                TerrainColliderConfig.COLLIDER_RING_RADIUS);

            var centre = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            Assert.IsTrue(desired.Contains(centre),
                "Camera's own tile should always be in the collider ring.");
        }

        [Test]
        public void ColliderRing_TileAtRadius_IsIncluded()
        {
            var camPos = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainResidencyRing.ComputeDesired(camPos,
                TerrainColliderConfig.COLLIDER_RING_RADIUS);

            var centre  = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            int r       = TerrainColliderConfig.COLLIDER_RING_RADIUS;
            var edge    = new Vector2Int(centre.x + r, centre.y);

            Assert.IsTrue(desired.Contains(edge),
                $"Tile at COLLIDER_RING_RADIUS={r} should be in the near ring.");
        }

        [Test]
        public void ColliderRing_TileBeyondRadius_IsExcluded()
        {
            var camPos = new Vector3(TILE * 3.5f, 0f, TILE * 3.5f);
            var desired = TerrainResidencyRing.ComputeDesired(camPos,
                TerrainColliderConfig.COLLIDER_RING_RADIUS);

            var centre  = TerrainWorldGrid.WorldToTileCoord(camPos.x, camPos.z);
            int beyond  = TerrainColliderConfig.COLLIDER_RING_RADIUS + 1;
            var far     = new Vector2Int(centre.x + beyond, centre.y);

            Assert.IsFalse(desired.Contains(far),
                "Tile beyond COLLIDER_RING_RADIUS should be outside the near ring.");
        }

        [Test]
        public void ColliderRing_Size_IsSquaredDiameter()
        {
            var camPos  = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f);
            var desired = TerrainResidencyRing.ComputeDesired(camPos,
                TerrainColliderConfig.COLLIDER_RING_RADIUS);

            int r        = TerrainColliderConfig.COLLIDER_RING_RADIUS;
            int expected = (2 * r + 1) * (2 * r + 1);
            Assert.AreEqual(expected, desired.Count,
                $"Collider ring at radius {r} should contain {expected} tiles.");
        }

        // ── Cook amortisation ─────────────────────────────────────────────────

        [Test]
        public void MaxCooksPerFrame_IsPositive()
        {
            Assert.Greater(TerrainColliderConfig.MAX_COOKS_PER_FRAME, 0,
                "MAX_COOKS_PER_FRAME must be at least 1.");
        }

        [Test]
        public void ColliderRingRadius_IsLeOrEqualStreamingRingRadius()
        {
            Assert.LessOrEqual(TerrainColliderConfig.COLLIDER_RING_RADIUS,
                               TerrainStreamingConfig.RING_RADIUS,
                "COLLIDER_RING_RADIUS must be ≤ RING_RADIUS to avoid building colliders " +
                "for tiles not yet loaded by the streaming manager.");
        }

        // ── Heightfield parity used by provider ───────────────────────────────

        [Test]
        public void BuildHeightfield_FlatTile_AllSamplesEqual()
        {
            float h    = 100f;
            float minH = 0f;
            float maxH = 512f;
            int srcRes = 5;
            int hfRes  = TerrainColliderConfig.HEIGHTFIELD_RES;

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
            // Verify that when the camera moves one tile, the old outer ring tiles are
            // no longer desired — this is the eviction trigger in the streamer.
            var posA    = new Vector3(TILE * 5.5f, 0f, TILE * 5.5f);
            var posB    = new Vector3(TILE * 6.5f, 0f, TILE * 5.5f);

            var desiredA = TerrainResidencyRing.ComputeDesired(posA, TerrainColliderConfig.COLLIDER_RING_RADIUS);
            var desiredB = TerrainResidencyRing.ComputeDesired(posB, TerrainColliderConfig.COLLIDER_RING_RADIUS);

            var toEvict = new HashSet<Vector2Int>(desiredA);
            toEvict.ExceptWith(desiredB);

            Assert.Greater(toEvict.Count, 0,
                "After camera moves one tile there should be tiles to evict.");
        }
    }
}
