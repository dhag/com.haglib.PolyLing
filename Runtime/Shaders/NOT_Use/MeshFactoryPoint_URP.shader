//ëΩï™ÉoÉOÇ†ÇËÅB
Shader "MeshFactory/Point"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 8.0
        _NormalColor ("Normal Color", Color) = (1, 1, 1, 1)
        _NormalBorderColor ("Normal Border Color", Color) = (0.5, 0.5, 0.5, 1)
        _SelectedColor ("Selected Color", Color) = (1, 0.8, 0, 1)
        _SelectedBorderColor ("Selected Border Color", Color) = (1, 0, 0, 1)
        _BorderWidth ("Border Width", Float) = 1.0
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
            
            StructuredBuffer<float4> _ScreenPositionBuffer;
            StructuredBuffer<float> _VertexVisibilityBuffer;
            StructuredBuffer<uint> _SelectionBuffer;
            float _PointSize;
            float4 _NormalColor;
            float4 _NormalBorderColor;
            float4 _SelectedColor;
            float4 _SelectedBorderColor;
            float _BorderWidth;
            float2 _MeshFactoryScreenSize;
            
            struct Attributes { uint vertexID : SV_VertexID; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 fillColor : COLOR0; float4 borderColor : COLOR1; float visibility : TEXCOORD1; };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                uint pointIndex = input.instanceID;
                uint quadVertex = input.vertexID;
                float4 screenPos = _ScreenPositionBuffer[pointIndex];
                float visibility = _VertexVisibilityBuffer[pointIndex];
                if (screenPos.w < 0.5 || visibility < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.uv = float2(0, 0);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    return o;
                }
                float2 offsets[6] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(-1, 1), float2(1, -1), float2(1, 1) };
                float2 uvOffsets[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
                float halfSize = _PointSize * 0.5;
                float2 pixelPos = screenPos.xy + offsets[quadVertex] * halfSize;
                o.positionCS.x = (pixelPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                o.uv = uvOffsets[quadVertex];
                uint isSelected = _SelectionBuffer[pointIndex];
                o.fillColor = isSelected ? _SelectedColor : _NormalColor;
                o.borderColor = isSelected ? _SelectedBorderColor : _NormalBorderColor;
                o.visibility = visibility;
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5) discard;
                float2 center = i.uv - 0.5;
                float2 absCenter = abs(center);
                float borderThickness = _BorderWidth / _PointSize;
                bool isBorder = absCenter.x > (0.5 - borderThickness) || absCenter.y > (0.5 - borderThickness);
                return isBorder ? i.borderColor : i.fillColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
