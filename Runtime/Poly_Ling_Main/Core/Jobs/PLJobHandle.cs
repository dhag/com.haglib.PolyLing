// Runtime/Poly_Ling_Main/Core/Jobs/PLJobHandle.cs
// バックグラウンドジョブの状態・進捗・中止要求を保持するハンドルと、
// ワーカー側から使うコンテキスト。
//
// 【役割分担】
//   PLJobHandle   … 呼び出し側（UI スレッド）が持つ。状態を読み、Cancel() を投げる。
//   PLJobContext  … ワーカースレッド側が持つ。中止要求を読み、進捗を書く。
//   両者は同じハンドルの別ファサードであり、状態は 1 か所にしか無い。
//
// 【スレッド安全性】
//   ・状態遷移は Interlocked.CompareExchange で 1 度だけ成立する。
//     結果・例外は状態を書く前に書くため、状態が終端になったのを観測した
//     スレッドからは必ず見える（CompareExchange が完全なフェンスになる）。
//   ・進捗（float / int / string）は volatile。読み手が 1 回遅れて読んでも
//     表示が 1 フレーム古くなるだけで、正しさには影響しない。
//   ・経過時間は Stopwatch のインスタンスを共有せず、開始タイムスタンプ
//     （ワーカー起動前に 1 度だけ書く long）から読み手側で算出する。
//
// 【ワーカーの制約】
//   ワーカーデリゲートの中では UnityEngine の API を呼んではならない。
//   Vector3 / Matrix4x4 / Mathf のような純粋な構造体・数値計算は安全だが、
//   Object 派生（Mesh・Material・GameObject）、Debug.Log、Time、
//   MeshContext / ModelContext への読み書きはすべて禁止。
//   必要なデータはワーカー起動前にメインスレッドで配列へ写しておくこと。

using System;
using System.Diagnostics;
using System.Threading;

namespace Poly_Ling.Jobs
{
    // ================================================================
    // 状態
    // ================================================================

    /// <summary>バックグラウンドジョブの状態。Running 以外は終端で、二度と変わらない。</summary>
    public enum PLJobStatus
    {
        /// <summary>実行中。</summary>
        Running   = 0,
        /// <summary>正常終了。</summary>
        Completed = 1,
        /// <summary>中止された。</summary>
        Canceled  = 2,
        /// <summary>例外で終了した。</summary>
        Faulted   = 3,
    }

    /// <summary>
    /// 中止要求を検出したワーカーが投げる例外。
    /// PLBackgroundJob が捕捉して状態を Canceled にするため、
    /// 呼び出し側がこの例外を見ることは無い。
    /// </summary>
    public sealed class PLJobCanceledException : Exception
    {
        public PLJobCanceledException() : base("ジョブが中止されました") { }
    }

    // ================================================================
    // ハンドル（呼び出し側）
    // ================================================================

    /// <summary>
    /// バックグラウンドジョブのハンドル。結果の型を問わない共通部分。
    /// </summary>
    public abstract class PLJobHandle
    {
        // 状態。Interlocked / Volatile 経由でのみ触る（volatile 修飾は
        // ref 渡しできなくなる CS0420 を避けるため付けない）。
        private int _status = (int)PLJobStatus.Running;

        private volatile bool      _cancelRequested;
        private volatile float     _progress;
        private volatile string    _message;
        private volatile int       _stepDone;
        private volatile int       _stepTotal;
        private volatile Exception _error;

        private readonly long _startTimestamp;
        private long          _endTimestamp;   // Volatile 経由で読み書き

        private Thread _thread;

        protected PLJobHandle(string name)
        {
            Name            = string.IsNullOrEmpty(name) ? "PLJob" : name;
            _message        = string.Empty;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>ジョブ名。ログとスレッド名に使う。</summary>
        public string Name { get; }

        /// <summary>現在の状態。</summary>
        public PLJobStatus Status => (PLJobStatus)Volatile.Read(ref _status);

        /// <summary>実行中かどうか。</summary>
        public bool IsRunning => Status == PLJobStatus.Running;

        /// <summary>終端に達したかどうか（Completed / Canceled / Faulted のいずれか）。</summary>
        public bool IsFinished => Status != PLJobStatus.Running;

        /// <summary>正常終了したかどうか。</summary>
        public bool IsCompleted => Status == PLJobStatus.Completed;

        /// <summary>中止されたかどうか。</summary>
        public bool IsCanceled => Status == PLJobStatus.Canceled;

        /// <summary>例外で終了したかどうか。</summary>
        public bool IsFaulted => Status == PLJobStatus.Faulted;

        /// <summary>
        /// 中止が要求されたかどうか。要求済みでもワーカーが応じるまで
        /// 状態は Running のままである点に注意。
        /// </summary>
        public bool IsCancellationRequested => _cancelRequested;

        /// <summary>進捗（0〜1）。ワーカーが報告しない場合は 0 のまま。</summary>
        public float Progress => _progress;

        /// <summary>進捗メッセージ。ワーカーが報告しない場合は空文字。</summary>
        public string Message => _message ?? string.Empty;

        /// <summary>処理済み件数。ReportStep を使った場合のみ意味を持つ。</summary>
        public int StepDone => _stepDone;

        /// <summary>総件数。ReportStep を使った場合のみ意味を持つ。</summary>
        public int StepTotal => _stepTotal;

        /// <summary>Faulted のときの例外。それ以外は null。</summary>
        public Exception Error => _error;

        /// <summary>
        /// 経過秒数。実行中は現在時刻まで、終端に達した後は終了時刻までを返す。
        /// </summary>
        public double ElapsedSeconds
        {
            get
            {
                long end = Volatile.Read(ref _endTimestamp);
                if (end == 0L) end = Stopwatch.GetTimestamp();
                return (end - _startTimestamp) / (double)Stopwatch.Frequency;
            }
        }

        /// <summary>
        /// 中止を要求する。何度呼んでもよい。既に終端に達していれば何も起きない。
        /// ワーカーが中止要求を確認するまで状態は Running のままなので、
        /// 完了の判定は必ず Status / IsFinished で行うこと。
        /// </summary>
        public void Cancel()
        {
            _cancelRequested = true;
        }

        // ------------------------------------------------------------
        // ワーカー側から使う（PLJobContext / PLBackgroundJob 経由）
        // ------------------------------------------------------------

        internal void SetProgress(float progress, string message)
        {
            if (progress < 0f) progress = 0f;
            else if (progress > 1f) progress = 1f;
            _progress = progress;
            if (message != null) _message = message;
        }

        internal void SetStep(int done, int total, string message)
        {
            _stepDone  = done;
            _stepTotal = total;
            _progress  = (total > 0) ? Mathf01((float)done / total) : 0f;
            if (message != null) _message = message;
        }

        private static float Mathf01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// 終端状態へ遷移させる。最初の 1 回だけ成功する。
        /// 結果と例外は呼び出し側がこのメソッドより前に書いておくこと。
        /// 既に終端に達している場合は何も書き換えない（後から呼ばれた
        /// 保険の呼び出しが、先に記録された例外を潰さないようにするため）。
        /// </summary>
        internal bool TrySetFinished(PLJobStatus status, Exception error)
        {
            if (status == PLJobStatus.Running) return false;
            if (Volatile.Read(ref _status) != (int)PLJobStatus.Running) return false;

            _error = error;

            int prev = Interlocked.CompareExchange(
                ref _status, (int)status, (int)PLJobStatus.Running);
            if (prev != (int)PLJobStatus.Running) return false;

            Volatile.Write(ref _endTimestamp, Stopwatch.GetTimestamp());
            return true;
        }

        internal void AttachThread(Thread thread)
        {
            _thread = thread;
        }

        /// <summary>
        /// ワーカースレッドの終了を待つ。中止させたいときは先に Cancel() を呼ぶこと。
        /// メインスレッドから無条件に呼ぶとフリーズするため、
        /// アプリ終了時の後始末など限られた場面でのみ使う。
        /// </summary>
        /// <param name="millisecondsTimeout">-1 で無制限。</param>
        /// <returns>スレッドが終了していれば true。</returns>
        public bool Join(int millisecondsTimeout = -1)
        {
            Thread t = _thread;
            if (t == null) return true;
            return t.Join(millisecondsTimeout);
        }
    }

    // ================================================================
    // ハンドル（結果付き）
    // ================================================================

    /// <summary>結果を返すバックグラウンドジョブのハンドル。</summary>
    public sealed class PLJobHandle<TResult> : PLJobHandle
    {
        private TResult _result;

        internal PLJobHandle(string name) : base(name) { }

        /// <summary>
        /// 結果。IsCompleted が true のときだけ意味を持つ。
        /// それ以外では default(TResult) を返す。
        /// </summary>
        public TResult Result => IsCompleted ? _result : default;

        /// <summary>結果を書いてから Completed へ遷移させる。</summary>
        internal bool TrySetResult(TResult result)
        {
            // TrySetFinished 内の Interlocked が完全なフェンスになるため、
            // ここでの書き込みは Completed を観測したスレッドから必ず見える。
            _result = result;
            return TrySetFinished(PLJobStatus.Completed, null);
        }
    }

    // ================================================================
    // コンテキスト（ワーカー側）
    // ================================================================

    /// <summary>
    /// ワーカーデリゲートに渡されるコンテキスト。
    /// 中止要求の確認と進捗報告だけを公開する。
    ///
    /// UnityEngine の API はこのコンテキストが渡された先では使えない。
    /// クラス冒頭のコメントを参照。
    /// </summary>
    public sealed class PLJobContext
    {
        private readonly PLJobHandle _handle;

        internal PLJobContext(PLJobHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        /// <summary>ジョブ名。</summary>
        public string Name => _handle.Name;

        /// <summary>中止が要求されているかどうか。</summary>
        public bool IsCancellationRequested => _handle.IsCancellationRequested;

        /// <summary>
        /// 中止が要求されていれば PLJobCanceledException を投げる。
        /// ループの内側から定期的に呼ぶ。
        /// </summary>
        public void ThrowIfCanceled()
        {
            if (_handle.IsCancellationRequested) throw new PLJobCanceledException();
        }

        /// <summary>進捗（0〜1）とメッセージを報告する。message が null ならメッセージは据え置く。</summary>
        public void Report(float progress, string message = null)
        {
            _handle.SetProgress(progress, message);
        }

        /// <summary>件数で進捗を報告する。message が null ならメッセージは据え置く。</summary>
        public void ReportStep(int done, int total, string message = null)
        {
            _handle.SetStep(done, total, message);
        }
    }
}
