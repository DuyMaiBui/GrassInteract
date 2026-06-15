#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Pure-logic tests for <see cref="WorldPainterImpostorLod"/> distance-threshold LOD
    /// selection (Phase 4 task 6).
    ///
    /// No GPU / scene / editor dependencies — runs as EditMode tests.
    /// </summary>
    [TestFixture]
    public class WorldPainterImpostorLodTests
    {
        // ── IsImpostor ────────────────────────────────────────────────────────

        [Test]
        public void IsImpostor_BeyondThreshold_ReturnsTrue()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(60f);

            var camPos      = new Vector3(0f, 10f, 0f);
            var instancePos = new Vector3(61f, 0f, 0f); // 61m XZ from camera

            Assert.IsTrue(lod.IsImpostor(instancePos, camPos),
                "Instance beyond 60m should be impostor");
        }

        [Test]
        public void IsImpostor_InsideThreshold_ReturnsFalse()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(60f);

            var camPos      = new Vector3(0f, 10f, 0f);
            var instancePos = new Vector3(59f, 0f, 0f); // 59m XZ from camera

            Assert.IsFalse(lod.IsImpostor(instancePos, camPos),
                "Instance inside 60m threshold should NOT be impostor");
        }

        [Test]
        public void IsImpostor_YDifferenceIgnored()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(60f);

            // XZ distance is only 5m; Y difference is 500m (cliff scenario).
            var camPos      = new Vector3(0f, 500f, 0f);
            var instancePos = new Vector3(5f, 0f, 0f);

            Assert.IsFalse(lod.IsImpostor(instancePos, camPos),
                "Y distance must be ignored in XZ-planar distance check");
        }

        [Test]
        public void IsImpostor_ExactlyAtThreshold_ReturnsTrue()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(60f);

            var camPos      = new Vector3(0f, 0f, 0f);
            var instancePos = new Vector3(60f, 0f, 0f); // exactly at threshold

            // Boundary: dist² >= threshold² → impostor.
            Assert.IsTrue(lod.IsImpostor(instancePos, camPos),
                "Instance exactly at threshold should be impostor (>= check)");
        }

        [Test]
        public void SetImpostorDistance_UpdatesCachedSquared()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(100f);

            Assert.AreEqual(100f, lod.ImpostorDistanceM, 0.001f);

            // 99m is NOT impostor after setting 100m threshold.
            var camPos      = new Vector3(0f, 0f, 0f);
            var instancePos = new Vector3(99f, 0f, 0f);
            Assert.IsFalse(lod.IsImpostor(instancePos, camPos),
                "99m < 100m threshold — should NOT be impostor");
        }

        [Test]
        public void SetImpostorDistance_NegativeValueClamped()
        {
            var lod = new WorldPainterImpostorLod();
            lod.SetImpostorDistance(-10f); // should clamp to 0

            Assert.AreEqual(0f, lod.ImpostorDistanceM, 0.001f,
                "Negative distance should be clamped to 0");
        }

        // ── Default constant ──────────────────────────────────────────────────

        [Test]
        public void DefaultDistance_Is60Metres()
        {
            Assert.AreEqual(60f, WorldPainterImpostorLod.DEFAULT_IMPOSTOR_DISTANCE_M, 0.001f);
        }
    }
}
