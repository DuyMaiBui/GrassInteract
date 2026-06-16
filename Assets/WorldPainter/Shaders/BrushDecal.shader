// Editor-only brush-mask preview decal. Draws a grayscale brush mask as a tinted, semi-
// transparent ground quad at the cursor so the artist sees the mask's SHAPE (not just a ring)
// while painting. Mask luminance (red channel) drives alpha — black = transparent, white =
// visible. ZTest Always so it shows through the GPU-rendered terrain; not depth-written.
Shader "Hidden/WorldPainter/BrushDecal"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color   ("Tint", Color) = (0.3, 0.7, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed mask = tex2D(_MainTex, i.uv).r; // grayscale brush mask → luminance
                return fixed4(_Color.rgb, mask * _Color.a);
            }
            ENDCG
        }
    }
}
