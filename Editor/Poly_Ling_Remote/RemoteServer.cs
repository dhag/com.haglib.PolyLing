// Remote/RemoteServer.cs
// EditorWindow上で動作するWebSocketサーバー
// TcpListener + 自前WebSocketハンドシェイク/フレーミング（Unity互換）
//
// HttpListener.AcceptWebSocketAsyncはUnityの.NETランタイムで動作しないため、
// TCP上でHTTPアップグレードとWebSocketフレームを直接処理する。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Model;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// リモートパネルサーバー（EditorWindow）
    /// </summary>
    public class RemoteServer : EditorWindow
    {
        // ================================================================
        // 設定
        // ================================================================

        private int _port = 8765;
        private bool _isRunningBacking;
        public bool IsRunning => _isRunningBacking;
        private bool _autoStart;

        private const string AutoStartPrefKey = "PolyLing.RemoteServer.AutoStart";
        private const string AutoStartPortKey  = "PolyLing.RemoteServer.Port";

        // ================================================================
        // サーバー
        // ================================================================

        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private readonly List<WsClient> _clients = new List<WsClient>();
        private readonly object _clientLock = new object();

        // メインスレッドで実行するキュー
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // ================================================================
        // ToolContext（毎回PolyLingから動的取得）
        // ================================================================

        private ToolContext Context
        {
            get
            {
                var windows = Resources.FindObjectsOfTypeAll<PolyLing>();
                if (windows == null || windows.Length == 0) return null;
                return windows[0].CurrentToolContext;
            }
        }

        private ModelContext _subscribedModel; // イベント購読解除用の参照

        // ================================================================
        // ログ
        // ================================================================

        private readonly List<string> _logMessages = new List<string>();
        private Vector2 _logScroll;
        private const int MaxLogLines = 50;

        // ================================================================
        // キャプチャ画像リスト
        // ================================================================

        private readonly List<ImageEntry> _capturedImages = new List<ImageEntry>();
        private ushort _nextImageId;

        // ================================================================
        // ウィンドウ管理
        // ================================================================

        public static void Open(ToolContext ctx = null)
        {
            var window = GetWindow<RemoteServer>("Remote Server");
            window.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            // 互換用（動的取得のため何もしない）
        }

        /// <summary>
        /// 開いているRemoteServerインスタンスを取得（未オープン時はnull）
        /// </summary>
        public static RemoteServer FindInstance()
        {
            var windows = Resources.FindObjectsOfTypeAll<RemoteServer>();
            return (windows != null && windows.Length > 0) ? windows[0] : null;
        }

        /// <summary>
        /// キャプチャした画像を送信リストに追加
        /// Texture2DはこのメソッドがコピーするのでDestroyImmediate可
        /// </summary>
        public void AddCapturedImage(Texture2D tex)
        {
            if (tex == null) return;

            var entry = RemoteImageSerializer.FromTexture2DJPEG(tex, _nextImageId++, 85);
            _capturedImages.Add(entry);

            Log($"キャプチャ追加: ID={entry.Id} {entry.Width}x{entry.Height} ({entry.Data.Length}B)");
            Repaint();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Remote Panel Server", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            bool hasContext = Context?.Model != null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("ToolContext", hasContext);
                if (hasContext)
                    EditorGUILayout.IntField("Meshes", Context.Model.Count);
            }

            EditorGUILayout.Space(5);

            using (new EditorGUI.DisabledScope(_isRunningBacking))
            {
                int newPort = EditorGUILayout.IntField("Port", _port);
                if (newPort != _port)
                {
                    _port = newPort;
                    EditorPrefs.SetInt(AutoStartPortKey, _port);
                }
            }

            bool newAutoStart = EditorGUILayout.Toggle("エディタ起動時に自動開始", _autoStart);
            if (newAutoStart != _autoStart)
            {
                _autoStart = newAutoStart;
                EditorPrefs.SetBool(AutoStartPrefKey, _autoStart);
            }

            if (!_isRunningBacking)
            {
                if (GUILayout.Button("Start Server"))
                    StartServer();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"ブラウザで http://localhost:{_port}/ を開いてください", MessageType.Info);

                int clientCount;
                lock (_clientLock) { clientCount = _clients.Count; }
                EditorGUILayout.LabelField($"接続クライアント: {clientCount}");

                if (GUILayout.Button("Stop Server"))
                    StopServer();

                if (GUILayout.Button("Send Test Image"))
                    SendTestImages();

                if (GUILayout.Button("Send Project Header"))
                    SendProjectHeader();

                // ================================================================
                // キャプチャ画像リスト
                // ================================================================
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Captured Images", EditorStyles.miniBoldLabel);

                if (_capturedImages.Count == 0)
                {
                    EditorGUILayout.LabelField("  (empty)", EditorStyles.miniLabel);
                }
                else
                {
                    long totalBytes = 0;
                    foreach (var img in _capturedImages) totalBytes += img.Data.Length;
                    EditorGUILayout.LabelField(
                        $"  {_capturedImages.Count} 枚 ({totalBytes / 1024}KB)",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(_capturedImages.Count == 0))
                {
                    if (GUILayout.Button("Send Images"))
                        SendCapturedImages();
                    if (GUILayout.Button("Clear"))
                    {
                        _capturedImages.Clear();
                        Log("キャプチャリストクリア");
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Log", EditorStyles.miniBoldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(150));
            foreach (var msg in _logMessages)
                EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Clear Log"))
                _logMessages.Clear();
        }

        // ================================================================
        // サーバーライフサイクル
        // ================================================================

        public void StartServer()
        {
            if (_isRunningBacking) return;

            try
            {
                _cts = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Loopback, _port);
                _tcpListener.Start();
                _isRunningBacking = true;

                SubscribeModel();
                _ = AcceptClientsAsync(_cts.Token);

                Log($"サーバー起動: http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                Log($"起動失敗: {ex.Message}");
                _isRunningBacking = false;
            }
        }

        private void StopServer()
        {
            if (!_isRunningBacking) return;

            UnsubscribeModel();
            _cts?.Cancel();

            lock (_clientLock)
            {
                foreach (var c in _clients)
                    c.Close();
                _clients.Clear();
            }

            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            _isRunningBacking = false;
            _cts?.Dispose();
            _cts = null;

            Log("サーバー停止");
        }

        // ================================================================
        // EditorWindowライフサイクル
        // ================================================================

        private void OnEnable()
        {
            _autoStart = EditorPrefs.GetBool(AutoStartPrefKey, false);
            _port      = EditorPrefs.GetInt(AutoStartPortKey, _port);
            EditorApplication.update += ProcessMainThreadQueue;
        }

        private void OnDisable()
        {
            EditorApplication.update -= ProcessMainThreadQueue;
            StopServer();
        }

        private void ProcessMainThreadQueue()
        {
            int processed = 0;
            while (_mainThreadQueue.TryDequeue(out var action) && processed < 10)
            {
                try { action(); }
                catch (Exception ex) { Log($"メインスレッドエラー: {ex.Message}"); }
                processed++;
            }
        }

        // ================================================================
        // TCP接続受付
        // ================================================================

        private async Task AcceptClientsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    _ = HandleConnectionAsync(tcpClient, ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _mainThreadQueue.Enqueue(() => Log($"接続受付エラー: {ex.Message}"));
                }
            }
        }

        /// <summary>
        /// TCP接続を処理。HTTPリクエストを読み、WebSocketアップグレードかHTMLリクエストかを判定する。
        /// </summary>
        private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken ct)
        {
            NetworkStream stream = null;
            try
            {
                stream = tcpClient.GetStream();

                // HTTPリクエストヘッダを読む
                string httpRequest = await ReadHttpRequestAsync(stream, ct);
                if (string.IsNullOrEmpty(httpRequest))
                {
                    tcpClient.Close();
                    return;
                }

                if (httpRequest.IndexOf("Upgrade: websocket", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // WebSocketハンドシェイク
                    string wsKey = ExtractWebSocketKey(httpRequest);
                    if (wsKey == null)
                    {
                        tcpClient.Close();
                        return;
                    }

                    string acceptKey = ComputeAcceptKey(wsKey);
                    string handshakeResponse =
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Sec-WebSocket-Accept: " + acceptKey + "\r\n" +
                        "\r\n";

                    byte[] responseBytes = Encoding.UTF8.GetBytes(handshakeResponse);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);

                    // WebSocketクライアントとして管理
                    var wsClient = new WsClient(tcpClient, stream);
                    lock (_clientLock) { _clients.Add(wsClient); }

                    _mainThreadQueue.Enqueue(() =>
                    {
                        Log("クライアント接続");
                        Repaint();
                    });

                    await HandleWebSocketAsync(wsClient, ct);
                }
                else
                {
                    // 通常のHTTPリクエスト → HTMLクライアントを返す
                    string html = RemoteHtmlClient.GetHtml(_port);
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    string httpResponse =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/html; charset=utf-8\r\n" +
                        "Content-Length: " + body.Length + "\r\n" +
                        "Connection: close\r\n" +
                        "\r\n";

                    byte[] header = Encoding.UTF8.GetBytes(httpResponse);
                    await stream.WriteAsync(header, 0, header.Length, ct);
                    await stream.WriteAsync(body, 0, body.Length, ct);

                    tcpClient.Close();
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _mainThreadQueue.Enqueue(() => Log($"接続処理エラー: {ex.Message}"));

                try { tcpClient?.Close(); } catch { }
            }
        }

        // ================================================================
        // WebSocketメッセージ処理
        // ================================================================

        /// <summary>フレーム種別</summary>
        private enum WsFrameType { Text, Binary, Ping, Close }

        /// <summary>受信フレーム</summary>
        private struct WsFrame
        {
            public WsFrameType Type;
            public string Text;       // Text時
            public byte[] Binary;     // Binary時
        }

        private async Task HandleWebSocketAsync(WsClient client, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && client.IsConnected)
                {
                    var frame = await ReadWsFrameAsync(client.Stream, ct);
                    if (frame == null || frame.Value.Type == WsFrameType.Close) break;
                    if (frame.Value.Type == WsFrameType.Ping) continue;

                    var f = frame.Value;

                    if (f.Type == WsFrameType.Text)
                    {
                        if (string.IsNullOrEmpty(f.Text)) continue;

                        _mainThreadQueue.Enqueue(async () =>
                        {
                            _pendingBinaryResponses = null;
                            string response = ProcessMessage(f.Text);
                            var pendingBinaries = _pendingBinaryResponses;
                            _pendingBinaryResponses = null;

                            if (response != null && client.IsConnected)
                            {
                                try
                                {
                                    await SendWsTextAsync(client.Stream, response);
                                    if (pendingBinaries != null && client.IsConnected)
                                        await SendWsBinaryAsync(client.Stream, BuildBatch(pendingBinaries));
                                }
                                catch { }
                            }
                        });
                    }
                    else if (f.Type == WsFrameType.Binary)
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            byte[] response = ProcessBinaryMessage(f.Binary);
                            if (response != null && client.IsConnected)
                                _ = SendWsBinaryAsync(client.Stream, response);
                        });
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() => Log($"WSエラー: {ex.Message}"));
            }
            finally
            {
                lock (_clientLock) { _clients.Remove(client); }
                client.Close();
                _mainThreadQueue.Enqueue(() =>
                {
                    Log("クライアント切断");
                    Repaint();
                });
            }
        }

        /// <summary>
        /// バイナリメッセージ処理
        /// ヘッダを読み、メッシュデータとして処理
        /// </summary>
        private byte[] ProcessBinaryMessage(byte[] data)
        {
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null)
            {
                Log("バイナリ: 無効なヘッダ");
                return null;
            }

            var h = header.Value;
            Log($"バイナリ受信: type={h.MessageType} flags={h.FieldFlags} V={h.VertexCount} F={h.FaceCount}");

            switch (h.MessageType)
            {
                case BinaryMessageType.MeshData:
                {
                    // メッシュデータ受信 → MeshObjectとしてインポート
                    var meshObject = RemoteBinarySerializer.Deserialize(data);
                    if (meshObject != null && Context != null)
                    {
                        Context.CreateNewMeshContext?.Invoke(meshObject, "RemoteMesh");
                        Context.Repaint?.Invoke();
                        Log($"メッシュ作成: V={meshObject.VertexCount} F={meshObject.FaceCount}");
                    }
                    return null;
                }

                case BinaryMessageType.PositionsOnly:
                {
                    // 位置のみ更新 → 選択中メッシュに適用
                    if (Context?.FirstSelectedMeshObject != null)
                    {
                        RemoteBinarySerializer.Deserialize(data, Context.FirstSelectedMeshObject);
                        Context.SyncMesh?.Invoke();
                        Context.Repaint?.Invoke();
                        Log("位置更新適用");
                    }
                    return null;
                }

                case BinaryMessageType.RawFile:
                {
                    var (fileData, ext) = RemoteBinarySerializer.ExtractRawFile(data);
                    if (fileData != null)
                    {
                        Log($"ファイル受信: {ext} ({fileData.Length} bytes)");
                        // TODO: 拡張子に応じたインポータに委譲
                    }
                    return null;
                }

                default:
                    Log($"未知のバイナリタイプ: {h.MessageType}");
                    return null;
            }
        }

        // ================================================================
        // WebSocketフレーム読み書き（RFC 6455 最小実装）
        // ================================================================

        /// <summary>フレームを1つ読む。切断/エラーでnullを返す。</summary>
        private static async Task<WsFrame?> ReadWsFrameAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] header = new byte[2];
            if (!await ReadExactAsync(stream, header, 0, 2, ct)) return null;

            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (opcode == 0x08)
                return new WsFrame { Type = WsFrameType.Close };

            // 拡張長
            if (payloadLen == 126)
            {
                byte[] ext = new byte[2];
                if (!await ReadExactAsync(stream, ext, 0, 2, ct)) return null;
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == 127)
            {
                byte[] ext = new byte[8];
                if (!await ReadExactAsync(stream, ext, 0, 8, ct)) return null;
                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                    payloadLen = (payloadLen << 8) | ext[i];
            }

            // マスクキー
            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                if (!await ReadExactAsync(stream, maskKey, 0, 4, ct)) return null;
            }

            // ペイロード（バイナリメッシュ用に上限を拡大: 50MB）
            if (payloadLen > 50_000_000) return null;
            byte[] payload = new byte[payloadLen];
            if (payloadLen > 0)
            {
                if (!await ReadExactAsync(stream, payload, 0, (int)payloadLen, ct)) return null;
            }

            // アンマスク
            if (masked && maskKey != null)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= maskKey[i % 4];
            }

            // Ping → Pong返送
            if (opcode == 0x09)
            {
                byte[] pong = new byte[2 + payload.Length];
                pong[0] = 0x8A; // FIN + Pong
                pong[1] = (byte)payload.Length;
                Array.Copy(payload, 0, pong, 2, payload.Length);
                try { await stream.WriteAsync(pong, 0, pong.Length, ct); } catch { }
                return new WsFrame { Type = WsFrameType.Ping };
            }

            // Text (0x01) or Binary (0x02)
            if (opcode == 0x02)
                return new WsFrame { Type = WsFrameType.Binary, Binary = payload };
            else
                return new WsFrame { Type = WsFrameType.Text, Text = Encoding.UTF8.GetString(payload) };
        }

        /// <summary>テキストフレームを送信</summary>
        private static async Task SendWsTextAsync(NetworkStream stream, string message)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message);
            await SendWsRawAsync(stream, 0x81, payload); // FIN + Text
        }

        /// <summary>バイナリフレームを送信</summary>
        private static async Task SendWsBinaryAsync(NetworkStream stream, byte[] payload)
        {
            await SendWsRawAsync(stream, 0x82, payload); // FIN + Binary
        }

        /// <summary>WebSocketフレームを送信（共通）</summary>
        private static async Task SendWsRawAsync(NetworkStream stream, byte opcodeWithFin, byte[] payload)
        {
            byte[] frame;

            if (payload.Length < 126)
            {
                frame = new byte[2 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = (byte)payload.Length;
                Array.Copy(payload, 0, frame, 2, payload.Length);
            }
            else if (payload.Length <= 65535)
            {
                frame = new byte[4 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
                Array.Copy(payload, 0, frame, 4, payload.Length);
            }
            else
            {
                frame = new byte[10 + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame[2 + i] = (byte)(len & 0xFF);
                    len >>= 8;
                }
                Array.Copy(payload, 0, frame, 10, payload.Length);
            }

            try
            {
                await stream.WriteAsync(frame, 0, frame.Length);
            }
            catch { }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buf, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buf, offset + total, count - total, ct);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        // ================================================================
        // HTTPリクエスト読み取り
        // ================================================================

        private static async Task<string> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            byte[] buf = new byte[1];
            int consecutiveCrLf = 0;

            while (consecutiveCrLf < 4)
            {
                int read = await stream.ReadAsync(buf, 0, 1, ct);
                if (read <= 0) return null;

                char c = (char)buf[0];
                sb.Append(c);

                if ((consecutiveCrLf % 2 == 0 && c == '\r') ||
                    (consecutiveCrLf % 2 == 1 && c == '\n'))
                    consecutiveCrLf++;
                else
                    consecutiveCrLf = (c == '\r') ? 1 : 0;

                if (sb.Length > 8192) return null;
            }

            return sb.ToString();
        }

        // ================================================================
        // WebSocketハンドシェイクヘルパー
        // ================================================================

        private static string ExtractWebSocketKey(string httpRequest)
        {
            foreach (string line in httpRequest.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring("Sec-WebSocket-Key:".Length).Trim();
                }
            }
            return null;
        }

        private static string ComputeAcceptKey(string wsKey)
        {
            string combined = wsKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
                return Convert.ToBase64String(hash);
            }
        }

        // ================================================================
        // メッセージ処理
        // ================================================================

        private string ProcessMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            RemoteMessage msg;
            try
            {
                msg = JsonParser.Parse(json);
            }
            catch (Exception ex)
            {
                Log($"パースエラー: {ex.Message}");
                return BuildErrorResponse(null, "Parse error");
            }

            Log($"受信: type={msg.Type} target={msg.Target} action={msg.Action}");

            if (msg.Type == "query")
                return ProcessQuery(msg);
            else if (msg.Type == "command")
                return ProcessCommand(msg);
            else
                return BuildErrorResponse(msg.Id, $"Unknown type: {msg.Type}");
        }

        private string ProcessQuery(RemoteMessage msg)
        {
            switch (msg.Target)
            {
                case "meshList":
                {
                    string data = RemoteDataProvider.QueryMeshList(Context, msg.Fields);
                    return BuildSuccessResponse(msg.Id, data);
                }

                case "meshData":
                {
                    int index = GetParamInt(msg, "index", 0);
                    string data = RemoteDataProvider.QueryMeshData(Context, index, msg.Fields);
                    return BuildSuccessResponse(msg.Id, data);
                }

                case "modelInfo":
                {
                    string data = RemoteDataProvider.QueryModelInfo(Context);
                    return BuildSuccessResponse(msg.Id, data);
                }

                case "availableFields":
                {
                    string data = RemoteDataProvider.QueryAvailableFields();
                    return BuildSuccessResponse(msg.Id, data);
                }

                // ---- プログレッシブプロトコル ----

                case "project_header":
                    return ProcessProjectHeaderQuery(msg);

                case "model_meta":
                    return ProcessModelMetaQuery(msg);

                case "mesh_data":
                    return ProcessMeshDataQuery(msg);

                case "mesh_data_batch":
                    return ProcessMeshDataBatchQuery(msg);

                default:
                    return BuildErrorResponse(msg.Id, $"Unknown target: {msg.Target}");
            }
        }

        /// <summary>
        /// 複数バイナリフレームを PLRB バッチに結合
        /// [4B Magic] [1B Version] [3B padding] [4B FrameCount] { [4B FrameLen][FrameData] } × N
        /// </summary>
        private static byte[] BuildBatch(List<byte[]> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                // 空PLRBヘッダのみ（frameCount=0）：クライアントコールバック発火用
                using (var ms = new System.IO.MemoryStream(12))
                using (var w = new System.IO.BinaryWriter(ms))
                {
                    w.Write(RemoteMagic.Batch);
                    w.Write((byte)1);
                    w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((uint)0); // frameCount = 0
                    return ms.ToArray();
                }
            }
            if (frames.Count == 1) return frames[0]; // 1件はそのまま

            int totalBody = 0;
            foreach (var f in frames) totalBody += 4 + f.Length;

            using (var ms = new System.IO.MemoryStream(12 + totalBody))
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Batch);        // 4B
                w.Write((byte)1);                   // 1B version
                w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); // 3B padding
                w.Write((uint)frames.Count);        // 4B frame count
                foreach (var f in frames)
                {
                    w.Write((uint)f.Length);        // 4B frame length
                    w.Write(f);                     // frame data
                }
                return ms.ToArray();
            }
        }

        /// <summary>テキスト応答の直後に順番に送るバイナリデータリスト（1回使い切り）</summary>
        private List<byte[]> _pendingBinaryResponses;

        /// <summary>
        /// project_header クエリ:
        /// PLRH を返し、全モデルの PLRM + 各メッシュの PLRS を連続プッシュ
        /// </summary>
        private string ProcessProjectHeaderQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null)
                return BuildErrorResponse(msg.Id, "No project");

            var binaries = new List<byte[]>();

            // PLRH
            byte[] header = RemoteProgressiveSerializer.SerializeProjectHeader(project);
            if (header == null)
                return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(header);

            // 全モデル: PLRM + 各メッシュ PLRS
            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                byte[] modelMeta = RemoteProgressiveSerializer.SerializeModelMeta(model, mi);
                if (modelMeta != null) binaries.Add(modelMeta);

                for (int si = 0; si < model.Count; si++)
                {
                    byte[] meshSummary = RemoteProgressiveSerializer.SerializeMeshSummary(
                        model.MeshContextList[si], mi, si);
                    if (meshSummary != null) binaries.Add(meshSummary);
                }
            }

            _pendingBinaryResponses = binaries;

            int totalMeshes = 0;
            for (int mi = 0; mi < project.ModelCount; mi++)
                totalMeshes += project.Models[mi].Count;

            Log($"project_header: {project.ModelCount}モデル {totalMeshes}メッシュ ({binaries.Count}フレーム)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("projectName", project.Name);
            jb.KeyValue("modelCount", project.ModelCount);
            jb.KeyValue("meshCount", totalMeshes);
            jb.KeyValue("frameCount", binaries.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        /// <summary>
        /// model_meta クエリ:
        /// 指定モデルの PLRM + 全メッシュ PLRS を返す
        /// params: modelIndex
        /// </summary>
        private string ProcessModelMetaQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null)
                return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            var binaries = new List<byte[]>();

            byte[] modelMeta = RemoteProgressiveSerializer.SerializeModelMeta(model, modelIndex);
            if (modelMeta == null)
                return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(modelMeta);

            for (int si = 0; si < model.Count; si++)
            {
                byte[] meshSummary = RemoteProgressiveSerializer.SerializeMeshSummary(
                    model.MeshContextList[si], modelIndex, si);
                if (meshSummary != null) binaries.Add(meshSummary);
            }

            _pendingBinaryResponses = binaries;

            Log($"model_meta: [{modelIndex}] {model.Name} meshes={model.Count}");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("modelName", model.Name);
            jb.KeyValue("meshCount", model.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        /// <summary>
        /// mesh_data クエリ:
        /// 指定メッシュのジオメトリ本体を PLRD で返す
        /// params: modelIndex, meshIndex, flags (MeshFieldFlags、省略時 All)
        /// </summary>
        private string ProcessMeshDataQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null)
                return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            int meshIndex  = GetParamInt(msg, "meshIndex", -1);

            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            if (meshIndex < 0 || meshIndex >= model.Count)
                return BuildErrorResponse(msg.Id, $"Invalid meshIndex: {meshIndex}");

            var mc = model.MeshContextList[meshIndex];
            uint flagsValue = (uint)GetParamInt(msg, "flags", (int)MeshFieldFlags.All);
            var flags = (MeshFieldFlags)flagsValue;

            byte[] binaryData = RemoteProgressiveSerializer.SerializeMeshData(mc, modelIndex, meshIndex, flags);
            if (binaryData == null)
                return BuildErrorResponse(msg.Id, "Serialize failed");

            _pendingBinaryResponses = new List<byte[]> { binaryData };

            Log($"mesh_data: [{modelIndex}][{meshIndex}] {mc.Name} V={mc.VertexCount} ({binaryData.Length}B)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("meshIndex", meshIndex);
            jb.KeyValue("meshName", mc.Name);
            jb.KeyValue("vertexCount", mc.VertexCount);
            jb.KeyValue("faceCount", mc.FaceCount);
            jb.KeyValue("binarySize", binaryData.Length);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        /// <summary>
        /// mesh_data_batch クエリ:
        /// カテゴリ内の全メッシュ PLRD を BuildBatch で1フレームにまとめて返す
        /// params: modelIndex, category ("bone"|"drawable"|"morph"|"all")
        /// </summary>
        private string ProcessMeshDataBatchQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null)
                return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            string category = GetParamString(msg, "category", "drawable");

            // カテゴリに対応する MeshContextList インデックスを収集
            System.Collections.Generic.IReadOnlyList<TypedMeshEntry> entries;
            switch (category)
            {
                case "bone":     entries = model.Bones;          break;
                case "morph":    entries = model.Morphs;         break;
                case "all":      entries = model.TypedIndices.GetEntries(MeshCategory.All); break;
                default:         entries = model.DrawableMeshes; break; // "drawable"
            }

            var frames = new List<byte[]>();
            foreach (var entry in entries)
            {
                var mc = entry.Context;
                if (mc?.MeshObject == null || mc.MeshObject.VertexCount == 0) continue;
                var data = RemoteProgressiveSerializer.SerializeMeshData(
                    mc, modelIndex, entry.MasterIndex, MeshFieldFlags.All);
                if (data != null) frames.Add(data);
            }

            if (frames.Count == 0)
            {
                // 0件でも空PLRBを送ってコールバックを確実に発火させる
                _pendingBinaryResponses = new List<byte[]> { BuildBatch(new List<byte[]>()) };
                Log($"mesh_data_batch: [{modelIndex}] {category} → 0件");
                var jbEmpty = new JsonBuilder();
                jbEmpty.BeginObject();
                jbEmpty.KeyValue("modelIndex", modelIndex);
                jbEmpty.KeyValue("category", category);
                jbEmpty.KeyValue("meshCount", 0);
                jbEmpty.KeyValue("binarySize", 0);
                jbEmpty.EndObject();
                return BuildSuccessResponse(msg.Id, jbEmpty.ToString());
            }

            _pendingBinaryResponses = new List<byte[]> { BuildBatch(frames) };

            int totalBytes = frames.Sum(f => f.Length);
            Log($"mesh_data_batch: [{modelIndex}] {category} {frames.Count}件 ({totalBytes}B)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("category", category);
            jb.KeyValue("meshCount", frames.Count);
            jb.KeyValue("binarySize", totalBytes);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }


        private string ProcessCommand(RemoteMessage msg)
        {
            if (Context == null)
                return BuildErrorResponse(msg.Id, "No ToolContext");

            try
            {
                switch (msg.Action)
                {
                    case "selectMesh":
                    {
                        int index = GetParamInt(msg, "index", -1);
                        if (index < 0)
                            return BuildErrorResponse(msg.Id, "Invalid index");

                        Context.SelectMeshContext?.Invoke(index);
                        Context.OnMeshSelectionChanged?.Invoke();
                        Context.Repaint?.Invoke();

                        Log($"selectMesh: {index}");
                        return BuildSuccessResponse(msg.Id, "true");
                    }

                    case "updateAttribute":
                    {
                        int index = GetParamInt(msg, "index", -1);
                        if (index < 0)
                            return BuildErrorResponse(msg.Id, "Invalid index");

                        var change = new MeshAttributeChange { Index = index };

                        if (msg.Params.TryGetValue("name", out var name))
                            change.Name = name;
                        if (msg.Params.TryGetValue("visible", out var vis))
                            change.IsVisible = vis == "true";
                        if (msg.Params.TryGetValue("locked", out var lck))
                            change.IsLocked = lck == "true";

                        Context.UpdateMeshAttributes?.Invoke(
                            new List<MeshAttributeChange> { change });
                        Context.Repaint?.Invoke();

                        Log($"updateAttribute: [{index}] {change}");
                        return BuildSuccessResponse(msg.Id, "true");
                    }

                    default:
                        return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");
                }
            }
            catch (Exception ex)
            {
                Log($"コマンドエラー: {ex.Message}");
                return BuildErrorResponse(msg.Id, ex.Message);
            }
        }

        /// <summary>
        /// プロジェクトヘッダ＋全モデルメタ＋全メッシュサマリを全クライアントにプッシュ
        /// </summary>
        private void SendProjectHeader()
        {
            var project = GetProjectContext();
            if (project == null) { Log("プロジェクトなし"); return; }

            var frames = new List<byte[]>();
            var header = RemoteProgressiveSerializer.SerializeProjectHeader(project);
            if (header != null) frames.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, mi);
                if (mm != null) frames.Add(mm);
                for (int si = 0; si < model.Count; si++)
                {
                    var ms2 = RemoteProgressiveSerializer.SerializeMeshSummary(model.MeshContextList[si], mi, si);
                    if (ms2 != null) frames.Add(ms2);
                }
            }

            foreach (var f in frames) BroadcastBinaryAsync(f);
            Log($"プロジェクトヘッダ送信: {frames.Count}フレーム");
        }

        /// <summary>
        /// ProjectContextを取得。nullの場合はModelContextから動的に構築。
        /// </summary>
        private ProjectContext GetProjectContext()
        {
            return Context?.Project;
        }

        // ================================================================
        // テスト画像送信
        // ================================================================

        /// <summary>
        /// サイズ違いの3枚のランダム色画像を生成し、全クライアントにバイナリ送信
        /// </summary>
        private void SendTestImages()
        {
            var images = new List<ImageEntry>();
            int[] sizes = { 32, 64, 128 };

            for (int s = 0; s < sizes.Length; s++)
            {
                int size = sizes[s];
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

                // ランダムカラーで塗りつぶし＋格子パターン
                Color baseColor = new Color(
                    UnityEngine.Random.Range(0.2f, 1f),
                    UnityEngine.Random.Range(0.2f, 1f),
                    UnityEngine.Random.Range(0.2f, 1f));

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool grid = ((x / 8) + (y / 8)) % 2 == 0;
                        Color c = grid ? baseColor : baseColor * 0.6f;
                        c.a = 1f;
                        tex.SetPixel(x, y, c);
                    }
                }
                tex.Apply();

                images.Add(RemoteImageSerializer.FromTexture2D(tex, (ushort)s));
                UnityEngine.Object.DestroyImmediate(tex);
            }

            byte[] data = RemoteImageSerializer.Serialize(images);
            if (data != null)
            {
                BroadcastBinaryAsync(data);
                Log($"テスト画像送信: {images.Count}枚 ({data.Length}B)");
            }
        }

        /// <summary>
        /// キャプチャ画像リストを全クライアントにバイナリ送信
        /// </summary>
        private void SendCapturedImages()
        {
            if (_capturedImages.Count == 0) return;

            byte[] data = RemoteImageSerializer.Serialize(_capturedImages);
            if (data != null)
            {
                BroadcastBinaryAsync(data);
                Log($"キャプチャ画像送信: {_capturedImages.Count}枚 ({data.Length}B)");
            }
        }

        // ================================================================
        // Pushイベント
        // ================================================================

        private void SubscribeModel()
        {
            UnsubscribeModel();
            var model = Context?.Model;
            if (model == null) return;
            model.OnListChanged += OnModelListChanged;
            _subscribedModel = model;
        }

        private void UnsubscribeModel()
        {
            if (_subscribedModel != null)
            {
                _subscribedModel.OnListChanged -= OnModelListChanged;
                _subscribedModel = null;
            }
        }

        private void OnModelListChanged()
        {
            string data = RemoteDataProvider.QueryMeshList(Context, null);
            string pushJson = BuildPushMessage("meshListChanged", data);
            BroadcastAsync(pushJson);
        }

        private void BroadcastAsync(string json)
        {
            List<WsClient> snapshot;
            lock (_clientLock) { snapshot = new List<WsClient>(_clients); }

            foreach (var c in snapshot)
            {
                if (c.IsConnected)
                    _ = SendWsTextAsync(c.Stream, json);
            }
        }

        private void BroadcastBinaryAsync(byte[] data)
        {
            List<WsClient> snapshot;
            lock (_clientLock) { snapshot = new List<WsClient>(_clients); }

            foreach (var c in snapshot)
            {
                if (c.IsConnected)
                    _ = SendWsBinaryAsync(c.Stream, data);
            }
        }

        /// <summary>
        /// 複数バイナリフレームを PLRB 形式に結合して1つの byte[] にする
        /// [4B Magic=PLRB] [1B Version] [3B padding] [4B FrameCount]
        /// { [4B FrameLen] [FrameData] } × N
        /// </summary>
        private static byte[] BuildBatchFrame(List<byte[]> frames)
        {
            if (frames == null || frames.Count == 0) return null;
            if (frames.Count == 1) return frames[0];

            int totalBody = 0;
            foreach (var f in frames) totalBody += 4 + f.Length;

            using (var ms = new System.IO.MemoryStream(12 + totalBody))
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Batch);      // 4B
                w.Write((byte)1);                // 1B version
                w.Write((byte)0);                // 3B padding
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((uint)frames.Count);     // 4B frame count
                foreach (var f in frames)
                {
                    w.Write((uint)f.Length);
                    w.Write(f);
                }
                return ms.ToArray();
            }
        }

        // ================================================================
        // レスポンスビルダー
        // ================================================================

        private static string BuildSuccessResponse(string id, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id", id);
            jb.KeyValue("type", "response");
            jb.KeyValue("success", true);
            jb.KeyRaw("data", dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildErrorResponse(string id, string error)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id", id);
            jb.KeyValue("type", "response");
            jb.KeyValue("success", false);
            jb.KeyValue("error", error);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildPushMessage(string eventName, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id", (string)null);
            jb.KeyValue("type", "push");
            jb.KeyValue("event", eventName);
            jb.KeyRaw("data", dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static int GetParamInt(RemoteMessage msg, string key, int defaultValue)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val))
            {
                if (int.TryParse(val, out int result))
                    return result;
            }
            return defaultValue;
        }

        private static string GetParamString(RemoteMessage msg, string key, string defaultValue)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val) && val != null)
                return val;
            return defaultValue;
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _logMessages.Add(line);
            while (_logMessages.Count > MaxLogLines)
                _logMessages.RemoveAt(0);
            Repaint();
        }

        // ================================================================
        // WebSocketクライアント管理
        // ================================================================

        private class WsClient
        {
            public TcpClient Tcp { get; }
            public NetworkStream Stream { get; }

            public bool IsConnected
            {
                get
                {
                    try { return Tcp != null && Tcp.Connected; }
                    catch { return false; }
                }
            }

            public WsClient(TcpClient tcp, NetworkStream stream)
            {
                Tcp = tcp;
                Stream = stream;
            }

            public void Close()
            {
                try { Stream?.Close(); } catch { }
                try { Tcp?.Close(); } catch { }
            }
        }
    }
}
