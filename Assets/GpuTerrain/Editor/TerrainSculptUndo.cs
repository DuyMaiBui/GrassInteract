#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Per-tile snapshot undo stack for the sculpt + paint tool (Phase 5).
    ///
    /// Design:
    ///   - One <see cref="TileSnapshot"/> = a copy of <c>heightData</c> + <c>splatData</c> bytes
    ///     taken BEFORE each stroke begins.
    ///   - Bounded to <see cref="TerrainSculptConfig.UNDO_DEPTH"/> entries per tile;
    ///     oldest entry evicted when the depth is exceeded.
    ///   - Integrates with Unity's Undo system so Ctrl+Z / Redo flow through the same callback.
    ///   - Does NOT hold GPU resources — snapshots are CPU byte arrays only.
    /// </summary>
    public sealed class TerrainSculptUndo
    {
        // ── Snapshot record ───────────────────────────────────────────────────

        /// <summary>Immutable snapshot of one tile's raw byte data at a point in time.</summary>
        public sealed class TileSnapshot
        {
            public readonly Vector2Int TileCoord;
            public readonly byte[]     HeightData;
            public readonly byte[]     SplatData;

            public TileSnapshot(Vector2Int tileCoord, byte[] heightData, byte[] splatData)
            {
                this.TileCoord  = tileCoord;
                this.HeightData = heightData ?? throw new ArgumentNullException(nameof(heightData));
                this.SplatData  = splatData  ?? throw new ArgumentNullException(nameof(splatData));
            }
        }

        // ── Per-tile stacks ───────────────────────────────────────────────────

        private readonly Dictionary<Vector2Int, LinkedList<TileSnapshot>> undoStacks =
            new Dictionary<Vector2Int, LinkedList<TileSnapshot>>();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Total snapshot count across all tiles (for memory monitoring).</summary>
        public int TotalSnapshotCount
        {
            get
            {
                int total = 0;
                foreach (var kv in this.undoStacks)
                    total += kv.Value.Count;
                return total;
            }
        }

        /// <summary>Returns the snapshot depth for a given tile (0 if none).</summary>
        public int DepthForTile(Vector2Int coord) =>
            this.undoStacks.TryGetValue(coord, out var stack) ? stack.Count : 0;

        /// <summary>
        /// Push a snapshot for a tile BEFORE a stroke begins.
        /// Copies the byte arrays so the snapshot is immutable.
        /// Evicts the oldest entry when depth exceeds <see cref="TerrainSculptConfig.UNDO_DEPTH"/>.
        /// </summary>
        public void Push(TerrainTileAsset tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            if (!this.undoStacks.TryGetValue(tile.tileCoord, out var stack))
            {
                stack = new LinkedList<TileSnapshot>();
                this.undoStacks[tile.tileCoord] = stack;
            }

            // Deep-copy bytes — snapshot must be immutable.
            var snap = new TileSnapshot(
                tile.tileCoord,
                CopyBytes(tile.heightData),
                CopyBytes(tile.splatData));

            stack.AddLast(snap);

            // Evict oldest if over depth cap.
            while (stack.Count > TerrainSculptConfig.UNDO_DEPTH)
                stack.RemoveFirst();
        }

        /// <summary>
        /// Pop the most-recent snapshot for a tile and restore its bytes into the asset.
        /// Returns the restored snapshot (caller can re-upload GPU resources).
        /// Returns null if no snapshot is available.
        /// </summary>
        public TileSnapshot? Pop(TerrainTileAsset tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));

            if (!this.undoStacks.TryGetValue(tile.tileCoord, out var stack) || stack.Count == 0)
                return null;

            var snap = stack.Last!.Value;
            stack.RemoveLast();

            // Restore bytes into the asset.
            Array.Copy(snap.HeightData, tile.heightData, snap.HeightData.Length);
            // L2: guard against null splatData — tile may have been created without splat.
            if (snap.SplatData.Length > 0 && tile.splatData != null &&
                tile.splatData.Length == snap.SplatData.Length)
                Array.Copy(snap.SplatData, tile.splatData, snap.SplatData.Length);

            return snap;
        }

        /// <summary>
        /// Returns true if any undo is available for the tile.
        /// </summary>
        public bool CanUndo(Vector2Int coord) =>
            this.undoStacks.TryGetValue(coord, out var s) && s.Count > 0;

        /// <summary>Clear all snapshots for all tiles.</summary>
        public void Clear() => this.undoStacks.Clear();

        /// <summary>Clear all snapshots for one tile.</summary>
        public void ClearTile(Vector2Int coord) => this.undoStacks.Remove(coord);

        // ── Helpers ───────────────────────────────────────────────────────────

        private static byte[] CopyBytes(byte[]? src)
        {
            if (src == null || src.Length == 0) return Array.Empty<byte>();
            var dst = new byte[src.Length];
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
            return dst;
        }
    }
}
