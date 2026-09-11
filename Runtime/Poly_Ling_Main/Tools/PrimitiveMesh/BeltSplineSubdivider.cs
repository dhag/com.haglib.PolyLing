// BeltSplineSubdivider.cs
// 梯子状ベルトの左右レールをスプライン補間し、段数（rung）を増やす。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 原型: NCSHAGLIB EPMXToolFor_HairMaker_Common.GetSplineBeltPair / GetSplineSegment
//   刻みは t = i + j/(segments+1)（j=0..segments）。
//   先端／終端の点をスプライン算出に含めるかを選べる。
//
// 原型との違い: 最終原点 t=(点数-1) のサンプルを末尾に追加する（梯子の終端を保持するため）。
// 先頭／末尾サンプルの除外は、実際に先端点を足したときだけ行う。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    public static class BeltSplineSubdivider
    {
        /// <summary>
        /// 左右レールをスプライン補間して再サンプルする。
        /// 補間できない場合は false を返す（呼び出し側は元データを使う）。
        /// </summary>
        public static bool Subdivide(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            Vector3? startPoint, Vector3? endPoint,
            int segments, bool useFirst, bool useLast, int trimStart, int trimEnd,
            out List<Vector3> outLeft, out List<Vector3> outRight)
            => Subdivide(left, right, startPoint, endPoint,
                         segments, useFirst, useLast, trimStart, trimEnd,
                         out outLeft, out outRight, out _);

        /// <summary>
        /// 再サンプルに加えて、出力段が入力段のどこから来たかを返す。
        ///
        /// outSourceParams[k] は入力 rung 列（left / right）の位置で、小数を含む。
        /// 先頭に足した先端点のぶんはここで差し引いてある。
        /// ウェイトのように「点ではないが段に紐づく値」を一緒に運ぶために使う。
        /// </summary>
        public static bool Subdivide(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            Vector3? startPoint, Vector3? endPoint,
            int segments, bool useFirst, bool useLast, int trimStart, int trimEnd,
            out List<Vector3> outLeft, out List<Vector3> outRight,
            out List<float> outSourceParams)
        {
            outLeft         = null;
            outRight        = null;
            outSourceParams = null;

            if (left == null || right == null) return false;
            int n = Mathf.Min(left.Count, right.Count);
            if (n < 2) return false;
            if (segments < 0) segments = 0;

            // スプライン入力（必要なら先端点を両レールへ追加）
            var p0 = new List<Vector3>(n + 2);
            var p1 = new List<Vector3>(n + 2);

            bool addedFirst = useFirst && startPoint.HasValue;
            bool addedLast  = useLast  && endPoint.HasValue;

            if (addedFirst) { p0.Add(startPoint.Value); p1.Add(startPoint.Value); }
            for (int i = 0; i < n; i++) { p0.Add(left[i]); p1.Add(right[i]); }
            if (addedLast) { p0.Add(endPoint.Value); p1.Add(endPoint.Value); }

            int m = p0.Count;
            if (m < 2) return false;

            // 媒介変数列
            var ts = new List<float>();
            for (int i = 0; i < m - 1; i++)
                for (int j = 0; j <= segments; j++)
                    ts.Add(i + (float)j / (segments + 1));
            ts.Add(m - 1);   // 最終原点

            var s0 = Sample(p0, ts);
            var s1 = Sample(p1, ts);

            // 先端点そのものは rung ではないので落とす
            int from = addedFirst ? 1 : 0;
            int to   = addedLast ? s0.Count - 1 : s0.Count;

            from += Mathf.Max(0, trimStart);
            to   -= Mathf.Max(0, trimEnd);
            if (to - from < 2) return false;

            outLeft         = new List<Vector3>(to - from);
            outRight        = new List<Vector3>(to - from);
            outSourceParams = new List<float>(to - from);

            // ts は「先端点を足したあとの索引空間」の媒介変数。
            // 先端点は rung ではないので、足したぶんを引いて入力 rung の位置へ直す。
            float shift = addedFirst ? 1f : 0f;

            for (int i = from; i < to; i++)
            {
                outLeft .Add(s0[i]);
                outRight.Add(s1[i]);
                outSourceParams.Add(ts[i] - shift);
            }
            return true;
        }

        /// <summary>成分ごとに 3 本のスプラインを作り、媒介変数列で再サンプルする。</summary>
        private static List<Vector3> Sample(List<Vector3> pts, List<float> ts)
        {
            var xs = new List<float>(pts.Count);
            var ys = new List<float>(pts.Count);
            var zs = new List<float>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                xs.Add(pts[i].x);
                ys.Add(pts[i].y);
                zs.Add(pts[i].z);
            }

            var sx = new CubicSpline(xs);
            var sy = new CubicSpline(ys);
            var sz = new CubicSpline(zs);

            var result = new List<Vector3>(ts.Count);
            for (int i = 0; i < ts.Count; i++)
                result.Add(new Vector3(sx.GetValue(ts[i]), sy.GetValue(ts[i]), sz.GetValue(ts[i])));
            return result;
        }
    }
}
