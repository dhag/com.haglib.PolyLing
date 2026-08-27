// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLPointCloud.cs
// 点群の重心・分散共分散行列・主成分（PCA）、および平面・直線あてはめ。
// 移植元: NCSHAGLIB/FPX/Helper/Eigen/Eigen_A.cs
//         :1072 CalcurateCovarianceMatrix、:757 testEigenVectors（重心と各軸を返す）
//
// 移植元からの変更点:
//   - List<double[]> ではなく UnityEngine.Vector3 のリストを直接扱う
//   - 移植元 :810 付近の正規化が全ベクトルを eigenVectors[0] の長さで割っていた点を修正
//     （PLJacobiEigen 側で各ベクトルを正規化して返す）
//   - Console 出力・文字列整形を伴う testEigenVectors* は非移植

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 点群に対する統計処理（重心・共分散・主成分分析・平面/直線あてはめ）。
    /// </summary>
    public static class PLPointCloud
    {
        // ================================================================
        // 重心・共分散
        // ================================================================

        /// <summary>点群の重心を返す。</summary>
        public static Vector3 ComputeCentroid(IReadOnlyList<Vector3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            int n = points.Count;
            if (n == 0) return Vector3.zero;

            double sx = 0.0, sy = 0.0, sz = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i];
                sx += p.x;
                sy += p.y;
                sz += p.z;
            }
            return new Vector3((float)(sx / n), (float)(sy / n), (float)(sz / n));
        }

        /// <summary>
        /// 点群の 3x3 分散共分散行列を返す。移植元と同じく標本数 n で割る（母集団共分散）。
        /// </summary>
        public static double[][] ComputeCovariance(IReadOnlyList<Vector3> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            int n = points.Count;

            double[][] cov = PLMatrixD.Create(3, 3);
            if (n == 0) return cov;

            double mx = 0.0, my = 0.0, mz = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i];
                mx += p.x;
                my += p.y;
                mz += p.z;
            }
            mx /= n;
            my /= n;
            mz /= n;

            double xx = 0.0, xy = 0.0, xz = 0.0, yy = 0.0, yz = 0.0, zz = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i];
                double dx = p.x - mx;
                double dy = p.y - my;
                double dz = p.z - mz;
                xx += dx * dx;
                xy += dx * dy;
                xz += dx * dz;
                yy += dy * dy;
                yz += dy * dz;
                zz += dz * dz;
            }

            cov[0][0] = xx / n; cov[0][1] = xy / n; cov[0][2] = xz / n;
            cov[1][0] = xy / n; cov[1][1] = yy / n; cov[1][2] = yz / n;
            cov[2][0] = xz / n; cov[2][1] = yz / n; cov[2][2] = zz / n;
            return cov;
        }

        // ================================================================
        // 主成分分析
        // ================================================================

        /// <summary>
        /// 点群の主成分を求める。axes[0] が第1主成分（固有値最大）、axes[2] が第3主成分。
        /// axes は正規化済み。eigenValues は降順。
        /// </summary>
        /// <returns>固有値分解が収束すれば true。</returns>
        public static bool TryComputePrincipalAxes(
            IReadOnlyList<Vector3> points,
            out Vector3 centroid,
            out Vector3[] axes,
            out double[] eigenValues)
        {
            centroid = Vector3.zero;
            axes = null;
            eigenValues = null;

            if (points == null || points.Count < 2) return false;

            centroid = ComputeCentroid(points);
            double[][] cov = ComputeCovariance(points);

            if (!PLJacobiEigen.TrySolveSymmetric(cov, out double[] values, out double[][] vectors))
            {
                return false;
            }

            Vector3[] result = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                double[] v = vectors[i];
                result[i] = new Vector3((float)v[0], (float)v[1], (float)v[2]);
            }

            axes = result;
            eigenValues = values;
            return true;
        }

        // ================================================================
        // あてはめ
        // ================================================================

        /// <summary>
        /// 点群との二乗距離の合計を最小にする平面を求める。
        /// 平面は origin（重心）を通り normal を法線とする。normal は第3主成分。
        /// </summary>
        /// <returns>点が 3 個以上あり固有値分解が収束すれば true。</returns>
        public static bool TryFitPlane(IReadOnlyList<Vector3> points, out Vector3 origin, out Vector3 normal)
        {
            origin = Vector3.zero;
            normal = Vector3.zero;

            if (points == null || points.Count < 3) return false;
            if (!TryComputePrincipalAxes(points, out origin, out Vector3[] axes, out _)) return false;

            normal = axes[2];
            return normal.sqrMagnitude > 0f;
        }

        /// <summary>
        /// 点群との二乗距離の合計を最小にする直線を求める。
        /// 直線は origin（重心）を通り direction を方向とする。direction は第1主成分。
        /// </summary>
        /// <returns>点が 2 個以上あり固有値分解が収束すれば true。</returns>
        public static bool TryFitLine(IReadOnlyList<Vector3> points, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.zero;
            direction = Vector3.zero;

            if (points == null || points.Count < 2) return false;
            if (!TryComputePrincipalAxes(points, out origin, out Vector3[] axes, out _)) return false;

            direction = axes[0];
            return direction.sqrMagnitude > 0f;
        }
    }
}
