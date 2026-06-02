// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/LadderCutResolver.cs
// 入力 (開始頂点, セグメント辺, 終了頂点) からラダー切断計画を解決する。
// 巡回は BeltSelectMode と同方式（四角形の対辺を辿る）。全てインデックスベース。
// 解決不能なら Ok=false の計画を返す（呼び出し側は警告して何もしない）。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    public static class LadderCutResolver
    {
        private const int MAX_ITER = 4096;

        /// <summary>
        /// 開始頂点 → セグメント(1辺) → 終了頂点 でラダー切断計画を解決する。
        /// </summary>
        public static LadderCutPlan Resolve(MeshObject mo, int startV, VertexPair seg, int endV)
        {
            if (mo == null) return LadderCutPlan.Fail("メッシュがありません");
            if (startV < 0 || startV >= mo.VertexCount) return LadderCutPlan.Fail("開始頂点が無効です");
            if (endV   < 0 || endV   >= mo.VertexCount) return LadderCutPlan.Fail("終了頂点が無効です");
            if (startV == endV) return LadderCutPlan.Fail("開始頂点と終了頂点が同じです");
            if (!seg.IsValid)   return LadderCutPlan.Fail("セグメント辺が無効です");

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);

            // セグメントを含む面のうち、開始頂点を含む四角形を起点にする。
            if (!edgeToFaces.TryGetValue(seg, out var segFaces))
                return LadderCutPlan.Fail("セグメント辺が見つかりません");

            int quad0 = -1;
            foreach (int f in segFaces)
            {
                var face = mo.Faces[f];
                if (face.VertexCount != 4) continue;
                if (!face.VertexIndices.Contains(startV)) continue;
                if (seg.Contains(startV)) continue; // セグメントは開始頂点に隣接しない辺
                quad0 = f;
                break;
            }
            if (quad0 < 0)
                return LadderCutPlan.Fail("開始頂点とセグメントが同じ四角形にありません");

            var plan = new LadderCutPlan { Ok = true };

            // --- 同一四角形内で終了する場合（対角線切断） ---
            if (mo.Faces[quad0].VertexIndices.Contains(endV))
            {
                if (AreAdjacentInFace(mo.Faces[quad0], startV, endV))
                    return LadderCutPlan.Fail("開始頂点と終了頂点が隣接しています");
                plan.FaceCuts.Add(new LadderFaceCut(quad0,
                    LadderAnchor.AtVertex(startV), LadderAnchor.AtVertex(endV)));
                return plan;
            }

            // --- 起点面：開始頂点 → セグメント ---
            plan.Rungs.Add(seg);
            plan.FaceCuts.Add(new LadderFaceCut(quad0,
                LadderAnchor.AtVertex(startV), LadderAnchor.AtRung(seg)));

            var visited = new HashSet<int> { quad0 };
            VertexPair current = seg;

            for (int iter = 0; iter < MAX_ITER; iter++)
            {
                if (!edgeToFaces.TryGetValue(current, out var faces))
                    return LadderCutPlan.Fail("終了頂点に到達できません");

                int nextFace = -1;
                foreach (int f in faces)
                {
                    if (!visited.Contains(f)) { nextFace = f; break; }
                }
                if (nextFace < 0)
                    return LadderCutPlan.Fail("端で途切れ、終了頂点に到達できません");

                var face = mo.Faces[nextFace];

                // 終了面に到達？（終了頂点を角に持つ）
                if (face.VertexIndices.Contains(endV))
                {
                    // 終了頂点が現ラング辺の端点だと退化するので不可
                    if (current.Contains(endV))
                        return LadderCutPlan.Fail("終了頂点の指定が不正です");
                    plan.FaceCuts.Add(new LadderFaceCut(nextFace,
                        LadderAnchor.AtRung(current), LadderAnchor.AtVertex(endV)));
                    visited.Add(nextFace);
                    return plan;
                }

                // 中継は四角形のみ
                if (face.VertexCount != 4)
                    return LadderCutPlan.Fail("四角形でない面に突き当たりました（5角以上）");

                var opp = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid)
                    return LadderCutPlan.Fail("対辺が取得できません");

                plan.Rungs.Add(opp.Value);
                plan.FaceCuts.Add(new LadderFaceCut(nextFace,
                    LadderAnchor.AtRung(current), LadderAnchor.AtRung(opp.Value)));
                visited.Add(nextFace);
                current = opp.Value;
            }

            return LadderCutPlan.Fail("巡回が長すぎます");
        }

        /// <summary>
        /// 四角形面で指定辺の対辺を返す（BeltSelectMode.FindOppositeEdge 同方式）。
        /// </summary>
        private static VertexPair? FindOppositeEdge(Face face, VertexPair edge)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            if (n != 4) return null;

            for (int i = 0; i < n; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % n];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1))
                {
                    int oppStart = verts[(i + 2) % n];
                    int oppEnd   = verts[(i + 3) % n];
                    return new VertexPair(oppStart, oppEnd);
                }
            }
            return null;
        }

        /// <summary>
        /// 面内で2頂点が隣接（同一辺の端点）か。
        /// </summary>
        private static bool AreAdjacentInFace(Face face, int v1, int v2)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % n];
                if ((a == v1 && b == v2) || (a == v2 && b == v1)) return true;
            }
            return false;
        }
    }
}
