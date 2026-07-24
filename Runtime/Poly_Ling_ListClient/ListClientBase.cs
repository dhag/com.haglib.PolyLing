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

using System.Collections.Generic;
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

        // ================================================================
        // 依存
        // ================================================================

        private UIDocument            _doc;
        private PolyLingPlayerClient  _client;
        private RemoteProjectReceiver _receiver;
        private PanelContext          _panelContext;
        private PanelCommandRouter    _router;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>復元済みプロジェクト(リスト源)。派生から参照する。</summary>
        protected ProjectContext Project { get; private set; }

        private float _retryTimer;
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

            _client = new PolyLingPlayerClient();
            _client.OnConnected    += HandleConnected;
            _client.OnDisconnected += HandleDisconnected;
            _client.OnPushReceived += HandlePush;

            // パネル操作をサーバへ送るルータ。サーバ対応コマンドのみ送信。
            _router = new PanelCommandRouter(_client);
            _panelContext = new PanelContext(cmd => _router?.Send(cmd));
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
                // 接続中はポーリングしない（更新は push 契機のみ）。
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
            // selectionChanged はインライン反映（再フェッチ不要・軽量）。
            // それ以外（meshListChanged 等の構造変更）は project_header 再取得。
            string ev = ExtractStr(json, "event");
            if (ev == "selectionChanged")
                ApplySelectionPush(json);
            else
                RefreshData();
        }

        // サーバの selectionChanged push を受信済み ProjectContext へ反映する。
        private void ApplySelectionPush(string json)
        {
            if (Project == null) return;

            int mi = ExtractInt(json, "modelIndex", -1);
            if (mi < 0 || mi >= Project.ModelCount) return;

            bool modelChanged = mi != Project.CurrentModelIndex;

            var model = Project.Models[mi];
            int cat = ExtractInt(json, "category", 0);

            model.SelectedDrawableMeshIndices = ParseCsv(ExtractStr(json, "drawable"));
            model.SelectedBoneIndices         = ParseCsv(ExtractStr(json, "bone"));
            model.SelectedMorphIndices        = ParseCsv(ExtractStr(json, "morph"));
            model.SetActiveCategory((ModelContext.SelectionCategory)cat);

            Project.CurrentModelIndex = mi;

            // 選択のみの変更は Selection（ツリー再構築・トグルリセットを伴わない）。
            // モデルが変わった場合のみ ModelSwitch でツリー再構築。
            if (_chromeBuilt)
                PushView(modelChanged ? ChangeKind.ModelSwitch : ChangeKind.Selection);
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

                // 全体取得はツリー構築を伴う ModelSwitch。
                if (_chromeBuilt) PushView(ChangeKind.ModelSwitch);
            });
        }

        private void PushView(ChangeKind kind)
        {
            if (Project == null) return;
            _panelContext.Notify(new PlayerProjectView(Project), kind);
            OnViewPushed();

            // サーバと同一の意匠を再適用（サブパネル再構築で新規生成された制御に暗テーマを掛ける）。
            ApplyDarkTheme();
            _doc?.rootVisualElement?.schedule.Execute(ApplyDarkTheme).ExecuteLater(1);
        }

        // サーバの共有テーマ機構（PlayerLayoutRoot.ApplyDarkTheme）を再利用する。
        private void ApplyDarkTheme()
        {
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root != null) PlayerLayoutRoot.ApplyDarkTheme(root);
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
            // サーバ右ペインと同一の暗背景（スカイボックス透過を防ぐ・単一ソース）。
            root.style.backgroundColor = PlayerLayoutRoot.RightPaneBackgroundColor;

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

            ApplyDarkTheme();
            if (Project != null) PushView(ChangeKind.ModelSwitch);
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }

        // ================================================================
        // 極小 JSON 抽出（push は既知フォーマットのため軽量抽出で足りる）
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

        private static List<int> ParseCsv(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (var part in csv.Split(','))
                if (int.TryParse(part.Trim(), out int n)) list.Add(n);
            return list;
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
