// BeltAutoDetector.cs
// オブジェクト全体から梯子状ベルトを自動検出する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【検出条件】先端の三角形は「四角形と辺を1本だけ共有し、残り2辺が自由辺」で判定する。
//  開始側: 三角形の3辺のうち、他面と共有しているのがちょうど1本で、その相手が四角形。
//          その辺を最初の rung、残る1頂点を開始点とする。
//  終了側: 開始側と同じ条件。四角形を辿った先の三角形が、rung 以外の2辺を共有していなければ
//          終了とみなし、rung に含まれない頂点を終了点とする。
//  開始点と終了点が両方そろったものだけを採用する。
//
// 【他の三角形は見ない】以前は「他の三角形と1頂点で接すること」を開始条件にしていたが、
//  ・先端が三角形1枚だけのモデルが検出できない
//  ・隣り合う rung 辺から2本の梯子が始まる形（先端三角形どうしが頂点を共有する形）が
//    「2頂点で接する」と判定されて両方落ちる
//  の2点で誤りだった。開始・終了とも三角形と四角形の辺の関係だけで決める。
//
// 【閉じた梯子】辿った先の rung が最初の rung へ戻ったら Closed = true で打ち切る。
//
// rung は四角形部分のみ。先端の三角形は rung に含めず、開始点／終了点として保持する
// （rung 長 0 の縮退 rung を作らないため）。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>自動検出した梯子1本ぶん。</summary>
    public sealed class BeltAutoStrip
    {
        public readonly List<int> Left  = new List<int>();
        public readonly List<int> Right = new List<int>();
        public readonly List<int> Faces = new List<int>();

        public bool FlipWinding;
        public int  StartPoint = -1;
        public int  EndPoint   = -1;

        /// <summary>一周してつながっている（閉じた梯子）なら true。</summary>
        public bool Closed;

        public int RungCount => Left.Count;
    }

    public static class BeltAutoDetector
    {
        private const int MaxIter = 4096;

        /// <summary>オブジェクト全体を検索し、条件を満たす梯子をすべて返す。</summary>
        public static List<BeltAutoStrip> Detect(MeshObject mo, out string message)
        {
            var result = new List<BeltAutoStrip>();
            message = "";

            if (mo == null || mo.FaceCount == 0)
            {
                message = "面がありません";
                return result;
            }

            var edgeToFaces = BuildEdgeToFaces(mo);

            int startCandidates = 0;
            int dropped         = 0;
            var usedSignatures  = new HashSet<string>();

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                if (!IsTriangle(mo, fi)) continue;
                if (!TryGetTipRung(mo, edgeToFaces, fi, out var rung, out int apex, out int quad)) continue;

                startCandidates++;

                var strip = WalkBelt(mo, edgeToFaces, rung, quad);
                if (strip == null) { dropped++; continue; }

                strip.StartPoint = apex;

                string sig = FaceSignature(strip.Faces);
                if (!usedSignatures.Add(sig)) continue;

                strip.FlipWinding = DetectFlipWinding(mo, strip);
                result.Add(strip);
            }

            message = $"自動検索: {result.Count} 本（開始候補 {startCandidates} / 終了点なしで除外 {dropped}）";
            return result;
        }

        // ================================================================
        // 先端判定（開始・終了で共通）
        // ================================================================

        /// <summary>
        /// 三角形 fi が梯子の先端かを判定する。
        /// 条件は「他面と共有している辺がちょうど1本で、その相手が四角形」。
        /// 満たすとき rung（共有している辺）、apex（rung に含まれない頂点）、
        /// quad（相手の四角形）を返す。
        ///
        /// 他の三角形との頂点共有は見ない。先端どうしが頂点でつながっていても、
        /// 隣り合う rung 辺から別々の梯子が始まってもよい。
        /// </summary>
        private static bool TryGetTipRung(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, int fi,
            out VertexPair rung, out int apex, out int quad)
        {
            rung = default; apex = -1; quad = -1;

            var verts = mo.Faces[fi].VertexIndices;

            int sharedCount = 0;
            for (int i = 0; i < 3; i++)
            {
                var pair = new VertexPair(verts[i], verts[(i + 1) % 3]);
                if (!pair.IsValid) return false;

                int other = OtherFace(edgeToFaces, pair, fi);
                if (other < 0) continue;

                sharedCount++;
                if (sharedCount > 1) return false;   // 共有辺は1本だけ

                rung = pair;
                quad = other;
            }

            if (sharedCount != 1) return false;
            if (!IsQuad(mo, quad)) return false;

            for (int i = 0; i < 3; i++)
                if (verts[i] != rung.V1 && verts[i] != rung.V2) apex = verts[i];

            return apex >= 0;
        }

        // ================================================================
        // 走査
        // ================================================================

        /// <summary>
        /// 開始 rung から四角形を辿り、終了三角形まで到達できたら梯子を返す。
        /// 最初の rung へ戻ってきた場合は閉じた梯子として打ち切る。
        /// </summary>
        private static BeltAutoStrip WalkBelt(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            VertexPair startRung, int startQuad)
        {
            var strip = new BeltAutoStrip();

            VertexPair current = startRung;
            int anchor  = startRung.V1;
            int faceIdx = startQuad;

            strip.Left.Add(anchor);
            strip.Right.Add(startRung.GetOtherVertex(anchor));

            var visitedFaces = new HashSet<int>();

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (faceIdx < 0 || !IsQuad(mo, faceIdx)) return null;
                if (!visitedFaces.Add(faceIdx)) return null;

                var face = mo.Faces[faceIdx];
                var opp  = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return null;

                int oppAnchor = OppositeAnchor(face, current, anchor);
                if (oppAnchor < 0) return null;

                strip.Faces.Add(faceIdx);

                // 一周して最初の rung へ戻った。rung を重複させずに閉じる。
                if (opp.Value == startRung)
                {
                    strip.Closed = true;
                    return strip.RungCount >= 3 ? strip : null;
                }

                strip.Left.Add(oppAnchor);
                strip.Right.Add(opp.Value.GetOtherVertex(oppAnchor));

                int next = OtherFace(edgeToFaces, opp.Value, faceIdx);
                if (next < 0) return null;

                if (IsTriangle(mo, next))
                {
                    // 終了三角形。開始側と同じ「共有辺は rung の1本だけ」で判定する。
                    if (!TryGetTipRung(mo, edgeToFaces, next, out var endRung, out int endPoint, out _))
                        return null;
                    if (endRung != opp.Value) return null;

                    strip.EndPoint = endPoint;
                    return strip.RungCount >= 2 ? strip : null;
                }

                current = opp.Value;
                anchor  = oppAnchor;
                faceIdx = next;
            }
            return null;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static bool IsQuad(MeshObject mo, int fi)
            => fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 4;

        private static bool IsTriangle(MeshObject mo, int fi)
            => fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 3;

        private static Dictionary<VertexPair, List<int>> BuildEdgeToFaces(MeshObject mo)
        {
            var map = new Dictionary<VertexPair, List<int>>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var v = mo.Faces[fi]?.VertexIndices;
                if (v == null || v.Count < 3) continue;
                for (int i = 0; i < v.Count; i++)
                {
                    var key = new VertexPair(v[i], v[(i + 1) % v.Count]);
                    if (!key.IsValid) continue;
                    if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
                    list.Add(fi);
                }
            }
            return map;
        }

        private static VertexPair? FindOppositeEdge(Face face, VertexPair edge)
        {
            var verts = face.VertexIndices;
            if (verts.Count != 4) return null;

            for (int i = 0; i < 4; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % 4];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1))
                    return new VertexPair(verts[(i + 2) % 4], verts[(i + 3) % 4]);
            }
            return null;
        }

        private static int OppositeAnchor(Face face, VertexPair knownEdge, int knownAnchor)
        {
            var verts = face.VertexIndices;
            if (verts.Count != 4) return -1;

            for (int i = 0; i < 4; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % 4];
                if ((a == knownEdge.V1 && b == knownEdge.V2) || (a == knownEdge.V2 && b == knownEdge.V1))
                {
                    if (knownAnchor == a) return verts[(i + 3) % 4];
                    if (knownAnchor == b) return verts[(i + 2) % 4];
                    return -1;
                }
            }
            return -1;
        }

        private static int OtherFace(Dictionary<VertexPair, List<int>> edgeToFaces, VertexPair edge, int exceptFace)
        {
            if (!edgeToFaces.TryGetValue(edge, out var list)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != exceptFace) return list[i];
            return -1;
        }

        private static bool DetectFlipWinding(MeshObject mo, BeltAutoStrip strip)
        {
            var verts = mo.Faces[strip.Faces[0]].VertexIndices;
            int l0 = strip.Left[0];
            int r0 = strip.Right[0];

            for (int i = 0; i < verts.Count; i++)
            {
                if (verts[i] != l0) continue;
                return verts[(i + 1) % verts.Count] != r0;
            }
            return false;
        }

        private static string FaceSignature(List<int> faces)
        {
            var sorted = new List<int>(faces);
            sorted.Sort();
            return string.Join(",", sorted);
        }
    }
}
