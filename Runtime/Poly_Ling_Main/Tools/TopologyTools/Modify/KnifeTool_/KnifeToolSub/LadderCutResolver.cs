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
        public static LadderCutPlan Resolve(MeshObject mo, int startV, VertexPair seg, int endV,
            float cutRatio = 0.5f, int ratioAnchorVertex = -1)
        {
            if (mo == null) return LadderCutPlan.Fail("メッシュがありません");
            if (startV < 0 || startV >= mo.VertexCount) return LadderCutPlan.Fail("開始頂点が無効です");
            if (endV   < 0 || endV   >= mo.VertexCount) return LadderCutPlan.Fail("終了頂点が無効です");
            if (startV == endV) return LadderCutPlan.Fail("開始頂点と終了頂点が同じです");
            if (!seg.IsValid)   return LadderCutPlan.Fail("セグメント辺が無効です");

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);

            // 起点四角形と起点ラングを決める。
            // 近傍：seg が開始頂点の四角形の辺（従来経路。完全互換）→ firstRung=seg。
            // 遠い：seg がベルト上の離れた辺のとき、開始頂点の四角形から外向きベルトで
            //       seg に到達する起点ラング R を採用（方向は quad0→seg 外向きに固定）。
            int quad0 = FindQuad0(mo, edgeToFaces, startV, seg);
            VertexPair firstRung = seg;
            if (quad0 < 0)
            {
                if (!TryFindStartRung(mo, edgeToFaces, startV, seg, out quad0, out firstRung))
                    return LadderCutPlan.Fail("開始頂点からセグメントに到達できません");
            }

            var plan = new LadderCutPlan { Ok = true };
            // 連続ラング（共有四角形）を結ぶ遷移。比率の側方向伝播に使う。
            var transitions = new List<(VertexPair from, VertexPair to, int face)>();

            // --- 同一四角形内で終了する場合（対角線切断） ---
            if (mo.Faces[quad0].VertexIndices.Contains(endV))
            {
                if (AreAdjacentInFace(mo.Faces[quad0], startV, endV))
                    return LadderCutPlan.Fail("開始頂点と終了頂点が隣接しています");
                plan.FaceCuts.Add(new LadderFaceCut(quad0,
                    LadderAnchor.AtVertex(startV), LadderAnchor.AtVertex(endV)));
                return plan;
            }

            // --- 起点面：開始頂点 → 起点ラング ---
            plan.Rungs.Add(firstRung);
            plan.FaceCuts.Add(new LadderFaceCut(quad0,
                LadderAnchor.AtVertex(startV), LadderAnchor.AtRung(firstRung)));

            var visited = new HashSet<int> { quad0 };
            VertexPair current = firstRung;

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
                    FillRungParams(mo, plan, transitions, seg, cutRatio, ratioAnchorVertex);
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
                transitions.Add((current, opp.Value, nextFace));
                current = opp.Value;
            }

            return LadderCutPlan.Fail("巡回が長すぎます");
        }

        /// <summary>
        /// seg を切断辺代表として採用できるか（開始頂点の四角形へベルトが届くか）。
        /// 受理判定・ハイライト判定用。終了頂点は不要。
        /// </summary>
        public static bool IsSegmentReachable(MeshObject mo, int startV, VertexPair seg)
        {
            if (mo == null) return false;
            if (startV < 0 || startV >= mo.VertexCount) return false;
            if (!seg.IsValid) return false;
            if (seg.Contains(startV)) return false;

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            if (FindQuad0(mo, edgeToFaces, startV, seg) >= 0) return true;
            return TryFindStartRung(mo, edgeToFaces, startV, seg, out _, out _);
        }

        /// <summary>
        /// seg が開始頂点を角に持つ四角形の辺（開始頂点に隣接しない）なら、その四角形を返す。
        /// 従来の近傍判定と完全に同一。到達しなければ -1。
        /// </summary>
        private static int FindQuad0(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, int startV, VertexPair seg)
        {
            if (seg.Contains(startV)) return -1;
            if (!edgeToFaces.TryGetValue(seg, out var segFaces)) return -1;

            foreach (int f in segFaces)
            {
                var face = mo.Faces[f];
                if (face.VertexCount != 4) continue;
                if (!face.VertexIndices.Contains(startV)) continue;
                return f;
            }
            return -1;
        }

        /// <summary>
        /// seg が開始頂点の四角形の辺でない（＝ベルト上の離れた辺）とき、
        /// 開始頂点を角に持つ四角形から外向きにベルトを辿り seg に到達する起点ラング R を探す。
        /// 見つかれば quad0=その四角形, firstRung=R。方向は quad0→seg 外向きに固定される。
        /// </summary>
        private static bool TryFindStartRung(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, int startV, VertexPair seg,
            out int quad0, out VertexPair firstRung)
        {
            quad0 = -1;
            firstRung = default;

            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face.VertexCount != 4) continue;
                if (!face.VertexIndices.Contains(startV)) continue;

                var verts = face.VertexIndices;
                for (int i = 0; i < 4; i++)
                {
                    var r = new VertexPair(verts[i], verts[(i + 1) % 4]);
                    if (r.Contains(startV)) continue; // 開始頂点に隣接する辺は起点ラングにできない
                    if (BeltOutwardReaches(mo, edgeToFaces, f, r, seg))
                    {
                        quad0 = f;
                        firstRung = r;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// quad0 から firstRung を起点に外向き（quad0 の反対側）へベルトを辿り、
        /// target ラングに到達できるか。到達で true。非四角形/境界/上限で終端。
        /// </summary>
        private static bool BeltOutwardReaches(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, int quad0, VertexPair firstRung, VertexPair target)
        {
            if (firstRung == target) return true;

            var visited = new HashSet<int> { quad0 };
            VertexPair current = firstRung;

            for (int iter = 0; iter < MAX_ITER; iter++)
            {
                if (!edgeToFaces.TryGetValue(current, out var faces)) return false;

                int nextFace = -1;
                foreach (int f in faces)
                {
                    if (!visited.Contains(f)) { nextFace = f; break; }
                }
                if (nextFace < 0) return false;

                var face = mo.Faces[nextFace];
                if (face.VertexCount != 4) return false;

                var opp = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return false;
                if (opp.Value == target) return true;

                visited.Add(nextFace);
                current = opp.Value;
            }
            return false;
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

        /// <summary>
        /// クリック比率をセグメント位置から全ラングへ同一側・同一比率で伝播し、
        /// plan.RungParams に格納する。seg が巡回に含まれなければ何もしない（＝中点扱い）。
        /// </summary>
        private static void FillRungParams(MeshObject mo, LadderCutPlan plan,
            List<(VertexPair from, VertexPair to, int face)> transitions,
            VertexPair seg, float cutRatio, int ratioAnchorVertex)
        {
            int count = plan.Rungs.Count;
            if (count == 0) return;

            int segIdx = plan.Rungs.IndexOf(seg);
            if (segIdx < 0) return; // seg 不在（想定外）→ 全ラング中点にフォールバック

            float ratio = cutRatio < 0f ? 0f : (cutRatio > 1f ? 1f : cutRatio);
            int segAnchor = (ratioAnchorVertex == seg.V1 || ratioAnchorVertex == seg.V2)
                ? ratioAnchorVertex : seg.V1;

            var anchors = new int[count];
            for (int i = 0; i < count; i++) anchors[i] = -1;
            anchors[segIdx] = segAnchor;

            // 下方向: transitions[k] は Rungs[k]→Rungs[k+1]。既知 Rungs[k+1] から Rungs[k] を求める。
            for (int k = segIdx - 1; k >= 0; k--)
            {
                var tr = transitions[k];
                anchors[k] = OppositeAnchorSafe(mo, tr.face, tr.to, anchors[k + 1], plan.Rungs[k]);
            }
            // 上方向: 既知 Rungs[k] から Rungs[k+1] を求める。
            for (int k = segIdx; k < count - 1; k++)
            {
                var tr = transitions[k];
                anchors[k + 1] = OppositeAnchorSafe(mo, tr.face, tr.from, anchors[k], plan.Rungs[k + 1]);
            }

            for (int i = 0; i < count; i++)
                plan.RungParams[plan.Rungs[i]] = new LadderCutPlan.RungCutParam(anchors[i], ratio);
        }

        /// <summary>
        /// 四角形 face 内で、既知辺 knownEdge 上の頂点 knownAnchor と側辺で接続する
        /// 対辺側の頂点を返す。取得不能なら -1。
        /// </summary>
        private static int OppositeAnchor(MeshObject mo, int faceIdx, VertexPair knownEdge, int knownAnchor)
        {
            if (faceIdx < 0 || faceIdx >= mo.FaceCount) return -1;
            var verts = mo.Faces[faceIdx].VertexIndices;
            int n = verts.Count;
            if (n != 4) return -1;
            for (int i = 0; i < n; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % n];
                if ((a == knownEdge.V1 && b == knownEdge.V2) || (a == knownEdge.V2 && b == knownEdge.V1))
                {
                    // 辺(a,b)=local(i,i+1)。側辺: a↔verts[i+3], b↔verts[i+2]。
                    if (knownAnchor == a) return verts[(i + 3) % n];
                    if (knownAnchor == b) return verts[(i + 2) % n];
                    return -1;
                }
            }
            return -1;
        }

        private static int OppositeAnchorSafe(MeshObject mo, int faceIdx, VertexPair knownEdge, int knownAnchor, VertexPair targetRung)
        {
            int a = OppositeAnchor(mo, faceIdx, knownEdge, knownAnchor);
            return a >= 0 ? a : targetRung.V1;
        }
    }
}
