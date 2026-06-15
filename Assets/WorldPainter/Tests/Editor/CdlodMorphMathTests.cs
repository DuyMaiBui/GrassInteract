#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for CDLOD morph blend math (host-replicated HLSL logic).
    ///
    /// Crack-free proof: morphBlend must reach 0 at bandStart and 1 at bandEnd.
    /// MorphVertex must return the same value as the coarser grid at morphBlend=1.
    /// Blend must be monotonically increasing over [morphStart, morphEnd].
    /// </summary>
    [TestFixture]
    public sealed class CdlodMorphMathTests
    {
        // ── Host-replicated HLSL math ─────────────────────────────────────────
        // Mirror of TerrainVtf.hlsl CalcMorphBlend and MorphVertex.

        private static float CalcMorphBlend(float sqrDist, float morphStart, float morphEnd)
        {
            if (morphEnd <= morphStart) return 0f;
            float t = (sqrDist - morphStart) / (morphEnd - morphStart);
            return Mathf.Clamp01(t);
        }

        private static Vector2 MorphVertex(Vector2 vertexXZ, float morphBlend)
        {
            // Mirror of TerrainPatch.shader MorphVertex (PATCH_RES-scaled formula).
            // vertexXZ is node-local UV [0,1] with step = 1/PATCH_RES.
            // Scale to integer-step space: even indices (0,2,4..PATCH_RES) are on the coarser
            // grid and remain unchanged; odd indices snap to the preceding even step.
            const float patchRes = TerrainPatchMesh.PATCH_RES;
            float viX   = vertexXZ.x * patchRes;
            float viZ   = vertexXZ.y * patchRes;
            float fracX = Mathf.Repeat(viX * 0.5f, 1f) * 2f; // 0 at even int, 1 at odd int
            float fracZ = Mathf.Repeat(viZ * 0.5f, 1f) * 2f;
            return new Vector2(
                vertexXZ.x - (fracX / patchRes) * morphBlend,
                vertexXZ.y - (fracZ / patchRes) * morphBlend);
        }

        // ── Blend range tests ─────────────────────────────────────────────────

        [Test]
        public void BlendIsZeroAtMorphStart()
        {
            float morphStart = 100f;
            float morphEnd   = 400f;
            float blend = CalcMorphBlend(morphStart, morphStart, morphEnd);
            Assert.AreEqual(0f, blend, 1e-5f, "Blend must be 0 at morphStart.");
        }

        [Test]
        public void BlendIsOneAtMorphEnd()
        {
            float morphStart = 100f;
            float morphEnd   = 400f;
            float blend = CalcMorphBlend(morphEnd, morphStart, morphEnd);
            Assert.AreEqual(1f, blend, 1e-5f, "Blend must be 1 at morphEnd.");
        }

        [Test]
        public void BlendIsZeroBelowMorphStart()
        {
            float blend = CalcMorphBlend(50f, 100f, 400f);
            Assert.AreEqual(0f, blend, 1e-5f, "Blend must clamp to 0 below morphStart.");
        }

        [Test]
        public void BlendIsOneAboveMorphEnd()
        {
            float blend = CalcMorphBlend(500f, 100f, 400f);
            Assert.AreEqual(1f, blend, 1e-5f, "Blend must clamp to 1 above morphEnd.");
        }

        [Test]
        public void BlendIsMonotonic()
        {
            float morphStart = 100f;
            float morphEnd   = 400f;
            int   steps      = 20;
            float prev       = -1f;

            for (int i = 0; i <= steps; ++i)
            {
                float d = Mathf.Lerp(morphStart, morphEnd, i / (float)steps);
                float b = CalcMorphBlend(d, morphStart, morphEnd);
                Assert.GreaterOrEqual(b, prev - 1e-6f,
                    $"Blend not monotonic: step {i}, prev={prev:F4}, current={b:F4}");
                prev = b;
            }
        }

        // ── MorphVertex tests ─────────────────────────────────────────────────

        [Test]
        public void MorphVertex_BlendZero_Unchanged()
        {
            var v = new Vector2(0.3f, 0.7f);
            var result = MorphVertex(v, 0f);
            Assert.AreEqual(v.x, result.x, 1e-5f);
            Assert.AreEqual(v.y, result.y, 1e-5f);
        }

        [Test]
        public void MorphVertex_BlendOne_EvenGridStepsUnchanged()
        {
            // Even steps (0, 2, 4...) in [0..PATCH_RES] should not move at any blend.
            // Normalized: 0, 2/16, 4/16 ... i.e. multiples of 1/8.
            for (int x = 0; x <= TerrainPatchMesh.PATCH_RES; x += 2)
            {
                float nx = x / (float)TerrainPatchMesh.PATCH_RES;
                var v = new Vector2(nx, nx);
                var result = MorphVertex(v, 1f);
                Assert.AreEqual(nx, result.x, 1e-5f,
                    $"Even-step vertex x={nx:F4} should not move at blend=1.");
            }
        }

        [Test]
        public void MorphVertex_BlendOne_OddStepSnapsToEven()
        {
            // An odd step at blend=1 should snap to the nearest even step.
            // Odd step: x=1/16. fracPart = frac(1/(2*16)) * 2 = frac(1/32) * 2 ≈ 2*(1/32) = 1/16.
            // result = 1/16 - 1/16 * 1 = 0.
            float nx = 1f / TerrainPatchMesh.PATCH_RES; // = 0.0625
            var v = new Vector2(nx, 0f);
            var result = MorphVertex(v, 1f);
            Assert.AreEqual(0f, result.x, 1e-5f,
                "Odd-step vertex at blend=1 should snap to even step (crack-free proof).");
        }

        // ── CPU-vs-HLSL height decode agreement ──────────────────────────────

        [Test]
        public void HeightDecode_CpuMatchesHlssMath()
        {
            // HLSL: worldY = minHeight + (raw / 65535.0) * (maxHeight - minHeight)
            // C# SSOT: TerrainHeightFormat.DecodeHeight
            float minH = 10f;
            float maxH = 200f;

            ushort[] testRaws = { 0, 1000, 32767, 65535 };
            foreach (ushort raw in testRaws)
            {
                float cpuResult  = TerrainHeightFormat.DecodeHeight(raw, minH, maxH);
                float hlslResult = minH + (raw / 65535f) * (maxH - minH);
                Assert.AreEqual(cpuResult, hlslResult, 0.001f,
                    $"CPU/HLSL height disagree for raw={raw}: cpu={cpuResult:F4} hlsl={hlslResult:F4}");
            }
        }
    }
}
