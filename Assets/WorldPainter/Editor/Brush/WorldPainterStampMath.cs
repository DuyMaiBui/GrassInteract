#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Pure stamp-spacing math for brush strokes: evenly-spaced stamp positions along a
    /// stroke segment. Ported from the retired Scatter-Studio <c>DensityPaintGPU</c>
    /// (P3b folded the GPU paint into <c>TerrainBrush.compute</c> + <c>WorldPainterDensityEncoder</c>);
    /// the spacing-interpolation math is kept here so its EditMode coverage survives.
    /// </summary>
    public static class WorldPainterStampMath
    {
        /// <summary>
        /// Stamp positions along the segment <paramref name="fromUv"/>→<paramref name="toUv"/> at
        /// <c>spacing = radiusUv * spacingFactor</c> intervals. Always includes
        /// <paramref name="toUv"/> as the final stamp; returns a single stamp at
        /// <paramref name="toUv"/> for a zero-length segment.
        /// </summary>
        public static Vector2[] ComputeStampPositions(
            Vector2 fromUv,
            Vector2 toUv,
            float   radiusUv,
            float   spacingFactor)
        {
            float spacing = radiusUv * Mathf.Max(0.001f, spacingFactor);
            float dist    = Vector2.Distance(fromUv, toUv);

            if (dist < 0.0001f)
                return new[] { toUv };

            int count  = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
            var result = new List<Vector2>(count);

            for (int i = 1; i <= count; ++i)
                result.Add(Vector2.Lerp(fromUv, toUv, i / (float)count));

            return result.ToArray();
        }
    }
}
