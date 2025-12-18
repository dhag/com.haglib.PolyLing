// Assets/Editor/MeshCreators/PyramidMeshCreatorWindow.cs
// 角錐メッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshData（Vertex/Face）ベース対応版 - 三角形面で構築
//
// 【頂点順序の規約】
// 三角形: md.AddTriangle(v0, v1, v2) - 時計回りが表面

using System;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;

public class PyramidMeshCreatorWindow : MeshCreatorWindowBase<PyramidMeshCreatorWindow.PyramidParams>
{
    // ================================================================
    // パラメータ構造体
    // ================================================================
    public struct PyramidParams : IEquatable<PyramidParams>
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
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "PyramidCreator";
    protected override string UndoDescription => "Pyramid Parameters";
    protected override float PreviewCameraDistance => Mathf.Max(_params.Height, _params.BaseRadius * 2f) * 2.5f;

    protected override PyramidParams GetDefaultParams() => PyramidParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static PyramidMeshCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<PyramidMeshCreatorWindow>(true, "Create Pyramid UnityMesh", true);
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
        EditorGUILayout.LabelField("Pyramid Parameters", EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

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
    // MeshData生成（三角形ベース）
    // ================================================================
    protected override MeshData GenerateMeshData()
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
