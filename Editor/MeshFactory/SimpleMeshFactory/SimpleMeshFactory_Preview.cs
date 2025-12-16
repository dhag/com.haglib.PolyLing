// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_Preview.cs
// プレビュー描画（ワイヤーフレーム、選択オーバーレイ、頂点ハンドル）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using MeshFactory.Rendering;
using static MeshFactory.Gizmo.GLGizmoDrawer;

public partial class SimpleMeshFactory
{
    // ================================================================
    // エッジキャッシュ（Phase 2 GPU描画で使用）
    // ================================================================

    private MeshEdgeCache _edgeCache;

    /// <summary>
    /// 描画キャッシュを初期化（OnEnableで呼び出し）
    /// </summary>
    private void InitializeDrawCache()
    {
        _edgeCache = new MeshEdgeCache();
    }

    /// <summary>
    /// 描画キャッシュをクリーンアップ（OnDisableで呼び出し）
    /// </summary>
    private void CleanupDrawCache()
    {
        _edgeCache?.Clear();

        if (_polygonMaterial != null)
        {
            DestroyImmediate(_polygonMaterial);
            _polygonMaterial = null;
        }
    }

    /// <summary>
    /// エッジキャッシュを無効化（メッシュ変更時に呼び出し）
    /// <summary>
    /// エッジキャッシュを無効化（メッシュ変更時に呼び出し）
    /// </summary>
    private void InvalidateDrawCache()
    {
        _edgeCache?.Invalidate();
    }

    // ================================================================
    // 中央ペイン：プレビュー
    // ================================================================

    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(
            200, 10000,
            200, 10000,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null || _preview == null)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.LabelField(rect, "Select a mesh", EditorStyles.centeredGreyMiniLabel);
            UnityEditor_Handles.EndGUI();
            return;
        }

        var mesh = meshContext.UnityMesh;

        float dist = _cameraDistance;
        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
        Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -dist);

        HandleInput(rect, meshContext, camPos, _cameraTarget, dist);

        if (Event.current.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        _preview.camera.transform.position = camPos;
        Quaternion lookRot = Quaternion.LookRotation(_cameraTarget - camPos, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        _preview.camera.transform.rotation = lookRot * rollRot;

        // メッシュ描画
        if (_showSelectedMeshOnly)
        {
            DrawMeshWithMaterials(meshContext, mesh);
        }
        else
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var ctx = _meshContextList[i];
                if (ctx?.UnityMesh == null) continue;

                bool isSelected = (i == _selectedIndex);
                DrawMeshWithMaterials(ctx, ctx.UnityMesh, isSelected ? 1f : 0.5f);
            }
        }

        // ミラーメッシュ描画
        if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
        {
            DrawMirroredMesh(meshContext, mesh);
        }

        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        // ワイヤーフレーム描画
        if (_showWireframe)
        {
            if (_showSelectedMeshOnly)
            {
                DrawWireframeOverlay(rect, meshContext.Data, camPos, _cameraTarget, true);
            }
            else
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    var ctx = _meshContextList[i];
                    if (ctx?.Data == null) continue;
                    bool isActive = (i == _selectedIndex);
                    DrawWireframeOverlay(rect, ctx.Data, camPos, _cameraTarget, isActive);
                }
            }

            // ミラーワイヤーフレーム描画
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                DrawMirroredWireframe(rect, meshContext.Data, camPos, _cameraTarget);
            }
        }

        // 選択状態のオーバーレイ描画
        DrawSelectionOverlay(rect, meshContext.Data, camPos, _cameraTarget);

        // 頂点描画
        if (_showSelectedMeshOnly)
        {
            DrawVertexHandles(rect, meshContext.Data, camPos, _cameraTarget, true);
        }
        else
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var ctx = _meshContextList[i];
                if (ctx?.Data == null) continue;
                bool isActive = (i == _selectedIndex);
                DrawVertexHandles(rect, ctx.Data, camPos, _cameraTarget, isActive);
            }
        }

        // ローカル原点マーカー
        DrawOriginMarker(rect, camPos, _cameraTarget);

        // ツールのギズモ描画
        UpdateToolContext(meshContext, rect, camPos, dist);
        _currentTool?.DrawGizmo(_toolContext);

        // WorkPlaneギズモ描画
        if (_showWorkPlaneGizmo && _vertexEditMode && _currentTool == _addFaceTool)
        {
            DrawWorkPlaneGizmo(rect, camPos, _cameraTarget);
        }

        // 対称平面ギズモ描画
        if (_symmetrySettings != null && _symmetrySettings.IsEnabled && _symmetrySettings.ShowSymmetryPlane)
        {
            Bounds meshBounds = meshContext.Data != null ? meshContext.Data.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            DrawSymmetryPlane(rect, camPos, _cameraTarget, meshBounds);
        }
    }

    // ================================================================
    // メッシュ描画
    // ================================================================

    /// <summary>
    /// マルチマテリアル対応でメッシュを描画
    /// </summary>
    private void DrawMeshWithMaterials(MeshContext meshContext, Mesh mesh, float alpha = 1f)
    {
        if (mesh == null)
            return;

        int subMeshCount = mesh.subMeshCount;
        Material defaultMat = GetPreviewMaterial();

        for (int i = 0; i < subMeshCount; i++)
        {
            Material mat = null;
            if (meshContext != null && i < meshContext.Materials.Count)
            {
                mat = meshContext.Materials[i];
            }

            if (mat == null)
            {
                mat = defaultMat;
            }

            _preview.DrawMesh(mesh, Matrix4x4.identity, mat, i);
        }
    }

    /// <summary>
    /// ローカル原点マーカーを点線で描画
    /// </summary>
    private void DrawOriginMarker(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        Vector3 origin = Vector3.zero;
        Vector2 originScreen = WorldToPreviewPos(origin, previewRect, camPos, lookAt);

        if (!previewRect.Contains(originScreen))
            return;

        float axisLength = 0.2f;

        // X軸（赤）点線
        Vector3 xEnd = origin + Vector3.right * axisLength;
        Vector2 xScreen = WorldToPreviewPos(xEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, xScreen, new Color(1f, 0.3f, 0.3f, 0.7f));

        // Y軸（緑）点線
        Vector3 yEnd = origin + Vector3.up * axisLength;
        Vector2 yScreen = WorldToPreviewPos(yEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, yScreen, new Color(0.3f, 1f, 0.3f, 0.7f));

        // Z軸（青）点線
        Vector3 zEnd = origin + Vector3.forward * axisLength;
        Vector2 zScreen = WorldToPreviewPos(zEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, zScreen, new Color(0.3f, 0.3f, 1f, 0.7f));

        UnityEditor_Handles.BeginGUI();
        float centerSize = 4f;
        UnityEditor_Handles.DrawRect(new Rect(
            originScreen.x - centerSize / 2,
            originScreen.y - centerSize / 2,
            centerSize,
            centerSize), new Color(1f, 1f, 1f, 0.7f));
        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // ワイヤーフレーム描画（最適化版）
    // ================================================================

    /// <summary>
    /// ワイヤーフレーム描画
    /// </summary>
    private void DrawWireframeOverlay(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true)
    {
        if (meshData == null)
            return;

        // 色設定
        Color edgeColor = isActiveMesh
            ? new Color(0f, 1f, 0.5f, 0.9f)
            : new Color(0.4f, 0.4f, 0.4f, 0.5f);

        Color auxLineColor = new Color(1f, 0.3f, 1f, 0.9f);

        UnityEditor_Handles.BeginGUI();

        // エッジ描画（HashSetで重複排除）
        var edges = new HashSet<(int, int)>();
        
        foreach (var face in meshData.Faces)
        {
            if (face.VertexCount == 2)
            {
                // 補助線
                Vector3 p1World = meshData.Vertices[face.VertexIndices[0]].Position;
                Vector3 p2World = meshData.Vertices[face.VertexIndices[1]].Position;
                Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
                Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

                UnityEditor_Handles.color = auxLineColor;
                UnityEditor_Handles.DrawAAPolyLine(2f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
            else if (face.VertexCount >= 3)
            {
                // 通常エッジ
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % face.VertexCount];

                    // 正規化
                    if (a > b) (a, b) = (b, a);
                    if (!edges.Add((a, b))) continue;

                    Vector3 p1World = meshData.Vertices[a].Position;
                    Vector3 p2World = meshData.Vertices[b].Position;
                    Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
                    Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

                    UnityEditor_Handles.color = edgeColor;
                    UnityEditor_Handles.DrawAAPolyLine(2f,
                        new Vector3(p1.x, p1.y, 0),
                        new Vector3(p2.x, p2.y, 0));
                }
            }
        }

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // 選択オーバーレイ
    // ================================================================

    /// <summary>
    /// 選択状態のオーバーレイを描画
    /// </summary>
    private void DrawSelectionOverlay(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (meshData == null || _selectionState == null)
            return;

        try
        {
            UnityEditor_Handles.BeginGUI();

            DrawSelectedFaces(previewRect, meshData, camPos, lookAt);
            DrawSelectedEdges(previewRect, meshData, camPos, lookAt);
            DrawSelectedLines(previewRect, meshData, camPos, lookAt);
        }
        finally
        {
            UnityEditor_Handles.EndGUI();
        }
    }

    /// <summary>
    /// 選択された面を描画
    /// </summary>
    private void DrawSelectedFaces(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Faces.Count == 0)
            return;

        Color faceColor = new Color(1f, 0.6f, 0.2f, 0.3f);
        Color edgeColor = new Color(1f, 0.5f, 0f, 0.9f);

        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx < 0 || faceIdx >= meshData.FaceCount)
                continue;

            var face = meshData.Faces[faceIdx];
            if (face.VertexCount < 3)
                continue;

            var screenPoints = new Vector2[face.VertexCount];

            for (int i = 0; i < face.VertexCount; i++)
            {
                var worldPos = meshData.Vertices[face.VertexIndices[i]].Position;
                screenPoints[i] = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);
            }

            DrawFilledPolygon(screenPoints, faceColor);

            UnityEditor_Handles.color = edgeColor;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int next = (i + 1) % face.VertexCount;
                UnityEditor_Handles.DrawAAPolyLine(3f,
                    new Vector3(screenPoints[i].x, screenPoints[i].y, 0),
                    new Vector3(screenPoints[next].x, screenPoints[next].y, 0));
            }
        }
    }

    /// <summary>
    /// 選択されたエッジを描画
    /// </summary>
    private void DrawSelectedEdges(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Edges.Count == 0)
            return;

        UnityEditor_Handles.color = new Color(0f, 1f, 1f, 1f);

        foreach (var edge in _selectionState.Edges)
        {
            if (edge.V1 < 0 || edge.V1 >= meshData.VertexCount ||
                edge.V2 < 0 || edge.V2 >= meshData.VertexCount)
                continue;

            Vector3 p1World = meshData.Vertices[edge.V1].Position;
            Vector3 p2World = meshData.Vertices[edge.V2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawAAPolyLine(4f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }
    }

    /// <summary>
    /// 選択された補助線を描画
    /// </summary>
    private void DrawSelectedLines(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Lines.Count == 0)
            return;

        UnityEditor_Handles.color = new Color(1f, 1f, 0f, 1f);

        foreach (int lineIdx in _selectionState.Lines)
        {
            if (lineIdx < 0 || lineIdx >= meshData.FaceCount)
                continue;

            var face = meshData.Faces[lineIdx];
            if (face.VertexCount != 2)
                continue;

            Vector3 p1World = meshData.Vertices[face.VertexIndices[0]].Position;
            Vector3 p2World = meshData.Vertices[face.VertexIndices[1]].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawAAPolyLine(4f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }
    }

    // ================================================================
    // 頂点ハンドル描画（最適化版）
    // ================================================================

    /// <summary>
    /// 頂点ハンドル描画
    /// </summary>
    private void DrawVertexHandles(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true)
    {
        if (!_showVertices || meshData == null)
            return;

        float handleSize = isActiveMesh ? 8f : 4f;

        bool vertexModeEnabled = _selectionState != null && _selectionState.Mode.Has(MeshSelectMode.Vertex);
        float alpha = vertexModeEnabled ? 1f : 0.4f;
        if (!isActiveMesh) alpha *= 0.5f;

        Color normalFill = isActiveMesh
            ? new Color(1f, 1f, 1f, alpha)
            : new Color(0.5f, 0.5f, 0.5f, alpha);
        Color normalBorder = isActiveMesh
            ? new Color(0.5f, 0.5f, 0.5f, alpha)
            : new Color(0.3f, 0.3f, 0.3f, alpha);
        Color selectedFill = new Color(1f, 0.8f, 0f, alpha);
        Color selectedBorder = new Color(1f, 0f, 0f, alpha);

        UnityEditor_Handles.BeginGUI();

        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector3 worldPos = meshData.Vertices[i].Position;
            Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            bool isSelected = isActiveMesh && _selectedVertices != null && _selectedVertices.Contains(i);

            // 塗りつぶし
            Rect handleRect = new Rect(
                screenPos.x - handleSize / 2,
                screenPos.y - handleSize / 2,
                handleSize,
                handleSize);

            UnityEditor_Handles.DrawRect(handleRect, isSelected ? selectedFill : normalFill);

            // 枠線
            DrawRectBorder(handleRect, isSelected ? selectedBorder : normalBorder);

            // インデックス表示
            if (_showVertexIndices && isActiveMesh)
            {
                GUI.Label(
                    new Rect(screenPos.x + 6, screenPos.y - 8, 30, 16),
                    i.ToString(),
                    EditorStyles.miniLabel);
            }
        }

        UnityEditor_Handles.EndGUI();

        // 矩形選択オーバーレイ
        if (isActiveMesh && _editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    /// <summary>
    /// 矩形選択オーバーレイを描画
    /// </summary>
    private void DrawBoxSelectOverlay()
    {
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.DrawRect(selectRect, new Color(0.3f, 0.6f, 1f, 0.2f));
        DrawRectBorder(selectRect, new Color(0.3f, 0.6f, 1f, 0.8f));
        UnityEditor_Handles.EndGUI();
    }
}
