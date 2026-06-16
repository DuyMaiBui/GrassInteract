using System.Collections.Generic;
using UnityEngine;

namespace MeshAtlas.Editor.Packing
{
    /// <summary>
    /// Detects whether a mesh's UV0 escapes the [0,1] range (tiled / wrapped UVs).
    /// Such meshes cannot be atlased by the rect-pack path and must be skipped — the
    /// wizard uses this to warn + exclude them rather than silently corrupt the atlas.
    /// Pure function — EditMode-unit-testable.
    /// </summary>
    public static class UvRangeInspector
    {
        public const float EPSILON = 0.001f;

        /// <summary>True if any uv falls outside [0-EPSILON, 1+EPSILON]. Reports the offending count.</summary>
        public static bool HasOutOfRangeUv(IReadOnlyList<Vector2> uv0, out int outOfRangeCount)
        {
            outOfRangeCount = 0;
            if (uv0 == null)
            {
                return false;
            }
            for (var i = 0; i < uv0.Count; i++)
            {
                var uv = uv0[i];
                if (uv.x < -EPSILON || uv.x > 1f + EPSILON || uv.y < -EPSILON || uv.y > 1f + EPSILON)
                {
                    outOfRangeCount++;
                }
            }
            return outOfRangeCount > 0;
        }
    }
}
