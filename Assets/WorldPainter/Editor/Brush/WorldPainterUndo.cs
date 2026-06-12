#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Per-tile snapshot undo stack for the WorldPainter sculpt tool (task 9).
    ///
    /// Extended in Phase 4 (task 5) to also snapshot instance records for prop layers,
    /// using the same depth/memory caps and eviction policy.
    ///
    /// Design §7.4:
    ///   - Depth cap: 10 snapshots per tile / per layer key.
    ///   - Memory cap: 128 MB total across all tiles; evict oldest when exceeded.
    ///   - Eviction: oldest-first (LinkedList head = oldest).
    ///   - One Unity Undo group per stroke → single Ctrl+Z reverts one stroke.
    ///
    /// Per-tile snapshot undo over <see cref="WorldPainter"/> tile refs.
    ///
    /// CPU byte arrays only — no GPU resources held.
    /// </summary>
    public sealed class WorldPainterUndo
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Maximum snapshot depth per tile.</summary>
        public const int DEPTH_CAP = 10;

        /// <summary>Total memory cap across all tiles (128 MB).</summary>
        public const long MEMORY_CAP_BYTES = 128L * 1024L * 1024L;

        // ── Snapshot record ───────────────────────────────────────────────────

        /// <summary>Immutable snapshot of one tile's raw byte data.</summary>
        public sealed class TileSnapshot
        {
            public readonly Vector2Int TileCoord;
            public readonly byte[]     HeightData;
            public readonly byte[]     SplatData;

            /// <summary>
            /// Monotonic insertion counter assigned at Push time.
            /// Lower value = older snapshot; used by <see cref="EvictToMemoryCap"/>
            /// to find the globally oldest entry across all tile stacks.
            /// </summary>
            public readonly long Sequence;

            public TileSnapshot(Vector2Int coord, byte[] heightData, byte[] splatData, long sequence)
            {
                this.TileCoord  = coord;
                this.HeightData = heightData ?? throw new ArgumentNullException(nameof(heightData));
                this.SplatData  = splatData  ?? throw new ArgumentNullException(nameof(splatData));
                this.Sequence   = sequence;
            }

            public long ByteSize => this.HeightData.LongLength + this.SplatData.LongLength;
        }

        // ── Per-tile stacks ───────────────────────────────────────────────────

        // LinkedList: head = oldest, tail = newest.
        private readonly Dictionary<Vector2Int, LinkedList<TileSnapshot>> stacks =
            new Dictionary<Vector2Int, LinkedList<TileSnapshot>>();

        // Monotonic insertion counter — used by EvictToMemoryCap to find the
        // globally oldest snapshot across tiles, regardless of tile order in the dict.
        private long nextSequence;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Total snapshot count across all tiles.</summary>
        public int TotalSnapshotCount
        {
            get
            {
                int n = 0;
                foreach (var kv in this.stacks) n += kv.Value.Count;
                return n;
            }
        }

        /// <summary>Estimated total memory used by all snapshots in bytes.</summary>
        public long TotalMemoryBytes
        {
            get
            {
                long n = 0;
                foreach (var kv in this.stacks)
                    foreach (var snap in kv.Value)
                        n += snap.ByteSize;
                return n;
            }
        }

        /// <summary>Snapshot depth for a given tile coordinate (0 if no snapshots).</summary>
        public int DepthForTile(Vector2Int coord) =>
            this.stacks.TryGetValue(coord, out var s) ? s.Count : 0;

        /// <summary>Returns true if any undo is available for the tile.</summary>
        public bool CanUndo(Vector2Int coord) =>
            this.stacks.TryGetValue(coord, out var s) && s.Count > 0;

        /// <summary>
        /// Push a snapshot for a tile BEFORE a stroke begins.
        /// Deep-copies the byte arrays so the snapshot is immutable.
        /// Enforces depth cap (evict oldest per tile) and 128 MB memory cap
        /// (evict oldest globally until under cap).
        /// </summary>
        public void Push(TerrainTileAsset tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            if (!this.stacks.TryGetValue(tile.tileCoord, out var stack))
            {
                stack = new LinkedList<TileSnapshot>();
                this.stacks[tile.tileCoord] = stack;
            }

            var snap = new TileSnapshot(
                tile.tileCoord,
                CopyBytes(tile.heightData),
                CopyBytes(tile.splatData),
                this.nextSequence++);

            stack.AddLast(snap);

            // Evict oldest per tile when depth cap exceeded.
            while (stack.Count > DEPTH_CAP)
                stack.RemoveFirst();

            // Evict oldest globally until memory cap satisfied.
            this.EvictToMemoryCap();
        }

        /// <summary>
        /// Pop the most-recent snapshot for a tile and restore its bytes into the asset.
        /// Returns the restored snapshot (caller can re-upload GPU).
        /// Returns null if no snapshot is available.
        /// </summary>
        public TileSnapshot? Pop(TerrainTileAsset tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            if (!this.stacks.TryGetValue(tile.tileCoord, out var stack) || stack.Count == 0)
                return null;

            var snap = stack.Last!.Value;
            stack.RemoveLast();

            Array.Copy(snap.HeightData, tile.heightData, snap.HeightData.Length);
            if (snap.SplatData.Length > 0 && tile.splatData != null &&
                tile.splatData.Length == snap.SplatData.Length)
                Array.Copy(snap.SplatData, tile.splatData, snap.SplatData.Length);

            return snap;
        }

        /// <summary>Clear all snapshots for all tiles.</summary>
        public void Clear() => this.stacks.Clear();

        /// <summary>Clear all snapshots for one tile.</summary>
        public void ClearTile(Vector2Int coord) => this.stacks.Remove(coord);

        // ── Memory-cap eviction ───────────────────────────────────────────────

        /// <summary>
        /// Evicts the globally oldest snapshot (across all tiles) until total
        /// memory is under <see cref="MEMORY_CAP_BYTES"/>.
        /// Uses the <see cref="TileSnapshot.Sequence"/> monotonic counter to
        /// find the true minimum across all per-tile stacks.
        /// </summary>
        private void EvictToMemoryCap()
        {
            while (this.TotalMemoryBytes > MEMORY_CAP_BYTES)
            {
                // Walk all tile stacks and find the head with the smallest Sequence.
                LinkedList<TileSnapshot>? oldestStack = null;
                long oldestSeq = long.MaxValue;

                foreach (var kv in this.stacks)
                {
                    if (kv.Value.Count == 0) continue;
                    long seq = kv.Value.First!.Value.Sequence;
                    if (seq < oldestSeq)
                    {
                        oldestSeq   = seq;
                        oldestStack = kv.Value;
                    }
                }

                if (oldestStack == null) break; // no snapshots left
                oldestStack.RemoveFirst();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static byte[] CopyBytes(byte[]? src)
        {
            if (src == null || src.Length == 0) return Array.Empty<byte>();
            var dst = new byte[src.Length];
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
            return dst;
        }

        // ── Record snapshots (Phase 4 task 5 — prop stroke undo) ─────────────

        // Key = layer instance ID (layer.GetInstanceID()).
        private readonly Dictionary<int, LinkedList<List<InstanceRecord>>> recordStacks =
            new Dictionary<int, LinkedList<List<InstanceRecord>>>();

        /// <summary>
        /// Snapshot current authored records for <paramref name="layer"/> BEFORE a prop stroke.
        /// Deep-copies the list so the snapshot is immutable.
        /// Evicts oldest when depth cap exceeded.
        /// </summary>
        public void PushRecords(AuthoredInstancesData data, int layerKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (!this.recordStacks.TryGetValue(layerKey, out var stack))
            {
                stack = new LinkedList<List<InstanceRecord>>();
                this.recordStacks[layerKey] = stack;
            }

            // Deep-copy the current working list.
            var snapshot = new List<InstanceRecord>(data.WorkingList);
            stack.AddLast(snapshot);

            while (stack.Count > DEPTH_CAP)
                stack.RemoveFirst();
        }

        /// <summary>
        /// Pop the most-recent record snapshot for a layer and restore the working list.
        /// Returns true when a snapshot was found and restored.
        /// </summary>
        public bool PopRecords(AuthoredInstancesData data, int layerKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (!this.recordStacks.TryGetValue(layerKey, out var stack) || stack.Count == 0)
                return false;

            var snap = stack.Last!.Value;
            stack.RemoveLast();

            // Restore working list in-place.
            var list = data.WorkingList;
            list.Clear();
            list.AddRange(snap);
            data.PackBlob();

            return true;
        }

        /// <summary>Returns true if any record snapshot is available for the layer key.</summary>
        public bool CanUndoRecords(int layerKey) =>
            this.recordStacks.TryGetValue(layerKey, out var s) && s.Count > 0;

        /// <summary>Clears all record snapshots for all layers.</summary>
        public void ClearAllRecords() => this.recordStacks.Clear();
    }
}
