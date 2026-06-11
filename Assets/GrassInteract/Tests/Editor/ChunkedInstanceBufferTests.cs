#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract.Tests
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
    }
}
