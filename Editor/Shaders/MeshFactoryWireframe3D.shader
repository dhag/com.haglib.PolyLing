Shader "Poly_Ling/Wireframe3D"
{
    Properties
    {
        _Color ("Color", Color) = (0, 1, 0.5, 0.9)
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
            
            #define FLAG_MESH_SELECTED 2  // 1 << 1
            #define FLAG_CULLED 16384  // 1 << 14
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;  // バッファインデックス
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };
            
            float4 _Color;
            
            StructuredBuffer<uint> _LineFlagsBuffer;
            int _UseLineFlagsBuffer;
            int _EnableBackfaceCulling;
            
            v2f vert(appdata v)
            {
                v2f o;
                
                if (_UseLineFlagsBuffer > 0)
                {
                    uint bufferIndex = (uint)v.uv.x;
                    uint flags = _LineFlagsBuffer[bufferIndex];
                    bool isMeshSelected = (flags & FLAG_MESH_SELECTED) != 0;
                    bool isCulled = (flags & FLAG_CULLED) != 0;
                    
                    // 選択メッシュのセグメントは非表示（オーバーレイで描画するため）
                    if (isMeshSelected)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = float4(0, 0, 0, 0);
                        return o;
                    }
                    
                    // 背面カリング（有効時のみ）
                    if (_EnableBackfaceCulling > 0 && isCulled)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = float4(0, 0, 0, 0);
                        return o;
                    }
                }
                
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }
            
            float4 frag(v2f i) : SV_Target
            {
                if (i.color.a < 0.01) discard;
                return i.color;
            }
            ENDCG
        }
    }
    FallBack Off
}
