#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassChunkedBuffer.Partition"/> — the pure CPU counting-sort that
    /// maps a baked placement into chunked blade/aabb/range arrays. Runs without a GPU (no GraphicsBuffer).
    /// </summary>
    public sealed class ChunkedBufferTests
    {
        private const int   CHUNK = 16;
        private const float SCALE_MAX = 1.2f;
        private const float REACH = 1.0f, LATERAL = 0.5f;

        private static (Vector3[] pos, float[] yaw, float[] scale) MakeField(int n, Bounds b, int seed)
        {
            var rng = new System.Random(seed);
            var pos = new Vector3[n];
            var yaw = new float[n];
            var scale = new float[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector3(
                    Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble()),
                    Mathf.Lerp(b.min.y, b.max.y, (float)rng.NextDouble()),
                    Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble()));
                yaw[i]   = (float)rng.NextDouble() * 360f;
                scale[i] = Mathf.Lerp(0.8f, SCALE_MAX, (float)rng.NextDouble());
            }
            return (pos, yaw, scale);
        }

        [Test]
        public void Partition_PreservesEveryBladePosition()
        {
            var bounds = new Bounds(new Vector3(50, 1, 50), new Vector3(80, 4, 80));
            var (pos, yaw, scale) = MakeField(500, bounds, 1234);

            var r = GpuGrassChunkedBuffer.Partition(pos, yaw, scale, pos.Length, bounds,
                SCALE_MAX, REACH, LATERAL, CHUNK);

            Assert.AreEqual(pos.Length, r.TotalBlades, "TotalBlades must equal input count.");
            Assert.AreEqual(pos.Length, r.Blades.Length, "Blade array length mismatch.");

            // Multiset of positions must be identical (partition reorders but never drops/duplicates).
            var inKeys  = new List<string>();
            var outKeys = new List<string>();
            foreach (var p in pos) inKeys.Add(Key(p));
            foreach (var b in r.Blades) outKeys.Add(Key(b.posWS));
            inKeys.Sort(); outKeys.Sort();
            CollectionAssert.AreEqual(inKeys, outKeys, "Blade positions changed during partition.");
        }

        [Test]
        public void Partition_RangesAreContiguousAndCoverAllBlades()
        {
            var bounds = new Bounds(new Vector3(50, 1, 50), new Vector3(100, 4, 100));
            var (pos, yaw, scale) = MakeField(777, bounds, 99);

            var r = GpuGrassChunkedBuffer.Partition(pos, yaw, scale, pos.Length, bounds,
                SCALE_MAX, REACH, LATERAL, CHUNK);

            int sum = 0, expectedStart = 0;
            for (int c = 0; c < r.TotalChunks; c++)
            {
                var range = r.Ranges[c];
                if (range.count == 0) continue;
                Assert.AreEqual((uint)expectedStart, range.start,
                    $"Chunk {c} range not contiguous (gap/overlap).");
                expectedStart = (int)(range.start + range.count);
                sum += (int)range.count;
            }
            Assert.AreEqual(r.TotalBlades, sum, "Sum of chunk ranges must equal total blades.");
            Assert.AreEqual(r.TotalBlades, expectedStart, "Ranges must cover [0, TotalBlades).");
        }

        [Test]
        public void Partition_NonEmptyAabbUnionCoversInputXz()
        {
            var bounds = new Bounds(new Vector3(50, 2, 50), new Vector3(60, 6, 60));
            var (pos, yaw, scale) = MakeField(400, bounds, 7);

            var r = GpuGrassChunkedBuffer.Partition(pos, yaw, scale, pos.Length, bounds,
                SCALE_MAX, REACH, LATERAL, CHUNK);

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in pos)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }

            float uMinX = float.MaxValue, uMaxX = float.MinValue, uMinZ = float.MaxValue, uMaxZ = float.MinValue;
            for (int c = 0; c < r.TotalChunks; c++)
            {
                if (r.Ranges[c].count == 0) continue;
                var a = r.Aabbs[c];
                uMinX = Mathf.Min(uMinX, a.min.x); uMaxX = Mathf.Max(uMaxX, a.max.x);
                uMinZ = Mathf.Min(uMinZ, a.min.z); uMaxZ = Mathf.Max(uMaxZ, a.max.z);
            }

            Assert.LessOrEqual(uMinX, minX, "AABB union must contain input min X.");
            Assert.GreaterOrEqual(uMaxX, maxX, "AABB union must contain input max X.");
            Assert.LessOrEqual(uMinZ, minZ, "AABB union must contain input min Z.");
            Assert.GreaterOrEqual(uMaxZ, maxZ, "AABB union must contain input max Z.");
        }

        [Test]
        public void Partition_EmptyCellsAreCollapsedSentinels()
        {
            // Two tight clusters far apart in a big bounds → guaranteed empty cells between them.
            var bounds = new Bounds(new Vector3(64, 1, 64), new Vector3(128, 2, 128));
            var pos = new Vector3[]
            {
                new(2, 1, 2), new(3, 1, 3), new(4, 1, 2),
                new(126, 1, 126), new(125, 1, 124),
            };
            var yaw = new float[pos.Length];
            var scale = new float[pos.Length];
            for (int i = 0; i < pos.Length; i++) { yaw[i] = 0f; scale[i] = 1f; }

            var r = GpuGrassChunkedBuffer.Partition(pos, yaw, scale, pos.Length, bounds,
                SCALE_MAX, REACH, LATERAL, CHUNK);

            int empty = 0;
            for (int c = 0; c < r.TotalChunks; c++)
            {
                if (r.Ranges[c].count != 0) continue;
                empty++;
                Assert.Greater(r.Aabbs[c].min.x, r.Aabbs[c].max.x,
                    $"Empty cell {c} must keep the min>max sentinel for the compute trivial-reject.");
            }
            Assert.Greater(empty, 0, "Test setup should leave at least one empty cell.");
        }

        [Test]
        public void Partition_YawAndScaleRoundTripThroughPacking()
        {
            var bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(10, 2, 10));
            var pos   = new[] { new Vector3(1f, 0.5f, 1f) };
            var yaw   = new[] { 123.4f };
            var scale = new[] { 1.05f };

            var r = GpuGrassChunkedBuffer.Partition(pos, yaw, scale, 1, bounds,
                SCALE_MAX, REACH, LATERAL, CHUNK);

            uint packed = r.Blades[0].packedYawScale;
            float decodedYaw   = ((packed >> 16) & 0xFFFF) / 65535f * 360f;
            float decodedScale = (packed & 0xFFFF) / 65535f * r.ScaleMax2;

            Assert.AreEqual(123.4f, decodedYaw, 0.02f, "Yaw must survive 16-bit packing.");
            Assert.AreEqual(1.05f, decodedScale, 0.01f, "Scale must survive 16-bit packing over [0,ScaleMax2].");
            Assert.AreEqual(SCALE_MAX, r.ScaleMax2, 1e-4f, "ScaleMax2 must echo the encode upper bound.");
        }

        [Test]
        public void Partition_EmptyFieldReturnsEmptyArraysWithValidGrid()
        {
            var bounds = new Bounds(new Vector3(50, 0, 50), new Vector3(100, 2, 100));
            var r = GpuGrassChunkedBuffer.Partition(
                System.Array.Empty<Vector3>(), System.Array.Empty<float>(), System.Array.Empty<float>(),
                0, bounds, SCALE_MAX, REACH, LATERAL, CHUNK);

            Assert.AreEqual(0, r.TotalBlades);
            Assert.AreEqual(0, r.Blades.Length);
            Assert.GreaterOrEqual(r.GridX, 1);
            Assert.GreaterOrEqual(r.GridZ, 1);
        }

        private static string Key(Vector3 p) => $"{p.x:F4},{p.y:F4},{p.z:F4}";
    }
}
