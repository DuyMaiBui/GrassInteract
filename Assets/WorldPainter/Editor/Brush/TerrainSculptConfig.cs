#nullable enable
namespace GpuTerrain.Editor
{
    /// <summary>
    /// Named constants for the sculpt + paint editor tool (Phase 5).
    /// All magic numbers in the window, controller, writeback, and undo live here.
    /// </summary>
    public static class TerrainSculptConfig
    {
        // ── Undo ──────────────────────────────────────────────────────────────

        /// <summary>Maximum per-tile snapshot depth. Oldest entry evicted when exceeded.</summary>
        public const int UNDO_DEPTH = 32;

        // ── Brush render texture ─────────────────────────────────────────────

        /// <summary>
        /// Resolution of the per-tile working RenderTexture (height + splat) in pixels per edge.
        /// Sized to match Phase 0 DEFAULT_HEIGHT_RES (257) rounded up to 512 for GPU alignment.
        /// </summary>
        public const int BRUSH_RT_RES = 512;

        // ── Default brush parameters ─────────────────────────────────────────

        /// <summary>Default brush radius in world metres.</summary>
        public const float DEFAULT_BRUSH_SIZE_M = 16f;

        /// <summary>
        /// Default sculpt strength [0,1]. 1 = full raise/lower per stroke sample.
        /// Kept gentle to avoid accidental large edits on first use.
        /// </summary>
        public const float DEFAULT_BRUSH_STRENGTH = 0.05f;

        /// <summary>Default smooth kernel radius in texels for the Smooth kernel.</summary>
        public const int DEFAULT_SMOOTH_RADIUS = 3;

        // ── Stroke throttle ──────────────────────────────────────────────────

        /// <summary>
        /// Minimum world-space distance the mouse must travel between brush dispatches.
        /// Prevents per-pixel dispatch storms during fast drags.
        /// </summary>
        public const float STROKE_STEP_M = 4f;

        // ── Splat ────────────────────────────────────────────────────────────

        /// <summary>Maximum number of paint layers supported (matches Phase 2 cap).</summary>
        public const int MAX_SPLAT_LAYERS = 4;

        // ── Compute kernel names ─────────────────────────────────────────────

        public const string KERNEL_RAISE_LOWER  = "RaiseLower";
        public const string KERNEL_SMOOTH        = "Smooth";
        public const string KERNEL_FLATTEN        = "Flatten";
        public const string KERNEL_PAINT_SPLAT   = "PaintSplat";
        public const string KERNEL_PAINT_DENSITY = "PaintDensity";

        // ── Compute thread group size ────────────────────────────────────────

        /// <summary>Thread group size matching the compute shader [numthreads(8,8,1)].</summary>
        public const int THREAD_GROUP_SIZE = 8;
    }
}
