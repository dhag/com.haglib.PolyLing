// Assets/Editor/MeshCreators/CylinderMeshCreatorWindow.cs
// シリンダーメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
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

public class CylinderMeshCreatorWindow : MeshCreatorWindowBase<CylinderMeshCreatorWindow.CylinderParams>
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct CylinderParams : IEquatable<CylinderParams>
    {
        public string MeshName;
        public float RadiusTop, RadiusBottom;
        public float Height;
        public int RadialSegments, HeightSegments;
        public bool CapTop, CapBottom;
        public float EdgeRadius;
        public int EdgeSegments;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static CylinderParams Default => new CylinderParams
        {
            MeshName = "Cylinder",
            RadiusTop = 0.5f,
            RadiusBottom = 0.5f,
            Height = 2f,
            RadialSegments = 24,
            HeightSegments = 4,
            CapTop = true,
            CapBottom = true,
            EdgeRadius = 0f,
            EdgeSegments = 4,
            Pivot = Vector3.zero,
            RotationX = 20f,
            RotationY = 30f
        };

        public bool Equals(CylinderParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(RadiusTop, o.RadiusTop) &&
            Mathf.Approximately(RadiusBottom, o.RadiusBottom) &&
            Mathf.Approximately(Height, o.Height) &&
            RadialSegments == o.RadialSegments &&
            HeightSegments == o.HeightSegments &&
            CapTop == o.CapTop &&
            CapBottom == o.CapBottom &&
            Mathf.Approximately(EdgeRadius, o.EdgeRadius) &&
            EdgeSegments == o.EdgeSegments &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is CylinderParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "CylinderCreator";
    protected override string UndoDescription => "Cylinder Parameters";
    protected override float PreviewCameraDistance => Mathf.Max(_params.Height, Mathf.Max(_params.RadiusTop, _params.RadiusBottom) * 2f) * 2f;

    protected override CylinderParams GetDefaultParams() => CylinderParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static CylinderMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<CylinderMeshCreatorWindow>(true, "Create Cylinder UnityMesh", true);
        window.minSize = new Vector2(400, 650);
        window.maxSize = new Vector2(500, 850);
        window._onMeshDataCreated = onMeshDataCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField("Cylinder Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Size", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadiusTop = EditorGUILayout.Slider("Radius Top", _params.RadiusTop, 0f, 5f);
            _params.RadiusBottom = EditorGUILayout.Slider("Radius Bottom", _params.RadiusBottom, 0f, 5f);
            _params.Height = EditorGUILayout.Slider("Height", _params.Height, 0.1f, 10f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Segments", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadialSegments = EditorGUILayout.IntSlider("Radial", _params.RadialSegments, 3, 48);
            _params.HeightSegments = EditorGUILayout.IntSlider("Height", _params.HeightSegments, 1, 32);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Edge Rounding", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            float maxEdgeRadius = _params.Height * 0.5f;
            if (_params.CapTop && _params.RadiusTop > 0)
                maxEdgeRadius = Mathf.Min(maxEdgeRadius, _params.RadiusTop);
            if (_params.CapBottom && _params.RadiusBottom > 0)
                maxEdgeRadius = Mathf.Min(maxEdgeRadius, _params.RadiusBottom);

            _params.EdgeRadius = EditorGUILayout.Slider("Radius", _params.EdgeRadius, 0f, maxEdgeRadius);

            using (new EditorGUI.DisabledScope(_params.EdgeRadius <= 0f))
            {
                _params.EdgeSegments = EditorGUILayout.IntSlider("Segments", _params.EdgeSegments, 1, 16);
            }
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Caps", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.CapTop = EditorGUILayout.Toggle("Top", _params.CapTop);
            _params.CapBottom = EditorGUILayout.Toggle("Bottom", _params.CapBottom);
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
        var md = new MeshData(_params.MeshName);

        Vector3 pivotOffset = new Vector3(0, _params.Pivot.y * _params.Height, 0);

        if (_params.EdgeRadius > 0)
        {
            GenerateRoundedCylinder(md, pivotOffset);
        }
        else
        {
            GenerateSimpleCylinder(md, pivotOffset);
        }

        return md;
    }

    private void GenerateSimpleCylinder(MeshData md, Vector3 pivotOffset)
    {
        float halfHeight = _params.Height * 0.5f;
        int radialSegs = _params.RadialSegments;
        int heightSegs = _params.HeightSegments;
        int cols = radialSegs + 1;

        // === 側面の頂点 ===
        int sideStartIdx = md.VertexCount;
        for (int h = 0; h <= heightSegs; h++)
        {
            float t = (float)h / heightSegs;
            float y = halfHeight - t * _params.Height;
            float radius = Mathf.Lerp(_params.RadiusTop, _params.RadiusBottom, t);

            for (int r = 0; r <= radialSegs; r++)
            {
                float angle = r * 2f * Mathf.PI / radialSegs;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                float slope = (_params.RadiusBottom - _params.RadiusTop) / _params.Height;
                Vector3 normal = new Vector3(cos, slope, sin).normalized;
                Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;

                float u = (float)r / radialSegs;
                float v = 1f - t;
                md.Vertices.Add(new Vertex(pos, new Vector2(u, v), normal));
            }
        }

        // === 側面の四角形 ===
        for (int h = 0; h < heightSegs; h++)
        {
            for (int r = 0; r < radialSegs; r++)
            {
                int i0 = sideStartIdx + h * cols + r;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // === 上キャップ ===
        if (_params.CapTop && _params.RadiusTop > 0)
        {
            GenerateCapSimple(md, halfHeight, _params.RadiusTop, true, pivotOffset);
        }

        // === 下キャップ ===
        if (_params.CapBottom && _params.RadiusBottom > 0)
        {
            GenerateCapSimple(md, -halfHeight, _params.RadiusBottom, false, pivotOffset);
        }
    }

    private void GenerateCapSimple(MeshData md, float y, float radius, bool isTop, Vector3 pivotOffset)
    {
        int centerIdx = md.VertexCount;
        Vector3 centerPos = new Vector3(0, y, 0) - pivotOffset;
        Vector3 normal = isTop ? Vector3.up : Vector3.down;

        md.Vertices.Add(new Vertex(centerPos, new Vector2(0.5f, 0.5f), normal));

        for (int r = 0; r <= _params.RadialSegments; r++)
        {
            float angle = r * 2f * Mathf.PI / _params.RadialSegments;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;
            Vector2 uv = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);
            md.Vertices.Add(new Vertex(pos, uv, normal));
        }

        for (int r = 0; r < _params.RadialSegments; r++)
        {
            int v0 = centerIdx;
            int v1 = centerIdx + 1 + r;
            int v2 = centerIdx + 1 + r + 1;

            if (isTop)
            {
                md.AddTriangle(v0, v2, v1);
            }
            else
            {
                md.AddTriangle(v0, v1, v2);
            }
        }
    }

    private void GenerateRoundedCylinder(MeshData md, Vector3 pivotOffset)
    {
        float halfHeight = _params.Height * 0.5f;
        float edgeR = _params.EdgeRadius;
        int edgeSeg = _params.EdgeSegments;
        float innerHalfHeight = halfHeight - edgeR;
        int radialSegs = _params.RadialSegments;
        int cols = radialSegs + 1;

        // === 上部角丸め ===
        if (_params.CapTop && _params.RadiusTop > 0 && edgeR > 0)
        {
            int topEdgeStartIdx = md.VertexCount;
            float torusCenterRadius = _params.RadiusTop - edgeR;

            for (int e = 0; e <= edgeSeg; e++)
            {
                float angle = (float)e / edgeSeg * Mathf.PI * 0.5f;
                float y = innerHalfHeight + Mathf.Sin(angle) * edgeR;
                float currentRadius = torusCenterRadius + Mathf.Cos(angle) * edgeR;

                for (int r = 0; r <= radialSegs; r++)
                {
                    float radAngle = r * 2f * Mathf.PI / radialSegs;
                    float cos = Mathf.Cos(radAngle);
                    float sin = Mathf.Sin(radAngle);

                    Vector3 pos = new Vector3(cos * currentRadius, y, sin * currentRadius) - pivotOffset;
                    Vector3 torusNormal = new Vector3(
                        cos * Mathf.Cos(angle),
                        Mathf.Sin(angle),
                        sin * Mathf.Cos(angle)
                    ).normalized;

                    float u = (float)r / radialSegs;
                    float v = 1f - (float)e / edgeSeg * (edgeR / _params.Height) * 0.5f;
                    md.Vertices.Add(new Vertex(pos, new Vector2(u, v), torusNormal));
                }
            }

            for (int e = 0; e < edgeSeg; e++)
            {
                for (int r = 0; r < radialSegs; r++)
                {
                    int i0 = topEdgeStartIdx + e * cols + r;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols + 1;
                    int i3 = i0 + cols;

                    md.AddQuad(i0, i3, i2, i1);
                }
            }
        }

        // === 側面 ===
        int sideStartIdx = md.VertexCount;
        float sideTop = (_params.CapTop && _params.RadiusTop > 0 && edgeR > 0) ? innerHalfHeight : halfHeight;
        float sideBottom = (_params.CapBottom && _params.RadiusBottom > 0 && edgeR > 0) ? -innerHalfHeight : -halfHeight;
        float sideHeight = sideTop - sideBottom;

        for (int h = 0; h <= _params.HeightSegments; h++)
        {
            float t = (float)h / _params.HeightSegments;
            float y = sideTop - t * sideHeight;
            float radius = Mathf.Lerp(_params.RadiusTop, _params.RadiusBottom, t);

            for (int r = 0; r <= radialSegs; r++)
            {
                float angle = r * 2f * Mathf.PI / radialSegs;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                float slope = (_params.RadiusBottom - _params.RadiusTop) / _params.Height;
                Vector3 normal = new Vector3(cos, slope, sin).normalized;
                Vector3 pos = new Vector3(cos * radius, y, sin * radius) - pivotOffset;

                float vTop = (_params.CapTop && _params.RadiusTop > 0 && edgeR > 0) ? (1f - edgeR / _params.Height * 0.5f) : 1f;
                float vBottom = (_params.CapBottom && _params.RadiusBottom > 0 && edgeR > 0) ? (edgeR / _params.Height * 0.5f) : 0f;
                float v = Mathf.Lerp(vTop, vBottom, t);
                md.Vertices.Add(new Vertex(pos, new Vector2((float)r / radialSegs, v), normal));
            }
        }

        for (int h = 0; h < _params.HeightSegments; h++)
        {
            for (int r = 0; r < radialSegs; r++)
            {
                int i0 = sideStartIdx + h * cols + r;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // === 下部角丸め ===
        if (_params.CapBottom && _params.RadiusBottom > 0 && edgeR > 0)
        {
            int bottomEdgeStartIdx = md.VertexCount;
            float torusCenterRadius = _params.RadiusBottom - edgeR;

            for (int e = 0; e <= edgeSeg; e++)
            {
                float angle = (float)e / edgeSeg * Mathf.PI * 0.5f;
                float y = -innerHalfHeight - Mathf.Sin(angle) * edgeR;
                float currentRadius = torusCenterRadius + Mathf.Cos(angle) * edgeR;

                for (int r = 0; r <= radialSegs; r++)
                {
                    float radAngle = r * 2f * Mathf.PI / radialSegs;
                    float cos = Mathf.Cos(radAngle);
                    float sin = Mathf.Sin(radAngle);

                    Vector3 pos = new Vector3(cos * currentRadius, y, sin * currentRadius) - pivotOffset;
                    Vector3 torusNormal = new Vector3(
                        cos * Mathf.Cos(angle),
                        -Mathf.Sin(angle),
                        sin * Mathf.Cos(angle)
                    ).normalized;

                    float u = (float)r / radialSegs;
                    float v = (float)e / edgeSeg * (edgeR / _params.Height) * 0.5f;
                    md.Vertices.Add(new Vertex(pos, new Vector2(u, v), torusNormal));
                }
            }

            for (int e = 0; e < edgeSeg; e++)
            {
                for (int r = 0; r < radialSegs; r++)
                {
                    int i0 = bottomEdgeStartIdx + e * cols + r;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols + 1;
                    int i3 = i0 + cols;

                    md.AddQuad(i0, i1, i2, i3);
                }
            }
        }

        // === キャップ ===
        if (_params.CapTop && _params.RadiusTop > 0)
        {
            float capRadius = edgeR > 0 ? _params.RadiusTop - edgeR : _params.RadiusTop;
            GenerateCapSimple(md, halfHeight, capRadius, true, pivotOffset);
        }

        if (_params.CapBottom && _params.RadiusBottom > 0)
        {
            float capRadius = edgeR > 0 ? _params.RadiusBottom - edgeR : _params.RadiusBottom;
            GenerateCapSimple(md, -halfHeight, capRadius, false, pivotOffset);
        }
    }
}
