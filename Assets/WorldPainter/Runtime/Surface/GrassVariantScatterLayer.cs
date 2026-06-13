#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Runtime-only adapter presenting ONE variant of a <see cref="GrassLayer"/> as a
    /// <see cref="ScatterLayer"/> + <see cref="IDensityPlacementSource"/>, so the FROZEN scatter
    /// engines build it unchanged. NOT serialized — created transiently by
    /// <c>WorldPainter.SurfaceLayers</c> per (GrassLayer, variantIndex) and destroyed with its engine.
    ///
    /// Shares the layer's wind/bend/bounds/placement + LOD meshes; differs ONLY by:
    ///   - Material: a runtime clone of the layer material with <c>_BaseMap</c> = variant.texture.
    ///   - DensityMap: the variant's own field-level density Texture2D (painted independently).
    /// </summary>
    public sealed class GrassVariantScatterLayer : ScatterLayer, IDensityPlacementSource
    {
        private static readonly int ID_BaseMap = Shader.PropertyToID("_BaseMap");

        private GrassLayer layer = null!;
        private Material?  variantMaterial;     // runtime clone, owned + disposed here
        private Texture2D? densityMap;           // variant tile density (referenced, not owned)
        private string     variantId = string.Empty;
        private bool       useExplicitBounds;    // true for per-tile adapters
        private Vector2    explicitFieldBounds;  // tile-sized bounds when useExplicitBounds == true

        /// <summary>
        /// Builds a transient adapter for one variant (field-wide, global density).
        /// Clones the layer material and overrides <c>_BaseMap</c> with the variant texture.
        /// Caller owns the lifetime (destroy with the engine).
        /// </summary>
        public static GrassVariantScatterLayer Create(GrassLayer layer, int variantIndex, Texture2D? densityMap)
        {
            var adapter = CreateInstance<GrassVariantScatterLayer>();
            adapter.hideFlags          = HideFlags.HideAndDontSave;
            adapter.layer              = layer;
            adapter.variantId          = $"{layer.name}#{variantIndex}";
            adapter.densityMap         = densityMap;
            adapter.name               = adapter.variantId;
            adapter.useExplicitBounds  = false;
            adapter.explicitFieldBounds = layer.FieldBounds;

            GrassVariant v = layer.Palette[variantIndex];
            Material? baseMat = layer.Render.Material;
            if (baseMat != null)
            {
                adapter.variantMaterial = new Material(baseMat) { name = $"{adapter.variantId}_Mat" };
                if (v.texture != null)
                    adapter.variantMaterial.SetTexture(ID_BaseMap, v.texture);
            }
            return adapter;
        }

        /// <summary>
        /// Builds a tile-aware transient adapter for one (variant, tile). Uses a tile-sized
        /// <see cref="FieldBounds"/> and sets <see cref="UseExplicitFieldBounds"/> = true so
        /// <see cref="DensityPlacement"/> does not override bounds with terrain size.
        /// </summary>
        public static GrassVariantScatterLayer Create(
            GrassLayer layer, int variantIndex, Vector2Int tileCoord, Texture2D? tileDensityTex)
        {
            var adapter = CreateInstance<GrassVariantScatterLayer>();
            adapter.hideFlags          = HideFlags.HideAndDontSave;
            adapter.layer              = layer;
            adapter.variantId          = $"{layer.name}#{variantIndex}@{tileCoord}";
            adapter.densityMap         = tileDensityTex;
            adapter.name               = adapter.variantId;
            adapter.useExplicitBounds  = true;
            adapter.explicitFieldBounds = new Vector2(
                TerrainWorldGrid.TILE_SIZE_M, TerrainWorldGrid.TILE_SIZE_M);

            GrassVariant v = layer.Palette[variantIndex];
            Material? baseMat = layer.Render.Material;
            if (baseMat != null)
            {
                adapter.variantMaterial = new Material(baseMat) { name = $"{adapter.variantId}_Mat" };
                if (v.texture != null)
                    adapter.variantMaterial.SetTexture(ID_BaseMap, v.texture);
            }
            return adapter;
        }

        // ── ScatterLayer config accessors (shared from layer; material overridden) ──

        public override ScatterRenderConfig Render
        {
            get
            {
                ScatterRenderConfig src = this.layer.Render;
                Material? mat = this.variantMaterial != null ? this.variantMaterial : src.Material;
                return new ScatterRenderConfig(mat, src.ShadowCastingMode, src.Lods, src.RenderCullDistance);
            }
        }

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
                error = $"Variant '{this.variantId}' has no density map (not yet authored).";
                return false;
            }
            if (!this.densityMap.isReadable)
            {
                error = $"Variant '{this.variantId}' density map '{this.densityMap.name}' is not readable.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        // ── IDensityPlacementSource (variant density + shared placement params) ──

        public bool       UseExplicitFieldBounds => this.useExplicitBounds;
        public Texture2D? DensityMap     => this.densityMap;
        public int        TargetInstances => this.layer.TargetInstances;
        public int        Seed            => this.layer.Seed;
        public Vector2    SlopeRange      => this.layer.SlopeRange;
        public int        SplatLayerIndex => -1;   // variants drive placement from their own density, no splat mask
        public float      SplatThreshold  => 0.5f;
        public Vector2    RandomPitchRange => this.layer.RandomPitchRange;
        public Vector2    RandomRollRange  => this.layer.RandomRollRange;
        public bool       AlignToNormal    => this.layer.AlignToNormal;
        public float      MaxBladeHeight   => this.layer.Bounds.MaxBladeHeight;
        public float      BendHeadroom     => this.layer.Bounds.BendHeadroom;

        // ── Lifetime ────────────────────────────────────────────────────────────

        private void OnDisable() => this.DisposeVariantMaterial();
        private void OnDestroy() => this.DisposeVariantMaterial();

        private void DisposeVariantMaterial()
        {
            if (this.variantMaterial == null) return;
            if (Application.isPlaying) Destroy(this.variantMaterial);
            else DestroyImmediate(this.variantMaterial);
            this.variantMaterial = null;
        }
    }
}
