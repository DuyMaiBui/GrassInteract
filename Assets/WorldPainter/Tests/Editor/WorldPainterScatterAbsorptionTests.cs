#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// P2 absorption verification: a WorldPainter with a WorldMapAsset (one tile,
    /// one density layer) rebuilds + steps + submits scatter with NO ScatterField
    /// component present.
    ///
    /// These are pure CPU/data tests — no GPU build is triggered because
    /// scatterCullCompute and scatterIndirectMat are not assigned, so the engine
    /// selection falls back to GrassCpuEngine automatically.
    /// </summary>
    [TestFixture]
    public class WorldPainterScatterAbsorptionTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeValidTile(Vector2Int coord)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord  = coord;
            tile.heightRes  = 5;
            tile.splatRes   = 5;
            tile.heightData = new byte[tile.ExpectedHeightBytes];
            tile.splatData  = new byte[tile.ExpectedSplatBytes];
            return tile;
        }

        private static WorldMapAsset MakeMapWithOneTileAndOneDensityLayer(
            out TerrainTileAsset tile,
            out DensityScatterLayer layer)
        {
            var mapAsset = ScriptableObject.CreateInstance<WorldMapAsset>();

            tile = MakeValidTile(Vector2Int.zero);
            mapAsset.RegisterTile(Vector2Int.zero, tile);

            layer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            layer.name = "TestDensityLayer";
            mapAsset.RegisterLayer(layer);

            return mapAsset;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void RebuildScatter_WithMapAndOneTile_DoesNotThrow()
        {
            // Arrange — WorldPainter with map assigned; NO ScatterField on the GameObject.
            var go = new GameObject("TestWorldPainter_RebuildScatter");
            var painter = go.AddComponent<WorldPainter>();

            var mapAsset = MakeMapWithOneTileAndOneDensityLayer(
                out TerrainTileAsset tile,
                out DensityScatterLayer densityLayer);
            painter.Map = mapAsset;

            // Assert — ScatterField is NOT present.
            Assert.IsNull(go.GetComponentInChildren<ScatterField>(),
                "No ScatterField component must be present (absorption test).");

            // DensityMap is null → engine skips the layer and logs an error; we expect that.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"\[WorldPainter\.Scatter\] Layer \[0\] '.*' invalid: DensityMap is not assigned\."));

            // Act — must not throw.
            Assert.DoesNotThrow(() => painter.RebuildScatter(),
                "RebuildScatter must not throw when map is assigned and ScatterField is absent.");

            // Cleanup.
            Object.DestroyImmediate(densityLayer);
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(mapAsset);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void StepScatter_AfterRebuild_DoesNotThrow()
        {
            var go = new GameObject("TestWorldPainter_StepScatter");
            var painter = go.AddComponent<WorldPainter>();

            var mapAsset = MakeMapWithOneTileAndOneDensityLayer(
                out TerrainTileAsset tile,
                out DensityScatterLayer densityLayer);
            painter.Map = mapAsset;

            // DensityMap is null → expected validation error on Rebuild.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"\[WorldPainter\.Scatter\] Layer \[0\] '.*' invalid: DensityMap is not assigned\."));

            painter.RebuildScatter();

            Assert.DoesNotThrow(() => painter.StepScatter(0.016f),
                "StepScatter must not throw after RebuildScatter.");

            // Cleanup.
            Object.DestroyImmediate(densityLayer);
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(mapAsset);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void SubmitScatter_AfterRebuild_DoesNotThrow()
        {
            var go = new GameObject("TestWorldPainter_SubmitScatter");
            var painter = go.AddComponent<WorldPainter>();

            var mapAsset = MakeMapWithOneTileAndOneDensityLayer(
                out TerrainTileAsset tile,
                out DensityScatterLayer densityLayer);
            painter.Map = mapAsset;

            // DensityMap is null → expected validation error on Rebuild.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    @"\[WorldPainter\.Scatter\] Layer \[0\] '.*' invalid: DensityMap is not assigned\."));

            painter.RebuildScatter();

            Assert.DoesNotThrow(() => painter.SubmitScatter(null),
                "SubmitScatter must not throw after RebuildScatter.");

            // Cleanup.
            Object.DestroyImmediate(densityLayer);
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(mapAsset);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RebuildScatter_WithNullMap_IsNoop()
        {
            var go = new GameObject("TestWorldPainter_NullMap");
            var painter = go.AddComponent<WorldPainter>();

            // Map not assigned — must be a silent no-op with no exceptions.
            Assert.IsNull(painter.Map, "Map must be null initially.");
            Assert.DoesNotThrow(() => painter.RebuildScatter(),
                "RebuildScatter with null map must be a no-op.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MapProperty_AssignAndRead_RoundTrips()
        {
            var go = new GameObject("TestWorldPainter_MapProp");
            var painter = go.AddComponent<WorldPainter>();

            var mapAsset = ScriptableObject.CreateInstance<WorldMapAsset>();
            painter.Map = mapAsset;

            Assert.AreSame(mapAsset, painter.Map,
                "Map property must round-trip the assigned WorldMapAsset reference.");

            Object.DestroyImmediate(mapAsset);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MapLayers_AreReadThrough_FromContainer()
        {
            var go = new GameObject("TestWorldPainter_LayersReadThrough");
            var painter = go.AddComponent<WorldPainter>();

            var mapAsset = MakeMapWithOneTileAndOneDensityLayer(
                out TerrainTileAsset tile,
                out DensityScatterLayer densityLayer);
            painter.Map = mapAsset;

            // The map reports 1 layer.
            Assert.AreEqual(1, painter.Map!.Layers.Count,
                "Map must expose the registered density layer.");
            Assert.AreSame(densityLayer, painter.Map.Layers[0],
                "Map layer must be the exact registered DensityScatterLayer instance.");

            Object.DestroyImmediate(densityLayer);
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(mapAsset);
            Object.DestroyImmediate(go);
        }
    }
}
