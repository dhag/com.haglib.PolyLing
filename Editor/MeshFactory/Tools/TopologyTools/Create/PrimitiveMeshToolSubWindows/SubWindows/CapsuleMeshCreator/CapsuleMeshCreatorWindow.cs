// Assets/Editor/MeshCreators/CapsuleMeshCreatorWindow.cs
// カプセルメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
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

public class CapsuleMeshCreatorWindow : MeshCreatorWindowBase<CapsuleMeshCreatorWindow.CapsuleParams>
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct CapsuleParams : IEquatable<CapsuleParams>
    {
        public string MeshName;
        public float RadiusTop, RadiusBottom;
        public float Height;
        public int RadialSegments, HeightSegments, CapSegments;
        public Vector3 Pivot;
        public float RotationX, RotationY;

        public static CapsuleParams Default => new CapsuleParams
        {
            MeshName = "Capsule",
            RadiusTop = 0.5f,
            RadiusBottom = 0.5f,
            Height = 2f,
            RadialSegments = 24,
            HeightSegments = 4,
            CapSegments = 8,
            Pivot = Vector3.zero,
            RotationX = 20f,
            RotationY = 30f
        };

        public bool Equals(CapsuleParams o) =>
            MeshName == o.MeshName &&
            Mathf.Approximately(RadiusTop, o.RadiusTop) &&
            Mathf.Approximately(RadiusBottom, o.RadiusBottom) &&
            Mathf.Approximately(Height, o.Height) &&
            RadialSegments == o.RadialSegments &&
            HeightSegments == o.HeightSegments &&
            CapSegments == o.CapSegments &&
            Pivot == o.Pivot &&
            Mathf.Approximately(RotationX, o.RotationX) &&
            Mathf.Approximately(RotationY, o.RotationY);

        public override bool Equals(object obj) => obj is CapsuleParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "CapsuleCreator";
    protected override string UndoDescription => "Capsule Parameters";
    protected override float PreviewCameraDistance => _params.Height * 2f;

    protected override CapsuleParams GetDefaultParams() => CapsuleParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static CapsuleMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<CapsuleMeshCreatorWindow>(true, "Create Capsule UnityMesh", true);
        window.minSize = new Vector2(400, 580);
        window.maxSize = new Vector2(500, 780);
        window._onMeshDataCreated = onMeshDataCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField("Capsule Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField("Name", _params.MeshName);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Size", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadiusTop = EditorGUILayout.Slider("Radius Top", _params.RadiusTop, 0.1f, 2f);
            _params.RadiusBottom = EditorGUILayout.Slider("Radius Bottom", _params.RadiusBottom, 0.1f, 2f);

            float minHeight = _params.RadiusTop + _params.RadiusBottom;
            _params.Height = EditorGUILayout.Slider("Height", _params.Height, minHeight, 10f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("Segments", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadialSegments = EditorGUILayout.IntSlider("Radial", _params.RadialSegments, 8, 48);
            _params.HeightSegments = EditorGUILayout.IntSlider("Height", _params.HeightSegments, 1, 16);
            _params.CapSegments = EditorGUILayout.IntSlider("Cap", _params.CapSegments, 2, 16);
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
    // MeshData生成（四角形ベース）
    // ================================================================
    protected override MeshData GenerateMeshData()
    {
        var md = new MeshData(_params.MeshName);

        float cylinderHeight = _params.Height - _params.RadiusTop - _params.RadiusBottom;
        if (cylinderHeight < 0) cylinderHeight = 0;

        float halfHeight = _params.Height * 0.5f;
        Vector3 pivotOffset = new Vector3(0, _params.Pivot.y * _params.Height, 0);

        float cylinderTop = halfHeight - _params.RadiusTop;
        float cylinderBottom = -halfHeight + _params.RadiusBottom;

        float topCapUVHeight = _params.RadiusTop / _params.Height;
        float bottomCapUVHeight = _params.RadiusBottom / _params.Height;
        float cylinderUVHeight = cylinderHeight / _params.Height;

        int radialSegs = _params.RadialSegments;
        int capSegs = _params.CapSegments;
        int heightSegs = _params.HeightSegments;

        // === 上半球の頂点 ===
        int topCapStartIdx = md.VertexCount;
        for (int lat = 0; lat <= capSegs; lat++)
        {
            float theta = lat * (Mathf.PI * 0.5f) / capSegs;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= radialSegs; lon++)
            {
                float phi = lon * 2f * Mathf.PI / radialSegs;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                Vector3 normal = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                Vector3 pos = normal * _params.RadiusTop + new Vector3(0, cylinderTop, 0) - pivotOffset;

                float u = (float)lon / radialSegs;
                float v = 1f - (float)lat / capSegs * topCapUVHeight;
                md.Vertices.Add(new Vertex(pos, new Vector2(u, v), normal));
            }
        }

        // === 円筒部分の頂点 ===
        int cylinderStartIdx = md.VertexCount;
        for (int h = 0; h <= heightSegs; h++)
        {
            float t = (float)h / heightSegs;
            float y = cylinderTop - t * cylinderHeight;
            float radius = Mathf.Lerp(_params.RadiusTop, _params.RadiusBottom, t);

            for (int lon = 0; lon <= radialSegs; lon++)
            {
                float phi = lon * 2f * Mathf.PI / radialSegs;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                float slope = (_params.RadiusBottom - _params.RadiusTop) / (cylinderHeight > 0 ? cylinderHeight : 1f);
                Vector3 normal = new Vector3(cosPhi, slope, sinPhi).normalized;

                Vector3 pos = new Vector3(cosPhi * radius, y, sinPhi * radius) - pivotOffset;

                float u = (float)lon / radialSegs;
                float v = 1f - topCapUVHeight - t * cylinderUVHeight;
                md.Vertices.Add(new Vertex(pos, new Vector2(u, v), normal));
            }
        }

        // === 下半球の頂点 ===
        int bottomCapStartIdx = md.VertexCount;
        for (int lat = 0; lat <= capSegs; lat++)
        {
            float theta = Mathf.PI * 0.5f + lat * (Mathf.PI * 0.5f) / capSegs;
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);

            for (int lon = 0; lon <= radialSegs; lon++)
            {
                float phi = lon * 2f * Mathf.PI / radialSegs;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                Vector3 normal = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                Vector3 pos = normal * _params.RadiusBottom + new Vector3(0, cylinderBottom, 0) - pivotOffset;

                float u = (float)lon / radialSegs;
                float v = bottomCapUVHeight - (float)lat / capSegs * bottomCapUVHeight;
                md.Vertices.Add(new Vertex(pos, new Vector2(u, v), normal));
            }
        }

        int cols = radialSegs + 1;

        // === 上半球の四角形面 ===
        for (int lat = 0; lat < capSegs; lat++)
        {
            for (int lon = 0; lon < radialSegs; lon++)
            {
                int i0 = topCapStartIdx + lat * cols + lon;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // === 円筒部分の四角形面 ===
        for (int h = 0; h < heightSegs; h++)
        {
            for (int lon = 0; lon < radialSegs; lon++)
            {
                int i0 = cylinderStartIdx + h * cols + lon;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        // === 下半球の四角形面 ===
        for (int lat = 0; lat < capSegs; lat++)
        {
            for (int lon = 0; lon < radialSegs; lon++)
            {
                int i0 = bottomCapStartIdx + lat * cols + lon;
                int i1 = i0 + 1;
                int i2 = i0 + cols + 1;
                int i3 = i0 + cols;

                md.AddQuad(i0, i1, i2, i3);
            }
        }

        return md;
    }
}
