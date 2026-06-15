#nullable enable

namespace WorldPainter
{
    /// <summary>
    /// Named constants for the heightfield collider streaming system.
    ///
    /// Collider RANGE is no longer a fixed ring radius — it derives from a WorldPainter LOD band
    /// (see <see cref="TerrainColliderStreamer"/> + <see cref="TerrainColliderRing"/>). Heightfield
    /// RESOLUTION is a per-component setting on the streamer. Only the cook-budget constant lives here.
    /// </summary>
    public static class TerrainColliderConfig
    {
        // ── Cook amortisation ─────────────────────────────────────────────────

        /// <summary>
        /// Maximum heightfield colliders cooked (built) per player-loop tick.
        /// Amortises CPU cook cost across frames; avoids a frame spike when the camera
        /// crosses a tile boundary and multiple colliders need building at once.
        /// </summary>
        public const int MAX_COOKS_PER_FRAME = 1;
    }
}
