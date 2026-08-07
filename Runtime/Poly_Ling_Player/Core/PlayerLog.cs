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

        private static readonly object                 _lock    = new object();
        private static readonly List<PlayerLogEntry>   _entries = new List<PlayerLogEntry>();

        private static int  _maxLines = DefaultMaxLines;
        private static bool _installed;
        private static bool _unityHooked;

        private static SynchronizationContext _syncCtx;
        private static int                    _mainThreadId = -1;

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

        /// <summary>現在の行数。</summary>
        public static int Count { get { lock (_lock) { return _entries.Count; } } }

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
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

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
            _installed    = false;
            _syncCtx      = null;
            _mainThreadId = -1;
        }

        // ================================================================
        // 投入
        // ================================================================

        /// <summary>ログを 1 行追加する。任意のスレッドから呼べる。</summary>
        public static void Add(string category, string message, PlayerLogLevel level = PlayerLogLevel.Info)
        {
            if (string.IsNullOrEmpty(message)) return;

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
                Trim();
            }
            RaiseChanged();
        }

        /// <summary>全消去。</summary>
        public static void Clear()
        {
            lock (_lock) { _entries.Clear(); }
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

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>上限超過分を破棄する。呼び出し側で lock 済みであること。</summary>
        private static bool Trim()
        {
            int over = _entries.Count - _maxLines;
            if (over <= 0) return false;
            _entries.RemoveRange(0, over);
            return true;
        }

        /// <summary>OnChanged をメインスレッドで発火する。</summary>
        private static void RaiseChanged()
        {
            var handler = OnChanged;
            if (handler == null) return;

            if (_syncCtx == null || Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                SafeInvoke(handler);
                return;
            }
            _syncCtx.Post(_ => SafeInvoke(handler), null);
        }

        private static void SafeInvoke(Action handler)
        {
            // ここで例外を握り潰さないと、ログ投入元（サーバ処理等）へ伝播する。
            // Debug.Log 系は使わない（Unity ログ取り込みと再帰するため）。
            try { handler(); }
            catch { /* UI 更新失敗はログ投入を妨げない */ }
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
