// Assets/Editor/MeshCreators/PlaneMeshCreatorWindow.cs
// プレーングリッドメッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshObject（Vertex/Face）ベース対応版 - 四角形面で構築
// ローカライズ対応版
//
// 【頂点順序の規約】
// グリッド配置: i0=左下, i1=右下, i2=右上, i3=左上
// 表面: md.AddQuad(i0, i1, i2, i3)
// 裏面: md.AddQuad(i0, i3, i2, i1)

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools.Creators;
using Poly_Ling.PrimitiveMesh;

public partial class PlaneMeshCreatorWindow : MeshCreatorWindowBase<PlaneMeshGenerator.PlaneParams>
{
    

    // ================================================================
    // パラメータ構造体

    // ================================================================
    // 基底クラス実装
    // ================================================================
    protected override string WindowName => "PlaneCreator";
    protected override string PresetKey => "Plane";
    protected override string UndoDescription => "Plane Parameters";
    protected override float PreviewCameraDistance => Mathf.Max(_params.Width, _params.Height) * 2.5f;

    protected override PlaneMeshGenerator.PlaneParams GetDefaultParams() => PlaneMeshGenerator.PlaneParams.Default;
    protected override string GetMeshName() => _params.MeshName;
    protected override float GetPreviewRotationX() => _params.RotationX;
    protected override float GetPreviewRotationY() => _params.RotationY;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    public static PlaneMeshCreatorWindow Open(Action<MeshObject, string, bool> onMeshObjectCreated)
    {
        var window = GetWindow<PlaneMeshCreatorWindow>(true, T("WindowTitle"), true);
        window.minSize = new Vector2(400, 580);
        window.maxSize = new Vector2(500, 780);
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

        _params.Width = EditorGUILayout.Slider(T("Width"), _params.Width, 0.1f, 10f);
        _params.Height = EditorGUILayout.Slider(T("Height"), _params.Height, 0.1f, 10f);
        _params.WidthSegments = EditorGUILayout.IntSlider(T("WidthSegments"), _params.WidthSegments, 1, 32);
        _params.HeightSegments = EditorGUILayout.IntSlider(T("HeightSegments"), _params.HeightSegments, 1, 32);

        EditorGUILayout.Space(5);

        _params.Orientation = (Poly_Ling.PrimitiveMesh.PlaneOrientation)EditorGUILayout.EnumPopup(T("Orientation"), _params.Orientation);
        _params.DoubleSided = EditorGUILayout.Toggle(T("DoubleSided"), _params.DoubleSided);

        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField(T("PivotOffset"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider(T("PivotX"), _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider(T("PivotY"), _params.Pivot.y, -0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Center"), GUILayout.Width(60)))
            {
                _params.Pivot = Vector3.zero;
                GUI.changed = true;
            }
            if (GUILayout.Button(T("Corner"), GUILayout.Width(60)))
            {
                _params.Pivot = new Vector3(0.5f, 0.5f, 0);
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
    protected override MeshObject GenerateMeshObject() => PlaneMeshGenerator.Generate(_params);
}
