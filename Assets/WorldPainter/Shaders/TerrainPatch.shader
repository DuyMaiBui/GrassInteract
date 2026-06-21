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

        // Skirt depth (m): how far skirt-border vertices drop to cover inter-tile LOD-seam
        // cracks. Bound per-tile by GpuTerrainEngine (tile height range); this is the fallback.
        _SkirtDepth ("Skirt Depth (m)", Float) = 8

        // Phase 5b: baked RG normal texture (oct-encoded XZ). Bound per-tile by GpuTerrainEngine
        // when a baked normal is available. Absent → _WP_LIVE_SCULPT keyword active → 4-tap fallback.
        [NoScaleOffset] _BakedNormalTex ("Baked Normal (RG oct XZ)", 2D) = "grey" {}
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

            // Phase 5d: layer-count variants — cheaper palette loop for tiles with few layers.
            // GpuTerrainEngine.Build sets the active keyword based on the tile's layer count so
            // the dead alphamap samples and unrolled loop are compiled out in the cheaper variants.
            // Bucket thresholds: ≤4 layers → _TERRAIN_LAYERS_4, ≤8 → _TERRAIN_LAYERS_8, else _TERRAIN_LAYERS_16.
            // multi_compile (not shader_feature) so all three variants ship in the build and the
            // keyword can be toggled at runtime without stripping.
            #pragma multi_compile _TERRAIN_LAYERS_4 _TERRAIN_LAYERS_8 _TERRAIN_LAYERS_16

            // Phase 5b: live-sculpt path — derive normals from 4 height taps rather than baked texture.
            // GpuTerrainEngine enables this during BeginSculptPreview and on pre-Phase-5 tiles.
            // multi_compile so both variants always ship (keyword is set per-material at runtime).
            #pragma multi_compile _ _WP_LIVE_SCULPT

            // WorldPainter root-transform (painting space → world). Global keyword set by
            // WorldRootBinder; OFF variant is byte-identical to the pre-feature shader.
            #pragma multi_compile _ _WP_ROOT_TRANSFORM

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
            // WorldPainter root-transform: maps painting space → world (no-op when off).
            #include "WorldRootTransform.hlsl"

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
                // CDLOD morph uses the camera in PAINTING space so the GPU morph metric matches
                // the painting-space CDLOD selection (CdlodQuadtree.Select fed camera→painting).
                // No-op when _WP_ROOT_TRANSFORM is off.
                float3 camPos    = WP_WorldToPaintingPos(_WorldSpaceCameraPos);
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

                // 6b. Skirt drop: skirt-border vertices (positionOS.y = SKIRT_FLAG) push DOWN to
                // form a vertical curtain covering inter-tile LOD-seam cracks. No-op for surface
                // vertices. Applied in painting space so it scales with the root TRS below.
                worldY -= SkirtDrop(IN.positionOS.y);

                // Painting-space surface position → world (root TRS). tileUV/worldXZ stay in
                // painting space — height + palette are authored there; only the rendered
                // position maps to world. No-op when _WP_ROOT_TRANSFORM is off.
                float3 posWS = WP_PaintingToWorldPos(float3(worldX, worldY, worldZ));
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.positionWS = posWS;
                OUT.tileUV     = tileUV;
                OUT.worldXZ    = float2(worldX, worldZ);

                return OUT;
            }

            // ── Fragment shader (Phase 2 shading body) ───────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                // ── Normal: baked fetch (default) or 4-tap derivation (_WP_LIVE_SCULPT) ─
                // Phase 5b: SampleNormalWS dispatches on _WP_LIVE_SCULPT:
                //   OFF → single fetch from _BakedNormalTex (Phase 5b, runtime default).
                //   ON  → original 4-tap DeriveNormalWS (editor sculpting / pre-Phase-5 tiles).
                // Returns a PAINTING-space normal; mapped to world below.
                float3 normalWS = WP_PaintingToWorldNormal(SampleNormalWS(IN.tileUV));

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

            // Shadow geometry must apply the SAME root transform as the forward pass,
            // else the shadow silhouette desyncs from the lit surface under a non-identity root.
            #pragma multi_compile _ _WP_ROOT_TRANSFORM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Height decode + CDLOD morph helpers (SSOT). Provides _HeightTex / _MinHeight /
            // _MaxHeight / _TileOriginWS / _TileSizeM / DecodeHeight / SampleHeightVTF /
            // MorphVertex / CalcMorphBlend — do NOT re-declare these here.
            #include "TerrainVtf.hlsl"
            // WorldPainter root-transform (painting → world); same seam as the forward pass.
            #include "WorldRootTransform.hlsl"

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
                // Camera in PAINTING space so the morph metric matches the forward pass + selection.
                float2 camPaintXZ = WP_WorldToPaintingPos(_WorldSpaceCameraPos).xz;
                float2 deltaXZ    = vtxWorldXZ.xz - camPaintXZ;
                float  morphBlend = CalcMorphBlend(dot(deltaXZ, deltaXZ), node.morphStart, node.morphEnd);
                float2 morphedXZ  = MorphVertex(vtxXZ, morphBlend);

                float worldX = node.worldOffset.x + morphedXZ.x * node.scale;
                float worldZ = node.worldOffset.z + morphedXZ.y * node.scale;
                // Tile-local UV (subtract tile origin so non-(0,0) tiles are correct).
                float worldY = SampleHeightVTF((float2(worldX, worldZ) - _TileOriginWS) / _TileSizeM);
                // Skirt drop — identical to the forward pass so shadow geometry matches the curtain.
                worldY -= SkirtDrop(IN.positionOS.y);
                // Painting-space position → world (root TRS); same seam as the forward pass.
                float3 posWS = WP_PaintingToWorldPos(float3(worldX, worldY, worldZ));
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            float4 fragShadow(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
