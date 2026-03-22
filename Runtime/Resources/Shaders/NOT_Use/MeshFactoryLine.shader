Shader "MeshFactory/Line"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 2.0
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 0.9)
        _AuxLineColor ("Aux Line Color", Color) = (1, 0.3, 1, 0.9)
        _HoverColor ("Hover Color", Color) = (0, 1, 1, 1)
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
            
            struct LineSegment
            {
                int v1;
                int v2;
                int faceIndex;
                int lineType;
            };
            
            StructuredBuffer<float4> _ScreenPositionBuffer;
            StructuredBuffer<LineSegment> _LineBuffer;
            StructuredBuffer<float> _LineVisibilityBuffer;
            StructuredBuffer<uint> _LineSelectionBuffer;
            
            float _LineWidth;
            float4 _EdgeColor;
            float4 _AuxLineColor;
            float4 _SelectedEdgeColor;    // 選択エッジ色
            float4 _HoverColor;           // ホバー色
            float2 _MeshFactoryScreenSize;
            float4 _PreviewRect;
            float2 _GUIOffset;   // タブバー等のオフセット
            float _Alpha;        // 透明度（非選択メッシュ用）
            //int _HoverLineIndex; // ホバー中の線分インデックス（-1=なし）Unity シェーダーでは int プロパティが正しく動作しないことがあります。
            float  _HoverLineIndex; // ホバー中の線分インデックス（-1=なし）
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float visibility : TEXCOORD0;
                float2 lineUV : TEXCOORD1;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint lineIndex = input.instanceID;
                uint quadVertex = input.vertexID;
                
                LineSegment seg = _LineBuffer[lineIndex];
                float visibility = _LineVisibilityBuffer[lineIndex];
                
                float4 p1 = _ScreenPositionBuffer[seg.v1];
                float4 p2 = _ScreenPositionBuffer[seg.v2];
                
                // 不可視の場合は画面外に配置
                if (visibility < 0.5 || p1.w < 0.5 || p2.w < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.color = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    o.lineUV = float2(0, 0);
                    return o;
                }
                
                // 線分の方向と法線を計算
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
                
                // ホバー中は少し太く表示
                bool isHovered = ((int)lineIndex == _HoverLineIndex);
                float lineWidth = isHovered ? _LineWidth * 2.0 : _LineWidth;
                
                float2 dir = delta / len;
                float2 normal = float2(-dir.y, dir.x);
                float halfWidth = lineWidth * 0.5;
                
                // 4隅の座標を計算
                float2 c0 = p1.xy + normal * halfWidth;
                float2 c1 = p1.xy - normal * halfWidth;
                float2 c2 = p2.xy + normal * halfWidth;
                float2 c3 = p2.xy - normal * halfWidth;
                
                // 6頂点（2三角形）
                float2 positions[6] = { c0, c1, c2, c2, c1, c3 };
                float2 uvs[6] = {
                    float2(0, 1), float2(0, -1), float2(1, 1),
                    float2(1, 1), float2(0, -1), float2(1, -1)
                };
                
                float2 pixelPos = positions[quadVertex];
                
                // Compute Shaderで既にウィンドウ座標に変換済み
                float2 windowPos = pixelPos;
                
                // ピクセル座標をクリップ座標に変換（GUI座標系）
                o.positionCS.x = (windowPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (windowPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.0;
                o.positionCS.w = 1.0;
                
                o.visibility = visibility;
                o.lineUV = uvs[quadVertex];
                
                // ホバー > 選択 > 通常 の優先順位で色分け
                uint isSelected = _LineSelectionBuffer[lineIndex];
                
                if (isHovered)
                {
                    o.color = _HoverColor;
                }
                else if (isSelected > 0)
                {
                    o.color = _SelectedEdgeColor;
                }
                else
                {
                    // lineType: 0=エッジ, 1=補助線
                    o.color = (seg.lineType == 1) ? _AuxLineColor : _EdgeColor;
                }
                
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5) discard;
                
                // 線のエッジをソフトにする
                float edgeDist = abs(i.lineUV.y);
                float alpha = 1.0 - smoothstep(0.7, 1.0, edgeDist);
                
                float4 col = i.color;
                col.a *= alpha * _Alpha;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
