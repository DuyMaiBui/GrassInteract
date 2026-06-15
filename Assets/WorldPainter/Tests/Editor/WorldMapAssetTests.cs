#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor; // WorldMapAssetLifecycle

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldMapAsset"/> lookup API.
    /// Verifies: 3 tiles (incl. negative coord), GetTile, enumerate, HasOpenNeighbor.
    /// No disk I/O â€” uses ScriptableObject.CreateInstance and the internal RegisterTile API.
    /// </summary>
    [TestFixture]
    public class WorldMapAssetTests
    {
        private WorldMapAsset map = null!;

        [SetUp]
        public void SetUp()
        {
            this.map = ScriptableObject.CreateInstance<WorldMapAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.map);
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private TerrainTileAsset MakeTile(Vector2Int coord)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = coord;
            tile.name      = $"Tile_{coord.x}_{coord.y}";
            return tile;
        }

        private void RegisterThreeTiles(out TerrainTileAsset t00, out TerrainTileAsset t10, out TerrainTileAsset tn10)
        {
            t00  = this.MakeTile(new Vector2Int(0,  0));
            t10  = this.MakeTile(new Vector2Int(1,  0));
            tn10 = this.MakeTile(new Vector2Int(-1, 0));

            this.map.RegisterTile(new Vector2Int(0,  0), t00);
            this.map.RegisterTile(new Vector2Int(1,  0), t10);
            this.map.RegisterTile(new Vector2Int(-1, 0), tn10);
        }

        // â”€â”€ GetTile tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void GetTile_ReturnsNull_WhenEmpty()
        {
            Assert.IsNull(this.map.GetTile(Vector2Int.zero));
        }

        [Test]
        public void GetTile_ReturnsCorrectTile_ForPositiveCoords()
        {
            this.RegisterThreeTiles(out var t00, out var t10, out _);

            Assert.AreSame(t00,  this.map.GetTile(new Vector2Int(0, 0)));
            Assert.AreSame(t10,  this.map.GetTile(new Vector2Int(1, 0)));
        }

        [Test]
        public void GetTile_ReturnsCorrectTile_ForNegativeCoord()
        {
            this.RegisterThreeTiles(out _, out _, out var tn10);

            Assert.AreSame(tn10, this.map.GetTile(new Vector2Int(-1, 0)));
        }

        [Test]
        public void GetTile_ReturnsNull_ForMissingCoord()
        {
            this.RegisterThreeTiles(out _, out _, out _);

            Assert.IsNull(this.map.GetTile(new Vector2Int(99, 99)));
        }

        // â”€â”€ TileCount / enumerate tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void TileCount_IsThree_AfterAddingThreeTiles()
        {
            this.RegisterThreeTiles(out _, out _, out _);

            Assert.AreEqual(3, this.map.TileCount);
        }

        [Test]
        public void EnumerateTileCoords_ContainsAllThreeCoords()
        {
            this.RegisterThreeTiles(out _, out _, out _);

            var coords = this.map.EnumerateTileCoords().ToList();
            Assert.AreEqual(3, coords.Count);
            Assert.IsTrue(coords.Contains(new Vector2Int(0,  0)));
            Assert.IsTrue(coords.Contains(new Vector2Int(1,  0)));
            Assert.IsTrue(coords.Contains(new Vector2Int(-1, 0)));
        }

        [Test]
        public void EnumerateTiles_ContainsAllThreeTileAssets()
        {
            this.RegisterThreeTiles(out var t00, out var t10, out var tn10);

            var tiles = this.map.EnumerateTiles().ToList();
            Assert.AreEqual(3, tiles.Count);
            Assert.IsTrue(tiles.Contains(t00));
            Assert.IsTrue(tiles.Contains(t10));
            Assert.IsTrue(tiles.Contains(tn10));
        }

        // â”€â”€ HasOpenNeighbor tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void HasOpenNeighbor_ReturnsFalse_ForUnknownCoord()
        {
            Assert.IsFalse(this.map.HasOpenNeighbor(new Vector2Int(5, 5), out _));
        }

        [Test]
        public void HasOpenNeighbor_ReturnsTrueWithOpenEdges_ForIsolatedTile()
        {
            var tile = this.MakeTile(Vector2Int.zero);
            this.map.RegisterTile(Vector2Int.zero, tile);

            bool hasOpen = this.map.HasOpenNeighbor(Vector2Int.zero, out var openEdges);

            Assert.IsTrue(hasOpen);
            // An isolated tile has all 4 neighbors open.
            Assert.AreEqual(4, openEdges.Length);
        }

        [Test]
        public void HasOpenNeighbor_ExcludesOccupiedNeighbor()
        {
            var t00 = this.MakeTile(new Vector2Int(0, 0));
            var t10 = this.MakeTile(new Vector2Int(1, 0));
            this.map.RegisterTile(new Vector2Int(0, 0), t00);
            this.map.RegisterTile(new Vector2Int(1, 0), t10);

            this.map.HasOpenNeighbor(new Vector2Int(0, 0), out var openEdges);

            // East (1,0) is occupied â€” should NOT appear in open edges.
            Assert.IsFalse(openEdges.Contains(new Vector2Int(1, 0)));
            // West (-1,0), North (0,1), South (0,-1) should appear.
            Assert.AreEqual(3, openEdges.Length);
        }

        // â”€â”€ Layers API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void SurfaceLayers_IsEmpty_ByDefault()
        {
            Assert.AreEqual(0, this.map.SurfaceLayers.Count);
        }

        [Test]
        public void RegisterSurfaceLayer_AppendsToSurfaceLayers()
        {
            // Use GrassLayer (unified replacement for the removed DensityScatterLayer).
            var layer = ScriptableObject.CreateInstance<GrassLayer>();
            layer.name = "Grass_Meadow";

            this.map.RegisterSurfaceLayer(layer);

            Assert.AreEqual(1, this.map.SurfaceLayers.Count);
            Assert.AreSame(layer, this.map.SurfaceLayers[0]);

            Object.DestroyImmediate(layer);
        }

        [Test]
        public void UnregisterSurfaceLayer_RemovesFromSurfaceLayers()
        {
            // Use GrassLayer (unified replacement for the removed DensityScatterLayer).
            var layer = ScriptableObject.CreateInstance<GrassLayer>();
            layer.name = "Grass_Meadow";
            this.map.RegisterSurfaceLayer(layer);

            this.map.UnregisterSurfaceLayer(layer);

            Assert.AreEqual(0, this.map.SurfaceLayers.Count);

            Object.DestroyImmediate(layer);
        }

        // â”€â”€ Unregister tile â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void UnregisterTile_ReducesCount_AndNullsGetTile()
        {
            this.RegisterThreeTiles(out _, out _, out _);

            this.map.UnregisterTile(new Vector2Int(1, 0));

            Assert.AreEqual(2, this.map.TileCount);
            Assert.IsNull(this.map.GetTile(new Vector2Int(1, 0)));
        }

        // â”€â”€ WorldGrid constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void WorldMapGrid_HasExpectedConstants()
        {
            Assert.AreEqual(256f, WorldMapGrid.TILE_SIZE_M);
            Assert.AreEqual(257,  WorldMapGrid.HEIGHT_RES);
            Assert.AreEqual(512,  WorldMapGrid.SPLAT_RES);
            Assert.AreEqual(256,  WorldMapGrid.DENSITY_RES);
        }

        // â”€â”€ TileSubAssetName sign-safe naming â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Test]
        public void TileSubAssetName_NegativeCoords_UseNPrefix()
        {
            // Delegate to lifecycle via the public static method.
            string name = WorldMapAssetLifecycle.TileSubAssetName(new Vector2Int(-1, 0));
            Assert.AreEqual("Tile_n1_0", name);
        }

        [Test]
        public void TileSubAssetName_PositiveCoords_NoPrefix()
        {
            string name = WorldMapAssetLifecycle.TileSubAssetName(new Vector2Int(3, 7));
            Assert.AreEqual("Tile_3_7", name);
        }
    }
}
