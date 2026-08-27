// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLAffineEstimator.cs
// 対応点から最小二乗（正規方程式・擬似逆行列）でアフィン変換係数を推定する。
// 移植元: NCSHAGLIB/FPX/Helper/EstimateAffine/EstimateAffineClass.cs :29 EstimateAffine
//
// 移植元からの変更点:
//   - ToMatrix の代入ミス（m.M14 = ary[0,1]。M24/M34/M44 も同様に列 3 ではなく列 1 を参照）を修正
//   - 座標系の変換: 移植元は System.Numerics（行ベクトル・平行移動は M41..M43）。
//     Unity は列ベクトル（平行移動は m03/m13/m23）のため転置して格納する。
//     結果は Matrix4x4.MultiplyPoint3x4 でそのまま使える。
//   - float 行列での逆行列計算を double での LU 求解に変更
//   - Gauss-Jordan 版 MatInverse は PLMatrixD.TryInverse と重複するため非移植

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 変形前後の対応点からアフィン変換行列を推定する。
    /// </summary>
    public static class PLAffineEstimator
    {
        /// <summary>推定に必要な最小対応点数。</summary>
        public const int MinimumPointCount = 4;

        /// <summary>
        /// before[i] を after[i] へ写す 4x4 アフィン変換行列を最小二乗で推定する。
        /// 対応点は 4 個以上必要で、かつ同一平面上に無いこと。
        /// </summary>
        /// <returns>正規方程式が解ければ true。</returns>
        public static bool TryEstimate(
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            out Matrix4x4 affine)
        {
            affine = Matrix4x4.identity;

            if (before == null || after == null) return false;
            int n = before.Count;
            if (n != after.Count) return false;
            if (n < MinimumPointCount) return false;

            // X は n 行 4 列、各行 (x, y, z, 1)
            // Y は n 行 3 列、各行 (x', y', z')
            // 正規方程式 (XT X) A = XT Y を解く。A は 4 行 3 列。

            double[][] normal = PLMatrixD.Create(4, 4);   // XT X
            double[][] rhs = PLMatrixD.Create(4, 3);      // XT Y
            double[] row = new double[4];

            for (int i = 0; i < n; i++)
            {
                Vector3 b = before[i];
                Vector3 a = after[i];

                row[0] = b.x;
                row[1] = b.y;
                row[2] = b.z;
                row[3] = 1.0;

                for (int r = 0; r < 4; r++)
                {
                    double rv = row[r];
                    if (rv == 0.0) continue;

                    double[] normalRow = normal[r];
                    for (int c = 0; c < 4; c++) normalRow[c] += rv * row[c];

                    double[] rhsRow = rhs[r];
                    rhsRow[0] += rv * a.x;
                    rhsRow[1] += rv * a.y;
                    rhsRow[2] += rv * a.z;
                }
            }

            if (!PLMatrixD.TryDecompose(normal, out double[][] lu, out int[] perm, out _)) return false;

            // 出力成分ごとに 3 回求解する
            double[] column = new double[4];
            double[][] coeff = new double[3][];   // coeff[d][k]
            for (int d = 0; d < 3; d++)
            {
                for (int k = 0; k < 4; k++) column[k] = rhs[k][d];
                coeff[d] = PLMatrixD.SolveWithLu(lu, perm, column);
            }

            // Unity は列ベクトル規約: y_d = Σ_k m[d, k] * x_k （x_3 = 1）
            Matrix4x4 m = Matrix4x4.identity;
            for (int d = 0; d < 3; d++)
            {
                double[] c = coeff[d];
                m[d, 0] = (float)c[0];
                m[d, 1] = (float)c[1];
                m[d, 2] = (float)c[2];
                m[d, 3] = (float)c[3];
            }
            m[3, 0] = 0f;
            m[3, 1] = 0f;
            m[3, 2] = 0f;
            m[3, 3] = 1f;

            affine = m;
            return true;
        }

        /// <summary>点列をまとめてアフィン変換する。</summary>
        public static List<Vector3> Transform(Matrix4x4 affine, IReadOnlyList<Vector3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            List<Vector3> result = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++) result.Add(affine.MultiplyPoint3x4(points[i]));
            return result;
        }

        /// <summary>点をアフィン変換する。</summary>
        public static Vector3 Transform(Matrix4x4 affine, Vector3 point)
        {
            return affine.MultiplyPoint3x4(point);
        }
    }
}
