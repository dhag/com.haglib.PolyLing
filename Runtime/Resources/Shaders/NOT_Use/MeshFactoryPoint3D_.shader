Shader "MeshFactory/Point3D_"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 0.01
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
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float2 uv : TEXCOORD0;
            };
            
            float _PointSize;
            float _BorderWidth;
            
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // color.rgb = fill color, color.a に選択状態をエンコード
                // 1.0 = selected, 0.5 = normal, 0.0 = hover
                float selectState = v.color.a;
                
                if (selectState > 0.9)
                {
                    // Selected
                    o.fillColor = float4(1, 0.8, 0, 1);
                    o.borderColor = float4(1, 0, 0, 1);
                }
                else if (selectState < 0.1)
                {
                    // Hover
                    o.fillColor = float4(0, 1, 1, 1);
                    o.borderColor = float4(0, 0.8, 0.8, 1);
                }
                else
                {
                    // Normal
                    o.fillColor = float4(1, 1, 1, 0.6);
                    o.borderColor = float4(0.5, 0.5, 0.5, 1);
                }
                
                o.uv = v.uv;
                return o;
            }
            
            float4 frag(v2f i) : SV_Target
            {
                // 矩形の枠を描画
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
