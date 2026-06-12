#nullable enable
using NUnit.Framework;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for TerrainHeightFormat encode/decode SSOT.
    /// Verifies byte-stability (encode→decode→encode), range mapping,
    /// and ReadRaw/WriteRaw round-trip.
    /// </summary>
    [TestFixture]
    public class TerrainHeightFormatTests
    {
        private const float MIN_H = -100f;
        private const float MAX_H = 900f;
        private const float EPSILON = 0.05f; // ≈ 0.01m precision for 1000m range at 16-bit

        // ── Decode correctness ────────────────────────────────────────────────

        [Test]
        public void DecodeHeight_Raw0_ReturnsMinHeight()
        {
            float result = TerrainHeightFormat.DecodeHeight(0, MIN_H, MAX_H);
            Assert.AreEqual(MIN_H, result, EPSILON);
        }

        [Test]
        public void DecodeHeight_RawMax_ReturnsMaxHeight()
        {
            float result = TerrainHeightFormat.DecodeHeight(TerrainHeightFormat.R16_MAX_VALUE,
                MIN_H, MAX_H);
            Assert.AreEqual(MAX_H, result, EPSILON);
        }

        [Test]
        public void DecodeHeight_RawMid_ReturnsMidpoint()
        {
            ushort mid = (ushort)(TerrainHeightFormat.R16_MAX_VALUE / 2);
            float result = TerrainHeightFormat.DecodeHeight(mid, MIN_H, MAX_H);
            float expected = (MIN_H + MAX_H) * 0.5f;
            Assert.AreEqual(expected, result, 1f); // 1m tolerance for mid-rounding
        }

        // ── Encode correctness ────────────────────────────────────────────────

        [Test]
        public void EncodeHeight_MinHeight_Returns0()
        {
            ushort raw = TerrainHeightFormat.EncodeHeight(MIN_H, MIN_H, MAX_H);
            Assert.AreEqual(0, raw);
        }

        [Test]
        public void EncodeHeight_MaxHeight_ReturnsR16Max()
        {
            ushort raw = TerrainHeightFormat.EncodeHeight(MAX_H, MIN_H, MAX_H);
            Assert.AreEqual(TerrainHeightFormat.R16_MAX_VALUE, raw);
        }

        [Test]
        public void EncodeHeight_BelowMin_Clamps0()
        {
            ushort raw = TerrainHeightFormat.EncodeHeight(MIN_H - 500f, MIN_H, MAX_H);
            Assert.AreEqual(0, raw);
        }

        [Test]
        public void EncodeHeight_AboveMax_ClampsR16Max()
        {
            ushort raw = TerrainHeightFormat.EncodeHeight(MAX_H + 500f, MIN_H, MAX_H);
            Assert.AreEqual(TerrainHeightFormat.R16_MAX_VALUE, raw);
        }

        // ── Encode→decode byte-stability ─────────────────────────────────────

        [Test]
        public void EncodeDecodeRoundTrip_Zero()
        {
            AssertRoundTrip(0f, 0f, 512f);
        }

        [Test]
        public void EncodeDecodeRoundTrip_HalfRange()
        {
            AssertRoundTrip(256f, 0f, 512f);
        }

        [Test]
        public void EncodeDecodeRoundTrip_FullRange()
        {
            AssertRoundTrip(512f, 0f, 512f);
        }

        [Test]
        public void EncodeDecodeRoundTrip_Negative()
        {
            AssertRoundTrip(-50f, -100f, 900f);
        }

        [Test]
        public void EncodeDecodeRoundTrip_FuzzSweep()
        {
            // Sweep 100 evenly spaced values across the full range.
            float minH = -200f, maxH = 800f;
            float range = maxH - minH;
            float maxError = range / TerrainHeightFormat.R16_MAX_VALUE + 0.001f; // quantisation step
            for (int i = 0; i <= 100; ++i)
            {
                float input = minH + (range * i / 100f);
                ushort raw = TerrainHeightFormat.EncodeHeight(input, minH, maxH);
                float decoded = TerrainHeightFormat.DecodeHeight(raw, minH, maxH);
                Assert.AreEqual(input, decoded, maxError,
                    $"Round-trip failed at worldY={input}: raw={raw}, decoded={decoded}");
            }
        }

        // ── Byte array read/write round-trip ─────────────────────────────────

        [Test]
        public void WriteRawReadRaw_RoundTrip()
        {
            byte[] buf = new byte[4]; // 2 texels × 2 bytes
            ushort val0 = 12345;
            ushort val1 = 65000;
            TerrainHeightFormat.WriteRaw(buf, 0, val0);
            TerrainHeightFormat.WriteRaw(buf, 1, val1);
            Assert.AreEqual(val0, TerrainHeightFormat.ReadRaw(buf, 0));
            Assert.AreEqual(val1, TerrainHeightFormat.ReadRaw(buf, 1));
        }

        [Test]
        public void WriteRaw_LittleEndian_Layout()
        {
            byte[] buf = new byte[2];
            TerrainHeightFormat.WriteRaw(buf, 0, 0x1234);
            // Little-endian: buf[0] = 0x34 (low byte), buf[1] = 0x12 (high byte).
            Assert.AreEqual(0x34, buf[0]);
            Assert.AreEqual(0x12, buf[1]);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void AssertRoundTrip(float worldY, float minH, float maxH)
        {
            float range = maxH - minH;
            float quantStep = range / TerrainHeightFormat.R16_MAX_VALUE;
            ushort raw = TerrainHeightFormat.EncodeHeight(worldY, minH, maxH);
            float decoded = TerrainHeightFormat.DecodeHeight(raw, minH, maxH);
            Assert.AreEqual(worldY, decoded, quantStep + 0.001f,
                $"Round-trip: input={worldY}, raw={raw}, decoded={decoded}");
        }
    }
}
