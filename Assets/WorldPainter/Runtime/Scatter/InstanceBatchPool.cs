#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Recycles fixed-size <see cref="Matrix4x4"/> instancing slabs (1023 = Unity's per-instanced-draw
    /// cap for the matrix-array path).
    ///
    /// This is the "object pool" of the system, reconciled correctly for GPU instancing on mobile:
    /// we do NOT pool GameObjects. Slabs are rented once at (re)build time and held by chunks for the
    /// field's lifetime; the per-frame render loop reuses those persistent arrays and allocates nothing.
    /// On rebuild (e.g. an inspector tweak), a chunk's slabs are returned here and handed back out,
    /// so repeated rebuilds do not churn the GC.
    /// </summary>
    public sealed class InstanceBatchPool
    {
        /// <summary>Unity's hard limit for instances per <c>Graphics.RenderMeshInstanced</c> matrix-array call.</summary>
        public const int MAX_INSTANCES_PER_BATCH = 1023;

        private readonly Stack<Matrix4x4[]> free = new Stack<Matrix4x4[]>();

        public InstanceBatchPool(int prewarmSlabs = 0)
        {
            for (int i = 0; i < prewarmSlabs; ++i)
                this.free.Push(new Matrix4x4[MAX_INSTANCES_PER_BATCH]);
        }

        /// <summary>Rents a fixed-size (1023) slab. Caller tracks the valid element count separately.</summary>
        public Matrix4x4[] Rent()
        {
            return this.free.Count > 0 ? this.free.Pop() : new Matrix4x4[MAX_INSTANCES_PER_BATCH];
        }

        /// <summary>Returns a slab for reuse. Foreign-sized arrays are rejected (defensive).</summary>
        public void Return(Matrix4x4[] slab)
        {
            if (slab != null && slab.Length == MAX_INSTANCES_PER_BATCH)
                this.free.Push(slab);
        }

        /// <summary>Drops all pooled slabs (lets the GC reclaim them).</summary>
        public void Clear()
        {
            this.free.Clear();
        }
    }
}
