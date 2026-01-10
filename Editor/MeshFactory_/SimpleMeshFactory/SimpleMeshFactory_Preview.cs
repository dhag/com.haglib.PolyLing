// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_Preview.cs
// プレビュー描画（ワイヤーフレーム、選択オーバーレイ、頂点ハンドル）
// UnifiedSystem使用版

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





        // カメラセットアップ後
        _preview.camera.transform.position = camPos;
        _preview.camera.transform.rotation = lookRot * rollRot;

        // ★新システム毎フレーム更新
        Vector2 mousePos = Event.current.mousePosition;
        UpdateUnifiedFrame(rect, mousePos);






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
        // ワイヤフレーム・頂点描画（UnifiedSystem使用）
        // ================================================================
        var pointSize = 0.01f;
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
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        // 選択メッシュ用の表示行列を取得
        Matrix4x4 selectedDisplayMatrix = GetDisplayMatrix(_selectedIndex);

        // ================================================================
        // 頂点インデックス表示（2Dオーバーレイ）
        // ================================================================
        if (_showVertexIndices && meshContext != null && meshContext.IsVisible)
        {
            _unifiedAdapter?.ReadBackVertexFlags();
            DrawVertexIndices(rect, meshContext.MeshObject, camPos, _cameraTarget, selectedDisplayMatrix);
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

        // 矩形選択オーバーレイ描画
        if (_editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    // ================================================================
    // 3D描画ヘルパーメソッド
    // ================================================================

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

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            if (useBackfaceCulling && _unifiedAdapter.IsVertexCulled(_selectedIndex, i))
                continue;

            Vector3 transformedPos = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            Vector2 screenPos = WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 40, 16), i.ToString(), EditorStyles.miniLabel);
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
