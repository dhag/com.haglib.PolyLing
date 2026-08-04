// BoundaryRimExtruder.cs
// メッシュの外縁（最も外側の境界ループ）を外向きに拡張し、
// 「餃子の羽根」状のフランジを張る処理。
//
// 手順:
//   1. 共有面を1つしか持たない辺群（境界辺）から境界ループを得る
//   2. 外周ループの各頂点について、隣の2点がなす線分に XY 平面で垂直な向きを求める
//   3. ループ重心から外向きの符号を選び、その方向へ新頂点を作る（z / UV は元頂点を継承）
//   4. 隣り合う元頂点・新頂点で四角形を張る
//
// 境界の抽出は BoundaryLoopUtil（内部で TopologyCache を使用）に委譲する。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    public static class BoundaryRimExtruder
    {
        /// <summary>
        /// 外周ループを幅 width だけ外向きに拡張する。
        /// </summary>
        /// <param name="mo">対象メッシュ（破壊的に変更する）</param>
        /// <param name="loop">外周ループの頂点列（環状）</param>
        /// <param name="width">拡張幅（Unity単位）</param>
        /// <param name="orientationSign">既存面の XY 巻き順の符号</param>
        /// <returns>張った面の数</returns>
        public static int Extend(MeshObject mo, List<int> loop, float width, float orientationSign)
        {
            if (mo == null || loop == null || loop.Count < 3) return 0;
            if (width <= 0f) return 0;

            // 既存面と同じ巻き順になる向きへループを揃えておく。
            var ring = BoundaryLoopUtil.OrientLoop(mo, loop, orientationSign);
            int n = ring.Count;

            Vector3 center = BoundaryLoopUtil.CentroidXY(mo, ring);

            // ── 新頂点を作る ──────────────────────────────────────────
            var outer = new int[n];
            for (int i = 0; i < n; i++)
            {
                int vi   = ring[i];
                int prev = ring[(i - 1 + n) % n];
                int next = ring[(i + 1) % n];

                Vector3 p  = mo.Vertices[vi].Position;
                Vector3 pa = mo.Vertices[prev].Position;
                Vector3 pb = mo.Vertices[next].Position;

                // 隣の2点がなす線分（pa→pb）に XY 平面上で垂直な向き。
                Vector2 seg = new Vector2(pb.x - pa.x, pb.y - pa.y);
                Vector2 dir = new Vector2(seg.y, -seg.x);

                if (dir.sqrMagnitude < 1e-12f)
                {
                    // 退化（隣の2点が一致）した場合は重心からの放射方向を使う。
                    dir = new Vector2(p.x - center.x, p.y - center.y);
                    if (dir.sqrMagnitude < 1e-12f) dir = Vector2.up;
                }
                dir.Normalize();

                // 重心から見て外向きの符号を採る。
                Vector2 outward = new Vector2(p.x - center.x, p.y - center.y);
                if (Vector2.Dot(dir, outward) < 0f) dir = -dir;

                var src = mo.Vertices[vi];
                Vector3 np = new Vector3(p.x + dir.x * width, p.y + dir.y * width, p.z);

                Vector2 uv = (src.UVs != null && src.UVs.Count > 0) ? src.UVs[0] : Vector2.zero;
                outer[i] = mo.AddVertex(np, uv, Vector3.forward);
            }

            // ── 四角形を張る ──────────────────────────────────────────
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                int u = ring[i];
                int v = ring[(i + 1) % n];
                var quad = new List<int> { v, u, outer[i], outer[(i + 1) % n] };
                BoundaryLoopUtil.AddFaceOriented(mo, quad, orientationSign);
                added++;
            }
            return added;
        }
    }
}
