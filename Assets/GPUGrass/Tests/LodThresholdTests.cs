#nullable enable
using NUnit.Framework;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassRenderer.LodThresholds"/> — the LOD switch-distance → squared
    /// threshold math. Guards the rule that missing switch distances extend the last present LOD to the
    /// cull boundary (so blades never bucket into an empty, mesh-less LOD slot).
    /// </summary>
    public sealed class LodThresholdTests
    {
        [Test]
        public void Thresholds_SquaresSwitchDistances()
        {
            var (l0, l1, max) = GpuGrassRenderer.LodThresholds(new[] { 15f, 40f }, 80f);
            Assert.AreEqual(225f, l0, 1e-3f);
            Assert.AreEqual(1600f, l1, 1e-3f);
            Assert.AreEqual(6400f, max, 1e-3f);
        }

        [Test]
        public void Thresholds_MissingSwitchesExtendToCullBoundary()
        {
            // Only one switch distance + a cull distance: LOD1 boundary must extend to cull² (not a default).
            var (l0, l1, max) = GpuGrassRenderer.LodThresholds(new[] { 15f }, 50f);
            Assert.AreEqual(225f, l0, 1e-3f);
            Assert.AreEqual(2500f, l1, 1e-3f, "Missing LOD1 switch must extend to cull².");
            Assert.AreEqual(2500f, max, 1e-3f);
        }

        [Test]
        public void Thresholds_NoCullMeansAllBladesBucketToLod0()
        {
            // cull <= 0 → "render all": both boundaries are MaxValue so everything is LOD0, max is 0 (no cap).
            var (l0, l1, max) = GpuGrassRenderer.LodThresholds(System.Array.Empty<float>(), 0f);
            Assert.AreEqual(float.MaxValue, l0);
            Assert.AreEqual(float.MaxValue, l1);
            Assert.AreEqual(0f, max);
        }

        [Test]
        public void Thresholds_NullSwitchesIsSafe()
        {
            var (l0, l1, max) = GpuGrassRenderer.LodThresholds(null, 30f);
            Assert.AreEqual(900f, l0, 1e-3f);
            Assert.AreEqual(900f, l1, 1e-3f);
            Assert.AreEqual(900f, max, 1e-3f);
        }
    }
}
