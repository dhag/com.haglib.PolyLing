// TextMeshParams.cs
// 文字列メッシュ生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.GlyphText
{
    /// <summary>文字列の配置のしかた。</summary>
    public enum TextCurveMode
    {
        /// <summary>XY 平面にそのまま並べる。</summary>
        Flat = 0,
        /// <summary>Y 軸まわりの円筒の外側に巻く。</summary>
        Cylinder = 1,
        /// <summary>XY 面上の円のふちに沿わせる。</summary>
        DiskRim = 2,
    }

    /// <summary>
    /// 文字列メッシュ生成パラメータ。
    /// 厚み・角処理は Profile2DExtrudeMeshGenerator へそのまま渡す。
    /// </summary>
    [Serializable]
    public struct TextMeshParams : IEquatable<TextMeshParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>曲線 1 本あたりの分割数の下限・上限</summary>
        public const int SegmentMin = 1;
        public const int SegmentMax = 20;

        /// <summary>1em の大きさの下限・上限</summary>
        public const float SizeMin = 0.01f;
        public const float SizeMax = 10f;

        /// <summary>字間の下限・上限（em）</summary>
        public const float LetterSpacingMin = -0.5f;
        public const float LetterSpacingMax = 1f;

        /// <summary>行送り倍率の下限・上限</summary>
        public const float LineSpacingMin = 0.5f;
        public const float LineSpacingMax = 3f;

        /// <summary>厚みの下限・上限</summary>
        public const float ThicknessMin = 0f;
        public const float ThicknessMax = 0.5f;

        /// <summary>エッジ分割数の下限・上限</summary>
        public const int EdgeSegmentsMin = 0;
        public const int EdgeSegmentsMax = 16;

        /// <summary>エッジサイズの下限・上限</summary>
        public const float EdgeSizeMin = 0.001f;
        public const float EdgeSizeMax = 0.25f;

        /// <summary>曲面配置の半径の下限・上限</summary>
        public const float CurveRadiusMin = 0.05f;
        public const float CurveRadiusMax = 50f;

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        /// <summary>fonts.txt のファミリ名。</summary>
        [PLParam(TextKey = "TextFontFamily", Description = "fonts.txt のファミリ名", Required = true)]
        public string FontFamily;

        /// <summary>生成する文字列。改行で行が増える。</summary>
        [PLParam(TextKey = "TextContent", Description = "生成する文字列。改行で行が増える", Required = true)]
        public string Text;

        /// <summary>曲線 1 本あたりの分割数。</summary>
        [PLParam(TextKey = "Segments", Description = "曲線 1 本あたりの分割数", Min = SegmentMin, Max = SegmentMax,
                 Step = 1)]
        public int Segment;

        /// <summary>1em を何単位にするか。</summary>
        [PLParam(TextKey = "Size", Description = "1em を何単位にするか", Min = SizeMin, Max = SizeMax)]
        public float Size;

        /// <summary>字間の追加量（em 単位）。</summary>
        [PLParam(TextKey = "TextLetterSpacing", Description = "字間の追加量（em 単位）", Min = LetterSpacingMin,
                 Max = LetterSpacingMax)]
        public float LetterSpacing;

        /// <summary>行送り倍率。1 で ascent+descent+lineGap。</summary>
        [PLParam(TextKey = "TextLineSpacing", Description = "行送り倍率。1 で ascent+descent+lineGap",
                 Min = LineSpacingMin, Max = LineSpacingMax)]
        public float LineSpacing;

        [PLParam(TextKey = "Thickness", Description = "押し出しの厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
        public float Thickness;
        [PLParam(TextKey = "FrontSegments", Description = "表側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsFront;
        [PLParam(TextKey = "BackSegments", Description = "裏側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsBack;
        [PLParam(TextKey = "EdgeSize", Description = "表側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeFront;
        [PLParam(TextKey = "EdgeSize", Description = "裏側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeBack;
        [PLParam(TextKey = "EdgeInward", Description = "ラウンドの曲率方向を入れ替える")]
        public bool EdgeInward;

        [PLParam(TextKey = "TextCurve",
                 Description = "配置のしかた。Flat = 平面 / Cylinder = Y軸まわりの円筒の外側に巻く / DiskRim = XY面上の円のふちに沿わせる。文字ごとに回して置く")]
        public TextCurveMode CurveMode;

        [PLParam(TextKey = "Radius",
                 Description = "円筒・円盤のふちの半径。円盤のふちでは1行目のベースラインの位置。行頭からの距離÷半径が回転角",
                 Min = CurveRadiusMin, Max = CurveRadiusMax)]
        public float CurveRadius;

        [PLParam(TextKey = "TextCurveFlip",
                 Description = "円盤のふちのみ。false = 文字の上側を中心へ向け、円の下端から反時計回りに並べる / true = 文字の下側を中心へ向け、円の上端から時計回りに並べる")]
        public bool CurveFlip;

        /// <summary>開始角の下限・上限（度）。</summary>
        public const float CurveStartAngleMin = -360f;
        public const float CurveStartAngleMax = 360f;

        [PLParam(TextKey = "TextCurveStartAngle",
                 Description = "円盤のふちのみ。並び全体を正面から見て時計回りに回す角度（度）",
                 Min = CurveStartAngleMin, Max = CurveStartAngleMax)]
        public float CurveStartAngle;

        [PLParam(TextKey = "TextCurveFitCircle",
                 Description = "円盤のふちのみ。各行を 1 周に均等割り付けする（字間は半径から決めず、1 周を文字数で割る）")]
        public bool CurveFitCircle;

        // ── 看板 ─────────────────────────────────────────────────

        /// <summary>看板の余白（片側）の下限・上限</summary>
        public const float SignMarginMin = 0f;
        public const float SignMarginMax = 5f;

        /// <summary>看板の奥行きの下限・上限</summary>
        public const float SignDepthMin = 0.001f;
        public const float SignDepthMax = 2f;

        [PLParam(TextKey = "TextSignboard",
                 Description = "配置が平面で厚みが 0 より大きいときのみ。文字の外形に余白を足した直方体（看板）を作り、表面を文字の裏面に合わせて貼る")]
        public bool Signboard;

        [PLParam(TextKey = "TextSignMarginH", Description = "看板の左右の余白（片側。左右同じ）",
                 Min = SignMarginMin, Max = SignMarginMax)]
        public float SignMarginH;

        [PLParam(TextKey = "TextSignMarginV", Description = "看板の上下の余白（片側。上下同じ）",
                 Min = SignMarginMin, Max = SignMarginMax)]
        public float SignMarginV;

        [PLParam(TextKey = "TextSignDepth", Description = "看板の奥行き（Z）", Min = SignDepthMin, Max = SignDepthMax)]
        public float SignDepth;

        [PLParam(TextKey = "TextSignBackText",
                 Description = "看板ありのときのみ。看板の裏面にも同じ文字を、裏から読める向きで貼る")]
        public bool SignBackText;

        public static TextMeshParams Default => new TextMeshParams
        {
            MeshName      = "Text",
            FontFamily    = "",
            Text          = "",
            Segment       = 5,
            Size          = 1.0f,
            LetterSpacing = 0f,
            LineSpacing   = 1.0f,
            Thickness     = 0f,
            SegmentsFront = 0,
            SegmentsBack  = 0,
            EdgeSizeFront = 0.02f,
            EdgeSizeBack  = 0.02f,
            EdgeInward    = false,
            CurveMode     = TextCurveMode.Flat,
            CurveRadius   = 2f,
            CurveFlip     = false,
            CurveStartAngle = 0f,
            CurveFitCircle  = false,
            Signboard       = false,
            SignMarginH     = 0.1f,
            SignMarginV     = 0.1f,
            SignDepth       = 0.05f,
            SignBackText    = false,
        };

        public bool Equals(TextMeshParams o)
        {
            if (MeshName != o.MeshName) return false;
            if (FontFamily != o.FontFamily) return false;
            if (Text != o.Text) return false;
            if (Segment != o.Segment) return false;
            if (!Mathf.Approximately(Size, o.Size)) return false;
            if (!Mathf.Approximately(LetterSpacing, o.LetterSpacing)) return false;
            if (!Mathf.Approximately(LineSpacing, o.LineSpacing)) return false;
            if (!Mathf.Approximately(Thickness, o.Thickness)) return false;
            if (SegmentsFront != o.SegmentsFront || SegmentsBack != o.SegmentsBack) return false;
            if (!Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)) return false;
            if (!Mathf.Approximately(EdgeSizeBack, o.EdgeSizeBack)) return false;
            if (EdgeInward != o.EdgeInward) return false;
            if (CurveMode != o.CurveMode) return false;
            if (!Mathf.Approximately(CurveRadius, o.CurveRadius)) return false;
            if (CurveFlip != o.CurveFlip) return false;
            if (!Mathf.Approximately(CurveStartAngle, o.CurveStartAngle)) return false;
            if (CurveFitCircle != o.CurveFitCircle) return false;
            if (Signboard != o.Signboard) return false;
            if (!Mathf.Approximately(SignMarginH, o.SignMarginH)) return false;
            if (!Mathf.Approximately(SignMarginV, o.SignMarginV)) return false;
            if (!Mathf.Approximately(SignDepth, o.SignDepth)) return false;
            if (SignBackText != o.SignBackText) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is TextMeshParams p && Equals(p);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (MeshName != null ? MeshName.GetHashCode() : 0);
                h = h * 31 + (FontFamily != null ? FontFamily.GetHashCode() : 0);
                h = h * 31 + (Text != null ? Text.GetHashCode() : 0);
                h = h * 31 + Segment;
                h = h * 31 + Size.GetHashCode();
                h = h * 31 + Thickness.GetHashCode();
                h = h * 31 + (int)CurveMode;
                h = h * 31 + CurveRadius.GetHashCode();
                h = h * 31 + (CurveFlip ? 1 : 0);
                return h;
            }
        }
    }
}
