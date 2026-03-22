// PolyLingPlayerClient.cs
// Runtime用 WebSocketクライアント MonoBehaviour
// PolyLingEditor（RemoteServer）に接続してデータを送受信する
// Runtime/Poly_Ling_Player/Remote/ に配置

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerClient : MonoBehaviour
    {
        // ================================================================
        // Inspector設定
        // ================================================================

        [SerializeField] private string _host        = "127.0.0.1";
        [SerializeField] private int    _port        = 8765;
        [SerializeField] private bool   _autoConnect = true;

        // ================================================================
        // 状態
        // ================================================================

        public bool IsConnected => _tcp != null && _tcp.Connected && _stream != null;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>フェッチ以外のpushイベント受信時</summary>
        public Action<string> OnPushReceived;
        public Action         OnConnected;
        public Action         OnDisconnected;

        // ================================================================
        // 内部
        // ================================================================

        private TcpClient                        _tcp;
        private NetworkStream                    _stream;
        private CancellationTokenSource          _cts;
        private readonly System.Random           _maskRng = new System.Random();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // リクエスト管理
        private int    _requestId;
        private string _lastTextResponseId;
        private string _lastTextResponseJson;
        private readonly Dictionary<string, Action<string, byte[]>> _binaryCallbacks
            = new Dictionary<string, Action<string, byte[]>>();
        private readonly Dictionary<string, Action<string>> _textCallbacks
            = new Dictionary<string, Action<string>>();

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Start()
        {
            if (_autoConnect) Connect();
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void Update()
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
            if (IsConnected) return;
            _cts = new CancellationTokenSource();
            _ = ConnectAsync(_cts.Token);
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            CloseSocket();
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            try
            {
                _tcp    = new TcpClient();
                await _tcp.ConnectAsync(_host, _port);
                _stream = _tcp.GetStream();

                string wsKey   = GenerateWebSocketKey();
                string request =
                    $"GET / HTTP/1.1\r\n" +
                    $"Host: {_host}:{_port}\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Key: {wsKey}\r\n" +
                    "Sec-WebSocket-Version: 13\r\n" +
                    "\r\n";
                byte[] reqBytes = Encoding.UTF8.GetBytes(request);
                await _stream.WriteAsync(reqBytes, 0, reqBytes.Length, ct);

                string response = await ReadHttpResponseAsync(_stream, ct);
                if (response == null || response.IndexOf("101", StringComparison.Ordinal) < 0)
                {
                    Debug.LogWarning("[PolyLingPlayerClient] ハンドシェイク失敗");
                    CloseSocket();
                    return;
                }

                _stream.ReadTimeout = 300_000;
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log($"[PolyLingPlayerClient] 接続: {_host}:{_port}");
                    OnConnected?.Invoke();
                });

                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() =>
                    Debug.LogWarning($"[PolyLingPlayerClient] 接続エラー: {ex.Message}"));
            }
            finally
            {
                CloseSocket();
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log("[PolyLingPlayerClient] 切断");
                    OnDisconnected?.Invoke();
                });
            }
        }

        private void CloseSocket()
        {
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close();   } catch { }
            _stream = null;
            _tcp    = null;
        }

        // ================================================================
        // 受信ループ
        // ================================================================

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                var frame = await ReadWsFrameAsync(ct);
                if (frame == null) break;

                var f = frame.Value;
                if (f.Type == WsFrameType.Close) break;
                if (f.Type == WsFrameType.Ping)  continue;

                if (f.Type == WsFrameType.Text)
                {
                    string text = f.Text;
                    Debug.Log($"[CLI←SRV] TEXT ({text.Length}B): {text.Substring(0, Math.Min(200, text.Length))}");
                    _mainThreadQueue.Enqueue(() => HandleTextMessage(text));
                }
                else if (f.Type == WsFrameType.Binary)
                {
                    byte[] bin = f.Binary;
                    Debug.Log($"[CLI←SRV] BINARY ({bin.Length}B)");
                    _mainThreadQueue.Enqueue(() => HandleBinaryMessage(bin));
                }
            }
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

            // バイナリコールバック待ち → テキストレスポンスを保持してバイナリ待機
            if (id != null && _binaryCallbacks.ContainsKey(id))
            {
                _lastTextResponseId   = id;
                _lastTextResponseJson = json;
                return;
            }

            // テキストのみのレスポンス
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

            Debug.Log($"[PolyLingPlayerClient] 未対応バイナリ受信 ({data.Length}B)");
        }

        // ================================================================
        // 低レベル送信
        // ================================================================

        public void SendText(string message)
        {
            if (!IsConnected) return;
            Debug.Log($"[CLI→SRV] TEXT ({message.Length}B): {message.Substring(0, Math.Min(200, message.Length))}");
            _ = SendFrameAsync(0x81, Encoding.UTF8.GetBytes(message));
        }

        public void SendBinary(byte[] data)
        {
            if (!IsConnected) return;
            Debug.Log($"[CLI→SRV] BINARY ({data.Length}B)");
            _ = SendFrameAsync(0x82, data);
        }

        // ================================================================
        // フェッチAPI
        // ================================================================

        /// <summary>project_header を取得する。</summary>
        public void FetchProjectHeader(Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"project_header\"}}",
                onResponse);
        }

        /// <summary>model_meta を取得する。</summary>
        public void FetchModelMeta(int modelIndex, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"model_meta\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\"}}}}",
                onResponse);
        }

        /// <summary>mesh_data を取得する。</summary>
        public void FetchMeshData(int modelIndex, int meshIndex, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"meshIndex\":\"{meshIndex}\"}}}}",
                onResponse);
        }

        /// <summary>mesh_data_batch を取得する。category: "drawable" / "bone" / "morph" / "all"</summary>
        public void FetchMeshDataBatch(int modelIndex, string category, Action<string, byte[]> onResponse)
        {
            string id = NextId();
            SendBinaryQuery(
                $"{{\"id\":\"{id}\",\"type\":\"query\",\"target\":\"mesh_data_batch\"," +
                $"\"params\":{{\"modelIndex\":\"{modelIndex}\",\"category\":\"{category}\"}}}}",
                onResponse);
        }

        /// <summary>コマンドを送信する（selectMesh等）。</summary>
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
            Debug.Log($"[CLI→SRV] TEXT ({json.Length}B): {json.Substring(0, Math.Min(200, json.Length))}");
            _ = SendFrameAsync(0x81, Encoding.UTF8.GetBytes(json));
        }

        // ================================================================
        // 内部: クエリ送信
        // ================================================================

        private void SendBinaryQuery(string json, Action<string, byte[]> onResponse)
        {
            string id = ExtractJsonString(json, "id");
            if (id != null && onResponse != null) _binaryCallbacks[id] = onResponse;
            Debug.Log($"[CLI→SRV] TEXT ({json.Length}B): {json.Substring(0, Math.Min(200, json.Length))}");
            _ = SendFrameAsync(0x81, Encoding.UTF8.GetBytes(json));
        }

        private string NextId() => $"pc_{++_requestId}";

        // ================================================================
        // WebSocketフレーム読み取り
        // ================================================================

        private enum WsFrameType { Text, Binary, Ping, Close }

        private struct WsFrame
        {
            public WsFrameType Type;
            public string      Text;
            public byte[]      Binary;
        }

        private async Task<WsFrame?> ReadWsFrameAsync(CancellationToken ct)
        {
            try
            {
                byte[] header = new byte[2];
                if (!await ReadExactAsync(header, 0, 2, ct)) return null;

                int  opcode     = header[0] & 0x0F;
                bool masked     = (header[1] & 0x80) != 0;
                long payloadLen = header[1] & 0x7F;

                if (opcode == 0x08) return new WsFrame { Type = WsFrameType.Close };

                if (payloadLen == 126)
                {
                    byte[] ext = new byte[2];
                    if (!await ReadExactAsync(ext, 0, 2, ct)) return null;
                    payloadLen = (ext[0] << 8) | ext[1];
                }
                else if (payloadLen == 127)
                {
                    byte[] ext = new byte[8];
                    if (!await ReadExactAsync(ext, 0, 8, ct)) return null;
                    payloadLen = 0;
                    for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
                }

                byte[] maskKey = null;
                if (masked)
                {
                    maskKey = new byte[4];
                    if (!await ReadExactAsync(maskKey, 0, 4, ct)) return null;
                }

                if (payloadLen > 512_000_000L) return null;
                byte[] payload = new byte[payloadLen];
                if (payloadLen > 0 && !await ReadExactAsync(payload, 0, (int)payloadLen, ct)) return null;

                if (masked && maskKey != null)
                    for (int i = 0; i < payload.Length; i++) payload[i] ^= maskKey[i % 4];

                if (opcode == 0x09)
                {
                    await SendFrameAsync(0x8A, payload);
                    return new WsFrame { Type = WsFrameType.Ping };
                }

                return opcode == 0x02
                    ? new WsFrame { Type = WsFrameType.Binary, Binary = payload }
                    : new WsFrame { Type = WsFrameType.Text,   Text   = Encoding.UTF8.GetString(payload) };
            }
            catch (OperationCanceledException) { return null; }
            catch (IOException)                { return null; }
            catch                              { return null; }
        }

        // ================================================================
        // WebSocketフレーム送信（クライアントはマスク必須）
        // ================================================================

        private async Task SendFrameAsync(byte opcodeWithFin, byte[] payload)
        {
            if (_stream == null) return;

            byte[] maskKey = new byte[4];
            _maskRng.NextBytes(maskKey);

            byte[] masked = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++)
                masked[i] = (byte)(payload[i] ^ maskKey[i % 4]);

            byte[] frame;
            if (payload.Length < 126)
            {
                frame    = new byte[2 + 4 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = (byte)(0x80 | payload.Length);
                Array.Copy(maskKey, 0, frame, 2, 4);
                Array.Copy(masked,  0, frame, 6, payload.Length);
            }
            else if (payload.Length <= 65535)
            {
                frame    = new byte[4 + 4 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 0x80 | 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
                Array.Copy(maskKey, 0, frame, 4, 4);
                Array.Copy(masked,  0, frame, 8, payload.Length);
            }
            else
            {
                frame    = new byte[10 + 4 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 0x80 | 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--) { frame[2 + i] = (byte)(len & 0xFF); len >>= 8; }
                Array.Copy(maskKey, 0, frame, 10, 4);
                Array.Copy(masked,  0, frame, 14, payload.Length);
            }

            try { await _stream.WriteAsync(frame, 0, frame.Length); }
            catch { }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private async Task<bool> ReadExactAsync(byte[] buf, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int read = await _stream.ReadAsync(buf, offset + total, count - total, ct);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        private static async Task<string> ReadHttpResponseAsync(NetworkStream stream, CancellationToken ct)
        {
            var sb        = new StringBuilder();
            byte[] buf    = new byte[1];
            int crlfCount = 0;
            while (crlfCount < 4)
            {
                int read = await stream.ReadAsync(buf, 0, 1, ct);
                if (read <= 0) return null;
                char c = (char)buf[0];
                sb.Append(c);
                if ((crlfCount % 2 == 0 && c == '\r') || (crlfCount % 2 == 1 && c == '\n'))
                    crlfCount++;
                else
                    crlfCount = (c == '\r') ? 1 : 0;
                if (sb.Length > 8192) return null;
            }
            return sb.ToString();
        }

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

        private static string GenerateWebSocketKey()
        {
            byte[] keyBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }
    }
}
