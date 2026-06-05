#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// A spatial tile of grass instances, pre-partitioned at build time into GPU-instancing batches of
    /// <see cref="InstanceBatchPool.MAX_INSTANCES_PER_BATCH"/> matrices each. The batch arrays are pooled
    /// and persist for the field's lifetime, so the per-frame render loop never allocates.
    /// </summary>
    public sealed class GrassChunk
    {
        /// <summary>World-space AABB used for frustum culling (sized to include bent-blade reach).</summary>
        public readonly Bounds Bounds;

        /// <summary>Cached AABB center, used for distance-to-camera LOD selection.</summary>
        public readonly Vector3 Center;

        /// <summary>Pooled instance matrices, jagged: <c>Batches[b]</c> holds up to 1023 TRS matrices.</summary>
        public readonly Matrix4x4[][] Batches;

        /// <summary>Valid instance count per batch (slabs are fixed-size; the last batch is partial).</summary>
        public readonly int[] BatchCounts;

        public GrassChunk(Bounds bounds, Matrix4x4[][] batches, int[] batchCounts)
        {
            this.Bounds = bounds;
            this.Center = bounds.center;
            this.Batches = batches;
            this.BatchCounts = batchCounts;
        }
    }
}
