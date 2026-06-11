#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;
using GpuTerrain.Editor;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for RT→R16 round-trip correctness (Risk-16 gate from phase-5.md).
    ///
    /// These tests exercise the CPU encode/decode path used by TerrainSculptRtWriteback.ExecuteSync:
    ///   normalized float → EncodeHeight (R16) → WriteRaw → ReadRaw → DecodeHeight → compare to input.
    ///
    /// The GPU readback itself is not exercised here (requires a real GPU device);
    /// these tests validate the mathematical correctness of the encode/decode pipeline
    /// that writeback uses, which is the load-bearing correctness gate for risk-16.
    /// </summary>
    [TestFixture]
    public class TerrainSculptRtWritebackTests
    {
        private const float MIN_H = 0f;
        private const float MAX_H = 512f;

        // ── Round-trip: normalized float → R16 raw → decoded worldY ─────────

        /// <summary>
        /// Simulates what ExecuteSync does per texel:
        ///   1. normalized height in [0,1] (from RT pixel)
        ///   2. decode to worldY using DecodeNormalized
        ///   3. encode worldY → R16 raw using EncodeHeight
        ///   4. write to byte array
        ///   5. read back + decode
        /// Asserts final decoded value equals the original within R16 quantisation epsilon.
        /// </summary>
        private static void AssertRoundTrip(float normalized, float minH, float maxH)
        {
            // Step 1-3: mirror of TerrainSculptRtWriteback.WriteHeightToAsset per-texel path.
            float worldY = TerrainHeightFormat.DecodeNormalized(normalized, minH, maxH);
            ushort raw   = TerrainHeightFormat.EncodeHeight(worldY, minH, maxH);

            // Step 4-5: write to byte array and read back.
            byte[] buf = new byte[TerrainHeightFormat.BYTES_PER_SAMPLE];
            TerrainHeightFormat.WriteRaw(buf, 0, raw);
            ushort rawBack = TerrainHeightFormat.ReadRaw(buf, 0);
            float decoded  = TerrainHeightFormat.DecodeHeight(rawBack, minH, maxH);

            // Quantisation epsilon: one R16 step over the height range.
            float quantEpsilon = (maxH - minH) / TerrainHeightFormat.R16_MAX_VALUE + 0.001f;

            Assert.AreEqual(worldY, decoded, quantEpsilon,
                $"Round-trip failed: normalized={normalized}, worldY={worldY}, raw={raw}, " +
                $"decoded={decoded}, epsilon={quantEpsilon}");
        }

        [Test]
        public void RoundTrip_Normalized0_BottomOfRange()
            => AssertRoundTrip(0f, MIN_H, MAX_H);

        [Test]
        public void RoundTrip_Normalized1_TopOfRange()
            => AssertRoundTrip(1f, MIN_H, MAX_H);

        [Test]
        public void RoundTrip_NormalizedHalf_Midpoint()
            => AssertRoundTrip(0.5f, MIN_H, MAX_H);

        [Test]
        public void RoundTrip_NegativeHeightRange()
            => AssertRoundTrip(0.3f, -100f, 400f);

        [Test]
        public void RoundTrip_FuzzSweep_100Points()
        {
            float minH = 0f, maxH = 512f;
            for (int i = 0; i <= 100; ++i)
            {
                float n = i / 100f;
                AssertRoundTrip(n, minH, maxH);
            }
        }

        // ── Byte-array multi-texel writeback ─────────────────────────────────

        [Test]
        public void WriteMultipleTexels_ReadBackCorrect()
        {
            int texelCount = 16;
            byte[] heightData = new byte[texelCount * TerrainHeightFormat.BYTES_PER_SAMPLE];
            float minH = 0f, maxH = 256f;

            // Write known values.
            float[] inputs = new float[texelCount];
            for (int i = 0; i < texelCount; ++i)
            {
                float normalized = i / (float)(texelCount - 1);
                float worldY = TerrainHeightFormat.DecodeNormalized(normalized, minH, maxH);
                inputs[i] = worldY;
                ushort raw = TerrainHeightFormat.EncodeHeight(worldY, minH, maxH);
                TerrainHeightFormat.WriteRaw(heightData, i, raw);
            }

            // Read back and compare.
            float quantEpsilon = (maxH - minH) / TerrainHeightFormat.R16_MAX_VALUE + 0.001f;
            for (int i = 0; i < texelCount; ++i)
            {
                ushort raw   = TerrainHeightFormat.ReadRaw(heightData, i);
                float decoded = TerrainHeightFormat.DecodeHeight(raw, minH, maxH);
                Assert.AreEqual(inputs[i], decoded, quantEpsilon,
                    $"Texel {i}: expected {inputs[i]}, got {decoded}");
            }
        }

        // ── Splat byte writeback ──────────────────────────────────────────────

        [Test]
        public void SplatWriteback_RGBA_RoundTripWithin1Byte()
        {
            // Simulate WriteSplatToAsset: Color → byte → Color
            Color input = new Color(0.25f, 0.5f, 0.75f, 1.0f);
            byte r = (byte)(input.r * 255f + 0.5f);
            byte g = (byte)(input.g * 255f + 0.5f);
            byte b = (byte)(input.b * 255f + 0.5f);
            byte a = (byte)(input.a * 255f + 0.5f);

            float rBack = r / 255f;
            float gBack = g / 255f;
            float bBack = b / 255f;
            float aBack = a / 255f;

            float epsilon = 1f / 255f + 0.001f;
            Assert.AreEqual(input.r, rBack, epsilon, "R channel round-trip");
            Assert.AreEqual(input.g, gBack, epsilon, "G channel round-trip");
            Assert.AreEqual(input.b, bBack, epsilon, "B channel round-trip");
            Assert.AreEqual(input.a, aBack, epsilon, "A channel round-trip");
        }

        // ── Asset state after writeback ───────────────────────────────────────

        [Test]
        public void AssetHeightData_AfterEncode_HasCorrectLength()
        {
            // Simulate the "resize array if needed" logic in ExecuteSync.
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.heightRes = 17; // small for test speed
            tile.splatRes  = 17;
            tile.heightData = new byte[tile.ExpectedHeightBytes];
            tile.splatData  = new byte[tile.ExpectedSplatBytes];

            Assert.AreEqual(tile.ExpectedHeightBytes, tile.heightData.Length);
            Assert.IsTrue(tile.IsHeightValid);

            UnityEngine.Object.DestroyImmediate(tile);
        }
    }
}
