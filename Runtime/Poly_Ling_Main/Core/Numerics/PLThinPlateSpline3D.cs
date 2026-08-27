// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLThinPlateSpline3D.cs
// 3次元 Thin Plate Spline（薄板スプライン）による対応点ベースの変形。
// 移植元: NCSHAGLIB/FPX/Helper/ThinplateSpline/thinplate3D.cs
//
// 移植元からの変更点:
//   - K / P / L / V をすべて double で構築する（移植元は float で組み立て、
//     逆行列計算のときだけ double に変換して float へ戻していた）
//   - 逆行列を求めてから乗算していたのを LU 1 回 + 3 回の求解に変更
//   - float 由来のノイズを潰すための係数クランプ（|c| < 1e-6 → 0）は
//     double 化により不要となるため廃止
//   - Size クラス / write() / paint() / ファイナライザ / コメントアウト済みコードは非移植
//   - 対応点数が 4 未満のとき L が特異になるため、明示的に false を返す
//     （移植元の判定は 2 点以上で、3 点以下では逆行列計算が例外になっていた）
//
// カーネルは移植元と同じ U(p1, p2) = r2 * log(r2)（r2 は 2 点間の二乗距離）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 3次元 Thin Plate Spline。Solve で対応点から係数を求め、Warp で任意点を変形する。
    /// </summary>
    public sealed class PLThinPlateSpline3D
    {
        /// <summary>既定の平滑化係数。K 行列の対角に加算される。</summary>
        public const float DefaultLambda = 0.001f;

        /// <summary>係数の算出に必要な最小対応点数。</summary>
        public const int MinimumPointCount = 4;

        private const int Dimension = 3;

        private Vector3[] _kernelCenters;   // 距離カーネルの中心。通常は before と同じ
        private double[][] _coeff;          // (n + 4) 行 3 列
        private int _pointCount;

        /// <summary>係数が算出済みかどうか。</summary>
        public bool IsSolved => _coeff != null;

        /// <summary>対応点数。</summary>
        public int PointCount => _pointCount;

        // ================================================================
        // 係数算出
        // ================================================================

        /// <summary>
        /// 対応点から TPS 係数を求める。
        /// </summary>
        /// <param name="before">変形前の対応点。</param>
        /// <param name="after">変形後の対応点。before と同数。</param>
        /// <param name="lambda">平滑化係数。</param>
        /// <param name="beforeForDistance">
        /// 距離カーネルの中心を before とは別に与える場合に指定する。null なら before を使う。
        /// before と同数であること。
        /// </param>
        /// <returns>係数が求まれば true。</returns>
        public bool Solve(
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            float lambda = DefaultLambda,
            IReadOnlyList<Vector3> beforeForDistance = null)
        {
            _kernelCenters = null;
            _coeff = null;
            _pointCount = 0;

            if (before == null || after == null) return false;

            int n = before.Count;
            if (n < MinimumPointCount) return false;
            if (after.Count != n) return false;

            IReadOnlyList<Vector3> centers = beforeForDistance ?? before;
            if (centers.Count != n) return false;

            Vector3[] centerArray = new Vector3[n];
            for (int i = 0; i < n; i++) centerArray[i] = centers[i];

            int size = n + Dimension + 1;
            double[][] l = PLMatrixD.Create(size, size);

            // 左上 n×n: K 行列（対応点間の距離カーネル）。対角は lambda
            for (int i = 0; i < n; i++)
            {
                double[] rowI = l[i];
                for (int j = i + 1; j < n; j++)
                {
                    double u = Kernel(centerArray[i], centerArray[j]);
                    rowI[j] = u;
                    l[j][i] = u;
                }
                rowI[i] = lambda;
            }

            // 右上 n×4: P 行列（1, x, y, z）と 左下 4×n: P の転置
            for (int i = 0; i < n; i++)
            {
                Vector3 b = before[i];
                l[i][n] = 1.0;
                l[i][n + 1] = b.x;
                l[i][n + 2] = b.y;
                l[i][n + 3] = b.z;

                l[n][i] = 1.0;
                l[n + 1][i] = b.x;
                l[n + 2][i] = b.y;
                l[n + 3][i] = b.z;
            }
            // 右下 4×4 はゼロのまま

            if (!PLMatrixD.TryDecompose(l, out double[][] lu, out int[] perm, out _)) return false;

            // 成分ごとに L c = v を解く
            double[][] coeff = PLMatrixD.Create(size, Dimension);
            double[] rhs = new double[size];
            for (int d = 0; d < Dimension; d++)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = after[i];
                    rhs[i] = (d == 0) ? a.x : (d == 1) ? a.y : a.z;
                }
                for (int i = n; i < size; i++) rhs[i] = 0.0;

                double[] c = PLMatrixD.SolveWithLu(lu, perm, rhs);
                for (int i = 0; i < size; i++) coeff[i][d] = c[i];
            }

            _kernelCenters = centerArray;
            _coeff = coeff;
            _pointCount = n;
            return true;
        }

        // ================================================================
        // 変形
        // ================================================================

        /// <summary>1 点を変形する。</summary>
        public Vector3 WarpPoint(Vector3 target)
        {
            return WarpPoint(target, target);
        }

        /// <summary>
        /// 1 点を変形する。距離カーネルの評価位置をアフィン項の評価位置と別に与える版。
        /// </summary>
        public Vector3 WarpPoint(Vector3 target, Vector3 targetForDistance)
        {
            if (_coeff == null) throw new InvalidOperationException("Solve が未実行です。");

            int n = _pointCount;
            double bendX = 0.0, bendY = 0.0, bendZ = 0.0;
            for (int j = 0; j < n; j++)
            {
                double u = Kernel(_kernelCenters[j], targetForDistance);
                if (u == 0.0) continue;
                double[] c = _coeff[j];
                bendX += c[0] * u;
                bendY += c[1] * u;
                bendZ += c[2] * u;
            }

            double[] a1 = _coeff[n];
            double[] ax = _coeff[n + 1];
            double[] ay = _coeff[n + 2];
            double[] az = _coeff[n + 3];

            double tx = target.x, ty = target.y, tz = target.z;
            double x = a1[0] + ax[0] * tx + ay[0] * ty + az[0] * tz + bendX;
            double y = a1[1] + ax[1] * tx + ay[1] * ty + az[1] * tz + bendY;
            double z = a1[2] + ax[2] * tx + ay[2] * ty + az[2] * tz + bendZ;

            return new Vector3((float)x, (float)y, (float)z);
        }

        /// <summary>点列をまとめて変形する。</summary>
        /// <param name="targets">変形対象。</param>
        /// <param name="targetsForDistance">
        /// 距離カーネルの評価位置。null なら targets を使う。targets と同数であること。
        /// </param>
        public List<Vector3> Warp(IReadOnlyList<Vector3> targets, IReadOnlyList<Vector3> targetsForDistance = null)
        {
            if (_coeff == null) throw new InvalidOperationException("Solve が未実行です。");
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            IReadOnlyList<Vector3> distanceTargets = targetsForDistance ?? targets;
            if (distanceTargets.Count != targets.Count)
            {
                throw new ArgumentException("targetsForDistance の要素数が targets と一致しません。");
            }

            List<Vector3> result = new List<Vector3>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                result.Add(WarpPoint(targets[i], distanceTargets[i]));
            }
            return result;
        }

        // ================================================================
        // 静的ヘルパ
        // ================================================================

        /// <summary>
        /// before → after の対応から TPS を構築し、targets を変形して返す。
        /// </summary>
        /// <returns>係数が求まらない場合は null。</returns>
        public static List<Vector3> DoMorph(
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            IReadOnlyList<Vector3> targets,
            float lambda = DefaultLambda)
        {
            if (targets == null) return null;

            PLThinPlateSpline3D tps = new PLThinPlateSpline3D();
            if (!tps.Solve(before, after, lambda)) return null;
            return tps.Warp(targets);
        }

        /// <summary>
        /// 変形対象ごとに別々の対応点集合を使って変形する。
        /// beforeList[i] / afterList[i] が targets[i] に対応する。
        /// </summary>
        /// <param name="beforeForDistanceList">距離カーネルの中心。null なら beforeList を使う。</param>
        /// <param name="targetsForDistance">距離カーネルの評価位置。null なら targets を使う。</param>
        /// <remarks>
        /// 移植元は係数が求まらなかった要素に Vector3.zero を格納していたが、
        /// 変形しない（元の位置のまま）に変更している。
        /// </remarks>
        public static List<Vector3> DoMorph(
            IReadOnlyList<IReadOnlyList<Vector3>> beforeList,
            IReadOnlyList<IReadOnlyList<Vector3>> afterList,
            IReadOnlyList<Vector3> targets,
            float lambda = DefaultLambda,
            IReadOnlyList<IReadOnlyList<Vector3>> beforeForDistanceList = null,
            IReadOnlyList<Vector3> targetsForDistance = null)
        {
            if (beforeList == null || afterList == null || targets == null) return null;

            int count = targets.Count;
            if (beforeList.Count != count || afterList.Count != count) return null;

            IReadOnlyList<IReadOnlyList<Vector3>> distanceCenters = beforeForDistanceList ?? beforeList;
            if (distanceCenters.Count != count) return null;

            IReadOnlyList<Vector3> distanceTargets = targetsForDistance ?? targets;
            if (distanceTargets.Count != count) return null;

            List<Vector3> result = new List<Vector3>(count);
            PLThinPlateSpline3D tps = new PLThinPlateSpline3D();

            for (int i = 0; i < count; i++)
            {
                if (tps.Solve(beforeList[i], afterList[i], lambda, distanceCenters[i]))
                {
                    result.Add(tps.WarpPoint(targets[i], distanceTargets[i]));
                }
                else
                {
                    result.Add(targets[i]);
                }
            }
            return result;
        }

        // ================================================================
        // カーネル
        // ================================================================

        /// <summary>U(r2) = r2 * log(r2)。r2 は 2 点間の二乗距離。</summary>
        private static double Kernel(Vector3 p1, Vector3 p2)
        {
            double dx = (double)p1.x - p2.x;
            double dy = (double)p1.y - p2.y;
            double dz = (double)p1.z - p2.z;
            double r2 = dx * dx + dy * dy + dz * dz;
            if (r2 <= 0.0) return 0.0;
            return r2 * Math.Log(r2);
        }
    }
}
