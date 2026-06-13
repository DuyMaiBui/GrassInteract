#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Runtime-only adapter presenting a <see cref="PropLayer"/> as a <see cref="ScatterLayer"/> +
    /// <see cref="IInstancePlacementSource"/>, so the FROZEN <see cref="InstancedPropEngine"/>
    /// builds it unchanged via <c>engine.Build(adapter, origin, pool, sampler)</c>.
    ///
    /// NOT serialized — created transiently by <c>WorldPainter.SurfaceLayers.RebuildSurfaceLayers</c>
    /// and destroyed alongside its engine via <see cref="OnDisable"/> / <see cref="OnDestroy"/>.
    ///
    /// Mirrors <see cref="GrassTileScatterLayer"/> lifetime/disposal patterns.
    /// </summary>
    public sealed class PropLayerScatterLayer : ScatterLayer, IInstancePlacementSource
    {
        private PropLayer layer = null!;

        // ── Factory ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a transient HideAndDontSave adapter for <paramref name="layer"/>.
        /// No material cloning — props use the layer material as-is (unlike grass variants
        /// that clone-per-variant to swap <c>_BaseMap</c>).
        /// </summary>
        public static PropLayerScatterLayer Create(PropLayer layer)
        {
            var adapter        = CreateInstance<PropLayerScatterLayer>();
            adapter.hideFlags  = HideFlags.HideAndDontSave;
            adapter.layer      = layer;
            adapter.name       = $"PropLayerAdapter_{layer.name}";
            return adapter;
        }

        // ── ScatterLayer abstract config accessors ───────────────────────────────

        public override ScatterRenderConfig    Render    => this.layer.Render;
        public override ScatterWindConfig      Wind      => this.layer.Wind;
        public override ScatterDeformConfig    Deform    => this.layer.Deform;
        public override ScatterBoundsConfig    Bounds    => this.layer.Bounds;
        public override ScatterPlacementConfig Placement => this.layer.Placement;

        // ── ScatterLayer abstract placement data ─────────────────────────────────

        /// <summary>
        /// XZ bounds computed from authored instance positions.
        /// </summary>
        public override Vector2 FieldBounds => this.ComputeFieldBoundsFromAuthored();

        /// <summary>
        /// Scale range from authored records (or override when enabled).
        /// </summary>
        public override Vector2 ScaleRange =>
            this.layer.OverrideScaleRange
                ? this.layer.ScaleRangeOverride
                : this.ComputeScaleRangeFromAuthored();

        public override Vector3 RotationOffsetEuler => Vector3.zero;

        public override bool IsOriented => this.ComputeIsOrientedFromAuthored();

        // ── IScatterPlacement factory ────────────────────────────────────────────

        public override IScatterPlacement CreatePlacement() => new InstancePlacement(this);

        // ── IInstancePlacementSource ─────────────────────────────────────────────

        public AuthoredInstancesData? AuthoredInstances => this.layer.AuthoredInstances;

        public float MaxBladeHeight => this.layer.Bounds.MaxBladeHeight;
        public float BendHeadroom   => this.layer.Bounds.BendHeadroom;

        // ── Prop-engine accessors (forwarded from PropLayer for InstancedPropEngine) ─

        public ScatterInstanceTiltConfig Tilt             => this.layer.Tilt;
        public Vector3                   AnchorOffsetLocal => this.layer.AnchorOffsetLocal;
        public bool                      GenerateColliders         => this.layer.GenerateColliders;
        public int                       MaxCollidersPerFrame      => this.layer.MaxCollidersPerFrame;
        public Mesh?                     DefaultColliderMesh       => this.layer.DefaultColliderMesh;
        public bool                      DefaultColliderConvex     => this.layer.DefaultColliderConvex;
        public UnityEngine.PhysicsMaterial? DefaultColliderMaterial => this.layer.DefaultColliderMaterial;
        public bool                      PoolColliders             => this.layer.PoolColliders;
        public int                       PoolCap                   => this.layer.PoolCap;
        public bool                      CullColliders             => this.layer.CullColliders;

        public bool AnyRecordWantsCollider()
        {
            var authored = this.layer.AuthoredInstances;
            if (authored == null) return false;
            var records = authored.GetRuntimeRecords();
            for (int i = 0; i < records.Length; ++i)
            {
                if (records[i].generateCollider) return true;
            }
            return false;
        }

        // ── Validation ───────────────────────────────────────────────────────────

        public override bool Validate(out string error)
        {
            var authored = this.layer.AuthoredInstances;
            if (authored == null)
            {
                error = $"PropLayer '{this.layer.name}' has no AuthoredInstancesData assigned.";
                return false;
            }

            if (authored.Count == 0)
            {
                error = $"PropLayer '{this.layer.name}' has zero authored instances — skipped.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        // ── Lifetime ─────────────────────────────────────────────────────────────

        // No runtime-owned material to dispose (props use the layer material as-is).
        // Unity's HideAndDontSave lifecycle handles object cleanup; nothing extra needed.
        private void OnDisable() { /* no owned resources */ }
        private void OnDestroy() { /* no owned resources */ }

        // ── Computed helpers (mirror InstanceScatterLayer private helpers) ────────

        private Vector2 ComputeFieldBoundsFromAuthored()
        {
            var authored = this.layer.AuthoredInstances;
            if (authored == null) return new Vector2(100f, 100f);
            // GetRuntimeRecords() returns a persistent cached NativeArray owned by authored — do NOT dispose.
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
            var authored = this.layer.AuthoredInstances;
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
            var authored = this.layer.AuthoredInstances;
            if (authored == null) return false;
            var records = authored.GetRuntimeRecords();
            for (int i = 0; i < records.Length; ++i)
            {
                if (records[i].rotation != Quaternion.identity)
                    return true;
            }
            return false;
        }
    }
}
