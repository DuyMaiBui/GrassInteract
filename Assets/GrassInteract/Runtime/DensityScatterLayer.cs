#nullable enable
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// ScatterLayer variant that drives placement from a density map via procedural RNG scatter.
    /// One of two concrete placement types; the other is <see cref="InstanceScatterLayer"/>.
    ///
    /// This type COMPOSES its shared configuration as embedded <see cref="System.Serializable"/> structs
    /// (<see cref="ScatterRenderConfig"/>, <see cref="ScatterWindConfig"/>, <see cref="ScatterDeformConfig"/>,
    /// <see cref="ScatterBoundsConfig"/>, <see cref="ScatterPlacementConfig"/>) — each struct is the single
    /// source of truth for its fields. No shared state with <see cref="InstanceScatterLayer"/> beyond the
    /// thin <see cref="ScatterLayer"/> serialization marker.
    /// </summary>
    public sealed class DensityScatterLayer : ScatterLayer, IDensityPlacementSource
    {
        // ── Composed shared config (embedded structs = SSOT) ───────────────────

        [SerializeField] private ScatterRenderConfig    render;
        [SerializeField] private ScatterWindConfig      wind;
        [SerializeField] private ScatterDeformConfig    deform;
        [SerializeField] private ScatterBoundsConfig    bounds;
        [SerializeField] private ScatterPlacementConfig placement;

        // ── Density-specific fields ────────────────────────────────────────────

        [Tooltip("Readable, uncompressed R8 density map. White = full density, black = no instances.")]
        [SerializeField] private Texture2D? densityMap;

        [Min(1)]
        [Tooltip("Candidate instances scattered across the field.")]
        [SerializeField] private int targetInstances = 50000;

        // ── Procedural placement fields ────────────────────────────────────────

        [Tooltip("World-space XZ size of the field.")]
        [SerializeField] private Vector2 fieldBounds = new Vector2(100f, 100f);

        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [SerializeField] private int seed = 0;

        [Tooltip("Placement is allowed only when the ground slope (deg) is within [x, y].")]
        [SerializeField] private Vector2 slopeRange = new Vector2(0f, 90f);

        [Tooltip("Terrain alphamap layer index to use as placement mask (-1 = off).")]
        [SerializeField] private int splatLayerIndex = -1;

        [Range(0f, 1f)]
        [Tooltip("Minimum splat weight required for placement when splatLayerIndex >= 0.")]
        [SerializeField] private float splatThreshold = 0.5f;

        // ── Orientation fields ─────────────────────────────────────────────────

        [Tooltip("Per-layer uniform rotation offset applied to every instance.")]
        [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

        [Tooltip("Per-instance random pitch (X-axis) range in degrees.")]
        [SerializeField] private Vector2 randomPitchRange = Vector2.zero;

        [Tooltip("Per-instance random roll (Z-axis) range in degrees.")]
        [SerializeField] private Vector2 randomRollRange = Vector2.zero;

        [Tooltip("Align instance up-axis to terrain/surface normal.")]
        [SerializeField] private bool alignToNormal = false;

        // ── Config struct accessors ────────────────────────────────────────────

        public override ScatterRenderConfig    Render    => this.render;
        public override ScatterWindConfig      Wind      => this.wind;
        public override ScatterDeformConfig    Deform    => this.deform;
        public override ScatterBoundsConfig    Bounds    => this.bounds;
        public override ScatterPlacementConfig Placement => this.placement;

        // ── Abstract placement data accessors ──────────────────────────────────

        public override Vector2 FieldBounds => this.fieldBounds;
        public override Vector2 ScaleRange => this.scaleRange;
        public override Vector3 RotationOffsetEuler => this.rotationOffsetEuler;

        public override bool IsOriented =>
            this.alignToNormal
            || this.randomPitchRange != Vector2.zero
            || this.randomRollRange != Vector2.zero;

        // ── IDensityPlacementSource ────────────────────────────────────────────

        public Texture2D? DensityMap => this.densityMap;
        public int TargetInstances => this.targetInstances;
        public int Seed => this.seed;
        public Vector2 SlopeRange => this.slopeRange;
        public int SplatLayerIndex => this.splatLayerIndex;
        public float SplatThreshold => this.splatThreshold;
        public Vector2 RandomPitchRange => this.randomPitchRange;
        public Vector2 RandomRollRange => this.randomRollRange;
        public bool AlignToNormal => this.alignToNormal;

        // ── Bounds (IDensityPlacementSource — delegate to the embedded struct) ──

        public float MaxBladeHeight => this.bounds.MaxBladeHeight;
        public float BendHeadroom => this.bounds.BendHeadroom;

        // ── IScatterPlacement factory ──────────────────────────────────────────

        public override IScatterPlacement CreatePlacement() => new DensityPlacement(this);

        // ── Validation ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            this.MigrateRenderCullDistance();
        }

        /// <summary>
        /// Back-fills <see cref="ScatterRenderConfig.RenderCullDistance"/> for assets serialized before the field existed
        /// (renderCullDistance == 0 → everything would cull at 0). Defaults to max(2 * second-last LOD switch, 500) to
        /// preserve the legacy derived-formula far cull. Idempotent: only writes when renderCullDistance is still 0.
        /// </summary>
        private void MigrateRenderCullDistance()
        {
            if (this.render.RenderCullDistance > 0f)
                return;

            float[] dists = this.render.LodMaxDistances; // length == lods.Length - 1
            // Legacy far cull was max(2 * secondLastLODdistance, 500). secondLastLODdistance == last switch distance.
            float lastSwitch = dists.Length > 0 ? dists[dists.Length - 1] : 0f;
            float migrated = Mathf.Max(2f * lastSwitch, 500f); // <2 LODs (dists empty) → 500m floor

            this.render = new ScatterRenderConfig(
                this.render.Material,
                this.render.ShadowCastingMode,
                this.render.Lods,
                migrated);

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

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
            if (this.scaleRange.x <= 0f || this.scaleRange.y < this.scaleRange.x)
            {
                error = $"ScaleRange ({this.scaleRange}) must be positive and non-decreasing.";
                return false;
            }
            if (this.fieldBounds.x <= 0f || this.fieldBounds.y <= 0f)
            {
                error = $"FieldBounds ({this.fieldBounds}) must be positive.";
                return false;
            }
            return base.Validate(out error);
        }
    }
}
