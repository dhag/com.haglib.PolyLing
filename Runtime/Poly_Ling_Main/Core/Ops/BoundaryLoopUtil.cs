// BoundaryLoopUtil.cs
// メッシュの境界ループ（共有面を1つしか持たない辺で構成される環）を列挙・分類する共通処理。
// 辺の隣接情報は既存の Poly_Ling.Selection.TopologyCache に委譲し、ここでは
// 半辺マップを新規に構築しない。
//
// BoundaryRimExtruder（外縁の羽根拡張）と MediaPipeFaceHoleFiller（穴埋め）の
// 両方から使う。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>境界ループの種別。</summary>
    public enum BoundaryLoopKind
    {
        /// <summary>最も外側の輪郭。</summary>
        Outer,
        /// <summary>内側の穴で、近傍に孤立頂点（虹彩点など）を持つもの。</summary>
        HoleWithIsolated,
        /// <summary>内側の穴で、近傍に孤立頂点を持たないもの。</summary>
        Hole,
    }

    /// <summary>境界ループ1本分の情報。</summary>
    public sealed class BoundaryLoop
    {
        /// <summary>環状に並んだ頂点インデックス（重複なし・先頭と末尾は隣接）。</summary>
        public List<int> Vertices = new List<int>();

        /// <summary>種別。</summary>
        public BoundaryLoopKind Kind = BoundaryLoopKind.Hole;

        /// <summary>このループに割り当てられた孤立頂点（面に使われていない頂点）。</summary>
        public List<int> IsolatedVertices = new List<int>();
    }

    public static class BoundaryLoopUtil
    {
        // ================================================================
        // 境界ループ列挙
        // ================================================================

        /// <summary>
        /// 境界ループを列挙する。
        /// 境界辺の判定は <see cref="TopologyCache.GetBoundaryEdges"/>、
        /// 頂点に連なる辺の取得は <see cref="TopologyCache.GetPairsContaining"/> を使う。
        /// </summary>
        public static List<List<int>> FindBoundaryLoops(MeshObject mo, TopologyCache cache)
        {
            var loops = new List<List<int>>();
            if (mo == null || cache == null) return loops;

            // 境界辺のみの隣接表を作る（頂点 → 隣接境界頂点）。
            var adj = new Dictionary<int, List<int>>();
            foreach (var pair in cache.GetBoundaryEdges())
            {
                AddAdj(adj, pair.V1, pair.V2);
                AddAdj(adj, pair.V2, pair.V1);
            }

            var visited = new HashSet<int>();
            foreach (var start in adj.Keys)
            {
                if (visited.Contains(start)) continue;

                var loop = new List<int> { start };
                visited.Add(start);

                int prev = -1;
                int cur  = start;
                while (true)
                {
                    int next = -1;
                    foreach (int cand in adj[cur])
                    {
                        if (cand == prev) continue;
                        if (visited.Contains(cand)) continue;
                        next = cand;
                        break;
                    }
                    if (next < 0) break;   // 閉じた or 行き止まり

                    loop.Add(next);
                    visited.Add(next);
                    prev = cur;
                    cur  = next;
                }

                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        private static void AddAdj(Dictionary<int, List<int>> adj, int a, int b)
        {
            if (!adj.TryGetValue(a, out var list))
            {
                list = new List<int>();
                adj[a] = list;
            }
            if (!list.Contains(b)) list.Add(b);
        }

        // ================================================================
        // 孤立頂点（面に使われていない頂点）
        // ================================================================

        /// <summary>どの面にも使われていない頂点インデックスを返す。</summary>
        public static List<int> FindIsolatedVertices(MeshObject mo)
        {
            var result = new List<int>();
            if (mo == null) return result;

            var used = new HashSet<int>();
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null) continue;
                foreach (int vi in f.VertexIndices) used.Add(vi);
            }
            for (int i = 0; i < mo.VertexCount; i++)
                if (!used.Contains(i)) result.Add(i);

            return result;
        }

        // ================================================================
        // 分類
        // ================================================================

        /// <summary>
        /// 境界ループを外周／穴に分類し、孤立頂点を最も近い穴へ割り当てる。
        /// 外周は XY 外接矩形の面積が最大のループとする。
        /// </summary>
        public static List<BoundaryLoop> Classify(MeshObject mo, List<List<int>> loops, List<int> isolated)
        {
            var result = new List<BoundaryLoop>();
            if (mo == null || loops == null) return result;

            foreach (var l in loops)
                result.Add(new BoundaryLoop { Vertices = l, Kind = BoundaryLoopKind.Hole });

            // 外周 = XY 外接矩形の面積が最大
            int outerIdx = -1;
            float best = -1f;
            for (int i = 0; i < result.Count; i++)
            {
                float a = BoundsAreaXY(mo, result[i].Vertices);
                if (a > best) { best = a; outerIdx = i; }
            }
            if (outerIdx >= 0) result[outerIdx].Kind = BoundaryLoopKind.Outer;

            // 孤立頂点を最も近い「穴」の重心へ割り当てる
            if (isolated != null)
            {
                foreach (int vi in isolated)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    Vector3 p = mo.Vertices[vi].Position;

                    int bestLoop = -1;
                    float bestD  = float.MaxValue;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (result[i].Kind == BoundaryLoopKind.Outer) continue;
                        Vector3 c = CentroidXY(mo, result[i].Vertices);
                        float d = (new Vector2(p.x - c.x, p.y - c.y)).sqrMagnitude;
                        if (d < bestD) { bestD = d; bestLoop = i; }
                    }
                    if (bestLoop >= 0) result[bestLoop].IsolatedVertices.Add(vi);
                }
            }

            foreach (var l in result)
            {
                if (l.Kind == BoundaryLoopKind.Outer) continue;
                l.Kind = l.IsolatedVertices.Count >= 2
                    ? BoundaryLoopKind.HoleWithIsolated
                    : BoundaryLoopKind.Hole;
            }

            return result;
        }

        // ================================================================
        // 幾何ヘルパー
        // ================================================================

        /// <summary>頂点列の XY 外接矩形の面積。</summary>
        public static float BoundsAreaXY(MeshObject mo, List<int> verts)
        {
            if (mo == null || verts == null || verts.Count == 0) return 0f;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (int vi in verts)
            {
                var p = mo.Vertices[vi].Position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            return (maxX - minX) * (maxY - minY);
        }

        /// <summary>頂点列の XY 重心（z は 0）。</summary>
        public static Vector3 CentroidXY(MeshObject mo, List<int> verts)
        {
            if (mo == null || verts == null || verts.Count == 0) return Vector3.zero;
            float sx = 0f, sy = 0f;
            foreach (int vi in verts)
            {
                var p = mo.Vertices[vi].Position;
                sx += p.x; sy += p.y;
            }
            return new Vector3(sx / verts.Count, sy / verts.Count, 0f);
        }

        /// <summary>頂点列を閉多角形とみなした XY 符号付き面積。</summary>
        public static float SignedAreaXY(MeshObject mo, IList<int> verts)
        {
            if (mo == null || verts == null || verts.Count < 3) return 0f;
            float s = 0f;
            for (int i = 0; i < verts.Count; i++)
            {
                var a = mo.Vertices[verts[i]].Position;
                var b = mo.Vertices[verts[(i + 1) % verts.Count]].Position;
                s += a.x * b.y - b.x * a.y;
            }
            return s * 0.5f;
        }

        /// <summary>
        /// 既存面の XY 符号付き面積の総和の符号を返す（+1 / -1）。
        /// 生成する面の巻き順をこれに合わせることで、法線の向きを既存面と揃える。
        /// XY 平面に対してほぼ正対したメッシュを前提とする。
        /// </summary>
        public static float FaceOrientationSignXY(MeshObject mo)
        {
            if (mo == null) return 1f;
            float sum = 0f;
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count < 3) continue;
                sum += SignedAreaXY(mo, f.VertexIndices);
            }
            return sum < 0f ? -1f : 1f;
        }

        /// <summary>
        /// 頂点列を、XY 符号付き面積の符号が sign と一致する向きに並べ替えて返す。
        /// </summary>
        public static List<int> OrientLoop(MeshObject mo, List<int> loop, float sign)
        {
            var result = new List<int>(loop);
            float a = SignedAreaXY(mo, result);
            if ((a < 0f && sign > 0f) || (a > 0f && sign < 0f)) result.Reverse();
            return result;
        }

        /// <summary>
        /// 面を追加する。XY 符号付き面積の符号が sign と一致しない場合は反転してから追加する。
        /// UV / 法線サブインデックスは 0 とする。
        /// </summary>
        public static void AddFaceOriented(MeshObject mo, List<int> indices, float sign, int materialIndex = 0)
        {
            if (mo == null || indices == null || indices.Count < 3) return;

            var vi = new List<int>(indices);
            float a = SignedAreaXY(mo, vi);
            if ((a < 0f && sign > 0f) || (a > 0f && sign < 0f)) vi.Reverse();

            var uvi = new List<int>(vi.Count);
            var ni  = new List<int>(vi.Count);
            for (int k = 0; k < vi.Count; k++) { uvi.Add(0); ni.Add(0); }

            mo.AddFace(new Face
            {
                VertexIndices = vi,
                UVIndices     = uvi,
                NormalIndices = ni,
                MaterialIndex = materialIndex,
            });
        }
    }
}
