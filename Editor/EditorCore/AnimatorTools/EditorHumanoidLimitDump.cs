using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// Humanoidボーンリミット・筋肉値のCSV生成ロジックのEditorCore実装。
    /// HumanoidLimitDumpWindow（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorHumanoidLimitDump
    {
        // ================================================================
        // デフォルト筋肉値（HumanTrait.GetMuscleDefaultMin/Max ベース）
        // ================================================================

        /// <summary>デフォルト筋肉値CSVを構築する</summary>
        public static string BuildDefaultMusclesCsv(bool includeIndex, bool includeDerivedGroup)
        {
            string[] names = HumanTrait.MuscleName;
            int count = HumanTrait.MuscleCount;
            var sb = new StringBuilder(80 * count);

            if (includeIndex) sb.Append("i,");
            if (includeDerivedGroup) sb.Append("group,");
            sb.AppendLine("name,defaultMin,defaultMax,defaultCenterMuscle");

            for (int i = 0; i < count; i++)
            {
                float mn = HumanTrait.GetMuscleDefaultMin(i);
                float mx = HumanTrait.GetMuscleDefaultMax(i);

                if (includeIndex) sb.Append(i).Append(',');
                if (includeDerivedGroup)
                    sb.Append('"').Append(DeriveMuscleGroup(names[i])).Append('"').Append(',');

                sb.Append('"').Append(names[i].Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(mn.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(mx.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
                sb.AppendLine("0");
            }

            return sb.ToString();
        }

        /// <summary>筋肉名からグループ名を導出する</summary>
        public static string DeriveMuscleGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Other";
            if (name.StartsWith("Spine", StringComparison.Ordinal) ||
                name.StartsWith("Chest", StringComparison.Ordinal) ||
                name.StartsWith("UpperChest", StringComparison.Ordinal) ||
                name.StartsWith("Neck", StringComparison.Ordinal) ||
                name.StartsWith("Head", StringComparison.Ordinal)) return "Body";
            if (name.StartsWith("Left Shoulder", StringComparison.Ordinal) ||
                name.StartsWith("Left Arm", StringComparison.Ordinal) ||
                name.StartsWith("Left Forearm", StringComparison.Ordinal) ||
                name.StartsWith("Left Hand", StringComparison.Ordinal)) return "LeftArm";
            if (name.StartsWith("Right Shoulder", StringComparison.Ordinal) ||
                name.StartsWith("Right Arm", StringComparison.Ordinal) ||
                name.StartsWith("Right Forearm", StringComparison.Ordinal) ||
                name.StartsWith("Right Hand", StringComparison.Ordinal)) return "RightArm";
            if (name.StartsWith("Left Upper Leg", StringComparison.Ordinal) ||
                name.StartsWith("Left Lower Leg", StringComparison.Ordinal) ||
                name.StartsWith("Left Foot", StringComparison.Ordinal) ||
                name.StartsWith("Left Toes", StringComparison.Ordinal)) return "LeftLeg";
            if (name.StartsWith("Right Upper Leg", StringComparison.Ordinal) ||
                name.StartsWith("Right Lower Leg", StringComparison.Ordinal) ||
                name.StartsWith("Right Foot", StringComparison.Ordinal) ||
                name.StartsWith("Right Toes", StringComparison.Ordinal)) return "RightLeg";
            if (name.StartsWith("Left Thumb", StringComparison.Ordinal) ||
                name.StartsWith("Left Index", StringComparison.Ordinal) ||
                name.StartsWith("Left Middle", StringComparison.Ordinal) ||
                name.StartsWith("Left Ring", StringComparison.Ordinal) ||
                name.StartsWith("Left Little", StringComparison.Ordinal)) return "LeftFingers";
            if (name.StartsWith("Right Thumb", StringComparison.Ordinal) ||
                name.StartsWith("Right Index", StringComparison.Ordinal) ||
                name.StartsWith("Right Middle", StringComparison.Ordinal) ||
                name.StartsWith("Right Ring", StringComparison.Ordinal) ||
                name.StartsWith("Right Little", StringComparison.Ordinal)) return "RightFingers";
            return "Other";
        }

        // ================================================================
        // Avatarリミット（HumanDescription.human ベース）
        // ================================================================

        /// <summary>AvatarのリミットCSVを構築する</summary>
        public static string BuildAvatarLimitsCsv(Avatar av, bool onlyOverrides, bool includeAxisLength)
        {
            if (av == null) return "error,avatar is null\n";
            if (!av.isHuman) return "error,avatar is not humanoid\n";

            HumanDescription hd = av.humanDescription;
            var list = hd.human ?? Array.Empty<HumanBone>();
            if (onlyOverrides)
                list = list.Where(h => h.limit.useDefaultValues == false).ToArray();

            var sb = new StringBuilder(120 * Math.Max(1, list.Length));
            sb.Append("humanName,boneName,useDefaultValues,");
            sb.Append("minX,minY,minZ,maxX,maxY,maxZ,centerX,centerY,centerZ");
            if (includeAxisLength) sb.Append(",axisLength");
            sb.AppendLine();

            foreach (var hb in list)
            {
                var lim = hb.limit;
                sb.Append('"').Append((hb.humanName ?? "").Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append('"').Append((hb.boneName  ?? "").Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(lim.useDefaultValues ? "true" : "false").Append(',');
                AppendVec3Csv(sb, lim.min); sb.Append(',');
                AppendVec3Csv(sb, lim.max); sb.Append(',');
                AppendVec3Csv(sb, lim.center);
                if (includeAxisLength)
                    sb.Append(',').Append(lim.axisLength.ToString("0.######", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ================================================================
        // Probe Center Pose（muscles=0 を適用してLocal Euler Dumpする）
        // ================================================================

        /// <summary>muscles=0 ポーズを適用して各ボーンの LocalEuler をCSV化する</summary>
        public static string BuildProbeCenterPoseCsv(
            Animator animator,
            bool useAnimatorTransform,
            bool dumpAllBones,
            bool includeWorld,
            bool applyPose,
            bool restoreAfter,
            bool preview = false,
            int previewMaxRows = 200)
        {
            if (animator == null) return "error,animator is null\n";
            if (animator.avatar == null || !animator.avatar.isHuman)
                return "error,animator avatar is not humanoid\n";

            Transform root = animator.transform;
            var handler = new HumanPoseHandler(animator.avatar, root);

            HumanPose backup = default;
            bool hasBackup = false;
            if (restoreAfter)
            {
                try { handler.GetHumanPose(ref backup); hasBackup = true; }
                catch { hasBackup = false; }
            }

            HumanPose pose = default;
            pose.bodyPosition = Vector3.zero;
            pose.bodyRotation = Quaternion.identity;
            pose.muscles = new float[HumanTrait.MuscleCount];

            if (applyPose)
            {
                handler.SetHumanPose(ref pose);
                animator.Update(0f);
            }

            var sb = new StringBuilder();
            sb.Append("boneEnum,boneName,transformPath,localEulerX,localEulerY,localEulerZ");
            if (includeWorld) sb.Append(",worldEulerX,worldEulerY,worldEulerZ");
            sb.AppendLine();

            var bones = Enum.GetValues(typeof(HumanBodyBones))
                .Cast<HumanBodyBones>()
                .Where(b => b != HumanBodyBones.LastBone);

            int row = 0;
            foreach (var b in bones)
            {
                if (!dumpAllBones)
                {
                    if (!(b == HumanBodyBones.Hips || b == HumanBodyBones.Spine || b == HumanBodyBones.Chest ||
                          b == HumanBodyBones.Neck || b == HumanBodyBones.Head ||
                          b == HumanBodyBones.LeftUpperArm  || b == HumanBodyBones.LeftLowerArm  || b == HumanBodyBones.LeftHand ||
                          b == HumanBodyBones.RightUpperArm || b == HumanBodyBones.RightLowerArm || b == HumanBodyBones.RightHand ||
                          b == HumanBodyBones.LeftUpperLeg  || b == HumanBodyBones.LeftLowerLeg  || b == HumanBodyBones.LeftFoot ||
                          b == HumanBodyBones.RightUpperLeg || b == HumanBodyBones.RightLowerLeg || b == HumanBodyBones.RightFoot))
                        continue;
                }

                Transform t = animator.GetBoneTransform(b);
                if (t == null) continue;

                Vector3 le = NormalizeEuler(t.localEulerAngles);
                sb.Append((int)b).Append(',');
                sb.Append('"').Append(b.ToString()).Append('"').Append(',');
                sb.Append('"').Append(GetPathRelative(root, t).Replace("\"", "\"\"")).Append('"').Append(',');
                sb.Append(le.x.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(le.y.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(le.z.ToString("0.######", CultureInfo.InvariantCulture));

                if (includeWorld)
                {
                    Vector3 we = NormalizeEuler(t.eulerAngles);
                    sb.Append(',')
                      .Append(we.x.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                      .Append(we.y.ToString("0.######", CultureInfo.InvariantCulture)).Append(',')
                      .Append(we.z.ToString("0.######", CultureInfo.InvariantCulture));
                }

                sb.AppendLine();
                row++;
                if (preview && row >= previewMaxRows) break;
            }

            if (restoreAfter && hasBackup)
            {
                try { handler.SetHumanPose(ref backup); animator.Update(0f); }
                catch { /* ignore */ }
            }

            return sb.ToString();
        }

        // ================================================================
        // 共通ユーティリティ（ウィンドウから移動）
        // ================================================================

        /// <summary>0..360 を -180..180 に正規化する</summary>
        public static Vector3 NormalizeEuler(Vector3 euler)
            => new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));

        public static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a >  180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        /// <summary>root から target への相対パスを返す</summary>
        public static string GetPathRelative(Transform root, Transform target)
        {
            if (target == root) return "";
            var sb = new StringBuilder();
            Transform cur = target;
            while (cur != null && cur != root)
            {
                if (sb.Length == 0) sb.Insert(0, cur.name);
                else sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }

        /// <summary>StringBuilderにVec3をCSV形式で追記する</summary>
        public static void AppendVec3Csv(StringBuilder sb, Vector3 v)
        {
            sb.Append(v.x.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(v.y.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(v.z.ToString("0.######", CultureInfo.InvariantCulture));
        }

        public static string Vec3ToString(Vector3 v)
            => $"{v.x.ToString("0.######", CultureInfo.InvariantCulture)}," +
               $"{v.y.ToString("0.######", CultureInfo.InvariantCulture)}," +
               $"{v.z.ToString("0.######", CultureInfo.InvariantCulture)}";

        /// <summary>CSVテキストをファイルとして保存する（SaveFilePanel）</summary>
        public static void SaveTextAsCsv(string defaultName, string csvText)
        {
            string path = EditorUtility.SaveFilePanel("CSVを保存", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, csvText, System.Text.Encoding.UTF8);
            Debug.Log($"[EditorHumanoidLimitDump] Saved: {path}");
        }
    }
}
