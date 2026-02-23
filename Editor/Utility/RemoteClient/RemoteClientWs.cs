// RemoteClient/RemoteClientWs.cs
// WebSocketクライアント（TcpClient + 自前ハンドシェイク/フレーム処理）
// PolyLing名前空間に依存しない独立実装
//
// サーバー側(RemoteServer)と対称の構造。
// クライアント側はマスクを付けて送信する（RFC 6455 必須）。

using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PolyLingRemoteClient
{
    /// <summary>
    /// 受信フレーム種別
    /// </summary>
    public enum WsFrameType { Text, Binary, Ping, Close }

    /// <summary>
    /// 受信フレーム
    /// </summary>
    public struct WsFrame
    {
        public WsFrameType Type;
        public string Text;
        public byte[] Binary;
    }

    /// <summary>
    /// WebSocketクライアント
    /// </summary>
    public class RemoteClientWs : IDisposable
    {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly Random _maskRng = new Random();

        public bool IsConnected
        {
            get
            {
                try { return _tcp != null && _tcp.Connected && _stream != null; }
                catch { return false; }
            }
        }

        // ================================================================
        // 接続
        // ================================================================

        /// <summary>
        /// WebSocketサーバーに接続
        /// </summary>
        public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct)
        {
            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(host, port);
                _stream = _tcp.GetStream();

                // WebSocketハンドシェイク送信
                string wsKey = GenerateWebSocketKey();
                string request =
                    $"GET / HTTP/1.1\r\n" +
                    $"Host: {host}:{port}\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Key: {wsKey}\r\n" +
                    "Sec-WebSocket-Version: 13\r\n" +
                    "\r\n";

                byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                await _stream.WriteAsync(requestBytes, 0, requestBytes.Length, ct);

                // ハンドシェイク応答読み取り
                string response = await ReadHttpResponseAsync(_stream, ct);
                if (response == null ||
                    response.IndexOf("101", StringComparison.Ordinal) < 0)
                {
                    Close();
                    return false;
                }

                // Accept-Keyの検証（簡易: 101が返ればOKとする）
                return true;
            }
            catch
            {
                Close();
                return false;
            }
        }

        // ================================================================
        // 送信
        // ================================================================

        /// <summary>テキストフレーム送信</summary>
        public async Task SendTextAsync(string message)
        {
            if (!IsConnected) return;
            byte[] payload = Encoding.UTF8.GetBytes(message);
            await SendFrameAsync(0x81, payload); // FIN + Text
        }

        /// <summary>バイナリフレーム送信</summary>
        public async Task SendBinaryAsync(byte[] data)
        {
            if (!IsConnected) return;
            await SendFrameAsync(0x82, data); // FIN + Binary
        }

        // ================================================================
        // 受信
        // ================================================================

        /// <summary>フレームを1つ受信</summary>
        public async Task<WsFrame?> ReceiveFrameAsync(CancellationToken ct)
        {
            if (!IsConnected) return null;

            try
            {
                byte[] header = new byte[2];
                if (!await ReadExactAsync(header, 0, 2, ct)) return null;

                int opcode = header[0] & 0x0F;
                bool masked = (header[1] & 0x80) != 0; // サーバーからは非マスク
                long payloadLen = header[1] & 0x7F;

                if (opcode == 0x08)
                    return new WsFrame { Type = WsFrameType.Close };

                // 拡張長
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
                    for (int i = 0; i < 8; i++)
                        payloadLen = (payloadLen << 8) | ext[i];
                }

                // マスクキー（サーバーからは通常なし）
                byte[] maskKey = null;
                if (masked)
                {
                    maskKey = new byte[4];
                    if (!await ReadExactAsync(maskKey, 0, 4, ct)) return null;
                }

                // ペイロード（50MB上限）
                if (payloadLen > 50_000_000) return null;
                byte[] payload = new byte[payloadLen];
                if (payloadLen > 0)
                {
                    if (!await ReadExactAsync(payload, 0, (int)payloadLen, ct)) return null;
                }

                if (masked && maskKey != null)
                {
                    for (int i = 0; i < payload.Length; i++)
                        payload[i] ^= maskKey[i % 4];
                }

                // Ping → Pong
                if (opcode == 0x09)
                {
                    await SendFrameAsync(0x8A, payload); // Pong
                    return new WsFrame { Type = WsFrameType.Ping };
                }

                if (opcode == 0x02)
                    return new WsFrame { Type = WsFrameType.Binary, Binary = payload };
                else
                    return new WsFrame { Type = WsFrameType.Text, Text = Encoding.UTF8.GetString(payload) };
            }
            catch (OperationCanceledException) { return null; }
            catch (IOException) { return null; }
            catch { return null; }
        }

        // ================================================================
        // 切断
        // ================================================================

        public void Close()
        {
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _stream = null;
            _tcp = null;
        }

        public void Dispose()
        {
            Close();
            _cts?.Dispose();
        }

        // ================================================================
        // 内部: フレーム送信（クライアントはマスク必須）
        // ================================================================

        private async Task SendFrameAsync(byte opcodeWithFin, byte[] payload)
        {
            // マスクキー生成
            byte[] maskKey = new byte[4];
            _maskRng.NextBytes(maskKey);

            // マスク適用
            byte[] maskedPayload = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++)
                maskedPayload[i] = (byte)(payload[i] ^ maskKey[i % 4]);

            byte[] frame;
            int headerLen;

            if (payload.Length < 126)
            {
                headerLen = 2 + 4; // header + mask
                frame = new byte[headerLen + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = (byte)(0x80 | payload.Length); // masked flag
                Array.Copy(maskKey, 0, frame, 2, 4);
                Array.Copy(maskedPayload, 0, frame, 6, payload.Length);
            }
            else if (payload.Length <= 65535)
            {
                headerLen = 4 + 4;
                frame = new byte[headerLen + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 0x80 | 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
                Array.Copy(maskKey, 0, frame, 4, 4);
                Array.Copy(maskedPayload, 0, frame, 8, payload.Length);
            }
            else
            {
                headerLen = 10 + 4;
                frame = new byte[headerLen + payload.Length];
                frame[0] = opcodeWithFin;
                frame[1] = 0x80 | 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame[2 + i] = (byte)(len & 0xFF);
                    len >>= 8;
                }
                Array.Copy(maskKey, 0, frame, 10, 4);
                Array.Copy(maskedPayload, 0, frame, 14, payload.Length);
            }

            try
            {
                await _stream.WriteAsync(frame, 0, frame.Length);
            }
            catch { }
        }

        // ================================================================
        // 内部: 読み取りヘルパー
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
            var sb = new StringBuilder();
            byte[] buf = new byte[1];
            int crlfCount = 0;

            while (crlfCount < 4)
            {
                int read = await stream.ReadAsync(buf, 0, 1, ct);
                if (read <= 0) return null;

                char c = (char)buf[0];
                sb.Append(c);

                if ((crlfCount % 2 == 0 && c == '\r') ||
                    (crlfCount % 2 == 1 && c == '\n'))
                    crlfCount++;
                else
                    crlfCount = (c == '\r') ? 1 : 0;

                if (sb.Length > 8192) return null;
            }

            return sb.ToString();
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
