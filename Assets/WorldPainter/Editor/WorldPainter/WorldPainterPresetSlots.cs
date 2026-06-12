#nullable enable
using UnityEditor;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Three F1/F2/F3 brush preset slots — save, recall, and X-swap.
    /// Persisted via <see cref="EditorPrefs"/> so presets survive domain reloads.
    /// Design §5.1 / Phase 6 task 8.
    /// </summary>
    internal static class WorldPainterPresetSlots
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const int SLOT_COUNT = 3;

        private const string SIZE_KEY     = "WP_Preset_Size_";
        private const string STRENGTH_KEY = "WP_Preset_Strength_";
        private const string SPACING_KEY  = "WP_Preset_Spacing_";
        private const string FLOW_KEY     = "WP_Preset_Flow_";
        private const string OCCUPIED_KEY = "WP_Preset_Occupied_";

        // ── Swap state (in-memory, resets on reload) ──────────────────────────

        private static BrushSnapshot? swapBuffer;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Saves the current brush settings into slot <paramref name="slotIndex"/>.</summary>
        public static void Save(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SLOT_COUNT) return;
            var b = WorldPainterState.Brush;
            EditorPrefs.SetFloat(SIZE_KEY     + slotIndex, b.size);
            EditorPrefs.SetFloat(STRENGTH_KEY + slotIndex, b.strength);
            EditorPrefs.SetFloat(SPACING_KEY  + slotIndex, b.spacing);
            EditorPrefs.SetFloat(FLOW_KEY     + slotIndex, b.flow);
            EditorPrefs.SetBool(OCCUPIED_KEY  + slotIndex, true);
        }

        /// <summary>Recalls slot <paramref name="slotIndex"/> into the current brush.</summary>
        public static void Recall(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SLOT_COUNT) return;
            if (!IsOccupied(slotIndex)) return;

            var b = WorldPainterState.Brush;
            b.size     = EditorPrefs.GetFloat(SIZE_KEY     + slotIndex, b.size);
            b.strength = EditorPrefs.GetFloat(STRENGTH_KEY + slotIndex, b.strength);
            b.spacing  = EditorPrefs.GetFloat(SPACING_KEY  + slotIndex, b.spacing);
            b.flow     = EditorPrefs.GetFloat(FLOW_KEY     + slotIndex, b.flow);
        }

        /// <summary>
        /// Swap: saves the current brush to the swap buffer and restores the last swap
        /// (or slot 0 if no swap exists yet).
        /// </summary>
        public static void Swap()
        {
            var b     = WorldPainterState.Brush;
            var prev  = swapBuffer;

            swapBuffer = new BrushSnapshot(b.size, b.strength, b.spacing, b.flow);

            if (prev != null)
            {
                b.size     = prev.Size;
                b.strength = prev.Strength;
                b.spacing  = prev.Spacing;
                b.flow     = prev.Flow;
            }
            else
            {
                // No buffer: swap with slot 0 if occupied.
                Recall(0);
            }
        }

        /// <summary>Returns true when slot <paramref name="slotIndex"/> has a saved preset.</summary>
        public static bool IsOccupied(int slotIndex) =>
            slotIndex >= 0 && slotIndex < SLOT_COUNT &&
            EditorPrefs.GetBool(OCCUPIED_KEY + slotIndex, false);

        /// <summary>Clears the saved preset in slot <paramref name="slotIndex"/>.</summary>
        public static void Clear(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SLOT_COUNT) return;
            EditorPrefs.DeleteKey(SIZE_KEY     + slotIndex);
            EditorPrefs.DeleteKey(STRENGTH_KEY + slotIndex);
            EditorPrefs.DeleteKey(SPACING_KEY  + slotIndex);
            EditorPrefs.DeleteKey(FLOW_KEY     + slotIndex);
            EditorPrefs.DeleteKey(OCCUPIED_KEY + slotIndex);
        }

        // ── Inner type ────────────────────────────────────────────────────────

        private sealed class BrushSnapshot
        {
            public readonly float Size;
            public readonly float Strength;
            public readonly float Spacing;
            public readonly float Flow;

            public BrushSnapshot(float size, float strength, float spacing, float flow)
            {
                this.Size     = size;
                this.Strength = strength;
                this.Spacing  = spacing;
                this.Flow     = flow;
            }
        }
    }
}
