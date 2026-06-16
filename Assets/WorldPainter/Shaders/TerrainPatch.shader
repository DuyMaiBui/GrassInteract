Shader "WorldPainter/TerrainPatch"
{
    Properties
    {
        _MinHeight ("Min Height", Float) = 0
        _MaxHeight ("Max Height", Float) = 512
        [NoScaleOffset] _HeightTex ("Height Texture", 2D) = "black" {}

        _NormalEpsilon ("Normal Epsilon", Float) = 0.00195

        // ── TerrainPalette properties ────────────────────────────────────────
        // These are populated by TerrainPaletteBinder (palette array) and
        // GpuTerrainEngine (per-tile alphamaps + count). Defaults are inert so a
        // material missing the binder simply shows the fallback colour.
        [NoScaleOffset] _TerrainPaletteArray   ("Terrain Palette Array",  2DArray) = "" {}
        [NoScaleOffset] _TerrainAlphamap0      ("Alphamap 0 (R=L0 G=L1 B=L2 A=L3)", 2D) = "black" {}
        [NoScaleOffset] _TerrainAlphamap1      ("Alphamap 1 (L4..L7)", 2D) = "black" {}
        [NoScaleOffset] _TerrainAlphamap2      ("Alphamap 2 (L8..L11)", 2D) = "black" {}
        [NoScaleOffset] _TerrainAlphamap3      ("Alphamap 3 (L12..L15)", 2D) = "black" {}

        // Fallback base color used when no LayerAlbedoArray is bound.
        _BaseColor ("Base Color (Fallback)", Color) = (0.4, 0.55, 0.3, 1)

        // Anti-tiling: per-cell rotated stochastic albedo sampling (2-tap).
        // Toggles the _TERRAIN_STOCHASTIC keyword on the forward pass.
        [Toggle(_TERRAIN_STOCHASTIC)] _StochasticTiling ("Stochastic Tiling (anti-repeat)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5          // structured buffers + texture arrays

            // URP forward-lighting keywords. Without these the URP ShaderLibrary compiles
            // out its real-time lighting / GI paths and UniversalFragmentPBR returns black
            // even with a valid main light. Mirrors the Lit.shader ForwardLit pass.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            // Anti-tiling stochastic albedo path (paired with [Toggle] _StochasticTiling).
            // multi_compile (not shader_feature) so the variant survives runtime/quality-tier toggling.
            #pragma multi_compile _ _TERRAIN_STOCHASTIC

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── TerrainVtf (height decode + morph helpers, SSOT) ────────────
            #include "TerrainVtf.hlsl"

            // ── Normal + palette helpers ────────────────────────────────────
            // TerrainVtf.hlsl included above → DecodeHeight / SampleHeightVTF available.
            #include "TerrainNormals.hlsl"
            // Palette-driven (Unity-Terrain-style) blend with up to 16 layers
            // across 4 alphamaps.
            #include "TerrainPalette.hlsl"

            // ── Node buffer ──────────────────────────────────────────────────
            // RenderNode layout must match CdlodNode.STRIDE = 32 B EXACTLY.
            struct RenderNode
            {
                float3 worldOffset;
                float  scale;
                uint   lod;
                float  morphStart;
                float  morphEnd;
                uint   tileIdx;
            };

            StructuredBuffer<uint>       _VisibleNodeIndices; // index → node slot
            StructuredBuffer<RenderNode> _NodeBuffer;         // all submitted nodes

            // ── Per-material uniforms ────────────────────────────────────────
            float4  _BaseColor;

            // ── Vertex input ─────────────────────────────────────────────────
            struct Attributes
            {
                float3 positionOS : POSITION;   // mesh local [0,1] XZ, Y=0
                float2 uv         : TEXCOORD0;  // same as positionOS.xz
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 tileUV     : TEXCOORD1;
                float2 worldXZ    : TEXCOORD2; // for per-layer palette UV
            };

            // ── Vertex shader ────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // 1. Resolve node from visible index list
                uint nodeIdx = _VisibleNodeIndices[IN.instanceID];
                RenderNode node = _NodeBuffer[nodeIdx];

                // 2. World XZ = nodeOrigin + vertexUV * nodeScale
                float2 vtxXZ = IN.uv;

                // 3. Morph: blend XZ toward 2× coarser grid by distance
                float3 vtxWorldXZ = float3(
                    node.worldOffset.x + vtxXZ.x * node.scale,
                    0.0,
                    node.worldOffset.z + vtxXZ.y * node.scale);
                float3 camPos    = _WorldSpaceCameraPos;
                // CDLOD crack fix: the LOD quadtree selects nodes and builds morphStart/morphEnd
                // from XZ-ONLY distance (CdlodQuadtree.SqrDistXZ ignores camera height). The morph
                // blend MUST use the SAME metric — otherwise vtxWorldXZ.y=0 vs camPos.y folds the
                // camera height into sqrDist, so a finer chunk's edge is not driven to morphBlend=1
                // at the LOD transition and a Y T-junction (crack) opens between tiles/chunks once
                // the terrain has height variation. Use horizontal distance only.
                float2 deltaXZ   = vtxWorldXZ.xz - camPos.xz;
                float  sqrDist   = dot(deltaXZ, deltaXZ);
                float  morphBlend = CalcMorphBlend(sqrDist, node.morphStart, node.morphEnd);

                float2 morphedXZ = MorphVertex(vtxXZ, morphBlend);

                // 4. World position
                float worldX = node.worldOffset.x + morphedXZ.x * node.scale;
                float worldZ = node.worldOffset.z + morphedXZ.y * node.scale;

                // 5. Tile-LOCAL UV for height/splat sample (B1 fix).
                // Subtract the tile's world-space min corner so non-(0,0) tiles map
                // to [0,1] correctly. _TileOriginWS and _TileSizeM are bound per-material
                // in GpuTerrainEngine.Build from TerrainWorldGrid.TileOriginWorld / TILE_SIZE_M.
                float2 tileUV = (float2(worldX, worldZ) - _TileOriginWS) / _TileSizeM;

                // 6. Sample height via VTF (TerrainVtf.hlsl)
                float worldY = SampleHeightVTF(tileUV);

                float3 posWS = float3(worldX, worldY, worldZ);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.positionWS = posWS;
                OUT.tileUV     = tileUV;
                OUT.worldXZ    = float2(worldX, worldZ);

                return OUT;
            }

            // ── Fragment shader (Phase 2 shading body) ───────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                // ── Normal: derived from heightmap central-difference ─────────
                // TerrainNormals.hlsl: DeriveNormalWS uses SampleHeightVTF + _NormalEpsilon.
                float3 normalWS = DeriveNormalWS(IN.tileUV);

                // ── Albedo: TerrainPalette-blended texture array ──────────────
                // TerrainPalette.hlsl: BlendTerrainPalette samples N alphamaps
                // (RGBA = 4 palette weights each) and accumulates per-layer
                // diffuse samples scaled by the layer's tileSize/tileOffset.
                // Phase 3 cleanup will delete the legacy SplatBlend path; for
                // now we keep the SplatBlend call out of the active codepath
                // so an unconfigured palette doesn't crash existing scenes.
                float4 albedo = BlendTerrainPalette(IN.tileUV, IN.worldXZ);

                // ── URP Lit output ────────────────────────────────────────────
                InputData inputData;
                ZERO_INITIALIZE(InputData, inputData);
                inputData.normalWS        = normalWS;
                inputData.positionWS      = IN.positionWS;
                inputData.viewDirectionWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = ComputeFogFactor(
                    TransformWorldToHClip(IN.positionWS).z);
                inputData.vertexLighting  = float3(0, 0, 0);
                inputData.bakedGI         = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = float2(0, 0);

                SurfaceData surfaceData;
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.albedo     = albedo.rgb;
                surfaceData.alpha      = 1.0;
                surfaceData.smoothness = 0.1;
                surfaceData.metallic   = 0.0;
                surfaceData.normalTS   = float3(0, 0, 1);
                surfaceData.occlusion  = 1.0;

                float4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }

            ENDHLSL
        }

        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Height decode + CDLOD morph helpers (SSOT). Provides _HeightTex / _MinHeight /
            // _MaxHeight / _TileOriginWS / _TileSizeM / DecodeHeight / SampleHeightVTF /
            // MorphVertex / CalcMorphBlend — do NOT re-declare these here.
            #include "TerrainVtf.hlsl"

            struct RenderNode
            {
                float3 worldOffset;
                float  scale;
                uint   lod;
                float  morphStart;
                float  morphEnd;
                uint   tileIdx;
            };

            StructuredBuffer<uint>       _VisibleNodeIndices;
            StructuredBuffer<RenderNode> _NodeBuffer;

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                uint nodeIdx = _VisibleNodeIndices[IN.instanceID];
                RenderNode node = _NodeBuffer[nodeIdx];

                // Match the forward pass: CDLOD morph with the XZ-ONLY metric (same as
                // CdlodQuadtree selection) so shadow geometry tracks the morphed lit surface at
                // LOD seams. Without this the shadow used the finest grid while the lit surface
                // morphed → shadow silhouette mismatch / acne along LOD boundaries.
                float2 vtxXZ      = IN.uv;
                float3 vtxWorldXZ = float3(node.worldOffset.x + vtxXZ.x * node.scale, 0.0,
                                           node.worldOffset.z + vtxXZ.y * node.scale);
                float2 deltaXZ    = vtxWorldXZ.xz - _WorldSpaceCameraPos.xz;
                float  morphBlend = CalcMorphBlend(dot(deltaXZ, deltaXZ), node.morphStart, node.morphEnd);
                float2 morphedXZ  = MorphVertex(vtxXZ, morphBlend);

                float worldX = node.worldOffset.x + morphedXZ.x * node.scale;
                float worldZ = node.worldOffset.z + morphedXZ.y * node.scale;
                // Tile-local UV (subtract tile origin so non-(0,0) tiles are correct).
                float worldY = SampleHeightVTF((float2(worldX, worldZ) - _TileOriginWS) / _TileSizeM);
                float3 posWS = float3(worldX, worldY, worldZ);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            float4 fragShadow(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
