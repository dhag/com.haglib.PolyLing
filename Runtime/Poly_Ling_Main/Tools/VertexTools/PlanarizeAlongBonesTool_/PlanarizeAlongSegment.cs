// Tools/PlanarizeAlongSegment.cs
// A-B方向に直交する平面に頂点群を平面化するユーティリティ

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public static class PlanarizeAlongSegment
    {
        /// <summary>
        /// A-B 方向に平行移動だけ許して、頂点群を「ABに直交する平面」に平面化する。
        ///
        /// - anchorIndex &lt; 0 : 最小移動量（二乗和最小） =&gt; c = average(u·v_i)
        /// - anchorIndex &gt;= 0: 平面位置を A に固定        =&gt; c = u·A
        ///
        /// 戻り値: 実際に使った c（平面式 u·x=c の c）
        /// </summary>
        public static float Planarize(IList<Vector3> verts, Vector3 A, Vector3 B, int anchorIndex = -1)
        {
            if (verts == null || verts.Count == 0) return 0f;
            Vector3 d = B - A;
            float len = d.magnitude;
            if (len < 1e-8f) return 0f; // 方向が定義できない
            Vector3 u = d / len; // 単位方向（平面法線）

            // 平面位置 c を決める
            float c;
            if (anchorIndex >= 0)
            {
                // インデックスが範囲外なら「最小移動量」にフォールバック
                if (anchorIndex >= verts.Count)
                {
                    c = AverageDot(u, verts);
                }
                else
                {
                    // 「平面が A を通る」モード
                    c = Vector3.Dot(u, A);
                }
            }
            else
            {
                // 最小移動量モード
                c = AverageDot(u, verts);
            }

            // 各頂点を u 方向にスライドして u·x=c に揃える
            for (int i = 0; i < verts.Count; i++)
            {
                float s = Vector3.Dot(u, verts[i]);
                float alpha = (c - s);      // u が単位なので割り算不要
                verts[i] = verts[i] + alpha * u;
            }
            return c;
        }

        private static float AverageDot(Vector3 u, IList<Vector3> verts)
        {
            float sum = 0f;
            for (int i = 0; i < verts.Count; i++)
                sum += Vector3.Dot(u, verts[i]);
            return sum / verts.Count;
        }
    }
}
