#nullable enable
using System;
using System.Collections.Generic;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Record-snapshot half of <see cref="WorldPainterUndo"/> (partial).
    ///
    /// Contains the prop-stroke undo path introduced in Phase 4 task 5:
    /// per-layer <see cref="InstanceRecord"/> list snapshots with the same
    /// depth cap (<see cref="WorldPainterUndo.DEPTH_CAP"/>) and eviction policy
    /// as the tile-snapshot stacks in the primary partial file.
    ///
    /// Key = <c>layer.GetInstanceID()</c> (int).
    /// </summary>
    public sealed partial class WorldPainterUndo
    {
        // ── Record snapshots (Phase 4 task 5 — prop stroke undo) ─────────────

        // Key = layer instance ID (layer.GetInstanceID()).
        private readonly Dictionary<int, LinkedList<List<InstanceRecord>>> recordStacks =
            new Dictionary<int, LinkedList<List<InstanceRecord>>>();

        /// <summary>
        /// Snapshot current authored records for <paramref name="data"/> BEFORE a prop stroke.
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
