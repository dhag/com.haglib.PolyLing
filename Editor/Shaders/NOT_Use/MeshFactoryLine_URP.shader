//ëΩï™ÉoÉOÇ†ÇËÅB
Shader "MeshFactory/Line"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 2.0
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 0.9)
        _AuxLineColor ("Aux Line Color", Color) = (1, 0.3, 1, 0.9)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct LineSegment { int v1; int v2; int faceIndex; int lineType; };
            StructuredBuffer<float4> _ScreenPositionBuffer;
            StructuredBuffer<LineSegment> _LineBuffer;
            StructuredBuffer<float> _LineVisibilityBuffer;
            float _LineWidth;
            float4 _EdgeColor;
            float4 _AuxLineColor;
            float2 _MeshFactoryScreenSize;
            
            struct Attributes { uint vertexID : SV_VertexID; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float visibility : TEXCOORD0; float2 lineUV : TEXCOORD1; };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                uint lineIndex = input.instanceID;
                uint quadVertex = input.vertexID;
                LineSegment seg = _LineBuffer[lineIndex];
                float visibility = _LineVisibilityBuffer[lineIndex];
                float4 p1 = _ScreenPositionBuffer[seg.v1];
                float4 p2 = _ScreenPositionBuffer[seg.v2];
                if (visibility < 0.5 || p1.w < 0.5 || p2.w < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.color = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    o.lineUV = float2(0, 0);
                    return o;
                }
                float2 delta = p2.xy - p1.xy;
                float len = length(delta);
                if (len < 0.001)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.color = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    o.lineUV = float2(0, 0);
                    return o;
                }
                float2 dir = delta / len;
                float2 normal = float2(-dir.y, dir.x);
                float halfWidth = _LineWidth * 0.5;
                float2 c0 = p1.xy + normal * halfWidth;
                float2 c1 = p1.xy - normal * halfWidth;
                float2 c2 = p2.xy + normal * halfWidth;
                float2 c3 = p2.xy - normal * halfWidth;
                float2 positions[6] = { c0, c1, c2, c2, c1, c3 };
                float2 uvs[6] = { float2(0, 1), float2(0, -1), float2(1, 1), float2(1, 1), float2(0, -1), float2(1, -1) };
                float2 pixelPos = positions[quadVertex];
                o.positionCS.x = (pixelPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                o.visibility = visibility;
                o.lineUV = uvs[quadVertex];
                o.color = (seg.lineType == 1) ? _AuxLineColor : _EdgeColor;
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5) discard;
                float edgeDist = abs(i.lineUV.y);
                float alpha = 1.0 - smoothstep(0.7, 1.0, edgeDist);
                float4 col = i.color;
                col.a *= alpha;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
