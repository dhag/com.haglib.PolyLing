// Editor上でPlayせずにHumanoidのMuscle値を編集してプレビューするウィンドウである。
// AnimationModeを併用し、停止時に元状態へ戻せるようにする。
// ヒエラルキーで選択したオブジェクトのAnimatorを自動検出する。

using UnityEditor;
using UnityEngine;

public class MusclePreviewWindow : EditorWindow
{
    private Animator _animator;
    private HumanPoseHandler _handler;
    private HumanPose _pose;

    private int _idxLeftLowerArmStretch = -1;

    // UI用
    private float _value = 0f;
    private bool _preview = false;

    [MenuItem("Tools/Muscle Preview")]
    static void Open() => GetWindow<MusclePreviewWindow>("Muscle Preview");

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        TryBindSelection();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;

        // ウィンドウを閉じた時にプレビュー状態が残らないようにする
        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }

    private void OnSelectionChanged()
    {
        // 選択が変わったらプレビューを停止してから再バインドする
        if (_preview)
            StopPreview();

        TryBindSelection();
        Repaint();
    }

    private void TryBindSelection()
    {
        _animator = null;
        _handler = null;
        _idxLeftLowerArmStretch = -1;
        _value = 0f;

        var go = Selection.activeGameObject;
        if (go == null) return;

        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.GetComponentInParent<Animator>();
        if (animator == null) return;
        if (animator.avatar == null || !animator.avatar.isHuman) return;

        _animator = animator;
        Init();
    }

    private void OnGUI()
    {
        // 選択中オブジェクト表示（読み取り専用）
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Selected", Selection.activeGameObject, typeof(GameObject), true);
            EditorGUILayout.ObjectField("Animator", _animator, typeof(Animator), true);
        }

        if (_animator == null)
        {
            EditorGUILayout.HelpBox("ヒエラルキーでHumanoid Avatarを持つオブジェクトを選択してください", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (!_preview)
            {
                if (GUILayout.Button("Start Preview"))
                {
                    StartPreview();
                }
            }
            else
            {
                if (GUILayout.Button("Stop Preview"))
                {
                    StopPreview();
                }
            }

            using (new EditorGUI.DisabledScope(!_preview || _handler == null || _idxLeftLowerArmStretch < 0))
            {
                // -1..1 の範囲は「muscle空間」の値である（角度ではない）
                float newValue = EditorGUILayout.Slider("LeftLowerArmStretch", _value, -1f, 1f);

                if (!Mathf.Approximately(newValue, _value))
                {
                    _value = newValue;
                    Apply();
                }

                if (GUILayout.Button("Reset (0)"))
                {
                    _value = 0f;
                    Apply();
                }
            }
        }
    }

    private void Init()
    {
        if (_animator == null) return;
        if (_animator.avatar == null || !_animator.avatar.isHuman) return;

        _handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
        _pose = new HumanPose();

        _idxLeftLowerArmStretch = FindMuscleIndex("LeftLowerArmStretch");

        // 現在値を読む
        _handler.GetHumanPose(ref _pose);
        if (_idxLeftLowerArmStretch >= 0)
            _value = _pose.muscles[_idxLeftLowerArmStretch];
    }

    private void StartPreview()
    {
        if (_animator == null) return;

        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        _preview = true;

        // 現在ポーズを読み直してスライダを同期する
        if (_handler != null)
        {
            _handler.GetHumanPose(ref _pose);
            if (_idxLeftLowerArmStretch >= 0)
                _value = _pose.muscles[_idxLeftLowerArmStretch];
        }

        RepaintScene();
    }

    private void StopPreview()
    {
        _preview = false;

        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode(); // これで基本的に元へ戻る

        RepaintScene();
    }

    private void Apply()
    {
        if (!_preview) return;
        if (_handler == null) return;
        if (_idxLeftLowerArmStretch < 0) return;

        // 現在のHumanPoseを取得→変更→適用する
        _handler.GetHumanPose(ref _pose);
        _pose.muscles[_idxLeftLowerArmStretch] = _value;
        _handler.SetHumanPose(ref _pose);

        RepaintScene();
    }

    private static int FindMuscleIndex(string muscleName)
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            if (HumanTrait.MuscleName[i] == muscleName)
                return i;
        }
        return -1;
    }

    private static void RepaintScene()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
}
