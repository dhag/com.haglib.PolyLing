// BeltRingDetector.cs
// オブジェクト全体から「円環状の梯子」（一周してつながる四角形列）を全部検出する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【検出条件】
//  四角形の対辺を辿り、開始 rung へ戻ってきたものを環とみなす。
//  さらに次を満たすものだけを採用する。
//   - 戻ってきたアンカー（左右の基準頂点）が開始アンカーと一致する
//     （不一致は左右が入れ替わって戻るねじれ。最後の1枚がねじれた四角形になるため棄却）
//   - rung 数が 3 以上
//   - 構成面がすべて未消費
//
// 【重複方向の扱い】
//  四角形には対辺のペアが 2 組あるため、同じ四角形から直交する 2 方向へ環を辿れる。
//  トーラスのように両方向とも閉じる形状では同じ面集合が二重に採れてしまうので、
//  採用した環の構成面を消費済みにし、消費済み面を含む候補は棄却する。
//  どちらの方向が残るかは面インデックスの昇順で決まる。
//
// 開始 rung には幾何的な必然性が無い（面インデックス順で決まる）。
// 断面プロファイルの向きや左右の割り当てはパネル側の「梯子の向き」で補正する。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    public static class BeltRingDetector
    {
        private const int MaxIter = 4096;

        /// <summary>オブジェクト全体を検索し、条件を満たす円環をすべて返す。</summary>
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
            var consumed    = new HashSet<int>();

            int droppedOverlap = 0;
            int droppedTwist   = 0;

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                if (!IsQuad(mo, fi)) continue;
                if (consumed.Contains(fi)) continue;

                var verts = mo.Faces[fi].VertexIndices;

                // 四角形の対辺ペアは 2 組。両方を開始 rung として試す。
                for (int e = 0; e < 2; e++)
                {
                    if (consumed.Contains(fi)) break;

                    var startRung = new VertexPair(verts[e], verts[e + 1]);
                    if (!startRung.IsValid) continue;

                    var strip = WalkRing(mo, edgeToFaces, startRung, fi, out bool twisted);

                    if (twisted) { droppedTwist++; continue; }
                    if (strip == null) continue;

                    bool overlap = false;
                    foreach (int f in strip.Faces)
                        if (consumed.Contains(f)) { overlap = true; break; }

                    if (overlap) { droppedOverlap++; continue; }

                    foreach (int f in strip.Faces) consumed.Add(f);

                    strip.Closed      = true;
                    strip.FlipWinding = DetectFlipWinding(mo, strip);
                    result.Add(strip);
                }
            }

            message = $"円環検索: {result.Count} 本（棄却: 重複 {droppedOverlap} / ねじれ {droppedTwist}）";
            return result;
        }

        // ================================================================
        // 走査
        // ================================================================

        /// <summary>
        /// startRung から startQuad 側へ対辺を辿り、開始 rung へ戻れたら環を返す。
        /// 左右が入れ替わって戻った場合は twisted = true にして null を返す。
        /// </summary>
        private static BeltAutoStrip WalkRing(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            VertexPair startRung, int startQuad, out bool twisted)
        {
            twisted = false;

            int startAnchor = startRung.V1;

            var strip = new BeltAutoStrip();
            strip.Left .Add(startAnchor);
            strip.Right.Add(startRung.GetOtherVertex(startAnchor));

            VertexPair current = startRung;
            int anchor  = startAnchor;
            int faceIdx = startQuad;

            var visitedFaces = new HashSet<int>();

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (!IsQuad(mo, faceIdx)) return null;
                if (!visitedFaces.Add(faceIdx)) return null;

                var face = mo.Faces[faceIdx];
                var opp  = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return null;

                int oppAnchor = OppositeAnchor(face, current, anchor);
                if (oppAnchor < 0) return null;

                strip.Faces.Add(faceIdx);

                if (opp.Value == startRung)
                {
                    // 一周した。左右が保たれているかを確認する。
                    if (oppAnchor != startAnchor) { twisted = true; return null; }
                    return strip.RungCount >= 3 ? strip : null;
                }

                strip.Left .Add(oppAnchor);
                strip.Right.Add(opp.Value.GetOtherVertex(oppAnchor));

                int next = OtherFace(edgeToFaces, opp.Value, faceIdx);
                if (next < 0) return null;   // 開いた列（境界に到達）

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

        /// <summary>四角形だけで 辺→面 マップを作る。</summary>
        private static Dictionary<VertexPair, List<int>> BuildEdgeToFaces(MeshObject mo)
        {
            var map = new Dictionary<VertexPair, List<int>>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var v = mo.Faces[fi]?.VertexIndices;
                if (v == null || v.Count != 4) continue;
                for (int i = 0; i < 4; i++)
                {
                    var key = new VertexPair(v[i], v[(i + 1) % 4]);
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

        /// <summary>先頭面の巻き順を調べ、(L[i], R[i], R[i+1], L[i+1]) と逆向きなら true。</summary>
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
    }
}
