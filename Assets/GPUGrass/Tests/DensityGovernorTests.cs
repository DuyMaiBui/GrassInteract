#nullable enable
using NUnit.Framework;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassDensityGovernor"/> — the frame-time → density load valve.
    /// Pure logic (no Unity objects), so it runs fast and deterministically.
    /// </summary>
    public sealed class DensityGovernorTests
    {
        private const float TARGET_FPS = 60f;   // ~16.7 ms budget
        private const float FLOOR      = 0.6f;
        private const float SLOW_DT    = 0.05f;  // 50 ms ≈ 20 fps → over budget
        private const float FAST_DT    = 0.004f; // 4 ms ≈ 250 fps → headroom

        [Test]
        public void StartsAtFullDensity()
        {
            var g = new GpuGrassDensityGovernor();
            Assert.AreEqual(1f, g.Density, 1e-6f);
        }

        [Test]
        public void OverBudget_ThinsBelowFullButNotBelowFloor()
        {
            var g = new GpuGrassDensityGovernor();
            for (int i = 0; i < 30; i++) g.Tick(SLOW_DT, TARGET_FPS, FLOOR);

            Assert.Less(g.Density, 1f, "Sustained over-budget frames must thin the field.");
            Assert.GreaterOrEqual(g.Density, FLOOR, "Density must never drop below the configured floor.");
        }

        [Test]
        public void ExtremeLoad_ClampsExactlyAtFloor()
        {
            var g = new GpuGrassDensityGovernor();
            for (int i = 0; i < 400; i++) g.Tick(0.1f, TARGET_FPS, FLOOR); // 100 ms frames, sustained

            Assert.AreEqual(FLOOR, g.Density, 1e-4f, "At max load density should pin to the floor, not below.");
        }

        [Test]
        public void Headroom_RestoresDensityAfterThinning()
        {
            var g = new GpuGrassDensityGovernor();
            for (int i = 0; i < 30; i++) g.Tick(SLOW_DT, TARGET_FPS, FLOOR);
            float thinned = g.Density;

            for (int i = 0; i < 60; i++) g.Tick(FAST_DT, TARGET_FPS, FLOOR);
            Assert.Greater(g.Density, thinned, "Frames with headroom must restore density back up.");
        }

        [Test]
        public void Reset_ReturnsToFullDensity()
        {
            var g = new GpuGrassDensityGovernor();
            for (int i = 0; i < 30; i++) g.Tick(SLOW_DT, TARGET_FPS, FLOOR);
            Assert.Less(g.Density, 1f);

            g.Reset();
            Assert.AreEqual(1f, g.Density, 1e-6f);
        }

        [Test]
        public void FloorOfOne_DisablesThinning()
        {
            var g = new GpuGrassDensityGovernor();
            for (int i = 0; i < 50; i++) g.Tick(SLOW_DT, TARGET_FPS, 1f); // floor == 1 → no thinning allowed
            Assert.AreEqual(1f, g.Density, 1e-6f);
        }
    }
}
