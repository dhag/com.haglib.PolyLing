using UnityEngine;

/// <summary>
/// 他のHumanoidモデルのマッスル値をリアルタイムにコピーするコンポーネント
/// ターゲット（コピー先）のモデルにアタッチして使用
/// </summary>
[RequireComponent(typeof(Animator))]
public class MuscleValueCopier : MonoBehaviour
{
    [Header("コピー元の設定")]
    [Tooltip("マッスル値のコピー元となるHumanoidモデル")]
    public Animator sourceAnimator;

    [Header("コピーオプション")]
    [Tooltip("マッスル値のコピーを有効にする")]
    public bool enableCopy = true;

    [Tooltip("ルート位置もコピーする")]
    public bool copyRootPosition = true;

    [Tooltip("ルート回転もコピーする")]
    public bool copyRootRotation = true;

    [Tooltip("マッスル値の補間を有効にする（滑らかな動き）")]
    public bool useLerp = false;

    [Range(0.1f, 30f)]
    [Tooltip("補間の速度")]
    public float lerpSpeed = 15f;

    [Header("デバッグ")]
    [Tooltip("マッスル値をインスペクタに表示")]
    public bool showMuscleValues = false;

    [SerializeField, ReadOnlyInInspector]
    private float[] currentMuscleValues;

    // HumanPoseHandler
    private HumanPoseHandler sourcePoseHandler;
    private HumanPoseHandler targetPoseHandler;

    // HumanPose構造体
    private HumanPose sourceHumanPose;
    private HumanPose targetHumanPose;

    // ターゲットのAnimator（自分自身）
    private Animator targetAnimator;

    // 初期化フラグ
    private bool isInitialized = false;

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        // 再有効化時に再初期化
        if (!isInitialized)
        {
            Initialize();
        }
    }

    void OnDisable()
    {
        Cleanup();
    }

    void OnDestroy()
    {
        Cleanup();
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    public void Initialize()
    {
        targetAnimator = GetComponent<Animator>();

        if (!ValidateAnimators())
        {
            return;
        }

        // HumanPoseHandlerを作成
        sourcePoseHandler = new HumanPoseHandler(sourceAnimator.avatar, sourceAnimator.transform);
        targetPoseHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);

        // HumanPose構造体を初期化
        sourceHumanPose = new HumanPose();
        targetHumanPose = new HumanPose();

        // デバッグ用配列を初期化
        if (showMuscleValues)
        {
            currentMuscleValues = new float[HumanTrait.MuscleCount];
        }

        isInitialized = true;
        Debug.Log($"[MuscleValueCopier] 初期化完了: {sourceAnimator.name} → {targetAnimator.name}");
    }

    /// <summary>
    /// Animatorの検証
    /// </summary>
    private bool ValidateAnimators()
    {
        if (sourceAnimator == null)
        {
            Debug.LogWarning("[MuscleValueCopier] コピー元のAnimatorが設定されていません");
            return false;
        }

        if (targetAnimator == null)
        {
            Debug.LogError("[MuscleValueCopier] ターゲットのAnimatorが見つかりません");
            return false;
        }

        if (!sourceAnimator.isHuman)
        {
            Debug.LogError($"[MuscleValueCopier] コピー元 '{sourceAnimator.name}' はHumanoidではありません");
            return false;
        }

        if (!targetAnimator.isHuman)
        {
            Debug.LogError($"[MuscleValueCopier] ターゲット '{targetAnimator.name}' はHumanoidではありません");
            return false;
        }

        if (sourceAnimator.avatar == null)
        {
            Debug.LogError("[MuscleValueCopier] コピー元のAvatarがnullです");
            return false;
        }

        if (targetAnimator.avatar == null)
        {
            Debug.LogError("[MuscleValueCopier] ターゲットのAvatarがnullです");
            return false;
        }

        return true;
    }

    /// <summary>
    /// クリーンアップ処理
    /// </summary>
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
        if (!enableCopy || !isInitialized)
        {
            return;
        }

        CopyMuscleValues();
    }

    /// <summary>
    /// マッスル値をコピー
    /// </summary>
    private void CopyMuscleValues()
    {
        if (sourcePoseHandler == null || targetPoseHandler == null)
        {
            return;
        }

        // ソースからHumanPoseを取得
        sourcePoseHandler.GetHumanPose(ref sourceHumanPose);

        if (useLerp)
        {
            // 現在のターゲットポーズを取得
            targetPoseHandler.GetHumanPose(ref targetHumanPose);

            // マッスル値を補間
            for (int i = 0; i < sourceHumanPose.muscles.Length; i++)
            {
                targetHumanPose.muscles[i] = Mathf.Lerp(
                    targetHumanPose.muscles[i],
                    sourceHumanPose.muscles[i],
                    Time.deltaTime * lerpSpeed
                );
            }

            // ルート位置・回転の補間
            if (copyRootPosition)
            {
                targetHumanPose.bodyPosition = Vector3.Lerp(
                    targetHumanPose.bodyPosition,
                    sourceHumanPose.bodyPosition,
                    Time.deltaTime * lerpSpeed
                );
            }

            if (copyRootRotation)
            {
                targetHumanPose.bodyRotation = Quaternion.Slerp(
                    targetHumanPose.bodyRotation,
                    sourceHumanPose.bodyRotation,
                    Time.deltaTime * lerpSpeed
                );
            }
        }
        else
        {
            // 直接コピー
            targetHumanPose.muscles = (float[])sourceHumanPose.muscles.Clone();

            if (copyRootPosition)
            {
                targetHumanPose.bodyPosition = sourceHumanPose.bodyPosition;
            }

            if (copyRootRotation)
            {
                targetHumanPose.bodyRotation = sourceHumanPose.bodyRotation;
            }
        }

        // ターゲットにHumanPoseを適用
        targetPoseHandler.SetHumanPose(ref targetHumanPose);

        // デバッグ表示
        if (showMuscleValues && currentMuscleValues != null)
        {
            System.Array.Copy(targetHumanPose.muscles, currentMuscleValues, 
                Mathf.Min(targetHumanPose.muscles.Length, currentMuscleValues.Length));
        }
    }

    /// <summary>
    /// コピー元を動的に変更
    /// </summary>
    public void SetSource(Animator newSource)
    {
        Cleanup();
        sourceAnimator = newSource;
        Initialize();
    }

    /// <summary>
    /// 特定のマッスル値を取得
    /// </summary>
    public float GetMuscleValue(int muscleIndex)
    {
        if (!isInitialized || sourceHumanPose.muscles == null)
        {
            return 0f;
        }

        if (muscleIndex < 0 || muscleIndex >= sourceHumanPose.muscles.Length)
        {
            return 0f;
        }

        return sourceHumanPose.muscles[muscleIndex];
    }

    /// <summary>
    /// マッスル名からインデックスを取得
    /// </summary>
    public static int GetMuscleIndex(string muscleName)
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            if (HumanTrait.MuscleName[i] == muscleName)
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// インスペクタで読み取り専用として表示するための属性
/// </summary>
public class ReadOnlyInInspectorAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyInInspectorAttribute))]
public class ReadOnlyInInspectorDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = true;
    }

    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label)
    {
        return UnityEditor.EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif
