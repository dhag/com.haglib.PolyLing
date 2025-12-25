// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_Preview.cs
// プレビュー描画（ワイヤーフレーム、選択オーバーレイ、頂点ハンドル）
// GPU描画対応版

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
    // GPU描画
    // ================================================================

    private MeshEdgeCache _edgeCache;
    private MeshGPURenderer _gpuRenderer;
    private bool _useGPURendering = true;  // GPU描画有効フラグ
    private bool _gpuRenderingAvailable = false;

    /// <summary>
    /// 描画キャッシュを初期化（OnEnableで呼び出し）
    /// </summary>
    private void InitializeDrawCache()
    {
        _edgeCache = new MeshEdgeCache();

        // GPU描画初期化
        _gpuRenderer = new MeshGPURenderer();

        var computeShader = Resources.Load<ComputeShader>("Compute2D_GPU");
        var pointShader = Shader.Find("MeshFactory/Point");
        var lineShader = Shader.Find("MeshFactory/Line");

        _gpuRenderingAvailable = _gpuRenderer.Initialize(computeShader, pointShader, lineShader);

        if (!_gpuRenderingAvailable)
        {
            Debug.LogWarning("SimpleMeshFactory: GPU Rendering not available, using CPU fallback");
        }
    }

    /// <summary>
    /// 描画キャッシュをクリーンアップ（OnDisableで呼び出し）
    /// </summary>
    private void CleanupDrawCache()
    {
        _edgeCache?.Clear();

        _gpuRenderer?.Dispose();
        _gpuRenderer = null;
        _gpuRenderingAvailable = false;

        if (_polygonMaterial != null)
        {
            DestroyImmediate(_polygonMaterial);
            _polygonMaterial = null;
        }
        // === 追加: ヒットテスト検証クリーンアップ ===
        CleanupHitTestValidation();
    }

    /*SyncMeshFromData()はトポロジー変更時に必ず呼ばれるためキャッシュの無効化が確実に行われます。
    /// <summary>
    /// エッジキャッシュを無効化（メッシュ変更時に呼び出し）
    /// </summary>
    private void InvalidateDrawCache()
    {
        _edgeCache?.Invalidate();
        _gpuRenderer?.InvalidateBuffers();
        // ★Phase2追加: 対称表示キャッシュも無効化
        InvalidateSymmetryCache();
    }
    */

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
            // 選択メッシュのみ表示モードでもIsVisibleをチェック
            if (meshContext != null && meshContext.IsVisible)
            {
                DrawMeshWithMaterials(meshContext, mesh);
            }
        }
        else
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var ctx = _meshContextList[i];
                if (ctx?.UnityMesh == null) continue;
                if (!ctx.IsVisible) continue;  // 非表示メッシュをスキップ

                bool isSelected = (i == _selectedIndex);
                DrawMeshWithMaterials(ctx, ctx.UnityMesh, isSelected ? 1f : 0.5f);
            }
        }

        // ミラーメッシュ描画（グローバル設定 OR MeshContextごとの設定）
        // 条件判定はDrawMirroredMesh内で行う
        if (_showSelectedMeshOnly)
        {
            if (meshContext != null && meshContext.IsVisible)
            {
                DrawMirroredMesh(meshContext, mesh);
            }
        }
        else
        {
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var ctx = _meshContextList[i];
                if (ctx?.UnityMesh == null) continue;
                if (!ctx.IsVisible) continue;

                DrawMirroredMesh(ctx, ctx.UnityMesh);
            }
        }


        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        // ================================================================
        // ワイヤーフレーム・頂点描画（GPU/CPU切り替え）
        // ================================================================
        bool useGPU = _useGPURendering && _gpuRenderingAvailable;

        if (useGPU)
        {
            if (_showSelectedMeshOnly)
            {
                // 選択メッシュのみGPU描画
                if (meshContext != null && meshContext.IsVisible)
                {
                    DrawWithGPU(rect, meshContext, camPos, true, _selectedVertices);
                }
            }
            else
            {
                // 非選択メッシュを先に描画（下のレイヤー）
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null) continue;
                    if (!ctx.IsVisible) continue;  // 非表示メッシュをスキップ
                    DrawWithGPU(rect, ctx, camPos, false);
                }

                // 選択メッシュを最後に描画（上のレイヤー）
                if (meshContext != null && meshContext.IsVisible)
                {
                    DrawWithGPU(rect, meshContext, camPos, true, _selectedVertices);
                }
            }
        }
        else
        {
            // CPU描画（従来方式）
            if (_showWireframe)
            {
                if (_showSelectedMeshOnly)
                {
                    if (meshContext != null && meshContext.IsVisible)
                    {
                        DrawWireframeOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, true);
                    }
                }
                else
                {
                    for (int i = 0; i < _meshContextList.Count; i++)
                    {
                        var ctx = _meshContextList[i];
                        if (ctx?.MeshObject == null) continue;
                        if (!ctx.IsVisible) continue;  // 非表示メッシュをスキップ
                        bool isActive = (i == _selectedIndex);
                        DrawWireframeOverlay(rect, ctx.MeshObject, camPos, _cameraTarget, isActive);
                    }
                }
            }

            // 頂点描画（CPU）
            if (_showSelectedMeshOnly)
            {
                if (meshContext != null && meshContext.IsVisible)
                {
                    DrawVertexHandles(rect, meshContext.MeshObject, camPos, _cameraTarget, true);
                }
            }
            else
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null) continue;
                    if (!ctx.IsVisible) continue;  // 非表示メッシュをスキップ
                    bool isActive = (i == _selectedIndex);
                    DrawVertexHandles(rect, ctx.MeshObject, camPos, _cameraTarget, isActive);
                }
            }
        }

        // ミラーワイヤーフレーム描画（常にCPU）
        if (_showWireframe && _symmetrySettings != null && _symmetrySettings.IsEnabled)
        {
            DrawMirroredWireframe(rect, meshContext.MeshObject, camPos, _cameraTarget);
        }

        // 選択状態のオーバーレイ描画
        DrawSelectionOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, useGPU);

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
            Bounds meshBounds = meshContext.MeshObject != null ? meshContext.MeshObject.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            DrawSymmetryPlane(rect, camPos, _cameraTarget, meshBounds);
        }
    }

    // ================================================================
    // GPU描画メソッド
    // ================================================================

    /// <summary>
    /// GPU描画（isSelected: trueなら選択メッシュとして明るく描画）
    /// </summary>
    private void DrawWithGPU(Rect rect, MeshContext meshContext, Vector3 camPos, bool isSelected, HashSet<int> selectedVertices = null)
    {
        if (_gpuRenderer == null || meshContext?.MeshObject == null)
            return;

        Vector2 windowSize = new Vector2(position.width, position.height);
        Vector2 guiOffset = Vector2.zero;

        float tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y;
        Rect adjustedRect = new Rect(rect.x, rect.y + tabHeight, rect.width, rect.height - tabHeight);

        // バッファ更新（メッシュが変わると自動更新）
        _gpuRenderer.UpdateBuffers(meshContext.MeshObject, _edgeCache);

        // 選択状態更新（選択メッシュのみ）
        _gpuRenderer.UpdateSelection(isSelected ? selectedVertices : null);

        // 線分選択状態更新（選択メッシュのみ）
        if (isSelected && _selectionState != null && _selectionState.Edges.Count > 0)
        {
            // EdgeをタプルHashSetに変換
            var edgeTuples = new HashSet<(int, int)>();
            foreach (var edge in _selectionState.Edges)
            {
                int v1 = edge.V1 < edge.V2 ? edge.V1 : edge.V2;
                int v2 = edge.V1 < edge.V2 ? edge.V2 : edge.V1;
                edgeTuples.Add((v1, v2));
            }
            _gpuRenderer.UpdateLineSelection(edgeTuples, _edgeCache);
        }
        else
        {
            _gpuRenderer.UpdateLineSelection(null, _edgeCache);
        }

        Matrix4x4 mvp = CalculateMVPMatrix(rect, camPos);
        _gpuRenderer.DispatchCompute(mvp, adjustedRect, windowSize);

        // アルファ：選択=1.0、非選択=0.4
        float alpha = isSelected ? 1f : 0.4f;
        float pointSize = isSelected ? 8f : 4f;

        if (_showWireframe)
        {
            // 選択メッシュ: 緑、非選択メッシュ: グレー
            Color edgeColor = isSelected
                ? new Color(0f, 1f, 0.5f, 0.9f)   // 緑
                : new Color(0.5f, 0.5f, 0.5f, 0.7f); // グレー

            // 面ホバー描画（選択メッシュのみ、最背面に描画）
            if (isSelected)
            {
                _gpuRenderer.DrawHoverFace(windowSize, meshContext.MeshObject);
            }

            _gpuRenderer.DrawLines(adjustedRect, windowSize, guiOffset, 2f, alpha, edgeColor);
        }

        if (_showVertices)
        {
            _gpuRenderer.DrawPoints(adjustedRect, windowSize, guiOffset, pointSize, alpha);
        }

        // インデックス表示は選択メッシュのみ
        if (_showVertexIndices && isSelected)
        {
            DrawVertexIndices(rect, meshContext.MeshObject, camPos, _cameraTarget);
        }

        // 矩形選択オーバーレイは選択メッシュのみ
        if (isSelected && _editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    /// <summary>
    /// MVP行列を計算
    /// </summary>
    private Matrix4x4 CalculateMVPMatrix(Rect rect, Vector3 camPos)
    {
        Vector3 forward = (_cameraTarget - camPos).normalized;
        Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        Quaternion camRot = lookRot * rollRot;

        Matrix4x4 camMatrix = Matrix4x4.TRS(camPos, camRot, Vector3.one);
        Matrix4x4 view = camMatrix.inverse;
        view.m20 *= -1; view.m21 *= -1; view.m22 *= -1; view.m23 *= -1;

        float aspect = rect.width / rect.height;
        Matrix4x4 proj = Matrix4x4.Perspective(_preview.cameraFieldOfView, aspect, 0.01f, 100f);

        return proj * view;
    }

    /// <summary>
    /// 頂点インデックス表示（GPU描画時のCPUフォールバック）
    /// </summary>
    private void DrawVertexIndices(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt)
    {
        if (meshObject == null)
            return;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector3 worldPos = meshObject.Vertices[i].Position;
            Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            GUI.Label(
                new Rect(screenPos.x + 6, screenPos.y - 8, 30, 16),
                i.ToString(),
                EditorStyles.miniLabel);
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
            if (meshContext != null && i < _model.Materials.Count)
            {
                mat = _model.Materials[i];
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

        Vector3 xEnd = origin + Vector3.right * axisLength;
        Vector2 xScreen = WorldToPreviewPos(xEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, xScreen, new Color(1f, 0.3f, 0.3f, 0.7f));

        Vector3 yEnd = origin + Vector3.up * axisLength;
        Vector2 yScreen = WorldToPreviewPos(yEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, yScreen, new Color(0.3f, 1f, 0.3f, 0.7f));

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
    // ワイヤーフレーム描画（CPU版）
    // ================================================================

    /// <summary>
    /// ワイヤーフレーム描画（MeshObjectベース）
    /// </summary>
    private void DrawWireframeOverlay(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true)
    {
        if (meshObject == null)
            return;

        var edges = new HashSet<(int, int)>();
        var lines = new List<(int, int)>();

        foreach (var face in meshObject.Faces)
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
                    // エッジを正規化して追加
                    if (a > b) (a, b) = (b, a);
                    edges.Add((a, b));
                }
            }
        }

        UnityEditor_Handles.BeginGUI();

        UnityEditor_Handles.color = isActiveMesh
            ? new Color(0f, 1f, 0.5f, 0.9f)
            : new Color(0.4f, 0.4f, 0.4f, 0.5f);
        foreach (var edge in edges)
        {
            Vector3 p1World = meshObject.Vertices[edge.Item1].Position;
            Vector3 p2World = meshObject.Vertices[edge.Item2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        UnityEditor_Handles.color = new Color(1f, 0.3f, 1f, 0.9f);
        foreach (var line in lines)
        {
            if (line.Item1 < 0 || line.Item1 >= meshObject.VertexCount ||
                line.Item2 < 0 || line.Item2 >= meshObject.VertexCount)
                continue;

            Vector3 p1World = meshObject.Vertices[line.Item1].Position;
            Vector3 p2World = meshObject.Vertices[line.Item2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawAAPolyLine(2f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // 選択オーバーレイ描画
    // ================================================================

    /// <summary>
    /// 選択状態のオーバーレイを描画
    /// </summary>
    private void DrawSelectionOverlay(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool gpuRendering = false)
    {
        if (meshObject == null || _selectionState == null)
            return;

        try
        {
            UnityEditor_Handles.BeginGUI();
            DrawSelectedFaces(previewRect, meshObject, camPos, lookAt);

            // GPU描画時はエッジと線分はシェーダーで描画済みなのでスキップ
            if (!gpuRendering)
            {
                DrawSelectedEdges(previewRect, meshObject, camPos, lookAt);
                DrawSelectedLines(previewRect, meshObject, camPos, lookAt);
            }
        }
        finally
        {
            UnityEditor_Handles.EndGUI();
        }
    }

    private void DrawSelectedFaces(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Faces.Count == 0)
            return;

        Color faceColor = new Color(1f, 0.6f, 0.2f, 0.3f);
        Color edgeColor = new Color(1f, 0.5f, 0f, 0.9f);

        foreach (int faceIdx in _selectionState.Faces)
        {
            if (faceIdx < 0 || faceIdx >= meshObject.FaceCount)
                continue;

            var face = meshObject.Faces[faceIdx];
            if (face.VertexCount < 3)
                continue;

            var screenPoints = new Vector2[face.VertexCount];

            for (int i = 0; i < face.VertexCount; i++)
            {
                var worldPos = meshObject.Vertices[face.VertexIndices[i]].Position;
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

    private void DrawSelectedEdges(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Edges.Count == 0)
            return;

        UnityEditor_Handles.color = new Color(0f, 1f, 1f, 1f);

        foreach (var edge in _selectionState.Edges)
        {
            if (edge.V1 < 0 || edge.V1 >= meshObject.VertexCount ||
                edge.V2 < 0 || edge.V2 >= meshObject.VertexCount)
                continue;

            Vector3 p1World = meshObject.Vertices[edge.V1].Position;
            Vector3 p2World = meshObject.Vertices[edge.V2].Position;

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

    private void DrawSelectedLines(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt)
    {
        if (_selectionState.Lines.Count == 0)
            return;

        UnityEditor_Handles.color = new Color(1f, 1f, 0f, 1f);

        foreach (int lineIdx in _selectionState.Lines)
        {
            if (lineIdx < 0 || lineIdx >= meshObject.FaceCount)
                continue;

            var face = meshObject.Faces[lineIdx];
            if (face.VertexCount != 2)
                continue;

            Vector3 p1World = meshObject.Vertices[face.VertexIndices[0]].Position;
            Vector3 p2World = meshObject.Vertices[face.VertexIndices[1]].Position;

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
    // 頂点ハンドル描画（CPU版）
    // ================================================================

    private void DrawVertexHandles(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true)
    {
        if (!_showVertices || meshObject == null)
            return;

        float handleSize = isActiveMesh ? 8f : 4f;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector2 screenPos = WorldToPreviewPos(meshObject.Vertices[i].Position, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            Rect handleRect = new Rect(
                screenPos.x - handleSize / 2,
                screenPos.y - handleSize / 2,
                handleSize,
                handleSize);

            bool isSelected = isActiveMesh && _selectedVertices.Contains(i);

            bool vertexModeEnabled = _selectionState != null && _selectionState.Mode.Has(MeshSelectMode.Vertex);
            float alpha = vertexModeEnabled ? 1f : 0.4f;
            if (!isActiveMesh) alpha *= 0.5f;

            UnityEditor_Handles.BeginGUI();

            Color col;
            Color borderCol;
            if (!isActiveMesh)
            {
                col = new Color(0.5f, 0.5f, 0.5f, alpha);
                borderCol = new Color(0.3f, 0.3f, 0.3f, alpha);
            }
            else if (isSelected)
            {
                col = new Color(1f, 0.8f, 0f, alpha);
                borderCol = new Color(1f, 0f, 0f, alpha);
            }
            else
            {//非選択メッシュの頂点？CPU
                col = new Color(1f, 1f, 1f, alpha);
                borderCol = new Color(0.5f, 0.5f, 0.5f, alpha);
            }
            UnityEditor_Handles.DrawRect(handleRect, col);
            DrawRectBorder(handleRect, borderCol);

            UnityEditor_Handles.EndGUI();

            if (_showVertexIndices && isActiveMesh)
            {
                GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 30, 16), i.ToString(), EditorStyles.miniLabel);
            }
        }

        if (isActiveMesh && _editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    private void DrawBoxSelectOverlay()
    {
        UnityEditor_Handles.BeginGUI();
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        Color fillColor = new Color(0.3f, 0.6f, 1f, 0.2f);
        UnityEditor_Handles.DrawRect(selectRect, fillColor);

        Color borderColor = new Color(0.3f, 0.6f, 1f, 0.8f);
        DrawRectBorder(selectRect, borderColor);

        UnityEditor_Handles.EndGUI();
    }
}