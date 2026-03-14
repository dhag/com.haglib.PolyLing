using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ボディパーツ別にマッスル値をコピーできる高度版
/// 部分的なコピーやブレンドが可能
/// </summary>
[RequireComponent(typeof(Animator))]
public class AdvancedMuscleValueCopier : MonoBehaviour
{
    [System.Serializable]
    public class BodyPartSettings
    {
        public bool enabled = true;
        [Range(0f, 1f)]
        public float weight = 1f;
    }

    [Header("コピー元")]
    public Animator sourceAnimator;

    [Header("有効/無効")]
    public bool enableCopy = true;

    [Header("ボディパーツ別設定")]
    public BodyPartSettings body = new BodyPartSettings();
    public BodyPartSettings head = new BodyPartSettings();
    public BodyPartSettings leftArm = new BodyPartSettings();
    public BodyPartSettings rightArm = new BodyPartSettings();
    public BodyPartSettings leftLeg = new BodyPartSettings();
    public BodyPartSettings rightLeg = new BodyPartSettings();
    public BodyPartSettings leftFingers = new BodyPartSettings();
    public BodyPartSettings rightFingers = new BodyPartSettings();

    [Header("ルート設定")]
    public bool copyRootPosition = false;
    public bool copyRootRotation = false;
    [Range(0f, 1f)]
    public float rootWeight = 1f;

    [Header("補間設定")]
    public bool useLerp = false;
    [Range(0.1f, 30f)]
    public float lerpSpeed = 15f;

    private HumanPoseHandler sourcePoseHandler;
    private HumanPoseHandler targetPoseHandler;
    private HumanPose sourceHumanPose;
    private HumanPose targetHumanPose;
    private HumanPose blendedPose;
    private Animator targetAnimator;
    private bool isInitialized = false;

    // マッスルインデックスのキャッシュ
    private static Dictionary<string, int[]> bodyPartMuscles;

    void Start()
    {
        InitializeMuscleMapping();
        Initialize();
    }

    /// <summary>
    /// ボディパーツとマッスルインデックスのマッピングを初期化
    /// </summary>
    private static void InitializeMuscleMapping()
    {
        if (bodyPartMuscles != null) return;

        bodyPartMuscles = new Dictionary<string, int[]>();

        // Body (Spine, Chest, UpperChest)
        bodyPartMuscles["Body"] = new int[]
        {
            0, 1, 2,    // Spine Front-Back, Left-Right, Twist Left-Right
            3, 4, 5,    // Chest Front-Back, Left-Right, Twist Left-Right
            6, 7, 8     // UpperChest Front-Back, Left-Right, Twist Left-Right
        };

        // Head (Neck, Head, Eye, Jaw)
        bodyPartMuscles["Head"] = new int[]
        {
            9, 10, 11,  // Neck Nod Down-Up, Tilt Left-Right, Turn Left-Right
            12, 13, 14, // Head Nod Down-Up, Tilt Left-Right, Turn Left-Right
            15, 16,     // Left Eye Down-Up, In-Out
            17, 18,     // Right Eye Down-Up, In-Out
            19, 20      // Jaw Close, Left-Right
        };

        // Left Arm (Shoulder, Arm, Forearm, Hand)
        bodyPartMuscles["LeftArm"] = new int[]
        {
            21, 22,     // Left Shoulder Down-Up, Front-Back
            23, 24, 25, // Left Arm Down-Up, Front-Back, Twist In-Out
            26, 27,     // Left Forearm Stretch, Twist In-Out
            28, 29      // Left Hand Down-Up, In-Out
        };

        // Right Arm
        bodyPartMuscles["RightArm"] = new int[]
        {
            30, 31,     // Right Shoulder Down-Up, Front-Back
            32, 33, 34, // Right Arm Down-Up, Front-Back, Twist In-Out
            35, 36,     // Right Forearm Stretch, Twist In-Out
            37, 38      // Right Hand Down-Up, In-Out
        };

        // Left Leg (UpperLeg, Leg, Foot, Toes)
        bodyPartMuscles["LeftLeg"] = new int[]
        {
            39, 40, 41, // Left Upper Leg Front-Back, In-Out, Twist In-Out
            42, 43,     // Left Lower Leg Stretch, Twist In-Out
            44, 45, 46  // Left Foot Up-Down, Twist In-Out, Toes Up-Down
        };

        // Right Leg
        bodyPartMuscles["RightLeg"] = new int[]
        {
            47, 48, 49, // Right Upper Leg Front-Back, In-Out, Twist In-Out
            50, 51,     // Right Lower Leg Stretch, Twist In-Out
            52, 53, 54  // Right Foot Up-Down, Twist In-Out, Toes Up-Down
        };

        // Left Fingers (55-74)
        List<int> leftFingers = new List<int>();
        for (int i = 55; i <= 74; i++) leftFingers.Add(i);
        bodyPartMuscles["LeftFingers"] = leftFingers.ToArray();

        // Right Fingers (75-94)
        List<int> rightFingers = new List<int>();
        for (int i = 75; i <= 94; i++) rightFingers.Add(i);
        bodyPartMuscles["RightFingers"] = rightFingers.ToArray();
    }

    public void Initialize()
    {
        targetAnimator = GetComponent<Animator>();

        if (!ValidateAnimators()) return;

        sourcePoseHandler = new HumanPoseHandler(sourceAnimator.avatar, sourceAnimator.transform);
        targetPoseHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);

        sourceHumanPose = new HumanPose();
        targetHumanPose = new HumanPose();
        blendedPose = new HumanPose();

        isInitialized = true;
    }

    private bool ValidateAnimators()
    {
        if (sourceAnimator == null || targetAnimator == null) return false;
        if (!sourceAnimator.isHuman || !targetAnimator.isHuman) return false;
        if (sourceAnimator.avatar == null || targetAnimator.avatar == null) return false;
        return true;
    }

    void OnDisable()
    {
        Cleanup();
    }

    void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        sourcePoseHandler?.Dispose();
        targetPoseHandler?.Dispose();
        sourcePoseHandler = null;
        targetPoseHandler = null;
        isInitialized = false;
    }

    void LateUpdate()
    {
        if (!enableCopy || !isInitialized) return;
        CopyMuscleValues();
    }

    private void CopyMuscleValues()
    {
        if (sourcePoseHandler == null || targetPoseHandler == null) return;

        // ソースと現在のターゲットポーズを取得
        sourcePoseHandler.GetHumanPose(ref sourceHumanPose);
        targetPoseHandler.GetHumanPose(ref targetHumanPose);

        // ブレンド用にターゲットをコピー
        blendedPose.bodyPosition = targetHumanPose.bodyPosition;
        blendedPose.bodyRotation = targetHumanPose.bodyRotation;

        if (blendedPose.muscles == null || blendedPose.muscles.Length != sourceHumanPose.muscles.Length)
        {
            blendedPose.muscles = new float[sourceHumanPose.muscles.Length];
        }
        System.Array.Copy(targetHumanPose.muscles, blendedPose.muscles, targetHumanPose.muscles.Length);

        // ボディパーツ別にブレンド
        ApplyBodyPart("Body", body);
        ApplyBodyPart("Head", head);
        ApplyBodyPart("LeftArm", leftArm);
        ApplyBodyPart("RightArm", rightArm);
        ApplyBodyPart("LeftLeg", leftLeg);
        ApplyBodyPart("RightLeg", rightLeg);
        ApplyBodyPart("LeftFingers", leftFingers);
        ApplyBodyPart("RightFingers", rightFingers);

        // ルート位置・回転
        if (copyRootPosition)
        {
            blendedPose.bodyPosition = Vector3.Lerp(
                targetHumanPose.bodyPosition,
                sourceHumanPose.bodyPosition,
                rootWeight
            );
        }

        if (copyRootRotation)
        {
            blendedPose.bodyRotation = Quaternion.Slerp(
                targetHumanPose.bodyRotation,
                sourceHumanPose.bodyRotation,
                rootWeight
            );
        }

        // 補間適用
        if (useLerp)
        {
            for (int i = 0; i < blendedPose.muscles.Length; i++)
            {
                blendedPose.muscles[i] = Mathf.Lerp(
                    targetHumanPose.muscles[i],
                    blendedPose.muscles[i],
                    Time.deltaTime * lerpSpeed
                );
            }
        }

        // 適用
        targetPoseHandler.SetHumanPose(ref blendedPose);
    }

    private void ApplyBodyPart(string partName, BodyPartSettings settings)
    {
        if (!settings.enabled || settings.weight <= 0f) return;

        if (!bodyPartMuscles.TryGetValue(partName, out int[] indices)) return;

        foreach (int i in indices)
        {
            if (i < blendedPose.muscles.Length && i < sourceHumanPose.muscles.Length)
            {
                blendedPose.muscles[i] = Mathf.Lerp(
                    targetHumanPose.muscles[i],
                    sourceHumanPose.muscles[i],
                    settings.weight
                );
            }
        }
    }

    /// <summary>
    /// 全ボディパーツを有効/無効
    /// </summary>
    public void SetAllBodyParts(bool enabled, float weight = 1f)
    {
        SetBodyPartSettings(body, enabled, weight);
        SetBodyPartSettings(head, enabled, weight);
        SetBodyPartSettings(leftArm, enabled, weight);
        SetBodyPartSettings(rightArm, enabled, weight);
        SetBodyPartSettings(leftLeg, enabled, weight);
        SetBodyPartSettings(rightLeg, enabled, weight);
        SetBodyPartSettings(leftFingers, enabled, weight);
        SetBodyPartSettings(rightFingers, enabled, weight);
    }

    /// <summary>
    /// 上半身のみ有効
    /// </summary>
    public void SetUpperBodyOnly(float weight = 1f)
    {
        SetAllBodyParts(false, 0f);
        SetBodyPartSettings(body, true, weight);
        SetBodyPartSettings(head, true, weight);
        SetBodyPartSettings(leftArm, true, weight);
        SetBodyPartSettings(rightArm, true, weight);
        SetBodyPartSettings(leftFingers, true, weight);
        SetBodyPartSettings(rightFingers, true, weight);
    }

    /// <summary>
    /// 下半身のみ有効
    /// </summary>
    public void SetLowerBodyOnly(float weight = 1f)
    {
        SetAllBodyParts(false, 0f);
        SetBodyPartSettings(leftLeg, true, weight);
        SetBodyPartSettings(rightLeg, true, weight);
    }

    private void SetBodyPartSettings(BodyPartSettings settings, bool enabled, float weight)
    {
        settings.enabled = enabled;
        settings.weight = weight;
    }
}
