// PolyLingPlayerClient.cs
// Runtime用 WebSocketクライアント（通常クラス版）
// PolyLingPlayerViewer にサブシステムとして格納する。
// Runtime/Poly_Ling_Player/Remote/ に配置

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using HagLib.NET.Duplex;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerClient
    {
        // ================================================================
        // 設定
        // ================================================================

        private string _host        = "127.0.0.1";
        private int    _port        = 8765;
        private bool   _autoConnect = true;

        // ================================================================
        // 状態
        // ================================================================

        public bool IsConnected => _client?.IsConnected ?? false;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>フェッチ以外のpushイベント受信時</summary>
        public Action<string> OnPushReceived;
        /// <summary>クエリ相関の取れない（サーバからの一方的な）バイナリ受信通知。連動の位置更新用。</summary>
        public Action<byte[]> OnBinaryPushReceived;
        public Action         OnConnected;
        public Action         OnDisconnected;

        // ================================================================
        // 内部
        // ================================================================

        private WebSocketDuplexClient            _client;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        // Phase 1: Tick による毎フレームポーリング禁止のため、
        // 背景スレッドからのメインスレッドディスパッチは SynchronizationContext 経由で行う。
        // Initialize で UnitySynchronizationContext をキャプチャする。
        private SynchronizationContext           _syncCtx;

        // リクエスト管理
        private int    _requestId;
        private string _lastTextResponseId;
        private string _lastTextResponseJson;
        private readonly Dictionary<string, Action<string, byte[]>> _binaryCallbacks
            = new Dictionary<string, Action<string, byte[]>>();
        private readonly Dictionary<string, Action<string>> _textCallbacks
            = new Dictionary<string, Action<string>>();

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        /// <summary>
        /// PolyLingPlayerViewer.Start() から呼ぶ。
        /// </summary>
        public void Initialize(string host, int port, bool autoConnect)
        {
            _host        = host;
            _port        = port;
            _autoConnect = autoConnect;
            // Initialize はメインスレッドから呼ばれる想定。
            // ここで UnitySynchronizationContext をキャプチャして、
            // 背景スレッドからのディスパッチに使う。
            _syncCtx = SynchronizationContext.Current;
            if (_autoConnect) Connect();
        }

        /// <summary>
        /// 背景スレッドからメインスレッドへ action を event 駆動でディスパッチする。
        /// SynchronizationContext が使えない場合（未初期化等）はフォールバックとして
        /// 従来の _mainThreadQueue に積む（Tick 経由の処理には非対応のため
        /// 実質的にはメインスレッドから呼ばれた場合のみ到達する想定）。
        /// </summary>
        private void RunOnMainThread(Action action)
        {
            if (action == null) return;
            if (_syncCtx != null)
            {
                _syncCtx.Post(_ =>
                {
                    try { action(); }
                    catch (Exception ex) { Debug.LogError($"[PolyLingPlayerClient] 実行エラー: {ex.Message}"); }
                }, null);
            }
            else
            {
                _mainThreadQueue.Enqueue(action);
            }
        }

        /// <summary>
        /// PolyLingPlayerViewer.OnDestroy() から呼ぶ。
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }

        /// <summary>
        /// PolyLingPlayerViewer.Update() から毎フレーム呼ぶ。
        /// メインスレッドへのディスパッチキューを処理する。
        /// </summary>
        public void Tick()
        {
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < 20)
            {
                try { action(); }
                catch (Exception ex) { Debug.LogError($"[PolyLingPlayerClient] キューエラー: {ex.Message}"); }
                processed++;
            }
        }

        // ================================================================
        // 接続 / 切断
        // ================================================================

        public void Connect()
        {
            if (_client != null && _client.IsConnected) return;

            _client = new WebSocketDuplexClient
            {
                DefaultFrame = WebSocketFrameKind.Text,
            };
            _client.OnReceived     += OnDuplexReceived;
            _client.OnDisconnected += _ => RunOnMainThread(() => OnDisconnected?.Invoke());

            _ = ConnectInternalAsync();
        }

        private async Task ConnectInternalAsync()
        {
            try
            {
                await _client.ConnectAsync($"ws://{_host}:{_port}/");
                RunOnMainThread(() => OnConnected?.Invoke());
            }
            catch (Exception ex)
            {
                RunOnMainThread(() => Debug.LogWarning($"[PolyLingPlayerClient] 接続失敗: {ex.Message}"));
            }
        }

        public void Disconnect()
        {
            try { _ = _client?.CloseAsync(); } catch { }
            _client = null;
        }

        // ================================================================
        // 受信（WebSocketDuplexClient.OnReceived）
        // ================================================================

        /// <summary>
        /// DuplexChannel の受信ハンドラ（背景スレッド）。
        /// 1メッセージ内の Json アイテムを先に、Binary アイテムを後に、
        /// 既存の HandleTextMessage / HandleBinaryMessage へ順に流す。
        /// これにより従来の「Text応答→Binaryフレーム」順序に依存した相関
        /// （_lastTextResponseId 方式）がそのまま機能する。
        /// </summary>
        private void OnDuplexReceived(IDuplexChannel channel, DuplexMessage message)
        {
            TypedPayload tp;
            try { tp = message.ToTypedPayload(); }
            catch { return; }

            var texts = new List<string>();
            var bins  = new List<byte[]>();
            foreach (var it in tp)
            {
                if (it.Type == ContentType.Json || it.Type == ContentType.Text)
                    texts.Add(it.DataString ?? "");
                else
                    bins.Add(it.Data);
            }

            RunOnMainThread(() =>
            {
                foreach (var t in texts) if (!string.IsNullOrEmpty(t)) HandleTextMessage(t);
                foreach (var b in bins)  if (b != null)                HandleBinaryMessage(b);
            });
        }

        // ================================================================
        // メッセージ処理
        // ================================================================

        private void HandleTextMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            string id   = ExtractJsonString(json, "id");
            string type = ExtractJsonString(json, "type");

            if (type == "push")
            {
                Debug.Log($"[PolyLingPlayerClient] Push: {ExtractJsonString(json, "event")}");
                OnPushReceived?.Invoke(json);
                return;
            }

            if (id != null && _binaryCallbacks.ContainsKey(id))
            {
                _lastTextResponseId   = id;
                _lastTextResponseJson = json;
                return;
            }

            if (id != null && _textCallbacks.TryGetValue(id, out var cb))
            {
                _textCallbacks.Remove(id);
                cb(json);
            }
        }

        private void HandleBinaryMessage(byte[] data)
        {
            if (_lastTextResponseId != null &&
                _binaryCallbacks.TryGetValue(_lastTextResponseId, out var cb))
            {
                _binaryCallbacks.Remove(_lastTextResponseId);
                cb(_lastTextResponseJson, data);
                _lastTextResponseId   = null;
                _lastTextResponseJson = null;
                return;
            }

            // クエリ相関が取れないバイナリ = サーバからの一方的 push（位置連動等）。
            if (OnBinaryPushReceived != null)
            {
                OnBinaryPushReceived.Invoke(data);
                return;
            }
            Debug.Log($"[PolyLingPlayerClient] 未対応バイナリ受信 ({data.Length}B)");
        }

        // ================================================================
        // 低レベル送信
        // ================================================================

        public void SendText(string message)
        {
            if (!IsConnected) return;
            Debug.Log($"[CLI→SRV] TEXT ({message.Length}B): {message.Substring(0, Math.Min(200, message.Length))}");
            _ = _client.SendAsync(TypedPayload.FromJson(message).ToMessage(), WebSocketFrameKind.Text);
        }

        public void SendBinary(byte[] data)
        {
            if (!IsConnected || data == null) return;
            Debug.Log($"[CLI→SRV] BINARY ({data.Length}B)");
            _client.SendAsync(TypedPayload.FromBinary(data).ToMessage(), WebSocketFrameKind.Binary)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.LogWarning($"[EditSync] SendBinary faulted: {t.Exception?.GetBaseException().Message}");
                    else
                        Debug.Log("[EditSync] SendBinary done");
                });
        }

        // ================================================================
        // フェッチAPI
        // ================================================================

        public void FetchProjectHeader(Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"project_header\"}}",
                onResponse);
        }

        public void FetchModelMeta(int modelIndex, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"model_meta\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\"}}}}",
                onResponse);
        }

        public void FetchMeshData(int modelIndex, int meshIndex, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"meshIndex\":\"{meshIndex}\"}}}}",
                onResponse);
        }

        public void FetchMeshDataBatch(int modelIndex, string category, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data_batch\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"category\":\"{category}\"}}}}",
                onResponse);
        }

        public void SendCommand(string action, int modelIndex,
            Dictionary<string, string> parameters = null, Action<string> onResponse = null)
        {
            string id = NextId();
            var sb = new StringBuilder();
            sb.Append($"{{\"id\":\"{id}\",\"type\":\"command\",\"action\":\"{action}\"");
            sb.Append($",\"params\":{{\"modelIndex\":\"{modelIndex}\"");
            if (parameters != null)
                foreach (var kv in parameters)
                    sb.Append($",\"{kv.Key}\":\"{kv.Value}\"");
            sb.Append("}}");
            string json = sb.ToString();
            if (onResponse != null) _textCallbacks[id] = onResponse;
            SendText(json);
        }

        // ================================================================
        // 内部: クエリ送信
        // ================================================================

        private void SendBinaryQuery(string json, Action<string, byte[]> onResponse)
        {
            string id = ExtractJsonString(json, "id");
            if (id != null && onResponse != null) _binaryCallbacks[id] = onResponse;
            SendText(json);
        }

        private string NextId() => $"pc_{++_requestId}";

        private static string ExtractJsonString(string json, string key)
        {
            string s = $"\"{key}\"";
            int i = json.IndexOf(s, StringComparison.Ordinal); if (i < 0) return null;
            int c = json.IndexOf(':', i + s.Length);           if (c < 0) return null;
            int vs = c + 1;
            while (vs < json.Length && json[vs] == ' ') vs++;
            if (vs >= json.Length || json[vs] != '"') return null;
            int ve = json.IndexOf('"', vs + 1);               if (ve < 0) return null;
            return json.Substring(vs + 1, ve - vs - 1);
        }

    }
}
