// MotionLiveHandler.cs
// ライブ受信：UDP で届くマッスル（MotionLivePacket）を受け取り、現在のモデルへ当てる。
// 「受け入れ許可」で指定ポートの待ち受けを始め、「不許可」で止める。
//
// ■ スレッド
//   受信は専用スレッド。手元には最新の 1 フレームだけを残し（古いものは上書き）、
//   メインスレッドへの適用依頼は SynchronizationContext.Post で出す。
//   前の依頼を処理し終えるまで次の依頼は出さない（溜めない・毎フレームの見回りをしない）。
//   RemoteServerCore と同じく、許可したとき（メインスレッド）の SynchronizationContext を控える。
//
// ■ 当てるもの
//   マッスルだけ。有効マスクが 0 のマッスルはトラック無しと同じ扱い（その骨は当てない）。
//   RootT / RootQ は受け取るが当てない（MotionClipApplier の再生経路も当てていない）。
//   可動域は UnityClipApplier の既定値（T ポーズ基準の Unity 定義値）。
//   当てるのは再生用の姿勢（保存しない表示状態）で、Undo は持たない（motionClip と同じ）。
//   不許可にしても姿勢は戻さない（戻すのは resetPose）。
//
// ■ 画面への配信（WebSocket）
//   受け入れ許可中は、画面（モーションスタジオ等）向けの WebSocket サーバも streamPort で立てる。
//   正しい形式の UDP パケットが届くたびに、画面が 1 つ以上つながっていれば、受信スレッドから
//   {type:'muscles', seq, time, muscles:{マッスル名:値}, rootT:[x,y,z], rootQ:[x,y,z,w]} を全員へ送る
//   （有効マスクが 1 のマッスルだけ。モデルの有無に関係なく送る）。
//   文字列は Duplex 封筒（Text フレーム {type,id,items:[{type:3,data:"…"}]}）に包まれて届く。
//   将来は同じ接続でバイナリ（モデル等）も送れる。受け手は中身の type で振り分ける。

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using HagLib.NET.Duplex;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Player
{
    [PLTool("motionLive", Description = "ライブ受信（UDP で届くマッスルを現在のモデルへ当てる）")]
    public sealed class MotionLiveHandler
    {
        public Func<ModelContext> GetModel;
        /// <summary>フレームを当てた後に呼ぶ（表示の更新）。</summary>
        public Action             OnFrameApplied;

        public const int DefaultPort       = 12360;
        public const int DefaultStreamPort = 12361;

        private readonly UnityClipApplier _applier = new UnityClipApplier();
        private readonly object _lock = new object();

        private UdpClient              _client;
        private Thread                 _thread;
        private volatile bool          _stop;
        private SynchronizationContext _syncCtx;
        private int                    _muscleCount;
        private string[]               _muscleNames;
        private WebSocketDuplexServer  _ws;

        // 受信スレッドが書き、メインスレッドが読む最新フレーム（_lock で守る）
        private MotionLiveFrame _latest = new MotionLiveFrame();
        private MotionLiveFrame _work   = new MotionLiveFrame();
        private bool            _hasLatest;
        private bool            _applyPending;

        private int             _received;
        private int             _rejected;
        private int             _applied;
        private volatile string _lastSender = "";
        private volatile string _lastReject = "";

        [PLToolState(Description = "受け入れ許可中（待ち受け中）か")]
        public bool   Listening => _thread != null;
        [PLToolState(Description = "待ち受けポート（停止中は直近の値）")]
        public int    Port { get; private set; } = DefaultPort;
        [PLToolState(Description = "画面への配信ポート（WebSocket。停止中は直近の値）")]
        public int    StreamPort { get; private set; } = DefaultStreamPort;
        [PLToolState(Description = "配信先としてつながっている画面の数")]
        public int    ViewerCount => _ws?.Clients.Length ?? 0;
        [PLToolState(Description = "受け取ったパケット数（形式が正しいもの）")]
        public int    ReceivedCount => Volatile.Read(ref _received);
        [PLToolState(Description = "捨てたパケット数（形式違い・マッスル数違い）")]
        public int    RejectedCount => Volatile.Read(ref _rejected);
        [PLToolState(Description = "モデルへ当てたフレーム数")]
        public int    AppliedCount => Volatile.Read(ref _applied);
        [PLToolState(Description = "最後に受け取ったパケットの送信元")]
        public string LastSender => _lastSender;
        [PLToolState(Description = "最後に捨てたパケットの理由")]
        public string LastReject => _lastReject;
        [PLToolState(Description = "直近の accept の失敗理由（成功なら空）")]
        public string Error { get; private set; } = "";

        /// <summary>受け入れ許可：port で UDP の待ち受け、streamPort で画面への配信を始める。待ち受け中なら開き直す。</summary>
        [PLToolAction(Description = "受け入れ許可（port で UDP の待ち受け、streamPort で画面への WebSocket 配信を始める）")]
        public void Accept(int port, int streamPort)
        {
            Reject();
            Error = "";
            if (port < 1 || port > 65535) { Error = $"ポート番号が正しくありません（{port}）"; return; }
            if (streamPort < 1 || streamPort > 65535) { Error = $"配信ポート番号が正しくありません（{streamPort}）"; return; }
            if (port == streamPort) { Error = "受信ポートと配信ポートは別の番号にしてください"; return; }
            Port       = port;
            StreamPort = streamPort;

            _syncCtx     = SynchronizationContext.Current;
            _muscleCount = HumanTrait.MuscleCount;
            _muscleNames = HumanTrait.MuscleName;
            if (_syncCtx == null) { Error = "メインスレッドの SynchronizationContext がありません"; return; }

            // 画面への配信（WebSocket）。立てられなければ UDP も開かない。
            try
            {
                _ws = new WebSocketDuplexServer { DefaultFrame = WebSocketFrameKind.Text };
                _ = _ws.StartAsync(streamPort);
            }
            catch (Exception e)
            {
                try { _ = _ws?.StopAsync(); } catch { }
                _ws = null;
                Error = $"配信ポート {streamPort} を開けません: {e.Message}";
                return;
            }

            try
            {
                _client = new UdpClient(port);
            }
            catch (SocketException e)
            {
                _client = null;
                try { _ = _ws?.StopAsync(); } catch { }
                _ws = null;
                Error = $"ポート {port} を開けません: {e.Message}";
                return;
            }

            Interlocked.Exchange(ref _received, 0);
            Interlocked.Exchange(ref _rejected, 0);
            Interlocked.Exchange(ref _applied, 0);
            _lastSender = "";
            _lastReject = "";
            lock (_lock) { _hasLatest = false; _applyPending = false; }

            _stop   = false;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MotionLiveHandler" };
            _thread.Start();
        }

        /// <summary>不許可：待ち受けを止める。姿勢はそのまま。</summary>
        [PLToolAction(Description = "不許可（待ち受けを止める。姿勢はそのまま）")]
        public void Reject()
        {
            _stop = true;
            var c = _client;
            _client = null;
            c?.Close();                              // ブロック中の Receive を抜けさせる
            var t = _thread;
            _thread = null;
            if (t != null && t != Thread.CurrentThread) t.Join(1000);
            lock (_lock) { _hasLatest = false; }

            var ws = _ws;
            _ws = null;
            try { _ = ws?.StopAsync(); } catch { }
        }

        /// <summary>ライブ受信で当てた姿勢を初期へ戻す（待ち受けは続ける）。</summary>
        [PLToolAction(Description = "ライブ受信で当てた姿勢を初期へ戻す")]
        public void ResetPose()
        {
            var model = GetModel?.Invoke();
            if (model == null) return;
            _applier.ResetAllBones(model);
            OnFrameApplied?.Invoke();
        }

        // ---------------- 受信スレッド ----------------

        private void ReceiveLoop()
        {
            var client = _client;
            var parsed = new MotionLiveFrame();
            while (!_stop && client != null)
            {
                byte[] data;
                IPEndPoint remote = null;
                try
                {
                    data = client.Receive(ref remote);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException)
                {
                    if (_stop) break;
                    continue;
                }

                _lastSender = remote != null ? remote.ToString() : "";
                if (!MotionLivePacket.TryRead(data, data.Length, _muscleCount, parsed, out string why))
                {
                    Interlocked.Increment(ref _rejected);
                    _lastReject = why;
                    continue;
                }
                Interlocked.Increment(ref _received);
                Broadcast(parsed);

                bool post;
                lock (_lock)
                {
                    // 最新だけ残す（入れ替え）
                    var tmp = _latest; _latest = parsed; parsed = tmp;
                    _hasLatest = true;
                    post = !_applyPending;
                    _applyPending = true;
                }
                if (post) _syncCtx.Post(_ => ApplyLatest(), null);
            }
        }

        // 画面へ 1 フレーム送る（受信スレッド）。つながっている画面が無ければ何もしない。
        private void Broadcast(MotionLiveFrame f)
        {
            var ws = _ws;
            if (ws == null || ws.Clients.Length == 0) return;

            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(4096);
            sb.Append("{\"type\":\"muscles\",\"seq\":").Append(f.Seq.ToString(inv))
              .Append(",\"time\":").Append(f.Time.ToString("R", inv))
              .Append(",\"muscles\":{");
            bool first = true;
            var names = _muscleNames;
            for (int i = 0; i < f.Muscles.Length && names != null && i < names.Length; i++)
            {
                if (!f.Valid[i]) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(names[i]).Append("\":").Append(f.Muscles[i].ToString("R", inv));
            }
            sb.Append('}');
            if (f.HasRoot)
            {
                sb.Append(",\"rootT\":[").Append(f.RootT.x.ToString("R", inv)).Append(',')
                  .Append(f.RootT.y.ToString("R", inv)).Append(',').Append(f.RootT.z.ToString("R", inv)).Append(']');
                sb.Append(",\"rootQ\":[").Append(f.RootQ.x.ToString("R", inv)).Append(',')
                  .Append(f.RootQ.y.ToString("R", inv)).Append(',').Append(f.RootQ.z.ToString("R", inv)).Append(',')
                  .Append(f.RootQ.w.ToString("R", inv)).Append(']');
            }
            sb.Append('}');

            try
            {
                _ = ws.BroadcastAsync(TypedPayload.FromJson(sb.ToString()).ToMessage(), WebSocketFrameKind.Text);
            }
            catch (Exception) { }   // 画面側の切断などは無視（次のフレームで送り直す）
        }

        // ---------------- メインスレッド ----------------

        private void ApplyLatest()
        {
            MotionLiveFrame f;
            lock (_lock)
            {
                _applyPending = false;
                if (!_hasLatest) return;
                _hasLatest = false;
                // 受信スレッドの上書きと衝突しないよう作業用と入れ替える
                f = _latest; _latest = _work; _work = f;
            }
            if (_thread == null) return;             // 不許可にした後に届いた依頼

            var model = GetModel?.Invoke();
            if (model == null) return;
            _applier.ApplyMuscleFrame(model, f.Muscles, f.Valid, (float)f.Time);
            Interlocked.Increment(ref _applied);
            OnFrameApplied?.Invoke();
        }
    }
}
