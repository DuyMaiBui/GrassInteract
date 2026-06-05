// Editor-only overlay shader for the scatter-paint brush cursor preview.
// Lives under Editor/ so it is stripped from runtime builds.
//
// Behaviour:
//   - Black pixels in _MainTex are treated as TRANSPARENT (luminance drives alpha, the
//     texture's own alpha channel is ignored). Lets B&W PNG brush stamps act as masks
//     without needing a real alpha channel.
//   - ZTest Always — the cursor draws on top of every scene object regardless of depth,
//     so trees / walls / props between the camera and the hit point can't occlude it.
//   - ZWrite Off + standard SrcAlpha,OneMinusSrcAlpha — alpha-blended over the surface.
//   - Cull Off — visible from both sides (slopes facing away from the camera still draw).
Shader "Hidden/GrassInteract/BrushCursor"
{
    Properties
    {
        _MainTex ("Stamp Texture", 2D)    = "white" {}
        _Color   ("Tint",          Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Overlay"
            "RenderType"     = "Overlay"
            "IgnoreProjector"= "True"
            "PreviewType"    = "Plane"
        }

        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4    _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                // Luminance-as-alpha — black pixels (lum=0) punch out, white pixels stay fully tinted.
                float  lum = max(max(tex.r, tex.g), tex.b);
                return fixed4(_Color.rgb, lum * _Color.a);
            }
            ENDCG
        }
    }
}
