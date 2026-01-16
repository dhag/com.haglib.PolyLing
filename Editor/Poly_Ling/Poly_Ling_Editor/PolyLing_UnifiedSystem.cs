// Assets/Editor/SimpleMeshFactory_UnifiedSystem.cs
// PolyLing - 統合システム拡張
// UnifiedMeshSystemとの連携機能

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Core;
using Poly_Ling.Core.Rendering;
using Poly_Ling.Model;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;

public partial class PolyLing
{
    // ============================================================
    // 統合システム
    // ============================================================

    private UnifiedSystemAdapter _unifiedAdapter;

    // UI設定
    private bool _foldUnifiedSystem = false;

    // ============================================================
    // プロパティ
    // ============================================================

    /// <summary>
    /// 統合システムアダプター
    /// </summary>
    public UnifiedSystemAdapter UnifiedAdapter => _unifiedAdapter;

    // ============================================================
    // 初期化・クリーンアップ
    // ============================================================

    /// <summary>
    /// 統合システムを初期化（OnEnableから呼び出し、失敗時はウィンドウを閉じる）
    /// </summary>
    /// <returns>初期化成功時 true</returns>
    private bool InitializeUnifiedSystem()
    {
        if (_unifiedAdapter != null)
            return true;

        _unifiedAdapter = new UnifiedSystemAdapter();

        if (!_unifiedAdapter.Initialize())
        {
            Debug.LogError("[PolyLing] Failed to initialize unified system");
            _unifiedAdapter?.Dispose();
            _unifiedAdapter = null;
            
            EditorUtility.DisplayDialog(
                "Initialization Error",
                "Failed to initialize unified rendering system.\nThe editor window will be closed.",
                "OK");
            
            return false;
        }


        // 参照を設定
        _unifiedAdapter.SetModelContext(_model);
        _unifiedAdapter.SetSelectionState(_selectionState);

        // アクティブメッシュを設定
        if (_selectedIndex >= 0)
        {
            _unifiedAdapter.SetActiveMesh(0, _selectedIndex);
        }


        // レンダリングモード設定
        _unifiedAdapter.UseUnifiedRendering = true;

        Debug.Log("[PolyLing] Unified system initialized");
        return true;
    }

    /// <summary>
    /// 統合システムをクリーンアップ
    /// </summary>
    private void CleanupUnifiedSystem()
    {
        _unifiedAdapter?.Dispose();
        _unifiedAdapter = null;
    }

    // ============================================================
    // 更新通知
    // ============================================================

    /// <summary>
    /// トポロジー変更を統合システムに通知
    /// </summary>
    private void NotifyUnifiedTopologyChanged()
    {
        _unifiedAdapter.NotifyTopologyChanged();
    }

    /// <summary>
    /// 位置変更を統合システムに通知
    /// </summary>
    private void NotifyUnifiedTransformChanged()
    {
        _unifiedAdapter.NotifyTransformChanged();
    }

    /// <summary>
    /// 選択変更を統合システムに通知
    /// </summary>
    private void NotifyUnifiedSelectionChanged()
    {
        _unifiedAdapter.NotifySelectionChanged();
    }

    /// <summary>
    /// アクティブメッシュ変更を統合システムに通知
    /// </summary>
    private void NotifyUnifiedActiveMeshChanged()
    {
        _unifiedAdapter.SetActiveMesh(0, _selectedIndex);
        _unifiedAdapter.NotifySelectionChanged();
    }

    // ============================================================
    // フレーム更新
    // ============================================================

    /// <summary>
    /// 統合システムのフレーム更新
    /// 
    /// 【座標系の設計 - 重要】
    /// スクリーン座標は adjustedRect 座標系（タブオフセット付き）を使用する。
    /// 
    /// previewRect: タブなしのプレビュー領域（rect座標系）
    /// mousePosition: rect座標系のマウス位置
    /// 
    /// GUI.BeginClip()使用後はローカル座標系（0,0起点）になるため、
    /// タブオフセット計算は不要。previewRectとmousePositionをそのまま使用。
    /// </summary>
    private void UpdateUnifiedFrame(Rect previewRect, Vector2 mousePosition)
    {
        // GUI.BeginClip()後はローカル座標系なので、タブオフセット不要
        // previewRectは (0, 0, width, height) の形式
        // mousePositionもローカル座標系

        // カメラ位置計算
        Quaternion rotation = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
        Vector3 cameraPosition = _cameraTarget + rotation * new Vector3(0, 0, -_cameraDistance);
        float fov = _preview?.cameraFieldOfView ?? 30f;

        _unifiedAdapter.UpdateFrame(
            cameraPosition,
            _cameraTarget,
            fov,
            previewRect,
            mousePosition,
            _rotationZ);
    }

    // ============================================================
    // 描画
    // ============================================================

    /// <summary>
    /// 統合システムのキュー描画
    /// </summary>
    private void DrawUnifiedQueued(PreviewRenderUtility preview)
    {
        _unifiedAdapter.DrawQueued(preview);
    }

    /// <summary>
    /// 統合システムの描画クリーンアップ
    /// </summary>
    private void CleanupUnifiedDrawing()
    {
        _unifiedAdapter.CleanupQueued();
    }

    // ============================================================
    // UI
    // ============================================================

    /// <summary>
    /// 統合システム設定UIを描画
    /// </summary>
    private void DrawUnifiedSystemUI()
    {
        _foldUnifiedSystem = EditorGUILayout.Foldout(_foldUnifiedSystem, "Unified System", true);

        if (!_foldUnifiedSystem)
            return;

        EditorGUI.indentLevel++;

        // 統計情報
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

        var bufferManager = _unifiedAdapter.BufferManager;
        if (bufferManager != null)
        {
            EditorGUILayout.LabelField($"Vertices: {bufferManager.TotalVertexCount}");
            EditorGUILayout.LabelField($"Lines: {bufferManager.TotalLineCount}");
            EditorGUILayout.LabelField($"Faces: {bufferManager.TotalFaceCount}");
            EditorGUILayout.LabelField($"Meshes: {bufferManager.MeshCount}");
            EditorGUILayout.LabelField($"Mirror: {(bufferManager.MirrorEnabled ? "On" : "Off")}");
        }

        // ホバー情報（グローバル・ローカル両方表示）
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Hover", EditorStyles.boldLabel);
        
        var unifiedSys = _unifiedAdapter.UnifiedSystem;
        
        // 頂点
        int globalVertex = _unifiedAdapter.HoverVertexIndex;
        string vertexStr = $"Vertex: {globalVertex}";
        if (globalVertex >= 0 && unifiedSys != null)
        {
            if (unifiedSys.GetHoveredVertexLocal(out int vMesh, out int vLocal))
            {
                vertexStr += $" (Mesh{vMesh}, Local{vLocal})";
            }
        }
        EditorGUILayout.LabelField(vertexStr);
        
        // 線分（頂点ローカルインデックスも表示）
        int globalLine = _unifiedAdapter.HoverLineIndex;
        string lineStr = $"Line: {globalLine}";
        if (globalLine >= 0 && bufferManager != null)
        {
            if (bufferManager.GetLineVerticesLocal(globalLine, out int lMesh, out int lV1, out int lV2))
            {
                lineStr += $" (Mesh{lMesh}, V{lV1}-V{lV2})";
            }
        }
        EditorGUILayout.LabelField(lineStr);
        
        // 面
        int globalFace = _unifiedAdapter.HoverFaceIndex;
        string faceStr = $"Face: {globalFace}";
        if (globalFace >= 0 && unifiedSys != null)
        {
            if (unifiedSys.GetHoveredFaceLocal(out int fMesh, out int fLocal))
            {
                faceStr += $" (Mesh{fMesh}, Local{fLocal})";
            }
        }
        EditorGUILayout.LabelField(faceStr);

        // デバッグボタン
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Log Status"))
        {
            _unifiedAdapter.LogStatus();
        }

        if (GUILayout.Button("Rebuild All"))
        {
            Debug.Log("[Debug] === Full Rebuild ===");
            _unifiedAdapter.SetModelContext(_model);
            _unifiedAdapter.NotifyTopologyChanged();
            var sysAll = _unifiedAdapter.UnifiedSystem;
            if (sysAll != null)
            {
                sysAll.BeginFrame();
                var level = sysAll.ProcessUpdates();
                Debug.Log($"[Debug] DirtyLevel: {level}");
                sysAll.ExecuteUpdates(level);
                sysAll.EndFrame();
            }
            Debug.Log($"[Debug] Result - Vertices: {_unifiedAdapter.BufferManager?.TotalVertexCount ?? 0}, Lines: {_unifiedAdapter.BufferManager?.TotalLineCount ?? 0}");
            Repaint();
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 既存描画を抑止するか（Preview描画から呼び出される）
    /// </summary>
    public bool ShouldSuppressLegacyRendering()
    {
        return true;  // 常に新システムを使用
    }

    // ============================================================
    // 対称設定との連携
    // ============================================================

    /// <summary>
    /// 対称設定を統合システムに適用
    /// </summary>
    private void ApplySymmetryToUnifiedSystem()
    {
        var symmetrySettings = GetCurrentSymmetrySettings();
        if (symmetrySettings != null)
        {
            _unifiedAdapter.SetSymmetrySettings(symmetrySettings);
        }
    }

    /// <summary>
    /// 現在の対称設定を取得（既存コードから）
    /// </summary>
    private SymmetrySettings GetCurrentSymmetrySettings()
    {
        // TODO: 既存の対称システムとの連携を実装
        return null;
    }

    // ============================================================
    // 選択操作との連携
    // ============================================================

    /// <summary>
    /// 統合システムからホバー頂点を取得
    /// </summary>
    private int GetHoveredVertexFromUnified()
    {
        return _unifiedAdapter.GetHoveredVertexLocal();
    }

    /// <summary>
    /// 統合システムで頂点ヒットテスト
    /// </summary>
    private int FindNearestVertexUnified(Vector2 screenPos, float radius)
    {
        return _unifiedAdapter.FindNearestVertex(screenPos, radius);
    }

    // ============================================================
    // 描画との連携
    // ============================================================

    /// <summary>
    /// 統合システムで描画を準備（選択/非選択フィルタリング対応）
    /// </summary>
    private void PrepareUnifiedDrawing(
        Camera camera,
        bool showWireframe,
        bool showVertices,
        bool showUnselectedWireframe,
        bool showUnselectedVertices,
        float pointSize,
        float alpha = 1f)
    {
        // 背面カリング設定を反映
        _unifiedAdapter.BackfaceCullingEnabled = _undoController?.EditorState.BackfaceCullingEnabled ?? true;
        
        // 【暫定】毎フレーム選択状態を同期（TECHNICAL_DEBT参照）
        SyncSelectionFromLegacy();
        
        // 選択フラグを直接更新
        var bufMgr = _unifiedAdapter.BufferManager;
        if (bufMgr != null)
        {
            bufMgr.SetActiveMesh(0, _selectedIndex);
            bufMgr.UpdateAllSelectionFlags();
            
            // 面・線分の可視性計算（Culledフラグ設定）
            var viewport = new Rect(0, 0, camera.pixelWidth, camera.pixelHeight);
            bufMgr.DispatchClearBuffersGPU();
            bufMgr.ComputeScreenPositionsGPU(camera.projectionMatrix * camera.worldToCameraMatrix, viewport);
            bufMgr.DispatchFaceVisibilityGPU();
            bufMgr.DispatchLineVisibilityGPU();
        }
        
        // ContextIndex → UnifiedMeshIndex に変換
        int unifiedMeshIndex = _unifiedAdapter.ContextToUnifiedMeshIndex(_selectedIndex);
        
        _unifiedAdapter.PrepareDrawing(
            camera,
            showWireframe,
            showVertices,
            showUnselectedWireframe,
            showUnselectedVertices,
            unifiedMeshIndex,
            pointSize,
            alpha);
    }
}
