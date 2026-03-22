// Assets/Editor/PolyLing.Tools.cs
// Phase 2: 設定フィールド削除・ToolManager統合版
// ツール設定はToolSettingsStorage経由で永続化
// Phase 4: PrimitiveMeshTool対応（メッシュ作成コールバック追加）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Commands;
using Poly_Ling.Context;

public partial class PolyLing : EditorWindow
{
    // ================================================================
    // ツール管理（ToolManagerに統合）
    // ================================================================

    /// <summary>ツールマネージャ</summary>
    // _toolManagerはPolyLingCoreが管理する。Core初期化後に参照する。
    private ToolManager _toolManager => _core?.ToolManager;

    /// <summary>現在のツール（後方互換）</summary>
    private IEditTool _currentTool => _toolManager?.CurrentTool;

    /// <summary>ToolContext（後方互換）</summary>
    private ToolContext _toolContext => _toolManager?.toolContext;

    // ================================================================
    // 型付きアクセス用プロパティ（後方互換）
    // ================================================================
/*
private SelectTool _selectTool => _toolManager?.GetTool<SelectTool>();
private MoveTool _moveTool => _toolManager?.GetTool<MoveTool>();
private KnifeTool _knifeTool => _toolManager?.GetTool<KnifeTool>();
*/
    private AddFaceTool _addFaceTool => _toolManager?.GetTool<AddFaceTool>();
/*    private EdgeTopologyTool _edgeTopoTool => _toolManager?.GetTool<EdgeTopologyTool>();
    private AdvancedSelectTool _advancedSelectTool => _toolManager?.GetTool<AdvancedSelectTool>();
    private SculptTool _sculptTool => _toolManager?.GetTool<SculptTool>();
    private MergeVerticesTool _mergeTool => _toolManager?.GetTool<MergeVerticesTool>();
    private EdgeExtrudeTool _extrudeTool => _toolManager?.GetTool<EdgeExtrudeTool>();
    private FaceExtrudeTool _faceExtrudeTool => _toolManager?.GetTool<FaceExtrudeTool>();
    private EdgeBevelTool _edgeBevelTool => _toolManager?.GetTool<EdgeBevelTool>();
    private LineExtrudeTool _lineExtrudeTool => _toolManager?.GetTool<LineExtrudeTool>();
    private FlipFaceTool _flipFaceTool => _toolManager?.GetTool<FlipFaceTool>();
    private PivotOffsetTool _pivotOffsetTool => _toolManager?.GetTool<PivotOffsetTool>();
    private PrimitiveMeshTool _primitiveMeshTool => _toolManager?.GetTool<PrimitiveMeshTool>();
    */

    /// <summary>
    /// v2.1: メッシュ選択変更時のコールバック（複数選択対応）
    /// MeshListPanelなどからの選択変更で呼ばれる
    /// </summary>
    private void OnMeshSelectionChanged()
    {
        if (_model == null) return;

        // 選択変更はCore側のOnMeshSelectionChangedInternalが処理済み。
        // Editor側は頂点オフセット初期化とViewport更新のみ担当する。
        if (_model.HasValidMeshContextSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.FirstSelectedMeshContext);
            UpdateTopology();
        }

        _unifiedAdapter?.RequestNormal();

        // パネル通知（選択変更）
        _core?.NotifyPanels(ChangeKind.Selection);

        // 再描画
        Repaint();
    }

    /// <summary>
    /// ツール切り替え時のコールバック
    /// </summary>
    private void OnToolChanged(IEditTool oldTool, IEditTool newTool)
    {
        // EditorStateに現在のツール名を記録
        if (_undoController != null)
        {
            _undoController.EditorState.CurrentToolName = newTool?.Name ?? "Select";
        }

        Repaint();
    }

    // ================================================================
    // 設定の同期（Undo/Redo対応）
    // ================================================================

    /// <summary>
    /// EditorStateからツール設定を復元（Undo/Redo時に呼ばれる）
    /// </summary>
    void ApplyToTools(EditorStateContext editorState)
    {
        // ToolSettingsStorageから全ツールに設定を復元
        if (editorState.ToolSettings != null)
        {
            _toolManager.LoadSettings(editorState.ToolSettings);
        }
    }

    /// <summary>
    /// ツール設定をEditorStateに保存（UI変更時に呼ばれる）
    /// </summary>
    private void SyncSettingsFromTool()
    {
        if (_toolManager == null) return;
        // EditorStateのToolSettingsStorageに全ツールの設定を保存
        if (_undoController?.EditorState != null)
        {
            if (_undoController.EditorState.ToolSettings == null)
            {
                _undoController.EditorState.ToolSettings = new ToolSettingsStorage();
            }
            _toolManager.SaveSettings(_undoController.EditorState.ToolSettings);
        }
    }

    // ================================================================
    // 【削除】SyncToolSettings()
    // ================================================================
    // 以下は削除済み - 設定の同期はToolManager.LoadSettings/SaveSettingsで行う
    // private void SyncToolSettings() { ... }

    // ================================================================
    // ToolContext更新
    // ================================================================

    private void SyncFrameStateToToolContext(MeshContext meshContext, Rect rect, Vector3 camPos, float camDist)
    {
        var ctx = _toolManager.toolContext;

        // MeshObjectはToolContext.FirstSelectedMeshContextから自動取得（計算プロパティ）
        ctx.OriginalPositions = meshContext?.OriginalPositions;
        ctx.PreviewRect = rect;
        ctx.CameraPosition = camPos;
        ctx.CameraTarget = _cameraTarget;
        ctx.CameraDistance = camDist;

        // 表示用変換行列
        Matrix4x4 displayMatrix = GetDisplayMatrix(_selectedIndex);
        ctx.DisplayMatrix = displayMatrix;

        // ================================================================
        // ★ ホバー/クリック整合性（Phase 6追加）
        // ホバー時のGPUヒットテスト結果をToolContextに渡す
        // ツールはこれを優先的に使用してホバーとクリックの整合性を保つ
        // ================================================================
        ctx.LastHoverHitResult = _inp.LastHoverHitResult;
        ctx.HoverVertexRadius = HOVER_VERTEX_RADIUS;

        // InputState接続（ツールがEvent.currentを直接参照しないようにするため）
        ctx.InputState = _inp;
        ctx.HoverLineDistance = HOVER_LINE_DISTANCE;

        // DisplayMatrix対応のWorldToScreenPos
        ctx.WorldToScreenPos = (worldPos, previewRect, cameraPos, lookAt) => {
            Vector3 transformedPos = displayMatrix.MultiplyPoint3x4(worldPos);
            return WorldToPreviewPos(transformedPos, previewRect, cameraPos, lookAt);
        };

        // DisplayMatrix対応のFindVertexAtScreenPos
        ctx.FindVertexAtScreenPos = (screenPos, meshObject, previewRect, cameraPos, lookAt, radius) => {
            if (meshObject == null) return -1;
            int closestVertex = -1;
            float closestDist = radius;
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                Vector3 transformedPos = displayMatrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
                Vector2 vertScreenPos = WorldToPreviewPos(transformedPos, previewRect, cameraPos, lookAt);
                float dist = Vector2.Distance(screenPos, vertScreenPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestVertex = i;
                }
            }
            return closestVertex;
        };

        // 注意: クローンを作成して独立したインスタンスにする
        // 参照代入するとRecord作成後に_selectionState.Verticesが変更された時に
        // Recordの中のデータも変わってしまう
        ctx.SelectedVertices = new HashSet<int>(_selectionState.Vertices);
        ctx.VertexOffsets = _vertexOffsets;
        ctx.GroupOffsets = _groupOffsets;
        ctx.UndoController = _undoController;
        ctx.WorkPlane = _undoController?.WorkPlane;
        // v2.1: 複数メッシュ対応 - 選択中の全メッシュの位置を同期
        // モーフプレビュー用: 任意のMeshContextの頂点位置を同期
        
        // GPUバッファのトポロジ再構築コールバック
        // SyncMeshは位置更新のみで軽量、これはトポロジ変更時のみ呼ぶ（重い）

        // 更新モード制御コールバック

        // マルチマテリアル対応
        ctx.CurrentMaterialIndex = meshContext?.CurrentMaterialIndex ?? 0;
        ctx.Materials = meshContext?.Materials;

        // 選択システム
        ctx.SelectionState = _selectionState;
        ctx.TopologyCache = _meshTopology;
        ctx.SelectionOps = _selectionOps;

        ctx.Model = _model;

        if (_undoController?.MeshUndoContext != null && meshContext != null)
        {
            // Materials setterは呼ばない（MaterialOwner経由で既に共有されている）
            _undoController.MeshUndoContext.CurrentMaterialIndex = meshContext.CurrentMaterialIndex;
        }

        // デフォルトマテリアルを同期
        if (_undoController?.MeshUndoContext != null)
        {
            _undoController.MeshUndoContext.DefaultMaterials = _defaultMaterials;
            _undoController.MeshUndoContext.DefaultCurrentMaterialIndex = _defaultCurrentMaterialIndex;
            _undoController.MeshUndoContext.AutoSetDefaultMaterials = _autoSetDefaultMaterials;
        }

        // ツール固有の更新処理
        NotifyToolOfContextUpdate();
    }

    /// <summary>
    /// ツール固有のコンテキスト更新通知
    /// </summary>
    private void NotifyToolOfContextUpdate()
    {
        var ctx = _toolManager.toolContext;
        var current = _toolManager.CurrentTool;
        // MergeToolの更新
        if (current is MergeVerticesTool mergeTool)
        {
            mergeTool.Update(ctx);
        }
        // ExtrudeToolの選択更新
        else if (current is EdgeExtrudeTool extrudeTool)
        {
            extrudeTool.OnSelectionChanged(ctx);
        }
        // FaceExtrudeToolの選択更新
        else if (current is FaceExtrudeTool faceExtrudeTool)
        {
            faceExtrudeTool.OnSelectionChanged(ctx);
        }
        // EdgeBevelToolの選択更新
        else if (current is EdgeBevelTool edgeBevelTool)
        {
            edgeBevelTool.OnSelectionChanged(ctx);
        }
        // LineExtrudeToolの選択更新
        else if (current is LineExtrudeTool lineExtrudeTool)
        {
            lineExtrudeTool.OnSelectionChanged();
        }
    }

    // ================================================================
    // ツール切り替え
    // ================================================================

    /// <summary>
    /// ツール名からツールを設定
    /// </summary>
    private void SetToolByName(string toolName)
    {
        _toolManager.SetTool(toolName);
    }

    /// <summary>
    /// ツール名からツールを復元（Undo/Redo用）
    /// </summary>
    private void RestoreToolFromName(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return;

        _toolManager.SetTool(toolName);
    }
}
