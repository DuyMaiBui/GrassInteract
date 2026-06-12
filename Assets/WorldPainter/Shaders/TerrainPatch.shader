Shader "WorldPainter/TerrainPatch"
{
    Properties
    {
        _MinHeight ("Min Height", Float) = 0
        _MaxHeight ("Max Height", Float) = 512
        [NoScaleOffset] _HeightTex ("Height Texture", 2D) = "black" {}

        // ── Phase 2: splat shading properties ───────────────────────────────
        [NoScaleOffset] _SplatTex ("Splat Weights (RGBA)", 2D) = "red" {}
        _LayerAlbedoArray ("Layer Albedo Array", 2DArray) = "" {}
        _LayerTiling ("Layer Tiling", Float) = 8
        _NormalEpsilon ("Normal Epsilon", Float) = 0.00195

        // Fallback base color used when no LayerAlbedoArray is bound.
        _BaseColor ("Base Color (Fallback)", Color) = (0.4, 0.55, 0.3, 1)
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── TerrainVtf (height decode + morph helpers, SSOT) ────────────
            #include "TerrainVtf.hlsl"

            // ── Phase 2: splat + normal helpers ─────────────────────────────
            // TerrainVtf.hlsl included above → DecodeHeight / SampleHeightVTF available.
            #include "TerrainNormals.hlsl"
            #include "TerrainSplat.hlsl"

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
                float3 delta     = vtxWorldXZ - camPos;
                float  sqrDist   = dot(delta, delta);
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

                return OUT;
            }

            // ── Fragment shader (Phase 2 shading body) ───────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                // ── Normal: derived from heightmap central-difference ─────────
                // TerrainNormals.hlsl: DeriveNormalWS uses SampleHeightVTF + _NormalEpsilon.
                float3 normalWS = DeriveNormalWS(IN.tileUV);

                // ── Albedo: splat-blended texture array ───────────────────────
                // TerrainSplat.hlsl: SplatBlend samples _SplatTex (RGBA weights)
                // then blends _LayerAlbedoArray slices.
                // SSOT channel→layer: R=0(ground) G=1(grass) B=2(rock) A=3(path).
                float4 albedo = SplatBlend(IN.tileUV);

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
            float _MinHeight;
            float _MaxHeight;
            Texture2D<float> _HeightTex;
            SAMPLER(sampler_HeightTex);
            float2 _TileOriginWS;   // tile world-space min corner (B1 fix)
            float  _TileSizeM;      // tile side length in metres (B1 fix + MINOR-2)

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            float DecodeHeight(float raw) { return _MinHeight + raw * (_MaxHeight - _MinHeight); }

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                uint nodeIdx = _VisibleNodeIndices[IN.instanceID];
                RenderNode node = _NodeBuffer[nodeIdx];
                float worldX = node.worldOffset.x + IN.uv.x * node.scale;
                float worldZ = node.worldOffset.z + IN.uv.y * node.scale;
                // Tile-local UV (B1 fix — subtract tile origin so non-(0,0) tiles are correct).
                float raw    = _HeightTex.SampleLevel(sampler_HeightTex, (float2(worldX, worldZ) - _TileOriginWS) / _TileSizeM, 0).r;
                float worldY = DecodeHeight(raw);
                float3 posWS = float3(worldX, worldY, worldZ);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            float4 fragShadow(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
