// Runtime/Poly_Ling_Main/Core/Jobs/PLJobMonitor.cs
// バックグラウンドジョブの進捗をメインスレッドへ運び、終端に達したら
// 完了コールバックを呼ぶ監視役。
//
// 【なぜ必要か】
// ワーカースレッドからは UnityEngine の API を触れないため、進捗表示も
// 結果の書き戻しもメインスレッドでやり直す必要がある。ワーカーから
// メインスレッドへ制御を渡す手段が Unity には無いので、メインスレッド側から
// 状態を読みに行く形になる。
//
// 【常駐しないこと】
// schedule.Execute(...).StartingIn(ms) の一発予約を、ジョブが実行中の間だけ
// 自分で張り直す。終端に達した時点、あるいはホスト要素がパネルから外れた
// 時点で張り直しをやめるため、監視が残り続けることはない。
// PlayerSpringBoneTestSubPanel.cs の段階実行と同じ形。Update は使わない。
//
// 【ホスト要素が消えた場合】
// パネルを閉じる・モデルを差し替えるなどでホスト要素が外れたら、
// 既定では中止を要求したうえで監視を止める。ワーカーだけが取り残されて
// CPU を食い続けるのを防ぐ。結果を捨てたくない場合は CancelOnDetach を false にする。

using System;
using UnityEngine.UIElements;

namespace Poly_Ling.Jobs
{
    /// <summary>バックグラウンドジョブをメインスレッドから監視する。</summary>
    public sealed class PLJobMonitor
    {
        /// <summary>既定の監視間隔（ミリ秒）。</summary>
        public const long DefaultIntervalMs = 100;

        private readonly VisualElement       _host;
        private readonly PLJobHandle         _handle;
        private readonly Action<PLJobHandle> _onProgress;
        private readonly Action<PLJobHandle> _onFinished;
        private readonly long                _intervalMs;

        private bool _stopped;
        private bool _finishedDelivered;
        private EventCallback<DetachFromPanelEvent> _detachCallback;

        /// <summary>ホスト要素がパネルから外れたときに中止を要求するか。既定 true。</summary>
        public bool CancelOnDetach { get; set; } = true;

        /// <summary>監視対象。</summary>
        public PLJobHandle Handle => _handle;

        /// <summary>監視が続いているか。</summary>
        public bool IsMonitoring => !_stopped;

        private PLJobMonitor(
            VisualElement host, PLJobHandle handle,
            Action<PLJobHandle> onProgress, Action<PLJobHandle> onFinished,
            long intervalMs)
        {
            _host       = host;
            _handle     = handle;
            _onProgress = onProgress;
            _onFinished = onFinished;
            _intervalMs = intervalMs > 0 ? intervalMs : DefaultIntervalMs;
        }

        /// <summary>
        /// ジョブの監視を始める。コールバックはすべてメインスレッドで呼ばれる。
        ///
        /// ジョブが既に終端に達している場合も、完了コールバックは即時ではなく
        /// 次の予約実行で呼ばれる。呼び出し側のコードが最後まで走ってから
        /// 完了処理が動くことを保証するため。
        /// </summary>
        /// <param name="host">予約実行の足場になる要素。パネルに載っていること。</param>
        /// <param name="handle">監視するジョブ。</param>
        /// <param name="onProgress">進捗更新。終端に達した回にも 1 度呼ばれる。null 可。</param>
        /// <param name="onFinished">終端に達したときに 1 度だけ呼ばれる。null 可。</param>
        /// <param name="intervalMs">監視間隔。既定 100ms。</param>
        public static PLJobMonitor Attach(
            VisualElement host,
            PLJobHandle handle,
            Action<PLJobHandle> onProgress = null,
            Action<PLJobHandle> onFinished = null,
            long intervalMs = DefaultIntervalMs)
        {
            if (host   == null) throw new ArgumentNullException(nameof(host));
            if (handle == null) throw new ArgumentNullException(nameof(handle));

            var monitor = new PLJobMonitor(host, handle, onProgress, onFinished, intervalMs);
            monitor.Begin();
            return monitor;
        }

        private void Begin()
        {
            _detachCallback = OnHostDetached;
            _host.RegisterCallback(_detachCallback);
            Arm();
        }

        /// <summary>次回の監視を予約する。</summary>
        private void Arm()
        {
            if (_stopped) return;
            _host.schedule.Execute(Tick).StartingIn(_intervalMs);
        }

        private void Tick()
        {
            if (_stopped) return;

            bool finished = _handle.IsFinished;

            // 終端に達した回も進捗を 1 度流す。最終状態が表示に残るようにするため。
            _onProgress?.Invoke(_handle);

            if (!finished)
            {
                Arm();
                return;
            }

            Stop();
            DeliverFinished();
        }

        private void DeliverFinished()
        {
            if (_finishedDelivered) return;
            _finishedDelivered = true;
            _onFinished?.Invoke(_handle);
        }

        private void OnHostDetached(DetachFromPanelEvent evt)
        {
            if (CancelOnDetach) _handle.Cancel();
            Stop();
        }

        /// <summary>
        /// 監視をやめる。ジョブ自体は止まらない。止めたい場合は Handle.Cancel() を併用する。
        /// 何度呼んでもよい。
        /// </summary>
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;

            if (_detachCallback != null)
            {
                _host.UnregisterCallback(_detachCallback);
                _detachCallback = null;
            }
        }

        /// <summary>
        /// 中止を要求し、監視も止める。完了コールバックは呼ばれない。
        /// パネル側の「中止」ボタンで結果を一切受け取りたくない場合に使う。
        /// 中止後の状態を受け取りたい場合は Handle.Cancel() だけを呼び、
        /// 監視は続けること。
        /// </summary>
        public void CancelAndStop()
        {
            _handle.Cancel();
            Stop();
        }
    }
}
