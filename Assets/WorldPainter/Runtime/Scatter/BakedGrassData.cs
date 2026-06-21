#nullable enable
using System;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Sub-asset <see cref="ScriptableObject"/> storing a fully baked grass scatter as flat byte blobs.
    /// Created by <c>WorldGrassBaker</c> in the editor; consumed at runtime by
    /// <see cref="ChunkedBladeBuffer.LoadFromBaked"/> to skip the main-thread scatter.
    ///
    /// V1 blob layout (SSOT for strides — any change MUST increment VERSION and update unpack):
    ///   <c>bladeBlob</c>: <c>[VERSION=1]</c> + <see cref="BladeInstance"/>[] @ 20 B each
    ///                     (float3 posWS=12 B, uint packedYawScale=4 B, uint hash=4 B)
    ///   <c>aabbBlob</c>:  <c>[VERSION=1]</c> + <see cref="ChunkAabb"/>[] @ 24 B each
    ///                     (float3 min=12 B, float3 max=12 B)
    ///   <c>rangeBlob</c>: <c>[VERSION=1]</c> + <see cref="ChunkRange"/>[] @ 8 B each
    ///                     (uint start=4 B, uint count=4 B)
    ///
    /// Any blob whose first byte ≠ 1 is rejected with an exception — no silent migration.
    /// </summary>
    public sealed class BakedGrassData : ScriptableObject
    {
        // ── Blob version ──────────────────────────────────────────────────────
        public const byte VERSION = 1;

        // ── Stride constants (SSOT — shared with ChunkedBladeBuffer) ─────────
        // Verified in BakedGrassDataBlobTests.BladeStride_Is20_AabbStride_Is24_RangeStride_Is8.
        public const int BLADE_STRIDE = 20; // float3(12) + uint(4) + uint(4)
        public const int AABB_STRIDE  = 24; // float3(12) + float3(12)
        public const int RANGE_STRIDE =  8; // uint(4)    + uint(4)

        // ── Grid metadata ─────────────────────────────────────────────────────

        [Tooltip("Baked grid width (cells along X). Mirrors ChunkedBladeBuffer.GridX.")]
        [SerializeField] public int GridX;

        [Tooltip("Baked grid depth (cells along Z). Mirrors ChunkedBladeBuffer.GridZ.")]
        [SerializeField] public int GridZ;

        [Tooltip("World-space cell size in metres. Mirrors ChunkedBladeBuffer.ChunkSize.")]
        [SerializeField] public int ChunkSize;

        [Tooltip("Total grid cells = GridX × GridZ.")]
        [SerializeField] public int TotalChunks;

        [Tooltip("Total baked blades.")]
        [SerializeField] public int TotalBlades;

        [Tooltip("Scale encode upper-bound (= ScatterLayer.ScaleRange.y). " +
                 "Must match _ScaleMax2 shader uniform.")]
        [SerializeField] public float ScaleMax2;

        [Tooltip("Whether oriented slot2 packing was used during bake " +
                 "(true = octNormal|pitch8|roll8; false = XorShift32 hash).")]
        [SerializeField] public bool Oriented;

        [Tooltip("Conservative world-space bounds of the baked grass field.")]
        [SerializeField] public Bounds WorldBounds;

        // ── Byte blobs ────────────────────────────────────────────────────────

        [Tooltip("V1 binary blob of BladeInstance array " +
                 "(1-byte version header + N×20B records). Do NOT hand-edit.")]
        [SerializeField] public byte[] bladeBlob = Array.Empty<byte>();

        [Tooltip("V1 binary blob of ChunkAabb array " +
                 "(1-byte version header + N×24B records). Do NOT hand-edit.")]
        [SerializeField] public byte[] aabbBlob = Array.Empty<byte>();

        [Tooltip("V1 binary blob of ChunkRange array " +
                 "(1-byte version header + N×8B records). Do NOT hand-edit.")]
        [SerializeField] public byte[] rangeBlob = Array.Empty<byte>();

        // ── Pack API (editor-only callable) ──────────────────────────────────

        /// <summary>
        /// Packs <paramref name="blades"/>, <paramref name="aabbs"/>, <paramref name="ranges"/>,
        /// and the supplied grid metadata into this asset's byte blobs.
        /// Mirrors the <c>AuthoredInstancesData.PackBlob</c> approach: version header byte +
        /// flat little-endian fields, no padding, no managed-boundary surprises.
        /// </summary>
        public void Pack(
            BladeInstance[] blades, ChunkAabb[] aabbs, ChunkRange[] ranges,
            int gridX, int gridZ, int chunkSize, int totalChunks, int totalBlades,
            float scaleMax2, bool oriented, Bounds worldBounds)
        {
            this.GridX        = gridX;
            this.GridZ        = gridZ;
            this.ChunkSize    = chunkSize;
            this.TotalChunks  = totalChunks;
            this.TotalBlades  = totalBlades;
            this.ScaleMax2    = scaleMax2;
            this.Oriented     = oriented;
            this.WorldBounds  = worldBounds;

            this.bladeBlob = PackBlades(blades);
            this.aabbBlob  = PackAabbs(aabbs);
            this.rangeBlob = PackRanges(ranges);
        }

        // ── Unpack API ────────────────────────────────────────────────────────

        /// <summary>
        /// Unpacks all three blobs back into managed arrays. Throws if any blob version ≠ 1.
        /// Returns (blades, aabbs, ranges) byte-identically to what <see cref="Pack"/> wrote.
        /// </summary>
        public (BladeInstance[] blades, ChunkAabb[] aabbs, ChunkRange[] ranges) Unpack()
        {
            return (UnpackBlades(this.bladeBlob),
                    UnpackAabbs(this.aabbBlob),
                    UnpackRanges(this.rangeBlob));
        }

        // ── Blade blob ────────────────────────────────────────────────────────

        private static byte[] PackBlades(BladeInstance[] blades)
        {
            // 1-byte version header + N × BLADE_STRIDE bytes.
            int n   = blades.Length;
            var buf = new byte[1 + n * BLADE_STRIDE];
            int off = 0;
            buf[off++] = VERSION;
            for (int i = 0; i < n; ++i)
            {
                BladeInstance b = blades[i];
                WriteFloat(buf, ref off, b.posWS.x);
                WriteFloat(buf, ref off, b.posWS.y);
                WriteFloat(buf, ref off, b.posWS.z);
                WriteUInt (buf, ref off, b.packedYawScale);
                WriteUInt (buf, ref off, b.hash);
            }
            return buf;
        }

        private static BladeInstance[] UnpackBlades(byte[] blob)
        {
            AssertVersion(blob, nameof(bladeBlob));
            int n   = (blob.Length - 1) / BLADE_STRIDE;
            var arr = new BladeInstance[n];
            int off = 1;
            for (int i = 0; i < n; ++i)
            {
                arr[i] = new BladeInstance
                {
                    posWS          = new Vector3(ReadFloat(blob, ref off),
                                                 ReadFloat(blob, ref off),
                                                 ReadFloat(blob, ref off)),
                    packedYawScale = ReadUInt(blob, ref off),
                    hash           = ReadUInt(blob, ref off),
                };
            }
            return arr;
        }

        // ── AABB blob ─────────────────────────────────────────────────────────

        private static byte[] PackAabbs(ChunkAabb[] aabbs)
        {
            int n   = aabbs.Length;
            var buf = new byte[1 + n * AABB_STRIDE];
            int off = 0;
            buf[off++] = VERSION;
            for (int i = 0; i < n; ++i)
            {
                ChunkAabb a = aabbs[i];
                WriteFloat(buf, ref off, a.min.x);
                WriteFloat(buf, ref off, a.min.y);
                WriteFloat(buf, ref off, a.min.z);
                WriteFloat(buf, ref off, a.max.x);
                WriteFloat(buf, ref off, a.max.y);
                WriteFloat(buf, ref off, a.max.z);
            }
            return buf;
        }

        private static ChunkAabb[] UnpackAabbs(byte[] blob)
        {
            AssertVersion(blob, nameof(aabbBlob));
            int n   = (blob.Length - 1) / AABB_STRIDE;
            var arr = new ChunkAabb[n];
            int off = 1;
            for (int i = 0; i < n; ++i)
            {
                arr[i] = new ChunkAabb
                {
                    min = new Vector3(ReadFloat(blob, ref off),
                                      ReadFloat(blob, ref off),
                                      ReadFloat(blob, ref off)),
                    max = new Vector3(ReadFloat(blob, ref off),
                                      ReadFloat(blob, ref off),
                                      ReadFloat(blob, ref off)),
                };
            }
            return arr;
        }

        // ── Range blob ────────────────────────────────────────────────────────

        private static byte[] PackRanges(ChunkRange[] ranges)
        {
            int n   = ranges.Length;
            var buf = new byte[1 + n * RANGE_STRIDE];
            int off = 0;
            buf[off++] = VERSION;
            for (int i = 0; i < n; ++i)
            {
                ChunkRange r = ranges[i];
                WriteUInt(buf, ref off, r.start);
                WriteUInt(buf, ref off, r.count);
            }
            return buf;
        }

        private static ChunkRange[] UnpackRanges(byte[] blob)
        {
            AssertVersion(blob, nameof(rangeBlob));
            int n   = (blob.Length - 1) / RANGE_STRIDE;
            var arr = new ChunkRange[n];
            int off = 1;
            for (int i = 0; i < n; ++i)
            {
                arr[i] = new ChunkRange
                {
                    start = ReadUInt(blob, ref off),
                    count = ReadUInt(blob, ref off),
                };
            }
            return arr;
        }

        // ── Version guard ─────────────────────────────────────────────────────

        private static void AssertVersion(byte[] blob, string blobName)
        {
            if (blob == null || blob.Length == 0)
                throw new InvalidOperationException(
                    $"[BakedGrassData] {blobName} is empty — bake has not been run.");
            byte v = blob[0];
            if (v != VERSION)
                throw new InvalidOperationException(
                    $"[BakedGrassData] {blobName} has unsupported version {v}. " +
                    $"Only V{VERSION} is supported. Re-bake the grass layer.");
        }

        // ── Blob serialization helpers (host-endian, symmetric — bake & load always run on the ──
        // ── same device so byte order is guaranteed identical. Not portable across endianness.) ──

        private static void WriteFloat(byte[] buf, ref int off, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            buf[off++] = b[0];
            buf[off++] = b[1];
            buf[off++] = b[2];
            buf[off++] = b[3];
        }

        private static void WriteUInt(byte[] buf, ref int off, uint value)
        {
            buf[off++] = (byte)(value & 0xFF);
            buf[off++] = (byte)((value >>  8) & 0xFF);
            buf[off++] = (byte)((value >> 16) & 0xFF);
            buf[off++] = (byte)((value >> 24) & 0xFF);
        }

        private static float ReadFloat(byte[] buf, ref int off)
        {
            float v = BitConverter.ToSingle(buf, off);
            off += 4;
            return v;
        }

        private static uint ReadUInt(byte[] buf, ref int off)
        {
            uint v = (uint)(buf[off] | (buf[off + 1] << 8) |
                            (buf[off + 2] << 16) | (buf[off + 3] << 24));
            off += 4;
            return v;
        }
    }
}
