// Assets/Editor/Poly_Ling/Core/Ops/HumanoidMappingResolver.cs
// ============================================================
// Humanoid 割当 導出／同期ユーティリティ
// ============================================================
//
// 【役割】
//   Humanoid 割当の2表現を相互変換する純ロジック（データを持たない）。
//     (1) 集中表現   : ModelContext.HumanoidMapping（name→index Dict）
//     (2) per-bone 表現: 各ボーンの MeshObject.HumanBodyBone（Unity Humanoid 名）
//   規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//
// 【方針（確定：境界のみ同期）】
//   per-bone を永続 canonical、Dict を実行時 working とする。両者の同期は
//   保存・読込の境界のみで行い、編集中のリアルタイム同期はしない
//   （割当は UI 経由で Dict を編集 → 保存時に Sync、読込後に Rebuild）。
//   import は Dict を確立しないため import 時同期は不要（no-op）。
//   - SyncPerBoneFromMapping   : (1) → (2)。保存前に呼ぶ。
//   - RebuildMappingFromPerBone: (2) → (1)。読込後に呼ぶ。
//
// 【一意性】
//   同一 Humanoid 名を複数ボーンが主張しない、を不変条件とする。
//   Dict は構造上1名1indexのため、Rebuild では先勝ちで重複を排除する
//   （検出時の warning は「利用しない」残骸としてコメントで残す）。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。
//
// ============================================================

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// Humanoid 割当の集中表現（Dict）と per-bone 表現（HumanBodyBone）を相互変換する。
    /// </summary>
    public static class HumanoidMappingResolver
    {
        // ------------------------------------------------------------
        // (1) 集中表現 → (2) per-bone 表現
        //   HumanoidMapping（name→index）から各ボーンの HumanBodyBone を再構築。
        // ------------------------------------------------------------
        public static void SyncPerBoneFromMapping(ModelContext model)
        {
            if (model == null || model.MeshContextList == null) return;
            var list = model.MeshContextList;

            // 既存の per-bone 割当を一旦クリア（stale 防止・全ボーン走査）
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo != null) mo.HumanBodyBone = "";
            }

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;

            foreach (var kv in mapping.BoneIndexMap)
            {
                int idx = kv.Value;
                if (idx < 0 || idx >= list.Count) continue;
                var mo = list[idx]?.MeshObject;
                if (mo == null) continue;
                mo.HumanBodyBone = kv.Key; // Unity Humanoid 名
            }
        }

        // ------------------------------------------------------------
        // (2) per-bone 表現 → (1) 集中表現
        //   各ボーンの HumanBodyBone を全走査して name→index Dict を再構築。
        //   一意性：同一名は先勝ち（後続は無視）。
        // ------------------------------------------------------------
        public static void RebuildMappingFromPerBone(ModelContext model)
        {
            if (model == null || model.MeshContextList == null) return;
            var list = model.MeshContextList;

            var dict = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo == null) continue;
                var name = mo.HumanBodyBone;
                if (string.IsNullOrEmpty(name)) continue;

                if (dict.ContainsKey(name))
                {
                    // ------------------------------------------------------------
                    // 【利用しない】同一 Humanoid 名の重複割当 warning（残骸）
                    //   先勝ちで排除するため active な dedup は下の continue のみ。
                    //   warning は原則不要（コメントのまま非活性）。
                    //   Debug.LogWarning(
                    //       $"[HumanoidMappingResolver] Humanoid名重複: {name} " +
                    //       $"既存index={dict[name]} 無視index={i}");
                    // ------------------------------------------------------------
                    continue; // 先勝ち
                }
                dict[name] = i;
            }

            if (model.HumanoidMapping != null)
                model.HumanoidMapping.FromDictionary(dict);
        }
    }
}
