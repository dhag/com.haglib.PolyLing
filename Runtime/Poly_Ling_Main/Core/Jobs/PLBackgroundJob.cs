// Runtime/Poly_Ling_Main/Core/Jobs/PLBackgroundJob.cs
// 中止できるバックグラウンドジョブの起動口。
//
// 【スレッドを直接使う理由】
// ThreadPool / Task は短い作業を回すための共有プールであり、数十秒かかる
// 計算で占有するとプール全体を細らせる。ここで扱うのは長時間の数値計算
// なので、専用スレッドを 1 本立てて使い捨てる。
//
// 【IsBackground = true にする理由】
// フォアグラウンドスレッドが 1 本でも残っているとプロセスが終了しない。
// Unity エディタの再生停止・ドメインリロード・プレイヤーの終了で
// 計算が終わっていなくても道連れにできるよう、必ず背景スレッドにする。
// ただし道連れは最後の砦であり、通常は Cancel() による協調的な停止を使う。
//
// 【優先度を下げる理由】
// メインスレッド（描画・入力）を計算スレッドが押しのけると、
// 中止ボタンを押すことすらできなくなる。BelowNormal にして譲る。
//
// 【後始末】
// 実行中のハンドルは静的なレジストリに登録され、終端に達すると外れる。
// ドメインリロードやビューア破棄のタイミングで CancelAll() を呼べば、
// 取り残されたジョブへ一括で中止要求を出せる。

using System;
using System.Collections.Generic;
using System.Threading;

namespace Poly_Ling.Jobs
{
    /// <summary>中止できるバックグラウンドジョブを起動する。</summary>
    public static class PLBackgroundJob
    {
        // ================================================================
        // 実行中ジョブのレジストリ
        // ================================================================

        private static readonly object          _registryLock = new object();
        private static readonly List<PLJobHandle> _live        = new List<PLJobHandle>();

        /// <summary>実行中のジョブ数。</summary>
        public static int LiveCount
        {
            get { lock (_registryLock) { return _live.Count; } }
        }

        /// <summary>実行中のジョブすべてに中止を要求する。応答は待たない。</summary>
        public static void CancelAll()
        {
            PLJobHandle[] snapshot;
            lock (_registryLock)
            {
                if (_live.Count == 0) return;
                snapshot = _live.ToArray();
            }
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Cancel();
        }

        /// <summary>
        /// 実行中のジョブすべてに中止を要求し、終了を待つ。
        /// メインスレッドを止めるため、破棄処理でのみ使う。
        /// </summary>
        /// <param name="millisecondsTimeout">1 本あたりの待ち時間の上限。</param>
        /// <returns>すべて終了していれば true。</returns>
        public static bool CancelAllAndJoin(int millisecondsTimeout = 2000)
        {
            PLJobHandle[] snapshot;
            lock (_registryLock)
            {
                if (_live.Count == 0) return true;
                snapshot = _live.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Cancel();

            bool all = true;
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (!snapshot[i].Join(millisecondsTimeout)) all = false;
            }
            return all;
        }

        private static void Register(PLJobHandle handle)
        {
            lock (_registryLock) { _live.Add(handle); }
        }

        private static void Unregister(PLJobHandle handle)
        {
            lock (_registryLock) { _live.Remove(handle); }
        }

        // ================================================================
        // 起動
        // ================================================================

        /// <summary>
        /// 結果を返す作業をバックグラウンドスレッドで実行する。
        ///
        /// work の中では UnityEngine の API を呼んではならない
        /// （PLJobHandle.cs 冒頭の「ワーカーの制約」を参照）。
        /// 必要なデータは呼び出し前にメインスレッドで配列へ写しておくこと。
        ///
        /// work は定期的に ctx.ThrowIfCanceled() を呼ぶこと。呼ばないジョブは
        /// 中止ボタンに反応しない。
        /// </summary>
        /// <param name="name">ジョブ名。スレッド名になる。</param>
        /// <param name="work">実行する作業。</param>
        /// <returns>状態と進捗を読み、中止を要求できるハンドル。</returns>
        public static PLJobHandle<TResult> Run<TResult>(string name, Func<PLJobContext, TResult> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var handle = new PLJobHandle<TResult>(name);
            var ctx    = new PLJobContext(handle);

            Register(handle);

            var thread = new Thread(() =>
            {
                try
                {
                    TResult result = work(ctx);

                    // work が例外を投げずに戻ったが、中止要求済みの場合がある
                    // （ThrowIfCanceled ではなく IsCancellationRequested を見て
                    //   途中で return する書き方）。結果は捨てて Canceled にする。
                    if (handle.IsCancellationRequested)
                        handle.TrySetFinished(PLJobStatus.Canceled, null);
                    else
                        handle.TrySetResult(result);
                }
                catch (PLJobCanceledException)
                {
                    handle.TrySetFinished(PLJobStatus.Canceled, null);
                }
                catch (Exception e)
                {
                    handle.TrySetFinished(PLJobStatus.Faulted, e);
                }
                finally
                {
                    // 例外経路も含めて必ず終端にする。上のどの分岐も通らずに
                    // ここへ来ることは無いはずだが、状態が Running のまま
                    // 取り残されると監視側が永久に待つため保険をかける。
                    if (handle.IsRunning)
                    {
                        handle.TrySetFinished(PLJobStatus.Faulted,
                            new InvalidOperationException("ジョブが状態を設定せずに終了しました"));
                    }
                    Unregister(handle);
                }
            });

            thread.IsBackground = true;
            thread.Name         = "PLJob:" + handle.Name;
            thread.Priority     = System.Threading.ThreadPriority.BelowNormal;

            handle.AttachThread(thread);
            thread.Start();

            return handle;
        }

        /// <summary>結果を返さない作業をバックグラウンドスレッドで実行する。</summary>
        public static PLJobHandle<bool> Run(string name, Action<PLJobContext> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Run<bool>(name, ctx => { work(ctx); return true; });
        }
    }
}
