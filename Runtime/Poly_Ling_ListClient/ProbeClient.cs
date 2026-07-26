// ProbeClient.cs
// 最小疎通テスト用クライアント。
// リスト系クライアント（Model/Mesh/Material）とは別データ（server_info）を授受し、
// クライアントタイプ登録とタイプ宛 push の振り分けを実証する。
//
// しくみ:
//   - endpoint.json を探索 → WebSocket 接続。
//   - 接続後 RegisterClientType("probe", userName) で自タイプを登録。
//   - server_info を query（テキスト応答）し、port/modelCount/currentModelIndex/
//     clientCount/serverTime を表示（リスト系は取得しないデータ）。
//   - サーバは一覧変更時に "probe" タイプにのみ serverInfoChanged を push する。
//     本クライアントはそれを受けて表示を更新する。list 系にはこの push は届かない。
//
// 使い方: 空の GameObject に本コンポーネントをアタッチする（list 系と同形）。
//         UIDocument は自動付与。PanelSettings は list 系と同じ Resources を再利用。

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Player;

namespace Poly_Ling.ListClient
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProbeClient : MonoBehaviour
    {
        // ================================================================
        // 設定
        // ================================================================

        [Tooltip("endpoint.json が見つからない/未接続時の再試行間隔(秒)")]
        [SerializeField] private float _retrySeconds = 1.0f;

        [Tooltip("サーバへ登録するユーザー名。既定は空（名前なし）。将来の協働開発向け。")]
        [SerializeField] private string _userName = "";

        // ================================================================
        // 依存 / 状態
        // ================================================================

        private UIDocument           _doc;
        private PolyLingPlayerClient _client;

        private float _retryTimer;
        private float _awaitTimer;
        private bool  _awaitingConnect;
        private bool  _chromeBuilt;

        private Label _statusLabel;
        private Label _portLabel;
        private Label _modelCountLabel;
        private Label _curModelLabel;
        private Label _clientCountLabel;
        private Label _serverTimeLabel;
        private Label _lastEventLabel;

        // chrome 構築前に届いた最新 server_info を保持し、構築後に反映する。
        private string _pendingInfoJson;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
            EnsurePanelSettings();

            _client = new PolyLingPlayerClient();
            _client.OnConnected    += HandleConnected;
            _client.OnDisconnected += HandleDisconnected;
            _client.OnPushReceived += HandlePush;
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        private void Update()
        {
            _client?.Tick();
            float dt = Time.unscaledDeltaTime;

            bool connected = _client != null && _client.IsConnected;
            if (connected)
            {
                _awaitingConnect = false;
            }
            else if (_awaitingConnect)
            {
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
            SetStatus("接続済（probe 登録）");
            // 自タイプを登録してから server_info を取得する。
            _client.RegisterClientType("probe", _userName);
            RequestServerInfo();
        }

        private void HandleDisconnected()
        {
            _awaitingConnect = false;
            SetStatus("切断");
        }

        private void HandlePush(string json)
        {
            // "probe" タイプ宛の push のみ本クライアントに届く。
            if (ExtractStr(json, "event") == "serverInfoChanged")
                ApplyInfo(json);
        }

        // ================================================================
        // データ取得 → 表示
        // ================================================================

        private void RequestServerInfo()
        {
            if (_client == null || !_client.IsConnected) return;
            _client.SendQuery("server_info", null, ApplyInfo);
        }

        // 応答（response）でも push でも同じ JSON 形（data 内に各フィールド）を扱える。
        private void ApplyInfo(string json)
        {
            if (!_chromeBuilt) { _pendingInfoJson = json; return; }

            int port    = ExtractInt(json, "port", 0);
            int mCount  = ExtractInt(json, "modelCount", 0);
            int curIdx  = ExtractInt(json, "currentModelIndex", -1);
            int cCount  = ExtractInt(json, "clientCount", 0);
            string time = ExtractStr(json, "serverTime");

            _portLabel.text        = $"port: {port}";
            _modelCountLabel.text  = $"modelCount: {mCount}";
            _curModelLabel.text    = $"currentModelIndex: {curIdx}";
            _clientCountLabel.text = $"clientCount: {cCount}";
            _serverTimeLabel.text  = $"serverTime: {time}";

            string ev = ExtractStr(json, "event");
            _lastEventLabel.text = string.IsNullOrEmpty(ev)
                ? "last: (query応答)"
                : $"last push: {ev}";
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildChrome()
        {
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return;

            root.Clear();
            root.style.flexGrow = 1;
            root.style.backgroundColor = PlayerLayoutRoot.RightPaneBackgroundColor;

            var col = new VisualElement();
            col.style.paddingTop    = 6;
            col.style.paddingLeft   = 6;
            col.style.paddingRight  = 6;
            col.style.color         = new StyleColor(Color.white);
            root.Add(col);

            var title = new Label("Probe Client (server_info)");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            col.Add(title);

            _statusLabel      = AddLine(col, "未接続");
            _portLabel        = AddLine(col, "port: -");
            _modelCountLabel  = AddLine(col, "modelCount: -");
            _curModelLabel    = AddLine(col, "currentModelIndex: -");
            _clientCountLabel = AddLine(col, "clientCount: -");
            _serverTimeLabel  = AddLine(col, "serverTime: -");
            _lastEventLabel   = AddLine(col, "last: -");

            var refreshBtn = new Button(RequestServerInfo) { text = "再取得" };
            refreshBtn.style.marginTop = 6;
            col.Add(refreshBtn);

            _chromeBuilt = true;

            PlayerLayoutRoot.ApplyDarkTheme(root);

            if (_pendingInfoJson != null)
            {
                ApplyInfo(_pendingInfoJson);
                _pendingInfoJson = null;
            }
        }

        private static Label AddLine(VisualElement parent, string text)
        {
            var l = new Label(text);
            l.style.fontSize = 11;
            l.style.paddingTop = 1;
            l.style.paddingBottom = 1;
            parent.Add(l);
            return l;
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }

        // ================================================================
        // PanelSettings（list 系と同じ Resources を再利用）
        // ================================================================

        private void EnsurePanelSettings()
        {
            if (_doc.panelSettings != null) return;

            var ps = Resources.Load<PanelSettings>("PolyLingListClient/PanelSettings");
            if (ps != null) { _doc.panelSettings = ps; return; }

            Debug.LogError(
                "[ProbeClient] UIDocument に PanelSettings が未設定です。" +
                "Inspector で割当てるか、Resources/PolyLingListClient/PanelSettings.asset を配置してください。");
        }

        // ================================================================
        // 極小 JSON 抽出（応答/push は既知フォーマットのため軽量抽出で足りる）
        // ================================================================

        private static int ValueStart(string json, string key)
        {
            string s = "\"" + key + "\"";
            int i = json.IndexOf(s, System.StringComparison.Ordinal);
            if (i < 0) return -1;
            int c = json.IndexOf(':', i + s.Length);
            if (c < 0) return -1;
            int vs = c + 1;
            while (vs < json.Length && (json[vs] == ' ' || json[vs] == '\t')) vs++;
            return vs;
        }

        private static string ExtractStr(string json, string key)
        {
            int vs = ValueStart(json, key);
            if (vs < 0 || vs >= json.Length || json[vs] != '"') return "";
            int ve = json.IndexOf('"', vs + 1);
            if (ve < 0) return "";
            return json.Substring(vs + 1, ve - vs - 1);
        }

        private static int ExtractInt(string json, string key, int def)
        {
            int vs = ValueStart(json, key);
            if (vs < 0) return def;
            int e = vs;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-')) e++;
            return e > vs && int.TryParse(json.Substring(vs, e - vs), out int v) ? v : def;
        }
    }
}
