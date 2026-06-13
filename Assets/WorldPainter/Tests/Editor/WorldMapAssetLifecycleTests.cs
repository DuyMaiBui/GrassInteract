#nullable enable
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WorldPainter.Editor; // WorldMapAssetLifecycle

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldMapAssetLifecycle"/>.
    ///
    /// Covers the two HIGH-RISK mitigations from the plan risk register (score=16):
    ///   1. ORPHAN GUARD: add tile then remove → <c>AssetDatabase.LoadAllAssetsAtPath</c>
    ///      returns ONLY the root SO (zero orphan sub-assets).
    ///   2. Round-trip persistence: sub-asset survives SaveAssets + reimport.
    ///
    /// NOTE: These tests create real .asset files in Assets/WorldPainter/Tests/Temp/.
    /// The [TearDown] cleans up every temp asset so the project stays clean.
    ///
    /// The AddObjectToAsset round-trip spike is included here as the persistence test —
    /// this is the first use of AddObjectToAsset in this project.
    /// </summary>
    [TestFixture]
    public class WorldMapAssetLifecycleTests
    {
        private const string TEMP_DIR    = "Assets/WorldPainter/Tests/Temp";
        private const string MAP_PATH    = TEMP_DIR + "/TestWorldMap.asset";

        [SetUp]
        public void SetUp()
        {
            // Ensure temp directory exists.
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
            {
                string parent = Path.GetDirectoryName(TEMP_DIR)!.Replace('\\', '/');
                string folder = Path.GetFileName(TEMP_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }

            // Create a fresh WorldMapAsset on disk.
            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, MAP_PATH);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            // Delete all temp assets to leave the project clean.
            if (AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.DeleteAsset(TEMP_DIR);
            AssetDatabase.Refresh();
        }

        private WorldMapAsset LoadMap() =>
            AssetDatabase.LoadAssetAtPath<WorldMapAsset>(MAP_PATH)!;

        // ── AddObjectToAsset round-trip spike ─────────────────────────────────
        // This is the FIRST use of AddObjectToAsset in this project.
        // Prove: create → add → save → reimport → sub-asset persists, bytes intact.

        [Test]
        public void RoundTrip_AddTile_SubAssetPersistsAfterSaveAndReimport()
        {
            var map  = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            // Act: add tile via lifecycle (AddObjectToAsset + SaveAssets).
            var tile = WorldMapAssetLifecycle.AddTile(map, coord);

            // Reimport the asset to flush Unity's in-memory cache.
            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);

            // Assert: tile sub-asset still present after reimport.
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            var tileAssets = allAssets.OfType<TerrainTileAsset>().ToArray();

            Assert.AreEqual(1, tileAssets.Length,
                "Expected exactly 1 TerrainTileAsset sub-asset after reimport.");
            Assert.AreEqual("Tile_0_0", tileAssets[0].name,
                "Sub-asset name should be sign-safe 'Tile_0_0'.");
            Assert.AreEqual(coord, tileAssets[0].tileCoord,
                "tileCoord should survive serialization round-trip.");
        }

        [Test]
        public void RoundTrip_NegativeCoordTile_PersistsAfterReimport()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(-1, 0);

            WorldMapAssetLifecycle.AddTile(map, coord);
            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);

            var tileAssets = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH)
                .OfType<TerrainTileAsset>().ToArray();

            Assert.AreEqual(1, tileAssets.Length);
            Assert.AreEqual("Tile_n1_0", tileAssets[0].name,
                "Negative coord tile should use 'n' prefix: Tile_n1_0.");
            Assert.AreEqual(coord, tileAssets[0].tileCoord);
        }

        // ── ORPHAN GUARD (score=16 risk mitigation) ───────────────────────────

        [Test]
        public void OrphanGuard_AddThenRemoveTile_LeavesZeroOrphanSubAssets()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            // Add tile.
            WorldMapAssetLifecycle.AddTile(map, coord);

            // Remove tile.
            WorldMapAssetLifecycle.RemoveTile(map, coord);

            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);

            // Only the root WorldMapAsset should remain (no orphan TerrainTileAsset).
            int nonRootCount = allAssets.Count(a => !(a is WorldMapAsset));
            Assert.AreEqual(0, nonRootCount,
                $"Expected 0 orphan sub-assets after remove, found {nonRootCount}: " +
                string.Join(", ", allAssets.Where(a => !(a is WorldMapAsset)).Select(a => a.name)));
        }

        // ── Multiple tiles + negative coord ───────────────────────────────────

        [Test]
        public void AddThreeTiles_IncludingNegativeCoord_AllRegistered()
        {
            var map = this.LoadMap();

            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(0,  0));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(1,  0));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(-1, 0));

            Assert.AreEqual(3, map.TileCount);
            Assert.IsNotNull(map.GetTile(new Vector2Int(0,  0)));
            Assert.IsNotNull(map.GetTile(new Vector2Int(1,  0)));
            Assert.IsNotNull(map.GetTile(new Vector2Int(-1, 0)));
        }

        [Test]
        public void AddTile_SeedsValidFlatHeightData_SoTileRenders()
        {
            var map  = this.LoadMap();
            var tile = WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            // Regression: a freshly created tile must have valid (flat, zero-filled) heightData.
            // Without it IsHeightValid is false and WorldPainter.BuildOneTileAsset silently skips
            // the tile, so nothing renders in Scene/Game view.
            Assert.IsTrue(tile.IsHeightValid,
                "Freshly added tile must be height-valid so it renders.");
            Assert.AreEqual(tile.ExpectedHeightBytes, tile.heightData.Length,
                "heightData should be seeded to ExpectedHeightBytes (flat at minHeight).");
        }

        [Test]
        public void AddTile_Idempotent_WhenCalledTwice()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            var first  = WorldMapAssetLifecycle.AddTile(map, coord);
            var second = WorldMapAssetLifecycle.AddTile(map, coord);

            Assert.AreSame(first, second, "AddTile should return the existing tile on second call.");
            Assert.AreEqual(1, map.TileCount, "TileCount should not increase on duplicate add.");
        }
    }
}
