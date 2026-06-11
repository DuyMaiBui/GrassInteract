#nullable enable

namespace GpuTerrain
{
    /// <summary>
    /// Named constants for the Phase 3 tile streaming system.
    /// All ring / cap / amortisation / hysteresis values live here — NO inline literals.
    /// </summary>
    public static class TerrainStreamingConfig
    {
        // ── Residency ring ────────────────────────────────────────────────────

        /// <summary>
        /// Chebyshev (Chessboard) ring half-radius in tile units.
        /// RING_RADIUS=2 → 5×5 = 25 tiles resident around the camera tile.
        /// Increase for larger view range (memory cost: O(radius²)).
        /// </summary>
        public const int RING_RADIUS = 2;

        /// <summary>
        /// Maximum tiles that may be simultaneously resident in GPU memory.
        /// Hard cap: eviction fires before a new load when this is reached.
        /// (2*RING_RADIUS+1)² = 25; headroom for one frame of overlap.
        /// </summary>
        public const int MAX_RESIDENT_TILES = 30;

        // ── Per-frame upload budget ───────────────────────────────────────────

        /// <summary>
        /// Maximum GPU texture uploads per player-loop tick.
        /// Amortises CPU→GPU upload cost across frames so tile crossings don't hitch.
        /// Mirrors maxCollidersPerFrame in InstanceVisibilityColliderDriver.
        /// </summary>
        public const int MAX_UPLOADS_PER_FRAME = 2;

        // ── Hysteresis ────────────────────────────────────────────────────────

        /// <summary>
        /// Extra tile-units of margin before a tile is evicted.
        /// A tile at ring edge [RING_RADIUS + HYSTERESIS_TILES] is still kept.
        /// Prevents load/evict thrash when the camera straddles a tile boundary.
        /// </summary>
        public const int HYSTERESIS_TILES = 1;
    }
}
