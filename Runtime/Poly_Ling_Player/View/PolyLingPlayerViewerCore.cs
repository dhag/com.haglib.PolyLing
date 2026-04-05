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
        private enum ToolMode { VertexMove, ObjectMove, PivotOffset, Sculpt, AdvancedSelect, SkinWeightPaint }
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
        private ObjectMoveTRSPanel           _objectMoveTRSPanel;
        private PivotOffsetToolHandler       _pivotOffsetHandler;
        private SculptToolHandler            _sculptHandler;
        private AdvancedSelectToolHandler    _advancedSelectHandler;
        private SkinWeightPaintToolHandler   _skinWeightPaintHandler;
        private PlayerSkinWeightPaintPanel   _skinWeightPaintPanel;
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
                _editOps?.UndoController);

            _fetchFlow = new PlayerRemoteFetchFlow(
                _client,
                _receiver,
                _localLoader,
                _viewportManager,
                _renderer,
                _selectionOps,
                NotifyPanels,
                s => _status = s);

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

                loadedModel.ComputeWorldMatrices();

                _viewportManager.RebuildAdapter(0, loadedModel);

                var loadedDrawables = loadedModel.DrawableMeshes;
                if (loadedDrawables != null)
                    foreach (var entry in loadedDrawables)
                    {
                        var mc = entry.Context;
                        if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0 && mc.IsVisible)
                        { loadedModel.SelectDrawableMesh(entry.MasterIndex); break; }
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

                var firstMcLocal = loadedModel.FirstSelectedDrawableMesh;
                if (firstMcLocal != null)
                {
                    _selectionOps?.SetSelectionState(firstMcLocal.Selection);
                    _renderer?.SetSelectionState(firstMcLocal.Selection);
                }
                _renderer?.UpdateSelectedDrawableMesh(0, loadedModel);
                _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);

                RebuildModelList();
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

            SyncRendererFlags();
        }

        /// <summary>毎フレーム Update 相当。</summary>
        public void Tick()
        {
            _viewportManager.Update();
            _editOps?.Tick();
            _client?.Tick();
            _playerServer?.Tick();
            _primitiveSubPanel?.Tick();
            SyncRendererFlags();
            SyncUI();
            UpdateFaceHoverOverlay();
            UpdateSelectedFacesOverlay();
            UpdateGizmoOverlay();
            UpdateAdvancedSelectOverlay();
            if (_toolMode == ToolMode.SkinWeightPaint)
                _skinWeightPaintHandler?.TickVisualization();
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

            _advancedSelectHandler = new AdvancedSelectToolHandler();
            _advancedSelectHandler.SetProject(ActiveProject);
            _advancedSelectHandler.SetSelectionOps(_selectionOps);
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
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                _viewportManager.UpdateTransform();
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
                    _boneInputHandler?.OnLeftClick(PlayerHitResult.Miss, pos, mods);
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragBegin += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    _boneInputHandler?.OnLeftDragBegin(PlayerHitResult.Miss, pos, mods);
                };
                panel.OnDrag += (btn, pos, delta, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    _boneInputHandler?.OnLeftDrag(pos, delta, mods);
                    _viewportManager.UpdateTransform();
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragEnd += (btn, pos, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
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
                p.OnDragBegin += (btn, pos, mods) =>
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
                model.ToggleSelection(idx);
            else
                model.Select(idx);

            _renderer?.NotifySelectionChanged();
            NotifyPanels(ChangeKind.Selection);
            _objectMoveTRSPanel?.Refresh();
            _activePanel?.MarkDirtyRepaint();
            return true;
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

            _objectMoveTRSPanel = new ObjectMoveTRSPanel();
            _objectMoveTRSPanel.Build(_layoutRoot.ObjectMoveTRSSection, _panelContext, () => ActiveProject);

            _skinWeightPaintPanel = new PlayerSkinWeightPaintPanel();
            _skinWeightPaintPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
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
            _blendSubPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
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

            _boneEditorSubPanel = new PlayerBoneEditorSubPanel();
            _boneEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _boneEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _boneEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _boneEditorSubPanel.OnFocusCamera     = pos =>
            {
                var orbit = _activeViewport?.Orbit;
                if (orbit != null) { orbit.SetTarget(pos); _activePanel?.MarkDirtyRepaint(); }
            };
            _boneEditorSubPanel.Build(_layoutRoot.BoneEditorSection);

            _uvEditorSubPanel = new PlayerUVEditorSubPanel();
            _uvEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _uvEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _uvEditorSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            _uvEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _uvEditorSubPanel.Build(_layoutRoot.UVEditorSection);

            _uvUnwrapSubPanel = new PlayerUVUnwrapSubPanel();
            _uvUnwrapSubPanel.GetModel    = () => ActiveProject?.CurrentModel;
            _uvUnwrapSubPanel.SendCommand = cmd => _commandDispatcher?.Dispatch(cmd);
            _uvUnwrapSubPanel.OnRepaint   = () => _activePanel?.MarkDirtyRepaint();
            _uvUnwrapSubPanel.Build(_layoutRoot.UVUnwrapSection);

            _materialListSubPanel = new PlayerMaterialListSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
                OnRepaint      = () => _activePanel?.MarkDirtyRepaint(),
            };
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
                GetProject         = () => ActiveProject,
                OnRebuildModelList = RebuildModelList,
            };
            _morphCreateSubPanel.Build(_layoutRoot.MorphCreateSection);

            _tposeSubPanel = new PlayerTPoseSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _tposeSubPanel.Build(_layoutRoot.TPoseSection);

            _humanoidMappingSubPanel = new PlayerHumanoidMappingSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _humanoidMappingSubPanel.Build(_layoutRoot.HumanoidMappingSection);

            _mirrorSubPanel = new PlayerMirrorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _mirrorSubPanel.Build(_layoutRoot.MirrorSection);

            _quadDecimatorSubPanel = new PlayerQuadDecimatorSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _quadDecimatorSubPanel.Build(_layoutRoot.QuadDecimatorSection);

            _mediaPipeSubPanel = new PlayerMediaPipeFaceDeformSubPanel
            {
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _mediaPipeSubPanel.Build(_layoutRoot.MediaPipeSection);

            _vmdTestSubPanel = new PlayerVMDTestSubPanel
            {
                GetModel      = () => ActiveProject?.CurrentModel,
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
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
            _primitiveSubPanel.OnMeshCreated = OnPrimitiveMeshCreated;

            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;

            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;

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

            void OnToggle(ChangeEvent<bool> _) => SyncRendererFlags();
            _layoutRoot.ShowSelectedMeshToggle      .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowUnselectedMeshToggle    .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowSelectedVerticesToggle  .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowUnselectedVerticesToggle.RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowSelectedWireToggle      .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowUnselectedWireToggle    .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowSelectedBoneToggle      .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.ShowUnselectedBoneToggle    .RegisterValueChangedCallback(OnToggle);
            _layoutRoot.BackfaceCullingToggle       .RegisterValueChangedCallback(OnToggle);

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
            _sectionRefreshPairs.Add((_layoutRoot.QuadDecimatorSection,     () => _quadDecimatorSubPanel?.Refresh()));
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
            _morphSubPanel?.Refresh();
        }

        private void ShowMorphCreatePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MorphCreateBtn);
            if (_layoutRoot?.MorphCreateSection != null)
                _layoutRoot.MorphCreateSection.style.display = DisplayStyle.Flex;
            _morphCreateSubPanel?.Refresh();
        }

        private void ShowTPosePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.TPoseBtn);
            if (_layoutRoot?.TPoseSection != null)
                _layoutRoot.TPoseSection.style.display = DisplayStyle.Flex;
            _tposeSubPanel?.Refresh();
        }

        private void ShowHumanoidMappingPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.HumanoidMappingBtn);
            if (_layoutRoot?.HumanoidMappingSection != null)
                _layoutRoot.HumanoidMappingSection.style.display = DisplayStyle.Flex;
            _humanoidMappingSubPanel?.Refresh();
        }

        private void ShowMirrorPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MirrorBtn);
            if (_layoutRoot?.MirrorSection != null)
                _layoutRoot.MirrorSection.style.display = DisplayStyle.Flex;
        }

        private void ShowQuadDecimatorPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.QuadDecimatorBtn);
            if (_layoutRoot?.QuadDecimatorSection != null)
                _layoutRoot.QuadDecimatorSection.style.display = DisplayStyle.Flex;
            _quadDecimatorSubPanel?.Refresh();
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
            Hide(_layoutRoot.ObjectMoveTRSSection);
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

        private void OnPrimitiveMeshCreated(MeshObject meshObject, string meshName, Vector3 worldPos)
        {
            _localLoader.EnsureProject();
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);
            _pivotOffsetHandler?.SetProject(ActiveProject);
            _sculptHandler?.SetProject(ActiveProject);
            _advancedSelectHandler?.SetProject(ActiveProject);
            _skinWeightPaintHandler?.SetProject(ActiveProject);
            _boneInputHandler?.SetProject(ActiveProject);

            var project = ActiveProject;
            if (project == null) return;
            if (project.CurrentModel == null && project.ModelCount > 0)
                project.SelectModel(0);
            var model = project.CurrentModel;
            if (model == null) return;

            var unityMesh = meshObject.ToUnityMesh();
            unityMesh.name      = meshName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            var ctx = new MeshContext
            {
                Name       = meshName,
                MeshObject = meshObject,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };

            if (ctx.BoneTransform != null && worldPos != Vector3.zero)
            {
                ctx.BoneTransform.UseLocalTransform = true;
                ctx.BoneTransform.Position = worldPos;
            }

            model.Add(ctx);
            model.ComputeWorldMatrices();
            int newIndex = model.MeshContextCount - 1;
            model.SelectByTypeExclusive(newIndex);
            model.SelectDrawableMesh(newIndex);

            _viewportManager.RebuildAdapter(0, model);

            var firstMc = model.FirstSelectedDrawableMesh;
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
            var firstMc = model.FirstSelectedDrawableMesh;
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
            var firstMc = model.FirstSelectedDrawableMesh;
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

            _toolMode = mode;

            switch (mode)
            {
                case ToolMode.VertexMove:
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    break;
                case ToolMode.ObjectMove:
                    _vertexInteractor?.SetToolHandler(_objectMoveHandler);
                    break;
                case ToolMode.PivotOffset:
                    _vertexInteractor?.SetToolHandler(_pivotOffsetHandler);
                    break;
                case ToolMode.Sculpt:
                    _vertexInteractor?.SetToolHandler(_sculptHandler);
                    break;
                case ToolMode.AdvancedSelect:
                    _vertexInteractor?.SetToolHandler(_advancedSelectHandler);
                    break;
                case ToolMode.SkinWeightPaint:
                    _vertexInteractor?.SetToolHandler(_skinWeightPaintHandler);
                    SkinWeightPaintTool.ActivePanel = _skinWeightPaintPanel;
                    _skinWeightPaintPanel?.RefreshBoneList(ActiveProject?.CurrentModel);
                    _skinWeightPaintHandler?.OnActivate();
                    break;
            }

            HideAllRightPanels();

            Button activeBtn;
            VisualElement section;
            switch (mode)
            {
                case ToolMode.ObjectMove:
                    activeBtn = _layoutRoot?.ToolObjectMoveBtn;
                    section   = _layoutRoot?.ObjectMoveTRSSection;
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
                _objectMoveTRSPanel?.Refresh();
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

            var m = ActiveProject?.CurrentModel;
            if (m != null)
            {
                var lbl = new Label($"{m.Name}  ({m.Count})");
                _layoutRoot.ModelListContainer.Add(lbl);
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

        private void NotifyPanels(ChangeKind kind)
        {
            var project = ActiveProject;
            if (project == null || _panelContext == null) return;
            var view = new PlayerProjectView(project);
            _panelContext.Notify(view, kind);

            if (_toolMode == ToolMode.ObjectMove)
                _objectMoveTRSPanel?.Refresh();

            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
                _blendSubPanel?.OnSelectionChanged();

            foreach (var (section, refresh) in _sectionRefreshPairs)
                if (section?.style.display == DisplayStyle.Flex) refresh();

            if (_layoutRoot?.ModelBlendSection != null &&
                _layoutRoot.ModelBlendSection.style.display == DisplayStyle.Flex)
                _modelBlendSubPanel?.OnViewChanged(view, kind);
        }

        // ================================================================
        // 描画フラグ同期
        // ================================================================

        private void SyncRendererFlags()
        {
            if (_renderer == null || _layoutRoot == null) return;
            _renderer.ShowSelectedMesh        = _layoutRoot.ShowSelectedMeshToggle.value;
            _renderer.ShowUnselectedMesh      = _layoutRoot.ShowUnselectedMeshToggle.value;
            _renderer.ShowSelectedVertices    = _layoutRoot.ShowSelectedVerticesToggle.value;
            _renderer.ShowUnselectedVertices  = _layoutRoot.ShowUnselectedVerticesToggle.value;
            _renderer.ShowSelectedWireframe   = _layoutRoot.ShowSelectedWireToggle.value;
            _renderer.ShowUnselectedWireframe = _layoutRoot.ShowUnselectedWireToggle.value;
            _renderer.ShowSelectedBone        = _layoutRoot.ShowSelectedBoneToggle.value;
            _renderer.ShowUnselectedBone      = _layoutRoot.ShowUnselectedBoneToggle.value;
            _renderer.BackfaceCullingEnabled  = _layoutRoot.BackfaceCullingToggle.value;
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
