// Assets/Editor/MeshFactory/Shaders/UnifiedPoint3D.shader
// 統合頂点描画シェーダー
// SelectionFlagsベースの色・サイズ制御

Shader "MeshFactory/UnifiedPoint3D"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 0.01
        _BorderWidth ("Border Width", Float) = 0.15
        _MirrorAlpha ("Mirror Alpha", Range(0, 1)) = 0.4
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+110" }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -2, -2
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "UnityCG.cginc"
            #include "Include/SelectionFlags.cginc"
            
            // ============================================================
            // 構造体
            // ============================================================
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint vertexId : SV_VertexID;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float2 uv : TEXCOORD0;
            };
            
            // ============================================================
            // バッファ
            // ============================================================
            
            // 統合位置バッファ（使用する場合）
            StructuredBuffer<float3> _PositionBuffer;
            
            // フラグバッファ
            StructuredBuffer<uint> _VertexFlagsBuffer;
            
            // スクリーン座標バッファ（オプション）
            StructuredBuffer<float4> _ScreenPosBuffer;
            
            // ミラー位置バッファ（ミラーパス用）
            StructuredBuffer<float3> _MirrorPositionBuffer;
            
            // ============================================================
            // パラメータ
            // ============================================================
            
            float _PointSize;
            float _BorderWidth;
            float _MirrorAlpha;
            
            // 使用フラグ
            int _UseUnifiedBuffer;     // 1: 統合バッファ使用
            int _UseMirrorBuffer;      // 1: ミラーバッファ使用
            
            // 頂点数
            uint _VertexCount;
            
            // カスタム行列（カメラから設定）
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;
            float4x4 _ModelMatrix;
            
            // ============================================================
            // 色設定（C#から設定）
            // ============================================================
            
            // 階層色
            float4 _ColorInactiveModel;    // 非選択モデルの頂点
            float4 _ColorDefault;          // デフォルト（選択モデル内、非選択メッシュ）
            float4 _ColorActive;           // アクティブメッシュの頂点
            float4 _ColorMeshSelected;     // 選択メッシュの頂点
            
            // 状態色
            float4 _ColorHovered;          // ホバー中
            float4 _ColorSelected;         // 選択された頂点
            float4 _ColorDragging;         // ドラッグ中
            
            // ボーダー色
            float4 _BorderColorDefault;
            float4 _BorderColorSelected;
            float4 _BorderColorHovered;
            
            // サイズ
            float _SizeDefault;
            float _SizeActive;
            float _SizeSelected;
            float _SizeHovered;
            
            // ============================================================
            // 頂点シェーダー
            // ============================================================
            
            v2f vert(appdata v)
            {
                v2f o;
                
                // クワッドの4頂点は同じ頂点データを参照
                uint pointIndex = v.vertexId / 4;
                
                // 範囲チェック
                if (pointIndex >= _VertexCount)
                {
                    o.pos = float4(99999, 99999, 99999, 1);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.uv = float2(0, 0);
                    return o;
                }
                
                // フラグ取得
                uint flags = _VertexFlagsBuffer[pointIndex];
                
                // 非表示チェック
                if (HAS_FLAG(flags, FLAG_HIDDEN) || HAS_FLAG(flags, FLAG_CULLED))
                {
                    o.pos = float4(99999, 99999, 99999, 1);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.uv = float2(0, 0);
                    return o;
                }
                
                // 位置取得
                float3 position;
                if (_UseMirrorBuffer > 0)
                {
                    position = _MirrorPositionBuffer[pointIndex];
                }
                else if (_UseUnifiedBuffer > 0)
                {
                    position = _PositionBuffer[pointIndex];
                }
                else
                {
                    position = v.vertex.xyz;
                }
                
                // カスタム行列でクリップ座標に変換
                float4 worldPos = mul(_ModelMatrix, float4(position, 1.0));
                float4 viewPos = mul(_ViewMatrix, worldPos);
                o.pos = mul(_ProjMatrix, viewPos);
                
                // 色計算
                float4 fillColor;
                float4 borderColor;
                
                // 優先順位: Dragging > Selected > Hovered > Hierarchy
                if (HAS_FLAG(flags, FLAG_DRAGGING))
                {
                    fillColor = _ColorDragging;
                    borderColor = _BorderColorSelected;
                }
                else if (HAS_FLAG(flags, FLAG_VERTEX_SELECTED))
                {
                    fillColor = _ColorSelected;
                    borderColor = _BorderColorSelected;
                }
                else if (HAS_FLAG(flags, FLAG_HOVERED))
                {
                    fillColor = _ColorHovered;
                    borderColor = _BorderColorHovered;
                }
                else if (IS_ACTIVE(flags))
                {
                    fillColor = _ColorActive;
                    borderColor = _BorderColorDefault;
                }
                else if (HAS_FLAG(flags, FLAG_MODEL_SELECTED))
                {
                    fillColor = _ColorDefault;
                    borderColor = _BorderColorDefault;
                }
                else
                {
                    fillColor = _ColorInactiveModel;
                    borderColor = _BorderColorDefault;
                }
                
                // ミラー要素の透明度調整
                if (HAS_FLAG(flags, FLAG_MIRROR))
                {
                    fillColor.a *= _MirrorAlpha;
                    borderColor.a *= _MirrorAlpha;
                }
                
                o.fillColor = fillColor;
                o.borderColor = borderColor;
                o.uv = v.uv;
                
                return o;
            }
            
            // ============================================================
            // フラグメントシェーダー
            // ============================================================
            
            float4 frag(v2f i) : SV_Target
            {
                if (i.fillColor.a < 0.01 && i.borderColor.a < 0.01) 
                    discard;
                
                // 矩形の枠を描画
                float2 center = i.uv - 0.5;
                float2 absCenter = abs(center);
                float borderThickness = _BorderWidth;
                bool isBorder = absCenter.x > (0.5 - borderThickness) || absCenter.y > (0.5 - borderThickness);
                
                return isBorder ? i.borderColor : i.fillColor;
            }
            
            ENDCG
        }
    }
    FallBack Off
}
