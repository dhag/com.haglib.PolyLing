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

        // 3D描画用シェーダー初期化
        var wireframe3DShader = Shader.Find("MeshFactory/Wireframe3D");
        var point3DShader = Shader.Find("MeshFactory/Point3D");

        if (wireframe3DShader == null)
            Debug.LogWarning("SimpleMeshFactory: Wireframe3D shader not found!");
        if (point3DShader == null)
            Debug.LogWarning("SimpleMeshFactory: Point3D shader not found!");

        if (wireframe3DShader != null && point3DShader != null)
        {
            _gpuRenderer.Initialize3D(wireframe3DShader, point3DShader);
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
        if (_showMesh)
        {
            if (_showSelectedMeshOnly)
            {
                // 選択メッシュのみ表示モードでもIsVisibleをチェック
                if (meshContext != null && meshContext.IsVisible)
                {
                    DrawMeshWithMaterials(meshContext, mesh, 1f, _selectedIndex);
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
                    DrawMeshWithMaterials(ctx, ctx.UnityMesh, isSelected ? 1f : 0.5f, i);
                }
            }
        }

        // ミラーメッシュ描画（グローバル設定 OR MeshContextごとの設定）
        // 条件判定はDrawMirroredMesh内で行う
        if (_showMesh)
        {
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
        }


        // ================================================================
        // ワイヤフレーム・頂点の3D描画（camera.Render()の前に実行）
        // 深度テスト付きで描画されるため、メッシュに隠れた部分は見えなくなる
        // ================================================================
        bool use3D = _useGPURendering && _gpuRenderingAvailable && _gpuRenderer.Is3DAvailable;

        if (use3D)
        {
            Vector2 windowSize = new Vector2(position.width, position.height);
            float tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y;
            Rect adjustedRect = new Rect(rect.x, rect.y + tabHeight, rect.width, rect.height - tabHeight);

            // 非選択メッシュを先に描画（下のレイヤー）
            if (_showUnselectedWireframe || _showUnselectedVertices)
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null || !ctx.IsVisible) continue;

                    // 表示用行列を取得
                    Matrix4x4 displayMatrix = GetDisplayMatrix(i);

                    _edgeCache.Update(ctx.MeshObject);
                    _gpuRenderer.UpdateBuffers(ctx.MeshObject, _edgeCache);

                    Matrix4x4 mvp = CalculateMVPMatrix(rect, camPos);
                    _gpuRenderer.DispatchCompute(mvp, adjustedRect, windowSize, displayMatrix);

                    // 非選択ワイヤフレーム
                    if (_showWireframe && _showUnselectedWireframe)
                    {
                        _gpuRenderer.UpdateWireframeMesh3D(ctx.MeshObject, _edgeCache, null, 0.4f, displayMatrix);
                        _gpuRenderer.DrawWireframe3D(_preview.camera);
                    }

                    // 非選択頂点
                    if (_showVertices && _showUnselectedVertices)
                    {
                        float pointSize = CalculatePointSize3D(camPos, ctx.MeshObject) * 0.7f;
                        _gpuRenderer.UpdatePointMesh3D(ctx.MeshObject, _preview.camera, null, -1, pointSize, 0.4f, displayMatrix);
                        _gpuRenderer.DrawPoints3D(_preview.camera);
                    }
                }
            }

            // 選択メッシュを描画（上のレイヤー）
            if (meshContext != null && meshContext.IsVisible)
            {
                // 表示用行列を取得
                Matrix4x4 displayMatrix = GetDisplayMatrix(_selectedIndex);

                _edgeCache.Update(meshContext.MeshObject);
                _gpuRenderer.UpdateBuffers(meshContext.MeshObject, _edgeCache);

                Matrix4x4 mvp = CalculateMVPMatrix(rect, camPos);
                _gpuRenderer.DispatchCompute(mvp, adjustedRect, windowSize, displayMatrix);

                // ワイヤフレーム3Dメッシュ生成・描画
                if (_showWireframe)
                {
                    _gpuRenderer.UpdateWireframeMesh3D(
                        meshContext.MeshObject,
                        _edgeCache,
                        null,  // selectedLines - 後で実装
                        1f,
                        displayMatrix
                    );
                    _gpuRenderer.DrawWireframe3D(_preview.camera);
                }

                // 頂点3Dメッシュ生成・描画（ミラー用DispatchComputeの前に実行）
                if (_showVertices)
                {
                    float pointSize = CalculatePointSize3D(camPos, meshContext.MeshObject);
                    _gpuRenderer.UpdatePointMesh3D(
                        meshContext.MeshObject,
                        _preview.camera,
                        _selectedVertices,
                        _gpuRenderer.HoverVertexIndex,
                        pointSize,
                        1f,
                        displayMatrix
                    );
                    _gpuRenderer.DrawPoints3D(_preview.camera);
                }

                // ミラーワイヤフレーム3D（チェックボックスで制御）
                // ミラー用DispatchComputeは可視性バッファを上書きするため、頂点描画の後に実行
                if (_showWireframe && meshContext.IsMirrored && (_symmetrySettings == null || _symmetrySettings.ShowMirrorWireframe))
                {
                    var effectiveSettings = GetEffectiveSymmetrySettings(meshContext);
                    if (effectiveSettings != null)
                    {
                        Matrix4x4 mirrorMatrix = effectiveSettings.GetMirrorMatrix();
                        bool cullingEnabled = _gpuRenderer?.CullingEnabled ?? true;

                        // 合成行列: displayMatrix * mirrorMatrix
                        Matrix4x4 combinedMatrix = displayMatrix * mirrorMatrix;

                        // ミラー用可視性計算（合成行列でDispatchCompute）
                        if (cullingEnabled)
                        {
                            _gpuRenderer.DispatchCompute(mvp, adjustedRect, windowSize, combinedMatrix, true);
                        }

                        _gpuRenderer.UpdateMirrorWireframeMesh3D(meshContext.MeshObject, _edgeCache, mirrorMatrix, effectiveSettings.MirrorAlpha, cullingEnabled, displayMatrix);
                        _gpuRenderer.QueueMirrorWireframe3D();
                    }
                }
            }

            // 非選択メッシュのミラーワイヤフレーム3D（チェックボックスで制御）
            if (_showWireframe && _showUnselectedWireframe && (_symmetrySettings == null || _symmetrySettings.ShowMirrorWireframe))
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null || !ctx.IsVisible) continue;
                    if (!ctx.IsMirrored) continue;

                    // 表示用行列を取得
                    Matrix4x4 unselDisplayMatrix = GetDisplayMatrix(i);

                    _edgeCache.Update(ctx.MeshObject);
                    _gpuRenderer.UpdateBuffers(ctx.MeshObject, _edgeCache);

                    var effectiveSettings = GetEffectiveSymmetrySettings(ctx);
                    if (effectiveSettings != null)
                    {
                        Matrix4x4 mvpUnsel = CalculateMVPMatrix(rect, camPos);
                        Matrix4x4 mirrorMatrix = effectiveSettings.GetMirrorMatrix();
                        bool cullingEnabled = _gpuRenderer?.CullingEnabled ?? true;

                        // 合成行列: displayMatrix * mirrorMatrix
                        Matrix4x4 combinedMatrix = unselDisplayMatrix * mirrorMatrix;

                        // ミラー用可視性計算
                        _gpuRenderer.DispatchCompute(mvpUnsel, adjustedRect, windowSize, combinedMatrix, true);

                        _gpuRenderer.UpdateMirrorWireframeMesh3D(ctx.MeshObject, _edgeCache, mirrorMatrix, effectiveSettings.MirrorAlpha * 0.4f, cullingEnabled, unselDisplayMatrix);
                        _gpuRenderer.QueueMirrorWireframe3D();
                    }
                }
            }
        }

        // キューに入っているメッシュを描画
        if (use3D)
        {
            _gpuRenderer.DrawQueued3D(_preview);
        }

        _preview.camera.Render();

        // 3D描画のクリーンアップ（コピーメッシュを破棄）
        if (use3D)
        {
            _gpuRenderer.CleanupQueued3D();

            // 選択メッシュ用の可視性を再計算（ホバー・選択オーバーレイ用）
            if (meshContext != null && meshContext.IsVisible)
            {
                // 表示用行列を取得（ヒットテストで使用するためキャッシュされる）
                Matrix4x4 displayMatrix = GetDisplayMatrix(_selectedIndex);

                Vector2 windowSize2 = new Vector2(position.width, position.height);
                float tabHeight2 = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y;
                Rect adjustedRect2 = new Rect(rect.x, rect.y + tabHeight2, rect.width, rect.height - tabHeight2);

                _edgeCache.Update(meshContext.MeshObject);
                _gpuRenderer.UpdateBuffers(meshContext.MeshObject, _edgeCache);
                Matrix4x4 mvp = CalculateMVPMatrix(rect, camPos);
                _gpuRenderer.DispatchCompute(mvp, adjustedRect2, windowSize2, displayMatrix);
            }
        }

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        // 選択メッシュ用の表示行列を取得
        Matrix4x4 selectedDisplayMatrix = GetDisplayMatrix(_selectedIndex);

        // ホバー線のGL描画（最前面に上書き）
        if (use3D && _showWireframe && meshContext != null && meshContext.IsVisible)
        {
            DrawHoverLineOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, selectedDisplayMatrix);
        }

        // ================================================================
        // 頂点インデックス表示（2Dオーバーレイ）
        // ================================================================
        if (_showVertexIndices && meshContext != null && meshContext.IsVisible)
        {
            DrawVertexIndices(rect, meshContext.MeshObject, camPos, _cameraTarget, selectedDisplayMatrix);
        }

        // ================================================================
        // ワイヤーフレーム・頂点描画（3D/2D切り替え）
        // ================================================================
        // 3D描画が有効な場合は2Dオーバーレイはスキップ（深度テスト付きで既に描画済み）
        bool useGPU2D = _useGPURendering && _gpuRenderingAvailable;

        if (use3D)
        {
            // 3D描画済み - 選択オーバーレイのみ追加で描画
            if (meshContext != null && meshContext.IsVisible)
            {
                DrawSelectionOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, true, selectedDisplayMatrix);

                // ホバー面描画（3D描画でもGPU 2Dオーバーレイで描画）
                Vector2 windowSize3D = new Vector2(position.width, position.height);
                _gpuRenderer.DrawHoverFace(windowSize3D, meshContext.MeshObject);

                // 矩形選択オーバーレイ
                if (_editState == VertexEditState.BoxSelecting)
                {
                    DrawBoxSelectOverlay();
                }
            }
        }
        else if (useGPU2D)
        {
            // 2D GPU描画（従来方式）
            // 選択メッシュを描画
            if (meshContext != null && meshContext.IsVisible)
            {
                DrawWithGPU(rect, meshContext, camPos, true, _selectedVertices, null, null, _selectedIndex);
            }

            // 非選択メッシュを描画（フラグで制御）
            if (!_showSelectedMeshOnly)
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null) continue;
                    if (!ctx.IsVisible) continue;

                    // 非選択表示フラグで制御
                    bool showWireframe = _showWireframe && _showUnselectedWireframe;
                    bool showVertices = _showVertices && _showUnselectedVertices;

                    if (showWireframe || showVertices)
                    {
                        DrawWithGPU(rect, ctx, camPos, false, null, showWireframe, showVertices, i);
                    }
                }
            }
        }
        else
        {
            // CPU描画（フォールバック）
            // 選択メッシュのワイヤフレーム
            if (_showWireframe && meshContext != null && meshContext.IsVisible)
            {
                DrawWireframeOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, true, selectedDisplayMatrix);
            }

            // 非選択メッシュのワイヤフレーム
            if (_showWireframe && _showUnselectedWireframe && !_showSelectedMeshOnly)
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null) continue;
                    if (!ctx.IsVisible) continue;
                    Matrix4x4 ctxDisplayMatrix = GetDisplayMatrix(i);
                    DrawWireframeOverlay(rect, ctx.MeshObject, camPos, _cameraTarget, false, ctxDisplayMatrix);
                }
            }

            // 選択メッシュの頂点
            if (_showVertices && meshContext != null && meshContext.IsVisible)
            {
                DrawVertexHandles(rect, meshContext.MeshObject, camPos, _cameraTarget, true, selectedDisplayMatrix);
            }

            // 非選択メッシュの頂点
            if (_showVertices && _showUnselectedVertices && !_showSelectedMeshOnly)
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null) continue;
                    if (!ctx.IsVisible) continue;
                    Matrix4x4 ctxDisplayMatrix = GetDisplayMatrix(i);
                    DrawVertexHandles(rect, ctx.MeshObject, camPos, _cameraTarget, false, ctxDisplayMatrix);
                }
            }
        }

        // ミラーワイヤーフレーム描画（MeshContextのMirror属性を使用）
        // 3D描画時は上で処理済みなのでスキップ
        if (_showWireframe && !use3D)
        {
            // 選択メッシュのミラーワイヤフレーム
            if (meshContext != null && meshContext.IsMirrored)
            {
                DrawMirroredWireframe(rect, meshContext, camPos, _cameraTarget, _selectedIndex);
            }

            // 非選択メッシュのミラーワイヤフレーム
            if (_showUnselectedWireframe && !_showSelectedMeshOnly)
            {
                for (int i = 0; i < _meshContextList.Count; i++)
                {
                    if (i == _selectedIndex) continue;
                    var ctx = _meshContextList[i];
                    if (ctx?.MeshObject == null || !ctx.IsVisible) continue;
                    if (!ctx.IsMirrored) continue;

                    DrawMirroredWireframe(rect, ctx, camPos, _cameraTarget, i);
                }
            }
        }

        // 選択状態のオーバーレイ描画（3D描画時は上で処理済み）
        if (!use3D)
        {
            DrawSelectionOverlay(rect, meshContext.MeshObject, camPos, _cameraTarget, useGPU2D, selectedDisplayMatrix);
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
            Bounds meshBounds = meshContext.MeshObject != null ? meshContext.MeshObject.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            DrawSymmetryPlane(rect, camPos, _cameraTarget, meshBounds);
        }
    }

    // ================================================================
    // 3D描画ヘルパーメソッド
    // ================================================================

    /// <summary>
    /// カメラ距離に基づいて頂点の3Dサイズを計算
    /// ホバー判定（10ピクセル）と一致するサイズを返す
    /// </summary>
    private float CalculatePointSize3D(Vector3 camPos, MeshObject meshObject, float targetPixelSize = 10f)
    {
        if (meshObject == null || meshObject.VertexCount == 0 || _preview == null)
            return 0.01f;

        // メッシュの中心までの距離
        Bounds bounds = meshObject.CalculateBounds();
        float distance = Vector3.Distance(camPos, bounds.center);

        // 画面上でtargetPixelSizeピクセルのサイズになるように計算
        float screenHeight = _preview.camera.pixelHeight;
        if (screenHeight <= 0) screenHeight = 500f;  // フォールバック

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad;
        float tanHalfFov = Mathf.Tan(fovRad * 0.5f);

        float worldSize = targetPixelSize * distance * 2f * tanHalfFov / screenHeight;

        return Mathf.Clamp(worldSize, 0.001f, 0.1f);
    }

    /// <summary>
    /// 頂点インデックスを2Dオーバーレイで描画（選択メッシュのみ）
    /// 可視性をチェックして表示
    /// </summary>
    private void DrawVertexIndices(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, Matrix4x4? displayMatrix = null)
    {
        if (meshObject == null)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;

        // 可視性データを取得
        float[] vertexVisibility = _gpuRenderer?.GetVertexVisibility();

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            // 可視性チェック（GPUデータがあれば使用）
            if (vertexVisibility != null && i < vertexVisibility.Length && vertexVisibility[i] < 0.5f)
                continue;

            Vector3 transformedPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            Vector2 screenPos = WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 40, 16), i.ToString(), EditorStyles.miniLabel);
        }
    }

    // ================================================================
    // GPU描画メソッド
    // ================================================================

    /// <summary>
    /// GPU描画（isSelected: trueなら選択メッシュとして明るく描画）
    /// </summary>
    private void DrawWithGPU(Rect rect, MeshContext meshContext, Vector3 camPos, bool isSelected, HashSet<int> selectedVertices = null, bool? overrideShowWireframe = null, bool? overrideShowVertices = null, int meshIndex = -1)
    {
        if (_gpuRenderer == null || meshContext?.MeshObject == null)
            return;

        // メッシュインデックスを特定
        int idx = meshIndex >= 0 ? meshIndex : _meshContextList.IndexOf(meshContext);
        Matrix4x4 displayMatrix = GetDisplayMatrix(idx);

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
        _gpuRenderer.DispatchCompute(mvp, adjustedRect, windowSize, displayMatrix);

        // アルファ：選択=1.0、非選択=0.4
        float alpha = isSelected ? 1f : 0.4f;
        float pointSize = isSelected ? 8f : 4f;

        // ワイヤフレーム/頂点表示のオーバーライド
        bool showWireframe = overrideShowWireframe ?? _showWireframe;
        bool showVertices = overrideShowVertices ?? _showVertices;

        if (showWireframe)
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

        if (showVertices)
        {
            _gpuRenderer.DrawPoints(adjustedRect, windowSize, guiOffset, pointSize, alpha);
        }

        // インデックス表示は選択メッシュのみ
        if (_showVertexIndices && isSelected)
        {
            DrawVertexIndices(rect, meshContext.MeshObject, camPos, _cameraTarget, displayMatrix);
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

    // ================================================================
    // メッシュ描画
    // ================================================================

    /// <summary>
    /// マルチマテリアル対応でメッシュを描画
    /// </summary>
    private void DrawMeshWithMaterials(MeshContext meshContext, Mesh mesh, float alpha = 1f, int meshIndex = -1)
    {
        if (mesh == null)
            return;

        // 表示用行列を取得（meshIndex が -1 の場合は meshContext から検索）
        Matrix4x4 displayMatrix;
        if (meshIndex >= 0)
        {
            displayMatrix = GetDisplayMatrix(meshIndex);
        }
        else
        {
            displayMatrix = GetDisplayMatrix(meshContext);
        }

        int subMeshCount = mesh.subMeshCount;
        Material defaultMat = GetPreviewMaterial();

        for (int i = 0; i < subMeshCount; i++)
        {
            Material mat = null;
            if (meshContext != null && i < _model.MaterialCount)
            {
                mat = _model.GetMaterial(i);
            }

            if (mat == null)
            {
                mat = defaultMat;
            }
            _preview.DrawMesh(mesh, displayMatrix, mat, i);
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
    private void DrawWireframeOverlay(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true, Matrix4x4? displayMatrix = null)
    {
        if (meshObject == null)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;

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
            Vector3 p1World = matrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item1].Position);
            Vector3 p2World = matrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item2].Position);

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

            Vector3 p1World = matrix.MultiplyPoint3x4(meshObject.Vertices[line.Item1].Position);
            Vector3 p2World = matrix.MultiplyPoint3x4(meshObject.Vertices[line.Item2].Position);

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
    private void DrawSelectionOverlay(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool gpuRendering = false, Matrix4x4? displayMatrix = null)
    {
        if (meshObject == null || _selectionState == null)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;

        try
        {
            UnityEditor_Handles.BeginGUI();
            DrawSelectedFaces(previewRect, meshObject, camPos, lookAt, matrix);

            // GPU描画時はエッジと線分はシェーダーで描画済みなのでスキップ
            if (!gpuRendering)
            {
                DrawSelectedEdges(previewRect, meshObject, camPos, lookAt, matrix);
                DrawSelectedLines(previewRect, meshObject, camPos, lookAt, matrix);
            }
        }
        finally
        {
            UnityEditor_Handles.EndGUI();
        }
    }

    private void DrawSelectedFaces(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, Matrix4x4 matrix)
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
                var worldPos = matrix.MultiplyPoint3x4(meshObject.Vertices[face.VertexIndices[i]].Position);
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

    private void DrawSelectedEdges(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, Matrix4x4 matrix)
    {
        if (_selectionState.Edges.Count == 0)
            return;

        UnityEditor_Handles.color = new Color(0f, 1f, 1f, 1f);

        foreach (var edge in _selectionState.Edges)
        {
            if (edge.V1 < 0 || edge.V1 >= meshObject.VertexCount ||
                edge.V2 < 0 || edge.V2 >= meshObject.VertexCount)
                continue;

            Vector3 p1World = matrix.MultiplyPoint3x4(meshObject.Vertices[edge.V1].Position);
            Vector3 p2World = matrix.MultiplyPoint3x4(meshObject.Vertices[edge.V2].Position);

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

    private void DrawSelectedLines(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, Matrix4x4 matrix)
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

            Vector3 p1World = matrix.MultiplyPoint3x4(meshObject.Vertices[face.VertexIndices[0]].Position);
            Vector3 p2World = matrix.MultiplyPoint3x4(meshObject.Vertices[face.VertexIndices[1]].Position);

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

    private void DrawVertexHandles(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, bool isActiveMesh = true, Matrix4x4? displayMatrix = null)
    {
        if (!_showVertices || meshObject == null)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;
        float handleSize = isActiveMesh ? 8f : 4f;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector3 transformedPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            Vector2 screenPos = WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);

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

    // ================================================================
    // ホバー線描画（GLGizmoDrawer経由）
    // ================================================================

    /// <summary>
    /// ホバー線を2Dオーバーレイで描画（GLGizmoDrawer経由）
    /// </summary>
    private void DrawHoverLineOverlay(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt, Matrix4x4? displayMatrix = null)
    {
        if (_gpuRenderer == null || _edgeCache == null || meshObject == null)
            return;

        int hoverLineIndex = _gpuRenderer.HoverLineIndex;
        if (hoverLineIndex < 0)
            return;

        var lines = _edgeCache.Lines;
        if (hoverLineIndex >= lines.Count)
            return;

        var line = lines[hoverLineIndex];
        if (line.V1 < 0 || line.V1 >= meshObject.VertexCount ||
            line.V2 < 0 || line.V2 >= meshObject.VertexCount)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;
        Vector3 p1World = matrix.MultiplyPoint3x4(meshObject.Vertices[line.V1].Position);
        Vector3 p2World = matrix.MultiplyPoint3x4(meshObject.Vertices[line.V2].Position);

        Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
        Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

        // 画面外チェック
        if (!previewRect.Contains(p1) && !previewRect.Contains(p2))
            return;

        UnityEditor_Handles.Begin();
        UnityEditor_Handles.Color = new Color(1f, 0f, 0f, 1f);  // 赤色
        UnityEditor_Handles.DrawLine(p1, p2, 3f);
        UnityEditor_Handles.End();
    }

    // ================================================================
    // トランスフォーム表示用行列計算
    // ================================================================

    /// <summary>
    /// メッシュのローカルトランスフォーム行列を取得
    /// BoneTransform の Position/Rotation/Scale から生成
    /// </summary>
    private Matrix4x4 GetLocalTransformMatrix(MeshContext ctx)
    {
        if (ctx?.BoneTransform == null || !ctx.BoneTransform.UseLocalTransform)
            return Matrix4x4.identity;

        return ctx.BoneTransform.TransformMatrix;
    }

    /// <summary>
    /// メッシュのワールドトランスフォーム行列を取得
    /// HierarchyParentIndex を遡って累積計算
    /// </summary>
    private Matrix4x4 GetWorldTransformMatrix(int meshIndex)
    {
        if (meshIndex < 0 || meshIndex >= _meshContextList.Count)
            return Matrix4x4.identity;

        // 親子チェーンを遡ってスタックに積む
        var chain = new System.Collections.Generic.Stack<int>();
        int current = meshIndex;

        // 循環参照検出用
        var visited = new System.Collections.Generic.HashSet<int>();

        while (current >= 0 && current < _meshContextList.Count)
        {
            if (visited.Contains(current))
                break; // 循環参照を検出したら終了

            visited.Add(current);
            chain.Push(current);

            var ctx = _meshContextList[current];
            current = ctx?.HierarchyParentIndex ?? -1;
        }

        // ルートから順に行列を累積
        Matrix4x4 worldMatrix = Matrix4x4.identity;
        while (chain.Count > 0)
        {
            int idx = chain.Pop();
            var ctx = _meshContextList[idx];
            if (ctx?.BoneTransform != null && ctx.BoneTransform.UseLocalTransform)
            {
                worldMatrix *= ctx.BoneTransform.TransformMatrix;
            }
        }

        return worldMatrix;
    }

    /// <summary>
    /// 現在の表示モードに応じた表示用行列を取得
    /// </summary>
    /// <param name="meshIndex">メッシュインデックス</param>
    /// <returns>表示用変換行列（identity, local, または world）</returns>
    private Matrix4x4 GetDisplayMatrix(int meshIndex)
    {
        var editorState = _undoController?.EditorState;
        if (editorState == null)
            return Matrix4x4.identity;

        // WorldTransform が優先（両方ONの場合）
        if (editorState.ShowWorldTransform)
        {
            return GetWorldTransformMatrix(meshIndex);
        }
        else if (editorState.ShowLocalTransform)
        {
            if (meshIndex >= 0 && meshIndex < _meshContextList.Count)
            {
                return GetLocalTransformMatrix(_meshContextList[meshIndex]);
            }
        }

        return Matrix4x4.identity;
    }

    /// <summary>
    /// MeshContext から直接表示用行列を取得（選択メッシュ用）
    /// </summary>
    private Matrix4x4 GetDisplayMatrix(MeshContext ctx)
    {
        int index = _meshContextList.IndexOf(ctx);
        return GetDisplayMatrix(index);
    }
}