#nullable enable

namespace GpuTerrain
{
    /// <summary>
    /// Named constants for the Phase 4 heightfield collider streaming system.
    /// All ring / cap / cook-budget values live here — NO inline literals.
    /// </summary>
    public static class TerrainColliderConfig
    {
        // ── Collider ring ─────────────────────────────────────────────────────

        /// <summary>
        /// Chebyshev half-radius (in tile units) for the near-tile collider ring.
        /// Must be ≤ <see cref="TerrainStreamingConfig.RING_RADIUS"/> so colliders only cover
        /// resident tiles.
        /// COLLIDER_RING_RADIUS=1 → 3×3 = 9 tiles with active colliders around the camera.
        /// </summary>
        public const int COLLIDER_RING_RADIUS = 1;

        // ── Cook amortisation ─────────────────────────────────────────────────

        /// <summary>
        /// Maximum heightfield colliders cooked (built) per player-loop tick.
        /// Amortises CPU cook cost across frames; avoids a frame spike when the camera
        /// crosses a tile boundary and multiple colliders need building at once.
        /// </summary>
        public const int MAX_COOKS_PER_FRAME = 1;

        // ── Heightfield resolution ────────────────────────────────────────────

        /// <summary>
        /// Heightfield resolution per tile edge fed to <c>TerrainData</c>.
        /// Lower than R16 source resolution to reduce cook time and memory.
        /// Must be (2^n + 1) for Unity's HeightmapResolution constraint.
        /// 65 = 2^6 + 1; covers 256 m / 64 cells = 4 m/cell resolution.
        /// </summary>
        public const int HEIGHTFIELD_RES = 65;
    }
}
