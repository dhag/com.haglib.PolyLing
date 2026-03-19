// PolyLingCore.cs
// PolyLingの中核ロジッククラス（IPolyLingCore実装）
// EditorWindow依存ゼロのplain C#クラス
// ライフサイクル: Initialize() → Tick()（毎フレーム）→ Dispose()

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.View;
using Poly_Ling.Materials;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore : IPolyLingCore, IDisposable
    {
        // ================================================================
        // フィールド
        // ================================================================

        private ProjectContext     _project;
        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;
        private SelectionState     _selectionState;
        private TopologyCache      _meshTopology;
        private SelectionOperations _selectionOps;
        private ToolManager        _toolManager;
        private PanelContext       _panelContext;
        private MeshListOps        _meshListOps;
        private LiveProjectView    _liveProjectView;
        private PolyLingCoreConfig _config;

        private bool _disposed;

        // ================================================================
        // IPolyLingCore プロパティ
        // ================================================================

        public ProjectContext  Project           => _project;
        public ModelContext    Model             => _project?.CurrentModel;
        public ToolContext     CurrentToolContext => _toolManager?.toolContext;
        public PanelContext    PanelContext       => _panelContext;
        public SelectionState  SelectionState    => _selectionState;
        public LiveProjectView LiveProjectView   => _liveProjectView;

        // IPolyLingCore 公開（EditorレイヤーがEditor固有コールバックを接続するために必要）
        public MeshUndoController  UndoController => _undoController;
        public CommandQueue         CommandQueue   => _commandQueue;
        public SelectionOperations  SelectionOps   => _selectionOps;
        public TopologyCache        MeshTopology   => _meshTopology;

        public ToolManager ToolManager => _toolManager;

        private ModelContext       _model             => Model;
        private List<MeshContext>  _meshContextList   => _model?.MeshContextList;
        private ToolContext        _toolContext        => CurrentToolContext;

        // ================================================================
        // IPolyLingCore イベント
        // ================================================================

        public event Action OnRepaintRequired;
        public event Action OnMeshListChanged;
        public event Action OnCurrentModelChanged;

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// Coreを初期化する。OnEnable相当。
        /// existingProjectが非nullの場合はそれを使用（Editor再有効化時の既存データ維持）
        /// </summary>
        public void Initialize(PolyLingCoreConfig config, ProjectContext existingProject = null)
        {
            _config = config ?? PolyLingCoreConfig.CreateStub();

            // ProjectContext（既存がある場合は引き継ぐ）
            _project = existingProject ?? _project ?? new ProjectContext();
            if (_project.ModelCount == 0)
                _project.AddModel(new ModelContext("Model"));

            // CommandQueue / UndoController
            _commandQueue   = new CommandQueue();
            _undoController = new MeshUndoController("PolyLing");
            _undoController.SetCommandQueue(_commandQueue);
            _undoController.OnUndoRedoPerformed += () =>
            {
                OnUndoRedoPerformed_Ext?.Invoke();
                OnRepaintRequired?.Invoke();
            };
            _undoController.OnProjectUndoRedoPerformed += (record, isUndo) =>
            {
                OnRepaintRequired?.Invoke();
            };

            // ProjectContext コールバック
            _project.OnCurrentModelChanged += OnCurrentModelChangedHandler;
            _project.OnModelsChanged       += OnModelsChanged;

            // ModelContext コールバック
            _undoController.SetModelContext(_model);
            _model.OnListChanged += OnMeshListChangedInternal;
            _model.OnCameraRestoreRequested    = null; // Editorが設定する
            _model.OnFocusMeshListRequested    = () => _undoController.FocusMeshList();
            _model.OnReorderCompleted          = OnModelReorderCompleted;
            _model.OnVertexEditStackClearRequested = () => _undoController?.VertexEditStack?.Clear();
            _model.WorkPlane = _undoController.WorkPlane;

            // MeshUndoContext
            if (_undoController.MeshUndoContext != null && _model != null)
            {
                _undoController.MeshUndoContext.ParentModelContext = _model;
            }
            if (_meshContextList != null)
            {
                foreach (var mc in _meshContextList)
                    if (mc != null) mc.ParentModelContext = _model;
            }

            // SelectionSystem
            InitializeSelectionSystem();

            // ToolManager
            InitializeTools();

            // PanelContext
            InitPanelContext();
        }

        private void InitializeSelectionSystem()
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            _selectionState = meshContext?.Selection ?? new SelectionState();
            _meshTopology   = new TopologyCache();
            _selectionOps   = new SelectionOperations(_selectionState, _meshTopology);
            _selectionOps.EdgeHitDistance = 18f;

            _selectionState.OnSelectionChanged += OnSelectionChanged;
            UpdateTopology();
        }

        private void InitializeTools()
        {
            _toolManager = new ToolManager();
            ToolRegistry.RegisterAllTools(_toolManager);
            _toolManager.OnToolChanged += OnToolChanged;
            SetupToolContext();
        }

        private void InitPanelContext()
        {
            _panelContext    = new PanelContext(DispatchPanelCommand);
            _meshListOps     = new MeshListOps(_model, _undoController);
            _liveProjectView = new LiveProjectView(_project);
            SetupMeshListOpsCallbacks();
            ToolContextReconnector.ReconnectAllPanelContexts(_panelContext);

            // RemoteServerの接続はEditorBridge経由でEditor依存を排除する
            PLEditorBridge.I.SetupRemoteServer(DispatchPanelCommand);
        }

        // ================================================================
        // SetupToolContext（コールバック配線）
        // ================================================================

        private void SetupToolContext()
        {
            var ctx = _toolManager.toolContext;

            // ビューポートコールバック（Configから注入）
            ctx.WorldToScreenPos            = _config.WorldToScreenPos;
            ctx.ScreenDeltaToWorldDelta     = _config.ScreenDeltaToWorldDelta;
            ctx.FindVertexAtScreenPos       = _config.FindVertexAtScreenPos;
            ctx.ScreenPosToRay              = _config.ScreenPosToRay;
            ctx.Repaint                     = () => OnRepaintRequired?.Invoke();
            ctx.SyncMesh                    = _config.SyncMesh;
            ctx.SyncMeshPositionsOnly       = _config.SyncMeshPositionsOnly;
            ctx.SyncMeshContextPositionsOnly = _config.SyncMeshContextPositionsOnly;
            ctx.SyncBoneTransforms          = _config.SyncBoneTransforms;

            // 選択・トポロジー
            ctx.SelectionState  = _selectionState;
            ctx.TopologyCache   = _meshTopology;
            ctx.SelectionOps    = _selectionOps;
            ctx.RecordSelectionChange = RecordSelectionChange;
            ctx.NotifyTopologyChanged = UpdateTopology;

            // Undo
            ctx.UndoController  = _undoController;
            ctx.CommandQueue    = _commandQueue;

            // WorkPlane
            ctx.WorkPlane = _undoController?.WorkPlane;

            // モデル・プロジェクト
            ctx.Model   = _model;
            ctx.Project = _project;

            // プロジェクト操作
            ctx.CreateNewModel = CreateNewModel;
            ctx.SelectModel    = index =>
                _commandQueue?.Enqueue(new SelectModelCommand(index, SelectModelWithUndo));

            // MeshContext操作（CommandQueue経由）
            ctx.AddMeshContext = mc =>
                _commandQueue?.Enqueue(new AddMeshContextCommand(mc, AddMeshContextWithUndo));
            ctx.AddMeshContexts = mcs =>
                _commandQueue?.Enqueue(new AddMeshContextsCommand(mcs, AddMeshContextsWithUndo));
            ctx.RemoveMeshContext = idx =>
                _commandQueue?.Enqueue(new RemoveMeshContextCommand(idx, RemoveMeshContextWithUndo));
            ctx.SelectMeshContext = idx =>
                _commandQueue?.Enqueue(new SelectMeshContextCommand(idx, SelectMeshContentWithUndo));
            ctx.DuplicateMeshContent = idx =>
                _commandQueue?.Enqueue(new DuplicateMeshContentCommand(idx, DuplicateMeshContentWithUndo));
            ctx.ReorderMeshContext = (from, to) =>
                _commandQueue?.Enqueue(new ReorderMeshContextCommand(from, to, ReorderMeshContentWithUndo));
            ctx.UpdateMeshAttributes = changes =>
                _commandQueue?.Enqueue(new UpdateMeshAttributesCommand(changes, UpdateMeshAttributesWithUndo));
            ctx.ClearAllMeshContexts = () =>
                _commandQueue?.Enqueue(new ClearAllMeshContextsCommand(ClearAllMeshContextsWithUndo));
            ctx.ReplaceAllMeshContexts = mcs =>
                _commandQueue?.Enqueue(new ReplaceAllMeshContextsCommand(mcs, ReplaceAllMeshContextsWithUndo));

            // マテリアル
            ctx.AddMaterials             = AddMaterialsToModel;
            ctx.AddMaterialReferences    = AddMaterialRefsToModel;
            ctx.ReplaceMaterials         = ReplaceMaterialsInModel;
            ctx.ReplaceMaterialReferences = ReplaceMaterialRefsInModel;
            ctx.SetCurrentMaterialIndex  = idx => { if (_model != null) _model.CurrentMaterialIndex = idx; };

            // メッシュ作成
            ctx.CreateNewMeshContext       = OnMeshContextCreatedAsNew;
            ctx.AddMeshObjectToCurrentMesh = OnMeshObjectCreatedAddToCurrent;

            // 選択変更通知
            ctx.OnMeshSelectionChanged = OnMeshSelectionChangedInternal;

            // カメラフォーカス
            ctx.FocusCameraOn = pos =>
            {
                OnFocusCameraRequested?.Invoke(pos);
                OnRepaintRequired?.Invoke();
            };

            // ToolContextReconnector に最新ContextをBroadcast
            ToolContextReconnector.ReconnectAll(ctx);
        }

        // ================================================================
        // IPolyLingCore 操作
        // ================================================================

        public void DispatchPanelCommand(PanelCommand cmd)
            => DispatchPanelCommandInternal(cmd);

        public ModelContext CreateNewModel(string name)
        {
            CreateNewModelWithUndo_void(name);
            return _project?.CurrentModel;
        }

        public void NotifyPanels(ChangeKind kind = ChangeKind.ListStructure)
        {
            if (_project == null || _panelContext == null || _liveProjectView == null) return;
            if (kind == ChangeKind.ListStructure || kind == ChangeKind.ModelSwitch)
                _liveProjectView.InvalidateLists();
            _panelContext.Notify(_liveProjectView, kind);
        }

        public void Tick()
        {
            if (_commandQueue != null && _commandQueue.Count > 0)
            {
                _commandQueue.ProcessAll();
                OnRepaintRequired?.Invoke();
            }
            int processed = UndoManager.Instance.ProcessAllQueues();
            if (processed > 0)
                OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // 追加イベント（Editorが接続して使う）
        // ================================================================

        /// <summary>カメラフォーカス要求（Editorがカメラ位置を変更する）</summary>
        public event Action<Vector3> OnFocusCameraRequested;

        /// <summary>UndoRedoが実行された（EditorがViewportを更新する）</summary>
        public event Action OnUndoRedoPerformed_Ext;
        /// <summary>DeleteSelectedVertices/MergeSelectedVertices後のSyncMesh要求</summary>
        public event Action<MeshContext> OnSyncMeshRequired;

        /// <summary>
        /// メッシュ切り替え時に新しいSelectionStateを通知する。
        /// Editorはこれを受けて _unifiedAdapter.SetSelectionState() を呼ぶ。
        /// </summary>
        public event Action<SelectionState> OnSelectionStateChanged;

        // ================================================================
        // 内部コールバック
        // ================================================================

        private void OnSelectionChanged()
            => OnRepaintRequired?.Invoke();

        private void OnCurrentModelChangedInternal()
        {
            _undoController?.SetModelContext(_model);
            if (_model != null)
            {
                _model.OnListChanged -= OnMeshListChangedInternal;
                _model.OnListChanged += OnMeshListChangedInternal;
                _model.WorkPlane = _undoController?.WorkPlane;
            }
            UpdateMeshListOpsContext();
            OnCurrentModelChanged?.Invoke();
        }

        private void OnModelsChanged()
            => NotifyPanels(ChangeKind.ListStructure);

        private void OnMeshListChangedInternal()
        {
            _model?.InvalidateTypedIndices();
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext != null)
                InitVertexOffsets(updateCamera: false);
            else
            {
                SetSelectedIndex(_meshContextList != null && _meshContextList.Count > 0 ? 0 : -1);
                var fallback = _model?.FirstSelectedMeshContext;
                if (fallback != null) InitVertexOffsets(updateCamera: false);
            }
            SaveSelectionToCurrentMesh();
            LoadSelectionFromCurrentMesh();
            NotifyPanels(ChangeKind.ListStructure);
            OnMeshListChanged?.Invoke();
            OnRepaintRequired?.Invoke();
        }

        private void OnMeshSelectionChangedInternal()
        {
            SaveSelectionToCurrentMesh();
            LoadSelectionFromCurrentMesh();
            if (_model?.HasValidMeshContextSelection == true)
            {
                InitVertexOffsets();
                var mc = _model.FirstSelectedMeshContext;
                if (mc != null && _undoController != null)
                    _undoController.MeshUndoContext.SelectedVertices = _selectionState.Vertices;
            }
        }

        private void OnToolChanged(IEditTool oldTool, IEditTool newTool)
        {
            if (_undoController?.EditorState != null && newTool != null)
                _undoController.EditorState.CurrentToolName = newTool.Name;
        }

        private void OnModelReorderCompleted()
        {
            UpdateTopology();
            NotifyPanels(ChangeKind.ListStructure);
        }

        private void UpdateMeshListOpsContext()
        {
            _meshListOps?.SetContext(_model, _undoController);
            SetupMeshListOpsCallbacks();
            NotifyPanels(ChangeKind.ModelSwitch);
        }

        private void SetupMeshListOpsCallbacks()
        {
            if (_meshListOps == null) return;
            _meshListOps.SyncPositionsOnly = ctx => _toolContext?.SyncMeshContextPositionsOnly?.Invoke(ctx);
            _meshListOps.SyncMesh          = () => _toolContext?.SyncMesh?.Invoke();
            _meshListOps.Repaint           = () => OnRepaintRequired?.Invoke();
        }

        private void UpdateTopology()
        {
            if (_meshTopology == null) return;
            var mc = _model?.FirstSelectedMeshContext;
            _meshTopology.SetMeshObject(mc?.MeshObject);
        }

        // ================================================================
        // partial メソッド宣言
        // ================================================================

        partial void InitVertexOffsets(bool updateCamera = true);
        partial void SetSelectedIndex(int index);
        partial void SaveSelectionToCurrentMesh();
        partial void LoadSelectionFromCurrentMesh();
        partial void RecordSelectionChange(HashSet<int> oldSel, HashSet<int> newSel);

        private void OnCurrentModelChangedHandler(int index)
            => OnCurrentModelChangedInternal();
        partial void DispatchPanelCommandInternal(PanelCommand cmd);

        partial void AddMeshContextWithUndo(MeshContext mc);
        partial void AddMeshContextsWithUndo(IList<MeshContext> mcs);
        partial void RemoveMeshContextWithUndo(int index);
        partial void SelectMeshContentWithUndo(int index);
        partial void DuplicateMeshContentWithUndo(int index);
        partial void ReorderMeshContentWithUndo(int fromIndex, int toIndex);
        partial void UpdateMeshAttributesWithUndo(IList<MeshAttributeChange> changes);
        partial void ClearAllMeshContextsWithUndo();
        partial void ReplaceAllMeshContextsWithUndo(IList<MeshContext> mcs);
        partial void SelectModelWithUndo(int index);
        partial void CreateNewModelWithUndo_void(string name);
        partial void OnMeshContextCreatedAsNew(MeshObject obj, string name);
        partial void OnMeshObjectCreatedAddToCurrent(MeshObject obj, string name);
        partial void AddMaterialsToModel(IList<Material> mats);
        partial void AddMaterialRefsToModel(IList<MaterialReference> refs);
        partial void ReplaceMaterialsInModel(IList<Material> mats);
        partial void ReplaceMaterialRefsInModel(IList<MaterialReference> refs);

        // ================================================================
        // Dispose
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_selectionState != null)
                _selectionState.OnSelectionChanged -= OnSelectionChanged;

            _commandQueue?.Clear();
            _commandQueue = null;

            if (_undoController != null)
            {
                _undoController.Dispose();
                _undoController = null;
            }

            if (_project != null)
            {
                _project.OnCurrentModelChanged -= OnCurrentModelChangedHandler;
                _project.OnModelsChanged       -= OnModelsChanged;
            }

            if (_model != null)
                _model.OnListChanged -= OnMeshListChangedInternal;
        }
    }
}
