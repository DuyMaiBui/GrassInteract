#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Shared static authoring state for WorldPainter.
    /// Ports the <see cref="TerrainSculptState"/> pattern so the Inspector
    /// (<see cref="WorldPainterInspector"/>) and future EditorTool read from one SSOT.
    ///
    /// Not persisted — defaults reset on domain reload (acceptable for editor tools).
    /// </summary>
    public static class WorldPainterState
    {
        // ── Active painter ────────────────────────────────────────────────────

        /// <summary>WorldPainter component currently bound for authoring. Set by the Inspector.</summary>
        public static WorldPainter? ActivePainter { get; set; }

        // ── Brush-dirty notification ──────────────────────────────────────────

        /// <summary>
        /// Raised by the brush dock when the falloff curve changes so the active
        /// <see cref="WorldPainterSculptTool"/> can re-upload the 256×1 LUT to GPU
        /// without requiring a tool re-activation.
        /// </summary>
        public static event System.Action? BrushFalloffDirty;

        /// <summary>Invoke to notify the active sculpt tool that the falloff curve changed.</summary>
        public static void RaiseBrushFalloffDirty() => BrushFalloffDirty?.Invoke();

        // ── Active layer ──────────────────────────────────────────────────────

        /// <summary>
        /// Index into the layer stack of the currently selected (active) layer.
        /// -1 = no layer selected. The layer stack view reads/writes this.
        /// Stack order (display): 0 = Height (synthetic), 1..N = splat rows.
        /// </summary>
        public static int ActiveLayerIndex { get; set; } = -1;

        /// <summary>
        /// Returns the <see cref="LayerType"/> and splat channel index for the currently
        /// active layer.  The stack layout is: index 0 = Height (synthetic), indices 1..K
        /// = Splat rows (mapped to channel 0..K-1).
        /// </summary>
        /// <param name="painter">The active WorldPainter (needed for splatLayers.Count).</param>
        /// <param name="splatChannel">
        /// Output: 0-based splat channel [0..3] if the active layer is Splat; -1 otherwise.
        /// </param>
        /// <returns>The <see cref="LayerType"/> of the active layer.</returns>
        public static LayerType ActiveLayerType(WorldPainter painter, out int splatChannel)
        {
            splatChannel = -1;
            int idx = ActiveLayerIndex;
            if (idx <= 0) return LayerType.Height; // 0 or -1 = Height base

            // Splat rows occupy indices 1 .. splatLayers.Count
            int splatCount = painter.SplatLayers.Count;
            if (idx <= splatCount)
            {
                splatChannel = idx - 1; // map stack display index → channel 0-based
                return LayerType.Splat;
            }

            // Scatter (Grass/Props) rows follow splat rows.
            int scatterOffset = idx - splatCount - 1;
            if (scatterOffset >= 0 && scatterOffset < painter.ScatterLayers.Count)
            {
                var layer = painter.ScatterLayers[scatterOffset];
                string ln = layer != null ? layer.name.ToLowerInvariant() : string.Empty;
                return ln.Contains("prop") ? LayerType.Props : LayerType.Grass;
            }

            return LayerType.Height;
        }

        /// <summary>
        /// Returns the 0-based index into <see cref="WorldPainter.ScatterLayers"/> for the
        /// currently active layer, or -1 when the active layer is not a scatter layer.
        /// </summary>
        public static int ActiveScatterIndex(WorldPainter painter)
        {
            int idx = ActiveLayerIndex;
            int splatCount = painter.SplatLayers.Count;
            int scatterOffset = idx - splatCount - 1;
            if (scatterOffset >= 0 && scatterOffset < painter.ScatterLayers.Count)
                return scatterOffset;
            return -1;
        }

        // ── Brush settings (SSOT) ─────────────────────────────────────────────

        /// <summary>
        /// Shared brush settings for all layer types (size/strength/falloff/spacing/flow).
        /// The brush dock binds to this instance; stroke dispatch reads from it.
        /// </summary>
        public static BrushSettings Brush { get; } = BrushSettings.Default;

        // ── Stroke tracking ───────────────────────────────────────────────────

        /// <summary>
        /// Full set of tile coords touched by the last completed stroke.
        /// Replaced at the start of each new stroke; never null but may be empty.
        /// </summary>
        public static readonly HashSet<Vector2Int> LastStrokedTileSet = new();

        /// <summary>Coord of the last tile that received a stroke this session.</summary>
        public static Vector2Int? LastStrokedCoord { get; set; }

        // ── Reset helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Clear stroke tracking state atomically.
        /// Call whenever <see cref="ActivePainter"/> changes.
        /// </summary>
        public static void ResetLastStroked()
        {
            LastStrokedCoord = null;
            LastStrokedTileSet.Clear();
        }

        /// <summary>Reset everything to defaults (domain-reload equivalent).</summary>
        public static void Reset()
        {
            ActivePainter    = null;
            ActiveLayerIndex = -1;
            BrushFalloffDirty = null;
            ResetLastStroked();
        }
    }

    // ── BrushSettings ─────────────────────────────────────────────────────────

    /// <summary>
    /// Unified brush parameters shared across all layer types.
    /// Design §5.1 SSOT — one vocabulary, one set of controls.
    /// </summary>
    [System.Serializable]
    public sealed class BrushSettings
    {
        private const float DEFAULT_SIZE_M    = 12f;
        private const float DEFAULT_STRENGTH  = 0.4f;
        private const float DEFAULT_SPACING_M = 2f;
        private const float DEFAULT_FLOW      = 0.8f;

        [Tooltip("Brush radius in world-space metres.")]
        [UnityEngine.Range(0.5f, 256f)]
        public float size = DEFAULT_SIZE_M;

        [Tooltip("Brush strength / opacity [0..1].")]
        [UnityEngine.Range(0f, 1f)]
        public float strength = DEFAULT_STRENGTH;

        [Tooltip("Falloff curve: X=normalized distance from centre [0..1], Y=weight [0..1].")]
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Spacing between stamps along the drag path, in metres.")]
        [UnityEngine.Range(0.1f, 64f)]
        public float spacing = DEFAULT_SPACING_M;

        [Tooltip("Flow: accumulated deposit per stamp [0..1].")]
        [UnityEngine.Range(0f, 1f)]
        public float flow = DEFAULT_FLOW;

        /// <summary>Returns a <see cref="BrushSettings"/> instance initialised to smart defaults.</summary>
        public static BrushSettings Default => new BrushSettings();
    }
}
