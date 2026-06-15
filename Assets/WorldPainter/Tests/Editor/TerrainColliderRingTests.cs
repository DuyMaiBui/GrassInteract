#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Pure-math tests for <see cref="TerrainColliderRing"/> (nearest-edge collider ring).
    /// No live scene required.
    /// </summary>
    [TestFixture]
    public sealed class TerrainColliderRingTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M; // 256

        [Test]
        public void NearestEdge_CameraInsideTile_IsZero()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f); // centre of tile (0,0)
            Assert.AreEqual(0f, TerrainColliderRing.NearestEdgeDistance(cam, new Vector2Int(0, 0)), 1e-3f);
        }

        [Test]
        public void NearestEdge_AdjacentTile_Is128AtCentre()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f); // (128,128)
            float d = TerrainColliderRing.NearestEdgeDistance(cam, new Vector2Int(1, 0)); // tile [256,512]
            Assert.AreEqual(128f, d, 1e-3f);
        }

        [Test]
        public void NearestEdge_DiagonalTile_Is181AtCentre()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f);
            float d = TerrainColliderRing.NearestEdgeDistance(cam, new Vector2Int(1, 1)); // corner (256,256)
            Assert.AreEqual(Mathf.Sqrt(128f * 128f + 128f * 128f), d, 1e-2f); // ≈181.02
        }

        [Test]
        public void ComputeDesired_ZeroRange_IsEmpty()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f);
            Assert.AreEqual(0, TerrainColliderRing.ComputeDesired(cam, 0f).Count);
            Assert.AreEqual(0, TerrainColliderRing.ComputeDesired(cam, -10f).Count);
        }

        [Test]
        public void ComputeDesired_32m_OwnTileOnly()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f);
            var set = TerrainColliderRing.ComputeDesired(cam, 32f);
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(new Vector2Int(0, 0)));
        }

        [Test]
        public void ComputeDesired_256m_Full3x3AtCentre()
        {
            var cam = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f);
            var set = TerrainColliderRing.ComputeDesired(cam, 256f);
            Assert.AreEqual(9, set.Count);
            for (int dz = -1; dz <= 1; ++dz)
                for (int dx = -1; dx <= 1; ++dx)
                    Assert.IsTrue(set.Contains(new Vector2Int(dx, dz)), $"missing ({dx},{dz})");
        }

        [Test]
        public void ComputeDesired_Monotonic_LargerRangeIsSuperset()
        {
            var cam  = new Vector3(TILE * 2.3f, 0f, TILE * 4.7f); // off-centre camera
            var near = TerrainColliderRing.ComputeDesired(cam, 64f);
            var far  = TerrainColliderRing.ComputeDesired(cam, 300f);
            Assert.IsTrue(far.IsSupersetOf(near));
        }
    }
}
