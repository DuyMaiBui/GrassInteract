#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// ScatterLayer variant that holds an authored <see cref="AuthoredInstancesData"/> sidecar and
    /// feeds it straight into the bake. One of two concrete placement subclasses; the other is
    /// <see cref="DensityScatterLayer"/>.
    ///
    /// FormerlySerializedAs shims ensure pre-Phase-A assets resolve these fields from the base-class
    /// serialized data that lived on <c>ScatterLayer</c> before the Phase A refactor.
    /// </summary>
    public sealed class InstanceScatterLayer : ScatterLayer
    {
        // ── Authored instances ────────────────────────────────────────────────

        [Tooltip("Sub-asset holding authored per-instance records.")]
        [UnityEngine.Serialization.FormerlySerializedAs("authoredInstances")]
        [SerializeField] private AuthoredInstancesData? authoredInstances;

        [Range(0.05f, 5f)]
        [Tooltip("Minimum spacing (metres) between placed instances during a Place-brush stroke.")]
        [UnityEngine.Serialization.FormerlySerializedAs("placeSpacing")]
        [SerializeField] private float placeSpacing = 0.5f;

        // ── Layer-default collider config ─────────────────────────────────────
        // Used when a record's generateCollider is true but no colliderOverride mesh is set.

        [Tooltip("Fallback collider mesh when a record's colliderOverride is null. " +
                 "If also null, lods[0].mesh is used.")]
        [SerializeField] private Mesh? defaultColliderMesh;

        [Tooltip("Use a convex MeshCollider for the layer-default collider " +
                 "(required for non-kinematic Rigidbody interaction).")]
        [SerializeField] private bool defaultColliderConvex = false;

        // ── Pooling + culling config (Phase H runtime — fields authored here) ─

        [Tooltip("Pool and reuse collider GameObjects rather than instantiating per frame.")]
        [SerializeField] private bool poolColliders = true;

        [Tooltip("Maximum number of collider GameObjects held in the pool at once.")]
        [SerializeField] private int poolCap = 256;

        [Tooltip("Disable colliders for instances beyond cullDistance from the camera.")]
        [SerializeField] private bool cullColliders = true;

        [Tooltip("Distance (metres) beyond which per-instance colliders are culled.")]
        [Range(1f, 500f)]
        [SerializeField] private float cullDistance = 80f;

        [Tooltip("Uniform scale multiplier applied to the layer-default collider mesh.")]
        [SerializeField] private float defaultColliderScale = 1f;

        // ── Accessors ─────────────────────────────────────────────────────────

        public override AuthoredInstancesData? AuthoredInstances => this.authoredInstances;
        public override float PlaceSpacing => this.placeSpacing;

        /// <summary>Fallback collider mesh when a record has no colliderOverride.</summary>
        public Mesh? DefaultColliderMesh => this.defaultColliderMesh;

        /// <summary>Convex flag for the layer-default collider.</summary>
        public bool DefaultColliderConvex => this.defaultColliderConvex;

        /// <summary>Pool and reuse collider GameObjects (Phase H).</summary>
        public bool PoolColliders => this.poolColliders;

        /// <summary>Max pooled collider count (Phase H).</summary>
        public int PoolCap => this.poolCap;

        /// <summary>Frustum-cull colliders beyond <see cref="CullDistance"/> (Phase H).</summary>
        public bool CullColliders => this.cullColliders;

        /// <summary>Camera-distance threshold for collider culling (Phase H).</summary>
        public float CullDistance => this.cullDistance;

        /// <summary>Uniform scale multiplier for the layer-default collider mesh.</summary>
        public float DefaultColliderScale => this.defaultColliderScale;

        // ── Runtime helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns true when at least one authored record has <c>generateCollider = true</c>.
        /// Used by <see cref="MeshScatterEngine"/> to decide whether to create the pool/culler.
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

        // ── IScatterPlacement ─────────────────────────────────────────────────

        public override IScatterPlacement CreatePlacement() => new InstancePlacement(this);

        // ── Validation ────────────────────────────────────────────────────────

        public override bool Validate(out string error) => base.Validate(out error);
    }
}
