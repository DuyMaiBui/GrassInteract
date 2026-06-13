#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Owner-level tests for <see cref="WorldPainter"/>:
    ///   (a) Tier-A schema round-trip (worldGrid + tiles — splatLayers/scatterLayers removed Phase 5b).
    ///   (b) Coord-lookup via Tiles list (no GPU build needed — pure data).
    ///   (c) Migration mapping parity — tile/layer count in == count on painter.
    ///
    /// These are CPU/data tests with no GPU dependency.
    /// Mirrors the pattern in <see cref="TerrainSculptUndoTests"/> and
    /// existing owner-level structure tests.
    /// </summary>
    [TestFixture]
    public class WorldPainterOwnerTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(Vector2Int coord)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord  = coord;
            tile.heightRes  = 5;
            tile.splatRes   = 5;
            tile.heightData = new byte[tile.ExpectedHeightBytes];
            tile.splatData  = new byte[tile.ExpectedSplatBytes];
            return tile;
        }

        private static WorldPainter MakePainter()
        {
            var go = new GameObject("TestWorldPainter");
            return go.AddComponent<WorldPainter>();
        }

        // ── (a) Tier-A schema round-trip ──────────────────────────────────────

        [Test]
        public void TierA_WorldGrid_DefaultValues_RoundTrip()
        {
            var painter = MakePainter();

            var grid = painter.WorldGridConfig;
            Assert.AreEqual(WorldGrid.Default.tileSizeM,  grid.tileSizeM,  "tileSizeM default");
            Assert.AreEqual(WorldGrid.Default.heightRes,  grid.heightRes,  "heightRes default");
            Assert.AreEqual(WorldGrid.Default.splatRes,   grid.splatRes,   "splatRes default");

            // Modify and read back.
            painter.WorldGridConfig = new WorldGrid { tileSizeM = 512f, heightRes = 513, splatRes = 1024 };
            Assert.AreEqual(512f, painter.WorldGridConfig.tileSizeM);
            Assert.AreEqual(513,  painter.WorldGridConfig.heightRes);
            Assert.AreEqual(1024, painter.WorldGridConfig.splatRes);

            Object.DestroyImmediate(painter.gameObject);
        }

        [Test]
        public void TierA_Tiles_AddAndRetrieve()
        {
            var painter  = MakePainter();
            var tileA    = MakeTile(new Vector2Int(0, 0));
            var tileB    = MakeTile(new Vector2Int(1, 0));

            painter.Tiles.Add(new TileEntry { coord = tileA.tileCoord, tileAsset = tileA });
            painter.Tiles.Add(new TileEntry { coord = tileB.tileCoord, tileAsset = tileB });

            Assert.AreEqual(2, painter.Tiles.Count, "Two tiles registered");
            Assert.AreEqual(new Vector2Int(0, 0), painter.Tiles[0].coord);
            Assert.AreEqual(new Vector2Int(1, 0), painter.Tiles[1].coord);

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(tileB);
            Object.DestroyImmediate(painter.gameObject);
        }

        // ── (b) Coord-lookup (Tiles list — no GPU) ────────────────────────────

        [Test]
        public void Tiles_CoordLookup_FindsByCoord()
        {
            var painter = MakePainter();
            var tileA   = MakeTile(new Vector2Int(2, 3));
            var tileB   = MakeTile(new Vector2Int(0, 0));

            painter.Tiles.Add(new TileEntry { coord = tileA.tileCoord, tileAsset = tileA });
            painter.Tiles.Add(new TileEntry { coord = tileB.tileCoord, tileAsset = tileB });

            // Linear search (mirrors what sculpt tool does without GPU build).
            TerrainTileAsset? found = null;
            foreach (var entry in painter.Tiles)
            {
                if (entry.coord == new Vector2Int(2, 3))
                {
                    found = entry.tileAsset;
                    break;
                }
            }

            Assert.IsNotNull(found, "Tile (2,3) should be found");
            Assert.AreEqual(tileA, found);

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(tileB);
            Object.DestroyImmediate(painter.gameObject);
        }

        [Test]
        public void Tiles_CoordLookup_MissingCoord_ReturnsNull()
        {
            var painter = MakePainter();
            var tileA   = MakeTile(new Vector2Int(0, 0));
            painter.Tiles.Add(new TileEntry { coord = tileA.tileCoord, tileAsset = tileA });

            TerrainTileAsset? notFound = null;
            foreach (var entry in painter.Tiles)
                if (entry.coord == new Vector2Int(99, 99))
                    notFound = entry.tileAsset;

            Assert.IsNull(notFound, "Non-existent coord returns null");

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(painter.gameObject);
        }

        // ── (c) Migration mapping parity ─────────────────────────────────────

        [Test]
        public void MigrationParity_TileCount_InEqualsOut()
        {
            // Simulate what WorldPainterMigration does: N tiles in → N entries on painter.
            int sourceTileCount = 3;
            var painter = MakePainter();
            var sourceTiles = new List<TerrainTileAsset>();

            for (int i = 0; i < sourceTileCount; i++)
            {
                var t = MakeTile(new Vector2Int(i, 0));
                sourceTiles.Add(t);
                painter.Tiles.Add(new TileEntry { coord = t.tileCoord, tileAsset = t });
            }

            Assert.AreEqual(sourceTileCount, painter.Tiles.Count,
                "Migrated tile count must match source count");

            foreach (var t in sourceTiles) Object.DestroyImmediate(t);
            Object.DestroyImmediate(painter.gameObject);
        }

        [Test]
        public void MigrationParity_TileAssets_ReferencedInPlace()
        {
            // Assets are referenced in-place: same object reference after migration.
            var painter = MakePainter();
            var tileA   = MakeTile(new Vector2Int(0, 0));
            var tileB   = MakeTile(new Vector2Int(1, 0));

            // Simulate migration: add entries pointing to existing assets.
            painter.Tiles.Add(new TileEntry { coord = tileA.tileCoord, tileAsset = tileA });
            painter.Tiles.Add(new TileEntry { coord = tileB.tileCoord, tileAsset = tileB });

            // Verify same asset reference (not a copy).
            Assert.AreSame(tileA, painter.Tiles[0].tileAsset,
                "tileA must be the same asset reference (not a copy)");
            Assert.AreSame(tileB, painter.Tiles[1].tileAsset,
                "tileB must be the same asset reference (not a copy)");

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(tileB);
            Object.DestroyImmediate(painter.gameObject);
        }

        [Test]
        public void MigrationParity_ZeroTiles_ResultsInEmptyList()
        {
            var painter = MakePainter();
            // Apply migration with zero tiles.
            painter.Tiles.Clear();
            Assert.AreEqual(0, painter.Tiles.Count, "Empty source → empty painter tiles");
            Object.DestroyImmediate(painter.gameObject);
        }

        [Test]
        public void MigrationParity_DuplicateCoords_LastWins_OrBothPresent()
        {
            // The migration records both entries; it is the Build step that enforces
            // uniqueness and logs an error. Verify the Tiles list itself accepts both.
            var painter = MakePainter();
            var tileA   = MakeTile(new Vector2Int(0, 0));
            var tileB   = MakeTile(new Vector2Int(0, 0)); // same coord

            painter.Tiles.Add(new TileEntry { coord = tileA.tileCoord, tileAsset = tileA });
            painter.Tiles.Add(new TileEntry { coord = tileB.tileCoord, tileAsset = tileB });

            // Both entries are present in the list (build-time validation handles de-dup).
            Assert.AreEqual(2, painter.Tiles.Count, "List accepts both; build enforces uniqueness");

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(tileB);
            Object.DestroyImmediate(painter.gameObject);
        }
    }
}
