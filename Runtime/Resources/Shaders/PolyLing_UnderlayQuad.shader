// PolyLing_UnderlayQuad.shader
// 作業空間の下絵を 3D の四角形として描く専用シェーダ（ロック解除中の下絵。設計方針 8.1）。
// PlateQuadRenderer が作る CPU Mesh（単位四角形・UV 付き）に画像を貼る。光は受けない。
// GridAxis と同じく ZTest LEqual + Queue Transparent + ZWrite Off で、
// モデルの手前にあるときだけ見え、モデルの奥行きを書き換えない。両面を描く。

Shader "Poly_Ling/UnderlayQuad"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color   ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4    _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv) * i.color;
                if (c.a < 0.01) discard;
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
