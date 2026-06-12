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
    ///   1. ORPHAN GUARD: add tile+layer then remove â†’ <c>AssetDatabase.LoadAllAssetsAtPath</c>
    ///      returns ONLY the root SO (zero orphan sub-assets).
    ///   2. Per-tile channel freed on layer remove.
    ///   3. Round-trip persistence: sub-asset survives SaveAssets + reimport.
    ///
    /// NOTE: These tests create real .asset files in Assets/WorldPainter/Tests/Temp/.
    /// The [TearDown] cleans up every temp asset so the project stays clean.
    ///
    /// The AddObjectToAsset round-trip spike is included here as the persistence test â€”
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

        // â”€â”€ AddObjectToAsset round-trip spike â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // This is the FIRST use of AddObjectToAsset in this project.
        // Prove: create â†’ add â†’ save â†’ reimport â†’ sub-asset persists, bytes intact.

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

        // â”€â”€ ORPHAN GUARD (score=16 risk mitigation) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        [Test]
        public void OrphanGuard_AddDensityLayerThenRemove_LeavesZeroOrphanSubAssets()
        {
            var map = this.LoadMap();

            // Add a density layer.
            WorldMapAssetLifecycle.AddDensityLayer(map, "Meadow");

            // Remove it.
            var layer = map.Layers.FirstOrDefault();
            Assert.IsNotNull(layer, "Layer should have been registered.");
            WorldMapAssetLifecycle.RemoveLayer(map, layer!);

            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);

            var allAssets     = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            int nonRootCount  = allAssets.Count(a => !(a is WorldMapAsset));
            Assert.AreEqual(0, nonRootCount,
                $"Expected 0 orphan sub-assets after layer remove, found {nonRootCount}.");
        }

        [Test]
        public void OrphanGuard_AddTileAndLayer_ThenRemoveBoth_LeavesZeroOrphans()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(2, 3);

            // Add tile and layer.
            WorldMapAssetLifecycle.AddTile(map, coord);
            WorldMapAssetLifecycle.AddDensityLayer(map, "Rocks");

            // Remove both.
            WorldMapAssetLifecycle.RemoveTile(map, coord);
            var layer = map.Layers.FirstOrDefault();
            Assert.IsNotNull(layer);
            WorldMapAssetLifecycle.RemoveLayer(map, layer!);

            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);

            var allAssets    = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            int nonRootCount = allAssets.Count(a => !(a is WorldMapAsset));
            Assert.AreEqual(0, nonRootCount,
                $"Expected 0 orphan sub-assets, found {nonRootCount}.");
        }

        // â”€â”€ Per-tile density channel alloc/free â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void DensityChannel_AllocatedOnTileWhenLayerAdded()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            // Add a tile first, then a layer.
            WorldMapAssetLifecycle.AddTile(map, coord);
            WorldMapAssetLifecycle.AddDensityLayer(map, "Meadow");

            var tile = map.GetTile(coord)!;
            Assert.IsNotNull(tile);

            // The layer sub-asset name includes the "Layer_" prefix.
            string layerId = WorldMapAssetLifecycle.LayerSubAssetName("Meadow");
            var channel    = tile.GetDensityChannel(layerId);
            Assert.IsNotNull(channel, $"Tile at {coord} should have density channel '{layerId}'.");
            Assert.AreEqual(WorldMapGrid.DENSITY_RES * WorldMapGrid.DENSITY_RES,
                channel!.densityData.Length,
                "Density channel should be 256Ã—256 bytes.");
        }

        [Test]
        public void DensityChannel_FreedFromTileWhenLayerRemoved()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            WorldMapAssetLifecycle.AddTile(map, coord);
            WorldMapAssetLifecycle.AddDensityLayer(map, "Meadow");

            var layer  = map.Layers.First();
            string lid = layer.name;

            WorldMapAssetLifecycle.RemoveLayer(map, layer);

            var tile    = map.GetTile(coord)!;
            var channel = tile.GetDensityChannel(lid);
            Assert.IsNull(channel, "Density channel should be freed when layer is removed.");
        }

        [Test]
        public void DensityChannel_AllocatedOnNewTile_ForExistingLayers()
        {
            var map = this.LoadMap();

            // Add layer first, then tile.
            WorldMapAssetLifecycle.AddDensityLayer(map, "Grass");
            var coord = new Vector2Int(5, -2);
            WorldMapAssetLifecycle.AddTile(map, coord);

            var tile   = map.GetTile(coord)!;
            string lid = WorldMapAssetLifecycle.LayerSubAssetName("Grass");
            var channel = tile.GetDensityChannel(lid);
            Assert.IsNotNull(channel,
                "Tile added after a layer should auto-allocate a density channel.");
        }

        // â”€â”€ Multiple tiles + negative coord â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        public void AddTile_Idempotent_WhenCalledTwice()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            var first  = WorldMapAssetLifecycle.AddTile(map, coord);
            var second = WorldMapAssetLifecycle.AddTile(map, coord);

            Assert.AreSame(first, second, "AddTile should return the existing tile on second call.");
            Assert.AreEqual(1, map.TileCount, "TileCount should not increase on duplicate add.");
        }

        // â”€â”€ Instance layer + prop bucket â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void PropBucket_AllocatedOnTileWhenInstanceLayerAdded()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            WorldMapAssetLifecycle.AddTile(map, coord);
            WorldMapAssetLifecycle.AddInstanceLayer(map, "Tree");

            var tile   = map.GetTile(coord)!;
            string lid = WorldMapAssetLifecycle.LayerSubAssetName("Tree");
            var bucket = tile.GetPropBucket(lid);
            Assert.IsNotNull(bucket, $"Tile should have a prop bucket for layer '{lid}'.");
        }

        [Test]
        public void PropBucket_FreedFromTileWhenInstanceLayerRemoved()
        {
            var map   = this.LoadMap();
            var coord = new Vector2Int(0, 0);

            WorldMapAssetLifecycle.AddTile(map, coord);
            WorldMapAssetLifecycle.AddInstanceLayer(map, "Rock");

            var layer  = map.Layers.First();
            string lid = layer.name;

            WorldMapAssetLifecycle.RemoveLayer(map, layer);

            var tile   = map.GetTile(coord)!;
            var bucket = tile.GetPropBucket(lid);
            Assert.IsNull(bucket, "Prop bucket should be freed when instance layer is removed.");
        }
    }
}
