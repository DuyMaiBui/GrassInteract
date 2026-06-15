#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Single source of truth for the field's world-XZ ↔ UV mapping. The static density map (placement,
    /// <see cref="ScatterLayer"/>) keys off this exact rect so scatter sampling can never drift.
    ///
    /// The rect is centered on the <see cref="ScatterField"/> transform and sized to the field
    /// bounds, matching <see cref="GrassScatter"/>'s placement (origin ± halfBounds, origin = transform pos).
    /// </summary>
    public readonly struct GrassFieldSpace
    {
        /// <summary>World-space XZ of the field's minimum corner (center - halfBounds).</summary>
        public readonly Vector2 MinXZ;

        /// <summary>World-space XZ size of the field (= field bounds: x = X extent, y = Z extent).</summary>
        public readonly Vector2 SizeXZ;

        /// <summary>
        /// Builds the field rect from the field center (the <see cref="ScatterField"/> transform
        /// position) and the XZ bounds. Mirrors <see cref="GrassScatter"/>.Build: origin ± halfBounds.
        /// </summary>
        public GrassFieldSpace(Vector3 center, Vector2 boundsXZ)
        {
            this.SizeXZ = boundsXZ;
            this.MinXZ = new Vector2(center.x - boundsXZ.x * 0.5f, center.z - boundsXZ.y * 0.5f);
        }

        /// <summary>World position → normalized UV in [0,1]² across the field rect (XZ plane).</summary>
        public Vector2 WorldToUv(Vector3 worldPos)
        {
            return new Vector2(
                (worldPos.x - this.MinXZ.x) / this.SizeXZ.x,
                (worldPos.z - this.MinXZ.y) / this.SizeXZ.y);
        }

        /// <summary>Normalized UV → world position at the given Y.</summary>
        public Vector3 UvToWorld(Vector2 uv, float y)
        {
            return new Vector3(
                this.MinXZ.x + uv.x * this.SizeXZ.x,
                y,
                this.MinXZ.y + uv.y * this.SizeXZ.y);
        }
    }
}
