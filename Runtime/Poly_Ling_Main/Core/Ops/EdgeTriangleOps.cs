// EdgeTriangleOps.cs
// 辺から三角形。辺 (a, b) と新しい頂点 n で三角形を作る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【表裏】
//   辺を含む面（頂点 3 以上）がちょうど 1 枚 F なら、F での辺の向きの逆回りにする
//   （F が a→b なら新しい三角形は b→a→n）。隣り合う面で辺の向きが逆になり表裏がそろう。
//   0 枚（線分だけ）・2 枚以上のときは一意に決まらないので、視点（メッシュローカル）から
//   表が見える向きにする。判定は AddFaceTool と同じく NormalHelper.CalculateFaceNormal と
//   視点方向の内積。
//
// 【四角形】makeQuad で、辺を含む面がちょうど 1 枚で、それが三角形のとき、
//   新しい三角形を足さずに F の a–b の間へ n を差し込んで四角形にする（向きは F のまま）。
//
// 【新しい頂点】
//   ・UV … F があれば F が a・b で使っているスロット、無ければスロット 0 の UV を edgeT で補間。
//   ・法線 … 作った面（四角形にしたときはその面）の法線。
//   ・ウェイト・部品ID など … edgeT < 0.5 なら a、そうでなければ b を写す。
//   ・UV と法線のスロットは 0 の 1 組だけ（UVIndices == NormalIndices を保つ）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class EdgeTriangleOps
    {
        public struct Result
        {
            public int    VertexIndex;
            public int    FaceIndex;
            public bool   MadeQuad;
            /// <summary>"adjacentFace" / "view"</summary>
            public string Winding;
        }

        /// <summary>辺 (a, b) を含む面（頂点 3 以上）と、その面で a→b の向きか。</summary>
        public static List<(int face, bool forward)> FacesOfEdge(MeshObject mo, int a, int b)
        {
            var list = new List<(int, bool)>();
            if (mo == null) return list;
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var vi = mo.Faces[fi].VertexIndices;
                int n = vi.Count;
                if (n < 3) continue;
                for (int k = 0; k < n; k++)
                {
                    int u = vi[k], w = vi[(k + 1) % n];
                    if (u == a && w == b) { list.Add((fi, true));  break; }
                    if (u == b && w == a) { list.Add((fi, false)); break; }
                }
            }
            return list;
        }

        /// <summary>四角形にできるか（辺を含む面がちょうど 1 枚の三角形）。</summary>
        public static bool CanMakeQuad(MeshObject mo, int a, int b)
        {
            var faces = FacesOfEdge(mo, a, b);
            return faces.Count == 1 && mo.Faces[faces[0].face].VertexIndices.Count == 3;
        }

        public static bool Execute(
            MeshObject mo, int a, int b, Vector3 position, float edgeT,
            bool makeQuad, Vector3 viewLocal, int materialIndex,
            out Result result, out string reason)
        {
            result = default;
            if (mo == null) { reason = "メッシュがありません"; return false; }
            int nv = mo.Vertices.Count;
            if (a < 0 || a >= nv || b < 0 || b >= nv) { reason = $"辺の頂点が範囲外です（{a}, {b}／頂点数 {nv}）"; return false; }
            if (a == b) { reason = "辺の 2 頂点が同じです"; return false; }

            edgeT = Mathf.Clamp01(edgeT);
            var faces = FacesOfEdge(mo, a, b);
            Face adj = faces.Count == 1 ? mo.Faces[faces[0].face] : null;

            int slotA = adj != null ? SlotOf(adj, a) : 0;
            int slotB = adj != null ? SlotOf(adj, b) : 0;

            var va = mo.Vertices[a];
            var vb = mo.Vertices[b];
            var vtx = (edgeT < 0.5f ? va : vb).Clone();
            vtx.Id = 0;
            vtx.Position = position;
            Vector2 uvA = slotA < va.UVs.Count ? va.UVs[slotA] : Vector2.zero;
            Vector2 uvB = slotB < vb.UVs.Count ? vb.UVs[slotB] : Vector2.zero;
            vtx.UVs     = new List<Vector2> { Vector2.Lerp(uvA, uvB, edgeT) };
            vtx.Normals = new List<Vector3> { Vector3.up };

            // ── 四角形
            if (makeQuad && adj != null && adj.VertexIndices.Count == 3)
            {
                int n = mo.AddVertex(vtx);
                var vi = adj.VertexIndices;
                int k = 0;
                for (; k < 3; k++)
                {
                    int u = vi[k], w = vi[(k + 1) % 3];
                    if ((u == a && w == b) || (u == b && w == a)) break;
                }
                int from = vi[k], to = vi[(k + 1) % 3];
                adj.VertexIndices.Insert(k + 1, n);
                adj.UVIndices.Insert(k + 1, 0);
                adj.NormalIndices.Insert(k + 1, 0);

                mo.Vertices[n].Normals[0] = NormalHelper.CalculateFaceNormal(
                    mo.Vertices[from].Position, position, mo.Vertices[to].Position);

                result = new Result { VertexIndex = n, FaceIndex = faces[0].face, MadeQuad = true, Winding = "adjacentFace" };
                reason = null;
                return true;
            }

            // ── 三角形
            int p0, p1;
            string winding;
            if (adj != null)
            {
                // 隣の面の辺の向きの逆回り
                if (faces[0].forward) { p0 = b; p1 = a; }
                else                  { p0 = a; p1 = b; }
                winding = "adjacentFace";
            }
            else
            {
                p0 = a; p1 = b;
                Vector3 nrm = NormalHelper.CalculateFaceNormal(mo.Vertices[p0].Position, mo.Vertices[p1].Position, position);
                Vector3 center = (mo.Vertices[p0].Position + mo.Vertices[p1].Position + position) / 3f;
                if (Vector3.Dot(nrm, viewLocal - center) < 0f) { p0 = b; p1 = a; }
                winding = "view";
            }

            int newV = mo.AddVertex(vtx);
            mo.Vertices[newV].Normals[0] = NormalHelper.CalculateFaceNormal(
                mo.Vertices[p0].Position, mo.Vertices[p1].Position, position);

            var f = new Face { MaterialIndex = adj != null ? adj.MaterialIndex : materialIndex };
            f.VertexIndices.Add(p0);   f.UVIndices.Add(p0 == a ? slotA : slotB); f.NormalIndices.Add(p0 == a ? slotA : slotB);
            f.VertexIndices.Add(p1);   f.UVIndices.Add(p1 == a ? slotA : slotB); f.NormalIndices.Add(p1 == a ? slotA : slotB);
            f.VertexIndices.Add(newV); f.UVIndices.Add(0);                        f.NormalIndices.Add(0);
            int fi = mo.AddFace(f);

            result = new Result { VertexIndex = newV, FaceIndex = fi, MadeQuad = false, Winding = winding };
            reason = null;
            return true;
        }

        private static int SlotOf(Face f, int v)
        {
            int k = f.VertexIndices.IndexOf(v);
            return (k >= 0 && k < f.UVIndices.Count) ? f.UVIndices[k] : 0;
        }
    }
}
