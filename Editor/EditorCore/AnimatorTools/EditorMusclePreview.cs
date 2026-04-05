using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// Humanoidの筋肉値プレビュー処理のEditorCore実装。
    /// MusclePreviewWindow（EditorWindow）はUIと状態を保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorMusclePreview
    {
        /// <summary>HumanPoseHandlerを生成して現在値を読み込む</summary>
        public static HumanPoseHandler Init(
            Animator animator,
            out HumanPose pose,
            out int muscleIndex,
            out float currentValue,
            string targetMuscleName = "LeftLowerArmStretch")
        {
            pose = new HumanPose();
            muscleIndex = -1;
            currentValue = 0f;

            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                return null;

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            muscleIndex = FindMuscleIndex(targetMuscleName);
            handler.GetHumanPose(ref pose);
            if (muscleIndex >= 0) currentValue = pose.muscles[muscleIndex];
            return handler;
        }

        /// <summary>AnimationModeを開始してプレビュー状態に入る</summary>
        public static void StartPreview()
        {
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();
        }

        /// <summary>AnimationModeを停止してプレビュー状態を解除する</summary>
        public static void StopPreview()
        {
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        /// <summary>指定の筋肉値をHumanPoseに適用する</summary>
        public static void Apply(HumanPoseHandler handler, ref HumanPose pose, int muscleIndex, float value)
        {
            if (handler == null || muscleIndex < 0) return;
            handler.GetHumanPose(ref pose);
            pose.muscles[muscleIndex] = value;
            handler.SetHumanPose(ref pose);
            RepaintScene();
        }

        /// <summary>筋肉名からインデックスを検索する</summary>
        public static int FindMuscleIndex(string muscleName)
        {
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
                if (HumanTrait.MuscleName[i] == muscleName) return i;
            return -1;
        }

        /// <summary>SceneViewを再描画する</summary>
        public static void RepaintScene()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}
