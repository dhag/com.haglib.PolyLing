Shader "MeshFactory/Wireframe3D_Overlay"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Offset -2, -2
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "UnityCG.cginc"
            
            #define FLAG_MESH_SELECTED 2
            #define FLAG_HOVERED 256
            #define FLAG_CULLED 16384
            
            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };
            
            StructuredBuffer<uint> _LineFlagsBuffer;
            int _UseLineFlagsBuffer;
            int _EnableBackfaceCulling;
            
            v2f vert(appdata v)
            {
                v2f o;
                
                if (_UseLineFlagsBuffer > 0)
                {
                    uint idx = (uint)v.uv.x;
                    uint flags = _LineFlagsBuffer[idx];
                    bool isMeshSelected = (flags & FLAG_MESH_SELECTED) != 0;
                    bool isCulled = (flags & FLAG_CULLED) != 0;
                    bool isHovered = (flags & FLAG_HOVERED) != 0;
                    
                    // 選択メッシュでない場合は非表示
                    if (!isMeshSelected)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = 0;
                        return o;
                    }
                    
                    // 背面カリング（有効時のみ、ホバー中は除く）
                    if (_EnableBackfaceCulling > 0 && isCulled && !isHovered)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = 0;
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
