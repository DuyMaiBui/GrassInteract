#nullable enable
namespace GrassInteract
{
    /// <summary>
    /// Pure math helper: converts LOD switch distances + cull distance into squared thresholds
    /// consumed by both <see cref="InstancedPropEngine"/> and <see cref="GrassGpuEngine"/>.
    ///
    /// Missing LOD switch entries extend the last present LOD to the cull boundary rather than
    /// falling back to arbitrary defaults (12 m / 30 m). This prevents the 1-LOD draw-distance
    /// bug where props vanish at 12 m regardless of <c>renderCullDistance</c>.
    /// </summary>
    public static class LodCullMath
    {
        /// <summary>
        /// Returns <c>(lod0Sqr, lod1Sqr, maxSqr)</c> from switch distances and cull distance.
        /// <para>
        /// Missing switch entries are filled with <c>farSqr = cull² &gt; 0 ? cull² : float.MaxValue</c>
        /// so instances bucket into the last present (mesh-owning) LOD slot rather than an empty one.
        /// </para>
        /// </summary>
        /// <param name="switches">LOD switch distances (metres). Index 0 = LOD0→1 boundary, index 1 = LOD1→2 boundary.</param>
        /// <param name="cull">Per-layer render cull distance in metres. ≤0 means "render all / no cull".</param>
        public static (float lod0Sqr, float lod1Sqr, float maxSqr) Thresholds(float[]? switches, float cull)
        {
            float maxSqr = cull * cull;
            // cull<=0 means "render all" → no cap; missing switch distances must be float.MaxValue so
            // every instance buckets to LOD0 (the only mesh present). Otherwise cap at cull boundary.
            float farSqr = cull > 0f ? maxSqr : float.MaxValue;
            float l0 = switches != null && switches.Length > 0 ? switches[0] * switches[0] : farSqr;
            float l1 = switches != null && switches.Length > 1 ? switches[1] * switches[1] : farSqr;
            return (l0, l1, maxSqr);
        }
    }
}
