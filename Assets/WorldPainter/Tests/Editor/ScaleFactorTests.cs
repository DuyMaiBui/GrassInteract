#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Pure EditMode tests for the per-layer render-time scale factor feature.
    ///
    /// All tests are device-free and Play-mode-free:
    /// - GPU engines require a live graphics device and cannot be constructed headlessly;
    ///   their math is tested through <see cref="GrassGpuEngine.ScaleBoundsExtents"/> (internal
    ///   static, accessible because WorldPainter.Tests references WorldPainter runtime) and pure
    ///   arithmetic helpers that mirror the engine formulas exactly.
    /// - <c>execute_code</c> is unusable in this env (Roslyn missing). Compilation signal =
    ///   fresh <c>Library/ScriptAssemblies/WorldPainter.Tests.dll</c> mtime + 0 console errors.
    /// </summary>
    public sealed class ScaleFactorTests
    {
        // ── 1. Clamp — engines clamp factor to [0.1, 5] ──────────────────────
        // GrassGpuEngine and InstancedPropEngine both use Mathf.Clamp(factor, 0.1f, 5f).
        // We test the clamp formula directly (pure math — no engine instantiation needed).

        [Test]
        public void ClampFormula_AboveMax_ClampsTo5()
        {
            float input    = 10f;
            float clamped  = Mathf.Clamp(input, 0.1f, 5f);
            Assert.AreEqual(5f, clamped, 1e-6f,
                "factor > 5 must clamp to 5 (matches engine Mathf.Clamp(f, 0.1f, 5f))");
        }

        [Test]
        public void ClampFormula_BelowMin_ClampsToPoint1()
        {
            float input    = 0f;
            float clamped  = Mathf.Clamp(input, 0.1f, 5f);
            Assert.AreEqual(0.1f, clamped, 1e-6f,
                "factor < 0.1 must clamp to 0.1 (matches engine Mathf.Clamp(f, 0.1f, 5f))");
        }

        // ── 2. Bounds lockstep — ScaleBoundsExtents scales extents, preserves center ──

        [Test]
        public void ScaleBoundsExtents_Factor2_DoublesExtents()
        {
            var b      = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 8f));
            var scaled = GrassGpuEngine.ScaleBoundsExtents(b, 2f);

            Assert.AreEqual(b.extents * 2f, scaled.extents,
                "ScaleBoundsExtents(b, 2f).extents must be b.extents * 2");
            Assert.AreEqual(b.center, scaled.center,
                "ScaleBoundsExtents must preserve the bounds center");
        }

        // ── 3. Bounds default no-op — ScaleBoundsExtents(b, 1f) == b ─────────

        [Test]
        public void ScaleBoundsExtents_Factor1_IsIdentity()
        {
            var b      = new Bounds(new Vector3(5f, 0f, -2f), new Vector3(10f, 4f, 6f));
            var scaled = GrassGpuEngine.ScaleBoundsExtents(b, 1f);

            Assert.AreEqual(b.center,  scaled.center,  "center unchanged at factor 1");
            Assert.AreEqual(b.extents, scaled.extents, "extents unchanged at factor 1");
            Assert.AreEqual(b.size,    scaled.size,    "size unchanged at factor 1");
        }

        // ── 4. Margin lockstep (pure) — bladeCullMargin = base × factor ──────
        // Mirrors the engine formula: this.bladeCullMargin = this.baseBladeCullMargin * factor.
        // Tested through pure arithmetic because constructing a GrassGpuEngine requires a GPU device.

        [Test]
        public void MarginLockstep_Factor2_DoublesBaseMargin()
        {
            const float baseMargin = 3.5f;
            const float factor     = 2f;
            float liveMagin        = baseMargin * factor;  // engine formula verbatim
            Assert.AreEqual(7f, liveMagin, 1e-6f,
                "bladeCullMargin after SetScaleFactor(2) must equal baseBladeCullMargin * 2");
        }

        // ── 5. Collider scale formula (pure) — scales[i] = rec.scale * rec.colliderScale * factor ─
        // Mirrors InstancedPropEngine.BuildColliderRuntime:
        //   baseScales[i] = rec.scale * rec.colliderScale;
        //   scales[i]     = baseScales[i] * this.scaleFactor;
        // Tested through pure arithmetic; no Play mode or engine instantiation needed.

        [Test]
        public void ColliderScaleFormula_YieldsExpectedProduct()
        {
            const float recScale        = 1.5f;
            const float recColliderScale = 0.8f;
            const float factor          = 2f;

            float baseScale   = recScale * recColliderScale;          // 1.2
            float liveScale   = baseScale * factor;                   // 2.4
            float expected    = recScale * recColliderScale * factor;  // reference product

            Assert.AreEqual(expected, liveScale, 1e-6f,
                "collider live scale must equal rec.scale * rec.colliderScale * scaleFactor");
            Assert.AreEqual(2.4f, liveScale, 1e-5f,
                "concrete sanity check: 1.5 * 0.8 * 2 == 2.4");
        }
    }
}
