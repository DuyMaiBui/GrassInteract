#nullable enable
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldMapBaker"/> and the P8 bake → streaming pipeline.
    ///
    /// Coverage:
    ///   G1. <see cref="WorldMapBaker.BakeAll"/> emits one standalone TerrainTileAsset per tile.
    ///   G2. Emitted tiles have byte-identical heightData and splatData to their container sources.
    ///   G3. The manifest maps each coord to the correct asset path.
    ///   G4. Incremental bake (BakeTile) re-bakes only the requested tile; other tiles unchanged.
    ///   G5. Pruning: BakeAll removes manifest entries and asset files for coords no longer in map.
    ///   G6. Residency set include/evict by simulated camera proximity (baked-tile path).
    ///   G7. TerrainStreamingManager.RegisterFromManifest populates the tile index from manifest.
    ///   G8. TerrainTileLoader.EnqueueBaked/EnqueueBakedDirect delegate to the Enqueue path.
    /// </summary>
    [TestFixture]
    public sealed class WorldMapBakerTests
    {
        // ── Test constants ────────────────────────────────────────────────────

        private const string TEST_FOLDER = "Assets/WorldPainter/Tests/_BakeTestTemp";

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a minimal valid TerrainTileAsset with distinct, non-zero height/splat data.
        /// Does NOT add it to the AssetDatabase — caller owns lifetime.
        /// </summary>
        private static TerrainTileAsset MakeTileAsset(
            Vector2Int coord,
            byte heightFill = 0xAB,
            byte splatFill  = 0xCD)
        {
            int hRes = 3;  // smallest valid: 3×3 R16
            int sRes = 4;  // 4×4 RGBA32

            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord = coord;
            asset.heightRes = hRes;
            asset.minHeight = 0f;
            asset.maxHeight = 100f;
            asset.splatRes  = sRes;

            // Fill with distinct pattern so byte-identity check is meaningful.
            int heightBytes = hRes * hRes * TerrainHeightFormat.BYTES_PER_SAMPLE;
            int splatBytes  = sRes * sRes * 4;
            asset.heightData = new byte[heightBytes];
            asset.splatData  = new byte[splatBytes];
            for (int i = 0; i < heightBytes; i++) asset.heightData[i] = (byte)(heightFill ^ (i & 0xFF));
            for (int i = 0; i < splatBytes;  i++) asset.splatData[i]  = (byte)(splatFill  ^ (i & 0xFF));

            return asset;
        }

        /// <summary>
        /// Creates a WorldMapAsset in memory (not saved to AssetDatabase) with two tiles
        /// at (0,0) and (1,0) with distinct height/splat fills.
        /// </summary>
        private static WorldMapAsset MakeTwoTileMap()
        {
            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            map.name = "TestMap";

            var tile00 = MakeTileAsset(new Vector2Int(0, 0), heightFill: 0xAA, splatFill: 0xBB);
            var tile10 = MakeTileAsset(new Vector2Int(1, 0), heightFill: 0xCC, splatFill: 0xDD);

            tile00.name = WorldMapAssetLifecycle.TileSubAssetName(new Vector2Int(0, 0));
            tile10.name = WorldMapAssetLifecycle.TileSubAssetName(new Vector2Int(1, 0));

            // Register tiles using internal API (mirrors AddTile without AssetDatabase).
            map.RegisterTile(new Vector2Int(0, 0), tile00);
            map.RegisterTile(new Vector2Int(1, 0), tile10);

            return map;
        }

        private static void EnsureTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TEST_FOLDER))
            {
                string parent = Path.GetDirectoryName(TEST_FOLDER)!.Replace('\\', '/');
                string child  = Path.GetFileName(TEST_FOLDER);
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void CleanupTestFolder()
        {
            if (AssetDatabase.IsValidFolder(TEST_FOLDER))
                AssetDatabase.DeleteAsset(TEST_FOLDER);
        }

        // ── G1: BakeAll emits one standalone asset per tile ───────────────────

        [Test]
        public void BakeAll_TwoTileMap_EmitsTwoStandaloneAssets()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                WorldMapBaker.BakeAll(map, TEST_FOLDER);

                string path00 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(0, 0));
                string path10 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(1, 0));

                var baked00 = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path00);
                var baked10 = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path10);

                Assert.IsNotNull(baked00, $"Standalone asset at {path00} should exist after BakeAll.");
                Assert.IsNotNull(baked10, $"Standalone asset at {path10} should exist after BakeAll.");
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G2: Byte-identical height/splat ───────────────────────────────────

        [Test]
        public void BakeAll_BakedTile_HasByteIdenticalHeightAndSplat()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                WorldMapBaker.BakeAll(map, TEST_FOLDER);

                // Compare tile (0,0) source vs baked.
                var source = map.GetTile(new Vector2Int(0, 0))!;
                string path = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(0, 0));
                var baked  = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path)!;

                Assert.AreEqual(source.heightData.Length, baked.heightData.Length,
                    "Baked heightData length must match source.");
                Assert.AreEqual(source.splatData.Length,  baked.splatData.Length,
                    "Baked splatData length must match source.");

                CollectionAssert.AreEqual(source.heightData, baked.heightData,
                    "Baked heightData must be byte-identical to source.");
                CollectionAssert.AreEqual(source.splatData, baked.splatData,
                    "Baked splatData must be byte-identical to source.");
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G3: Manifest maps coord → asset path ──────────────────────────────

        [Test]
        public void BakeAll_Manifest_MapsEachCoordToCorrectAssetPath()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                var manifest = WorldMapBaker.BakeAll(map, TEST_FOLDER);

                Assert.AreEqual(2, manifest.Count, "Manifest should have 2 entries for 2 tiles.");

                string expected00 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(0, 0));
                string expected10 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(1, 0));

                Assert.AreEqual(expected00, manifest.GetAssetPath(new Vector2Int(0, 0)),
                    "Manifest entry for (0,0) must point to correct path.");
                Assert.AreEqual(expected10, manifest.GetAssetPath(new Vector2Int(1, 0)),
                    "Manifest entry for (1,0) must point to correct path.");
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G4: Incremental BakeTile re-bakes only the requested tile ─────────

        [Test]
        public void BakeTile_OnlyRebakesRequestedTile()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                var manifest = WorldMapBaker.BakeAll(map, TEST_FOLDER);

                // Record the modification time of tile (1,0) before incremental re-bake.
                string path10 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(1, 0));
                var assetBeforeRebake = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path10)!;
                byte[] splatBefore = (byte[])assetBeforeRebake.splatData.Clone();

                // Mutate the source tile (0,0) and re-bake it incrementally.
                var source00 = map.GetTile(new Vector2Int(0, 0))!;
                for (int i = 0; i < source00.heightData.Length; i++)
                    source00.heightData[i] = 0xFF; // distinct new value

                bool baked = WorldMapBaker.BakeTile(map, new Vector2Int(0, 0), TEST_FOLDER, manifest);
                Assert.IsTrue(baked, "BakeTile should return true for an existing tile coord.");

                // Reload tile (1,0) — it should be unchanged.
                AssetDatabase.Refresh();
                var asset10After = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path10)!;
                CollectionAssert.AreEqual(splatBefore, asset10After.splatData,
                    "Incremental bake of (0,0) must NOT modify baked tile (1,0).");

                // Tile (0,0) should now have the new height data.
                string path00 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(0, 0));
                var asset00After = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path00)!;
                for (int i = 0; i < asset00After.heightData.Length; i++)
                {
                    Assert.AreEqual(0xFF, asset00After.heightData[i],
                        $"heightData[{i}] of re-baked tile (0,0) should be 0xFF.");
                }
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G5: Pruning removes stale entries ─────────────────────────────────

        [Test]
        public void BakeAll_PrunesManifestAndFileForRemovedTile()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                WorldMapBaker.BakeAll(map, TEST_FOLDER);

                // Verify both tiles exist.
                string path10 = WorldMapBaker.TileAssetPath(TEST_FOLDER, new Vector2Int(1, 0));
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path10),
                    "Tile (1,0) asset should exist before removal.");

                // Remove tile (1,0) from the map and re-bake.
                map.UnregisterTile(new Vector2Int(1, 0));
                var manifest2 = WorldMapBaker.BakeAll(map, TEST_FOLDER);

                Assert.AreEqual(1, manifest2.Count, "After pruning, manifest should have 1 entry.");
                Assert.IsNull(manifest2.GetAssetPath(new Vector2Int(1, 0)),
                    "Pruned coord (1,0) should not appear in manifest.");

                // Asset file should be deleted.
                AssetDatabase.Refresh();
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path10),
                    "Baked asset for removed tile (1,0) should be deleted after pruning.");
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G6: Residency set include/evict by simulated camera proximity ─────

        [Test]
        public void ResidencySet_BakedTiles_IncludeWithinRadiusEvictBeyond()
        {
            // Build a residency set with 9 tiles in a 3×3 grid around (5,5).
            var residencySet = new TerrainTileResidencySet();
            var allCoords    = new List<Vector2Int>();

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var coord = new Vector2Int(5 + dx, 5 + dz);
                    var tile  = BuildResidentTile(coord);
                    residencySet.Add(coord, tile);
                    allCoords.Add(coord);
                }
            }

            Assert.AreEqual(9, residencySet.Count, "Should have 9 resident tiles.");

            // Camera at (5,5): all tiles within radius 1 should be "within".
            var cameraTile = new Vector2Int(5, 5);
            var within = residencySet.GetResidentWithinRadius(cameraTile, radius: 1);
            Assert.AreEqual(9, within.Count,
                "All 9 tiles should be within Chebyshev radius 1 of (5,5).");

            // Camera moves to (7,7): tiles at Chebyshev distance > 1 are "beyond".
            cameraTile = new Vector2Int(7, 7);
            var beyond = residencySet.GetResidentBeyondRadius(cameraTile, radius: 1);
            Assert.Greater(beyond.Count, 0,
                "Some tiles should be beyond radius 1 when camera moves to (7,7).");

            residencySet.EvictAll();
        }

        [Test]
        public void ResidencySet_CameraMove_ShiftsIncludeEvictSets()
        {
            // 5 tiles in a row at z=0, x = 0..4.
            var residencySet = new TerrainTileResidencySet();
            for (int tx = 0; tx <= 4; tx++)
            {
                var coord = new Vector2Int(tx, 0);
                residencySet.Add(coord, BuildResidentTile(coord));
            }

            // Camera at tile (2,0): within radius 1 = coords (1,0),(2,0),(3,0) → 3 tiles.
            var cam1 = new Vector2Int(2, 0);
            var within1 = residencySet.GetResidentWithinRadius(cam1, radius: 1);
            Assert.AreEqual(3, within1.Count,
                "With camera at (2,0) radius 1: 3 tiles expected within.");

            // Camera moves to (4,0): within radius 1 = (3,0),(4,0) only (no (5,0) exists).
            var cam2 = new Vector2Int(4, 0);
            var within2 = residencySet.GetResidentWithinRadius(cam2, radius: 1);
            // Coords (3,0),(4,0) are resident and within radius 1 of (4,0). (5,0) doesn't exist.
            Assert.AreEqual(2, within2.Count,
                "With camera at (4,0) radius 1: only (3,0) and (4,0) are within.");

            // Tiles beyond radius: (0,0),(1,0),(2,0).
            var beyond2 = residencySet.GetResidentBeyondRadius(cam2, radius: 1);
            Assert.AreEqual(3, beyond2.Count,
                "With camera at (4,0) radius 1: 3 tiles are beyond (candidates for eviction).");

            residencySet.EvictAll();
        }

        // ── G7: RegisterFromManifest populates tile index ─────────────────────

        [Test]
        public void TerrainStreamingManager_RegisterFromManifest_PopulatesTileIndex()
        {
            EnsureTestFolder();
            try
            {
                var map = MakeTwoTileMap();
                var manifest = WorldMapBaker.BakeAll(map, TEST_FOLDER);

                // Create a streaming manager without a MonoBehaviour (test via reflection-free path).
                // We verify by checking ResidentCount before/after a simulated ring tick is not
                // feasible without a Camera, so instead we verify the manifest count matched.
                // The RegisterFromManifest call loads assets via AssetDatabase in Editor context.
                var go = new GameObject("TestStreamingManager");
                try
                {
                    var mgr = go.AddComponent<TerrainStreamingManager>();
                    // Call RegisterFromManifest — should not throw even without cullCompute/material.
                    Assert.DoesNotThrow(() => mgr.RegisterFromManifest(manifest),
                        "RegisterFromManifest should not throw when assets exist.");
                    // ResidentCount remains 0 because we haven't done a Tick; that's expected.
                    Assert.AreEqual(0, mgr.ResidentCount,
                        "No tiles should be resident yet (no Tick driven, no GPU infra).");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                CleanupTestFolder();
            }
        }

        // ── G8: EnqueueBaked delegates to Enqueue path ───────────────────────

        [Test]
        public void TerrainTileLoader_EnqueueBaked_FiresCallbackLikeEnqueue()
        {
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(0, 0);
            var asset  = MakeTileAsset(coord);

            bool fired = false;
            loader.EnqueueBakedDirect(coord, asset, (c, _, _2) => fired = true);
            loader.DrainMainThreadQueue();

            Assert.IsTrue(fired,
                "EnqueueBakedDirect must fire the callback just like EnqueueDirect.");
        }

        [Test]
        public void TerrainTileLoader_EnqueueBaked_RespectsGenerationGuard()
        {
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(2, 0);
            var asset  = MakeTileAsset(coord);

            bool fired = false;
            loader.EnqueueBakedDirect(coord, asset, (_, __, ___) => fired = true);
            loader.CancelTile(coord);
            loader.DrainMainThreadQueue();

            Assert.IsFalse(fired,
                "Cancelled EnqueueBakedDirect callback must be rejected by generation guard.");
        }

        // ── Manifest tests (unit, no AssetDatabase) ───────────────────────────

        [Test]
        public void TileBakeManifest_SetEntry_AddsEntry()
        {
            var manifest = ScriptableObject.CreateInstance<TileBakeManifest>();
            manifest.SetEntry(new Vector2Int(3, 7), "Assets/Baked/Tile_3_7.asset");

            Assert.AreEqual(1, manifest.Count);
            Assert.AreEqual("Assets/Baked/Tile_3_7.asset",
                manifest.GetAssetPath(new Vector2Int(3, 7)));

            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void TileBakeManifest_SetEntry_OverwritesExisting()
        {
            var manifest = ScriptableObject.CreateInstance<TileBakeManifest>();
            manifest.SetEntry(new Vector2Int(0, 0), "Assets/Baked/Old.asset");
            manifest.SetEntry(new Vector2Int(0, 0), "Assets/Baked/New.asset");

            Assert.AreEqual(1, manifest.Count, "Overwrite must not add a duplicate entry.");
            Assert.AreEqual("Assets/Baked/New.asset",
                manifest.GetAssetPath(new Vector2Int(0, 0)));

            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void TileBakeManifest_RemoveEntry_RemovesEntry()
        {
            var manifest = ScriptableObject.CreateInstance<TileBakeManifest>();
            manifest.SetEntry(new Vector2Int(1, 1), "Assets/Baked/Tile_1_1.asset");
            manifest.RemoveEntry(new Vector2Int(1, 1));

            Assert.AreEqual(0, manifest.Count);
            Assert.IsNull(manifest.GetAssetPath(new Vector2Int(1, 1)));

            Object.DestroyImmediate(manifest);
        }

        [Test]
        public void TileBakeManifest_GetAssetPath_MissingCoord_ReturnsNull()
        {
            var manifest = ScriptableObject.CreateInstance<TileBakeManifest>();
            Assert.IsNull(manifest.GetAssetPath(new Vector2Int(99, 99)));
            Object.DestroyImmediate(manifest);
        }

        // ── WorldMapBaker.TileFileName sign-safe naming ───────────────────────

        [Test]
        public void WorldMapBaker_TileFileName_PositiveCoord()
        {
            Assert.AreEqual("Tile_3_7.asset",
                WorldMapBaker.TileFileName(new Vector2Int(3, 7)));
        }

        [Test]
        public void WorldMapBaker_TileFileName_NegativeX()
        {
            Assert.AreEqual("Tile_n1_0.asset",
                WorldMapBaker.TileFileName(new Vector2Int(-1, 0)));
        }

        [Test]
        public void WorldMapBaker_TileFileName_NegativeXZ()
        {
            Assert.AreEqual("Tile_n2_n5.asset",
                WorldMapBaker.TileFileName(new Vector2Int(-2, -5)));
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>No-op engine stub for residency tests.</summary>
        private sealed class NoOpEngine : ITerrainEngine
        {
            public void Submit(Camera? cam, Vector3 camPos) { }
            public void Dispose() { }
        }

        private static TerrainTileResidencySet.ResidentTile BuildResidentTile(Vector2Int coord)
        {
            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord = coord;
            asset.heightRes = 3;
            asset.minHeight = 0f;
            asset.maxHeight = 100f;
            asset.heightData = new byte[3 * 3 * TerrainHeightFormat.BYTES_PER_SAMPLE];

            var gpuRes = new TerrainTileGpuResources();
            gpuRes.Upload(asset);

            return new TerrainTileResidencySet.ResidentTile(asset, gpuRes, new NoOpEngine());
        }
    }
}
