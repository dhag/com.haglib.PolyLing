// TextMeshParams.cs
// 文字列メッシュ生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.GlyphText
{
    /// <summary>
    /// 文字列メッシュ生成パラメータ。
    /// 厚み・角処理は Profile2DExtrudeMeshGenerator へそのまま渡す。
    /// </summary>
    [Serializable]
    public struct TextMeshParams : IEquatable<TextMeshParams>
    {
        public string MeshName;

        /// <summary>fonts.txt のファミリ名。</summary>
        public string FontFamily;

        /// <summary>生成する文字列。改行で行が増える。</summary>
        public string Text;

        /// <summary>曲線 1 本あたりの分割数。</summary>
        public int Segment;

        /// <summary>1em を何単位にするか。</summary>
        public float Size;

        /// <summary>字間の追加量（em 単位）。</summary>
        public float LetterSpacing;

        /// <summary>行送り倍率。1 で ascent+descent+lineGap。</summary>
        public float LineSpacing;

        public float Thickness;
        public int SegmentsFront, SegmentsBack;
        public float EdgeSizeFront, EdgeSizeBack;
        public bool EdgeInward;

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
                return h;
            }
        }
    }
}
