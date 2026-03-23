// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア（全体統括MonoBehaviour）
// 役割:
//   - ライフサイクル管理 (Awake/Start/Update/LateUpdate/OnDestroy)
//   - カメラオービット操作
//   - UI（UIToolkit によるステータス・ボタン表示）
//   - RemoteProjectReceiver / MeshSceneRenderer / PlayerEditOps の配線
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector設定
        // ================================================================

        [SerializeField] private PolyLingPlayerClient _client;

        /// <summary>UIDocument。未アサインの場合は Awake で自動生成する。</summary>
        [SerializeField] private UIDocument _uiDocument;

        /// <summary>メッシュ生成の親Transform。nullの場合はthis.transform</summary>
        [SerializeField] private Transform _sceneRoot;

        /// <summary>カメラオービット対象カメラ。nullの場合はCamera.main</summary>
        [SerializeField] private Camera _camera;

        // ================================================================
        // 描画フラグ（Inspector）
        // ================================================================

        [Header("Display Flags")]
        [SerializeField] private bool _showSelectedMesh            = true;
        [SerializeField] private bool _showUnselectedMesh          = true;
        [SerializeField] private bool _showSelectedVertices        = true;
        [SerializeField] private bool _showUnselectedVertices      = true;
        [SerializeField] private bool _showSelectedWireframe       = true;
        [SerializeField] private bool _showUnselectedWireframe     = true;
        [SerializeField] private bool _showSelectedBone            = true;
        [SerializeField] private bool _showUnselectedBone          = false;

        [Header("Rendering")]
        [SerializeField] private bool _backfaceCullingEnabled      = true;

        // ================================================================
        // サブクラス
        // ================================================================

        private RemoteProjectReceiver      _receiver;
        private MeshSceneRenderer          _renderer;
        private readonly UndoManager       _undoManager  = UndoManager.CreateNew();
        private PlayerEditOps              _editOps;
        private readonly PlayerLocalLoader _localLoader  = new PlayerLocalLoader();
        private readonly OrbitCameraController _orbit    = new OrbitCameraController();

        // ================================================================
        // ステータス
        // ================================================================

        private string _status = "未接続";
        private int    _fetchingModelIndex = -1;
        private int    _modelCount;

        // ================================================================
        // UI 要素参照
        // ================================================================

        private Label         _statusLabel;
        private Button        _connectBtn;
        private Button        _disconnectBtn;
        private Button        _fetchBtn;
        private Button        _undoBtn;
        private Button        _redoBtn;
        private VisualElement _remoteSection;
        private VisualElement _modelListContainer;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            if (_sceneRoot == null) _sceneRoot = transform;

            // UIDocument を確保
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_uiDocument.panelSettings == null)
            {
                Debug.LogError("[PolyLingPlayerViewer] UIDocument に PanelSettings がアサインされていません。" +
                               " Inspector で UIDocument コンポーネントの PanelSettings を設定してください。");
            }
        }

        private void Start()
        {
            if (_client == null)
            {
                _client = GetComponent<PolyLingPlayerClient>();
                if (_client == null)
                    Debug.LogWarning("[PolyLingPlayerViewer] PolyLingPlayerClient が見つかりません。リモート機能は無効。");
            }

            _receiver = new RemoteProjectReceiver(); // WebSocket受信バイナリ → ProjectContext反映
            _renderer = new MeshSceneRenderer();     // ProjectContext を Graphics.DrawMesh で描画
            _editOps  = new PlayerEditOps(_undoManager);

            // ローカルローダーのコールバック配線
            _localLoader.OnStatusChanged = s => _status = s;
            _localLoader.OnLoaded = project =>
            {
                _renderer?.ClearScene();
                var meshList = project.Models[0].MeshContextList;
                for (int i = 0; i < meshList.Count; i++)
                {
                    var um = meshList[i].UnityMesh;
                    if (um != null)
                    {
                        _orbit.ResetToMesh(um.bounds);
                        break;
                    }
                }
                RebuildModelList();
            };

            // レンダラーにフラグを反映
            SyncRendererFlags();

            // 受信イベント配線
            _receiver.OnProjectHeaderReceived += OnProjectHeaderReceived;
            _receiver.OnModelMetaReceived     += OnModelMetaReceived;
            _receiver.OnMeshSummaryReceived   += OnMeshSummaryReceived;
            _receiver.OnMeshDataReceived      += OnMeshDataReceived;

            // _client が存在する場合のみ配線
            if (_client != null)
            {
                _client.OnConnected    += OnConnected;
                _client.OnDisconnected += OnDisconnected;
                _client.OnPushReceived += OnPushReceived;
            }

            // UI 構築（PanelSettings が設定済みの場合のみ）
            if (_uiDocument != null && _uiDocument.panelSettings != null)
                BuildUI(_uiDocument.rootVisualElement);
        }

        private void OnDestroy()
        {
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
            _editOps?.Tick();
            _orbit.Update(ActiveCamera, IsPointerOverUI());
            SyncRendererFlags();
            SyncUI();
        }

        private void LateUpdate()
        {
            var project = ActiveProject;
            var cam = ActiveCamera;
            _renderer?.DrawMeshes(project, cam);
            _renderer?.DrawWireframeAndVertices(cam);
            _renderer?.DrawBones(project, cam);
        }

        // ================================================================
        // カメラ / プロジェクト
        // ================================================================

        private Camera ActiveCamera => _camera != null ? _camera : Camera.main;

        /// <summary>表示対象プロジェクト。ローカルロード優先、なければリモート受信分。</summary>
        private ProjectContext ActiveProject => _localLoader.Project ?? _receiver?.Project;

        /// <summary>ポインタが UIDocument のパネル上にあるか判定する。</summary>
        private bool IsPointerOverUI()
        {
            if (_uiDocument == null) return false;
            var panel = _uiDocument.rootVisualElement?.panel;
            if (panel == null) return false;
            Vector2 screenPos = Input.mousePosition;
            // UIToolkit のスクリーン座標は Y 軸反転
            var uiPos = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = panel.Pick(uiPos);
            return picked != null;
        }

        // ================================================================
        // UI 構築（UIToolkit）
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            // パネル全体（左上に固定）
            var panel = new VisualElement();
            panel.style.position        = Position.Absolute;
            panel.style.top             = 10;
            panel.style.left            = 10;
            panel.style.width           = 260;
            panel.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            panel.style.paddingTop      = 8;
            panel.style.paddingBottom   = 8;
            panel.style.paddingLeft     = 8;
            panel.style.paddingRight    = 8;
            root.Add(panel);

            // ステータス
            _statusLabel = new Label($"Status: {_status}");
            _statusLabel.style.marginBottom = 6;
            panel.Add(_statusLabel);

            // ローカルロードセクション
            var loaderSection = new VisualElement();
            loaderSection.style.marginBottom = 6;
            _localLoader.BuildUI(loaderSection);
            panel.Add(loaderSection);

            // リモートセクション（_client が null でも構築、SyncUI で表示制御）
            _remoteSection = new VisualElement();

            _connectBtn = new Button(() => _client?.Connect()) { text = "Connect" };
            _connectBtn.style.marginBottom = 4;
            _remoteSection.Add(_connectBtn);

            _disconnectBtn = new Button(() => _client?.Disconnect()) { text = "Disconnect" };
            _disconnectBtn.style.marginBottom = 4;
            _remoteSection.Add(_disconnectBtn);

            _fetchBtn = new Button(() => FetchProject()) { text = "Fetch Project" };
            _fetchBtn.style.marginBottom = 4;
            _remoteSection.Add(_fetchBtn);

            var undoRedoRow = new VisualElement();
            undoRedoRow.style.flexDirection = FlexDirection.Row;
            undoRedoRow.style.marginBottom  = 4;

            _undoBtn = new Button(() => _editOps?.PerformUndo()) { text = "Undo" };
            _undoBtn.style.flexGrow    = 1;
            _undoBtn.style.marginRight = 2;

            _redoBtn = new Button(() => _editOps?.PerformRedo()) { text = "Redo" };
            _redoBtn.style.flexGrow   = 1;
            _redoBtn.style.marginLeft = 2;

            undoRedoRow.Add(_undoBtn);
            undoRedoRow.Add(_redoBtn);
            _remoteSection.Add(undoRedoRow);

            panel.Add(_remoteSection);

            // モデルリスト
            _modelListContainer = new VisualElement();
            panel.Add(_modelListContainer);

            // フッター（右下固定）
            var footer = new Label("右ドラッグ: 回転 / 中ドラッグ: 移動 / スクロール: ズーム");
            footer.style.position        = Position.Absolute;
            footer.style.bottom          = 10;
            footer.style.left            = 10;
            footer.style.color           = new StyleColor(Color.white);
            footer.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.45f));
            footer.style.paddingLeft     = 4;
            footer.style.paddingRight    = 4;
            root.Add(footer);
        }

        /// <summary>毎フレーム UI の有効状態・表示を同期する。</summary>
        private void SyncUI()
        {
            if (_statusLabel != null)
                _statusLabel.text = $"Status: {_status}";

            bool clientExists  = _client != null;
            bool isConnected   = clientExists && _client.IsConnected;

            if (_remoteSection != null)
                _remoteSection.style.display = clientExists ? DisplayStyle.Flex : DisplayStyle.None;

            if (_connectBtn    != null) _connectBtn.style.display    = isConnected ? DisplayStyle.None : DisplayStyle.Flex;
            if (_disconnectBtn != null) _disconnectBtn.style.display  = isConnected ? DisplayStyle.Flex : DisplayStyle.None;
            if (_fetchBtn      != null) _fetchBtn.SetEnabled(isConnected);

            if (_undoBtn != null) _undoBtn.SetEnabled(_editOps != null && _editOps.CanUndo);
            if (_redoBtn != null) _redoBtn.SetEnabled(_editOps != null && _editOps.CanRedo);
        }

        /// <summary>ActiveProject のモデル一覧を _modelListContainer に再構築する。</summary>
        private void RebuildModelList()
        {
            if (_modelListContainer == null) return;
            _modelListContainer.Clear();

            var project = ActiveProject;
            if (project == null) return;

            var header = new Label($"Project: {project.Name}  Models: {project.ModelCount}");
            header.style.marginTop    = 6;
            header.style.marginBottom = 2;
            _modelListContainer.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var entry = new Label($"  [{mi}] {model.Name}  Meshes: {model.Count}");
                _modelListContainer.Add(entry);
            }
        }

        // ================================================================
        // クライアントイベント
        // ================================================================

        private void OnConnected()
        {
            _status = "接続済み";
            Debug.Log("[PolyLingPlayerViewer] 接続");
        }

        private void OnDisconnected()
        {
            _status = "切断";
            _fetchingModelIndex = -1;
            Debug.Log("[PolyLingPlayerViewer] 切断");
        }

        private void OnPushReceived(string json)
        {
            if (json.Contains("\"event\":\"mesh_changed\"") ||
                json.Contains("\"event\":\"model_changed\""))
            {
                Debug.Log("[PolyLingPlayerViewer] Push受信 → 再フェッチ");
                FetchProject();
            }
        }

        // ================================================================
        // 受信イベント
        // ================================================================

        private void OnProjectHeaderReceived(ProjectContext project)
        {
            _modelCount = project.ModelCount;
            _fetchingModelIndex = -1;
            _renderer?.ClearScene();
            RebuildModelList();
        }

        private void OnModelMetaReceived(int mi, ModelContext model) { }

        private void OnMeshSummaryReceived(int mi, int si, Data.MeshContext mc) { }

        private void OnMeshDataReceived(int mi, int si, Data.MeshContext mc)
        {
            var project = _receiver?.Project;
            if (project == null) return;

            // 初回メッシュでカメラ位置を初期化
            if (mi == 0 && si == 0 && mc.UnityMesh != null)
                _orbit.ResetToMesh(mc.UnityMesh.bounds);

            RebuildModelList();
        }

        // ================================================================
        // フェッチフロー
        // ================================================================

        private void FetchProject()
        {
            if (!_client.IsConnected) return;
            _localLoader.Clear();
            _status = "project_header フェッチ中...";
            _receiver?.Reset();
            _modelCount = 0;
            _fetchingModelIndex = -1;
            _renderer?.ClearScene();

            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4) { _status = "project_header 失敗"; return; }
                _receiver?.ProcessBatch(bin);
                if (_modelCount > 0)
                    FetchAllModelsBatch(0);
            });
        }

        private void FetchAllModelsBatch(int mi)
        {
            if (mi >= _modelCount) return;
            _fetchingModelIndex = mi;
            _status = $"メッシュフェッチ中... [{mi}/{_modelCount - 1}]";

            FetchMeshDataBatch(mi, "bone", () =>
            FetchMeshDataBatch(mi, "drawable", () =>
            FetchMeshDataBatch(mi, "morph", () =>
            {
                var project = _receiver?.Project;
                if (project != null && mi < project.ModelCount)
                    _renderer?.RebuildAdapter(mi, project.Models[mi]);

                int next = mi + 1;
                if (next < _modelCount)
                    FetchAllModelsBatch(next);
                else
                    _status = $"完了 ({project?.Name})";
            })));
        }

        private void FetchMeshDataBatch(int mi, string cat, Action done)
        {
            _client.FetchMeshDataBatch(mi, cat, (json, bin) =>
            {
                if (bin != null && bin.Length >= 4)
                    _receiver?.ProcessBatch(bin);
                done?.Invoke();
            });
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SyncRendererFlags()
        {
            if (_renderer == null) return;
            _renderer.ShowSelectedMesh        = _showSelectedMesh;
            _renderer.ShowUnselectedMesh      = _showUnselectedMesh;
            _renderer.ShowSelectedVertices    = _showSelectedVertices;
            _renderer.ShowUnselectedVertices  = _showUnselectedVertices;
            _renderer.ShowSelectedWireframe   = _showSelectedWireframe;
            _renderer.ShowUnselectedWireframe = _showUnselectedWireframe;
            _renderer.ShowSelectedBone        = _showSelectedBone;
            _renderer.ShowUnselectedBone      = _showUnselectedBone;
            _renderer.BackfaceCullingEnabled  = _backfaceCullingEnabled;
        }
    }
}
