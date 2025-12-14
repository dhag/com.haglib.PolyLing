// Assets/Editor/SimpleMeshFactory_Symmetry.cs
// 対称モード関連（UI、ミラー描画）
// Phase1: 表示ミラーのみ

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Symmetry;

public partial class SimpleMeshFactory
{
    // ================================================================
    // フィールド
    // ================================================================

    // 後方互換プロパティ（ModelContextに移行）
    private SymmetrySettings _symmetrySettings => _model.SymmetrySettings;

    // ミラー描画用マテリアル
    private Material _mirrorMaterial;

    // UI状態
    private bool _foldSymmetry = true;

    // ================================================================
    // 対称設定プロパティ
    // ================================================================

    /// <summary>
    /// 対称設定へのアクセス
    /// </summary>
    public SymmetrySettings SymmetrySettings => _model.SymmetrySettings;

    // ================================================================
    // UI描画
    // ================================================================

    /// <summary>
    /// 対称モードUIを描画（Displayセクション内で呼び出し）
    /// </summary>
    private void DrawSymmetryUI()
    {
        _foldSymmetry = EditorGUILayout.Foldout(_foldSymmetry, "Symmetry (Mirror)", true);
        if (!_foldSymmetry) return;

        EditorGUI.indentLevel++;

        // 有効/無効トグル
        EditorGUI.BeginChangeCheck();
        bool newEnabled = EditorGUILayout.Toggle("Enable Mirror", _symmetrySettings.IsEnabled);
        if (EditorGUI.EndChangeCheck())
        {
            _symmetrySettings.IsEnabled = newEnabled;
            Repaint();
        }

        // 有効時のみ詳細設定を表示
        if (_symmetrySettings.IsEnabled)
        {
            EditorGUILayout.Space(2);

            // 軸選択
            EditorGUI.BeginChangeCheck();
            var newAxis = (SymmetryAxis)EditorGUILayout.EnumPopup("Axis", _symmetrySettings.Axis);
            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.Axis = newAxis;
                Repaint();
            }

            // 平面オフセット
            EditorGUI.BeginChangeCheck();
            float newOffset = EditorGUILayout.Slider("Plane Offset", _symmetrySettings.PlaneOffset, -1f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.PlaneOffset = newOffset;
                Repaint();
            }

            // オフセットリセットボタン
            if (Mathf.Abs(_symmetrySettings.PlaneOffset) > 0.001f)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset Offset", EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    _symmetrySettings.PlaneOffset = 0f;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(3);

            // 表示オプション
            EditorGUILayout.LabelField("Display Options", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            bool showMesh = EditorGUILayout.Toggle("Mirror UnityMesh", _symmetrySettings.ShowMirrorMesh);
            bool showWire = EditorGUILayout.Toggle("Mirror Wireframe", _symmetrySettings.ShowMirrorWireframe);
            bool showPlane = EditorGUILayout.Toggle("Symmetry Plane", _symmetrySettings.ShowSymmetryPlane);
            float alpha = EditorGUILayout.Slider("Mirror Alpha", _symmetrySettings.MirrorAlpha, 0.1f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.ShowMirrorMesh = showMesh;
                _symmetrySettings.ShowMirrorWireframe = showWire;
                _symmetrySettings.ShowSymmetryPlane = showPlane;
                _symmetrySettings.MirrorAlpha = alpha;
                Repaint();
            }
        }

        EditorGUI.indentLevel--;
    }

    // ================================================================
    // ミラーメッシュ描画
    // ================================================================

    /// <summary>
    /// ミラーメッシュを描画（PreviewRenderUtility用）
    /// </summary>
    private void DrawMirroredMesh(MeshContext meshContext, Mesh mesh)
    {
        if (!_symmetrySettings.IsEnabled || !_symmetrySettings.ShowMirrorMesh)
            return;

        if (mesh == null || _preview == null)
            return;

        Matrix4x4 mirrorMatrix = _symmetrySettings.GetMirrorMatrix();
        Material mirrorMat = GetMirrorMaterial();

        int subMeshCount = mesh.subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            // メッシュコンテキストのマテリアルがあればそれを使用、なければミラー用マテリアル
            Material baseMat = null;
            if (meshContext != null && i < meshContext.Materials.Count)
            {
                baseMat = meshContext.Materials[i];
            }

            Material mat = (baseMat != null) ? baseMat : mirrorMat;

            // ミラー行列を適用して描画
            // 注: 面の向きが反転するため、カリングを考慮する必要がある
            _preview.DrawMesh(mesh, mirrorMatrix, mat, i);
        }
    }

    /// <summary>
    /// ミラー用マテリアルを取得
    /// </summary>
    private Material GetMirrorMaterial()
    {
        if (_mirrorMaterial != null)
            return _mirrorMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _mirrorMaterial = new Material(shader);
            UpdateMirrorMaterialAlpha();
        }

        return _mirrorMaterial;
    }

    /// <summary>
    /// ミラーマテリアルの透明度を更新
    /// </summary>
    private void UpdateMirrorMaterialAlpha()
    {
        if (_mirrorMaterial == null) return;

        float alpha = _symmetrySettings.MirrorAlpha;
        Color col = new Color(0.6f, 0.6f, 0.7f, alpha);

        _mirrorMaterial.SetColor("_BaseColor", col);
        _mirrorMaterial.SetColor("_Color", col);

        // 透明度が1未満の場合は透明設定
        if (alpha < 0.99f)
        {
            _mirrorMaterial.SetFloat("_Surface", 1); // Transparent
            _mirrorMaterial.SetFloat("_Blend", 0);   // Alpha
            _mirrorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mirrorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mirrorMaterial.SetInt("_ZWrite", 0);
            _mirrorMaterial.renderQueue = 3000;
        }
    }

    // ================================================================
    // ミラーワイヤーフレーム描画
    // ================================================================

    /// <summary>
    /// ミラーワイヤーフレームを描画
    /// </summary>
    private void DrawMirroredWireframe(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (!_symmetrySettings.IsEnabled || !_symmetrySettings.ShowMirrorWireframe)
            return;

        if (meshData == null)
            return;

        Matrix4x4 mirrorMatrix = _symmetrySettings.GetMirrorMatrix();
        float alpha = _symmetrySettings.MirrorAlpha * 0.7f;  // ワイヤーフレームはやや薄く

        var edges = new HashSet<(int, int)>();
        var lines = new List<(int, int)>();

        // 各面からエッジを抽出
        foreach (var face in meshData.Faces)
        {
            if (face.VertexCount == 2)
            {
                lines.Add((face.VertexIndices[0], face.VertexIndices[1]));
            }
            else if (face.VertexCount >= 3)
            {
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % face.VertexCount];
                    AddEdge(edges, a, b);
                }
            }
        }

        Handles.BeginGUI();

        // 通常のエッジを描画（シアン、やや薄く）
        Handles.color = new Color(0f, 0.8f, 0.8f, alpha);
        foreach (var edge in edges)
        {
            Vector3 p1World = mirrorMatrix.MultiplyPoint3x4(meshData.Vertices[edge.Item1].Position);
            Vector3 p2World = mirrorMatrix.MultiplyPoint3x4(meshData.Vertices[edge.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        // 補助線を描画（ピンク、やや薄く）
        Handles.color = new Color(1f, 0.5f, 0.8f, alpha);
        foreach (var line in lines)
        {
            if (line.Item1 < 0 || line.Item1 >= meshData.VertexCount ||
                line.Item2 < 0 || line.Item2 >= meshData.VertexCount)
                continue;

            Vector3 p1World = mirrorMatrix.MultiplyPoint3x4(meshData.Vertices[line.Item1].Position);
            Vector3 p2World = mirrorMatrix.MultiplyPoint3x4(meshData.Vertices[line.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawAAPolyLine(3f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        Handles.EndGUI();
    }

    // ================================================================
    // 対称平面描画
    // ================================================================

    /// <summary>
    /// 対称平面を描画
    /// </summary>
    private void DrawSymmetryPlane(Rect previewRect, Vector3 camPos, Vector3 lookAt, Bounds meshBounds)
    {
        if (!_symmetrySettings.IsEnabled || !_symmetrySettings.ShowSymmetryPlane)
            return;

        Vector3 normal = _symmetrySettings.GetPlaneNormal();
        Vector3 planePoint = _symmetrySettings.GetPlanePoint();
        Color planeColor = _symmetrySettings.GetAxisColor();

        // 平面のサイズをメッシュバウンドに合わせる
        float planeSize = Mathf.Max(meshBounds.size.magnitude, 0.5f) * 0.6f;

        // 平面上の2つの軸を計算
        Vector3 axis1, axis2;
        switch (_symmetrySettings.Axis)
        {
            case SymmetryAxis.X:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Y:
                axis1 = Vector3.right;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Z:
                axis1 = Vector3.right;
                axis2 = Vector3.up;
                break;
            default:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
        }

        Handles.BeginGUI();

        // 平面の四隅
        Vector3[] corners = new Vector3[4];
        corners[0] = planePoint + (-axis1 - axis2) * planeSize * 0.5f;
        corners[1] = planePoint + (axis1 - axis2) * planeSize * 0.5f;
        corners[2] = planePoint + (axis1 + axis2) * planeSize * 0.5f;
        corners[3] = planePoint + (-axis1 + axis2) * planeSize * 0.5f;

        Vector2[] screenCorners = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            screenCorners[i] = WorldToPreviewPos(corners[i], previewRect, camPos, lookAt);
        }

        // 半透明の平面を描画
        Color fillColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.15f);
        DrawFilledPolygon(screenCorners, fillColor);

        // 枠線を描画
        Handles.color = new Color(planeColor.r, planeColor.g, planeColor.b, 0.6f);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Handles.DrawAAPolyLine(2f,
                new Vector3(screenCorners[i].x, screenCorners[i].y, 0),
                new Vector3(screenCorners[next].x, screenCorners[next].y, 0));
        }

        // 中心線（対称軸）を点線で描画
        Vector2 center = WorldToPreviewPos(planePoint, previewRect, camPos, lookAt);
        Vector2 axis1End = WorldToPreviewPos(planePoint + axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis1Start = WorldToPreviewPos(planePoint - axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2End = WorldToPreviewPos(planePoint + axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2Start = WorldToPreviewPos(planePoint - axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);

        Color lineColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.5f);
        DrawDottedLine(axis1Start, axis1End, lineColor);
        DrawDottedLine(axis2Start, axis2End, lineColor);

        // 中心マーカー
        if (previewRect.Contains(center))
        {
            float markerSize = 6f;
            EditorGUI.DrawRect(new Rect(
                center.x - markerSize / 2,
                center.y - markerSize / 2,
                markerSize,
                markerSize), planeColor);
        }

        Handles.EndGUI();
    }

    // ================================================================
    // クリーンアップ
    // ================================================================

    /// <summary>
    /// ミラー関連リソースをクリーンアップ
    /// </summary>
    private void CleanupMirrorResources()
    {
        if (_mirrorMaterial != null)
        {
            DestroyImmediate(_mirrorMaterial);
            _mirrorMaterial = null;
        }
    }
}
