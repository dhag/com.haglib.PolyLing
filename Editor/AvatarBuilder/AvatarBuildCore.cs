// Editor/AvatarBuilder/AvatarBuildCore.cs
// ============================================================
// Humanoid Avatar 生成の共有コア
// ============================================================
//
// 【役割】
//   root（ボーン Transform 階層）＋ Humanoid 対応（humanName→boneName）＋
//   可動域（humanName→HumanLimit・度）から Avatar を生成し .asset 保存する。
//   HumanoidAvatarBuilderWindow（csv 経路）と HierarchyExportWindow（model 経路）の
//   両方から呼ぶ。手順（HumanBone/必須判定/SkeletonBone/HumanDescription/生成/保存）を
//   1箇所に集約し重複を排除する。
//
// 【入力の前提】
//   - map/limits の humanName は HumanTrait.BoneName 形式（指はスペース付き）。
//   - root は Instantiate 済み（プレファブ資産は呼び出し側でシーン化しておくこと）。
//   - savePath は "Assets/..." 以下・拡張子 .asset。既存は上書き（delete→create）。
//
// 【Editor 依存】本ファイルは Editor アセンブリ（AssetDatabase/AvatarBuilder を直接使用）。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorIO
{
    /// <summary>Humanoid Avatar 生成・保存の共有処理。</summary>
    public static class AvatarBuildCore
    {
        /// <summary>
        /// root/map/limits から Avatar を生成し savePath(.asset) に保存する。
        /// 失敗時は null（理由は log で通知）。
        /// </summary>
        public static Avatar BuildAndSaveAvatar(
            GameObject root,
            Dictionary<string, string> map,
            Dictionary<string, HumanLimit> limits,
            string savePath,
            Action<string> log)
        {
            void L(string m) => log?.Invoke(m);

            if (root == null) { L("Avatar: root が null。"); return null; }
            if (map == null || map.Count == 0) { L("Avatar: 対応表が空。"); return null; }
            if (string.IsNullOrEmpty(savePath) || !savePath.Replace('\\', '/').StartsWith("Assets/"))
            {
                L("Avatar: 保存先は Assets/ 以下の .asset を指定してください: " + savePath);
                return null;
            }

            // 2) root 配下の全 Transform を名前で索引化
            var allTf = root.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in allTf)
            {
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
                else L("ボーン名の重複（先勝ちで採用）: " + t.name);
            }

            var validHuman = new HashSet<string>(HumanTrait.BoneName);

            // 3) HumanBone[] を構築
            var humanBones = new List<HumanBone>();
            var resolvedHuman = new HashSet<string>();
            foreach (var kv in map)
            {
                string humanName = kv.Key, boneName = kv.Value;
                if (!validHuman.Contains(humanName)) { L("未知の Humanoid 名（無視）: " + humanName); continue; }
                if (!byName.ContainsKey(boneName))    { L("ボーンが階層に無い（無視）: " + humanName + " → " + boneName); continue; }

                var hb = new HumanBone { humanName = humanName, boneName = boneName };
                if (limits != null && limits.TryGetValue(humanName, out var lim))
                    hb.limit = lim;                     // useDefaultValues=false 込み（度）
                else
                    hb.limit.useDefaultValues = true;   // 既定可動域
                humanBones.Add(hb);
                resolvedHuman.Add(humanName);
            }

            // 4) 必須ボーンの充足判定
            var missing = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
                if (HumanTrait.RequiredBone(i) && !resolvedHuman.Contains(HumanTrait.BoneName[i]))
                    missing.Add(HumanTrait.BoneName[i]);
            if (missing.Count > 0)
            {
                L("必須ボーンが不足のため生成中止:\n  " + string.Join("\n  ", missing));
                return null;
            }

            // 5) SkeletonBone[]（root 含む配下の全 Transform を局所TRSで）
            var skeleton = new List<SkeletonBone>(allTf.Length);
            foreach (var t in allTf)
            {
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                });
            }

            // 6) HumanDescription → Avatar（現姿勢をバインドに使用）
            var desc = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0.0f,
                hasTranslationDoF = false
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
            if (avatar == null || !avatar.isValid)
            {
                L("Avatar 生成に失敗（isValid=false）。マッピング/姿勢/必須ボーンを確認。");
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                return null;
            }
            avatar.name = System.IO.Path.GetFileNameWithoutExtension(savePath);

            // 7) .asset として保存（既存は上書き＝delete→create で決定論）
            if (AssetDatabase.LoadAssetAtPath<Avatar>(savePath) != null)
                AssetDatabase.DeleteAsset(savePath);
            AssetDatabase.CreateAsset(avatar, savePath);
            AssetDatabase.SaveAssets();

            L($"Avatar 生成・保存: human={humanBones.Count} / skeleton={skeleton.Count}\n  → {savePath}");
            return avatar;
        }
    }
}
