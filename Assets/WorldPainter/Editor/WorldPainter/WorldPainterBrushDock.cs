#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Constant brush dock always visible below the layer stack.
    /// Binds size / strength / falloff CurveField / spacing / flow to the
    /// one <see cref="BrushSettings"/> SSOT on <see cref="WorldPainterState"/>.
    /// Position is fixed across layer selection — only the layer stack changes.
    ///
    /// P6 additions (task 8):
    ///   - 72×92 stamp-grid with 2px blue selected border.
    ///   - F1–F3 preset slots bar + X=swap shortcut label.
    ///
    /// Design §4.4 §5.1.
    /// </summary>
    internal sealed class WorldPainterBrushDock
    {
        // ── Stamp shapes ──────────────────────────────────────────────────────

        private static readonly string[] STAMP_NAMES =
        {
            "Circle",
            "Square",
            "Soft",
        };

        private int selectedStampIndex;

        // ── Preset slot labels ────────────────────────────────────────────────

        private Label[] presetLabels = new Label[WorldPainterPresetSlots.SLOT_COUNT];

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the brush dock VisualElement.</summary>
        public VisualElement Build()
        {
            var dock = new VisualElement();
            dock.AddToClassList("wp-brush-dock");

            var title = new Label("BRUSH");
            title.AddToClassList("wp-section-title");
            dock.Add(title);

            var brush = WorldPainterState.Brush;

            // Size
            dock.Add(this.BuildSlider("Size (m)", 0.5f, 256f,
                () => brush.size,
                v  => brush.size = v));

            // Strength
            dock.Add(this.BuildSlider("Strength", 0f, 1f,
                () => brush.strength,
                v  => brush.strength = v));

            // Falloff CurveField
            dock.Add(this.BuildCurveField(brush));

            // Spacing
            dock.Add(this.BuildSlider("Spacing (m)", 0.1f, 64f,
                () => brush.spacing,
                v  => brush.spacing = v));

            // Flow
            dock.Add(this.BuildSlider("Flow", 0f, 1f,
                () => brush.flow,
                v  => brush.flow = v));

            // P6: stamp grid
            dock.Add(this.BuildStampGrid());

            // P6: preset slots bar
            dock.Add(this.BuildPresetBar());

            return dock;
        }

        // ── Stamp grid ────────────────────────────────────────────────────────

        private VisualElement BuildStampGrid()
        {
            var grid = new VisualElement();
            grid.AddToClassList("wp-stamp-grid");

            for (int i = 0; i < STAMP_NAMES.Length; i++)
            {
                int capturedIdx = i;
                var cell = new VisualElement();
                cell.AddToClassList("wp-stamp-cell");
                if (i == this.selectedStampIndex)
                    cell.AddToClassList("wp-stamp-cell--selected");

                var lbl = new Label(STAMP_NAMES[i]);
                lbl.style.fontSize  = 10;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                cell.Add(lbl);

                cell.RegisterCallback<ClickEvent>(_ =>
                {
                    this.selectedStampIndex = capturedIdx;
                    this.RefreshStampSelection(grid);
                });

                grid.Add(cell);
            }

            return grid;
        }

        private void RefreshStampSelection(VisualElement grid)
        {
            for (int i = 0; i < grid.childCount; i++)
            {
                var cell = grid[i];
                if (i == this.selectedStampIndex)
                    cell.AddToClassList("wp-stamp-cell--selected");
                else
                    cell.RemoveFromClassList("wp-stamp-cell--selected");
            }
        }

        // ── Preset bar ────────────────────────────────────────────────────────

        private VisualElement BuildPresetBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("wp-preset-bar");

            for (int i = 0; i < WorldPainterPresetSlots.SLOT_COUNT; i++)
            {
                int capturedIdx = i;
                var slot = new VisualElement();
                slot.AddToClassList("wp-preset-slot");
                if (WorldPainterPresetSlots.IsOccupied(i))
                    slot.AddToClassList("wp-preset-slot--occupied");

                var label = new Label(this.PresetSlotLabel(i));
                label.style.fontSize = 10;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                slot.Add(label);
                this.presetLabels[i] = label;

                slot.RegisterCallback<ClickEvent>(ev =>
                {
                    if (ev.shiftKey)
                    {
                        WorldPainterPresetSlots.Save(capturedIdx);
                        this.RefreshPresetLabels(bar);
                    }
                    else
                    {
                        WorldPainterPresetSlots.Recall(capturedIdx);
                    }
                });

                slot.tooltip = $"F{i + 1}: Recall  |  Shift+Click: Save";
                bar.Add(slot);
            }

            // Swap label hint.
            var swapHint = new Label("X=swap");
            swapHint.style.fontSize = 9;
            swapHint.style.marginLeft = 4;
            swapHint.style.alignSelf  = Align.Center;
            bar.Add(swapHint);

            return bar;
        }

        private void RefreshPresetLabels(VisualElement bar)
        {
            for (int i = 0; i < bar.childCount - 1 && i < WorldPainterPresetSlots.SLOT_COUNT; i++)
            {
                var slot = bar[i];
                if (WorldPainterPresetSlots.IsOccupied(i))
                    slot.AddToClassList("wp-preset-slot--occupied");
                else
                    slot.RemoveFromClassList("wp-preset-slot--occupied");

                var lbl = slot.Q<Label>();
                if (lbl != null) lbl.text = this.PresetSlotLabel(i);
            }
        }

        private string PresetSlotLabel(int slotIdx) =>
            WorldPainterPresetSlots.IsOccupied(slotIdx)
                ? $"F{slotIdx + 1}*"
                : $"F{slotIdx + 1}";

        // ── Field builders ────────────────────────────────────────────────────

        private VisualElement BuildSlider(
            string label,
            float min,
            float max,
            System.Func<float> getter,
            System.Action<float> setter)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-field-row");

            var slider = new Slider(label, min, max)
            {
                value = getter(),
            };
            slider.AddToClassList("wp-brush-slider");
            slider.RegisterValueChangedCallback(e => setter(e.newValue));

            row.Add(slider);
            return row;
        }

        private VisualElement BuildCurveField(BrushSettings brush)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-field-row");

            // CurveField is an IMGUI-backed control; wrap in an IMGUIContainer.
            var container = new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                var newCurve = EditorGUILayout.CurveField("Falloff", brush.falloff);
                if (EditorGUI.EndChangeCheck())
                {
                    brush.falloff = newCurve;
                    // Notify the active sculpt tool to re-upload the 256×1 LUT.
                    WorldPainterState.RaiseBrushFalloffDirty();
                }
            });
            container.AddToClassList("wp-curve-container");

            row.Add(container);
            return row;
        }
    }
}
