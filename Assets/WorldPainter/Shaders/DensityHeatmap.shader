// Editor-only density-paint preview decal. Draws a tile's painted density RenderTexture (RFloat,
// red channel = density) as a green→yellow→red heatmap ground quad while a grass stroke is in
// progress. The grass-blade scatter is deferred to mouse-up (perf), so this overlay is the live
// painted-area indicator during the drag. Alpha is proportional to density so un-painted areas
// stay transparent. ZTest Always so it shows through the GPU-rendered terrain; not depth-written.
Shader "Hidden/WorldPainter/DensityHeatmap"
{
    Properties
    {
        _MainTex ("Density (RFloat)", 2D) = "black" {}
        _Color   ("Tint", Color) = (1, 1, 1, 1)
        _Scale   ("Density Scale", Float) = 1
        _MaxAlpha("Max Alpha", Range(0,1)) = 0.55
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
            float     _Scale;
            float     _MaxAlpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Density in [0,1] after scale. RFloat texture → red channel holds the value.
                float d = saturate(tex2D(_MainTex, i.uv).r * _Scale);

                // green (low) → yellow (mid) → red (high) ramp.
                fixed3 lowMid  = lerp(fixed3(0.15, 0.85, 0.25), fixed3(0.95, 0.90, 0.15), saturate(d * 2.0));
                fixed3 ramp    = lerp(lowMid, fixed3(0.95, 0.20, 0.15), saturate(d * 2.0 - 1.0));

                // Un-painted texels (d ≈ 0) stay transparent; alpha grows with density.
                fixed  a = d * _MaxAlpha * _Color.a;
                return fixed4(ramp * _Color.rgb, a);
            }
            ENDCG
        }
    }
}
