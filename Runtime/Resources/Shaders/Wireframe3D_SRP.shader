// SRP(URP)専用ワイヤーフレームシェーダー
// Graphics.RenderPrimitives(topology=Lines, vertexCount=lineCount*2) で呼び出す
// SV_VertexID から lineIndex / endpoint を計算し、StructuredBuffer で頂点座標を取得する
Shader "Poly_Ling/Wireframe3D_SRP"
{
    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── フラグ定数 ──────────────────────────────────────────
            #define FLAG_HIDDEN   4096u   // 1 << 12
            #define FLAG_CULLED   16384u  // 1 << 14
            #define FLAG_AUX      65536u  // 1 << 16 (IsAuxLine)

            // ── GPU バッファ ──────────────────────────────────────────
            struct UnifiedLineData
            {
                uint V1, V2, Flags, FaceIndex, MeshIndex, ModelIndex;
            };

            StructuredBuffer<float3>          _PositionBuffer;
            StructuredBuffer<UnifiedLineData> _LineBuffer;
            StructuredBuffer<uint>            _LineFlagsBuffer;

            // ── 色プロパティ ─────────────────────────────────────────
            float4 _ColorUnselectedMesh;      // 非選択ライン
            float4 _ColorAuxLineUnselected;   // 補助線（非選択）

            // ── 頂点シェーダー ────────────────────────────────────────
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR0;
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;

                uint lineIdx = vid >> 1;  // vid / 2
                uint endPt   = vid &  1u; // vid % 2

                UnifiedLineData ln = _LineBuffer[lineIdx];
                uint flags = _LineFlagsBuffer[lineIdx];

                // 非表示・カリング済み → クリップ外へ飛ばす
                if ((flags & FLAG_HIDDEN) != 0u || (flags & FLAG_CULLED) != 0u)
                {
                    o.pos   = float4(99999.0, 99999.0, 99999.0, 1.0);
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                uint   vi  = (endPt == 0u) ? ln.V1 : ln.V2;
                float3 pos = _PositionBuffer[vi];
                o.pos      = TransformObjectToHClip(pos);

                bool isAux = (ln.Flags & FLAG_AUX) != 0u;
                o.color = isAux ? _ColorAuxLineUnselected : _ColorUnselectedMesh;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (i.color.a < 0.01) discard;
                return i.color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
