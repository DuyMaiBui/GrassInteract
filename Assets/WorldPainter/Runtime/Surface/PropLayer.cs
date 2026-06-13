#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Unified PROP authoring layer. Holds ONE scatter config (mesh/material/wind/deform/
    /// bounds/placement/tilt) plus an <see cref="AuthoredInstancesData"/> sub-asset that stores
    /// the explicitly placed instance records.
    ///
    /// Provides the config surface that the transient
    /// <see cref="PropLayerScatterLayer"/> adapter forwards to the frozen engine.
    ///
    /// Built into a frozen <see cref="InstancedPropEngine"/> via <see cref="PropLayerScatterLayer"/>
    /// in <c>WorldPainter.SurfaceLayers.RebuildSurfaceLayers</c> — no changes to any frozen
    /// engine file are required.
    /// </summary>
    public sealed class PropLayer : WorldPainterLayer
    {
        // ── Composed config (SSOT structs — mirroring InstanceScatterLayer) ──────

        [SerializeField] private ScatterRenderConfig    render;
        [SerializeField] private ScatterWindConfig      wind;
        [SerializeField] private ScatterDeformConfig    deform;
        [SerializeField] private ScatterBoundsConfig    bounds;
        [SerializeField] private ScatterPlacementConfig placement;

        // ── Instance-only tilt config ────────────────────────────────────────────

        [SerializeField] private ScatterInstanceTiltConfig tilt;

        // ── Deform anchor ────────────────────────────────────────────────────────

        [Tooltip("Local-space offset of the deform sampling anchor from the instance pivot.")]
        [SerializeField] private Vector3 anchorOffsetLocal = Vector3.zero;

        // ── Prop placement anchor config ─────────────────────────────────────────

        [Tooltip("Pivot offset applied to every placed instance in local space.")]
        [SerializeField] private Vector3 propPivotOffset = Vector3.zero;

        [Tooltip("When true, each placed instance is snapped to the surface Y.")]
        [SerializeField] private bool propGroundSnap = true;

        [Tooltip("When true, each placed instance is rotated to align to the surface normal.")]
        [SerializeField] private bool propAlignToNormal = false;

        // ── Authored instances ───────────────────────────────────────────────────

        [Tooltip("Sub-asset holding authored per-instance records.")]
        [SerializeField] private AuthoredInstancesData? authoredInstances;

        // ── Scale-range override ─────────────────────────────────────────────────

        [Tooltip("When true, ScaleRange is driven by scaleRangeOverride.")]
        [SerializeField] private bool overrideScaleRange = false;

        [Tooltip("Manual scale range (x = min, y = max) used when overrideScaleRange is true.")]
        [SerializeField] private Vector2 scaleRangeOverride = new Vector2(1f, 1f);

        // ── Collider config ──────────────────────────────────────────────────────

        [Tooltip("Generate a pooled, visibility-culled collider for every instance at runtime.")]
        [SerializeField] private bool generateColliders = false;

        [Tooltip("Max new colliders created per frame to amortise MeshCollider cooking.")]
        [SerializeField] private int maxCollidersPerFrame = 8;

        [Tooltip("Fallback collider mesh when a record's colliderOverride is null.")]
        [SerializeField] private Mesh? defaultColliderMesh;

        [Tooltip("Use a convex MeshCollider for the layer-default collider.")]
        [SerializeField] private bool defaultColliderConvex = false;

        [Tooltip("Fallback collider PhysicMaterial when a record has no material override.")]
        [SerializeField] private PhysicsMaterial? defaultColliderMaterial;

        // ── Pooling + culling config ─────────────────────────────────────────────

        [Tooltip("Pool and reuse collider GameObjects rather than instantiating per frame.")]
        [SerializeField] private bool poolColliders = true;

        [Tooltip("Maximum number of collider GameObjects held in the pool at once.")]
        [SerializeField] private int poolCap = 256;

        [Tooltip("Use the GPU LOD0 visibility buffer to cull colliders.")]
        [SerializeField] private bool cullColliders = true;

        [Tooltip("Uniform scale multiplier applied to the layer-default collider mesh.")]
        [SerializeField] private float defaultColliderScale = 1f;

        // ── Identity ─────────────────────────────────────────────────────────────

        public override LayerKind Kind => LayerKind.Prop;

        /// <summary>Number of authored instances, or 0 when <see cref="AuthoredInstances"/> is null.</summary>
        public override int PaletteCount =>
            this.authoredInstances != null ? this.authoredInstances.Count : 0;

        // ── Config struct accessors ──────────────────────────────────────────────

        public ScatterRenderConfig    Render    => this.render;
        public ScatterWindConfig      Wind      => this.wind;
        public ScatterDeformConfig    Deform    => this.deform;
        public ScatterBoundsConfig    Bounds    => this.bounds;
        public ScatterPlacementConfig Placement => this.placement;
        public ScatterInstanceTiltConfig Tilt   => this.tilt;
        public Vector3 AnchorOffsetLocal        => this.anchorOffsetLocal;

        // ── Prop placement accessors ─────────────────────────────────────────────

        public Vector3 PropPivotOffset  => this.propPivotOffset;
        public bool    PropGroundSnap   => this.propGroundSnap;
        public bool    PropAlignToNormal => this.propAlignToNormal;

        // ── Authored instances ───────────────────────────────────────────────────

        public AuthoredInstancesData? AuthoredInstances => this.authoredInstances;

        // ── Scale override ───────────────────────────────────────────────────────

        public bool    OverrideScaleRange  => this.overrideScaleRange;
        public Vector2 ScaleRangeOverride  => this.scaleRangeOverride;

        // ── Collider accessors ───────────────────────────────────────────────────

        public bool           GenerateColliders       => this.generateColliders;
        public int            MaxCollidersPerFrame     => Mathf.Max(1, this.maxCollidersPerFrame);
        public Mesh?          DefaultColliderMesh      => this.defaultColliderMesh;
        public bool           DefaultColliderConvex    => this.defaultColliderConvex;
        public PhysicsMaterial? DefaultColliderMaterial => this.defaultColliderMaterial;
        public bool           PoolColliders            => this.poolColliders;
        public int            PoolCap                  => this.poolCap;
        public bool           CullColliders            => this.cullColliders;
        public float          DefaultColliderScale     => this.defaultColliderScale;

        // ── Editor authoring setters (used by WorldMapAssetLifecycle) ────────────

        /// <summary>Sets the shared render material. Editor lifecycle only.</summary>
        internal void EditorSetMaterial(Material material)
        {
            this.render = new ScatterRenderConfig(
                material, this.render.ShadowCastingMode, this.render.Lods, this.render.RenderCullDistance);
        }

        /// <summary>Sets the shared LOD meshes. Editor lifecycle only.</summary>
        internal void EditorSetLods(ScatterLod[] lods)
        {
            this.render = new ScatterRenderConfig(
                this.render.Material, this.render.ShadowCastingMode, lods, this.render.RenderCullDistance);
        }

        /// <summary>Assigns the AuthoredInstancesData sub-asset. Editor lifecycle only.</summary>
        internal void EditorSetAuthored(AuthoredInstancesData data)
        {
            this.authoredInstances = data;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var migrated = ScatterRenderConfig.MigrateCull(this.render);
            if (migrated.RenderCullDistance != this.render.RenderCullDistance)
            {
                this.render = migrated;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
