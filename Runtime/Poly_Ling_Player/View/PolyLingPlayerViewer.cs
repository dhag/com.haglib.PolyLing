// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア（全体統括MonoBehaviour）
//
// Viewer はリモートクライアント（受信側）。PolyLingCore は使わない。
// 受信した ProjectContext に直接操作する。
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Linq;
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
        private PanelContext                   _panelContext;
        private ModelListSubPanel              _modelListSubPanel;
        private MeshListSubPanel               _meshListSubPanel;

        // 頂点インタラクション（Perspective ビューポート専用）
        private SelectionState         _selectionState;
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private MoveToolHandler        _moveToolHandler;

        // 現在インタラクション対象のパネル / ビューポート（3視点切替用）
        private PlayerViewportPanel    _activePanel;
        private PlayerViewport         _activeViewport;

        // ================================================================
        // ステータス
        // ================================================================

        private string _status = "未接続";
        private int    _fetchingModelIndex = -1;
        private int    _modelCount;

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
            SyncRendererFlags();
            SyncUI();
            UpdateFaceHoverOverlay();
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
                            var verts = new UnityEngine.Vector3[mc.MeshObject.VertexCount];
                            for (int i = 0; i < verts.Length; i++)
                                verts[i] = mc.MeshObject.Vertices[i].Position;
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
                OnRepaint               = () => { },

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

        private void UpdateGizmoOverlay()
        {
            var panel = _activePanel;
            if (panel == null) return;
            var ctx = _viewportManager.GetCurrentToolContext(_activeViewport);
            if (ctx == null || _moveToolHandler == null) { panel.HideGizmo(); return; }
            if (_moveToolHandler.TryGetGizmoScreenPositions(
                    ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
            {
                panel.UpdateGizmo(new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    Origin      = origin,
                    XEnd        = xEnd,
                    YEnd        = yEnd,
                    ZEnd        = zEnd,
                    HoveredAxis = hovAxis,
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
        }

        /// <summary>右ペインにインポートサブパネルを表示し、モードを切り替える。</summary>
        private void ShowImportPanel(PlayerImportSubPanel.Mode mode)
        {
            _importSubPanel?.SetMode(mode);
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

        // ================================================================
        // プロジェクト
        // ================================================================

        private ProjectContext ActiveProject => _localLoader.Project ?? _receiver?.Project;

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
            var project = ActiveProject;
            if (project == null) return;
            var model   = project.CurrentModel;

            switch (cmd)
            {
                // ── モデル選択
                case SwitchModelCommand c:
                    project.SelectModel(c.TargetModelIndex);
                    {
                        var switchedModel = project.CurrentModel;
                        if (switchedModel != null)
                        {
                            _renderer?.ClearScene();
                            _viewportManager.RebuildAdapter(0, switchedModel);
                            var firstMc = switchedModel.FirstSelectedDrawableMesh;
                            if (firstMc != null)
                            {
                                _selectionOps?.SetSelectionState(firstMc.Selection);
                                _renderer?.SetSelectionState(firstMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, switchedModel);
                            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                        }
                    }
                    NotifyPanels(ChangeKind.ModelSwitch);
                    return;

                // ── モデル名前変更
                case RenameModelCommand c:
                    var renameTarget = project.GetModel(c.ModelIndex);
                    if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                        renameTarget.Name = c.NewName;
                    NotifyPanels(ChangeKind.ListStructure);
                    return;

                // ── モデル削除
                case DeleteModelCommand c:
                    project.RemoveModelAt(c.ModelIndex);
                    RebuildModelList();
                    return;

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) return;
                    switch (sel.Category)
                    {
                        case MeshCategory.Drawable:
                            model.ClearMeshSelection();
                            foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                            if (sel.Indices.Length > 0)
                                model.SelectDrawableMesh(sel.Indices[0]);
                            var selMc = model.FirstSelectedDrawableMesh;
                            if (selMc != null)
                            {
                                _selectionOps?.SetSelectionState(selMc.Selection);
                                _renderer?.SetSelectionState(selMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, model);
                            break;
                        case MeshCategory.Bone:
                            model.ClearBoneSelection();
                            foreach (int idx in sel.Indices) model.AddToBoneSelection(idx);
                            break;
                        case MeshCategory.Morph:
                            model.ClearMorphSelection();
                            foreach (int idx in sel.Indices) model.AddToMorphSelection(idx);
                            break;
                    }
                    NotifyPanels(ChangeKind.Selection);
                    return;

                // ── 可視性トグル
                case ToggleVisibilityCommand c:
                    if (model == null) return;
                    var visCtx = model.GetMeshContext(c.MasterIndex);
                    if (visCtx != null) visCtx.IsVisible = !visCtx.IsVisible;
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── 一括可視性
                case SetBatchVisibilityCommand c:
                    if (model == null) return;
                    foreach (int mi in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(mi);
                        if (ctx != null) ctx.IsVisible = c.Visible;
                    }
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── ロックトグル
                case ToggleLockCommand c:
                    if (model == null) return;
                    var lckCtx = model.GetMeshContext(c.MasterIndex);
                    if (lckCtx != null) lckCtx.IsLocked = !lckCtx.IsLocked;
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── ミラータイプ
                case CycleMirrorTypeCommand c:
                    if (model == null) return;
                    var mirCtx = model.GetMeshContext(c.MasterIndex);
                    if (mirCtx != null)
                        mirCtx.MirrorType = (mirCtx.MirrorType + 1) % 4;
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── メッシュ名前変更
                case RenameMeshCommand c:
                    if (model == null) return;
                    var renCtx = model.GetMeshContext(c.MasterIndex);
                    if (renCtx != null && !string.IsNullOrEmpty(c.NewName))
                        renCtx.Name = c.NewName;
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── メッシュ削除
                case DeleteMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                        model.RemoveAt(idx);
                    NotifyPanels(ChangeKind.ListStructure);
                    return;

                // ── メッシュ複製
                case DuplicateMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var srcCtx = model.GetMeshContext(idx);
                        if (srcCtx == null) continue;
                        var dup = new Data.MeshContext
                        {
                            Name       = srcCtx.Name + "_copy",
                            MeshObject = srcCtx.MeshObject?.Clone(),
                            IsVisible  = srcCtx.IsVisible,
                            IsLocked   = srcCtx.IsLocked,
                            Depth      = srcCtx.Depth,
                        };
                        model.Add(dup);
                    }
                    NotifyPanels(ChangeKind.ListStructure);
                    return;

                // ── BonePose Active
                case SetBonePoseActiveCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData != null) ctx.BonePoseData.IsActive = c.Active;
                    }
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose レイヤーリセット
                case ResetBonePoseLayersCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                        model.GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose ベイク（Player では BindPose 更新のみ）
                case BakePoseToBindPoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData == null) continue;
                        // Player では WorldMatrix/BindPose 再計算は省略し Notify のみ
                    }
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── モーフ全選択 / 全解除
                case SelectAllMorphsCommand c:
                    if (model == null) return;
                    model.ClearMorphSelection();
                    foreach (int idx in c.AllMorphIndices) model.AddToMorphSelection(idx);
                    NotifyPanels(ChangeKind.Selection);
                    return;

                case DeselectAllMorphsCommand _:
                    model?.ClearMorphSelection();
                    NotifyPanels(ChangeKind.Selection);
                    return;

                // ── モーフ変換・プレビュー・セット作成（PolyLingCore が必要、Player では未実装）
                case ConvertMeshToMorphCommand _:
                case ConvertMorphToMeshCommand _:
                case CreateMorphSetCommand _:
                case StartMorphPreviewCommand _:
                case ApplyMorphPreviewCommand _:
                case EndMorphPreviewCommand _:
                    UnityEngine.Debug.LogWarning($"[PolyLingPlayerViewer] {cmd.GetType().Name} requires PolyLingCore (not implemented in Player).");
                    return;

                // ── BoneTransform 値設定
                case SetBoneTransformValueCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BoneTransform == null) continue;
                        ctx.BoneTransform.UseLocalTransform = true;
                        switch (c.TargetField)
                        {
                            case SetBoneTransformValueCommand.Field.PositionX: ctx.BoneTransform.Position = new UnityEngine.Vector3(c.Value, ctx.BoneTransform.Position.y, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionY: ctx.BoneTransform.Position = new UnityEngine.Vector3(ctx.BoneTransform.Position.x, c.Value, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionZ: ctx.BoneTransform.Position = new UnityEngine.Vector3(ctx.BoneTransform.Position.x, ctx.BoneTransform.Position.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.RotationX: ctx.BoneTransform.Rotation = new UnityEngine.Vector3(c.Value, ctx.BoneTransform.Rotation.y, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationY: ctx.BoneTransform.Rotation = new UnityEngine.Vector3(ctx.BoneTransform.Rotation.x, c.Value, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationZ: ctx.BoneTransform.Rotation = new UnityEngine.Vector3(ctx.BoneTransform.Rotation.x, ctx.BoneTransform.Rotation.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.ScaleX:    ctx.BoneTransform.Scale    = new UnityEngine.Vector3(c.Value, ctx.BoneTransform.Scale.y, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleY:    ctx.BoneTransform.Scale    = new UnityEngine.Vector3(ctx.BoneTransform.Scale.x, c.Value, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleZ:    ctx.BoneTransform.Scale    = new UnityEngine.Vector3(ctx.BoneTransform.Scale.x, ctx.BoneTransform.Scale.y, c.Value); break;
                        }
                    }
                    model.ComputeWorldMatrices();
                    NotifyPanels(ChangeKind.Attributes);
                    return;

                // ── BoneTransform スライダー開始／終了（通知不要）
                case BeginBoneTransformSliderDragCommand _:
                case EndBoneTransformSliderDragCommand _:
                    return;

                // ── その他（モーフ変換・プレビュー等）は Player では未実装
                default:
                    UnityEngine.Debug.LogWarning($"[PolyLingPlayerViewer] Unhandled PanelCommand: {cmd.GetType().Name}");
                    return;
            }
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
        private void OnDisconnected() { _status = "切断"; _fetchingModelIndex = -1; }

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
            _modelCount         = project.ModelCount;
            _fetchingModelIndex = -1;
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
            if (_client == null || !_client.IsConnected) return;
            _localLoader.Clear();
            _status = "project_header フェッチ中...";
            _receiver?.Reset();
            _modelCount         = 0;
            _fetchingModelIndex = -1;
            _viewportManager.ClearScene();

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4) { _status = "project_header 失敗"; return; }
                _receiver?.ProcessBatch(bin);
                if (_modelCount > 0) FetchAllModelsBatch(0);
            });
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (mi >= _modelCount) return;
            _fetchingModelIndex = mi;
            _status = $"メッシュフェッチ中... [{mi}/{_modelCount - 1}]";

            FetchMeshDataBatch(mi, "bone",     () =>
            FetchMeshDataBatch(mi, "drawable", () =>
            FetchMeshDataBatch(mi, "morph",    () =>
            {
                var project = _receiver?.Project;
                if (project != null && mi < project.ModelCount)
                {
                    var model = project.Models[mi];
                    _viewportManager.RebuildAdapter(mi, model);

                    // ────────────────────────────────────────────────
                    // RebuildAdapter 後の初期選択設定
                    //
                    // 【描画メッシュ選択】
                    //   DrawableMeshes から頂点数・面数がゼロでない先頭を選択する。
                    //   model.SelectedDrawableMeshIndices に設定し、
                    //   DrawMeshes / DrawWireframeAndVertices が正しく描画できるようにする。
                    //
                    // 【ボーン選択】
                    //   「首」ボーンを優先し、なければ先頭ボーンを選択する。
                    //   model.SelectedBoneIndices に設定し、DrawBones が正しく描画できるようにする。
                    // ────────────────────────────────────────────────

                    // 先頭の非空 Drawable を選択
                    var drawables = model.DrawableMeshes;
                    if (drawables != null)
                    {
                        foreach (var entry in drawables)
                        {
                            var mc = entry.Context;
                            if (mc?.MeshObject != null
                                && mc.MeshObject.VertexCount > 0
                                && mc.IsVisible)
                            {
                                model.SelectDrawableMesh(entry.MasterIndex);
                                break;
                            }
                        }
                    }

                    // 首ボーン（または先頭ボーン）を選択
                    int neckIdx = -1, firstBoneIdx = -1;
                    for (int ci = 0; ci < model.MeshContextCount; ci++)
                    {
                        var bmc = model.GetMeshContext(ci);
                        if (bmc == null || bmc.Type != Poly_Ling.Data.MeshType.Bone) continue;
                        if (firstBoneIdx < 0) firstBoneIdx = ci;
                        string n = bmc.Name ?? "";
                        if (n == "首" || n.ToLower() == "neck") { neckIdx = ci; break; }
                    }
                    int selectedBone = neckIdx >= 0 ? neckIdx : firstBoneIdx;
                    if (selectedBone >= 0)
                        model.SelectBone(selectedBone);

                    // SelectionState 同期（選択描画メッシュの Selection を使う）
                    var firstMc = model.FirstSelectedDrawableMesh;
                    if (firstMc != null)
                    {
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }

                    // DrawWireframeAndVertices 用の selectedMeshIndex を更新
                    _renderer?.UpdateSelectedDrawableMesh(mi, model);

                    // カメラパラメータをアダプターに設定（UpdateFrame 1回）
                    _viewportManager.NotifyCameraChanged(
                        _viewportManager.PerspectiveViewport);
                }

                int next = mi + 1;
                if (next < _modelCount) FetchAllModelsBatch(next);
                else
                {
                    _status = $"完了 ({project?.Name})";
                    NotifyPanels(ChangeKind.ModelSwitch);
                }
            })));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                if (bin != null && bin.Length >= 4) _receiver?.ProcessBatch(bin);
                done?.Invoke();
            });
        }
    }
}
