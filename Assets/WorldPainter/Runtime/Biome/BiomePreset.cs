#nullable enable
using System;
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    /// <summary>
    /// Tier-C ScriptableObject defining a composite terrain biome:
    /// four paint channels bundled into one asset.
    ///
    /// Disk location: Assets/Worlds/&lt;WorldName&gt;/Biomes/&lt;name&gt;.asset
    ///
    /// Each channel is optional — null reference = channel not painted.
    /// The biome stamp fan-out (<see cref="WorldPainter.Editor.WorldPainterBiomeStamp"/>)
    /// reads these rules and dispatches the appropriate kernel per enabled channel.
    ///
    /// Design §4.3 / Phase 5.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Biome Preset", fileName = "NewBiomePreset")]
    public sealed class BiomePreset : ScriptableObject
    {
        // ── Channel 1 — Height delta ──────────────────────────────────────────

        [Header("Height Channel")]
        [Tooltip("When enabled, each stamp applies a height delta.")]
        [SerializeField] private bool heightEnabled = false;

        [Tooltip("Height delta to add each stamp (metres, signed).")]
        [SerializeField] private float heightDeltaM = 0f;

        [Tooltip("Strength multiplier for the height channel [0..1].")]
        [Range(0f, 1f)]
        [SerializeField] private float heightStrength = 0.3f;

        public bool   HeightEnabled  => this.heightEnabled;
        public float  HeightDeltaM   => this.heightDeltaM;
        public float  HeightStrength => this.heightStrength;

        // ── Channel 2 — Splat paint ───────────────────────────────────────────

        [Header("Splat Channel")]
        [Tooltip("When enabled, each stamp paints the specified splat layer.")]
        [SerializeField] private bool splatEnabled = false;

        [Tooltip("0-based splat layer index to paint (0 = R, 1 = G, 2 = B, 3 = A).")]
        [Range(0, 3)]
        [SerializeField] private int splatLayerIndex = 0;

        [Tooltip("Target weight to paint onto the splat layer [0..1].")]
        [Range(0f, 1f)]
        [SerializeField] private float splatWeight = 1f;

        public bool  SplatEnabled    => this.splatEnabled;
        public int   SplatLayerIndex => this.splatLayerIndex;
        public float SplatWeight     => this.splatWeight;

        // ── Channel 3 — Grass density ─────────────────────────────────────────

        [Header("Grass Channel")]
        [Tooltip("When enabled, paints grass density onto the referenced layer.")]
        [SerializeField] private bool grassEnabled = false;

        [Tooltip("Grass scatter layer asset to paint density on.")]
        [SerializeField] private DensityScatterLayer? grassLayer = null;

        [Tooltip("Target density to paint [0..1].")]
        [Range(0f, 1f)]
        [SerializeField] private float grassDensity = 0.8f;

        public bool                GrassEnabled   => this.grassEnabled;
        public DensityScatterLayer? GrassLayer    => this.grassLayer;
        public float               GrassDensity   => this.grassDensity;

        // ── Channel 4 — Prop scatter ──────────────────────────────────────────

        [Header("Prop Channel")]
        [Tooltip("When enabled, stamps prop instances from the referenced palette layer.")]
        [SerializeField] private bool propEnabled = false;

        [Tooltip("Prop instance scatter layer to stamp from.")]
        [SerializeField] private InstanceScatterLayer? propLayer = null;

        [Tooltip("Scatter density multiplier for prop stamps [0..1].")]
        [Range(0f, 1f)]
        [SerializeField] private float propDensity = 0.5f;

        public bool                  PropEnabled  => this.propEnabled;
        public InstanceScatterLayer? PropLayer    => this.propLayer;
        public float                 PropDensity  => this.propDensity;

        // ── Channel summary ────────────────────────────────────────────────────

        /// <summary>Count of channels that are currently enabled.</summary>
        public int EnabledChannelCount =>
            (this.heightEnabled ? 1 : 0) +
            (this.splatEnabled  ? 1 : 0) +
            (this.grassEnabled  ? 1 : 0) +
            (this.propEnabled   ? 1 : 0);

        /// <summary>
        /// Returns a one-line human-readable summary of active channels.
        /// Used by the card palette hover tooltip.
        /// </summary>
        public string ChannelSummary()
        {
            var sb = new System.Text.StringBuilder();
            if (this.heightEnabled) sb.Append("⛰ Height ");
            if (this.splatEnabled)  sb.Append($"🎨 Splat[{this.splatLayerIndex}] ");
            if (this.grassEnabled)  sb.Append("🌿 Grass ");
            if (this.propEnabled)   sb.Append("🌳 Props ");
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no channels)";
        }
    }
}
