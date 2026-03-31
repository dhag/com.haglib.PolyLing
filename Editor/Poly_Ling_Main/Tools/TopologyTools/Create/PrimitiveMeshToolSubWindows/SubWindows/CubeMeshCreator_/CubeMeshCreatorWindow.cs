// Assets/Editor/MeshCreators/CubeMeshCreatorWindow.cs
// 角を丸めた直方体メッシュ生成用のサブウインドウ（MeshCreatorWindowBase継承版）
// MeshObject（Vertex/Face）ベース対応版 - 四角形面で構築
// ローカライズ対応版
//
// 【頂点順序の規約】
// AddQuadFace入力: v0=(0,0)左下, v1=(1,0)右下, v2=(1,1)右上, v3=(0,1)左上
// 法線方向から見た座標系で指定する

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools.Creators;
using Poly_Ling.PrimitiveMesh;

public partial class CubeMeshCreatorWindow : MeshCreatorWindowBase<CubeMeshGenerator.CubeParams>
{
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
    protected override string PresetKey => "Cube";
    protected override string UndoDescription => "Cube Parameters";
    protected override float PreviewCameraDistance =>
        Mathf.Max(_params.WidthTop, _params.WidthBottom, _params.DepthTop, _params.DepthBottom, _params.Height) * 2.5f;

    protected override CubeMeshGenerator.CubeParams GetDefaultParams() => CubeMeshGenerator.CubeParams.Default;
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
    public static CubeMeshCreatorWindow Open(Action<MeshObject, string, bool> onMeshObjectCreated)
    {
        var window = GetWindow<CubeMeshCreatorWindow>(true, T("WindowTitle"), true);
        window.minSize = new Vector2(400, 750);
        window.maxSize = new Vector2(500, 950);
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

        // ====== 連動オプション ======
        EditorGUILayout.LabelField(T("LinkOptions"), EditorStyles.miniBoldLabel);

        bool prevLinkWHD = _params.LinkWHD;
        _params.LinkWHD = EditorGUILayout.Toggle(T("LinkWHD"), _params.LinkWHD);

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
            _params.LinkTopBottom = EditorGUILayout.Toggle(T("LinkTopBottom"), _params.LinkTopBottom);

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
            EditorGUILayout.LabelField(T("SizeLinked"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider(T("WidthX"), _params.WidthTop, 0.1f, 10f);
                float newHeight = EditorGUILayout.Slider(T("HeightY"), _params.Height, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider(T("DepthZ"), _params.DepthTop, 0.1f, 10f);

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
            EditorGUILayout.LabelField(T("Size"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                float newWidth = EditorGUILayout.Slider(T("WidthX"), _params.WidthTop, 0.1f, 10f);
                float newDepth = EditorGUILayout.Slider(T("DepthZ"), _params.DepthTop, 0.1f, 10f);

                _params.WidthTop = _params.WidthBottom = newWidth;
                _params.DepthTop = _params.DepthBottom = newDepth;
            }
        }
        else
        {
            EditorGUILayout.LabelField(T("SizeTop"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _params.WidthTop = EditorGUILayout.Slider(T("WidthX"), _params.WidthTop, 0.1f, 10f);
                _params.DepthTop = EditorGUILayout.Slider(T("DepthZ"), _params.DepthTop, 0.1f, 10f);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("SizeBottom"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _params.WidthBottom = EditorGUILayout.Slider(T("WidthX"), _params.WidthBottom, 0.1f, 10f);
                _params.DepthBottom = EditorGUILayout.Slider(T("DepthZ"), _params.DepthBottom, 0.1f, 10f);
            }
        }

        if (!_params.LinkWHD)
        {
            EditorGUILayout.Space(5);
            _params.Height = EditorGUILayout.Slider(T("HeightY"), _params.Height, 0.1f, 10f);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(T("Corner"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            float minSize = Mathf.Min(_params.WidthTop, _params.DepthTop, _params.WidthBottom, _params.DepthBottom, _params.Height);
            float maxRadius = minSize * 0.5f;
            _params.CornerRadius = EditorGUILayout.Slider(T("CornerRadius"), _params.CornerRadius, 0f, maxRadius);

            using (new EditorGUI.DisabledScope(_params.CornerRadius <= 0f))
            {
                _params.CornerSegments = EditorGUILayout.IntSlider(T("CornerSegments"), _params.CornerSegments, 1, 8);
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(T("Subdivisions"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Subdivisions.x = EditorGUILayout.IntSlider(T("SubdivX"), _params.Subdivisions.x, 1, 16);
            _params.Subdivisions.y = EditorGUILayout.IntSlider(T("SubdivY"), _params.Subdivisions.y, 1, 16);
            _params.Subdivisions.z = EditorGUILayout.IntSlider(T("SubdivZ"), _params.Subdivisions.z, 1, 16);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(T("PivotOffset"), EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            _params.Pivot.x = EditorGUILayout.Slider(T("PivotX"), _params.Pivot.x, -0.5f, 0.5f);
            _params.Pivot.y = EditorGUILayout.Slider(T("PivotY"), _params.Pivot.y, -0.5f, 0.5f);
            _params.Pivot.z = EditorGUILayout.Slider(T("PivotZ"), _params.Pivot.z, -0.5f, 0.5f);

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
    protected override MeshObject GenerateMeshObject() => CubeMeshGenerator.Generate(_params);
}
