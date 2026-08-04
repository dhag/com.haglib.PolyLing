// PolyLing_GridAxis.shader
// 3Dプレビューの軸線・グリッド平面の描画専用シェーダ。
// GridAxisRenderer が構築する CPU Mesh（MeshTopology.Lines・頂点色に色が焼き込み済み）を
// そのまま描画する。GPU compute バッファには依存しない。
//
// Bone3D_Overlay とは異なり ZTest LEqual + Queue Transparent とし、
// モデルの手前にあるグリッド線だけが見える（モデル背面のグリッドは隠れる）。

Shader "Poly_Ling/GridAxis"
{
    Properties
    {
        _GlobalAlpha ("Global Alpha", Float) = 1.0
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
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            float _GlobalAlpha;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.color.a *= _GlobalAlpha;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (i.color.a < 0.01) discard;
                return i.color;
            }
            ENDCG
        }
    }
    FallBack Off
}
