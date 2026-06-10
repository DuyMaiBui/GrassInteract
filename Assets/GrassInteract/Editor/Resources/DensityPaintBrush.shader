// Hidden/GrassInteract/DensityPaintBrush
// GPU brush-splat shader. Renders a brush-mask quad in UV space into a linear R8 RenderTexture.
// Paint = additive clamp01, Erase = subtractive clamp01, Smooth = local-average lerp.
// No sRGB write — the RT is created with sRGB=false to preserve R8 density semantics.
//
// Stamp mask convention (SF-4):
//   _HasBrush == 0: no stamp — use purely procedural radial falloff (do NOT sample _BrushTex).
//   _HasBrush == 1: stamp present — sample _BrushTex.r (BrushStamp.Shape is Grayscale R8;
//                  white = full opacity, black = transparent edge; mask is in R channel).
//
// D3D11 same-RT aliasing fix (BLOCKER-1):
//   _DensityTex is always the SCRATCH RT (a stable copy of the canonical rt made before this
//   stamp render). The target RT (written via SV_Target) is the canonical rt — source and target
//   are ALWAYS distinct objects, never the same RenderTexture. No read-modify-write aliasing.
//
// Texel size (SF-3):
//   _TexelSize is set to (1/rt.width, 1/rt.height) by DensityPaintGPU.PaintAt so the smooth
//   kernel adapts to any map resolution. The old hardcoded 1/512 has been removed.
Shader "Hidden/GrassInteract/DensityPaintBrush"
{
    Properties
    {
        _BrushTex  ("Brush Mask (R channel)",  2D)    = "white" {}
        _Strength  ("Strength",                Float) = 1.0
        _Mode      ("Mode",                    Float) = 0.0   // 0=Paint 1=Erase 2=Smooth
        _Inner     ("Inner Radius [0,1]",      Float) = 0.5   // falloff flat zone, in brush UV
        _HasBrush  ("Has Stamp Texture",       Float) = 0.0   // 0=procedural, 1=sample _BrushTex.r
        _DensityTex("Current Density (scratch)",2D)  = "black" {}
        _TexelSize ("Density Texel Size",      Vector)= (0.00195, 0.00195, 0, 0) // 1/512 default
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Name "DensityPaint"
            Blend One Zero
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BrushTex;
            sampler2D _DensityTex;
            float     _Strength;
            float     _Mode;
            float     _Inner;
            float     _HasBrush;
            float4    _TexelSize; // .x = 1/width, .y = 1/height

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;   // brush-local UV [0,1]
                float2 uvFull : TEXCOORD1;   // UV in the full RT (scratch) space
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos    = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                // Convert clip-space position to UV for sampling the density scratch.
                // GL.LoadOrtho: clip.xy in [-1,1] for [0,1] input, flip Y on D3D.
                o.uvFull = o.pos.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
                o.uvFull.y = 1.0 - o.uvFull.y;
#endif
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // ── Brush weight ──────────────────────────────────────────────
                // Radial distance from quad centre in [-1,1] space → [0,1] at edge.
                float2 centred = i.uv * 2.0 - 1.0;
                float  dist    = length(centred);

                // Procedural falloff: flat at 1.0 inside _Inner, smooth-step to 0 at edge.
                float falloff;
                if (dist <= _Inner)
                {
                    falloff = 1.0;
                }
                else
                {
                    float t = (dist - _Inner) / max(0.0001, 1.0 - _Inner);
                    falloff = 1.0 - smoothstep(0.0, 1.0, t);
                }

                // Stamp mask: sample R channel only when a stamp is bound (_HasBrush == 1).
                // When _HasBrush == 0 (no stamp / procedural mode), brushMask stays 1.0
                // so the shape is entirely driven by the radial falloff above.
                float brushMask = 1.0;
                if (_HasBrush > 0.5)
                {
                    brushMask = tex2D(_BrushTex, i.uv).r;
                }

                float w = _Strength * brushMask * falloff;
                w = clamp(w, 0.0, 1.0);

                // ── Read current density from the SCRATCH texture (not the render target) ──
                // _DensityTex is always set to the scratch copy — see BLOCKER-1 comment above.
                float current = tex2D(_DensityTex, i.uvFull).r;

                // ── Mode-specific blend ───────────────────────────────────────
                float result;
                if (_Mode < 0.5)          // Paint: additive clamp
                {
                    result = clamp(current + w, 0.0, 1.0);
                }
                else if (_Mode < 1.5)     // Erase: subtractive clamp
                {
                    result = clamp(current - w, 0.0, 1.0);
                }
                else                      // Smooth: 4-cardinal-neighbour average, lerp by w
                {
                    // _TexelSize.xy = (1/rt.width, 1/rt.height) — set per-stamp from DensityPaintGPU.
                    float n0 = tex2D(_DensityTex, i.uvFull + float2( _TexelSize.x,  0)).r;
                    float n1 = tex2D(_DensityTex, i.uvFull + float2(-_TexelSize.x,  0)).r;
                    float n2 = tex2D(_DensityTex, i.uvFull + float2( 0,  _TexelSize.y)).r;
                    float n3 = tex2D(_DensityTex, i.uvFull + float2( 0, -_TexelSize.y)).r;
                    float avg = (current + n0 + n1 + n2 + n3) * 0.2;
                    result = lerp(current, avg, w);
                }

                return float4(result, result, result, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
