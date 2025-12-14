// Assets/Editor/MeshCreators/PlaneMeshCreatorWindow.cs
// プレーングリッドメッシュ生成用のサブウインドウ（Undo対応）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)
// 裏面: md.AddQuad(i0, i3, i2, i1)

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public class PlaneMeshCreatorWindow : EditorWindow
{
    public enum PlaneOrientation { XY, XZ, YZ }

    // ================================================================
    // パラメータ構造体
    // ================================================================
    private struct PlaneParams : IEquatable<PlaneParams>
    {
        public string MeshName;
        public float Width, Height;
        public int WidthSegments, HeightSegments;
        public bool DoubleSided;
        public PlaneOrientation Orientation;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static PlaneParams Default => new PlaneParams
        {
            MeshName = "Plane",
            Width = 1f,
            Height = 1f,
            WidthSegments = 1,
            HeightSegments = 1,
            DoubleSided = false,
            Orientation = PlaneOrientation.XZ,
            Pivot = Vector3.zero,
            RotationX = 45f,
            RotationY = 30f
        };

        public bool Equals(PlaneParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(Width, o.Width) &&
            Mathf.Approximately(Height, o.Height) &&
            WidthSegments == o.WidthSegments &&
            HeightSegments == o.HeightSegments &&
            DoubleSided == o.DoubleSided &&
            Orientation == o.Orientation &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is PlaneParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private PlaneParams _params = PlaneParams.Default;

    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;

    private Action<MeshData, string> _onMeshDataCreated;

    private ParameterUndoHelper<PlaneParams> _undoHelper;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static PlaneMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<PlaneMeshCreatorWindow>(true, "Create Plane UnityMesh", true);
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
        _undoHelper = new ParameterUndoHelper<PlaneParams>(
            "PlaneCreator",
            "Plane Parameters",
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
        _preview?.Cleanup();
        _preview = null;
        if (_previewMesh != null) DestroyImmediate(_previewMesh);
        if (_previewMaterial != null) DestroyImmediate(_previewMaterial);
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
        EditorGUILayout.LabelField("Plane Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);

        EditorGUILayout.Space(5);

        _params.Width = EditorGUILayout.Slider("Width", _params.Width, 0.1f, 10f);
        _params.Height = EditorGUILayout.Slider("Height", _params.Height, 0.1f, 10f);
        _params.WidthSegments = EditorGUILayout.IntSlider("Width Segments", _params.WidthSegments, 1, 32);
        _params.HeightSegments = EditorGUILayout.IntSlider("Height Segments", _params.HeightSegments, 1, 32);

        EditorGUILayout.Space(5);

        _params.Orientation = (PlaneOrientation)EditorGUILayout.EnumPopup("Orientation", _params.Orientation);
        _params.DoubleSided = EditorGUILayout.Toggle("Double Sided", _params.DoubleSided);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider("X", _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider("Y", _params.Pivot.y, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button("Corner", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0.5f, 0.5f, 0);
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

        float dist = Mathf.Max(_params.Width, _params.Height) * 2.5f;
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
            int quadCount = _previewMeshData.Faces.Count(f => f.IsQuad);
            int triCount = _previewMeshData.Faces.Count(f => f.IsTriangle);
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount} (Quad:{quadCount}, Tri:{triCount})",
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
    // MeshData生成（四角形ベース）
    // ================================================================
    private MeshData GenerateMeshData()
    {
        var md = new MeshData(_params.MeshName);

        Vector3 pivotOffset = new Vector3(_params.Pivot.x * _params.Width, _params.Pivot.y * _params.Height, 0);

        // 表面
        AddPlaneFace(md, pivotOffset, false);

        // 裏面（DoubleSidedの場合）
        if (_params.DoubleSided)
        {
            AddPlaneFace(md, pivotOffset, true);
        }

        return md;
    }

    /// <summary>
    /// 平面を追加
    /// </summary>
    private void AddPlaneFace(MeshData md, Vector3 pivotOffset, bool flip)
    {
        int startIdx = md.VertexCount;

        // 法線を決定
        Vector3 normal;
        switch (_params.Orientation)
        {
            case PlaneOrientation.XY:
                normal = flip ? Vector3.back : Vector3.forward;
                break;
            case PlaneOrientation.XZ:
                normal = flip ? Vector3.down : Vector3.up;
                break;
            case PlaneOrientation.YZ:
                normal = flip ? Vector3.left : Vector3.right;
                break;
            default:
                normal = Vector3.up;
                break;
        }

        // 頂点グリッドを生成
        // 法線方向から見た座標系で i0=左下, i1=右下, i2=右上, i3=左上 になるように配置
        for (int h = 0; h <= _params.HeightSegments; h++)
        {
            for (int w = 0; w <= _params.WidthSegments; w++)
            {
                float u = (float)w / _params.WidthSegments;
                float v = (float)h / _params.HeightSegments;

                float x = (u - 0.5f) * _params.Width;
                float y = (v - 0.5f) * _params.Height;

                Vector3 pos;
                switch (_params.Orientation)
                {
                    case PlaneOrientation.XY:
                        // +Z方向から見る: X=右, Y=上
                        pos = new Vector3(x - pivotOffset.x, y - pivotOffset.y, 0);
                        break;
                    case PlaneOrientation.XZ:
                        // +Y方向から見る: X=右, Z=下（手前）なので反転
                        pos = new Vector3(x - pivotOffset.x, 0, -y + pivotOffset.y);
                        break;
                    case PlaneOrientation.YZ:
                        // +X方向から見る: Z=右, Y=上 なのでZ反転
                        pos = new Vector3(0, y - pivotOffset.y, -x + pivotOffset.x);
                        break;
                    default:
                        pos = new Vector3(x, 0, -y);
                        break;
                }

                Vector2 uv = new Vector2(flip ? (1f - u) : u, v);
                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        // 四角形面を生成
        int cols = _params.WidthSegments + 1;
        for (int h = 0; h < _params.HeightSegments; h++)
        {
            for (int w = 0; w < _params.WidthSegments; w++)
            {
                int i0 = startIdx + h * cols + w;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                // グリッド配置:
                //   i3 -- i2   (h+1)
                //   |     |
                //   i0 -- i1   (h)
                //   w    w+1
                //
                // CubeMeshCreatorWindowと統一:
                // 表面: md.AddQuad(i0, i1, i2, i3)
                // 裏面: md.AddQuad(i0, i3, i2, i1)
                if (flip)
                {
                    md.AddQuad(i0, i3, i2, i1);
                }
                else
                {
                    md.AddQuad(i0, i1, i2, i3);
                }
            }
        }
    }
}