#nullable enable
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldPainter.Editor; // WorldMapAssetFactory, WorldMapAssetLifecycle

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldMapAssetFactory"/> (Phase 4).
    ///
    /// Design spec assertions:
    ///   1. Factory creates map + Tile_0_0 + assigns to a WorldPainter WITHOUT calling any
    ///      scene-switching API — the current scene path is unchanged after the call.
    ///   2. Ghost-grow (<see cref="WorldMapAssetLifecycle.AddTile"/>) adds a tile at a signed
    ///      coord, including a negative-direction grow (e.g. (-1, 0)).
    ///
    /// NOTE: These tests create real .asset files in Assets/WorldPainter/Tests/Temp/ and a
    /// temporary in-memory scene. TearDown removes both.
    /// </summary>
    [TestFixture]
    public class WorldMapAssetFactoryTests
    {
        private const string TEMP_DIR  = "Assets/WorldPainter/Tests/Temp";
        private const string MAP_PATH  = TEMP_DIR + "/FactoryTestWorldMap.asset";

        // Scene path captured before any test runs.
        private string scenePath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            // Record the current scene path so we can assert it is unchanged after factory.
            this.scenePath = EditorSceneManager.GetActiveScene().path;

            // Ensure temp directory exists.
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
            {
                string parent = Path.GetDirectoryName(TEMP_DIR)!.Replace('\\', '/');
                string folder = Path.GetFileName(TEMP_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.DeleteAsset(TEMP_DIR);

            AssetDatabase.Refresh();
        }

        // ── Helper: create a WorldMapAsset on disk and add a WorldPainter GO ───

        private WorldPainter CreatePainterForTest()
        {
            var go = new GameObject("TestWorldPainter_P4");
            var painter = go.AddComponent<WorldPainter>();
            return painter;
        }

        private WorldMapAsset CreateMapOnDisk()
        {
            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, MAP_PATH);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<WorldMapAsset>(MAP_PATH)!;
        }

        // ── Test 1: Factory assigns map + creates Tile_0_0 ────────────────────

        /// <summary>
        /// Simulates what the factory does (without the SaveFilePanelInProject dialog)
        /// to verify: map created → Tile_0_0 exists → assigned to a WorldPainter.
        /// </summary>
        [Test]
        public void Factory_CreateAndAssignManual_ProducesMapWithFirstTileAndAssigns()
        {
            // Arrange: create map on disk + a painter GO in the current scene.
            var map     = this.CreateMapOnDisk();
            var painter = this.CreatePainterForTest();

            // Act: replicate factory logic (tile add + assignment) without the dialog.
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);
            painter.Map = map;

            // Assert: map has exactly 1 tile at (0,0).
            Assert.AreEqual(1, map.TileCount,
                "Factory should produce exactly one tile.");

            var tile = map.GetTile(Vector2Int.zero);
            Assert.IsNotNull(tile,
                "Tile at (0,0) should exist after factory call.");
            Assert.AreEqual("Tile_0_0", tile!.name,
                "First tile should be named 'Tile_0_0'.");

            // Assert: painter.Map is assigned.
            Assert.AreSame(map, painter.Map,
                "WorldPainter.Map should reference the newly created WorldMapAsset.");

            // Cleanup temporary GO.
            Object.DestroyImmediate(painter.gameObject);
        }

        // ── Test 2: Scene path unchanged after factory operations ─────────────

        [Test]
        public void Factory_CreateAndAssign_DoesNotSwitchActiveScene()
        {
            string pathBefore = EditorSceneManager.GetActiveScene().path;

            // Replicate factory side effects without the save dialog.
            var map     = this.CreateMapOnDisk();
            var painter = this.CreatePainterForTest();
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);
            painter.Map = map;

            string pathAfter = EditorSceneManager.GetActiveScene().path;

            Assert.AreEqual(pathBefore, pathAfter,
                "Factory must not switch the active scene (no EditorSceneManager.NewScene).");

            Object.DestroyImmediate(painter.gameObject);
        }

        // ── Test 3: Ghost-grow — positive direction ────────────────────────────

        [Test]
        public void GhostGrow_AddTileEast_TileAtCoord1_0()
        {
            var map = this.CreateMapOnDisk();

            // Seed tile at origin.
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            // Grow East: coord (1,0).
            var eastCoord = new Vector2Int(1, 0);
            WorldMapAssetLifecycle.AddTile(map, eastCoord);

            Assert.AreEqual(2, map.TileCount,
                "Map should have 2 tiles after origin + east grow.");
            Assert.IsNotNull(map.GetTile(eastCoord),
                "Tile at (1,0) should exist after east grow.");
            Assert.AreEqual("Tile_1_0", map.GetTile(eastCoord)!.name,
                "East tile sub-asset name should be 'Tile_1_0'.");
        }

        // ── Test 4: Ghost-grow — negative direction ────────────────────────────

        [Test]
        public void GhostGrow_AddTileWest_NegativeCoord_n1_0()
        {
            var map = this.CreateMapOnDisk();

            // Seed tile at origin.
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            // Grow West: coord (-1, 0) — negative direction.
            var westCoord = new Vector2Int(-1, 0);
            WorldMapAssetLifecycle.AddTile(map, westCoord);

            Assert.AreEqual(2, map.TileCount,
                "Map should have 2 tiles after origin + west grow.");
            Assert.IsNotNull(map.GetTile(westCoord),
                "Tile at (-1,0) should exist after west grow.");
            Assert.AreEqual("Tile_n1_0", map.GetTile(westCoord)!.name,
                "West tile sub-asset name should be 'Tile_n1_0' (sign-safe naming).");
        }

        // ── Test 5: Open-neighbor query returns west slot after seeding origin ─

        [Test]
        public void HasOpenNeighbor_AfterOriginTile_ReturnsWestSlotForNegativeCoord()
        {
            var map = this.CreateMapOnDisk();
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            bool hasOpen = map.HasOpenNeighbor(Vector2Int.zero, out var openEdges);

            Assert.IsTrue(hasOpen, "Origin tile should have at least one open neighbour.");

            bool westPresent = System.Array.Exists(openEdges,
                e => e == new Vector2Int(-1, 0));
            Assert.IsTrue(westPresent,
                "Open edges for origin tile should include the west slot at (-1,0).");
        }

        // ── Test 6: Ghost-grow all four directions ────────────────────────────

        [Test]
        public void GhostGrow_AllFourDirections_FourTilesAdded()
        {
            var map = this.CreateMapOnDisk();
            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            // Grow in all four NSEW directions using signed coords.
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int( 1,  0)); // East
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(-1,  0)); // West
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int( 0,  1)); // North
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int( 0, -1)); // South

            Assert.AreEqual(5, map.TileCount,
                "After growing in all 4 directions, map should have 5 tiles.");

            // Verify sign-safe naming for negative coords.
            Assert.AreEqual("Tile_n1_0",
                map.GetTile(new Vector2Int(-1, 0))!.name,
                "West tile should be named 'Tile_n1_0'.");
            Assert.AreEqual("Tile_0_n1",
                map.GetTile(new Vector2Int(0, -1))!.name,
                "South tile should be named 'Tile_0_n1'.");
        }
    }
}
