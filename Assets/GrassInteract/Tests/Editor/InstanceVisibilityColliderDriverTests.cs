#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace GrassInteract.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="InstanceVisibilityColliderDriver"/> Phase 2.
    ///
    /// These tests bypass AsyncGPUReadback entirely and call
    /// <see cref="InstanceVisibilityColliderDriver.ApplyVisibleSetFromArray"/> directly,
    /// so they run headless without a GPU.
    ///
    /// Coverage:
    /// 1. Acquire on newly-visible records.
    /// 2. Release on dropped records.
    /// 3. Idempotent Acquire on still-visible records.
    /// 4. PoolCap cap: records beyond the cap are not acquired.
    /// 5. wantsCollider=false records are never acquired even when visible.
    /// </summary>
    public sealed class InstanceVisibilityColliderDriverTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        private const int RECORD_COUNT = 6;

        private static (GameObject root, InstanceColliderPool pool) MakePool(int cap)
        {
            var go   = new GameObject("PoolRoot");
            var pool = go.AddComponent<InstanceColliderPool>();
            // Use a single-face quad mesh so MeshCollider doesn't log errors.
            // Passing null mesh is fine for this test — we just check key bookkeeping.
            pool.Init(cap, null, false, null);
            return (go, pool);
        }

        /// <summary>Builds a driver with RECORD_COUNT records, all wantsCollider=true.</summary>
        private static InstanceVisibilityColliderDriver MakeDriver(
            InstanceColliderPool pool,
            int[] sortedToAuthored,
            bool[]? wantsOverride = null)
        {
            int n = RECORD_COUNT;
            var positions   = new Vector3[n];
            var rotations   = new Quaternion[n];
            var scales      = new float[n];
            var meshes      = new Mesh?[n];
            var convex      = new bool[n];
            var wants       = wantsOverride ?? new bool[n];
            var materials   = new PhysicsMaterial?[n];

            if (wantsOverride == null)
                for (int i = 0; i < n; ++i) wants[i] = true; // all want colliders by default

            for (int i = 0; i < n; ++i)
                positions[i] = new Vector3(i * 2f, 0f, 0f);

            return new InstanceVisibilityColliderDriver(
                pool, sortedToAuthored,
                positions, rotations, scales, meshes, convex, wants, materials,
                poolCap: pool.ActiveKeys.Count + 100); // generous cap set below per test
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 1: Acquire on newly-visible records.
        // sortedToAuthored is identity [0..5]; visible = {0,1,2}
        // → pool.ActiveKeys should contain authored indices 0,1,2.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void NewlyVisible_RecordsAreAcquired()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 }; // identity permutation
                var   driver = MakeDriver(pool, map);

                // Apply visible set: sorted indices 0,1,2 → authored 0,1,2.
                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u, 2u }, 3);

                var active = pool.ActiveKeys;
                Assert.AreEqual(3, active.Count, "three colliders acquired");
                Assert.IsTrue(Contains(active, 0), "authored 0 active");
                Assert.IsTrue(Contains(active, 1), "authored 1 active");
                Assert.IsTrue(Contains(active, 2), "authored 2 active");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 2: Release on dropped records.
        // Frame 1 visible={0,1,2}; Frame 2 visible={0,2} → 1 must be released.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void DroppedFromVisible_RecordsAreReleased()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = MakeDriver(pool, map);

                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u, 2u }, 3);
                Assert.AreEqual(3, pool.ActiveKeys.Count, "frame 1: three active");

                // Frame 2: sorted index 1 gone.
                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 2u }, 2);

                var active = pool.ActiveKeys;
                Assert.AreEqual(2, active.Count, "frame 2: two active after drop");
                Assert.IsTrue (Contains(active, 0), "authored 0 still active");
                Assert.IsFalse(Contains(active, 1), "authored 1 released");
                Assert.IsTrue (Contains(active, 2), "authored 2 still active");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 3: Idempotent Acquire on still-visible records.
        // Two consecutive frames with the same visible set → count stays constant.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void StillVisible_AcquireIsIdempotent()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = MakeDriver(pool, map);

                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u }, 2);
                Assert.AreEqual(2, pool.ActiveKeys.Count, "after frame 1");

                // Same visible set again.
                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u }, 2);
                Assert.AreEqual(2, pool.ActiveKeys.Count, "after frame 2 (idempotent)");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 4: PoolCap respected — records beyond cap are not acquired.
        // Cap=2, visible={0,1,2,3} → only first 2 that succeed cap check.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PoolCap_LimitsAcquisition()
        {
            var (go, pool) = MakePool(cap: 2);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = new InstanceVisibilityColliderDriver(
                    pool, map,
                    positions:   new Vector3[6],
                    rotations:   new Quaternion[6],
                    scales:      new float[6],
                    meshes:      new Mesh?[6],
                    convex:      new bool[6],
                    wantsCollider: new bool[] { true, true, true, true, true, true },
                    materials:   new PhysicsMaterial?[6],
                    poolCap: 2);

                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u, 2u, 3u }, 4);

                // Pool hard cap is 2 → at most 2 can be active.
                Assert.LessOrEqual(pool.ActiveKeys.Count, 2,
                    "pool cap must not be exceeded");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 5: wantsCollider=false records are never acquired.
        // sortedToAuthored: 0→authored0, 1→authored1; authored0.wants=false.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void WantsColliderFalse_NeverAcquired()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[]  map   = { 0, 1, 2, 3, 4, 5 };
                bool[] wants = { false, true, true, true, true, true }; // authored 0 doesn't want
                var    driver = MakeDriver(pool, map, wantsOverride: wants);

                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u }, 2);

                Assert.AreEqual(1, pool.ActiveKeys.Count, "only authored 1 acquired");
                Assert.IsFalse(Contains(pool.ActiveKeys, 0), "authored 0 not acquired (wants=false)");
                Assert.IsTrue (Contains(pool.ActiveKeys, 1), "authored 1 acquired");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 6: Non-identity permutation — GPU sorted index correctly maps to
        // a different authored index.
        // sortedToAuthored = [3, 0, 5, 1, 4, 2] (arbitrary permutation).
        // visible sorted = {0} → authored 3.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void NonIdentityPermutation_MapsCorrectly()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 3, 0, 5, 1, 4, 2 };
                var   driver = MakeDriver(pool, map);

                // GPU sorted index 0 → authored index map[0] = 3.
                driver.ApplyVisibleSetFromArray(new uint[] { 0u }, 1);

                Assert.AreEqual(1, pool.ActiveKeys.Count, "one collider acquired");
                Assert.IsTrue(Contains(pool.ActiveKeys, 3),
                    "authored index 3 should be active (sorted 0 → map[0]=3)");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 7: 3-band union — instances spread across LOD0, LOD1, LOD2
        // all end up in the desired set and are acquired.
        // Records 0,1 in band0; records 2,3 in band1; records 4,5 in band2.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void ThreeBandUnion_AllBandsAcquired()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = MakeDriver(pool, map);

                driver.ApplyThreeBandsFromArrays(
                    new uint[] { 0u, 1u }, 2,
                    new uint[] { 2u, 3u }, 2,
                    new uint[] { 4u, 5u }, 2);

                Assert.AreEqual(6, pool.ActiveKeys.Count, "all 6 records acquired from 3 bands");
                for (int i = 0; i < 6; ++i)
                    Assert.IsTrue(Contains(pool.ActiveKeys, i), $"authored {i} should be active");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 8: Per-frame acquire budget.
        // Desired set = 6 records, maxCollidersPerFrame = 2.
        // Three TickAcquireRelease calls grow the pool by 2 each time.
        // A fourth call is idempotent.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void PerFrameBudget_AcquiresIncrementally()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = new InstanceVisibilityColliderDriver(
                    pool, map,
                    positions:            new Vector3[6],
                    rotations:            new Quaternion[6],
                    scales:               new float[6],
                    meshes:               new Mesh?[6],
                    convex:               new bool[6],
                    wantsCollider:        new bool[] { true, true, true, true, true, true },
                    materials:            new PhysicsMaterial?[6],
                    poolCap:              10,
                    maxCollidersPerFrame: 2);

                // Prime desired set without triggering the full acquire.
                driver.SetDesiredSetForTest(new uint[] { 0u, 1u, 2u, 3u, 4u, 5u }, 6);

                driver.TickAcquireRelease();
                Assert.AreEqual(2, pool.ActiveKeys.Count, "frame 1: 2 acquired");

                driver.TickAcquireRelease();
                Assert.AreEqual(4, pool.ActiveKeys.Count, "frame 2: 4 acquired");

                driver.TickAcquireRelease();
                Assert.AreEqual(6, pool.ActiveKeys.Count, "frame 3: all 6 acquired");

                driver.TickAcquireRelease();
                Assert.AreEqual(6, pool.ActiveKeys.Count, "frame 4: idempotent");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 9: Release is immediate.
        // All 6 active; desired narrows to {0,1} → one TickAcquireRelease
        // releases 4 records immediately regardless of budget.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Release_IsImmediate_OneTickClearsDropped()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                int[] map    = { 0, 1, 2, 3, 4, 5 };
                var   driver = MakeDriver(pool, map);

                // Acquire all 6 via the instant test path.
                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u, 2u, 3u, 4u, 5u }, 6);
                Assert.AreEqual(6, pool.ActiveKeys.Count, "pre: all 6 active");

                // Narrow desired to {0,1}.
                driver.SetDesiredSetForTest(new uint[] { 0u, 1u }, 2);
                driver.TickAcquireRelease();

                Assert.AreEqual(2, pool.ActiveKeys.Count, "post: only 2 remain");
                Assert.IsTrue (Contains(pool.ActiveKeys, 0), "authored 0 still active");
                Assert.IsTrue (Contains(pool.ActiveKeys, 1), "authored 1 still active");
                Assert.IsFalse(Contains(pool.ActiveKeys, 2), "authored 2 released");
                Assert.IsFalse(Contains(pool.ActiveKeys, 5), "authored 5 released");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Test 10: GenerateColliders=true semantics — all records with a mesh
        // produce wantsCollider=true and are acquired when visible.
        // Simulates the layer-level toggle: a driver built with wantsCollider
        // uniformly true (as BuildColliderRuntime sets when GenerateColliders is
        // on) should acquire every visible sorted index.
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void GenerateColliders_AllRecordsAcquiredWhenVisible()
        {
            var (go, pool) = MakePool(cap: 10);
            try
            {
                bool[] allWant = { true, true, true, true, true, true };
                int[]  map     = { 0, 1, 2, 3, 4, 5 };
                var    driver  = MakeDriver(pool, map, wantsOverride: allWant);

                driver.ApplyVisibleSetFromArray(new uint[] { 0u, 1u, 2u, 3u, 4u, 5u }, 6);

                Assert.AreEqual(6, pool.ActiveKeys.Count,
                    "all 6 records acquired when GenerateColliders=true and all are visible");

                driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper: safe Contains for KeyCollection without LINQ.
        // ─────────────────────────────────────────────────────────────────────

        private static bool Contains(
            System.Collections.Generic.Dictionary<int, MeshCollider>.KeyCollection keys,
            int value)
        {
            foreach (int k in keys)
                if (k == value) return true;
            return false;
        }
    }
}
