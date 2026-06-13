#nullable enable
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainter"/> Phase 2 splat-layer model:
    ///   – 5th add is rejected with an <see cref="System.InvalidOperationException"/>
    ///     (errors-over-fallbacks rule; hard cap = <see cref="WorldPainter.MAX_SPLAT_LAYERS"/>).
    ///   – Reorder via list manipulation changes blend priority
    ///     (channel 0 = R = highest priority, matches TerrainSplatWeightTests SSOT).
    /// </summary>
    [TestFixture]
    public sealed class WorldPainterSplatLayerTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static WorldPainter CreatePainter()
        {
            var go = new GameObject("TestWorldPainter");
            return go.AddComponent<WorldPainter>();
        }

        private static void DestroyPainter(WorldPainter painter)
        {
            if (painter != null)
                Object.DestroyImmediate(painter.gameObject);
        }

        private static SplatLayerDef MakeLayer(string name) =>
            new SplatLayerDef { name = name, tiling = Vector2.one };

        // ── Cap enforcement ───────────────────────────────────────────────────

        [Test]
        public void AddSplatLayer_UpToMax_Succeeds()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    Assert.DoesNotThrow(
                        () => painter.AddSplatLayer(MakeLayer($"Layer{i}")),
                        $"Adding layer {i} should succeed (cap is {WorldPainter.MAX_SPLAT_LAYERS}).");

                Assert.AreEqual(WorldPainter.MAX_SPLAT_LAYERS, painter.SplatLayers.Count);
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void AddSplatLayer_FifthLayer_ThrowsInvalidOperationException()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    painter.AddSplatLayer(MakeLayer($"Layer{i}"));

                // 5th add MUST throw — not silently drop.
                Assert.Throws<System.InvalidOperationException>(
                    () => painter.AddSplatLayer(MakeLayer("ExtraLayer")),
                    "Adding a 5th splat layer must throw InvalidOperationException.");

                // Count must still be capped at MAX, not 5.
                Assert.AreEqual(WorldPainter.MAX_SPLAT_LAYERS, painter.SplatLayers.Count,
                    "Layer count must stay at MAX_SPLAT_LAYERS after rejected add.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void AddSplatLayer_ErrorMessage_ContainsLayerName()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    painter.AddSplatLayer(MakeLayer($"Layer{i}"));

                var ex = Assert.Throws<System.InvalidOperationException>(
                    () => painter.AddSplatLayer(MakeLayer("SurfacedError")));

                Assert.IsTrue(ex!.Message.Contains("SurfacedError"),
                    "Exception message must include the layer name that was rejected.");
                Assert.IsTrue(ex.Message.Contains(WorldPainter.MAX_SPLAT_LAYERS.ToString()),
                    "Exception message must include the cap value.");
            }
            finally { DestroyPainter(painter); }
        }

        // ── Reorder priority ──────────────────────────────────────────────────

        [Test]
        public void SplatLayers_Reorder_ChangesChannelIndex()
        {
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("Alpha"));
                painter.AddSplatLayer(MakeLayer("Beta"));
                painter.AddSplatLayer(MakeLayer("Gamma"));

                // Initial order: Alpha=0, Beta=1, Gamma=2
                Assert.AreEqual("Alpha", painter.SplatLayers[0].name);
                Assert.AreEqual("Beta",  painter.SplatLayers[1].name);
                Assert.AreEqual("Gamma", painter.SplatLayers[2].name);

                // Simulate reorder: move index 2 → index 0 (Gamma becomes channel 0 = highest priority)
                var layers = painter.SplatLayers;
                var moved  = layers[2];
                layers.RemoveAt(2);
                layers.Insert(0, moved);

                Assert.AreEqual("Gamma", painter.SplatLayers[0].name,
                    "After reorder, Gamma must be at channel 0 (highest priority / R channel).");
                Assert.AreEqual("Alpha", painter.SplatLayers[1].name);
                Assert.AreEqual("Beta",  painter.SplatLayers[2].name);
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void SplatLayers_ReorderSwap_OverlapWinnerChanges()
        {
            // Channel 0 (R) always wins overlap in NormalizeWeights.
            // Verify that placing a layer at index 0 makes it the overlap winner.
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("Ground")); // ch 0 = R = winner
                painter.AddSplatLayer(MakeLayer("Grass"));  // ch 1 = G

                // Before swap: Ground (index 0) is channel R — it's the winner.
                Assert.AreEqual("Ground", painter.SplatLayers[0].name);

                // Swap: move Grass to index 0.
                var layers = painter.SplatLayers;
                var grass   = layers[1];
                layers.RemoveAt(1);
                layers.Insert(0, grass);

                // After swap: Grass is channel 0 = new winner.
                Assert.AreEqual("Grass", painter.SplatLayers[0].name,
                    "After swap, Grass at index 0 becomes the R-channel (overlap winner).");
                Assert.AreEqual("Ground", painter.SplatLayers[1].name);
            }
            finally { DestroyPainter(painter); }
        }

        // ── Albedo sub-asset creation (Phase 1) ───────────────────────────────

        private const string TEMP_DIR  = "Assets/WorldPainter/Tests/Temp";
        private const string MAP_PATH  = TEMP_DIR + "/SplatAlbedoTestMap.asset";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
            {
                string parent = Path.GetDirectoryName(TEMP_DIR)!.Replace('\\', '/');
                string folder = Path.GetFileName(TEMP_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.DeleteAsset(TEMP_DIR);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// After <see cref="WorldMapAssetLifecycle.AddSplatLayer"/> the created
        /// <see cref="TerrainLayerSet"/> must have at least one albedo assigned
        /// (<see cref="TerrainLayerSet.ActiveLayerCount"/> ≥ 1) AND the albedo
        /// Texture2D must be a sub-asset of the map file (not a standalone asset).
        /// </summary>
        [Test]
        public void AddSplatLayer_CreatesBlankAlbedoSubAsset()
        {
            // Create a fresh saved map for this test.
            string testMapPath = TEMP_DIR + "/SplatAlbedoTestMap_Run.asset";
            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, testMapPath);
            AssetDatabase.SaveAssets();
            map = AssetDatabase.LoadAssetAtPath<WorldMapAsset>(testMapPath)!;

            try
            {
                SplatLayer splatLayer = WorldMapAssetLifecycle.AddSplatLayer(map, "TestGround");

                // The set must have at least 1 active layer.
                TerrainLayerSet? set = splatLayer.LayerSet;
                Assert.IsNotNull(set, "AddSplatLayer must create a TerrainLayerSet.");
                Assert.GreaterOrEqual(set!.ActiveLayerCount, 1,
                    "TerrainLayerSet.ActiveLayerCount must be >= 1 after AddSplatLayer.");

                // The albedo must be a sub-asset of the map (same path).
                AssetDatabase.ImportAsset(testMapPath, ImportAssetOptions.ForceUpdate);
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(testMapPath);
                var albedos   = allAssets.OfType<Texture2D>().ToArray();

                Assert.IsTrue(albedos.Length >= 1,
                    "At least one Texture2D sub-asset (albedo) must exist in the map asset.");

                foreach (Texture2D albedo in albedos)
                {
                    string albedoPath = AssetDatabase.GetAssetPath(albedo);
                    Assert.AreEqual(testMapPath, albedoPath,
                        $"Albedo sub-asset '{albedo.name}' must be a child of the map asset, " +
                        $"not a standalone asset at '{albedoPath}'.");
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(testMapPath);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// After <see cref="WorldMapAssetLifecycle.RemoveSurfaceLayer"/> for a splat layer,
        /// no Texture2D sub-assets must remain in the map (albedos cleaned up).
        /// </summary>
        [Test]
        public void RemoveSplatLayer_RemovesAlbedoSubAssets()
        {
            string testMapPath = TEMP_DIR + "/SplatRemoveTestMap_Run.asset";
            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, testMapPath);
            AssetDatabase.SaveAssets();
            map = AssetDatabase.LoadAssetAtPath<WorldMapAsset>(testMapPath)!;

            try
            {
                SplatLayer splatLayer = WorldMapAssetLifecycle.AddSplatLayer(map, "RemoveMe");

                // Confirm albedo was created.
                AssetDatabase.ImportAsset(testMapPath, ImportAssetOptions.ForceUpdate);
                var before = AssetDatabase.LoadAllAssetsAtPath(testMapPath).OfType<Texture2D>().ToArray();
                Assert.IsTrue(before.Length >= 1, "Albedo sub-asset must exist before removal.");

                // Remove the splat layer.
                map = AssetDatabase.LoadAssetAtPath<WorldMapAsset>(testMapPath)!;
                var layers = map.SurfaceLayers;
                WorldPainterLayer? toRemove = layers.Count > 0 ? layers[0] : null;
                if (toRemove != null)
                    WorldMapAssetLifecycle.RemoveSurfaceLayer(map, toRemove);

                // No Texture2D sub-assets should remain.
                AssetDatabase.ImportAsset(testMapPath, ImportAssetOptions.ForceUpdate);
                var after = AssetDatabase.LoadAllAssetsAtPath(testMapPath).OfType<Texture2D>().ToArray();
                Assert.AreEqual(0, after.Length,
                    "All albedo Texture2D sub-assets must be removed with the splat layer.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(testMapPath);
                AssetDatabase.Refresh();
            }
        }

        // ── ActiveSplatLayerIndex clamping ────────────────────────────────────

        [Test]
        public void ActiveSplatLayerIndex_Clamps_ToValidRange()
        {
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("L0"));
                painter.AddSplatLayer(MakeLayer("L1"));

                painter.ActiveSplatLayerIndex = 99; // beyond range
                Assert.AreEqual(1, painter.ActiveSplatLayerIndex,
                    "ActiveSplatLayerIndex must clamp to splatLayers.Count-1.");

                painter.ActiveSplatLayerIndex = -5; // below range
                Assert.AreEqual(0, painter.ActiveSplatLayerIndex,
                    "ActiveSplatLayerIndex must clamp to 0 when set negative.");
            }
            finally { DestroyPainter(painter); }
        }
    }
}
