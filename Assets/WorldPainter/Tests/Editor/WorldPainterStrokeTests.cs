#nullable enable
using NUnit.Framework;
using GpuTerrain.Editor;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Deterministic math tests for the spacing-stamping stroke model (task 7).
    ///
    /// Tests the <see cref="WorldPainterStroke.CountStamps"/> helper which is the
    /// pure-math core used by <see cref="WorldPainterStroke.Advance"/>.
    /// Validates: stamp count, accumulator residual, consistency across speeds.
    /// </summary>
    [TestFixture]
    public class WorldPainterStrokeTests
    {
        // ── CountStamps helper ────────────────────────────────────────────────

        [Test]
        public void CountStamps_ExactMultiple_ReturnsCorrectCount()
        {
            // 10m drag / 2m spacing = 5 stamps, 0 residual.
            int stamps = WorldPainterStroke.CountStamps(10f, 2f, 0f, out float end);
            Assert.AreEqual(5, stamps, "Exactly 5 stamps expected");
            Assert.AreEqual(0f, end, 0.0001f, "No residual after exact multiple");
        }

        [Test]
        public void CountStamps_NonMultiple_CorrectResidual()
        {
            // 11m / 2m = 5 stamps + 1m residual.
            int stamps = WorldPainterStroke.CountStamps(11f, 2f, 0f, out float end);
            Assert.AreEqual(5, stamps);
            Assert.AreEqual(1f, end, 0.0001f, "Residual should be 1m");
        }

        [Test]
        public void CountStamps_WithStartAccum_AccountsForExisting()
        {
            // Start with 1.5m already accumulated; add 0.75m → total 2.25m at 2m spacing.
            int stamps = WorldPainterStroke.CountStamps(0.75f, 2f, 1.5f, out float end);
            Assert.AreEqual(1, stamps, "One stamp emitted when accumulator crosses spacing");
            Assert.AreEqual(0.25f, end, 0.0001f, "Residual = 2.25 - 2 = 0.25m");
        }

        [Test]
        public void CountStamps_ShortDrag_NoStamps()
        {
            // 0.5m drag / 2m spacing = no stamps yet.
            int stamps = WorldPainterStroke.CountStamps(0.5f, 2f, 0f, out float end);
            Assert.AreEqual(0, stamps);
            Assert.AreEqual(0.5f, end, 0.0001f, "Residual should be the dragged distance");
        }

        [Test]
        public void CountStamps_MultipleFrames_ConsistentTotal()
        {
            // Simulate 4 frames, each advancing 1m; spacing = 2m.
            // Total distance = 4m → should yield exactly 2 stamps total.
            float accum = 0f;
            int total = 0;
            for (int i = 0; i < 4; ++i)
            {
                total += WorldPainterStroke.CountStamps(1f, 2f, accum, out accum);
            }
            Assert.AreEqual(2, total, "Two stamps for 4m total at 2m spacing");
        }

        [Test]
        public void CountStamps_SpeedIndependence_SameResultAsFastDrag()
        {
            // 10 slow frames of 1m vs 1 fast frame of 10m should yield same total stamps.
            float spacing = 3f;

            // Slow: 10 × 1m.
            float slowAccum = 0f;
            int slowTotal = 0;
            for (int i = 0; i < 10; ++i)
                slowTotal += WorldPainterStroke.CountStamps(1f, spacing, slowAccum, out slowAccum);

            // Fast: 1 × 10m.
            int fastTotal = WorldPainterStroke.CountStamps(10f, spacing, 0f, out float fastEnd);

            Assert.AreEqual(fastTotal, slowTotal,
                "Spacing-stamping must be speed-independent");
        }

        [Test]
        public void CountStamps_VerySmallSpacing_NoCrash()
        {
            // Minimum spacing guard: 0.001m.
            int stamps = WorldPainterStroke.CountStamps(1f, 0.0001f, 0f, out float end);
            Assert.GreaterOrEqual(stamps, 0, "Should not crash on tiny spacing");
        }

        // ── WorldPainterStroke instance (Advance) ─────────────────────────────

        [Test]
        public void Advance_EmitsCorrectStamps_AlongPath()
        {
            var stroke = new WorldPainterStroke();
            var start  = new UnityEngine.Vector3(0f, 0f, 0f);
            var end    = new UnityEngine.Vector3(10f, 0f, 0f);

            stroke.Begin(start);

            int count = 0;
            int emitted = stroke.Advance(end, spacingM: 2f, flow: 0.8f,
                (pos, flow) => ++count);

            // 10m at 2m spacing → 5 stamps.
            Assert.AreEqual(5, emitted, "Advance should emit 5 stamps for 10m at 2m spacing");
            Assert.AreEqual(5, count,   "onStamp callback should be called 5 times");
        }

        [Test]
        public void Advance_InStroke_TrueAfterBegin_FalseAfterEnd()
        {
            var stroke = new WorldPainterStroke();
            Assert.IsFalse(stroke.InStroke, "Initially not in stroke");

            stroke.Begin(UnityEngine.Vector3.zero);
            Assert.IsTrue(stroke.InStroke, "Should be in stroke after Begin");

            stroke.End();
            Assert.IsFalse(stroke.InStroke, "Should not be in stroke after End");
        }

        [Test]
        public void Advance_OutsideStroke_ReturnsZero()
        {
            var stroke = new WorldPainterStroke();
            // Not calling Begin first.
            int emitted = stroke.Advance(
                UnityEngine.Vector3.one, 2f, 1f, (_, __) => { });
            Assert.AreEqual(0, emitted, "Advance outside stroke returns 0");
        }
    }
}
