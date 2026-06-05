#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Single source of truth for the field's world-XZ ↔ UV mapping. Both top-down maps — the static
    /// density map (placement, <see cref="GrassLayer"/>) and the runtime trample RenderTexture
    /// (interaction, GrassTrampleMap) — key off this exact rect so they can never drift.
    ///
    /// The rect is centered on the <see cref="GrassInteractField"/> transform and sized to the field
    /// bounds, matching <see cref="ChunkGrid"/>'s placement (origin ± halfBounds, origin = transform pos).
    /// The matching shader-side math lives in <c>GrassInteractDeform.hlsl</c> (<c>GrassField_WorldToUv</c>),
    /// fed by the <c>_GrassFieldRect</c> global this struct binds.
    /// </summary>
    public readonly struct GrassFieldSpace
    {
        /// <summary>World-space XZ of the field's minimum corner (center - halfBounds).</summary>
        public readonly Vector2 MinXZ;

        /// <summary>World-space XZ size of the field (= field bounds: x = X extent, y = Z extent).</summary>
        public readonly Vector2 SizeXZ;

        private static readonly int FieldRectId = Shader.PropertyToID("_GrassFieldRect");

        /// <summary>
        /// Builds the field rect from the field center (the <see cref="GrassInteractField"/> transform
        /// position) and the XZ bounds. Mirrors <see cref="ChunkGrid"/>.Build: origin ± halfBounds.
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

        /// <summary>
        /// Pushes <c>_GrassFieldRect</c> = (minX, minZ, sizeX, sizeZ) as a global so every shader sampler
        /// (trample, density preview) reads the same rect this struct computes.
        /// </summary>
        public void BindGlobals()
        {
            Shader.SetGlobalVector(FieldRectId,
                new Vector4(this.MinXZ.x, this.MinXZ.y, this.SizeXZ.x, this.SizeXZ.y));
        }
    }
}
