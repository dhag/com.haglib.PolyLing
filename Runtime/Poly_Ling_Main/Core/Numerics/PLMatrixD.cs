// Packages/com.haglib.polyling/Runtime/Poly_Ling_Main/Core/Numerics/PLMatrixD.cs
// double[][] による密行列の基本演算（LU分解・逆行列・行列式・連立一次方程式）。
// 移植元: NCSHAGLIB/FPX/Helper/ThinplateSpline/MatrixDecompositionProgram.cs
//
// 移植元からの変更点:
//   - ピボット選択が絶対値を取っていなかった（移植元 :214 の `result[i][j] > colMax`）ため修正
//   - 失敗時に Exception を投げていたのを bool 戻りへ変更
//   - LU を再利用して複数の右辺を解く API（TryDecompose / SolveWithLu）を追加
//   - デモ用 Main / Console 出力 / MatrixRandom を非移植

using System;

namespace Poly_Ling.Numerics
{
    /// <summary>
    /// double[][]（ジャグ配列）で表現した密行列の静的ユーティリティ。
    /// 行列は m[row][col] で参照する。全行の長さが等しいことを前提とする。
    /// </summary>
    public static class PLMatrixD
    {
        /// <summary>LU分解でピボットの絶対値がこの値未満の場合、特異とみなす。</summary>
        public const double PivotEpsilon = 1.0e-20;

        /// <summary>
        /// LU分解でピボットの絶対値が「行列の最大要素 × この値」未満の場合も特異とみなす。
        /// 完全な特異だけでなく、丸め誤差で 0 にならなかった縮退も弾くための下限。
        /// 行列のスケールが偏っている場合は条件数の指標にはならない点に注意。
        /// </summary>
        public const double RelativePivotEpsilon = 1.0e-15;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>rows×cols のゼロ行列を生成する。</summary>
        public static double[][] Create(int rows, int cols)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));

            double[][] result = new double[rows][];
            for (int i = 0; i < rows; i++) result[i] = new double[cols];
            return result;
        }

        /// <summary>n×n の単位行列を生成する。</summary>
        public static double[][] Identity(int n)
        {
            double[][] result = Create(n, n);
            for (int i = 0; i < n; i++) result[i][i] = 1.0;
            return result;
        }

        /// <summary>行列の複製を返す。</summary>
        public static double[][] Duplicate(double[][] matrix)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));

            double[][] result = new double[matrix.Length][];
            for (int i = 0; i < matrix.Length; i++)
            {
                result[i] = new double[matrix[i].Length];
                Array.Copy(matrix[i], result[i], matrix[i].Length);
            }
            return result;
        }

        // ================================================================
        // 基本演算
        // ================================================================

        /// <summary>転置行列を返す。</summary>
        public static double[][] Transpose(double[][] matrix)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            int rows = matrix.Length;
            int cols = matrix[0].Length;

            double[][] result = Create(cols, rows);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++) result[j][i] = matrix[i][j];
            }
            return result;
        }

        /// <summary>行列積 a×b を返す。</summary>
        public static double[][] Product(double[][] a, double[][] b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int aRows = a.Length;
            int aCols = a[0].Length;
            int bRows = b.Length;
            int bCols = b[0].Length;
            if (aCols != bRows)
            {
                throw new ArgumentException(
                    $"次元が一致しません。a は {aRows}x{aCols}、b は {bRows}x{bCols}。");
            }

            double[][] result = Create(aRows, bCols);
            for (int i = 0; i < aRows; i++)
            {
                double[] aRow = a[i];
                double[] rRow = result[i];
                for (int k = 0; k < aCols; k++)
                {
                    double aik = aRow[k];
                    if (aik == 0.0) continue;
                    double[] bRow = b[k];
                    for (int j = 0; j < bCols; j++) rRow[j] += aik * bRow[j];
                }
            }
            return result;
        }

        /// <summary>行列とベクトルの積 matrix×vector を返す。</summary>
        public static double[] Multiply(double[][] matrix, double[] vector)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            if (vector == null) throw new ArgumentNullException(nameof(vector));

            int rows = matrix.Length;
            int cols = matrix[0].Length;
            if (cols != vector.Length)
            {
                throw new ArgumentException(
                    $"次元が一致しません。matrix は {rows}x{cols}、vector は {vector.Length}。");
            }

            double[] result = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                double sum = 0.0;
                double[] row = matrix[i];
                for (int j = 0; j < cols; j++) sum += row[j] * vector[j];
                result[i] = sum;
            }
            return result;
        }

        // ================================================================
        // LU分解（Doolittle・部分ピボット選択）
        // ================================================================

        /// <summary>
        /// 正方行列を LU 分解する。lu は L（対角は 1、格納されない）と U を合成した行列。
        /// perm は行の入れ替え情報、toggle は入れ替え回数の偶奇（+1 / -1）。
        /// </summary>
        /// <returns>特異でなければ true。</returns>
        public static bool TryDecompose(double[][] matrix, out double[][] lu, out int[] perm, out int toggle)
        {
            lu = null;
            perm = null;
            toggle = 1;

            if (matrix == null) return false;
            int n = matrix.Length;
            if (n == 0 || matrix[0].Length != n) return false;

            double[][] work = Duplicate(matrix);
            int[] p = new int[n];
            for (int i = 0; i < n; i++) p[i] = i;
            int tg = 1;

            // 許容ピボットの下限。行列のスケールに応じて決める
            double scale = 0.0;
            for (int i = 0; i < n; i++)
            {
                double[] row = work[i];
                for (int j = 0; j < n; j++)
                {
                    double v = Math.Abs(row[j]);
                    if (v > scale) scale = v;
                }
            }
            if (scale <= 0.0) return false;

            double pivotFloor = Math.Max(PivotEpsilon, scale * RelativePivotEpsilon);

            for (int j = 0; j < n - 1; j++)
            {
                // 絶対値最大の要素をピボットに選ぶ
                double colMax = Math.Abs(work[j][j]);
                int pivotRow = j;
                for (int i = j + 1; i < n; i++)
                {
                    double v = Math.Abs(work[i][j]);
                    if (v > colMax)
                    {
                        colMax = v;
                        pivotRow = i;
                    }
                }

                if (pivotRow != j)
                {
                    double[] rowPtr = work[pivotRow];
                    work[pivotRow] = work[j];
                    work[j] = rowPtr;

                    int tmp = p[pivotRow];
                    p[pivotRow] = p[j];
                    p[j] = tmp;

                    tg = -tg;
                }

                if (Math.Abs(work[j][j]) < pivotFloor) return false;

                double pivot = work[j][j];
                for (int i = j + 1; i < n; i++)
                {
                    work[i][j] /= pivot;
                    double factor = work[i][j];
                    if (factor == 0.0) continue;
                    for (int k = j + 1; k < n; k++) work[i][k] -= factor * work[j][k];
                }
            }

            if (Math.Abs(work[n - 1][n - 1]) < pivotFloor) return false;

            lu = work;
            perm = p;
            toggle = tg;
            return true;
        }

        /// <summary>
        /// TryDecompose の結果を使って A x = b を解く。LU を再利用したい場合に用いる。
        /// </summary>
        public static double[] SolveWithLu(double[][] lu, int[] perm, double[] b)
        {
            if (lu == null) throw new ArgumentNullException(nameof(lu));
            if (perm == null) throw new ArgumentNullException(nameof(perm));
            if (b == null) throw new ArgumentNullException(nameof(b));

            int n = lu.Length;
            if (b.Length != n)
            {
                throw new ArgumentException($"次元が一致しません。lu は {n}x{n}、b は {b.Length}。");
            }

            // 行の入れ替えを b に適用
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = b[perm[i]];

            // 前進代入（L は対角 1）
            for (int i = 1; i < n; i++)
            {
                double sum = x[i];
                double[] row = lu[i];
                for (int j = 0; j < i; j++) sum -= row[j] * x[j];
                x[i] = sum;
            }

            // 後退代入
            x[n - 1] /= lu[n - 1][n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                double sum = x[i];
                double[] row = lu[i];
                for (int j = i + 1; j < n; j++) sum -= row[j] * x[j];
                x[i] = sum / row[i];
            }

            return x;
        }

        // ================================================================
        // 逆行列 / 行列式 / 連立一次方程式
        // ================================================================

        /// <summary>A x = b を解く。</summary>
        /// <returns>A が特異でなければ true。</returns>
        public static bool TrySolve(double[][] a, double[] b, out double[] x)
        {
            x = null;
            if (!TryDecompose(a, out double[][] lu, out int[] perm, out _)) return false;
            if (b == null || b.Length != lu.Length) return false;

            x = SolveWithLu(lu, perm, b);
            return true;
        }

        /// <summary>逆行列を求める。</summary>
        /// <returns>特異でなければ true。</returns>
        public static bool TryInverse(double[][] matrix, out double[][] inverse)
        {
            inverse = null;
            if (!TryDecompose(matrix, out double[][] lu, out int[] perm, out _)) return false;

            int n = lu.Length;
            double[][] result = Create(n, n);
            double[] unit = new double[n];

            for (int col = 0; col < n; col++)
            {
                for (int i = 0; i < n; i++) unit[i] = (i == col) ? 1.0 : 0.0;
                double[] x = SolveWithLu(lu, perm, unit);
                for (int row = 0; row < n; row++) result[row][col] = x[row];
            }

            inverse = result;
            return true;
        }

        /// <summary>行列式を求める。</summary>
        /// <returns>特異でなければ true。特異の場合は false を返し determinant に 0 を設定する。</returns>
        public static bool TryDeterminant(double[][] matrix, out double determinant)
        {
            determinant = 0.0;
            if (!TryDecompose(matrix, out double[][] lu, out _, out int toggle)) return false;

            double result = toggle;
            for (int i = 0; i < lu.Length; i++) result *= lu[i][i];
            determinant = result;
            return true;
        }
    }
}
