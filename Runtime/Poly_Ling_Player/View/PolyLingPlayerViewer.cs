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
using Poly_Ling.View;
using Poly_Ling.MeshListV2;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector 設定
        // ================================================================

        [SerializeField] private PolyLingPlayerClient _client;
        [SerializeField] private UIDocument            _uiDocument;
        [SerializeField] private Transform             _sceneRoot;

        // ================================================================
        // サブシステム
        // ================================================================

        private RemoteProjectReceiver          _receiver;
        private MeshSceneRenderer              _renderer;
        private readonly PlayerLocalLoader     _localLoader = new PlayerLocalLoader();
        private readonly UndoManager           _undoManager = UndoManager.CreateNew();
        private          PlayerEditOps         _editOps;

        private readonly PlayerViewportManager _viewportManager = new PlayerViewportManager();
        private PlayerLayoutRoot               _layoutRoot;
        private PlayerImportSubPanel           _importSubPanel;
        private PlayerPrimitiveMeshSubPanel    _primitiveSubPanel;
        private MeshFilterToSkinnedSubPanel    _mfToSkinnedSubPanel;
        private PanelContext                   _panelContext;
        private ModelListSubPanel              _modelListSubPanel;
        private MeshListSubPanel               _meshListSubPanel;

        // 頂点インタラクション（Perspective ビューポート専用）
        private SelectionState         _selectionState;
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private enum ToolMode { VertexMove, ObjectMove }
        private ToolMode               _toolMode = ToolMode.VertexMove;
        private MoveToolHandler        _moveToolHandler;
        private ObjectMoveToolHandler  _objectMoveHandler;

        // 現在インタラクション対象のパネル / ビューポート（3視点切替用）
        private PlayerViewportPanel    _activePanel;
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
            if (_client == null)
            {
                _client = GetComponent<PolyLingPlayerClient>();
                if (_client == null)
                    Debug.LogWarning("[PolyLingPlayerViewer] PolyLingPlayerClient が見つかりません。");
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
                RebuildModelList);

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
            }

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
            _primitiveSubPanel?.Tick();
            SyncRendererFlags();
            SyncUI();
            UpdateFaceHoverOverlay();
            UpdateSelectedFacesOverlay();
            UpdateGizmoOverlay();
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

            // ローカルローダー UI（Load PMX / Load MQO ボタン）
            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            // インポートサブパネル生成・配線
            _importSubPanel = new PlayerImportSubPanel();
            _importSubPanel.Build(_layoutRoot.ImportSection);
            _importSubPanel.OnImportPmx = OnImportPmx;
            _importSubPanel.OnImportMqo = OnImportMqo;

            // 図形生成サブパネル生成・配線
            _primitiveSubPanel = new PlayerPrimitiveMeshSubPanel();
            _primitiveSubPanel.Build(_layoutRoot.PrimitiveSection, _sceneRoot);
            _primitiveSubPanel.OnMeshCreated = OnPrimitiveMeshCreated;
            _layoutRoot.PrimitiveSection.style.display = DisplayStyle.None;

            // 左ペイン「図形生成」ボタン → 右ペインに図形生成パネルを切り替え表示
            _layoutRoot.PrimitiveBtn.clicked += ShowPrimitivePanel;

            // MeshFilter→Skinnedサブパネル生成・配線
            _mfToSkinnedSubPanel = new MeshFilterToSkinnedSubPanel();
            _mfToSkinnedSubPanel.Build(_layoutRoot.MeshFilterToSkinnedSection);
            _mfToSkinnedSubPanel.OnConversionComplete = OnMeshFilterToSkinnedComplete;
            _layoutRoot.MeshFilterToSkinnedSection.style.display = DisplayStyle.None;

            // 左ペイン「MF→Skinned」ボタン
            _layoutRoot.MeshFilterToSkinnedBtn.clicked += ShowMeshFilterToSkinnedPanel;

            _layoutRoot.ToolVertexMoveBtn.clicked += () => SwitchTool(ToolMode.VertexMove);
            _layoutRoot.ToolObjectMoveBtn.clicked += () => SwitchTool(ToolMode.ObjectMove);

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

            // 初期ツールボタンスタイル
            UpdateToolButtonStyles();
        }

        /// <summary>右ペインにインポートサブパネルを表示し、モードを切り替える。</summary>
        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            HideAllRightPanels();
            if (_layoutRoot?.ImportSection != null)
                _layoutRoot.ImportSection.style.display = DisplayStyle.Flex;
            _importSubPanel?.SetMode(mode);
        }

        /// <summary>右ペインに図形生成パネルを表示する。</summary>
        private void ShowPrimitivePanel()
        {
            HideAllRightPanels();
            if (_layoutRoot?.PrimitiveSection != null)
                _layoutRoot.PrimitiveSection.style.display = DisplayStyle.Flex;
        }

        /// <summary>右ペインに MeshFilter→Skinned パネルを表示する。</summary>
        private void ShowMeshFilterToSkinnedPanel()
        {
            HideAllRightPanels();
            if (_layoutRoot?.MeshFilterToSkinnedSection != null)
                _layoutRoot.MeshFilterToSkinnedSection.style.display = DisplayStyle.Flex;
            // モデルを渡して階層表示を更新
            _mfToSkinnedSubPanel?.SetModel(ActiveProject?.CurrentModel);
        }

        /// <summary>右ペインの全サブパネルを非表示にする。</summary>
        private void HideAllRightPanels()
        {
            if (_layoutRoot?.ImportSection != null)
                _layoutRoot.ImportSection.style.display = DisplayStyle.None;
            if (_layoutRoot?.PrimitiveSection != null)
                _layoutRoot.PrimitiveSection.style.display = DisplayStyle.None;
            if (_layoutRoot?.MeshFilterToSkinnedSection != null)
                _layoutRoot.MeshFilterToSkinnedSection.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 図形生成パネルからのコールバック。
        /// MeshObject をモデルに追加し、GPU バッファを再構築してパネルに通知する。
        /// </summary>
        private void OnPrimitiveMeshCreated(Data.MeshObject meshObject, string meshName)
        {
            // プロジェクト/モデルが未存在なら静かに生成（OnLoaded不要）
            _localLoader.EnsureProject();
            _moveToolHandler?.SetProject(ActiveProject);
            _objectMoveHandler?.SetProject(ActiveProject);

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

        private void SwitchTool(ToolMode mode)
        {
            _toolMode = mode;
            switch (mode)
            {
                case ToolMode.VertexMove:
                    _vertexInteractor?.SetToolHandler(_moveToolHandler);
                    break;
                case ToolMode.ObjectMove:
                    _vertexInteractor?.SetToolHandler(_objectMoveHandler);
                    break;
            }
            UpdateToolButtonStyles();
        }

        private void UpdateToolButtonStyles()
        {
            if (_layoutRoot == null) return;
            var activeColor   = new StyleColor(new Color(0.25f, 0.45f, 0.65f));
            var inactiveColor = new StyleColor(StyleKeyword.Null);

            if (_layoutRoot.ToolVertexMoveBtn != null)
                _layoutRoot.ToolVertexMoveBtn.style.backgroundColor =
                    _toolMode == ToolMode.VertexMove ? activeColor : inactiveColor;
            if (_layoutRoot.ToolObjectMoveBtn != null)
                _layoutRoot.ToolObjectMoveBtn.style.backgroundColor =
                    _toolMode == ToolMode.ObjectMove ? activeColor : inactiveColor;
        }

        // ================================================================
        // SyncUI
        // ================================================================

        private void SyncUI()
        {
            if (_layoutRoot == null) return;

            _layoutRoot.StatusLabel.text = $"Status: {_status}";

            bool clientExists = _client != null;
            bool isConnected  = clientExists && _client.IsConnected;

            _layoutRoot.RemoteSection.style.display =
                clientExists ? DisplayStyle.Flex : DisplayStyle.None;
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
            _panelContext.Notify(new PlayerProjectView(project), kind);
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
