#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Constant brush dock always visible below the layer stack.
    /// Binds size / strength / falloff CurveField / spacing / flow to the
    /// one <see cref="BrushSettings"/> SSOT on <see cref="WorldPainterState"/>.
    /// Position is fixed across layer selection — only the layer stack changes.
    /// Design §4.4 §5.1.
    /// </summary>
    internal sealed class WorldPainterBrushDock
    {
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

            return dock;
        }

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
                    // Batch 2: re-upload 256×1 RFloat LUT to compute on change.
                }
            });
            container.AddToClassList("wp-curve-container");

            row.Add(container);
            return row;
        }
    }
}
