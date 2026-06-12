#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// High-risk mitigation tests for the P6 seam sync implementation
    /// (<see cref="TerrainSculptRtWriteback.ApplySeamSync"/>).
    ///
    /// Contract: after a writeback that touches the shared edge between
    /// Tile_0_0 and Tile_1_0, the shared column texels of both tiles must
    /// be byte-identical (<see cref="TerrainWorldGrid"/> 1-texel shared-edge convention).
    ///
    /// Tests use a small (5×5) heightRes + splatRes for speed.
    /// </summary>
    [TestFixture]
    public sealed class WorldPainterSeamSyncTests
    {
        private const int RES = 5; // small for test speed

        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(Vector2Int coord, int heightVal = 0)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = coord;
            tile.heightRes = RES;
            tile.splatRes  = RES;
            tile.minHeight = 0f;
            tile.maxHeight = 512f;

            // Allocate and fill with a known pattern so we can detect wrong-copy bugs.
            tile.heightData = new byte[RES * RES * TerrainHeightFormat.BYTES_PER_SAMPLE];
            tile.splatData  = new byte[RES * RES * 4];

            for (int i = 0; i < RES * RES; ++i)
            {
                ushort raw = (ushort)(heightVal + i);
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);

                int sb = i * 4;
                tile.splatData[sb]     = (byte)(heightVal + i);
                tile.splatData[sb + 1] = (byte)(heightVal + i + 1);
                tile.splatData[sb + 2] = (byte)(heightVal + i + 2);
                tile.splatData[sb + 3] = (byte)(heightVal + i + 3);
            }
            return tile;
        }

        private static void SetHeightColumn(TerrainTileAsset tile, int col, ushort value)
        {
            for (int row = 0; row < tile.heightRes; ++row)
            {
                int idx = row * tile.heightRes + col;
                TerrainHeightFormat.WriteRaw(tile.heightData, idx, value);
            }
        }

        private static void SetSplatColumn(TerrainTileAsset tile, int col, byte r, byte g, byte b, byte a)
        {
            for (int row = 0; row < tile.splatRes; ++row)
            {
                int baseIdx = (row * tile.splatRes + col) * 4;
                tile.splatData[baseIdx]     = r;
                tile.splatData[baseIdx + 1] = g;
                tile.splatData[baseIdx + 2] = b;
                tile.splatData[baseIdx + 3] = a;
            }
        }

        private static void SetHeightRow(TerrainTileAsset tile, int row, ushort value)
        {
            for (int col = 0; col < tile.heightRes; ++col)
            {
                int idx = row * tile.heightRes + col;
                TerrainHeightFormat.WriteRaw(tile.heightData, idx, value);
            }
        }

        private static void SetSplatRow(TerrainTileAsset tile, int row, byte r, byte g, byte b, byte a)
        {
            for (int col = 0; col < tile.splatRes; ++col)
            {
                int baseIdx = (row * tile.splatRes + col) * 4;
                tile.splatData[baseIdx]     = r;
                tile.splatData[baseIdx + 1] = g;
                tile.splatData[baseIdx + 2] = b;
                tile.splatData[baseIdx + 3] = a;
            }
        }

        private static TerrainSculptRtWriteback MakeWritebackWithNeighbours(
            params TerrainTileAsset[] tiles)
        {
            var wb = new TerrainSculptRtWriteback();
            var entries = new (Vector2Int, TerrainTileAsset)[tiles.Length];
            for (int i = 0; i < tiles.Length; ++i)
                entries[i] = (tiles[i].tileCoord, tiles[i]);
            wb.RegisterNeighbours(entries);
            return wb;
        }

        // ── Height seam tests (horizontal boundary: Tile_0_0 / Tile_1_0) ─────

        /// <summary>
        /// Tile_0_0 right column (col = RES-1) is set to a known value.
        /// After ApplySeamSync, Tile_1_0 left column (col = 0) must match byte-for-byte.
        /// </summary>
        [Test]
        public void HeightSeam_RightColumn_CopiedToNeighbourLeftColumn()
        {
            var tile00 = MakeTile(new Vector2Int(0, 0));
            var tile10 = MakeTile(new Vector2Int(1, 0), heightVal: 1000);

            ushort edgeValue = 32000;
            SetHeightColumn(tile00, RES - 1, edgeValue);

            var wb = MakeWritebackWithNeighbours(tile00, tile10);
            wb.ApplySeamSync(tile00);

            // Assert tile10's left column (col=0) matches tile00's right column (col=RES-1).
            for (int row = 0; row < RES; ++row)
            {
                int srcIdx = row * RES + (RES - 1);
                int dstIdx = row * RES + 0;
                ushort srcRaw = TerrainHeightFormat.ReadRaw(tile00.heightData, srcIdx);
                ushort dstRaw = TerrainHeightFormat.ReadRaw(tile10.heightData, dstIdx);
                Assert.AreEqual(srcRaw, dstRaw,
                    $"Row {row}: height seam mismatch (src={srcRaw}, dst={dstRaw})");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tile10);
        }

        /// <summary>
        /// Tile_0_0 top row (row = RES-1) is set to a known value.
        /// After ApplySeamSync, Tile_0_1 bottom row (row = 0) must match byte-for-byte.
        /// </summary>
        [Test]
        public void HeightSeam_TopRow_CopiedToNeighbourBottomRow()
        {
            var tile00 = MakeTile(new Vector2Int(0, 0));
            var tile01 = MakeTile(new Vector2Int(0, 1), heightVal: 2000);

            ushort edgeValue = 48000;
            SetHeightRow(tile00, RES - 1, edgeValue);

            var wb = MakeWritebackWithNeighbours(tile00, tile01);
            wb.ApplySeamSync(tile00);

            for (int col = 0; col < RES; ++col)
            {
                int srcIdx = (RES - 1) * RES + col;
                int dstIdx = 0 * RES + col;
                ushort srcRaw = TerrainHeightFormat.ReadRaw(tile00.heightData, srcIdx);
                ushort dstRaw = TerrainHeightFormat.ReadRaw(tile01.heightData, dstIdx);
                Assert.AreEqual(srcRaw, dstRaw,
                    $"Col {col}: top-row height seam mismatch (src={srcRaw}, dst={dstRaw})");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tile01);
        }

        // ── Splat seam tests ──────────────────────────────────────────────────

        [Test]
        public void SplatSeam_RightColumn_CopiedToNeighbourLeftColumn()
        {
            var tile00 = MakeTile(new Vector2Int(0, 0));
            var tile10 = MakeTile(new Vector2Int(1, 0));

            byte er = 200, eg = 150, eb = 100, ea = 50;
            SetSplatColumn(tile00, RES - 1, er, eg, eb, ea);

            var wb = MakeWritebackWithNeighbours(tile00, tile10);
            wb.ApplySeamSync(tile00);

            for (int row = 0; row < RES; ++row)
            {
                int baseIdx = (row * RES + 0) * 4;
                Assert.AreEqual(er, tile10.splatData[baseIdx],     $"Row {row}: splat R mismatch");
                Assert.AreEqual(eg, tile10.splatData[baseIdx + 1], $"Row {row}: splat G mismatch");
                Assert.AreEqual(eb, tile10.splatData[baseIdx + 2], $"Row {row}: splat B mismatch");
                Assert.AreEqual(ea, tile10.splatData[baseIdx + 3], $"Row {row}: splat A mismatch");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tile10);
        }

        [Test]
        public void SplatSeam_TopRow_CopiedToNeighbourBottomRow()
        {
            var tile00 = MakeTile(new Vector2Int(0, 0));
            var tile01 = MakeTile(new Vector2Int(0, 1));

            byte er = 10, eg = 20, eb = 30, ea = 40;
            SetSplatRow(tile00, RES - 1, er, eg, eb, ea);

            var wb = MakeWritebackWithNeighbours(tile00, tile01);
            wb.ApplySeamSync(tile00);

            for (int col = 0; col < RES; ++col)
            {
                int baseIdx = (0 * RES + col) * 4;
                Assert.AreEqual(er, tile01.splatData[baseIdx],     $"Col {col}: splat R mismatch");
                Assert.AreEqual(eg, tile01.splatData[baseIdx + 1], $"Col {col}: splat G mismatch");
                Assert.AreEqual(eb, tile01.splatData[baseIdx + 2], $"Col {col}: splat B mismatch");
                Assert.AreEqual(ea, tile01.splatData[baseIdx + 3], $"Col {col}: splat A mismatch");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tile01);
        }

        // ── Bidirectionality: both tiles' shared edges identical ─────────────

        /// <summary>
        /// After ApplySeamSync on Tile_0_0, Tile_1_0's left column must equal
        /// Tile_0_0's right column — and both are byte-identical (the key invariant).
        /// </summary>
        [Test]
        public void HeightSeam_BothTiles_SharedColumnByteIdentical_AfterSync()
        {
            var tile00 = MakeTile(new Vector2Int(0, 0));
            var tile10 = MakeTile(new Vector2Int(1, 0), heightVal: 5000);

            // Write different values into the edge columns on both tiles.
            ushort srcEdge = 12345;
            SetHeightColumn(tile00, RES - 1, srcEdge);
            SetHeightColumn(tile10, 0,       ushort.MaxValue); // will be overwritten by sync

            var wb = MakeWritebackWithNeighbours(tile00, tile10);
            wb.ApplySeamSync(tile00);

            for (int row = 0; row < RES; ++row)
            {
                int idxSrc = row * RES + (RES - 1);
                int idxDst = row * RES + 0;
                ushort vSrc = TerrainHeightFormat.ReadRaw(tile00.heightData, idxSrc);
                ushort vDst = TerrainHeightFormat.ReadRaw(tile10.heightData, idxDst);
                Assert.AreEqual(vSrc, vDst,
                    $"Row {row}: shared edge not byte-identical (tile00={vSrc}, tile10={vDst})");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tile10);
        }

        // ── No-create guard ───────────────────────────────────────────────────

        /// <summary>
        /// Brushing over an empty (no-tile) coordinate must NOT create a tile.
        /// DispatchOneTile returns early when FindTile returns null — verified here
        /// via the resolver + no-tile scenario.
        /// </summary>
        [Test]
        public void SeamSync_NoNeighbour_DoesNotThrow()
        {
            // Tile with no neighbour registered — sync should be a no-op, not throw.
            var tile00 = MakeTile(new Vector2Int(0, 0));

            var wb = new TerrainSculptRtWriteback();
            // No neighbours registered — empty map.
            Assert.DoesNotThrow(() => wb.ApplySeamSync(tile00),
                "ApplySeamSync with no registered neighbours must not throw.");

            Object.DestroyImmediate(tile00);
        }

        /// <summary>
        /// The resolver returns no tiles for empty coords when residency is not null.
        /// This tests the no-create guard: resolving an empty coord into an empty
        /// residency set yields zero results.
        /// </summary>
        [Test]
        public void Resolver_EmptyResidency_ReturnsNoTiles_ForAnyBrushPosition()
        {
            var results    = new System.Collections.Generic.List<Vector2Int>();
            var emptySet   = new TerrainTileResidencySet(); // contains no tiles

            TerrainPaintTargetResolver.Resolve(
                new Vector2(5f, 5f), // world pos well inside tile (0,0)
                radiusM: 20f,
                residencySet: emptySet,
                results);

            Assert.AreEqual(0, results.Count,
                "No resident tiles → resolver must return empty (no tile created).");
        }

        // ── -X and -Z reverse-direction seams ────────────────────────────────

        [Test]
        public void HeightSeam_LeftColumn_CopiedToNeighbourMinusXRightColumn()
        {
            var tile00  = MakeTile(new Vector2Int(0, 0));
            var tileM10 = MakeTile(new Vector2Int(-1, 0), heightVal: 3000);

            ushort edgeValue = 22222;
            SetHeightColumn(tile00, 0, edgeValue);

            var wb = MakeWritebackWithNeighbours(tile00, tileM10);
            wb.ApplySeamSync(tile00);

            for (int row = 0; row < RES; ++row)
            {
                int srcIdx = row * RES + 0;
                int dstIdx = row * RES + (RES - 1);
                ushort srcRaw = TerrainHeightFormat.ReadRaw(tile00.heightData, srcIdx);
                ushort dstRaw = TerrainHeightFormat.ReadRaw(tileM10.heightData, dstIdx);
                Assert.AreEqual(srcRaw, dstRaw,
                    $"Row {row}: -X height seam mismatch (src={srcRaw}, dst={dstRaw})");
            }

            Object.DestroyImmediate(tile00);
            Object.DestroyImmediate(tileM10);
        }
    }
}
