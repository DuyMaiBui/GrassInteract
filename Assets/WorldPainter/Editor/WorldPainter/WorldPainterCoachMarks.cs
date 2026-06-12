#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Coach marks, empty states, and the "?" cheat-sheet popover.
    ///
    /// Empty states:
    ///   - No tiles registered → "Create 1×1 tile" button (calls TerrainValidationSceneBuilder)
    ///   - Empty layer stack → "Add your first layer"
    ///
    /// Per-layer coach marks: shown once per layer type per user via EditorPrefs gate.
    ///
    /// Cheat-sheet: "?" header button opens a fixed popover with keyboard shortcuts.
    ///
    /// Design §4.7 / Phase 6 task 7.
    /// </summary>
    internal sealed class WorldPainterCoachMarks
    {
        // ── EditorPrefs keys ─────────────────────────────────────────────────

        private const string KEY_SEEN_GRASS  = "WP_CoachMark_GrassLayer_Seen";
        private const string KEY_SEEN_SPLAT  = "WP_CoachMark_SplatLayer_Seen";
        private const string KEY_SEEN_PROPS  = "WP_CoachMark_PropsLayer_Seen";
        private const string KEY_SEEN_BIOME  = "WP_CoachMark_BiomeLayer_Seen";

        // ── State ─────────────────────────────────────────────────────────────

        private VisualElement? markContainer;
        private LayerType lastMarkedType = LayerType.Height;

        // ── Build — no-tile empty state ────────────────────────────────────

        /// <summary>
        /// Returns an empty-state element shown when the painter has no tiles.
        /// Directs the user to assign a WorldMapAsset to begin painting.
        /// (P4 factory will provide tile creation tooling.)
        /// </summary>
        public VisualElement BuildNoTilesEmptyState()
        {
            var container = new VisualElement();
            container.AddToClassList("wp-coach-mark");

            var icon = new Label("i");
            icon.AddToClassList("wp-coach-mark-icon");
            container.Add(icon);

            var textCol = new VisualElement();
            textCol.style.flexDirection = FlexDirection.Column;

            var msg = new Label("No tiles loaded. Assign a WorldMapAsset to begin painting.");
            msg.AddToClassList("wp-coach-mark-text");
            textCol.Add(msg);

            container.Add(textCol);
            return container;
        }

        /// <summary>
        /// Returns an empty-state element shown when the layer stack is empty.
        /// </summary>
        public VisualElement BuildEmptyStackState()
        {
            var container = new VisualElement();
            container.AddToClassList("wp-coach-mark");

            var icon = new Label("i");
            icon.AddToClassList("wp-coach-mark-icon");
            container.Add(icon);

            var msg = new Label("Add your first layer using the + button above.");
            msg.AddToClassList("wp-coach-mark-text");
            container.Add(msg);

            return container;
        }

        // ── Build — reactive coach-mark area ──────────────────────────────────

        /// <summary>
        /// Returns a container that shows a type-specific coach mark the FIRST time
        /// the user selects a layer of that type. EditorPrefs-gated so it shows once.
        /// </summary>
        public VisualElement BuildLayerCoachArea(WorldPainter painter)
        {
            this.markContainer = new VisualElement();
            this.markContainer.schedule.Execute(() => this.RefreshCoachMark(painter)).Every(300);
            return this.markContainer;
        }

        // ── Build — "?" cheat-sheet button ────────────────────────────────────

        /// <summary>Builds the "?" header button that opens the cheat-sheet popover.</summary>
        public VisualElement BuildCheatSheetButton()
        {
            var btn = new Button(this.ShowCheatSheet) { text = "?" };
            btn.tooltip = "Show WorldPainter keyboard shortcuts";
            btn.style.width  = 18;
            btn.style.height = 18;
            btn.style.fontSize = 11;
            return btn;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void RefreshCoachMark(WorldPainter painter)
        {
            if (this.markContainer == null) return;

            LayerType layerType = WorldPainterState.ActiveLayerType(painter, out _);
            if (layerType == this.lastMarkedType) return;

            this.lastMarkedType = layerType;
            this.markContainer.Clear();

            string? prefKey = layerType switch
            {
                LayerType.Grass => KEY_SEEN_GRASS,
                LayerType.Splat => KEY_SEEN_SPLAT,
                LayerType.Props => KEY_SEEN_PROPS,
                LayerType.Biome => KEY_SEEN_BIOME,
                _               => null,
            };

            if (prefKey == null || EditorPrefs.GetBool(prefKey, false)) return;

            string tip = layerType switch
            {
                LayerType.Grass => "Paint grass density by dragging. Shift=erase, Ctrl=smooth.",
                LayerType.Splat => "Paint surface blend weights. Shift=invert, F1-F3=preset slots.",
                LayerType.Props => "Stamp props by clicking. Alt=eyedropper to sample from scene.",
                LayerType.Biome => "Biome brush writes multiple channels at once. Use channel toggles.",
                _               => string.Empty,
            };

            var mark = new VisualElement();
            mark.AddToClassList("wp-coach-mark");

            var icon = new Label("i");
            icon.AddToClassList("wp-coach-mark-icon");
            mark.Add(icon);

            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;

            var txt = new Label(tip);
            txt.AddToClassList("wp-coach-mark-text");
            col.Add(txt);

            var dismiss = new Button(() =>
            {
                EditorPrefs.SetBool(prefKey, true);
                this.markContainer.Clear();
            }) { text = "Got it" };
            dismiss.style.marginTop   = 3;
            dismiss.style.alignSelf   = Align.FlexStart;
            col.Add(dismiss);

            mark.Add(col);
            this.markContainer.Add(mark);

            // Mark as seen immediately so it shows exactly once.
            EditorPrefs.SetBool(prefKey, true);
        }

        private void ShowCheatSheet()
        {
            const string text =
                "WorldPainter Shortcuts\n\n" +
                "Drag          Paint\n" +
                "Shift+Drag    Inverse / Erase\n" +
                "Ctrl+Drag     Smooth\n" +
                "Alt+Click     Eyedropper (sample layer)\n" +
                "Mod+H-Drag    Resize brush\n" +
                "Mod+V-Drag    Adjust strength\n" +
                "F1 / F2 / F3  Recall preset slot\n" +
                "X             Swap brush preset\n";

            EditorUtility.DisplayDialog("WorldPainter Shortcuts", text, "Close");
        }
    }
}
