// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア（全体統括MonoBehaviour）
//
// Viewer はリモートクライアント（受信側）。PolyLingCore は使わない。
// 受信した ProjectContext に直接操作する。
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
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
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector 設定
        // ================================================================

        // ── リモート起動モード ────────────────────────────────────────────
        /// <summary>
        /// None  : リモート機能を使わない。
        /// Client: PolyLingEditor（RemoteServer）に接続してデータを受信する。
        /// Server: PolyLingPlayerServer を起動してブラウザ等からの接続を受ける。
        /// </summary>
        public enum RemoteMode { None, Client, Server }

        [SerializeField] private RemoteMode _remoteMode = RemoteMode.None;

        // ── Client モード設定 ─────────────────────────────────────────────
        [SerializeField] private string _clientHost        = "127.0.0.1";
        [SerializeField] private int    _clientPort        = 8765;
        [SerializeField] private bool   _clientAutoConnect = true;

        // ── Server モード設定 ─────────────────────────────────────────────
        [SerializeField] private int  _serverPort      = 8765;
        [SerializeField] private bool _serverAutoStart = true;

        // ── その他 Inspector 設定 ─────────────────────────────────────────
        [SerializeField] private UIDocument            _uiDocument;
        [SerializeField] private Transform             _sceneRoot;

        // ================================================================
        // サブシステム
        // ================================================================

        private PolyLingPlayerClient           _client;       // RemoteMode.Client 時に内部生成
        private PolyLingPlayerServer           _playerServer;  // RemoteMode.Server 時に内部生成
        private RemoteProjectReceiver          _receiver;
        private MeshSceneRenderer              _renderer;
        private readonly PlayerLocalLoader     _localLoader = new PlayerLocalLoader();
        private readonly UndoManager           _undoManager = UndoManager.CreateNew();
        private          PlayerEditOps         _editOps;

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

        // ================================================================
        // ボタンアクティブ状態管理
        // ----------------------------------------------------------------
        // 全ボタン共通：押したボタンを青くし、前のボタンを元色に戻す。
        // ツールモードボタンは追加で SwitchTool() 内でビューポートハンドラも
        // 切り替える（UI 上の見た目は他ボタンと同一）。
        // ================================================================
        private Button _activeBtn;

        // ================================================================
        // 菱形オーバーレイインジケータ（UpdateBoneOverlay が毎フレーム更新）
        // ----------------------------------------------------------------
        // ObjectMove / PivotOffset / BoneEditor 表示中に描画する菱形の
        // スクリーン座標とMeshContextIndexを保持する。
        // クリック・ドラッグ開始時のヒットテストで参照する。
        // ================================================================
        private struct OverlayIndicator
        {
            public int     MeshContextIndex; // ModelContext 内の MeshContext インデックス
            public Vector2 ScreenPos;        // Y=0下スクリーン座標
            public bool    IsBone;
        }
        private readonly System.Collections.Generic.List<OverlayIndicator> _overlayIndicators =
            new System.Collections.Generic.List<OverlayIndicator>();
        private const float OverlayHitRadius = 8f; // px
        private MoveToolHandler        _moveToolHandler;
        private ObjectMoveToolHandler  _objectMoveHandler;
        private ObjectMoveTRSPanel     _objectMoveTRSPanel;
        private PivotOffsetToolHandler _pivotOffsetHandler;
        private SculptToolHandler      _sculptHandler;
        private AdvancedSelectToolHandler _advancedSelectHandler;
        private SkinWeightPaintToolHandler _skinWeightPaintHandler;
        private PlayerSkinWeightPaintPanel _skinWeightPaintPanel;
        private PlayerBlendSubPanel        _blendSubPanel;
        private PlayerModelBlendSubPanel   _modelBlendSubPanel;
        private BoneInputHandler           _boneInputHandler;
        private PlayerBoneEditorSubPanel   _boneEditorSubPanel;
        private PlayerUVEditorSubPanel     _uvEditorSubPanel;
        private PlayerUVUnwrapSubPanel     _uvUnwrapSubPanel;
        // ── 追加パネル ────────────────────────────────────────────────────
        private PlayerMaterialListSubPanel    _materialListSubPanel;
        private PlayerUVZSubPanel             _uvzSubPanel;
        private PlayerPartsSelectionSetSubPanel _partsSelSetSubPanel;
        private PlayerMeshSelectionSetSubPanel  _meshSelSetSubPanel;
        private PlayerMergeMeshesSubPanel     _mergeMeshesSubPanel;
        private PlayerMorphSubPanel           _morphSubPanel;
        private PlayerTPoseSubPanel           _tposeSubPanel;
        private PlayerHumanoidMappingSubPanel _humanoidMappingSubPanel;
        private PlayerMirrorSubPanel          _mirrorSubPanel;
        private PlayerQuadDecimatorSubPanel   _quadDecimatorSubPanel;
        private PlayerMediaPipeFaceDeformSubPanel _mediaPipeSubPanel;
        private PlayerVMDTestSubPanel         _vmdTestSubPanel;
        private PlayerRemoteServerSubPanel    _remoteServerSubPanel;

        private PlayerVertexMoveSubPanel   _vertexMoveSubPanel;
        private PlayerPivotSubPanel        _pivotSubPanel;
        private PlayerSculptSubPanel       _sculptSubPanel;
        private PlayerAdvancedSelectSubPanel _advancedSelectSubPanel;

        // 現在インタラクション対象のパネル / ビューポート（3視点切替用）
        private PlayerViewportPanel    _activePanel;
        private Vector2                _lastMouseScreenPos;
        private PlayerViewport         _activeViewport;

        // ================================================================
        // コマンド・フェッチ委譲
        // ================================================================

        private PlayerCommandDispatcher _commandDispatcher;
        private PlayerRemoteFetchFlow   _fetchFlow;

        // ================================================================
        // ステータス
        // ================================================================

        private string _status = "未接続";

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            if (_sceneRoot == null) _sceneRoot = transform;

            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_uiDocument.panelSettings == null)
                Debug.LogError("[PolyLingPlayerViewer] UIDocument に PanelSettings が未設定です。");
        }

        private void Start()
        {
            // リモートモードに応じてクライアントを初期化
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

            // SelectionState 生成 → レンダラーに設定（RebuildAdapter より前）
            _selectionState = new SelectionState();
            _renderer.SetSelectionState(_selectionState);

            // ビューポート初期化
            _viewportManager.Initialize(_sceneRoot, _renderer);

            // UIレイアウト構築
            if (_uiDocument != null && _uiDocument.panelSettings != null)
                BuildLayout(_uiDocument.rootVisualElement);

            // 頂点インタラクター（BuildLayout 後に Panel が使える）
            SetupVertexInteraction();

            // コマンドディスパッチャー（_selectionOps は SetupVertexInteraction 後に確定）
            _commandDispatcher = new PlayerCommandDispatcher(
                () => ActiveProject,
                _renderer,
                _viewportManager,
                _selectionOps,
                NotifyPanels,
                RebuildModelList,
                _editOps?.UndoController);

            // リモートフェッチフロー
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

            // RemoteMode.Server: BuildLayout 後に Initialize（_commandDispatcher 確定後）
            if (_remoteMode == RemoteMode.Server && _playerServer != null)
            {
                _playerServer.Initialize(
                    _serverPort,
                    _serverAutoStart,
                    () => _viewportManager.GetCurrentToolContext(_activeViewport),
                    cmd => _commandDispatcher?.Dispatch(cmd));
            }

            // ローカルローダー配線
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

                // GPU バッファ構築（アダプタは常にインデックス0）
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
                    if (bmc == null || bmc.Type != Poly_Ling.Data.MeshType.Bone) continue;
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
                _viewportManager.NotifyCameraChanged(
                    _viewportManager.PerspectiveViewport);

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

        private void OnDestroy()
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

        private void Update()
        {
            _viewportManager.Update();
            _editOps?.Tick();
            _client?.Tick();        // メインスレッドキュー処理（通常クラス版）
            _playerServer?.Tick();  // RemoteServerCore キュー処理
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

        private void LateUpdate()
        {
            _viewportManager.LateUpdate(ActiveProject);
        }

        // ================================================================
        // 頂点インタラクション セットアップ
        // ================================================================

        private void SetupVertexInteraction()
        {
            _selectionOps = new PlayerSelectionOps(_selectionState);

            // 選択変更 → GPU バッファ更新
            _selectionOps.OnSelectionChanged = () =>
            {
                _renderer?.NotifySelectionChanged();
            };

            _moveToolHandler = new MoveToolHandler(_selectionOps, ActiveProject)
            {
                OnSyncMeshPositions     = mc =>
                {
                    if (mc?.MeshObject != null && mc.UnityMesh != null)
                    {
                        if (mc.MeshObject.VertexCount == mc.UnityMesh.vertexCount)
                        {
                            // 展開なし（頂点数一致）: 直接更新
                            // DrawMesh に identity を渡すため UnityMesh.vertices はワールド座標で保持する
                            var wm = mc.WorldMatrix;
                            var verts = new UnityEngine.Vector3[mc.MeshObject.VertexCount];
                            for (int i = 0; i < verts.Length; i++)
                                verts[i] = wm.MultiplyPoint3x4(mc.MeshObject.Vertices[i].Position);
                            mc.UnityMesh.vertices = verts;
                            mc.UnityMesh.RecalculateBounds();
                        }
                        else
                        {
                            // 展開済み（UV分割で頂点数増）:
                            // _expandedToOriginal マッピングを使いCPUで直接更新する。
                            _viewportManager.UpdateExpandedUnityMesh(
                                mc, ActiveProject?.CurrentModel);
                        }
                    }
                    _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
                },
                OnRepaint               = () => _activePanel?.MarkDirtyRepaint(),

                // GetHoverElement: Mode に応じた GPU ホバー要素取得
                GetHoverElement = mode => _viewportManager.GetHoverElement(
                    mode, ActiveProject?.CurrentModel),

                GetToolContext  = () => _viewportManager.GetCurrentToolContext(_activeViewport),

                // GPU スクリーン座標・可視性コールバック（矩形選択用）
                GetScreenPositions = () => _viewportManager.GetScreenPositions(),
                GetVertexOffset    = ctxIdx => _viewportManager.GetVertexOffset(ctxIdx),
                IsVertexVisible    = gi  => _viewportManager.IsVertexVisible(gi),
                GetViewportHeight  = () => _activeViewport?.Cam?.pixelHeight ?? 0f,
                GetPanelHeight     = () => _activeViewport?.Cam?.pixelHeight ?? 0f,

                // 矩形選択オーバーレイ表示
                OnBoxSelectUpdate = (start, end) =>
                    _activePanel?.ShowBoxSelect(start, end),
                OnBoxSelectEnd = () =>
                    _activePanel?.HideBoxSelect(),

                OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging(),
                OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging(),
                OnEnterBoxSelecting      = () => _viewportManager.EnterBoxSelecting(),
                OnReadBackVertexFlags    = () => _viewportManager.ReadBackVertexFlags(),
                OnExitBoxSelecting       = () => _viewportManager.ExitBoxSelecting(),
                OnRequestNormal          = () => _viewportManager.RequestNormal(),
                OnClearMouseHover        = () => _viewportManager.ClearMouseHover(),
            };
            _viewportManager.RegisterMoveToolHandler(_moveToolHandler);

            // ObjectMoveToolHandler 生成・配線
            _objectMoveHandler = new ObjectMoveToolHandler();
            _objectMoveHandler.SetProject(ActiveProject);
            _objectMoveHandler.SetUndoController(_editOps?.UndoController);
            _objectMoveHandler.GetToolContext         = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _objectMoveHandler.OnRepaint              = () => _activePanel?.MarkDirtyRepaint();
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

            // PivotOffsetToolHandler 生成・配線
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
                if (mc?.MeshObject != null && mc.UnityMesh != null)
                {
                    var wm = mc.WorldMatrix;
                    if (mc.MeshObject.VertexCount == mc.UnityMesh.vertexCount)
                    {
                        var verts = new Vector3[mc.MeshObject.VertexCount];
                        for (int i = 0; i < verts.Length; i++)
                            verts[i] = wm.MultiplyPoint3x4(mc.MeshObject.Vertices[i].Position);
                        mc.UnityMesh.vertices = verts;
                        mc.UnityMesh.RecalculateBounds();
                    }
                    else
                    {
                        _viewportManager.UpdateExpandedUnityMesh(mc, ActiveProject?.CurrentModel);
                    }
                }
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
            };

            // SculptToolHandler 生成・配線
            _sculptHandler = new SculptToolHandler();
            _sculptHandler.SetProject(ActiveProject);
            _sculptHandler.SetUndoController(_editOps?.UndoController);
            _sculptHandler.GetToolContext           = () => _viewportManager.GetCurrentToolContext(_activeViewport);
            _sculptHandler.OnRepaint                = () => _activePanel?.MarkDirtyRepaint();
            _sculptHandler.OnEnterTransformDragging = () => _viewportManager.EnterTransformDragging();
            _sculptHandler.OnExitTransformDragging  = () => _viewportManager.ExitTransformDragging();
            _sculptHandler.OnSyncMeshPositions = mc =>
            {
                if (mc?.MeshObject != null && mc.UnityMesh != null)
                {
                    var wm = mc.WorldMatrix;
                    if (mc.MeshObject.VertexCount == mc.UnityMesh.vertexCount)
                    {
                        var verts = new Vector3[mc.MeshObject.VertexCount];
                        for (int i = 0; i < verts.Length; i++)
                            verts[i] = wm.MultiplyPoint3x4(mc.MeshObject.Vertices[i].Position);
                        mc.UnityMesh.vertices = verts;
                        mc.UnityMesh.RecalculateBounds();
                    }
                    else
                    {
                        _viewportManager.UpdateExpandedUnityMesh(mc, ActiveProject?.CurrentModel);
                    }
                }
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
            };
            _sculptHandler.OnUpdateBrushCircle = (center, radius) =>
                _activePanel?.ShowBrushCircle(center, radius);
            _sculptHandler.OnHideBrushCircle = () =>
                _activePanel?.HideBrushCircle();

            // AdvancedSelectToolHandler 生成・配線
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

            // SkinWeightPaintToolHandler 生成・配線
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
                if (mc?.MeshObject != null && mc.UnityMesh != null)
                {
                    var wm = mc.WorldMatrix;
                    if (mc.MeshObject.VertexCount == mc.UnityMesh.vertexCount)
                    {
                        var verts = new Vector3[mc.MeshObject.VertexCount];
                        for (int i = 0; i < verts.Length; i++)
                            verts[i] = wm.MultiplyPoint3x4(mc.MeshObject.Vertices[i].Position);
                        mc.UnityMesh.vertices = verts;
                        mc.UnityMesh.RecalculateBounds();
                    }
                    else
                    {
                        _viewportManager.UpdateExpandedUnityMesh(mc, ActiveProject?.CurrentModel);
                    }
                }
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
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

            // 初期アクティブは Perspective
            _activePanel    = _layoutRoot?.PerspectivePanel;
            _activeViewport = _viewportManager.PerspectiveViewport;

            if (_activePanel != null)
                _vertexInteractor.Connect(_activePanel);

            // ── 3パネル共通のアクティブ切替 + ホバー通知ヘルパー ──
            void ConnectPanelHover(PlayerViewportPanel panel, PlayerViewport vp)
            {
                if (panel == null) return;

                panel.OnPointerMoved += (pos, mods) =>
                {
                    _lastMouseScreenPos = pos;
                    // ボーンエディタ表示中はBoneInputHandlerにホバー通知
                    if (_layoutRoot?.BoneEditorSection != null &&
                        _layoutRoot.BoneEditorSection.style.display == DisplayStyle.Flex)
                    {
                        var boneCtx = _viewportManager.GetCurrentToolContext(_activeViewport);
                        _boneInputHandler?.UpdateHover(pos, boneCtx);
                    }
                };

                panel.OnPointerHover += localPos =>
                {
                    // アクティブ切替（別パネルからの移動時のみ）
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

            // ── ボーンエディタ: パネルクリック/ドラッグをBoneInputHandlerに転送 ──
            void ConnectBoneInput(PlayerViewportPanel panel)
            {
                if (panel == null) return;
                panel.OnClick     += (btn, pos, mods) =>
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
                panel.OnDrag      += (btn, pos, delta, mods) =>
                {
                    if (btn != 0) return;
                    if (_layoutRoot?.BoneEditorSection == null) return;
                    if (_layoutRoot.BoneEditorSection.style.display != DisplayStyle.Flex) return;
                    _boneInputHandler?.OnLeftDrag(pos, delta, mods);
                    _viewportManager.UpdateTransform();
                    _boneEditorSubPanel?.Refresh();
                };
                panel.OnDragEnd   += (btn, pos, mods) =>
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

            // ── 菱形インジケータのクリック / ドラッグ開始インターセプト ──────────
            // ObjectMove / PivotOffset モードのとき、菱形ヒット範囲内をクリック・
            // ドラッグした場合に対象オブジェクトを選択する。
            // ツールモードボタン固有のビューポートハンドラ処理（ObjectMoveTool 等）は
            // TrySelectIndicatorAtScreenPos の後も VertexInteractor 経由で継続して
            // 呼ばれるため、選択と移動を同一ドラッグ操作で完結させることができる。
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

            // ── カメラ変更通知（全3ビューポート）──
            void ConnectCameraChanged(PlayerViewport vp)
            {
                if (vp == null) return;
                if (vp.Orbit != null)
                {
                    vp.Orbit.OnCameraDragBegin = () =>
                        _viewportManager.EnterCameraDragging();
                    vp.Orbit.OnCameraChanged = () =>
                    {
                        _viewportManager.ExitCameraDragging();
                        _viewportManager.NotifyCameraChanged(vp);
                    };
                }
                if (vp.Ortho != null)
                {
                    vp.Ortho.OnCameraDragBegin = () =>
                        _viewportManager.EnterCameraDragging();
                    vp.Ortho.OnCameraDragEnd = () =>
                        _viewportManager.ExitCameraDragging();
                    vp.Ortho.OnCameraChanged = () =>
                        _viewportManager.NotifyCameraChanged(vp);
                }
            }

            ConnectCameraChanged(_viewportManager.PerspectiveViewport);
            ConnectCameraChanged(_viewportManager.TopViewport);
            ConnectCameraChanged(_viewportManager.FrontViewport);
            ConnectCameraChanged(_viewportManager.SideViewport);
        }

        private void UpdateFaceHoverOverlay()
        {
            // 頂点操作を行わないモードでは面ホバーは不要なので抑制する
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

            // 表示条件：ボーンエディタパネル表示中 / ObjectMove / PivotOffset モード
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

            float panelH = ctx.PreviewRect.height;
            var positions = new System.Collections.Generic.List<Vector2>();
            var selected  = new System.Collections.Generic.List<bool>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                bool isBone        = mc.Type == Data.MeshType.Bone;
                bool isNonSkinned  = mc.Type == Data.MeshType.Mesh
                                     && mc.MeshObject != null
                                     && !mc.MeshObject.HasBoneWeight;

                // ボーンエディタではボーンのみ / ObjectMove・PivotOffset では両方
                bool include = boneEditorOpen
                    ? isBone
                    : (isBone || isNonSkinned);

                if (!include) continue;

                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                // WorldToScreen は Y=0上 → Y=0下に変換
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

        // ================================================================
        // 菱形インジケータのヒットテスト
        // ================================================================

        /// <summary>
        /// スクリーン座標 screenPos（Y=0下）に対して菱形インジケータをヒットテストする。
        /// ヒットした場合は対応する MeshContextIndex を返す。なければ -1。
        /// </summary>
        private int HitTestOverlayIndicator(Vector2 screenPos)
        {
            float minDist = OverlayHitRadius;
            int   result  = -1;
            foreach (var ind in _overlayIndicators)
            {
                float d = Vector2.Distance(screenPos, ind.ScreenPos);
                if (d < minDist)
                {
                    minDist = d;
                    result  = ind.MeshContextIndex;
                }
            }
            return result;
        }

        /// <summary>
        /// ObjectMove / PivotOffset モード時に菱形をクリック・ドラッグした場合の選択処理。
        /// 選択が行われた場合 true を返す。ToolHandler の通常処理は継続して呼ぶ。
        /// </summary>
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

            // GPU バッファ・パネルに選択変更を通知
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

            // 頂点スクリーン座標（Y=0下）を収集
            var pts = new System.Collections.Generic.List<Vector2>();
            var verts = previewCtx.PreviewVertices;
            if (verts != null)
            {
                foreach (int vi in verts)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }
            }
            // 最短パス頂点も追加
            var path = previewCtx.PreviewPath;
            if (path != null)
                foreach (int vi in path)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    var sp = ctx.WorldToScreen(mo.Vertices[vi].Position);
                    pts.Add(new Vector2(sp.x, ctx.PreviewRect.height - sp.y));
                }

            // 辺スクリーン座標を収集
            var lines = new System.Collections.Generic.List<(Vector2, Vector2)>();
            var edges = previewCtx.PreviewEdges;
            if (edges != null)
            {
                foreach (var e in edges)
                {
                    if (e.V1 < 0 || e.V1 >= mo.VertexCount) continue;
                    if (e.V2 < 0 || e.V2 >= mo.VertexCount) continue;
                    var s1 = ctx.WorldToScreen(mo.Vertices[e.V1].Position);
                    var s2 = ctx.WorldToScreen(mo.Vertices[e.V2].Position);
                    float h = ctx.PreviewRect.height;
                    lines.Add((new Vector2(s1.x, h - s1.y), new Vector2(s2.x, h - s2.y)));
                }
            }
            // 最短パス辺
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
                        HasGizmo       = true,
                        IsDiamondStyle = false,
                        Origin         = origin,
                        XEnd           = xEnd,
                        YEnd           = yEnd,
                        ZEnd           = zEnd,
                        HoveredAxis    = hovAxis,
                        HasPivotGizmo  = true,
                        PivotOrigin    = pivotScreen,
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
                        Origin         = origin,
                        XEnd           = xEnd,
                        YEnd           = yEnd,
                        ZEnd           = zEnd,
                        HoveredAxis    = hovAxis,
                    });
                }
                else panel.HideGizmo();
                return;
            }

            if (_toolMode == ToolMode.Sculpt)
            {
                panel.HideGizmo(); // スカルプトはブラシ円オーバーレイを使うためギズモ不要
                return;
            }

            if (_toolMode == ToolMode.AdvancedSelect)
            {
                panel.HideGizmo();
                return;
            }

            if (_toolMode == ToolMode.SkinWeightPaint)
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
                    Origin      = o,
                    XEnd        = xe,
                    YEnd        = ye,
                    ZEnd        = ze,
                    HoveredAxis = ha,
                });
            }
            else panel.HideGizmo();
        }

        private Vector3 CalcWorldDelta(Vector2 screenDelta)
        {
            var vp = _viewportManager.PerspectiveViewport;
            if (vp?.Cam == null) return Vector3.zero;
            float dist  = vp.Orbit?.Distance ?? 1f;
            float scale = dist / vp.Cam.pixelHeight
                        * Mathf.Tan(vp.Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
            return vp.Cam.transform.right * screenDelta.x * scale
                 + vp.Cam.transform.up    * screenDelta.y * scale;
        }

        // ================================================================
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            // ── PanelContext 生成（コマンドディスパッチ）
            _panelContext = new PanelContext(DispatchPanelCommand);

            // ── モデルリストサブパネル
            _modelListSubPanel = new ModelListSubPanel();
            _modelListSubPanel.Build(_layoutRoot.ModelListSection);
            _modelListSubPanel.SetContext(_panelContext);

            // ── メッシュリストサブパネル
            _meshListSubPanel = new MeshListSubPanel();
            _meshListSubPanel.Build(_layoutRoot.MeshListSection);
            _meshListSubPanel.SetContext(_panelContext);

            // ── オブジェクト移動TRSパネル
            _objectMoveTRSPanel = new ObjectMoveTRSPanel();
            _objectMoveTRSPanel.Build(_layoutRoot.ObjectMoveTRSSection, _panelContext, () => ActiveProject);

            // ── スキンウェイトペイントパネル
            _skinWeightPaintPanel = new PlayerSkinWeightPaintPanel();
            _skinWeightPaintPanel.OnRepaint = () => _activePanel?.MarkDirtyRepaint();
            _skinWeightPaintPanel.Build(_layoutRoot.SkinWeightPaintSection);

            // ── ブレンドサブパネル
            _blendSubPanel = new PlayerBlendSubPanel();
            _blendSubPanel.OnSyncMeshPositions = mc =>
                _viewportManager.SyncMeshPositionsAndTransform(mc, ActiveProject?.CurrentModel);
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

            // ── モデルブレンドサブパネル
            _modelBlendSubPanel = new PlayerModelBlendSubPanel();
            _modelBlendSubPanel.SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd);
            _modelBlendSubPanel.GetProjectView = () => ActiveProject != null
                ? new PlayerProjectView(ActiveProject) : null;
            _modelBlendSubPanel.Build(_layoutRoot.ModelBlendSection);

            // ── ボーン入力ハンドラ
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

            // ── ボーンエディタサブパネル
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

            // ── UV エディタサブパネル
            _uvEditorSubPanel = new PlayerUVEditorSubPanel();
            _uvEditorSubPanel.GetModel          = () => ActiveProject?.CurrentModel;
            _uvEditorSubPanel.GetUndoController = () => _editOps?.UndoController;
            _uvEditorSubPanel.GetCommandQueue   = () => _editOps?.CommandQueue;
            _uvEditorSubPanel.OnRepaint         = () => _activePanel?.MarkDirtyRepaint();
            _uvEditorSubPanel.Build(_layoutRoot.UVEditorSection);

            // ── UV 展開サブパネル
            _uvUnwrapSubPanel = new PlayerUVUnwrapSubPanel();
            _uvUnwrapSubPanel.GetModel      = () => ActiveProject?.CurrentModel;
            _uvUnwrapSubPanel.SendCommand   = cmd => _commandDispatcher?.Dispatch(cmd);
            _uvUnwrapSubPanel.OnRepaint     = () => _activePanel?.MarkDirtyRepaint();
            _uvUnwrapSubPanel.Build(_layoutRoot.UVUnwrapSection);

            // ── 追加パネル生成・配線 ──────────────────────────────────────
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
                GetCameraForward  = () => { var ctx = _viewportManager.GetCurrentToolContext(_activeViewport); return ctx != null ? (ctx.CameraTarget - ctx.CameraPosition).normalized : Vector3.forward; },
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
                GetToolContext = () => _viewportManager.GetCurrentToolContext(_activeViewport),
            };
            _morphSubPanel.Build(_layoutRoot.MorphSection);

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

            // ── ピボットオフセットサブパネル生成
            _pivotSubPanel = new PlayerPivotSubPanel();
            _pivotSubPanel.Build(_layoutRoot.PivotSection);

            // ── スカルプトサブパネル生成・配線
            _sculptSubPanel = new PlayerSculptSubPanel
            {
                GetHandler = () => _sculptHandler,
            };
            _sculptSubPanel.Build(_layoutRoot.SculptSection);

            // ── 詳細選択サブパネル生成・配線
            _advancedSelectSubPanel = new PlayerAdvancedSelectSubPanel
            {
                GetHandler = () => _advancedSelectHandler,
            };
            _advancedSelectSubPanel.Build(_layoutRoot.AdvancedSelectSection);

            // ローカルローダー UI（Load PMX / Load MQO ボタン）
            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            // インポートサブパネル生成・配線
            _importSubPanel = new PlayerImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;

            // エクスポートサブパネル生成・配線
            _exportSubPanel = new PlayerExportSubPanel();
            _exportSubPanel.Build(_layoutRoot.ExportSection);
            _exportSubPanel.OnExportPmx = OnExportPmx;
            _exportSubPanel.OnExportMqo = OnExportMqo;
            // 表示/非表示は SwitchTool(VertexMove) で HideAllRightPanels が行う

            // プロジェクトファイルサブパネル生成・配線
            _projectFileSubPanel = new PlayerProjectFileSubPanel();
            _projectFileSubPanel.Build(_layoutRoot.ProjectFileSection);
            _projectFileSubPanel.OnSave     = OnSaveProject;
            _projectFileSubPanel.OnLoad     = OnLoadProject;
            _projectFileSubPanel.OnSaveCsv  = OnSaveCsvProject;
            _projectFileSubPanel.OnLoadCsv  = OnLoadCsvProject;
            _projectFileSubPanel.OnMergeCsv = OnMergeCsvProject;

            // 部分インポートサブパネル生成・配線
            _partialImportSubPanel = new PlayerPartialImportSubPanel();
            _partialImportSubPanel.Build(_layoutRoot.PartialImportSection);
            _partialImportSubPanel.OnImportDone = OnPartialImportDone;

            // 部分エクスポートサブパネル生成・配線
            _partialExportSubPanel = new PlayerPartialExportSubPanel();
            _partialExportSubPanel.Build(_layoutRoot.PartialExportSection);

            // 図形生成サブパネル生成・配線
            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            _primitiveSubPanel.OnMeshCreated = OnPrimitiveMeshCreated;

            // 左ペイン「図形生成」ボタン → 右ペインに図形生成パネルを切り替え表示
            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;

            // MeshFilter→Skinnedサブパネル生成・配線
            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;

            // 左ペイン「MF→Skinned」ボタン
            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _layoutRoot.BlendBtn.clicked      += ShowBlendPanel;
            _layoutRoot.ModelBlendBtn.clicked += ShowModelBlendPanel;
            _layoutRoot.BoneEditorBtn.clicked  += ShowBoneEditorPanel;
            _layoutRoot.UVEditorBtn.clicked    += ShowUVEditorPanel;
            _layoutRoot.UVUnwrapBtn.clicked    += ShowUVUnwrapPanel;
            // ── 追加パネルボタン配線 ──────────────────────────────────────
            _layoutRoot.MaterialListBtn.clicked    += ShowMaterialListPanel;
            _layoutRoot.UVZBtn.clicked             += ShowUVZPanel;
            _layoutRoot.PartsSelectionSetBtn.clicked += ShowPartsSelectionSetPanel;
            _layoutRoot.MeshSelectionSetBtn.clicked  += ShowMeshSelectionSetPanel;
            _layoutRoot.MergeMeshesBtn.clicked     += ShowMergeMeshesPanel;
            _layoutRoot.MorphBtn.clicked           += ShowMorphPanel;
            _layoutRoot.TPoseBtn.clicked           += ShowTPosePanel;
            _layoutRoot.HumanoidMappingBtn.clicked += ShowHumanoidMappingPanel;
            _layoutRoot.MirrorBtn.clicked          += ShowMirrorPanel;
            _layoutRoot.QuadDecimatorBtn.clicked   += ShowQuadDecimatorPanel;
            _layoutRoot.MediaPipeBtn.clicked        += ShowMediaPipePanel;
            _layoutRoot.VMDTestBtn.clicked          += ShowVMDTestPanel;
            _layoutRoot.RemoteServerBtn.clicked     += ShowRemoteServerPanel;
            _layoutRoot.FullExportMqoBtn.clicked += () => ShowExportPanel(PlayerExportSubPanel.Mode.MQO);
            _layoutRoot.ProjectFileBtn.clicked += ShowProjectFilePanel;
            _layoutRoot.PartialImportPmxBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.PMX);
            _layoutRoot.PartialImportMqoBtn.clicked += () => ShowPartialImportPanel(PlayerPartialImportSubPanel.Mode.MQO);
            _layoutRoot.PartialExportPmxBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.PMX);
            _layoutRoot.PartialExportMqoBtn.clicked += () => ShowPartialExportPanel(PlayerPartialExportSubPanel.Mode.MQO);

            _layoutRoot.ToolVertexMoveBtn.clicked  += () => SwitchTool(ToolMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked  += () => SwitchTool(ToolMode.ObjectMove);
            _layoutRoot.ToolPivotOffsetBtn.clicked += () => SwitchTool(ToolMode.PivotOffset);
            _layoutRoot.ToolSculptBtn.clicked      += () => SwitchTool(ToolMode.Sculpt);
            _layoutRoot.ToolAdvancedSelBtn.clicked        += () => SwitchTool(ToolMode.AdvancedSelect);
            _layoutRoot.ToolSkinWeightPaintBtn.clicked    += () => SwitchTool(ToolMode.SkinWeightPaint);

            // モデルリスト・メッシュリストボタン → 右ペインに対応セクションを切り替え表示
            _layoutRoot.ModelListBtn.clicked += ShowModelListPanel;
            _layoutRoot.MeshListBtn .clicked += ShowMeshListPanel;

            // Load PMX / Load MQO ボタン → 右ペインにサブパネルを切り替え表示
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

            // 初期状態：頂点移動モード・メッシュリスト表示
            // （_vertexInteractor は SetupVertexInteraction() で後設定されるが
            //   SwitchTool 内で ?. アクセスするため null セーフ）
            SwitchTool(ToolMode.VertexMove);
        }

        /// <summary>右ペインにインポートサブパネルを表示し、モードを切り替える。</summary>
        /// <remarks>LocalLoader の Load ボタンから呼ばれるため対応する左ペインボタンがなく、
        /// SetActiveButton(null) でアクティブ色をクリアする。</remarks>
        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            SetActiveButton(null);
            if (_layoutRoot?.ImportSection != null)
                _layoutRoot.ImportSection.style.display = DisplayStyle.Flex;
            _importSubPanel?.SetMode(mode);
        }

        /// <summary>右ペインに図形生成パネルを表示する。</summary>
        private void ShowPrimitivePanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.PrimitiveBtn);
            if (_layoutRoot?.PrimitiveSection != null)
                _layoutRoot.PrimitiveSection.style.display = DisplayStyle.Flex;
        }

        /// <summary>右ペインに MeshFilter→Skinned パネルを表示する。</summary>
        private void ShowMeshFilterToSkinnedPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MeshFilterToSkinnedBtn);
            if (_layoutRoot?.MeshFilterToSkinnedSection != null)
                _layoutRoot.MeshFilterToSkinnedSection.style.display = DisplayStyle.Flex;
            // モデルを渡して階層表示を更新
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        /// <summary>右ペインにブレンドパネルを表示する。</summary>
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

        /// <summary>右ペインにモデルリストを表示する。</summary>
        private void ShowModelListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.ModelListBtn);
            if (_layoutRoot?.ModelListSection != null)
                _layoutRoot.ModelListSection.style.display = DisplayStyle.Flex;
        }

        /// <summary>右ペインにメッシュリストを表示する。</summary>
        private void ShowMeshListPanel()
        {
            HideAllRightPanels();
            SetActiveButton(_layoutRoot?.MeshListBtn);
            if (_layoutRoot?.MeshListSection != null)
                _layoutRoot.MeshListSection.style.display = DisplayStyle.Flex;
        }

        /// <summary>右ペインの全セクションを非表示にする。</summary>
        /// <remarks>
        /// ボタン押下時は必ずこれを呼んでから対象セクションだけを Flex にする。
        /// ModelListSection / MeshListSection / ObjectMoveTRSSection /
        /// SkinWeightPaintSection も含め、全セクションを統一管理する。
        /// </remarks>
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
            Hide(_layoutRoot.TPoseSection);
            Hide(_layoutRoot.HumanoidMappingSection);
            Hide(_layoutRoot.MirrorSection);
            Hide(_layoutRoot.QuadDecimatorSection);
            Hide(_layoutRoot.MediaPipeSection);
            Hide(_layoutRoot.VMDTestSection);
            Hide(_layoutRoot.RemoteServerSection);
        }

        // ================================================================
        // ボタンアクティブ色の切替
        // ================================================================

        private static readonly StyleColor ActiveBtnColor   = new StyleColor(new Color(0.25f, 0.45f, 0.65f));
        private static readonly StyleColor InactiveBtnColor = new StyleColor(StyleKeyword.Null);

        /// <summary>
        /// 押されたボタンを青くし、前のアクティブボタンを元色に戻す。
        /// 全ボタン（ツールモード・サブパネル切替どちらも）共通で呼ぶ。
        /// </summary>
        private void SetActiveButton(Button btn)
        {
            if (_activeBtn != null)
                _activeBtn.style.backgroundColor = InactiveBtnColor;
            _activeBtn = btn;
            if (_activeBtn != null)
                _activeBtn.style.backgroundColor = ActiveBtnColor;
        }

        /// <summary>
        /// 図形生成パネルからのコールバック。
        /// MeshObject をモデルに追加し、GPU バッファを再構築してパネルに通知する。
        /// </summary>
        private void OnPrimitiveMeshCreated(Data.MeshObject meshObject, string meshName, Vector3 worldPos)
        {
            // プロジェクト/モデルが未存在なら静かに生成（OnLoaded不要）
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

            // UnityMesh生成
            var unityMesh = meshObject.ToUnityMesh();
            unityMesh.name = meshName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshContextを生成してモデルに追加
            var ctx = new Data.MeshContext
            {
                Name       = meshName,
                MeshObject = meshObject,
                UnityMesh  = unityMesh,
                IsVisible  = true,
            };

            // ワールド生成位置をBoneTransformに設定
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

            // GPU バッファ再構築
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

        /// <summary>
        /// PMX インポート要求。ImportPmxCommand をコマンドキューにエンキューする。
        /// コマンドの onResult で _localLoader.LoadModel を呼び、OnLoaded を発火させる。
        /// </summary>
        private void OnImportPmx(string filePath, PMXImportSettings settings)
        {
            var cmd = new ImportPmxCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"PMX読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        /// <summary>
        /// MQO インポート要求。ImportMqoCommand をコマンドキューにエンキューする。
        /// </summary>
        private void OnImportMqo(string filePath, MQOImportSettings settings)
        {
            var cmd = new ImportMqoCommand(
                filePath, settings,
                onResult: (model, _) => _localLoader.LoadModel(filePath, model),
                onError:  msg       => _status = $"MQO読込失敗: {msg}");
            _editOps?.CommandQueue.Enqueue(cmd);
        }

        /// <summary>PMX エクスポート要求。</summary>
        private void OnExportPmx(string outputPath, PMXExportSettings settings)
        {
            var project = ActiveProject;
            var model   = project?.CurrentModel;
            if (model == null)
            {
                _exportSubPanel?.SetStatus("モデルがありません");
                return;
            }

            try
            {
                var result = PMXExporter.Export(model, outputPath, settings);
                if (result.Success)
                {
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                    UnityEngine.Debug.Log($"[PlayerViewer] PMX export OK: {outputPath}");
                }
                else
                {
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                    UnityEngine.Debug.LogError($"[PlayerViewer] PMX export failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerViewer] PMX export exception: {ex.Message}");
            }
        }

        /// <summary>MQO エクスポート要求。</summary>
        private void OnExportMqo(string outputPath, MQOExportSettings settings)
        {
            var project = ActiveProject;
            var model   = project?.CurrentModel;
            if (model == null)
            {
                _exportSubPanel?.SetStatus("モデルがありません");
                return;
            }

            try
            {
                var result = MQOExporter.ExportFile(outputPath, model, settings);
                if (result.Success)
                {
                    _exportSubPanel?.SetStatus($"完了: {System.IO.Path.GetFileName(outputPath)}");
                    UnityEngine.Debug.Log($"[PlayerViewer] MQO export OK: {outputPath}");
                }
                else
                {
                    _exportSubPanel?.SetStatus($"失敗: {result.ErrorMessage}");
                    UnityEngine.Debug.LogError($"[PlayerViewer] MQO export failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                _exportSubPanel?.SetStatus($"例外: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerViewer] MQO export exception: {ex.Message}");
            }
        }

        /// <summary>プロジェクト保存要求。</summary>
        private void OnSaveProject()
        {
            var project = ActiveProject;
            if (project == null)
            {
                _projectFileSubPanel?.SetStatus("プロジェクトがありません");
                return;
            }

            var dto = ProjectSerializer.FromProjectContext(project);
            if (dto == null)
            {
                _projectFileSubPanel?.SetStatus("シリアライズ失敗");
                return;
            }

            bool ok = ProjectSerializer.ExportWithDialog(dto, project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "保存完了" : "キャンセルまたは失敗");
        }

        /// <summary>プロジェクト読み込み要求。</summary>
        private void OnLoadProject()
        {
            var dto = ProjectSerializer.ImportWithDialog();
            if (dto == null)
            {
                _projectFileSubPanel?.SetStatus("キャンセルまたは失敗");
                return;
            }

            var loadedProject = ProjectSerializer.ToProjectContext(dto);
            if (loadedProject == null)
            {
                _projectFileSubPanel?.SetStatus("復元失敗");
                return;
            }

            // PlayerLocalLoader 経由で既存フローに乗せる
            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? dto.name, m);

            _projectFileSubPanel?.SetStatus($"読込完了: {dto.name}");
        }

        /// <summary>CSVフォルダ保存要求。</summary>
        private void OnSaveCsvProject()
        {
            var project = ActiveProject;
            if (project == null) { _projectFileSubPanel?.SetStatus("プロジェクトがありません"); return; }

            bool ok = CsvProjectSerializer.ExportWithDialog(
                project,
                defaultName: project.Name ?? "Project");
            _projectFileSubPanel?.SetStatus(ok ? "CSVフォルダ保存完了" : "キャンセルまたは失敗");
        }

        /// <summary>CSVフォルダ読込要求。</summary>
        private void OnLoadCsvProject()
        {
            var loadedProject = CsvProjectSerializer.ImportWithDialog(
                out _,   // editorStates は Player では使わない
                out _);  // workPlanes  は Player では使わない

            if (loadedProject == null)
            {
                _projectFileSubPanel?.SetStatus("キャンセルまたは失敗");
                return;
            }

            _localLoader.Clear();
            foreach (var m in loadedProject.Models)
                _localLoader.LoadModel(m.FilePath ?? loadedProject.Name, m);

            _projectFileSubPanel?.SetStatus($"CSV読込完了: {loadedProject.Name}");
        }

        /// <summary>
        /// CSVフォルダ追加マージ要求。
        /// フォルダ内の全メッシュエントリを読み込み、現在のモデルにマージする。
        /// 名前重複時は置き換え（EditorのMergeAdditionalEntriesにあるダイアログは省略）。
        /// </summary>
        private void OnMergeCsvProject()
        {
            var project = ActiveProject;
            var model   = project?.CurrentModel;
            if (model == null) { _projectFileSubPanel?.SetStatus("モデルがありません"); return; }

            string folderPath = PLEditorBridge.I.OpenFolderPanel(
                "Add from CSV Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(folderPath))
            {
                _projectFileSubPanel?.SetStatus("キャンセル");
                return;
            }

            var entries = CsvModelSerializer.LoadAllMeshEntriesFromFolder(folderPath);
            if (entries == null || entries.Count == 0)
            {
                _projectFileSubPanel?.SetStatus("読み込めるデータがありません");
                return;
            }

            // 名前→インデックス辞書を構築し、重複時は置き換え・新規時は追加
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
                {
                    model.MeshContextList[existIdx] = entry.MeshContext;
                    replaced++;
                }
                else
                {
                    model.Add(entry.MeshContext);
                    added++;
                }
            }

            // 名前ベース参照の解決
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

            // ビューポート再構築
            _renderer?.ClearScene();
            _viewportManager.RebuildAdapter(0, model);
            model.OnListChanged?.Invoke();

            _projectFileSubPanel?.SetStatus($"マージ完了: +{added} /{replaced}置換");
            Debug.Log($"[PlayerViewer] MergeCsv: added={added}, replaced={replaced}");
        }

        /// <summary>
        /// 部分インポート完了コールバック。
        /// 頂点位置・トポロジーいずれの変更も RebuildAdapter で反映する。
        /// </summary>
        private void OnPartialImportDone(bool topologyChanged)
        {
            var project = ActiveProject;
            var model   = project?.CurrentModel;
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

        /// <summary>
        /// MeshFilter→Skinned 変換完了後のコールバック。
        /// トポロジーが大きく変わるため ModelSwitch 相当の再構築を行う。
        /// </summary>
        private void OnMeshFilterToSkinnedComplete()
        {
            var project = ActiveProject;
            if (project == null) return;
            var model = project.CurrentModel;
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

        /// <summary>
        /// ビューポート操作ツールを切り替える。
        /// </summary>
        /// <remarks>
        /// ■ ツールモードボタン固有の処理（サブパネル切替ボタンとの実装上の差異）
        ///   - _vertexInteractor.SetToolHandler() でビューポートのマウス入力ハンドラを切り替える。
        ///   - モード離脱時にブラシ円・プレビュー・ペイントハンドラをクリーンアップする。
        ///   上記はツールモードボタン専用。UI の見た目（ボタン色・右ペイン切替）は
        ///   他の全ボタンと同一ルール（SetActiveButton + HideAllRightPanels）で統一する。
        ///
        /// ■ 右ペインに表示するセクションの対応：
        ///   VertexMove / PivotOffset / Sculpt / AdvancedSelect → MeshListSection
        ///   ObjectMove   → ObjectMoveTRSSection
        ///   SkinWeightPaint → SkinWeightPaintSection
        /// </remarks>
        private void SwitchTool(ToolMode mode)
        {
            // ── モード離脱クリーンアップ（ツールモードボタン固有） ──────────
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

            // ── ビューポートハンドラ切替（ツールモードボタン固有） ───────────
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

            // ── 右ペイン切替（全ボタン共通ルール） ────────────────────────
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

            // ObjectMove 時は TRS パネルの値を最新状態に更新
            if (mode == ToolMode.ObjectMove)
                _objectMoveTRSPanel?.Refresh();
        }

        // ================================================================
        // SyncUI
        // ================================================================

        private void SyncUI()
        {
            if (_layoutRoot == null) return;

            _layoutRoot.StatusLabel.text = $"Status: {_status}";

            bool clientExists = _remoteMode == RemoteMode.Client && _client != null;
            bool serverActive = _remoteMode == RemoteMode.Server && _playerServer != null;
            bool isConnected  = clientExists && _client.IsConnected;

            // Client/Server どちらかが有効な場合にリモートセクション表示
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
            if (project == null) return;

            var m = project.CurrentModel;
            if (m != null)
            {
                var lbl = new Label($"{m.Name}  ({m.Count})");
                lbl.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
                _layoutRoot.ModelListContainer.Add(lbl);
            }

            // サブパネルに変更を通知
            NotifyPanels(ChangeKind.ListStructure);
        }

        // ================================================================
        // PanelCommand ディスパッチ
        // ================================================================

        /// <summary>
        /// ModelListSubPanel / MeshListSubPanel から送られた PanelCommand を
        /// ActiveProject に適用し、パネルに再通知する。
        /// </summary>
        private void DispatchPanelCommand(PanelCommand cmd)
        {
            _commandDispatcher?.Dispatch(cmd);
        }

        /// <summary>PanelContext 経由でサブパネルに変更通知を送る。</summary>
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

            // ボーンエディタ表示中は常にRefresh
            if (_layoutRoot?.BoneEditorSection != null &&
                _layoutRoot.BoneEditorSection.style.display == DisplayStyle.Flex)
                _boneEditorSubPanel?.Refresh();

            // UVエディタ表示中はRefresh
            if (_layoutRoot?.UVEditorSection != null &&
                _layoutRoot.UVEditorSection.style.display == DisplayStyle.Flex)
                _uvEditorSubPanel?.Refresh();

            // UV展開パネル表示中はRefresh
            if (_layoutRoot?.UVUnwrapSection != null &&
                _layoutRoot.UVUnwrapSection.style.display == DisplayStyle.Flex)
                _uvUnwrapSubPanel?.Refresh();

            // モデルブレンドパネルが表示中のときViewChanged を通知
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
        private void OnMeshSummaryReceived(int mi, int si, Data.MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, Data.MeshContext mc)
        {
            var project = _receiver?.Project;
            if (project == null) return;

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
