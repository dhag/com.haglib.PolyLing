// PlayerLog.cs
// プレイヤー統合ログ。リモートサーバのログと Unity の Debug.Log 系を
// 単一のストアへ集約する。表示は PlayerLogSubPanel が OnChanged を購読して行う。
//
// スレッド安全性:
//   Add() はワーカースレッドから呼ばれうる（RemoteServerCore.RunOnMainThread の
//   catch 経路など）。エントリ追加は lock で保護し、OnChanged は Install() 時に
//   捕捉したメインスレッドの SynchronizationContext 経由で発火する。
//   毎フレーム処理は行わない（イベント駆動のみ）。
//
// 通知の合体:
//   OnChanged は 1 行ごとに同期発火しない。未処理の通知が 1 件でもあれば
//   それ以上は投げず、SynchronizationContext 経由で次の処理タイミングに
//   まとめて 1 回だけ発火する。1 行ごとに UI を更新すると、
//   UI 更新中に出たログで Add に再入し、更新が更新を呼ぶ。
//
// 保持量:
//   行数（MaxLines）と総文字数（MaxChars）の両方で上限を持つ。
//   行数だけだと、1 件が数十 KB になる診断ダンプが溜まったときに
//   数十 MB を保持し続ける。1 件あたりも MaxEntryChars で切り詰める。
//
// 注意: このクラス内で Debug.Log 系を呼んではならない（Unity ログ取り込みと
//       相互再帰するため）。
//
// Runtime/Poly_Ling_Player/Core/ に配置

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>統合ログの重要度。</summary>
    public enum PlayerLogLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>統合ログ 1 行分。</summary>
    public sealed class PlayerLogEntry
    {
        public DateTime       Time;
        public PlayerLogLevel Level;
        public string         Category;
        public string         Message;

        /// <summary>表示・保存用の 1 行文字列を組み立てる。</summary>
        public string ToLine()
        {
            string lv = Level == PlayerLogLevel.Warning ? " [WARN]"
                      : Level == PlayerLogLevel.Error   ? " [ERROR]"
                      : string.Empty;
            return $"[{Time:HH:mm:ss}] [{Category}]{lv} {Message}";
        }
    }

    /// <summary>
    /// 統合ログのストア（静的）。
    /// 投入元: PolyLingPlayerServer（サーバログ）／Application.logMessageReceived（Unity ログ）／
    ///         任意の Add() 呼び出し。
    /// </summary>
    public static class PlayerLog
    {
        // ================================================================
        // 状態
        // ================================================================

        private const int DefaultMaxLines = 2000;

        /// <summary>保持する総文字数の既定上限。</summary>
        private const int DefaultMaxChars = 4 * 1024 * 1024;

        /// <summary>1 件あたりの本文上限。超過分は切り詰める。</summary>
        public const int MaxEntryChars = 8 * 1024;

        /// <summary>ToLine() が本文以外に足すぶんの概算（時刻・カテゴリ区切り等）。</summary>
        private const int LineOverheadChars = 24;

        private static readonly object                 _lock    = new object();
        private static readonly List<PlayerLogEntry>   _entries = new List<PlayerLogEntry>();

        private static int  _maxLines = DefaultMaxLines;
        private static int  _maxChars = DefaultMaxChars;
        private static int  _totalChars;
        private static long _totalAdded;
        private static bool _installed;
        private static bool _unityHooked;

        /// <summary>未処理の OnChanged 通知があるか。</summary>
        private static bool _changePending;

        /// <summary>OnChanged ハンドラ実行中か。再入検出用。</summary>
        private static bool _inRaise;

        private static SynchronizationContext _syncCtx;

        /// <summary>
        /// 捕捉したメインスレッドの ID。診断用に保持する。
        /// 発火先の判定には使わない（RaiseChanged は常に _syncCtx 経由で回す）。
        /// </summary>
        public static int MainThreadId { get; private set; } = -1;

        /// <summary>ログ内容が変化したときに発火する（メインスレッド）。</summary>
        public static event Action OnChanged;

        /// <summary>保持する最大行数。超過分は古い順に破棄する。</summary>
        public static int MaxLines
        {
            get { lock (_lock) { return _maxLines; } }
            set
            {
                bool trimmed;
                lock (_lock)
                {
                    _maxLines = Mathf.Max(1, value);
                    trimmed   = Trim();
                }
                if (trimmed) RaiseChanged();
            }
        }

        /// <summary>保持する総文字数の上限。超過分は古い順に破棄する。</summary>
        public static int MaxChars
        {
            get { lock (_lock) { return _maxChars; } }
            set
            {
                bool trimmed;
                lock (_lock)
                {
                    _maxChars = Mathf.Max(1024, value);
                    trimmed   = Trim();
                }
                if (trimmed) RaiseChanged();
            }
        }

        /// <summary>現在の行数。</summary>
        public static int Count { get { lock (_lock) { return _entries.Count; } } }

        /// <summary>現在保持している総文字数（概算）。</summary>
        public static int TotalChars { get { lock (_lock) { return _totalChars; } } }

        /// <summary>これまでに投入した総行数（破棄したぶんも含む）。Clear で 0 に戻る。</summary>
        public static long TotalAdded { get { lock (_lock) { return _totalAdded; } } }

        // ================================================================
        // 設置 / 撤去
        // ================================================================

        /// <summary>
        /// メインスレッドの SynchronizationContext を捕捉し、Unity ログの取り込みを開始する。
        /// PolyLingPlayerViewerCore.Initialize から呼ぶ。
        /// </summary>
        /// <param name="hookUnityLog">true で Debug.Log/LogWarning/LogError を取り込む</param>
        public static void Install(bool hookUnityLog = true)
        {
            if (_installed) return;
            _installed    = true;
            _syncCtx      = SynchronizationContext.Current;
            MainThreadId  = Thread.CurrentThread.ManagedThreadId;

            if (hookUnityLog && !_unityHooked)
            {
                Application.logMessageReceived += OnUnityLogMessage;
                _unityHooked = true;
            }
        }

        /// <summary>Unity ログの取り込みを停止する。PolyLingPlayerViewerCore.Dispose から呼ぶ。</summary>
        public static void Uninstall()
        {
            if (_unityHooked)
            {
                Application.logMessageReceived -= OnUnityLogMessage;
                _unityHooked = false;
            }
            _installed     = false;
            _syncCtx       = null;
            MainThreadId   = -1;
            _changePending = false;
        }

        // ================================================================
        // 投入
        // ================================================================

        /// <summary>ログを 1 行追加する。任意のスレッドから呼べる。</summary>
        public static void Add(string category, string message, PlayerLogLevel level = PlayerLogLevel.Info)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 1 件が大きすぎると、行数上限だけでは保持量を抑えられない。
            // 切り詰めた事実は本文に残す。
            if (message.Length > MaxEntryChars)
            {
                int cut = message.Length - MaxEntryChars;
                message = message.Substring(0, MaxEntryChars) +
                          "\n…(以下 " + cut.ToString() + " 文字を切り詰め)";
            }

            var entry = new PlayerLogEntry
            {
                Time     = DateTime.Now,
                Level    = level,
                Category = string.IsNullOrEmpty(category) ? "-" : category,
                Message  = message,
            };

            lock (_lock)
            {
                _entries.Add(entry);
                _totalChars += EntryChars(entry);
                _totalAdded++;
                Trim();
            }
            RaiseChanged();
        }

        /// <summary>全消去。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _totalChars = 0;
                _totalAdded = 0;
            }
            RaiseChanged();
        }

        // ================================================================
        // 取得
        // ================================================================

        /// <summary>現在のエントリのスナップショットを返す。</summary>
        public static PlayerLogEntry[] Snapshot()
        {
            lock (_lock) { return _entries.ToArray(); }
        }

        /// <summary>表示・保存用のテキスト全文を組み立てる。</summary>
        public static string BuildText()
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(_entries[i].ToLine());
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// cursor（呼出し側が既に表示済みの総投入行数）より後の行だけを組み立てる。
        ///
        /// 表示のたびに全文を作り直すと、行数 N に対して 1 行あたり O(N)、
        /// N 行投入で O(N^2) になる。追記表示のためにこれを使うこと。
        ///
        /// cursor 以前の行が Trim / Clear で失われていた場合は fullRebuild=true を
        /// 返し、戻り値は保持中の全文になる。呼出し側は cursor を newCursor で
        /// 更新すること。
        /// </summary>
        public static string BuildTextSince(long cursor, out long newCursor, out bool fullRebuild)
        {
            var sb = new StringBuilder();
            lock (_lock)
            {
                // 保持している先頭行の通し番号。
                long firstKept = _totalAdded - _entries.Count;

                fullRebuild = cursor < firstKept || cursor > _totalAdded;
                int start   = fullRebuild ? 0 : (int)(cursor - firstKept);

                for (int i = start; i < _entries.Count; i++)
                {
                    if (i > start) sb.Append('\n');
                    sb.Append(_entries[i].ToLine());
                }
                newCursor = _totalAdded;
            }
            return sb.ToString();
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>1 件が占める文字数の概算。総文字数の加減算に使う。</summary>
        private static int EntryChars(PlayerLogEntry e)
        {
            if (e == null) return 0;
            return (e.Message?.Length ?? 0) + (e.Category?.Length ?? 0) + LineOverheadChars;
        }

        /// <summary>
        /// 上限超過分を破棄する。呼び出し側で lock 済みであること。
        /// 行数と総文字数の両方を見る。
        /// </summary>
        private static bool Trim()
        {
            int drop = 0;

            int over = _entries.Count - _maxLines;
            if (over > 0) drop = over;

            // 総文字数の超過分を古い順に落とす。最後の 1 件は必ず残す。
            int chars = _totalChars;
            for (int i = 0; i < drop; i++) chars -= EntryChars(_entries[i]);
            while (chars > _maxChars && drop < _entries.Count - 1)
            {
                chars -= EntryChars(_entries[drop]);
                drop++;
            }

            if (drop <= 0) return false;

            _entries.RemoveRange(0, drop);
            _totalChars = chars;
            if (_entries.Count == 0) _totalChars = 0;
            return true;
        }

        /// <summary>
        /// OnChanged の発火を予約する。未処理の予約があれば何もしない。
        ///
        /// メインスレッドから呼ばれた場合も同期発火せず、必ず
        /// SynchronizationContext 経由で次の処理タイミングへ回す。
        /// 同期発火すると 1 行ごとに UI 更新が走り、
        /// UI 更新中に出たログで Add に再入して更新が更新を呼ぶ。
        /// </summary>
        private static void RaiseChanged()
        {
            if (OnChanged == null) return;

            lock (_lock)
            {
                if (_changePending) return;
                _changePending = true;
            }

            var ctx = _syncCtx;
            if (ctx == null)
            {
                // Install 前 / Uninstall 後。合体先が無いのでその場で処理する。
                FlushChanged();
                return;
            }
            ctx.Post(_ => FlushChanged(), null);
        }

        /// <summary>予約された OnChanged を 1 回だけ発火する（メインスレッド）。</summary>
        private static void FlushChanged()
        {
            lock (_lock) { _changePending = false; }

            // 再入（ハンドラ内でログが出た等）。増えたぶんは次の Add が改めて予約する。
            if (_inRaise) return;

            var handler = OnChanged;
            if (handler == null) return;

            _inRaise = true;
            // ここで例外を握り潰さないと、ログ投入元（サーバ処理等）へ伝播する。
            // Debug.Log 系は使わない（Unity ログ取り込みと再帰するため）。
            try { handler(); }
            catch { /* UI 更新失敗はログ投入を妨げない */ }
            finally { _inRaise = false; }
        }

        /// <summary>Application.logMessageReceived ハンドラ（メインスレッド）。</summary>
        private static void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            PlayerLogLevel level;
            switch (type)
            {
                case LogType.Warning:   level = PlayerLogLevel.Warning; break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:    level = PlayerLogLevel.Error;   break;
                default:                level = PlayerLogLevel.Info;    break;
            }

            string msg = condition;
            // エラー系のみスタックトレースを併記する（Log/Warning は本文のみ）。
            if (level == PlayerLogLevel.Error && !string.IsNullOrEmpty(stackTrace))
                msg = condition + "\n" + stackTrace.TrimEnd();

            Add("Unity", msg, level);
        }
    }
}
