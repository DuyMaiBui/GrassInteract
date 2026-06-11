#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Horizontal filter chip bar: All / Height / Splat / Grass / Props.
    /// Chips filter which rows are visible in <see cref="WorldPainterLayerStackView"/>.
    /// Design §4.1 (A3 hybrid filter chips shipped in P1).
    /// </summary>
    internal sealed class WorldPainterFilterChips
    {
        // ── Filter enum ───────────────────────────────────────────────────────

        public enum Filter
        {
            All,
            Height,
            Splat,
            Grass,
            Props,
        }

        // ── State ─────────────────────────────────────────────────────────────

        public Filter ActiveFilter { get; private set; } = Filter.All;

        /// <summary>Raised whenever the active filter changes.</summary>
        public event Action<Filter>? FilterChanged;

        // ── Chip definitions ──────────────────────────────────────────────────

        private static readonly (Filter filter, string label)[] Chips =
        {
            (Filter.All,    "All"),
            (Filter.Height, "⛰ Height"),
            (Filter.Splat,  "🎨 Splat"),
            (Filter.Grass,  "🌿 Grass"),
            (Filter.Props,  "🌳 Props"),
        };

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the chip bar VisualElement.</summary>
        public VisualElement Build()
        {
            var bar = new VisualElement();
            bar.AddToClassList("wp-filter-bar");

            var buttons = new List<Button>();

            foreach (var (filter, label) in Chips)
            {
                var chip = new Button();
                chip.text = label;
                chip.AddToClassList("wp-chip");

                var captured = filter;
                chip.clicked += () =>
                {
                    this.SetFilter(captured, buttons);
                };

                if (filter == Filter.All)
                {
                    chip.AddToClassList("wp-chip--active");
                }

                buttons.Add(chip);
                bar.Add(chip);
            }

            return bar;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetFilter(Filter filter, List<Button> buttons)
        {
            if (this.ActiveFilter == filter)
            {
                return;
            }

            this.ActiveFilter = filter;

            for (int i = 0; i < buttons.Count; i++)
            {
                var active = Chips[i].filter == filter;
                if (active)
                {
                    buttons[i].AddToClassList("wp-chip--active");
                }
                else
                {
                    buttons[i].RemoveFromClassList("wp-chip--active");
                }
            }

            this.FilterChanged?.Invoke(filter);
        }

        /// <summary>Returns true if the given layer type passes the current filter.</summary>
        public bool Passes(LayerType layerType)
        {
            return this.ActiveFilter switch
            {
                Filter.All    => true,
                Filter.Height => layerType == LayerType.Height,
                Filter.Splat  => layerType == LayerType.Splat,
                Filter.Grass  => layerType == LayerType.Grass,
                Filter.Props  => layerType == LayerType.Props,
                _             => true,
            };
        }
    }
}
