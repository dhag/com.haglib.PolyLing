// NormalBrushOps.cs
// 法線ブラシ。ブラシ中心の列（ワールド座標）に沿って、範囲内の頂点の法線を変える。
// マウス経路（NormalEditToolHandler のブラシ）とコマンド（NormalBrushStrokeCommand）が
// 同じ ApplyStroke を通る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【範囲と減衰】
//   ブラシ半径は対象のローカル空間の単位（スカルプトと同じ）。中心はワールドで受け、
//   オブジェクトごとに ObjectToWorld の逆でローカルへ直す。
//   重みは (1 - d/r)^2 * Strength。範囲内の頂点の全スロットが対象（面単位では選ばない）。
//   「手動の法線編集からも守る」除外セットのコーナーは変えない。
//
// 【モード】
//   Comb    … 指定方向（Direction、ワールド）へ寄せる
//   Smooth  … 辺で繋がった隣接頂点の法線平均へ寄せる
//   Average … そのひと塗り（中心 1 点）の範囲内の法線平均へ寄せる
//
// 【左右対称】MirrorX なら、各中心をローカル X で反転した位置にも同じ塗りを掛け、
//   Comb の方向も X を反転して使う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public enum NormalBrushMode
    {
        /// <summary>指定方向へ寄せる。</summary>
        Comb,
        /// <summary>隣接頂点の法線平均へ寄せる。</summary>
        Smooth,
        /// <summary>ブラシ範囲内の法線平均へ寄せる。</summary>
        Average,
    }

    public static class NormalBrushOps
    {
        /// <summary>
        /// 1 オブジェクトにストロークを掛ける。
        /// </summary>
        /// <returns>書き換えたスロット数（延べ）。</returns>
        public static int ApplyStroke(
            MeshContext mc,
            IReadOnlyList<Vector3> centersWorld,
            float radius,
            float strength,
            NormalBrushMode mode,
            Vector3 directionWorld,
            bool mirrorX)
        {
            var mo = mc?.MeshObject;
            if (mo == null || centersWorld == null || centersWorld.Count == 0) return 0;
            if (radius <= 0f || strength <= 0f) return 0;

            Matrix4x4 m    = NormalEditOps.ObjectToWorld(mc);
            Matrix4x4 inv  = m.inverse;
            Vector3 dirLoc = m.transpose.MultiplyVector(directionWorld);
            dirLoc = dirLoc.sqrMagnitude < 1e-12f ? Vector3.up : dirLoc.normalized;

            var guard = NormalEditOps.CollectManualProtectedCorners(mo);

            // 頂点ごとの編集可能スロット（守るコーナーが使うスロットは外す）
            var editable = new List<int>[mo.Vertices.Count];
            var guarded  = new HashSet<(int, int)>();
            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var face = mo.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                for (int j = 0; j < face.VertexCount && j < face.UVIndices.Count; j++)
                {
                    int vi = face.VertexIndices[j];
                    if (vi < 0 || vi >= mo.Vertices.Count) continue;
                    int slot = face.UVIndices[j];
                    if (guard.Contains((fi, j))) { guarded.Add((vi, slot)); continue; }
                    var l = editable[vi] ?? (editable[vi] = new List<int>());
                    if (!l.Contains(slot)) l.Add(slot);
                }
            }

            // 隣接（Smooth 用）
            List<int>[] neighbors = null;
            if (mode == NormalBrushMode.Smooth)
            {
                neighbors = new List<int>[mo.Vertices.Count];
                foreach (var face in mo.Faces)
                {
                    if (face == null || face.VertexCount < 3) continue;
                    int n = face.VertexCount;
                    for (int j = 0; j < n; j++)
                    {
                        int a = face.VertexIndices[j], b = face.VertexIndices[(j + 1) % n];
                        if (a == b || a < 0 || b < 0 || a >= mo.Vertices.Count || b >= mo.Vertices.Count) continue;
                        (neighbors[a] ?? (neighbors[a] = new List<int>())).Add(b);
                        (neighbors[b] ?? (neighbors[b] = new List<int>())).Add(a);
                    }
                }
            }

            var dabs = new List<(Vector3 Center, Vector3 Dir)>();
            foreach (var cw in centersWorld)
            {
                Vector3 c = inv.MultiplyPoint3x4(cw);
                dabs.Add((c, dirLoc));
                if (mirrorX)
                    dabs.Add((new Vector3(-c.x, c.y, c.z), new Vector3(-dirLoc.x, dirLoc.y, dirLoc.z)));
            }

            int written = 0;
            float r2 = radius * radius;

            foreach (var dab in dabs)
            {
                // 範囲内の頂点と重み
                var hits = new List<(int Vi, float W)>();
                for (int vi = 0; vi < mo.Vertices.Count; vi++)
                {
                    if (editable[vi] == null) continue;
                    float d2 = (mo.Vertices[vi].Position - dab.Center).sqrMagnitude;
                    if (d2 > r2) continue;
                    float t = 1f - Mathf.Sqrt(d2) / radius;
                    hits.Add((vi, t * t * Mathf.Clamp01(strength)));
                }
                if (hits.Count == 0) continue;

                // Average の目標（塗り 1 回分の範囲内の平均）
                Vector3 avg = Vector3.zero;
                if (mode == NormalBrushMode.Average)
                {
                    foreach (var h in hits)
                        foreach (int s in editable[h.Vi]) avg += mo.Vertices[h.Vi].Normals[s];
                    if (avg.sqrMagnitude < 1e-12f) continue;
                    avg.Normalize();
                }

                // 目標は塗る前の値から決め、書き込みは後でまとめて行う（走査順に依存させない）
                var writes = new List<(int Vi, int Slot, Vector3 N)>();
                foreach (var h in hits)
                {
                    var vertex = mo.Vertices[h.Vi];
                    Vector3 goal;
                    switch (mode)
                    {
                        case NormalBrushMode.Comb:
                            goal = dab.Dir; break;
                        case NormalBrushMode.Average:
                            goal = avg; break;
                        default:
                        {
                            Vector3 sum = Vector3.zero;
                            var nb = neighbors[h.Vi];
                            if (nb == null) continue;
                            foreach (int k in nb)
                                foreach (var nn in mo.Vertices[k].Normals) sum += nn;
                            if (sum.sqrMagnitude < 1e-12f) continue;
                            goal = sum.normalized;
                            break;
                        }
                    }

                    foreach (int s in editable[h.Vi])
                    {
                        if (s < 0 || s >= vertex.Normals.Count) continue;
                        if (guarded.Contains((h.Vi, s))) continue;
                        Vector3 cur = vertex.Normals[s];
                        if (cur.sqrMagnitude < 1e-12f) continue;
                        writes.Add((h.Vi, s, Vector3.Slerp(cur.normalized, goal, h.W).normalized));
                    }
                }

                foreach (var w in writes)
                {
                    mo.Vertices[w.Vi].Normals[w.Slot] = w.N;
                    written++;
                }
            }

            if (written > 0)
            {
                NormalSmoothingOps.NormalizeSlotCounts(mo);
                NormalSmoothingOps.ValidateSlotInvariant(mo, mo.Name);
            }
            return written;
        }
    }
}
