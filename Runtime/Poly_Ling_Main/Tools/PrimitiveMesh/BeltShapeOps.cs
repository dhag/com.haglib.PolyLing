// BeltShapeOps.cs
// 基準ベルト（梯子状データ）の前処理と、フリル／パイプ共通の厚み付け。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ Runtime に置くか】
//   これらはもともと PlayerPrimitiveMeshSubPanel.BeltProfile.cs の private 静的関数だった。
//   図形生成をコマンド経由にすると PrimitiveMeshFactory が同じ処理を要るので、
//   UI から切り離して Runtime へ置く。パネル側も同じものを呼ぶ。
//
// 【扱う型を BeltCsvEntry にした理由】
//   パネルの BeltSnapshot は Left / Right / Closed / FlipWinding / HeightScale /
//   StartPoint / EndPoint / GroupId / RowIndex / RowCount を持つ private クラスで、
//   BeltCsvEntry（BeltCsvIO.cs）と同じ構成である。既に Runtime にある方を使えば
//   private クラスを移す必要がない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>梯子の向き補正。</summary>
    public struct BeltOrientOptions
    {
        /// <summary>左右レールを入れ替える。段番号も反転する。</summary>
        [PLParam(TextKey = "BeltSwapSides", Description = "左右レールを入れ替える")]
        public bool SwapSides;

        /// <summary>rung の並び順を反転する。</summary>
        [PLParam(TextKey = "BeltReverseOrder", Description = "rung の並び順を反転する")]
        public bool ReverseOrder;

        public bool IsIdentity => !SwapSides && !ReverseOrder;

        public static BeltOrientOptions Default => new BeltOrientOptions();
    }

    /// <summary>梯子のスプライン分割。</summary>
    public struct BeltSplineOptions
    {
        /// <summary>段間の補間分割の下限・上限。</summary>
        public const int SegmentsMin = 0;
        public const int SegmentsMax = 10;

        /// <summary>両端の切り詰め段数の下限・上限。</summary>
        public const int TrimMin = 0;
        public const int TrimMax = 10;

        [PLParam(TextKey = "BeltSplineEnabled", Description = "スプラインで rung を細分する")]
        public bool Enabled;

        /// <summary>段間の補間数。</summary>
        [PLParam(TextKey = "BeltSplineSegments", Description = "段間の補間数",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int Segments;

        [PLParam(TextKey = "BeltSplineUseFirst", Description = "先頭の rung を制御点に使う")]
        public bool UseFirst;

        [PLParam(TextKey = "BeltSplineUseLast", Description = "末尾の rung を制御点に使う")]
        public bool UseLast;

        [PLParam(TextKey = "BeltSplineTrimStart", Description = "先頭側を切り詰める段数",
                 Min = TrimMin, Max = TrimMax, Step = 1)]
        public int TrimStart;

        [PLParam(TextKey = "BeltSplineTrimEnd", Description = "末尾側を切り詰める段数",
                 Min = TrimMin, Max = TrimMax, Step = 1)]
        public int TrimEnd;

        /// <summary>
        /// 残す段の番号（スプライン分割と切り詰めのあとの番号、0 起点）。空なら全段を残す。
        /// 旧ツール（NCSHAGLIB の髪パイプ生成）の「残す梯子を番号で指定」にあたる。
        /// スプライン分割を切っていても効く。閉じた梯子には効かない。
        /// </summary>
        [PLParam(TextKey = "BeltSplineKeepRungs",
                 Description = "残す段の番号（スプライン分割と切り詰めのあとの番号、0 起点）。空にすると全段を残す。分割を切っていても効く")]
        public int[] KeepRungs;

        public bool HasKeepRungs => KeepRungs != null && KeepRungs.Length > 0;

        public static BeltSplineOptions Default => new BeltSplineOptions
        {
            Enabled   = false,
            Segments  = 1,
            UseFirst  = true,
            UseLast   = false,
            TrimStart = 0,
            TrimEnd   = 0,
            KeepRungs = System.Array.Empty<int>(),
        };
    }

    /// <summary>
    /// 前処理（向き補正・スプライン分割・残す段）の段の対応。逆方向（ModifierInverse）が使う。
    /// Source[k] は出力の段 k が元の梯子（向き補正の前）のどの段から来たか（小数。整数なら元の段そのもの）。
    /// </summary>
    public sealed class BeltRungTrace
    {
        public float[] Source;
        /// <summary>左右を入れ替えたか。</summary>
        public bool Swapped;
        /// <summary>並び順を反転したか（始点と終点も入れ替わる）。</summary>
        public bool Reversed;
    }

    /// <summary>基準ベルトの前処理と厚み付け。</summary>
    public static class BeltShapeOps
    {
        // ================================================================
        // 複製
        // ================================================================

        /// <summary>内容を複製する。元を書き換えないため、前処理は必ず複製へ書く。</summary>
        public static BeltCsvEntry Clone(BeltCsvEntry b)
        {
            if (b == null) return null;
            return new BeltCsvEntry
            {
                Left         = b.Left  != null ? new List<Vector3>(b.Left)  : new List<Vector3>(),
                Right        = b.Right != null ? new List<Vector3>(b.Right) : new List<Vector3>(),
                LeftWeights  = b.LeftWeights  != null ? new List<BoneWeight?>(b.LeftWeights)  : null,
                RightWeights = b.RightWeights != null ? new List<BoneWeight?>(b.RightWeights) : null,
                Closed      = b.Closed,
                FlipWinding = b.FlipWinding,
                HeightScale = b.HeightScale,
                StartPoint  = b.StartPoint,
                EndPoint    = b.EndPoint,
                GroupId     = b.GroupId,
                RowIndex    = b.RowIndex,
                RowCount    = b.RowCount,
            };
        }

        // ================================================================
        // 向き補正
        // ================================================================

        /// <summary>
        /// 左右入れ替え・並び順反転を適用する。
        /// 入れ替えは巻き方向を反転させ、段番号も反転させる
        /// （段グループの t=0 側と t=1 側が入れ替わるため）。
        /// </summary>
        public static BeltCsvEntry ApplyOrient(BeltCsvEntry belt, BeltOrientOptions opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (opt.IsIdentity) return belt;

            var left  = new List<Vector3>(belt.Left);
            var right = new List<Vector3>(belt.Right);

            // ウェイト列も点列と同じ入替・反転を受ける。片方だけ掛けると
            // 段とウェイトの対応がずれ、黙って別のボーンへ付く。
            var leftW  = belt.LeftWeights  != null ? new List<BoneWeight?>(belt.LeftWeights)  : null;
            var rightW = belt.RightWeights != null ? new List<BoneWeight?>(belt.RightWeights) : null;

            var start = belt.StartPoint;
            var end   = belt.EndPoint;
            bool flip = belt.FlipWinding;
            int  rowCount = Mathf.Max(1, belt.RowCount);
            int  rowIndex = Mathf.Clamp(belt.RowIndex, 0, rowCount - 1);

            if (opt.SwapSides)
            {
                var tmp = left; left = right; right = tmp;
                var tmpW = leftW; leftW = rightW; rightW = tmpW;
                flip = !flip;
                rowIndex = rowCount - 1 - rowIndex;
            }

            if (opt.ReverseOrder)
            {
                left.Reverse();
                right.Reverse();
                leftW?.Reverse();
                rightW?.Reverse();
                var tmp = start; start = end; end = tmp;
                flip = !flip;
            }

            return new BeltCsvEntry
            {
                Left         = left,
                Right        = right,
                LeftWeights  = leftW,
                RightWeights = rightW,
                Closed      = belt.Closed,
                FlipWinding = flip,
                HeightScale = belt.HeightScale,
                StartPoint  = start,
                EndPoint    = end,
                GroupId     = belt.GroupId,
                RowIndex    = rowIndex,
                RowCount    = rowCount,
            };
        }

        // ================================================================
        // スプライン分割
        // ================================================================

        /// <summary>
        /// rung 列をスプラインで細分する。閉じた梯子は対象外（そのまま返す）。
        /// 細分に失敗したときも元をそのまま返す。
        /// </summary>
        public static BeltCsvEntry ApplySpline(BeltCsvEntry belt, BeltSplineOptions opt)
            => ApplySplineTraced(belt, opt, out _);

        /// <summary>
        /// スプライン分割と残す段を掛け、出力の段ごとに「入力のどの段から来たか（小数）」を返す。
        /// 掛けなかったときも恒等の対応を返す。ApplySpline はこれを呼ぶだけ（規則は 1 か所）。
        /// </summary>
        public static BeltCsvEntry ApplySplineTraced(BeltCsvEntry belt, BeltSplineOptions opt, out float[] source)
        {
            source = null;
            if (belt == null || !belt.HasData) return belt;

            var s = ApplySplineOnly(belt, opt, out var srcParams);
            int ns = Mathf.Min(s.Left.Count, s.Right.Count);
            if (srcParams == null)
            {
                srcParams = new List<float>(ns);
                for (int i = 0; i < ns; i++) srcParams.Add(i);
            }

            var k = ApplyKeepRungs(s, opt, out var keep);
            if (keep != null)
            {
                var picked = new float[keep.Count];
                for (int i = 0; i < keep.Count; i++) picked[i] = srcParams[keep[i]];
                source = picked;
            }
            else source = srcParams.ToArray();
            return k;
        }

        /// <summary>
        /// 向き補正 → スプライン分割 → 残す段を掛け、段の対応を返す（逆方向が使う）。
        /// Preprocess と同じ結果を返す。
        /// </summary>
        public static BeltCsvEntry PreprocessTraced(
            BeltCsvEntry belt, BeltOrientOptions orient, BeltSplineOptions spline, out BeltRungTrace trace)
        {
            trace = null;
            if (belt == null || !belt.HasData) return belt;
            int n0 = Mathf.Min(belt.Left.Count, belt.Right.Count);

            var o = ApplyOrient(belt, orient);
            var s = ApplySplineTraced(o, spline, out var src);

            if (src != null && orient.ReverseOrder)
                for (int i = 0; i < src.Length; i++) src[i] = (n0 - 1) - src[i];

            trace = new BeltRungTrace
            {
                Source   = src,
                Swapped  = orient.SwapSides,
                Reversed = orient.ReverseOrder,
            };
            return s;
        }

        /// <summary>残す段を掛ける。掛けたときは残した段の番号を keep に返す（掛けなければ null）。</summary>
        private static BeltCsvEntry ApplyKeepRungs(BeltCsvEntry belt, BeltSplineOptions opt, out List<int> keep)
        {
            keep = null;
            if (belt == null || !belt.HasData) return belt;
            if (!opt.HasKeepRungs || belt.Closed) return belt;
            var k = KeepIndices(Mathf.Min(belt.Left.Count, belt.Right.Count), opt.KeepRungs);
            if (k.Count < 2) return belt;
            keep = k;
            return ApplyKeepRungs(belt, opt);
        }

        /// <summary>
        /// 残す段だけを残す（BeltSplineOptions.KeepRungs）。指定が無い・閉じた梯子・
        /// 残る段が 2 未満のときは元をそのまま返す。ウェイト列も同じ段を残す。
        /// </summary>
        public static BeltCsvEntry ApplyKeepRungs(BeltCsvEntry belt, BeltSplineOptions opt)
        {
            if (belt == null || !belt.HasData) return belt;
            if (!opt.HasKeepRungs || belt.Closed) return belt;

            var keep = KeepIndices(Mathf.Min(belt.Left.Count, belt.Right.Count), opt.KeepRungs);
            if (keep.Count < 2) return belt;

            var b = Clone(belt);
            b.Left  = Pick(belt.Left,  keep);
            b.Right = Pick(belt.Right, keep);
            b.LeftWeights  = belt.LeftWeights  != null ? Pick(belt.LeftWeights,  keep) : null;
            b.RightWeights = belt.RightWeights != null ? Pick(belt.RightWeights, keep) : null;
            return b;
        }

        /// <summary>残す段の番号を、0..count-1 の範囲で昇順・重複なしにして返す。</summary>
        public static List<int> KeepIndices(int count, int[] keepRungs)
        {
            var set = new HashSet<int>(keepRungs ?? System.Array.Empty<int>());
            var result = new List<int>();
            for (int i = 0; i < count; i++)
                if (set.Contains(i)) result.Add(i);
            return result;
        }

        /// <summary>list から indices の要素だけを順に取り出す。範囲外の番号は飛ばす。</summary>
        public static List<T> Pick<T>(IReadOnlyList<T> list, List<int> indices)
        {
            var result = new List<T>(indices.Count);
            foreach (int i in indices)
                if (i >= 0 && i < list.Count) result.Add(list[i]);
            return result;
        }

        private static BeltCsvEntry ApplySplineOnly(BeltCsvEntry belt, BeltSplineOptions opt, out List<float> sourceParams)
        {
            sourceParams = null;
            if (belt == null || !belt.HasData) return belt;
            if (!opt.Enabled) return belt;
            if (belt.Closed)  return belt;

            if (!BeltSplineSubdivider.Subdivide(
                    belt.Left, belt.Right, belt.StartPoint, belt.EndPoint,
                    opt.Segments, opt.UseFirst, opt.UseLast, opt.TrimStart, opt.TrimEnd,
                    out var left, out var right, out var srcParams))
                return belt;

            sourceParams = new List<float>(srcParams);

            // 細分で段が増えるので、ウェイトは「出力段が入力のどこから来たか」で補間する。
            // srcParams[k] は入力段の位置（小数）。
            var leftW  = ResampleWeights(belt.LeftWeights,  srcParams);
            var rightW = ResampleWeights(belt.RightWeights, srcParams);

            return new BeltCsvEntry
            {
                Left         = left,
                Right        = right,
                LeftWeights  = leftW,
                RightWeights = rightW,
                Closed      = false,
                FlipWinding = belt.FlipWinding,
                HeightScale = belt.HeightScale,
                StartPoint  = belt.StartPoint,
                EndPoint    = belt.EndPoint,
                GroupId     = belt.GroupId,
                RowIndex    = belt.RowIndex,
                RowCount    = belt.RowCount,
            };
        }

        /// <summary>
        /// 入力段のウェイト列を、出力段の位置（小数）で引き直す。
        /// 端は詰め、間は挟む 2 段の線形補間（SkinWeightOps.LerpNullable）。
        /// 入力が無いときは null を返す。
        /// </summary>
        private static List<BoneWeight?> ResampleWeights(
            List<BoneWeight?> src, IReadOnlyList<float> srcParams)
        {
            if (src == null || src.Count == 0 || srcParams == null) return null;

            var dst = new List<BoneWeight?>(srcParams.Count);

            for (int k = 0; k < srcParams.Count; k++)
            {
                float t = Mathf.Clamp(srcParams[k], 0f, src.Count - 1);
                int   i0 = Mathf.FloorToInt(t);
                int   i1 = Mathf.Min(i0 + 1, src.Count - 1);
                float f  = t - i0;

                dst.Add(Poly_Ling.UI.SkinWeightOps.LerpNullable(src[i0], src[i1], f));
            }

            return dst;
        }

        /// <summary>向き補正 → スプライン分割の順で通す。</summary>
        public static BeltCsvEntry Preprocess(
            BeltCsvEntry belt, BeltOrientOptions orient, BeltSplineOptions spline)
            => PreprocessTraced(belt, orient, spline, out _);

        // ================================================================
        // 厚み付け
        // ================================================================

        /// <summary>
        /// メッシュ全面を1グループとして厚み付けする。
        /// 厚みが実質ゼロなら元をそのまま返す。失敗時も元を返す。
        /// </summary>
        public static MeshObject ApplySolidify(
            MeshObject part, float thickness, int segFront, int segBack,
            float edgeFront, float edgeBack, bool edgeInward, string meshName)
        {
            if (part == null || part.FaceCount == 0 || thickness <= 0.0001f) return part;

            var faces = new List<int>(part.FaceCount);
            for (int i = 0; i < part.FaceCount; i++) faces.Add(i);

            var r = FaceGroupSolidifier.Build(part, faces, new FaceGroupSolidifier.Params
            {
                Thickness     = thickness,
                SegmentsFront = segFront,
                SegmentsBack  = segBack,
                EdgeSizeFront = edgeFront,
                EdgeSizeBack  = edgeBack,
                EdgeInward    = edgeInward,
            }, meshName);

            if (!r.Ok) return part;

            // 逆方向の控え（段 15）：元の部品の枠を、厚み付けした立体の対応する頂点へ広げて付け替える。
            Poly_Ling.Ops.ModifierFrameTrace.Expanded(part, r.Mesh, r.SourceToVertices);
            return r.Mesh;
        }
    }
}
