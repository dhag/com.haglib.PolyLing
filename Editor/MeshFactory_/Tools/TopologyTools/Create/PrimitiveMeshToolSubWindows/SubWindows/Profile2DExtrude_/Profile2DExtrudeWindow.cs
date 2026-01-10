// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DExtrudeWindow.cs
// 2D閉曲線押し出しメッシュ生成エディタウィンドウ
// CSV読み込み、穴あきポリゴン対応、手動編集機能付き
// ローカライズ対応版

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Profile2DExtrude;
using static MeshFactory.Gizmo.GLGizmoDrawer;
using static MeshFactory.Profile2DExtrude.Profile2DExtrudeTexts;

public class Profile2DExtrudeWindow : EditorWindow
{
    // ================================================================
    // フィールド
    // ================================================================
    private Action<MeshObject, string> _onMeshObjectCreated;

    // パラメータ
    [SerializeField] private string _meshName = "Profile2DExtrude";
    [SerializeField] private string _csvPath = "";
    [SerializeField] private float _scale = 1.0f;
    [SerializeField] private Vector2 _offset = Vector2.zero;
    [SerializeField] private bool _flipY = false;
    [SerializeField] private float _thickness = 0f;
    [SerializeField] private int _segmentsFront = 0;
    [SerializeField] private int _segmentsBack = 0;
    [SerializeField] private float _edgeSizeFront = 0.1f;
    [SerializeField] private float _edgeSizeBack = 0.1f;
    [SerializeField] private bool _edgeInward = false;

    // ループ
    [SerializeField] private List<Loop> _loops = new List<Loop>();
    private int _selectedLoopIndex = 0;
    private int _newLoopSides = 4;

    // 2Dエディタ
    private Profile2DLoopEditor _loopEditor;

    // 3Dプレビュー
    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshObject _previewMeshObject;
    private Material _previewMaterial;
    private float _rotationY = 20f;
    private float _rotationX = 30f;

    // スクロール
    private Vector2 _leftScrollPos = Vector2.zero;

    // スプリッター
    private float _leftPaneWidth = 320f;
    private const float MIN_LEFT_PANE_WIDTH = 250f;
    private const float MAX_LEFT_PANE_WIDTH = 500f;
    private const float SPLITTER_WIDTH = 6f;
    private bool _isDraggingSplitter = false;

    // Undo
    private ParameterUndoHelper<Profile2DParams> _undoHelper;
    private const string SESSION_STATE_KEY = "Profile2DExtrudeWindow_State";

    // ================================================================
    // エントリポイント
    // ================================================================
    public static Profile2DExtrudeWindow Open(Action<MeshObject, string> onMeshObjectCreated)
    {
        var window = GetWindow<Profile2DExtrudeWindow>(
            utility: true,
            title: T("WindowTitle"),
            focus: true);

        window.minSize = new Vector2(800, 600);
        window.maxSize = new Vector2(1200, 900);
        window._onMeshObjectCreated = onMeshObjectCreated;
        window.InitPreview();

        if (window._loops.Count == 0)
        {
            window.InitializeDefaultLoops();
        }

        window.UpdatePreviewMesh();
        window.Show();
        return window;
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        InitPreview();
        InitUndo();
        InitLoopEditor();
        LoadState();
        if (_loops.Count == 0)
        {
            InitializeDefaultLoops();
        }
        UpdatePreviewMesh();
    }

    private void OnDisable()
    {
        SaveState();
        CleanupPreview();
        _undoHelper?.Dispose();
    }

    // ================================================================
    // 初期化
    // ================================================================
    private void InitUndo()
    {
        _undoHelper = new ParameterUndoHelper<Profile2DParams>(
            "Profile2DExtrude",
            "Profile2D Parameters",
            () => CaptureParams(),
            (p) => { ApplyParams(p); UpdatePreviewMesh(); },
            () => Repaint()
        );
    }

    private void InitLoopEditor()
    {
        _loopEditor = new Profile2DLoopEditor();
        _loopEditor.SetLoops(_loops);
        _loopEditor.OnLoopChanged = () =>
        {
            UpdatePreviewMesh();
            Repaint();
        };
        _loopEditor.OnRecordUndo = (desc) =>
        {
            _undoHelper?.RecordImmediate(desc);
        };
    }

    private void InitPreview()
    {
        if (_preview != null) return;

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

    private void InitializeDefaultLoops()
    {
        _loops.Clear();

        // 外側: 四角形
        var outer = new Loop();
        outer.Points.Add(new Vector2(-1f, -1f));
        outer.Points.Add(new Vector2(1f, -1f));
        outer.Points.Add(new Vector2(1f, 1f));
        outer.Points.Add(new Vector2(-1f, 1f));
        outer.IsHole = false;
        _loops.Add(outer);

        // 穴: 小さな四角形
        var hole = new Loop();
        hole.Points.Add(new Vector2(-0.3f, -0.3f));
        hole.Points.Add(new Vector2(0.3f, -0.3f));
        hole.Points.Add(new Vector2(0.3f, 0.3f));
        hole.Points.Add(new Vector2(-0.3f, 0.3f));
        hole.IsHole = true;
        _loops.Add(hole);

        _selectedLoopIndex = 0;
        _loopEditor?.SetLoops(_loops);
    }

    // ================================================================
    // パラメータ管理
    // ================================================================
    private Profile2DParams CaptureParams()
    {
        var p = new Profile2DParams
        {
            MeshName = _meshName,
            CsvPath = _csvPath,
            Scale = _scale,
            Offset = _offset,
            FlipY = _flipY,
            Thickness = _thickness,
            SegmentsFront = _segmentsFront,
            SegmentsBack = _segmentsBack,
            EdgeSizeFront = _edgeSizeFront,
            EdgeSizeBack = _edgeSizeBack,
            EdgeInward = _edgeInward,
            SelectedLoopIndex = _selectedLoopIndex,
            SelectedPointIndex = _loopEditor?.SelectedPointIndex ?? -1,
            RotationX = _rotationX,
            RotationY = _rotationY
        };
        p.SetLoops(_loops);
        return p;
    }

    private void ApplyParams(Profile2DParams p)
    {
        _meshName = p.MeshName;
        _csvPath = p.CsvPath;
        _scale = p.Scale;
        _offset = p.Offset;
        _flipY = p.FlipY;
        _thickness = p.Thickness;
        _segmentsFront = p.SegmentsFront;
        _segmentsBack = p.SegmentsBack;
        _edgeSizeFront = p.EdgeSizeFront;
        _edgeSizeBack = p.EdgeSizeBack;
        _edgeInward = p.EdgeInward;
        _selectedLoopIndex = p.SelectedLoopIndex;
        _rotationX = p.RotationX;
        _rotationY = p.RotationY;

        _loops = p.ToLoopList();
        _loopEditor?.SetLoops(_loops);
        _loopEditor?.SetSelectedIndex(p.SelectedPointIndex);
    }

    // ================================================================
    // SessionState
    // ================================================================
    private void SaveState()
    {
        try
        {
            var wrapper = new Profile2DStateWrapper(CaptureParams());
            string json = JsonUtility.ToJson(wrapper);
            SessionState.SetString(SESSION_STATE_KEY, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Profile2DExtrudeWindow: Failed to save state: {e.Message}");
        }
    }

    private void LoadState()
    {
        try
        {
            string json = SessionState.GetString(SESSION_STATE_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                var wrapper = JsonUtility.FromJson<Profile2DStateWrapper>(json);
                if (wrapper != null)
                {
                    ApplyParams(wrapper.ToParams());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Profile2DExtrudeWindow: Failed to load state: {e.Message}");
        }
    }

    // ================================================================
    // OnGUI
    // ================================================================
    private void OnGUI()
    {
        _undoHelper?.HandleGUIEvents(Event.current);

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        // 左側：パラメータ、ループリスト、3Dプレビュー
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_leftPaneWidth)))
        {
            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos, GUILayout.ExpandHeight(true));
            DrawParameters();
            EditorGUILayout.Space(10);
            DrawLoopList();
            EditorGUILayout.Space(10);
            Draw3DPreviewSmall();
            EditorGUILayout.EndScrollView();
        }

        // スプリッター
        DrawSplitter();

        // 右側：2Dエディタ
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            _loopEditor.SelectedLoopIndex = _selectedLoopIndex;
            _loopEditor.Draw2DEditorLarge();
            _selectedLoopIndex = _loopEditor.SelectedLoopIndex;
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        DrawButtons();
    }

    private void DrawSplitter()
    {
        Rect splitterRect = GUILayoutUtility.GetRect(SPLITTER_WIDTH, SPLITTER_WIDTH, GUILayout.ExpandHeight(true));

        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.DrawRect(splitterRect, new Color(0.1f, 0.1f, 0.1f, 1f));
        Rect lineRect = new Rect(splitterRect.x + 2, splitterRect.y, 2, splitterRect.height);
        UnityEditor_Handles.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 1f));
        UnityEditor_Handles.EndGUI();

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        switch (e.type)
        {
            case EventType.MouseDown:
                if (splitterRect.Contains(e.mousePosition) && e.button == 0)
                {
                    _isDraggingSplitter = true;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (_isDraggingSplitter)
                {
                    _leftPaneWidth += e.delta.x;
                    _leftPaneWidth = Mathf.Clamp(_leftPaneWidth, MIN_LEFT_PANE_WIDTH, MAX_LEFT_PANE_WIDTH);
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (_isDraggingSplitter)
                {
                    _isDraggingSplitter = false;
                    e.Use();
                }
                break;
        }
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    private void DrawParameters()
    {
        EditorGUILayout.LabelField(T("Parameters"), EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _meshName = EditorGUILayout.TextField(T("Name"), _meshName);

        EditorGUILayout.Space(5);

        // CSVパス
        EditorGUILayout.LabelField(T("CSVFile"), EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            string displayPath = string.IsNullOrEmpty(_csvPath) ? T("NoFile") : Path.GetFileName(_csvPath);
            EditorGUILayout.LabelField(displayPath, EditorStyles.helpBox, GUILayout.Height(20));

            if (GUILayout.Button(T("LoadCSV"), GUILayout.Width(80)))
            {
                var result = Profile2DCSVHandler.LoadFromCSVWithDialog(_csvPath);
                if (result.Success)
                {
                    _csvPath = result.ErrorMessage; // パスが返される
                    _loops = result.Loops;
                    _loopEditor?.SetLoops(_loops);
                    _selectedLoopIndex = 0;
                    _undoHelper?.RecordImmediate(T("UndoLoadCSV"));
                    UpdatePreviewMesh();
                }
            }
        }

        EditorGUILayout.Space(5);

        _scale = EditorGUILayout.Slider(T("Scale"), _scale, 0.01f, 10f);
        _offset = EditorGUILayout.Vector2Field(T("Offset"), _offset);
        _flipY = EditorGUILayout.Toggle(T("FlipY"), _flipY);
        _thickness = EditorGUILayout.Slider(T("Thickness"), _thickness, 0f, 2f);

        // 厚みがある場合のみ角処理を表示
        if (_thickness > 0.001f)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("EdgeSettings"), EditorStyles.miniBoldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                _segmentsFront = EditorGUILayout.IntSlider(T("FrontSegments"), _segmentsFront, 0, 16);
                if (_segmentsFront > 0)
                {
                    _edgeSizeFront = EditorGUILayout.Slider("  " + T("Size"), _edgeSizeFront, 0.01f, 0.5f);
                }

                EditorGUILayout.Space(3);

                _segmentsBack = EditorGUILayout.IntSlider(T("BackSegments"), _segmentsBack, 0, 16);
                if (_segmentsBack > 0)
                {
                    _edgeSizeBack = EditorGUILayout.Slider("  " + T("Size"), _edgeSizeBack, 0.01f, 0.5f);
                }

                if (_segmentsFront > 0 || _segmentsBack > 0)
                {
                    EditorGUILayout.Space(3);
                    _edgeInward = EditorGUILayout.Toggle(T("OutwardKeepFaceSize"), _edgeInward);
                }
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreviewMesh();
        }
    }

    // ================================================================
    // ループリスト
    // ================================================================
    private void DrawLoopList()
    {
        EditorGUILayout.LabelField(T("Loops"), EditorStyles.boldLabel);

        // ループ追加設定
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(T("NewLoopSides"), GUILayout.Width(100));
            _newLoopSides = EditorGUILayout.IntSlider(_newLoopSides, 3, 12);
        }

        // ループ追加ボタン
        if (GUILayout.Button(T("AddNgon", _newLoopSides)))
        {
            var newLoop = new Loop();
            float radius = 0.3f;
            float angleOffset = -Mathf.PI / 2f;
            
            for (int i = 0; i < _newLoopSides; i++)
            {
                float angle = angleOffset + (2f * Mathf.PI * i / _newLoopSides);
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                newLoop.Points.Add(new Vector2(x, y));
            }
            
            newLoop.IsHole = _loops.Count > 0;
            _loops.Add(newLoop);
            _selectedLoopIndex = _loops.Count - 1;
            _loopEditor?.SetLoops(_loops);
            _undoHelper?.RecordImmediate(T("UndoAddLoop"));
            UpdatePreviewMesh();
        }

        EditorGUILayout.Space(5);

        // ループ一覧
        for (int i = 0; i < _loops.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool selected = (i == _selectedLoopIndex);
                string label = _loops[i].IsHole ? T("Hole", i) : T("Outer", i);
                label += $" ({_loops[i].Points.Count} pts)";

                if (GUILayout.Toggle(selected, label, "Button", GUILayout.Width(120)))
                {
                    if (!selected)
                    {
                        _selectedLoopIndex = i;
                        _loopEditor.SelectedLoopIndex = i;
                        _loopEditor.SelectedPointIndex = -1;
                    }
                }

                EditorGUI.BeginChangeCheck();
                bool isHole = EditorGUILayout.Toggle(_loops[i].IsHole, GUILayout.Width(20));
                EditorGUILayout.LabelField(T("IsHole"), GUILayout.Width(35));
                if (EditorGUI.EndChangeCheck())
                {
                    _loops[i].IsHole = isHole;
                    _undoHelper?.RecordImmediate(T("UndoToggleHole"));
                    UpdatePreviewMesh();
                }

                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    _loops.RemoveAt(i);
                    if (_selectedLoopIndex >= _loops.Count)
                        _selectedLoopIndex = _loops.Count - 1;
                    _loopEditor?.SetLoops(_loops);
                    _undoHelper?.RecordImmediate(T("UndoDeleteLoop"));
                    UpdatePreviewMesh();
                    GUIUtility.ExitGUI();
                }
            }
        }

        // CSV保存ボタン
        EditorGUILayout.Space(5);
        GUI.enabled = _loops.Count > 0 && _loops.Any(l => l.Points.Count >= 3);
        if (GUILayout.Button(T("SaveAsCSV")))
        {
            string newPath = Profile2DCSVHandler.SaveToCSVWithDialog(_loops, _meshName, _csvPath);
            if (!string.IsNullOrEmpty(newPath))
            {
                _csvPath = newPath;
            }
        }
        GUI.enabled = true;

        // 選択ループの変形UI
        if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("TransformLoop"), EditorStyles.miniBoldLabel);

            var loop = _loops[_selectedLoopIndex];

            // スケール
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(T("ScaleLabel"), GUILayout.Width(45));
                if (GUILayout.Button("0.5x", GUILayout.Width(40))) ScaleLoop(loop, 0.5f);
                if (GUILayout.Button("0.9x", GUILayout.Width(40))) ScaleLoop(loop, 0.9f);
                if (GUILayout.Button("1.1x", GUILayout.Width(40))) ScaleLoop(loop, 1.1f);
                if (GUILayout.Button("2x", GUILayout.Width(40))) ScaleLoop(loop, 2f);
            }

            // 移動
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(T("MoveLabel"), GUILayout.Width(45));
                float moveStep = 0.1f;
                if (GUILayout.Button("←", GUILayout.Width(30))) MoveLoop(loop, new Vector2(-moveStep, 0));
                if (GUILayout.Button("→", GUILayout.Width(30))) MoveLoop(loop, new Vector2(moveStep, 0));
                if (GUILayout.Button("↑", GUILayout.Width(30))) MoveLoop(loop, new Vector2(0, moveStep));
                if (GUILayout.Button("↓", GUILayout.Width(30))) MoveLoop(loop, new Vector2(0, -moveStep));
                if (GUILayout.Button(T("Center"), GUILayout.Width(50))) CenterLoop(loop);
            }
        }
    }

    private void ScaleLoop(Loop loop, float scale)
    {
        Vector2 center = Vector2.zero;
        foreach (var pt in loop.Points) center += pt;
        center /= loop.Points.Count;

        for (int i = 0; i < loop.Points.Count; i++)
        {
            Vector2 offset = loop.Points[i] - center;
            loop.Points[i] = center + offset * scale;
        }

        _undoHelper?.RecordImmediate(T("UndoScaleLoop", scale));
        UpdatePreviewMesh();
    }

    private void MoveLoop(Loop loop, Vector2 delta)
    {
        for (int i = 0; i < loop.Points.Count; i++)
        {
            loop.Points[i] += delta;
        }

        _undoHelper?.RecordImmediate(T("UndoMoveLoop"));
        UpdatePreviewMesh();
    }

    private void CenterLoop(Loop loop)
    {
        Vector2 center = Vector2.zero;
        foreach (var pt in loop.Points) center += pt;
        center /= loop.Points.Count;

        for (int i = 0; i < loop.Points.Count; i++)
        {
            loop.Points[i] -= center;
        }

        _undoHelper?.RecordImmediate(T("UndoCenterLoop"));
        UpdatePreviewMesh();
    }

    // ================================================================
    // 3Dプレビュー
    // ================================================================
    private void Draw3DPreviewSmall()
    {
        EditorGUILayout.LabelField(T("Preview3D"), EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(280, 200);

        if (_preview == null || _previewMesh == null)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f));
            UnityEditor_Handles.EndGUI();
            return;
        }

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
        if (maxExtent < 0.001f) maxExtent = 1f;

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad * 0.5f;
        float dist = maxExtent / Mathf.Tan(fovRad) * 2.0f;

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

    // ================================================================
    // ボタン
    // ================================================================
    private void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !string.IsNullOrEmpty(_csvPath);
        if (GUILayout.Button(T("ReloadCSV"), GUILayout.Height(30)))
        {
            var result = Profile2DCSVHandler.LoadFromCSV(_csvPath);
            if (result.Success)
            {
                _loops = result.Loops;
                _loopEditor?.SetLoops(_loops);
                _undoHelper?.RecordImmediate(T("UndoReloadCSV"));
                UpdatePreviewMesh();
            }
        }
        GUI.enabled = true;

        if (GUILayout.Button(T("ResetToDefault"), GUILayout.Height(30)))
        {
            InitializeDefaultLoops();
            _undoHelper?.RecordImmediate(T("UndoResetDefault"));
            UpdatePreviewMesh();
        }

        GUI.enabled = _loops != null && _loops.Any(l => l.Points.Count >= 3 && !l.IsHole);
        if (GUILayout.Button(T("Create"), GUILayout.Height(30)))
        {
            CreateMeshAndClose();
        }
        GUI.enabled = true;

        if (GUILayout.Button(T("Cancel"), GUILayout.Height(30)))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();

        // 情報表示
        if (_previewMeshObject != null)
        {
            EditorGUILayout.Space(5);
            int triCount = _previewMeshObject.Faces.Count(f => f.IsTriangle);
            int quadCount = _previewMeshObject.Faces.Count(f => f.IsQuad);
            EditorGUILayout.HelpBox(
                T("MeshInfo", _previewMeshObject.VertexCount, _previewMeshObject.FaceCount, triCount, quadCount, _loops.Count),
                MessageType.None);
        }
    }

    // ================================================================
    // メッシュ生成
    // ================================================================
    private void UpdatePreviewMesh()
    {
        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        _previewMeshObject = GenerateMeshObject();
        if (_previewMeshObject != null)
        {
            _previewMesh = _previewMeshObject.ToUnityMesh();
        }

        Repaint();
    }

    private MeshObject GenerateMeshObject()
    {
        var genParams = new Profile2DGenerateParams
        {
            Scale = _scale,
            Offset = _offset,
            FlipY = _flipY,
            Thickness = _thickness,
            SegmentsFront = _segmentsFront,
            SegmentsBack = _segmentsBack,
            EdgeSizeFront = _edgeSizeFront,
            EdgeSizeBack = _edgeSizeBack,
            EdgeInward = _edgeInward
        };

        return Profile2DExtrudeMeshGenerator.Generate(_loops, _meshName, genParams);
    }

    private void CreateMeshAndClose()
    {
        var meshObject = GenerateMeshObject();
        if (meshObject == null)
        {
            EditorUtility.DisplayDialog(
                T("WindowTitle"),
                T("MeshGenerationFailed"),
                T("OK"));
            return;
        }

        _onMeshObjectCreated?.Invoke(meshObject, _meshName);
        Close();
    }
}
