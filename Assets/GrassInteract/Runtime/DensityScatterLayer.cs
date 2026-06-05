#nullable enable
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// ScatterLayer variant that drives placement from a density map via procedural RNG scatter.
    /// One of two concrete placement subclasses; the other is <see cref="InstanceScatterLayer"/>.
    ///
    /// Holds density-specific serialized fields: <see cref="densityMap"/> and <see cref="targetInstances"/>.
    /// All procedural placement fields (fieldBounds, seed, scaleRange, slope, splat, orientation)
    /// also live here — InstanceScatterLayer does not use procedural placement.
    ///
    /// FormerlySerializedAs shims ensure pre-Phase-A assets that stored these fields on the base
    /// ScatterLayer continue to resolve correctly.
    /// </summary>
    public sealed class DensityScatterLayer : ScatterLayer
    {
        // ── Density ───────────────────────────────────────────────────────────

        [Tooltip("Readable, uncompressed R8 density map. White = full density, black = no instances.")]
        [UnityEngine.Serialization.FormerlySerializedAs("densityMap")]
        [SerializeField] private Texture2D? densityMap;

        [Min(1)]
        [Tooltip("Candidate instances scattered across the field. Each is KEPT with probability = density at its position.")]
        [UnityEngine.Serialization.FormerlySerializedAs("targetInstances")]
        [SerializeField] private int targetInstances = 50000;

        // ── Placement fields (moved from ScatterLayer base in Phase A) ────────

        [Tooltip("World-space XZ size of the field, centered on the ScatterField transform.")]
        [UnityEngine.Serialization.FormerlySerializedAs("fieldBounds")]
        [SerializeField] private Vector2 fieldBounds = new Vector2(100f, 100f);

        [UnityEngine.Serialization.FormerlySerializedAs("scaleRange")]
        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [UnityEngine.Serialization.FormerlySerializedAs("seed")]
        [SerializeField] private int seed = 0;

        [Tooltip("Placement is allowed only when the ground slope (deg) is within [x, y]. Default (0, 90) = no filter.")]
        [UnityEngine.Serialization.FormerlySerializedAs("slopeRange")]
        [SerializeField] private Vector2 slopeRange = new Vector2(0f, 90f);

        [Tooltip("Terrain alphamap layer index to use as placement mask (-1 = off).")]
        [UnityEngine.Serialization.FormerlySerializedAs("splatLayerIndex")]
        [SerializeField] private int splatLayerIndex = -1;

        [Range(0f, 1f)]
        [Tooltip("Minimum splat weight required for placement when splatLayerIndex >= 0.")]
        [UnityEngine.Serialization.FormerlySerializedAs("splatThreshold")]
        [SerializeField] private float splatThreshold = 0.5f;

        // ── Orientation fields (moved from ScatterLayer base in Phase A) ──────

        [Tooltip("Per-layer uniform rotation offset applied to every instance after yaw and optional pitch/roll.")]
        [UnityEngine.Serialization.FormerlySerializedAs("rotationOffsetEuler")]
        [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

        [Tooltip("Per-instance random pitch (X-axis) range in degrees. (0,0) = no random pitch.")]
        [UnityEngine.Serialization.FormerlySerializedAs("randomPitchRange")]
        [SerializeField] private Vector2 randomPitchRange = Vector2.zero;

        [Tooltip("Per-instance random roll (Z-axis) range in degrees. (0,0) = no random roll.")]
        [UnityEngine.Serialization.FormerlySerializedAs("randomRollRange")]
        [SerializeField] private Vector2 randomRollRange = Vector2.zero;

        [Tooltip("Align instance up-axis to terrain/surface normal.")]
        [UnityEngine.Serialization.FormerlySerializedAs("alignToNormal")]
        [SerializeField] private bool alignToNormal = false;

        // ── Accessors ─────────────────────────────────────────────────────────

        public override Texture2D? DensityMap  => this.densityMap;
        public int TargetInstances             => this.targetInstances;

        public override Vector2 FieldBounds         => this.fieldBounds;
        public override Vector2 ScaleRange          => this.scaleRange;
        public override int     Seed                => this.seed;
        public override Vector2 SlopeRange          => this.slopeRange;
        public override int     SplatLayerIndex     => this.splatLayerIndex;
        public override float   SplatThreshold      => this.splatThreshold;
        public override Vector3 RotationOffsetEuler => this.rotationOffsetEuler;
        public override Vector2 RandomPitchRange    => this.randomPitchRange;
        public override Vector2 RandomRollRange     => this.randomRollRange;
        public override bool    AlignToNormal       => this.alignToNormal;

        /// <summary>
        /// True when per-instance oriented packing is needed in the GPU buffers.
        /// Activated by AlignToNormal or non-zero pitch/roll ranges.
        /// </summary>
        public override bool IsOriented =>
            this.alignToNormal
            || this.randomPitchRange != Vector2.zero
            || this.randomRollRange  != Vector2.zero;

        // ── IScatterPlacement ─────────────────────────────────────────────────

        public override IScatterPlacement CreatePlacement() => new DensityPlacement(this);

        // ── Validation ────────────────────────────────────────────────────────

        public override bool Validate(out string error)
        {
            if (this.densityMap == null) { error = "DensityMap is not assigned."; return false; }
            if (!this.densityMap.isReadable)
            {
                error = $"DensityMap '{this.densityMap.name}' is not readable. Enable Read/Write in import settings.";
                return false;
            }
            if (GraphicsFormatUtility.IsCompressedFormat(this.densityMap.graphicsFormat))
            {
                error = $"DensityMap '{this.densityMap.name}' is compressed. Use uncompressed single-channel format (R8).";
                return false;
            }
            return base.Validate(out error);
        }
    }
}
