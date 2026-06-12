#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for TerrainTileResidencySet:
    ///   - Add/Contains/Get/Count lifecycle.
    ///   - Double-load guard: second Add for same coord returns false.
    ///   - Evict removes the coord from resident set.
    ///   - Evict disposes GPU resources: TerrainTileGpuResources.IsUploaded becomes false.
    ///   - EvictAll clears the set and disposes all GPU resources.
    ///   - Diff correctly partitions toLoad / toEvict.
    /// </summary>
    [TestFixture]
    public sealed class TerrainTileResidencySetTests
    {
        // ── No-op engine stub ─────────────────────────────────────────────────

        /// <summary>
        /// Test-only ITerrainEngine that does nothing.
        /// Allows testing ResidentTile bookkeeping without a real ComputeShader.
        /// </summary>
        private sealed class NoOpEngine : ITerrainEngine
        {
            public bool IsDisposed { get; private set; }

            public void Submit(Camera? targetCamera, Vector3 cameraPos) { /* no-op */ }

            public void Dispose()
            {
                this.IsDisposed = true;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(int tx, int tz)
        {
            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord = new Vector2Int(tx, tz);
            asset.heightRes = 3;
            asset.minHeight = 0f;
            asset.maxHeight = 100f;
            asset.heightData = new byte[3 * 3 * TerrainHeightFormat.BYTES_PER_SAMPLE];
            return asset;
        }

        private static (TerrainTileResidencySet.ResidentTile tile,
                        TerrainTileGpuResources gpuRes,
                        NoOpEngine engine)
            MakeResidentTile(int tx, int tz)
        {
            var asset  = MakeTile(tx, tz);
            var gpuRes = new TerrainTileGpuResources();
            gpuRes.Upload(asset);
            var engine = new NoOpEngine();
            var tile   = new TerrainTileResidencySet.ResidentTile(asset, gpuRes, engine);
            return (tile, gpuRes, engine);
        }

        // ── Add / Contains / Get / Count ──────────────────────────────────────

        [Test]
        public void Add_NewCoord_IncreasesCount()
        {
            var set            = new TerrainTileResidencySet();
            var (tile, _, _)   = MakeResidentTile(0, 0);

            bool added = set.Add(new Vector2Int(0, 0), tile);

            Assert.IsTrue(added,        "Add new coord should return true.");
            Assert.AreEqual(1, set.Count, "Count should be 1 after one Add.");

            set.EvictAll();
        }

        [Test]
        public void Contains_ExistingCoord_ReturnsTrue()
        {
            var set          = new TerrainTileResidencySet();
            var coord        = new Vector2Int(3, 7);
            var (tile, _, _) = MakeResidentTile(3, 7);

            set.Add(coord, tile);
            Assert.IsTrue(set.Contains(coord));
            set.EvictAll();
        }

        [Test]
        public void Get_ExistingCoord_ReturnsTile()
        {
            var set          = new TerrainTileResidencySet();
            var coord        = new Vector2Int(1, 2);
            var (tile, _, _) = MakeResidentTile(1, 2);

            set.Add(coord, tile);
            var retrieved = set.Get(coord);

            Assert.IsNotNull(retrieved);
            Assert.AreSame(tile, retrieved, "Get should return the exact tile that was added.");
            set.EvictAll();
        }

        [Test]
        public void Get_MissingCoord_ReturnsNull()
        {
            var set = new TerrainTileResidencySet();
            Assert.IsNull(set.Get(new Vector2Int(99, 99)));
        }

        // ── Double-load guard ─────────────────────────────────────────────────

        [Test]
        public void Add_SameCoordTwice_SecondReturnsFalse()
        {
            var set              = new TerrainTileResidencySet();
            var coord            = new Vector2Int(0, 0);
            var (tileA, _, _)    = MakeResidentTile(0, 0);
            var (tileB, gpuB, _) = MakeResidentTile(0, 0);

            bool first  = set.Add(coord, tileA);
            bool second = set.Add(coord, tileB);

            Assert.IsTrue(first,   "First Add should return true.");
            Assert.IsFalse(second, "Second Add for same coord should return false (double-load guard).");
            Assert.AreEqual(1, set.Count, "Count should remain 1.");

            set.EvictAll();
            // tileB's gpuRes was never added — dispose manually to avoid leak.
            gpuB.Dispose();
        }

        // ── Evict — dispose proof ─────────────────────────────────────────────

        [Test]
        public void Evict_ExistingCoord_RemovesFromSet()
        {
            var set          = new TerrainTileResidencySet();
            var coord        = new Vector2Int(2, 3);
            var (tile, _, _) = MakeResidentTile(2, 3);

            set.Add(coord, tile);
            set.Evict(coord);

            Assert.AreEqual(0, set.Count,       "Count should be 0 after Evict.");
            Assert.IsFalse(set.Contains(coord), "Contains should return false after Evict.");
        }

        [Test]
        public void Evict_DisposesGpuResources_IsUploadedBecomeFalse()
        {
            var set               = new TerrainTileResidencySet();
            var coord             = new Vector2Int(4, 4);
            var (tile, gpuRes, _) = MakeResidentTile(4, 4);

            Assert.IsTrue(gpuRes.IsUploaded, "GPU resources should be uploaded before evict.");

            set.Add(coord, tile);
            set.Evict(coord);

            Assert.IsFalse(gpuRes.IsUploaded,
                "GPU resources must be disposed (IsUploaded=false) after Evict — no memory leak.");
        }

        [Test]
        public void Evict_DisposesEngine()
        {
            var set                    = new TerrainTileResidencySet();
            var coord                  = new Vector2Int(5, 5);
            var (tile, _, engineProxy) = MakeResidentTile(5, 5);

            set.Add(coord, tile);
            set.Evict(coord);

            Assert.IsTrue(engineProxy.IsDisposed,
                "Engine Dispose() must be called on evict.");
        }

        [Test]
        public void Evict_MissingCoord_IsNoOp()
        {
            var set = new TerrainTileResidencySet();
            Assert.DoesNotThrow(() => set.Evict(new Vector2Int(99, 99)),
                "Evict on missing coord should not throw.");
        }

        // ── EvictAll ──────────────────────────────────────────────────────────

        [Test]
        public void EvictAll_ClearsAllEntries_AndDisposesAllGpu()
        {
            var set    = new TerrainTileResidencySet();
            var gpuRefs = new List<TerrainTileGpuResources>();

            for (int i = 0; i < 5; ++i)
            {
                var (tile, gpuRes, _) = MakeResidentTile(i, 0);
                set.Add(new Vector2Int(i, 0), tile);
                gpuRefs.Add(gpuRes);
            }

            Assert.AreEqual(5, set.Count);
            set.EvictAll();
            Assert.AreEqual(0, set.Count, "EvictAll should clear all resident tiles.");

            foreach (var gpuRes in gpuRefs)
                Assert.IsFalse(gpuRes.IsUploaded,
                    "All GPU resources must be disposed after EvictAll — no leak.");
        }

        // ── Diff ─────────────────────────────────────────────────────────────

        [Test]
        public void Diff_CorrectlyPartitionsLoadAndEvict()
        {
            var set    = new TerrainTileResidencySet();
            var coordA = new Vector2Int(0, 0);
            var coordB = new Vector2Int(1, 0);
            var coordC = new Vector2Int(2, 0);

            var (tileA, _, _) = MakeResidentTile(0, 0);
            var (tileB, _, _) = MakeResidentTile(1, 0);
            set.Add(coordA, tileA);
            set.Add(coordB, tileB);

            // Desired: B and C. A is no longer desired.
            var desired = new HashSet<Vector2Int> { coordB, coordC };
            var toLoad  = new List<Vector2Int>();
            var toEvict = new List<Vector2Int>();
            set.Diff(desired, toLoad, toEvict);

            Assert.AreEqual(1, toLoad.Count,        "Only C should be in toLoad.");
            Assert.IsTrue(toLoad.Contains(coordC),  "C should be in toLoad.");
            Assert.AreEqual(1, toEvict.Count,       "Only A should be in toEvict.");
            Assert.IsTrue(toEvict.Contains(coordA), "A should be in toEvict.");

            set.EvictAll();
        }

        [Test]
        public void Diff_DoesNotMutateResidentSet()
        {
            var set   = new TerrainTileResidencySet();
            var coord = new Vector2Int(0, 0);
            var (tile, _, _) = MakeResidentTile(0, 0);
            set.Add(coord, tile);

            var desired = new HashSet<Vector2Int>(); // empty
            var toLoad  = new List<Vector2Int>();
            var toEvict = new List<Vector2Int>();
            set.Diff(desired, toLoad, toEvict);

            // Diff shouldn't evict anything on its own.
            Assert.AreEqual(1, set.Count, "Diff must not mutate resident set.");
            set.EvictAll();
        }
    }
}
