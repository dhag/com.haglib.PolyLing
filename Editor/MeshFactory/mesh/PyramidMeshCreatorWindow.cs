// Assets/Editor/MeshCreators/PyramidMeshCreatorWindow.cs
// 角錐メッシュ生成用のサブウインドウ（Undo対応）
// MeshData（Vertex/Face）ベース対応版 - 三角形面で構築
//
// 【頂点順序の規約】
// 三角形: md.AddTriangle(v0, v1, v2) - 時計回りが表面

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public class PyramidMeshCreatorWindow : EditorWindow
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    private struct PyramidParams : IEquatable<PyramidParams>
    {
        public string MeshName;
        public float BaseRadius;
        public float Height;
        public int Sides;
        public float ApexOffset;
        public bool CapBottom;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static PyramidParams Default => new PyramidParams
        {
            MeshName = "Pyramid",
            BaseRadius = 0.5f,
            Height = 1f,
            Sides = 4,
            ApexOffset = 0f,
            CapBottom = true,
            Pivot = Vector3.zero,
            RotationX = 20f,
            RotationY = 30f
        };

        public bool Equals(PyramidParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(BaseRadius, o.BaseRadius) &&
            Mathf.Approximately(Height, o.Height) &&
            Sides == o.Sides &&
            Mathf.Approximately(ApexOffset, o.ApexOffset) &&
            CapBottom == o.CapBottom &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is PyramidParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private PyramidParams _params = PyramidParams.Default;

    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;

    private Action<MeshData, string> _onMeshDataCreated;

    private ParameterUndoHelper<PyramidParams> _undoHelper;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static PyramidMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<PyramidMeshCreatorWindow>(true, "Create Pyramid UnityMesh", true);
        window.minSize = new Vector2(400, 520);
        window.maxSize = new Vector2(500, 720);
        window._onMeshDataCreated = onMeshDataCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    private void OnEnable()
    {
        InitPreview();
        InitUndo();
        UpdatePreviewMesh();
    }

    private void OnDisable()
    {
        CleanupPreview();
        _undoHelper?.Dispose();
    }

    private void InitUndo()
    {
        _undoHelper = new ParameterUndoHelper<PyramidParams>(
            "PyramidCreator",
            "Pyramid Parameters",
            () => _params,
            (p) => { _params = p; UpdatePreviewMesh(); },
            () => Repaint()
        );
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

    // ================================================================
    // GUI
    // ================================================================
    private void OnGUI()
    {
        _undoHelper?.HandleGUIEvents(Event.current);

        EditorGUILayout.Space(10);
        DrawParameters();
        EditorGUILayout.Space(10);
        DrawPreview();
        EditorGUILayout.Space(10);
        DrawButtons();
    }

    private void DrawParameters()
    {
        EditorGUILayout.LabelField("Pyramid Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);

        EditorGUILayout.Space(5);

        _params.Sides = EditorGUILayout.IntSlider("Sides", _params.Sides, 3, 16);
        _params.BaseRadius = EditorGUILayout.Slider("Base Radius", _params.BaseRadius, 0.1f, 5f);
        _params.Height = EditorGUILayout.Slider("Height", _params.Height, 0.1f, 10f);
        _params.ApexOffset = EditorGUILayout.Slider("Apex Offset", _params.ApexOffset, -1f, 1f);

        EditorGUILayout.Space(5);

        _params.CapBottom = EditorGUILayout.Toggle("Cap Bottom", _params.CapBottom);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.y = EditorGUILayout.Slider("Y", _params.Pivot.y, -0.5f, 0.5f);

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
            if (GUILayout.Button("Apex", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0, -0.5f, 0);
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreviewMesh();
        }
    }

    private void DrawPreview()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

        if (_preview == null || _previewMesh == null)
            return;

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

        if (e.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        float dist = Mathf.Max(_params.Height, _params.BaseRadius * 2f) * 2.5f;
        Quaternion rot = Quaternion.Euler(_params.RotationX, _params.RotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -dist);

        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(Vector3.zero);

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
            int triCount = _previewMeshData.Faces.Count(f => f.IsTriangle);
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount} (Tri:{triCount})",
                MessageType.None);
        }
    }

    private void UpdatePreviewMesh()
    {
        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
        }

        _previewMeshData = GenerateMeshData();
        _previewMesh = _previewMeshData.ToUnityMesh();
        Repaint();
    }

    private void CreateMesh()
    {
        var meshData = GenerateMeshData();
        meshData.Name = _params.MeshName;
        _onMeshDataCreated?.Invoke(meshData, _params.MeshName);
        Close();
    }

    // ================================================================
    // MeshData生成（三角形ベース）
    // ================================================================
    private MeshData GenerateMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float halfHeight = _params.Height * 0.5f;
        Vector3 pivotOffset = new Vector3(0, _params.Pivot.y * _params.Height, 0);

        Vector3 apex = new Vector3(_params.ApexOffset * _params.BaseRadius, halfHeight, 0) - pivotOffset;

        // 底面の頂点位置を計算
        Vector3[] basePositions = new Vector3[_params.Sides];
        for (int i = 0; i < _params.Sides; i++)
        {
            float angle = i * 2f * Mathf.PI / _params.Sides;
            basePositions[i] = new Vector3(
                Mathf.Cos(angle) * _params.BaseRadius,
                -halfHeight,
                Mathf.Sin(angle) * _params.BaseRadius
            ) - pivotOffset;
        }

        // 側面（各面ごとに頂点を作成）
        for (int i = 0; i < _params.Sides; i++)
        {
            int startIdx = md.VertexCount;

            Vector3 p0 = basePositions[i];
            Vector3 p1 = basePositions[(i + 1) % _params.Sides];

            // 法線計算
            Vector3 edge1 = p1 - p0;
            Vector3 edge2 = apex - p0;
            Vector3 normal = Vector3.Cross(edge1, edge2).normalized;

            // 頂点追加
            md.Vertices.Add(new Vertex(p0, new Vector2(0, 0), normal));
            md.Vertices.Add(new Vertex(p1, new Vector2(1, 0), normal));
            md.Vertices.Add(new Vertex(apex, new Vector2(0.5f, 1), normal));

            // 三角形追加（時計回りが表面）
            md.AddTriangle(startIdx, startIdx + 2, startIdx + 1);
        }

        // 底面キャップ
        if (_params.CapBottom)
        {
            int centerIdx = md.VertexCount;

            // 中心頂点
            Vector3 centerPos = new Vector3(0, -halfHeight, 0) - pivotOffset;
            md.Vertices.Add(new Vertex(centerPos, new Vector2(0.5f, 0.5f), Vector3.down));

            // 底面の頂点（下から見るので法線は下向き）
            for (int i = 0; i < _params.Sides; i++)
            {
                float angle = i * 2f * Mathf.PI / _params.Sides;
                Vector2 uv = new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f);
                md.Vertices.Add(new Vertex(basePositions[i], uv, Vector3.down));
            }

            // 三角形追加（下から見て時計回り = 上から見て反時計回り）
            for (int i = 0; i < _params.Sides; i++)
            {
                int v0 = centerIdx;
                int v1 = centerIdx + 1 + i;
                int v2 = centerIdx + 1 + (i + 1) % _params.Sides;
                md.AddTriangle(v0, v1, v2);
            }
        }

        return md;
    }
}
