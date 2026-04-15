// PolyLingPlayerViewerCore.cs
// PolyLingPlayerViewer のロジック本体（MonoBehaviour 非依存プレーンクラス）
//
// PolyLingPlayerViewer（MonoBehaviour ラッパー）と
// PolyLingPlayerEditorWindow（EditorWindow ラッパー）の両方から使う。
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// PolyLingPlayer のロジック本体。MonoBehaviour に依存しないプレーンクラス。
    /// 外部から Initialize / Tick / LateTick / Dispose を呼んでライフサイクルを制御する。
    /// </summary>
    public class PolyLingPlayerViewerCore
    {
        // ================================================================
        // 公開型
        // ================================================================

        /// <summary>リモート起動モード。</summary>
        public enum RemoteMode { None, Client, Server }

        /// <summary>Initialize に渡すリモート設定。</summary>
        public struct RemoteConfig
        {
            public RemoteMode Mode;
            public string     ClientHost;
            public int        ClientPort;
            public bool       ClientAutoConnect;
            public int        ServerPort;
            public bool       ServerAutoStart;

            /// <summary>デフォルト値（None モード）を返す。</summary>
            public static RemoteConfig Default => new RemoteConfig
            {
                Mode             = RemoteMode.None,
                ClientHost       = "127.0.0.1",
                ClientPort       = 8765,
                ClientAutoConnect = true,
                ServerPort       = 8765,
                ServerAutoStart  = true,
            };
        }

        // ================================================================
        // リモート設定（Initialize で設定）
        // ================================================================

        private RemoteMode _remoteMode;
        private string     _clientHost;
        private int        _clientPort;
        private bool       _clientAutoConnect;
        private int        _serverPort;
        private bool       _serverAutoStart;
        private Transform  _sceneRoot;

        // ================================================================
        // サブシステム
        // ================================================================

        private PolyLingPlayerClient           _client;
        private PolyLingPlayerServer           _playerServer;
        private RemoteProjectReceiver          _receiver;
        private MeshSceneRenderer              _renderer;
        private readonly PlayerLocalLoader     _localLoader    = new PlayerLocalLoader();
        private readonly UndoManager           _undoManager    = UndoManager.CreateNew();
        private          PlayerEditOps         _editOps;
        private VisualElement                  _uiRoot;

        private readonly PlayerViewportManager _viewportManager = new PlayerViewportManager();
        private PlayerLayoutRoot               _layoutRoot;
        private PlayerImportSubPanel           _importSubPanel;
        private PlayerExportSubPanel           _exportSubPanel;
        private PlayerProjectFileSubPanel      _projectFileSubPanel;
        private PlayerPartialImportSubPanel    _partialImportSubPanel;
        private PlayerPartialExportSubPanel    _partialExportSubPanel;
        private PlayerPrimitiveMeshSubPanel    _primitiveSubPanel;
        private MeshFilterToSkinnedSubPanel    _mfToSkinnedSubPanel;
        private PanelContext                   _panelContext;
        private ModelListSubPanel              _modelListSubPanel;
        private MeshListSubPanel               _meshListSubPanel;

        // 頂点インタラクション（Perspective ビューポート専用）
        private SelectionState         _selectionState;
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private enum ToolMode { VertexMove, ObjectMove, PivotOffset, Sculpt, AdvancedSelect, SkinWeightPaint, AddFace, EdgeBevel, EdgeExtrude, FaceExtrude, EdgeTopology, Knife }
        private ToolMode               _toolMode = ToolMode.VertexMove;

        private Button _activeBtn;

        private struct OverlayIndicator
        {
            public int     MeshContextIndex;
            public Vector2 ScreenPos;
            public bool    IsBone;
        }
        private readonly System.Collections.Generic.List<OverlayIndicator> _overlayIndicators =
            new System.Collections.Generic.List<OverlayIndicator>();
        private const float OverlayHitRadius = 8f;

        private MoveToolHandler              _moveToolHandler;
        private ObjectMoveToolHandler        _objectMoveHandler;
        private PivotOffsetToolHandler       _pivotOffsetHandler;
        private SculptToolHandler            _sculptHandler;
        private AdvancedSelectToolHandler    _advancedSelectHandler;
        private SkinWeightPaintToolHandler   _skinWeightPaintHandler;
        private PlayerSkinWeightPaintPanel   _skinWeightPaintPanel;
        private int                          _skinWeightUndoMasterIndex = -1;
        private int                          _uvUndoMasterIndex         = -1;
        private PlayerBlendSubPanel          _blendSubPanel;
        private PlayerModelBlendSubPanel     _modelBlendSubPanel;
        private BoneInputHandler             _boneInputHandler;
        private PlayerBoneEditorSubPanel     _boneEditorSubPanel;
        private PlayerUVEditorSubPanel       _uvEditorSubPanel;
        private PlayerUVUnwrapSubPanel       _uvUnwrapSubPanel;
        private PlayerMaterialListSubPanel   _materialListSubPanel;
        private PlayerUVZSubPanel            _uvzSubPanel;
        private PlayerPartsSelectionSetSubPanel _partsSelSetSubPanel;
        private PlayerMeshSelectionSetSubPanel  _meshSelSetSubPanel;
        private PlayerMergeMeshesSubPanel    _mergeMeshesSubPanel;
        private PlayerMorphSubPanel          _morphSubPanel;
        private PlayerMorphCreateSubPanel    _morphCreateSubPanel;
        private PlayerTPoseSubPanel          _tposeSubPanel;
        private PlayerHumanoidMappingSubPanel _humanoidMappingSubPanel;
        private PlayerMirrorSubPanel         _mirrorSubPanel;
        private PlayerQuadDecimatorSubPanel  _quadDecimatorSubPanel;
        private PlayerAlignVerticesSubPanel       _alignVerticesSubPanel;
        private AlignVerticesToolHandler          _alignVerticesHandler;
        private PlayerPlanarizeAlongBonesSubPanel _planarizeAlongBonesSubPanel;
        private PlanarizeAlongBonesToolHandler    _planarizeAlongBonesHandler;
        private PlayerMergeVerticesSubPanel       _mergeVerticesSubPanel;
        private MergeVerticesToolHandler          _mergeVerticesHandler;
        private PlayerSplitVerticesSubPanel       _splitVerticesSubPanel;
        private SplitVerticesToolHandler          _splitVerticesHandler;
        private PlayerAddFaceSubPanel             _addFaceSubPanel;
        private AddFaceToolHandler                _addFaceHandler;
        private PlayerFlipFaceSubPanel            _flipFaceSubPanel;
        private FlipFaceToolHandler               _flipFaceHandler;
        private PlayerRotateSubPanel              _rotateSubPanel;
        private RotateToolHandler                 _rotateHandler;
        private PlayerScaleSubPanel               _scaleSubPanel;
        private ScaleToolHandler                  _scaleHandler;
        private PlayerEdgeBevelSubPanel           _edgeBevelSubPanel;
        private EdgeBevelToolHandler              _edgeBevelHandler;
        private PlayerEdgeExtrudeSubPanel         _edgeExtrudeSubPanel;
        private EdgeExtrudeToolHandler            _edgeExtrudeHandler;
        private PlayerFaceExtrudeSubPanel         _faceExtrudeSubPanel;
        private FaceExtrudeToolHandler            _faceExtrudeHandler;
        private PlayerEdgeTopologySubPanel        _edgeTopologySubPanel;
        private EdgeTopologyToolHandler           _edgeTopologyHandler;
        private PlayerKnifeSubPanel               _knifeSubPanel;
        private KnifeToolHandler                  _knifeHandler;
        private PlayerLineExtrudeSubPanel         _lineExtrudeSubPanel;
        private LineExtrudeToolHandler            _lineExtrudeHandler;
        private PlayerMediaPipeFaceDeformSubPanel _mediaPipeSubPanel;
        private PlayerVMDTestSubPanel        _vmdTestSubPanel;
        private PlayerRemoteServerSubPanel   _remoteServerSubPanel;
        private PlayerVertexMoveSubPanel     _vertexMoveSubPanel;
        private PlayerPivotSubPanel          _pivotSubPanel;
        private PlayerSculptSubPanel         _sculptSubPanel;
        private PlayerAdvancedSelectSubPanel _advancedSelectSubPanel;

        private PlayerViewportPanel    _activePanel;
        private Vector2                _lastMouseScreenPos;
        private PlayerViewport         _activeViewport;

        private PlayerCommandDispatcher _commandDispatcher;

        private readonly List<(VisualElement section, Action refresh)> _sectionRefreshPairs = new();
        private PlayerRemoteFetchFlow   _fetchFlow;

        private string _status = "未接続";

        // ================================================================
        // 公開ライフサイクル API
        // ================================================================

        /// <summary>
        /// 初期化。MonoBehaviour の Awake + Start に相当する処理を行う。
        /// uiRoot には EditorWindow.rootVisualElement または UIDocument.rootVisualElement を渡す。
        /// sceneRoot には Camera 等を親付けする Transform（通常はプレイヤーの gameObject.transform）を渡す。
        /// </summary>
        public void Initialize(VisualElement uiRoot, Transform sceneRoot, RemoteConfig config)
        {
            _sceneRoot          = sceneRoot;
            _remoteMode         = config.Mode;
            _clientHost         = config.ClientHost;
            _clientPort         = config.ClientPort;
            _clientAutoConnect  = config.ClientAutoConnect;
            _serverPort         = config.ServerPort;
            _serverAutoStart    = config.ServerAutoStart;

            // ── リモートモード初期化 ────────────────────────────────────
            switch (_remoteMode)
            {
                case RemoteMode.Client:
                    _client = new PolyLingPlayerClient();
                    _client.Initialize(_clientHost, _clientPort, _clientAutoConnect);
                    break;
                case RemoteMode.Server:
                    _client = null;
                    _playerServer = new PolyLingPlayerServer();
                    // Initialize は BuildLayout 後（_commandDispatcher 確定後）に呼ぶ
                    break;
                default: // None
                    _client = null;
                    break;
            }

            _renderer = new MeshSceneRenderer();
            _receiver = new RemoteProjectReceiver();
            _editOps  = new PlayerEditOps(_undoManager);

            // VertexEdit スタック Undo/Redo 後の復元ハンドラ
            // 頂点移動（PendingMeshMoveEntries）と選択変更（CurrentSelectionSnapshot）を消費する
            _editOps.UndoController.OnUndoRedoPerformed += () =>
            {
                var stackType = _editOps.UndoController.LastUndoRedoStackType;

                // ── MeshList（BoneTransform変更・PivotMove等）の復元
                if (stackType == MeshUndoController.UndoStackType.MeshList)
                {
                    var listCtx   = _editOps.UndoController.MeshListContext;
                    var model     = listCtx ?? ActiveProject?.CurrentModel;
                    if (model != null)
                    {
                        model.ComputeWorldMatrices();

                        var lastRecord = _editOps.UndoController.MeshListStack.LastExecutedRecord;
                        bool needsRebuild = lastRecord is MeshListChangeRecord
                                         || lastRecord is MeshAttributesBatchChangeRecord
                                         || lastRecord is MultiMeshVertexSnapshotRecord;
                        if (needsRebuild)
                        {
                            _viewportManager.RebuildAdapter(0, model);
                            RebuildModelList();
                        }
                        else if (lastRecord is PivotMoveRecord pivotRec)
                        {
                            // PivotMoveRecord は頂点位置も変更するため、
                            // GPU位置バッファを更新してからトランスフォームを適用する
                            var pivotMc = model.GetMeshContext(pivotRec.MasterIndex);
                            if (pivotMc != null)
                                _viewportManager.SyncMeshPositionsAndTransform(pivotMc, model);
                        }

                        _renderer?.UpdateSelectedDrawableMesh(0, model);
                        _viewportManager.UpdateTransform();
                        NotifyPanels(ChangeKind.Attributes);
                    }
                    return;
                }

                if (stackType != MeshUndoController.UndoStackType.VertexEdit)
                    return;
                var ctx = _editOps.UndoController.MeshUndoContext;
                if (ctx == null) return;
                var targetModel = ctx.ParentModelContext;
                if (targetModel == null) return;

                // ── 頂点移動の復元
                var pending = ctx.PendingMeshMoveEntries;
                if (pending != null && pending.Length > 0)
                {
                    foreach (var entry in pending)
                    {
                        var mc = targetModel.GetMeshContext(entry.MeshContextIndex);
                        if (mc?.MeshObject == null) continue;
                        var mo = mc.MeshObject;
                        for (int i = 0; i < entry.Indices.Length; i++)
                        {
                            int vi = entry.Indices[i];
                            if (vi >= 0 && vi < mo.VertexCount)
                                mo.Vertices[vi].Position = entry.NewPositions[i];
                        }
                        mo.InvalidatePositionCache();
                        _viewportManager.SyncMeshPositionsAndTransform(mc, targetModel);
                    }
                    ctx.PendingMeshMoveEntries = null;
                    _viewportManager.ExitTransformDragging();
                    _viewportManager.UpdateTransform();
                    _renderer?.UpdateSelectedDrawableMesh(0, targetModel);
                    NotifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 選択状態の復元
                var snapshot = ctx.CurrentSelectionSnapshot;
                if (snapshot != null)
                {
                    var firstMc = targetModel.FirstSelectedMeshContext
                                  ?? targetModel.FirstDrawableMeshContext;
                    if (firstMc?.Selection != null)
                    {
                        firstMc.Selection.RestoreFromSnapshot(snapshot);
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }
                    ctx.CurrentSelectionSnapshot = null;
                    NotifyPanels(ChangeKind.Selection);
                }

                // ── トポロジー／ボーンウェイト／UV／マテリアル変更の復元
                // MeshSnapshotRecord は ctx.MeshObject をクローンに差し替えるだけで
                // ModelContext 上の実 MeshContext には書き戻さないため、ここで同期する
                if (ctx.MeshObject != null)
                {
                    // 優先度順で対象 MasterIndex を決定
                    int topoMasterIdx = _skinWeightUndoMasterIndex >= 0
                        ? _skinWeightUndoMasterIndex
                        : _uvUndoMasterIndex;

                    // 明示的な MasterIndex がない場合は MeshObject 参照から逆引き
                    if (topoMasterIdx < 0)
                    {
                        for (int mi = 0; mi < targetModel.MeshContextCount; mi++)
                        {
                            var searchMc = targetModel.GetMeshContext(mi);
                            if (searchMc?.MeshObject != null &&
                                ReferenceEquals(searchMc.MeshObject, ctx.MeshObject))
                            { topoMasterIdx = mi; break; }
                        }
                        // 逆引きでも見つからない（既に差し替え後）→ 先頭Drawableにフォールバック
                        if (topoMasterIdx < 0)
                        {
                            var fb = targetModel.FirstDrawableMeshContext;
                            if (fb != null) topoMasterIdx = targetModel.IndexOf(fb);
                        }
                    }

                    if (topoMasterIdx >= 0)
                    {
                        var liveMc = targetModel.GetMeshContext(topoMasterIdx);
                        if (liveMc?.MeshObject != null && !ReferenceEquals(liveMc.MeshObject, ctx.MeshObject))
                        {
                            CopyMeshObjectVertexData(ctx.MeshObject, liveMc.MeshObject);
                            _editOps.UndoController.SetMeshObject(liveMc.MeshObject, liveMc.UnityMesh);
                            _viewportManager.RebuildAdapter(0, targetModel);
                            _renderer?.UpdateSelectedDrawableMesh(0, targetModel);
                            NotifyPanels(ChangeKind.Attributes);
                        }
                    }
                }
            };

            _selectionState = new SelectionState();
            _renderer.SetSelectionState(_selectionState);

            _viewportManager.Initialize(_sceneRoot, _renderer);

            BuildLayout(uiRoot);

            SetupVertexInteraction();

            _commandDispatcher = new PlayerCommandDispatcher(
                () => ActiveProject,
                _renderer,
                _viewportManager,
                _selectionOps,
                NotifyPanels,
                RebuildModelList,
                _editOps?.UndoController,
                _editOps?.CommandQueue);

            _fetchFlow = new PlayerRemoteFetchFlow(
                _client,
                _receiver,
                _localLoader,
                _viewportManager,
                _renderer,
                _selectionOps,
                NotifyPanels,
                s => _status = s);
            _fetchFlow.OnModelContextReady = model =>
            {
                if (_editOps?.UndoController?.MeshUndoContext != null)
                    _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
            };

            // RemoteMode.Server: BuildLayout 後に Initialize（_commandDispatcher 確定後）
            if (_remoteMode == RemoteMode.Server && _playerServer != null)
            {
                _playerServer.Initialize(
                    _serverPort,
                    _serverAutoStart,
                    () => _viewportManager.GetCurrentToolContext(_activeViewport),
                    cmd => _commandDispatcher?.Dispatch(cmd));
            }

            // ── ローカルローダー配線 ────────────────────────────────────
            _localLoader.OnStatusChanged = s => _status = s;
            _localLoader.OnLoaded = project =>
            {
                _renderer.ClearScene();
                var loadedModel = project.CurrentModel;

                if (_importSubPanel?.AutoScale == true)
                {
                    var list = loadedModel.MeshContextList;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].UnityMesh != null)
                        {
                            _viewportManager.ResetToMesh(list[i].UnityMesh.bounds);
                            break;
                        }
                    }
                }
                _moveToolHandler?.SetProject(ActiveProject);
                _objectMoveHandler?.SetProject(ActiveProject);
                _pivotOffsetHandler?.SetProject(ActiveProject);
                _sculptHandler?.SetProject(ActiveProject);
                _advancedSelectHandler?.SetProject(ActiveProject);
                _skinWeightPaintHandler?.SetProject(ActiveProject);
                _boneInputHandler?.SetProject(ActiveProject);
                _alignVerticesHandler?.SetProject(ActiveProject);
                _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);

                _editOps?.UndoController.SetModelContext(loadedModel);

                loadedModel.ComputeWorldMatrices();

                _viewportManager.RebuildAdapter(0, loadedModel);

                var loadedDrawables = loadedModel.DrawableMeshes;
                if (loadedDrawables != null)
                    foreach (var entry in loadedDrawables)
                    {
                        var mc = entry.Context;
                        if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0 && mc.IsVisible)
                        { loadedModel.SelectMesh(entry.MasterIndex); break; }
                    }

                int lNeckIdx = -1, lFirstBone = -1;
                for (int ci = 0; ci < loadedModel.MeshContextCount; ci++)
                {
                    var bmc = loadedModel.GetMeshContext(ci);
                    if (bmc == null || bmc.Type != MeshType.Bone) continue;
                    if (lFirstBone < 0) lFirstBone = ci;
                    string n = bmc.Name ?? "";
                    if (n == "首" || n.ToLower() == "neck") { lNeckIdx = ci; break; }
                }
                int lSelBone = lNeckIdx >= 0 ? lNeckIdx : lFirstBone;
                if (lSelBone >= 0) loadedModel.SelectBone(lSelBone);

                var firstMcLocal = loadedModel.FirstDrawableMeshContext;
                if (firstMcLocal != null)
                {
                    _selectionOps?.SetSelectionState(firstMcLocal.Selection);
                    _renderer?.SetSelectionState(firstMcLocal.Selection);
                }
                if (_editOps?.UndoController?.MeshUndoContext != null)
                    _editOps.UndoController.MeshUndoContext.ParentModelContext = loadedModel;
                _renderer?.UpdateSelectedDrawableMesh(0, loadedModel);
                _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);

                // UNDO記録：インポートした全メッシュを1ステップとして記録
                // 図形生成と同じパターン（RecordMeshContextsAdd）。
                // UNDO → 全メッシュ削除（空モデルになる）、REDO → 復元。
                if (_editOps?.UndoController != null && loadedModel.MeshContextCount > 0)
                {
                    var importAdded = new System.Collections.Generic.List<(int, Poly_Ling.Data.MeshContext)>();
                    for (int _ii = 0; _ii < loadedModel.MeshContextCount; _ii++)
                    {
                        var _imc = loadedModel.GetMeshContext(_ii);
                        if (_imc != null) importAdded.Add((_ii, _imc));
                    }
                    if (importAdded.Count > 0)
                    {
                        var importNewSel = loadedModel.CaptureAllSelectedIndices();
                        _editOps.UndoController.RecordMeshContextsAdd(
                            importAdded,
                            new System.Collections.Generic.List<int>(),
                            importNewSel);
                    }
                }

                RebuildModelList();
                _skinWeightPaintPanel?.RefreshMeshList(loadedModel);
                _skinWeightPaintPanel?.RefreshBoneList(loadedModel);
                NotifyPanels(ChangeKind.ModelSwitch);
            };

            _receiver.OnProjectHeaderReceived += OnProjectHeaderReceived;
            _receiver.OnModelMetaReceived     += OnModelMetaReceived;
            _receiver.OnMeshSummaryReceived   += OnMeshSummaryReceived;
            _receiver.OnMeshDataReceived      += OnMeshDataReceived;

            if (_client != null)
            {
                _client.OnConnected    += OnConnected;
                _client.OnDisconnected += OnDisconnected;
                _client.OnPushReceived += OnPushReceived;
            }

        }

        /// <summary>毎フレーム Update 相当。</summary>
        public void Tick()
        {
            _viewportManager.Update();
            _editOps?.Tick();
            _client?.Tick();
            _playerServer?.Tick();
            _primitiveSubPanel?.Tick();
            SyncUI();
            UpdateFaceHoverOverlay();
            UpdateSelectedFacesOverlay();
            UpdateGizmoOverlay();
            UpdateAdvancedSelectOverlay();
            UpdateAddFaceOverlay();
            UpdateBoneOverlay();
        }

        /// <summary>毎フレーム LateUpdate 相当。</summary>
        public void LateTick()
        {
            _viewportManager.LateUpdate(ActiveProject);
        }

        /// <summary>破棄。OnDestroy 相当。</summary>
        public void Dispose()
        {
            if (_activePanel != null)
                _vertexInteractor?.Disconnect(_activePanel);

            _viewportManager.Dispose();

            _primitiveSubPanel?.Dispose();
            _primitiveSubPanel = null;

            if (_client != null)
            {
                _client.OnConnected    -= OnConnected;
                _client.OnDisconnected -= OnDisconnected;
                _client.OnPushReceived -= OnPushReceived;
                _client.Dispose();
            }
            _playerServer?.Dispose();

            if (_receiver != null)
            {
                _receiver.OnProjectHeaderReceived -= OnProjectHeaderReceived;
                _receiver.OnModelMetaReceived     -= OnModelMetaReceived;
                _receiver.OnMeshSummaryReceived   -= OnMeshSummaryReceived;
                _receiver.OnMeshDataReceived      -= OnMeshDataReceived;
            }

            _editOps?.Dispose();
            _editOps = null;
            _renderer?.Dispose();
            _renderer = null;
        }

        // ================================================================
        // 頂点インタラクション セットアップ
        // ================================================================

        private void SetupVertexInteraction()
        {
            _selectionOps = new PlayerSelectionOps(_selectionState);

            _selectionOps.OnSelectionChanged = () =>
            {
                _renderer?.NotifySelectionChanged();
            };

            _moveToolHandler = new MoveToolHandler(_selectionOps, ActiveProject)
            {
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                    _viewportManager.UpdateTransform();
                },
                OnRepaint = () => _activePanel?.MarkDirtyRepaint(),

                GetHoverElement = mode => _viewportManager.GetHoverElement(mode, ActiveProject?.CurrentModel),
                GetToolContext  = () => _viewportManager.GetCurrentToolContext(_activeViewport),

                GetScreenPositions = () => _viewportManager.GetScreenPositions(),
                GetVertexOffset    = ctxIdx => _viewportManager.GetVertexOffset(ctxIdx),
                IsVertexVisible    = gi  => _viewportManager.IsVertexVisible(gi),
                GetViewportHeight  = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                GetPanelHeight     = () => _activeViewport?.Cam?.pixelHeight ?? 0f,

                OnBoxSelectUpdate = (start, end) => _activePanel?.ShowBoxSelect(start, end),
                OnBoxSelectEnd    = () => _activePanel?.HideBoxSelect(),

                OnLassoSelectUpdate = points => _activePanel?.ShowLassoSelect(points),
                OnLassoSelectEnd    = () => _activePanel?.HideLassoSelect(),

                OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging(),
                OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging(),
                OnEnterBoxSelecting      = () => _viewportManager.EnterBoxSelecting(),
                OnReadBackVertexFlags    = () => _viewportManager.ReadBackVertexFlags(),
                OnExitBoxSelecting       = () => _viewportManager.ExitBoxSelecting(),
                OnRequestNormal          = () => _viewportManager.RequestNormal(),
                OnClearMouseHover        = () => _viewportManager.ClearMouseHover(),
            };
            _moveToolHandler.SetUndoController(_editOps?.UndoController);
            _viewportManager.RegisterMoveToolHandler(_moveToolHandler);

            _objectMoveHandler = new ObjectMoveToolHandler();
            _objectMoveHandler.SetProject(ActiveProject);
            _objectMoveHandler.SetUndoController(_editOps?.UndoController);
            _objectMoveHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _objectMoveHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _objectMoveHandler.OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging();
            _objectMoveHandler.OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging();
            _objectMoveHandler.OnMeshSelectionChanged   = () => { };
            _objectMoveHandler.OnSyncBoneTransforms     = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel != null)
                {
                    proj.CurrentModel.ComputeWorldMatrices();
                    _viewportManager.UpdateTransform();
                }
                NotifyPanels(ChangeKind.Attributes);
            };

            _pivotOffsetHandler = new PivotOffsetToolHandler();
            _pivotOffsetHandler.SetProject(ActiveProject);
            _pivotOffsetHandler.SetUndoController(_editOps?.UndoController);
            _pivotOffsetHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _pivotOffsetHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _pivotOffsetHandler.OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging();
            _pivotOffsetHandler.OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging();
            _pivotOffsetHandler.OnSyncBoneTransforms     = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel != null)
                {
                    proj.CurrentModel.ComputeWorldMatrices();
                    _viewportManager.UpdateTransform();
                }
                NotifyPanels(ChangeKind.Attributes);
            };
            _pivotOffsetHandler.OnSyncMeshPositions = mc =>
            {
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                _viewportManager.UpdateTransform();
            };

            _sculptHandler = new SculptToolHandler();
            _sculptHandler.SetProject(ActiveProject);
            _sculptHandler.SetUndoController(_editOps?.UndoController);
            _sculptHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _sculptHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _sculptHandler.OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging();
            _sculptHandler.OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging();
            _sculptHandler.OnSyncMeshPositions = mc =>
            {
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                _viewportManager.UpdateTransform();
            };
            _sculptHandler.OnUpdateBrushCircle = (center, radius) =>
                _activePanel?.ShowBrushCircle(center, radius);
            _sculptHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();
            _sculptHandler.GetBrushHit = (pos, r) => _viewportManager.GetBrushHit(pos, r);

            _advancedSelectHandler = new AdvancedSelectToolHandler();
            _advancedSelectHandler.SetProject(ActiveProject);
            _advancedSelectHandler.SetSelectionOps(_selectionOps);
            _advancedSelectHandler.SetUndoController(_editOps?.UndoController);
            _advancedSelectHandler.GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _advancedSelectHandler.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _advancedSelectHandler.OnSelectionChanged = () =>
            {
                _renderer?.NotifySelectionChanged();
                _viewportManager.RequestNormal();
            };

            _skinWeightPaintHandler = new SkinWeightPaintToolHandler();
            _skinWeightPaintHandler.SetProject(ActiveProject);
            _skinWeightPaintHandler.SetUndoController(_editOps?.UndoController);
            _skinWeightPaintHandler.SetCommandQueue(_editOps?.CommandQueue);
            _skinWeightPaintHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintHandler.OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging();
            _skinWeightPaintHandler.OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging();
            _skinWeightPaintHandler.OnSyncMeshPositions = mc =>
            {
                // boneWeights 変更はバッファ全体の再構築が必要
                var swModel = ActiveProject?.CurrentModel;
                if (swModel != null)
                {
                    _viewportManager.RebuildAdapter(0, swModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, swModel);
                }
            };
            _skinWeightPaintHandler.OnUpdateBrushCircle = (center, radius, color) =>
                _activePanel?.ShowBrushCircle(center, radius, color);
            _skinWeightPaintHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();

            _vertexInteractor = new PlayerVertexInteractor(_selectionOps)
            {
                GetHoverHit = () => _viewportManager.GetHoverHit(),
            };
            _vertexInteractor.SetToolHandler(_moveToolHandler);

            _activePanel    = _layoutRoot?.PerspectivePanel;
            _activeViewport = _viewportManager.PerspectiveViewport;

            if (_activePanel != null)
                _vertexInteractor.Connect(_activePanel);

            void ConnectPanelHover(PlayerViewportPanel panel, PlayerViewport vp)
            {
                if (panel == null) return;

                panel.OnPointerMoved += (pos, mods) =>
                {
                    _lastMouseScreenPos = pos;
                    if (_layoutRoot?.BoneEditorSection != null &&
                        _layoutRoot.BoneEditorSection.style.display == DisplayStyle.Flex)
                    {
                        var boneCtx = _viewportManager.GetCurrentToolContext(_activeViewport);
                        _boneInputHandler?.UpdateHover(pos, boneCtx);
                    }
                };

                panel.OnPointerHover += localPos =>
                {
                    if (_activePanel != panel)
                    {
                        if (_activePanel != null)
                        {
                            _activePanel.HideBoxSelect();
                            _activePanel.HideFaceHover();
                            _activePanel.HideGizmo();
                            _vertexInteractor.Disconnect(_activePanel);
                        }
                        _activePanel    = panel;
                        _activeViewport = vp;
                        _vertexInteractor.Connect(_activePanel);
                    }
                    _viewportManager.NotifyPointerHover(vp, localPos);
                };
            }

            ConnectPanelHover(_layoutRoot?.PerspectivePanel, _viewportManager.PerspectiveViewport);
            ConnectPanelHover(_layoutRoot?.TopPanel,         _viewportManager.TopViewport);
            ConnectPanelHover(_layoutRoot?.FrontPanel,       _viewportManager.FrontViewport);
            ConnectPanelHover(_layoutRoot?.SidePanel,        _viewportManager.SideViewport);

            void ConnectBoneInput(PlayerViewportPanel panel)
            {
                if (panel == null) return;
                panel.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_toolMode == ToolMode.ObjectMove || _toolMode == ToolMode.PivotOffset) return;
                    _boneInputHandler?.OnLeftClick(PlayerHitResult.Miss, pos, mods);
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragBegin += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_toolMode == ToolMode.ObjectMove || _toolMode == ToolMode.PivotOffset) return;
                    _boneInputHandler?.OnLeftDragBegin(PlayerHitResult.Miss, pos, mods);
                };
                panel.OnDrag += (btn, pos, delta, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_toolMode == ToolMode.ObjectMove || _toolMode == ToolMode.PivotOffset) return;
                    _boneInputHandler?.OnLeftDrag(pos, delta, mods);
                    _viewportManager.UpdateTransform();
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragEnd += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    if (_toolMode == ToolMode.ObjectMove || _toolMode == ToolMode.PivotOffset) return;
                    _boneInputHandler?.OnLeftDragEnd(pos, mods);
                    _boneEditorSubPanel?.Refresh();
                };
            }
            ConnectBoneInput(_layoutRoot?.PerspectivePanel);
            ConnectBoneInput(_layoutRoot?.TopPanel);
            ConnectBoneInput(_layoutRoot?.FrontPanel);
            ConnectBoneInput(_layoutRoot?.SidePanel);

            void ConnectIndicatorInput(PlayerViewportPanel p)
            {
                if (p == null) return;
                p.OnClick += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    TrySelectIndicatorAtScreenPos(pos, mods);
                };
            }
            ConnectIndicatorInput(_layoutRoot?.PerspectivePanel);
            ConnectIndicatorInput(_layoutRoot?.TopPanel);
            ConnectIndicatorInput(_layoutRoot?.FrontPanel);
            ConnectIndicatorInput(_layoutRoot?.SidePanel);

            void ConnectCameraChanged(PlayerViewport vp)
            {
                if (vp == null) return;
                if (vp.Orbit != null)
                {
                    vp.Orbit.OnCameraDragBegin = () => _viewportManager.EnterCameraDragging();
                    vp.Orbit.OnCameraChanged   = () =>
                    {
                        _viewportManager.ExitCameraDragging();
                        _viewportManager.NotifyCameraChanged(vp);
                    };
                }
                if (vp.Ortho != null)
                {
                    vp.Ortho.OnCameraDragBegin = () => _viewportManager.EnterCameraDragging();
                    vp.Ortho.OnCameraDragEnd   = () => _viewportManager.ExitCameraDragging();
                    vp.Ortho.OnCameraChanged   = () => _viewportManager.NotifyCameraChanged(vp);
                }
            }

            ConnectCameraChanged(_viewportManager.PerspectiveViewport);
            ConnectCameraChanged(_viewportManager.TopViewport);
            ConnectCameraChanged(_viewportManager.FrontViewport);
            ConnectCameraChanged(_viewportManager.SideViewport);
        }

        // ================================================================
        // オーバーレイ更新
        // ================================================================

        private void UpdateFaceHoverOverlay()
        {
            if (_toolMode == ToolMode.ObjectMove   ||
                _toolMode == ToolMode.PivotOffset  ||
                _toolMode == ToolMode.SkinWeightPaint)
            {
                _activePanel?.HideFaceHover();
                return;
            }
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideFaceHover(); return; }
            var pts = _viewportManager.GetHoverFaceScreenPts(_activeViewport, model);
            if (pts == null) panel.HideFaceHover();
            else             panel.ShowFaceHover(pts);
        }

        private void UpdateSelectedFacesOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.HideSelectedFaces(); return; }
            var faces = _viewportManager.GetSelectedFacesScreenPts(_activeViewport, model);
            if (faces == null) panel.HideSelectedFaces();
            else               panel.ShowSelectedFaces(faces);
        }

        private void UpdateBoneOverlay()
        {
            var panel = _activePanel;
            _overlayIndicators.Clear();

            if (panel == null) return;

            bool boneEditorOpen = _layoutRoot?.BoneEditorSection?.style.display == DisplayStyle.Flex;
            bool objectMoveMode = _toolMode == ToolMode.ObjectMove;
            bool pivotMode      = _toolMode == ToolMode.PivotOffset;
            if (!boneEditorOpen && !objectMoveMode && !pivotMode)
            {
                panel.HideBoneWire();
                return;
            }

            var model = ActiveProject?.CurrentModel;
            if (model == null || model.MeshContextCount == 0)
            {
                panel.HideBoneWire();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideBoneWire(); return; }

            float panelH    = ctx.PreviewRect.height;
            var positions   = new System.Collections.Generic.List<Vector2>();
            var selected    = new System.Collections.Generic.List<bool>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                bool isBone       = mc.Type == MeshType.Bone;
                bool isNonSkinned = mc.Type == MeshType.Mesh
                                    && mc.MeshObject != null
                                    && !mc.MeshObject.HasBoneWeight;

                bool include = boneEditorOpen
                    ? isBone
                    : (isBone || isNonSkinned);

                if (!include) continue;

                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                sp.y = panelH - sp.y;

                bool isSel = model.SelectedMeshContextIndices.Contains(i);

                positions.Add(sp);
                selected.Add(isSel);

                _overlayIndicators.Add(new OverlayIndicator
                {
                    MeshContextIndex = i,
                    ScreenPos        = sp,
                    IsBone           = isBone,
                });
            }

            if (positions.Count == 0) { panel.HideBoneWire(); return; }

            panel.UpdateBoneWire(positions.ToArray(), selected.ToArray());
        }

        private int HitTestOverlayIndicator(Vector2 screenPos)
        {
            float minDist = OverlayHitRadius;
            int   result  = -1;
            foreach (var ind in _overlayIndicators)
            {
                float d = Vector2.Distance(screenPos, ind.ScreenPos);
                if (d < minDist) { minDist = d; result = ind.MeshContextIndex; }
            }
            return result;
        }

        private bool TrySelectIndicatorAtScreenPos(Vector2 screenPos, ModifierKeys mods)
        {
            if (_toolMode != ToolMode.ObjectMove && _toolMode != ToolMode.PivotOffset)
                return false;

            int idx = HitTestOverlayIndicator(screenPos);
            if (idx < 0) return false;

            var model = ActiveProject?.CurrentModel;
            if (model == null) return false;

            if (mods.Shift || mods.Ctrl)
                model.ToggleMeshContextSelection(idx);
            else
                model.Select(idx);

            if (model.ActiveCategory == ModelContext.SelectionCategory.Mesh)
                _renderer?.UpdateSelectedDrawableMesh(0, model);
            _renderer?.NotifySelectionChanged();
            NotifyPanels(ChangeKind.Selection);
            _boneEditorSubPanel?.Refresh();
            _activePanel?.MarkDirtyRepaint();
            return true;
        }

        private void UpdateAddFaceOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            if (_toolMode != ToolMode.AddFace || _addFaceHandler == null)
            {
                panel.HideAddFacePreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAddFacePreview(); return; }

            var data = _addFaceHandler.GetPreviewData();
            float h = ctx.PreviewRect.height;

            // AdvSel と完全に同じパターン:
            // ViewerCore側で h - sp.y を行い、Panel側で panelH - pt.y を行う。
            System.Func<UnityEngine.Vector3, UnityEngine.Vector2> toScreen = (world) =>
            {
                var sp = ctx.WorldToScreen(world);
                return new UnityEngine.Vector2(sp.x, h - sp.y);
            };

            // 確定済み点
            var pts = new System.Collections.Generic.List<UnityEngine.Vector2>();
            foreach (var p in data.PlacedPoints)
                pts.Add(toScreen(p.Position));

            // 線（配置済み点間）
            var lines = new System.Collections.Generic.List<(UnityEngine.Vector2, UnityEngine.Vector2)>();
            for (int i = 1; i < data.PlacedPoints.Length; i++)
                lines.Add((toScreen(data.PlacedPoints[i - 1].Position), toScreen(data.PlacedPoints[i].Position)));

            // 連続線分モード開始点からプレビューへの線
            if (data.ContinuousLineStart.HasValue && data.PreviewValid)
                lines.Add((toScreen(data.ContinuousLineStart.Value.Position), toScreen(data.PreviewPoint)));

            // 最後の確定済み点からプレビューへの線
            if (data.PlacedPoints.Length > 0 && data.PreviewValid)
                lines.Add((toScreen(data.PlacedPoints[data.PlacedPoints.Length - 1].Position), toScreen(data.PreviewPoint)));

            // プレビュー点
            var previewPts  = new System.Collections.Generic.List<UnityEngine.Vector2>();
            var previewSnap = new System.Collections.Generic.List<bool>();
            if (data.PreviewValid)
            {
                previewPts.Add(toScreen(data.PreviewPoint));
                previewSnap.Add(data.PreviewSnapped);
            }

            panel.UpdateAddFacePreview(pts, previewPts, previewSnap, lines);
        }

        private void UpdateAdvancedSelectOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;

            if (_toolMode != ToolMode.AdvancedSelect || _advancedSelectHandler == null)
            {
                panel.HideAdvSelPreview();
                return;
            }

            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideAdvSelPreview(); return; }

            var previewCtx = _advancedSelectHandler.GetPreviewContext();
            if (previewCtx == null) { panel.HideAdvSelPreview(); return; }

            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) { panel.HideAdvSelPreview(); return; }

            var pts = new System.Collections.Generic.List<Vector2>();
            var verts = previewCtx.PreviewVertices;
            if (verts != null)
                foreach (int vi in verts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }
            var path = previewCtx.PreviewPath;
            if (path != null)
                foreach (int vi in path)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }

            var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
            var edges = previewCtx.PreviewEdges;
            if (edges != null)
                foreach (var e in edges)
                {
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount || e.V2 < 0 || e.V2 >= mo.VertexCount) continue;
                    var s1 = ctx.WorldToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }
            if (path != null && path.Count > 1)
                for (int i = 0; i < path.Count - 1; i++)
                {
                    int v1 = path[i], v2 = path[i + 1];
                    if (v1 < 0 || v1 >= mo.VertexCount || v2 < 0 || v2 >= mo.VertexCount) continue;
                    var s1 = ctx.WorldToScreen(mo.Vertices[v1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[v2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }

            panel.UpdateAdvSelPreview(pts, lines, _advancedSelectHandler.AddToSelection);
        }

        private void UpdateGizmoOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null) { panel.HideGizmo(); return; }

            if (_toolMode == ToolMode.ObjectMove)
            {
                if (_objectMoveHandler == null) { panel.HideGizmo(); return; }
                if (_objectMoveHandler.TryGetGizmoScreenPositions(
                        ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
                {
                    _objectMoveHandler.GetPivotScreenPos(out var pivotScreen);
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo      = true,
                        IsDiamondStyle = false,
                        Origin        = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                        HoveredAxis   = hovAxis,
                        HasPivotGizmo = true, PivotOrigin = pivotScreen,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_toolMode == ToolMode.PivotOffset)
            {
                if (_pivotOffsetHandler == null) { panel.HideGizmo(); return; }
                if (_pivotOffsetHandler.TryGetGizmoScreenPositions(
                        ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
                {
                    panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                    {
                        HasGizmo       = true,
                        IsDiamondStyle = true,
                        Origin         = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                        HoveredAxis    = hovAxis,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_toolMode == ToolMode.Sculpt || _toolMode == ToolMode.AdvancedSelect ||
                _toolMode == ToolMode.SkinWeightPaint)
            {
                panel.HideGizmo();
                return;
            }

            if (_moveToolHandler == null) { panel.HideGizmo(); return; }
            if (_moveToolHandler.TryGetGizmoScreenPositions(
                    ctx, out var o, out var xe, out var ye, out var ze, out var ha))
            {
                panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                    HoveredAxis = ha,
                });
            }
            else panel.HideGizmo();
        }

        // ================================================================
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _uiRoot = root;
            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            _panelContext = new PanelContext(DispatchPanelCommand);

            _modelListSubPanel = new ModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            _meshListSubPanel = new MeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);

            // ObjectMoveTRSPanel は BoneEditorSubPanel に統合済みのため生成不要

            _skinWeightPaintPanel = new PlayerSkinWeightPaintPanel();
            _skinWeightPaintPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintPanel.OnMeshSelectionChanged = () =>
            {
                if (_toolMode == ToolMode.SkinWeightPaint)
                    SyncSkinWeightUndoMesh();
                _activePanel?.MarkDirtyRepaint();
            };
            _skinWeightPaintPanel.GetToolContext =
                () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _skinWeightPaintPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _skinWeightPaintPanel.Build(_layoutRoot.SkinWeightPaintSection);

            _blendSubPanel = new PlayerBlendSubPanel();
            _blendSubPanel.OnSyncMeshPositions = mc =>
            {
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                _viewportManager.UpdateTransform();
            };
            _blendSubPanel.OnNotifyTopologyChanged = () =>
            {
                var proj = ActiveProject;
                if (proj?.CurrentModel == null) return;
                _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                NotifyPanels(ChangeKind.ListStructure);
            };
            _blendSubPanel.OnRepaint          = () => _activePanel?.MarkDirtyRepaint();
            _blendSubPanel.GetUndoController  = () => _editOps?.UndoController;
            _blendSubPanel.GetCommandQueue    = () => _editOps?.CommandQueue;
            _blendSubPanel.SetCommandContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _blendSubPanel.Build(_layoutRoot.BlendSection);

            _modelBlendSubPanel = new PlayerModelBlendSubPanel();
            _modelBlendSubPanel.SendCommand    = cmd => _commandDispatcher?.Dispatch(cmd);
            _modelBlendSubPanel.GetProjectView = () => ActiveProject != null
                ? new PlayerProjectView(ActiveProject) : null;
            _modelBlendSubPanel.Build(_layoutRoot.ModelBlendSection);

            _boneInputHandler = new BoneInputHandler();
            _boneInputHandler.SetProject(ActiveProject);
            _boneInputHandler.SetUndoController(_editOps?.UndoController);
            _boneInputHandler.GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _boneInputHandler.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _boneInputHandler.OnSelectionChanged = () =>
            {
                _viewportManager.RebuildAdapter(0, ActiveProject?.CurrentModel);
                _boneEditorSubPanel?.Refresh();
                NotifyPanels(ChangeKind.Selection);
            };
            _boneInputHandler.OnDrawableMeshSelectionChanged = () =>
            {
                _renderer?.UpdateSelectedDrawableMesh(0, ActiveProject?.CurrentModel);
            };

            _boneEditorSubPanel = new PlayerBoneEditorSubPanel();
            _boneEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _boneEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _boneEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _boneEditorSubPanel.SetContext(_panelContext);
            _boneEditorSubPanel.GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0;
            _boneEditorSubPanel.OnFocusCamera     = pos =>
            {
                var orbit = _activeViewport?.Orbit;
                if (orbit != null) { orbit.SetTarget(pos); _activePanel?.MarkDirtyRepaint(); }
            };
            // ObjectMoveツール用セクションとBoneEditorセクションを統合
            // ObjectMoveTRSSectionは廃止し、BoneEditorSectionを共用する
            _boneEditorSubPanel.Build(_layoutRoot.BoneEditorSection);

            _uvEditorSubPanel = new PlayerUVEditorSubPanel();
            _uvEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _uvEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _uvEditorSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            _uvEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _uvEditorSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _uvEditorSubPanel.Build(_layoutRoot.UVEditorSection);

            _uvUnwrapSubPanel = new PlayerUVUnwrapSubPanel();
            _uvUnwrapSubPanel.GetModel    = () => ActiveProject?.CurrentModel;
            _uvUnwrapSubPanel.SendCommand = cmd => _commandDispatcher?.Dispatch(cmd);
            _uvUnwrapSubPanel.OnRepaint   = () => _activePanel?.MarkDirtyRepaint();
            _uvUnwrapSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _uvUnwrapSubPanel.Build(_layoutRoot.UVUnwrapSection);

            _materialListSubPanel = new PlayerMaterialListSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
            _materialListSubPanel.SetCommandContext(
                _panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);
            _materialListSubPanel.Build(_layoutRoot.MaterialListSection);

            _uvzSubPanel = new PlayerUVZSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                SendCommand       = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex     = () => ActiveProject?.CurrentModelIndex ?? 0,
                GetCameraPosition = () => _viewportManager.GetCurrentToolContext(_activeViewport)?.CameraPosition ?? Vector3.zero,
                GetCameraForward  = () =>
                {
                    var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                    return ctx != null ? (ctx.CameraTarget - ctx.CameraPosition).normalized : Vector3.forward;
                },
            };
            _uvzSubPanel.Build(_layoutRoot.UVZSection);

            _partsSelSetSubPanel = new PlayerPartsSelectionSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _partsSelSetSubPanel.Build(_layoutRoot.PartsSelectionSetSection);

            _meshSelSetSubPanel = new PlayerMeshSelectionSetSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _meshSelSetSubPanel.Build(_layoutRoot.MeshSelectionSetSection);

            _mergeMeshesSubPanel = new PlayerMergeMeshesSubPanel
            {
                GetView     = () => _localLoader.Project ?? _receiver?.Project,
                SendCommand = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _mergeMeshesSubPanel.Build(_layoutRoot.MergeMeshesSection);

            _morphSubPanel = new PlayerMorphSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () =>
                {
                    var model = ActiveProject?.CurrentModel;
                    if (model == null) return null;
                    var ctx = new Poly_Ling.Tools.ToolContext();
                    ctx.Model          = model;
                    ctx.UndoController = _editOps?.UndoController;
                    ctx.SyncMeshContextPositionsOnly = mc =>
                    {
                        _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                        _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                        _viewportManager.UpdateTransform();
                        _activePanel?.MarkDirtyRepaint();
                    };
                    ctx.Repaint = () => _activePanel?.MarkDirtyRepaint();
                    return ctx;
                },
            };
            _morphSubPanel.Build(_layoutRoot.MorphSection);

            _morphCreateSubPanel = new PlayerMorphCreateSubPanel
            {
                GetProject          = () => ActiveProject,
                OnRebuildModelList  = RebuildModelList,
                GetUndoController   = () => _editOps?.UndoController,
                SendCommand         = cmd => _commandDispatcher?.Dispatch(cmd),
            };
            _morphCreateSubPanel.Build(_layoutRoot.MorphCreateSection);

            _tposeSubPanel = new PlayerTPoseSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _tposeSubPanel.Build(_layoutRoot.TPoseSection);

            _humanoidMappingSubPanel = new PlayerHumanoidMappingSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _humanoidMappingSubPanel.Build(_layoutRoot.HumanoidMappingSection);

            _mirrorSubPanel = new PlayerMirrorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _mirrorSubPanel.Build(_layoutRoot.MirrorSection);

            _quadDecimatorSubPanel = new PlayerQuadDecimatorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _quadDecimatorSubPanel.Build(_layoutRoot.QuadDecimatorSection);

            _alignVerticesHandler = new AlignVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
            };
            _alignVerticesHandler.SetProject(ActiveProject);
            _alignVerticesHandler.SetUndoController(_editOps?.UndoController);
            _alignVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _alignVerticesSubPanel = new PlayerAlignVerticesSubPanel
            {
                GetH = () => _alignVerticesHandler,
            };
            _alignVerticesSubPanel.Build(_layoutRoot.AlignVerticesSection);

            _planarizeAlongBonesHandler = new PlanarizeAlongBonesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
            };
            _planarizeAlongBonesHandler.SetProject(ActiveProject);
            _planarizeAlongBonesHandler.SetUndoController(_editOps?.UndoController);
            _planarizeAlongBonesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _planarizeAlongBonesSubPanel = new PlayerPlanarizeAlongBonesSubPanel
            {
                GetH = () => _planarizeAlongBonesHandler,
            };
            _planarizeAlongBonesSubPanel.Build(_layoutRoot.PlanarizeAlongBonesSection);

            _mergeVerticesHandler = new MergeVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
            };
            _mergeVerticesHandler.SetProject(ActiveProject);
            _mergeVerticesHandler.SetUndoController(_editOps?.UndoController);
            _mergeVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _mergeVerticesHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _mergeVerticesSubPanel = new PlayerMergeVerticesSubPanel
            {
                GetH = () => _mergeVerticesHandler,
            };
            _mergeVerticesSubPanel.Build(_layoutRoot.MergeVerticesSection);

            _splitVerticesHandler = new SplitVerticesToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
            };
            _splitVerticesHandler.SetProject(ActiveProject);
            _splitVerticesHandler.SetUndoController(_editOps?.UndoController);
            _splitVerticesHandler.SetCommandQueue(_editOps?.CommandQueue);
            _splitVerticesHandler.NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                };
            _splitVerticesSubPanel = new PlayerSplitVerticesSubPanel
            {
                GetH = () => _splitVerticesHandler,
            };
            _splitVerticesSubPanel.Build(_layoutRoot.SplitVerticesSection);

            _addFaceHandler = new AddFaceToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc =>
                {
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
                EnsureDrawableMesh = () =>
                {
                    // モデル・描画メッシュがなければ空のMeshContextを自動生成する
                    _localLoader.EnsureProject();
                    _moveToolHandler?.SetProject(ActiveProject);
                    _objectMoveHandler?.SetProject(ActiveProject);
                    var proj = ActiveProject;
                    if (proj == null) return false;
                    if (proj.CurrentModel == null && proj.ModelCount > 0)
                        proj.SelectModel(0);
                    var model = proj.CurrentModel;
                    if (model == null) return false;

                    // 描画可能メッシュが既にあればそのまま使う
                    if (model.FirstDrawableMeshContext != null) return true;

                    // 空のMeshContextを1つ作成してUNDO記録
                    var emptyMo = new Poly_Ling.Data.MeshObject("New Mesh");
                    var unityMesh = emptyMo.ToUnityMesh();
                    unityMesh.name      = "New Mesh";
                    unityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    var ctx = new Poly_Ling.Data.MeshContext
                    {
                        Name       = "New Mesh",
                        MeshObject = emptyMo,
                        UnityMesh  = unityMesh,
                        IsVisible  = true,
                        ParentModelContext = model,
                    };
                    var oldSelected = model.CaptureAllSelectedIndices();
                    int insertIndex = model.Add(ctx);
                    model.ComputeWorldMatrices();
                    model.SelectMeshContextExclusive(insertIndex);
                    model.SelectMesh(insertIndex);
                    var newSelected = model.CaptureAllSelectedIndices();

                    if (_editOps?.UndoController != null)
                    {
                        _editOps.UndoController.SetModelContext(model);
                        _editOps.UndoController.RecordMeshContextAdd(
                            ctx, insertIndex, oldSelected, newSelected);
                    }

                    _viewportManager.RebuildAdapter(0, model);
                    var firstMc = model.FirstDrawableMeshContext;
                    if (firstMc != null)
                    {
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _addFaceHandler?.SetProject(ActiveProject);
                    RebuildModelList();
                    NotifyPanels(ChangeKind.ListStructure);
                    return true;
                },
            };
            _addFaceHandler.SetProject(ActiveProject);
            _addFaceHandler.SetUndoController(_editOps?.UndoController);
            _addFaceSubPanel = new PlayerAddFaceSubPanel
            {
                GetH = () => _addFaceHandler,
            };
            _addFaceSubPanel.Build(_layoutRoot.AddFaceSection);
            _flipFaceHandler = new FlipFaceToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _flipFaceHandler.SetProject(ActiveProject);
            _flipFaceHandler.SetUndoController(_editOps?.UndoController);
            _flipFaceHandler.SetCommandQueue(_editOps?.CommandQueue);
            _flipFaceSubPanel = new PlayerFlipFaceSubPanel { GetH = () => _flipFaceHandler };
            _flipFaceSubPanel.Build(_layoutRoot.FlipFaceSection);
            _rotateHandler = new RotateToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
            };
            _rotateHandler.SetProject(ActiveProject);
            _rotateHandler.SetUndoController(_editOps?.UndoController);
            _rotateSubPanel = new PlayerRotateSubPanel { GetH = () => _rotateHandler };
            _rotateSubPanel.Build(_layoutRoot.RotateSection);
            _scaleHandler = new ScaleToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
            };
            _scaleHandler.SetProject(ActiveProject);
            _scaleHandler.SetUndoController(_editOps?.UndoController);
            _scaleSubPanel = new PlayerScaleSubPanel { GetH = () => _scaleHandler };
            _scaleSubPanel.Build(_layoutRoot.ScaleSection);
            _edgeBevelHandler = new EdgeBevelToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _edgeBevelHandler.SetProject(ActiveProject);
            _edgeBevelHandler.SetUndoController(_editOps?.UndoController);
            _edgeBevelHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeBevelSubPanel = new PlayerEdgeBevelSubPanel { GetH = () => _edgeBevelHandler };
            _edgeBevelSubPanel.Build(_layoutRoot.EdgeBevelSection);
            _edgeExtrudeHandler = new EdgeExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _edgeExtrudeHandler.SetProject(ActiveProject);
            _edgeExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _edgeExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeExtrudeSubPanel = new PlayerEdgeExtrudeSubPanel { GetH = () => _edgeExtrudeHandler };
            _edgeExtrudeSubPanel.Build(_layoutRoot.EdgeExtrudeSection);
            _faceExtrudeHandler = new FaceExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _faceExtrudeHandler.SetProject(ActiveProject);
            _faceExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _faceExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _faceExtrudeSubPanel = new PlayerFaceExtrudeSubPanel { GetH = () => _faceExtrudeHandler };
            _faceExtrudeSubPanel.Build(_layoutRoot.FaceExtrudeSection);
            _edgeTopologyHandler = new EdgeTopologyToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _edgeTopologyHandler.SetProject(ActiveProject);
            _edgeTopologyHandler.SetUndoController(_editOps?.UndoController);
            _edgeTopologyHandler.SetCommandQueue(_editOps?.CommandQueue);
            _edgeTopologySubPanel = new PlayerEdgeTopologySubPanel { GetH = () => _edgeTopologyHandler };
            _edgeTopologySubPanel.Build(_layoutRoot.EdgeTopologySection);
            _knifeHandler = new KnifeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _knifeHandler.SetProject(ActiveProject);
            _knifeHandler.SetUndoController(_editOps?.UndoController);
            _knifeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _knifeSubPanel = new PlayerKnifeSubPanel { GetH = () => _knifeHandler };
            _knifeSubPanel.Build(_layoutRoot.KnifeSection);

            _lineExtrudeHandler = new LineExtrudeToolHandler
            {
                GetToolContext      = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint           = () => _activePanel?.MarkDirtyRepaint(),
                OnSyncMeshPositions = mc => { _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel); },
                NotifyTopologyChanged = () =>
                {
                    var proj = ActiveProject;
                    if (proj?.CurrentModel == null) return;
                    _viewportManager.RebuildAdapter(0, proj.CurrentModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, proj.CurrentModel);
                    NotifyPanels(ChangeKind.ListStructure);
                },
            };
            _lineExtrudeHandler.SetProject(ActiveProject);
            _lineExtrudeHandler.SetUndoController(_editOps?.UndoController);
            _lineExtrudeHandler.SetCommandQueue(_editOps?.CommandQueue);
            _lineExtrudeSubPanel = new PlayerLineExtrudeSubPanel { GetH = () => _lineExtrudeHandler };
            _lineExtrudeSubPanel.Build(_layoutRoot.LineExtrudeSection);

            _mediaPipeSubPanel = new PlayerMediaPipeFaceDeformSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd),
                GetModel      = () => ActiveProject?.CurrentModel,
                GetModelIndex = () => ActiveProject?.CurrentModelIndex ?? 0,
            };
            _mediaPipeSubPanel.Build(_layoutRoot.MediaPipeSection);

            _vmdTestSubPanel = new PlayerVMDTestSubPanel
            {
                GetModel          = () => ActiveProject?.CurrentModel,
                GetToolContext    = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                GetUndoController = () => _editOps?.UndoController,
            };
            _vmdTestSubPanel.Build(_layoutRoot.VMDTestSection);

            _remoteServerSubPanel = new PlayerRemoteServerSubPanel
            {
                GetServer = () => _playerServer,
            };
            _remoteServerSubPanel.Build(_layoutRoot.RemoteServerSection);

            _vertexMoveSubPanel = new PlayerVertexMoveSubPanel
            {
                GetHandler = () => _moveToolHandler,
            };
            _vertexMoveSubPanel.Build(_layoutRoot.VertexMoveSection);

            _pivotSubPanel = new PlayerPivotSubPanel();
            _pivotSubPanel.Build(_layoutRoot.PivotSection);

            _sculptSubPanel = new PlayerSculptSubPanel
            {
                GetHandler = () => _sculptHandler,
            };
            _sculptSubPanel.Build(_layoutRoot.SculptSection);

            _advancedSelectSubPanel = new PlayerAdvancedSelectSubPanel
            {
                GetHandler = () => _advancedSelectHandler,
            };
            _advancedSelectSubPanel.Build(_layoutRoot.AdvancedSelectSection);

            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            _importSubPanel = new PlayerImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;

            _exportSubPanel = new PlayerExportSubPanel();
            _exportSubPanel.Build(_layoutRoot.ExportSection);
            _exportSubPanel.OnExportPmx = OnExportPmx;
            _exportSubPanel.OnExportMqo = OnExportMqo;

            _projectFileSubPanel = new PlayerProjectFileSubPanel();
            _projectFileSubPanel.Build(_layoutRoot.ProjectFileSection);
            _projectFileSubPanel.OnSave     = OnSaveProject;
            _projectFileSubPanel.OnLoad     = OnLoadProject;
            _projectFileSubPanel.OnSaveCsv  = OnSaveCsvProject;
            _projectFileSubPanel.OnLoadCsv  = OnLoadCsvProject;
            _projectFileSubPanel.OnMergeCsv = OnMergeCsvProject;

            _partialImportSubPanel = new PlayerPartialImportSubPanel();
            _partialImportSubPanel.Build(_layoutRoot.PartialImportSection);
            _partialImportSubPanel.OnImportDone = OnPartialImportDone;

            _partialExportSubPanel = new PlayerPartialExportSubPanel();
            _partialExportSubPanel.Build(_layoutRoot.PartialExportSection);

            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            _primitiveSubPanel.OnMeshCreated = (mo, name, pos, ign, mode) => OnPrimitiveMeshCreated(mo, name, pos, ign, mode);

            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;

            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;
            _mfToSkinnedSubPanel.SetContext(_panelContext, () => ActiveProject?.CurrentModelIndex ?? 0);

            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _layoutRoot.BlendBtn.clicked      += ShowBlendPanel;
            _layoutRoot.ModelBlendBtn.clicked += ShowModelBlendPanel;
            _layoutRoot.BoneEditorBtn.clicked  += ShowBoneEditorPanel;
            _layoutRoot.UVEditorBtn.clicked    += ShowUVEditorPanel;
            _layoutRoot.UVUnwrapBtn.clicked    += ShowUVUnwrapPanel;
            _layoutRoot.MaterialListBtn.clicked    += ShowMaterialListPanel;
            _layoutRoot.UVZBtn.clicked             += ShowUVZPanel;
            _layoutRoot.PartsSelectionSetBtn.clicked += ShowPartsSelectionSetPanel;
            _layoutRoot.MeshSelectionSetBtn.clicked  += ShowMeshSelectionSetPanel;
            _layoutRoot.MergeMeshesBtn.clicked     += ShowMergeMeshesPanel;
            _layoutRoot.TPoseBtn.clicked           += ShowTPosePanel;
            _layoutRoot.HumanoidMappingBtn.clicked += ShowHumanoidMappingPanel;
            _layoutRoot.MirrorBtn.clicked          += ShowMirrorPanel;
            _layoutRoot.QuadDecimatorBtn.clicked   += ShowQuadDecimatorPanel;
            _layoutRoot.AlignVerticesBtn.clicked       += ShowAlignVerticesPanel;
            _layoutRoot.PlanarizeAlongBonesBtn.clicked += ShowPlanarizeAlongBonesPanel;
            _layoutRoot.MergeVerticesBtn.clicked       += ShowMergeVerticesPanel;
            _layoutRoot.SplitVerticesBtn.clicked        += ShowSplitVerticesPanel;
            _layoutRoot.AddFaceBtn.clicked               += ShowAddFacePanel;
            _layoutRoot.FlipFaceBtn.clicked              += ShowFlipFacePanel;
            _layoutRoot.RotateBtn.clicked                += ShowRotatePanel;
            _layoutRoot.ScaleBtn.clicked                 += ShowScalePanel;
            _layoutRoot.EdgeBevelBtn.clicked             += ShowEdgeBevelPanel;
            _layoutRoot.EdgeExtrudeBtn.clicked           += ShowEdgeExtrudePanel;
            _layoutRoot.FaceExtrudeBtn.clicked           += ShowFaceExtrudePanel;
            _layoutRoot.EdgeTopologyBtn.clicked          += ShowEdgeTopologyPanel;
            _layoutRoot.KnifeBtn.clicked                 += ShowKnifePanel;
            _layoutRoot.LineExtrudeBtn.clicked           += ShowLineExtrudePanel;
            _layoutRoot.MediaPipeBtn.clicked        += ShowMediaPipePanel;
            _layoutRoot.VMDTestBtn.clicked          += ShowVMDTestPanel;
            _layoutRoot.RemoteServerBtn.clicked     += ShowRemoteServerPanel;
            _layoutRoot.FullExportMqoBtn.clicked    += () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO);
            _layoutRoot.ProjectFileBtn.clicked      += ShowProjectFilePanel;
            _layoutRoot.PartialImportPmxBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX);
            _layoutRoot.PartialImportMqoBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO);
            _layoutRoot.PartialExportPmxBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX);
            _layoutRoot.PartialExportMqoBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO);

            _layoutRoot.ToolVertexMoveBtn.clicked        += () => SwitchTool(ToolMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked        += () => SwitchTool(ToolMode.ObjectMove);
            _layoutRoot.ToolPivotOffsetBtn.clicked       += () => SwitchTool(ToolMode.PivotOffset);
            _layoutRoot.ToolSculptBtn.clicked            += () => SwitchTool(ToolMode.Sculpt);
            _layoutRoot.ToolAdvancedSelBtn.clicked       += () => SwitchTool(ToolMode.AdvancedSelect);
            _layoutRoot.ToolSkinWeightPaintBtn.clicked   += () => SwitchTool(ToolMode.SkinWeightPaint);

            _layoutRoot.LassoToggle.RegisterValueChangedCallback(e =>
            {
                if (_moveToolHandler != null)
                    _moveToolHandler.DragSelectMode = e.newValue
                        ? MoveToolHandler.SelectionDragMode.Lasso
                        : MoveToolHandler.SelectionDragMode.Box;
            });

            _layoutRoot.ModelListBtn.clicked += ShowModelListPanel;
            _layoutRoot.MeshListBtn .clicked += ShowMeshListPanel;

            _layoutRoot.ModelSelectDropdown.RegisterValueChangedCallback(e =>
            {
                var project = ActiveProject;
                if (project == null) return;
                var choices = _layoutRoot.ModelSelectDropdown.choices;
                int idx = choices != null ? choices.IndexOf(e.newValue) : -1;
                if (idx < 0 || idx == project.CurrentModelIndex) return;
                SwitchActiveModel(idx);
            });

            _localLoader.OnPmxRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.PMX);
            _localLoader.OnMqoRequested = () => ShowImportPanel(PlayerImportSubPanel.Mode.MQO);

            _layoutRoot.ConnectBtn   .clicked += () => _client?.Connect();
            _layoutRoot.DisconnectBtn.clicked += () => _client?.Disconnect();
            _layoutRoot.FetchBtn     .clicked += FetchProject;
            _layoutRoot.UndoBtn      .clicked += () => _editOps?.PerformUndo();
            _layoutRoot.RedoBtn      .clicked += () => _editOps?.PerformRedo();

            _layoutRoot.PerspectivePanel.SetViewport(_viewportManager.PerspectiveViewport);
            _layoutRoot.TopPanel        .SetViewport(_viewportManager.TopViewport);
            _layoutRoot.FrontPanel      .SetViewport(_viewportManager.FrontViewport);
            _layoutRoot.SidePanel       .SetViewport(_viewportManager.SideViewport);

            // 面ごとの表示設定トグルを接続する。
            // ViewportDisplayToggles[slot, item] → _viewportManager の設定を更新。
            for (int s = 0; s < 4; s++)
            {
                for (int i = 0; i < PlayerLayoutRoot.VD_COUNT; i++)
                {
                    int slot = s, item = i;
                    _layoutRoot.ViewportDisplayToggles[slot, item]
                        .RegisterValueChangedCallback(e =>
                        {
                            var ds = _viewportManager.GetDisplaySettings(slot);
                            switch (item)
                            {
                                case PlayerLayoutRoot.VD_CULLING:    ds.BackfaceCulling         = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_MESH:   ds.ShowSelectedMesh        = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_WIRE:   ds.ShowSelectedWireframe   = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_VERT:   ds.ShowSelectedVertices    = e.newValue; break;
                                case PlayerLayoutRoot.VD_SEL_BONE:   ds.ShowSelectedBone        = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_MESH: ds.ShowUnselectedMesh      = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_WIRE: ds.ShowUnselectedWireframe = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_VERT: ds.ShowUnselectedVertices  = e.newValue; break;
                                case PlayerLayoutRoot.VD_UNSEL_BONE: ds.ShowUnselectedBone      = e.newValue; break;
                            }
                            _viewportManager.SetDisplaySettings(slot, ds);
                        });
                }
            }

            _layoutRoot.MorphBtn.clicked       += ShowMorphPanel;
            _layoutRoot.MorphCreateBtn.clicked += ShowMorphCreatePanel;

            _layoutRoot.PostBuildButtonColors(_uiRoot);

            _sectionRefreshPairs.Clear();
            _sectionRefreshPairs.Add((_layoutRoot.BoneEditorSection,        () => _boneEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVEditorSection,          () => _uvEditorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVUnwrapSection,          () => _uvUnwrapSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MaterialListSection,      () => _materialListSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.UVZSection,               () => _uvzSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PartsSelectionSetSection, () => _partsSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MeshSelectionSetSection,  () => _meshSelSetSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeMeshesSection,       () => _mergeMeshesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphSection,             () => _morphSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MorphCreateSection,       () => _morphCreateSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.TPoseSection,             () => _tposeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.HumanoidMappingSection,   () => _humanoidMappingSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.QuadDecimatorSection,         () => _quadDecimatorSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.AlignVerticesSection,         () => _alignVerticesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.PlanarizeAlongBonesSection,   () => _planarizeAlongBonesSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.MergeVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
                _mergeVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.SplitVerticesSection, () =>
            {
                var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
                if (ctx != null) _splitVerticesHandler?.Activate(ctx);
                _splitVerticesSubPanel?.Refresh();
            }));
            _sectionRefreshPairs.Add((_layoutRoot.AddFaceSection,           () => _addFaceSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FlipFaceSection,          () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _flipFaceHandler?.Activate(ctx); _flipFaceSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.RotateSection,            () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _rotateHandler?.Activate(ctx); _rotateSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.ScaleSection,             () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _scaleHandler?.Activate(ctx); _scaleSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeBevelSection,         () => _edgeBevelSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeExtrudeSection,       () => _edgeExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.FaceExtrudeSection,       () => _faceExtrudeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.EdgeTopologySection,      () => _edgeTopologySubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.KnifeSection,             () => _knifeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.LineExtrudeSection,       () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); if (ctx != null) _lineExtrudeHandler?.Activate(ctx); _lineExtrudeSubPanel?.Refresh(); }));
            _sectionRefreshPairs.Add((_layoutRoot.MediaPipeSection,         () => _mediaPipeSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.VMDTestSection,           () => _vmdTestSubPanel?.Refresh()));
            _sectionRefreshPairs.Add((_layoutRoot.RemoteServerSection,      () => _remoteServerSubPanel?.Refresh()));

            SwitchTool(ToolMode.VertexMove);
        }

        // ================================================================
        // パネル表示切替
        // ================================================================

        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            SetActiveButton(null);
            if (_layoutRoot?.ImportSection != null)
                _layoutRoot.ImportSection.style.display = DisplayStyle.Flex;
            _importSubPanel?.SetMode(mode);
        }

        private void ShowPrimitivePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.PrimitiveBtn);
            if (_layoutRoot?.PrimitiveSection != null)
                _layoutRoot.PrimitiveSection.style.display = DisplayStyle.Flex;
        }

        private void ShowMeshFilterToSkinnedPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MeshFilterToSkinnedBtn);
            if (_layoutRoot?.MeshFilterToSkinnedSection != null)
                _layoutRoot.MeshFilterToSkinnedSection.style.display = DisplayStyle.Flex;
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowBlendPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.BlendBtn);
            if (_layoutRoot?.BlendSection != null)
                _layoutRoot.BlendSection.style.display = DisplayStyle.Flex;
            _blendSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        private void ShowModelBlendPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.ModelBlendBtn);
            if (_layoutRoot?.ModelBlendSection != null)
                _layoutRoot.ModelBlendSection.style.display = DisplayStyle.Flex;
            _modelBlendSubPanel?.Init();
        }

        private void ShowBoneEditorPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.BoneEditorBtn);
            if (_layoutRoot?.BoneEditorSection != null)
                _layoutRoot.BoneEditorSection.style.display = DisplayStyle.Flex;
            _boneEditorSubPanel?.Refresh();
        }

        private void ShowUVEditorPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.UVEditorBtn);
            if (_layoutRoot?.UVEditorSection != null)
                _layoutRoot.UVEditorSection.style.display = DisplayStyle.Flex;

            // UndoController に対象メッシュを設定（CaptureMeshObjectSnapshot に必要）
            var uvModel = ActiveProject?.CurrentModel;
            var uvMc    = uvModel?.FirstDrawableMeshContext;
            if (uvMc?.MeshObject != null && _editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = uvModel;
                _uvUndoMasterIndex = uvModel.IndexOf(uvMc);
            }

            _uvEditorSubPanel?.Refresh();
        }

        private void ShowUVUnwrapPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.UVUnwrapBtn);
            if (_layoutRoot?.UVUnwrapSection != null)
                _layoutRoot.UVUnwrapSection.style.display = DisplayStyle.Flex;
            _uvUnwrapSubPanel?.Refresh();
        }

        private void ShowMaterialListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MaterialListBtn);
            if (_layoutRoot?.MaterialListSection != null)
                _layoutRoot.MaterialListSection.style.display = DisplayStyle.Flex;
            _materialListSubPanel?.Refresh();
        }

        private void ShowUVZPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.UVZBtn);
            if (_layoutRoot?.UVZSection != null)
                _layoutRoot.UVZSection.style.display = DisplayStyle.Flex;
            _uvzSubPanel?.Refresh();
        }

        private void ShowPartsSelectionSetPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.PartsSelectionSetBtn);
            if (_layoutRoot?.PartsSelectionSetSection != null)
                _layoutRoot.PartsSelectionSetSection.style.display = DisplayStyle.Flex;
            _partsSelSetSubPanel?.Refresh();
        }

        private void ShowMeshSelectionSetPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MeshSelectionSetBtn);
            if (_layoutRoot?.MeshSelectionSetSection != null)
                _layoutRoot.MeshSelectionSetSection.style.display = DisplayStyle.Flex;
            _meshSelSetSubPanel?.Refresh();
        }

        private void ShowMergeMeshesPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MergeMeshesBtn);
            if (_layoutRoot?.MergeMeshesSection != null)
                _layoutRoot.MergeMeshesSection.style.display = DisplayStyle.Flex;
            _mergeMeshesSubPanel?.Refresh();
        }

        private void ShowMorphPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MorphBtn);
            if (_layoutRoot?.MorphSection != null)
                _layoutRoot.MorphSection.style.display = DisplayStyle.Flex;

            // MeshListStack のコンテキストを現在のモデルに設定
            // （MorphExpressionEditRecord/ChangeRecord が正しいモデルを参照するために必要）
            var morphModel = ActiveProject?.CurrentModel;
            if (morphModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphModel);

            _morphSubPanel?.Refresh();
        }

        private void ShowMorphCreatePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MorphCreateBtn);
            if (_layoutRoot?.MorphCreateSection != null)
                _layoutRoot.MorphCreateSection.style.display = DisplayStyle.Flex;

            // MeshListStack のコンテキストを現在のモデルに設定
            var morphCrModel = ActiveProject?.CurrentModel;
            if (morphCrModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(morphCrModel);

            _morphCreateSubPanel?.Refresh();
        }

        private void ShowTPosePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.TPoseBtn);
            if (_layoutRoot?.TPoseSection != null)
                _layoutRoot.TPoseSection.style.display = DisplayStyle.Flex;
            // MeshListStack のコンテキストを現在のモデルに設定（TPoseUndoRecord が参照するため）
            var tpModel = ActiveProject?.CurrentModel;
            if (tpModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(tpModel);
            _tposeSubPanel?.Refresh();
        }

        private void ShowHumanoidMappingPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.HumanoidMappingBtn);
            if (_layoutRoot?.HumanoidMappingSection != null)
                _layoutRoot.HumanoidMappingSection.style.display = DisplayStyle.Flex;
            var hmModel = ActiveProject?.CurrentModel;
            if (hmModel != null && _editOps?.UndoController != null)
                _editOps.UndoController.SetModelContext(hmModel);
            _humanoidMappingSubPanel?.Refresh();
        }

        private void ShowMirrorPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.MirrorBtn);
            if (_layoutRoot?.MirrorSection != null)
                _layoutRoot.MirrorSection.style.display = DisplayStyle.Flex;
        }

        private void ShowQuadDecimatorPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.QuadDecimatorBtn);
            if (_layoutRoot?.QuadDecimatorSection != null)
                _layoutRoot.QuadDecimatorSection.style.display = DisplayStyle.Flex;
            _quadDecimatorSubPanel?.Refresh();
        }

        private void ShowAlignVerticesPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.AlignVerticesBtn);
            if (_layoutRoot?.AlignVerticesSection != null)
                _layoutRoot.AlignVerticesSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _alignVerticesHandler?.Activate(ctx);
            _alignVerticesSubPanel?.Refresh();
        }

        private void ShowPlanarizeAlongBonesPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.PlanarizeAlongBonesBtn);
            if (_layoutRoot?.PlanarizeAlongBonesSection != null)
                _layoutRoot.PlanarizeAlongBonesSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _planarizeAlongBonesHandler?.Activate(ctx);
            _planarizeAlongBonesSubPanel?.Refresh();
        }

        private void ShowMergeVerticesPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.MergeVerticesBtn);
            if (_layoutRoot?.MergeVerticesSection != null)
                _layoutRoot.MergeVerticesSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null)
            {
                _mergeVerticesHandler?.Activate(ctx);
                _mergeVerticesHandler?.UpdateHover(Vector2.zero, ctx);
            }
            _mergeVerticesSubPanel?.Refresh();
        }


        private void ShowFlipFacePanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.FlipFaceBtn);
            if (_layoutRoot?.FlipFaceSection != null)
                _layoutRoot.FlipFaceSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _flipFaceHandler?.Activate(ctx);
            _flipFaceSubPanel?.Refresh();
        }

        private void ShowRotatePanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.RotateBtn);
            if (_layoutRoot?.RotateSection != null)
                _layoutRoot.RotateSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _rotateHandler?.Activate(ctx);
            _rotateSubPanel?.Refresh();
        }

        private void ShowScalePanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.ScaleBtn);
            if (_layoutRoot?.ScaleSection != null)
                _layoutRoot.ScaleSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _scaleHandler?.Activate(ctx);
            _scaleSubPanel?.Refresh();
        }

        private void ShowEdgeBevelPanel()
        {
            SwitchTool(ToolMode.EdgeBevel);
            if (_layoutRoot?.EdgeBevelSection != null)
                _layoutRoot.EdgeBevelSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeBevelHandler?.Activate(ctx);
            _edgeBevelSubPanel?.Refresh();
        }

        private void ShowEdgeExtrudePanel()
        {
            SwitchTool(ToolMode.EdgeExtrude);
            if (_layoutRoot?.EdgeExtrudeSection != null)
                _layoutRoot.EdgeExtrudeSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeExtrudeHandler?.Activate(ctx);
            _edgeExtrudeSubPanel?.Refresh();
        }

        private void ShowFaceExtrudePanel()
        {
            SwitchTool(ToolMode.FaceExtrude);
            if (_layoutRoot?.FaceExtrudeSection != null)
                _layoutRoot.FaceExtrudeSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _faceExtrudeHandler?.Activate(ctx);
            _faceExtrudeSubPanel?.Refresh();
        }

        private void ShowEdgeTopologyPanel()
        {
            SwitchTool(ToolMode.EdgeTopology);
            if (_layoutRoot?.EdgeTopologySection != null)
                _layoutRoot.EdgeTopologySection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _edgeTopologyHandler?.Activate(ctx);
            _edgeTopologySubPanel?.Refresh();
        }

        private void ShowLineExtrudePanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.LineExtrudeBtn);
            if (_layoutRoot?.LineExtrudeSection != null)
                _layoutRoot.LineExtrudeSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _lineExtrudeHandler?.Activate(ctx);
            _lineExtrudeSubPanel?.Refresh();
        }

        private void ShowKnifePanel()
        {
            SwitchTool(ToolMode.Knife);
            if (_layoutRoot?.KnifeSection != null)
                _layoutRoot.KnifeSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _knifeHandler?.Activate(ctx);
            _knifeSubPanel?.Refresh();
        }
        private void ShowAddFacePanel()
        {
            SwitchTool(ToolMode.AddFace);
            if (_layoutRoot?.AddFaceSection != null)
                _layoutRoot.AddFaceSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _addFaceHandler?.Activate(ctx);
            // 面追加時は頂点ホバーのみ必要。辺・面のホバーは有害なので抑制する。
            var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
            if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.Vertex;
            _addFaceSubPanel?.Refresh();
        }

        private void ShowSplitVerticesPanel()
        {
            HideAllRightPanels();
            _viewportManager?.RegisterActiveToolHandler(null);
            SetActiveButton(_layoutRoot?.SplitVerticesBtn);
            if (_layoutRoot?.SplitVerticesSection != null)
                _layoutRoot.SplitVerticesSection.style.display = DisplayStyle.Flex;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx != null) _splitVerticesHandler?.Activate(ctx);
            _splitVerticesSubPanel?.Refresh();
        }

        private void ShowMediaPipePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MediaPipeBtn);
            if (_layoutRoot?.MediaPipeSection != null)
                _layoutRoot.MediaPipeSection.style.display = DisplayStyle.Flex;
            _mediaPipeSubPanel?.Refresh();
        }

        private void ShowVMDTestPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.VMDTestBtn);
            if (_layoutRoot?.VMDTestSection != null)
                _layoutRoot.VMDTestSection.style.display = DisplayStyle.Flex;
            _vmdTestSubPanel?.Refresh();
        }

        private void ShowRemoteServerPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.RemoteServerBtn);
            if (_layoutRoot?.RemoteServerSection != null)
                _layoutRoot.RemoteServerSection.style.display = DisplayStyle.Flex;
            _remoteServerSubPanel?.Refresh();
        }

        private void ShowExportPanel(PlayerExportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            SetActiveButton(mode == PlayerExportSubPanel.Mode.PMX
                ? _layoutRoot?.FullExportPmxBtn
                : _layoutRoot?.FullExportMqoBtn);
            if (_layoutRoot?.ExportSection != null)
                _layoutRoot.ExportSection.style.display = DisplayStyle.Flex;
            _exportSubPanel?.SetMode(mode);
        }

        private void ShowProjectFilePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.ProjectFileBtn);
            if (_layoutRoot?.ProjectFileSection != null)
                _layoutRoot.ProjectFileSection.style.display = DisplayStyle.Flex;
        }

        private void ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            SetActiveButton(mode == PlayerPartialImportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialImportPmxBtn
                : _layoutRoot?.PartialImportMqoBtn);
            if (_layoutRoot?.PartialImportSection != null)
                _layoutRoot.PartialImportSection.style.display = DisplayStyle.Flex;
            var model = ActiveProject?.CurrentModel;
            if (model != null) _editOps?.UndoController.SetModelContext(model);
            _partialImportSubPanel?.SetModel(model, _editOps?.UndoController);
            _partialImportSubPanel?.SetMode(mode);
        }

        private void ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            SetActiveButton(mode == PlayerPartialExportSubPanel.Mode.PMX
                ? _layoutRoot?.PartialExportPmxBtn
                : _layoutRoot?.PartialExportMqoBtn);
            if (_layoutRoot?.PartialExportSection != null)
                _layoutRoot.PartialExportSection.style.display = DisplayStyle.Flex;
            var model = ActiveProject?.CurrentModel;
            _partialExportSubPanel?.SetModel(model);
            _partialExportSubPanel?.SetMode(mode);
        }

        private void ShowModelListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.ModelListBtn);
            if (_layoutRoot?.ModelListSection != null)
                _layoutRoot.ModelListSection.style.display = DisplayStyle.Flex;
        }

        private void ShowMeshListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MeshListBtn);
            if (_layoutRoot?.MeshListSection != null)
                _layoutRoot.MeshListSection.style.display = DisplayStyle.Flex;
        }

        private void HideAllRightPanels()
        {
            if (_layoutRoot == null) return;
            void Hide(VisualElement e) { if (e != null) e.style.display = DisplayStyle.None; }
            Hide(_layoutRoot.ModelListSection);
            Hide(_layoutRoot.MeshListSection);
            Hide(_layoutRoot.SkinWeightPaintSection);
            Hide(_layoutRoot.VertexMoveSection);
            Hide(_layoutRoot.PivotSection);
            Hide(_layoutRoot.SculptSection);
            Hide(_layoutRoot.AdvancedSelectSection);
            Hide(_layoutRoot.ImportSection);
            Hide(_layoutRoot.ExportSection);
            Hide(_layoutRoot.ProjectFileSection);
            Hide(_layoutRoot.PartialImportSection);
            Hide(_layoutRoot.PartialExportSection);
            Hide(_layoutRoot.PrimitiveSection);
            Hide(_layoutRoot.MeshFilterToSkinnedSection);
            Hide(_layoutRoot.BlendSection);
            Hide(_layoutRoot.ModelBlendSection);
            Hide(_layoutRoot.BoneEditorSection);
            Hide(_layoutRoot.UVEditorSection);
            Hide(_layoutRoot.UVUnwrapSection);
            Hide(_layoutRoot.MaterialListSection);
            Hide(_layoutRoot.UVZSection);
            Hide(_layoutRoot.PartsSelectionSetSection);
            Hide(_layoutRoot.MeshSelectionSetSection);
            Hide(_layoutRoot.MergeMeshesSection);
            Hide(_layoutRoot.MorphSection);
            Hide(_layoutRoot.MorphCreateSection);
            Hide(_layoutRoot.TPoseSection);
            Hide(_layoutRoot.HumanoidMappingSection);
            Hide(_layoutRoot.MirrorSection);
            Hide(_layoutRoot.QuadDecimatorSection);
            Hide(_layoutRoot.AlignVerticesSection);
            Hide(_layoutRoot.PlanarizeAlongBonesSection);
            Hide(_layoutRoot.MergeVerticesSection);
            Hide(_layoutRoot.SplitVerticesSection);
            Hide(_layoutRoot.AddFaceSection);
            Hide(_layoutRoot.FlipFaceSection);
            Hide(_layoutRoot.RotateSection);
            Hide(_layoutRoot.ScaleSection);
            Hide(_layoutRoot.EdgeBevelSection);
            Hide(_layoutRoot.EdgeExtrudeSection);
            Hide(_layoutRoot.FaceExtrudeSection);
            Hide(_layoutRoot.EdgeTopologySection);
            Hide(_layoutRoot.KnifeSection);
            Hide(_layoutRoot.LineExtrudeSection);
            Hide(_layoutRoot.MediaPipeSection);
            Hide(_layoutRoot.VMDTestSection);
            Hide(_layoutRoot.RemoteServerSection);
        }

        // ================================================================
        // ボタンアクティブ色
        // ================================================================

        private static readonly StyleColor ActiveBtnColor   = new StyleColor(new Color(0.3f, 0.5f, 1f));
        private static readonly StyleColor InactiveBtnColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));

        private void SetActiveButton(Button btn)
        {
            if (_activeBtn != null)
                _activeBtn.style.backgroundColor = InactiveBtnColor;
            _activeBtn = btn;
            if (_activeBtn != null)
                _activeBtn.style.backgroundColor = ActiveBtnColor;
        }

        // ================================================================
        // コールバック / イベントハンドラ
        // ================================================================

        private void OnPrimitiveMeshCreated(
            MeshObject meshObject, string meshName, Vector3 worldPos,
            bool ignorePoseInArmature, PrimitiveAddMode addMode)
        {
            _localLoader.EnsureProject();
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _boneInputHandler?.SetProject(ActiveProject);
            _alignVerticesHandler?.SetProject(ActiveProject);
            _planarizeAlongBonesHandler?.SetProject(ActiveProject);
            _mergeVerticesHandler?.SetProject(ActiveProject);
            _splitVerticesHandler?.SetProject(ActiveProject);
            _addFaceHandler?.SetProject(ActiveProject);
            _flipFaceHandler?.SetProject(ActiveProject);
            _rotateHandler?.SetProject(ActiveProject);
            _scaleHandler?.SetProject(ActiveProject);
            _edgeBevelHandler?.SetProject(ActiveProject);
            _edgeExtrudeHandler?.SetProject(ActiveProject);
            _faceExtrudeHandler?.SetProject(ActiveProject);
            _edgeTopologyHandler?.SetProject(ActiveProject);
            _knifeHandler?.SetProject(ActiveProject);
            _lineExtrudeHandler?.SetProject(ActiveProject);

            var project = ActiveProject;
            if (project == null) return;
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);

            switch (addMode)
            {
                case PrimitiveAddMode.NewObject:
                    PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
                case PrimitiveAddMode.AddToExisting:
                    PrimitiveMeshAddToExisting(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
                case PrimitiveAddMode.NewModel:
                    PrimitiveMeshCreateNewModel(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                    break;
            }
        }

        /// <summary>
        /// 図形生成共通: MeshContextを作って返す。
        /// </summary>
        private MeshContext BuildPrimitiveMeshContext(
            MeshObject meshObject, string meshName, Vector3 worldPos, bool ignorePoseInArmature)
        {
            var unityMesh = meshObject.ToUnityMesh();
            unityMesh.name      = meshName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            var ctx = new MeshContext
            {
                Name      = meshName,
                MeshObject = meshObject,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };

            if (ctx.BoneTransform != null && worldPos != Vector3.zero)
            {
                ctx.BoneTransform.UseLocalTransform = true;
                ctx.BoneTransform.Position = worldPos;
            }

            ctx.IgnorePoseInArmature = ignorePoseInArmature;
            return ctx;
        }

        /// <summary>
        /// モード1: 新しい描画オブジェクトとして現在のモデルに追加。UNDO対応。
        /// </summary>
        private void PrimitiveMeshCreateNewObject(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var model = project.CurrentModel;
            if (model == null) return;

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos, ignorePoseInArmature);
            ctx.ParentModelContext = model;

            var oldSelected = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(ctx);
            model.ComputeWorldMatrices();
            model.SelectMeshContextExclusive(insertIndex);
            model.SelectMesh(insertIndex);
            var newSelected = model.CaptureAllSelectedIndices();

            // UNDO記録
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// モード2: 既存の選択中描画オブジェクトに頂点・面をマージ。UNDO対応。
        /// 描画オブジェクトが存在しない場合はモード1にフォールバック。
        /// </summary>
        private void PrimitiveMeshAddToExisting(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var model  = project.CurrentModel;
            if (model == null) return;

            // 対象MeshContextを選択（なければ新規作成にフォールバック）
            var targetMc = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
            if (targetMc == null || targetMc.MeshObject == null)
            {
                PrimitiveMeshCreateNewObject(project, meshObject, meshName, worldPos, ignorePoseInArmature);
                return;
            }

            // ワールド位置オフセットを頂点に適用
            var srcObject = meshObject;
            if (worldPos != Vector3.zero)
            {
                srcObject = meshObject.Clone();
                foreach (var v in srcObject.Vertices)
                    v.Position += worldPos;
            }

            // UNDO: 変更前スナップショット
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            // マージ
            int baseVertIdx = targetMc.MeshObject.VertexCount;
            foreach (var v in srcObject.Vertices)
                targetMc.MeshObject.Vertices.Add(v.Clone());
            foreach (var f in srcObject.Faces)
            {
                var newFace = new Face();
                newFace.VertexIndices  = f.VertexIndices.ConvertAll(i => i + baseVertIdx);
                newFace.UVIndices      = new System.Collections.Generic.List<int>(f.UVIndices);
                newFace.NormalIndices  = new System.Collections.Generic.List<int>(f.NormalIndices);
                newFace.MaterialIndex  = f.MaterialIndex;
                targetMc.MeshObject.Faces.Add(newFace);
            }
            // UnityMesh再構築
            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            if (targetMc.UnityMesh != null)
                UnityEngine.Object.Destroy(targetMc.UnityMesh);
            targetMc.UnityMesh = newUnityMesh;

            // UNDO: 変更後スナップショット記録
            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(before, after, $"Add Primitive to {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
        }

        /// <summary>
        /// モード3: 新しいモデルを作って描画オブジェクトを追加。UNDO対応（メッシュ追加のみ）。
        /// </summary>
        private void PrimitiveMeshCreateNewModel(
            ProjectContext project, MeshObject meshObject, string meshName,
            Vector3 worldPos, bool ignorePoseInArmature)
        {
            var newModel = project.CreateNewModel(meshName);
            if (newModel == null) return;

            var ctx = BuildPrimitiveMeshContext(meshObject, meshName, worldPos, ignorePoseInArmature);
            ctx.ParentModelContext = newModel;

            var oldSelected = newModel.CaptureAllSelectedIndices();
            int insertIndex = newModel.Add(ctx);
            newModel.ComputeWorldMatrices();
            newModel.SelectMeshContextExclusive(insertIndex);
            newModel.SelectMesh(insertIndex);
            var newSelected = newModel.CaptureAllSelectedIndices();

            // UNDO記録（新モデル上のメッシュ追加）
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(newModel);
                _editOps.UndoController.RecordMeshContextAdd(
                    ctx, insertIndex, oldSelected, newSelected);
            }

            // ハンドラーを新モデルに切り替え
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);

            PrimitiveMeshFinalize(newModel);
            RebuildModelList();
        }

        /// <summary>
        /// 図形生成後の共通ビュー更新処理。
        /// </summary>
        private void PrimitiveMeshFinalize(ModelContext model)
        {
            _viewportManager.RebuildAdapter(0, model);

            var firstMc = model.FirstDrawableMeshContext;
            if (firstMc != null)
            {
                _selectionOps?.SetSelectionState(firstMc.Selection);
                _renderer?.SetSelectionState(firstMc.Selection);
            }
            _renderer?.UpdateSelectedDrawableMesh(0, model);
            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);

            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
        }

        private void OnImportPmx(string filePath, PMXImportSettings settings)
        {
            var cmd = new ImportPmxCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"PMX読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnImportMqo(string filePath, MQOImportSettings settings)
        {
            var cmd = new ImportMqoCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"MQO読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        private void OnExportPmx(string outputPath, PMXExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = PMXExporter.Export(model, outputPath, settings);
                if (result.Success)
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
        }

        private void OnExportMqo(string outputPath, MQOExportSettings settings)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _exportSubPanel?.SetStatus("モデルがありません"); return; }
            try
            {
                var result = MQOExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                else
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
            }
            catch (Exception ex) { _exportSubPanel?.SetStatus($"例外: {ex.Message}"); }
        }

        private void OnSaveProject()
        {
            var project = ActiveProject;
            if (project == null) { _projectFileSubPanel?.SetStatus("プロジェクトがありません"); return; }
            var dto = ProjectSerializer.FromProjectContext(project);
            if (dto == null) { _projectFileSubPanel?.SetStatus("シリアライズ失敗"); return; }
            bool ok = ProjectSerializer.ExportWithDialog(dto, project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "保存完了" : "キャンセルまたは失敗");
        }

        private void OnLoadProject()
        {
            var dto = ProjectSerializer.ImportWithDialog();
            if (dto == null) { _projectFileSubPanel?.SetStatus("キャンセルまたは失敗"); return; }
            var loadedProject = ProjectSerializer.ToProjectContext(dto);
            if (loadedProject == null) { _projectFileSubPanel?.SetStatus("復元失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? dto.name, m);
            _projectFileSubPanel?.SetStatus($"読込完了: {dto.name}");
        }

        private void OnSaveCsvProject()
        {
            var project = ActiveProject;
            if (project == null) { _projectFileSubPanel?.SetStatus("プロジェクトがありません"); return; }
            bool ok = CsvProjectSerializer.ExportWithDialog(project, defaultName: project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "CSVフォルダ保存完了" : "キャンセルまたは失敗");
        }

        private void OnLoadCsvProject()
        {
            var loadedProject = CsvProjectSerializer.ImportWithDialog(out _, out _);
            if (loadedProject == null) { _projectFileSubPanel?.SetStatus("キャンセルまたは失敗"); return; }
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? loadedProject.Name, m);
            _projectFileSubPanel?.SetStatus($"CSV読込完了: {loadedProject.Name}");
        }

        private void OnMergeCsvProject()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { _projectFileSubPanel?.SetStatus("モデルがありません"); return; }

            string folderPath = PLEditorBridge.I.OpenFolderPanel("Add from CSV Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(folderPath)) { _projectFileSubPanel?.SetStatus("キャンセル"); return; }

            var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
            if (entries == null || entries.Count == 0) { _projectFileSubPanel?.SetStatus("読み込めるデータがありません"); return; }

            int added = 0, replaced = 0;
            var existingNames = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var mc = model.MeshContextList[i];
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    existingNames[mc.Name] = i;
            }
            foreach (var entry in entries)
            {
                if (entry.MeshContext == null) continue;
                string name = entry.MeshContext.Name ?? "";
                entry.MeshContext.ParentModelContext = model;
                if (existingNames.TryGetValue(name, out int existIdx))
                { model.MeshContextList[existIdx] = entry.MeshContext; replaced++; }
                else
                { model.Add(entry.MeshContext); added++; }
            }
            bool hasNameBased = false;
            foreach (var e in entries) { if (e.IsNameBased) { hasNameBased = true; break; } }
            if (hasNameBased)
            {
                var nameToIndex = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < model.MeshContextList.Count; i++)
                {
                    var mc = model.MeshContextList[i];
                    if (mc != null && !string.IsNullOrEmpty(mc.Name) && !nameToIndex.ContainsKey(mc.Name))
                        nameToIndex[mc.Name] = i;
                }
                CsvMeshSerializer.ResolveNameReferences(entries, nameToIndex);
            }
            CsvModelSerializer.BuildMirrorPairsFromEntries(entries, model);

            _renderer?.ClearScene();
            _viewportManager.RebuildAdapter(0, model);
            model.OnListChanged?.Invoke();

            _projectFileSubPanel?.SetStatus($"マージ完了: +{added} /{replaced}置換");
            Debug.Log($"[PlayerViewerCore] MergeCsv: added={added}, replaced={replaced}");
        }

        private void OnPartialImportDone(bool topologyChanged)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            _renderer?.ClearScene();
            _viewportManager.RebuildAdapter(0, model);
            var firstMc = model.FirstDrawableMeshContext;
            if (firstMc != null)
            {
                _selectionOps?.SetSelectionState(firstMc.Selection);
                _renderer?.SetSelectionState(firstMc.Selection);
            }
        }

        private void OnMeshFilterToSkinnedComplete()
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) return;
            _renderer?.ClearScene();
            _viewportManager.RebuildAdapter(0, model);
            var firstMc = model.FirstDrawableMeshContext;
            if (firstMc != null)
            {
                _selectionOps?.SetSelectionState(firstMc.Selection);
                _renderer?.SetSelectionState(firstMc.Selection);
            }
            _renderer?.UpdateSelectedDrawableMesh(0, model);
            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
            RebuildModelList();
            NotifyPanels(ChangeKind.ModelSwitch);
        }

        // ================================================================
        // プロジェクト
        // ================================================================

        private ProjectContext ActiveProject => _localLoader.Project ?? _receiver?.Project;

        // ================================================================
        // 外部パネル向け公開API
        // ================================================================

        /// <summary>データ変化通知。Editor専用外部パネル等がサブスクライブする。</summary>
        public Action<ChangeKind> OnChanged;

        /// <summary>現在アクティブな ProjectContext を返す。null の場合あり。</summary>
        public ProjectContext GetActiveProject() => ActiveProject;

        /// <summary>外部からコマンドをディスパッチする。</summary>
        public void Dispatch(PanelCommand cmd) => _commandDispatcher?.Dispatch(cmd);

        // ================================================================
        // ツール切り替え
        // ================================================================

        private void SwitchTool(ToolMode mode)
        {
            if (_toolMode == ToolMode.Sculpt && mode != ToolMode.Sculpt)
                _activePanel?.HideBrushCircle();

            if (_toolMode == ToolMode.AdvancedSelect && mode != ToolMode.AdvancedSelect)
                _activePanel?.HideAdvSelPreview();

            if (_toolMode == ToolMode.SkinWeightPaint && mode != ToolMode.SkinWeightPaint)
            {
                _skinWeightPaintHandler?.OnDeactivate();
                SkinWeightPaintTool.ActivePanel = null;
            }

            if (_toolMode == ToolMode.AddFace && mode != ToolMode.AddFace)
            {
                var firstMc = ActiveProject?.CurrentModel?.FirstSelectedMeshContext;
                if (firstMc != null) firstMc.Selection.Mode = MeshSelectMode.All;
            }

            _toolMode = mode;

            switch (mode)
            {
                case ToolMode.VertexMove:
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    _viewportManager?.RegisterActiveToolHandler(null);
                    break;
                case ToolMode.ObjectMove:
                    _vertexInteractor?.SetToolHandler(_objectMoveHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _objectMoveHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.PivotOffset:
                    _vertexInteractor?.SetToolHandler(_pivotOffsetHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _pivotOffsetHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.Sculpt:
                    _vertexInteractor?.SetToolHandler(_sculptHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _sculptHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.AdvancedSelect:
                    _vertexInteractor?.SetToolHandler(_advancedSelectHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _advancedSelectHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.AddFace:
                    _vertexInteractor?.SetToolHandler(_addFaceHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _addFaceHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.EdgeBevel:
                    _vertexInteractor?.SetToolHandler(_edgeBevelHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeBevelHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.EdgeExtrude:
                    _vertexInteractor?.SetToolHandler(_edgeExtrudeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeExtrudeHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.FaceExtrude:
                    _vertexInteractor?.SetToolHandler(_faceExtrudeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _faceExtrudeHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.EdgeTopology:
                    _vertexInteractor?.SetToolHandler(_edgeTopologyHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _edgeTopologyHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.Knife:
                    _vertexInteractor?.SetToolHandler(_knifeHandler);
                    _viewportManager?.RegisterActiveToolHandler((pos, ctx) => _knifeHandler?.UpdateHover(pos, ctx));
                    break;
                case ToolMode.SkinWeightPaint:
                    _vertexInteractor?.SetToolHandler(_skinWeightPaintHandler);
                    SkinWeightPaintTool.ActivePanel = _skinWeightPaintPanel;
                    _skinWeightPaintPanel?.RefreshMeshList(ActiveProject?.CurrentModel);
                    _skinWeightPaintPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    _skinWeightPaintHandler?.OnActivate();
                    // UndoController に対象 MeshObject を設定（スナップショット取得のため必須）
                    SyncSkinWeightUndoMesh();
                    break;
            }

            HideAllRightPanels();

            Button activeBtn;
            VisualElement section;
            switch (mode)
            {
                case ToolMode.ObjectMove:
                    activeBtn = _layoutRoot?.ToolObjectMoveBtn;
                    section   = _layoutRoot?.BoneEditorSection;
                    break;
                case ToolMode.SkinWeightPaint:
                    activeBtn = _layoutRoot?.ToolSkinWeightPaintBtn;
                    section   = _layoutRoot?.SkinWeightPaintSection;
                    break;
                case ToolMode.PivotOffset:
                    activeBtn = _layoutRoot?.ToolPivotOffsetBtn;
                    section   = _layoutRoot?.PivotSection;
                    break;
                case ToolMode.Sculpt:
                    activeBtn = _layoutRoot?.ToolSculptBtn;
                    section   = _layoutRoot?.SculptSection;
                    break;
                case ToolMode.AdvancedSelect:
                    activeBtn = _layoutRoot?.ToolAdvancedSelBtn;
                    section   = _layoutRoot?.AdvancedSelectSection;
                    _advancedSelectSubPanel?.Refresh();
                    break;
                case ToolMode.AddFace:
                case ToolMode.EdgeBevel:
                case ToolMode.EdgeExtrude:
                case ToolMode.FaceExtrude:
                case ToolMode.EdgeTopology:
                case ToolMode.Knife:
                    // セクション表示は Show***Panel() 側で行う。
                    // SwitchTool ではアクティブボタン・セクションを設定しない。
                    activeBtn = null;
                    section   = null;
                    break;
                default: // VertexMove
                    activeBtn = _layoutRoot?.ToolVertexMoveBtn;
                    section   = _layoutRoot?.VertexMoveSection;
                    _vertexMoveSubPanel?.Refresh();
                    break;
            }

            if (section != null)
                section.style.display = DisplayStyle.Flex;
            SetActiveButton(activeBtn);

            if (mode == ToolMode.ObjectMove)
                _boneEditorSubPanel?.Refresh();
        }

        // ================================================================
        // モデル切り替え
        // ================================================================

        private void SwitchActiveModel(int index)
        {
            var project = ActiveProject;
            if (project == null) return;
            if (!project.SelectModel(index)) return;

            var model = project.CurrentModel;
            if (model == null) return;

            _moveToolHandler?.SetProject(project);
            _objectMoveHandler?.SetProject(project);
            _pivotOffsetHandler?.SetProject(project);
            _sculptHandler?.SetProject(project);
            _advancedSelectHandler?.SetProject(project);
            _skinWeightPaintHandler?.SetProject(project);
            _boneInputHandler?.SetProject(project);

            _viewportManager.RebuildAdapter(0, model);

            var firstMc = model.FirstDrawableMeshContext;
            if (firstMc != null)
            {
                _selectionOps?.SetSelectionState(firstMc.Selection);
                _renderer?.SetSelectionState(firstMc.Selection);
            }
            _renderer?.UpdateSelectedDrawableMesh(0, model);
            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);

            RebuildModelList();
            _skinWeightPaintPanel?.RefreshMeshList(model);
            _skinWeightPaintPanel?.RefreshBoneList(model);
            NotifyPanels(ChangeKind.ModelSwitch);
        }

        // ================================================================
        // SyncUI / RebuildModelList
        // ================================================================

        private void SyncUI()
        {
            if (_layoutRoot == null) return;

            _layoutRoot.StatusLabel.text = $"Status: {_status}";

            bool clientExists = _remoteMode == RemoteMode.Client && _client != null;
            bool serverActive = _remoteMode == RemoteMode.Server && _playerServer != null;
            bool isConnected  = clientExists && _client.IsConnected;

            _layoutRoot.RemoteSection.style.display =
                (clientExists || serverActive) ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.ConnectBtn   .style.display = isConnected ? DisplayStyle.None : DisplayStyle.Flex;
            _layoutRoot.DisconnectBtn.style.display = isConnected ? DisplayStyle.Flex : DisplayStyle.None;
            _layoutRoot.FetchBtn.SetEnabled(isConnected);
            _layoutRoot.UndoBtn.SetEnabled(_editOps?.CanUndo ?? false);
            _layoutRoot.RedoBtn.SetEnabled(_editOps?.CanRedo ?? false);
        }

        private void RebuildModelList()
        {
            if (_layoutRoot?.ModelListContainer == null) return;
            _layoutRoot.ModelListContainer.Clear();

            var project = ActiveProject;
            var m = project?.CurrentModel;
            if (m != null)
            {
                var lbl = new Label($"{m.Name}  ({m.Count})");
                lbl.style.color = new StyleColor(Color.white);
                _layoutRoot.ModelListContainer.Add(lbl);
            }

            // ドロップダウン更新
            if (_layoutRoot.ModelSelectDropdown != null && project != null)
            {
                var choices = new List<string>();
                for (int i = 0; i < project.ModelCount; i++)
                {
                    var mdl = project.GetModel(i);
                    choices.Add(mdl?.Name ?? $"Model {i}");
                }
                _layoutRoot.ModelSelectDropdown.choices = choices;
                int cur = project.CurrentModelIndex;
                string curVal = (cur >= 0 && cur < choices.Count) ? choices[cur] : (choices.Count > 0 ? choices[0] : "");
                _layoutRoot.ModelSelectDropdown.SetValueWithoutNotify(curVal);
            }

            NotifyPanels(ChangeKind.ListStructure);
        }

        // ================================================================
        // コマンドディスパッチ / パネル通知
        // ================================================================

        private void DispatchPanelCommand(PanelCommand cmd)
        {
            _commandDispatcher?.Dispatch(cmd);
        }

        /// <summary>
        /// SkinWeightPaint 用 UndoController に現在のターゲットメッシュを設定する。
        /// ツール切り替え時・メッシュドロップダウン変更時に呼ぶ。
        /// </summary>
        private void SyncSkinWeightUndoMesh()
        {
            var swModel = ActiveProject?.CurrentModel;
            if (swModel == null || _editOps?.UndoController == null) return;

            // パネルで選択されたメッシュを優先、なければ FirstDrawableMeshContext
            int targetMasterIdx = _skinWeightPaintPanel?.CurrentTargetMesh ?? -1;
            MeshContext swMc = targetMasterIdx >= 0
                ? swModel.GetMeshContext(targetMasterIdx)
                : swModel.FirstDrawableMeshContext;

            if (swMc?.MeshObject == null) return;
            _editOps.UndoController.SetMeshObject(swMc.MeshObject, swMc.UnityMesh);
            _editOps.UndoController.MeshUndoContext.ParentModelContext = swModel;
            _skinWeightUndoMasterIndex = swModel.IndexOf(swMc);
        }

        /// <summary>
        /// src の頂点データ（Position・Normal・BoneWeight 等）を dst に上書きコピーする。
        /// 頂点数が一致する場合のみ実行。スキンウェイト Undo 書き戻し用。
        /// </summary>
        private static void CopyMeshObjectVertexData(Poly_Ling.Data.MeshObject src, Poly_Ling.Data.MeshObject dst)
        {
            if (src == null || dst == null) return;
            if (src.VertexCount != dst.VertexCount) return;
            for (int i = 0; i < src.VertexCount; i++)
            {
                var sv = src.Vertices[i];
                var dv = dst.Vertices[i];
                dv.Position   = sv.Position;
                dv.BoneWeight = sv.BoneWeight;
                // UV スロットを同期（UV 編集の Undo に必要）
                dv.UVs.Clear();
                foreach (var uv in sv.UVs)
                    dv.UVs.Add(uv);
            }
            dst.InvalidatePositionCache();
        }

        private void NotifyPanels(ChangeKind kind)
        {
            var project = ActiveProject;
            if (project == null || _panelContext == null) return;
            var view = new PlayerProjectView(project);
            _panelContext.Notify(view, kind);

            if (_toolMode == ToolMode.ObjectMove)
                _boneEditorSubPanel?.Refresh();

            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                _blendSubPanel?.OnSelectionChanged();

            foreach (var (section, refresh) in _sectionRefreshPairs)
                if (section?.style.display == DisplayStyle.Flex) refresh();

            if (_layoutRoot?.ModelBlendSection != null &&
                _layoutRoot.ModelBlendSection.style.display == DisplayStyle.Flex)
                _modelBlendSubPanel?.OnViewChanged(view, kind);

            OnChanged?.Invoke(kind);
        }

        // ================================================================
        // クライアントイベント
        // ================================================================

        private void OnConnected()    { _status = "接続済み"; }
        private void OnDisconnected() { _status = "切断"; }

        private void OnPushReceived(string json)
        {
            if (json.Contains("\"event\":\"mesh_changed\"") ||
                json.Contains("\"event\":\"model_changed\""))
                FetchProject();
        }

        // ================================================================
        // 受信イベント
        // ================================================================

        private void OnProjectHeaderReceived(ProjectContext project)
        {
            if (_fetchFlow != null) _fetchFlow.ModelCount = project.ModelCount;
            _viewportManager.ClearScene();
            RebuildModelList();
        }

        private void OnModelMetaReceived(int mi, ModelContext model) { }
        private void OnMeshSummaryReceived(int mi, int si, MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, MeshContext mc)
        {
            if (_receiver?.Project == null) return;
            if (mi == 0 && si == 0 && mc.UnityMesh != null)
                _viewportManager.ResetToMesh(mc.UnityMesh.bounds);
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _boneInputHandler?.SetProject(ActiveProject);
            _alignVerticesHandler?.SetProject(ActiveProject);
            _planarizeAlongBonesHandler?.SetProject(ActiveProject);
                _mergeVerticesHandler?.SetProject(ActiveProject);
                _splitVerticesHandler?.SetProject(ActiveProject);
                _addFaceHandler?.SetProject(ActiveProject);
                _flipFaceHandler?.SetProject(ActiveProject);
                _rotateHandler?.SetProject(ActiveProject);
                _scaleHandler?.SetProject(ActiveProject);
                _edgeBevelHandler?.SetProject(ActiveProject);
                _edgeExtrudeHandler?.SetProject(ActiveProject);
                _faceExtrudeHandler?.SetProject(ActiveProject);
                _edgeTopologyHandler?.SetProject(ActiveProject);
                _knifeHandler?.SetProject(ActiveProject);
                _lineExtrudeHandler?.SetProject(ActiveProject);
            RebuildModelList();
            NotifyPanels(ChangeKind.ListStructure);
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            _fetchFlow?.FetchProject();
        }
    }
}
