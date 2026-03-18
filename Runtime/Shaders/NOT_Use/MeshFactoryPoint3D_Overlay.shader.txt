Shader "Poly_Ling/Point3D_Overlay"
{
    Properties
    {
        // 頂点色（ShaderColorSettingsから設定）
        _ColorSelected ("Selected Fill", Color) = (1, 0.6, 0, 0.95)
        _BorderColorSelected ("Selected Border", Color) = (1, 0.6, 0, 1)
        _ColorHovered ("Hovered Fill", Color) = (1, 0, 0, 0.95)
        _BorderColorHovered ("Hovered Border", Color) = (1, 0, 0, 1)
        _ColorDefault ("Default Fill", Color) = (1, 1, 1, 0.8)
        _BorderColorDefault ("Default Border", Color) = (0.7, 0.7, 0.7, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Offset -3, -3
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "UnityCG.cginc"
            
            #define FLAG_MESH_SELECTED 2
            #define FLAG_HOVERED 256
            #define FLAG_HIDDEN 4096       // 1 << 12
            #define FLAG_CULLED 16384
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float2 uv : TEXCOORD0;
            };
            
            // 色プロパティ
            float4 _ColorSelected;
            float4 _BorderColorSelected;
            float4 _ColorHovered;
            float4 _BorderColorHovered;
            float4 _ColorDefault;
            float4 _BorderColorDefault;
            
            StructuredBuffer<uint> _VertexFlagsBuffer;
            int _UseVertexFlagsBuffer;
            int _EnableBackfaceCulling;
            
            v2f vert(appdata v)
            {
                v2f o;
                
                if (_UseVertexFlagsBuffer > 0)
                {
                    uint idx = (uint)v.uv2.x;
                    uint flags = _VertexFlagsBuffer[idx];
                    bool isMeshSelected = (flags & FLAG_MESH_SELECTED) != 0;
                    bool isHidden = (flags & FLAG_HIDDEN) != 0;
                    bool isCulled = (flags & FLAG_CULLED) != 0;
                    bool isHovered = (flags & FLAG_HOVERED) != 0;
                    
                    // 非表示メッシュをスキップ
                    if (isHidden)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.fillColor = 0;
                        o.borderColor = 0;
                        o.uv = 0;
                        return o;
                    }
                    
                    // 選択メッシュでない場合は非表示
                    if (!isMeshSelected)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.fillColor = 0;
                        o.borderColor = 0;
                        o.uv = 0;
                        return o;
                    }
                    
                    // 背面カリング（有効時のみ、ホバー中は除く）
                    if (_EnableBackfaceCulling > 0 && isCulled && !isHovered)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.fillColor = 0;
                        o.borderColor = 0;
                        o.uv = 0;
                        return o;
                    }
                }
                
                o.pos = UnityObjectToClipPos(v.vertex);
                
                // ShaderColorSettingsからの色を使用
                float selectState = v.color.a;
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
