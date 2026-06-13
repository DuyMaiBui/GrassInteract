#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Runtime-only adapter presenting ONE tile of a <see cref="GrassLayer"/> as a
    /// <see cref="ScatterLayer"/> + <see cref="IDensityPlacementSource"/>, so the FROZEN scatter
    /// engines build it unchanged. NOT serialized — created transiently by
    /// <c>WorldPainter.SurfaceLayers</c> per (GrassLayer, tileCoord) and destroyed with its engine.
    ///
    /// Uses the layer's material directly (no per-tile material clone or _BaseMap override).
    /// DensityMap: the tile's density Texture2D (painted independently per tile).
    /// </summary>
    public sealed class GrassTileScatterLayer : ScatterLayer, IDensityPlacementSource
    {
        private GrassLayer layer = null!;
        private Texture2D? densityMap;           // tile density (referenced, not owned)
        private string     tileId = string.Empty;
        private Vector2    explicitFieldBounds;  // tile-sized bounds

        /// <summary>
        /// Builds a tile-aware transient adapter for one tile. Uses a tile-sized
        /// <see cref="FieldBounds"/> and sets <see cref="UseExplicitFieldBounds"/> = true so
        /// <see cref="DensityPlacement"/> does not override bounds with terrain size.
        /// </summary>
        public static GrassTileScatterLayer Create(
            GrassLayer layer, Vector2Int tileCoord, Texture2D densityTex)
        {
            var adapter = CreateInstance<GrassTileScatterLayer>();
            adapter.hideFlags          = HideFlags.HideAndDontSave;
            adapter.layer              = layer;
            adapter.tileId             = $"{layer.name}@{tileCoord}";
            adapter.densityMap         = densityTex;
            adapter.name               = adapter.tileId;
            adapter.explicitFieldBounds = new Vector2(
                TerrainWorldGrid.TILE_SIZE_M, TerrainWorldGrid.TILE_SIZE_M);
            return adapter;
        }

        // ── ScatterLayer config accessors (shared from layer; material used directly) ──

        public override ScatterRenderConfig    Render    => this.layer.Render;
        public override ScatterWindConfig      Wind      => this.layer.Wind;
        public override ScatterDeformConfig    Deform    => this.layer.Deform;
        public override ScatterBoundsConfig    Bounds    => this.layer.Bounds;
        public override ScatterPlacementConfig Placement => this.layer.Placement;

        // ── Abstract placement data (shared, also satisfies IDensityPlacementSource) ──

        public override Vector2 FieldBounds         => this.explicitFieldBounds;
        public override Vector2 ScaleRange          => this.layer.ScaleRange;
        public override Vector3 RotationOffsetEuler => this.layer.RotationOffsetEuler;
        public override bool    IsOriented          => this.layer.IsOriented;

        public override IScatterPlacement CreatePlacement() => new DensityPlacement(this);

        public override bool Validate(out string error)
        {
            if (this.densityMap == null)
            {
                error = $"Tile '{this.tileId}' has no density map (not yet authored).";
                return false;
            }
            if (!this.densityMap.isReadable)
            {
                error = $"Tile '{this.tileId}' density map '{this.densityMap.name}' is not readable.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        // ── IDensityPlacementSource (tile density + shared placement params) ──

        public bool       UseExplicitFieldBounds => true;
        public Texture2D? DensityMap     => this.densityMap;
        public int        TargetInstances => this.layer.TargetInstances;
        public int        Seed            => this.layer.Seed;
        public Vector2    SlopeRange      => this.layer.SlopeRange;
        public int        SplatLayerIndex => -1;   // drives placement from density, no splat mask
        public float      SplatThreshold  => 0.5f;
        public Vector2    RandomPitchRange => this.layer.RandomPitchRange;
        public Vector2    RandomRollRange  => this.layer.RandomRollRange;
        public bool       AlignToNormal    => this.layer.AlignToNormal;
        public float      MaxBladeHeight   => this.layer.Bounds.MaxBladeHeight;
        public float      BendHeadroom     => this.layer.Bounds.BendHeadroom;

        // ── Lifetime (no owned material — no disposal needed beyond the ScriptableObject) ──

        // GrassTileScatterLayer owns no resources beyond the ScriptableObject itself.
        // DestroyImmediate/Destroy is sufficient; no OnDisable/OnDestroy override required.
    }
}
