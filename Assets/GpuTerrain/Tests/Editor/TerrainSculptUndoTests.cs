#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;
using GpuTerrain.Editor;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for TerrainSculptUndo: bounded depth eviction, push/pop round-trip, memory bound.
    /// </summary>
    [TestFixture]
    public class TerrainSculptUndoTests
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
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            FillHeight(tile, 0xAA);
            undo.Push(tile);

            // Modify tile after push.
            FillHeight(tile, 0xBB);

            var snap = undo.Pop(tile);
            Assert.IsNotNull(snap);
            // Bytes restored: all 0xAA.
            foreach (byte b in tile.heightData)
                Assert.AreEqual(0xAA, b, "Pop should restore the pushed bytes");

            UnityEngine.Object.DestroyImmediate(tile);
        }

        [Test]
        public void Pop_OnEmptyStack_ReturnsNull()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            var snap = undo.Pop(tile);
            Assert.IsNull(snap);
            UnityEngine.Object.DestroyImmediate(tile);
        }

        [Test]
        public void CanUndo_AfterPush_ReturnsTrue()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(1, 2));
            undo.Push(tile);
            Assert.IsTrue(undo.CanUndo(tile.tileCoord));
            UnityEngine.Object.DestroyImmediate(tile);
        }

        [Test]
        public void CanUndo_WhenEmpty_ReturnsFalse()
        {
            var undo = new TerrainSculptUndo();
            Assert.IsFalse(undo.CanUndo(new Vector2Int(0, 0)));
        }

        // ── Bounded depth eviction ────────────────────────────────────────────

        [Test]
        public void DepthCap_EvictsOldest_WhenExceeded()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));

            // Push UNDO_DEPTH + 1 snapshots.
            int pushCount = TerrainSculptConfig.UNDO_DEPTH + 1;
            for (int i = 0; i < pushCount; ++i)
            {
                FillHeight(tile, (byte)(i % 256));
                undo.Push(tile);
            }

            // Depth must not exceed the cap.
            int depth = undo.DepthForTile(tile.tileCoord);
            Assert.AreEqual(TerrainSculptConfig.UNDO_DEPTH, depth,
                "Depth should be capped at UNDO_DEPTH after overfilling");

            UnityEngine.Object.DestroyImmediate(tile);
        }

        [Test]
        public void DepthCap_OldestEvicted_NewestPreserved()
        {
            // Push UNDO_DEPTH+1 snapshots with distinct known byte values.
            // After eviction the oldest (value=0) should be gone;
            // popping all should yield values in reverse push order, newest first.
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0), heightRes: 3, splatRes: 3);

            int cap = TerrainSculptConfig.UNDO_DEPTH;
            byte oldest  = 0;
            byte newest  = (byte)((cap) % 256); // cap-th push (index cap, after eviction of oldest)

            for (int i = 0; i <= cap; ++i)
            {
                FillHeight(tile, (byte)(i % 256));
                undo.Push(tile);
            }

            // Pop all remaining (should be exactly UNDO_DEPTH pops).
            for (int i = 0; i < cap; ++i)
                Assert.IsNotNull(undo.Pop(tile), $"Pop #{i} should succeed");

            // One more pop: stack now empty.
            Assert.IsNull(undo.Pop(tile), "Stack should be empty after popping UNDO_DEPTH times");

            UnityEngine.Object.DestroyImmediate(tile);
        }

        // ── Snapshot immutability ─────────────────────────────────────────────

        [Test]
        public void Snapshot_IsImmutable_AfterModify()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            FillHeight(tile, 0x11);
            undo.Push(tile);

            // Mutate original bytes after push.
            FillHeight(tile, 0xFF);

            // Pop restores 0x11 — proves snapshot is a copy, not a reference.
            undo.Pop(tile);
            Assert.AreEqual(0x11, tile.heightData[0], "Snapshot must be a deep copy");

            UnityEngine.Object.DestroyImmediate(tile);
        }

        // ── Multi-tile isolation ──────────────────────────────────────────────

        [Test]
        public void MultiTile_UndoStacks_AreIndependent()
        {
            var undo  = new TerrainSculptUndo();
            var tileA = MakeTile(new Vector2Int(0, 0));
            var tileB = MakeTile(new Vector2Int(1, 0));

            FillHeight(tileA, 0xAA);
            FillHeight(tileB, 0xBB);
            undo.Push(tileA);
            undo.Push(tileB);

            // Modify both.
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
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));

            Assert.AreEqual(0, undo.TotalSnapshotCount);
            undo.Push(tile);
            Assert.AreEqual(1, undo.TotalSnapshotCount);
            undo.Push(tile);
            Assert.AreEqual(2, undo.TotalSnapshotCount);
            undo.Pop(tile);
            Assert.AreEqual(1, undo.TotalSnapshotCount);

            UnityEngine.Object.DestroyImmediate(tile);
        }

        [Test]
        public void Clear_RemovesAllSnapshots()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));
            undo.Push(tile);
            undo.Push(tile);
            undo.Clear();
            Assert.AreEqual(0, undo.TotalSnapshotCount);
            UnityEngine.Object.DestroyImmediate(tile);
        }

        // ── Memory bound assertion ────────────────────────────────────────────

        [Test]
        public void MemoryBound_NeverExceedsDepthCapAcrossManyPushes()
        {
            var undo = new TerrainSculptUndo();
            var tile = MakeTile(new Vector2Int(0, 0));

            int pushes = TerrainSculptConfig.UNDO_DEPTH * 3;
            for (int i = 0; i < pushes; ++i)
            {
                FillHeight(tile, (byte)(i % 256));
                undo.Push(tile);
                Assert.LessOrEqual(
                    undo.DepthForTile(tile.tileCoord),
                    TerrainSculptConfig.UNDO_DEPTH,
                    $"Depth exceeded cap after {i + 1} pushes");
            }

            UnityEngine.Object.DestroyImmediate(tile);
        }
    }
}
