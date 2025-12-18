// Assets/Editor/MeshCreators/NohMaskMeshCreatorWindow.cs
// 能面（Noh Mask）ベースメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshData（Vertex/Face）ベース対応版 - 四角形面で構築
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;

public class NohMaskMeshCreatorWindow : MeshCreatorWindowBase<NohMaskMeshCreatorWindow.NohMaskParams>
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct NohMaskParams : IEquatable<NohMaskParams>
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
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "NohMaskCreator";
    protected override string UndoDescription => "NohMask Parameters";
    protected override float PreviewCameraDistance => _params.Height * 4f;

    protected override NohMaskParams GetDefaultParams() => NohMaskParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // プレビューマテリアル色を肌色系に
    protected override void OnInitialize()
    {
        if (_previewMaterial != null)
        {
            _previewMaterial.SetColor("_BaseColor", new Color(0.9f, 0.85f, 0.75f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.9f, 0.85f, 0.75f, 1f));
        }
    }

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static NohMaskMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<NohMaskMeshCreatorWindow>(true, "Create Noh Mask UnityMesh", true);
        window.minSize = new Vector2(420, 800);
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
        EditorGUILayout.LabelField("Noh Mask Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

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
            _params.VerticalSegments = EditorGUILayout.IntSlider("Vertical", _params.VerticalSegments, 4, 32);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Transform", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.FlipY = EditorGUILayout.Toggle("Flip Y", _params.FlipY);
            _params.FlipZ = EditorGUILayout.Toggle("Flip Z", _params.FlipZ);
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
    // MeshData生成（四角形ベース）
    // ================================================================
    protected override MeshData GenerateMeshData()
    {
        var md = new MeshData(_params.MeshName);

        int hSegs = _params.HorizontalSegments;
        int vSegs = _params.VerticalSegments;
        int cols = hSegs + 1;

        float avgDepth = (_params.DepthTop + _params.DepthBottom) * 0.5f;
        Vector3 pivotOffset = new Vector3(0, 0, _params.Pivot.z * avgDepth * 2f);

        // 頂点グリッドを生成
        for (int v = 0; v <= vSegs; v++)
        {
            float ty = (float)v / vSegs;
            float normalizedY = ty * 2f - 1f;
            float y = normalizedY * _params.Height;

            float taperT = _params.FlipZ ? (1f - ty) : ty;
            float currentWidth = Mathf.Lerp(_params.WidthBottom, _params.WidthTop, taperT);
            float currentDepth = Mathf.Lerp(_params.DepthBottom, _params.DepthTop, taperT);

            for (int h = 0; h <= hSegs; h++)
            {
                float tx = (float)h / hSegs;
                float normalizedX = tx * 2f - 1f;

                float ellipseRadius = GetEllipseRadius(normalizedX, normalizedY);
                float x = normalizedX * currentWidth * ellipseRadius;

                float baseDepth = GetBaseDepth(normalizedX, normalizedY, ellipseRadius, currentDepth);
                float noseDepth = GetNoseDepth(normalizedX, normalizedY, currentWidth);

                float topCurveValue = _params.FlipZ ? _params.BottomCurve : _params.TopCurve;
                float bottomCurveValue = _params.FlipZ ? _params.TopCurve : _params.BottomCurve;
                float topCurve = GetTopCurve(normalizedY, topCurveValue);
                float bottomCurve = GetBottomCurve(normalizedY, bottomCurveValue);

                float z = baseDepth + noseDepth + topCurve + bottomCurve;

                Vector3 pos = new Vector3(x, y, z) - pivotOffset;

                if (_params.FlipY)
                {
                    pos = new Vector3(-pos.x, pos.y, -pos.z);
                }

                if (_params.FlipZ)
                {
                    pos = new Vector3(-pos.x, -pos.y, pos.z);
                }

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

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // 法線を再計算
        md.RecalculateSmoothNormals();

        return md;
    }

    private float GetEllipseRadius(float nx, float ny)
    {
        float topNarrow = 1f - ny * 0.15f;
        float cheekBulge = 1f + Mathf.Max(0, (1f - Mathf.Abs(ny + 0.2f) * 2f)) * 0.1f;
        return topNarrow * cheekBulge;
    }

    private float GetBaseDepth(float nx, float ny, float ellipseRadius, float depth)
    {
        float distFromCenter = Mathf.Sqrt(nx * nx + ny * ny);
        float falloff = 1f - Mathf.Clamp01(distFromCenter);
        falloff = Mathf.Pow(falloff, 0.7f);

        float horizontalCurve = 1f - nx * nx;
        horizontalCurve = Mathf.Pow(Mathf.Max(0, horizontalCurve), 0.5f);

        return depth * falloff * horizontalCurve;
    }

    private float GetNoseDepth(float nx, float ny, float currentWidth)
    {
        float noseY = _params.NosePosition / _params.Height;
        float noseCenterY = noseY;
        float noseTopY = noseCenterY + (_params.NoseLength / _params.Height) * 0.5f;
        float noseBottomY = noseCenterY - (_params.NoseLength / _params.Height) * 0.5f;

        float yInfluence = 0f;
        if (ny >= noseBottomY && ny <= noseTopY)
        {
            float localY = (ny - noseBottomY) / (noseTopY - noseBottomY);
            yInfluence = Mathf.Sin(localY * Mathf.PI);
            yInfluence *= 1f - localY * 0.3f;
        }

        float avgWidth = (_params.WidthTop + _params.WidthBottom) * 0.5f;
        float noseWidthNorm = _params.NoseWidth / avgWidth;
        float absNx = Mathf.Abs(nx);

        float xInfluence;
        if (absNx <= noseWidthNorm)
        {
            float t = absNx / noseWidthNorm;
            xInfluence = 1f - t * t * 0.3f;
        }
        else
        {
            float falloff = (absNx - noseWidthNorm) / (noseWidthNorm * 0.5f);
            xInfluence = 0.7f * Mathf.Max(0f, 1f - falloff);
        }

        return _params.NoseHeight * xInfluence * yInfluence;
    }

    private float GetTopCurve(float ny, float curveValue)
    {
        if (ny > 0.5f)
        {
            float t = (ny - 0.5f) / 0.5f;
            return curveValue * t * t;
        }
        return 0f;
    }

    private float GetBottomCurve(float ny, float curveValue)
    {
        if (ny < -0.5f)
        {
            float t = (-0.5f - ny) / 0.5f;
            return curveValue * t * t;
        }
        return 0f;
    }
}
