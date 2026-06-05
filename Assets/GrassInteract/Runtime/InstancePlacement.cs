#nullable enable
using Unity.Collections;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Authored-instances placement strategy: feed pre-authored <see cref="InstanceRecord"/> stream
    /// straight into the chunk-emit pipeline, bypassing RNG scatter.
    /// Mirrors the existing <c>BuildFromAuthored</c> code path in <see cref="GrassScatter"/>.
    /// </summary>
    internal sealed class InstancePlacement : IScatterPlacement
    {
        private readonly InstanceScatterLayer layer;

        public InstancePlacement(InstanceScatterLayer layer)
        {
            this.layer = layer;
        }

        public GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            NativeArray<InstanceRecord> records = this.layer.AuthoredInstances!.GetRuntimeRecords();

            int total = records.Length;
            int slabCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)InstanceBatchPool.MAX_INSTANCES_PER_BATCH));

            var baseSlabs     = new Matrix4x4[slabCount][];
            var positionSlabs = new Vector3[slabCount][];
            var normalSlabs   = new Vector3[slabCount][];
            var slabCounts    = new int[slabCount];

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            for (int b = 0; b < slabCount; ++b)
            {
                int start = b * InstanceBatchPool.MAX_INSTANCES_PER_BATCH;
                int count = Mathf.Min(InstanceBatchPool.MAX_INSTANCES_PER_BATCH, total - start);
                if (count < 0) count = 0;

                Matrix4x4[] slab    = pool.Rent();
                var posSlab         = new Vector3[InstanceBatchPool.MAX_INSTANCES_PER_BATCH];
                var nrmSlab         = new Vector3[InstanceBatchPool.MAX_INSTANCES_PER_BATCH];

                for (int k = 0; k < count; ++k)
                {
                    InstanceRecord rec = records[start + k];
                    Vector3 pos = rec.position;

                    // Build TRS from authored record. V2: scale is float (uniform).
                    slab[k]    = Matrix4x4.TRS(pos, rec.rotation, Vector3.one * rec.scale);
                    posSlab[k] = pos;
                    // Surface normal is not stored in the authored record — use Vector3.up
                    // (non-oriented layers) or attempt to recover from rotation when IsOriented.
                    // For byte-stability with the procedural path the surface normal is only
                    // consumed by slot2 packing in ChunkedInstanceBuffer.Bake; for overrideMask=0
                    // records it resolves to XorShift32 hash (legacy path), so the normal value
                    // passed here does not affect the GPU byte stream on non-oriented layers.
                    nrmSlab[k] = Vector3.up;

                    if (pos.y < minY) minY = pos.y;
                    if (pos.y > maxY) maxY = pos.y;
                }

                baseSlabs[b]     = slab;
                positionSlabs[b] = posSlab;
                normalSlabs[b]   = nrmSlab;
                slabCounts[b]    = count;
            }

            Vector2 bounds = this.layer.FieldBounds;
            float halfX    = bounds.x * 0.5f;
            float halfZ    = bounds.y * 0.5f;
            float maxScale = this.layer.ScaleRange.y;
            Bounds worldBounds = BuildFieldBounds(this.layer, origin, bounds, halfX, halfZ, maxScale, minY, maxY);

            return new GrassScatterResult(baseSlabs, slabCounts, positionSlabs, normalSlabs, total, worldBounds);
        }

        /// <summary>
        /// One field-wide AABB mirroring <c>GrassScatter.BuildFieldBounds</c>.
        /// Kept private until R5 promotes the helper to internal static on GrassScatter.
        /// </summary>
        private static Bounds BuildFieldBounds(ScatterLayer layer, Vector3 origin, Vector2 bounds,
            float halfX, float halfZ, float maxScale, float minY, float maxY)
        {
            float maxBladeHeight = layer.MaxBladeHeight;
            float bendHeadroom   = layer.BendHeadroom;
            float bladeReachY = maxBladeHeight * maxScale + bendHeadroom;
            float lateralPad = maxScale + bendHeadroom;

            if (float.IsPositiveInfinity(minY))
            {
                minY = origin.y;
                maxY = origin.y;
            }

            float yLow = minY;
            float yHigh = maxY + bladeReachY;
            var center = new Vector3(origin.x, (yLow + yHigh) * 0.5f, origin.z);
            var size = new Vector3(bounds.x, Mathf.Max(yHigh - yLow, 0.01f), bounds.y);
            var aabb = new Bounds(center, size);
            aabb.Expand(new Vector3(lateralPad, 0f, lateralPad));
            return aabb;
        }
    }
}
