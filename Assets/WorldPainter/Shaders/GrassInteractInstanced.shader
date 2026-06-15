// Dumb instanced grass renderer.
//
// This shader does NO motion. It draws the blade mesh at the per-instance object-to-world matrix
// (unity_ObjectToWorld) and colors it by a height gradient (uv.y: _BaseColor at the root,
// _TipColor at the tip, times an optional _BaseMap). ALL motion - bend, wind, yaw, scale -
// is baked into the per-instance matrix by GrassBendSimulator (C#); with a plain static matrix
// it is just static placement. No deform include, no wind, no trample sampling, no _Grass* globals.
//
// ESCAPE HATCH (documented, unimplemented): if a future mobile tune wants cheap ambient sway
// without a CPU pass, add a single _Time-driven horizontal offset in vert(), scaled by heightT,
// e.g. posWS.xz += float2(sin(_Time.y + posWS.x), 0) * heightT * _SwayAmount. Left out by design:
// the C# matrix path is the source of truth for motion.
Shader "WorldPainter/InstancedGrass"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.20, 0.45, 0.12, 1)
        _TipColor  ("Tip Color",  Color) = (0.55, 0.85, 0.30, 1)
        [NoScaleOffset] _BaseMap ("Base Map (optional)", 2D) = "white" {}
        _BaseMap_ST ("Base Map Tiling", Vector) = (1,1,0,0)
        [Toggle(_ALPHACLIP)] _Alphaclip ("Alpha Clip (transparent cards)", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 100
        Cull Off // blades are single-sided strips; render both faces

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // Cutout grass cards (BaseMap with an alpha channel): discard fragments under _Cutoff
            // so transparent texels disappear instead of rendering as solid blade quads.
            #pragma shader_feature_local _ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float4 _BaseMap_ST;
                float  _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  heightT    : TEXCOORD1;
            };

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                // No deform: the per-instance matrix already carries all motion (once C# is live).
                float3 posWS   = TransformObjectToWorld(input.positionOS.xyz);
                float  heightT = saturate(input.uv.y);

                output.positionCS = TransformWorldToHClip(posWS);
                output.uv         = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.heightT    = heightT;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                #if defined(_ALPHACLIP)
                    clip(tex.a - _Cutoff);
                #endif
                half3 col = lerp(_BaseColor.rgb, _TipColor.rgb, input.heightT) * tex.rgb;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Casts shadows from the matrix-placed blade. Motion is in the matrix, which this pass also
        // receives via unity_ObjectToWorld, so the shadow silhouette matches the visible blade.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // Mirror the forward keyword so the shadow silhouette matches the visible blade.
            #pragma shader_feature_local _ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            #if defined(_ALPHACLIP)
                TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
                float4 _BaseMap_ST;
                float  _Cutoff;
            #endif

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHACLIP)
                    float2 uv : TEXCOORD0;
                #endif
            };

            float4 ShadowClipPos(float3 posWS, float3 nrmWS)
            {
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            ShadowVaryings shadowVert (ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                // No deform: plain world placement from the per-instance matrix.
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = ShadowClipPos(posWS, nrmWS);
                #if defined(_ALPHACLIP)
                    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                #endif
                return output;
            }

            half4 shadowFrag (ShadowVaryings input) : SV_Target
            {
                #if defined(_ALPHACLIP)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // Writes depth from the matrix-placed blade so depth-based effects (fog, soft particles) line up.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            // Mirror the forward keyword so the depth-prepass silhouette matches.
            #pragma shader_feature_local _ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #if defined(_ALPHACLIP)
                TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
                float4 _BaseMap_ST;
                float  _Cutoff;
            #endif

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHACLIP)
                    float2 uv : TEXCOORD0;
                #endif
            };

            DepthVaryings depthVert (DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                // No deform: plain world placement from the per-instance matrix.
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionCS = TransformWorldToHClip(posWS);
                #if defined(_ALPHACLIP)
                    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                #endif
                return output;
            }

            half4 depthFrag (DepthVaryings input) : SV_Target
            {
                #if defined(_ALPHACLIP)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
