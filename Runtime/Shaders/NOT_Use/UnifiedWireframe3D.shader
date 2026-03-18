// Assets/Editor/MeshFactory/Shaders/UnifiedWireframe3D.shader
// 統合ワイヤーフレーム描画シェーダー
// SelectionFlagsベースの色・太さ制御

Shader "MeshFactory/UnifiedWireframe3D"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 1.0
        _MirrorAlpha ("Mirror Alpha", Range(0, 1)) = 0.4
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+100" }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Offset -1, -1
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
            
            struct UnifiedLine
            {
                uint v1;
                uint v2;
                uint flags;
                uint faceIndex;
                uint meshIndex;
                uint modelIndex;
            };
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                uint vertexId : SV_VertexID;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };
            
            // ============================================================
            // バッファ
            // ============================================================
            
            // 統合位置バッファ
            StructuredBuffer<float3> _PositionBuffer;
            
            // ラインバッファ
            StructuredBuffer<UnifiedLine> _LineBuffer;
            
            // フラグバッファ
            StructuredBuffer<uint> _LineFlagsBuffer;
            
            // ミラー位置バッファ
            StructuredBuffer<float3> _MirrorPositionBuffer;
            
            // ============================================================
            // パラメータ
            // ============================================================
            
            float _LineWidth;
            float _MirrorAlpha;
            
            // 使用フラグ
            int _UseUnifiedBuffer;     // 1: 統合バッファ使用
            int _UseMirrorBuffer;      // 1: ミラーバッファ使用
            
            // ライン数
            uint _LineCount;
            
            // カスタム行列（カメラから設定）
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;
            float4x4 _ModelMatrix;
            
            // ============================================================
            // 色設定
            // ============================================================
            
            // 階層色
            float4 _ColorInactiveModel;    // 非選択モデル
            float4 _ColorDefault;          // デフォルト
            float4 _ColorActive;           // アクティブメッシュ
            float4 _ColorMeshSelected;     // 選択メッシュ
            
            // 状態色
            float4 _ColorHovered;          // ホバー中
            float4 _ColorEdgeSelected;     // 選択エッジ
            float4 _ColorDragging;         // ドラッグ中
            
            // 補助線色
            float4 _ColorAuxLine;          // 補助線（通常）
            float4 _ColorAuxLineSelected;  // 補助線（選択）
            
            // 境界エッジ色
            float4 _ColorBoundary;         // 境界エッジ
            
            // ============================================================
            // 頂点シェーダー
            // ============================================================
            
            v2f vert(appdata v)
            {
                v2f o;
                
                // 線分の2頂点は同じライン
                uint lineIndex = v.vertexId / 2;
                uint vertInLine = v.vertexId % 2; // 0 or 1
                
                // 範囲チェック
                if (lineIndex >= _LineCount)
                {
                    o.pos = float4(99999, 99999, 99999, 1);
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }
                
                // ラインデータ取得
                UnifiedLine lineSeg = _LineBuffer[lineIndex];
                uint flags = _LineFlagsBuffer[lineIndex];
                
                // 非表示チェック
                if (HAS_FLAG(flags, FLAG_HIDDEN) || HAS_FLAG(flags, FLAG_CULLED))
                {
                    o.pos = float4(99999, 99999, 99999, 1);
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }
                
                // 頂点インデックス
                uint vertexIndex = (vertInLine == 0) ? lineSeg.v1 : lineSeg.v2;
                
                // 位置取得
                float3 position;
                if (_UseMirrorBuffer > 0)
                {
                    position = _MirrorPositionBuffer[vertexIndex];
                }
                else if (_UseUnifiedBuffer > 0)
                {
                    position = _PositionBuffer[vertexIndex];
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
                float4 lineColor;
                
                // 補助線かどうか
                bool isAuxLine = HAS_FLAG(flags, FLAG_IS_AUXLINE);
                
                // 優先順位に基づく色決定
                if (HAS_FLAG(flags, FLAG_DRAGGING))
                {
                    lineColor = _ColorDragging;
                }
                else if (isAuxLine)
                {
                    // 補助線
                    if (HAS_FLAG(flags, FLAG_LINE_SELECTED))
                    {
                        lineColor = _ColorAuxLineSelected;
                    }
                    else if (HAS_FLAG(flags, FLAG_HOVERED))
                    {
                        lineColor = lerp(_ColorAuxLine, _ColorHovered, 0.5);
                    }
                    else
                    {
                        lineColor = _ColorAuxLine;
                    }
                }
                else
                {
                    // 通常のエッジ
                    if (HAS_FLAG(flags, FLAG_EDGE_SELECTED))
                    {
                        lineColor = _ColorEdgeSelected;
                    }
                    else if (HAS_FLAG(flags, FLAG_HOVERED))
                    {
                        lineColor = _ColorHovered;
                    }
                    else if (HAS_FLAG(flags, FLAG_IS_BOUNDARY))
                    {
                        // 境界エッジはハイライト
                        lineColor = _ColorBoundary;
                    }
                    else if (IS_ACTIVE(flags))
                    {
                        lineColor = _ColorActive;
                    }
                    else if (HAS_FLAG(flags, FLAG_MODEL_SELECTED))
                    {
                        lineColor = _ColorDefault;
                    }
                    else
                    {
                        lineColor = _ColorInactiveModel;
                    }
                }
                
                // ミラー要素の透明度調整
                if (HAS_FLAG(flags, FLAG_MIRROR))
                {
                    lineColor.a *= _MirrorAlpha;
                }
                
                o.color = lineColor;
                
                return o;
            }
            
            // ============================================================
            // フラグメントシェーダー
            // ============================================================
            
            float4 frag(v2f i) : SV_Target
            {
                if (i.color.a < 0.01) 
                    discard;
                return i.color;
            }
            
            ENDCG
        }
    }
    FallBack Off
}
