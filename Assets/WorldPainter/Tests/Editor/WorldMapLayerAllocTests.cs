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
    /// Phase 5 verification tests — updated for the per-tile density model.
    ///
    /// GrassLayer now maps a density texture DIRECTLY per tile (no GrassVariant/palette indirection).
    ///
    /// 1. ALLOC: 2 tiles + AddGrassLayerWithBlades → layer has 2 densityTiles entries.
    /// 2. FREE:  remove layer → density textures freed + zero orphan sub-assets.
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

        // ── G6 Per-tile grass density alloc/remove tests ──────────────────────

        [Test]
        public void TwoTiles_AddGrassLayerWithBlades_LayerHasTwoDensityTiles()
        {
            var map = this.LoadMap();

            var c0 = new Vector2Int(0, 0);
            var c1 = new Vector2Int(1, 0);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);

            GrassLayer grass = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "TestGrass");

            // The layer should have 2 densityTiles (one per tile).
            var tiles = grass.EditorTileDensities;
            Assert.IsNotNull(tiles, "densityTiles must not be null.");
            Assert.AreEqual(2, tiles.Length,
                "GrassLayer must have exactly 2 densityTiles (one per tile).");

            // Both textures must be sub-assets of the map.
            string mapPath = AssetDatabase.GetAssetPath(map);
            foreach (var entry in tiles)
            {
                Assert.IsNotNull(entry.tex,
                    $"Tile {entry.coord} density tex must not be null.");
                Assert.AreEqual(mapPath, AssetDatabase.GetAssetPath(entry.tex),
                    $"Tile {entry.coord} density tex must be a sub-asset of the map.");
            }
        }

        [Test]
        public void AddGrassLayerWithBlades_ZeroTiles_LayerHasEmptyDensityTiles()
        {
            var map = this.LoadMap();
            // No tiles — AddGrassLayerWithBlades must produce an empty densityTiles array (valid state).
            GrassLayer grass = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "NoTileGrass");

            Assert.IsNotNull(grass.EditorTileDensities,
                "densityTiles must not be null even with 0 tiles.");
            Assert.AreEqual(0, grass.EditorTileDensities.Length,
                "densityTiles must be empty when map has no tiles.");
        }

        [Test]
        public void AddTileAfterGrassLayer_LayerGainsThirdDensityTile()
        {
            var map = this.LoadMap();

            // Start with 2 tiles, add a grass layer.
            var c0 = new Vector2Int(0, 0);
            var c1 = new Vector2Int(1, 0);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);
            GrassLayer grass = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "ExpandGrass");

            // Add a third tile after the layer exists.
            var c2 = new Vector2Int(0, 1);
            WorldMapAssetLifecycle.AddTile(map, c2);

            // Reload from asset to get the persisted state.
            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);
            var reloaded = this.LoadMap();
            GrassLayer? reloadedGrass = reloaded.SurfaceLayers.OfType<GrassLayer>().FirstOrDefault();
            Assert.IsNotNull(reloadedGrass, "GrassLayer must survive reimport.");

            var densityTiles = reloadedGrass!.EditorTileDensities;
            Assert.AreEqual(3, densityTiles.Length,
                "After AddTile, layer must have 3 densityTiles.");
            bool hasC2 = false;
            foreach (var e in densityTiles)
                if (e.coord == c2) { hasC2 = true; break; }
            Assert.IsTrue(hasC2, "Third tile coord must appear in densityTiles.");
        }

        [Test]
        public void RemoveSurfaceLayer_Grass_LeavesNoOrphanDensityTextures()
        {
            var map = this.LoadMap();

            var c0 = new Vector2Int(0, 0);
            var c1 = new Vector2Int(1, 0);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);
            GrassLayer grass = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "RemoveGrass");

            WorldMapAssetLifecycle.RemoveSurfaceLayer(map, grass);

            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            int texCount  = allAssets.OfType<Texture2D>().Count();
            Assert.AreEqual(0, texCount,
                $"After RemoveSurfaceLayer, zero Texture2D sub-assets must remain, found {texCount}.");
        }

        [Test]
        public void RemoveTile_RemovesDensityTextureForThatCoord()
        {
            var map = this.LoadMap();

            var c0 = new Vector2Int(0, 0);
            var c1 = new Vector2Int(1, 0);
            WorldMapAssetLifecycle.AddTile(map, c0);
            WorldMapAssetLifecycle.AddTile(map, c1);
            GrassLayer grass = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "TileRemoveGrass");

            // Should have 2 density textures before removal.
            Assert.AreEqual(2, grass.EditorTileDensities.Length, "Pre-condition: 2 density tiles.");

            WorldMapAssetLifecycle.RemoveTile(map, c1);

            AssetDatabase.ImportAsset(MAP_PATH, ImportAssetOptions.ForceUpdate);
            var reloaded = this.LoadMap();
            GrassLayer? rg = reloaded.SurfaceLayers.OfType<GrassLayer>().FirstOrDefault();
            Assert.IsNotNull(rg);
            Assert.AreEqual(1, rg!.EditorTileDensities.Length,
                "After RemoveTile, layer must have 1 density tile remaining.");
            Assert.AreEqual(c0, rg.EditorTileDensities[0].coord,
                "Remaining density tile must be coord c0.");

            // No orphan Texture2D for the removed tile.
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(MAP_PATH);
            int texCount  = allAssets.OfType<Texture2D>().Count();
            Assert.AreEqual(1, texCount,
                $"Only 1 Texture2D (c0's density) must remain, found {texCount}.");
        }

        // ── G7 Unified PropLayer lifecycle ────────────────────────────────────

        [Test]
        public void AddPropLayer_LayerInSurfaceLayers_AuthoredDataIsSubAsset()
        {
            var map = this.LoadMap();

            PropLayer prop = WorldMapAssetLifecycle.AddPropLayer(map, "Tree");

            // PropLayer must appear in SurfaceLayers.
            Assert.AreEqual(1, map.SurfaceLayers.Count,
                "SurfaceLayers must have 1 entry after AddPropLayer.");
            Assert.AreSame(prop, map.SurfaceLayers[0],
                "SurfaceLayers[0] must be the newly created PropLayer.");
            Assert.AreEqual(WorldPainterLayer.LayerKind.Prop, prop.Kind,
                "PropLayer.Kind must be LayerKind.Prop.");

            // AuthoredInstancesData must be a sub-asset of the map.
            AuthoredInstancesData? authored = prop.AuthoredInstances;
            Assert.IsNotNull(authored,
                "PropLayer.AuthoredInstances must be set after AddPropLayer.");

            string mapPath = AssetDatabase.GetAssetPath(map);
            Assert.AreEqual(mapPath, AssetDatabase.GetAssetPath(authored),
                "AuthoredInstancesData must be a sub-asset of the map.");
        }

        [Test]
        public void RemoveSurfaceLayer_Prop_LeavesNoOrphanSubAssets()
        {
            var map = this.LoadMap();

            PropLayer prop = WorldMapAssetLifecycle.AddPropLayer(map, "Bush");

            // Verify sub-assets exist before removal.
            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.ImportAsset(mapPath, ImportAssetOptions.ForceUpdate);
            var beforeAssets = AssetDatabase.LoadAllAssetsAtPath(mapPath);
            int authoredBefore = System.Linq.Enumerable.Count(
                beforeAssets, a => a is AuthoredInstancesData);
            Assert.AreEqual(1, authoredBefore,
                "Pre-condition: 1 AuthoredInstancesData sub-asset must exist before removal.");

            // Remove the prop layer.
            WorldMapAssetLifecycle.RemoveSurfaceLayer(map, prop);

            // No PropLayer or AuthoredInstancesData orphans must remain.
            AssetDatabase.ImportAsset(mapPath, ImportAssetOptions.ForceUpdate);
            var afterAssets = AssetDatabase.LoadAllAssetsAtPath(mapPath);
            int propCount     = System.Linq.Enumerable.Count(afterAssets, a => a is PropLayer);
            int authoredCount = System.Linq.Enumerable.Count(afterAssets, a => a is AuthoredInstancesData);
            Assert.AreEqual(0, propCount,
                "Zero PropLayer sub-assets must remain after RemoveSurfaceLayer.");
            Assert.AreEqual(0, authoredCount,
                "Zero AuthoredInstancesData sub-assets must remain after RemoveSurfaceLayer.");
            Assert.AreEqual(0, map.SurfaceLayers.Count,
                "SurfaceLayers must be empty after removing the only layer.");
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
