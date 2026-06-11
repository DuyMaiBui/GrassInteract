Shader "GpuTerrain/TerrainPatch"
{
    Properties
    {
        _MinHeight ("Min Height", Float) = 0
        _MaxHeight ("Max Height", Float) = 512
        [NoScaleOffset] _HeightTex ("Height Texture", 2D) = "black" {}
        _BaseColor ("Base Color", Color) = (0.4, 0.55, 0.3, 1)
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
            #pragma target 4.5          // requires structured buffers
            // Toggle: define to use pre-baked vertex Y (TEXCOORD1) instead of VTF.
            // #pragma multi_compile_local __ TERRAIN_VTF_FALLBACK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
            float   _MinHeight;
            float   _MaxHeight;
            float4  _BaseColor;

            Texture2D<float> _HeightTex;
            SAMPLER(sampler_HeightTex);

            // ── TerrainVtf helpers (inlined — no file include needed) ─────────

            float DecodeHeight(float raw)
            {
                return _MinHeight + raw * (_MaxHeight - _MinHeight);
            }

            float SampleHeightVTF(float2 uv)
            {
                float raw = _HeightTex.SampleLevel(sampler_HeightTex, uv, 0).r;
                return DecodeHeight(raw);
            }

            float2 MorphVertex(float2 vertexXZ, float morphBlend)
            {
                // vertexXZ is node-local UV [0,1] with step = 1/PATCH_RES.
                // Scale to integer-step space so even indices (0,2,4...) are unchanged,
                // odd indices snap to the preceding even step (crack-free coarser grid).
                const float patchRes = 16.0; // must match TerrainPatchMesh.PATCH_RES
                float2 vi       = vertexXZ * patchRes;
                float2 fracPart = frac(vi * 0.5) * 2.0; // 0 at even int, 1 at odd int
                return vertexXZ - (fracPart / patchRes) * morphBlend;
            }

            float CalcMorphBlend(float sqrDist, float morphStart, float morphEnd)
            {
                if (morphEnd <= morphStart) return 0.0;
                return saturate((sqrDist - morphStart) / (morphEnd - morphStart));
            }

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
                float3 normalWS   : TEXCOORD0;
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
                float3 camPos   = _WorldSpaceCameraPos;
                float3 delta    = vtxWorldXZ - camPos;
                float sqrDist   = dot(delta, delta);
                float morphBlend = CalcMorphBlend(sqrDist, node.morphStart, node.morphEnd);

                float2 morphedXZ = MorphVertex(vtxXZ, morphBlend);

                // 4. World position
                float worldX = node.worldOffset.x + morphedXZ.x * node.scale;
                float worldZ = node.worldOffset.z + morphedXZ.y * node.scale;

                // 5. Tile UV for height sample
                // Tile UV = (worldXZ - tileOrigin) / tileSize
                // node.worldOffset is tile-relative in Phase 1 (single tile at origin).
                // For Phase 3 multi-tile: tileOrigin would come from a tile array.
                float tileU = worldX / 256.0; // TILE_SIZE_M = 256
                float tileV = worldZ / 256.0;
                float2 tileUV = float2(tileU, tileV);

                // 6. Sample height via VTF
                float worldY = SampleHeightVTF(tileUV);

                float3 posWS = float3(worldX, worldY, worldZ);
                OUT.positionCS = TransformWorldToHClip(posWS);

                // 7. Simple upward normal (Phase 2 replaces with heightmap gradient)
                OUT.normalWS   = float3(0, 1, 0);
                OUT.tileUV     = tileUV;

                return OUT;
            }

            // ── Fragment shader ──────────────────────────────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                // Simple Lambert lit (Phase 2 replaces with full splat blend)
                InputData inputData;
                ZERO_INITIALIZE(InputData, inputData);
                inputData.normalWS        = normalize(IN.normalWS);
                inputData.positionWS      = float3(0, 0, 0);
                inputData.viewDirectionWS = float3(0, 0, 1);
                inputData.shadowCoord     = float4(0, 0, 0, 0);
                inputData.fogCoord        = 0;
                inputData.vertexLighting  = float3(0, 0, 0);
                inputData.bakedGI         = float3(0, 0, 0);

                SurfaceData surfaceData;
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                surfaceData.albedo     = _BaseColor.rgb;
                surfaceData.alpha      = 1.0;
                surfaceData.smoothness = 0.1;
                surfaceData.metallic   = 0.0;
                surfaceData.normalTS   = float3(0, 0, 1);
                surfaceData.occlusion  = 1.0;

                return UniversalFragmentPBR(inputData, surfaceData);
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
                float raw    = _HeightTex.SampleLevel(sampler_HeightTex, float2(worldX / 256.0, worldZ / 256.0), 0).r;
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
