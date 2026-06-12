// Editor-only brush cursor decal. Draws ON TOP of the terrain (ZTest Always) so the
// conforming brush disc is never occluded by the GPU-rendered surface — whose CDLOD
// morph/skirt geometry can dip below the CPU heightmap the disc samples. Used via
// Graphics.DrawMeshNow + SetPass from TerrainBrushPreview (immediate mode, so a plain
// unlit CG pass is intentional — no SRP batching needed).
Shader "WorldPainter/BrushDecal"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always                       // always draw over the terrain
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4    _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
