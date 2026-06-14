#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Named constants for Phase 2 terrain shading.
    /// All shader literals must reference these — no magic numbers in HLSL files.
    ///
    /// Shader properties use these values via:
    ///   material.SetFloat(TerrainShadingConfig.PROPERTY_NORMAL_EPSILON, TerrainShadingConfig.DEFAULT_NORMAL_EPSILON);
    /// </summary>
    public static class TerrainShadingConfig
    {
        // ── Normal derivation ─────────────────────────────────────────────────

        /// <summary>
        /// UV offset (in tile UV space) used for central-difference normal derivation.
        /// Corresponds to one texel at a 512-texel height resolution = 1/512 ≈ 0.00195.
        /// Keep in sync with DEFAULT_HEIGHT_RES in TerrainWorldGrid.
        /// </summary>
        public const float DEFAULT_NORMAL_EPSILON = 1f / 512f;

        /// <summary>Shader property name for the normal epsilon uniform.</summary>
        public const string PROPERTY_NORMAL_EPSILON = "_NormalEpsilon";

        // ── Terrain palette (replaces the legacy SplatLayer / TerrainLayerSet path) ──

        /// <summary>
        /// Maximum number of TerrainLayer entries the palette + shader will sample.
        /// Drives the slice cap on <see cref="PROPERTY_PALETTE_ARRAY"/> and the unrolled loop
        /// in <c>TerrainPalette.hlsl</c>. Mobile-friendly upper bound; raise once the shader
        /// switches to a `#pragma multi_compile` per-alphamap-count strategy.
        /// </summary>
        public const int MAX_TERRAIN_LAYERS = 16;

        /// <summary>Maximum number of per-tile RGBA32 alphamap textures = ceil(MAX_TERRAIN_LAYERS / 4).</summary>
        public const int MAX_TERRAIN_ALPHAMAPS = 4;

        /// <summary>Texture2DArray of palette diffuse textures (up to MAX_TERRAIN_LAYERS slices).</summary>
        public const string PROPERTY_PALETTE_ARRAY = "_TerrainPaletteArray";

        /// <summary>int — number of active palette layers (≤ MAX_TERRAIN_LAYERS).</summary>
        public const string PROPERTY_PALETTE_COUNT = "_TerrainPaletteCount";

        /// <summary>Vector4[] — per-layer (.xy = tileSize, .zw = tileOffset). Size = MAX_TERRAIN_LAYERS.</summary>
        public const string PROPERTY_PALETTE_TILINGS = "_TerrainPaletteTilings";

        /// <summary>Per-tile alphamap texture names — bound from <c>TerrainTileAsset.alphamaps</c>.</summary>
        public const string PROPERTY_ALPHAMAP_0 = "_TerrainAlphamap0";
        public const string PROPERTY_ALPHAMAP_1 = "_TerrainAlphamap1";
        public const string PROPERTY_ALPHAMAP_2 = "_TerrainAlphamap2";
        public const string PROPERTY_ALPHAMAP_3 = "_TerrainAlphamap3";

        /// <summary>int — number of active alphamap textures for this tile.</summary>
        public const string PROPERTY_ALPHAMAP_COUNT = "_TerrainAlphamapCount";
    }
}
