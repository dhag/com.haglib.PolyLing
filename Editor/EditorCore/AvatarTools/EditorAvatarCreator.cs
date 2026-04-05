using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// Humanoid AvatarアセットをビルドしてAssetDatabaseに保存するロジックのEditorCore実装。
    /// AvatarCreatorPanel（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorAvatarCreator
    {
        /// <summary>
        /// AvatarBuilder.BuildHumanAvatar を実行し、結果をアセットとして保存する。
        /// 成功時は Avatarオブジェクトを、失敗時は null を返す。
        /// </summary>
        public static Avatar BuildAndSaveAvatar(
            GameObject rootObject,
            Dictionary<string, Transform> boneMapping,
            string savePath)
        {
            if (rootObject == null || boneMapping == null || boneMapping.Count == 0) return null;

            try
            {
                var allTransforms = rootObject.GetComponentsInChildren<Transform>(true);
                var skeletonBones = allTransforms.Select(t => new SkeletonBone
                {
                    name     = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale    = t.localScale
                }).ToList();

                var skeletonNames = new HashSet<string>(allTransforms.Select(t => t.name));
                var valid   = new Dictionary<string, Transform>();
                var skipped = new List<string>();

                foreach (var kv in boneMapping)
                {
                    var tf = kv.Value;
                    if (tf == null)                                            { skipped.Add($"{kv.Key} (null)"); continue; }
                    if (!skeletonNames.Contains(tf.name))                      { skipped.Add($"{kv.Key} → {tf.name} (not in hierarchy)"); continue; }
                    if (!tf.IsChildOf(rootObject.transform))                   { skipped.Add($"{kv.Key} → {tf.name} (not child of root)"); continue; }
                    valid[kv.Key] = tf;
                }

                ValidateHumanoidHierarchy(valid, skipped);

                if (skipped.Count > 0)
                    Debug.Log($"[EditorAvatarCreator] Skipped {skipped.Count} bones:\n{string.Join("\n", skipped.Select(s => "  - " + s))}");

                var humanBones = valid.Select(kv => new HumanBone
                {
                    humanName = kv.Key,
                    boneName  = kv.Value.name,
                    limit     = new HumanLimit { useDefaultValues = true }
                }).ToList();

                if (humanBones.Count == 0) { Debug.LogError("[EditorAvatarCreator] No valid human bones"); return null; }

                var desc = new HumanDescription
                {
                    human    = humanBones.ToArray(),
                    skeleton = skeletonBones.ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f,
                    feetSpacing = 0f, hasTranslationDoF = false
                };

                var avatar = AvatarBuilder.BuildHumanAvatar(rootObject, desc);
                if (avatar == null) { Debug.LogError("[EditorAvatarCreator] BuildHumanAvatar returned null"); return null; }

                avatar.name = Path.GetFileNameWithoutExtension(savePath);

                string dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    CreateFolderRecursive(dir);

                AssetDatabase.CreateAsset(avatar, savePath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[EditorAvatarCreator] Saved: {savePath} (isHuman:{avatar.isHuman} isValid:{avatar.isValid})");
                return avatar;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditorAvatarCreator] BuildAndSaveAvatar failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>Humanoidの親子関係整合性を検証し、問題のあるボーンを除外する</summary>
        public static void ValidateHumanoidHierarchy(Dictionary<string, Transform> mapping, List<string> skipped)
        {
            var requirements = new (string child, string parent)[]
            {
                ("Spine","Hips"),("Chest","Spine"),("UpperChest","Chest"),
                ("Neck","Spine"),("Head","Neck"),
                ("LeftShoulder","Spine"),("LeftUpperArm","Spine"),("LeftLowerArm","LeftUpperArm"),("LeftHand","LeftLowerArm"),
                ("RightShoulder","Spine"),("RightUpperArm","Spine"),("RightLowerArm","RightUpperArm"),("RightHand","RightLowerArm"),
                ("LeftUpperLeg","Hips"),("LeftLowerLeg","LeftUpperLeg"),("LeftFoot","LeftLowerLeg"),("LeftToes","LeftFoot"),
                ("RightUpperLeg","Hips"),("RightLowerLeg","RightUpperLeg"),("RightFoot","RightLowerLeg"),("RightToes","RightFoot"),
            };
            var toRemove = new List<string>();
            foreach (var (child, parent) in requirements)
            {
                if (!mapping.TryGetValue(child, out var ct)) continue;
                if (!mapping.TryGetValue(parent, out var pt)) continue;
                if (!ct.IsChildOf(pt)) { skipped.Add($"{child} not descendant of {parent}"); toRemove.Add(child); }
            }
            foreach (var b in toRemove) mapping.Remove(b);
        }

        /// <summary>"Assets/..." 形式のフォルダパスを再帰的に作成する</summary>
        public static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
