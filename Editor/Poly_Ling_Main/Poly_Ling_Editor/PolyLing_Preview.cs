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
using Poly_Ling.Core;
using Poly_Ling.Remote;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

public partial class PolyLing
{
    // ================================================================
    // プレビューキャプチャ
    // ================================================================
    private bool _captureRequested;
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
        //CleanupHitTestValidation();
    }

    // ================================================================
    // 中央ペイン：プレビュー
    // ================================================================
    private void DrawPreview()
    {
        // ================================================================
        // 計算（常に実行。ViewportPanel経由の入力処理でも必要）
        // ================================================================
        _model?.ComputeWorldMatrices();

        var editorState = _undoController?.EditorState;
        bool useWorldTransform = editorState?.ShowWorldTransform ?? false;
        bool useLocalTransform = editorState?.ShowLocalTransform ?? false;
        if (useWorldTransform || useLocalTransform)
        {
            _unifiedAdapter?.UpdateTransform(useWorldTransform);
            if (useWorldTransform)
                _unifiedAdapter?.WritebackTransformedVertices();
        }

        // ViewportPanelが開いている場合: プレースホルダー表示のみ
        if (Poly_Ling.MeshListV2.ViewportPanel.IsOpen)
        {
            Rect placeholder = GUILayoutUtility.GetRect(
                200, 10000, 200, 10000,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                _lastPreviewRect = placeholder;
                EditorGUI.DrawRect(placeholder, new Color(0.15f, 0.15f, 0.15f));
                EditorGUI.LabelField(placeholder, "Viewport Panel Active",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
            }
            return;
        }

        // ================================================================
        // 通常フロー（ViewportPanel閉時）
        // ================================================================
        if (_viewportCore == null) return;

        // メッシュが未選択の場合: 背景のみ表示
        var meshContext = _model.FirstSelectedMeshContext;
        if (meshContext == null && Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive)
            meshContext = _model.FirstSelectedDrawableMeshContext;

        Rect rect = GUILayoutUtility.GetRect(
            200, 10000, 200, 10000,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (Event.current.type == EventType.Repaint)
            _lastPreviewRect = rect;

        if (meshContext == null)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.LabelField(rect, "Select a mesh", EditorStyles.centeredGreyMiniLabel);
            UnityEditor_Handles.EndGUI();
            return;
        }

        // ================================================================
        // ViewportCore の表示設定を EditorState から同期
        // ================================================================
        _viewportCore.ShowMesh = _showMesh;
        _viewportCore.ShowWireframe = _showWireframe;
        _viewportCore.ShowVertices = _showVertices;
        _viewportCore.ShowUnselectedWireframe = _showUnselectedWireframe;
        _viewportCore.ShowUnselectedVertices = _showUnselectedVertices;
        _viewportCore.ShowSelectedMeshOnly = _showSelectedMeshOnly;
        _viewportCore.BackfaceCulling = _undoController?.EditorState.BackfaceCullingEnabled ?? true;
        _viewportCore.ShowVertexIndices = _showVertexIndices;
        _viewportCore.ShowBones = _showBones;
        _viewportCore.ShowFocusPoint = _showFocusPoint;

        // カメラ状態を ViewportCore に同期
        _viewportCore.RotX = _rotationX;
        _viewportCore.RotY = _rotationY;
        _viewportCore.RotZ = _rotationZ;
        _viewportCore.Distance = _cameraDistance;
        _viewportCore.Target = _cameraTarget;

        // 入力処理コールバック（HandleInput を注入）
        _viewportCore.OnHandleInput = evt =>
        {
            // HandleInput が実行される前にカメラ状態を同期
            _viewportCore.RotX = _rotationX;
            _viewportCore.RotY = _rotationY;
            _viewportCore.RotZ = _rotationZ;
            _viewportCore.Distance = _cameraDistance;
            _viewportCore.Target = _cameraTarget;

            var mc = _model.FirstSelectedMeshContext;
            if (mc == null && Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive)
                mc = _model.FirstSelectedDrawableMeshContext;
            if (mc == null) return;

            HandleInput(evt.Rect, mc, evt.CameraPos, evt.CameraTarget, evt.CameraDistance);

            // HandleInput がカメラを変更した場合は ViewportCore に反映
            _viewportCore.RotX = _rotationX;
            _viewportCore.RotY = _rotationY;
            _viewportCore.RotZ = _rotationZ;
            _viewportCore.Distance = _cameraDistance;
            _viewportCore.Target = _cameraTarget;
        };

        // オーバーレイコールバック（PolyLing 固有ギズモ）
        // ※ absRect をキャプチャ: DrawBoxSelectOverlay/DrawLassoSelectOverlay は
        //   ウィンドウ座標をローカル座標に変換するため absolute rect が必要
        Rect absRect = rect;
        _viewportCore.OnDrawOverlay = evt =>
        {
            var mc = _model.FirstSelectedMeshContext;
            if (mc == null) return;

            UpdateToolContext(mc, evt.Rect, evt.CameraPos, evt.CameraDistance);
            _currentTool?.DrawGizmo(_toolContext);

            if (_showBones) DrawBoneGizmo(evt.Rect, evt.CameraPos, evt.CameraTarget);
            if (_showWorkPlaneGizmo && _vertexEditMode && _currentTool == _addFaceTool)
                DrawWorkPlaneGizmo(evt.Rect, evt.CameraPos, evt.CameraTarget);

            // 対称平面
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled && _symmetrySettings.ShowSymmetryPlane)
            {
                Bounds meshBounds = mc.MeshObject != null
                    ? mc.MeshObject.CalculateBounds()
                    : new Bounds(Vector3.zero, Vector3.one);
                DrawSymmetryPlane(evt.Rect, evt.CameraPos, evt.CameraTarget, meshBounds);
            }

            if (_inp.EditState == VertexEditState.BoxSelecting)
                DrawBoxSelectOverlay(absRect);
            if (_inp.EditState == VertexEditState.LassoSelecting)
                DrawLassoSelectOverlay(absRect);
        };

        _viewportCore.Draw(rect);
    }


    // ================================================================
    // プレビューキャプチャ → Remote送信キュー
    // ================================================================

    /// <summary>
    /// プレビューテクスチャをキャプチャしてRemoteServerの画像リストに追加
    /// </summary>
    private void CapturePreviewToRemote(Texture src)
    {
        if (src == null) return;

        // RenderTextureに転写してTexture2Dに変換
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;

        var tex2d = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tex2d.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex2d.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        // RemoteServerの画像リストに追加
        var server = RemoteServer.FindInstance();
        if (server != null)
        {
            server.AddCapturedImage(tex2d);
        }
        else
        {
            Debug.LogWarning("[PolyLing] RemoteServer が開かれていません");
        }

        DestroyImmediate(tex2d);
    }

    // ================================================================
    // 選択オーバーレイ（矩形選択・投げ縄選択）
    // ================================================================

    private void DrawBoxSelectOverlay(Rect clipRect)
    {
        UnityEditor_Handles.BeginGUI();

        // _inp.BoxSelectStart/End はウィンドウ座標で記録されている
        // GUI.BeginClip(clipRect) 後はローカル座標系なので、clipRectの位置を引く
        Vector2 localStart = _inp.BoxSelectStart - clipRect.position;
        Vector2 localEnd = _inp.BoxSelectEnd - clipRect.position;

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

    private void DrawLassoSelectOverlay(Rect clipRect)
    {
        if (_inp.LassoPoints == null || _inp.LassoPoints.Count < 2)
            return;

        UnityEditor_Handles.BeginGUI();

        var boxColors = _unifiedAdapter?.ColorSettings ?? ShaderColorSettings.Default;
        Color borderColor = boxColors.BoxSelectBorder;

        // _inp.LassoPointsはウィンドウ座標 → clipRectのオフセットを引いてローカル座標に変換
        Vector2 offset = clipRect.position;

        // 投げ縄の線分を描画
        UnityEditor_Handles.color = borderColor;
        for (int i = 0; i < _inp.LassoPoints.Count - 1; i++)
        {
            Vector2 p1 = _inp.LassoPoints[i] - offset;
            Vector2 p2 = _inp.LassoPoints[i + 1] - offset;
            UnityEditor_Handles.DrawLine(p1, p2);
        }

        // 閉じ線（最後の点→最初の点）
        if (_inp.LassoPoints.Count >= 3)
        {
            Vector2 first = _inp.LassoPoints[0] - offset;
            Vector2 last = _inp.LassoPoints[_inp.LassoPoints.Count - 1] - offset;
            UnityEditor_Handles.color = new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * 0.5f);
            UnityEditor_Handles.DrawLine(last, first);
        }

        UnityEditor_Handles.EndGUI();
    }


    private Matrix4x4 GetLocalTransformMatrix(MeshContext ctx)
    {
        if (ctx == null)
            return Matrix4x4.identity;

        return ctx.LocalMatrix;
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


}