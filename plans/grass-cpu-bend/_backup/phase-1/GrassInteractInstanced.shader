// Trample sampling uses sampler_LinearClamp (Unity built-in, declared in GlobalSamplers.hlsl which
// URP Core.hlsl already includes). GrassInteractDeform.hlsl also includes it explicitly for clarity.
// Root-cause note: the prior name sampler_linear_clamp (lowercase) is undeclared in URP — DX11
// silently returns 0 for an undeclared SamplerState, causing zero bend. See phase-0.md verdict.
Shader "GrassInteract/InstancedGrass"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.20, 0.45, 0.12, 1)
        _TipColor  ("Tip Color",  Color) = (0.55, 0.85, 0.30, 1)
        [NoScaleOffset] _BaseMap ("Base Map (optional)", 2D) = "white" {}
        _BaseMap_ST ("Base Map Tiling", Vector) = (1,1,0,0)
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // GrassInteract deform: shared field-rect UV mapping + ambient wind + vector-field trample lean.
            #include "Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float4 _BaseMap_ST;
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

                float3 posWS   = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS   = TransformObjectToWorldNormal(input.normalOS);
                float3 pivotWS = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float  heightT = saturate(input.uv.y);

                // Vector-field trample lean + ambient wind.
                GrassInteract_ApplyDeform(posWS, nrmWS, pivotWS, heightT);

                output.positionCS = TransformWorldToHClip(posWS);
                output.uv         = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.heightT    = heightT;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 col = lerp(_BaseColor.rgb, _TipColor.rgb, input.heightT) * tex.rgb;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Casts shadows with the SAME deform so shadow silhouettes match the visible blades.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

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
            };

            float4 DeformedShadowClipPos(float3 posWS, float3 nrmWS)
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

                float3 posWS   = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS   = TransformObjectToWorldNormal(input.normalOS);
                float3 pivotWS = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float  heightT = saturate(input.uv.y);
                // SSOT: mirror of GrassInteractDeform.hlsl GrassInteract_ApplyDeform.
                // Include path works now (sampler fix). If include ever fails again, inline here
                // and add the SSOT fence comment per plans/grass-lean-away-deform/phase-0.md.
                GrassInteract_ApplyDeform(posWS, nrmWS, pivotWS, heightT);

                output.positionCS = DeformedShadowClipPos(posWS, nrmWS);
                return output;
            }

            half4 shadowFrag (ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Writes deformed depth so depth-based effects (fog, soft particles) line up with the visible blades.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl"

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
            };

            DepthVaryings depthVert (DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 posWS   = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS   = TransformObjectToWorldNormal(input.normalOS);
                float3 pivotWS = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;
                float  heightT = saturate(input.uv.y);
                GrassInteract_ApplyDeform(posWS, nrmWS, pivotWS, heightT);

                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 depthFrag (DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
