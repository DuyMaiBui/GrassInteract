#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Enhanced scene-view input for WorldPainter sculpt:
    ///   - Radial scrub: hold modifier key + drag  (H = brush size, V = strength)
    ///   - Shift = inverse mode
    ///   - Ctrl  = smooth mode
    ///   - F1–F3 = recall brush preset slots
    ///   - X     = swap brush preset slot (save/recall)
    ///
    /// Attach to the active <see cref="WorldPainterSculptTool"/> via
    /// <see cref="HandleInput"/> called from the tool's OnToolGUI.
    /// Design §4.5 / §5.5 / Phase 6 task 6.
    /// </summary>
    internal sealed class WorldPainterSceneInput
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float SIZE_DRAG_SCALE     = 0.5f;
        private const float STRENGTH_DRAG_SCALE = 0.002f;

        // ── State ─────────────────────────────────────────────────────────────

        private bool  isScrubbing;
        private Vector2 scrubOrigin;
        private float   scrubStartSize;
        private float   scrubStartStrength;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Process scene-view event for WorldPainter advanced input.
        /// Call from the active EditorTool's OnToolGUI / OnSceneGUI.
        /// Returns true if the event was consumed and should NOT propagate further.
        /// </summary>
        public bool HandleInput(Event e, WorldPainter painter)
        {
            // Determine active modifiers.
            bool altKey   = e.alt;
            bool ctrlKey  = e.control || e.command;
            bool shiftKey = e.shift;
            bool modKey   = altKey || ctrlKey; // general "modifier held" for scrub

            // Radial scrub: modifier + mouse drag
            if (e.type == EventType.MouseDown && e.button == 0 && modKey && !altKey)
            {
                this.isScrubbing       = true;
                this.scrubOrigin       = e.mousePosition;
                this.scrubStartSize    = WorldPainterState.Brush.size;
                this.scrubStartStrength = WorldPainterState.Brush.strength;
                e.Use();
                return true;
            }

            if (this.isScrubbing && e.type == EventType.MouseDrag)
            {
                Vector2 delta = e.mousePosition - this.scrubOrigin;
                WorldPainterState.Brush.size = Mathf.Clamp(
                    this.scrubStartSize + delta.x * SIZE_DRAG_SCALE, 0.5f, 256f);
                WorldPainterState.Brush.strength = Mathf.Clamp01(
                    this.scrubStartStrength + delta.y * STRENGTH_DRAG_SCALE);
                e.Use();
                return true;
            }

            if (this.isScrubbing && (e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow))
            {
                this.isScrubbing = false;
                if (e.type == EventType.MouseUp) { e.Use(); return true; }
            }

            // Shift = inverse (invert strength sign flag, communicated via state).
            if (shiftKey && !ctrlKey)
            {
                // Inverse mode: handled by caller checking this helper.
                // No event consumed — let the tool also see shift.
            }

            // Ctrl = smooth mode.
            if (ctrlKey && !altKey)
            {
                // Smooth mode: communicated via IsSmoothing property below.
            }

            // Symmetry mirror placeholder (no consume — future full impl).
            // F1–F3: recall presets.
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.F1) { WorldPainterPresetSlots.Recall(0); e.Use(); return true; }
                if (e.keyCode == KeyCode.F2) { WorldPainterPresetSlots.Recall(1); e.Use(); return true; }
                if (e.keyCode == KeyCode.F3) { WorldPainterPresetSlots.Recall(2); e.Use(); return true; }
                if (e.keyCode == KeyCode.X)  { WorldPainterPresetSlots.Swap();    e.Use(); return true; }
            }

            return false;
        }

        /// <summary>True when Ctrl is held (smooth mode active).</summary>
        public static bool IsSmoothing => Event.current?.control ?? false;

        /// <summary>True when Shift is held (inverse mode active).</summary>
        public static bool IsInverse => Event.current?.shift ?? false;
    }
}
