// Assets/Editor/MeshCreators/Revolution/RevolutionMeshCreatorWindow.cs
// 回転体メッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// XY平面上で断面の頂点を編集し、Y軸周りに回転させてメッシュを生成

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;
using MeshFactory.Revolution;

public class RevolutionMeshCreatorWindow : MeshCreatorWindowBase<RevolutionParams>
{
    // ================================================================
    // 追加フィールド
    // ================================================================
    private List<Vector2> _profile = new List<Vector2>();
    private RevolutionProfileEditor _profileEditor;
    private Vector2 _leftScrollPos = Vector2.zero;

    // SessionState永続化キー
    private const string SESSION_STATE_KEY = "RevolutionMeshCreatorWindow_State";

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "RevolutionCreator";
    protected override string UndoDescription => "Revolution Parameters";
    protected override float PreviewCameraDistance => 3f; // 動的に計算するので使わない

    protected override RevolutionParams GetDefaultParams() => RevolutionParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static RevolutionMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<RevolutionMeshCreatorWindow>(true, "Create Revolution UnityMesh", true);
        window.minSize = new Vector2(750, 650);
        window.maxSize = new Vector2(1000, 900);
        window._onMeshDataCreated = onMeshDataCreated;

        if (window._profile.Count == 0)
        {
            window.InitializeDefaultProfile();
        }
        window.UpdatePreviewMesh();
        return window;
    }

    protected override void OnInitialize()
    {
        LoadState();

        if (_profile.Count == 0)
        {
            InitializeDefaultProfile();
        }

        // プロファイルエディタ初期化
        _profileEditor = new RevolutionProfileEditor();
        _profileEditor.SetProfile(_profile);
        _profileEditor.OnProfileChanged = () =>
        {
            _params.CurrentPreset = ProfilePreset.Custom;
            UpdatePreviewMesh();
            Repaint();
        };
        _profileEditor.OnRecordUndo = (desc) =>
        {
            _undoHelper?.RecordImmediate(desc);
        };
    }

    protected override void OnCleanup()
    {
        SaveState();
    }

    private void InitializeDefaultProfile()
    {
        _profile.Clear();
        var defaultProfile = RevolutionProfileGenerator.CreateDefault();
        foreach (var p in defaultProfile)
        {
            _profile.Add(p);
        }
    }

    // ================================================================
    // OnGUIオーバーライド（2ペインレイアウト）
    // ================================================================
    protected override void OnGUI()
    {
        _undoHelper?.HandleGUIEvents(Event.current);

        EditorGUILayout.BeginHorizontal();

        // === 左ペイン ===
        EditorGUILayout.BeginVertical(GUILayout.Width(360));
        _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos);
        EditorGUILayout.Space(10);
        DrawParametersUI();
        EditorGUILayout.Space(10);
        DrawProfileEditorSection();
        EditorGUILayout.Space(10);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // === 右ペイン ===
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space(10);
        Draw3DPreview();
        EditorGUILayout.Space(10);
        DrawAutoMergeUI();
        EditorGUILayout.Space(10);
        DrawButtons();
        EditorGUILayout.Space(10);
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField("Revolution Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        _params.RadialSegments = EditorGUILayout.IntSlider("Radial Segments", _params.RadialSegments, 3, 64);
        EditorGUILayout.Space(5);

        _params.CloseTop = EditorGUILayout.Toggle("Close Top", _params.CloseTop);
        _params.CloseBottom = EditorGUILayout.Toggle("Close Bottom", _params.CloseBottom);
        _params.CloseLoop = EditorGUILayout.Toggle("Close Loop", _params.CloseLoop);

        EditorGUI.BeginChangeCheck();
        bool newSpiral = EditorGUILayout.Toggle("Spiral", _params.Spiral);
        if (EditorGUI.EndChangeCheck() && newSpiral && !_params.Spiral)
        {
            _params.CloseTop = true;
            _params.CloseBottom = true;
        }
        _params.Spiral = newSpiral;

        if (_params.Spiral)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                _params.SpiralTurns = EditorGUILayout.IntSlider("Turns", _params.SpiralTurns, 1, 10);
                _params.SpiralPitch = EditorGUILayout.Slider("Pitch", _params.SpiralPitch, -2f, 2f);
            }
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            Vector3 pivot = _params.Pivot;
            pivot.y = EditorGUILayout.Slider("Y", pivot.y, -0.5f, 0.5f);
            _params.Pivot = pivot;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button("Bottom", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0, 0.5f, 0);
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(5);

        _params.FlipY = EditorGUILayout.Toggle("Flip Y (180°)", _params.FlipY);
        _params.FlipZ = EditorGUILayout.Toggle("Flip Z (180°)", _params.FlipZ);

        EditorGUILayout.Space(5);

        // プリセット選択
        DrawPresetSection();

        EndParamChange();
    }

    private void DrawPresetSection()
    {
        EditorGUILayout.LabelField("Preset", EditorStyles.miniBoldLabel);
        ProfilePreset newPreset = (ProfilePreset)EditorGUILayout.EnumPopup(_params.CurrentPreset);
        if (newPreset != _params.CurrentPreset)
        {
            _undoHelper?.RecordImmediate("Change Preset");
            _params.CurrentPreset = newPreset;
            ApplyPreset(newPreset);
            GUI.changed = true;
        }

        // ドーナツ用パラメータ
        if (_params.CurrentPreset == ProfilePreset.Donut)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Donut Settings", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                _params.DonutMajorRadius = EditorGUILayout.Slider("Major Radius", _params.DonutMajorRadius, 0.2f, 2f);
                _params.DonutMinorRadius = EditorGUILayout.Slider("Minor Radius", _params.DonutMinorRadius, 0.05f, 1f);
                _params.DonutTubeSegments = EditorGUILayout.IntSlider("Tube Segments", _params.DonutTubeSegments, 4, 32);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyPreset(ProfilePreset.Donut);
                    GUI.changed = true;
                }
            }
        }

        // 角丸パイプ用パラメータ
        if (_params.CurrentPreset == ProfilePreset.RoundedPipe)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Rounded Pipe Settings", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                _params.PipeInnerRadius = EditorGUILayout.Slider("Inner Radius", _params.PipeInnerRadius, 0.05f, 2f);
                _params.PipeOuterRadius = EditorGUILayout.Slider("Outer Radius", _params.PipeOuterRadius, _params.PipeInnerRadius + 0.01f, 3f);
                _params.PipeHeight = EditorGUILayout.Slider("Height", _params.PipeHeight, 0.1f, 3f);

                float wallThickness = _params.PipeOuterRadius - _params.PipeInnerRadius;
                float maxCornerR = Mathf.Min(wallThickness * 0.5f, _params.PipeHeight * 0.5f);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Inner Corner", EditorStyles.miniLabel);
                _params.PipeInnerCornerRadius = EditorGUILayout.Slider("  Radius", _params.PipeInnerCornerRadius, 0f, maxCornerR);
                _params.PipeInnerCornerSegments = EditorGUILayout.IntSlider("  Segments", _params.PipeInnerCornerSegments, 1, 16);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Outer Corner", EditorStyles.miniLabel);
                _params.PipeOuterCornerRadius = EditorGUILayout.Slider("  Radius", _params.PipeOuterCornerRadius, 0f, maxCornerR);
                _params.PipeOuterCornerSegments = EditorGUILayout.IntSlider("  Segments", _params.PipeOuterCornerSegments, 1, 16);

                if (EditorGUI.EndChangeCheck())
                {
                    ApplyPreset(ProfilePreset.RoundedPipe);
                    GUI.changed = true;
                }
            }
        }
    }

    private void ApplyPreset(ProfilePreset preset)
    {
        if (preset == ProfilePreset.Custom) return;

        _profile.Clear();
        var newProfile = RevolutionProfileGenerator.CreatePreset(preset, ref _params);
        foreach (var p in newProfile)
        {
            _profile.Add(p);
        }
        _profileEditor?.SetProfile(_profile);
        UpdatePreviewMesh();
    }

    // ================================================================
    // プロファイルエディタセクション
    // ================================================================
    private void DrawProfileEditorSection()
    {
        _profileEditor?.DrawEditor(_params.CloseLoop);

        EditorGUILayout.Space(5);

        // CSVボタン
        _profileEditor?.DrawCSVButtons(
            () => LoadProfileFromCSV(),
            () => SaveProfileToCSV()
        );
    }

    private void LoadProfileFromCSV()
    {
        var result = RevolutionCSVHandler.LoadFromCSVWithDialog(_params);
        if (result.Success)
        {
            _profile.Clear();
            foreach (var p in result.Profile)
            {
                _profile.Add(p);
            }
            _params.RadialSegments = result.RadialSegments;
            _params.CloseTop = result.CloseTop;
            _params.CloseBottom = result.CloseBottom;
            _params.CloseLoop = result.CloseLoop;
            _params.Spiral = result.Spiral;
            _params.Pivot = new Vector3(0, result.PivotY, 0);
            _params.SpiralTurns = result.SpiralTurns;
            _params.SpiralPitch = result.SpiralPitch;
            _params.FlipY = result.FlipY;
            _params.FlipZ = result.FlipZ;
            _params.CurrentPreset = ProfilePreset.Custom;

            _profileEditor?.SetProfile(_profile);
            _profileEditor?.SetSelectedIndex(-1);
            UpdatePreviewMesh();
        }
    }

    private void SaveProfileToCSV()
    {
        RevolutionCSVHandler.SaveToCSVWithDialog(_profile, _params);
    }

    // ================================================================
    // 3Dプレビュー
    // ================================================================
    protected override void DrawPreview()
    {
        // Draw3DPreviewで代替
    }

    private void Draw3DPreview()
    {
        EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(300, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // メッシュ情報（常に実行）
        if (_previewMeshData != null)
        {
            EditorGUILayout.LabelField(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount}",
                EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.LabelField(" ", EditorStyles.miniLabel);
        }

        Event e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                _params.RotationY += e.delta.x * 0.5f;
                _params.RotationX += e.delta.y * 0.5f;
                _params.RotationX = Mathf.Clamp(_params.RotationX, -89f, 89f);
                e.Use();
                Repaint();
            }
        }

        if (e.type != EventType.Repaint) return;
        if (_preview == null || _previewMesh == null) return;

        _preview.BeginPreview(rect, GUIStyle.none);
        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        Bounds bounds = _previewMesh.bounds;
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad * 0.5f;
        float dist = maxExtent / Mathf.Tan(fovRad) * 2.0f;

        Quaternion rot = Quaternion.Euler(_params.RotationX, _params.RotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -dist) + bounds.center;

        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(bounds.center);

        if (_previewMaterial != null)
        {
            _preview.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
        }

        _preview.camera.Render();
        GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
    }

    // ================================================================
    // MeshData生成
    // ================================================================
    protected override MeshData GenerateMeshData()
    {
        // プロファイルをパラメータに同期
        _params.Profile = _profile.ToArray();

        return RevolutionMeshGenerator.Generate(_profile, _params);
    }

    // ================================================================
    // SessionState永続化
    // ================================================================
    private void LoadState()
    {
        string json = SessionState.GetString(SESSION_STATE_KEY, "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var data = JsonUtility.FromJson<SessionStateData>(json);
                if (data != null)
                {
                    _params = data.ToParams();
                    if (_params.Profile != null)
                    {
                        _profile = new List<Vector2>(_params.Profile);
                    }
                }
            }
            catch { }
        }
    }

    private void SaveState()
    {
        _params.Profile = _profile.ToArray();
        var data = new SessionStateData(_params);
        string json = JsonUtility.ToJson(data);
        SessionState.SetString(SESSION_STATE_KEY, json);
    }

    [Serializable]
    private class SessionStateData
    {
        public string MeshName;
        public int RadialSegments;
        public bool CloseTop, CloseBottom, CloseLoop, Spiral;
        public int SpiralTurns;
        public float SpiralPitch;
        public float PivotY;
        public bool FlipY, FlipZ;
        public float RotationX, RotationY;
        public int CurrentPreset;
        public float DonutMajorRadius, DonutMinorRadius;
        public int DonutTubeSegments;
        public float PipeInnerRadius, PipeOuterRadius, PipeHeight;
        public float PipeInnerCornerRadius, PipeOuterCornerRadius;
        public int PipeInnerCornerSegments, PipeOuterCornerSegments;
        public float[] ProfileX, ProfileY;

        public SessionStateData() { }

        public SessionStateData(RevolutionParams p)
        {
            MeshName = p.MeshName;
            RadialSegments = p.RadialSegments;
            CloseTop = p.CloseTop;
            CloseBottom = p.CloseBottom;
            CloseLoop = p.CloseLoop;
            Spiral = p.Spiral;
            SpiralTurns = p.SpiralTurns;
            SpiralPitch = p.SpiralPitch;
            PivotY = p.Pivot.y;
            FlipY = p.FlipY;
            FlipZ = p.FlipZ;
            RotationX = p.RotationX;
            RotationY = p.RotationY;
            CurrentPreset = (int)p.CurrentPreset;
            DonutMajorRadius = p.DonutMajorRadius;
            DonutMinorRadius = p.DonutMinorRadius;
            DonutTubeSegments = p.DonutTubeSegments;
            PipeInnerRadius = p.PipeInnerRadius;
            PipeOuterRadius = p.PipeOuterRadius;
            PipeHeight = p.PipeHeight;
            PipeInnerCornerRadius = p.PipeInnerCornerRadius;
            PipeOuterCornerRadius = p.PipeOuterCornerRadius;
            PipeInnerCornerSegments = p.PipeInnerCornerSegments;
            PipeOuterCornerSegments = p.PipeOuterCornerSegments;

            if (p.Profile != null)
            {
                ProfileX = new float[p.Profile.Length];
                ProfileY = new float[p.Profile.Length];
                for (int i = 0; i < p.Profile.Length; i++)
                {
                    ProfileX[i] = p.Profile[i].x;
                    ProfileY[i] = p.Profile[i].y;
                }
            }
        }

        public RevolutionParams ToParams()
        {
            var p = RevolutionParams.Default;
            p.MeshName = MeshName ?? "Revolution";
            p.RadialSegments = RadialSegments > 0 ? RadialSegments : 24;
            p.CloseTop = CloseTop;
            p.CloseBottom = CloseBottom;
            p.CloseLoop = CloseLoop;
            p.Spiral = Spiral;
            p.SpiralTurns = SpiralTurns > 0 ? SpiralTurns : 3;
            p.SpiralPitch = SpiralPitch;
            p.Pivot = new Vector3(0, PivotY, 0);
            p.FlipY = FlipY;
            p.FlipZ = FlipZ;
            p.RotationX = RotationX;
            p.RotationY = RotationY;
            p.CurrentPreset = (ProfilePreset)CurrentPreset;
            p.DonutMajorRadius = DonutMajorRadius > 0 ? DonutMajorRadius : 0.5f;
            p.DonutMinorRadius = DonutMinorRadius > 0 ? DonutMinorRadius : 0.2f;
            p.DonutTubeSegments = DonutTubeSegments > 0 ? DonutTubeSegments : 12;
            p.PipeInnerRadius = PipeInnerRadius > 0 ? PipeInnerRadius : 0.3f;
            p.PipeOuterRadius = PipeOuterRadius > 0 ? PipeOuterRadius : 0.5f;
            p.PipeHeight = PipeHeight > 0 ? PipeHeight : 1f;
            p.PipeInnerCornerRadius = PipeInnerCornerRadius;
            p.PipeOuterCornerRadius = PipeOuterCornerRadius;
            p.PipeInnerCornerSegments = PipeInnerCornerSegments > 0 ? PipeInnerCornerSegments : 4;
            p.PipeOuterCornerSegments = PipeOuterCornerSegments > 0 ? PipeOuterCornerSegments : 4;

            if (ProfileX != null && ProfileY != null && ProfileX.Length == ProfileY.Length)
            {
                p.Profile = new Vector2[ProfileX.Length];
                for (int i = 0; i < ProfileX.Length; i++)
                {
                    p.Profile[i] = new Vector2(ProfileX[i], ProfileY[i]);
                }
            }

            return p;
        }
    }
}