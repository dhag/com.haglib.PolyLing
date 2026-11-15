// ShapeSymmetryOps.cs
// 同じメッシュの中の左右対称の頂点の対応と、ミラー軸の決め方。
// 形の左右反転（ShapeOps.MirrorShape。PolyLing_モーフ整理計画.md 3.7）が使う。
// 段 7 で旧モーフの鏡像の導出（MorphMirrorOps）を撤去したとき、そこから残る部分だけを移した。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Ops
{
    public static class ShapeSymmetryOps
    {
        /// <summary>対称頂点の対応を取るときの既定のしきい値</summary>
        public const float DefaultSymmetryThreshold = 1e-4f;

        /// <summary>
        /// 位置の並び（頂点番号順）から、頂点 i の左右対称位置にある頂点番号を実測で求める。
        /// 対称面上の頂点は自分自身を指す。相手が見つからない頂点は -1。
        /// </summary>
        /// <param name="mirrorAxis">ミラー軸（1:X, 2:Y, 4:Z。MirrorBranchOps と同じ体系）</param>
        /// <param name="mirrorDistance">ミラー平面の位置</param>
        /// <param name="threshold">同一位置とみなす距離</param>
        public static int[] BuildSymmetryVertexMap(
            Vector3[] positions, int mirrorAxis, float mirrorDistance,
            float threshold = DefaultSymmetryThreshold)
        {
            if (positions == null || positions.Length == 0) return null;

            int n = positions.Length;
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = -1;

            // 位置を格子に落として候補を絞る（全探索 O(n^2) を避ける）
            float cell = Mathf.Max(threshold, 1e-6f) * 2f;
            var grid = new Dictionary<(int, int, int), List<int>>(n);

            for (int i = 0; i < n; i++)
            {
                var key = CellKey(positions[i], cell);
                if (!grid.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(4);
                    grid[key] = bucket;
                }
                bucket.Add(i);
            }

            float thresholdSq = threshold * threshold;

            for (int i = 0; i < n; i++)
            {
                Vector3 mirrored = MirrorBranchOps.MirrorPoint(mirrorAxis, mirrorDistance, positions[i]);

                int best = -1;
                float bestSq = thresholdSq;

                var baseKey = CellKey(mirrored, cell);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = (baseKey.Item1 + dx, baseKey.Item2 + dy, baseKey.Item3 + dz);
                    if (!grid.TryGetValue(key, out var bucket)) continue;

                    for (int b = 0; b < bucket.Count; b++)
                    {
                        int j = bucket[b];
                        float sq = (positions[j] - mirrored).sqrMagnitude;
                        if (sq <= bestSq)
                        {
                            bestSq = sq;
                            best = j;
                        }
                    }
                }

                map[i] = best;
            }

            return map;
        }

        private static (int, int, int) CellKey(Vector3 p, float cell)
        {
            return (Mathf.FloorToInt(p.x / cell),
                    Mathf.FloorToInt(p.y / cell),
                    Mathf.FloorToInt(p.z / cell));
        }

        /// <summary>
        /// ミラー軸と平面位置を決める。
        /// メッシュ自身の設定（MirrorAxis / MirrorDistance）を優先し、
        /// 無い場合はモデルの対称設定にフォールバックする。
        /// </summary>
        public static void ResolveAxis(
            ModelContext model, MeshContext meshCtx, out int mirrorAxis, out float mirrorDistance)
        {
            if (meshCtx != null && (meshCtx.MirrorAxis == 1 || meshCtx.MirrorAxis == 2 || meshCtx.MirrorAxis == 4))
            {
                mirrorAxis = meshCtx.MirrorAxis;
                mirrorDistance = meshCtx.MirrorDistance;
                return;
            }

            var settings = model?.SymmetrySettings;
            if (settings != null)
            {
                mirrorAxis = settings.Axis switch
                {
                    SymmetryAxis.Y => 2,
                    SymmetryAxis.Z => 4,
                    _ => 1
                };
                mirrorDistance = settings.PlaneOffset;
                return;
            }

            mirrorAxis = 1;
            mirrorDistance = 0f;
        }
    }
}
