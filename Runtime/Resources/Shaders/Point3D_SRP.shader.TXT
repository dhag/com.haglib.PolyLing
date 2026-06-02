// SRP(URP)専用頂点表示シェーダー
// Graphics.RenderPrimitives(topology=Triangles, vertexCount=vertexCount*6) で呼び出す
// 1点 = 2三角形(6頂点)のスクリーンスペース billboard
Shader "Poly_Ling/Point3D_SRP"
{
    Properties
    {
        _ScreenSpaceSize ("Screen Space Size (px)", Float) = 8.0
    }
    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+110"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── フラグ定数 ──────────────────────────────────────────
            #define FLAG_HIDDEN   4096u
            #define FLAG_CULLED   16384u

            // ── GPU バッファ ──────────────────────────────────────────
            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<uint>   _VertexFlagsBuffer;
            StructuredBuffer<uint>   _VertexCulledBuffer;

            // ── 色プロパティ ─────────────────────────────────────────
            float4 _ColorDefault;
            float4 _BorderColorDefault;
            float  _ScreenSpaceSize;

            // quad の 6 頂点 UV オフセット（2 三角形、CCW）
            static const float2 QUAD_UV[6] =
            {
                float2(0,0), float2(1,0), float2(0,1),
                float2(0,1), float2(1,0), float2(1,1)
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float4 fillColor   : COLOR0;
                float4 borderColor : COLOR1;
                float2 uv          : TEXCOORD0;
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;

                uint ptIdx     = vid / 6u;
                uint cornerIdx = vid % 6u;

                uint flags = _VertexFlagsBuffer[ptIdx];

                if ((flags & FLAG_HIDDEN) != 0u || _VertexCulledBuffer[ptIdx] != 0u)
                {
                    o.pos         = float4(99999.0, 99999.0, 99999.0, 1.0);
                    o.fillColor   = 0;
                    o.borderColor = 0;
                    o.uv          = 0;
                    return o;
                }

                float3 worldPos = _PositionBuffer[ptIdx];
                float4 clipPos  = TransformObjectToHClip(worldPos);

                // スクリーンスペース billboard 展開
                float2 uv        = QUAD_UV[cornerIdx];
                float2 offset    = (uv - 0.5) * 2.0;
                float2 pixelSize = _ScreenSpaceSize / _ScreenParams.xy;
                clipPos.xy      += offset * pixelSize * clipPos.w;

                o.pos         = clipPos;
                o.fillColor   = _ColorDefault;
                o.borderColor = _BorderColorDefault;
                o.uv          = uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (i.fillColor.a < 0.01 && i.borderColor.a < 0.01) discard;

                float2 center = i.uv - 0.5;
                float2 a      = abs(center);
                bool isBorder = a.x > 0.35 || a.y > 0.35;
                return isBorder ? i.borderColor : i.fillColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
