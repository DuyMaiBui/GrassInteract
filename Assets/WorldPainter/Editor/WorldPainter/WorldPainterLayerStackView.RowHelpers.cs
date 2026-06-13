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
    /// primitive row/toggle factory methods (<see cref="CreateBaseRow"/>, <see cref="MakeEyeToggle"/>,
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

        /// <summary>
        /// Selects the Height base row (index=0) or any legacy-indexed row. Clears ALL other
        /// selection state so <see cref="WorldPainterState.EffectiveLayerType"/> returns Height.
        /// </summary>
        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;

            // Clear unified surface-layer selection.
            WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);

            // Clear direct biome selection so EffectiveLayerType falls through to Height.
            WorldPainterState.ActiveBiomeIndex = -1;

            this.RefreshStack();
        }

        /// <summary>
        /// Selects a unified surface layer (<see cref="WorldPainterLayer"/>), routing the
        /// paint-kind via <see cref="WorldPainterState.SetActiveLayer"/> so the sculpt tool
        /// dispatches to the correct brush sub-path.
        ///
        /// For <see cref="GrassLayer"/>: selects the layer without a variant (kind = Meadow but
        /// variant index = -1). The user then clicks a variant sub-row to start painting.
        /// For <see cref="SplatLayer"/>: selects the layer without a channel (kind = Splat but
        /// channel = -1). The user then clicks an albedo-slot sub-row to start painting.
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

            // For splat: reset the active channel so the stack shows albedo-slot sub-rows but
            // no specific channel is active until the user clicks one.
            if (layer.Kind == WorldPainterLayer.LayerKind.Splat)
                WorldPainterState.ActiveSplatChannel = -1;

            // Clear the legacy index and biome selection.
            WorldPainterState.ActiveLayerIndex = -1;
            WorldPainterState.ActiveBiomeIndex = -1;

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

        /// <summary>
        /// Selects a Biome row at the given 0-based biome list index.
        /// Sets <see cref="WorldPainterState.ActiveBiomeIndex"/> directly so
        /// <see cref="WorldPainterState.ActiveBiomeLayerIndex"/> returns the correct value
        /// regardless of how many unified surface layers precede the biome rows in the stack.
        /// </summary>
        private void SelectBiomeLayer(int biomeIndex)
        {
            WorldPainterState.ActiveBiomeIndex = biomeIndex;
            // Keep ActiveLayerIndex at a sentinel so EffectiveLayerType returns Biome.
            // We use a large negative value so the legacy index arithmetic returns -1 for
            // all other layer types, and the biome brush dispatch uses ActiveBiomeIndex directly.
            WorldPainterState.ActiveLayerIndex = -(biomeIndex + 100);
            // Clear unified surface-layer selection.
            WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            this.RefreshStack();
        }

        // ── Biome row builder ─────────────────────────────────────────────────

        private VisualElement BuildBiomeRow(
            int displayIdx,
            string name,
            System.Action onSelect,
            System.Action onRemove,
            Texture2D? albedoPreview = null)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            // Biome row is "selected" when the active biome index matches the biome list index.
            // We detect selection via the display index being the active layer index (legacy path).
            if (displayIdx == WorldPainterState.ActiveLayerIndex)
                row.AddToClassList("wp-layer-row--selected");

            row.Add(this.MakeEyeToggle(null));
            row.Add(this.MakeLockToggle(false, null));

            var typeChip = new Label(LayerIcon(LayerType.Biome));
            typeChip.AddToClassList("wp-type-chip");
            row.Add(typeChip);

            if (albedoPreview != null)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("wp-splat-swatch");
                swatch.style.backgroundImage = new StyleBackground(albedoPreview);
                row.Add(swatch);
            }

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            row.Add(removeBtn);

            // Violet tint for biome rows.
            row.style.borderLeftColor = new StyleColor(new Color(0.6f, 0.2f, 0.9f, 0.9f));
            row.style.borderLeftWidth = new StyleFloat(3f);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button) return;
                onSelect();
            });
            return row;
        }

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
