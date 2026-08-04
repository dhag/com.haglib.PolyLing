// MediaPipeFaceHoleFiller.cs
// MediaPipe フェイスメッシュの内側の穴（目・口）を面で塞ぐ処理。
//
// 目（近傍に孤立頂点を持つ穴）:
//   孤立頂点のうち穴の重心に最も近い1点を中心、残りをリング点とする。
//   穴ループの各頂点を最も近いリング点へ割り当て、ループ辺ごとに三角形を張る。
//   割り当てが切り替わる箇所にはリング点2点＋ループ頂点1点の三角形を足す。
//   最後に中心点とリング点で三角形を張る（リング点数と同数）。
//
// 口（近傍に孤立頂点を持たない穴）:
//   XY の x が最小／最大の頂点を左右端とみなし、上下2チェーンへ分割する。
//   対応する頂点同士を四角形でつなぎ、左右端の2箇所は三角形で埋める。
//
// 境界ループの抽出・分類・巻き順合わせは BoundaryLoopUtil に委譲する。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    public static class MediaPipeFaceHoleFiller
    {
        // ================================================================
        // 目（孤立頂点あり）
        // ================================================================

        /// <summary>
        /// 孤立頂点（虹彩点）を使って穴を塞ぐ。張った面数を返す。
        /// </summary>
        public static int FillEye(MeshObject mo, BoundaryLoop hole, float orientationSign)
        {
            if (mo == null || hole == null) return 0;
            if (hole.Vertices.Count < 3 || hole.IsolatedVertices.Count < 2) return 0;

            var loop = BoundaryLoopUtil.OrientLoop(mo, hole.Vertices, orientationSign);
            int n = loop.Count;

            // 中心 = 穴の重心に最も近い孤立頂点。残りがリング点。
            Vector3 c = BoundaryLoopUtil.CentroidXY(mo, loop);
            int centerVi = -1;
            float bestD = float.MaxValue;
            foreach (int vi in hole.IsolatedVertices)
            {
                Vector3 p = mo.Vertices[vi].Position;
                float d = (new Vector2(p.x - c.x, p.y - c.y)).sqrMagnitude;
                if (d < bestD) { bestD = d; centerVi = vi; }
            }

            var ring = new List<int>();
            foreach (int vi in hole.IsolatedVertices)
                if (vi != centerVi) ring.Add(vi);
            if (ring.Count < 3) return 0;

            // リング点を中心まわりの角度順に並べ、ループと同じ巻き向きに揃える。
            Vector3 cp = mo.Vertices[centerVi].Position;
            ring.Sort((a, b) =>
            {
                Vector3 pa = mo.Vertices[a].Position;
                Vector3 pb = mo.Vertices[b].Position;
                float aa = Mathf.Atan2(pa.y - cp.y, pa.x - cp.x);
                float ab = Mathf.Atan2(pb.y - cp.y, pb.x - cp.x);
                return aa.CompareTo(ab);
            });
            ring = BoundaryLoopUtil.OrientLoop(mo, ring, orientationSign);
            int m = ring.Count;

            // ループ各頂点 → 最も近いリング点（ring 内の位置）
            var nearest = new int[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = mo.Vertices[loop[i]].Position;
                int bi = 0;
                float bd = float.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    Vector3 q = mo.Vertices[ring[k]].Position;
                    float d = (new Vector2(p.x - q.x, p.y - q.y)).sqrMagnitude;
                    if (d < bd) { bd = d; bi = k; }
                }
                nearest[i] = bi;
            }

            int added = 0;

            // ループ辺ごとの三角形＋割り当て切替部の三角形
            for (int i = 0; i < n; i++)
            {
                int u = loop[i];
                int v = loop[(i + 1) % n];
                int ru = nearest[i];
                int rv = nearest[(i + 1) % n];

                BoundaryLoopUtil.AddFaceOriented(mo, new List<int> { u, v, ring[ru] }, orientationSign);
                added++;

                if (ru != rv)
                {
                    // ru から rv までリング上を順に辿って隙間を埋める
                    int k = ru;
                    int guard = 0;
                    while (k != rv && guard++ <= m)
                    {
                        int kn = (k + 1) % m;
                        BoundaryLoopUtil.AddFaceOriented(mo, new List<int> { v, ring[kn], ring[k] }, orientationSign);
                        added++;
                        k = kn;
                    }
                }
            }

            // 中心とリング点で扇形に埋める
            for (int k = 0; k < m; k++)
            {
                BoundaryLoopUtil.AddFaceOriented(
                    mo, new List<int> { centerVi, ring[k], ring[(k + 1) % m] }, orientationSign);
                added++;
            }

            return added;
        }

        // ================================================================
        // 口（孤立頂点なし）
        // ================================================================

        /// <summary>
        /// 上下に対応する頂点を四角形でつないで穴を塞ぐ。張った面数を返す。
        /// </summary>
        public static int FillMouth(MeshObject mo, BoundaryLoop hole, float orientationSign)
        {
            if (mo == null || hole == null) return 0;
            var loop = hole.Vertices;
            int n = loop.Count;
            if (n < 4) return 0;

            // 左右端 = x が最小 / 最大の頂点
            int iMin = 0, iMax = 0;
            for (int i = 1; i < n; i++)
            {
                float x = mo.Vertices[loop[i]].Position.x;
                if (x < mo.Vertices[loop[iMin]].Position.x) iMin = i;
                if (x > mo.Vertices[loop[iMax]].Position.x) iMax = i;
            }
            if (iMin == iMax) return 0;

            // 左端→右端 と 右端→左端 の2チェーン（端点は含まない）
            var chainA = new List<int>();
            for (int k = 1; ; k++)
            {
                int idx = (iMin + k) % n;
                if (idx == iMax) break;
                chainA.Add(loop[idx]);
                if (k > n) return 0;
            }
            var chainB = new List<int>();
            for (int k = 1; ; k++)
            {
                int idx = (iMax + k) % n;
                if (idx == iMin) break;
                chainB.Add(loop[idx]);
                if (k > n) return 0;
            }
            if (chainA.Count == 0 || chainA.Count != chainB.Count) return 0;

            // chainB は右端→左端の並びなので反転して左→右に揃える
            chainB.Reverse();

            int left  = loop[iMin];
            int right = loop[iMax];
            int count = chainA.Count;
            int added = 0;

            // 左端の三角形
            BoundaryLoopUtil.AddFaceOriented(
                mo, new List<int> { left, chainA[0], chainB[0] }, orientationSign);
            added++;

            // 上下チェーンを四角形でつなぐ
            for (int k = 0; k + 1 < count; k++)
            {
                BoundaryLoopUtil.AddFaceOriented(
                    mo, new List<int> { chainA[k], chainA[k + 1], chainB[k + 1], chainB[k] }, orientationSign);
                added++;
            }

            // 右端の三角形
            BoundaryLoopUtil.AddFaceOriented(
                mo, new List<int> { right, chainB[count - 1], chainA[count - 1] }, orientationSign);
            added++;

            return added;
        }

        // ================================================================
        // まとめて実行
        // ================================================================

        /// <summary>
        /// 内側の穴をすべて塞ぐ。孤立頂点を持つ穴は目として、持たない穴は口として処理する。
        /// </summary>
        public static int FillAllHoles(MeshObject mo, List<BoundaryLoop> loops, float orientationSign)
        {
            if (mo == null || loops == null) return 0;
            int added = 0;
            foreach (var l in loops)
            {
                switch (l.Kind)
                {
                    case BoundaryLoopKind.HoleWithIsolated:
                        added += FillEye(mo, l, orientationSign);
                        break;
                    case BoundaryLoopKind.Hole:
                        added += FillMouth(mo, l, orientationSign);
                        break;
                }
            }
            return added;
        }
    }
}
