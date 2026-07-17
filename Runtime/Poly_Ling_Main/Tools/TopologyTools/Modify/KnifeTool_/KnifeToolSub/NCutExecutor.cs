// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/NCutExecutor.cs
// LadderCutPlan を N 等分（N-1 本の等間隔カット）で実行する。
// LadderCutExecutor（単一・自由比率カット）とは独立。EqualDivide / BeltLoop 用。
// 等分割は各辺の分割点集合 {i/N} が対称なため、面ごとの winding で局所的に
// 平行ストリップを構成でき、大域的なアンカー伝播は不要。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    public static class NCutExecutor
    {
        /// <summary>
        /// 計画を N 等分で実行する。divisions は分割ピース数（≥2）。切断本数は divisions-1。
        /// </summary>
        public static void Execute(ToolContext ctx, MeshObject mo, LadderCutPlan plan, int divisions)
        {
            if (mo == null || plan == null || !plan.Ok) return;
            if (plan.FaceCuts.Count == 0) return;

            int cuts = Mathf.Max(1, divisions - 1);

            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : null;

            // 1) rung ごとに cuts 個の新頂点（V1→V2 に沿って i/divisions）
            var rungVerts = new Dictionary<VertexPair, int[]>();
            foreach (var rung in plan.Rungs)
            {
                if (rungVerts.ContainsKey(rung)) continue;
                var arr = new int[cuts];
                for (int i = 1; i <= cuts; i++)
                    arr[i - 1] = CreateCutVertexAt(mo, rung.V1, rung.V2, (float)i / divisions);
                rungVerts[rung] = arr;
            }

            // 2) 面ごとに分割（置換＋追加のみ。既存面の添字は不変）
            foreach (var cut in plan.FaceCuts)
            {
                if (cut.FaceIndex < 0 || cut.FaceIndex >= mo.FaceCount) continue;

                bool aVert = cut.A.Kind == LadderAnchorKind.Vertex;
                bool bVert = cut.B.Kind == LadderAnchorKind.Vertex;

                if (aVert && bVert)
                {
                    SplitVertexToVertex(mo, cut.FaceIndex, cut.A.VertexIndex, cut.B.VertexIndex);
                }
                else if (aVert != bVert)
                {
                    var vAnc = aVert ? cut.A : cut.B;
                    var eAnc = aVert ? cut.B : cut.A;
                    if (!rungVerts.TryGetValue(eAnc.RungEdge, out var pts)) continue;
                    SplitVertexToEdgeN(mo, cut.FaceIndex, vAnc.VertexIndex, eAnc.RungEdge, pts);
                }
                else
                {
                    if (!rungVerts.TryGetValue(cut.A.RungEdge, out var aPts)) continue;
                    if (!rungVerts.TryGetValue(cut.B.RungEdge, out var bPts)) continue;
                    SplitEdgeToEdgeN(mo, cut.FaceIndex, cut.A.RungEdge, aPts, cut.B.RungEdge, bPts);
                }
            }

            ctx.SyncMesh?.Invoke();

            if (ctx.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                ctx.UndoController.RecordMeshTopologyChange(before, after, "Knife N-Cut");
            }
        }

        // ================================================================
        // 頂点生成
        // ================================================================

        private static int CreateCutVertexAt(MeshObject mo, int v1, int v2, float t)
        {
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
        // 面分割
        // ================================================================

        /// <summary>辺→辺 の N 分割（四角形を N ストリップに）。</summary>
        private static void SplitEdgeToEdgeN(MeshObject mo, int faceIdx,
            VertexPair rungA, int[] aPts, VertexPair rungB, int[] bPts)
        {
            var orig  = mo.Faces[faceIdx];
            var verts = orig.VertexIndices;
            int n = verts.Count;
            if (n != 4) return;

            int ia = LocalOfEdge(orig, rungA);
            if (ia < 0) return;
            int c0 = verts[ia], c1 = verts[(ia + 1) % 4], c2 = verts[(ia + 2) % 4], c3 = verts[(ia + 3) % 4];

            // 対辺 B は (c2,c3) のはず
            if (!((rungB.V1 == c2 && rungB.V2 == c3) || (rungB.V1 == c3 && rungB.V2 == c2))) return;

            // aSeq: c0→c1 方向。bSeq: c3→c2 方向（c0 と同じ側=c3 起点）。
            int[] aSeq = (c0 == rungA.V1) ? aPts : Reversed(aPts);
            int[] bSeq = (c3 == rungB.V1) ? bPts : Reversed(bPts);
            if (aSeq.Length != bSeq.Length || aSeq.Length == 0) return;

            int m = aSeq.Length;
            var subs = new List<Face>();

            subs.Add(BuildSub(orig, new List<(int, bool)>
                { (c0, false), (aSeq[0], true), (bSeq[0], true), (c3, false) }));

            for (int k = 1; k < m; k++)
                subs.Add(BuildSub(orig, new List<(int, bool)>
                    { (aSeq[k - 1], true), (aSeq[k], true), (bSeq[k], true), (bSeq[k - 1], true) }));

            subs.Add(BuildSub(orig, new List<(int, bool)>
                { (aSeq[m - 1], true), (c1, false), (c2, false), (bSeq[m - 1], true) }));

            Commit(mo, faceIdx, subs);
        }

        /// <summary>頂点→辺 の N 分割（頂点から辺上 N-1 点への扇）。</summary>
        private static void SplitVertexToEdgeN(MeshObject mo, int faceIdx,
            int vGlobal, VertexPair rung, int[] pts)
        {
            var orig  = mo.Faces[faceIdx];
            var verts = orig.VertexIndices;
            int n = verts.Count;

            int vLocal = LocalOfVertex(orig, vGlobal);
            int eLocal = LocalOfEdge(orig, rung);
            if (vLocal < 0 || eLocal < 0) return;

            int e0   = verts[eLocal];       // 辺の始点（winding）
            int eEnd = (eLocal + 1) % n;

            int[] seq = (e0 == rung.V1) ? pts : Reversed(pts);
            if (seq.Length == 0) return;
            int m = seq.Length;

            var subs = new List<Face>();

            // 先頭面: vLocal→eLocal（inclusive）+ seq[0]
            {
                var c = new List<(int, bool)>();
                for (int i = vLocal; ; i = (i + 1) % n) { c.Add((verts[i], false)); if (i == eLocal) break; }
                c.Add((seq[0], true));
                subs.Add(BuildSub(orig, c));
            }
            // 中間三角形: [v, seq[k-1], seq[k]]
            for (int k = 1; k < m; k++)
                subs.Add(BuildSub(orig, new List<(int, bool)>
                    { (vGlobal, false), (seq[k - 1], true), (seq[k], true) }));
            // 末尾面: seq[m-1] + eEnd→vLocal（inclusive）
            {
                var c = new List<(int, bool)>();
                c.Add((seq[m - 1], true));
                for (int i = eEnd; ; i = (i + 1) % n) { c.Add((verts[i], false)); if (i == vLocal) break; }
                subs.Add(BuildSub(orig, c));
            }

            Commit(mo, faceIdx, subs);
        }

        /// <summary>頂点→頂点（単一対角、N 無視）。</summary>
        private static void SplitVertexToVertex(MeshObject mo, int faceIdx, int aGlobal, int bGlobal)
        {
            var orig  = mo.Faces[faceIdx];
            var verts = orig.VertexIndices;
            int n = verts.Count;

            int aLocal = LocalOfVertex(orig, aGlobal);
            int bLocal = LocalOfVertex(orig, bGlobal);
            if (aLocal < 0 || bLocal < 0 || aLocal == bLocal) return;

            var f1 = new List<(int, bool)>();
            for (int i = aLocal; ; i = (i + 1) % n) { f1.Add((verts[i], false)); if (i == bLocal) break; }
            var f2 = new List<(int, bool)>();
            for (int i = bLocal; ; i = (i + 1) % n) { f2.Add((verts[i], false)); if (i == aLocal) break; }

            Commit(mo, faceIdx, new List<Face> { BuildSub(orig, f1), BuildSub(orig, f2) });
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static Face BuildSub(Face orig, List<(int v, bool isNew)> corners)
        {
            var vi = new List<int>();
            var uv = new List<int>();
            var nm = new List<int>();
            foreach (var (v, isNew) in corners)
            {
                vi.Add(v);
                if (isNew) { uv.Add(0); nm.Add(0); }
                else
                {
                    int lo = LocalOfVertex(orig, v);
                    uv.Add(lo >= 0 && orig.UVIndices.Count     > lo ? orig.UVIndices[lo]     : 0);
                    nm.Add(lo >= 0 && orig.NormalIndices.Count > lo ? orig.NormalIndices[lo] : 0);
                }
            }
            return new Face
            {
                VertexIndices = vi,
                UVIndices     = uv,
                NormalIndices = nm,
                MaterialIndex = orig.MaterialIndex,
            };
        }

        private static void Commit(MeshObject mo, int faceIdx, List<Face> subs)
        {
            if (subs.Count == 0) return;
            mo.Faces[faceIdx] = subs[0];
            for (int k = 1; k < subs.Count; k++) mo.Faces.Add(subs[k]);
        }

        private static int LocalOfVertex(Face face, int gv)
        {
            for (int i = 0; i < face.VertexIndices.Count; i++)
                if (face.VertexIndices[i] == gv) return i;
            return -1;
        }

        private static int LocalOfEdge(Face face, VertexPair e)
        {
            int n = face.VertexIndices.Count;
            for (int i = 0; i < n; i++)
            {
                int a = face.VertexIndices[i];
                int b = face.VertexIndices[(i + 1) % n];
                if ((a == e.V1 && b == e.V2) || (a == e.V2 && b == e.V1)) return i;
            }
            return -1;
        }

        private static int[] Reversed(int[] a)
        {
            var r = new int[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[a.Length - 1 - i];
            return r;
        }
    }
}
