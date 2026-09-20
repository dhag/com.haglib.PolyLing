// RemoteServerConnector.cs
// ============================================================
// クライアント側の接続先決定：マスターに一覧を問い合わせ、接続する
// ============================================================
//
// 【流れ】
//   Begin() → マスターへ list を問い合わせる
//     マスター不在        → Idle（状態文字列で通知）
//     1 台 / 前回の pid    → そのサーバへ接続
//     複数                → Choosing。OnChoicesChanged で選択肢を渡す。Choose() で接続
//   接続失敗・切断       → Idle。次の Begin() で問い合わせからやり直す
//
// 【スレッド】
//   メインスレッドから呼ぶこと。問い合わせの await は呼び出し元の
//   SynchronizationContext（Unity のメインスレッド）へ戻る。
//   PolyLingPlayerClient のコールバックもメインスレッドで届く。
//   毎フレームのポーリングは行わない（再試行の間隔は呼び出し側が持つ）。
// ============================================================

using System;
using System.Collections.Generic;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    public sealed class RemoteServerConnector
    {
        public enum State { Idle, Querying, Choosing, Connecting, Connected }

        private readonly PolyLingPlayerClient _client;
        private int _preferredPid;
        private int _querySerial;

        public State Current { get; private set; } = State.Idle;

        /// <summary>接続中・接続済みのサーバ。無ければ null。</summary>
        public RemoteServerInfo Target { get; private set; }

        /// <summary>選択待ちのときの選択肢。それ以外は null。</summary>
        public List<RemoteServerInfo> Choices { get; private set; }

        /// <summary>状態の説明文（表示用）。</summary>
        public Action<string> OnStatus;

        /// <summary>選択肢が変わったとき。null は選択肢を閉じる。</summary>
        public Action<List<RemoteServerInfo>> OnChoicesChanged;

        public RemoteServerConnector(PolyLingPlayerClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.OnConnected     += HandleConnected;
            _client.OnDisconnected  += HandleDisconnected;
            _client.OnConnectFailed += HandleConnectFailed;
        }

        /// <summary>購読を外す。クライアントを破棄する前に呼ぶ。</summary>
        public void Detach()
        {
            _querySerial++;
            _client.OnConnected     -= HandleConnected;
            _client.OnDisconnected  -= HandleDisconnected;
            _client.OnConnectFailed -= HandleConnectFailed;
            SetChoices(null);
            Current = State.Idle;
        }

        // ================================================================
        // 操作
        // ================================================================

        /// <summary>
        /// 問い合わせから始める。Idle のときだけ動く。
        /// 選択待ちのときに呼ぶと一覧を取り直す。
        /// </summary>
        public async void Begin()
        {
            if (Current == State.Choosing)
            {
                SetChoices(null);
                Current = State.Idle;
            }
            if (Current != State.Idle) return;

            Current = State.Querying;
            int serial = ++_querySerial;
            Status("サーバ一覧を問い合わせ中...");

            List<RemoteServerInfo> servers;
            try
            {
                servers = await RemoteDirectory.QueryAsync();
            }
            catch (Exception ex)
            {
                servers = null;
                Status($"サーバ一覧の問い合わせに失敗: {ex.Message}");
            }

            // 途中で Detach / Cancel された場合は結果を捨てる。
            if (serial != _querySerial || Current != State.Querying) return;

            if (servers == null)
            {
                Current = State.Idle;
                Status("サーバが見つかりません（マスター未起動）");
                return;
            }
            if (servers.Count == 0)
            {
                Current = State.Idle;
                Status("起動中のサーバがありません");
                return;
            }

            var pick = RemoteDirectory.PickAuto(servers, _preferredPid);
            if (pick != null)
            {
                ConnectTo(pick);
                return;
            }

            Current = State.Choosing;
            SetChoices(servers);
            Status($"サーバが {servers.Count} 台あります。接続先を選んでください");
        }

        /// <summary>選択肢から接続先を決める。</summary>
        public void Choose(RemoteServerInfo info)
        {
            if (Current != State.Choosing || info == null) return;
            SetChoices(null);
            ConnectTo(info);
        }

        /// <summary>問い合わせ・選択待ちを取り消して Idle に戻す（接続そのものは切らない）。</summary>
        public void Cancel()
        {
            _querySerial++;
            SetChoices(null);
            if (Current == State.Querying || Current == State.Choosing)
                Current = State.Idle;
        }

        /// <summary>接続を切って Idle に戻す。</summary>
        public void Disconnect()
        {
            Cancel();
            _client.Disconnect();
            Current = State.Idle;
            Status("未接続");
        }

        private void ConnectTo(RemoteServerInfo info)
        {
            Target        = info;
            _preferredPid = info.Pid;
            Current       = State.Connecting;
            Status($"接続中... {info.Label}");
            _client.Initialize(RemoteDirectory.Host, info.Port, autoConnect: true);
        }

        // ================================================================
        // クライアントのコールバック（メインスレッド）
        // ================================================================

        private void HandleConnected()
        {
            Current = State.Connected;
        }

        private void HandleDisconnected()
        {
            if (Current == State.Connected || Current == State.Connecting)
                Current = State.Idle;
        }

        private void HandleConnectFailed(string reason)
        {
            if (Current != State.Connecting) return;
            Current = State.Idle;
            Status($"接続できませんでした: {Target?.Label} ({reason})");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SetChoices(List<RemoteServerInfo> choices)
        {
            Choices = choices;
            OnChoicesChanged?.Invoke(choices);
        }

        private void Status(string text) => OnStatus?.Invoke(text);
    }
}
