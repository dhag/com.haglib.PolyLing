// MeshFactoryBone3D_Overlay.shader
// Phase 2c-2: ボーン wire の overlay 描画専用シェーダ。
// MeshSceneRenderer.DrawBones が BuildBoneLineMesh で構築する CPU Mesh
// （頂点色にボーンの選択/非選択色が焼き込まれている）をそのまま描画する。
// GPU compute バッファ (_LineFlagsBuffer 等) には依存しない。
// ZTest Always で常に最前面に表示されるため、ボーンがボディに埋もれない。

Shader "Poly_Ling/Bone3D_Overlay"
{
    Properties
    {
        _GlobalAlpha ("Global Alpha", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
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
                // 非選択ボーンを半透明にするため global alpha を乗算する
                // （選択・非選択の個別マテリアルで _GlobalAlpha を切り替える想定）。
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
