#nullable enable
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WorldPainter.Editor; // WorldMapAssetLifecycle, WorldPainterState, WorldPainterPreviewCache

namespace WorldPainter.Tests
{
    /// <summary>
    /// Phase 5 verification tests.
    ///
    /// 1. ALLOC: 3 tiles + activate density layer → each tile gains R8 256×256 channel keyed by layerId.
    /// 2. FREE:  remove layer → channels freed + zero orphan sub-assets.
    /// 3. PREVIEW: <see cref="WorldPainterPreviewCache"/> produces a non-null thumbnail for a grass LOD0
    ///    mesh+material (light smoke — headless EditMode may return null, assertion is lenient).
    /// 4. ACTIVE-LAYER STATE: clicking a square (calling SetActiveLayer) sets
    ///    <see cref="WorldPainterState.ActiveLayerId"/> / <see cref="WorldPainterState.ActiveLayerKind"/>
    ///    and NEVER modifies <c>UnityEditor.Selection.activeObject</c>.
    /// </summary>
    [TestFixture]
    public class WorldMapLayerAllocTests
    {
        private const string TEMP_DIR = "Assets/WorldPainter/Tests/Temp";
        private const string MAP_PATH = TEMP_DIR + "/P5TestWorldMap.asset";

        // ── Setup / teardown ──────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_DIR))
            {
                string parent = Path.GetDirectoryName(TEMP_DIR)!.Replace('\\', '/');
                string folder = Path.GetFileName(TEMP_DIR);
                AssetDatabase.CreateFolder(parent, folder);
            }

            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, MAP_PATH);
            AssetDatabase.SaveAssets();

            // Reset WorldPainterState so tests start clean.
            WorldPainterState.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TEMP_DIR))
                AssetDatabase.DeleteAsset(TEMP_DIR);
            AssetDatabase.Refresh();

            WorldPainterState.Reset();
        }

        private WorldMapAsset LoadMap() =>
            AssetDatabase.LoadAssetAtPath<WorldMapAsset>(MAP_PATH)!;

        // ── G5.1 Alloc: 3 tiles + layer → each gains R8 256×256 channel ──────

        [Test]
        public void ThreeTiles_AddDensityLayer_EachTileGainsR8Channel()
        {
            var map = this.LoadMap();

            // Add 3 tiles first.
            var c0 = new Vector2Int(0,  0);
            var c1 = new Vector2Int(1,  0);
            var c2 = new Vector2Int(-1, 0);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);
            WorldMapAssetLifecycle.AddTile(map, c2);

            // Now activate a density layer.
            var layer = WorldMapAssetLifecycle.AddDensityLayer(map, "Meadow");
            string layerId = layer.name; // "Layer_Meadow"

            // Assert each tile has the channel with correct dimensions.
            foreach (var coord in new[] { c0, c1, c2 })
            {
                var tile = map.GetTile(coord);
                Assert.IsNotNull(tile, $"Tile at {coord} must exist.");

                var channel = tile!.GetDensityChannel(layerId);
                Assert.IsNotNull(channel,
                    $"Tile at {coord} must have density channel '{layerId}' after AddDensityLayer.");

                Assert.AreEqual(
                    WorldMapGrid.DENSITY_RES * WorldMapGrid.DENSITY_RES,
                    channel!.densityData.Length,
                    $"Tile {coord} channel densityData must be {WorldMapGrid.DENSITY_RES}×{WorldMapGrid.DENSITY_RES} = " +
                    $"{WorldMapGrid.DENSITY_RES * WorldMapGrid.DENSITY_RES} bytes (R8 256×256).");
            }
        }

        // ── G5.1 Free: remove → channels freed + zero orphan sub-assets ───────

        [Test]
        public void ThreeTiles_RemoveDensityLayer_ChannelsFreedAndZeroOrphans()
        {
            var map = this.LoadMap();

            var c0 = new Vector2Int(0, 0);
            var c1 = new Vector2Int(1, 0);
            var c2 = new Vector2Int(0, 1);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);
            WorldMapAssetLifecycle.AddTile(map, c2);

            var layer = WorldMapAssetLifecycle.AddDensityLayer(map, "Grass");
            string layerId = layer.name;

            // Remove the layer.
            WorldMapAssetLifecycle.RemoveLayer(map, layer);

            // Channels must be freed.
            foreach (var coord in new[] { c0, c1, c2 })
            {
                var tile    = map.GetTile(coord)!;
                var channel = tile.GetDensityChannel(layerId);
                Assert.IsNull(channel,
                    $"Tile {coord}: density channel '{layerId}' must be freed after RemoveLayer.");
            }

            // No orphan DensityScatterLayer sub-assets.
            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);
            var allAssets    = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            int layerCount   = allAssets.OfType<DensityScatterLayer>().Count();
            Assert.AreEqual(0, layerCount,
                "Zero DensityScatterLayer sub-assets must remain after RemoveLayer.");
        }

        // ── G5.1 New-tile auto-alloc ──────────────────────────────────────────

        [Test]
        public void AddTileAfterLayer_TileAutoAllocatesChannelForAllDensityLayers()
        {
            var map = this.LoadMap();

            // Add two density layers first.
            var layerA = WorldMapAssetLifecycle.AddDensityLayer(map, "LayerA");
            var layerB = WorldMapAssetLifecycle.AddDensityLayer(map, "LayerB");

            // Then add a tile.
            var coord = new Vector2Int(5, -2);
            WorldMapAssetLifecycle.AddTile(map, coord);

            var tile = map.GetTile(coord)!;
            Assert.IsNotNull(tile.GetDensityChannel(layerA.name),
                $"New tile must auto-allocate channel for '{layerA.name}'.");
            Assert.IsNotNull(tile.GetDensityChannel(layerB.name),
                $"New tile must auto-allocate channel for '{layerB.name}'.");
        }

        // ── G5.2 Preview: non-null thumbnail smoke ────────────────────────────

        [Test]
        public void PreviewCache_NonNullThumbForMeshAndMaterial()
        {
            var cache = new WorldPainterPreviewCache();
            try
            {
                var mesh = MakeMesh();
                try
                {
                    // GetOrRender may legitimately return null in headless CI (no display).
                    // We assert: it MUST NOT throw, and if it returns non-null the texture
                    // must have the correct pixel dimensions.
                    Texture2D? thumb = null;
                    Assert.DoesNotThrow(
                        () => { thumb = cache.GetOrRender(key: 9001, mesh: mesh, mat: null, px: 16); },
                        "GetOrRender must not throw for a valid mesh.");

                    if (thumb != null)
                    {
                        Assert.AreEqual(16, thumb.width,  "Thumbnail width must match requested px.");
                        Assert.AreEqual(16, thumb.height, "Thumbnail height must match requested px.");
                    }
                    // No assertion when thumb == null — headless editor has no display device.
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            finally { cache.Cleanup(); }
        }

        // ── G5.2 Active-layer state: SetActiveLayer + no Selection change ─────

        [Test]
        public void SetActiveLayer_SetsIdAndKind_DoesNotChangeUnitySelection()
        {
            // Record current Unity selection to detect any change.
            Object? selectionBefore = Selection.activeObject;

            // Act: simulate clicking a Meadow layer chip.
            string meadowId = WorldMapAssetLifecycle.LayerSubAssetName("Grass");
            WorldPainterState.SetActiveLayer(meadowId, WorldPainterState.PaintLayerKind.Meadow);

            // Assert state was set.
            Assert.AreEqual(meadowId, WorldPainterState.ActiveLayerId,
                "ActiveLayerId must equal the id passed to SetActiveLayer.");
            Assert.AreEqual(WorldPainterState.PaintLayerKind.Meadow, WorldPainterState.ActiveLayerKind,
                "ActiveLayerKind must equal the kind passed to SetActiveLayer.");

            // Assert Unity Selection was NOT modified.
            Assert.AreSame(selectionBefore, Selection.activeObject,
                "SetActiveLayer must NOT change UnityEditor.Selection.activeObject.");
        }

        [Test]
        public void SetActiveLayer_Splat_SetsCorrectKind()
        {
            WorldPainterState.SetActiveLayer("Splat 0", WorldPainterState.PaintLayerKind.Splat);

            Assert.AreEqual(WorldPainterState.PaintLayerKind.Splat, WorldPainterState.ActiveLayerKind);
            Assert.AreEqual("Splat 0", WorldPainterState.ActiveLayerId);
        }

        [Test]
        public void SetActiveLayer_Prop_SetsCorrectKind()
        {
            string propId = WorldMapAssetLifecycle.LayerSubAssetName("Tree");
            WorldPainterState.SetActiveLayer(propId, WorldPainterState.PaintLayerKind.Prop);

            Assert.AreEqual(WorldPainterState.PaintLayerKind.Prop, WorldPainterState.ActiveLayerKind);
            Assert.AreEqual(propId, WorldPainterState.ActiveLayerId);
        }

        [Test]
        public void SetActiveLayer_FiresActiveLayerChangedEvent()
        {
            bool eventFired = false;
            string receivedId = string.Empty;
            WorldPainterState.PaintLayerKind receivedKind = WorldPainterState.PaintLayerKind.None;

            WorldPainterState.ActiveLayerChanged += (id, kind) =>
            {
                eventFired    = true;
                receivedId    = id;
                receivedKind  = kind;
            };

            string meadowId = WorldMapAssetLifecycle.LayerSubAssetName("Flower");
            WorldPainterState.SetActiveLayer(meadowId, WorldPainterState.PaintLayerKind.Meadow);

            Assert.IsTrue(eventFired, "ActiveLayerChanged event must fire when layer changes.");
            Assert.AreEqual(meadowId,                            receivedId);
            Assert.AreEqual(WorldPainterState.PaintLayerKind.Meadow, receivedKind);
        }

        [Test]
        public void SetActiveLayer_DoesNotFireEvent_WhenSameValue()
        {
            string id = "Layer_Same";
            WorldPainterState.SetActiveLayer(id, WorldPainterState.PaintLayerKind.Meadow);

            int fireCount = 0;
            WorldPainterState.ActiveLayerChanged += (_, __) => fireCount++;

            // Set the same value again.
            WorldPainterState.SetActiveLayer(id, WorldPainterState.PaintLayerKind.Meadow);

            Assert.AreEqual(0, fireCount,
                "ActiveLayerChanged must NOT fire when the id+kind pair is unchanged.");
        }

        [Test]
        public void Reset_ClearsActivePaintLayer()
        {
            WorldPainterState.SetActiveLayer("Layer_Test", WorldPainterState.PaintLayerKind.Meadow);
            WorldPainterState.Reset();

            Assert.AreEqual(string.Empty,                          WorldPainterState.ActiveLayerId);
            Assert.AreEqual(WorldPainterState.PaintLayerKind.None, WorldPainterState.ActiveLayerKind);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Mesh MakeMesh()
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            m.vertices  = new[] { Vector3.zero, Vector3.right, Vector3.up };
            m.triangles = new[] { 0, 1, 2 };
            m.RecalculateNormals();
            return m;
        }
    }
}
