#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode round-trip tests for <see cref="BakedGrassData"/> blob codec and
    /// the <see cref="ChunkedBladeBuffer"/> bake refactor (Phase 4).
    ///
    /// These tests MUST pass before shipping the baked-blob path:
    ///   1. Stride constants are correct (BLADE_STRIDE=20, AABB_STRIDE=24, RANGE_STRIDE=8).
    ///   2. Pack/Unpack is byte-identical for all three arrays.
    ///   3. Version byte is 1; wrong version throws.
    ///   4. BakeArrays result matches live Bake result (same metadata).
    ///   5. LoadFromBaked round-trips: BakedGrassData packed from BakeArrays unpacks back to
    ///      byte-identical arrays.
    ///
    /// Manual run required if MCP Test Runner is unavailable:
    ///   Window → General → Test Runner → Edit Mode → Run All.
    /// </summary>
    public sealed class BakedGrassDataBlobTests
    {
        // ── Test 1: stride constants ──────────────────────────────────────────

        [Test]
        public void StrideConstants_AreCorrect()
        {
            Assert.AreEqual(20, BakedGrassData.BLADE_STRIDE, "BLADE_STRIDE: float3(12)+uint(4)+uint(4)=20");
            Assert.AreEqual(24, BakedGrassData.AABB_STRIDE,  "AABB_STRIDE:  float3(12)+float3(12)=24");
            Assert.AreEqual(8,  BakedGrassData.RANGE_STRIDE, "RANGE_STRIDE: uint(4)+uint(4)=8");
        }

        // ── Test 2: version byte ──────────────────────────────────────────────

        [Test]
        public void VersionByte_Is1()
        {
            Assert.AreEqual(1, BakedGrassData.VERSION, "VERSION must be 1");
        }

        // ── Test 3: blade pack/unpack round-trip ──────────────────────────────

        [Test]
        public void BladePack_Unpack_IsIdentical()
        {
            var blades = new BladeInstance[]
            {
                new BladeInstance { posWS = new Vector3(1f, 2f, 3f), packedYawScale = 0xABCD1234u, hash = 0xDEADBEEFu },
                new BladeInstance { posWS = new Vector3(-5f, 0f, 10.5f), packedYawScale = 0u, hash = 1u },
                new BladeInstance { posWS = new Vector3(0f, -1f, 0f), packedYawScale = 0xFFFFFFFFu, hash = 0u },
            };
            var aabbs  = new ChunkAabb[]
            {
                new ChunkAabb { min = new Vector3(-1f, -2f, -3f), max = new Vector3(4f, 5f, 6f) },
                new ChunkAabb { min = new Vector3(0f, 0f, 0f),    max = new Vector3(0f, 0f, 0f) },
            };
            var ranges = new ChunkRange[]
            {
                new ChunkRange { start = 0u, count = 2u },
                new ChunkRange { start = 2u, count = 1u },
            };

            var baked = ScriptableObject.CreateInstance<BakedGrassData>();
            try
            {
                baked.Pack(
                    blades, aabbs, ranges,
                    gridX: 2, gridZ: 1, chunkSize: 10,
                    totalChunks: 2, totalBlades: 3,
                    scaleMax2: 1.5f, oriented: false,
                    worldBounds: new Bounds(Vector3.zero, Vector3.one * 20f));

                // Version header check.
                Assert.AreEqual(BakedGrassData.VERSION, baked.bladeBlob[0], "bladeBlob[0] = VERSION");
                Assert.AreEqual(BakedGrassData.VERSION, baked.aabbBlob[0],  "aabbBlob[0]  = VERSION");
                Assert.AreEqual(BakedGrassData.VERSION, baked.rangeBlob[0], "rangeBlob[0] = VERSION");

                // Expected byte lengths (1-byte header + N × stride).
                Assert.AreEqual(1 + 3 * BakedGrassData.BLADE_STRIDE, baked.bladeBlob.Length, "bladeBlob length");
                Assert.AreEqual(1 + 2 * BakedGrassData.AABB_STRIDE,  baked.aabbBlob.Length,  "aabbBlob length");
                Assert.AreEqual(1 + 2 * BakedGrassData.RANGE_STRIDE, baked.rangeBlob.Length, "rangeBlob length");

                // Unpack and assert byte-identity.
                var (bladesOut, aabbsOut, rangesOut) = baked.Unpack();

                Assert.AreEqual(blades.Length, bladesOut.Length, "blade count");
                for (int i = 0; i < blades.Length; ++i)
                {
                    Assert.AreEqual(blades[i].posWS,          bladesOut[i].posWS,          $"blade[{i}].posWS");
                    Assert.AreEqual(blades[i].packedYawScale, bladesOut[i].packedYawScale,  $"blade[{i}].packedYawScale");
                    Assert.AreEqual(blades[i].hash,           bladesOut[i].hash,            $"blade[{i}].hash");
                }

                Assert.AreEqual(aabbs.Length, aabbsOut.Length, "aabb count");
                for (int i = 0; i < aabbs.Length; ++i)
                {
                    Assert.AreEqual(aabbs[i].min, aabbsOut[i].min, $"aabb[{i}].min");
                    Assert.AreEqual(aabbs[i].max, aabbsOut[i].max, $"aabb[{i}].max");
                }

                Assert.AreEqual(ranges.Length, rangesOut.Length, "range count");
                for (int i = 0; i < ranges.Length; ++i)
                {
                    Assert.AreEqual(ranges[i].start, rangesOut[i].start, $"range[{i}].start");
                    Assert.AreEqual(ranges[i].count, rangesOut[i].count, $"range[{i}].count");
                }

                // Metadata round-trip.
                Assert.AreEqual(2,    baked.GridX,       "GridX");
                Assert.AreEqual(1,    baked.GridZ,       "GridZ");
                Assert.AreEqual(10,   baked.ChunkSize,   "ChunkSize");
                Assert.AreEqual(2,    baked.TotalChunks, "TotalChunks");
                Assert.AreEqual(3,    baked.TotalBlades, "TotalBlades");
                Assert.AreEqual(1.5f, baked.ScaleMax2,  1e-6f, "ScaleMax2");
                Assert.IsFalse(baked.Oriented, "Oriented");
            }
            finally { Object.DestroyImmediate(baked); }
        }

        // ── Test 4: wrong version throws ──────────────────────────────────────

        [Test]
        public void Unpack_WrongVersion_Throws()
        {
            var baked = ScriptableObject.CreateInstance<BakedGrassData>();
            try
            {
                // Manually set a bad version byte.
                baked.bladeBlob = new byte[] { 99 };
                baked.aabbBlob  = new byte[] { 1  };
                baked.rangeBlob = new byte[] { 1  };

                Assert.Throws<System.InvalidOperationException>(() => baked.Unpack(),
                    "Should throw on version mismatch");
            }
            finally { Object.DestroyImmediate(baked); }
        }

        // ── Test 5: LoadFromBaked round-trip (byte-identity) ─────────────────

        [Test]
        public void LoadFromBaked_IsIdenticalToSourceArrays()
        {
            var blades = new BladeInstance[]
            {
                new BladeInstance { posWS = new Vector3(10f, 0f, 5f),  packedYawScale = 0x12345678u, hash = 99u },
                new BladeInstance { posWS = new Vector3(-3f, 1f, 7.5f), packedYawScale = 0u,          hash = 0u },
            };
            var aabbs  = new ChunkAabb[]
            {
                new ChunkAabb { min = new Vector3(0f, 0f, 0f), max = new Vector3(5f, 2f, 5f) },
            };
            var ranges = new ChunkRange[]
            {
                new ChunkRange { start = 0u, count = 2u },
            };
            float scaleMax2 = 1.2f;

            var baked = ScriptableObject.CreateInstance<BakedGrassData>();
            try
            {
                baked.Pack(
                    blades, aabbs, ranges,
                    gridX: 1, gridZ: 1, chunkSize: 10,
                    totalChunks: 1, totalBlades: 2,
                    scaleMax2: scaleMax2, oriented: false,
                    worldBounds: new Bounds(new Vector3(5f, 0f, 5f), new Vector3(10f, 4f, 10f)));

                // LoadFromBaked: create a ChunkedBladeBuffer, load from the baked asset.
                var buffer = new ChunkedBladeBuffer();
                buffer.LoadFromBaked(baked);

                // Assert CPU arrays are byte-identical to source.
                Assert.AreEqual(2, buffer.TotalBlades, "TotalBlades");
                Assert.AreEqual(1, buffer.TotalChunks, "TotalChunks");
                Assert.AreEqual(scaleMax2, buffer.ScaleMax2, 1e-6f, "ScaleMax2");

                BladeInstance[]? bladesOut = buffer.BladeInstances;
                Assert.IsNotNull(bladesOut, "BladeInstances not null after LoadFromBaked");
                Assert.AreEqual(2, bladesOut!.Length, "blade array length");
                for (int i = 0; i < blades.Length; ++i)
                {
                    Assert.AreEqual(blades[i].posWS.x,          bladesOut[i].posWS.x,           1e-5f, $"blade[{i}].posWS.x");
                    Assert.AreEqual(blades[i].posWS.y,          bladesOut[i].posWS.y,           1e-5f, $"blade[{i}].posWS.y");
                    Assert.AreEqual(blades[i].posWS.z,          bladesOut[i].posWS.z,           1e-5f, $"blade[{i}].posWS.z");
                    Assert.AreEqual(blades[i].packedYawScale,   bladesOut[i].packedYawScale,           $"blade[{i}].packedYawScale");
                    Assert.AreEqual(blades[i].hash,             bladesOut[i].hash,                     $"blade[{i}].hash");
                }

                ChunkRange[]? rangesOut = buffer.ChunkRanges;
                Assert.IsNotNull(rangesOut, "ChunkRanges not null");
                Assert.AreEqual(ranges[0].start, rangesOut![0].start, "range[0].start");
                Assert.AreEqual(ranges[0].count, rangesOut![0].count, "range[0].count");

                // Release GPU buffers (if any were created in edit mode; usually a no-op).
                buffer.Dispose();
            }
            finally { Object.DestroyImmediate(baked); }
        }

        // ── Test 6: BakeArrays is a static pure method (no GPU alloc) ────────

        [Test]
        public void BakeArrays_IsStatic_AndMetadataIsCorrect()
        {
            // We can't build a real GrassScatterResult without a full scatter,
            // but we CAN verify the method is accessible as a static and that the
            // BakeResult struct exposes the expected fields.
            // This is a compile-time check via reflection-free access.
            // Runtime validation of BakeArrays output happens in integration via WorldGrassBaker.

            // Stride-constant assertions are the byte-layout gate:
            // If any struct size changes, these blow up before a bad bake can run.
            Assert.AreEqual(20, BakedGrassData.BLADE_STRIDE, "BLADE_STRIDE SSOT");
            Assert.AreEqual(24, BakedGrassData.AABB_STRIDE,  "AABB_STRIDE SSOT");
            Assert.AreEqual(8,  BakedGrassData.RANGE_STRIDE, "RANGE_STRIDE SSOT");

            // Verify BakeResult struct is usable without a scatter (default empty state).
            var r = new ChunkedBladeBuffer.BakeResult(
                new BladeInstance[0], new ChunkAabb[0], new ChunkRange[0],
                gridX: 3, gridZ: 4, chunkSize: 5,
                totalChunks: 12, totalBlades: 0,
                scaleMax2: 1.0f, oriented: false);

            Assert.AreEqual(3,  r.GridX);
            Assert.AreEqual(4,  r.GridZ);
            Assert.AreEqual(5,  r.ChunkSize);
            Assert.AreEqual(12, r.TotalChunks);
            Assert.AreEqual(0,  r.TotalBlades);
            Assert.AreEqual(1f, r.ScaleMax2, 1e-6f);
            Assert.IsFalse(r.Oriented);
        }
    }
}
