#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Row-helper half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Contains: selection logic (<see cref="SelectLayer"/>, <see cref="SelectSurfaceLayer"/>),
    /// and the primitive row/toggle factory methods (<see cref="CreateBaseRow"/>,
    /// <see cref="MakeEyeToggle"/>, <see cref="MakeLockToggle"/>, <see cref="LayerIcon"/>).
    ///
    /// These helpers are called from both the view-build half
    /// (<c>WorldPainterLayerStackView.cs</c>) and the mutations half
    /// (<c>WorldPainterLayerStackView.Mutations.cs</c>).
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Layer selection ───────────────────────────────────────────────────

        /// <summary>
        /// Selects the Height base row (index=0) or any legacy-indexed row. Clears unified
        /// surface-layer selection so <see cref="WorldPainterState.EffectiveLayerType"/> returns Height.
        /// </summary>
        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;

            // Clear unified surface-layer selection.
            WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);

            this.RefreshStack();
        }

        /// <summary>
        /// Selects a unified surface layer (<see cref="WorldPainterLayer"/>), routing the
        /// paint-kind via <see cref="WorldPainterState.SetActiveLayer"/> so the sculpt tool
        /// dispatches to the correct brush sub-path.
        /// </summary>
        private void SelectSurfaceLayer(WorldPainterLayer layer)
        {
            WorldPainterState.PaintLayerKind kind = layer.Kind switch
            {
                WorldPainterLayer.LayerKind.Splat => WorldPainterState.PaintLayerKind.Splat,
                WorldPainterLayer.LayerKind.Grass => WorldPainterState.PaintLayerKind.Meadow,
                WorldPainterLayer.LayerKind.Prop  => WorldPainterState.PaintLayerKind.Prop,
                _ => WorldPainterState.PaintLayerKind.None,
            };

            // Clear the legacy index so the synthetic Height row no longer reads as selected
            // (CreateBaseRow highlights on displayIdx == ActiveLayerIndex).
            WorldPainterState.ActiveLayerIndex = -1;

            WorldPainterState.SetActiveLayer(layer.name, kind);
            this.RefreshStack();
        }

        // ── Row / toggle factory methods ──────────────────────────────────────

        private VisualElement CreateBaseRow(int displayIdx)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            if (displayIdx == WorldPainterState.ActiveLayerIndex)
                row.AddToClassList("wp-layer-row--selected");
            return row;
        }

        /// <summary>
        /// Eye (visibility) toggle. <paramref name="value"/> seeds the initial state;
        /// <paramref name="onChange"/> (when non-null) fires with the new value on user toggle.
        /// </summary>
        private Toggle MakeEyeToggle(bool value, System.Action<bool>? onChange)
        {
            var eye = new Toggle { value = value, tooltip = "Show / hide layer" };
            eye.AddToClassList("wp-eye-toggle");
            if (onChange != null)
                eye.RegisterValueChangedCallback(e => onChange(e.newValue));
            return eye;
        }

        private Toggle MakeLockToggle(bool locked, System.Action<bool>? onChange)
        {
            var lockBtn = new Toggle { value = !locked, tooltip = "Lock layer" };
            lockBtn.AddToClassList("wp-lock-toggle");
            if (onChange != null)
                lockBtn.RegisterValueChangedCallback(e => onChange(!e.newValue));
            return lockBtn;
        }

        private static string LayerIcon(LayerType t) => t switch
        {
            LayerType.Height => "⛰",
            LayerType.Splat  => "🎨",
            LayerType.Grass  => "🌿",
            LayerType.Props  => "🌳",
            _                => "?",
        };
    }
}
