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
        ctx.CreateNewMeshContext = OnMeshContextCreatedAsNew;
        ctx.AddMeshObjectToCurrentMesh = OnMeshObjectCreatedAddToCurrent;

        // MeshContext操作コールバック
        ctx.Model = _model;
        ctx.AddMeshContext = AddMeshContextWithUndo;
        ctx.AddMeshContexts = AddMeshContextsWithUndo;  // ★追加
        ctx.RemoveMeshContext = RemoveMeshContextWithUndo;
        ctx.SelectMeshContext = SelectMeshContentWithUndo;
        ctx.DuplicateMeshContent = DuplicateMeshContentWithUndo;
        ctx.ReorderMeshContext = ReorderMeshContentWithUndo;
        ctx.ClearAllMeshContexts = ClearAllMeshContextsWithUndo;  // ★ImportMode用
        ctx.ReplaceAllMeshContexts = ReplaceAllMeshContextsWithUndo;  // ★1回のUndoで戻せるReplace

        // Phase 5追加: マテリアル操作コールバック
        ctx.AddMaterials = AddMaterialsToModel;
        ctx.ReplaceMaterials = ReplaceMaterialsInModel;
        ctx.SetCurrentMaterialIndex = (index) => { if (_model != null) _model.CurrentMaterialIndex = index; };
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

        ctx.MeshObject = meshContext?.MeshObject;
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
        ctx.LastHoverHitResult = _lastHoverHitResult;
        ctx.HoverVertexRadius = HOVER_VERTEX_RADIUS;
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
        ctx.ClearAllMeshContexts = ClearAllMeshContextsWithUndo;
        ctx.ReplaceAllMeshContexts = ReplaceAllMeshContextsWithUndo;

        // Phase 4追加: メッシュ作成コールバック
        ctx.CreateNewMeshContext = OnMeshContextCreatedAsNew;
        ctx.AddMeshObjectToCurrentMesh = OnMeshObjectCreatedAddToCurrent;

        // Phase 5追加: マテリアル操作コールバック
        ctx.AddMaterials = AddMaterialsToModel;
        ctx.ReplaceMaterials = ReplaceMaterialsInModel;
        ctx.SetCurrentMaterialIndex = (index) => { if (_model != null) _model.CurrentMaterialIndex = index; };

        // UndoコンテキストにもMaterialsを同期
        if (_undoController?.MeshUndoContext != null && meshContext != null)
        {
            _undoController.MeshUndoContext.Materials = meshContext.Materials;
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
    // メッシュ作成コールバック（Phase 4追加）
    // ================================================================

    /// <summary>
    /// MeshObjectから新しいMeshContextを作成（PrimitiveMeshTool用ラッパー）
    /// </summary>
    private void OnMeshContextCreatedAsNew(MeshObject meshObject, string name)
    {
        // SimpleMeshFactory_MeshIO.csのCreateNewMeshContextを呼び出し
        CreateNewMeshContext(meshObject, name);
    }

    /// <summary>
    /// 現在選択中のメッシュにMeshObjectを追加（PrimitiveMeshTool用ラッパー）
    /// </summary>
    private void OnMeshObjectCreatedAddToCurrent(MeshObject meshObject, string name)
    {
        AddMeshObjectToCurrent(meshObject, name);
    }

    // ================================================================
    // マテリアル操作（Phase 5追加）
    // ================================================================

    /// <summary>
    /// マテリアルを追加（ModelContext.Materials に追加）
    /// </summary>
    private void AddMaterialsToModel(IList<Material> materials)
    {
        if (_model == null || materials == null) return;
        
        // ★重要: 直接List操作ではなく、新しいリストを作成してsetterで設定
        var currentMaterials = _model.Materials;
        var newList = new List<Material>();
        
        // 初期状態（null のみ）の場合は既存をスキップ
        bool skipExisting = (currentMaterials.Count == 1 && currentMaterials[0] == null);
        
        if (!skipExisting)
        {
            foreach (var mat in currentMaterials)
            {
                newList.Add(mat);
            }
        }
        
        foreach (var mat in materials)
        {
            newList.Add(mat);
        }
        
        _model.Materials = newList;  // setterを使用
        
        // 最後のレコードに NewMaterials を設定（Undo/Redo用）
        _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
    }

    /// <summary>
    /// マテリアルを置換（ModelContext.Materials を置換）
    /// </summary>
    private void ReplaceMaterialsInModel(IList<Material> materials)
    {
        if (_model == null) return;
        
        // ★重要: 直接List操作ではなく、新しいリストを作成してsetterで設定
        // （Materialsプロパティのgetterが新しいリストを返す可能性があるため）
        var newList = new List<Material>();
        if (materials != null)
        {
            foreach (var mat in materials)
            {
                newList.Add(mat);
            }
        }
        if (newList.Count == 0)
        {
            newList.Add(null);  // 少なくとも1つ
        }
        
        _model.Materials = newList;  // setterを使用
        
        // 最後のレコードに NewMaterials を設定（Undo/Redo用）
        _undoController?.UpdateLastRecordMaterials(_model.Materials, _model.CurrentMaterialIndex);
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

        // MaterialOwner を設定（Materials 委譲用）
        meshContext.MaterialOwner = _model;

        int oldIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        // カメラ状態を保存（追加前）
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        _meshContextList.Add(meshContext);
        _selectedIndex = insertIndex;
        
        InitVertexOffsets();

        // カメラ状態を保存（追加後）
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録 - カメラ状態付き
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldIndex, _selectedIndex, oldCamera, newCamera);
        }

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
        
        // マテリアル状態を保存（追加前）
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;

        // カメラ状態を保存（追加前）
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        foreach (var meshContext in meshContexts)
        {
            if (meshContext == null) continue;

            // MaterialOwner を設定（Materials 委譲用）
            meshContext.MaterialOwner = _model;

            int insertIndex = _meshContextList.Count;
            _meshContextList.Add(meshContext);
            addedContexts.Add((insertIndex, meshContext));
        }

        if (addedContexts.Count == 0) return;

        // 最後に追加したものを選択
        _selectedIndex = _meshContextList.Count - 1;
        
        InitVertexOffsets();

        // カメラ状態を保存（追加後）
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録（1回でまとめて）- カメラ状態付き、マテリアル状態付き
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextsAdd(addedContexts, oldIndex, _selectedIndex, oldCamera, newCamera, oldMaterials, oldMaterialIndex);
        }

        LoadMeshContextToUndoController(_model.CurrentMeshContext);
        UpdateTopology();
        Repaint();
    }

    /// <summary>
    /// 全メッシュコンテキストをクリア（Undo対応・Replaceインポート用）
    /// </summary>
    private void ClearAllMeshContextsWithUndo()
    {
        if (_meshContextList.Count == 0) return;

        int oldIndex = _selectedIndex;
        
        // マテリアル状態を保存（削除前）
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;

        // カメラ状態を保存（削除前）
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // 削除するメッシュのスナップショットを作成（Undo記録用）
        List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            removedSnapshots.Add((i, MeshContextSnapshot.Capture(_meshContextList[i])));
        }

        // 全メッシュリソースを解放
        foreach (var meshContext in _meshContextList)
        {
            if (meshContext.UnityMesh != null)
                DestroyImmediate(meshContext.UnityMesh);
        }

        // クリア
        _meshContextList.Clear();
        _selectedIndex = -1;

        _selectionState?.ClearAll();
        _selectedVertices.Clear();
        InitVertexOffsets();

        // カメラ状態を保存（削除後）
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録 - Materials 対応版
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                OldSelectedIndex = oldIndex,
                NewSelectedIndex = _selectedIndex,
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };
            _undoController.RecordMeshListChange(record, "Clear All Meshes", oldMaterials, oldMaterialIndex);
        }

        UpdateTopology();
        Repaint();
    }

    /// <summary>
    /// 全メッシュコンテキストを置換（Undo対応・1回のUndoで戻せる）
    /// </summary>
    private void ReplaceAllMeshContextsWithUndo(IList<MeshContext> newMeshContexts)
    {
        if (newMeshContexts == null || newMeshContexts.Count == 0)
        {
            // 新しいメッシュがない場合はクリアのみ
            ClearAllMeshContextsWithUndo();
            return;
        }

        int oldSelectedIndex = _selectedIndex;
        
        // マテリアル状態を保存（置換前）
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;

        // カメラ状態を保存（置換前）
        CameraSnapshot oldCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // 既存メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            removedSnapshots.Add((i, snapshot));
        }

        // 既存メッシュリソースを解放
        foreach (var meshContext in _meshContextList)
        {
            if (meshContext.UnityMesh != null)
                DestroyImmediate(meshContext.UnityMesh);
        }
        _meshContextList.Clear();

        // 新しいメッシュを追加
        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        foreach (var meshContext in newMeshContexts)
        {
            if (meshContext == null) continue;

            // MaterialOwner を設定（Materials 委譲用）
            meshContext.MaterialOwner = _model;

            int insertIndex = _meshContextList.Count;
            _meshContextList.Add(meshContext);

            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(meshContext);
            addedSnapshots.Add((insertIndex, snapshot));
        }

        // 選択を更新
        _selectedIndex = _meshContextList.Count > 0 ? 0 : -1;
        _selectionState?.ClearAll();
        _selectedVertices.Clear();

        InitVertexOffsets();

        // カメラ状態を保存（置換後）
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録（1回のレコードで削除と追加を両方記録）- Materials 対応版
        if (_undoController != null)
        {
            MeshListChangeRecord record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndex = oldSelectedIndex,
                NewSelectedIndex = _selectedIndex,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.RecordMeshListChange(record, $"Replace All: {newMeshContexts.Count} meshes", oldMaterials, oldMaterialIndex);
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
        }


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

        MeshContext meshContext = _meshContextList[index];
        int oldIndex = _selectedIndex;

        // カメラ状態を保存（削除前）
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

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

        // 選択クリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();

        if (_model.HasValidMeshContextSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.CurrentMeshContext);
        }

        // カメラ状態を保存（削除後）
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録 - カメラ状態付き
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            List<(int Index, MeshContext meshContext)> removedList = new List<(int Index, MeshContext meshContext)> { (index, meshContext) };
            _undoController.RecordMeshContextsRemove(removedList, oldIndex, _selectedIndex, oldCamera, newCamera);
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

        // ★Phase 5: 現在の選択を保存（切り替え前）
        SaveSelectionToCurrentMesh();

        int oldIndex = _selectedIndex;
        _selectedIndex = index;

        // Undo記録（選択変更のみ）
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            // MeshListChangeRecordで選択変更を記録
            MeshListChangeRecord record = new MeshListChangeRecord
            {
                OldSelectedIndex = oldIndex,
                NewSelectedIndex = _selectedIndex
            };
            _undoController.MeshListStack.Record(record, "Select UnityMesh");
        }
        // ★Phase 5: 選択を復元（切り替え後）
        // 旧: _selectedVertices.Clear(); _selectionState?.ClearAll();
        LoadSelectionFromCurrentMesh();
        // 選択クリア
       // _selectedVertices.Clear();
       // _selectionState?.ClearAll();

        if (_model.HasValidMeshContextSelection)
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

        MeshContext original = _meshContextList[index];

        // MeshContextを複製
        var clone = new MeshContext
        {
            Name = original.Name + " (Copy)",
            MeshObject = original.MeshObject?.Clone(),
            UnityMesh = original.MeshObject?.ToUnityMesh(),
            OriginalPositions = original.OriginalPositions?.ToArray(),
            BoneTransform = new BoneTransform
            {
                UseLocalTransform = original.BoneTransform?.UseLocalTransform ?? false,
                Position = original.BoneTransform?.Position ?? Vector3.zero,
                Rotation = original.BoneTransform?.Rotation ?? Vector3.zero,
                Scale = original.BoneTransform?.Scale ?? Vector3.one
            },
            Materials = new List<Material>(original.Materials ?? new List<Material> { null }),
            CurrentMaterialIndex = original.CurrentMaterialIndex,
            MaterialOwner = _model  // Materials 委譲用
        };

        int oldIndex = _selectedIndex;
        int insertIndex = index + 1;

        // カメラ状態を保存（追加前）
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        _meshContextList.Insert(insertIndex, clone);
        _selectedIndex = insertIndex;

        InitVertexOffsets();

        // カメラ状態を保存（追加後）
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録 - カメラ状態付き
        if (_undoController != null)
        {
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(clone, insertIndex, oldIndex, _selectedIndex, oldCamera, newCamera);
        }

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
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextReorder(meshContext, fromIndex, toIndex, oldSelectedIndex, _selectedIndex);
        }

        Repaint();
    }
}
