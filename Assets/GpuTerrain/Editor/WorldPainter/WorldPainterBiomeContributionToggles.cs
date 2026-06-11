#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Flags enum for per-stroke biome channel muting.
    /// A set flag means the channel is MUTED (omitted from the stamp dispatch).
    /// </summary>
    [Flags]
    public enum BiomeChannelMask
    {
        None   = 0,
        Height = 1 << 0,
        Splat  = 1 << 1,
        Grass  = 1 << 2,
        Props  = 1 << 3,
    }

    /// <summary>
    /// Toolbar of four ⛰/🎨/🌿/🌳 toggle buttons that mute biome channels per stroke.
    ///
    /// Muting a channel sets the corresponding <see cref="BiomeChannelMask"/> flag.
    /// The stamp dispatcher reads <see cref="CurrentMask"/> and omits muted channels —
    /// no per-payload branching, no GPU cost for muted channels.
    ///
    /// Hover shows a tooltip summarising what the channel contributes.
    ///
    /// Design §5.5 / Phase 5 task 4.
    /// </summary>
    internal sealed class WorldPainterBiomeContributionToggles
    {
        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Current mute mask.  A SET flag = channel MUTED.
        /// Defaults to <see cref="BiomeChannelMask.None"/> (all channels enabled).
        /// </summary>
        public BiomeChannelMask CurrentMask { get; private set; } = BiomeChannelMask.None;

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the four-toggle toolbar VisualElement.</summary>
        public VisualElement Build()
        {
            var row = new VisualElement();
            row.AddToClassList("wp-biome-toggles");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap      = Wrap.NoWrap;

            row.Add(this.MakeChannelToggle(BiomeChannelMask.Height, "⛰",
                "Height channel — sculpt delta per stamp"));
            row.Add(this.MakeChannelToggle(BiomeChannelMask.Splat,  "🎨",
                "Splat channel — paint texture weight per stamp"));
            row.Add(this.MakeChannelToggle(BiomeChannelMask.Grass,  "🌿",
                "Grass channel — paint density per stamp"));
            row.Add(this.MakeChannelToggle(BiomeChannelMask.Props,  "🌳",
                "Props channel — scatter prop instances per stamp"));

            return row;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private VisualElement MakeChannelToggle(BiomeChannelMask channel, string icon, string tip)
        {
            // Not-muted = button is "active" (background highlight).
            bool isMuted   = this.CurrentMask.HasFlag(channel);
            var toggle     = new Toggle { value = !isMuted, tooltip = tip };
            toggle.text    = icon;
            toggle.AddToClassList("wp-biome-channel-toggle");

            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue)
                    this.CurrentMask &= ~channel;   // un-mute
                else
                    this.CurrentMask |=  channel;   // mute
            });

            return toggle;
        }

        // ── Channel-contribution readout ──────────────────────────────────────

        /// <summary>
        /// Returns a one-line readout of what a specific channel contributes
        /// given the <paramref name="preset"/>. Used for hover tooltips in the card palette.
        /// </summary>
        public static string ChannelContribution(BiomePreset preset, BiomeChannelMask channel)
        {
            if (preset == null) return string.Empty;
            return channel switch
            {
                BiomeChannelMask.Height =>
                    preset.HeightEnabled
                        ? $"Height Δ {preset.HeightDeltaM:+0.##;-0.##}m @ {preset.HeightStrength:P0}"
                        : "(disabled)",
                BiomeChannelMask.Splat =>
                    preset.SplatEnabled
                        ? $"Splat layer {preset.SplatLayerIndex}, weight {preset.SplatWeight:P0}"
                        : "(disabled)",
                BiomeChannelMask.Grass =>
                    preset.GrassEnabled
                        ? $"Grass density {preset.GrassDensity:P0}" +
                          (preset.GrassLayer != null ? $" → {preset.GrassLayer.name}" : "")
                        : "(disabled)",
                BiomeChannelMask.Props =>
                    preset.PropEnabled
                        ? $"Props density {preset.PropDensity:P0}" +
                          (preset.PropLayer != null ? $" → {preset.PropLayer.name}" : "")
                        : "(disabled)",
                _ => string.Empty,
            };
        }
    }
}
