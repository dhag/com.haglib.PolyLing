// Runtime/Poly_Ling_Main/UI/ThinPlateMorph/LocalThinPlateMorphSolver.cs
// ターゲット頂点ごとに独立に係数を求める局所 TPS の計算本体。
//
// 【スレッド】
// このファイルの計算部は UnityEngine の Object 派生・MeshContext・ModelContext に
// 一切触れない。入力はすべて配列で受け取り、出力も配列で返す。
// バックグラウンドスレッドから呼ぶことを前提とする
// （PLJobHandle.cs 冒頭の「ワーカーの制約」を参照）。
// メインスレッドでの入力の組み立ては ThinPlateMorphOperation.BuildLocalInput が行う。
//
// 【降格ラダー】
// 近傍で選ばれ、位置重複を除いた制御点数 n に対して上から順に試し、
// 最初に成功したものを採用する。
//
//   n >= 4  TPS          PLThinPlateSpline3D.Solve
//   n >= 4  アフィン      PLAffineEstimator.TryEstimate
//   n >= 3  相似          PLSimilarityEstimator.TryEstimate
//   n >= 1  重心平行移動   target + (重心After - 重心Before)
//   n == 0  不動
//
// TPS とアフィンは対応点が同一平面・同一直線のとき、どちらも LU のピボット判定で
// 失敗する。その受け皿が相似変換で、同一平面でも（同一直線でなければ）解ける。
// 同一直線は相似変換も明示的に失敗させるため、重心へ落ちる。
//
// 【位置重複の除去】
// ビフォー位置が完全一致する制御点が同じ近傍に入ると連立方程式が退化する。
// 近傍を選んだ直後、係数を解く直前に除去する。誘導部分グラフの構築より後に
// 行うことで、一致点がノードごと消えて経路が切れるのを避けている。
//
// 【結果の不連続】
// 隣り合うターゲット頂点で制御点集合が入れ替わる境界では写像が跳ぶ。
// 頂点ごとに独立に係数を求める以上避けられない帰結で、裂け目として現れる。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Jobs;
using Poly_Ling.Numerics;

namespace Poly_Ling.UI
{
    // ================================================================
    // 入力
    // ================================================================

    /// <summary>
    /// 局所 TPS の入力。メインスレッドで組み立て、ワーカースレッドへ渡す。
    /// 渡した後は内容を変更しないこと。
    /// </summary>
    public sealed class LocalMorphInput
    {
        /// <summary>制御点候補のワールド座標。位置重複は除去していない。</summary>
        public Vector3[] BeforeWorld;

        /// <summary>制御点候補の変形後ワールド座標。BeforeWorld と同数・同順。</summary>
        public Vector3[] AfterWorld;

        /// <summary>候補点だけの誘導部分グラフ（CSR 先頭索引）。辺が 1 本も無ければ null。</summary>
        public int[] AdjacencyStart;

        /// <summary>候補点だけの誘導部分グラフ（CSR 隣接列）。辺が 1 本も無ければ null。</summary>
        public int[] AdjacencyList;

        /// <summary>ターゲット頂点のワールド座標。</summary>
        public Vector3[] TargetWorld;

        /// <summary>ターゲットのワールド→ローカル変換。</summary>
        public Matrix4x4 TargetToLocal;

        /// <summary>候補点数。</summary>
        public int CandidateCount => BeforeWorld != null ? BeforeWorld.Length : 0;

        /// <summary>ターゲット頂点数。</summary>
        public int TargetCount => TargetWorld != null ? TargetWorld.Length : 0;

        /// <summary>リンク距離モードが使えるかどうか。</summary>
        public bool HasGraph => AdjacencyStart != null && AdjacencyList != null;
    }

    // ================================================================
    // 設定
    // ================================================================

    /// <summary>局所 TPS の設定。</summary>
    public struct LocalMorphOptions
    {
        /// <summary>選択モード。</summary>
        public ThinPlateLocalMode Mode;

        /// <summary>件数モードで選ぶ制御点数。</summary>
        public int NeighborCount;

        /// <summary>半径モードの距離しきい値。</summary>
        public float Radius;

        /// <summary>制御点数の上限。半径モードで効く。</summary>
        public int MaxControlPoints;

        /// <summary>平滑化係数。</summary>
        public float Lambda;
    }

    // ================================================================
    // 結果
    // ================================================================

    /// <summary>局所 TPS の結果。</summary>
    public sealed class LocalMorphResult
    {
        /// <summary>ターゲットのローカル座標に戻した変形結果。</summary>
        public Vector3[] LocalPositions;

        /// <summary>TPS で解けた頂点数。</summary>
        public int TpsCount;

        /// <summary>アフィンへ降格した頂点数。</summary>
        public int AffineCount;

        /// <summary>相似へ降格した頂点数。</summary>
        public int SimilarityCount;

        /// <summary>重心平行移動へ降格した頂点数。</summary>
        public int CentroidCount;

        /// <summary>制御点が 1 つも無く動かさなかった頂点数。</summary>
        public int UnchangedCount;

        /// <summary>実際に使われた制御点数の合計。平均を出すのに使う。</summary>
        public long TotalControlPoints;

        /// <summary>1 頂点あたりの平均制御点数。</summary>
        public double AverageControlPoints
        {
            get
            {
                int n = LocalPositions != null ? LocalPositions.Length : 0;
                return n > 0 ? TotalControlPoints / (double)n : 0.0;
            }
        }
    }

    // ================================================================
    // 計算本体
    // ================================================================

    /// <summary>ターゲット頂点ごとに独立に係数を求める局所 TPS。</summary>
    public static class LocalThinPlateMorphSolver
    {
        /// <summary>この頂点数ごとに進捗報告と中止判定を行う。</summary>
        public const int ProgressInterval = 256;

        /// <summary>
        /// 局所 TPS を解く。ワーカースレッドから呼ぶこと。
        /// </summary>
        /// <param name="input">メインスレッドで組み立てた入力。</param>
        /// <param name="options">設定。</param>
        /// <param name="jobContext">中止判定と進捗報告に使う。null 可（中止できなくなる）。</param>
        /// <returns>結果。中止された場合は PLJobCanceledException を投げる。</returns>
        public static LocalMorphResult Solve(
            LocalMorphInput input, LocalMorphOptions options, PLJobContext jobContext)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.BeforeWorld == null || input.AfterWorld == null || input.TargetWorld == null)
                throw new ArgumentException("入力の配列が揃っていません", nameof(input));
            if (input.BeforeWorld.Length != input.AfterWorld.Length)
                throw new ArgumentException("ビフォーとアフターの候補点数が一致しません", nameof(input));

            int targetCount = input.TargetWorld.Length;
            var result = new LocalMorphResult
            {
                LocalPositions = new Vector3[targetCount],
            };
            if (targetCount == 0) return result;

            var selector = new LocalControlPointSelector(
                input.BeforeWorld, input.AdjacencyStart, input.AdjacencyList);

            int cap = options.MaxControlPoints > 0 ? options.MaxControlPoints : 1;

            var neighbors = new List<int>(cap);
            var before    = new List<Vector3>(cap);
            var after     = new List<Vector3>(cap);
            var seen      = new HashSet<Vector3>();

            var tps = new PLThinPlateSpline3D();

            Matrix4x4 toLocal = input.TargetToLocal;

            for (int t = 0; t < targetCount; t++)
            {
                if (jobContext != null && (t % ProgressInterval) == 0)
                {
                    jobContext.ThrowIfCanceled();
                    jobContext.ReportStep(t, targetCount);
                }

                Vector3 world = input.TargetWorld[t];

                selector.Select(
                    world, options.Mode, options.NeighborCount,
                    options.Radius, options.MaxControlPoints, neighbors);

                // ── 位置重複の除去
                before.Clear();
                after.Clear();
                seen.Clear();
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int c = neighbors[i];
                    Vector3 bw = input.BeforeWorld[c];
                    if (!seen.Add(bw)) continue;
                    before.Add(bw);
                    after.Add(input.AfterWorld[c]);
                }

                int n = before.Count;
                result.TotalControlPoints += n;

                Vector3 warped = Warp(world, before, after, n, options.Lambda, tps, result);
                result.LocalPositions[t] = toLocal.MultiplyPoint3x4(warped);
            }

            if (jobContext != null)
            {
                jobContext.ThrowIfCanceled();
                jobContext.ReportStep(targetCount, targetCount);
            }

            return result;
        }

        /// <summary>降格ラダーを上から試して 1 点を変形する。</summary>
        private static Vector3 Warp(
            Vector3 world,
            List<Vector3> before, List<Vector3> after, int n,
            float lambda, PLThinPlateSpline3D tps, LocalMorphResult stats)
        {
            // ── TPS
            if (n >= PLThinPlateSpline3D.MinimumPointCount)
            {
                if (tps.Solve(before, after, lambda))
                {
                    stats.TpsCount++;
                    return tps.WarpPoint(world);
                }

                // ── アフィン
                if (PLAffineEstimator.TryEstimate(before, after, out Matrix4x4 affine))
                {
                    stats.AffineCount++;
                    return affine.MultiplyPoint3x4(world);
                }
            }

            // ── 相似
            if (n >= PLSimilarityEstimator.MinimumPointCount)
            {
                if (PLSimilarityEstimator.TryEstimate(before, after, out Matrix4x4 similarity))
                {
                    stats.SimilarityCount++;
                    return similarity.MultiplyPoint3x4(world);
                }
            }

            // ── 重心平行移動
            if (n >= 1)
            {
                stats.CentroidCount++;
                Vector3 cb = PLPointCloud.ComputeCentroid(before);
                Vector3 ca = PLPointCloud.ComputeCentroid(after);
                return world + (ca - cb);
            }

            // ── 不動
            stats.UnchangedCount++;
            return world;
        }
    }
}
