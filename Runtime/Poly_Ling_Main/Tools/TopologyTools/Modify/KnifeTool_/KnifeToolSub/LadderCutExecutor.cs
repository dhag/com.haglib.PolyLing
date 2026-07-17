// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/LadderCutExecutor.cs
// LadderCutPlan を消費して実際に面を分割する。
// 頂点挿入(UV/法線補間)・面分割・Undo 記録を一本化。
// 面分割ロジックは旧 KnifeTool_Core / KnifeTool_Vertex から移設。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    public static class LadderCutExecutor
    {
        private const float MID = 0.5f;

        /// <summary>
        /// 計画を実行する。失敗（Plan.Ok==false）の場合は何もしない。
        /// </summary>
        public static void Execute(ToolContext ctx, MeshObject mo, LadderCutPlan plan)
        {
            if (mo == null || plan == null || !plan.Ok) return;
            if (plan.FaceCuts.Count == 0) return;

            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : null;

            // 1) ラング辺ごとに中点頂点を生成（重複なし）
            var rungVertex = new Dictionary<VertexPair, int>();
            foreach (var rung in plan.Rungs)
            {
                if (rungVertex.ContainsKey(rung)) continue;
                int anchor; float ratio;
                if (plan.RungParams.TryGetValue(rung, out var rp)) { anchor = rp.AnchorVertex; ratio = rp.Ratio; }
                else { anchor = rung.V1; ratio = MID; }
                rungVertex[rung] = CreateCutVertex(mo, rung, anchor, ratio);
            }

            // 2) 面ごとに分割（面の置換＋追加のみ。既存面の添字はずれない）
            foreach (var cut in plan.FaceCuts)
            {
                if (cut.FaceIndex < 0 || cut.FaceIndex >= mo.FaceCount) continue;
                var face = mo.Faces[cut.FaceIndex];

                bool aVert = cut.A.Kind == LadderAnchorKind.Vertex;
                bool bVert = cut.B.Kind == LadderAnchorKind.Vertex;

                if (aVert && bVert)
                {
                    int la = LocalOfVertex(face, cut.A.VertexIndex);
                    int lb = LocalOfVertex(face, cut.B.VertexIndex);
                    if (la < 0 || lb < 0 || la == lb) continue;
                    SplitFaceVertexToVertex(mo, cut.FaceIndex, la, lb);
                }
                else if (aVert != bVert)
                {
                    var vAnc = aVert ? cut.A : cut.B;
                    var eAnc = aVert ? cut.B : cut.A;
                    int lv = LocalOfVertex(face, vAnc.VertexIndex);
                    int le = LocalOfEdge(face, eAnc.RungEdge);
                    if (lv < 0 || le < 0) continue;
                    if (!rungVertex.TryGetValue(eAnc.RungEdge, out int nv)) continue;
                    SplitFaceVertexToEdge(mo, cut.FaceIndex, lv, le, nv);
                }
                else
                {
                    int l0 = LocalOfEdge(face, cut.A.RungEdge);
                    int l1 = LocalOfEdge(face, cut.B.RungEdge);
                    if (l0 < 0 || l1 < 0 || l0 == l1) continue;
                    if (!rungVertex.TryGetValue(cut.A.RungEdge, out int n0)) continue;
                    if (!rungVertex.TryGetValue(cut.B.RungEdge, out int n1)) continue;
                    SplitFaceEdgeToEdge(mo, cut.FaceIndex, l0, n0, l1, n1);
                }
            }

            ctx.SyncMesh?.Invoke();

            if (ctx.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                ctx.UndoController.RecordMeshTopologyChange(before, after, "Knife Ladder Cut");
            }
        }

        // ================================================================
        // 頂点生成
        // ================================================================

        private static int CreateCutVertex(MeshObject mo, VertexPair edge, int anchorVertex, float ratio)
        {
            int v1 = edge.V1, v2 = edge.V2;
            // anchor 起点の比率を V1 起点の t に正規化（anchor==V2 なら 1-ratio）。
            float t = (anchorVertex == v2) ? (1f - ratio) : ratio;

            var p1 = mo.Vertices[v1].Position;
            var p2 = mo.Vertices[v2].Position;
            var v  = new Vertex(Vector3.Lerp(p1, p2, t));

            var a = mo.Vertices[v1];
            var b = mo.Vertices[v2];
            if (a.UVs.Count > 0 && b.UVs.Count > 0)
                v.UVs.Add(Vector2.Lerp(a.UVs[0], b.UVs[0], t));
            if (a.Normals.Count > 0 && b.Normals.Count > 0)
                v.Normals.Add(Vector3.Lerp(a.Normals[0], b.Normals[0], t).normalized);

            int idx = mo.VertexCount;
            mo.Vertices.Add(v);
            return idx;
        }

        // ================================================================
        // ローカル添字解決
        // ================================================================

        private static int LocalOfVertex(Face face, int globalVertexIndex)
        {
            for (int i = 0; i < face.VertexIndices.Count; i++)
                if (face.VertexIndices[i] == globalVertexIndex) return i;
            return -1;
        }

        private static int LocalOfEdge(Face face, VertexPair edge)
        {
            int n = face.VertexIndices.Count;
            for (int i = 0; i < n; i++)
            {
                int a = face.VertexIndices[i];
                int b = face.VertexIndices[(i + 1) % n];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1)) return i;
            }
            return -1;
        }

        // ================================================================
        // 面分割（旧 KnifeTool_Core / KnifeTool_Vertex から移設）
        // ================================================================

        /// <summary>辺→辺（2つの新頂点で分割）。</summary>
        private static void SplitFaceEdgeToEdge(
            MeshObject mo, int faceIdx, int edge0Local, int newV0, int edge1Local, int newV1)
        {
            var face = mo.Faces[faceIdx];
            var verts = face.VertexIndices;
            var uvs = face.UVIndices;
            var normals = face.NormalIndices;
            int n = verts.Count;

            int e0 = edge0Local, e1 = edge1Local;
            int nv0 = newV0, nv1 = newV1;
            if (e0 > e1) { (e0, e1) = (e1, e0); (nv0, nv1) = (nv1, nv0); }

            var f1V = new List<int>(); var f1U = new List<int>(); var f1N = new List<int>();
            f1V.Add(nv0); f1U.Add(0); f1N.Add(0);
            for (int i = e0 + 1; i <= e1; i++)
            {
                f1V.Add(verts[i]); f1U.Add(uvs.Count > i ? uvs[i] : 0); f1N.Add(normals.Count > i ? normals[i] : 0);
            }
            f1V.Add(nv1); f1U.Add(0); f1N.Add(0);

            var f2V = new List<int>(); var f2U = new List<int>(); var f2N = new List<int>();
            f2V.Add(nv1); f2U.Add(0); f2N.Add(0);
            for (int i = e1 + 1; i < n; i++)
            {
                f2V.Add(verts[i]); f2U.Add(uvs.Count > i ? uvs[i] : 0); f2N.Add(normals.Count > i ? normals[i] : 0);
            }
            for (int i = 0; i <= e0; i++)
            {
                f2V.Add(verts[i]); f2U.Add(uvs.Count > i ? uvs[i] : 0); f2N.Add(normals.Count > i ? normals[i] : 0);
            }
            f2V.Add(nv0); f2U.Add(0); f2N.Add(0);

            mo.Faces[faceIdx] = new Face { VertexIndices = f1V, UVIndices = f1U, NormalIndices = f1N, MaterialIndex = face.MaterialIndex };
            mo.Faces.Add(new Face { VertexIndices = f2V, UVIndices = f2U, NormalIndices = f2N, MaterialIndex = face.MaterialIndex });
        }

        /// <summary>頂点→辺（既存頂点から辺上の新頂点へ）。</summary>
        private static void SplitFaceVertexToEdge(
            MeshObject mo, int faceIdx, int vertexLocal, int edgeLocal, int newV)
        {
            var face = mo.Faces[faceIdx];
            var verts = face.VertexIndices;
            var uvs = face.UVIndices;
            var normals = face.NormalIndices;
            int n = verts.Count;
            int edgeEnd = (edgeLocal + 1) % n;

            var f1V = new List<int>(); var f1U = new List<int>(); var f1N = new List<int>();
            for (int i = vertexLocal; ; i = (i + 1) % n)
            {
                f1V.Add(verts[i]); f1U.Add(uvs.Count > i ? uvs[i] : 0); f1N.Add(normals.Count > i ? normals[i] : 0);
                if (i == edgeLocal) break;
            }
            f1V.Add(newV); f1U.Add(0); f1N.Add(0);

            var f2V = new List<int>(); var f2U = new List<int>(); var f2N = new List<int>();
            f2V.Add(newV); f2U.Add(0); f2N.Add(0);
            for (int i = edgeEnd; ; i = (i + 1) % n)
            {
                f2V.Add(verts[i]); f2U.Add(uvs.Count > i ? uvs[i] : 0); f2N.Add(normals.Count > i ? normals[i] : 0);
                if (i == vertexLocal) break;
            }

            mo.Faces[faceIdx] = new Face { VertexIndices = f1V, UVIndices = f1U, NormalIndices = f1N, MaterialIndex = face.MaterialIndex };
            mo.Faces.Add(new Face { VertexIndices = f2V, UVIndices = f2U, NormalIndices = f2N, MaterialIndex = face.MaterialIndex });
        }

        /// <summary>頂点→頂点（既存2頂点の対角分割）。</summary>
        private static void SplitFaceVertexToVertex(MeshObject mo, int faceIdx, int aLocal, int bLocal)
        {
            var face = mo.Faces[faceIdx];
            var verts = face.VertexIndices;
            var uvs = face.UVIndices;
            var normals = face.NormalIndices;
            int n = verts.Count;

            var f1V = new List<int>(); var f1U = new List<int>(); var f1N = new List<int>();
            for (int i = aLocal; ; i = (i + 1) % n)
            {
                f1V.Add(verts[i]); f1U.Add(uvs.Count > i ? uvs[i] : 0); f1N.Add(normals.Count > i ? normals[i] : 0);
                if (i == bLocal) break;
            }

            var f2V = new List<int>(); var f2U = new List<int>(); var f2N = new List<int>();
            for (int i = bLocal; ; i = (i + 1) % n)
            {
                f2V.Add(verts[i]); f2U.Add(uvs.Count > i ? uvs[i] : 0); f2N.Add(normals.Count > i ? normals[i] : 0);
                if (i == aLocal) break;
            }

            mo.Faces[faceIdx] = new Face { VertexIndices = f1V, UVIndices = f1U, NormalIndices = f1N, MaterialIndex = face.MaterialIndex };
            mo.Faces.Add(new Face { VertexIndices = f2V, UVIndices = f2U, NormalIndices = f2N, MaterialIndex = face.MaterialIndex });
        }
    }
}
