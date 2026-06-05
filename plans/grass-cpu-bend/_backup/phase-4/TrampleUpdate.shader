// Fade + vector-bake update for the GrassInteract trample RenderTexture.
// Driven by GrassTrampleMap via a ping-pong Graphics.Blit (pipeline-agnostic — not part of URP's
// per-camera loop). Reads the previous map as _MainTex, fades its MAGNITUDE toward 0 (recovery), then
// writes, per texel, the push VECTOR (away from the dominant interactor) + magnitude of the strongest
// footprint: float4(pushDir.xy, magnitude, 0) into an ARGBHalf RT. The grass deform reads this vector
// directly — no gradient reconstruction — which removes the old zero-gradient-core and overshoot defects.
// Format history: R8 render textures sample as 0 in the grass shader on this URP/GPU path; RHalf samples
// correctly, and ARGBHalf (same half precision, more channels) does too. See GrassTrampleMap.CreateRT.
Shader "GrassInteract/TrampleUpdate"
{
    Properties
    {
        _MainTex ("Previous Trample", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_TRAMPLE_INTERACTORS 16

            sampler2D _MainTex;
            float  _TrampleFade;                                  // saturate(1 - recoveryPerSec * dt)
            float4 _GrassFieldRect;                               // (minX, minZ, sizeX, sizeZ)
            float4 _TrampleInteractors[MAX_TRAMPLE_INTERACTORS];  // xy = world XZ, z = radius, w = strength
            int    _TrampleInteractorCount;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Previous frame: rg = push direction (world XZ), b = magnitude. Fade only the MAGNITUDE
                // (recovery) but keep the direction, so a fading trail still leans blades the right way.
                float4 prev     = tex2D(_MainTex, i.uv);
                float2 prevDir  = prev.rg;
                float  fadedMag = prev.b * _TrampleFade;

                // World XZ of this texel from its UV across the field rect.
                float2 wpos = _GrassFieldRect.xy + i.uv * _GrassFieldRect.zw;

                // This frame's STRONGEST interactor at this texel wins both direction and magnitude
                // (mirrors the old scalar max() recovery semantics, now vector-valued).
                float  splatMag = 0.0;
                float2 splatDir = float2(0.0, 0.0);
                [loop]
                for (int k = 0; k < _TrampleInteractorCount; ++k)
                {
                    float2 ip = _TrampleInteractors[k].xy;
                    float  r  = max(_TrampleInteractors[k].z, 1e-4);
                    float  s  = _TrampleInteractors[k].w;
                    float2 delta = wpos - ip;
                    float  d  = length(delta);
                    float  m  = s * saturate(1.0 - d / r);
                    if (m > splatMag)
                    {
                        splatMag = m;
                        // Lean AWAY from the interactor center. Undefined exactly at the center (d~0):
                        // store zero there — magnitude is maximal and the deform mats the core straight
                        // DOWN via _GrassFlatten, so a zero lateral dir at the core is correct.
                        splatDir = d > 1e-4 ? (delta / d) : float2(0.0, 0.0);
                    }
                }

                // Keep whichever is stronger: this frame's splat, or the faded previous trail.
                if (splatMag >= fadedMag)
                    return float4(splatDir, splatMag, 0.0);
                return float4(prevDir, fadedMag, 0.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
