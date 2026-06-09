#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Result of a flat scatter: pooled instance-matrix slabs (each &lt;= 1023 TRS matrices) plus a parallel
    /// set of base world positions (one per matrix, same slab/index layout) so the bend simulator can iterate
    /// slabs uniformly. One field-wide <see cref="WorldBounds"/> covers the whole field (no spatial chunks).
    ///
    /// Phase 4: <see cref="BaseNormalSlabs"/> carries the surface normal used to build each instance's
    /// base matrix. For non-oriented layers every entry is Vector3.up. The normals are needed by
    /// <see cref="ChunkedBladeBuffer"/> and <see cref="ChunkedInstanceBuffer"/> to pack oriented slot2
    /// (octNormal16 | pitch8 | roll8) when <see cref="ScatterLayer.IsOriented"/> is true.
    /// </summary>
    public sealed class GrassScatterResult
    {
        /// <summary>Pooled base TRS matrices, jagged: <c>BaseSlabs[b]</c> holds up to 1023 matrices.</summary>
        public readonly Matrix4x4[][] BaseSlabs;

        /// <summary>Valid instance count per slab (slabs are fixed-size; the last slab is partial).</summary>
        public readonly int[] SlabCounts;

        /// <summary>Base world positions, parallel to <see cref="BaseSlabs"/> (same slab/index layout).</summary>
        public readonly Vector3[][] BasePositionSlabs;

        /// <summary>
        /// Surface normals, parallel to <see cref="BaseSlabs"/> (same slab/index layout).
        /// Vector3.up for non-oriented layers. Used by GPU bakers to pack oriented slot2.
        /// </summary>
        public readonly Vector3[][] BaseNormalSlabs;

        /// <summary>Total kept blade count across all slabs.</summary>
        public readonly int TotalCount;

        /// <summary>Single field-wide AABB used as the render bounds (sized to include bent-blade reach).</summary>
        public readonly Bounds WorldBounds;

        public GrassScatterResult(Matrix4x4[][] baseSlabs, int[] slabCounts, Vector3[][] basePositionSlabs,
            Vector3[][] baseNormalSlabs, int totalCount, Bounds worldBounds)
        {
            this.BaseSlabs = baseSlabs;
            this.SlabCounts = slabCounts;
            this.BasePositionSlabs = basePositionSlabs;
            this.BaseNormalSlabs = baseNormalSlabs;
            this.TotalCount = totalCount;
            this.WorldBounds = worldBounds;
        }
    }

    /// <summary>
    /// Façade for the scatter build pipeline. Delegates to <see cref="ScatterLayer.CreatePlacement"/>
    /// which returns the appropriate <see cref="IScatterPlacement"/> strategy for the layer type.
    ///
    /// Build-time only — the per-frame path touches none of this.
    /// </summary>
    public static class GrassScatter
    {
        /// <summary>
        /// Builds a <see cref="GrassScatterResult"/> for <paramref name="layer"/> by delegating to
        /// the layer's placement strategy (<see cref="ScatterLayer.CreatePlacement"/>).
        /// </summary>
        public static GrassScatterResult Build(ScatterLayer layer, Vector3 origin,
            InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            return layer.CreatePlacement().Build(origin, pool, sampler);
        }

        /// <summary>
        /// One field-wide AABB: XZ = the field rect (origin ± halfBounds) expanded by lateral pad; Y spans
        /// [minSnappedY, maxSnappedY + bladeReachY] where bladeReachY covers the tallest scaled blade plus
        /// bend/wind headroom, so no blade is ever wrongly culled.
        /// </summary>
        internal static Bounds BuildFieldBounds(ScatterLayer layer, Vector3 origin, Vector2 bounds,
            float halfX, float halfZ, float maxScale, float minY, float maxY)
        {
            // Cull headroom from the layer's own MaxBladeHeight + BendHeadroom (SSOT — all render
            // parameters live on ScatterLayer post-refactor). MeshScatterEngine derives its own cull
            // headroom from mesh bounds and does not rely on these for Mesh-kind layers.
            float maxBladeHeight = layer.Bounds.MaxBladeHeight;
            float bendHeadroom   = layer.Bounds.BendHeadroom;
            float bladeReachY = maxBladeHeight * maxScale + bendHeadroom;
            float lateralPad = maxScale + bendHeadroom;

            // Empty field (no kept blades): collapse the Y span onto the field plane so the box is still valid.
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
            aabb.Expand(new Vector3(lateralPad, 0f, lateralPad)); // XZ headroom; Y already exact
            return aabb;
        }

        /// <summary>Returns the pooled matrix slabs so a subsequent <see cref="Build"/> can reuse them.
        /// Position slabs are plain arrays and are GC-dropped (not pooled).</summary>
        public static void ReturnSlabs(GrassScatterResult? result, InstanceBatchPool pool)
        {
            if (result == null)
                return;

            foreach (Matrix4x4[] slab in result.BaseSlabs)
                pool.Return(slab);
        }
    }
}
