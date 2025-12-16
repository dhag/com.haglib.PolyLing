// Assets/Editor/SimpleMeshFactory.Tools.cs
// Phase 2: 設定フィールド削除・ToolManager統合版
// ツール設定はToolSettingsStorage経由で永続化
// Phase 4: PrimitiveMeshTool対応（メッシュ作成コールバック追加）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;


public partial class SimpleMeshFactory : EditorWindow
{
    // ================================================================
    // ツール管理（ToolManagerに統合）
    // ================================================================

    /// <summary>ツールマネージャ</summary>
    private ToolManager _toolManager;

    /// <summary>現在のツール（後方互換）</summary>
    private IEditTool _currentTool => _toolManager?.CurrentTool;

    /// <summary>ToolContext（後方互換）</summary>
    private ToolContext _toolContext => _toolManager?.toolContext;

    // ================================================================
    // 型付きアクセス用プロパティ（後方互換）
    // ================================================================

    private SelectTool _selectTool => _toolManager?.GetTool<SelectTool>();
    private MoveTool _moveTool => _toolManager?.GetTool<MoveTool>();
    private KnifeTool _knifeTool => _toolManager?.GetTool<KnifeTool>();
    private AddFaceTool _addFaceTool => _toolManager?.GetTool<AddFaceTool>();
    private EdgeTopologyTool _edgeTopoTool => _toolManager?.GetTool<EdgeTopologyTool>();
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

    // ================================================================
    // 【削除】ツール設定フィールド
    // ================================================================
    // 以下は削除済み - MoveToolが自身のMoveSettingsで管理
    // [SerializeField] private bool _useMagnet = false;
    // [SerializeField] private float _magnetRadius = 0.5f;
    // [SerializeField] private FalloffType _magnetFalloff = FalloffType.Smooth;

    // ================================================================
    // 初期化
    // ================================================================

    private void InitializeTools()
    {
        // ToolManagerを作成
        _toolManager = new ToolManager();

        // 全ツールを登録
        ToolRegistry.RegisterAllTools(_toolManager);

        // ツール切り替えイベントを購読
        _toolManager.OnToolChanged += OnToolChanged;

        // ToolContextの初期設定
        SetupToolContext();

        // 【削除】SyncToolSettings()呼び出し
        // ツール設定はEditorStateから復元される
    }

    /// <summary>
    /// ToolContextの初期設定
    /// </summary>
    private void SetupToolContext()
    {
        var ctx = _toolManager.toolContext;

        // コールバック設定（エディタ機能への橋渡し）
        ctx.RecordSelectionChange = RecordSelectionChange;
        ctx.Repaint = Repaint;
        ctx.WorldToScreenPos = WorldToPreviewPos;
        ctx.ScreenDeltaToWorldDelta = ScreenDeltaToWorldDelta;
        ctx.FindVertexAtScreenPos = FindVertexAtScreenPos;
        ctx.ScreenPosToRay = ScreenPosToRay;
        ctx.WorkPlane = _undoController?.WorkPlane;
        ctx.CurrentMaterialIndex = 0;
        ctx.Materials = null;
        ctx.SelectionState = _selectionState;
        ctx.TopologyCache = _meshTopology;
        ctx.SelectionOps = _selectionOps;

        // Phase 4追加: メッシュ作成コールバック
        ctx.CreateNewMeshContext = OnMeshDataCreatedAsNew;
        ctx.AddMeshDataToCurrentMesh = OnMeshDataCreatedAddToCurrent;

        // MeshContext操作コールバック
        ctx.Model = _model;
        ctx.AddMeshContext = AddMeshContextWithUndo;
        ctx.AddMeshContexts = AddMeshContextsWithUndo;  // ★追加
        ctx.RemoveMeshContext = RemoveMeshContextWithUndo;
        ctx.SelectMeshContext = SelectMeshContentWithUndo;
        ctx.DuplicateMeshContent = DuplicateMeshContentWithUndo;
        ctx.ReorderMeshContext = ReorderMeshContentWithUndo;

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

    private void UpdateToolContext(MeshContext meshContext, Rect rect, Vector3 camPos, float camDist)
    {
        var ctx = _toolManager.toolContext;

        ctx.MeshData = meshContext?.Data;
        ctx.OriginalPositions = meshContext?.OriginalPositions;
        ctx.PreviewRect = rect;
        ctx.CameraPosition = camPos;
        ctx.CameraTarget = _cameraTarget;
        ctx.CameraDistance = camDist;
        ctx.SelectedVertices = _selectedVertices;
        ctx.VertexOffsets = _vertexOffsets;
        ctx.GroupOffsets = _groupOffsets;
        ctx.UndoController = _undoController;
        ctx.WorkPlane = _undoController?.WorkPlane;
        ctx.SyncMesh = () => SyncMeshFromData(meshContext);

        // マルチマテリアル対応
        ctx.CurrentMaterialIndex = meshContext?.CurrentMaterialIndex ?? 0;
        ctx.Materials = meshContext?.Materials;

        // 選択システム
        ctx.SelectionState = _selectionState;
        ctx.TopologyCache = _meshTopology;
        ctx.SelectionOps = _selectionOps;

        // ModelContext（Phase 3追加）
        ctx.Model = _model;
        ctx.AddMeshContexts = AddMeshContextsWithUndo;  // ★追加
        ctx.AddMeshContext = AddMeshContextWithUndo;
        ctx.RemoveMeshContext = RemoveMeshContextWithUndo;
        ctx.SelectMeshContext = SelectMeshContentWithUndo;
        ctx.DuplicateMeshContent = DuplicateMeshContentWithUndo;
        ctx.ReorderMeshContext = ReorderMeshContentWithUndo;

        // Phase 4追加: メッシュ作成コールバック
        ctx.CreateNewMeshContext = OnMeshDataCreatedAsNew;
        ctx.AddMeshDataToCurrentMesh = OnMeshDataCreatedAddToCurrent;

        // UndoコンテキストにもMaterialsを同期
        if (_undoController?.MeshContext != null && meshContext != null)
        {
            _undoController.MeshContext.Materials = meshContext.Materials;
            _undoController.MeshContext.CurrentMaterialIndex = meshContext.CurrentMaterialIndex;
        }

        // デフォルトマテリアルを同期
        if (_undoController?.MeshContext != null)
        {
            _undoController.MeshContext.DefaultMaterials = _defaultMaterials;
            _undoController.MeshContext.DefaultCurrentMaterialIndex = _defaultCurrentMaterialIndex;
            _undoController.MeshContext.AutoSetDefaultMaterials = _autoSetDefaultMaterials;
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
    // メッシュ作成コールバック（Phase 4追加）
    // ================================================================

    /// <summary>
    /// MeshDataから新しいMeshContextを作成（PrimitiveMeshTool用ラッパー）
    /// </summary>
    private void OnMeshDataCreatedAsNew(MeshData meshData, string name)
    {
        // SimpleMeshFactory_MeshIO.csのCreateNewMeshContextを呼び出し
        CreateNewMeshContext(meshData, name);
    }

    /// <summary>
    /// 現在選択中のメッシュにMeshDataを追加（PrimitiveMeshTool用ラッパー）
    /// </summary>
    private void OnMeshDataCreatedAddToCurrent(MeshData meshData, string name)
    {
        // SimpleMeshFactory_MeshIO.csのAddMeshDataToCurrentを呼び出し
        AddMeshDataToCurrent(meshData, name);
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

    // ================================================================
    // クリーンアップ
    // ================================================================

    private void CleanupTools()
    {
        if (_toolManager != null)
        {
            _toolManager.OnToolChanged -= OnToolChanged;
            _toolManager = null;
        }
    }

    // ================================================================
    // メッシュリスト操作（Undo対応）- Phase 3追加
    // ================================================================

    /// <summary>
    /// メッシュコンテキストを追加（Undo対応）
    /// </summary>
    private void AddMeshContextWithUndo(MeshContext meshContext)
    {
        if (meshContext == null) return;

        int oldIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        _meshContextList.Add(meshContext);
        _selectedIndex = insertIndex;

        // Undo記録
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldIndex, _selectedIndex);
        }

        InitVertexOffsets();
        LoadMeshContextToUndoController(_model.CurrentMeshContext); 
        UpdateTopology();  // ← これを追加
        Repaint();
    }
    /// <summary>
    /// メッシュコンテキストを複数追加（Undo対応・バッチ）
    /// </summary>
    private void AddMeshContextsWithUndo(IList<MeshContext> meshContexts)
    {
        if (meshContexts == null || meshContexts.Count == 0) return;

        int oldIndex = _selectedIndex;
        var addedContexts = new List<(int, MeshContext)>();

        foreach (var meshContext in meshContexts)
        {
            if (meshContext == null) continue;

            int insertIndex = _meshContextList.Count;
            _meshContextList.Add(meshContext);
            addedContexts.Add((insertIndex, meshContext));
        }

        if (addedContexts.Count == 0) return;

        // 最後に追加したものを選択
        _selectedIndex = _meshContextList.Count - 1;

        // Undo記録（1回でまとめて）
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            _undoController.RecordMeshContextsAdd(addedContexts, oldIndex, _selectedIndex);
        }

        InitVertexOffsets();
        LoadMeshContextToUndoController(_model.CurrentMeshContext);
        UpdateTopology();
        Repaint();
    }


    /// <summary>
    /// メッシュコンテキストを削除（Undo対応）
    /// </summary>
    private void RemoveMeshContextWithUndo(int index)
    {
        if (index < 0 || index >= _meshContextList.Count) return;

        var meshContext = _meshContextList[index];
        int oldIndex = _selectedIndex;

        // メッシュリソース解放
        if (meshContext.UnityMesh != null)
        {
            DestroyImmediate(meshContext.UnityMesh);
        }

        _meshContextList.RemoveAt(index);

        // インデックス調整
        if (_selectedIndex >= _meshContextList.Count)
            _selectedIndex = _meshContextList.Count - 1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        // Undo記録
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            var removedList = new List<(int Index, MeshContext meshContext)> { (index, meshContext) };
            _undoController.RecordMeshContextsRemove(removedList, oldIndex, _selectedIndex);
        }

        // 選択クリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        if (_model.HasValidSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        Repaint();
    }

    /// <summary>
    /// メッシュを選択（Undo対応）
    /// </summary>
    private void SelectMeshContentWithUndo(int index)
    {
        if (index < -1 || index >= _meshContextList.Count) return;
        if (index == _selectedIndex) return;

        int oldIndex = _selectedIndex;
        _selectedIndex = index;

        // Undo記録（選択変更のみ）
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            // MeshListChangeRecordで選択変更を記録
            var record = new MeshListChangeRecord
            {
                OldSelectedIndex = oldIndex,
                NewSelectedIndex = _selectedIndex
            };
            _undoController.MeshListStack.Record(record, "Select UnityMesh");
        }

        // 選択クリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        if (_model.HasValidSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        Repaint();
    }

    /// <summary>
    /// メッシュを複製（Undo対応）
    /// </summary>
    private void DuplicateMeshContentWithUndo(int index)
    {
        if (index < 0 || index >= _meshContextList.Count) return;

        var original = _meshContextList[index];

        // MeshContextを複製
        var clone = new MeshContext
        {
            Name = original.Name + " (Copy)",
            Data = original.Data?.Clone(),
            UnityMesh = original.Data?.ToUnityMesh(),
            OriginalPositions = original.OriginalPositions?.ToArray(),
            ExportSettings = new ExportSettings
            {
                UseLocalTransform = original.ExportSettings?.UseLocalTransform ?? false,
                Position = original.ExportSettings?.Position ?? Vector3.zero,
                Rotation = original.ExportSettings?.Rotation ?? Vector3.zero,
                Scale = original.ExportSettings?.Scale ?? Vector3.one
            },
            Materials = new List<Material>(original.Materials ?? new List<Material> { null }),
            CurrentMaterialIndex = original.CurrentMaterialIndex
        };

        int oldIndex = _selectedIndex;
        int insertIndex = index + 1;

        _meshContextList.Insert(insertIndex, clone);
        _selectedIndex = insertIndex;

        // Undo記録
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(clone, insertIndex, oldIndex, _selectedIndex);
        }

        InitVertexOffsets();
        LoadMeshContextToUndoController(_model.CurrentMeshContext);
        Repaint();
    }

    /// <summary>
    /// メッシュの順序を変更（Undo対応）
    /// </summary>
    private void ReorderMeshContentWithUndo(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _meshContextList.Count) return;
        if (toIndex < 0 || toIndex >= _meshContextList.Count) return;
        if (fromIndex == toIndex) return;

        int oldSelectedIndex = _selectedIndex;

        var meshContext = _meshContextList[fromIndex];
        _meshContextList.RemoveAt(fromIndex);
        _meshContextList.Insert(toIndex, meshContext);

        // 選択インデックス調整
        if (_selectedIndex == fromIndex)
        {
            _selectedIndex = toIndex;
        }
        else if (fromIndex < _selectedIndex && toIndex >= _selectedIndex)
        {
            _selectedIndex--;
        }
        else if (fromIndex > _selectedIndex && toIndex <= _selectedIndex)
        {
            _selectedIndex++;
        }

        // Undo記録
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
            _undoController.RecordMeshContextReorder(meshContext, fromIndex, toIndex, oldSelectedIndex, _selectedIndex);
        }

        Repaint();
    }
}
