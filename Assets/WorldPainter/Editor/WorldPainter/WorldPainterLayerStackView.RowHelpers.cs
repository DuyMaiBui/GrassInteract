#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Row-helper half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Contains: selection logic (<see cref="SelectLayer"/>), primitive row/toggle
    /// factory methods (<see cref="CreateBaseRow"/>, <see cref="MakeEyeToggle"/>,
    /// <see cref="MakeLockToggle"/>, <see cref="LayerIcon"/>), and the no-op
    /// biome-row redirect (<see cref="AddBiomeRow"/>).
    ///
    /// These helpers are called from both the view-build half
    /// (<c>WorldPainterLayerStackView.cs</c>) and the mutations half
    /// (<c>WorldPainterLayerStackView.Mutations.cs</c>).
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Layer selection ───────────────────────────────────────────────────

        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;
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

        private Toggle MakeEyeToggle(System.Action<bool>? onChange)
        {
            var eye = new Toggle { value = true };
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
            LayerType.Biome  => "🌎",
            _                => "?",
        };

        // ── Biome row redirect ────────────────────────────────────────────────

        private void AddBiomeRow()
        {
            // Biome layers are added via the BiomePaletteView (+) button.
            // From the layer stack "+" menu we just select the palette tab if available.
            // Fallback: no-op since the BiomePaletteView handles its own add flow.
            Debug.Log("[WorldPainter] Use the Biomes palette panel to add a biome preset.");
        }
    }
}
