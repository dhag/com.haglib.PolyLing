// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLThinPlateSpline2D.cs
// 2次元 Thin Plate Spline（薄板スプライン）。および 3D 点群を平面へ射影して変形するヘルパ。
// 移植元: NCSHAGLIB/FPX/Helper/ThinplateSpline/thinplate2D.cs
//
// 移植元からの変更点:
//   - K / P / L / V をすべて double で構築する（移植元は float で組み立て、
//     逆行列計算のときだけ double に変換して float へ戻していた）
//   - 逆行列を求めてから乗算していたのを LU 1 回 + 2 回の求解に変更
//   - float 由来のノイズを潰すための係数クランプ（|c| < 1e-6 → 0）は廃止
//   - FORWARD_WARP / BACK_WARP のフラグ機構を非移植。
//     移植元 doMorph_ は常に BACK_WARP で呼ばれ、その場合
//     K/P は before、V は after から作られる（3D 版と同じ挙動）ため、
//     before → after の一方向として整理した
//   - Point2D / Size / CV_ResizeAlgorithm / computeMaps(Size) / Console 出力は非移植。
//     座標は UnityEngine.Vector2 を使う
//   - 平面射影版で無視した軸に 0 を入れていたのを、変形対象の元の値を保持するよう変更
//   - 対応点数が 3 未満のとき L が特異になるため、明示的に false を返す
//
// カーネルは移植元と同じ U(p1, p2) = r2 * log(r2)（r2 は 2 点間の二乗距離）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// 3D 点群を 2D TPS で変形する際の射影平面。値は移植元の mode 引数と同じ。
    /// </summary>
    public enum PLTpsPlane
    {
        /// <summary>Z を無視する（正面）。</summary>
        XY = 0,

        /// <summary>Y を無視する（上から）。</summary>
        XZ = 1,

        /// <summary>X を無視する（横から）。</summary>
        YZ = 2,
    }

    /// <summary>
    /// 2次元 Thin Plate Spline。Solve で対応点から係数を求め、Warp で任意点を変形する。
    /// </summary>
    public sealed class PLThinPlateSpline2D
    {
        /// <summary>既定の平滑化係数。K 行列の対角に加算される。</summary>
        public const float DefaultLambda = 0.001f;

        /// <summary>係数の算出に必要な最小対応点数。</summary>
        public const int MinimumPointCount = 3;

        private const int Dimension = 2;

        private Vector2[] _kernelCenters;
        private double[][] _coeff;          // (n + 3) 行 2 列
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
        /// <returns>係数が求まれば true。</returns>
        public bool Solve(
            IReadOnlyList<Vector2> before,
            IReadOnlyList<Vector2> after,
            float lambda = DefaultLambda)
        {
            _kernelCenters = null;
            _coeff = null;
            _pointCount = 0;

            if (before == null || after == null) return false;

            int n = before.Count;
            if (n < MinimumPointCount) return false;
            if (after.Count != n) return false;

            Vector2[] centerArray = new Vector2[n];
            for (int i = 0; i < n; i++) centerArray[i] = before[i];

            int size = n + Dimension + 1;
            double[][] l = PLMatrixD.Create(size, size);

            // 左上 n×n: K 行列。対角は lambda
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

            // 右上 n×3: P 行列（1, x, y）と 左下 3×n: P の転置
            for (int i = 0; i < n; i++)
            {
                Vector2 b = before[i];
                l[i][n] = 1.0;
                l[i][n + 1] = b.x;
                l[i][n + 2] = b.y;

                l[n][i] = 1.0;
                l[n + 1][i] = b.x;
                l[n + 2][i] = b.y;
            }
            // 右下 3×3 はゼロのまま

            if (!PLMatrixD.TryDecompose(l, out double[][] lu, out int[] perm, out _)) return false;

            double[][] coeff = PLMatrixD.Create(size, Dimension);
            double[] rhs = new double[size];
            for (int d = 0; d < Dimension; d++)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = after[i];
                    rhs[i] = (d == 0) ? a.x : a.y;
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
        public Vector2 WarpPoint(Vector2 target)
        {
            if (_coeff == null) throw new InvalidOperationException("Solve が未実行です。");

            int n = _pointCount;
            double bendX = 0.0, bendY = 0.0;
            for (int j = 0; j < n; j++)
            {
                double u = Kernel(_kernelCenters[j], target);
                if (u == 0.0) continue;
                double[] c = _coeff[j];
                bendX += c[0] * u;
                bendY += c[1] * u;
            }

            double[] a1 = _coeff[n];
            double[] ax = _coeff[n + 1];
            double[] ay = _coeff[n + 2];

            double tx = target.x, ty = target.y;
            double x = a1[0] + ax[0] * tx + ay[0] * ty + bendX;
            double y = a1[1] + ax[1] * tx + ay[1] * ty + bendY;

            return new Vector2((float)x, (float)y);
        }

        /// <summary>点列をまとめて変形する。</summary>
        public List<Vector2> Warp(IReadOnlyList<Vector2> targets)
        {
            if (_coeff == null) throw new InvalidOperationException("Solve が未実行です。");
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            List<Vector2> result = new List<Vector2>(targets.Count);
            for (int i = 0; i < targets.Count; i++) result.Add(WarpPoint(targets[i]));
            return result;
        }

        // ================================================================
        // 静的ヘルパ（2D）
        // ================================================================

        /// <summary>
        /// before → after の対応から 2D TPS を構築し、targets を変形して返す。
        /// </summary>
        /// <returns>係数が求まらない場合は null。</returns>
        public static List<Vector2> DoMorph(
            IReadOnlyList<Vector2> before,
            IReadOnlyList<Vector2> after,
            IReadOnlyList<Vector2> targets,
            float lambda = DefaultLambda)
        {
            if (targets == null) return null;

            PLThinPlateSpline2D tps = new PLThinPlateSpline2D();
            if (!tps.Solve(before, after, lambda)) return null;
            return tps.Warp(targets);
        }

        // ================================================================
        // 静的ヘルパ（3D 点群を平面へ射影）
        // ================================================================

        /// <summary>
        /// 3D 点群を plane で指定した平面へ射影して 2D TPS で変形する。
        /// 射影で無視した軸は、変形対象の元の値をそのまま保持する。
        /// </summary>
        /// <returns>係数が求まらない場合は null。</returns>
        public static List<Vector3> DoMorph(
            PLTpsPlane plane,
            IReadOnlyList<Vector3> before,
            IReadOnlyList<Vector3> after,
            IReadOnlyList<Vector3> targets,
            float lambda = DefaultLambda)
        {
            if (before == null || after == null || targets == null) return null;

            List<Vector2> before2D = Project(plane, before);
            List<Vector2> after2D = Project(plane, after);
            List<Vector2> targets2D = Project(plane, targets);

            PLThinPlateSpline2D tps = new PLThinPlateSpline2D();
            if (!tps.Solve(before2D, after2D, lambda)) return null;

            List<Vector3> result = new List<Vector3>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                Vector2 warped = tps.WarpPoint(targets2D[i]);
                result.Add(Unproject(plane, warped, targets[i]));
            }
            return result;
        }

        /// <summary>3D 点を plane へ射影して 2D 座標にする。</summary>
        public static Vector2 Project(PLTpsPlane plane, Vector3 point)
        {
            switch (plane)
            {
                case PLTpsPlane.XZ: return new Vector2(point.x, point.z);
                case PLTpsPlane.YZ: return new Vector2(point.y, point.z);
                default: return new Vector2(point.x, point.y);
            }
        }

        /// <summary>
        /// 2D 座標を 3D に戻す。射影で無視した軸は original の値を使う。
        /// </summary>
        public static Vector3 Unproject(PLTpsPlane plane, Vector2 point, Vector3 original)
        {
            switch (plane)
            {
                case PLTpsPlane.XZ: return new Vector3(point.x, original.y, point.y);
                case PLTpsPlane.YZ: return new Vector3(original.x, point.x, point.y);
                default: return new Vector3(point.x, point.y, original.z);
            }
        }

        private static List<Vector2> Project(PLTpsPlane plane, IReadOnlyList<Vector3> points)
        {
            List<Vector2> result = new List<Vector2>(points.Count);
            for (int i = 0; i < points.Count; i++) result.Add(Project(plane, points[i]));
            return result;
        }

        // ================================================================
        // カーネル
        // ================================================================

        /// <summary>U(r2) = r2 * log(r2)。r2 は 2 点間の二乗距離。</summary>
        private static double Kernel(Vector2 p1, Vector2 p2)
        {
            double dx = (double)p1.x - p2.x;
            double dy = (double)p1.y - p2.y;
            double r2 = dx * dx + dy * dy;
            if (r2 <= 0.0) return 0.0;
            return r2 * Math.Log(r2);
        }
    }
}
