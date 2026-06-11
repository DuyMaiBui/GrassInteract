#nullable enable
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Shared static state for terrain sculpt/paint brush settings.
    /// Mirrors the ScatterAuthoringState pattern so the Inspector
    /// (<see cref="GpuTerrainRendererEditor"/>) and the EditorTool
    /// (<see cref="TerrainSculptTool"/>) read from one SSOT.
    ///
    /// Not persisted — defaults reset on domain reload (acceptable for editor tools).
    /// </summary>
    public static class TerrainSculptState
    {
        // ── Active renderer ───────────────────────────────────────────────────

        /// <summary>Renderer currently bound for sculpting. Set by the Inspector.</summary>
        public static GpuTerrainRenderer? ActiveRenderer { get; set; }

        /// <summary>
        /// Coord of the last tile that received a brush stroke this session.
        /// Used by Undo/Save in the renderer inspector to target the right tile.
        /// Cleared when ActiveRenderer changes so stale coord from a previous renderer
        /// does not gate the Save/Undo buttons on a newly selected renderer.
        /// </summary>
        public static Vector2Int? LastStrokedCoord { get; set; }

        // ── Shared undo stack (SSOT) ──────────────────────────────────────────

        // Lazy-init: no allocation cost when the tool is never activated.
        private static TerrainSculptUndo? undoInstance;

        /// <summary>
        /// Single shared undo stack used by both <see cref="TerrainSculptTool"/> and
        /// <see cref="GpuTerrainRendererEditor"/>. Neither constructs its own instance.
        /// </summary>
        public static TerrainSculptUndo Undo => undoInstance ??= new TerrainSculptUndo();

        // ── Mode ──────────────────────────────────────────────────────────────

        /// <summary>True = paint splat layers; false = sculpt height.</summary>
        public static bool PaintMode { get; set; } = false;

        /// <summary>Current sculpt sub-mode (Raise/Lower/Smooth/Flatten).</summary>
        public static SculptMode SculptSubMode { get; set; } = SculptMode.Raise;

        // ── Brush parameters ──────────────────────────────────────────────────

        public static float BrushSize     { get; set; } = TerrainSculptConfig.DEFAULT_BRUSH_SIZE_M;
        public static float BrushStrength { get; set; } = TerrainSculptConfig.DEFAULT_BRUSH_STRENGTH;
        public static float FlattenTarget { get; set; } = 0.5f;
        public static int   SplatLayer    { get; set; } = 0;

        // ── Derived ───────────────────────────────────────────────────────────

        /// <summary>Effective SculptMode for the controller (paint maps to SculptMode.Paint).</summary>
        public static SculptMode EffectiveMode =>
            PaintMode ? SculptMode.Paint : SculptSubMode;

        // ── Colour per mode (used by brush preview) ───────────────────────────

        /// <summary>
        /// Returns the world-space brush preview colour for the current effective mode.
        /// Raise=green, Lower=red, Smooth=cyan, Flatten=yellow, Paint=orange.
        /// </summary>
        public static Color BrushColor() => ModeColor(EffectiveMode);

        /// <summary>Returns the brush preview colour for a given sculpt mode.</summary>
        public static Color ModeColor(SculptMode mode)
        {
            return mode switch
            {
                SculptMode.Raise   => new Color(0.2f, 0.9f, 0.2f, 0.7f),
                SculptMode.Lower   => new Color(0.9f, 0.2f, 0.2f, 0.7f),
                SculptMode.Smooth  => new Color(0.2f, 0.8f, 0.9f, 0.7f),
                SculptMode.Flatten => new Color(0.9f, 0.9f, 0.2f, 0.7f),
                SculptMode.Paint   => new Color(0.9f, 0.5f, 0.1f, 0.7f),
                _                  => Color.white,
            };
        }
    }
}
