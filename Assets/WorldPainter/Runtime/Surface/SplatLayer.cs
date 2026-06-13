#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Unified SPLAT authoring layer. Wraps a <see cref="TerrainLayerSet"/> (the albedo-palette
    /// SSOT) into the <see cref="WorldPainterLayer"/> hierarchy so splat and grass live in one
    /// <see cref="WorldMapAsset.SurfaceLayers"/> list.
    ///
    /// The asset-based <see cref="TerrainLayerSet"/> is the single source of truth for
    /// splat albedo data (SplatLayerDef and inline splatLayers[] removed in Phase 5b).
    ///
    /// Painted control = the per-tile RGBA weight map (<c>TerrainTileAsset.splatData</c>, uploaded
    /// as <see cref="TerrainTileGpuResources.SplatTexture"/>); blending is done by
    /// <c>TerrainPatch.shader</c> from <see cref="TerrainLayerSet.BuildArray"/>.
    /// </summary>
    public sealed class SplatLayer : WorldPainterLayer
    {
        [Tooltip("Albedo-palette source. Builds the _LayerAlbedoArray bound by GpuTerrainEngine.")]
        [SerializeField] private TerrainLayerSet? layerSet;

        /// <summary>The albedo-palette set (SSOT). Null when not yet assigned.</summary>
        public TerrainLayerSet? LayerSet => this.layerSet;

        public override LayerKind Kind => LayerKind.Splat;

        public override int PaletteCount =>
            this.layerSet != null ? this.layerSet.ActiveLayerCount : 0;

        /// <summary>Assigns the palette set. Editor lifecycle only.</summary>
        internal void SetLayerSet(TerrainLayerSet set) => this.layerSet = set;
    }
}
