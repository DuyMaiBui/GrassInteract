#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// ScatterLayer variant that holds authored per-instance records (mesh props).
    /// One of two concrete placement types; the other is <see cref="DensityScatterLayer"/>.
    ///
    /// This type COMPOSES its shared configuration as embedded <see cref="System.Serializable"/> structs
    /// (<see cref="ScatterRenderConfig"/>, <see cref="ScatterWindConfig"/>, <see cref="ScatterDeformConfig"/>,
    /// <see cref="ScatterBoundsConfig"/>, <see cref="ScatterPlacementConfig"/>) plus an instance-only
    /// <see cref="ScatterInstanceTiltConfig"/>. Each struct is the single source of truth for its fields.
    /// No shared state with <see cref="DensityScatterLayer"/> beyond the thin <see cref="ScatterLayer"/>
    /// serialization marker.
    /// </summary>
    public sealed class InstanceScatterLayer : ScatterLayer, IInstancePlacementSource
    {
        // ── Composed shared config (embedded structs = SSOT) ───────────────────

        [SerializeField] private ScatterRenderConfig    render;
        [SerializeField] private ScatterWindConfig      wind;
        [SerializeField] private ScatterDeformConfig    deform;
        [SerializeField] private ScatterBoundsConfig    bounds;
        [SerializeField] private ScatterPlacementConfig placement;

        // ── Instance-only rigid tilt config (NOT shared with Density) ──────────

        [SerializeField] private ScatterInstanceTiltConfig tilt;

        // ── Instance-only deform anchor (NOT shared with Density) ──────────────

        [Tooltip("Local-space offset of the deform sampling anchor from the instance pivot. " +
                 "Wind phase, interactor lean, and rigid tilt all measure from this anchor instead of the " +
                 "pivot. The offset is rotated by the instance's base orientation and multiplied by the " +
                 "instance's scale, so a taller/wider instance samples proportionally further out. " +
                 "(0,0,0) = sample at the pivot (legacy behaviour).")]
        [SerializeField] private Vector3 anchorOffsetLocal = Vector3.zero;

        // ── Instance-specific fields ───────────────────────────────────────────

        [Tooltip("Sub-asset holding authored per-instance records.")]
        [SerializeField] private AuthoredInstancesData? authoredInstances;

        // ── Scale-range override ───────────────────────────────────────────────

        [Tooltip("When true, ScaleRange is driven by scaleRangeOverride instead of being auto-computed from authored instance records.")]
        [SerializeField] private bool overrideScaleRange = false;

        [Tooltip("Manual scale range (x = min, y = max) used when overrideScaleRange is true.")]
        [SerializeField] private Vector2 scaleRangeOverride = new Vector2(1f, 1f);

        // ── Layer-default collider config ─────────────────────────────────────

        [Tooltip("Fallback collider mesh when a record's colliderOverride is null.")]
        [SerializeField] private Mesh? defaultColliderMesh;

        [Tooltip("Use a convex MeshCollider for the layer-default collider.")]
        [SerializeField] private bool defaultColliderConvex = false;

        [Tooltip("Fallback collider PhysicMaterial when a record's material override is null.")]
        [SerializeField] private PhysicsMaterial? defaultColliderMaterial;

        // ── Pooling + culling config ──────────────────────────────────────────

        [Tooltip("Pool and reuse collider GameObjects rather than instantiating per frame.")]
        [SerializeField] private bool poolColliders = true;

        [Tooltip("Maximum number of collider GameObjects held in the pool at once.")]
        [SerializeField] private int poolCap = 256;

        [Tooltip("Disable colliders for instances beyond cullDistance from the camera.")]
        [SerializeField] private bool cullColliders = true;

        [Range(1f, 500f)]
        [Tooltip("Distance (metres) beyond which per-instance colliders are culled.")]
        [SerializeField] private float cullDistance = 80f;

        [Tooltip("Uniform scale multiplier applied to the layer-default collider mesh.")]
        [SerializeField] private float defaultColliderScale = 1f;

        // ── Config struct accessors ────────────────────────────────────────────

        public override ScatterRenderConfig    Render    => this.render;
        public override ScatterWindConfig      Wind      => this.wind;
        public override ScatterDeformConfig    Deform    => this.deform;
        public override ScatterBoundsConfig    Bounds    => this.bounds;
        public override ScatterPlacementConfig Placement => this.placement;

        /// <summary>Rigid whole-instance tilt config — instance-only (Density never reads this).</summary>
        public ScatterInstanceTiltConfig Tilt => this.tilt;

        /// <summary>
        /// Local-space offset of the deform sampling anchor from the instance pivot. Wind phase,
        /// interactor lean, and rigid tilt sample from <c>pivot + baseRot * (AnchorOffsetLocal * scale)</c>.
        /// <c>Vector3.zero</c> = sample at the pivot (legacy behaviour).
        /// </summary>
        public Vector3 AnchorOffsetLocal => this.anchorOffsetLocal;

        // ── Abstract placement data accessors ──────────────────────────────────

        public override Vector2 FieldBounds => ComputeFieldBoundsFromAuthored();
        public override Vector2 ScaleRange => this.overrideScaleRange ? this.scaleRangeOverride : ComputeScaleRangeFromAuthored();
        public override Vector3 RotationOffsetEuler => Vector3.zero;
        public override bool IsOriented => ComputeIsOrientedFromAuthored();

        /// <summary>Whether the manual scale-range override is active.</summary>
        public bool OverrideScaleRange => this.overrideScaleRange;

        /// <summary>Manual scale range (x = min, y = max) used when <see cref="OverrideScaleRange"/> is true.</summary>
        public Vector2 ScaleRangeOverride => this.scaleRangeOverride;

        // ── IInstancePlacementSource ───────────────────────────────────────────

        public AuthoredInstancesData? AuthoredInstances => this.authoredInstances;

        // ── Bounds (IInstancePlacementSource — delegate to the embedded struct) ─
        public float MaxBladeHeight => this.bounds.MaxBladeHeight;
        public float BendHeadroom => this.bounds.BendHeadroom;

        /// <summary>Fallback collider mesh when a record has no colliderOverride.</summary>
        public Mesh? DefaultColliderMesh => this.defaultColliderMesh;

        /// <summary>Convex flag for the layer-default collider.</summary>
        public bool DefaultColliderConvex => this.defaultColliderConvex;

        /// <summary>Fallback collider PhysicMaterial when a record has no material override.</summary>
        public PhysicsMaterial? DefaultColliderMaterial => this.defaultColliderMaterial;

        /// <summary>Pool and reuse collider GameObjects.</summary>
        public bool PoolColliders => this.poolColliders;

        /// <summary>Max pooled collider count.</summary>
        public int PoolCap => this.poolCap;

        /// <summary>Frustum-cull colliders beyond CullDistance.</summary>
        public bool CullColliders => this.cullColliders;

        /// <summary>Camera-distance threshold for collider culling.</summary>
        public float CullDistance => this.cullDistance;

        /// <summary>Uniform scale multiplier for the layer-default collider mesh.</summary>
        public float DefaultColliderScale => this.defaultColliderScale;

        // ── Runtime helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns true when at least one authored record has generateCollider = true.
        /// </summary>
        public bool AnyRecordWantsCollider()
        {
            var authored = this.authoredInstances;
            if (authored == null) return false;
            var records = authored.GetRuntimeRecords();
            for (int i = 0; i < records.Length; ++i)
            {
                if (records[i].generateCollider) return true;
            }
            return false;
        }

        // ── IScatterPlacement factory ──────────────────────────────────────────

        public override IScatterPlacement CreatePlacement() => new InstancePlacement(this);

        // ── Validation ─────────────────────────────────────────────────────────

        public override bool Validate(out string error) => base.Validate(out error);

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

        // ── Computed placement data from authored instances ────────────────────

        private Vector2 ComputeFieldBoundsFromAuthored()
        {
            var authored = this.authoredInstances;
            if (authored == null) return new Vector2(100f, 100f);
            var records = authored.GetRuntimeRecords();
            if (records.Length == 0) return new Vector2(100f, 100f);

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < records.Length; ++i)
            {
                Vector3 pos = records[i].position;
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.z < minZ) minZ = pos.z;
                if (pos.z > maxZ) maxZ = pos.z;
            }
            return new Vector2(maxX - minX, maxZ - minZ);
        }

        private Vector2 ComputeScaleRangeFromAuthored()
        {
            var authored = this.authoredInstances;
            if (authored == null) return new Vector2(1f, 1f);
            var records = authored.GetRuntimeRecords();
            if (records.Length == 0) return new Vector2(1f, 1f);

            float minS = float.PositiveInfinity, maxS = float.NegativeInfinity;
            for (int i = 0; i < records.Length; ++i)
            {
                float s = records[i].scale;
                if (s < minS) minS = s;
                if (s > maxS) maxS = s;
            }
            return new Vector2(minS, maxS);
        }

        private bool ComputeIsOrientedFromAuthored()
        {
            var authored = this.authoredInstances;
            if (authored == null) return false;
            var records = authored.GetRuntimeRecords();
            for (int i = 0; i < records.Length; ++i)
            {
                if (records[i].rotation != Quaternion.identity) return true;
            }
            return false;
        }
    }
}
