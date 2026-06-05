#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// <see cref="ISurfaceSampler"/> implementation that wraps the legacy downward
    /// <c>Physics.Raycast</c> ground-snap used by the original <see cref="GrassScatter"/> code.
    ///
    /// Behavior is byte-identical to the pre-seam scatter path:
    ///   ray origin  = (worldX, originY + RAY_START_HEIGHT, worldZ)
    ///   direction   = Vector3.down
    ///   max distance = RAY_MAX_DISTANCE
    ///   layer mask  = groundMask passed to the constructor.
    ///
    /// On hit  → returns <c>true</c>; populates Y, Normal, SlopeDeg; SplatWeights = null.
    /// No hit  → returns <c>false</c>; the caller applies the field-plane-Y fallback and the
    ///            warn-once log (same as the original inline code).
    /// </summary>
    public sealed class RaycastSurfaceSampler : ISurfaceSampler
    {
        // Constants mirror the original GrassScatter constants — they MUST stay in sync.
        internal const float RAY_START_HEIGHT = 1000f; // metres above the field origin
        internal const float RAY_MAX_DISTANCE = 5000f;  // downward cast length

        private readonly int groundMask;
        private readonly float originY;

        /// <summary>
        /// Creates a raycast-based surface sampler.
        /// </summary>
        /// <param name="groundMask">
        /// Physics layer mask for the downward cast (same as <c>ScatterLayer.GroundSnapMask</c>).
        /// </param>
        /// <param name="originY">
        /// World-space Y of the field origin. The ray start height is offset from this value.
        /// Pass <c>ScatterField.transform.position.y</c>.
        /// </param>
        public RaycastSurfaceSampler(LayerMask groundMask, float originY)
        {
            this.groundMask = groundMask.value;
            this.originY = originY;
        }

        /// <inheritdoc/>
        public bool TrySample(float worldX, float worldZ, out SurfaceHit hit)
        {
            var rayOrigin = new Vector3(worldX, this.originY + RAY_START_HEIGHT, worldZ);
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit rayHit, RAY_MAX_DISTANCE, this.groundMask))
            {
                hit.Y = rayHit.point.y;
                hit.Normal = rayHit.normal;
                hit.SlopeDeg = Vector3.Angle(rayHit.normal, Vector3.up);
                hit.SplatWeights = null;
                return true;
            }

            hit = default;
            return false;
        }
    }
}
