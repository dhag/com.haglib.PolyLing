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
using Poly_Ling.Transforms;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Commands;
using Poly_Ling.Model;
using MeshEditor;


public partial class PolyLing : EditorWindow
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

        // モデル操作コールバック（NewModelモード対応）
        ctx.Project = _project;
        ctx.CreateNewModel = CreateNewModelWithUndo;
        ctx.SelectModel = (index) =>
        {
            _commandQueue?.Enqueue(new SelectModelCommand(index, SelectModelWithUndo));
        };

        // MeshContext操作コールバック（すべてCommandQueue経由）
        ctx.Model = _model;
        ctx.AddMeshContext = (meshContext) =>
        {
            _commandQueue?.Enqueue(new AddMeshContextCommand(meshContext, AddMeshContextWithUndo));
        };
        ctx.AddMeshContexts = (meshContexts) =>
        {
            _commandQueue?.Enqueue(new AddMeshContextsCommand(meshContexts, AddMeshContextsWithUndo));
        };
        ctx.RemoveMeshContext = (index) =>
        {
            _commandQueue?.Enqueue(new RemoveMeshContextCommand(index, RemoveMeshContextWithUndo));
        };
        ctx.SelectMeshContext = (index) =>
        {
            _commandQueue?.Enqueue(new SelectMeshContextCommand(index, SelectMeshContentWithUndo));
        };
        ctx.DuplicateMeshContent = (index) =>
        {
            _commandQueue?.Enqueue(new DuplicateMeshContentCommand(index, DuplicateMeshContentWithUndo));
        };
        ctx.ReorderMeshContext = (fromIndex, toIndex) =>
        {
            _commandQueue?.Enqueue(new ReorderMeshContextCommand(fromIndex, toIndex, ReorderMeshContentWithUndo));
        };
        ctx.UpdateMeshAttributes = (changes) =>
        {
            _commandQueue?.Enqueue(new UpdateMeshAttributesCommand(changes, UpdateMeshAttributesWithUndo));
        };
        ctx.ClearAllMeshContexts = () =>
        {
            _commandQueue?.Enqueue(new ClearAllMeshContextsCommand(ClearAllMeshContextsWithUndo));
        };
        ctx.ReplaceAllMeshContexts = (meshContexts) =>
        {
            _commandQueue?.Enqueue(new ReplaceAllMeshContextsCommand(meshContexts, ReplaceAllMeshContextsWithUndo));
        };

        // Phase 5追加: マテリアル操作コールバック
        ctx.AddMaterials = AddMaterialsToModel;
        ctx.AddMaterialReferences = AddMaterialRefsToModel;
        ctx.ReplaceMaterials = ReplaceMaterialsInModel;
        ctx.ReplaceMaterialReferences = ReplaceMaterialRefsInModel;
        ctx.SetCurrentMaterialIndex = (index) => { if (_model != null) _model.CurrentMaterialIndex = index; };
        
        // v2.1: 複数メッシュ選択変更時のコールバック（GPUバッファ同期）
        ctx.OnMeshSelectionChanged = OnMeshSelectionChanged;
    }

    /// <summary>
    /// v2.1: メッシュ選択変更時のコールバック（複数選択対応）
    /// MeshListPanelなどからの選択変更で呼ばれる
    /// </summary>
    private void OnMeshSelectionChanged()
    {
        if (_model == null) return;

        // 旧メッシュの選択イベント解除
        SaveSelectionToCurrentMesh();

        // 新メッシュの_selectionState参照差替え
        LoadSelectionFromCurrentMesh();

        // 頂点編集対象の初期化
        if (_model.HasValidMeshContextSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.FirstSelectedMeshContext);
            UpdateTopology();
        }

        // 選択変更をワンショットパイプラインに通知
        _unifiedAdapter?.RequestNormal();

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

        // 注意: クローンを作成して独立したインスタンスにする
        // 参照代入するとRecord作成後に_selectionState.Verticesが変更された時に
        // Recordの中のデータも変わってしまう
        ctx.SelectedVertices = new HashSet<int>(_selectionState.Vertices);
        ctx.VertexOffsets = _vertexOffsets;
        ctx.GroupOffsets = _groupOffsets;
        ctx.UndoController = _undoController;
        ctx.WorkPlane = _undoController?.WorkPlane;
        ctx.SyncMesh = () => {
            // 全選択メッシュを同期
            if (_model != null)
            {
                foreach (var mc in _model.SelectedMeshContexts)
                    SyncMeshFromData(mc);
            }
        };
        // v2.1: 複数メッシュ対応 - 選択中の全メッシュの位置を同期
        ctx.SyncMeshPositionsOnly = () => SyncAllSelectedMeshPositions();
        // モーフプレビュー用: 任意のMeshContextの頂点位置を同期
        ctx.SyncMeshContextPositionsOnly = (mc) => SyncMeshPositionsOnly(mc);
        
        // GPUバッファのトポロジ再構築コールバック
        // SyncMeshは位置更新のみで軽量、これはトポロジ変更時のみ呼ぶ（重い）
        ctx.NotifyTopologyChanged = () => _unifiedAdapter?.NotifyTopologyChanged();

        // 更新モード制御コールバック
        ctx.EnterTransformDragging = () => _unifiedAdapter?.EnterTransformDragging();
        ctx.ExitTransformDragging = () => _unifiedAdapter?.ExitTransformDragging();

        // マルチマテリアル対応
        ctx.CurrentMaterialIndex = meshContext?.CurrentMaterialIndex ?? 0;
        ctx.Materials = meshContext?.Materials;

        // 選択システム
        ctx.SelectionState = _selectionState;
        ctx.TopologyCache = _meshTopology;
        ctx.SelectionOps = _selectionOps;

        // ModelContext（Phase 3追加）- すべてCommandQueue経由
        ctx.Model = _model;
        ctx.AddMeshContexts = (meshContexts) =>
        {
            _commandQueue?.Enqueue(new AddMeshContextsCommand(meshContexts, AddMeshContextsWithUndo));
        };
        ctx.AddMeshContext = (meshContext) =>
        {
            _commandQueue?.Enqueue(new AddMeshContextCommand(meshContext, AddMeshContextWithUndo));
        };
        ctx.RemoveMeshContext = (index) =>
        {
            _commandQueue?.Enqueue(new RemoveMeshContextCommand(index, RemoveMeshContextWithUndo));
        };
        ctx.SelectMeshContext = (index) =>
        {
            _commandQueue?.Enqueue(new SelectMeshContextCommand(index, SelectMeshContentWithUndo));
        };
        ctx.DuplicateMeshContent = (index) =>
        {
            _commandQueue?.Enqueue(new DuplicateMeshContentCommand(index, DuplicateMeshContentWithUndo));
        };
        ctx.ReorderMeshContext = (fromIndex, toIndex) =>
        {
            _commandQueue?.Enqueue(new ReorderMeshContextCommand(fromIndex, toIndex, ReorderMeshContentWithUndo));
        };
        ctx.UpdateMeshAttributes = (changes) =>
        {
            _commandQueue?.Enqueue(new UpdateMeshAttributesCommand(changes, UpdateMeshAttributesWithUndo));
        };
        ctx.ClearAllMeshContexts = () =>
        {
            _commandQueue?.Enqueue(new ClearAllMeshContextsCommand(ClearAllMeshContextsWithUndo));
        };
        ctx.ReplaceAllMeshContexts = (meshContexts) =>
        {
            _commandQueue?.Enqueue(new ReplaceAllMeshContextsCommand(meshContexts, ReplaceAllMeshContextsWithUndo));
        };

        // Phase 4追加: メッシュ作成コールバック
        ctx.CreateNewMeshContext = OnMeshContextCreatedAsNew;
        ctx.AddMeshObjectToCurrentMesh = OnMeshObjectCreatedAddToCurrent;

        // Phase 5追加: マテリアル操作コールバック
        ctx.AddMaterials = AddMaterialsToModel;
        ctx.AddMaterialReferences = AddMaterialRefsToModel;
        ctx.ReplaceMaterials = ReplaceMaterialsInModel;
        ctx.ReplaceMaterialReferences = ReplaceMaterialRefsInModel;
        ctx.SetCurrentMaterialIndex = (index) => { if (_model != null) _model.CurrentMaterialIndex = index; };

        // UndoコンテキストにもMaterialsを同期
        // 注意: MaterialOwnerが設定されている場合、MeshUndoContext.MaterialsはModelContext.Materialsを参照するため
        // ここでsetterを呼ぶとSourceTexturePathが失われる。MaterialIndexのみ同期する。
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
    /// マテリアル参照を追加（ソースパス情報付き）
    /// </summary>
    private void AddMaterialRefsToModel(IList<Poly_Ling.Materials.MaterialReference> materialRefs)
    {
        if (_model == null || materialRefs == null) return;
        
        var currentRefs = _model.MaterialReferences;
        var newList = new List<Poly_Ling.Materials.MaterialReference>();
        
        // 初期状態（空または1つのみで未設定）の場合は既存をスキップ
        bool skipExisting = (currentRefs.Count == 1 && currentRefs[0]?.Data?.Name == "New Material");
        
        if (!skipExisting)
        {
            foreach (var matRef in currentRefs)
            {
                newList.Add(matRef);
            }
        }
        
        foreach (var matRef in materialRefs)
        {
            newList.Add(matRef);
        }
        
        _model.MaterialReferences = newList;
        
        // Undo記録
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

    /// <summary>
    /// マテリアル参照を置換（ソースパス情報付き）
    /// </summary>
    private void ReplaceMaterialRefsInModel(IList<Poly_Ling.Materials.MaterialReference> materialRefs)
    {
        if (_model == null) return;
        
        var newList = new List<Poly_Ling.Materials.MaterialReference>();
        if (materialRefs != null)
        {
            foreach (var matRef in materialRefs)
            {
                newList.Add(matRef);
            }
        }
        if (newList.Count == 0)
        {
            newList.Add(new Poly_Ling.Materials.MaterialReference());
        }
        
        _model.MaterialReferences = newList;
        
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

        // ParentModelContext を設定（Materials 委譲用）
        meshContext.ParentModelContext = _model;

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // ModelContext API経由でリスト操作（MorphSet調整・TypedIndices無効化を含む）
        int insertIndex = _model.Add(meshContext);

        // 選択を新しいメッシュに設定
        SetSelectedIndex(insertIndex);
        
        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録
        if (_undoController != null)
        {
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndices, newSelectedIndices, oldCamera, newCamera);
        }
        
        // 他のパネルに通知（MeshUndoContext設定・選択復元・GPU更新を一元処理）
        _model?.OnListChanged?.Invoke();
    }
    /// <summary>
    /// メッシュコンテキストを複数追加（Undo対応・バッチ）
    /// </summary>
    private void AddMeshContextsWithUndo(IList<MeshContext> meshContexts)
    {
        if (meshContexts == null || meshContexts.Count == 0) return;

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        var addedContexts = new List<(int, MeshContext)>();
        foreach (var meshContext in meshContexts)
        {
            if (meshContext == null) continue;

            // MaterialOwner を設定（Materials 委譲用）
            meshContext.ParentModelContext = _model;

            // ModelContext API経由でリスト操作
            int insertIndex = _model.Add(meshContext);
            addedContexts.Add((insertIndex, meshContext));
        }

        if (addedContexts.Count == 0) return;

        // 最後に追加したものを選択
        SetSelectedIndex(_meshContextList.Count - 1);
        
        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録
        if (_undoController != null)
        {
            _undoController.RecordMeshContextsAdd(addedContexts, oldSelectedIndices, newSelectedIndices, oldCamera, newCamera, oldMaterials, oldMaterialIndex);
        }
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
    }

    // ================================================================
    // モデル操作（NewModelモード対応）
    // ================================================================
    // 【現在: 方針B（全状態保存方式）】
    //   操作前後でProjectSnapshot.CaptureFromProjectContext()を呼び出し、
    //   ProjectRecord として記録する
    //
    // 【方針A移行時の変更】
    //   CreateNewModelWithUndo → CreateModelRecord を使用
    //   SelectModelWithUndo → SelectModelRecord を使用
    //   これにより、モデル作成時は作成されたモデルのみ、
    //   モデル選択時はインデックス変更のみを記録し、メモリ効率が向上
    //
    // 詳細は Core/Records/ProjectUndoRecords.cs を参照
    // ================================================================

    /// <summary>
    /// 新規モデルを作成してカレントに設定（Undo対応）
    /// </summary>
    /// <param name="name">モデル名（nullの場合は自動生成）</param>
    /// <returns>作成されたModelContext</returns>
    /// <remarks>
    /// 方針A移行時: CreateModelRecord を使用し、作成されたモデルのスナップショットのみ保存
    /// </remarks>
    private ModelContext CreateNewModelWithUndo(string name)
    {
        // ★ 操作前のスナップショットを取得
        var beforeSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project);
        
        // 新規モデルを作成（ProjectContextが処理）
        var newModel = _project.CreateNewModel(name);
        
        //Debug.Log($"[PolyLing] Created new model: {newModel.Name} (index: {_project.CurrentModelIndex})");
        
        // ToolContextのModel参照を更新
        _toolManager.toolContext.Model = newModel;
        
        // ★ 操作後のスナップショットを取得
        var afterSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project);
        
        // ★ Undo記録
        if (_undoController != null && beforeSnapshot != null && afterSnapshot != null)
        {
            var record = ProjectRecord.CreateNewModel(beforeSnapshot, afterSnapshot);
            _undoController.RecordProjectOperation(record);
            //Debug.Log($"[PolyLing] Recorded NewModel undo: {_undoController.GetProjectStackDebugInfo()}");
        }
        
        // バッファを再構築（OnCurrentModelChangedで処理される）
        // ProjectContext.CreateNewModelがCurrentModelIndexを設定するとコールバックが呼ばれる
        
        return newModel;
    }

    /// <summary>
    /// モデルを選択（カレントに設定・Undo対応）
    /// </summary>
    /// <param name="index">モデルインデックス</param>
    /// <remarks>
    /// 方針A移行時: SelectModelRecord を使用し、oldIndex/newIndex のみ保存
    /// （全モデルのスナップショットは不要になる）
    /// </remarks>
    private void SelectModelWithUndo(int index)
    {
        if (_project == null) return;
        
        int oldIndex = _project.CurrentModelIndex;
        if (oldIndex == index) return;
        
        // ★ 操作前のスナップショットを取得
        var beforeSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project);
        
        Debug.Log($"[PolyLing] Selecting model: {index} (from {oldIndex})");
        
        // モデルを選択（ProjectContextが処理）
        if (_project.SelectModel(index))
        {
            // ToolContextのModel参照を更新
            _toolManager.toolContext.Model = _project.CurrentModel;
            
            // ★ 操作後のスナップショットを取得
            var afterSnapshot = ProjectSnapshot.CaptureFromProjectContext(_project);
            
            // ★ Undo記録
            if (_undoController != null && beforeSnapshot != null && afterSnapshot != null)
            {
                var record = ProjectRecord.CreateSelectModel(beforeSnapshot, afterSnapshot);
                _undoController.RecordProjectOperation(record);
            }
            
            // バッファ再構築はOnCurrentModelChangedで処理される
        }
    }

    /// <summary>
    /// 全メッシュコンテキストをクリア（Undo対応・Replaceインポート用）
    /// </summary>
    private void ClearAllMeshContextsWithUndo()
    {
        if (_meshContextList.Count == 0) return;

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;
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

        // 全メッシュリソースを解放してModelContext API経由で削除（逆順）
        for (int i = _meshContextList.Count - 1; i >= 0; i--)
        {
            if (_meshContextList[i].UnityMesh != null)
                DestroyImmediate(_meshContextList[i].UnityMesh);
            _model.RemoveAt(i);
        }

        // 選択クリア
        SetSelectedIndex(-1);
        _selectionState?.ClearAll();
        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録
        if (_undoController != null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                OldSelectedIndices = oldSelectedIndices,
                NewSelectedIndices = newSelectedIndices,
                OldCameraState = oldCamera,
                NewCameraState = newCamera
            };
            _undoController.RecordMeshListChange(record, "Clear All Meshes", oldMaterials, oldMaterialIndex);
        }

        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
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

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        var oldMaterials = _model?.Materials != null ? new List<Material>(_model.Materials) : null;
        var oldMaterialIndex = _model?.CurrentMaterialIndex ?? 0;
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

        // 既存メッシュリソースを解放してModelContext API経由で削除（逆順）
        for (int i = _meshContextList.Count - 1; i >= 0; i--)
        {
            if (_meshContextList[i].UnityMesh != null)
                DestroyImmediate(_meshContextList[i].UnityMesh);
            _model.RemoveAt(i);
        }

        // 新しいメッシュをModelContext API経由で追加
        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        foreach (var meshContext in newMeshContexts)
        {
            if (meshContext == null) continue;
            meshContext.ParentModelContext = _model;
            int insertIndex = _model.Add(meshContext);
            addedSnapshots.Add((insertIndex, MeshContextSnapshot.Capture(meshContext)));
        }

        // 選択を更新
        SetSelectedIndex(_meshContextList.Count > 0 ? 0 : -1);
        _selectionState?.ClearAll();
        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録（1回のレコードで削除と追加を両方記録）
        if (_undoController != null)
        {
            MeshListChangeRecord record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndices = oldSelectedIndices,
                NewSelectedIndices = newSelectedIndices,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.RecordMeshListChange(record, $"Replace All: {newMeshContexts.Count} meshes", oldMaterials, oldMaterialIndex);
        }
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
    }


    /// <summary>
    /// メッシュコンテキストを削除（Undo対応）
    /// </summary>
    private void RemoveMeshContextWithUndo(int index)
    {
        if (index < 0 || index >= _meshContextList.Count) return;

        MeshContext meshContext = _meshContextList[index];

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
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

        // ModelContext API経由で削除（MorphSet調整・選択インデックス調整を含む）
        _model.RemoveAt(index);

        // 選択調整（RemoveAtが全カテゴリ調整済みだが、Mesh選択が空になった場合のフォールバック）
        if (_selectedIndex < 0 && _meshContextList.Count > 0)
        {
            SetSelectedIndex(Mathf.Min(index, _meshContextList.Count - 1));
        }

        // 選択クリア
        _selectionState?.ClearAll();

        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録
        if (_undoController != null)
        {
            List<(int Index, MeshContext meshContext)> removedList = new List<(int Index, MeshContext meshContext)> { (index, meshContext) };
            _undoController.RecordMeshContextsRemove(removedList, oldSelectedIndices, newSelectedIndices, oldCamera, newCamera);
        }
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
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

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        SetSelectedIndex(index);
        var newSelectedIndices = _model.CaptureAllSelectedIndices();

        // Undo記録（選択変更のみ）
        if (_undoController != null)
        {
            _undoController.RecordMeshSelectionChange(oldSelectedIndices, newSelectedIndices);
        }
        // ★Phase 5: 選択を復元（切り替え後）
        LoadSelectionFromCurrentMesh();

        if (_model.HasValidMeshContextSelection)
        {
            InitVertexOffsets();
            LoadMeshContextToUndoController(_model.FirstSelectedMeshContext);
            UpdateTopology();
        }
        
        _unifiedAdapter?.RequestNormal();
        Repaint();
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
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
            ParentModelContext = _model
        };

        int insertIndex = index + 1;

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot oldCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // ModelContext API経由で挿入（MorphSet調整・選択インデックス調整を含む）
        _model.Insert(insertIndex, clone);

        // 選択を複製先に設定
        SetSelectedIndex(insertIndex);
        InitVertexOffsets();

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();
        CameraSnapshot newCamera = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録
        if (_undoController != null)
        {
            _undoController.RecordMeshContextAdd(clone, insertIndex, oldSelectedIndices, newSelectedIndices, oldCamera, newCamera);
        }
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
    }

    /// <summary>
    /// メッシュの順序を変更（Undo対応）
    /// </summary>
    private void ReorderMeshContentWithUndo(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _meshContextList.Count) return;
        if (toIndex < 0 || toIndex >= _meshContextList.Count) return;
        if (fromIndex == toIndex) return;

        // 変更前の状態を保存
        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        var meshContext = _meshContextList[fromIndex];

        // ModelContext API経由で移動（選択インデックス調整を含む）
        _model.Move(fromIndex, toIndex);

        // 変更後の状態を保存
        var newSelectedIndices = _model.CaptureAllSelectedIndices();

        // Undo記録
        if (_undoController != null)
        {
            _undoController.RecordMeshContextReorder(meshContext, fromIndex, toIndex, oldSelectedIndices, newSelectedIndices);
        }
        
        // 他のパネルに通知
        _model?.OnListChanged?.Invoke();
    }

    /// <summary>
    /// メッシュ属性を変更（Undo対応）
    /// </summary>
    private void UpdateMeshAttributesWithUndo(IList<MeshAttributeChange> changes)
    {
        if (changes == null || changes.Count == 0) return;

        // 変更前の値を保存（Undo用）
        var oldValues = new List<MeshAttributeChange>();
        
        foreach (var change in changes)
        {
            if (change.Index < 0 || change.Index >= _meshContextList.Count) continue;
            
            var ctx = _meshContextList[change.Index];
            var oldValue = new MeshAttributeChange { Index = change.Index };
            
            // 変更前の値を記録
            if (change.IsVisible.HasValue) oldValue.IsVisible = ctx.IsVisible;
            if (change.IsLocked.HasValue) oldValue.IsLocked = ctx.IsLocked;
            if (change.MirrorType.HasValue) oldValue.MirrorType = ctx.MirrorType;
            if (change.Name != null) oldValue.Name = ctx.Name;
            
            oldValues.Add(oldValue);
            
            // 値を変更
            if (change.IsVisible.HasValue) ctx.IsVisible = change.IsVisible.Value;
            if (change.IsLocked.HasValue) ctx.IsLocked = change.IsLocked.Value;
            if (change.MirrorType.HasValue) ctx.MirrorType = change.MirrorType.Value;
            if (change.Name != null) ctx.Name = change.Name;
        }

        // Undo記録
        if (_undoController != null && oldValues.Count > 0)
        {
            var record = new MeshAttributesBatchChangeRecord(oldValues, changes.ToList());
            _undoController.MeshListStack.Record(record, "属性変更");
        }

        _model.IsDirty = true;
        _model?.OnListChanged?.Invoke();
        Repaint();
    }
}
