// MotionLiveHandler.cs
// ライブ受信：WebSocket で届くマッスル（MotionLiveJson の {type:'muscles'}）を受け取り、現在のモデルへ当てる。
// 同じフレームを、送ってきた接続以外の接続（モーションスタジオ等の画面）へ転送する。
// 「受け入れ許可」で指定ポートの待ち受けを始め、「不許可」で止める。
//
// ■ 待ち受け
//   com.haglib.net_duplexchannel の WebSocketDuplexServer を 1 つ立てる。送る側（mocopi 受信アプリ）も
//   受ける側（画面）も同じポートへつなぐ。lan=false は同じ PC の中だけ（127.0.0.1 と ::1）、
//   lan=true はすべてのアドレス（LAN 上の別の PC からも接続できる）。
//   文字列は Duplex 封筒（Text フレーム {type,id,items:[{type:3,data:"…"}]}）に包まれて届く。
//
// ■ スレッド
//   受信は部品の接続ごとのスレッドから OnReceived で届く。手元には最新の 1 フレームだけを残し（古いものは
//   上書き）、メインスレッドへの適用依頼は SynchronizationContext.Post で出す。前の依頼を処理し終えるまで
//   次の依頼は出さない（溜めない・毎フレームの見回りをしない）。
//   RemoteServerCore と同じく、許可したとき（メインスレッド）の SynchronizationContext を控える。
//
// ■ 転送
//   受け取った JSON をそのまま（読めたものだけ）送ってきた接続以外へ送る。部品は 1 接続の送信を 1 件ずつ
//   行う（WebSocketDuplexChannel の _sendLock）ので、前の送信が終わっていない接続にはそのフレームを送らずに
//   捨てる（送信待ちを溜めない）。接続ごとに判定するので、遅い画面が速い画面を止めることもない。
//
// ■ 当てるもの
//   マッスルだけ。JSON に無いマッスルはトラック無しと同じ扱い（その骨は当てない）。
//   RootT / RootQ は受け取るが当てない（MotionClipApplier の再生経路も当てていない）。
//   可動域は UnityClipApplier の既定値（T ポーズ基準の Unity 定義値）。
//   当てるのは再生用の姿勢（保存しない表示状態）で、Undo は持たない（motionClip と同じ）。
//   不許可にしても姿勢は戻さない（戻すのは resetPose）。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Player
{
    [PLTool("motionLive", Description = "ライブ受信（WebSocket で届くマッスルを現在のモデルへ当て、他の接続へ転送する）")]
    public sealed class MotionLiveHandler
    {
        public Func<ModelContext> GetModel;
        /// <summary>フレームを当てた後に呼ぶ（表示の更新）。</summary>
        public Action             OnFrameApplied;

        public const int DefaultPort = 12361;

        private readonly UnityClipApplier _applier = new UnityClipApplier();
        private readonly object _lock = new object();

        private WebSocketDuplexServer   _ws;
        private SynchronizationContext  _syncCtx;
        private int                     _muscleCount;
        private Dictionary<string, int> _muscleIndex;

        // 受信スレッドが書き、メインスレッドが読む最新フレーム（_lock で守る）
        private MotionLiveFrame _latest;
        private bool            _applyPending;

        // 送信が終わっていない接続（_sendingLock で守る）
        private readonly HashSet<string> _sending = new HashSet<string>();
        private readonly object          _sendingLock = new object();

        private int             _received;
        private int             _rejected;
        private int             _applied;
        private int             _skippedSends;
        private volatile string _lastSender = "";
        private volatile string _lastReject = "";

        [PLToolState(Description = "受け入れ許可中（待ち受け中）か")]
        public bool   Listening => _ws != null;
        [PLToolState(Description = "待ち受けポート（停止中は直近の値）")]
        public int    Port { get; private set; } = DefaultPort;
        [PLToolState(Description = "LAN 上の別の PC からも受けるか（false は同じ PC の中だけ）")]
        public bool   Lan { get; private set; }
        [PLToolState(Description = "つながっている接続の数（送る側・画面の両方）")]
        public int    ClientCount => _ws?.Clients.Length ?? 0;
        [PLToolState(Description = "受け取ったフレーム数（形式が正しいもの）")]
        public int    ReceivedCount => Volatile.Read(ref _received);
        [PLToolState(Description = "捨てたメッセージ数（形式違い）")]
        public int    RejectedCount => Volatile.Read(ref _rejected);
        [PLToolState(Description = "モデルへ当てたフレーム数")]
        public int    AppliedCount => Volatile.Read(ref _applied);
        [PLToolState(Description = "前の送信が終わっていなかったため転送せずに捨てた数（接続ごとに数える）")]
        public int    SkippedSendCount => Volatile.Read(ref _skippedSends);
        [PLToolState(Description = "最後にフレームを送ってきた接続の ID")]
        public string LastSender => _lastSender;
        [PLToolState(Description = "最後に捨てたメッセージの理由")]
        public string LastReject => _lastReject;
        [PLToolState(Description = "直近の accept の失敗理由（成功なら空）")]
        public string Error { get; private set; } = "";

        /// <summary>受け入れ許可：port で WebSocket の待ち受けを始める。lan=true なら LAN からも受ける。待ち受け中なら開き直す。</summary>
        [PLToolAction(Description = "受け入れ許可（port で WebSocket の待ち受けを始める。lan=true なら LAN 上の別の PC からも受ける）")]
        public void Accept(int port, bool lan)
        {
            Reject();
            Error = "";
            if (port < 1 || port > 65535) { Error = $"ポート番号が正しくありません（{port}）"; return; }
            Port = port;
            Lan  = lan;

            _syncCtx     = SynchronizationContext.Current;
            _muscleCount = HumanTrait.MuscleCount;
            _muscleIndex = MotionLiveJson.BuildIndex(HumanTrait.MuscleName);
            if (_syncCtx == null) { Error = "メインスレッドの SynchronizationContext がありません"; return; }

            Interlocked.Exchange(ref _received, 0);
            Interlocked.Exchange(ref _rejected, 0);
            Interlocked.Exchange(ref _applied, 0);
            Interlocked.Exchange(ref _skippedSends, 0);
            _lastSender = "";
            _lastReject = "";
            lock (_lock) { _latest = null; _applyPending = false; }
            lock (_sendingLock) _sending.Clear();

            var ws = new WebSocketDuplexServer
            {
                DefaultFrame    = WebSocketFrameKind.Text,
                ListenAddresses = lan ? WebSocketDuplexServer.AnyAddresses : WebSocketDuplexServer.LoopbackAddresses,
            };
            ws.OnReceived += OnReceived;
            try
            {
                _ = ws.StartAsync(port);        // 待ち受けは同期で開始し、失敗は例外で返る
            }
            catch (Exception e)
            {
                try { _ = ws.StopAsync(); } catch { }
                Error = $"ポート {port} を開けません: {e.Message}";
                return;
            }
            _ws = ws;
        }

        /// <summary>不許可：待ち受けを止め、全接続を閉じる。姿勢はそのまま。</summary>
        [PLToolAction(Description = "不許可（待ち受けを止め、全接続を閉じる。姿勢はそのまま）")]
        public void Reject()
        {
            var ws = _ws;
            _ws = null;
            if (ws != null)
            {
                ws.OnReceived -= OnReceived;
                try { _ = ws.StopAsync(); } catch { }
            }
            lock (_lock) { _latest = null; }
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

        // ---------------- 受信スレッド（部品の接続ごと） ----------------

        private void OnReceived(IDuplexChannel from, DuplexMessage msg)
        {
            var ws = _ws;
            if (ws == null) return;

            string json;
            try { json = TypedPayload.FromMessage(msg).GetJson(); }
            catch (Exception e) { RejectMessage(from, "封筒を開けない: " + e.Message); return; }
            if (string.IsNullOrEmpty(json)) { RejectMessage(from, "JSON が無い"); return; }

            var frame = new MotionLiveFrame();
            if (!MotionLiveJson.TryParse(json, _muscleIndex, _muscleCount, frame, out string why))
            {
                RejectMessage(from, why);
                return;
            }
            Interlocked.Increment(ref _received);
            _lastSender = from?.Id ?? "";

            Relay(ws, from, json);

            bool post;
            lock (_lock)
            {
                _latest = frame;                 // 最新だけ残す
                post = !_applyPending;
                _applyPending = true;
            }
            if (post) _syncCtx.Post(_ => ApplyLatest(), null);
        }

        private void RejectMessage(IDuplexChannel from, string why)
        {
            Interlocked.Increment(ref _rejected);
            _lastReject = $"{from?.Id}: {why}";
        }

        // 送ってきた接続以外へ送る。前の送信が終わっていない接続には送らない。
        private void Relay(WebSocketDuplexServer ws, IDuplexChannel from, string json)
        {
            foreach (var ch in ws.Clients)
            {
                if (ch == null || ch == from || (from != null && ch.Id == from.Id)) continue;
                string id = ch.Id;
                lock (_sendingLock)
                {
                    if (!_sending.Add(id))
                    {
                        Interlocked.Increment(ref _skippedSends);   // 前の送信中：このフレームは捨てる
                        continue;
                    }
                }
                try
                {
                    // 送信で封筒の Id・種別が書き換わるので、接続ごとに作る
                    var task = ch.SendAsync(TypedPayload.FromJson(json).ToMessage());
                    task.ContinueWith(_ => { lock (_sendingLock) _sending.Remove(id); },
                                      TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception)
                {
                    lock (_sendingLock) _sending.Remove(id);        // 切断などは無視（次のフレームで送り直す）
                }
            }
        }

        // ---------------- メインスレッド ----------------

        private void ApplyLatest()
        {
            MotionLiveFrame f;
            lock (_lock)
            {
                _applyPending = false;
                f = _latest;
                _latest = null;
            }
            if (f == null || _ws == null) return;    // 不許可にした後に届いた依頼

            var model = GetModel?.Invoke();
            if (model == null) return;
            _applier.ApplyMuscleFrame(model, f.Muscles, f.Valid, (float)f.Time);
            Interlocked.Increment(ref _applied);
            OnFrameApplied?.Invoke();
        }
    }
}
