// MorphMirrorOps.cs
// ============================================================
// 鏡像モーフ（MorphMirrorPolicy.MirrorOf）の導出
// ============================================================
//
// 【規約】
//   モーフとミラーの関係についての規約は MorphMirrorPolicy.cs 冒頭のコメントを
//   正典とする。ここには規約そのものを書き写さない。
//
// 【役割】
//   MorphMirrorPolicy.MirrorOf のモーフは差分を保持せず、参照先モーフ
//   （MeshContext.MirrorOfMorphIndex）の差分を左右反転して自身へ適用する。
//   「ウインク左」を作れば「ウインク右」が差分ゼロで得られる、という関係を成立させる。
//
// 【左右対応の解決】
//   参照元と参照先が同じ親メッシュに属する場合
//     … 1つのメッシュの中に左右の頂点が両方ある状態。対称頂点マップを実測で構築する。
//   親どうしが MirrorPair の場合
//     … MirrorPair.VertexMap（実体側頂点 index → ミラー側頂点 index）を使う。
//
//   親が①表示ミラー（MirrorType > 0）のときは成立しない。①には頂点実体が半身しかなく、
//   反対側は描画時に写した像でしかないため、片側だけを動かす差分を書き込む先が無い
//   （正典コメントの 2. を参照）。本 Ops は該当時に false を返して何もしない。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Ops
{
    public static class MorphMirrorOps
    {
        /// <summary>対称頂点マップ構築の既定しきい値</summary>
        public const float DefaultSymmetryThreshold = 1e-4f;

        // ================================================================
        // 鏡像モーフの導出
        // ================================================================

        /// <summary>
        /// モデル内の MirrorOf モーフをすべて導出し直す。
        /// </summary>
        /// <returns>導出できたモーフの数</returns>
        public static int ResolveAllMirrorOfMorphs(
            ModelContext model, float threshold = DefaultSymmetryThreshold)
        {
            if (model?.MeshContextList == null) return 0;

            int resolved = 0;
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var mc = model.MeshContextList[i];
                if (mc == null || mc.MorphMirrorPolicy != MorphMirrorPolicy.MirrorOf) continue;

                if (ResolveMirrorOf(model, mc, threshold)) resolved++;
            }
            return resolved;
        }

        /// <summary>
        /// 1つの MirrorOf モーフを、参照先モーフの差分の左右反転として作り直す。
        /// </summary>
        /// <returns>導出できたら true</returns>
        public static bool ResolveMirrorOf(
            ModelContext model, MeshContext morphCtx, float threshold = DefaultSymmetryThreshold)
        {
            if (model == null || morphCtx == null) return false;
            if (morphCtx.MorphMirrorPolicy != MorphMirrorPolicy.MirrorOf) return false;

            var dstMo   = morphCtx.MeshObject;
            var dstBase = morphCtx.MorphBaseData;
            if (dstMo == null || dstBase == null || !dstBase.IsValid) return false;

            int srcIdx = morphCtx.MirrorOfMorphIndex;
            if (srcIdx < 0 || srcIdx >= model.Count) return false;

            var srcCtx = model.GetMeshContext(srcIdx);
            if (srcCtx == null || !srcCtx.IsMorph) return false;

            var srcMo   = srcCtx.MeshObject;
            var srcBase = srcCtx.MorphBaseData;
            if (srcMo == null || srcBase == null || !srcBase.IsValid) return false;

            // 自分自身を参照していたら導出できない（無限の自己参照）
            if (ReferenceEquals(srcCtx, morphCtx)) return false;

            // 親を引く
            int dstParentIdx = morphCtx.MorphParentIndex;
            int srcParentIdx = srcCtx.MorphParentIndex;
            if (dstParentIdx < 0 || dstParentIdx >= model.Count) return false;
            if (srcParentIdx < 0 || srcParentIdx >= model.Count) return false;

            var dstParent = model.GetMeshContext(dstParentIdx);
            var srcParent = model.GetMeshContext(srcParentIdx);
            if (dstParent == null || srcParent == null) return false;

            // ①表示ミラーでは片側だけの差分を書き込む先が無い（正典コメント 2.）
            if (dstParent.MirrorType > 0 || srcParent.MirrorType > 0) return false;

            // 左右対応 dstIndex → srcIndex を決める
            int[] dstToSrc;
            int mirrorAxis;
            float mirrorDistance;

            if (dstParentIdx == srcParentIdx)
            {
                // 同一メッシュ内に左右の頂点が両方ある。実測で対称頂点マップを作る。
                ResolveAxis(model, dstParent, out mirrorAxis, out mirrorDistance);
                dstToSrc = BuildSymmetryVertexMap(dstMo, mirrorAxis, mirrorDistance, threshold);
            }
            else
            {
                // 親どうしが MirrorPair。VertexMap は 実体側 → ミラー側 の向き。
                var pair = model.GetMirrorPair(dstParent);
                if (pair == null || !pair.IsValid) return false;
                if (pair.VertexMap == null) return false;

                ResolveAxis(model, pair.Real, out mirrorAxis, out mirrorDistance);

                if (ReferenceEquals(pair.Real, srcParent) && ReferenceEquals(pair.Mirror, dstParent))
                {
                    // dst がミラー側。VertexMap[realIndex] = mirrorIndex を逆に引く。
                    dstToSrc = InvertMap(pair.VertexMap, dstMo.VertexCount);
                }
                else if (ReferenceEquals(pair.Real, dstParent) && ReferenceEquals(pair.Mirror, srcParent))
                {
                    // dst が実体側。VertexMap をそのまま使える。
                    dstToSrc = pair.VertexMap;
                }
                else
                {
                    return false;
                }
            }

            if (dstToSrc == null) return false;

            // 参照先の差分を左右反転して自分の基準位置へ加算する
            int count = Mathf.Min(dstMo.VertexCount, dstBase.VertexCount);
            int applied = 0;

            for (int d = 0; d < count; d++)
            {
                int sIdx = (d < dstToSrc.Length) ? dstToSrc[d] : -1;
                if (sIdx < 0 || sIdx >= srcMo.VertexCount) continue;
                if (sIdx >= srcBase.VertexCount) continue;

                Vector3 srcOffset = srcMo.Vertices[sIdx].Position - srcBase.BasePositions[sIdx];
                Vector3 dstOffset = MirrorBranchOps.MirrorNormal(mirrorAxis, srcOffset);

                var v = dstMo.Vertices[d];
                v.Position = dstBase.BasePositions[d] + dstOffset;
                applied++;
            }

            if (applied == 0) return false;

            dstMo.InvalidatePositionCache();
            morphCtx.ApplyVertexPositionsToMesh();
            return true;
        }

        // ================================================================
        // 対称頂点マップ
        // ================================================================

        /// <summary>
        /// 1つの MeshObject の中で、頂点 i の左右対称位置にある頂点 index を実測で求める。
        ///
        /// 対称面上の頂点は自分自身を指す。相手が見つからない頂点は -1 になる。
        /// 位置一致の探索なので、左右で頂点数や配置が対称でないメッシュでは
        /// -1 が並ぶ（呼び出し側はそれを許容して部分適用する）。
        /// </summary>
        /// <param name="mirrorAxis">ミラー軸（1:X, 2:Y, 4:Z。MirrorBranchOps と同じ体系）</param>
        /// <param name="mirrorDistance">ミラー平面の位置</param>
        /// <param name="threshold">同一位置とみなす距離</param>
        public static int[] BuildSymmetryVertexMap(
            MeshObject mo, int mirrorAxis, float mirrorDistance,
            float threshold = DefaultSymmetryThreshold)
        {
            if (mo == null || mo.VertexCount == 0) return null;

            int n = mo.VertexCount;
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = -1;

            // 位置を格子に落として候補を絞る（全探索 O(n^2) を避ける）
            float cell = Mathf.Max(threshold, 1e-6f) * 2f;
            var grid = new Dictionary<(int, int, int), List<int>>(n);

            for (int i = 0; i < n; i++)
            {
                var key = CellKey(mo.Vertices[i].Position, cell);
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
                Vector3 mirrored = MirrorBranchOps.MirrorPoint(mirrorAxis, mirrorDistance, mo.Vertices[i].Position);

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
                        float sq = (mo.Vertices[j].Position - mirrored).sqrMagnitude;
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

        /// <summary>src[i] = j の対応を、j → i の向きへ引き直す。</summary>
        private static int[] InvertMap(int[] src, int dstLength)
        {
            if (src == null || dstLength <= 0) return null;

            var inv = new int[dstLength];
            for (int i = 0; i < dstLength; i++) inv[i] = -1;

            for (int i = 0; i < src.Length; i++)
            {
                int j = src[i];
                if (j < 0 || j >= dstLength) continue;
                inv[j] = i;
            }
            return inv;
        }

        /// <summary>
        /// ミラー軸と平面位置を決める。
        /// メッシュ自身の設定（MirrorAxis / MirrorDistance）を優先し、
        /// 無い場合はモデルの対称設定にフォールバックする。
        /// </summary>
        private static void ResolveAxis(
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
