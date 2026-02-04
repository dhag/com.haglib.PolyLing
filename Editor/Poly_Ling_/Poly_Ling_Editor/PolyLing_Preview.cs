// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_Preview.cs
// プレビュー描画（ワイヤーフレーム、選択オーバーレイ、頂点ハンドル）
// UnifiedSystem使用版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Rendering;
using Poly_Ling.Core.Rendering;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

public partial class PolyLing
{
    // ================================================================
    // 描画キャッシュ
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
        CleanupHitTestValidation();
    }

    // ================================================================
    // 中央ペイン：プレビュー
    // ================================================================
    private void DrawPreview()
    {
        // ワールド変換行列を計算（親子関係を解決）
        _model?.ComputeWorldMatrices();

        // GPU変換を更新（ローカル/ワールド表示モードに応じて）
        var editorState = _undoController?.EditorState;
        bool useWorldTransform = editorState?.ShowWorldTransform ?? false;
        bool useLocalTransform = editorState?.ShowLocalTransform ?? false;

        // ワールドモード: WorldMatrix（親子関係適用）
        // ローカルモード: LocalMatrix（親子関係無視）
        // どちらでもない: 変換なし
        if (useWorldTransform || useLocalTransform)
        {
            _unifiedAdapter?.UpdateTransform(useWorldTransform);  // trueならWorld、falseならLocal

            // スキンドメッシュの面描画用：GPU変換後の頂点をUnityMeshに書き戻す
            if (useWorldTransform)
            {
                _unifiedAdapter?.WritebackTransformedVertices();
            }
        }

        Rect rect = GUILayoutUtility.GetRect(
            200, 10000,
            200, 10000,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        // 注目点移動XYで使用するためにプレビュー領域を保存
        // Repaintイベント時のみ保存（Layoutイベント時は最大値が返るため）
        if (Event.current.type == EventType.Repaint)
        {
            _lastPreviewRect = rect;
        }

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

        // ============================================================
        // クリップ領域を設定
        // これにより描画が中央ペイン内に制限される
        // また、座標系が (0,0) 起点のローカル座標になる
        // ============================================================
        GUI.BeginClip(rect);

        // ローカル座標系での領域（0,0起点）
        Rect localRect = new Rect(0, 0, rect.width, rect.height);

        _preview.BeginPreview(localRect, GUIStyle.none);

        // ShaderColorSettingsから背景色を取得
        var bgColors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = bgColors.Background;

        _preview.camera.transform.position = camPos;
        Quaternion lookRot = Quaternion.LookRotation(_cameraTarget - camPos, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        _preview.camera.transform.rotation = lookRot * rollRot;





        // カメラセットアップ後
        _preview.camera.transform.position = camPos;
        _preview.camera.transform.rotation = lookRot * rollRot;

        // ★新システム毎フレーム更新（ローカル座標系を使用）
        Vector2 mousePos = Event.current.mousePosition;
        UpdateUnifiedFrame(localRect, mousePos);






        // メッシュ描画
        if (_showMesh)
        {
            if (_showSelectedMeshOnly)
            {
                // 選択メッシュのみ表示モード - 複数選択対応
                if (_model != null && _model.SelectedMeshIndices.Count > 0)
                {
                    foreach (int idx in _model.SelectedMeshIndices)
                    {
                        if (idx < 0 || idx >= _meshContextList.Count) continue;
                        var ctx = _meshContextList[idx];
                        if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;
                        
                        bool isPrimary = (idx == _selectedIndex);
                        DrawMeshWithMaterials(ctx, ctx.UnityMesh, isPrimary ? 1f : 0.8f, idx);
                    }
                }
                else if (meshContext != null && meshContext.IsVisible)
                {
                    // フォールバック: 単一選択
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

                    // 複数選択対応: 選択メッシュは明るく表示
                    bool isSelected = (_model?.SelectedMeshIndices.Contains(i) ?? false) || (i == _selectedIndex);
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
                // 複数選択対応
                if (_model != null && _model.SelectedMeshIndices.Count > 0)
                {
                    foreach (int idx in _model.SelectedMeshIndices)
                    {
                        if (idx < 0 || idx >= _meshContextList.Count) continue;
                        var ctx = _meshContextList[idx];
                        if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;
                        
                        DrawMirroredMesh(ctx, ctx.UnityMesh);
                    }
                }
                else if (meshContext != null && meshContext.IsVisible)
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
        // ワイヤフレーム・頂点描画（UnifiedSystem使用）
        // ================================================================
        var pointSize =ShaderColorSettings.Default.VertexPointScale;
        PrepareUnifiedDrawing(
            _preview.camera,
            _showWireframe,
            _showVertices,
            _showUnselectedWireframe,
            _showUnselectedVertices,
            pointSize);

        DrawUnifiedQueued(_preview);

        _preview.camera.Render();

        CleanupUnifiedDrawing();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(localRect, result, ScaleMode.StretchToFill, false);

        // 選択メッシュ用の表示行列を取得
        Matrix4x4 selectedDisplayMatrix = GetDisplayMatrix(_selectedIndex);

        // ================================================================
        // 面ホバー描画（2Dオーバーレイ）
        // ================================================================
        DrawHoveredFace(localRect, meshContext, selectedDisplayMatrix);

        // 選択面ハイライト描画
        DrawSelectedFaces(localRect, meshContext, selectedDisplayMatrix);

        // ================================================================
        // 頂点インデックス表示（2Dオーバーレイ）- v2.1: 複数選択対応
        // ================================================================
        if (_showVertexIndices)
        {
            _unifiedAdapter?.ReadBackVertexFlags();
            
            // 選択されているメッシュすべてに対して頂点インデックスを表示
            if (_model != null && _model.SelectedMeshIndices.Count > 0)
            {
                foreach (int meshIdx in _model.SelectedMeshIndices)
                {
                    if (meshIdx < 0 || meshIdx >= _model.MeshContextCount)
                        continue;
                        
                    var ctx = _model.GetMeshContext(meshIdx);
                    if (ctx == null || !ctx.IsVisible || ctx.MeshObject == null)
                        continue;
                    
                    // 表示用行列を取得
                    var displayMatrix = GetDisplayMatrix(meshIdx);
                    bool isPrimary = (meshIdx == _selectedIndex);
                    DrawVertexIndicesForMesh(localRect, ctx.MeshObject, meshIdx, isPrimary, camPos, _cameraTarget, displayMatrix);
                }
            }
            else if (meshContext != null && meshContext.IsVisible)
            {
                // フォールバック: 単一選択
                DrawVertexIndices(localRect, meshContext.MeshObject, camPos, _cameraTarget, selectedDisplayMatrix);
            }
        }

        // ローカル原点マーカー
        DrawOriginMarker(localRect, camPos, _cameraTarget);

        // ツールのギズモ描画
        UpdateToolContext(meshContext, localRect, camPos, dist);
        _currentTool?.DrawGizmo(_toolContext);

        // WorkPlaneギズモ描画
        if (_showWorkPlaneGizmo && _vertexEditMode && _currentTool == _addFaceTool)
        {
            DrawWorkPlaneGizmo(localRect, camPos, _cameraTarget);
        }

        // 対称平面ギズモ描画
        if (_symmetrySettings != null && _symmetrySettings.IsEnabled && _symmetrySettings.ShowSymmetryPlane)
        {
            Bounds meshBounds = meshContext.MeshObject != null ? meshContext.MeshObject.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);
            DrawSymmetryPlane(localRect, camPos, _cameraTarget, meshBounds);
        }

        // 矩形選択オーバーレイ描画
        if (_editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay(rect);
        }

        // クリップ領域を終了
        GUI.EndClip();
    }

    // ================================================================
    // 3D描画ヘルパーメソッド
    // ================================================================

    /// <summary>
    /// ホバー中の面をハイライト描画
    /// </summary>
    private void DrawHoveredFace(Rect previewRect, MeshContext meshContext, Matrix4x4 displayMatrix)
    {
        if (_unifiedAdapter == null || meshContext?.MeshObject == null)
            return;

        var unifiedSys = _unifiedAdapter.UnifiedSystem;
        if (unifiedSys == null)
            return;

        // ローカル面インデックスを取得
        if (!unifiedSys.GetHoveredFaceLocal(out int hoveredMeshIndex, out int localFaceIndex))
            return;

        // 選択メッシュのみ描画
        if (hoveredMeshIndex != _selectedIndex)
            return;

        var meshObject = meshContext.MeshObject;
        if (localFaceIndex < 0 || localFaceIndex >= meshObject.FaceCount)
            return;

        var face = meshObject.Faces[localFaceIndex];
        if (face.VertexCount < 3)
            return;

        // カメラ情報を取得
        Vector3 camPos = _preview.camera.transform.position;
        Vector3 lookAt = _cameraTarget;

        // 面の頂点をスクリーン座標に変換
        var screenPoints = new Vector2[face.VertexCount];
        for (int i = 0; i < face.VertexCount; i++)
        {
            int vi = face.VertexIndices[i];
            if (vi < 0 || vi >= meshObject.VertexCount)
                return;

            Vector3 worldPos = displayMatrix.MultiplyPoint3x4(meshObject.Vertices[vi].Position);
            Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);
            screenPoints[i] = screenPos;
        }

        // ShaderColorSettingsから色を取得
        var colors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        Color fillColor = colors.FaceHoveredFill;
        DrawFilledPolygon(screenPoints, fillColor);

        // 輪郭線
        Color edgeColor = colors.FaceHoveredEdge;
        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.color = edgeColor;
        for (int i = 0; i < face.VertexCount; i++)
        {
            int next = (i + 1) % face.VertexCount;
            UnityEditor_Handles.DrawAAPolyLine(2f, screenPoints[i], screenPoints[next]);
        }
        UnityEditor_Handles.EndGUI();
    }

    /// <summary>
    /// 選択中の面をハイライト描画
    /// </summary>
    private void DrawSelectedFaces(Rect previewRect, MeshContext meshContext, Matrix4x4 displayMatrix)
    {
        if (meshContext?.MeshObject == null || _selectionState == null)
            return;

        if (_selectionState.Faces.Count == 0)
            return;

        var meshObject = meshContext.MeshObject;
        Vector3 camPos = _preview.camera.transform.position;
        Vector3 lookAt = _cameraTarget;

        // ShaderColorSettingsから色を取得
        var colors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        Color fillColor = colors.FaceSelectedFill;
        Color edgeColor = colors.FaceSelectedEdge;

        foreach (int faceIndex in _selectionState.Faces)
        {
            if (faceIndex < 0 || faceIndex >= meshObject.FaceCount)
                continue;

            var face = meshObject.Faces[faceIndex];
            if (face.VertexCount < 3)
                continue;

            // 面の頂点をスクリーン座標に変換
            var screenPoints = new Vector2[face.VertexCount];
            bool valid = true;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vi = face.VertexIndices[i];
                if (vi < 0 || vi >= meshObject.VertexCount)
                {
                    valid = false;
                    break;
                }

                Vector3 worldPos = displayMatrix.MultiplyPoint3x4(meshObject.Vertices[vi].Position);
                Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);
                screenPoints[i] = screenPos;
            }

            if (!valid)
                continue;

            // 半透明で塗りつぶし
            DrawFilledPolygon(screenPoints, fillColor);

            // 輪郭線
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = edgeColor;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int next = (i + 1) % face.VertexCount;
                UnityEditor_Handles.DrawAAPolyLine(2f, screenPoints[i], screenPoints[next]);
            }
            UnityEditor_Handles.EndGUI();
        }
    }

    /// <summary>
    /// カメラ距離に基づいて頂点の3Dサイズを計算
    /// ホバー判定（10ピクセル）と一致するサイズを返す
    /// </summary>
    private float CalculatePointSize3D(Vector3 camPos, MeshObject meshObject, float targetPixelSize = 2f)
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
        bool useBackfaceCulling = _unifiedAdapter != null && _unifiedAdapter.BackfaceCullingEnabled;

        // ワールド変換済み座標を取得（ボーンベンディング対応）
        Vector3[] displayPositions = null;
        int vertexOffset = 0;
        if (_unifiedAdapter != null)
        {
            var bufferManager = _unifiedAdapter.BufferManager;
            if (bufferManager != null)
            {
                displayPositions = bufferManager.GetDisplayPositions();
                // 選択メッシュの頂点オフセットを取得
                int unifiedIdx = _unifiedAdapter.ContextToUnifiedMeshIndex(_selectedIndex);
                if (unifiedIdx >= 0 && bufferManager.MeshInfos != null)
                {
                    vertexOffset = (int)bufferManager.MeshInfos[unifiedIdx].VertexStart;
                }
            }
        }

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            if (useBackfaceCulling && _unifiedAdapter.IsVertexCulled(_selectedIndex, i))
                continue;

            // ワールド変換済み座標を使用（利用可能な場合）
            Vector3 worldPos;
            if (displayPositions != null && (vertexOffset + i) < displayPositions.Length)
            {
                worldPos = displayPositions[vertexOffset + i];
            }
            else
            {
                worldPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            }

            Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 40, 16), i.ToString(), EditorStyles.miniLabel);
        }
    }

    /// <summary>
    /// v2.1: 指定メッシュの頂点インデックスを表示（複数選択対応）
    /// </summary>
    /// <param name="meshIndex">表示対象のメッシュインデックス</param>
    /// <param name="isPrimary">プライマリ選択かどうか</param>
    private void DrawVertexIndicesForMesh(Rect previewRect, MeshObject meshObject, int meshIndex, bool isPrimary, Vector3 camPos, Vector3 lookAt, Matrix4x4? displayMatrix = null)
    {
        if (meshObject == null)
            return;

        Matrix4x4 matrix = displayMatrix ?? Matrix4x4.identity;
        bool useBackfaceCulling = _unifiedAdapter != null && _unifiedAdapter.BackfaceCullingEnabled;

        // ワールド変換済み座標を取得（ボーンベンディング対応）
        Vector3[] displayPositions = null;
        int vertexOffset = 0;
        if (_unifiedAdapter != null)
        {
            var bufferManager = _unifiedAdapter.BufferManager;
            if (bufferManager != null)
            {
                displayPositions = bufferManager.GetDisplayPositions();
                // 指定メッシュの頂点オフセットを取得
                int unifiedIdx = _unifiedAdapter.ContextToUnifiedMeshIndex(meshIndex);
                if (unifiedIdx >= 0 && bufferManager.MeshInfos != null)
                {
                    vertexOffset = (int)bufferManager.MeshInfos[unifiedIdx].VertexStart;
                }
            }
        }

        // プライマリ選択は通常表示、追加選択は薄い表示
        var labelStyle = isPrimary ? EditorStyles.miniLabel : EditorStyles.whiteMiniLabel;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            if (useBackfaceCulling && _unifiedAdapter.IsVertexCulled(meshIndex, i))
                continue;

            // ワールド変換済み座標を使用（利用可能な場合）
            Vector3 worldPos;
            if (displayPositions != null && (vertexOffset + i) < displayPositions.Length)
            {
                worldPos = displayPositions[vertexOffset + i];
            }
            else
            {
                worldPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            }

            Vector2 screenPos = WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            // ローカルインデックスのみ表示
            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 40, 16), i.ToString(), labelStyle);
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

        // ShaderColorSettingsから軸色を取得
        var axisColors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        float axisLength = 0.2f;

        Vector3 xEnd = origin + Vector3.right * axisLength;
        Vector2 xScreen = WorldToPreviewPos(xEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, xScreen, axisColors.AxisX);

        Vector3 yEnd = origin + Vector3.up * axisLength;
        Vector2 yScreen = WorldToPreviewPos(yEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, yScreen, axisColors.AxisY);

        Vector3 zEnd = origin + Vector3.forward * axisLength;
        Vector2 zScreen = WorldToPreviewPos(zEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, zScreen, axisColors.AxisZ);

        UnityEditor_Handles.BeginGUI();
        float centerSize = 4f;
        UnityEditor_Handles.DrawRect(new Rect(
            originScreen.x - centerSize / 2,
            originScreen.y - centerSize / 2,
            centerSize,
            centerSize), new Color(1f, 1f, 1f, 0.7f));
        UnityEditor_Handles.EndGUI();
    }


    private void DrawBoxSelectOverlay(Rect clipRect)
    {
        UnityEditor_Handles.BeginGUI();

        // _boxSelectStart/End はウィンドウ座標で記録されている
        // GUI.BeginClip(clipRect) 後はローカル座標系なので、clipRectの位置を引く
        Vector2 localStart = _boxSelectStart - clipRect.position;
        Vector2 localEnd = _boxSelectEnd - clipRect.position;

        Rect selectRect = new Rect(
            Mathf.Min(localStart.x, localEnd.x),
            Mathf.Min(localStart.y, localEnd.y),
            Mathf.Abs(localEnd.x - localStart.x),
            Mathf.Abs(localEnd.y - localStart.y)
        );

        // ShaderColorSettingsから色を取得
        var boxColors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        UnityEditor_Handles.DrawRect(selectRect, boxColors.BoxSelectFill);
        DrawRectBorder(selectRect, boxColors.BoxSelectBorder);

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // トランスフォーム表示用行列計算
    // ================================================================

    /// <summary>
    /// メッシュのローカルトランスフォーム行列を取得
    /// MeshContext.LocalMatrix を使用（BoneTransform.UseLocalTransformを考慮済み）
    /// </summary>
    private Matrix4x4 GetLocalTransformMatrix(MeshContext ctx)
    {
        if (ctx == null)
            return Matrix4x4.identity;

        return ctx.LocalMatrix;
    }

    /// <summary>
    /// メッシュのワールドトランスフォーム行列を取得
    /// MeshContext.WorldMatrix を使用（ComputeWorldMatrices()で事前計算済み）
    /// </summary>
    private Matrix4x4 GetWorldTransformMatrix(int meshIndex)
    {
        if (meshIndex < 0 || meshIndex >= _meshContextList.Count)
            return Matrix4x4.identity;

        var ctx = _meshContextList[meshIndex];
        if (ctx == null)
            return Matrix4x4.identity;

        return ctx.WorldMatrix;
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

        // WorldTransformモードでは頂点は既にGPUで変換済み（WritebackTransformedVertices）
        // なのでIdentityを返す
        if (editorState.ShowWorldTransform)
        {
            return Matrix4x4.identity;
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