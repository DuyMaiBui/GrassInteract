#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// FIFO of pending brush-stamp positions (painting space) captured during a drag.
    ///
    /// <see cref="WorldPainterSculptTool.HandleMouseDrag"/> enqueues interpolated stamp positions
    /// (O(1), no GPU) at mouse-event frequency; the throttled drain (~15 Hz) pops the whole batch
    /// and dispatches the density compute + ONE scoped scatter rebuild. Decoupling input sampling
    /// from GPU work is what makes drag painting smooth — the old path dispatched a density compute
    /// + writeback per mouse event, which stuttered.
    ///
    /// Main-thread only (editor scene GUI / update loop).
    /// </summary>
    internal sealed class StrokeStampBuffer
    {
        private readonly Queue<Vector3> pending = new();

        /// <summary>Number of stamps buffered but not yet dispatched.</summary>
        public int Count => this.pending.Count;

        /// <summary>Append one interpolated stamp position (painting space).</summary>
        public void Enqueue(Vector3 stampPos) => this.pending.Enqueue(stampPos);

        /// <summary>Pop the oldest buffered stamp; returns false when empty.</summary>
        public bool TryDequeue(out Vector3 stampPos)
        {
            if (this.pending.Count == 0)
            {
                stampPos = Vector3.zero;
                return false;
            }
            stampPos = this.pending.Dequeue();
            return true;
        }

        /// <summary>Drop all buffered stamps (stroke begin / abort).</summary>
        public void Clear() => this.pending.Clear();
    }
}
