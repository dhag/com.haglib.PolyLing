// SourcePolygons.cs
// ============================================================
// Unity メッシュの三角形から、元の MeshObject の多角形を組み直す（FBX 書き出し用）
// ------------------------------------------------------------
// ■ 前提（MeshBridgeDefault.ToUnityMesh(MeshObject, Matrix4x4) と Face.Triangulate）
//   ・サブメッシュ数は MeshObject.SubMeshCount。面の材質番号を 0〜サブメッシュ数−1 に丸めた番号へ入る。
//   ・面は MeshObject.Faces の順。3 頂点未満の面と非表示の面は飛ばす。
//   ・n 角形は角 0 を中心とした扇形の n−2 個の三角形 (c0,c1,c2), (c0,c2,c3), … になり、続けて並ぶ。
//   ・HierarchyBuilder の CompactSubMeshes は空のサブメッシュだけを取り除き、残りの順と中身は変えない。
// ■ 組み直し
//   サブメッシュごとに、そこへ入る面を順に取り、n−2 個の三角形から角を c0, c1, c2, 以降は各三角形の 3 番目として読む。
//   各三角形の 1 番目が c0、2 番目が直前の角であること、三角形をちょうど使い切ることを確かめる。
//   1 つでも合わなければ false（呼び出し側は三角形のまま書く）。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.FbxImpl
{
    internal static class SourcePolygons
    {
        /// <summary>Unity メッシュの三角形をそのまま面にする（サブメッシュ番号, 角の Unity 頂点番号）。</summary>
        public static List<(int sub, int[] corners)> Triangles(Mesh mesh)
        {
            var list = new List<(int, int[])>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(s);
                for (int f = 0; f + 2 < tris.Length; f += 3)
                    list.Add((s, new[] { tris[f], tris[f + 1], tris[f + 2] }));
            }
            return list;
        }

        /// <summary>元の MeshObject の多角形を組み直す（サブメッシュ番号, 角の Unity 頂点番号）。</summary>
        public static bool TryBuild(MeshObject source, Mesh mesh,
                                    out List<(int sub, int[] corners)> polygons, out string reason)
        {
            polygons = null;
            reason   = null;
            if (source == null) { reason = "元の MeshObject がありません"; return false; }
            if (mesh == null)   { reason = "Unity メッシュがありません"; return false; }

            int srcSub = source.SubMeshCount;
            if (srcSub <= 0) { reason = $"MeshObject のサブメッシュ数が {srcSub}"; return false; }

            // サブメッシュごとの面（面の順）
            var groups = new List<Face>[srcSub];
            for (int s = 0; s < srcSub; s++) groups[s] = new List<Face>();
            foreach (var face in source.Faces)
            {
                if (face == null || face.VertexCount < 3 || face.IsHidden) continue;
                groups[Mathf.Clamp(face.MaterialIndex, 0, srcSub - 1)].Add(face);
            }

            // Unity のサブメッシュ番号 → 面の組
            var groupOfSub = new List<List<Face>>();
            if (mesh.subMeshCount == srcSub)
            {
                groupOfSub.AddRange(groups);
            }
            else
            {
                foreach (var g in groups) if (g.Count > 0) groupOfSub.Add(g);   // 空のサブメッシュを除いた順
                if (groupOfSub.Count != mesh.subMeshCount)
                {
                    reason = $"サブメッシュ数が合いません（Unity {mesh.subMeshCount}、面のある材質 {groupOfSub.Count}）";
                    return false;
                }
            }

            var result = new List<(int, int[])>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var faces = groupOfSub[s];
                if (mesh.GetTopology(s) != MeshTopology.Triangles)
                {
                    if (faces.Count == 0) continue;
                    reason = $"サブメッシュ {s} が三角形ではありません";
                    return false;
                }
                var tris = mesh.GetTriangles(s);
                int pos = 0;
                foreach (var face in faces)
                {
                    int n = face.VertexCount;
                    if (pos + (n - 2) * 3 > tris.Length)
                    {
                        reason = $"サブメッシュ {s} の三角形が足りません";
                        return false;
                    }
                    var c = new int[n];
                    c[0] = tris[pos]; c[1] = tris[pos + 1]; c[2] = tris[pos + 2];
                    for (int k = 1; k <= n - 3; k++)
                    {
                        int t = pos + 3 * k;
                        if (tris[t] != c[0] || tris[t + 1] != c[k + 1])
                        {
                            reason = $"サブメッシュ {s} の三角形が扇形の並びになっていません";
                            return false;
                        }
                        c[k + 2] = tris[t + 2];
                    }
                    result.Add((s, c));
                    pos += (n - 2) * 3;
                }
                if (pos != tris.Length)
                {
                    reason = $"サブメッシュ {s} の三角形が余ります（{tris.Length - pos} 個の添字）";
                    return false;
                }
            }

            polygons = result;
            return true;
        }
    }
}
