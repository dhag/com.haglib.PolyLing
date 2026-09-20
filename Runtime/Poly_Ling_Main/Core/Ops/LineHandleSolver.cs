// LineHandleSolver.cs
// 線分群の点ごとのハンドル拘束（LineHandle.cs）から、ハンドルのずれを解き直す。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【決め方】
//   1. 向き：Chord / Free を先に決め、Tangent はその後で反対側から決める。
//      Chord  … その側の隣の点への単位ベクトル。
//      Free   … 今のずれの向き（長さ 0 なら弦の向き）。
//      Tangent… 反対側の向きの逆。両側とも Tangent なら、今の (出 − 入) の向きを共有する。
//   2. 長さ：Free / ChordRatio / Group を先に決め、EqualOpposite はその後で反対側から決める。
//      両側とも EqualOpposite なら、今の 2 本の長さの平均。
//   隣の点が無い側（開いた群の端）の Chord / ChordRatio / Tangent / EqualOpposite は
//   満たせないので、今の値を保ち、満たせなかった拘束として返す。
//
// 【座標】点の位置は MeshObject.Vertices[Order[i]].Position（ローカル）。ずれもローカル。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class LineHandleSolver
    {
        /// <summary>満たせなかった拘束 1 件。</summary>
        public struct Unsatisfied
        {
            /// <summary>群の中の点の位置（Order の添字）。</summary>
            public int PointIndex;
            /// <summary>true = 出、false = 入り。</summary>
            public bool IsOut;
            public string Reason;
        }

        /// <summary>
        /// 群のハンドルを解き直す。ハンドルを持たない群は何もしない。
        /// </summary>
        /// <returns>満たせなかった拘束。</returns>
        public static List<Unsatisfied> Solve(MeshObject mo, LineGroup g)
        {
            var bad = new List<Unsatisfied>();
            if (mo == null || g == null || !g.HasHandles) return bad;

            int n = g.Order.Count;
            var pos = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int vi = g.Order[i];
                pos[i] = (vi >= 0 && vi < mo.VertexCount) ? mo.Vertices[vi].Position : Vector3.zero;
            }

            var groupLen = new Dictionary<int, float>();
            if (g.LengthGroups != null)
                foreach (var lg in g.LengthGroups) if (lg != null) groupLen[lg.Id] = lg.Length;

            for (int i = 0; i < n; i++)
            {
                var h = g.PointHandles[i];
                if (h == null) { h = LinePointHandle.CreateDefault(); g.PointHandles[i] = h; }

                bool hasPrev = g.Closed || i > 0;
                bool hasNext = g.Closed || i < n - 1;
                Vector3 prevChord = hasPrev ? pos[(i - 1 + n) % n] - pos[i] : Vector3.zero;
                Vector3 nextChord = hasNext ? pos[(i + 1) % n] - pos[i] : Vector3.zero;

                // ---- 向き ----
                Vector3? dirIn  = BaseDirection(h.InConstraint,  h.InOffset,  prevChord, hasPrev, i, false, bad);
                Vector3? dirOut = BaseDirection(h.OutConstraint, h.OutOffset, nextChord, hasNext, i, true,  bad);

                bool inTan  = h.InConstraint.Direction  == HandleDirection.Tangent;
                bool outTan = h.OutConstraint.Direction == HandleDirection.Tangent;
                if (inTan && outTan)
                {
                    Vector3 t = h.OutOffset - h.InOffset;
                    if (t.sqrMagnitude < 1e-12f) t = nextChord - prevChord;
                    if (t.sqrMagnitude < 1e-12f) t = Vector3.right;
                    t.Normalize();
                    dirOut = t; dirIn = -t;
                }
                else if (inTan)
                {
                    if (dirOut.HasValue) dirIn = -dirOut.Value;
                    else { dirIn = SafeDir(h.InOffset, prevChord); Add(bad, i, false, "接線の相手（出）の向きが決まらない"); }
                }
                else if (outTan)
                {
                    if (dirIn.HasValue) dirOut = -dirIn.Value;
                    else { dirOut = SafeDir(h.OutOffset, nextChord); Add(bad, i, true, "接線の相手（入り）の向きが決まらない"); }
                }

                // ---- 長さ ----
                float? lenIn  = BaseLength(h.InConstraint,  h.InOffset,  prevChord, hasPrev, groupLen, i, false, bad);
                float? lenOut = BaseLength(h.OutConstraint, h.OutOffset, nextChord, hasNext, groupLen, i, true,  bad);

                bool inEq  = h.InConstraint.Length  == HandleLength.EqualOpposite;
                bool outEq = h.OutConstraint.Length == HandleLength.EqualOpposite;
                if (inEq && outEq)
                {
                    float avg = (h.InOffset.magnitude + h.OutOffset.magnitude) * 0.5f;
                    lenIn = avg; lenOut = avg;
                }
                else if (inEq)  lenIn  = lenOut ?? h.InOffset.magnitude;
                else if (outEq) lenOut = lenIn  ?? h.OutOffset.magnitude;

                h.InOffset  = (dirIn  ?? Vector3.zero) * (lenIn  ?? 0f);
                h.OutOffset = (dirOut ?? Vector3.zero) * (lenOut ?? 0f);
            }
            return bad;
        }

        /// <summary>メッシュのすべての群を解き直す。</summary>
        public static void SolveAll(MeshObject mo)
        {
            if (mo?.LineGroups == null) return;
            foreach (var g in mo.LineGroups) Solve(mo, g);
        }

        /// <summary>Tangent 以外の向き。Tangent なら null（後で反対側から決める）。</summary>
        private static Vector3? BaseDirection(
            HandleConstraint c, Vector3 offset, Vector3 chord, bool hasNeighbor,
            int i, bool isOut, List<Unsatisfied> bad)
        {
            switch (c.Direction)
            {
                case HandleDirection.Tangent:
                    return null;
                case HandleDirection.Chord:
                    if (hasNeighbor && chord.sqrMagnitude > 1e-12f) return chord.normalized;
                    Add(bad, i, isOut, "弦の相手の点が無い");
                    return SafeDir(offset, chord);
                default:
                    return SafeDir(offset, chord);
            }
        }

        /// <summary>EqualOpposite 以外の長さ。EqualOpposite なら null（後で反対側から決める）。</summary>
        private static float? BaseLength(
            HandleConstraint c, Vector3 offset, Vector3 chord, bool hasNeighbor,
            Dictionary<int, float> groupLen, int i, bool isOut, List<Unsatisfied> bad)
        {
            switch (c.Length)
            {
                case HandleLength.EqualOpposite:
                    return null;
                case HandleLength.ChordRatio:
                    if (hasNeighbor) return chord.magnitude * c.Ratio;
                    Add(bad, i, isOut, "弦の相手の点が無い");
                    return offset.magnitude;
                case HandleLength.Group:
                    if (groupLen.TryGetValue(c.LengthGroupId, out float l)) return l;
                    Add(bad, i, isOut, $"長さの組 {c.LengthGroupId} が無い");
                    return offset.magnitude;
                default:
                    return offset.magnitude;
            }
        }

        /// <summary>今のずれの向き。長さ 0 なら弦の向き、それも 0 なら零ベクトル。</summary>
        private static Vector3 SafeDir(Vector3 offset, Vector3 chord)
        {
            if (offset.sqrMagnitude > 1e-12f) return offset.normalized;
            if (chord.sqrMagnitude  > 1e-12f) return chord.normalized;
            return Vector3.zero;
        }

        private static void Add(List<Unsatisfied> bad, int i, bool isOut, string reason)
            => bad.Add(new Unsatisfied { PointIndex = i, IsOut = isOut, Reason = reason });
    }
}
