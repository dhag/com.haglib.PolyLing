Shader "MeshFactory/Point"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 8.0
        _NormalColor ("Normal Color", Color) = (1, 1, 1, 0.6)//デフォルト色（非選択メッシュ頂点色など）
        _NormalBorderColor ("Normal Border Color", Color) = (0.5, 0.5, 0.5, 1)
        _SelectedColor ("Selected Color", Color) = (1, 0.8, 0, 1)
        _SelectedBorderColor ("Selected Border Color", Color) = (1, 0, 0, 1)
        _HoverColor ("Hover Color", Color) = (0, 1, 1, 1)
        _HoverBorderColor ("Hover Border Color", Color) = (0, 0.8, 0.8, 1)
        _BorderWidth ("Border Width", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "UnityCG.cginc"
            
            StructuredBuffer<float4> _ScreenPositionBuffer;
            StructuredBuffer<float> _VertexVisibilityBuffer;
            StructuredBuffer<uint> _SelectionBuffer;
            
            float _PointSize;
            float4 _NormalColor;
            float4 _NormalBorderColor;
            float4 _SelectedColor;
            float4 _SelectedBorderColor;
            float4 _HoverColor;
            float4 _HoverBorderColor;
            float _BorderWidth;
            float2 _MeshFactoryScreenSize;
            float4 _PreviewRect; // x, y, width, height
            float2 _GUIOffset;   // タブバー等のオフセット
            float _Alpha;        // 透明度（非選択メッシュ用）
            //int _HoverVertexIndex;  // ホバー中の頂点インデックス（-1=なし）Unity シェーダーでは int プロパティが正しく動作しないことがあります。
            float _HoverVertexIndex;  // ホバー中の頂点インデックス（-1=なし）Unity シェーダーでは int プロパティが正しく動作しないことがあります。
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float visibility : TEXCOORD1;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint pointIndex = input.instanceID;
                uint quadVertex = input.vertexID;
                
                float4 screenPos = _ScreenPositionBuffer[pointIndex];
                float visibility = _VertexVisibilityBuffer[pointIndex];
                
                // 不可視の場合は画面外に配置
                if (screenPos.w < 0.5 || visibility < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.uv = float2(0, 0);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    return o;
                }
                
                // クワッドの6頂点オフセット
                float2 offsets[6] = {
                    float2(-1, -1), float2(1, -1), float2(-1, 1),
                    float2(-1, 1), float2(1, -1), float2(1, 1)
                };
                float2 uvOffsets[6] = {
                    float2(0, 0), float2(1, 0), float2(0, 1),
                    float2(0, 1), float2(1, 0), float2(1, 1)
                };
                
                // ホバー中は少し大きく表示
                bool isHovered = ((int)pointIndex == _HoverVertexIndex);
                float size = isHovered ? _PointSize * 1.3 : _PointSize;
                
                float halfSize = size * 0.5;
                float2 pixelPos = screenPos.xy + offsets[quadVertex] * halfSize;
                
                // Compute Shaderで既にウィンドウ座標に変換済み
                float2 windowPos = pixelPos;
                
                // ピクセル座標をクリップ座標に変換（GUI座標系）
                o.positionCS.x = (windowPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (windowPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.0;
                o.positionCS.w = 1.0;
                
                o.uv = uvOffsets[quadVertex];
                
                // ホバー > 選択 > 通常 の優先順位で色分け
                uint isSelected = _SelectionBuffer[pointIndex];
                if (isHovered)
                {
                    o.fillColor = _HoverColor;
                    o.borderColor = _HoverBorderColor;
                }
                else if (isSelected)
                {
                    o.fillColor = _SelectedColor;
                    o.borderColor = _SelectedBorderColor;
                }
                else
                {
                    o.fillColor = _NormalColor;
                    o.borderColor = _NormalBorderColor;
                }
                o.visibility = visibility;
                
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5) discard;
                
                // 矩形の枠を描画
                float2 center = i.uv - 0.5;
                float2 absCenter = abs(center);
                float borderThickness = _BorderWidth / _PointSize;
                bool isBorder = absCenter.x > (0.5 - borderThickness) || absCenter.y > (0.5 - borderThickness);
                
                float4 col = isBorder ? i.borderColor : i.fillColor;
                col.a *= _Alpha;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
