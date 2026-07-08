// Assets/Editor/Poly_Ling/Core/Ops/IKChainResolver.cs
// ============================================================
// IKチェーン導出／同期ユーティリティ
// ============================================================
//
// 【役割】
//   IK の2表現を相互変換する純ロジック（データを持たない）。
//     (1) 集約表現   : IKルートの IKData.Links（順序付き）＋ TargetIndex
//     (2) per-bone 表現: IKルートの IKData.EffectorBoneName ＋
//                        各リンクボーンの MeshObject.IKLink
//   規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//
// 【方針（#4a：併存・非破壊）】
//   現段階の源泉は (1)。本クラスは追加のみで、どこからも呼ばれない
//   （4c で source-of-truth を (2) へ切替、runtime Links を派生に降格する）。
//   - SyncPerBoneFromLinks : (1) → (2)。EffectorBoneName と各 IKLink を再構築。
//   - RebuildLinksFromPerBone: (2) → (1)。エフェクタから親(HierarchyParentIndex)を
//     辿り IKLink 付きボーンを連続収集して順序付き Links を再構築。
//
// 【前提】
//   - チェーン導出は世界行列と同じボーン親 HierarchyParentIndex を用いる
//     （ModelContext.ComputeWorldMatrices と一致）。
//   - リンクはエフェクタの連続した親鎖であること（PMX標準の脚/腕IKは満たす）。
//   - 1ボーンは高々1つのIKチェーンのリンクに属する。
//   ※順序規約（root↔tip）の実データ整合は 4c で検証する。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// IK の集約表現（Links）と per-bone 表現（IKLink）を相互変換する。
    /// </summary>
    public static class IKChainResolver
    {
        // ------------------------------------------------------------
        // (1) 集約表現 → (2) per-bone 表現
        //   IKData.Links / TargetIndex から EffectorBoneName と各 IKLink を再構築。
        // ------------------------------------------------------------
        public static void SyncPerBoneFromLinks(ModelContext model)
        {
            if (model == null || model.MeshContextList == null) return;
            var list = model.MeshContextList;

            // 既存の per-bone フラグを一旦クリア（stale 防止・全ボーン走査）
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo != null) mo.IKLink = null;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                var ik = ctx?.MeshObject?.IKData;
                if (ik == null || !ik.IsIK) continue;

                // エフェクタ名を index から解決
                ik.EffectorBoneName = ResolveName(list, ik.TargetIndex);

                if (ik.Links == null) continue;
                foreach (var link in ik.Links)
                {
                    if (link == null) continue;
                    if (link.BoneIndex < 0 || link.BoneIndex >= list.Count) continue;
                    var linkMo = list[link.BoneIndex]?.MeshObject;
                    if (linkMo == null) continue;

                    linkMo.IKLink = new IKLinkData
                    {
                        HasLimit = link.HasLimit,
                        LimitMin = link.LimitMin,
                        LimitMax = link.LimitMax
                    };
                }
            }
        }

        // ------------------------------------------------------------
        // (2) per-bone 表現 → (1) 集約表現
        //   EffectorBoneName ＋ 各 IKLink から順序付き Links / TargetIndex を再構築。
        //   エフェクタ→親(HierarchyParentIndex)方向へ連続する IKLink を収集する
        //   （収集順＝エフェクタに近い側から）。
        // ------------------------------------------------------------
        public static void RebuildLinksFromPerBone(ModelContext model)
        {
            if (model == null || model.MeshContextList == null) return;
            var list = model.MeshContextList;
            var nameToIndex = BuildNameToIndex(list);

            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                var ik = ctx?.MeshObject?.IKData;
                if (ik == null || !ik.IsIK) continue;

                // エフェクタ index を name から解決（空なら既存 TargetIndex を維持）
                int effector = ik.TargetIndex;
                if (!string.IsNullOrEmpty(ik.EffectorBoneName) &&
                    nameToIndex.TryGetValue(ik.EffectorBoneName, out int ei))
                {
                    effector = ei;
                }

                var links = new List<IKLinkInfo>();
                if (effector >= 0 && effector < list.Count)
                {
                    // エフェクタの親から上へ、IKLink 付きボーンを連続収集
                    int cur = ParentOf(list, effector);
                    var guard = new HashSet<int>();
                    while (cur >= 0 && cur < list.Count && guard.Add(cur))
                    {
                        var mo = list[cur]?.MeshObject;
                        if (mo == null || mo.IKLink == null) break; // 連続鎖が途切れたら終了

                        links.Add(new IKLinkInfo
                        {
                            BoneIndex = cur,
                            HasLimit = mo.IKLink.HasLimit,
                            LimitMin = mo.IKLink.LimitMin,
                            LimitMax = mo.IKLink.LimitMax
                        });
                        cur = ParentOf(list, cur);
                    }
                }

                // ------------------------------------------------------------
                // 【利用しない】非連続鎖の warning ＋旧 Links フォールバック（残骸）
                //   前提(b)＝リンクがエフェクタの連続親鎖、が崩れた場合の防御。
                //   原則不要（連続親鎖を前提とする）ため非活性のまま残す。
                //   ※復活させる場合は、導出リンク数と per-bone フラグ総数の不一致等で
                //     非連続を検出し、旧 Links を保持してスキップする。
                //
                //   int flaggedCount = CountFlaggedLinksForEffector(list, effector);
                //   if (links.Count != flaggedCount)
                //   {
                //       Debug.LogWarning(
                //           $"[IKChainResolver] 非連続IK鎖: effector={ik.EffectorBoneName} " +
                //           $"derived={links.Count} flagged={flaggedCount} → 旧Links保持");
                //       continue; // ik.Links を上書きせずスキップ（旧 Links フォールバック）
                //   }
                // ------------------------------------------------------------

                ik.TargetIndex = effector;
                ik.Links = links;
            }
        }

        // ------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------

        private static int ParentOf(List<MeshContext> list, int index)
        {
            if (index < 0 || index >= list.Count) return -1;
            return list[index]?.MeshObject?.HierarchyParentIndex ?? -1;
        }

        private static string ResolveName(List<MeshContext> list, int index)
        {
            if (index < 0 || index >= list.Count) return "";
            return list[index]?.Name ?? "";
        }

        private static Dictionary<string, int> BuildNameToIndex(List<MeshContext> list)
        {
            var map = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                var name = list[i]?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!map.ContainsKey(name)) map[name] = i; // 先勝ち（同名は先頭を採用）
            }
            return map;
        }
    }
}
