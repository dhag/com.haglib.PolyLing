// Remote/RemoteServerCore.cs
// WebSocketサーバーのコアロジック。UnityEditor非依存。
// EditorWindow（RemoteServer）またはスタンドアロンアプリからホストされる。
//
// 使用方法:
//   var core = new RemoteServerCore(() => toolContext, port: 8765);
//   core.OnLog     = msg => Debug.Log(msg);
//   core.OnRepaint = () => editorWindow.Repaint();   // または独自UIの更新
//   core.Start();
//   // ゲームループ/EditorApplication.updateから毎フレーム呼ぶ
//   core.Tick();

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Model;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    /// <summary>
    /// WebSocketサーバーコア。EditorWindow非依存。
    /// スタンドアロン化の際はこのクラスをそのまま使用できる。
    /// </summary>
    public class RemoteServerCore
    {
        // ================================================================
        // 設定・状態
        // ================================================================

        public int  Port      { get; set; }
        public bool IsRunning { get; private set; }

        public int ClientCount
        {
            get { lock (_clientLock) { return _clients.Count; } }
        }

        // ================================================================
        // コールバック（ホスト側が設定）
        // ================================================================

        /// <summary>ログ出力コールバック。nullなら無視。</summary>
        public Action<string> OnLog;

        /// <summary>UI再描画要求コールバック（EditorWindow.Repaint等）。</summary>
        public Action OnRepaint;

        /// <summary>
        /// PanelCommandディスパッチコールバック。
        /// PanelContext.SendCommand を渡すとDispatchPanelCommandを通じて全コマンドが処理される。
        /// nullの場合はlegacyの直接ToolContext操作にフォールバックする。
        /// </summary>
        public Action<PanelCommand> DispatchCommand;

        // ================================================================
        // コンテキスト注入
        // ================================================================

        private readonly Func<ToolContext> _contextProvider;

        private ToolContext Context => _contextProvider?.Invoke();

        // ================================================================
        // TCP / WebSocket
        // ================================================================

        private TcpListener                  _tcpListener;
        private CancellationTokenSource      _cts;
        private readonly List<WsClient>      _clients    = new List<WsClient>();
        private readonly object              _clientLock = new object();
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // ================================================================
        // プッシュ／画像
        // ================================================================

        private ModelContext   _subscribedModel;
        private readonly List<ImageEntry> _capturedImages = new List<ImageEntry>();
        private ushort         _nextImageId;

        /// <summary>バッチ送信用：テキスト応答直後に送るバイナリフレーム（1回使い切り）</summary>
        private List<byte[]> _pendingBinaryResponses;

        // ================================================================
        // コンストラクタ
        // ================================================================

        /// <param name="contextProvider">ToolContextを返すデリゲート（毎回動的取得）</param>
        /// <param name="port">待ち受けポート番号（デフォルト8765）</param>
        public RemoteServerCore(Func<ToolContext> contextProvider, int port = 8765)
        {
            _contextProvider = contextProvider;
            Port = port;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        public void Start()
        {
            if (IsRunning) return;
            try
            {
                _cts         = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Loopback, Port);
                _tcpListener.Start();
                IsRunning = true;

                SubscribeModel();
                _ = AcceptClientsAsync(_cts.Token);

                Log($"サーバー起動: http://localhost:{Port}/");
            }
            catch (Exception ex)
            {
                Log($"起動失敗: {ex.Message}");
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            UnsubscribeModel();
            _cts?.Cancel();

            lock (_clientLock)
            {
                foreach (var c in _clients) c.Close();
                _clients.Clear();
            }

            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;

            Log("サーバー停止");
        }

        /// <summary>
        /// メインスレッドキューを処理する。
        /// EditorApplication.update またはスタンドアロンのUpdate()から毎フレーム呼ぶ。
        /// </summary>
        public void Tick()
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
        // 画像管理（Texture2D変換はホスト側で実施）
        // ================================================================

        /// <summary>
        /// 既にシリアライズ済みのImageEntryを送信リストに追加。
        /// Texture2D → ImageEntry 変換はRemoteServer（EditorWindow側）で行う。
        /// </summary>
        public void AddCapturedImageEntry(ImageEntry entry)
        {
            if (entry == null) return;
            _capturedImages.Add(entry);
            Log($"キャプチャ追加: ID={entry.Id} {entry.Width}x{entry.Height} ({entry.Data.Length}B)");
            OnRepaint?.Invoke();
        }

        public List<ImageEntry> CapturedImages => _capturedImages;

        // ================================================================
        // 公開送信API
        // ================================================================

        public void SendProjectHeader()
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
                    var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                        model.MeshContextList[si], mi, si);
                    if (ms != null) frames.Add(ms);
                }
            }

            foreach (var f in frames) BroadcastBinaryAsync(f);
            Log($"プロジェクトヘッダ送信: {frames.Count}フレーム");
        }

        public void SendCapturedImages()
        {
            if (_capturedImages.Count == 0) return;
            byte[] data = RemoteImageSerializer.Serialize(_capturedImages);
            if (data != null)
            {
                BroadcastBinaryAsync(data);
                Log($"キャプチャ画像送信: {_capturedImages.Count}枚 ({data.Length}B)");
            }
        }

        public void ClearCapturedImages()
        {
            _capturedImages.Clear();
            Log("キャプチャリストクリア");
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
                catch (SocketException)         { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        _mainThreadQueue.Enqueue(() => Log($"接続受付エラー: {ex.Message}"));
                }
            }
        }

        private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken ct)
        {
            System.Net.Sockets.NetworkStream stream = null;
            try
            {
                stream = tcpClient.GetStream();
                string httpRequest = await ReadHttpRequestAsync(stream, ct);
                if (string.IsNullOrEmpty(httpRequest)) { tcpClient.Close(); return; }

                if (httpRequest.IndexOf("Upgrade: websocket", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string wsKey = ExtractWebSocketKey(httpRequest);
                    if (wsKey == null) { tcpClient.Close(); return; }

                    string acceptKey = ComputeAcceptKey(wsKey);
                    string handshake =
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Sec-WebSocket-Accept: " + acceptKey + "\r\n" +
                        "\r\n";

                    byte[] hsBytes = Encoding.UTF8.GetBytes(handshake);
                    await stream.WriteAsync(hsBytes, 0, hsBytes.Length, ct);

                    var wsClient = new WsClient(tcpClient, stream);
                    lock (_clientLock) { _clients.Add(wsClient); }

                    _mainThreadQueue.Enqueue(() =>
                    {
                        Log("クライアント接続");
                        OnRepaint?.Invoke();
                    });

                    await HandleWebSocketAsync(wsClient, ct);
                }
                else
                {
                    string html  = RemoteHtmlClient.GetHtml(Port);
                    byte[] body  = Encoding.UTF8.GetBytes(html);
                    string httpResp =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/html; charset=utf-8\r\n" +
                        "Content-Length: " + body.Length + "\r\n" +
                        "Connection: close\r\n" +
                        "\r\n";

                    byte[] hdrBytes = Encoding.UTF8.GetBytes(httpResp);
                    await stream.WriteAsync(hdrBytes, 0, hdrBytes.Length, ct);
                    await stream.WriteAsync(body,     0, body.Length,     ct);
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

        private enum WsFrameType { Text, Binary, Ping, Close }

        private struct WsFrame
        {
            public WsFrameType Type;
            public string  Text;
            public byte[]  Binary;
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
                            var pending = _pendingBinaryResponses;
                            _pendingBinaryResponses = null;

                            if (response != null && client.IsConnected)
                            {
                                try
                                {
                                    await SendWsTextAsync(client.Stream, response);
                                    if (pending != null && client.IsConnected)
                                        await SendWsBinaryAsync(client.Stream, BuildBatch(pending));
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
            catch (System.IO.IOException)       { }
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
                    OnRepaint?.Invoke();
                });
            }
        }

        private byte[] ProcessBinaryMessage(byte[] data)
        {
            var header = RemoteBinarySerializer.ReadHeader(data);
            if (header == null) { Log("バイナリ: 無効なヘッダ"); return null; }

            var h = header.Value;
            Log($"バイナリ受信: type={h.MessageType} flags={h.FieldFlags} V={h.VertexCount} F={h.FaceCount}");

            switch (h.MessageType)
            {
                case BinaryMessageType.MeshData:
                {
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
                        Log($"ファイル受信: {ext} ({fileData.Length} bytes)");
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

        private static async Task<WsFrame?> ReadWsFrameAsync(
            System.Net.Sockets.NetworkStream stream, CancellationToken ct)
        {
            byte[] header = new byte[2];
            if (!await ReadExactAsync(stream, header, 0, 2, ct)) return null;

            int  opcode     = header[0] & 0x0F;
            bool masked     = (header[1] & 0x80) != 0;
            long payloadLen = header[1] & 0x7F;

            if (opcode == 0x08) return new WsFrame { Type = WsFrameType.Close };

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
                for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
            }

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                if (!await ReadExactAsync(stream, maskKey, 0, 4, ct)) return null;
            }

            if (payloadLen > 1_000_000_000L) return null;   // 1GB上限
            byte[] payload = new byte[payloadLen];
            if (payloadLen > 0 && !await ReadExactAsync(stream, payload, 0, (int)payloadLen, ct))
                return null;

            if (masked && maskKey != null)
                for (int i = 0; i < payload.Length; i++) payload[i] ^= maskKey[i % 4];

            if (opcode == 0x09) // Ping → Pong
            {
                byte[] pong = new byte[2 + payload.Length];
                pong[0] = 0x8A;
                pong[1] = (byte)payload.Length;
                Array.Copy(payload, 0, pong, 2, payload.Length);
                try { await stream.WriteAsync(pong, 0, pong.Length, ct); } catch { }
                return new WsFrame { Type = WsFrameType.Ping };
            }

            return opcode == 0x02
                ? new WsFrame { Type = WsFrameType.Binary, Binary = payload }
                : new WsFrame { Type = WsFrameType.Text,   Text   = Encoding.UTF8.GetString(payload) };
        }

        private static async Task SendWsTextAsync(
            System.Net.Sockets.NetworkStream stream, string message)
            => await SendWsRawAsync(stream, 0x81, Encoding.UTF8.GetBytes(message));

        private static async Task SendWsBinaryAsync(
            System.Net.Sockets.NetworkStream stream, byte[] payload)
            => await SendWsRawAsync(stream, 0x82, payload);

        private static async Task SendWsRawAsync(
            System.Net.Sockets.NetworkStream stream, byte opcodeWithFin, byte[] payload)
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
                for (int i = 7; i >= 0; i--) { frame[2 + i] = (byte)(len & 0xFF); len >>= 8; }
                Array.Copy(payload, 0, frame, 10, payload.Length);
            }
            try { await stream.WriteAsync(frame, 0, frame.Length); } catch { }
        }

        private static async Task<bool> ReadExactAsync(
            System.Net.Sockets.NetworkStream stream,
            byte[] buf, int offset, int count, CancellationToken ct)
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

        private static async Task<string> ReadHttpRequestAsync(
            System.Net.Sockets.NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            byte[] buf = new byte[1];
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

        // ================================================================
        // WebSocketハンドシェイクヘルパー
        // ================================================================

        private static string ExtractWebSocketKey(string httpRequest)
        {
            foreach (string line in httpRequest.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring("Sec-WebSocket-Key:".Length).Trim();
            }
            return null;
        }

        private static string ComputeAcceptKey(string wsKey)
        {
            string combined = wsKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using (var sha1 = SHA1.Create())
                return Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(combined)));
        }

        // ================================================================
        // メッセージ処理（クエリ・コマンド）
        // ================================================================

        private string ProcessMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            RemoteMessage msg;
            try { msg = JsonParser.Parse(json); }
            catch (Exception ex)
            {
                Log($"パースエラー: {ex.Message}");
                return BuildErrorResponse(null, "Parse error");
            }

            Log($"受信: type={msg.Type} target={msg.Target} action={msg.Action}");

            if (msg.Type == "query")   return ProcessQuery(msg);
            if (msg.Type == "command") return ProcessCommand(msg);
            return BuildErrorResponse(msg.Id, $"Unknown type: {msg.Type}");
        }

        private string ProcessQuery(RemoteMessage msg)
        {
            switch (msg.Target)
            {
                case "meshList":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryMeshList(Context, msg.Fields));

                case "meshData":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryMeshData(Context, GetParamInt(msg, "index", 0), msg.Fields));

                case "modelInfo":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryModelInfo(Context));

                case "availableFields":
                    return BuildSuccessResponse(msg.Id,
                        RemoteDataProvider.QueryAvailableFields());

                case "project_header":    return ProcessProjectHeaderQuery(msg);
                case "model_meta":        return ProcessModelMetaQuery(msg);
                case "mesh_data":         return ProcessMeshDataQuery(msg);
                case "mesh_data_batch":   return ProcessMeshDataBatchQuery(msg);

                default:
                    return BuildErrorResponse(msg.Id, $"Unknown target: {msg.Target}");
            }
        }

        private string ProcessProjectHeaderQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            var binaries = new List<byte[]>();
            byte[] header = RemoteProgressiveSerializer.SerializeProjectHeader(project);
            if (header == null) return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(header);

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, mi);
                if (mm != null) binaries.Add(mm);
                for (int si = 0; si < model.Count; si++)
                {
                    var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                        model.MeshContextList[si], mi, si);
                    if (ms != null) binaries.Add(ms);
                }
            }

            _pendingBinaryResponses = binaries;

            int totalMeshes = 0;
            for (int mi = 0; mi < project.ModelCount; mi++) totalMeshes += project.Models[mi].Count;
            Log($"project_header: {project.ModelCount}モデル {totalMeshes}メッシュ ({binaries.Count}フレーム)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("projectName", project.Name);
            jb.KeyValue("modelCount",  project.ModelCount);
            jb.KeyValue("meshCount",   totalMeshes);
            jb.KeyValue("frameCount",  binaries.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessModelMetaQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            var binaries = new List<byte[]>();

            var mm = RemoteProgressiveSerializer.SerializeModelMeta(model, modelIndex);
            if (mm == null) return BuildErrorResponse(msg.Id, "Serialize failed");
            binaries.Add(mm);

            for (int si = 0; si < model.Count; si++)
            {
                var ms = RemoteProgressiveSerializer.SerializeMeshSummary(
                    model.MeshContextList[si], modelIndex, si);
                if (ms != null) binaries.Add(ms);
            }

            _pendingBinaryResponses = binaries;
            Log($"model_meta: [{modelIndex}] {model.Name} meshes={model.Count}");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.KeyValue("modelName",  model.Name);
            jb.KeyValue("meshCount",  model.Count);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessMeshDataQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            int meshIndex  = GetParamInt(msg, "meshIndex",  -1);

            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model = project.Models[modelIndex];
            if (meshIndex < 0 || meshIndex >= model.Count)
                return BuildErrorResponse(msg.Id, $"Invalid meshIndex: {meshIndex}");

            var mc       = model.MeshContextList[meshIndex];
            var flags    = (MeshFieldFlags)(uint)GetParamInt(msg, "flags", (int)MeshFieldFlags.All);
            var binData  = RemoteProgressiveSerializer.SerializeMeshData(mc, modelIndex, meshIndex, flags);
            if (binData == null) return BuildErrorResponse(msg.Id, "Serialize failed");

            _pendingBinaryResponses = new List<byte[]> { binData };
            Log($"mesh_data: [{modelIndex}][{meshIndex}] {mc.Name} V={mc.VertexCount} ({binData.Length}B)");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex",  modelIndex);
            jb.KeyValue("meshIndex",   meshIndex);
            jb.KeyValue("meshName",    mc.Name);
            jb.KeyValue("vertexCount", mc.VertexCount);
            jb.KeyValue("faceCount",   mc.FaceCount);
            jb.KeyValue("binarySize",  binData.Length);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessMeshDataBatchQuery(RemoteMessage msg)
        {
            var project = GetProjectContext();
            if (project == null) return BuildErrorResponse(msg.Id, "No project");

            int modelIndex = GetParamInt(msg, "modelIndex", project.CurrentModelIndex);
            if (modelIndex < 0 || modelIndex >= project.ModelCount)
                return BuildErrorResponse(msg.Id, $"Invalid modelIndex: {modelIndex}");

            var model    = project.Models[modelIndex];
            string category = GetParamString(msg, "category", "drawable");

            IReadOnlyList<TypedMeshEntry> entries;
            switch (category)
            {
                case "bone":  entries = model.Bones;  break;
                case "morph": entries = model.Morphs; break;
                case "all":   entries = model.TypedIndices.GetEntries(MeshCategory.All); break;
                default:      entries = model.DrawableMeshes; break;
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
                _pendingBinaryResponses = new List<byte[]> { BuildBatch(new List<byte[]>()) };
                Log($"mesh_data_batch: [{modelIndex}] {category} → 0件");
                var jbEmpty = new JsonBuilder();
                jbEmpty.BeginObject();
                jbEmpty.KeyValue("modelIndex", modelIndex);
                jbEmpty.KeyValue("category",   category);
                jbEmpty.KeyValue("meshCount",  0);
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
            jb.KeyValue("category",   category);
            jb.KeyValue("meshCount",  frames.Count);
            jb.KeyValue("binarySize", totalBytes);
            jb.EndObject();
            return BuildSuccessResponse(msg.Id, jb.ToString());
        }

        private string ProcessCommand(RemoteMessage msg)
        {
            try
            {
                // DispatchCommandが設定されている場合はPanelCommand経由で全処理
                if (DispatchCommand != null)
                    return ProcessCommandViaPanelCommand(msg);

                // フォールバック: ToolContext直接操作（後方互換）
                if (Context == null) return BuildErrorResponse(msg.Id, "No ToolContext");
                return ProcessCommandLegacy(msg);
            }
            catch (Exception ex)
            {
                Log($"コマンドエラー: {ex.Message}");
                return BuildErrorResponse(msg.Id, ex.Message);
            }
        }

        /// <summary>
        /// PanelCommand経由のコマンド処理。
        /// JSON → PanelCommand に変換してDispatchCommandに流す。
        /// DispatchPanelCommand（SummaryNotify）が実処理を担う。
        /// </summary>
        private string ProcessCommandViaPanelCommand(RemoteMessage msg)
        {
            int modelIndex = GetParamInt(msg, "modelIndex", 0);
            PanelCommand cmd = BuildPanelCommand(msg, modelIndex);
            if (cmd == null)
                return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");

            DispatchCommand(cmd);
            Log($"cmd: {msg.Action} model={modelIndex}");
            return BuildSuccessResponse(msg.Id, "true");
        }

        /// <summary>
        /// RemoteMessageからPanelCommandを組み立てる。
        /// 対応するコマンドがない場合はnullを返す。
        /// </summary>
        private static PanelCommand BuildPanelCommand(RemoteMessage msg, int modelIndex)
        {
            // int[]パラメータ取得ヘルパー（"1,2,3" 形式）
            int[] GetIndices(string key)
            {
                if (msg.Params == null || !msg.Params.TryGetValue(key, out var s) || string.IsNullOrEmpty(s))
                    return System.Array.Empty<int>();
                var parts = s.Split(',');
                var result = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    int.TryParse(parts[i].Trim(), out result[i]);
                return result;
            }

            switch (msg.Action)
            {
                // ── 選択 ──────────────────────────────────────────────
                case "selectMesh":
                {
                    var indices  = GetIndices("indices");
                    if (indices.Length == 0)
                    {
                        int idx = GetParamInt(msg, "index", -1);
                        indices = idx >= 0 ? new[] { idx } : System.Array.Empty<int>();
                    }
                    var category = (MeshCategory)GetParamInt(msg, "category", (int)MeshCategory.Drawable);
                    return new SelectMeshCommand(modelIndex, category, indices);
                }

                // ── 属性変更 ──────────────────────────────────────────
                case "toggleVisibility":
                    return new ToggleVisibilityCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "setBatchVisibility":
                    return new SetBatchVisibilityCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "visible", "true") == "true");

                case "toggleLock":
                    return new ToggleLockCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "cycleMirrorType":
                    return new CycleMirrorTypeCommand(
                        modelIndex, GetParamInt(msg, "masterIndex", 0));

                case "renameMesh":
                    return new RenameMeshCommand(
                        modelIndex,
                        GetParamInt(msg, "masterIndex", 0),
                        GetParamString(msg, "name", ""));

                // ── リスト操作 ────────────────────────────────────────
                case "addMesh":
                    return new AddMeshCommand(modelIndex);

                case "deleteMeshes":
                    return new DeleteMeshesCommand(modelIndex, GetIndices("masterIndices"));

                case "duplicateMeshes":
                    return new DuplicateMeshesCommand(modelIndex, GetIndices("masterIndices"));

                // ── BonePose ──────────────────────────────────────────
                case "initBonePose":
                    return new InitBonePoseCommand(modelIndex, GetIndices("masterIndices"));

                case "setBonePoseActive":
                    return new SetBonePoseActiveCommand(
                        modelIndex,
                        GetIndices("masterIndices"),
                        GetParamString(msg, "active", "true") == "true");

                case "resetBonePoseLayers":
                    return new ResetBonePoseLayersCommand(modelIndex, GetIndices("masterIndices"));

                case "bakePoseToBindPose":
                    return new BakePoseToBindPoseCommand(modelIndex, GetIndices("masterIndices"));

                // ── モデル操作 ────────────────────────────────────────
                case "switchModel":
                    return new SwitchModelCommand(
                        GetParamInt(msg, "targetModelIndex", 0));

                case "renameModel":
                    return new RenameModelCommand(
                        modelIndex, GetParamString(msg, "name", ""));

                case "deleteModel":
                    return new DeleteModelCommand(modelIndex);

                default:
                    return null;
            }
        }

        /// <summary>後方互換: ToolContext直接操作（DispatchCommandなし時）</summary>
        private string ProcessCommandLegacy(RemoteMessage msg)
        {
            switch (msg.Action)
            {
                case "selectMesh":
                {
                    int index = GetParamInt(msg, "index", -1);
                    if (index < 0) return BuildErrorResponse(msg.Id, "Invalid index");
                    Context.SelectMeshContext?.Invoke(index);
                    Context.OnMeshSelectionChanged?.Invoke();
                    Context.Repaint?.Invoke();
                    Log($"selectMesh(legacy): {index}");
                    return BuildSuccessResponse(msg.Id, "true");
                }
                case "updateAttribute":
                {
                    int index = GetParamInt(msg, "index", -1);
                    if (index < 0) return BuildErrorResponse(msg.Id, "Invalid index");
                    var change = new MeshAttributeChange { Index = index };
                    if (msg.Params.TryGetValue("name",    out var n)) change.Name      = n;
                    if (msg.Params.TryGetValue("visible", out var v)) change.IsVisible = v == "true";
                    if (msg.Params.TryGetValue("locked",  out var l)) change.IsLocked  = l == "true";
                    Context.UpdateMeshAttributes?.Invoke(new List<MeshAttributeChange> { change });
                    Context.Repaint?.Invoke();
                    Log($"updateAttribute(legacy): [{index}]");
                    return BuildSuccessResponse(msg.Id, "true");
                }
                default:
                    return BuildErrorResponse(msg.Id, $"Unknown action: {msg.Action}");
            }
        }

        // ================================================================
        // バッチフレーム組み立て
        // [4B Magic=PLRB][1B Version][3B padding][4B FrameCount]{ [4B Len][Data] }×N
        // ================================================================

        private static byte[] BuildBatch(List<byte[]> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                using (var ms = new System.IO.MemoryStream(12))
                using (var w  = new System.IO.BinaryWriter(ms))
                {
                    w.Write(RemoteMagic.Batch);
                    w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((uint)0);
                    return ms.ToArray();
                }
            }
            if (frames.Count == 1) return frames[0];

            int totalBody = 0;
            foreach (var f in frames) totalBody += 4 + f.Length;

            using (var ms = new System.IO.MemoryStream(12 + totalBody))
            using (var w  = new System.IO.BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Batch);
                w.Write((byte)1); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                w.Write((uint)frames.Count);
                foreach (var f in frames) { w.Write((uint)f.Length); w.Write(f); }
                return ms.ToArray();
            }
        }

        // ================================================================
        // Pushイベント（モデル変更通知）
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
            string data     = RemoteDataProvider.QueryMeshList(Context, null);
            string pushJson = BuildPushMessage("meshListChanged", data);
            BroadcastAsync(pushJson);
        }

        private void BroadcastAsync(string json)
        {
            List<WsClient> snapshot;
            lock (_clientLock) { snapshot = new List<WsClient>(_clients); }
            foreach (var c in snapshot)
                if (c.IsConnected) _ = SendWsTextAsync(c.Stream, json);
        }

        private void BroadcastBinaryAsync(byte[] data)
        {
            List<WsClient> snapshot;
            lock (_clientLock) { snapshot = new List<WsClient>(_clients); }
            foreach (var c in snapshot)
                if (c.IsConnected) _ = SendWsBinaryAsync(c.Stream, data);
        }

        // ================================================================
        // レスポンスビルダー
        // ================================================================

        private static string BuildSuccessResponse(string id, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",      id);
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyRaw("data",      dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildErrorResponse(string id, string error)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",      id);
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", false);
            jb.KeyValue("error",   error);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildPushMessage(string eventName, string dataJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("id",    (string)null);
            jb.KeyValue("type",  "push");
            jb.KeyValue("event", eventName);
            jb.KeyRaw("data",    dataJson);
            jb.EndObject();
            return jb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private ProjectContext GetProjectContext() => Context?.Project;

        private static int GetParamInt(RemoteMessage msg, string key, int def)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val) &&
                int.TryParse(val, out int r)) return r;
            return def;
        }

        private static string GetParamString(RemoteMessage msg, string key, string def)
        {
            if (msg.Params != null && msg.Params.TryGetValue(key, out var val) && val != null)
                return val;
            return def;
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            OnLog?.Invoke(line);
        }

        // ================================================================
        // WebSocketクライアント管理
        // ================================================================

        private class WsClient
        {
            public TcpClient     Tcp    { get; }
            public System.Net.Sockets.NetworkStream Stream { get; }

            public bool IsConnected
            {
                get { try { return Tcp != null && Tcp.Connected; } catch { return false; } }
            }

            public WsClient(TcpClient tcp, System.Net.Sockets.NetworkStream stream)
            {
                Tcp    = tcp;
                Stream = stream;
            }

            public void Close()
            {
                try { Stream?.Close(); } catch { }
                try { Tcp?.Close();    } catch { }
            }
        }
    }
}
