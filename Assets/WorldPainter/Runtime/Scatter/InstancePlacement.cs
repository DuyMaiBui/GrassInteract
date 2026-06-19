#nullable enable
using Unity.Collections;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Authored-instances placement strategy: feed pre-authored <see cref="InstanceRecord"/> stream
    /// straight into the chunk-emit pipeline, bypassing RNG scatter.
    /// Mirrors the existing <c>BuildFromAuthored</c> code path in <see cref="GrassScatter"/>.
    /// </summary>
    internal sealed class InstancePlacement : IScatterPlacement
    {
        private readonly IInstancePlacementSource source;

        public InstancePlacement(IInstancePlacementSource source)
        {
            this.source = source;
        }

        public GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            NativeArray<InstanceRecord> records = this.source.AuthoredInstances!.GetRuntimeRecords();

            int total = records.Length;
            int slabCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)InstanceBatchPool.MAX_INSTANCES_PER_BATCH));

            var baseSlabs     = new Matrix4x4[slabCount][];
            var positionSlabs = new Vector3[slabCount][];
            var normalSlabs   = new Vector3[slabCount][];
            var slabCounts    = new int[slabCount];

            // Track the TIGHT world-space extent of the actual instance positions. Authored instances
            // sit at arbitrary world positions (unlike density scatter, which fills origin ± FieldBounds),
            // so the render bounds MUST come from the real positions — see BuildInstanceBounds.
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
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

                    if (pos.x < minX) minX = pos.x;
                    if (pos.x > maxX) maxX = pos.x;
                    if (pos.z < minZ) minZ = pos.z;
                    if (pos.z > maxZ) maxZ = pos.z;
                    if (pos.y < minY) minY = pos.y;
                    if (pos.y > maxY) maxY = pos.y;
                }

                baseSlabs[b]     = slab;
                positionSlabs[b] = posSlab;
                normalSlabs[b]   = nrmSlab;
                slabCounts[b]    = count;
            }

            float maxScale = this.source.ScaleRange.y;
            Bounds worldBounds = BuildInstanceBounds(this.source, origin, maxScale, minX, maxX, minY, maxY, minZ, maxZ);

            // ── DIAGNOSTIC (remove with matching prints) ──────────────────────
            // Log the LAST record's position fed into the slab — should match the most-recent
            // EmitExactlyOneAt pivot. If it doesn't, the bug is in AuthoredInstancesData.
            if (total > 0 && positionSlabs.Length > 0)
            {
                int lastSlabIdx  = slabCounts.Length - 1;
                int lastSlabSize = slabCounts[lastSlabIdx];
                if (lastSlabSize > 0)
                {
                    Vector3 lastPos = positionSlabs[lastSlabIdx][lastSlabSize - 1];
                    WpLog.Log(
                        $"[InstancePlacement.Build] totalRecords={total} " +
                        $"lastPos={lastPos:F4} maxScale={maxScale:F4} " +
                        $"worldBounds.center={worldBounds.center:F4} worldBounds.size={worldBounds.size:F4}");
                }
            }

            return new GrassScatterResult(baseSlabs, slabCounts, positionSlabs, normalSlabs, total, worldBounds);
        }

        /// <summary>
        /// World-render AABB for authored instances: the TIGHT bounding box of the ACTUAL instance
        /// positions, inflated by lateral sway pad (XZ) and bent-prop reach (Y), with a minimum extent on
        /// every axis.
        ///
        /// Unlike density layers — whose positions fill <c>origin ± FieldBounds</c>, so an origin-centred
        /// box sized to <c>FieldBounds</c> is correct — authored instances sit at arbitrary world positions
        /// that may be far from the field origin, and <c>FieldBounds</c> is only their EXTENT. An
        /// origin-centred box therefore need not contain them, and a single/colinear set yields a
        /// zero-extent box; either way <see cref="Graphics.RenderMeshIndirect"/> frustum-culls the WHOLE
        /// draw (zero-extent or out-of-frustum bounds → nothing renders, surviving rebuild + domain reload).
        /// Deriving the box from the real positions + a minimum extent fixes both.
        /// </summary>
        private static Bounds BuildInstanceBounds(IInstancePlacementSource source, Vector3 origin,
            float maxScale, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
        {
            float bladeReachY = source.MaxBladeHeight * maxScale + source.BendHeadroom;
            float lateralPad  = maxScale + source.BendHeadroom;

            // Empty layer (no records): a small valid box at the origin (never zero-extent / NaN).
            if (float.IsPositiveInfinity(minX))
                return new Bounds(origin, Vector3.one * Mathf.Max(1f, lateralPad * 2f));

            var aabb = new Bounds();
            aabb.SetMinMax(
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, maxY + bladeReachY, maxZ));
            aabb.Expand(new Vector3(lateralPad, 0f, lateralPad)); // XZ sway headroom

            // Guard: a single instance / colinear set has zero extent on an axis; RenderMeshIndirect culls
            // zero-extent draws, so clamp every axis to a small positive minimum.
            Vector3 sz = aabb.size;
            aabb.size = new Vector3(Mathf.Max(sz.x, 0.5f), Mathf.Max(sz.y, 0.5f), Mathf.Max(sz.z, 0.5f));
            return aabb;
        }
    }
}
