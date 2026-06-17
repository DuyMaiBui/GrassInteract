#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ChunkedInstanceBuffer"/> Phase 1:
    /// verifies that <c>SortedToAuthored</c> is a valid permutation of [0,N)
    /// and that the baked <c>Instances[k].posWS</c> matches the authored record at
    /// <c>sortedToAuthored[k]</c>.
    /// </summary>
    public sealed class ChunkedInstanceBufferTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Helper: build a synthetic GrassScatterResult with known positions.
        // Uses two slabs so at least 2 grid cells are covered.
        // ─────────────────────────────────────────────────────────────────────

        private static GrassScatterResult MakeSyntheticScatter(Vector3[] positions)
        {
            int total = positions.Length;
            // All in one slab for simplicity (InstanceBatchPool.MAX_INSTANCES_PER_BATCH = 1023,
            // we use small counts so this is fine).
            var baseSlabs     = new Matrix4x4[1][];
            var positionSlabs = new Vector3[1][];
            var normalSlabs   = new Vector3[1][];
            var slabCounts    = new int[1];

            var matSlab = new Matrix4x4[1023];
            var posSlab = new Vector3[1023];
            var nrmSlab = new Vector3[1023];

            for (int i = 0; i < total; ++i)
            {
                matSlab[i] = Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one);
                posSlab[i] = positions[i];
                nrmSlab[i] = Vector3.up;
            }

            baseSlabs[0]     = matSlab;
            positionSlabs[0] = posSlab;
            normalSlabs[0]   = nrmSlab;
            slabCounts[0]    = total;

            var worldBounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            return new GrassScatterResult(baseSlabs, slabCounts, positionSlabs, normalSlabs, total, worldBounds);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 1: SortedToAuthored is a valid permutation of [0, N).
        // Uses 6 instances spread across ≥2 grid cells.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SortedToAuthored_IsValidPermutation_AfterBake()
        {
            // Arrange: 6 positions placed in two different 8m grid cells
            // (cx=0,cz=0 and cx=1,cz=0) from origin (0,0,0), fieldBounds=(32,32).
            var positions = new[]
            {
                new Vector3( 2f, 0f,  2f),  // cell (0,0)
                new Vector3(10f, 0f,  2f),  // cell (1,0)
                new Vector3( 4f, 0f,  3f),  // cell (0,0)
                new Vector3(12f, 0f,  1f),  // cell (1,0)
                new Vector3( 1f, 0f,  6f),  // cell (0,0)
                new Vector3(18f, 0f,  2f),  // cell (2,0)
            };

            GrassScatterResult scatter = MakeSyntheticScatter(positions);
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            try
            {
                buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 1f, meshBounds,
                    oriented: false, chunkSize: 8);

                int n = positions.Length;
                int[]? map = buf.SortedToAuthored;

                Assert.IsNotNull(map,                     "SortedToAuthored must not be null after Bake");
                Assert.AreEqual(n, map!.Length,           "SortedToAuthored.Length == TotalInstances");
                Assert.AreEqual(n, buf.TotalInstances,    "TotalInstances == input count");

                // Each authored index appears exactly once → valid permutation.
                var seen = new HashSet<int>();
                foreach (int idx in map)
                {
                    Assert.IsTrue(idx >= 0 && idx < n,
                        $"authored index {idx} is out of [0,{n})");
                    Assert.IsTrue(seen.Add(idx),
                        $"authored index {idx} appears more than once → not a permutation");
                }
            }
            finally
            {
                buf.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 2: Instances[k].posWS matches authored record sortedToAuthored[k].
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SortedToAuthored_PositionMatchesBakedInstance()
        {
            var positions = new[]
            {
                new Vector3( 3f, 1f,  3f),  // authored index 0
                new Vector3(11f, 2f,  3f),  // authored index 1
                new Vector3( 5f, 0f,  7f),  // authored index 2
                new Vector3(14f, 1f,  6f),  // authored index 3
            };

            GrassScatterResult scatter = MakeSyntheticScatter(positions);
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            try
            {
                buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 1f, meshBounds,
                    oriented: false, chunkSize: 8);

                InstanceData[]? instances = buf.Instances;
                int[]?          map       = buf.SortedToAuthored;

                Assert.IsNotNull(instances, "Instances must not be null after Bake");
                Assert.IsNotNull(map,       "SortedToAuthored must not be null after Bake");

                for (int k = 0; k < instances!.Length; ++k)
                {
                    int authoredIdx = map![k];
                    Vector3 bakedPos   = instances[k].posWS;
                    Vector3 authoredPos = positions[authoredIdx];

                    Assert.AreEqual(authoredPos.x, bakedPos.x, 1e-4f,
                        $"Instances[{k}].posWS.x mismatch (authored[{authoredIdx}])");
                    Assert.AreEqual(authoredPos.y, bakedPos.y, 1e-4f,
                        $"Instances[{k}].posWS.y mismatch (authored[{authoredIdx}])");
                    Assert.AreEqual(authoredPos.z, bakedPos.z, 1e-4f,
                        $"Instances[{k}].posWS.z mismatch (authored[{authoredIdx}])");
                }
            }
            finally
            {
                buf.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 3: SortedToAuthored is null after Dispose.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void SortedToAuthored_IsNull_AfterDispose()
        {
            var positions = new[] { new Vector3(1f, 0f, 1f), new Vector3(9f, 0f, 1f) };
            GrassScatterResult scatter = MakeSyntheticScatter(positions);
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 1f, meshBounds);
            buf.Dispose();

            Assert.IsNull(buf.SortedToAuthored, "SortedToAuthored must be null after Dispose");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 4: PatchInstance writes the new pos + encoded yaw/scale into the
        // GPU slot the authored index maps to (verified via the CPU Instances copy).
        // This is the live editor transform-drag preview path.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PatchInstance_UpdatesMappedSlot_PosYawScale()
        {
            var positions = new[]
            {
                new Vector3( 3f, 0f,  3f),  // authored index 0
                new Vector3(11f, 0f,  3f),  // authored index 1
                new Vector3( 5f, 0f,  7f),  // authored index 2
                new Vector3(14f, 0f,  6f),  // authored index 3
            };

            GrassScatterResult scatter = MakeSyntheticScatter(positions);
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            try
            {
                // scaleRangeMax = 2 so a 1.5 scale is inside the encode range.
                buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 2f, meshBounds,
                    oriented: false, chunkSize: 8);

                const int   authoredIdx = 2;
                var         newPos      = new Vector3(20f, 4f, 9f);
                const float newYaw      = 90f;
                const float newScale    = 1.5f;

                bool ok = buf.PatchInstance(authoredIdx, newPos, newYaw, newScale);
                Assert.IsTrue(ok, "PatchInstance should succeed for an in-range index after Bake");

                // Find the sorted slot the authored index landed in.
                int[]?          map       = buf.SortedToAuthored;
                InstanceData[]? instances = buf.Instances;
                Assert.IsNotNull(map);
                Assert.IsNotNull(instances);

                int sortedIdx = -1;
                for (int k = 0; k < map!.Length; ++k)
                    if (map[k] == authoredIdx) { sortedIdx = k; break; }
                Assert.GreaterOrEqual(sortedIdx, 0, "authored index must map to a sorted slot");

                InstanceData rec = instances![sortedIdx];
                Assert.AreEqual(newPos.x, rec.posWS.x, 1e-4f, "patched posWS.x");
                Assert.AreEqual(newPos.y, rec.posWS.y, 1e-4f, "patched posWS.y");
                Assert.AreEqual(newPos.z, rec.posWS.z, 1e-4f, "patched posWS.z");

                // Decode packedYawScale exactly as the vertex shader does.
                float decodedYaw   = ((rec.packedYawScale >> 16) & 0xFFFFu) / 65535f * 360f;
                float decodedScale = (rec.packedYawScale        & 0xFFFFu) / 65535f * buf.ScaleMax;
                Assert.AreEqual(newYaw,   decodedYaw,   0.02f, "patched yaw decodes back");
                Assert.AreEqual(newScale, decodedScale, 0.001f, "patched scale decodes back");

                // Patching one slot must not disturb a sibling's position.
                int otherAuthored = authoredIdx == 0 ? 1 : 0;
                int otherSorted   = -1;
                for (int k = 0; k < map.Length; ++k)
                    if (map[k] == otherAuthored) { otherSorted = k; break; }
                Assert.AreEqual(positions[otherAuthored].x, instances[otherSorted].posWS.x, 1e-4f,
                    "non-patched sibling posWS.x must be unchanged");
            }
            finally
            {
                buf.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 4b: a Select-tool scale drag PAST the current ceiling raises ScaleMax
        // (re-encoding all slots) so the enlarged instance decodes back at its new size,
        // while a sibling's scale survives the re-encode. This is the "scale handle is
        // dead on a default (1,1) layer" fix — the handle must grow past the baked ceiling.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PatchInstance_AboveCeiling_RaisesScaleMax_AndPreservesSiblings()
        {
            var positions = new[]
            {
                new Vector3( 2f, 0f,  2f),  // authored index 0 (sibling, stays scale 1)
                new Vector3(10f, 0f,  2f),  // authored index 1 (gets enlarged)
            };

            GrassScatterResult scatter = MakeSyntheticScatter(positions); // all scale 1
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            try
            {
                // Default-style layer: scaleRangeMax = 1. With SCALE_HEADROOM the baked ceiling is 1.5,
                // so a 3.0 scale drag is ABOVE the ceiling and must raise ScaleMax.
                buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 1f, meshBounds,
                    oriented: false, chunkSize: 8);

                float ceilingBefore = buf.ScaleMax;
                Assert.Less(ceilingBefore, 3f, "precondition: 3.0 must exceed the baked ceiling");

                const int   bigIdx   = 1;
                const float bigScale = 3.0f;
                bool ok = buf.PatchInstance(bigIdx, positions[bigIdx], 0f, bigScale);
                Assert.IsTrue(ok, "PatchInstance should succeed for an in-range index");

                Assert.Greater(buf.ScaleMax, ceilingBefore,
                    "scaling past the ceiling must RAISE ScaleMax so the decode divisor has room");
                Assert.GreaterOrEqual(buf.ScaleMax, bigScale,
                    "the raised ceiling must be >= the enlarged scale");

                int[]?          map       = buf.SortedToAuthored;
                InstanceData[]? instances = buf.Instances;
                Assert.IsNotNull(map);
                Assert.IsNotNull(instances);

                // Enlarged instance decodes back to ~3.0 against the raised ScaleMax.
                int bigSorted = SortedSlotOf(map!, bigIdx);
                float bigDecoded = (instances![bigSorted].packedYawScale & 0xFFFFu) / 65535f * buf.ScaleMax;
                Assert.AreEqual(bigScale, bigDecoded, 0.01f, "enlarged instance decodes at its new size");

                // Sibling (authored 0) was scale 1 — the global re-encode must preserve it.
                int sibSorted = SortedSlotOf(map, 0);
                float sibDecoded = (instances[sibSorted].packedYawScale & 0xFFFFu) / 65535f * buf.ScaleMax;
                Assert.AreEqual(1f, sibDecoded, 0.01f, "sibling scale survives the re-encode to the new ceiling");
            }
            finally
            {
                buf.Dispose();
            }
        }

        private static int SortedSlotOf(int[] sortedToAuthored, int authoredIdx)
        {
            for (int k = 0; k < sortedToAuthored.Length; ++k)
                if (sortedToAuthored[k] == authoredIdx) return k;
            return -1;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 5: PatchInstance rejects out-of-range indices and post-dispose calls
        // (so the caller falls back to a full rebuild instead of corrupting memory).
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PatchInstance_ReturnsFalse_OutOfRangeOrAfterDispose()
        {
            var positions = new[] { new Vector3(1f, 0f, 1f), new Vector3(9f, 0f, 1f) };
            GrassScatterResult scatter = MakeSyntheticScatter(positions);
            var meshBounds = new Bounds(Vector3.zero, Vector3.one);

            var buf = new ChunkedInstanceBuffer();
            buf.Bake(scatter, Vector3.zero, new Vector2(32f, 32f), 1f, meshBounds);

            Assert.IsFalse(buf.PatchInstance(-1,  Vector3.zero, 0f, 1f), "negative index rejected");
            Assert.IsFalse(buf.PatchInstance(99,  Vector3.zero, 0f, 1f), "out-of-range index rejected");

            buf.Dispose();
            Assert.IsFalse(buf.PatchInstance(0, Vector3.zero, 0f, 1f), "patch after Dispose rejected");
        }
    }
}
