// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLJacobiEigen.cs
// 実対称行列の固有値・固有ベクトルをヤコビ法で求める。
// 移植元: NCSHAGLIB/FPX/Helper/Eigen/Eigen_A.cs :950 GetEigenVectorsByJacobi
//
// 移植元からの変更点:
//   - 独自の Vector / Matrix / SquareMatrix クラス群（移植元 :17〜:625）は移植せず double[][] を直接扱う
//   - 反復ごとに行列を 2 個 Clone していたのを in-place 更新に変更
//   - 収束失敗時に null を返していたのを bool 戻りへ変更
//   - 固有値の降順ソートを追加（移植元は未ソート）

using System;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 実対称行列に対するヤコビ法の固有値分解。
    /// </summary>
    public static class PLJacobiEigen
    {
        /// <summary>既定の最大反復回数。</summary>
        public const int DefaultMaxStep = 100000;

        /// <summary>既定の収束判定値（非対角成分の絶対値の最大）。</summary>
        public const double DefaultSettleValue = 1.0e-12;

        /// <summary>
        /// 実対称行列 matrix の固有値・固有ベクトルを求める。
        /// matrix は変更しない。対称性の検証は行わないため、呼び出し側が保証すること。
        /// </summary>
        /// <param name="eigenValues">固有値。降順にソートされる。</param>
        /// <param name="eigenVectors">固有ベクトル。eigenVectors[k] が eigenValues[k] に対応する長さ n の配列。正規化済み。</param>
        /// <returns>収束すれば true。</returns>
        public static bool TrySolveSymmetric(
            double[][] matrix,
            out double[] eigenValues,
            out double[][] eigenVectors,
            int maxStep = DefaultMaxStep,
            double settleValue = DefaultSettleValue)
        {
            eigenValues = null;
            eigenVectors = null;

            if (matrix == null) return false;
            int n = matrix.Length;
            if (n == 0 || matrix[0].Length != n) return false;

            double[][] a = PLMatrixD.Duplicate(matrix);
            double[][] x = PLMatrixD.Identity(n);

            bool converged = false;
            for (int step = 0; step < maxStep; step++)
            {
                // 非対角成分の絶対値最大を探す
                double maxAbs = 0.0;
                int p = 0, q = 1 % n;
                for (int i = 0; i < n; i++)
                {
                    double[] rowI = a[i];
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        double v = Math.Abs(rowI[j]);
                        if (v <= maxAbs) continue;
                        maxAbs = v;
                        p = i;
                        q = j;
                    }
                }

                // maxAbs == 0 は完全な対角行列。settleValue に 0 以下が渡されても
                // apq での除算に進まないよう、ここで打ち切る
                if (maxAbs < settleValue || maxAbs <= 0.0)
                {
                    converged = true;
                    break;
                }

                double app = a[p][p];
                double aqq = a[q][q];
                double apq = a[p][q];

                // 回転角。tan で求める（移植元の sqrt(0.5*(1-|t|/D)) 形は
                // 微小回転のとき 1 - |t|/D が桁落ちして精度が落ちるため置き換えている）
                double theta = (aqq - app) / (2.0 * apq);
                double tan;
                if (theta >= 0.0) tan = 1.0 / (theta + Math.Sqrt(theta * theta + 1.0));
                else tan = -1.0 / (-theta + Math.Sqrt(theta * theta + 1.0));

                double cs = 1.0 / Math.Sqrt(tan * tan + 1.0);
                double sn = tan * cs;

                // p 行 / q 行（および対称位置）の更新。p,q 以外の列のみ
                for (int j = 0; j < n; j++)
                {
                    if (j == p || j == q) continue;
                    double apj = a[p][j];
                    double aqj = a[q][j];
                    double newPj = apj * cs - aqj * sn;
                    double newQj = aqj * cs + apj * sn;
                    a[p][j] = newPj;
                    a[j][p] = newPj;
                    a[q][j] = newQj;
                    a[j][q] = newQj;
                }

                a[p][p] = app * cs * cs + aqq * sn * sn - 2.0 * apq * sn * cs;
                a[q][q] = app * sn * sn + aqq * cs * cs + 2.0 * apq * sn * cs;
                a[p][q] = 0.0;
                a[q][p] = 0.0;

                // 固有ベクトルの累積
                for (int i = 0; i < n; i++)
                {
                    double xip = x[i][p];
                    double xiq = x[i][q];
                    x[i][p] = xip * cs - xiq * sn;
                    x[i][q] = xiq * cs + xip * sn;
                }
            }

            if (!converged) return false;

            // 固有値は a の対角、固有ベクトルは x の列
            double[] values = new double[n];
            double[][] vectors = new double[n][];
            for (int k = 0; k < n; k++)
            {
                values[k] = a[k][k];

                double[] vec = new double[n];
                for (int j = 0; j < n; j++) vec[j] = x[j][k];
                Normalize(vec);
                vectors[k] = vec;
            }

            SortDescending(values, vectors);

            eigenValues = values;
            eigenVectors = vectors;
            return true;
        }

        // ================================================================
        // 内部処理
        // ================================================================

        private static void Normalize(double[] vector)
        {
            double sum = 0.0;
            for (int i = 0; i < vector.Length; i++) sum += vector[i] * vector[i];
            if (sum <= 0.0) return;

            double length = Math.Sqrt(sum);
            for (int i = 0; i < vector.Length; i++) vector[i] /= length;
        }

        /// <summary>固有値の降順に values と vectors を並べ替える（単純な選択ソート）。</summary>
        private static void SortDescending(double[] values, double[][] vectors)
        {
            int n = values.Length;
            for (int i = 0; i < n - 1; i++)
            {
                int maxIndex = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (values[j] > values[maxIndex]) maxIndex = j;
                }
                if (maxIndex == i) continue;

                double dv = values[i];
                values[i] = values[maxIndex];
                values[maxIndex] = dv;

                double[] pv = vectors[i];
                vectors[i] = vectors[maxIndex];
                vectors[maxIndex] = pv;
            }
        }
    }
}
