// SkinWeightOperations.cs
// Flood/Normalize/Prune の実処理
// UnityEditor非依存 → Runtime/に移行可能

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class SkinWeightOperations
    {
        // ================================================================
        // 1 メッシュに対する実処理（Undo・同期は持たない）
        //
        // 対象メッシュは呼び出し側（PlayerCommandDispatcher）が
        // CollectTargetMeshContexts で列挙し、メッシュごとに
        // UndoController.SetMeshObject → before → Apply → after → 記録 を行う。
        // ここで Undo を扱うと 1 メッシュ分しか記録できない。
        // ================================================================

        /// <summary>指定 1 メッシュの選択頂点へ Flood を適用する。</summary>
        /// <returns>書き換えた頂点数</returns>
        public static int ApplyFloodToMesh(
            MeshContext meshCtx,
            int targetBoneMasterIndex, SkinWeightPaintMode paintMode,
            float weightValue, float brushStrength)
        {
            var mo = meshCtx?.MeshObject;
            if (mo == null || targetBoneMasterIndex < 0) return 0;
            if (paintMode == SkinWeightPaintMode.Smooth) return 0;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0) return 0;

            int count = 0;
            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                BoneWeight bw = vertex.BoneWeight ?? default;
                switch (paintMode)
                {
                    case SkinWeightPaintMode.Replace: bw = SkinWeightOps.SetBoneWeight(bw, targetBoneMasterIndex, weightValue); break;
                    case SkinWeightPaintMode.Add:     bw = SkinWeightOps.AddBoneWeight(bw, targetBoneMasterIndex, weightValue * brushStrength); break;
                    case SkinWeightPaintMode.Scale:   bw = SkinWeightOps.ScaleBoneWeight(bw, targetBoneMasterIndex, weightValue); break;
                }
                vertex.BoneWeight = SkinWeightOps.NormalizeBoneWeight(bw);
                count++;
            }

            // ウェイトを持たなかった頂点にも書き込めるため、ここは無 → 有の遷移点。
            // 描画オブジェクトの種別を確定させる。
            if (count > 0) mo.RecomputeSkinKind();

            return count;
        }

        /// <summary>指定 1 メッシュの選択頂点を正規化する。</summary>
        /// <returns>書き換えた頂点数</returns>
        public static int ApplyNormalizeToMesh(MeshContext meshCtx)
        {
            var mo = meshCtx?.MeshObject;
            if (mo == null) return 0;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0) return 0;

            int count = 0;
            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;
                vertex.BoneWeight = SkinWeightOps.NormalizeBoneWeight(vertex.BoneWeight.Value);
                count++;
            }
            return count;
        }

        /// <summary>指定 1 メッシュの選択頂点から微小ウェイトを除去する。</summary>
        /// <returns>書き換えた頂点数</returns>
        public static int ApplyPruneToMesh(MeshContext meshCtx, float pruneThreshold)
        {
            var mo = meshCtx?.MeshObject;
            if (mo == null) return 0;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0) return 0;

            int prunedCount = 0;
            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                var bw = vertex.BoneWeight.Value;
                bool changed = false;
                if (bw.weight0 > 0f && bw.weight0 < pruneThreshold) { bw.weight0 = 0f; bw.boneIndex0 = 0; changed = true; }
                if (bw.weight1 > 0f && bw.weight1 < pruneThreshold) { bw.weight1 = 0f; bw.boneIndex1 = 0; changed = true; }
                if (bw.weight2 > 0f && bw.weight2 < pruneThreshold) { bw.weight2 = 0f; bw.boneIndex2 = 0; changed = true; }
                if (bw.weight3 > 0f && bw.weight3 < pruneThreshold) { bw.weight3 = 0f; bw.boneIndex3 = 0; changed = true; }

                if (changed)
                {
                    bw = SkinWeightOps.NormalizeBoneWeight(bw);
                    bw = SkinWeightOps.SortBoneWeight(bw);
                    vertex.BoneWeight = bw;
                    prunedCount++;
                }
            }
            return prunedCount;
        }

        /// <summary>
        /// 指定 1 メッシュの選択頂点のボーンウェイトを、最大 4 組で直接上書きする。
        /// boneMasters が負値のスロットは (0, 0) で埋める。
        /// 入力値はそのまま書き込む（正規化はパネル側の操作）。
        ///
        /// Undo 記録と GPU 同期はここでは行わない。複数メッシュを対象にする場合、
        /// メッシュごとに UndoController の対象を差し替えて before/after を取る必要が
        /// あり、その制御は呼び出し側（PlayerCommandDispatcher）が持つ。
        /// </summary>
        /// <returns>書き換えた頂点数。0 なら何も変更していない。</returns>
        public static int ApplyNumericToMesh(
            MeshContext meshCtx, int[] boneMasters, float[] weights)
        {
            if (meshCtx?.MeshObject == null) return 0;
            if (boneMasters == null || weights == null) return 0;
            if (boneMasters.Length < 4 || weights.Length < 4) return 0;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0) return 0;

            // 有効スロットが 1 つも無い状態で適用すると全ウェイトが消えるため弾く。
            bool anySlot = false;
            for (int i = 0; i < 4; i++) if (boneMasters[i] >= 0) { anySlot = true; break; }
            if (!anySlot) return 0;

            var slots = new (int idx, float w)[4];
            for (int i = 0; i < 4; i++)
                slots[i] = boneMasters[i] >= 0
                    ? (boneMasters[i], Mathf.Clamp01(weights[i]))
                    : (0, 0f);

            var bw    = SkinWeightOps.SortBoneWeight(SkinWeightOps.Pack(slots));
            var mo    = meshCtx.MeshObject;
            int count = 0;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                mo.Vertices[vi].BoneWeight = bw;
                count++;
            }

            // 数値設定もウェイトを持たなかった頂点へ書き込むため、無 → 有の遷移点。
            if (count > 0) mo.RecomputeSkinKind();

            return count;
        }

        /// <summary>
        /// 選択中の描画メッシュ全件の選択頂点からボーンウェイトを読み出し、
        /// 全頂点で一致する (ボーン, ウェイト) の組だけを最大 4 スロット返す。
        /// 一致しないスロットは (-1, 0)。数値入力パネルの「現在値を取り込む」で使う。
        ///
        /// メッシュ境界は跨いで突き合わせる。適用側 ApplyNumericToMesh も
        /// 選択メッシュ全件へ同じ値を書くため、取得と適用の範囲を一致させる。
        /// </summary>
        /// <param name="tolerance">ウェイト一致とみなす誤差</param>
        /// <returns>長さ 4 の配列。対象が無いときは null。</returns>
        public static (int bone, float weight)[] GatherCommonBoneWeights(
            ModelContext model, float tolerance = 1e-4f,
            System.Action<string> onError = null)
        {
            if (model == null) return null;

            var targets = CollectTargetMeshContexts(model);
            if (targets.Count == 0) { onError?.Invoke("メッシュが選択されていません。"); return null; }

            // 基準となる共通集合。最初の有効頂点で作り、以降の頂点と突き合わせて削る。
            Dictionary<int, float> common = null;
            int scanned = 0;

            foreach (var meshCtx in targets)
            {
                var mo = meshCtx?.MeshObject;
                if (mo == null) continue;

                var selectedVerts = meshCtx.SelectedVertices;
                if (selectedVerts == null || selectedVerts.Count == 0) continue;

                foreach (int vi in selectedVerts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    scanned++;

                    var vertex = mo.Vertices[vi];
                    var map    = new Dictionary<int, float>();
                    if (vertex.HasBoneWeight)
                    {
                        var s = SkinWeightOps.Extract(vertex.BoneWeight.Value);
                        for (int i = 0; i < 4; i++)
                        {
                            if (s[i].w <= 0f) continue;
                            // 同一ボーンが複数スロットにある場合は合算して 1 組として扱う。
                            if (map.ContainsKey(s[i].idx)) map[s[i].idx] += s[i].w;
                            else                           map[s[i].idx]  = s[i].w;
                        }
                    }

                    if (common == null) { common = map; continue; }

                    var drop = new List<int>();
                    foreach (var kv in common)
                    {
                        if (!map.TryGetValue(kv.Key, out float w) || Mathf.Abs(w - kv.Value) > tolerance)
                            drop.Add(kv.Key);
                    }
                    foreach (int k in drop) common.Remove(k);
                    if (common.Count == 0) break;
                }

                if (common != null && common.Count == 0) break;
            }

            if (scanned == 0) { onError?.Invoke("頂点が選択されていません。"); return null; }

            var result = new (int bone, float weight)[4];
            for (int i = 0; i < 4; i++) result[i] = (-1, 0f);
            if (common == null) return result;

            // ウェイトの大きい順に最大 4 スロットへ詰める。
            var list = new List<KeyValuePair<int, float>>(common);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < list.Count && i < 4; i++)
                result[i] = (list[i].Key, list[i].Value);

            return result;
        }

        /// <summary>ウェイト合計の検査結果。</summary>
        public struct WeightSumReport
        {
            /// <summary>走査した頂点数（ウェイトを持つもののみ）。</summary>
            public int Checked;
            /// <summary>合計が 1 から tolerance 以上ずれていた頂点数。</summary>
            public int Broken;
            /// <summary>ウェイトを 1 つも持たない頂点数。</summary>
            public int NoWeight;
            /// <summary>壊れた頂点を含むオブジェクト名。</summary>
            public List<string> BrokenMeshNames;
            /// <summary>観測した合計の最小値・最大値（壊れた頂点のみ）。</summary>
            public float MinSum, MaxSum;
        }

        /// <summary>
        /// 対象メッシュ全頂点のウェイト合計を検査する。
        ///
        /// GPU スキニング (UnifiedCompute.compute:974-977) はボーン行列の加重和を
        /// そのまま使い正規化しない。合計が 1 でない頂点は原点方向へ寄って
        /// 見た目が崩れるため、崩れの原因箇所を特定できるようにする。
        /// </summary>
        public static WeightSumReport CheckWeightSums(ModelContext model, float tolerance = 0.001f)
        {
            var rep = new WeightSumReport
            {
                BrokenMeshNames = new List<string>(),
                MinSum = float.MaxValue,
                MaxSum = float.MinValue,
            };
            if (model == null) return rep;

            foreach (var meshCtx in CollectTargetMeshContexts(model))
            {
                var mo = meshCtx?.MeshObject;
                if (mo == null) continue;

                bool meshHasBroken = false;
                for (int vi = 0; vi < mo.VertexCount; vi++)
                {
                    var vertex = mo.Vertices[vi];
                    if (!vertex.HasBoneWeight) { rep.NoWeight++; continue; }

                    var bw = vertex.BoneWeight.Value;
                    float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                    rep.Checked++;

                    if (sum <= 0f) { rep.NoWeight++; }

                    if (Mathf.Abs(sum - 1f) > tolerance)
                    {
                        rep.Broken++;
                        meshHasBroken = true;
                        if (sum < rep.MinSum) rep.MinSum = sum;
                        if (sum > rep.MaxSum) rep.MaxSum = sum;
                    }
                }

                if (meshHasBroken)
                    rep.BrokenMeshNames.Add(string.IsNullOrEmpty(meshCtx.Name) ? "?" : meshCtx.Name);
            }

            if (rep.Broken == 0) { rep.MinSum = 0f; rep.MaxSum = 0f; }
            return rep;
        }

        /// <summary>
        /// 指定 1 メッシュの全頂点のウェイトを正規化する。
        /// 合計が 0 に近い頂点（ウェイト未設定を含む）は触らない。
        /// Undo 記録と GPU 同期は呼び出し側が行う。
        /// </summary>
        /// <returns>正規化した頂点数</returns>
        public static int NormalizeAllInMesh(MeshContext meshCtx, float tolerance = 0.001f)
        {
            var mo = meshCtx?.MeshObject;
            if (mo == null) return 0;

            int count = 0;
            for (int vi = 0; vi < mo.VertexCount; vi++)
            {
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                var bw    = vertex.BoneWeight.Value;
                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                if (sum < 0.0001f) continue;                 // 全部 0。正規化できない
                if (Mathf.Abs(sum - 1f) <= tolerance) continue;  // 既に正規化済み

                vertex.BoneWeight = SkinWeightOps.NormalizeBoneWeight(bw);
                count++;
            }
            return count;
        }

        /// <summary>
        /// 数値設定の対象メッシュ（選択中の描画メッシュ全件）。
        /// 1 件も選択されていなければ ActiveMeshContext へフォールバックする。
        /// PlayerCommandDispatcher.CollectSelectedMeshContexts と同じ規則。
        /// </summary>
        public static List<MeshContext> CollectTargetMeshContexts(ModelContext model)
        {
            var list = new List<MeshContext>();
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) list.Add(mc);
            }
            if (list.Count == 0)
            {
                var mc = model.ActiveMeshContext;
                if (mc?.MeshObject != null) list.Add(mc);
            }
            return list;
        }

    }
}
