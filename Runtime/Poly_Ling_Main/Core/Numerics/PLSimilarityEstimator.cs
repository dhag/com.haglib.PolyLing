// Runtime/Poly_Ling_Main/Core/Numerics/PLSimilarityEstimator.cs
// 変形前後の対応点から相似変換（回転＋等方スケール＋平行移動）を推定する。
// Horn (1987) の単位四元数による閉形式解。
//
// PLAffineEstimator との使い分け:
//   アフィン推定は正規方程式 XT X（4x4）を解くため、対応点が同一平面上に
//   あると rank 3 に落ちて失敗する。相似変換は自由度が 7 しかなく、
//   同一平面でも（同一直線でなければ）一意に決まるので、
//   アフィンが失敗した縮退ケースの受け皿になる。
//
// 縮退の扱い:
//   同一直線上の点群では、直線まわりの回転が拘束されない。四元数の
//   最大固有値が重複し、返る固有ベクトルは無数の最適解のうちの 1 つに
//   なる。対応点自体は最小二乗の意味で合うが、直線から離れた点を
//   その変換で写すと結果が任意になるため、明示的に失敗させる。
//   判定には変形前の点群の共分散行列の固有値比を使う。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 変形前後の対応点から相似変換（回転＋等方スケール＋平行移動）を推定する。
    /// </summary>
    public static class PLSimilarityEstimator
    {
        /// <summary>推定に必要な最小対応点数。</summary>
        public const int MinimumPointCount = 3;

        /// <summary>
        /// 変形前の点群を同一直線とみなす固有値比のしきい値。
        /// 共分散の固有値は二乗距離の次元を持つため、1e-8 は
        /// 「直線方向の広がりに対する横方向の広がりが 1e-4」に相当する。
        /// </summary>
        public const double CollinearEigenRatio = 1.0e-8;

        /// <summary>
        /// before[i] を after[i] へ写す相似変換を最小二乗で推定する。
        /// </summary>
        /// <param name="before">変形前の対応点。3 点以上、かつ同一直線上でないこと。</param>
        /// <param name="after">変形後の対応点。before と同数。</param>
        /// <param name="similarity">推定された 4x4 行列。</param>
        /// <returns>推定できれば true。</returns>
        public static bool TryEstimate(
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            out Matrix4x4 similarity)
        {
            return TryEstimate(before, after, out similarity, out _, out _);
        }

        /// <summary>
        /// before[i] を after[i] へ写す相似変換を最小二乗で推定する。
        /// 回転とスケールを個別に取り出したい場合に使う。
        /// </summary>
        /// <param name="rotation">推定された回転。</param>
        /// <param name="scale">推定された等方スケール。必ず正。</param>
        public static bool TryEstimate(
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            out Matrix4x4 similarity,
            out Quaternion rotation,
            out float scale)
        {
            similarity = Matrix4x4.identity;
            rotation   = Quaternion.identity;
            scale      = 1f;

            if (before == null || after == null) return false;
            int n = before.Count;
            if (n != after.Count) return false;
            if (n < MinimumPointCount) return false;

            // ── 重心
            double cbx = 0.0, cby = 0.0, cbz = 0.0;
            double cax = 0.0, cay = 0.0, caz = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3 b = before[i];
                Vector3 a = after[i];
                cbx += b.x; cby += b.y; cbz += b.z;
                cax += a.x; cay += a.y; caz += a.z;
            }
            cbx /= n; cby /= n; cbz /= n;
            cax /= n; cay /= n; caz /= n;

            // ── 相互相関 S[a][b] = Σ x_a * y_b、変形前の 2 次モーメント、Σ|x|^2
            double sxx = 0.0, sxy = 0.0, sxz = 0.0;
            double syx = 0.0, syy = 0.0, syz = 0.0;
            double szx = 0.0, szy = 0.0, szz = 0.0;

            double mxx = 0.0, mxy = 0.0, mxz = 0.0;
            double myy = 0.0, myz = 0.0, mzz = 0.0;

            double sumXX = 0.0;

            for (int i = 0; i < n; i++)
            {
                Vector3 b = before[i];
                Vector3 a = after[i];

                double x0 = b.x - cbx, x1 = b.y - cby, x2 = b.z - cbz;
                double y0 = a.x - cax, y1 = a.y - cay, y2 = a.z - caz;

                sxx += x0 * y0; sxy += x0 * y1; sxz += x0 * y2;
                syx += x1 * y0; syy += x1 * y1; syz += x1 * y2;
                szx += x2 * y0; szy += x2 * y1; szz += x2 * y2;

                mxx += x0 * x0; mxy += x0 * x1; mxz += x0 * x2;
                myy += x1 * x1; myz += x1 * x2; mzz += x2 * x2;

                sumXX += x0 * x0 + x1 * x1 + x2 * x2;
            }

            if (!(sumXX > 0.0)) return false;   // 変形前の点がすべて同一位置

            // ── 同一直線の判定（変形前の点群の 2 次モーメントの固有値比）
            if (!IsRankAtLeastTwo(mxx, mxy, mxz, myy, myz, mzz)) return false;

            // ── Horn の 4x4 対称行列
            double[][] hornMatrix = PLMatrixD.Create(4, 4);

            hornMatrix[0][0] =  sxx + syy + szz;
            hornMatrix[0][1] =  syz - szy;
            hornMatrix[0][2] =  szx - sxz;
            hornMatrix[0][3] =  sxy - syx;

            hornMatrix[1][0] =  syz - szy;
            hornMatrix[1][1] =  sxx - syy - szz;
            hornMatrix[1][2] =  sxy + syx;
            hornMatrix[1][3] =  szx + sxz;

            hornMatrix[2][0] =  szx - sxz;
            hornMatrix[2][1] =  sxy + syx;
            hornMatrix[2][2] = -sxx + syy - szz;
            hornMatrix[2][3] =  syz + szy;

            hornMatrix[3][0] =  sxy - syx;
            hornMatrix[3][1] =  szx + sxz;
            hornMatrix[3][2] =  syz + szy;
            hornMatrix[3][3] = -sxx - syy + szz;

            // PLJacobiEigen の収束判定は非対角成分の絶対値に対する固定値なので、
            // 行列のスケールに左右されないよう最大要素で割ってから渡す。
            // 固有ベクトルはスケール不変。
            if (!NormalizeInPlace(hornMatrix)) return false;

            if (!PLJacobiEigen.TrySolveSymmetric(hornMatrix, out _, out double[][] vectors))
                return false;

            // eigenValues は降順なので vectors[0] が最大固有値に対応する
            double[] q = vectors[0];
            double qw = q[0], qx = q[1], qy = q[2], qz = q[3];

            double qLen = Math.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
            if (!(qLen > 0.0)) return false;
            qw /= qLen; qx /= qLen; qy /= qLen; qz /= qLen;

            // ── 四元数 → 回転行列
            double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
            double r01 =       2.0 * (qx * qy - qw * qz);
            double r02 =       2.0 * (qx * qz + qw * qy);
            double r10 =       2.0 * (qx * qy + qw * qz);
            double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
            double r12 =       2.0 * (qy * qz - qw * qx);
            double r20 =       2.0 * (qx * qz - qw * qy);
            double r21 =       2.0 * (qy * qz + qw * qx);
            double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

            // ── 等方スケール s = Σ(y・Rx) / Σ|x|^2
            //    Σ(y・Rx) は相互相関 S と回転行列の要素で書けるので、
            //    点をもう一度なめる必要はない。
            //    Σ_i y_i・(R x_i) = Σ_ab R[a][b] * (Σ_i x_i[b] * y_i[a]) = Σ_ab R[a][b] * S[b][a]
            double trace =
                r00 * sxx + r01 * syx + r02 * szx +
                r10 * sxy + r11 * syy + r12 * szy +
                r20 * sxz + r21 * syz + r22 * szz;

            double s = trace / sumXX;
            if (!(s > 0.0) || double.IsNaN(s) || double.IsInfinity(s)) return false;

            // ── 平行移動 t = ca - s * R * cb
            double tx = cax - s * (r00 * cbx + r01 * cby + r02 * cbz);
            double ty = cay - s * (r10 * cbx + r11 * cby + r12 * cbz);
            double tz = caz - s * (r20 * cbx + r21 * cby + r22 * cbz);

            Matrix4x4 m = Matrix4x4.identity;
            m[0, 0] = (float)(s * r00); m[0, 1] = (float)(s * r01); m[0, 2] = (float)(s * r02); m[0, 3] = (float)tx;
            m[1, 0] = (float)(s * r10); m[1, 1] = (float)(s * r11); m[1, 2] = (float)(s * r12); m[1, 3] = (float)ty;
            m[2, 0] = (float)(s * r20); m[2, 1] = (float)(s * r21); m[2, 2] = (float)(s * r22); m[2, 3] = (float)tz;
            m[3, 0] = 0f; m[3, 1] = 0f; m[3, 2] = 0f; m[3, 3] = 1f;

            similarity = m;
            rotation   = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
            scale      = (float)s;
            return true;
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 変形前の点群の 2 次モーメント（重心まわり）の階数が 2 以上かを見る。
        /// 第 2 固有値が第 1 固有値の CollinearEigenRatio 倍に満たなければ
        /// 同一直線（または同一点）とみなす。
        /// </summary>
        private static bool IsRankAtLeastTwo(
            double mxx, double mxy, double mxz, double myy, double myz, double mzz)
        {
            double[][] m = PLMatrixD.Create(3, 3);
            m[0][0] = mxx; m[0][1] = mxy; m[0][2] = mxz;
            m[1][0] = mxy; m[1][1] = myy; m[1][2] = myz;
            m[2][0] = mxz; m[2][1] = myz; m[2][2] = mzz;

            if (!NormalizeInPlace(m)) return false;
            if (!PLJacobiEigen.TrySolveSymmetric(m, out double[] values, out _)) return false;

            // 共分散は半正定値なので固有値は非負。降順に並んでいる。
            double first  = values[0];
            double second = values[1];
            if (!(first > 0.0)) return false;

            return second / first >= CollinearEigenRatio;
        }

        /// <summary>
        /// 行列を最大絶対値で割って正規化する。固有ベクトルは変わらない。
        /// 全要素が 0 の場合は false。
        /// </summary>
        private static bool NormalizeInPlace(double[][] m)
        {
            double max = 0.0;
            for (int i = 0; i < m.Length; i++)
            {
                double[] row = m[i];
                for (int j = 0; j < row.Length; j++)
                {
                    double v = Math.Abs(row[j]);
                    if (v > max) max = v;
                }
            }

            if (!(max > 0.0) || double.IsNaN(max) || double.IsInfinity(max)) return false;

            double inv = 1.0 / max;
            for (int i = 0; i < m.Length; i++)
            {
                double[] row = m[i];
                for (int j = 0; j < row.Length; j++) row[j] *= inv;
            }
            return true;
        }
    }
}
