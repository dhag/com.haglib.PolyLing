Shader "Poly_Ling/Point3D"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 0.01
        _ScreenSpaceSize ("Screen Space Size", Float) = 8.0
        _UseScreenSpace ("Use Screen Space Size", Int) = 1
        
        // 頂点色（ShaderColorSettingsから設定）
        _ColorSelected ("Selected Fill", Color) = (1, 0.6, 0, 1)
        _BorderColorSelected ("Selected Border", Color) = (1, 0.6, 0, 1)
        _ColorHovered ("Hovered Fill", Color) = (1, 0, 0, 1)
        _BorderColorHovered ("Hovered Border", Color) = (1, 0, 0, 1)
        _ColorDefault ("Default Fill", Color) = (1, 1, 1, 0.6)
        _BorderColorDefault ("Default Border", Color) = (0.5, 0.5, 0.5, 1)
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
            
            #define FLAG_MESH_SELECTED 2  // 1 << 1
            #define FLAG_HIDDEN 4096      // 1 << 12
            #define FLAG_CULLED 16384     // 1 << 14
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;  // バッファインデックス
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float2 uv : TEXCOORD0;
            };
            
            float _PointSize;
            float _ScreenSpaceSize;
            int _UseScreenSpace;
            
            // 色プロパティ
            float4 _ColorSelected;
            float4 _BorderColorSelected;
            float4 _ColorHovered;
            float4 _BorderColorHovered;
            float4 _ColorDefault;
            float4 _BorderColorDefault;
            
            StructuredBuffer<uint> _VertexFlagsBuffer;
            int _UseVertexFlagsBuffer;    // バッファ使用フラグ
            int _EnableBackfaceCulling;   // 背面カリング有効フラグ
            
            v2f vert(appdata v)
            {
                v2f o;
                
                float selectState = v.color.a;
                
                if (selectState < 0)
                {
                    o.pos = float4(99999, 99999, 99999, 1);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.uv = float2(0, 0);
                    return o;
                }
                
                // フラグバッファチェック
                if (_UseVertexFlagsBuffer > 0)
                {
                    uint bufferIndex = (uint)v.uv2.x;
                    uint flags = _VertexFlagsBuffer[bufferIndex];
                    bool isHidden = (flags & FLAG_HIDDEN) != 0;
                    bool isCulled = (flags & FLAG_CULLED) != 0;
                    bool isHover = selectState < 0.1;
                    
                    // 非表示メッシュをスキップ
                    if (isHidden)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.fillColor = float4(0, 0, 0, 0);
                        o.borderColor = float4(0, 0, 0, 0);
                        o.uv = float2(0, 0);
                        return o;
                    }
                    
                    // 背面カリング（有効時のみ）
                    if (_EnableBackfaceCulling > 0 && isCulled && !isHover)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.fillColor = float4(0, 0, 0, 0);
                        o.borderColor = float4(0, 0, 0, 0);
                        o.uv = float2(0, 0);
                        return o;
                    }
                }
                
                // 頂点位置をクリップ空間に変換
                float4 clipPos = UnityObjectToClipPos(v.vertex);
                
                // スクリーンスペースサイズを使用する場合、距離に依存しないサイズに補正
                if (_UseScreenSpace > 0)
                {
                    // クリップ空間でのオフセットを計算
                    // v.uv は quad の各頂点で (0,0), (1,0), (0,1), (1,1) など
                    // 中心からのオフセットに変換: -0.5 ~ 0.5
                    float2 offset = (v.uv - 0.5) * 2.0;
                    
                    // スクリーンピクセルサイズをクリップ空間サイズに変換
                    // clipPos.w で割ることで、距離に関係なく一定のスクリーンサイズになる
                    float2 pixelSize = _ScreenSpaceSize / _ScreenParams.xy;
                    
                    clipPos.xy += offset * pixelSize * clipPos.w;
                }
                
                o.pos = clipPos;
                
                // ShaderColorSettingsからの色を使用
                if (selectState > 0.9)
                {
                    // 選択状態
                    o.fillColor = _ColorSelected;
                    o.borderColor = _BorderColorSelected;
                }
                else if (selectState < 0.1)
                {
                    // ホバー状態
                    o.fillColor = _ColorHovered;
                    o.borderColor = _BorderColorHovered;
                }
                else
                {
                    // 通常状態
                    o.fillColor = _ColorDefault;
                    o.borderColor = _BorderColorDefault;
                }
                
                o.uv = v.uv;
                return o;
            }
            
            float4 frag(v2f i) : SV_Target
            {
                if (i.fillColor.a < 0.01 && i.borderColor.a < 0.01) discard;
                
                float2 center = i.uv - 0.5;
                float2 absCenter = abs(center);
                float borderThickness = 0.15;
                bool isBorder = absCenter.x > (0.5 - borderThickness) || absCenter.y > (0.5 - borderThickness);
                
                return isBorder ? i.borderColor : i.fillColor;
            }
            ENDCG
        }
    }
    FallBack Off
}
