#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// SSOT for R16 height encoding and world-Y mapping.
    ///
    /// R16 stores height as a 16-bit unsigned integer in [0, 65535].
    /// Normalized value n = raw / 65535.0 maps to world Y via:
    ///   worldY = minHeight + n * (maxHeight - minHeight)
    ///
    /// Inverse (world Y → normalized → raw):
    ///   n      = (worldY - minHeight) / (maxHeight - minHeight)
    ///   raw    = round(clamp(n, 0, 1) * 65535)
    ///
    /// HLSL replication (Phase 1 GPU vertex shader — mirror this EXACTLY):
    ///   float n      = rawR16 / 65535.0;
    ///   float worldY = _MinHeight + n * (_MaxHeight - _MinHeight);
    ///
    /// All constants here are the canonical values; GPU uniforms _MinHeight / _MaxHeight
    /// are populated from TerrainTileAsset.minHeight / maxHeight (no separate derivation).
    /// </summary>
    public static class TerrainHeightFormat
    {
        // ── R16 encoding constants ────────────────────────────────────────────

        /// <summary>Bits per height sample. R16 = 16-bit unsigned.</summary>
        public const int HEIGHT_BITS = 16;

        /// <summary>Maximum raw R16 value (2^16 - 1).</summary>
        public const int R16_MAX_VALUE = 65535;

        /// <summary>Bytes per height sample (2 bytes = 16 bits).</summary>
        public const int BYTES_PER_SAMPLE = 2;

        /// <summary>
        /// Encode a world-Y height value to a 16-bit unsigned R16 sample.
        /// Clamps to [minHeight, maxHeight] before quantisation.
        /// </summary>
        /// <param name="worldY">World-space Y coordinate to encode.</param>
        /// <param name="minHeight">World-Y that maps to R16 = 0.</param>
        /// <param name="maxHeight">World-Y that maps to R16 = 65535.</param>
        /// <returns>Raw R16 value in [0, 65535].</returns>
        public static ushort EncodeHeight(float worldY, float minHeight, float maxHeight)
        {
            float range = maxHeight - minHeight;
            if (range <= 0f) return 0;
            float normalized = (worldY - minHeight) / range;
            normalized = Mathf.Clamp01(normalized);
            return (ushort)Mathf.RoundToInt(normalized * R16_MAX_VALUE);
        }

        /// <summary>
        /// Decode a raw R16 sample to a world-Y height value.
        /// This is the SSOT decode formula — replicated verbatim in HLSL (Phase 1 VS).
        /// </summary>
        /// <param name="raw">Raw R16 value in [0, 65535].</param>
        /// <param name="minHeight">World-Y at R16 = 0.</param>
        /// <param name="maxHeight">World-Y at R16 = 65535.</param>
        /// <returns>World-space Y height.</returns>
        public static float DecodeHeight(ushort raw, float minHeight, float maxHeight)
        {
            float normalized = raw / (float)R16_MAX_VALUE;
            return minHeight + normalized * (maxHeight - minHeight);
        }

        /// <summary>
        /// Decode from a normalized [0,1] value (GPU-side equivalent — for bilinear on CPU).
        /// </summary>
        /// <param name="normalized">Normalized height in [0,1].</param>
        /// <param name="minHeight">World-Y at normalized = 0.</param>
        /// <param name="maxHeight">World-Y at normalized = 1.</param>
        /// <returns>World-space Y height.</returns>
        public static float DecodeNormalized(float normalized, float minHeight, float maxHeight)
        {
            return minHeight + normalized * (maxHeight - minHeight);
        }

        /// <summary>
        /// Read a raw R16 value from a byte array at the given texel index.
        /// Byte layout: little-endian (low byte at index*2, high byte at index*2+1).
        /// </summary>
        /// <param name="bytes">R16 byte array (heightRes² × 2 bytes).</param>
        /// <param name="texelIndex">Flat texel index (row-major, y * width + x).</param>
        /// <returns>Raw R16 value.</returns>
        public static ushort ReadRaw(byte[] bytes, int texelIndex)
        {
            int offset = texelIndex * BYTES_PER_SAMPLE;
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        /// <summary>
        /// Write a raw R16 value into a byte array at the given texel index.
        /// Byte layout: little-endian (matches ReadRaw and Unity's R16 texture format).
        /// </summary>
        /// <param name="bytes">R16 byte array (heightRes² × 2 bytes).</param>
        /// <param name="texelIndex">Flat texel index.</param>
        /// <param name="raw">Raw R16 value to write.</param>
        public static void WriteRaw(byte[] bytes, int texelIndex, ushort raw)
        {
            int offset = texelIndex * BYTES_PER_SAMPLE;
            bytes[offset]     = (byte)(raw & 0xFF);
            bytes[offset + 1] = (byte)(raw >> 8);
        }
    }
}
