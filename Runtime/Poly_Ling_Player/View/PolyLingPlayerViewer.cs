// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア（全体統括MonoBehaviour）
// 役割:
//   - ライフサイクル管理 (Awake/Start/Update/LateUpdate/OnDestroy)
//   - PlayerViewportManager / PlayerLayoutRoot の構築と配線
//   - RemoteProjectReceiver / MeshSceneRenderer / PlayerEditOps の配線
//   - PlayerVertexInteractor（Perspectiveビューポートのみ）の配線
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

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

        private RemoteProjectReceiver         _receiver;
        private MeshSceneRenderer             _renderer;
        private readonly UndoManager          _undoManager  = UndoManager.CreateNew();
        private PlayerEditOps                 _editOps;
        private readonly PlayerLocalLoader    _localLoader  = new PlayerLocalLoader();

        private readonly PlayerViewportManager _viewportManager = new PlayerViewportManager();
        private PlayerLayoutRoot               _layoutRoot;

        // 頂点インタラクション（Perspective ビューポート専用）
        private PlayerSelectionOps     _selectionOps;
        private PlayerVertexInteractor _vertexInteractor;
        private MoveToolHandler        _moveToolHandler;

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
            _editOps  = new PlayerEditOps(_undoManager);
            _receiver = new RemoteProjectReceiver();

            // ビューポート初期化
            _viewportManager.Initialize(_sceneRoot, _renderer);

            // UIレイアウト構築（先にビューポートを初期化しておく必要がある）
            if (_uiDocument != null && _uiDocument.panelSettings != null)
                BuildLayout(_uiDocument.rootVisualElement);

            // 頂点インタラクター（BuildLayout 後に Panel 参照が使える）
            SetupVertexInteraction();

            // ローカルローダー配線
            _localLoader.OnStatusChanged = s => _status = s;
            _localLoader.OnLoaded = project =>
            {
                _renderer.ClearScene();
                var list = project.Models[0].MeshContextList;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].UnityMesh != null)
                    {
                        _viewportManager.ResetToMesh(list[i].UnityMesh.bounds);
                        break;
                    }
                }
                _moveToolHandler?.SetProject(ActiveProject);
                RebuildModelList();
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
            if (_layoutRoot?.PerspectivePanel != null)
                _vertexInteractor?.Disconnect(_layoutRoot.PerspectivePanel);

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
        }

        private void LateUpdate()
        {
            _viewportManager.LateUpdate(ActiveProject);
        }

        // ================================================================
        // UIレイアウト構築
        // ================================================================

        private void BuildLayout(VisualElement root)
        {
            _layoutRoot = new PlayerLayoutRoot();
            _layoutRoot.Build(root);

            _localLoader.BuildUI(_layoutRoot.LocalLoaderSection);

            _layoutRoot.ConnectBtn   .clicked += () => _client?.Connect();
            _layoutRoot.DisconnectBtn.clicked += () => _client?.Disconnect();
            _layoutRoot.FetchBtn     .clicked += FetchProject;
            _layoutRoot.UndoBtn      .clicked += () => _editOps?.PerformUndo();
            _layoutRoot.RedoBtn      .clicked += () => _editOps?.PerformRedo();

            // ビューポートパネルを PlayerViewport に接続
            _layoutRoot.PerspectivePanel.SetViewport(_viewportManager.PerspectiveViewport);
            _layoutRoot.TopPanel        .SetViewport(_viewportManager.TopViewport);
            _layoutRoot.FrontPanel      .SetViewport(_viewportManager.FrontViewport);

            // 表示フラグ Toggle コールバック
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

        // ================================================================
        // 頂点インタラクション（Perspective 専用）
        // ================================================================

        private void SetupVertexInteraction()
        {
            _selectionOps = new PlayerSelectionOps(new SelectionState());

            _moveToolHandler = new MoveToolHandler(_selectionOps, ActiveProject)
            {
                WorldToScreen = pos =>
                {
                    var cam = _viewportManager.PerspectiveViewport?.Cam;
                    return cam != null ? (Vector2)cam.WorldToScreenPoint(pos) : Vector2.zero;
                },
                ScreenDeltaToWorldDelta = (_, delta) => CalcWorldDelta(delta),
                OnSyncMeshPositions     = _ => { },
                OnRepaint               = () => { },
            };

            _vertexInteractor = new PlayerVertexInteractor(_selectionOps)
            {
                HitTest = FindVertex,
            };
            _vertexInteractor.SetToolHandler(_moveToolHandler);

            if (_layoutRoot?.PerspectivePanel != null)
                _vertexInteractor.Connect(_layoutRoot.PerspectivePanel);
        }

        // ================================================================
        // ヒットテスト（Perspective カメラ）
        // ================================================================

        private PlayerHitResult FindVertex(Vector2 screenPos)
        {
            var cam   = _viewportManager.PerspectiveViewport?.Cam;
            var model = ActiveProject?.CurrentModel;
            if (cam == null || model == null) return PlayerHitResult.Miss;

            const float hitRadiusPx = 8f;
            float bestDistSq = hitRadiusPx * hitRadiusPx;
            int   bestMesh   = -1;
            int   bestVert   = -1;

            for (int mi = 0; mi < model.MeshContextList.Count; mi++)
            {
                var mc = model.MeshContextList[mi];
                if (mc?.MeshObject?.Vertices == null) continue;
                var verts = mc.MeshObject.Vertices;

                for (int vi = 0; vi < verts.Count; vi++)
                {
                    Vector3 sp3 = cam.WorldToScreenPoint(verts[vi].Position);
                    if (sp3.z <= 0f) continue;

                    float dx = sp3.x - screenPos.x;
                    float dy = sp3.y - screenPos.y;
                    float dSq = dx * dx + dy * dy;
                    if (dSq < bestDistSq) { bestDistSq = dSq; bestMesh = mi; bestVert = vi; }
                }
            }

            return bestVert < 0
                ? PlayerHitResult.Miss
                : new PlayerHitResult { HasHit = true, MeshIndex = bestMesh, VertexIndex = bestVert };
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
            _layoutRoot.UndoBtn.SetEnabled(_editOps != null && _editOps.CanUndo);
            _layoutRoot.RedoBtn.SetEnabled(_editOps != null && _editOps.CanRedo);
        }

        private void RebuildModelList()
        {
            if (_layoutRoot?.ModelListContainer == null) return;
            _layoutRoot.ModelListContainer.Clear();

            var project = ActiveProject;
            if (project == null) return;

            _layoutRoot.ModelListContainer.Add(
                new Label($"Project: {project.Name}  ({project.ModelCount})"));

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var m = project.Models[mi];
                _layoutRoot.ModelListContainer.Add(
                    new Label($"  [{mi}] {m.Name}  ({m.Count})")
                    { style = { color = new StyleColor(new Color(0.75f, 0.75f, 0.75f)) } });
            }
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
            _modelCount = project.ModelCount;
            _fetchingModelIndex = -1;
            _viewportManager.ClearScene();
            RebuildModelList();
        }

        private void OnModelMetaReceived(int mi, ModelContext model)   { }
        private void OnMeshSummaryReceived(int mi, int si, Data.MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, Data.MeshContext mc)
        {
            var project = _receiver?.Project;
            if (project == null) return;

            if (mi == 0 && si == 0 && mc.UnityMesh != null)
                _viewportManager.ResetToMesh(mc.UnityMesh.bounds);

            _moveToolHandler?.SetProject(ActiveProject);
            RebuildModelList();
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
            _modelCount = 0;
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
                    _viewportManager.RebuildAdapter(mi, project.Models[mi]);

                int next = mi + 1;
                if (next < _modelCount) FetchAllModelsBatch(next);
                else _status = $"完了 ({project?.Name})";
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
