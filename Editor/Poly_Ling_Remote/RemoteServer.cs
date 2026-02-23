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
        private bool _isRunning;

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

            using (new EditorGUI.DisabledScope(_isRunning))
            {
                _port = EditorGUILayout.IntField("Port", _port);
            }

            if (!_isRunning)
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

                if (GUILayout.Button("Send Project"))
                    SendProject();

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

        private void StartServer()
        {
            if (_isRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Loopback, _port);
                _tcpListener.Start();
                _isRunning = true;

                SubscribeModel();
                _ = AcceptClientsAsync(_cts.Token);

                Log($"サーバー起動: http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                Log($"起動失敗: {ex.Message}");
                _isRunning = false;
            }
        }

        private void StopServer()
        {
            if (!_isRunning) return;

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
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;

            Log("サーバー停止");
        }

        // ================================================================
        // EditorWindowライフサイクル
        // ================================================================

        private void OnEnable()
        {
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

                        _mainThreadQueue.Enqueue(() =>
                        {
                            _pendingBinaryResponse = null;
                            string response = ProcessMessage(f.Text);
                            byte[] pendingBinary = _pendingBinaryResponse;
                            _pendingBinaryResponse = null;

                            if (response != null && client.IsConnected)
                            {
                                _ = SendWsTextAsync(client.Stream, response).ContinueWith(t =>
                                {
                                    // テキスト応答の直後にバイナリフレームを送信
                                    if (pendingBinary != null && client.IsConnected)
                                        _ = SendWsBinaryAsync(client.Stream, pendingBinary);
                                });
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
            string data;

            switch (msg.Target)
            {
                case "meshList":
                    data = RemoteDataProvider.QueryMeshList(Context, msg.Fields);
                    break;

                case "meshData":
                    int index = GetParamInt(msg, "index", 0);
                    data = RemoteDataProvider.QueryMeshData(Context, index, msg.Fields);
                    break;

                case "modelInfo":
                    data = RemoteDataProvider.QueryModelInfo(Context);
                    break;

                case "availableFields":
                    data = RemoteDataProvider.QueryAvailableFields();
                    break;

                case "meshBinary":
                    // バイナリ形式でメッシュデータを返す（応答はJSON+後続バイナリフレーム）
                    return ProcessMeshBinaryQuery(msg);

                case "project":
                    // プロジェクト全体をバイナリで返す
                    return ProcessProjectQuery(msg);

                default:
                    return BuildErrorResponse(msg.Id, $"Unknown target: {msg.Target}");
            }

            return BuildSuccessResponse(msg.Id, data);
        }

        /// <summary>
        /// meshBinaryクエリ: 指定メッシュのバイナリデータをバイナリフレームで送信
        /// params: index=メッシュインデックス, flags=MeshFieldFlagsの数値
        /// </summary>
        private string ProcessMeshBinaryQuery(RemoteMessage msg)
        {
            if (Context?.Model == null)
                return BuildErrorResponse(msg.Id, "No model");

            int index = GetParamInt(msg, "index", -1);
            if (index < 0 || index >= Context.Model.Count)
                return BuildErrorResponse(msg.Id, "Invalid index");

            uint flagsValue = (uint)GetParamInt(msg, "flags", (int)MeshFieldFlags.VertexBasic);
            var flags = (MeshFieldFlags)flagsValue;

            var mc = Context.Model.MeshContextList[index];
            byte[] binaryData = RemoteBinarySerializer.Serialize(mc, flags);
            if (binaryData == null)
                return BuildErrorResponse(msg.Id, "Serialize failed");

            // 送信元クライアントにバイナリフレームで送信
            // Broadcastではなくリクエスト元に返すため、ここではキューに入れる
            // （ProcessMessageの呼び出し元がメインスレッドなので直接送信は不可）
            // 代わりにJSON応答でサイズを返し、直後にバイナリフレームを送る
            _pendingBinaryResponse = binaryData;

            Log($"meshBinary: [{index}] flags={flags} size={binaryData.Length}B");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("binarySize", binaryData.Length);
            jb.KeyValue("vertexCount", (int)mc.VertexCount);
            jb.KeyValue("faceCount", (int)mc.FaceCount);
            jb.KeyValue("flags", (int)flagsValue);
            jb.EndObject();

            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        /// <summary>テキスト応答の直後に送るバイナリデータ（1回使い切り）</summary>
        private byte[] _pendingBinaryResponse;

        /// <summary>
        /// projectクエリ: プロジェクト全体をPLRPバイナリフレームで送信
        /// </summary>
        private string ProcessProjectQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null)
                return BuildErrorResponse(msg.Id, "No project and no model");

            byte[] binaryData = RemoteProjectSerializer.Serialize(project);
            if (binaryData == null)
                return BuildErrorResponse(msg.Id, "Serialize failed");

            _pendingBinaryResponse = binaryData;

            Log($"project: {project.ModelCount} models, {binaryData.Length}B");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("binarySize", binaryData.Length);
            jb.KeyValue("projectName", project.Name);
            jb.KeyValue("modelCount", project.ModelCount);
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
        /// プロジェクト全体をPLRPバイナリで全クライアントに送信
        /// </summary>
        private void SendProject()
        {
            var project = GetProjectContext();
            if (project == null)
            {
                Log("プロジェクト/モデルなし");
                return;
            }

            byte[] data = RemoteProjectSerializer.Serialize(project);
            if (data != null)
            {
                BroadcastBinaryAsync(data);
                Log($"プロジェクト送信: {project.ModelCount}モデル ({data.Length}B)");
            }
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
