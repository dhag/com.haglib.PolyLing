// ListClientBase.cs
// 軽量リストクライアントの共通基底（MonoBehaviour）。
// endpoint.json 探索 → WebSocket 接続 → project_header 1回フェッチ →
// RemoteProjectReceiver で ProjectContext を復元 → 現行メインパネルの実サブパネルを
// PanelContext + PlayerProjectView 経由で駆動する。
//
// 描画メッシュ(MeshData)は取得しない。表示専用のため SendCommand は no-op。
//
// 使い方: 空の GameObject に派生コンポーネント(ModelListClient / MaterialListClient /
//         MeshListClient) のいずれか1つをアタッチする。UIDocument は自動付与。
//         PanelSettings は Inspector で割当てるか Resources/PolyLingListClient/PanelSettings を配置。

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Player;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.ListClient
{
    [RequireComponent(typeof(UIDocument))]
    public abstract class ListClientBase : MonoBehaviour
    {
        // ================================================================
        // 設定
        // ================================================================

        [Tooltip("endpoint.json が見つからない/未接続時の再試行間隔(秒)")]
        [SerializeField] private float _retrySeconds = 1.0f;

        [Tooltip("自動リフレッシュ間隔(秒)。0 で無効(push受信時のみ再取得)")]
        [SerializeField] private float _autoRefreshSeconds = 0f;

        // ================================================================
        // 依存
        // ================================================================

        private UIDocument            _doc;
        private PolyLingPlayerClient  _client;
        private RemoteProjectReceiver _receiver;
        private PanelContext          _panelContext;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>復元済みプロジェクト(リスト源)。派生から参照する。</summary>
        protected ProjectContext Project { get; private set; }

        private float _retryTimer;
        private float _autoTimer;
        private float _awaitTimer;
        private bool  _awaitingConnect;
        private bool  _chromeBuilt;

        private Label         _statusLabel;
        private VisualElement _host;

        // ================================================================
        // 派生フック
        // ================================================================

        /// <summary>host に現行サブパネルを構築し、ctx へ結線する。</summary>
        protected abstract void BuildPanel(VisualElement host, PanelContext ctx);

        /// <summary>View 反映後の追加処理(例: MaterialサブパネルのRefresh)。</summary>
        protected virtual void OnViewPushed() { }

        /// <summary>破棄時の後始末(例: MeshサブパネルのDetach)。</summary>
        protected virtual void OnTeardown() { }

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected virtual void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            EnsurePanelSettings();

            _receiver = new RemoteProjectReceiver();

            // 表示専用: 編集コマンドはサーバへ送らない(no-op)。
            _panelContext = new PanelContext(_ => { });

            _client = new PolyLingPlayerClient();
            _client.OnConnected    += HandleConnected;
            _client.OnDisconnected += HandleDisconnected;
            _client.OnPushReceived += HandlePush;
        }

        protected virtual void OnDestroy()
        {
            OnTeardown();
            _client?.Dispose();
        }

        protected virtual void Update()
        {
            _client?.Tick();
            float dt = Time.unscaledDeltaTime;

            bool connected = _client != null && _client.IsConnected;
            if (connected)
            {
                _awaitingConnect = false;

                if (_autoRefreshSeconds > 0f)
                {
                    _autoTimer -= dt;
                    if (_autoTimer <= 0f)
                    {
                        _autoTimer = _autoRefreshSeconds;
                        RefreshData();
                    }
                }
            }
            else if (_awaitingConnect)
            {
                // 接続失敗時はコールバックが無い場合があるためタイムアウトで打ち切る。
                _awaitTimer -= dt;
                if (_awaitTimer <= 0f) _awaitingConnect = false;
            }
            else
            {
                _retryTimer -= dt;
                if (_retryTimer <= 0f)
                {
                    _retryTimer = _retrySeconds;
                    TryConnect();
                }
            }

            if (!_chromeBuilt) BuildChrome();
        }

        // ================================================================
        // 接続
        // ================================================================

        private void TryConnect()
        {
            if (!EndpointLocator.TryLocate(out string host, out int port, out string _))
            {
                SetStatus("endpoint.json 待機中...");
                return;
            }

            _awaitingConnect = true;
            _awaitTimer = Mathf.Max(3f, _retrySeconds * 3f);
            SetStatus($"接続中... {host}:{port}");
            _client.Initialize(host, port, autoConnect: true);
        }

        private void HandleConnected()
        {
            _awaitingConnect = false;
            SetStatus("接続済");
            RefreshData();
        }

        private void HandleDisconnected()
        {
            _awaitingConnect = false;
            SetStatus("切断");
        }

        private void HandlePush(string json)
        {
            // 一覧変更等の push を契機に再取得(project_header はメタのみで軽量)。
            RefreshData();
        }

        // ================================================================
        // データ取得 → View 反映
        // ================================================================

        private void RefreshData()
        {
            if (_client == null || !_client.IsConnected) return;

            _receiver.Reset();
            _client.FetchProjectHeader((json, bin) =>
            {
                if (bin == null || bin.Length < 4)
                {
                    SetStatus("project_header 失敗");
                    return;
                }
                _receiver.ProcessBatch(bin);
                Project = _receiver.Project;
                if (Project != null)
                    SetStatus($"接続済 : {Project.Name} ({Project.ModelCount} models)");

                if (_chromeBuilt) PushView();
            });
        }

        private void PushView()
        {
            if (Project == null) return;
            _panelContext.Notify(new PlayerProjectView(Project), ChangeKind.ModelSwitch);
            OnViewPushed();
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildChrome()
        {
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return; // UIDocument が root を生成するまで待つ

            root.Clear();
            root.style.flexGrow = 1;

            // 現行パネルと同じキャレット等スタイルを適用(存在すれば)。
            var caret = Resources.Load<StyleSheet>("PolyLingCaret");
            if (caret != null) root.styleSheets.Add(caret);

            _statusLabel = new Label("未接続");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.paddingLeft = 4;
            _statusLabel.style.paddingTop = 2;
            _statusLabel.style.paddingBottom = 2;
            root.Add(_statusLabel);

            _host = new VisualElement();
            _host.style.flexGrow = 1;
            root.Add(_host);

            BuildPanel(_host, _panelContext);
            _chromeBuilt = true;

            if (Project != null) PushView();
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }

        // ================================================================
        // PanelSettings
        // ================================================================

        private void EnsurePanelSettings()
        {
            if (_doc.panelSettings != null) return;

            var ps = Resources.Load<PanelSettings>("PolyLingListClient/PanelSettings");
            if (ps != null) { _doc.panelSettings = ps; return; }

            Debug.LogError(
                "[PolyLingListClient] UIDocument に PanelSettings が未設定です。" +
                "Inspector で割当てるか、Resources/PolyLingListClient/PanelSettings.asset を配置してください。");
        }
    }
}
