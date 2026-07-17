// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/BeltCutResolver.cs
// 1辺から両方向にベルトを巡回し、LadderCutPlan を生成する（BeltLoop モード用）。
// 巡回は BeltSelectMode.GetBeltData と同方式（四角形の対辺を辿る）。
//   - 一周（円筒）: 開始 rung へ回帰したら閉ループとして終端。
//   - 三角形終端: 端の三角形は apex→rung で閉じる。
//   - 境界/ngon 終端: 最後の rung で終端（切断なし）。
// 分割比率は NCutExecutor 側が等分（i/N）で決めるため、ここでは扱わない。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    public static class BeltCutResolver
    {
        private const int MAX_ITER = 4096;

        /// <summary>
        /// clickedEdge を含むベルト全体の切断計画を返す。構成不能なら Ok=false。
        /// </summary>
        public static LadderCutPlan Resolve(MeshObject mo, VertexPair clickedEdge,
            float cutRatio = 0.5f, int ratioAnchorVertex = -1)
        {
            if (mo == null) return LadderCutPlan.Fail("メッシュがありません");
            if (!clickedEdge.IsValid) return LadderCutPlan.Fail("辺が無効です");

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            if (!edgeToFaces.TryGetValue(clickedEdge, out var startFaces) || startFaces.Count == 0)
                return LadderCutPlan.Fail("辺に隣接する面がありません");

            var plan = new LadderCutPlan { Ok = true };
            var visitedRungs = new HashSet<VertexPair> { clickedEdge };
            var visitedFaces = new HashSet<int>();

            plan.Rungs.Add(clickedEdge);

            // クリック辺の比率を基準に、ベルト全周へ同一側・同一比率で伝播する。
            float ratio = cutRatio < 0f ? 0f : (cutRatio > 1f ? 1f : cutRatio);
            int startAnchor = (ratioAnchorVertex == clickedEdge.V1 || ratioAnchorVertex == clickedEdge.V2)
                ? ratioAnchorVertex : clickedEdge.V1;
            plan.RungParams[clickedEdge] = new LadderCutPlan.RungCutParam(startAnchor, ratio);

            // 開始辺の隣接面（最大2枚）それぞれから外側へ巡回。
            foreach (int faceIdx in startFaces)
                WalkBelt(mo, edgeToFaces, clickedEdge, startAnchor, ratio, faceIdx, visitedRungs, visitedFaces, plan);

            if (plan.FaceCuts.Count == 0)
                return LadderCutPlan.Fail("この辺からベルトを構成できません");

            return plan;
        }

        private static void WalkBelt(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            VertexPair startRung, int startAnchor, float ratio, int startFaceIdx,
            HashSet<VertexPair> visitedRungs, HashSet<int> visitedFaces, LadderCutPlan plan)
        {
            VertexPair current = startRung;
            int currentAnchor = startAnchor;
            int faceIdx = startFaceIdx;

            for (int iter = 0; iter < MAX_ITER; iter++)
            {
                if (faceIdx < 0 || faceIdx >= mo.FaceCount) break;
                if (visitedFaces.Contains(faceIdx)) break;

                var face = mo.Faces[faceIdx];
                int vc = face.VertexIndices.Count;

                if (vc == 4)
                {
                    var opp = FindOppositeEdge(face, current);
                    if (!opp.HasValue || !opp.Value.IsValid) break;

                    visitedFaces.Add(faceIdx);
                    plan.FaceCuts.Add(new LadderFaceCut(faceIdx,
                        LadderAnchor.AtRung(current), LadderAnchor.AtRung(opp.Value)));

                    if (visitedRungs.Contains(opp.Value))
                        break; // 閉ループ（円筒一周）。対辺は既存 rung なので追加しない。

                    int oppAnchor = OppositeAnchor(face, current, currentAnchor);
                    if (oppAnchor < 0) oppAnchor = opp.Value.V1;

                    plan.Rungs.Add(opp.Value);
                    visitedRungs.Add(opp.Value);
                    plan.RungParams[opp.Value] = new LadderCutPlan.RungCutParam(oppAnchor, ratio);

                    int next = OtherFace(edgeToFaces, opp.Value, faceIdx);
                    if (next < 0) break; // 境界

                    current = opp.Value;
                    currentAnchor = oppAnchor;
                    faceIdx = next;
                }
                else if (vc == 3)
                {
                    int apex = ApexOf(face, current);
                    if (apex >= 0)
                    {
                        visitedFaces.Add(faceIdx);
                        plan.FaceCuts.Add(new LadderFaceCut(faceIdx,
                            LadderAnchor.AtVertex(apex), LadderAnchor.AtRung(current)));
                    }
                    break; // 三角形終端
                }
                else
                {
                    break; // ngon 終端
                }
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 四角形 face 内で、既知辺 knownEdge 上の頂点 knownAnchor と側辺で接続する
        /// 対辺側の頂点を返す。取得不能なら -1。
        /// </summary>
        private static int OppositeAnchor(Face face, VertexPair knownEdge, int knownAnchor)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            if (n != 4) return -1;
            for (int i = 0; i < n; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % n];
                if ((a == knownEdge.V1 && b == knownEdge.V2) || (a == knownEdge.V2 && b == knownEdge.V1))
                {
                    if (knownAnchor == a) return verts[(i + 3) % n];
                    if (knownAnchor == b) return verts[(i + 2) % n];
                    return -1;
                }
            }
            return -1;
        }

        /// <summary>四角形 face の指定辺の対辺を返す（順序は VertexPair 正規化）。</summary>
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
                    return new VertexPair(verts[(i + 2) % n], verts[(i + 3) % n]);
            }
            return null;
        }

        /// <summary>三角形 face のうち rung 端点でない頂点（apex）を返す。無ければ -1。</summary>
        private static int ApexOf(Face face, VertexPair rung)
        {
            var verts = face.VertexIndices;
            if (verts.Count != 3) return -1;
            for (int i = 0; i < 3; i++)
            {
                int v = verts[i];
                if (v != rung.V1 && v != rung.V2) return v;
            }
            return -1;
        }

        /// <summary>edge を共有する面のうち faceIdx でない方を返す。無ければ -1。</summary>
        private static int OtherFace(Dictionary<VertexPair, List<int>> edgeToFaces, VertexPair edge, int faceIdx)
        {
            if (!edgeToFaces.TryGetValue(edge, out var faces)) return -1;
            foreach (int f in faces)
                if (f != faceIdx) return f;
            return -1;
        }
    }
}
