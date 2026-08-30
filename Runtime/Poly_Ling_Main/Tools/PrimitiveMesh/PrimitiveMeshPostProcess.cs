// PrimitiveMeshPostProcess.cs
// 基本図形生成後の共有後処理（頂点並べ替え等）。Runtime / Editor 共有。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    public static class PrimitiveMeshPostProcess
    {
        /// <summary>
        /// パーツIDごとに、頂点の並び順の先頭から 0,1,2… とサブIDを振り直す。
        /// 実体は PartsIdOps.AssignSubIdByPartsId。図形生成側からの呼び出し口として残す。
        /// </summary>
        public static void AssignSubIdByPartsId(MeshObject mo)
            => PartsIdOps.AssignSubIdByPartsId(mo);

        /// <summary>
        /// 頂点を Y 降順 → X 降順 → Z 降順（同値は元順で安定）に並べ替え、
        /// Face.VertexIndices を再マップする。
        /// Face.UVIndices / NormalIndices は各頂点の UVs / Normals へのサブ参照であり、
        /// 頂点は自身の UV/法線を伴って移動するため不変（書き換え不要）。
        /// </summary>
        public static void SortVerticesCanonical(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count < 2) return;

            int n = mo.Vertices.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            System.Array.Sort(order, (a, b) =>
            {
                Vector3 pa = mo.Vertices[a].Position, pb = mo.Vertices[b].Position;
                int c = pb.y.CompareTo(pa.y); if (c != 0) return c; // Y 降順
                c = pb.x.CompareTo(pa.x);     if (c != 0) return c; // X 降順
                c = pb.z.CompareTo(pa.z);     if (c != 0) return c; // Z 降順
                return a.CompareTo(b);                              // 同値は元順（決定的）
            });

            var newVerts = new List<Vertex>(n);
            var oldToNew = new int[n];
            for (int k = 0; k < n; k++)
            {
                newVerts.Add(mo.Vertices[order[k]]);
                oldToNew[order[k]] = k;
            }
            // 並べ替えのみ。同じ Vertex インスタンスを詰め替えるだけなので
            // ウェイトの有無は変わらず、SkinKind の再計算は不要。
            mo.Vertices = newVerts;

            if (mo.Faces == null) return;
            foreach (var f in mo.Faces)
            {
                if (f == null || f.VertexIndices == null) continue;
                for (int j = 0; j < f.VertexIndices.Count; j++)
                {
                    int oi = f.VertexIndices[j];
                    if (oi >= 0 && oi < n) f.VertexIndices[j] = oldToNew[oi];
                }
            }
        }

        /// <summary>
        /// メッシュ全体を X について鏡映する（位置・法線を反転し、面の巻き順を戻す）。
        ///
        /// 2D 編集面の x をワールド -X へ載せる規約（AuthoringFrame）へ、
        /// 生成アルゴリズムに手を入れずに合わせるための後処理。
        /// 入力側のループを反転させる方式は、符号付き面積で内外を判断する処理
        /// （ベベルのオフセット等）の向きを狂わせるため採らない。
        /// UV は編集座標のまま保持する（正面ビューの画面座標と一致する）。
        /// </summary>
        public static void MirrorX(MeshObject mo)
        {
            if (mo == null) return;

            if (mo.Vertices != null)
            {
                foreach (var v in mo.Vertices)
                {
                    if (v == null) continue;
                    var p = v.Position; p.x = -p.x; v.Position = p;
                    if (v.Normals == null) continue;
                    for (int i = 0; i < v.Normals.Count; i++)
                    {
                        var n = v.Normals[i]; n.x = -n.x; v.Normals[i] = n;
                    }
                }
            }

            // 鏡映は巻き順の向きを反転させるので、面を裏返して元の表裏へ戻す。
            if (mo.Faces != null)
            {
                foreach (var f in mo.Faces)
                    f?.Flip();
            }

            mo.InvalidatePositionCache();
        }

        /// <summary>
        /// AABB サイズを基準にピボットぶん平行移動する。
        /// 基本図形と同じ規約（ピボット p で頂点を -p * サイズ だけ移動）に合わせる。
        /// AABB 中心へ寄せる処理は行わない（生成位置が元メッシュに依存する図形を動かさないため）。
        /// </summary>
        public static void ApplyPivotOffset(MeshObject mo, Vector3 pivot)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0) return;
            if (pivot == Vector3.zero) return;

            Vector3 min = mo.Vertices[0].Position, max = min;
            foreach (var v in mo.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            Vector3 size = max - min;

            Vector3 offset = new Vector3(pivot.x * size.x, pivot.y * size.y, pivot.z * size.z);
            if (offset == Vector3.zero) return;

            foreach (var v in mo.Vertices)
                v.Position -= offset;

            // Vertex.Position を直接書き換えたので位置キャッシュを無効化する（MeshObject.cs:822）。
            mo.InvalidatePositionCache();
        }

        /// <summary>
        /// メッシュ全体の面を裏返す。全 Face の頂点順を反転し、全頂点の法線を反転する。
        /// Normals の要素数は変えないため、スロット不変条件（UVs.Count == Normals.Count）は保たれる。
        /// </summary>
        public static void FlipFaces(MeshObject mo)
        {
            if (mo == null) return;

            if (mo.Faces != null)
            {
                foreach (var f in mo.Faces)
                {
                    if (f == null) continue;
                    f.Flip();
                }
            }

            if (mo.Vertices != null)
            {
                foreach (var v in mo.Vertices)
                {
                    if (v?.Normals == null) continue;
                    for (int i = 0; i < v.Normals.Count; i++)
                        v.Normals[i] = -v.Normals[i];
                }
            }
        }
    }
}
