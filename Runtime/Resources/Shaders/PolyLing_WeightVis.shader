// PolyLing_WeightVis.shader
// スキンウェイト可視化専用シェーダ。
// SkinWeightPaintTool.ApplyVisualizationColors が UnityMesh.colors へ焼き込んだ
// ヒートマップ色をそのまま出力する。GPU compute バッファには依存しない。
//
// 【Resources 配下必須】
// SkinWeightPaintTool.GetVisualizationMaterial が Shader.Find で取得する。
// Resources の外に置くとスタンドアロンビルドに含まれず Shader.Find が null を返し、
// 無音で可視化が消える。他の Shader.Find 対象シェーダと同じくここに置くこと。
//
// 【LightMode タグ必須】
// URP は LightMode タグで描画パスを選別する。省略すると URP の
// DrawObjectsPass から漏れて描画されない。他の overlay シェーダと同じく
// SRPDefaultUnlit を明記する。
//
// Queue は Geometry+1。通常面 (Queue=Geometry) と同一ジオメトリを同一行列で
// 重ね描きするため、必ず後に出す。深度は同値になるので ZTest LEqual で通る。

Shader "Hidden/PolyLing_WeightVis"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
