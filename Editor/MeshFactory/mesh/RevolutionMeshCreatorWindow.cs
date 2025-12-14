// Assets/Editor/MeshCreators/RevolutionMeshCreatorWindow.cs
// 回転体メッシュ生成用のサブウインドウ（Undo対応版）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
// XY平面上で断面の頂点を編集し、Y軸周りに回転させてメッシュを生成
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public class RevolutionMeshCreatorWindow : EditorWindow
{
    // ================================================================
    // Undo用スナップショット構造体
    // ================================================================
    private struct RevolutionSnapshot : IEquatable<RevolutionSnapshot>
    {
        // 基本パラメータ
        public string MeshName;
        public int RadialSegments;
        public bool CloseTop, CloseBottom, CloseLoop, Spiral;
        public int SpiralTurns;
        public float SpiralPitch;
        public Vector3 Pivot;
        public bool FlipY, FlipZ;
        public float RotationX, RotationY;

        // プロファイル（頂点リスト）
        public Vector2[] Profile;
        public int SelectedPointIndex;

        // プリセット
        public int CurrentPreset;  // ProfilePreset enum value

        // ドーナツ
        public float DonutMajorRadius, DonutMinorRadius;
        public int DonutTubeSegments;

        // パイプ
        public float PipeInnerRadius, PipeOuterRadius, PipeHeight;
        public float PipeInnerCornerRadius, PipeOuterCornerRadius;
        public int PipeInnerCornerSegments, PipeOuterCornerSegments;

        public bool Equals(RevolutionSnapshot o)
        {
            if (MeshName != o.MeshName) return false;
            if (RadialSegments != o.RadialSegments) return false;
            if (CloseTop != o.CloseTop || CloseBottom != o.CloseBottom) return false;
            if (CloseLoop != o.CloseLoop || Spiral != o.Spiral) return false;
            if (SpiralTurns != o.SpiralTurns) return false;
            if (!Mathf.Approximately(SpiralPitch, o.SpiralPitch)) return false;
            if (Pivot != o.Pivot) return false;
            if (FlipY != o.FlipY || FlipZ != o.FlipZ) return false;
            if (!Mathf.Approximately(RotationX, o.RotationX)) return false;
            if (!Mathf.Approximately(RotationY, o.RotationY)) return false;
            if (CurrentPreset != o.CurrentPreset) return false;
            if (SelectedPointIndex != o.SelectedPointIndex) return false;

            // プロファイル比較
            if (Profile == null && o.Profile == null) return true;
            if (Profile == null || o.Profile == null) return false;
            if (Profile.Length != o.Profile.Length) return false;
            for (int i = 0; i < Profile.Length; i++)
            {
                if (!Mathf.Approximately(Profile[i].x, o.Profile[i].x) ||
                    !Mathf.Approximately(Profile[i].y, o.Profile[i].y))
                    return false;
            }

            // ドーナツ
            if (!Mathf.Approximately(DonutMajorRadius, o.DonutMajorRadius)) return false;
            if (!Mathf.Approximately(DonutMinorRadius, o.DonutMinorRadius)) return false;
            if (DonutTubeSegments != o.DonutTubeSegments) return false;

            // パイプ
            if (!Mathf.Approximately(PipeInnerRadius, o.PipeInnerRadius)) return false;
            if (!Mathf.Approximately(PipeOuterRadius, o.PipeOuterRadius)) return false;
            if (!Mathf.Approximately(PipeHeight, o.PipeHeight)) return false;
            if (!Mathf.Approximately(PipeInnerCornerRadius, o.PipeInnerCornerRadius)) return false;
            if (!Mathf.Approximately(PipeOuterCornerRadius, o.PipeOuterCornerRadius)) return false;
            if (PipeInnerCornerSegments != o.PipeInnerCornerSegments) return false;
            if (PipeOuterCornerSegments != o.PipeOuterCornerSegments) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is RevolutionSnapshot s && Equals(s);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private string _meshName = "Revolution";
    private int _radialSegments = 24;
    private bool _closeTop = true;
    private bool _closeBottom = true;
    private bool _closeLoop = false;
    private bool _spiral = false;
    private Vector3 _pivot = Vector3.zero;
    private bool _flipY = false;
    private bool _flipZ = false;

    // 断面プロファイル
    private List<Vector2> _profile = new List<Vector2>();

    // 断面編集用
    private int _selectedPointIndex = -1;
    private int _dragPointIndex = -1;
    private bool _isDragging = false;
    private Vector2 _dragStartPos;  // ドラッグ開始時の位置

    // プロファイルエディタの表示範囲
    private float _profileZoom = 1f;
    private Vector2 _profileOffset = Vector2.zero;

    // 3Dプレビュー
    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;
    private float _rotationY = 0f;
    private float _rotationX = 20f;

    // スクロール位置
    private Vector2 _leftScrollPos = Vector2.zero;

    private Action<MeshData, string> _onMeshDataCreated;

    // プリセット
    private enum ProfilePreset
    {
        Custom,
        Donut,
        RoundedPipe,
        Vase,
        Goblet,
        Bell,
        Hourglass,
    }
    private ProfilePreset _currentPreset = ProfilePreset.Custom;

    // ドーナツ用パラメータ
    private float _donutMajorRadius = 0.5f;
    private float _donutMinorRadius = 0.2f;
    private int _donutTubeSegments = 12;

    // パイプ用パラメータ
    private float _pipeInnerRadius = 0.3f;
    private float _pipeOuterRadius = 0.5f;
    private float _pipeHeight = 1f;
    private float _pipeInnerCornerRadius = 0.05f;
    private int _pipeInnerCornerSegments = 4;
    private float _pipeOuterCornerRadius = 0.05f;
    private int _pipeOuterCornerSegments = 4;

    // らせん用パラメータ
    private int _spiralTurns = 3;
    private float _spiralPitch = 0.35f;

    // Undo
    private ParameterUndoHelper<RevolutionSnapshot> _undoHelper;

    // SessionState永続化キー
    private const string SESSION_STATE_KEY = "RevolutionMeshCreatorWindow_State";

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static RevolutionMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<RevolutionMeshCreatorWindow>(true, "Create Revolution UnityMesh", true);
        window.minSize = new Vector2(750, 650);
        window.maxSize = new Vector2(1000, 900);
        window._onMeshDataCreated = onMeshDataCreated;
        // 永続化された状態がなければデフォルトプロファイルを初期化
        if (window._profile.Count == 0)
        {
            window.InitializeDefaultProfile();
        }
        window.UpdatePreviewMesh();
        return window;
    }

    private void OnEnable()
    {
        InitPreview();
        InitUndo();
        LoadState();
        if (_profile.Count == 0)
        {
            InitializeDefaultProfile();
        }
        UpdatePreviewMesh();
    }

    private void OnDisable()
    {
        SaveState();
        CleanupPreview();
        _undoHelper?.Dispose();
    }

    private void SaveState()
    {
        try
        {
            var snapshot = CaptureSnapshot();
            string json = JsonUtility.ToJson(new RevolutionStateWrapper(snapshot));
            SessionState.SetString(SESSION_STATE_KEY, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"RevolutionMeshCreatorWindow: Failed to save state: {e.Message}");
        }
    }

    private void LoadState()
    {
        try
        {
            string json = SessionState.GetString(SESSION_STATE_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                var wrapper = JsonUtility.FromJson<RevolutionStateWrapper>(json);
                if (wrapper != null)
                {
                    ApplySnapshot(wrapper.ToSnapshot());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"RevolutionMeshCreatorWindow: Failed to load state: {e.Message}");
        }
    }

    // JsonUtility用のシリアライズ可能なラッパークラス
    [Serializable]
    private class RevolutionStateWrapper
    {
        public string MeshName;
        public int RadialSegments;
        public bool CloseTop, CloseBottom, CloseLoop, Spiral;
        public int SpiralTurns;
        public float SpiralPitch;
        public Vector3 Pivot;
        public bool FlipY, FlipZ;
        public float RotationX, RotationY;
        public Vector2[] Profile;
        public int SelectedPointIndex;
        public int CurrentPreset;
        public float DonutMajorRadius, DonutMinorRadius;
        public int DonutTubeSegments;
        public float PipeInnerRadius, PipeOuterRadius, PipeHeight;
        public float PipeInnerCornerRadius, PipeOuterCornerRadius;
        public int PipeInnerCornerSegments, PipeOuterCornerSegments;

        public RevolutionStateWrapper() { }

        public RevolutionStateWrapper(RevolutionSnapshot s)
        {
            MeshName = s.MeshName;
            RadialSegments = s.RadialSegments;
            CloseTop = s.CloseTop;
            CloseBottom = s.CloseBottom;
            CloseLoop = s.CloseLoop;
            Spiral = s.Spiral;
            SpiralTurns = s.SpiralTurns;
            SpiralPitch = s.SpiralPitch;
            Pivot = s.Pivot;
            FlipY = s.FlipY;
            FlipZ = s.FlipZ;
            RotationX = s.RotationX;
            RotationY = s.RotationY;
            Profile = s.Profile;
            SelectedPointIndex = s.SelectedPointIndex;
            CurrentPreset = s.CurrentPreset;
            DonutMajorRadius = s.DonutMajorRadius;
            DonutMinorRadius = s.DonutMinorRadius;
            DonutTubeSegments = s.DonutTubeSegments;
            PipeInnerRadius = s.PipeInnerRadius;
            PipeOuterRadius = s.PipeOuterRadius;
            PipeHeight = s.PipeHeight;
            PipeInnerCornerRadius = s.PipeInnerCornerRadius;
            PipeOuterCornerRadius = s.PipeOuterCornerRadius;
            PipeInnerCornerSegments = s.PipeInnerCornerSegments;
            PipeOuterCornerSegments = s.PipeOuterCornerSegments;
        }

        public RevolutionSnapshot ToSnapshot()
        {
            return new RevolutionSnapshot
            {
                MeshName = MeshName,
                RadialSegments = RadialSegments,
                CloseTop = CloseTop,
                CloseBottom = CloseBottom,
                CloseLoop = CloseLoop,
                Spiral = Spiral,
                SpiralTurns = SpiralTurns,
                SpiralPitch = SpiralPitch,
                Pivot = Pivot,
                FlipY = FlipY,
                FlipZ = FlipZ,
                RotationX = RotationX,
                RotationY = RotationY,
                Profile = Profile,
                SelectedPointIndex = SelectedPointIndex,
                CurrentPreset = CurrentPreset,
                DonutMajorRadius = DonutMajorRadius,
                DonutMinorRadius = DonutMinorRadius,
                DonutTubeSegments = DonutTubeSegments,
                PipeInnerRadius = PipeInnerRadius,
                PipeOuterRadius = PipeOuterRadius,
                PipeHeight = PipeHeight,
                PipeInnerCornerRadius = PipeInnerCornerRadius,
                PipeOuterCornerRadius = PipeOuterCornerRadius,
                PipeInnerCornerSegments = PipeInnerCornerSegments,
                PipeOuterCornerSegments = PipeOuterCornerSegments,
            };
        }
    }

    private void InitUndo()
    {
        _undoHelper = new ParameterUndoHelper<RevolutionSnapshot>(
            "RevolutionCreator",
            "Revolution Parameters",
            () => CaptureSnapshot(),
            (s) => { ApplySnapshot(s); UpdatePreviewMesh(); },
            () => Repaint()
        );
    }

    private RevolutionSnapshot CaptureSnapshot()
    {
        return new RevolutionSnapshot
        {
            MeshName = _meshName,
            RadialSegments = _radialSegments,
            CloseTop = _closeTop,
            CloseBottom = _closeBottom,
            CloseLoop = _closeLoop,
            Spiral = _spiral,
            SpiralTurns = _spiralTurns,
            SpiralPitch = _spiralPitch,
            Pivot = _pivot,
            FlipY = _flipY,
            FlipZ = _flipZ,
            RotationX = _rotationX,
            RotationY = _rotationY,
            Profile = _profile.ToArray(),
            SelectedPointIndex = _selectedPointIndex,
            CurrentPreset = (int)_currentPreset,
            DonutMajorRadius = _donutMajorRadius,
            DonutMinorRadius = _donutMinorRadius,
            DonutTubeSegments = _donutTubeSegments,
            PipeInnerRadius = _pipeInnerRadius,
            PipeOuterRadius = _pipeOuterRadius,
            PipeHeight = _pipeHeight,
            PipeInnerCornerRadius = _pipeInnerCornerRadius,
            PipeOuterCornerRadius = _pipeOuterCornerRadius,
            PipeInnerCornerSegments = _pipeInnerCornerSegments,
            PipeOuterCornerSegments = _pipeOuterCornerSegments,
        };
    }

    private void ApplySnapshot(RevolutionSnapshot s)
    {
        _meshName = s.MeshName;
        _radialSegments = s.RadialSegments;
        _closeTop = s.CloseTop;
        _closeBottom = s.CloseBottom;
        _closeLoop = s.CloseLoop;
        _spiral = s.Spiral;
        _spiralTurns = s.SpiralTurns;
        _spiralPitch = s.SpiralPitch;
        _pivot = s.Pivot;
        _flipY = s.FlipY;
        _flipZ = s.FlipZ;
        _rotationX = s.RotationX;
        _rotationY = s.RotationY;
        _profile = s.Profile != null ? new List<Vector2>(s.Profile) : new List<Vector2>();
        _selectedPointIndex = s.SelectedPointIndex;
        _currentPreset = (ProfilePreset)s.CurrentPreset;
        _donutMajorRadius = s.DonutMajorRadius;
        _donutMinorRadius = s.DonutMinorRadius;
        _donutTubeSegments = s.DonutTubeSegments;
        _pipeInnerRadius = s.PipeInnerRadius;
        _pipeOuterRadius = s.PipeOuterRadius;
        _pipeHeight = s.PipeHeight;
        _pipeInnerCornerRadius = s.PipeInnerCornerRadius;
        _pipeOuterCornerRadius = s.PipeOuterCornerRadius;
        _pipeInnerCornerSegments = s.PipeInnerCornerSegments;
        _pipeOuterCornerSegments = s.PipeOuterCornerSegments;
    }

    private void InitPreview()
    {
        _preview = new PreviewRenderUtility();
        _preview.cameraFieldOfView = 30f;
        _preview.camera.nearClipPlane = 0.01f;
        _preview.camera.farClipPlane = 100f;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _previewMaterial = new Material(shader);
            _previewMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
        }
    }

    private void CleanupPreview()
    {
        if (_preview != null)
        {
            _preview.Cleanup();
            _preview = null;
        }

        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }
    }

    private void InitializeDefaultProfile()
    {
        _profile.Clear();
        _profile.Add(new Vector2(0.5f, 0f));
        _profile.Add(new Vector2(0.5f, 0.5f));
        _profile.Add(new Vector2(0.5f, 1f));
    }

    // ================================================================
    // GUI
    // ================================================================
    private void OnGUI()
    {
        _undoHelper?.HandleGUIEvents(Event.current);

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        // 左側：パラメータと断面エディタ
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(350)))
        {
            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos, GUILayout.ExpandHeight(true));
            DrawParameters();
            EditorGUILayout.Space(10);
            DrawProfileEditor();
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(10);

        // 右側：3Dプレビュー
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
        {
            Draw3DPreview();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        DrawButtons();
    }

    private void DrawParameters()
    {
        EditorGUILayout.LabelField("Revolution Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _meshName = EditorGUILayout.TextField("Name", _meshName);

        EditorGUILayout.Space(5);

        _radialSegments = EditorGUILayout.IntSlider("Radial Segments", _radialSegments, 3, 64);

        EditorGUILayout.Space(5);

        _closeTop = EditorGUILayout.Toggle("Close Top", _closeTop);
        _closeBottom = EditorGUILayout.Toggle("Close Bottom", _closeBottom);
        _closeLoop = EditorGUILayout.Toggle("Close Loop", _closeLoop);

        EditorGUI.BeginChangeCheck();
        bool newSpiral = EditorGUILayout.Toggle("Spiral", _spiral);
        if (EditorGUI.EndChangeCheck() && newSpiral && !_spiral)
        {
            _closeTop = true;
            _closeBottom = true;
        }
        _spiral = newSpiral;

        if (_spiral)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                _spiralTurns = EditorGUILayout.IntSlider("Turns", _spiralTurns, 1, 10);
                _spiralPitch = EditorGUILayout.Slider("Pitch", _spiralPitch, -2f, 2f);
            }
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _pivot.y = EditorGUILayout.Slider("Y", _pivot.y, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60)))
            {
                _pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button("Bottom", GUILayout.Width(60)))
            {
                _pivot = new Vector3(0, 0.5f, 0);
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(5);

        _flipY = EditorGUILayout.Toggle("Flip Y (180°)", _flipY);
        _flipZ = EditorGUILayout.Toggle("Flip Z (180°)", _flipZ);

        EditorGUILayout.Space(5);

        // プリセット選択
        EditorGUILayout.LabelField("Preset", EditorStyles.miniBoldLabel);
        ProfilePreset newPreset = (ProfilePreset)EditorGUILayout.EnumPopup(_currentPreset);
        if (newPreset != _currentPreset)
        {
            _undoHelper?.RecordImmediate("Change Preset");
            _currentPreset = newPreset;
            ApplyPreset(newPreset);
            GUI.changed = true;
        }

        // ドーナツ用パラメータ
        if (_currentPreset == ProfilePreset.Donut)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Donut Settings", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                _donutMajorRadius = EditorGUILayout.Slider("Major Radius", _donutMajorRadius, 0.2f, 2f);
                _donutMinorRadius = EditorGUILayout.Slider("Minor Radius", _donutMinorRadius, 0.05f, 1f);
                _donutTubeSegments = EditorGUILayout.IntSlider("Tube Segments", _donutTubeSegments, 4, 32);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyDonutProfile();
                    GUI.changed = true;
                }
            }
        }

        // 角丸パイプ用パラメータ
        if (_currentPreset == ProfilePreset.RoundedPipe)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Rounded Pipe Settings", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                _pipeInnerRadius = EditorGUILayout.Slider("Inner Radius", _pipeInnerRadius, 0.05f, 2f);
                _pipeOuterRadius = EditorGUILayout.Slider("Outer Radius", _pipeOuterRadius, _pipeInnerRadius + 0.01f, 3f);
                _pipeHeight = EditorGUILayout.Slider("Height", _pipeHeight, 0.1f, 3f);

                float wallThickness = _pipeOuterRadius - _pipeInnerRadius;
                float maxInnerR = Mathf.Min(wallThickness * 0.5f, _pipeHeight * 0.5f);
                float maxOuterR = Mathf.Min(wallThickness * 0.5f, _pipeHeight * 0.5f);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Inner Corner", EditorStyles.miniLabel);
                _pipeInnerCornerRadius = EditorGUILayout.Slider("  Radius", _pipeInnerCornerRadius, 0f, maxInnerR);
                _pipeInnerCornerSegments = EditorGUILayout.IntSlider("  Segments", _pipeInnerCornerSegments, 1, 16);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Outer Corner", EditorStyles.miniLabel);
                _pipeOuterCornerRadius = EditorGUILayout.Slider("  Radius", _pipeOuterCornerRadius, 0f, maxOuterR);
                _pipeOuterCornerSegments = EditorGUILayout.IntSlider("  Segments", _pipeOuterCornerSegments, 1, 16);

                if (EditorGUI.EndChangeCheck())
                {
                    ApplyRoundedPipeProfile();
                    GUI.changed = true;
                }
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreviewMesh();
        }
    }

    private void DrawProfileEditor()
    {
        EditorGUILayout.LabelField("Profile Editor (XY Plane)", EditorStyles.boldLabel);

        // プロファイル編集ボタン（1行目）
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Point"))
        {
            _undoHelper?.RecordImmediate("Add Profile Point");
            AddProfilePoint();
        }
        if (GUILayout.Button("Remove Point") && _profile.Count > 2)
        {
            _undoHelper?.RecordImmediate("Remove Profile Point");
            RemoveSelectedPoint();
        }
        if (GUILayout.Button("Reset"))
        {
            _undoHelper?.RecordImmediate("Reset Profile");
            InitializeDefaultProfile();
            _currentPreset = ProfilePreset.Custom;
            UpdatePreviewMesh();
        }
        EditorGUILayout.EndHorizontal();

        // CSV読み書きボタン（2行目）
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load CSV..."))
        {
            _undoHelper?.RecordImmediate("Load Profile CSV");
            LoadProfileFromCSV();
        }
        if (GUILayout.Button("Save CSV..."))
        {
            SaveProfileToCSV();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 断面エディタ領域
        Rect editorRect = GUILayoutUtility.GetRect(340, 300, GUILayout.ExpandWidth(true));
        DrawProfileEditorArea(editorRect);

        EditorGUILayout.Space(5);

        // 選択中の点の座標編集
        if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField($"Point {_selectedPointIndex}", EditorStyles.miniBoldLabel);

            Vector2 point = _profile[_selectedPointIndex];
            float newX = EditorGUILayout.Slider("Radius (X)", point.x, 0f, 2f);
            float newY = EditorGUILayout.Slider("Height (Y)", point.y, -1f, 2f);

            if (EditorGUI.EndChangeCheck())
            {
                _profile[_selectedPointIndex] = new Vector2(newX, newY);
                _currentPreset = ProfilePreset.Custom;
                UpdatePreviewMesh();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("クリックで点を選択、ドラッグで移動", MessageType.Info);
        }
    }

    private void DrawProfileEditorArea(Rect rect)
    {
        // 背景
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f, 1f));

        // グリッド描画
        DrawProfileGrid(rect);

        // プロファイル線描画
        DrawProfileLines(rect);

        // 頂点描画
        DrawProfilePoints(rect);

        // 入力処理
        HandleProfileEditorInput(rect);
    }

    private void DrawProfileGrid(Rect rect)
    {
        Handles.color = new Color(0.3f, 0.3f, 0.35f, 1f);

        // 0.5刻みのグリッド
        for (float x = 0f; x <= 2f; x += 0.5f)
        {
            Vector2 p0 = ProfileToScreen(new Vector2(x, -1f), rect);
            Vector2 p1 = ProfileToScreen(new Vector2(x, 2f), rect);
            Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
        }

        for (float y = -1f; y <= 2f; y += 0.5f)
        {
            Vector2 p0 = ProfileToScreen(new Vector2(0f, y), rect);
            Vector2 p1 = ProfileToScreen(new Vector2(2f, y), rect);
            Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
        }

        // 軸線
        Handles.color = new Color(0.5f, 0.5f, 0.55f, 1f);
        Vector2 axisY0 = ProfileToScreen(new Vector2(0f, -1f), rect);
        Vector2 axisY1 = ProfileToScreen(new Vector2(0f, 2f), rect);
        Handles.DrawLine(new Vector3(axisY0.x, axisY0.y), new Vector3(axisY1.x, axisY1.y));

        Vector2 axisX0 = ProfileToScreen(new Vector2(0f, 0f), rect);
        Vector2 axisX1 = ProfileToScreen(new Vector2(2f, 0f), rect);
        Handles.DrawLine(new Vector3(axisX0.x, axisX0.y), new Vector3(axisX1.x, axisX1.y));
    }

    private void DrawProfileLines(Rect rect)
    {
        if (_profile.Count < 2) return;

        Handles.color = Color.cyan;

        for (int i = 0; i < _profile.Count - 1; i++)
        {
            Vector2 p0 = ProfileToScreen(_profile[i], rect);
            Vector2 p1 = ProfileToScreen(_profile[i + 1], rect);
            Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
        }

        // 閉じたループの場合、最後と最初を結ぶ
        if (_closeLoop && _profile.Count >= 3)
        {
            Vector2 pLast = ProfileToScreen(_profile[_profile.Count - 1], rect);
            Vector2 pFirst = ProfileToScreen(_profile[0], rect);
            Handles.DrawLine(new Vector3(pLast.x, pLast.y), new Vector3(pFirst.x, pFirst.y));
        }
    }

    private void DrawProfilePoints(Rect rect)
    {
        for (int i = 0; i < _profile.Count; i++)
        {
            Vector2 screenPos = ProfileToScreen(_profile[i], rect);

            // 選択状態で色を変える
            Color pointColor = (i == _selectedPointIndex) ? Color.yellow : Color.white;

            // 点を描画
            Rect pointRect = new Rect(screenPos.x - 5, screenPos.y - 5, 10, 10);
            EditorGUI.DrawRect(pointRect, pointColor);

            // インデックス表示
            GUI.Label(new Rect(screenPos.x + 8, screenPos.y - 8, 30, 20), i.ToString(), EditorStyles.miniLabel);
        }
    }

    private void HandleProfileEditorInput(Rect rect)
    {
        Event e = Event.current;

        if (!rect.Contains(e.mousePosition))
        {
            return;
        }

        Vector2 profilePos = ScreenToProfile(e.mousePosition, rect);

        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0)
                {
                    int closestIndex = FindClosestProfilePoint(e.mousePosition, rect, 15f);

                    if (closestIndex >= 0)
                    {
                        _selectedPointIndex = closestIndex;
                        _dragPointIndex = closestIndex;
                        _isDragging = true;
                        _dragStartPos = _profile[closestIndex];  // ドラッグ開始位置を記録
                        e.Use();
                    }
                    else
                    {
                        _selectedPointIndex = -1;
                    }
                    Repaint();
                }
                break;

            case EventType.MouseDrag:
                if (_isDragging && _dragPointIndex >= 0 && e.button == 0)
                {
                    profilePos.x = Mathf.Max(0, profilePos.x);
                    _profile[_dragPointIndex] = profilePos;
                    _currentPreset = ProfilePreset.Custom;
                    UpdatePreviewMesh();
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                if (_isDragging && e.button == 0)
                {
                    // ドラッグ終了時にUndoを記録（開始位置と終了位置が異なる場合）
                    if (_dragPointIndex >= 0 && _dragStartPos != _profile[_dragPointIndex])
                    {
                        _undoHelper?.RecordImmediate("Move Profile Point");
                    }
                    _isDragging = false;
                    _dragPointIndex = -1;
                    e.Use();
                }
                break;

            case EventType.ScrollWheel:
                // ズーム
                float zoomDelta = -e.delta.y * 0.05f;
                _profileZoom = Mathf.Clamp(_profileZoom + zoomDelta, 0.5f, 3f);
                e.Use();
                Repaint();
                break;
        }
    }

    private int FindClosestProfilePoint(Vector2 screenPos, Rect rect, float maxDist)
    {
        int closest = -1;
        float closestDist = maxDist;

        for (int i = 0; i < _profile.Count; i++)
        {
            Vector2 pointScreen = ProfileToScreen(_profile[i], rect);
            float dist = Vector2.Distance(screenPos, pointScreen);

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = i;
            }
        }

        return closest;
    }

    private Vector2 ProfileToScreen(Vector2 profilePos, Rect rect)
    {
        float profileRangeX = 2f;
        float profileRangeY = 3f;

        float scale = Mathf.Min(rect.width / profileRangeX, rect.height / profileRangeY) * _profileZoom;

        float usedWidth = profileRangeX * scale;
        float usedHeight = profileRangeY * scale;
        float offsetX = (rect.width - usedWidth) * 0.5f;
        float offsetY = (rect.height - usedHeight) * 0.5f;

        float screenX = rect.xMin + offsetX + profilePos.x * scale + _profileOffset.x;
        float screenY = rect.yMax - offsetY - (profilePos.y + 1f) * scale + _profileOffset.y;

        return new Vector2(screenX, screenY);
    }

    private Vector2 ScreenToProfile(Vector2 screenPos, Rect rect)
    {
        float profileRangeX = 2f;
        float profileRangeY = 3f;

        float scale = Mathf.Min(rect.width / profileRangeX, rect.height / profileRangeY) * _profileZoom;

        float usedWidth = profileRangeX * scale;
        float usedHeight = profileRangeY * scale;
        float offsetX = (rect.width - usedWidth) * 0.5f;
        float offsetY = (rect.height - usedHeight) * 0.5f;

        float profileX = (screenPos.x - rect.xMin - offsetX - _profileOffset.x) / scale;
        float profileY = (rect.yMax - offsetY - screenPos.y + _profileOffset.y) / scale - 1f;

        return new Vector2(profileX, profileY);
    }

    private void AddProfilePoint()
    {
        Vector2 newPoint;
        int insertIndex;

        if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count - 1)
        {
            newPoint = (_profile[_selectedPointIndex] + _profile[_selectedPointIndex + 1]) * 0.5f;
            insertIndex = _selectedPointIndex + 1;
        }
        else if (_profile.Count >= 2)
        {
            Vector2 lastDir = _profile[_profile.Count - 1] - _profile[_profile.Count - 2];
            newPoint = _profile[_profile.Count - 1] + lastDir.normalized * 0.2f;
            insertIndex = _profile.Count;
        }
        else
        {
            newPoint = new Vector2(0.5f, 0.5f);
            insertIndex = _profile.Count;
        }

        _profile.Insert(insertIndex, newPoint);
        _selectedPointIndex = insertIndex;
        _currentPreset = ProfilePreset.Custom;
        UpdatePreviewMesh();
    }

    private void RemoveSelectedPoint()
    {
        if (_selectedPointIndex >= 0 && _selectedPointIndex < _profile.Count && _profile.Count > 2)
        {
            _profile.RemoveAt(_selectedPointIndex);
            _selectedPointIndex = Mathf.Min(_selectedPointIndex, _profile.Count - 1);
            _currentPreset = ProfilePreset.Custom;
            UpdatePreviewMesh();
        }
    }

    // ================================================================
    // プリセット
    // ================================================================
    private void ApplyPreset(ProfilePreset preset)
    {
        switch (preset)
        {
            case ProfilePreset.Donut:
                ApplyDonutProfile();
                break;
            case ProfilePreset.RoundedPipe:
                ApplyRoundedPipeProfile();
                break;
            case ProfilePreset.Vase:
                ApplyVaseProfile();
                break;
            case ProfilePreset.Goblet:
                ApplyGobletProfile();
                break;
            case ProfilePreset.Bell:
                ApplyBellProfile();
                break;
            case ProfilePreset.Hourglass:
                ApplyHourglassProfile();
                break;
        }
    }

    private void ApplyDonutProfile()
    {
        _profile.Clear();

        float centerY = _donutMinorRadius + 0.1f;

        for (int i = 0; i < _donutTubeSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / _donutTubeSegments;
            float x = _donutMajorRadius + _donutMinorRadius * Mathf.Cos(angle);
            float y = centerY + _donutMinorRadius * Mathf.Sin(angle);
            _profile.Add(new Vector2(x, y));
        }

        _closeLoop = true;
        UpdatePreviewMesh();
    }

    private void ApplyRoundedPipeProfile()
    {
        _profile.Clear();

        float halfH = _pipeHeight * 0.5f;
        float innerR = _pipeInnerRadius;
        float outerR = _pipeOuterRadius;
        float iR = _pipeInnerCornerRadius;
        int iSeg = _pipeInnerCornerSegments;
        float oR = _pipeOuterCornerRadius;
        int oSeg = _pipeOuterCornerSegments;

        // 内側下角
        if (iR > 0.001f && iSeg > 0)
        {
            Vector2 center = new Vector2(innerR + iR, -halfH + iR);
            for (int i = 0; i <= iSeg; i++)
            {
                float t = (float)i / iSeg;
                float angle = Mathf.PI + t * Mathf.PI * 0.5f;
                float x = center.x + iR * Mathf.Cos(angle);
                float y = center.y + iR * Mathf.Sin(angle);
                _profile.Add(new Vector2(x, y));
            }
        }
        else
        {
            _profile.Add(new Vector2(innerR, -halfH));
        }

        // 外側下角
        if (oR > 0.001f && oSeg > 0)
        {
            Vector2 center = new Vector2(outerR - oR, -halfH + oR);
            for (int i = 0; i <= oSeg; i++)
            {
                float t = (float)i / oSeg;
                float angle = Mathf.PI * 1.5f + t * Mathf.PI * 0.5f;
                float x = center.x + oR * Mathf.Cos(angle);
                float y = center.y + oR * Mathf.Sin(angle);
                _profile.Add(new Vector2(x, y));
            }
        }
        else
        {
            _profile.Add(new Vector2(outerR, -halfH));
        }

        // 外側上角
        if (oR > 0.001f && oSeg > 0)
        {
            Vector2 center = new Vector2(outerR - oR, halfH - oR);
            for (int i = 0; i <= oSeg; i++)
            {
                float t = (float)i / oSeg;
                float angle = t * Mathf.PI * 0.5f;
                float x = center.x + oR * Mathf.Cos(angle);
                float y = center.y + oR * Mathf.Sin(angle);
                _profile.Add(new Vector2(x, y));
            }
        }
        else
        {
            _profile.Add(new Vector2(outerR, halfH));
        }

        // 内側上角
        if (iR > 0.001f && iSeg > 0)
        {
            Vector2 center = new Vector2(innerR + iR, halfH - iR);
            for (int i = 0; i <= iSeg; i++)
            {
                float t = (float)i / iSeg;
                float angle = Mathf.PI * 0.5f + t * Mathf.PI * 0.5f;
                float x = center.x + iR * Mathf.Cos(angle);
                float y = center.y + iR * Mathf.Sin(angle);
                _profile.Add(new Vector2(x, y));
            }
        }
        else
        {
            _profile.Add(new Vector2(innerR, halfH));
        }

        _closeLoop = true;
        UpdatePreviewMesh();
    }

    private void ApplyVaseProfile()
    {
        _profile.Clear();
        _profile.Add(new Vector2(0.3f, 0f));
        _profile.Add(new Vector2(0.5f, 0.1f));
        _profile.Add(new Vector2(0.6f, 0.3f));
        _profile.Add(new Vector2(0.5f, 0.6f));
        _profile.Add(new Vector2(0.3f, 0.8f));
        _profile.Add(new Vector2(0.25f, 0.9f));
        _profile.Add(new Vector2(0.3f, 1f));
        _closeLoop = false;
        UpdatePreviewMesh();
    }

    private void ApplyGobletProfile()
    {
        _profile.Clear();
        _profile.Add(new Vector2(0.3f, 0f));
        _profile.Add(new Vector2(0.35f, 0.02f));
        _profile.Add(new Vector2(0.08f, 0.1f));
        _profile.Add(new Vector2(0.08f, 0.5f));
        _profile.Add(new Vector2(0.15f, 0.55f));
        _profile.Add(new Vector2(0.4f, 0.7f));
        _profile.Add(new Vector2(0.45f, 1f));
        _closeLoop = false;
        UpdatePreviewMesh();
    }

    private void ApplyBellProfile()
    {
        _profile.Clear();
        _profile.Add(new Vector2(0.5f, 0f));
        _profile.Add(new Vector2(0.45f, 0.1f));
        _profile.Add(new Vector2(0.35f, 0.3f));
        _profile.Add(new Vector2(0.2f, 0.6f));
        _profile.Add(new Vector2(0.1f, 0.85f));
        _profile.Add(new Vector2(0.05f, 1f));
        _closeLoop = false;
        UpdatePreviewMesh();
    }

    private void ApplyHourglassProfile()
    {
        _profile.Clear();
        _profile.Add(new Vector2(0.4f, 0f));
        _profile.Add(new Vector2(0.35f, 0.15f));
        _profile.Add(new Vector2(0.15f, 0.4f));
        _profile.Add(new Vector2(0.1f, 0.5f));
        _profile.Add(new Vector2(0.15f, 0.6f));
        _profile.Add(new Vector2(0.35f, 0.85f));
        _profile.Add(new Vector2(0.4f, 1f));
        _closeLoop = false;
        UpdatePreviewMesh();
    }

    // ================================================================
    // CSV
    // ================================================================
    private void LoadProfileFromCSV()
    {
        string path = EditorUtility.OpenFilePanel("Load Profile CSV", "", "csv");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var lines = File.ReadAllLines(path);
            var newProfile = new List<Vector2>();

            int radialSegments = _radialSegments;
            bool closeTop = _closeTop;
            bool closeBottom = _closeBottom;
            bool closeLoop = _closeLoop;
            bool spiral = _spiral;
            float pivotY = _pivot.y;
            int spiralTurns = _spiralTurns;
            float spiralPitch = _spiralPitch;
            bool flipY = _flipY;
            bool flipZ = _flipZ;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                    continue;

                if (trimmed.StartsWith("$"))
                {
                    ParseParameter(trimmed.Substring(1).Trim(),
                        ref radialSegments, ref closeTop, ref closeBottom, ref closeLoop,
                        ref spiral, ref pivotY, ref spiralTurns, ref spiralPitch,
                        ref flipY, ref flipZ);
                    continue;
                }

                if (trimmed.ToLower().Contains("x") && trimmed.ToLower().Contains("y"))
                    continue;

                string[] parts = trimmed.Split(',');
                if (parts.Length >= 2)
                {
                    if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float y))
                    {
                        newProfile.Add(new Vector2(x, y));
                    }
                }
            }

            if (newProfile.Count >= 2)
            {
                _profile = newProfile;
                _radialSegments = radialSegments;
                _closeTop = closeTop;
                _closeBottom = closeBottom;
                _closeLoop = closeLoop;
                _spiral = spiral;
                _pivot.y = pivotY;
                _spiralTurns = spiralTurns;
                _spiralPitch = spiralPitch;
                _flipY = flipY;
                _flipZ = flipZ;
                _currentPreset = ProfilePreset.Custom;
                _selectedPointIndex = -1;
                UpdatePreviewMesh();
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"CSV読み込みエラー: {e.Message}", "OK");
        }
    }

    private void ParseParameter(string paramLine,
        ref int radialSegments, ref bool closeTop, ref bool closeBottom, ref bool closeLoop,
        ref bool spiral, ref float pivotY, ref int spiralTurns, ref float spiralPitch,
        ref bool flipY, ref bool flipZ)
    {
        string[] parts = paramLine.Split('=');
        if (parts.Length != 2) return;

        string key = parts[0].Trim().ToLower();
        string value = parts[1].Trim();

        switch (key)
        {
            case "radialsegments":
                if (int.TryParse(value, out int rs)) radialSegments = rs;
                break;
            case "closetop":
                if (bool.TryParse(value, out bool ct)) closeTop = ct;
                break;
            case "closebottom":
                if (bool.TryParse(value, out bool cb)) closeBottom = cb;
                break;
            case "closeloop":
                if (bool.TryParse(value, out bool cl)) closeLoop = cl;
                break;
            case "spiral":
                if (bool.TryParse(value, out bool sp)) spiral = sp;
                break;
            case "pivoty":
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float py)) pivotY = py;
                break;
            case "spiralturns":
                if (int.TryParse(value, out int st)) spiralTurns = st;
                break;
            case "spiralpitch":
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float spt)) spiralPitch = spt;
                break;
            case "flipy":
                if (bool.TryParse(value, out bool fy)) flipY = fy;
                break;
            case "flipz":
                if (bool.TryParse(value, out bool fz)) flipZ = fz;
                break;
        }
    }

    private void SaveProfileToCSV()
    {
        string path = EditorUtility.SaveFilePanel("Save Profile CSV", "", "profile.csv", "csv");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# Revolution Profile");
                writer.WriteLine($"$radialSegments={_radialSegments}");
                writer.WriteLine($"$closeTop={_closeTop}");
                writer.WriteLine($"$closeBottom={_closeBottom}");
                writer.WriteLine($"$closeLoop={_closeLoop}");
                writer.WriteLine($"$spiral={_spiral}");
                writer.WriteLine($"$pivotY={_pivot.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                writer.WriteLine($"$spiralTurns={_spiralTurns}");
                writer.WriteLine($"$spiralPitch={_spiralPitch.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                writer.WriteLine($"$flipY={_flipY}");
                writer.WriteLine($"$flipZ={_flipZ}");
                writer.WriteLine("X,Y");

                foreach (var p in _profile)
                {
                    writer.WriteLine($"{p.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"CSV保存エラー: {e.Message}", "OK");
        }
    }

    // ================================================================
    // 3Dプレビュー
    // ================================================================
    private void Draw3DPreview()
    {
        EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(300, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (_preview == null || _previewMesh == null)
            return;

        Event e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                _rotationY += e.delta.x * 0.5f;
                _rotationX += e.delta.y * 0.5f;
                _rotationX = Mathf.Clamp(_rotationX, -89f, 89f);
                e.Use();
                Repaint();
            }
        }

        if (e.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        Bounds bounds = _previewMesh.bounds;
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

        // FOVを考慮した距離計算（オブジェクト全体が画面に収まるように）
        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad * 0.5f;
        float dist = maxExtent / Mathf.Tan(fovRad) * 2.0f; // 2.0fで十分なマージン

        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -dist) + bounds.center;

        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(bounds.center);

        if (_previewMaterial != null)
        {
            _preview.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
        }

        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
    }

    private void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Create", GUILayout.Height(30)))
        {
            CreateMesh();
        }

        if (GUILayout.Button("Cancel", GUILayout.Height(30)))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();

        if (_previewMeshData != null)
        {
            EditorGUILayout.Space(5);
            int quadCount = _previewMeshData.Faces.Count(f => f.IsQuad);
            int triCount = _previewMeshData.Faces.Count(f => f.IsTriangle);
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount} (Quad:{quadCount}, Tri:{triCount}), Profile: {_profile.Count}",
                MessageType.None);
        }
    }

    private void UpdatePreviewMesh()
    {
        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
        }

        if (_profile.Count >= 2)
        {
            if (_spiral)
            {
                _previewMeshData = GenerateSpiralMeshData();
            }
            else
            {
                _previewMeshData = GenerateRevolutionMeshData(_profile, _radialSegments, _closeTop, _closeBottom, _closeLoop, _pivot);
            }
            ApplyAxisFlip(_previewMeshData);
            _previewMesh = _previewMeshData.ToUnityMesh();
        }
        Repaint();
    }

    private void CreateMesh()
    {
        if (_profile.Count < 2)
        {
            EditorUtility.DisplayDialog("Error", "プロファイルには最低2点必要です", "OK");
            return;
        }

        MeshData meshData;
        if (_spiral)
        {
            meshData = GenerateSpiralMeshData();
        }
        else
        {
            meshData = GenerateRevolutionMeshData(_profile, _radialSegments, _closeTop, _closeBottom, _closeLoop, _pivot);
        }
        ApplyAxisFlip(meshData);
        meshData.Name = _meshName;

        _onMeshDataCreated?.Invoke(meshData, _meshName);
        Close();
    }

    private void ApplyAxisFlip(MeshData meshData)
    {
        if (!_flipY && !_flipZ) return;

        foreach (var vertex in meshData.Vertices)
        {
            Vector3 pos = vertex.Position;

            if (_flipY)
            {
                pos = new Vector3(-pos.x, pos.y, -pos.z);
            }

            if (_flipZ)
            {
                pos = new Vector3(-pos.x, -pos.y, pos.z);
            }

            vertex.Position = pos;

            // 法線も変換
            for (int i = 0; i < vertex.Normals.Count; i++)
            {
                Vector3 norm = vertex.Normals[i];
                if (_flipY)
                {
                    norm = new Vector3(-norm.x, norm.y, -norm.z);
                }
                if (_flipZ)
                {
                    norm = new Vector3(-norm.x, -norm.y, norm.z);
                }
                vertex.Normals[i] = norm;
            }
        }
    }

    // ================================================================
    // MeshData生成（四角形ベース）
    // ================================================================
    private MeshData GenerateRevolutionMeshData(List<Vector2> profile, int radialSeg, bool closeTop, bool closeBottom, bool closeLoop, Vector3 pivot)
    {
        var md = new MeshData("Revolution");

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (var p in profile)
        {
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }
        float height = maxY - minY;
        if (height < 0.001f) height = 1f;

        Vector3 pivotOffset = new Vector3(0, pivot.y * height + (minY + maxY) * 0.5f, 0);

        float angleStep = 2f * Mathf.PI / radialSeg;
        int profileCount = profile.Count;
        int cols = radialSeg + 1;

        // 側面の頂点
        for (int p = 0; p < profileCount; p++)
        {
            float radius = profile[p].x;
            float y = profile[p].y;
            float v = (y - minY) / height;

            Vector2 tangent;
            if (closeLoop)
            {
                int prevIdx = (p - 1 + profileCount) % profileCount;
                int nextIdx = (p + 1) % profileCount;
                tangent = profile[nextIdx] - profile[prevIdx];
            }
            else
            {
                if (p == 0)
                    tangent = profile[1] - profile[0];
                else if (p == profileCount - 1)
                    tangent = profile[p] - profile[p - 1];
                else
                    tangent = profile[p + 1] - profile[p - 1];
            }
            tangent.Normalize();

            Vector2 normal2D = new Vector2(tangent.y, -tangent.x);

            for (int r = 0; r <= radialSeg; r++)
            {
                float angle = r * angleStep;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
                Vector3 normal = new Vector3(cos * normal2D.x, normal2D.y, sin * normal2D.x).normalized;
                Vector2 uv = new Vector2((float)r / radialSeg, v);

                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        // 側面の四角形
        int loopCount = closeLoop ? profileCount : profileCount - 1;
        for (int p = 0; p < loopCount; p++)
        {
            int nextP = (p + 1) % profileCount;
            for (int r = 0; r < radialSeg; r++)
            {
                int i0 = p * cols + r;
                int i1 = i0 + 1;
                int i2 = nextP * cols + r + 1;
                int i3 = nextP * cols + r;

                // グリッド配置:
                //   i3 -- i2   (nextP)
                //   |     |
                //   i0 -- i1   (p)
                //   r    r+1
                // 外側から見て時計回り
                md.AddQuad(i0, i3, i2, i1);
            }
        }

        // 上キャップ
        if (closeTop && !closeLoop && profile[profileCount - 1].x > 0.001f)
        {
            int centerIdx = md.VertexCount;
            Vector3 topCenter = new Vector3(0, profile[profileCount - 1].y, 0) - pivotOffset;
            md.Vertices.Add(new Vertex(topCenter, new Vector2(0.5f, 0.5f), Vector3.up));

            int topRowStart = (profileCount - 1) * cols;
            for (int r = 0; r < radialSeg; r++)
            {
                // 上から見て時計回り
                md.AddTriangle(centerIdx, topRowStart + r + 1, topRowStart + r);
            }
        }

        // 下キャップ
        if (closeBottom && !closeLoop && profile[0].x > 0.001f)
        {
            int centerIdx = md.VertexCount;
            Vector3 bottomCenter = new Vector3(0, profile[0].y, 0) - pivotOffset;
            md.Vertices.Add(new Vertex(bottomCenter, new Vector2(0.5f, 0.5f), Vector3.down));

            for (int r = 0; r < radialSeg; r++)
            {
                // 下から見て時計回り
                md.AddTriangle(centerIdx, r, r + 1);
            }
        }

        return md;
    }

    private MeshData GenerateSpiralMeshData()
    {
        var md = new MeshData("Spiral");

        int totalRadialSteps = _radialSegments * _spiralTurns;
        int profileCount = _profile.Count;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (var p in _profile)
        {
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }
        float height = maxY - minY;
        if (height < 0.001f) height = 1f;

        Vector3 pivotOffset = new Vector3(0, _pivot.y * height + (minY + maxY) * 0.5f, 0);

        float angleStep = 2f * Mathf.PI / _radialSegments;

        // 頂点生成
        for (int r = 0; r <= totalRadialSteps; r++)
        {
            float angle = r * angleStep;
            float yOffset = r * _spiralPitch / _radialSegments;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            for (int p = 0; p < profileCount; p++)
            {
                float radius = _profile[p].x;
                float y = _profile[p].y + yOffset;
                float v = (float)p / (profileCount - 1);

                Vector2 tangent;
                if (_closeLoop)
                {
                    int prevIdx = (p - 1 + profileCount) % profileCount;
                    int nextIdx = (p + 1) % profileCount;
                    tangent = _profile[nextIdx] - _profile[prevIdx];
                }
                else
                {
                    if (p == 0)
                        tangent = _profile[1] - _profile[0];
                    else if (p == profileCount - 1)
                        tangent = _profile[p] - _profile[p - 1];
                    else
                        tangent = _profile[p + 1] - _profile[p - 1];
                }
                tangent.Normalize();

                Vector2 normal2D = new Vector2(tangent.y, -tangent.x);

                Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
                Vector3 normal = new Vector3(cos * normal2D.x, normal2D.y, sin * normal2D.x).normalized;
                Vector2 uv = new Vector2((float)r / totalRadialSteps, v);

                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        // 側面の四角形
        int loopCount = _closeLoop ? profileCount : profileCount - 1;
        for (int r = 0; r < totalRadialSteps; r++)
        {
            for (int p = 0; p < loopCount; p++)
            {
                int nextP = (p + 1) % profileCount;
                int i0 = r * profileCount + p;
                int i1 = r * profileCount + nextP;
                int i2 = (r + 1) * profileCount + nextP;
                int i3 = (r + 1) * profileCount + p;

                // グリッド配置:
                //   i3 -- i2   (r+1)
                //   |     |
                //   i0 -- i1   (r)
                //   p   nextP
                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // 上キャップ（らせん終端の断面を閉じる）
        // Spiralではprofileの端点のX > 0.001なら閉じる（closeLoopに関係なく）
        if (_closeTop && _profile[profileCount - 1].x > 0.001f)
        {
            int centerIdx = md.VertexCount;

            // 断面の中心
            float profileCenterX = 0f;
            foreach (var p in _profile) profileCenterX += p.x;
            profileCenterX /= profileCount;
            float profileCenterY = (minY + maxY) * 0.5f;

            // らせん終端の角度とY位置
            float endAngle = totalRadialSteps * angleStep;
            float topYOffset = totalRadialSteps * _spiralPitch / _radialSegments;

            // 断面中心は回転している
            Vector3 topCenter = new Vector3(
                Mathf.Cos(endAngle) * profileCenterX,
                profileCenterY + topYOffset,
                Mathf.Sin(endAngle) * profileCenterX
            ) - pivotOffset;

            // 法線は断面に垂直（回転方向）
            Vector3 topNormal = new Vector3(Mathf.Cos(endAngle), 0, Mathf.Sin(endAngle));
            md.Vertices.Add(new Vertex(topCenter, new Vector2(0.5f, 0.5f), topNormal));

            // 最後のリング（r = totalRadialSteps）の頂点を結ぶ
            for (int p = 0; p < profileCount - 1; p++)
            {
                md.AddTriangle(centerIdx, totalRadialSteps * profileCount + p, totalRadialSteps * profileCount + p + 1);
            }
            // closeLoopの場合は最後の点と最初の点も結ぶ
            if (_closeLoop)
            {
                md.AddTriangle(centerIdx, totalRadialSteps * profileCount + profileCount - 1, totalRadialSteps * profileCount);
            }
        }

        // 下キャップ（らせん始端の断面を閉じる）
        if (_closeBottom && _profile[0].x > 0.001f)
        {
            int centerIdx = md.VertexCount;

            // 断面の中心
            float profileCenterX = 0f;
            foreach (var p in _profile) profileCenterX += p.x;
            profileCenterX /= profileCount;
            float profileCenterY = (minY + maxY) * 0.5f;

            // らせん始端の角度（0）
            float startAngle = 0f;

            // 断面中心は回転している
            Vector3 bottomCenter = new Vector3(
                Mathf.Cos(startAngle) * profileCenterX,
                profileCenterY,
                Mathf.Sin(startAngle) * profileCenterX
            ) - pivotOffset;

            // 法線は断面に垂直（回転方向の逆）
            Vector3 bottomNormal = new Vector3(-Mathf.Cos(startAngle), 0, -Mathf.Sin(startAngle));
            md.Vertices.Add(new Vertex(bottomCenter, new Vector2(0.5f, 0.5f), bottomNormal));

            // 最初のリング（r = 0）の頂点を結ぶ
            for (int p = 0; p < profileCount - 1; p++)
            {
                md.AddTriangle(centerIdx, p + 1, p);
            }
            // closeLoopの場合は最後の点と最初の点も結ぶ
            if (_closeLoop)
            {
                md.AddTriangle(centerIdx, 0, profileCount - 1);
            }
        }

        return md;
    }
}