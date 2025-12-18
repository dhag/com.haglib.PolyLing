// Assets/Editor/MeshCreators/CubeMeshCreatorWindow.cs
// 角を丸めた直方体メッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
//
// 【頂点順序の規約】
// AddQuadFace入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
// 法線方向から見た座標系で指定する

using System;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;

public class CubeMeshCreatorWindow : MeshCreatorWindowBase<CubeMeshCreatorWindow.CubeParams>
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct CubeParams : IEquatable<CubeParams>
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
        public bool LinkTopBottom;
        public bool LinkWHD;

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
    // WHD連動用フィールド
    // ================================================================
    private float _prevWidthForWHD;
    private float _prevHeightForWHD;
    private float _prevDepthForWHD;

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "CubeCreator";
    protected override string UndoDescription => "Cube Parameters";
    protected override float PreviewCameraDistance =>
        Mathf.Max(_params.WidthTop, _params.WidthBottom, _params.DepthTop, _params.DepthBottom, _params.Height) * 2.5f;

    protected override CubeParams GetDefaultParams() => CubeParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    protected override void OnInitialize()
    {
        SyncPrevWHDValues();
    }

    protected override void OnParamsChanged()
    {
        SyncPrevWHDValues();
    }

    private void SyncPrevWHDValues()
    {
        _prevWidthForWHD = _params.WidthTop;
        _prevHeightForWHD = _params.Height;
        _prevDepthForWHD = _params.DepthTop;
    }

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static CubeMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<CubeMeshCreatorWindow>(true, "Create Rounded Cube UnityMesh", true);
        window.minSize = new Vector2(400, 750);
        window.maxSize = new Vector2(500, 950);
        window._onMeshDataCreated = onMeshDataCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField("Rounded Cube Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        // ====== 連動オプション ======
        EditorGUILayout.LabelField("Link Options", EditorStyles.miniBoldLabel);

        bool prevLinkWHD = _params.LinkWHD;
        _params.LinkWHD = EditorGUILayout.Toggle("Link W/H/D (Cube Mode)", _params.LinkWHD);

        if (_params.LinkWHD && !prevLinkWHD)
        {
            float unifiedSize = _params.WidthTop;
            _params.WidthTop = _params.WidthBottom = unifiedSize;
            _params.DepthTop = _params.DepthBottom = unifiedSize;
            _params.Height = unifiedSize;
            SyncPrevWHDValues();
        }

        if (!_params.LinkWHD)
        {
            bool prevLink = _params.LinkTopBottom;
            _params.LinkTopBottom = EditorGUILayout.Toggle("Link Top/Bottom Size", _params.LinkTopBottom);

            if (_params.LinkTopBottom && !prevLink)
            {
                _params.WidthBottom = _params.WidthTop;
                _params.DepthBottom = _params.DepthTop;
            }
        }
        else
        {
            _params.LinkTopBottom = true;
        }

        EditorGUILayout.Space(5);

        // ====== サイズ入力 ======
        if (_params.LinkWHD)
        {
            EditorGUILayout.LabelField("Size (Linked)", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider("Width (X)", _params.WidthTop, 0.1f, 10f);
                float newHeight = EditorGUILayout.Slider("Height (Y)", _params.Height, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider("Depth (Z)", _params.DepthTop, 0.1f, 10f);

                float targetSize = _params.WidthTop;

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

                _params.WidthTop = _params.WidthBottom = targetSize;
                _params.DepthTop = _params.DepthBottom = targetSize;
                _params.Height = targetSize;

                _prevWidthForWHD = targetSize;
                _prevHeightForWHD = targetSize;
                _prevDepthForWHD = targetSize;
            }
        }
        else if (_params.LinkTopBottom)
        {
            EditorGUILayout.LabelField("Size", EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider("Width (X)", _params.WidthTop, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider("Depth (Z)", _params.DepthTop, 0.1f, 10f);

                _params.WidthTop = _params.WidthBottom = newWidth;
                _params.DepthTop = _params.DepthBottom = newDepth;
            }
        }
        else
        {
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
                _params.CornerSegments = EditorGUILayout.IntSlider("Segments", _params.CornerSegments, 1, 8);
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Subdivisions", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Subdivisions.x = EditorGUILayout.IntSlider("X", _params.Subdivisions.x, 1, 16);
            _params.Subdivisions.y = EditorGUILayout.IntSlider("Y", _params.Subdivisions.y, 1, 16);
            _params.Subdivisions.z = EditorGUILayout.IntSlider("Z", _params.Subdivisions.z, 1, 16);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Pivot Offset", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider("X", _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider("Y", _params.Pivot.y, -0.5f, 0.5f);
            _params.Pivot.z = EditorGUILayout.Slider("Z", _params.Pivot.z, -0.5f, 0.5f);

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

        EndParamChange();
    }

    // ================================================================
    // プレビュー（マウスドラッグ回転対応）
    // ================================================================
    protected override void DrawPreview()
    {
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

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

        Quaternion rot = Quaternion.Euler(_params.RotationX, _params.RotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -PreviewCameraDistance);
        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(Vector3.zero);

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
        return _params.CornerRadius <= 0f ? GenerateSimpleCubeMeshData() : GenerateRoundedCubeMeshData();
    }

    private MeshData GenerateSimpleCubeMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float halfH = _params.Height * 0.5f;
        float avgW = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float avgD = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivot = new Vector3(_params.Pivot.x * avgW, _params.Pivot.y * _params.Height, _params.Pivot.z * avgD);

        Vector3 tFL = new Vector3(-_params.WidthTop * 0.5f, halfH, _params.DepthTop * 0.5f) - pivot;
        Vector3 tFR = new Vector3(_params.WidthTop * 0.5f, halfH, _params.DepthTop * 0.5f) - pivot;
        Vector3 tBL = new Vector3(-_params.WidthTop * 0.5f, halfH, -_params.DepthTop * 0.5f) - pivot;
        Vector3 tBR = new Vector3(_params.WidthTop * 0.5f, halfH, -_params.DepthTop * 0.5f) - pivot;
        Vector3 bFL = new Vector3(-_params.WidthBottom * 0.5f, -halfH, _params.DepthBottom * 0.5f) - pivot;
        Vector3 bFR = new Vector3(_params.WidthBottom * 0.5f, -halfH, _params.DepthBottom * 0.5f) - pivot;
        Vector3 bBL = new Vector3(-_params.WidthBottom * 0.5f, -halfH, -_params.DepthBottom * 0.5f) - pivot;
        Vector3 bBR = new Vector3(_params.WidthBottom * 0.5f, -halfH, -_params.DepthBottom * 0.5f) - pivot;

        AddQuadFace(md, bFR, bBR, tBR, tFR, Vector3.right, _params.Subdivisions.z, _params.Subdivisions.y);
        AddQuadFace(md, bBL, bFL, tFL, tBL, Vector3.left, _params.Subdivisions.z, _params.Subdivisions.y);
        AddQuadFace(md, tFL, tFR, tBR, tBL, Vector3.up, _params.Subdivisions.x, _params.Subdivisions.z);
        AddQuadFace(md, bBL, bBR, bFR, bFL, Vector3.down, _params.Subdivisions.x, _params.Subdivisions.z);
        AddQuadFace(md, bFL, bFR, tFR, tFL, Vector3.forward, _params.Subdivisions.x, _params.Subdivisions.y);
        AddQuadFace(md, bBR, bBL, tBL, tBR, Vector3.back, _params.Subdivisions.x, _params.Subdivisions.y);

        return md;
    }

    private MeshData GenerateRoundedCubeMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float r = _params.CornerRadius;
        int seg = _params.CornerSegments;
        float halfH = _params.Height * 0.5f;

        float avgW = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float avgD = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivot = new Vector3(_params.Pivot.x * avgW, _params.Pivot.y * _params.Height, _params.Pivot.z * avgD);

        float inXT = _params.WidthTop * 0.5f - r;
        float inZT = _params.DepthTop * 0.5f - r;
        float inXB = _params.WidthBottom * 0.5f - r;
        float inZB = _params.DepthBottom * 0.5f - r;
        float inY = halfH - r;

        // === 8つの角（1/8球） ===
        AddCornerSphere(md, new Vector3(inXT, inY, inZT), new Vector3(1, 1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXT, inY, -inZT), new Vector3(-1, 1, -1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(inXT, inY, -inZT), new Vector3(1, 1, -1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(inXB, -inY, inZB), new Vector3(1, -1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXB, -inY, inZB), new Vector3(-1, -1, 1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-1, -1, -1), r, seg, pivot);
        AddCornerSphere(md, new Vector3(inXB, -inY, -inZB), new Vector3(1, -1, -1), r, seg, pivot);

        // === 12の辺（1/4円柱） ===
        AddEdgeCylinder(md, new Vector3(-inXT, inY, inZT), new Vector3(inXT, inY, inZT), new Vector3(0, 1, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXT, inY, inZT), new Vector3(inXT, inY, -inZT), new Vector3(1, 1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXT, inY, -inZT), new Vector3(-inXT, inY, -inZT), new Vector3(0, 1, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXT, inY, -inZT), new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, inZB), new Vector3(inXB, -inY, inZB), new Vector3(0, -1, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, inZB), new Vector3(inXB, -inY, -inZB), new Vector3(1, -1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, -inZB), new Vector3(-inXB, -inY, -inZB), new Vector3(0, -1, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-inXB, -inY, inZB), new Vector3(-1, -1, 0), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, inZB), new Vector3(inXT, inY, inZT), new Vector3(1, 0, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, inZB), new Vector3(-inXT, inY, inZT), new Vector3(-1, 0, 1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(inXB, -inY, -inZB), new Vector3(inXT, inY, -inZT), new Vector3(1, 0, -1), r, seg, pivot);
        AddEdgeCylinder(md, new Vector3(-inXB, -inY, -inZB), new Vector3(-inXT, inY, -inZT), new Vector3(-1, 0, -1), r, seg, pivot);

        // === 6つの面（平面部分） ===
        AddQuadFace(md,
            new Vector3(_params.WidthBottom * 0.5f, -inY, inZB) - pivot,
            new Vector3(_params.WidthBottom * 0.5f, -inY, -inZB) - pivot,
            new Vector3(_params.WidthTop * 0.5f, inY, -inZT) - pivot,
            new Vector3(_params.WidthTop * 0.5f, inY, inZT) - pivot,
            Vector3.right, _params.Subdivisions.z, _params.Subdivisions.y);
        AddQuadFace(md,
            new Vector3(-_params.WidthBottom * 0.5f, -inY, -inZB) - pivot,
            new Vector3(-_params.WidthBottom * 0.5f, -inY, inZB) - pivot,
            new Vector3(-_params.WidthTop * 0.5f, inY, inZT) - pivot,
            new Vector3(-_params.WidthTop * 0.5f, inY, -inZT) - pivot,
            Vector3.left, _params.Subdivisions.z, _params.Subdivisions.y);
        AddQuadFace(md,
            new Vector3(-inXT, halfH, inZT) - pivot,
            new Vector3(inXT, halfH, inZT) - pivot,
            new Vector3(inXT, halfH, -inZT) - pivot,
            new Vector3(-inXT, halfH, -inZT) - pivot,
            Vector3.up, _params.Subdivisions.x, _params.Subdivisions.z);
        AddQuadFace(md,
            new Vector3(-inXB, -halfH, -inZB) - pivot,
            new Vector3(inXB, -halfH, -inZB) - pivot,
            new Vector3(inXB, -halfH, inZB) - pivot,
            new Vector3(-inXB, -halfH, inZB) - pivot,
            Vector3.down, _params.Subdivisions.x, _params.Subdivisions.z);
        AddQuadFace(md,
            new Vector3(-inXB, -inY, _params.DepthBottom * 0.5f) - pivot,
            new Vector3(inXB, -inY, _params.DepthBottom * 0.5f) - pivot,
            new Vector3(inXT, inY, _params.DepthTop * 0.5f) - pivot,
            new Vector3(-inXT, inY, _params.DepthTop * 0.5f) - pivot,
            Vector3.forward, _params.Subdivisions.x, _params.Subdivisions.y);
        AddQuadFace(md,
            new Vector3(inXB, -inY, -_params.DepthBottom * 0.5f) - pivot,
            new Vector3(-inXB, -inY, -_params.DepthBottom * 0.5f) - pivot,
            new Vector3(-inXT, inY, -_params.DepthTop * 0.5f) - pivot,
            new Vector3(inXT, inY, -_params.DepthTop * 0.5f) - pivot,
            Vector3.back, _params.Subdivisions.x, _params.Subdivisions.y);

        return md;
    }

    // ================================================================
    // ヘルパーメソッド
    // ================================================================

    private void AddQuadFace(MeshData md, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, int divU, int divV)
    {
        int startIdx = md.VertexCount;

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

        int cols = divU + 1;
        for (int iv = 0; iv < divV; iv++)
        {
            for (int iu = 0; iu < divU; iu++)
            {
                int i0 = startIdx + iv * cols + iu;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }
    }

    private void AddCornerSphere(MeshData md, Vector3 center, Vector3 dir, float radius, int seg, Vector3 pivot)
    {
        int startIdx = md.VertexCount;

        bool reverseLon = (dir.x * dir.y * dir.z) < 0;

        for (int lat = 0; lat <= seg; lat++)
        {
            float latAngle = lat * (Mathf.PI * 0.5f) / seg;
            float cosLat = Mathf.Cos(latAngle);
            float sinLat = Mathf.Sin(latAngle);

            for (int lon = 0; lon <= seg; lon++)
            {
                float lonAngle = reverseLon
                    ? (seg - lon) * (Mathf.PI * 0.5f) / seg
                    : lon * (Mathf.PI * 0.5f) / seg;

                float localX = sinLat * Mathf.Cos(lonAngle);
                float localY = cosLat;
                float localZ = sinLat * Mathf.Sin(lonAngle);

                Vector3 n = new Vector3(localX * dir.x, localY * dir.y, localZ * dir.z).normalized;
                Vector3 pos = center + n * radius - pivot;
                Vector2 uv = new Vector2((float)lon / seg, (float)lat / seg);
                md.Vertices.Add(new Vertex(pos, uv, n));
            }
        }

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

    private void AddEdgeCylinder(MeshData md, Vector3 start, Vector3 end, Vector3 cornerDir, float radius, int seg, Vector3 pivot)
    {
        int startIdx = md.VertexCount;

        Vector3 axis = (end - start).normalized;

        Vector3 perpDir1, perpDir2;
        if (Mathf.Abs(axis.x) > 0.9f)
        {
            perpDir1 = new Vector3(0, cornerDir.y, 0).normalized;
            perpDir2 = new Vector3(0, 0, cornerDir.z).normalized;
        }
        else if (Mathf.Abs(axis.y) > 0.9f)
        {
            perpDir1 = new Vector3(cornerDir.x, 0, 0).normalized;
            perpDir2 = new Vector3(0, 0, cornerDir.z).normalized;
        }
        else
        {
            perpDir1 = new Vector3(cornerDir.x, 0, 0).normalized;
            perpDir2 = new Vector3(0, cornerDir.y, 0).normalized;
        }

        Vector3 cross = Vector3.Cross(perpDir1, perpDir2);
        bool reverseRing = Vector3.Dot(cross, axis) < 0;

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