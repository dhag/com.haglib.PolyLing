// Assets/Editor/MeshCreators/CylinderMeshCreatorWindow.cs
// シリンダーメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshObject（Vertex/Face）ベース対応版 - 四角形面で構築
// ローカライズ対応版
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools.Creators;
using Poly_Ling.PrimitiveMesh;

public partial class CylinderMeshCreatorWindow : MeshCreatorWindowBase<CylinderMeshGenerator.CylinderParams>
{
    // ================================================================
    // パラメータ構造体

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "CylinderCreator";
    protected override string PresetKey => "Cylinder";
    protected override string UndoDescription => "Cylinder Parameters";
    protected override float PreviewCameraDistance => Mathf.Max(_params.Height, Mathf.Max(_params.RadiusTop, _params.RadiusBottom) * 2f) * 2f;

    protected override CylinderMeshGenerator.CylinderParams GetDefaultParams() => CylinderMeshGenerator.CylinderParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static CylinderMeshCreatorWindow Open(Action<MeshObject, string, bool> onMeshObjectCreated)
    {
        var window = GetWindow<CylinderMeshCreatorWindow>(true, T("WindowTitle"), true);
        window.minSize = new Vector2(400, 650);
        window.maxSize = new Vector2(500, 850);
        window._onMeshObjectCreated = onMeshObjectCreated;
        window.UpdatePreviewMesh();
        return window;
    }

    // ================================================================
    // パラメータUI
    // ================================================================
    protected override void DrawParametersUI()
    {
        EditorGUILayout.LabelField(T("Parameters"), EditorStyles.boldLabel);

        DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        BeginParamChange();

        _params.MeshName = EditorGUILayout.TextField(T("Name"), _params.MeshName);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("Size"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadiusTop = EditorGUILayout.Slider(T("RadiusTop"), _params.RadiusTop, 0f, 5f);
            _params.RadiusBottom = EditorGUILayout.Slider(T("RadiusBottom"), _params.RadiusBottom, 0f, 5f);
            _params.Height = EditorGUILayout.Slider(T("Height"), _params.Height, 0.1f, 10f);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("Segments"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.RadialSegments = EditorGUILayout.IntSlider(T("Radial"), _params.RadialSegments, 3, 48);
            _params.HeightSegments = EditorGUILayout.IntSlider(T("HeightSeg"), _params.HeightSegments, 1, 32);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("EdgeRounding"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            float maxEdgeRadius = _params.Height * 0.5f;
            if (_params.CapTop && _params.RadiusTop > 0)
                maxEdgeRadius = Mathf.Min(maxEdgeRadius, _params.RadiusTop);
            if (_params.CapBottom && _params.RadiusBottom > 0)
                maxEdgeRadius = Mathf.Min(maxEdgeRadius, _params.RadiusBottom);

            _params.EdgeRadius = EditorGUILayout.Slider(T("EdgeRadius"), _params.EdgeRadius, 0f, maxEdgeRadius);

            using (new EditorGUI.DisabledScope(_params.EdgeRadius <= 0f))
            {
                _params.EdgeSegments = EditorGUILayout.IntSlider(T("EdgeSegments"), _params.EdgeSegments, 1, 16);
            }
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("Caps"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.CapTop = EditorGUILayout.Toggle(T("CapTop"), _params.CapTop);
            _params.CapBottom = EditorGUILayout.Toggle(T("CapBottom"), _params.CapBottom);
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("PivotOffset"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.y = EditorGUILayout.Slider(T("PivotY"), _params.Pivot.y, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Bottom"), GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0, -0.5f, 0);
                GUI.changed = true;
            }
            if (GUILayout.Button(T("Center"), GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button(T("Top"), GUILayout.Width(60)))
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
        EditorGUILayout.LabelField(T("Preview"), EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

        if (_previewMeshObject != null)
        {
            EditorGUILayout.LabelField(
                T("VertsFaces", _previewMeshObject.VertexCount, _previewMeshObject.FaceCount),
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
    // MeshObject生成

    // ================================================================
    // MeshObject生成
    // ================================================================
    protected override MeshObject GenerateMeshObject() => CylinderMeshGenerator.Generate(_params);
}
