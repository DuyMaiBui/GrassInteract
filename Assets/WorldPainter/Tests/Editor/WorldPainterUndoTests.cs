#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainterUndo"/>: depth cap, memory cap,
    /// push/pop round-trip, snapshot immutability, multi-tile isolation.
    ///
    /// Mirrors <see cref="TerrainSculptUndoTests"/> to verify the retargeted
    /// WorldPainter-specific undo with its tighter depth (10) and 128 MB memory cap.
    /// </summary>
    [TestFixture]
    public class WorldPainterUndoTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(Vector2Int coord, int heightRes = 5, int splatRes = 5)
        {
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord  = coord;
            tile.heightRes  = heightRes;
            tile.splatRes   = splatRes;
            tile.heightData = new byte[tile.ExpectedHeightBytes];
            tile.splatData  = new byte[tile.ExpectedSplatBytes];
            return tile;
        }

        private static void FillHeight(TerrainTileAsset tile, byte value)
        {
            for (int i = 0; i < tile.heightData.Length; ++i)
                tile.heightData[i] = value;
        }

        // ── Basic push/pop ────────────────────────────────────────────────────

        [Test]
        public void Push_ThenPop_RestoresBytes()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            FillHeight(tile, 0xAA);
            undo.Push(tile);

            FillHeight(tile, 0xBB);

            var snap = undo.Pop(tile);
            Assert.IsNotNull(snap);
            foreach (byte b in tile.heightData)
                Assert.AreEqual(0xAA, b, "Pop should restore the pushed bytes");

            Object.DestroyImmediate(tile);
        }

        [Test]
        public void Pop_OnEmptyStack_ReturnsNull()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            Assert.IsNull(undo.Pop(tile));
            Object.DestroyImmediate(tile);
        }

        [Test]
        public void CanUndo_AfterPush_ReturnsTrue()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(1, 2));
            undo.Push(tile);
            Assert.IsTrue(undo.CanUndo(tile.tileCoord));
            Object.DestroyImmediate(tile);
        }

        [Test]
        public void CanUndo_WhenEmpty_ReturnsFalse()
        {
            var undo = new WorldPainterUndo();
            Assert.IsFalse(undo.CanUndo(new Vector2Int(0, 0)));
        }

        // ── Depth cap (10 per tile) ───────────────────────────────────────────

        [Test]
        public void DepthCap_EvictsOldest_WhenExceeded()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));

            for (int i = 0; i <= WorldPainterUndo.DEPTH_CAP; ++i)
            {
                FillHeight(tile, (byte)(i % 256));
                undo.Push(tile);
            }

            Assert.AreEqual(WorldPainterUndo.DEPTH_CAP, undo.DepthForTile(tile.tileCoord),
                "Depth must be capped at DEPTH_CAP");

            Object.DestroyImmediate(tile);
        }

        [Test]
        public void DepthCap_DefaultIs10()
        {
            Assert.AreEqual(10, WorldPainterUndo.DEPTH_CAP,
                "Design §7.4 requires depth 10");
        }

        // ── Memory cap (128 MB) ───────────────────────────────────────────────

        [Test]
        public void MemoryCap_Is128MB()
        {
            Assert.AreEqual(128L * 1024L * 1024L, WorldPainterUndo.MEMORY_CAP_BYTES,
                "Design requires 128 MB memory cap");
        }

        [Test]
        public void MemoryCap_EvictsWhenExceeded()
        {
            // Create tiles with large data so a few pushes exceed 128 MB.
            // 257*257*2 (R16 height) + 512*512*4 (RGBA32 splat) ≈ 1.18 MB per tile.
            // Push ~200 snapshots on one tile → well over 128 MB.
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0), heightRes: 257, splatRes: 512);

            // Resize to realistic sizes.
            tile.heightData = new byte[tile.ExpectedHeightBytes];
            tile.splatData  = new byte[tile.ExpectedSplatBytes];

            // Push enough snapshots to exceed the 128 MB cap.
            long singleSnapshotBytes = tile.heightData.LongLength + tile.splatData.LongLength;
            int  pushesToExceedCap   = (int)(WorldPainterUndo.MEMORY_CAP_BYTES / singleSnapshotBytes) + 2;

            for (int i = 0; i < pushesToExceedCap; ++i)
            {
                FillHeight(tile, (byte)(i % 256));
                undo.Push(tile);
            }

            Assert.LessOrEqual(undo.TotalMemoryBytes, WorldPainterUndo.MEMORY_CAP_BYTES,
                "Total memory must never exceed 128 MB cap after eviction");

            Object.DestroyImmediate(tile);
        }

        // ── Snapshot immutability ─────────────────────────────────────────────

        [Test]
        public void Snapshot_IsImmutable_AfterModify()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            FillHeight(tile, 0x11);
            undo.Push(tile);

            FillHeight(tile, 0xFF);
            undo.Pop(tile);
            Assert.AreEqual(0x11, tile.heightData[0], "Snapshot must be a deep copy");

            Object.DestroyImmediate(tile);
        }

        // ── Multi-tile isolation ──────────────────────────────────────────────

        [Test]
        public void MultiTile_UndoStacks_AreIndependent()
        {
            var undo  = new WorldPainterUndo();
            var tileA = MakeTile(new Vector2Int(0, 0));
            var tileB = MakeTile(new Vector2Int(1, 0));

            FillHeight(tileA, 0xAA);
            FillHeight(tileB, 0xBB);
            undo.Push(tileA);
            undo.Push(tileB);

            FillHeight(tileA, 0x00);
            FillHeight(tileB, 0x00);

            undo.Pop(tileA);
            undo.Pop(tileB);

            Assert.AreEqual(0xAA, tileA.heightData[0], "Tile A restored");
            Assert.AreEqual(0xBB, tileB.heightData[0], "Tile B restored");

            Object.DestroyImmediate(tileA);
            Object.DestroyImmediate(tileB);
        }

        // ── TotalSnapshotCount ────────────────────────────────────────────────

        [Test]
        public void TotalSnapshotCount_ReflectsPushPop()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));

            Assert.AreEqual(0, undo.TotalSnapshotCount);
            undo.Push(tile);
            Assert.AreEqual(1, undo.TotalSnapshotCount);
            undo.Push(tile);
            Assert.AreEqual(2, undo.TotalSnapshotCount);
            undo.Pop(tile);
            Assert.AreEqual(1, undo.TotalSnapshotCount);

            Object.DestroyImmediate(tile);
        }

        [Test]
        public void Clear_RemovesAllSnapshots()
        {
            var undo = new WorldPainterUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            undo.Push(tile);
            undo.Push(tile);
            undo.Clear();
            Assert.AreEqual(0, undo.TotalSnapshotCount);
            Object.DestroyImmediate(tile);
        }
    }
}
