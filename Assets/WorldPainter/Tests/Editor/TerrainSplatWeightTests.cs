#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Host-replicated tests for the weight-normalize logic in TerrainSplat.hlsl.
    ///
    /// SSOT channel→layer (TerrainTileAsset):
    ///   R = layer 0 (base ground)
    ///   G = layer 1 (grass)
    ///   B = layer 2 (rock)
    ///   A = layer 3 (path)
    ///
    /// Covers:
    ///   - Normalised weights always sum to 1.
    ///   - A 100%-channel input selects exactly that layer.
    ///   - Zero-weight fallback returns full weight on layer 0.
    ///   - Partial weights are scaled correctly.
    /// </summary>
    [TestFixture]
    public sealed class TerrainSplatWeightTests
    {
        // ── Host-replicated HLSL NormaliseSplatWeights ────────────────────────
        // Mirrors SampleSplatWeights() in TerrainSplat.hlsl (minus the texture sample).

        private static Vector4 NormalizeWeights(Vector4 raw)
        {
            float total = raw.x + raw.y + raw.z + raw.w;
            if (total < 1e-5f)
                return new Vector4(1, 0, 0, 0);
            return new Vector4(raw.x / total, raw.y / total, raw.z / total, raw.w / total);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void NormalizedWeights_SumToOne_AllEqualWeights()
        {
            var raw      = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            var result   = NormalizeWeights(raw);
            float sum    = result.x + result.y + result.z + result.w;
            Assert.AreEqual(1f, sum, 1e-5f, "Normalised weights must sum to 1.");
        }

        [Test]
        public void NormalizedWeights_SumToOne_MixedWeights()
        {
            var raw    = new Vector4(0.8f, 0.4f, 0.2f, 0.1f);
            var result = NormalizeWeights(raw);
            float sum  = result.x + result.y + result.z + result.w;
            Assert.AreEqual(1f, sum, 1e-5f, "Normalised weights must sum to 1 for mixed input.");
        }

        [Test]
        public void NormalizedWeights_ZeroSum_FallsBackToLayer0()
        {
            var raw    = new Vector4(0, 0, 0, 0);
            var result = NormalizeWeights(raw);
            Assert.AreEqual(1f, result.x, 1e-5f, "Zero-sum fallback must assign full weight to layer 0 (R channel).");
            Assert.AreEqual(0f, result.y, 1e-5f);
            Assert.AreEqual(0f, result.z, 1e-5f);
            Assert.AreEqual(0f, result.w, 1e-5f);
        }

        [TestCase(1f, 0f, 0f, 0f, 0)]  // R = layer 0 (base ground)
        [TestCase(0f, 1f, 0f, 0f, 1)]  // G = layer 1 (grass)
        [TestCase(0f, 0f, 1f, 0f, 2)]  // B = layer 2 (rock)
        [TestCase(0f, 0f, 0f, 1f, 3)]  // A = layer 3 (path)
        public void NormalizedWeights_100PercentChannel_SelectsExactLayer(
            float r, float g, float b, float a, int expectedLayerIndex)
        {
            var raw    = new Vector4(r, g, b, a);
            var result = NormalizeWeights(raw);
            float[] weights = { result.x, result.y, result.z, result.w };

            Assert.AreEqual(1f, weights[expectedLayerIndex], 1e-5f,
                $"100%% channel at index {expectedLayerIndex} must yield weight=1 for that layer.");

            for (int i = 0; i < 4; i++)
            {
                if (i != expectedLayerIndex)
                    Assert.AreEqual(0f, weights[i], 1e-5f,
                        $"Layer {i} weight must be 0 when channel {expectedLayerIndex} is 100%%.");
            }
        }

        [Test]
        public void NormalizedWeights_AllChannelsNonZero_EachWeightInRange()
        {
            var raw    = new Vector4(0.3f, 0.1f, 0.5f, 0.2f);
            var result = NormalizeWeights(raw);
            float[] weights = { result.x, result.y, result.z, result.w };

            foreach (float w in weights)
                Assert.IsTrue(w >= 0f && w <= 1f, $"Each normalised weight must be in [0,1], got {w:F4}.");
        }

        [Test]
        public void NormalizedWeights_SplitTwoChannels_CorrectRatio()
        {
            // R=1, B=1 → each should be 0.5 after normalisation.
            var raw    = new Vector4(1f, 0f, 1f, 0f);
            var result = NormalizeWeights(raw);
            Assert.AreEqual(0.5f, result.x, 1e-5f, "Equal R and B should normalise to 0.5 each.");
            Assert.AreEqual(0.5f, result.z, 1e-5f);
        }
    }
}
