// Assets/Editor/MeshCreators/SphereMeshCreatorWindow.cs
// スフィアメッシュ生成用のサブウインドウ（Undo対応）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
// 通常の球体とCube Sphere（立方体トポロジー）モードをサポート
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public class SphereMeshCreatorWindow : EditorWindow
{
    // ================================================================
    // パラメータ構造体（IEquatable実装）
    // ================================================================
    private struct SphereParams : IEquatable<SphereParams>
    {
        public string MeshName;
        public float Radius;
        public int LongitudeSegments;
        public int LatitudeSegments;
        public int CubeSubdivisions;
        public bool CubeSphere;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static SphereParams Default => new SphereParams
        {
            MeshName = "Sphere",
            Radius = 0.5f,
            LongitudeSegments = 24,
            LatitudeSegments = 16,
            CubeSubdivisions = 8,
            CubeSphere = false,
            Pivot = Vector3.zero,
            RotationX = 20f,
            RotationY = 30f
        };

        public bool Equals(SphereParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(Radius, o.Radius) &&
            LongitudeSegments == o.LongitudeSegments &&
            LatitudeSegments == o.LatitudeSegments &&
            CubeSubdivisions == o.CubeSubdivisions &&
            CubeSphere == o.CubeSphere &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is SphereParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private SphereParams _params = SphereParams.Default;

    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;

    private Action<MeshData, string> _onMeshDataCreated;

    private ParameterUndoHelper<SphereParams> _undoHelper;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static SphereMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<SphereMeshCreatorWindow>(true, "Create Sphere UnityMesh", true);
        window.minSize = new Vector2(400, 500);
        window.maxSize = new Vector2(500, 700);
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
        _undoHelper = new ParameterUndoHelper<SphereParams>(
            "SphereCreator",
            "Sphere Parameters",
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
        EditorGUILayout.LabelField("Sphere Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);

        EditorGUILayout.Space(5);

        _params.Radius = EditorGUILayout.Slider("Radius", _params.Radius, 0.1f, 5f);

        _params.CubeSphere = EditorGUILayout.Toggle("Cube Sphere", _params.CubeSphere);

        if (_params.CubeSphere)
        {
            _params.CubeSubdivisions = EditorGUILayout.IntSlider("Subdivisions", _params.CubeSubdivisions, 1, 32);
            EditorGUILayout.HelpBox("立方体の各面を細分化して球に投影します。極点に三角形が集中しない均一なメッシュになります。", MessageType.Info);
        }
        else
        {
            _params.LongitudeSegments = EditorGUILayout.IntSlider("Longitude Segments", _params.LongitudeSegments, 8, 64);
            _params.LatitudeSegments = EditorGUILayout.IntSlider("Latitude Segments", _params.LatitudeSegments, 4, 32);
        }

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

        float dist = _params.Radius * 5f;
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
    // MeshData生成
    // ================================================================
    private MeshData GenerateMeshData()
    {
        if (_params.CubeSphere)
        {
            return GenerateCubeSphereMeshData(_params.Radius, _params.CubeSubdivisions, _params.Pivot);
        }
        else
        {
            return GenerateSphereMeshData(_params.Radius, _params.LongitudeSegments, _params.LatitudeSegments, _params.Pivot);
        }
    }

    private MeshData GenerateSphereMeshData(float radius, int lonSegments, int latSegments, Vector3 pivot)
    {
        var md = new MeshData("Sphere");

        Vector3 pivotOffset = pivot * radius * 2f;
        int cols = lonSegments + 1;

        // 頂点生成
        for (int lat = 0; lat <= latSegments; lat++)
        {
            float theta = lat * Mathf.PI / latSegments;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float phi = lon * 2f * Mathf.PI / lonSegments;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                Vector3 normal = new Vector3(
                    cosPhi * sinTheta,
                    cosTheta,
                    sinPhi * sinTheta
                );

                Vector3 pos = normal * radius - pivotOffset;
                Vector2 uv = new Vector2((float)lon / lonSegments, 1f - (float)lat / latSegments);
                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        // 四角形面生成
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int i0 = lat * cols + lon;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                // グリッド配置:
                //   i3 -- i2   (lat+1)
                //   |     |
                //   i0 -- i1   (lat)
                //  lon  lon+1
                md.AddQuad(i0, i1, i2, i3);
            }
        }

        return md;
    }

    private MeshData GenerateCubeSphereMeshData(float radius, int subdivisions, Vector3 pivot)
    {
        var md = new MeshData("CubeSphere");

        Vector3 pivotOffset = pivot * radius * 2f;

        Vector3[] faceNormals = new Vector3[]
        {
            Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back
        };

        Vector3[] faceTangentsU = new Vector3[]
        {
            Vector3.forward, Vector3.back, Vector3.right,
            Vector3.right, Vector3.left, Vector3.right
        };

        Vector3[] faceTangentsV = new Vector3[]
        {
            Vector3.up, Vector3.up, Vector3.forward,
            Vector3.back, Vector3.up, Vector3.up
        };

        int vertsPerRow = subdivisions + 1;

        for (int face = 0; face < 6; face++)
        {
            Vector3 normal = faceNormals[face];
            Vector3 tangentU = faceTangentsU[face];
            Vector3 tangentV = faceTangentsV[face];

            int faceStartIdx = md.VertexCount;

            // 頂点生成
            for (int v = 0; v <= subdivisions; v++)
            {
                for (int u = 0; u <= subdivisions; u++)
                {
                    float cu = (u / (float)subdivisions) * 2f - 1f;
                    float cv = (v / (float)subdivisions) * 2f - 1f;

                    Vector3 cubePoint = normal + tangentU * cu + tangentV * cv;
                    Vector3 sphereNormal = cubePoint.normalized;
                    Vector3 pos = sphereNormal * radius - pivotOffset;

                    Vector2 uv = new Vector2((float)u / subdivisions, (float)v / subdivisions);
                    md.Vertices.Add(new Vertex(pos, uv, sphereNormal));
                }
            }

            // 四角形面生成
            for (int v = 0; v < subdivisions; v++)
            {
                for (int u = 0; u < subdivisions; u++)
                {
                    int i0 = faceStartIdx + v * vertsPerRow + u;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsPerRow + 1;
                    int i3 = i0 + vertsPerRow;

                    md.AddQuad(i0, i1, i2, i3);
                }
            }
        }

        return md;
    }
}
