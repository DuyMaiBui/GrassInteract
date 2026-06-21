#nullable enable
// WorldPainterShaderStripper.cs — Phase 6a: IPreprocessShaders variant stripping
//
// SAFETY CONTRACT:
// ─────────────────────────────────────────────────────────────────────────────────
// This stripper ONLY fires for WorldPainter's own three shaders (gated by shader
// name). It does NOT touch any other shader in the project. Each stripped keyword
// is verified to be unreachable on the shipping Mobile/Medium URP profile:
//
//   _ADDITIONAL_LIGHTS / _ADDITIONAL_LIGHTS_VERTEX
//     Medium.asset = "Additional Lights: Disabled" (PerPixelLightCount=0, PerVertexCount=0).
//     No additional lights can be added to the WorldPainter field scene (single directional
//     sun only). Variants that sample additional lights consume register pressure for zero
//     runtime benefit. SAFE TO STRIP.
//
//   _ADDITIONAL_LIGHT_SHADOWS
//     Only relevant when _ADDITIONAL_LIGHTS is active (there must be lights to cast shadows).
//     Medium.asset disables additional lights → no additional-light shadows possible.
//     SAFE TO STRIP.
//
//   _SCREEN_SPACE_OCCLUSION
//     Medium tier has RendererFeatures: [] (confirmed from Medium.asset serialisation check
//     performed during Phase 1 build-validator work). SSAO is a RendererFeature; with none
//     registered it cannot run. TerrainPatch.shader includes the pragma; the variant would
//     bloat the build for zero effect. SAFE TO STRIP.
//
//   _FORWARD_PLUS
//     Forward+ requires a tile/cluster compute that is part of the Forward+ renderer path.
//     Medium.asset uses ForwardRenderer (not Forward+). SAFE TO STRIP.
//
//   LIGHTMAP_ON / DIRLIGHTMAP_COMBINED / SHADOWS_SHADOWMASK / LIGHTMAP_SHADOW_MIXING
//     WorldPainter's grass/terrain meshes are runtime-generated and therefore cannot be
//     baked into lightmaps. No lightmap UV channel exists; no lightmap asset is ever
//     assigned. These variants would run dead code (lightmap sample on texcoord1=0).
//     SAFE TO STRIP.
//
// CONSERVATIVE APPROACH — what we do NOT strip:
//   _MAIN_LIGHT_SHADOWS / _MAIN_LIGHT_SHADOWS_CASCADE / _MAIN_LIGHT_SHADOWS_SCREEN
//     The main directional light CAN cast shadows (configurable per scene). Keep.
//   _SHADOWS_SOFT
//     Soft shadow quality is a runtime-switchable quality setting. Keep.
//   All WorldPainter-custom keywords (_NORMALMAP, _PBR, _LOD2_BILLBOARD, etc.)
//     These are runtime-toggled per layer configuration. Keep.
//
// HOW TO REGENERATE THE ShaderVariantCollection:
//   1. Enter Play mode in the WorldPainter demo scene (walk through the grass field so
//      all LOD distances are triggered, brush through the terrain).
//   2. Open Window → Analysis → Shader Variant Collection.
//   3. Click "Save to asset" and point at
//      Assets/WorldPainter/Runtime/Resources/ShaderVariants/WorldPainterVariants.shadervariants.
//      (This is the Resources/ load path used by WorldPainterShaderWarmup.WarmUp.)
//   4. The warmup hook (WorldPainterShaderWarmup) will pick it up automatically.
// ─────────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Editor.Build
{
    /// <summary>
    /// Build-time shader-variant stripper for the WorldPainter grass/terrain shaders.
    ///
    /// Removes variants that are unreachable on the shipping Mobile/Medium URP pipeline
    /// (single directional light, no GI, no SSAO, Forward renderer — not Forward+).
    /// Gated strictly to WorldPainter's three shaders so no other project shader is affected.
    ///
    /// Win: smaller build size + eliminates first-use shader-compile hitches on mobile (the
    /// driver compiles each variant on first draw; fewer variants = fewer hitches = better
    /// 1% low frame-time).
    /// </summary>
    internal sealed class WorldPainterShaderStripper : IPreprocessShaders
    {
        // ── Shader names to gate stripping (exact match against Shader.name) ──

        private const string SHADER_INDIRECT_GRASS    = "WorldPainter/IndirectGrass";
        private const string SHADER_SCATTER_INSTANCED = "WorldPainter/ScatterInstanced";
        private const string SHADER_TERRAIN_PATCH     = "WorldPainter/TerrainPatch";

        // ── Keywords safe to strip from ALL three WorldPainter shaders ─────────
        //
        // These correspond to URP global multi_compile sets that generate many cross-product
        // variants. Each is verified unreachable on Medium (see safety contract above).
        private static readonly HashSet<string> ALWAYS_STRIP_KEYWORDS = new HashSet<string>
        {
            // Additional lights (Medium = disabled)
            "_ADDITIONAL_LIGHTS",
            "_ADDITIONAL_LIGHTS_VERTEX",
            // Additional-light shadows (no additional lights → no their shadows)
            "_ADDITIONAL_LIGHT_SHADOWS",
            // Forward+ (Medium uses ForwardRenderer, not Forward+)
            "_FORWARD_PLUS",
            // Lightmapping (runtime-generated meshes; no lightmap UV / lightmap asset)
            "LIGHTMAP_ON",
            "DIRLIGHTMAP_COMBINED",
            "SHADOWS_SHADOWMASK",
            "LIGHTMAP_SHADOW_MIXING",
        };

        // ── Keywords to strip only from TerrainPatch (which declares SSAO pragma) ─
        //
        // TerrainPatch.shader includes #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
        // because it uses UniversalFragmentPBR which implies that include. Medium tier has no
        // SSAO renderer feature. Grass shaders do NOT declare this pragma so they won't
        // encounter it, but we include it in the terrain-only set for safety.
        private static readonly HashSet<string> TERRAIN_ONLY_STRIP_KEYWORDS = new HashSet<string>
        {
            "_SCREEN_SPACE_OCCLUSION",
        };

        // IPreprocessShaders: lower = earlier in the chain.
        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            string shaderName = shader.name;

            // Gate: only process WorldPainter's own shaders.
            bool isIndirectGrass    = shaderName == SHADER_INDIRECT_GRASS;
            bool isScatterInstanced = shaderName == SHADER_SCATTER_INSTANCED;
            bool isTerrainPatch     = shaderName == SHADER_TERRAIN_PATCH;

            if (!isIndirectGrass && !isScatterInstanced && !isTerrainPatch)
                return;

            // Work backwards so removal by index is safe.
            for (int i = data.Count - 1; i >= 0; --i)
            {
                ShaderKeywordSet keywords = data[i].shaderKeywordSet;

                if (this.ShouldStrip(keywords, isTerrainPatch))
                    data.RemoveAt(i);
            }
        }

        private bool ShouldStrip(ShaderKeywordSet keywords, bool isTerrainPatch)
        {
            // Strip any variant that has at least one of the always-strip keywords enabled.
            foreach (string kw in ALWAYS_STRIP_KEYWORDS)
            {
                if (keywords.IsEnabled(new ShaderKeyword(kw)))
                    return true;
            }

            // Terrain-only additional strips.
            if (isTerrainPatch)
            {
                foreach (string kw in TERRAIN_ONLY_STRIP_KEYWORDS)
                {
                    if (keywords.IsEnabled(new ShaderKeyword(kw)))
                        return true;
                }
            }

            return false;
        }
    }
}
