#nullable enable
using UnityEngine;

namespace GpuTerrain
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
        // ── Layer cap ─────────────────────────────────────────────────────────

        /// <summary>Maximum splat layers supported (drives Texture2DArray slice count).</summary>
        public const int MAX_SPLAT_LAYERS = 4;

        // ── Normal derivation ─────────────────────────────────────────────────

        /// <summary>
        /// UV offset (in tile UV space) used for central-difference normal derivation.
        /// Corresponds to one texel at a 512-texel height resolution = 1/512 ≈ 0.00195.
        /// Keep in sync with DEFAULT_HEIGHT_RES in TerrainWorldGrid.
        /// </summary>
        public const float DEFAULT_NORMAL_EPSILON = 1f / 512f;

        /// <summary>Shader property name for the normal epsilon uniform.</summary>
        public const string PROPERTY_NORMAL_EPSILON = "_NormalEpsilon";

        // ── Splat texture tiling ──────────────────────────────────────────────

        /// <summary>Default UV tiling applied to each splat layer texture (world-units per repeat).</summary>
        public const float DEFAULT_LAYER_TILING = 8f;

        /// <summary>Shader property name for the layer tiling uniform.</summary>
        public const string PROPERTY_LAYER_TILING = "_LayerTiling";

        // ── Splat texture bindings ────────────────────────────────────────────

        /// <summary>Material property name for the splat-weight RGBA texture (one texel per tile UV).</summary>
        public const string PROPERTY_SPLAT_TEX = "_SplatTex";

        /// <summary>Material property name for the Texture2DArray carrying layer albedos (≤4 slices).</summary>
        public const string PROPERTY_LAYER_ARRAY = "_LayerAlbedoArray";
    }
}
