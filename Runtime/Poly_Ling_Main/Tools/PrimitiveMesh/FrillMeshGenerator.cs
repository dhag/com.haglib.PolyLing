// FrillMeshGenerator.cs
// 基準ベルト（梯子状の四角形群）＋断面プロファイルからフリルメッシュを生成する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【構成】梯子を縦に置いた見立てで、rung 1区間（1ステップ）ごとに同じ波形を繰り返す。
//   左右の手すり（レール）それぞれに、その区間長で正規化した波形を置き、面でつなぐ。
//   左右のレール長が等しければ平坦なリボン、異なればスカートのフリル状になる。
//
// 【プロファイルの座標系】ステップ s（rung s → rung s+1）ごとに次の系で解釈する。
//   X = 進行方向（x=0 が rung s、x=1 が rung s+1）
//   Y = 基準ベルトの面法線方向（そのレール区間長で正規化。y=1 が区間長）
//
// 【巻き順】取り込み時に判定した基準ベルトの巻き順に従う。
//   断面が 2 点 (0,0)-(1,0) のときは基準ベルトと同一の面になる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Frill
{
    public static class FrillMeshGenerator
    {
        /// <summary>
        /// フリルメッシュを生成する。基準ベルトまたは断面が不足していれば空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool closed, bool flipWinding,
            IReadOnlyList<Vector2> profile, string meshName)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Frill" : meshName);

            int n = (left == null || right == null) ? 0 : Mathf.Min(left.Count, right.Count);
            int m = (profile == null) ? 0 : profile.Count;
            if (n < 2 || m < 2) return mo;

            int steps = closed ? n : n - 1;
            if (steps < 1) return mo;

            for (int s = 0; s < steps; s++)
            {
                int j = (s + 1) % n;

                Vector3 a0 = left[s],  a1 = left[j];
                Vector3 b0 = right[s], b1 = right[j];

                Vector3 dirA = a1 - a0;
                Vector3 dirB = b1 - b0;
                float   lenA = dirA.magnitude;
                float   lenB = dirB.magnitude;

                Vector3 nrm = StepNormal(a0, b0, b1, a1, flipWinding);

                // このステップの頂点: 左レール m 点 → 右レール m 点
                int baseIdx = mo.VertexCount;

                for (int k = 0; k < m; k++)
                {
                    Vector2 p = profile[k];
                    Vector3 pos = a0 + dirA * p.x + nrm * (p.y * lenA);
                    mo.Vertices.Add(new Vertex(pos, new Vector2((s + p.x) / steps, 0f)));
                }
                for (int k = 0; k < m; k++)
                {
                    Vector2 p = profile[k];
                    Vector3 pos = b0 + dirB * p.x + nrm * (p.y * lenB);
                    mo.Vertices.Add(new Vertex(pos, new Vector2((s + p.x) / steps, 1f)));
                }

                // 面: 波形に沿って左右をつなぐ
                for (int k = 0; k < m - 1; k++)
                {
                    int l0 = baseIdx + k;
                    int l1 = baseIdx + k + 1;
                    int r0 = baseIdx + m + k;
                    int r1 = baseIdx + m + k + 1;

                    if (flipWinding) mo.AddQuad(l0, l1, r1, r0);
                    else             mo.AddQuad(l0, r0, r1, l1);
                }
            }

            mo.RecalculateNormals();
            return mo;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// ステップの基準面法線。基準ベルトの巻き順 (a0, b0, b1, a1) / 反転時 (a0, a1, b1, b0) で算出する。
        /// </summary>
        private static Vector3 StepNormal(Vector3 a0, Vector3 b0, Vector3 b1, Vector3 a1, bool flipWinding)
        {
            return flipWinding
                ? NormalHelper.CalculateFaceNormal(a0, a1, b1)
                : NormalHelper.CalculateFaceNormal(a0, b0, b1);
        }
    }
}
