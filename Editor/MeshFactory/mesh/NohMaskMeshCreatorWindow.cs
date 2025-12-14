// Assets/Editor/MeshCreators/NohMaskMeshCreatorWindow.cs
// 能面（Noh Mask）ベースメッシュ生成用のサブウインドウ（Undo対応）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
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

public class NohMaskMeshCreatorWindow : EditorWindow
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    private struct NohMaskParams : IEquatable<NohMaskParams>
    {
        public string MeshName;
        public float WidthTop, WidthBottom;
        public float Height;
        public float DepthTop, DepthBottom;
        public float NoseHeight, NoseWidth, NoseLength, NosePosition;
        public float BottomCurve, TopCurve;
        public int HorizontalSegments, VerticalSegments;
        public Vector3 Pivot;
        public bool FlipY, FlipZ;
        public float RotationX, RotationY;

        public static NohMaskParams Default => new NohMaskParams
        {
            MeshName = "NohMask",
            WidthTop = 0.12f,
            WidthBottom = 0.15f,
            Height = 0.2f,
            DepthTop = 0.04f,
            DepthBottom = 0.05f,
            NoseHeight = 0.03f,
            NoseWidth = 0.03f,
            NoseLength = 0.06f,
            NosePosition = 0.1f,
            BottomCurve = 0.02f,
            TopCurve = 0.02f,
            HorizontalSegments = 16,
            VerticalSegments = 20,
            Pivot = Vector3.zero,
            FlipY = true,
            FlipZ = true,
            RotationX = 20f,
            RotationY = 30f
        };

        public bool Equals(NohMaskParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(WidthTop, o.WidthTop) &&
            Mathf.Approximately(WidthBottom, o.WidthBottom) &&
            Mathf.Approximately(Height, o.Height) &&
            Mathf.Approximately(DepthTop, o.DepthTop) &&
            Mathf.Approximately(DepthBottom, o.DepthBottom) &&
            Mathf.Approximately(NoseHeight, o.NoseHeight) &&
            Mathf.Approximately(NoseWidth, o.NoseWidth) &&
            Mathf.Approximately(NoseLength, o.NoseLength) &&
            Mathf.Approximately(NosePosition, o.NosePosition) &&
            Mathf.Approximately(BottomCurve, o.BottomCurve) &&
            Mathf.Approximately(TopCurve, o.TopCurve) &&
            HorizontalSegments == o.HorizontalSegments &&
            VerticalSegments == o.VerticalSegments &&
            Pivot == o.Pivot &&
            FlipY == o.FlipY &&
            FlipZ == o.FlipZ &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is NohMaskParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private NohMaskParams _params = NohMaskParams.Default;

    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;

    private Action<MeshData, string> _onMeshDataCreated;
    private Vector2 _scrollPos;

    private ParameterUndoHelper<NohMaskParams> _undoHelper;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static NohMaskMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<NohMaskMeshCreatorWindow>(true, "Create Noh Mask UnityMesh", true);
        window.minSize = new Vector2(420, 750);
        window.maxSize = new Vector2(500, 900);
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
        _undoHelper = new ParameterUndoHelper<NohMaskParams>(
            "NohMaskCreator",
            "NohMask Parameters",
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
            _previewMaterial.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.75f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.9f, 0.85f, 0.75f, 1f));
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

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(10);
        DrawParameters();
        EditorGUILayout.Space(10);
        DrawPreview();
        EditorGUILayout.Space(10);

        EditorGUILayout.EndScrollView();

        DrawButtons();
    }

    private void DrawParameters()
    {
        EditorGUILayout.LabelField("Noh Mask Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Face Size", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.WidthTop = EditorGUILayout.Slider("Width Top", _params.WidthTop, 0.05f, 0.3f);
            _params.WidthBottom = EditorGUILayout.Slider("Width Bottom", _params.WidthBottom, 0.05f, 0.3f);
            _params.Height = EditorGUILayout.Slider("Height", _params.Height, 0.1f, 0.5f);
            _params.DepthTop = EditorGUILayout.Slider("Depth Top", _params.DepthTop, 0.01f, 0.15f);
            _params.DepthBottom = EditorGUILayout.Slider("Depth Bottom", _params.DepthBottom, 0.01f, 0.15f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Nose", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.NoseHeight = EditorGUILayout.Slider("Height", _params.NoseHeight, 0f, 0.1f);
            _params.NoseWidth = EditorGUILayout.Slider("Width", _params.NoseWidth, 0.01f, 0.1f);
            _params.NoseLength = EditorGUILayout.Slider("Length", _params.NoseLength, 0.02f, 0.15f);
            _params.NosePosition = EditorGUILayout.Slider("Position", _params.NosePosition, 0f, 0.2f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Curve", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.TopCurve = EditorGUILayout.Slider("Top", _params.TopCurve, 0f, 0.1f);
            _params.BottomCurve = EditorGUILayout.Slider("Bottom", _params.BottomCurve, 0f, 0.1f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Segments", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.HorizontalSegments = EditorGUILayout.IntSlider("Horizontal", _params.HorizontalSegments, 4, 32);
            _params.VerticalSegments = EditorGUILayout.IntSlider("Vertical", _params.VerticalSegments, 4, 40);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Orientation", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.FlipY = EditorGUILayout.Toggle("Flip Y (180° Y rotation)", _params.FlipY);
            _params.FlipZ = EditorGUILayout.Toggle("Flip Z (Upside down)", _params.FlipZ);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.z = EditorGUILayout.Slider("Z", _params.Pivot.z, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button("Front", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0, 0, 0.5f);
                GUI.changed = true;
            }
            if (GUILayout.Button("Back", GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0, 0, -0.5f);
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

        float dist = _params.Height * 3f;
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

        int hSegs = _params.HorizontalSegments;
        int vSegs = _params.VerticalSegments;
        int cols = hSegs + 1;

        // ピボットオフセット（平均深さで計算）
        float avgDepth = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivotOffset = new Vector3(0, 0, _params.Pivot.z * avgDepth * 2f);

        // 頂点グリッドを生成
        for (int v = 0; v <= vSegs; v++)
        {
            float ty = (float)v / vSegs;  // 0 to 1
            float normalizedY = ty * 2f - 1f;  // -1 to 1
            float y = normalizedY * _params.Height;

            // テーパー：上下で幅と深さを補間
            float taperT = _params.FlipZ ? (1f - ty) : ty;
            float currentWidth = Mathf.Lerp(_params.WidthBottom, _params.WidthTop, taperT);
            float currentDepth = Mathf.Lerp(_params.DepthBottom, _params.DepthTop, taperT);

            for (int h = 0; h <= hSegs; h++)
            {
                float tx = (float)h / hSegs;  // 0 to 1
                float normalizedX = tx * 2f - 1f;  // -1 to 1

                // 楕円形の輪郭
                float ellipseRadius = GetEllipseRadius(normalizedX, normalizedY);
                float x = normalizedX * currentWidth * ellipseRadius;

                // 基本の深さ（楕円状の凸面）- テーパー対応
                float baseDepth = GetBaseDepth(normalizedX, normalizedY, ellipseRadius, currentDepth);

                // 鼻の凸
                float noseDepth = GetNoseDepth(normalizedX, normalizedY, currentWidth);

                // カーブ（flipZで上下反転する場合はパラメータを入れ替え）
                float topCurveValue = _params.FlipZ ? _params.BottomCurve : _params.TopCurve;
                float bottomCurveValue = _params.FlipZ ? _params.TopCurve : _params.BottomCurve;
                float topCurve = GetTopCurve(normalizedY, topCurveValue);
                float bottomCurve = GetBottomCurve(normalizedY, bottomCurveValue);

                float z = baseDepth + noseDepth + topCurve + bottomCurve;

                Vector3 pos = new Vector3(x, y, z) - pivotOffset;

                // Y軸回りに180度回転（X, Zを反転）
                if (_params.FlipY)
                {
                    pos = new Vector3(-pos.x, pos.y, -pos.z);
                }

                // Z軸回りに180度回転（X, Yを反転）
                if (_params.FlipZ)
                {
                    pos = new Vector3(-pos.x, -pos.y, pos.z);
                }

                // 法線は後で再計算するのでダミー
                md.Vertices.Add(new Vertex(pos, new Vector2(tx, ty), Vector3.forward));
            }
        }

        // 四角形面生成
        for (int v = 0; v < vSegs; v++)
        {
            for (int h = 0; h < hSegs; h++)
            {
                int i0 = v * cols + h;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                // グリッド配置:
                //   i3 -- i2   (v+1)
                //   |     |
                //   i0 -- i1   (v)
                //   h    h+1
                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // 法線を再計算
        md.RecalculateSmoothNormals();

        return md;
    }

    /// <summary>
    /// 楕円形の輪郭を計算（端で0、中心で1）
    /// </summary>
    private float GetEllipseRadius(float nx, float ny)
    {
        // 顔の輪郭は卵形（上が少し狭い）
        float topNarrow = 1f - ny * 0.15f;  // 上に行くほど狭く

        // 頬の膨らみ
        float cheekBulge = 1f + Mathf.Max(0, (1f - Mathf.Abs(ny + 0.2f) * 2f)) * 0.1f;

        return topNarrow * cheekBulge;
    }

    /// <summary>
    /// 基本の深さ（滑らかな凸面）- テーパー対応
    /// </summary>
    private float GetBaseDepth(float nx, float ny, float ellipseRadius, float depth)
    {
        // 中心が最も深く、端で0になる
        float distFromCenter = Mathf.Sqrt(nx * nx + ny * ny);
        float falloff = 1f - Mathf.Clamp01(distFromCenter);
        falloff = Mathf.Pow(falloff, 0.7f);  // より滑らかなカーブ

        // 横方向のカーブ（顔の丸み）
        float horizontalCurve = 1f - nx * nx;
        horizontalCurve = Mathf.Pow(Mathf.Max(0, horizontalCurve), 0.5f);

        return depth * falloff * horizontalCurve;
    }

    /// <summary>
    /// 鼻の凸を計算（テーパー対応）
    /// </summary>
    private float GetNoseDepth(float nx, float ny, float currentWidth)
    {
        // 鼻の位置（Y方向）
        float noseY = _params.NosePosition / _params.Height;  // 正規化された位置

        // 鼻の範囲
        float noseCenterY = noseY;
        float noseTopY = noseCenterY + (_params.NoseLength / _params.Height) * 0.5f;
        float noseBottomY = noseCenterY - (_params.NoseLength / _params.Height) * 0.5f;

        // Y方向の鼻の影響（上で尖り、下で広がる）
        float yInfluence = 0f;
        if (ny >= noseBottomY && ny <= noseTopY)
        {
            float localY = (ny - noseBottomY) / (noseTopY - noseBottomY);  // 0 to 1
            // 下から上に向かって山型
            yInfluence = Mathf.Sin(localY * Mathf.PI);
            // 鼻筋は上に向かって細くなる
            yInfluence *= 1f - localY * 0.3f;
        }

        // X方向の鼻の影響（中心で最大、鼻筋を形成）
        // 幅に対する相対的な鼻の幅を使用
        float avgWidth = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float noseWidthNorm = _params.NoseWidth / avgWidth;
        float absNx = Mathf.Abs(nx);

        float xInfluence;
        if (absNx <= noseWidthNorm)
        {
            // 鼻の幅内：台形状に隆起（中心は平らに近い）
            float t = absNx / noseWidthNorm;  // 0 to 1
            xInfluence = 1f - t * t * 0.3f;   // 中心で1、端で0.7程度
        }
        else
        {
            // 鼻の外側：急速に減衰
            float falloff = (absNx - noseWidthNorm) / (noseWidthNorm * 0.5f);
            xInfluence = 0.7f * Mathf.Max(0f, 1f - falloff);
        }

        return _params.NoseHeight * xInfluence * yInfluence;
    }

    /// <summary>
    /// 上のカーブ
    /// </summary>
    private float GetTopCurve(float ny, float curveValue)
    {
        if (ny > 0.5f)
        {
            float t = (ny - 0.5f) / 0.5f;  // 0 to 1
            return curveValue * t * t;
        }
        return 0f;
    }

    /// <summary>
    /// 下のカーブ
    /// </summary>
    private float GetBottomCurve(float ny, float curveValue)
    {
        if (ny < -0.5f)
        {
            float t = (-0.5f - ny) / 0.5f;  // 0 to 1
            return curveValue * t * t;
        }
        return 0f;
    }
}
