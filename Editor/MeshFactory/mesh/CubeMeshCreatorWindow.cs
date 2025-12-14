// Assets/Editor/MeshCreators/CubeMeshCreatorWindow.cs
// 角を丸めた直方体メッシュ生成用のサブウインドウ（テーパー対応・Undo対応）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
//
// 【頂点順序の規約】
// AddQuadFace入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
// 法線方向から見た座標系で指定する（PlaneMeshCreatorWindowと同じ仕様）
// 内部で md.AddQuad(i0, i3, i2, i1) を呼び、表面を生成する

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;

public class CubeMeshCreatorWindow : EditorWindow
{
    // ================================================================
    // パラメータ構造体（IEquatable実装）
    // ================================================================
    private struct CubeParams : IEquatable<CubeParams>
    {
        public string MeshName;
        public float WidthTop, DepthTop;
        public float WidthBottom, DepthBottom;
        public float Height;
        public float CornerRadius;
        public int CornerSegments;
        public Vector3Int Subdivisions;
        public Vector3 Pivot;
        public float RotationX, RotationY;
        public bool LinkTopBottom;  // Top/Bottom連動フラグ
        public bool LinkWHD;        // Width/Height/Depth連動フラグ

        public static CubeParams Default => new CubeParams
        {
            MeshName = "RoundedCube",
            WidthTop = 1f,
            DepthTop = 1f,
            WidthBottom = 1f,
            DepthBottom = 1f,
            Height = 1f,
            CornerRadius = 0.1f,
            CornerSegments = 4,
            Subdivisions = Vector3Int.one,
            Pivot = Vector3.zero,
            RotationX = 20f,
            RotationY = 30f,
            LinkTopBottom = false,
            LinkWHD = false
        };

        public bool Equals(CubeParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(WidthTop, o.WidthTop) &&
            Mathf.Approximately(DepthTop, o.DepthTop) &&
            Mathf.Approximately(WidthBottom, o.WidthBottom) &&
            Mathf.Approximately(DepthBottom, o.DepthBottom) &&
            Mathf.Approximately(Height, o.Height) &&
            Mathf.Approximately(CornerRadius, o.CornerRadius) &&
            CornerSegments == o.CornerSegments &&
            Subdivisions == o.Subdivisions &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY) &&
            LinkTopBottom == o.LinkTopBottom &&
            LinkWHD == o.LinkWHD;

        public override bool Equals(object obj) => obj is CubeParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // フィールド
    // ================================================================
    private CubeParams _params = CubeParams.Default;
    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;
    private Action<MeshData, string> _onMeshDataCreated;
    private Vector2 _scrollPos;
    private ParameterUndoHelper<CubeParams> _undoHelper;

    // WHD連動時の前回値（どの値が変化したか検出用）
    private float _prevWidthForWHD;
    private float _prevHeightForWHD;
    private float _prevDepthForWHD;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static CubeMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<CubeMeshCreatorWindow>(true, "Create Rounded Cube UnityMesh", true);
        window.minSize = new Vector2(400, 700);
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
        SyncPrevWHDValues();
    }

    private void OnDisable()
    {
        CleanupPreview();
        _undoHelper?.Dispose();
    }

    private void InitUndo()
    {
        _undoHelper = new ParameterUndoHelper<CubeParams>(
            "CubeCreator",
            "Cube Parameters",
            () => _params,
            (p) => { _params = p; SyncPrevWHDValues(); UpdatePreviewMesh(); },
            () => Repaint()
        );
    }

    /// <summary>
    /// WHD連動用の前回値を同期
    /// </summary>
    private void SyncPrevWHDValues()
    {
        _prevWidthForWHD = _params.WidthTop;
        _prevHeightForWHD = _params.Height;
        _prevDepthForWHD = _params.DepthTop;
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
    // メインGUI
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
        EditorGUILayout.LabelField("Rounded Cube Parameters", EditorStyles.boldLabel);
        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        // ====== 連動オプション ======
        EditorGUILayout.LabelField("Link Options", EditorStyles.miniBoldLabel);

        // WHD連動チェックボックス
        bool prevLinkWHD = _params.LinkWHD;
        _params.LinkWHD = EditorGUILayout.Toggle("Link W/H/D (Cube Mode)", _params.LinkWHD);

        // WHD連動ONに切り替わった瞬間、Width優先で全て同期
        if (_params.LinkWHD && !prevLinkWHD)
        {
            float unifiedSize = _params.WidthTop;
            _params.WidthTop = _params.WidthBottom = unifiedSize;
            _params.DepthTop = _params.DepthBottom = unifiedSize;
            _params.Height = unifiedSize;
            SyncPrevWHDValues();
        }

        // WHD連動がOFFの場合のみTop/Bottom連動を表示
        if (!_params.LinkWHD)
        {
            // Top/Bottom連動チェックボックス
            bool prevLink = _params.LinkTopBottom;
            _params.LinkTopBottom = EditorGUILayout.Toggle("Link Top/Bottom Size", _params.LinkTopBottom);

            // 連動ONに切り替わった瞬間、Top優先でBottomを同期
            if (_params.LinkTopBottom && !prevLink)
            {
                _params.WidthBottom = _params.WidthTop;
                _params.DepthBottom = _params.DepthTop;
            }
        }
        else
        {
            // WHD連動時はTop/Bottomも強制連動
            _params.LinkTopBottom = true;
        }

        EditorGUILayout.Space(5);

        // ====== サイズ入力 ======
        if (_params.LinkWHD)
        {
            // WHD連動モード: 単一のSizeスライダー群
            EditorGUILayout.LabelField("Size (Linked)", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider("Width (X)", _params.WidthTop, 0.1f, 10f);
                float newHeight = EditorGUILayout.Slider("Height (Y)", _params.Height, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider("Depth (Z)", _params.DepthTop, 0.1f, 10f);

                // どの値が変化したか検出して、変化した値で全てを同期
                float targetSize = _params.WidthTop; // デフォルト

                if (!Mathf.Approximately(newWidth, _prevWidthForWHD))
                {
                    targetSize = newWidth;
                }
                else if (!Mathf.Approximately(newHeight, _prevHeightForWHD))
                {
                    targetSize = newHeight;
                }
                else if (!Mathf.Approximately(newDepth, _prevDepthForWHD))
                {
                    targetSize = newDepth;
                }

                // 全ての値を同期
                _params.WidthTop = _params.WidthBottom = targetSize;
                _params.DepthTop = _params.DepthBottom = targetSize;
                _params.Height = targetSize;

                // 前回値を更新
                _prevWidthForWHD = targetSize;
                _prevHeightForWHD = targetSize;
                _prevDepthForWHD = targetSize;
            }
        }
        else if (_params.LinkTopBottom)
        {
            // Top/Bottom連動モード: Size（共通）
            EditorGUILayout.LabelField("Size", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider("Width (X)", _params.WidthTop, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider("Depth (Z)", _params.DepthTop, 0.1f, 10f);

                // Top/Bottom両方に適用
                _params.WidthTop = _params.WidthBottom = newWidth;
                _params.DepthTop = _params.DepthBottom = newDepth;
            }
        }
        else
        {
            // 独立モード: Size Top / Size Bottom
            EditorGUILayout.LabelField("Size Top", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _params.WidthTop = EditorGUILayout.Slider("Width (X)", _params.WidthTop, 0.1f, 10f);
                _params.DepthTop = EditorGUILayout.Slider("Depth (Z)", _params.DepthTop, 0.1f, 10f);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Size Bottom", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _params.WidthBottom = EditorGUILayout.Slider("Width (X)", _params.WidthBottom, 0.1f, 10f);
                _params.DepthBottom = EditorGUILayout.Slider("Depth (Z)", _params.DepthBottom, 0.1f, 10f);
            }
        }

        // Height (WHD連動時以外)
        if (!_params.LinkWHD)
        {
            EditorGUILayout.Space(5);
            _params.Height = EditorGUILayout.Slider("Height (Y)", _params.Height, 0.1f, 10f);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Corner", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            float minSize = Mathf.Min(_params.WidthTop, _params.DepthTop, _params.WidthBottom, _params.DepthBottom, _params.Height);
            float maxRadius = minSize * 0.5f;
            _params.CornerRadius = EditorGUILayout.Slider("Radius", _params.CornerRadius, 0f, maxRadius);

            using (new EditorGUI.DisabledScope(_params.CornerRadius <= 0f))
            {
                _params.CornerSegments = EditorGUILayout.IntSlider("Segments", _params.CornerSegments, 1, 16);
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Subdivisions", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Subdivisions.x = EditorGUILayout.IntSlider("X Divisions", _params.Subdivisions.x, 1, 10);
            _params.Subdivisions.y = EditorGUILayout.IntSlider("Y Divisions", _params.Subdivisions.y, 1, 10);
            _params.Subdivisions.z = EditorGUILayout.IntSlider("Z Divisions", _params.Subdivisions.z, 1, 10);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider("X", _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider("Y", _params.Pivot.y, -0.5f, 0.5f);
            _params.Pivot.z = EditorGUILayout.Slider("Z", _params.Pivot.z, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Center", GUILayout.Width(60))) { _params.Pivot = Vector3.zero; GUI.changed = true; }
            if (GUILayout.Button("Bottom", GUILayout.Width(60))) { _params.Pivot = new Vector3(0, 0.5f, 0); GUI.changed = true; }
            if (GUILayout.Button("Corner", GUILayout.Width(60))) { _params.Pivot = new Vector3(0.5f, 0.5f, 0.5f); GUI.changed = true; }
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
        {
            EditorGUILayout.HelpBox("Preview not available", MessageType.None);
            return;
        }

        Event e = Event.current;
        if (rect.Contains(e.mousePosition) && e.type == EventType.MouseDrag && e.button == 1)
        {
            _params.RotationY += e.delta.x * 0.5f;
            _params.RotationX += e.delta.y * 0.5f;
            _params.RotationX = Mathf.Clamp(_params.RotationX, -89f, 89f);
            e.Use();
            Repaint();
        }

        if (e.type == EventType.Repaint)
        {
            _preview.BeginPreview(rect, GUIStyle.none);
            float maxSize = Mathf.Max(_params.WidthTop, _params.DepthTop, _params.WidthBottom, _params.DepthBottom, _params.Height);
            float distance = maxSize * 3f;
            Quaternion rotation = Quaternion.Euler(_params.RotationX, _params.RotationY, 0);
            Vector3 cameraPos = rotation * new Vector3(0, 0, -distance);
            _preview.camera.transform.position = cameraPos;
            _preview.camera.transform.LookAt(Vector3.zero);
            if (_previewMaterial != null)
                _preview.DrawMesh(_previewMesh, Vector3.zero, Quaternion.identity, _previewMaterial, 0);
            _preview.camera.Render();
            _preview.EndAndDrawPreview(rect);
        }

        if (_previewMeshData != null)
        {
            int quadCount = _previewMeshData.Faces.Where(f => f.IsQuad).Count();
            int triCount = _previewMeshData.Faces.Where(f => f.IsTriangle).Count();
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount} (Quad:{quadCount}, Tri:{triCount})",
                MessageType.None);
        }
    }

    private void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Create", GUILayout.Height(30))) CreateMesh();
        if (GUILayout.Button("Cancel", GUILayout.Height(30))) Close();
        EditorGUILayout.EndHorizontal();
    }

    private void UpdatePreviewMesh()
    {
        if (_previewMesh != null) DestroyImmediate(_previewMesh);
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
        return _params.CornerRadius <= 0f ? GenerateSimpleCubeMeshData() : GenerateRoundedCubeMeshData();
    }

    /// <summary>
    /// シンプルな直方体（四角形6面）
    /// 頂点命名規則: t=top, b=bottom, F=front(+Z), B=back(-Z), L=left(-X), R=right(+X)
    /// </summary>
    private MeshData GenerateSimpleCubeMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float halfH = _params.Height * 0.5f;
        float avgW = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float avgD = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivot = new Vector3(_params.Pivot.x * avgW, _params.Pivot.y * _params.Height, _params.Pivot.z * avgD);

        // 8頂点の位置
        Vector3 tFL = new Vector3(-_params.WidthTop * 0.5f, halfH, _params.DepthTop * 0.5f) - pivot;
        Vector3 tFR = new Vector3(_params.WidthTop * 0.5f, halfH, _params.DepthTop * 0.5f) - pivot;
        Vector3 tBL = new Vector3(-_params.WidthTop * 0.5f, halfH, -_params.DepthTop * 0.5f) - pivot;
        Vector3 tBR = new Vector3(_params.WidthTop * 0.5f, halfH, -_params.DepthTop * 0.5f) - pivot;
        Vector3 bFL = new Vector3(-_params.WidthBottom * 0.5f, -halfH, _params.DepthBottom * 0.5f) - pivot;
        Vector3 bFR = new Vector3(_params.WidthBottom * 0.5f, -halfH, _params.DepthBottom * 0.5f) - pivot;
        Vector3 bBL = new Vector3(-_params.WidthBottom * 0.5f, -halfH, -_params.DepthBottom * 0.5f) - pivot;
        Vector3 bBR = new Vector3(_params.WidthBottom * 0.5f, -halfH, -_params.DepthBottom * 0.5f) - pivot;

        // 6面を四角形で生成
        // AddQuadFace入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
        // 法線方向から見た座標系で指定する
        // 
        // 頂点命名: t=top, b=bottom, F=front(+Z), B=back(-Z), L=left(-X), R=right(+X)

        // +X面（右から見て: 左下=bFR, 右下=bBR, 右上=tBR, 左上=tFR）
        AddQuadFace(md, bFR, bBR, tBR, tFR, Vector3.right, _params.Subdivisions.z, _params.Subdivisions.y);
        // -X面（左から見て: 左下=bBL, 右下=bFL, 右上=tFL, 左上=tBL）
        AddQuadFace(md, bBL, bFL, tFL, tBL, Vector3.left, _params.Subdivisions.z, _params.Subdivisions.y);
        // +Y面（上から見て: 左下=tFL, 右下=tFR, 右上=tBR, 左上=tBL）
        AddQuadFace(md, tFL, tFR, tBR, tBL, Vector3.up, _params.Subdivisions.x, _params.Subdivisions.z);
        // -Y面（下から見て: 左下=bBL, 右下=bBR, 右上=bFR, 左上=bFL）
        AddQuadFace(md, bBL, bBR, bFR, bFL, Vector3.down, _params.Subdivisions.x, _params.Subdivisions.z);
        // +Z面（前から見て: 左下=bFL, 右下=bFR, 右上=tFR, 左上=tFL）
        AddQuadFace(md, bFL, bFR, tFR, tFL, Vector3.forward, _params.Subdivisions.x, _params.Subdivisions.y);
        // -Z面（後ろから見て: 左下=bBR, 右下=bBL, 右上=tBL, 左上=tBR）
        AddQuadFace(md, bBR, bBL, tBL, tBR, Vector3.back, _params.Subdivisions.x, _params.Subdivisions.y);

        return md;
    }

    /// <summary>
    /// 角丸直方体
    /// </summary>
    private MeshData GenerateRoundedCubeMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float r = _params.CornerRadius;
        int seg = _params.CornerSegments;
        float halfH = _params.Height * 0.5f;

        float avgW = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float avgD = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivot = new Vector3(_params.Pivot.x * avgW, _params.Pivot.y * _params.Height, _params.Pivot.z * avgD);

        // 内側の寸法（角丸の中心位置）
        float inXT = _params.WidthTop * 0.5f - r;
        float inZT = _params.DepthTop * 0.5f - r;
        float inXB = _params.WidthBottom * 0.5f - r;
        float inZB = _params.DepthBottom * 0.5f - r;
        float inY = halfH - r;

        // === 8つの角（1/8球） ===
        // 各角の中心位置と方向
        // 上面4角
        AddCornerSphere(md, new Vector3(inXT, inY, inZT), new Vector3(1, 1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXT, inY, -inZT), new Vector3(-1, 1, -1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(inXT, inY, -inZT), new Vector3(1, 1, -1), r, seg, pivot);
        // 下面4角
        AddCornerSphere(md, new Vector3(inXB, -inY, inZB), new Vector3(1, -1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXB, -inY, inZB), new Vector3(-1, -1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-1, -1, -1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(inXB, -inY, -inZB), new Vector3(1, -1, -1), r, seg, pivot);

        // === 12の辺（1/4円柱） ===
        // 上面の4辺（Y=+inY, 水平）
        AddEdgeCylinder(md, new Vector3(-inXT, inY, inZT), new Vector3(inXT, inY, inZT), new Vector3(0, 1, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXT, inY, inZT), new Vector3(inXT, inY, -inZT), new Vector3(1, 1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXT, inY, -inZT), new Vector3(-inXT, inY, -inZT), new Vector3(0, 1, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXT, inY, -inZT), new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 0), r, seg, pivot);
        // 下面の4辺（Y=-inY, 水平）
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, inZB), new Vector3(inXB, -inY, inZB), new Vector3(0, -1, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, inZB), new Vector3(inXB, -inY, -inZB), new Vector3(1, -1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, -inZB), new Vector3(-inXB, -inY, -inZB), new Vector3(0, -1, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-inXB, -inY, inZB), new Vector3(-1, -1, 0), r, seg, pivot);
        // 縦の4辺
        AddEdgeCylinder(md, new Vector3(inXB, -inY, inZB), new Vector3(inXT, inY, inZT), new Vector3(1, 0, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, inZB), new Vector3(-inXT, inY, inZT), new Vector3(-1, 0, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, -inZB), new Vector3(inXT, inY, -inZT), new Vector3(1, 0, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-inXT, inY, -inZT), new Vector3(-1, 0, -1), r, seg, pivot);

        // === 6つの面（平面部分） ===
        // AddQuadFace入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
        // 法線方向から見た座標系で指定する

        // +X面（右から見て: 左下=(+Z,-Y), 右下=(-Z,-Y), 右上=(-Z,+Y), 左上=(+Z,+Y)）
        AddQuadFace(md,
            new Vector3(_params.WidthBottom * 0.5f, -inY, inZB) - pivot,   // 左下
            new Vector3(_params.WidthBottom * 0.5f, -inY, -inZB) - pivot,  // 右下
            new Vector3(_params.WidthTop * 0.5f, inY, -inZT) - pivot,      // 右上
            new Vector3(_params.WidthTop * 0.5f, inY, inZT) - pivot,       // 左上
            Vector3.right, _params.Subdivisions.z, _params.Subdivisions.y);
        // -X面（左から見て: 左下=(-Z,-Y), 右下=(+Z,-Y), 右上=(+Z,+Y), 左上=(-Z,+Y)）
        AddQuadFace(md,
            new Vector3(-_params.WidthBottom * 0.5f, -inY, -inZB) - pivot, // 左下
            new Vector3(-_params.WidthBottom * 0.5f, -inY, inZB) - pivot,  // 右下
            new Vector3(-_params.WidthTop * 0.5f, inY, inZT) - pivot,      // 右上
            new Vector3(-_params.WidthTop * 0.5f, inY, -inZT) - pivot,     // 左上
            Vector3.left, _params.Subdivisions.z, _params.Subdivisions.y);
        // +Y面（上から見て: 左下=(-X,+Z), 右下=(+X,+Z), 右上=(+X,-Z), 左上=(-X,-Z)）
        AddQuadFace(md,
            new Vector3(-inXT, halfH, inZT) - pivot,   // 左下
            new Vector3(inXT, halfH, inZT) - pivot,    // 右下
            new Vector3(inXT, halfH, -inZT) - pivot,   // 右上
            new Vector3(-inXT, halfH, -inZT) - pivot,  // 左上
            Vector3.up, _params.Subdivisions.x, _params.Subdivisions.z);
        // -Y面（下から見て: 左下=(-X,-Z), 右下=(+X,-Z), 右上=(+X,+Z), 左上=(-X,+Z)）
        AddQuadFace(md,
            new Vector3(-inXB, -halfH, -inZB) - pivot, // 左下
            new Vector3(inXB, -halfH, -inZB) - pivot,  // 右下
            new Vector3(inXB, -halfH, inZB) - pivot,   // 右上
            new Vector3(-inXB, -halfH, inZB) - pivot,  // 左上
            Vector3.down, _params.Subdivisions.x, _params.Subdivisions.z);
        // +Z面（前から見て: 左下=(-X,-Y), 右下=(+X,-Y), 右上=(+X,+Y), 左上=(-X,+Y)）
        AddQuadFace(md,
            new Vector3(-inXB, -inY, _params.DepthBottom * 0.5f) - pivot,  // 左下
            new Vector3(inXB, -inY, _params.DepthBottom * 0.5f) - pivot,   // 右下
            new Vector3(inXT, inY, _params.DepthTop * 0.5f) - pivot,       // 右上
            new Vector3(-inXT, inY, _params.DepthTop * 0.5f) - pivot,      // 左上
            Vector3.forward, _params.Subdivisions.x, _params.Subdivisions.y);
        // -Z面（後ろから見て: 左下=(+X,-Y), 右下=(-X,-Y), 右上=(-X,+Y), 左上=(+X,+Y)）
        AddQuadFace(md,
            new Vector3(inXB, -inY, -_params.DepthBottom * 0.5f) - pivot,  // 左下
            new Vector3(-inXB, -inY, -_params.DepthBottom * 0.5f) - pivot, // 右下
            new Vector3(-inXT, inY, -_params.DepthTop * 0.5f) - pivot,     // 右上
            new Vector3(inXT, inY, -_params.DepthTop * 0.5f) - pivot,      // 左上
            Vector3.back, _params.Subdivisions.x, _params.Subdivisions.y);

        return md;
    }

    // ================================================================
    // 四角形面生成ヘルパー
    // ================================================================

    /// <summary>
    /// 分割された四角形面を追加
    /// 入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
    /// 法線方向から見た座標系で指定する
    /// </summary>
    private void AddQuadFace(MeshData md, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, int divU, int divV)
    {
        int startIdx = md.VertexCount;

        // 頂点グリッドを生成
        // v0が(0,0), v1が(1,0), v2が(1,1), v3が(0,1)に対応
        for (int iv = 0; iv <= divV; iv++)
        {
            float vt = (float)iv / divV;
            Vector3 left = Vector3.Lerp(v0, v3, vt);
            Vector3 right = Vector3.Lerp(v1, v2, vt);

            for (int iu = 0; iu <= divU; iu++)
            {
                float ut = (float)iu / divU;
                Vector3 pos = Vector3.Lerp(left, right, ut);
                Vector2 uv = new Vector2(ut, vt);
                md.Vertices.Add(new Vertex(pos, uv, normal));
            }
        }

        // 四角形面を生成
        int cols = divU + 1;
        for (int iv = 0; iv < divV; iv++)
        {
            for (int iu = 0; iu < divU; iu++)
            {
                int i0 = startIdx + iv * cols + iu;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                // グリッド配置:
                //   i3 -- i2
                //   |     |
                //   i0 -- i1
                //
                // PlaneMeshCreatorWindowと同じ仕様:
                // 表面は md.AddQuad(i0, i1, i2, i3)
                md.AddQuad(i0, i1, i2, i3);
            }
        }
    }

    /// <summary>
    /// 角の球面部分（1/8球）
    /// dir: 角の方向ベクトル（各成分は±1）
    /// </summary>
    private void AddCornerSphere(MeshData md, Vector3 center, Vector3 dir, float radius, int seg, Vector3 pivot)
    {
        int startIdx = md.VertexCount;

        // dir の各成分の符号の積で、経度の回転方向を決める
        // これにより、グリッドの向きが常に法線方向から見て同じになる
        bool reverseLon = (dir.x * dir.y * dir.z) < 0;

        // 頂点を生成（緯度・経度グリッド）
        // lat=0が極（dir.y方向）、lat=segが赤道
        for (int lat = 0; lat <= seg; lat++)
        {
            float latAngle = lat * (Mathf.PI * 0.5f) / seg;
            float cosLat = Mathf.Cos(latAngle);
            float sinLat = Mathf.Sin(latAngle);

            for (int lon = 0; lon <= seg; lon++)
            {
                // reverseLonの場合、経度を逆方向に回す
                float lonAngle = reverseLon
                    ? (seg - lon) * (Mathf.PI * 0.5f) / seg
                    : lon * (Mathf.PI * 0.5f) / seg;

                // ローカル座標での法線（極がY軸、XZ平面が赤道）
                float localX = sinLat * Mathf.Cos(lonAngle);
                float localY = cosLat;
                float localZ = sinLat * Mathf.Sin(lonAngle);

                // dirで反転
                Vector3 n = new Vector3(localX * dir.x, localY * dir.y, localZ * dir.z).normalized;
                Vector3 pos = center + n * radius - pivot;
                Vector2 uv = new Vector2((float)lon / seg, (float)lat / seg);
                md.Vertices.Add(new Vertex(pos, uv, n));
            }
        }

        // 四角形面を生成
        // グリッド配置:
        //   i3 -- i2   (lat+1)
        //   |     |
        //   i0 -- i1   (lat)
        //  lon  lon+1
        //
        // PlaneMeshCreatorWindowと同じ仕様:
        // 表面は md.AddQuad(i0, i3, i2, i1)

        int cols = seg + 1;
        for (int lat = 0; lat < seg; lat++)
        {
            for (int lon = 0; lon < seg; lon++)
            {
                int i0 = startIdx + lat * cols + lon;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }
    }

    /// <summary>
    /// 辺の円柱部分（1/4円柱）
    /// cornerDir: この辺が接する角の方向（法線の向きを決める）
    /// </summary>
    private void AddEdgeCylinder(MeshData md, Vector3 start, Vector3 end, Vector3 cornerDir, float radius, int seg, Vector3 pivot)
    {
        int startIdx = md.VertexCount;

        Vector3 axis = (end - start).normalized;

        // 円柱の断面を定義する2つの方向ベクトル
        Vector3 perpDir1, perpDir2;
        if (Mathf.Abs(axis.x) > 0.9f)
        {
            // X軸に沿った辺
            perpDir1 = new Vector3(0, cornerDir.y, 0).normalized;
            perpDir2 = new Vector3(0, 0, cornerDir.z).normalized;
        }
        else if (Mathf.Abs(axis.y) > 0.9f)
        {
            // Y軸に沿った辺
            perpDir1 = new Vector3(cornerDir.x, 0, 0).normalized;
            perpDir2 = new Vector3(0, 0, cornerDir.z).normalized;
        }
        else
        {
            // Z軸に沿った辺
            perpDir1 = new Vector3(cornerDir.x, 0, 0).normalized;
            perpDir2 = new Vector3(0, cornerDir.y, 0).normalized;
        }

        // perpDir1→perpDir2 の回転方向と axis の向きで、頂点生成順序を決める
        // 法線が外向きになるように、グリッドの向きを調整
        Vector3 cross = Vector3.Cross(perpDir1, perpDir2);
        bool reverseRing = Vector3.Dot(cross, axis) < 0;

        // 2リングの頂点を生成
        // reverseRingの場合、start/endを入れ替えてグリッドの向きを反転
        Vector3 ringStart = reverseRing ? end : start;
        Vector3 ringEnd = reverseRing ? start : end;

        for (int ring = 0; ring <= 1; ring++)
        {
            Vector3 basePos = (ring == 0) ? ringStart : ringEnd;
            for (int j = 0; j <= seg; j++)
            {
                float angle = j * (Mathf.PI * 0.5f) / seg;
                Vector3 n = perpDir1 * Mathf.Cos(angle) + perpDir2 * Mathf.Sin(angle);
                Vector3 pos = basePos + n * radius - pivot;
                Vector2 uv = new Vector2(ring, (float)j / seg);
                md.Vertices.Add(new Vertex(pos, uv, n));
            }
        }

        // 四角形面を生成
        // グリッド配置:
        //   i3 -- i2   (ring=1)
        //   |     |
        //   i0 -- i1   (ring=0)
        //   j    j+1
        //
        // 表面は md.AddQuad(i0, i1, i2, i3)

        int cols = seg + 1;
        for (int j = 0; j < seg; j++)
        {
            int i0 = startIdx + j;
            int i1 = i0 + 1;
            int i2 = startIdx + cols + j + 1;
            int i3 = startIdx + cols + j;

            md.AddQuad(i0, i1, i2, i3);
        }
    }
}