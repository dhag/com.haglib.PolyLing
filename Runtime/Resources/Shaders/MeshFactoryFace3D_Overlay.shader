// MeshFactoryFace3D_Overlay.shader
// Phase 2c: 選択面・ホバー面の塗りつぶし描画専用 overlay シェーダ。
// UnifiedRenderer が CPU で構築する三角形化済み mesh を受け取り、
// uv.x に埋め込まれた globalFaceIndex で _FaceFlagsBuffer / _FaceCulledBuffer を参照し、
// 選択/ホバー/カリングに応じた色決定と discard を行う。
// 通常面描画パイプラインと同じ cam.pixel 基準投影のため、画面サイズ変更に自動追従する。

Shader "Poly_Ling/Face3D_Overlay"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 通常面より手前に出すが、wireframe/point(Overlay キュー ZTest Always) よりは奥。
            ZTest LEqual
            Offset -1, -1
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            // UnifiedCompute.compute の FLAG_* 定義と同じ値を使用。
            #define FLAG_FACE_SELECTED  0x00000040
            #define FLAG_HOVERED        0x00000100
            #define FLAG_HIDDEN         0x00001000

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;  // uv.x = globalFaceIndex
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            StructuredBuffer<uint> _FaceFlagsBuffer;
            StructuredBuffer<uint> _FaceCulledBuffer;
            int _UseFaceFlagsBuffer;
            int _EnableBackfaceCulling;
            float4 _FaceHoveredFill;
            float4 _FaceSelectedFill;

            v2f vert(appdata v)
            {
                v2f o;
                float4 outColor = float4(0, 0, 0, 0);

                if (_UseFaceFlagsBuffer > 0)
                {
                    uint idx = (uint)v.uv.x;
                    uint flags = _FaceFlagsBuffer[idx];
                    bool isHidden   = (flags & FLAG_HIDDEN) != 0;
                    bool isSelected = (flags & FLAG_FACE_SELECTED) != 0;
                    bool isHovered  = (flags & FLAG_HOVERED) != 0;
                    bool isCulled   = _FaceCulledBuffer[idx] != 0u;

                    // 非表示面をスキップ
                    if (isHidden)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = 0;
                        return o;
                    }

                    // 背面カリング（ホバー中は表示維持）
                    if (_EnableBackfaceCulling > 0 && isCulled && !isHovered)
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = 0;
                        return o;
                    }

                    // 色決定: ホバー優先、次に選択、それ以外は discard（alpha=0）
                    if (isHovered)
                        outColor = _FaceHoveredFill;
                    else if (isSelected)
                        outColor = _FaceSelectedFill;
                    else
                    {
                        o.pos = float4(99999, 99999, 99999, 1);
                        o.color = 0;
                        return o;
                    }
                }
                else
                {
                    outColor = v.color;
                }

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = outColor;
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
