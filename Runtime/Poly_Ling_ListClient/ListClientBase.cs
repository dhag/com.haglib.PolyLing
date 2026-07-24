// ListClientBase.cs
// 軽量リストクライアントの共通基底（MonoBehaviour）。
// endpoint.json 探索 → WebSocket 接続 → project_header 1回フェッチ →
// RemoteProjectReceiver で ProjectContext を復元 → 各派生がリスト描画。
//
// 描画メッシュ(MeshData)は取得しない。頂点数/面数は MeshSummary から取得する
// (RemoteProjectReceiver.OnMeshSummaryCounts)。
//
// 使い方: 空の GameObject に派生コンポーネント(ModelListClient / MaterialListClient /
//         MeshListClient) のいずれか1つをアタッチする。UIDocument は自動付与。
//         PanelSettings は Inspector で割当てるか Resources/PolyLingListClient/PanelSettings を配置。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Player;
using Poly_Ling.Remote;
using Poly_Ling.Context;

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

        private UIDocument             _doc;
        private PolyLingPlayerClient   _client;
        private RemoteProjectReceiver  _receiver;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>復元済みプロジェクト(リスト源)。</summary>
        protected ProjectContext Project { get; private set; }

        /// <summary>Summary から得た (modelIndex, meshIndex) 毎の (頂点数, 面数)。</summary>
        protected readonly Dictionary<(int mi, int si), (int vc, int fc)> Counts
            = new Dictionary<(int, int), (int, int)>();

        /// <summary>モデル選択(セレクタ使用時)。既定は CurrentModelIndex。</summary>
        protected int SelectedModelIndex { get; private set; }

        private float _retryTimer;
        private float _autoTimer;
        private float _awaitTimer;
        private bool  _awaitingConnect;
        private bool  _chromeBuilt;

        // UI 参照
        private Label        _statusLabel;
        private DropdownField _modelDropdown;
        private VisualElement _listRoot;

        // ================================================================
        // 派生がオーバーライドするフック
        // ================================================================

        /// <summary>ウィンドウ上部に表示するタイトル。</summary>
        protected abstract string PanelTitle { get; }

        /// <summary>モデル選択ドロップダウンを使うか(Material/Mesh は true)。</summary>
        protected virtual bool UsesModelSelector => false;

        /// <summary>listRoot に行を構築する。Project/SelectedModelIndex/Counts を参照する。</summary>
        protected abstract void BuildRows(VisualElement listRoot);

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected virtual void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            EnsurePanelSettings();

            _receiver = new RemoteProjectReceiver();
            _receiver.OnMeshSummaryCounts += (mi, si, vc, fc) => Counts[(mi, si)] = (vc, fc);

            _client = new PolyLingPlayerClient();
            _client.OnConnected    += HandleConnected;
            _client.OnDisconnected += HandleDisconnected;
            _client.OnPushReceived += HandlePush;
        }

        protected virtual void OnDestroy()
        {
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

                // 自動リフレッシュ
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
                // 接続要求中。失敗時はコールバックが無い場合があるためタイムアウトで打ち切る。
                _awaitTimer -= dt;
                if (_awaitTimer <= 0f) _awaitingConnect = false;
            }
            else
            {
                // 未接続かつ待機外: 一定間隔で endpoint.json 再探索→接続
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
            if (!EndpointLocator.TryLocate(out string host, out int port, out string path))
            {
                SetStatus("endpoint.json 待機中...");
                return;
            }

            _awaitingConnect = true;
            _awaitTimer = Mathf.Max(3f, _retrySeconds * 3f);
            SetStatus($"接続中... {host}:{port}");
            // Initialize はメインスレッドから(ここは Update=メインスレッド)。autoConnect=true。
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
        // データ取得
        // ================================================================

        private void RefreshData()
        {
            if (_client == null || !_client.IsConnected) return;

            Counts.Clear();
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
                {
                    if (SelectedModelIndex < 0 || SelectedModelIndex >= Project.ModelCount)
                        SelectedModelIndex = Mathf.Clamp(Project.CurrentModelIndex, 0,
                            Mathf.Max(0, Project.ModelCount - 1));
                    SetStatus($"接続済 : {Project.Name} ({Project.ModelCount} models)");
                }
                RebuildList();
            });
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
            root.style.paddingLeft = 6; root.style.paddingRight = 6;
            root.style.paddingTop = 6;  root.style.paddingBottom = 6;

            var title = new Label(PanelTitle);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.marginBottom = 4;
            root.Add(title);

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 4;

            _statusLabel = new Label("未接続");
            _statusLabel.style.flexGrow = 1;
            _statusLabel.style.fontSize = 11;
            bar.Add(_statusLabel);

            var refreshBtn = new Button(RefreshData) { text = "更新" };
            bar.Add(refreshBtn);
            root.Add(bar);

            if (UsesModelSelector)
            {
                _modelDropdown = new DropdownField("Model");
                _modelDropdown.choices = new List<string>();
                _modelDropdown.RegisterValueChangedCallback(_ =>
                {
                    if (_modelDropdown.index >= 0)
                    {
                        SelectedModelIndex = _modelDropdown.index;
                        RebuildList();
                    }
                });
                _modelDropdown.style.marginBottom = 4;
                root.Add(_modelDropdown);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            _listRoot = scroll.contentContainer;

            _chromeBuilt = true;
            RebuildList();
        }

        private void RebuildList()
        {
            if (!_chromeBuilt || _listRoot == null) return;

            // モデルドロップダウン更新
            if (UsesModelSelector && _modelDropdown != null && Project != null)
            {
                var names = new List<string>();
                for (int i = 0; i < Project.ModelCount; i++)
                    names.Add($"[{i}] {Project.Models[i].Name}");
                _modelDropdown.choices = names;
                if (names.Count > 0)
                    _modelDropdown.SetValueWithoutNotify(names[Mathf.Clamp(SelectedModelIndex, 0, names.Count - 1)]);
            }

            _listRoot.Clear();
            if (Project == null)
            {
                _listRoot.Add(new Label("(データなし)"));
                return;
            }
            BuildRows(_listRoot);
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }

        // ================================================================
        // 派生向けユーティリティ
        // ================================================================

        /// <summary>行コンテナ(横並び・下罫線)を生成する。</summary>
        protected static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2; row.style.paddingBottom = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1, 1, 1, 0.08f);
            return row;
        }

        /// <summary>固定幅ラベル。</summary>
        protected static Label MakeCell(string text, float width, bool grow = false)
        {
            var l = new Label(text);
            l.style.fontSize = 12;
            if (grow) l.style.flexGrow = 1;
            else { l.style.width = width; l.style.flexShrink = 0; }
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
            return l;
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
