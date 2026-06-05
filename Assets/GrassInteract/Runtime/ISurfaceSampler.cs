#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Seam for surface-height and normal sampling during scatter placement.
    /// <see cref="RaycastSurfaceSampler"/> wraps the legacy downward <c>Physics.Raycast</c> path;
    /// <see cref="TerrainSurfaceSampler"/> reads directly from <see cref="TerrainData"/>.
    ///
    /// <see cref="GrassScatter.Build"/> accepts any implementation so the scatter algorithm is
    /// independent of the ground-probe strategy.
    /// </summary>
    public interface ISurfaceSampler
    {
        /// <summary>
        /// Attempt to sample the surface at world-space position (<paramref name="worldX"/>,
        /// <paramref name="worldZ"/>).
        /// </summary>
        /// <param name="worldX">World-space X coordinate.</param>
        /// <param name="worldZ">World-space Z coordinate.</param>
        /// <param name="hit">Populated on <c>true</c>; undefined on <c>false</c>.</param>
        /// <returns>
        /// <c>true</c> if a surface exists at the given XZ (hit, or in-bounds non-hole terrain);
        /// <c>false</c> if the position is a terrain hole, outside terrain bounds, or no physics
        /// collider was found.
        /// </returns>
        bool TrySample(float worldX, float worldZ, out SurfaceHit hit);
    }

    /// <summary>
    /// Result of a surface sample. Populated by <see cref="ISurfaceSampler.TrySample"/> on success.
    /// </summary>
    public struct SurfaceHit
    {
        /// <summary>World-space Y (height) of the surface at the sampled XZ.</summary>
        public float Y;

        /// <summary>World-space surface normal.</summary>
        public Vector3 Normal;

        /// <summary>
        /// Angle in degrees between the surface normal and <c>Vector3.up</c>.
        /// 0 = perfectly flat; 90 = vertical wall.
        /// </summary>
        public float SlopeDeg;

        /// <summary>
        /// Terrain alphamap (splat) weights at this position, one entry per alphamap layer.
        /// <c>null</c> for raycast samples or when the terrain has no splat layers.
        /// Used in Phase 4 for splat-mask painting; Phase 1 populates this but scatter does not
        /// yet consume it as a placement mask.
        /// </summary>
        public float[]? SplatWeights;
    }
}
