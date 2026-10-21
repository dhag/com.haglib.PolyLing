// ExecutionLog.cs
// 実行記録。一番外側で実行したコマンドを、学習用の材料として JSONL に残す。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【棚とは別】（PolyLing_シナリオ引数と実行記録計画.md の段 4）
//   シナリオの置き場（ScenarioLibrary）は流す手本で、検索の対象。こちらは学習用の記録で、
//   検索・describe・棚からは読まない。値だけ違う流し方はここにだけ残る。
//
// 【既定は入】（2026-10-21 決定。メインスレッドの負荷は 1 件あたり約 0.2 ms）
//   起動時に PlayerCommandDispatcher.ApplyExecutionLogPreference が覚えている入切（無ければ入）で始める。
//   setExecutionLog で変えると覚える。切の間、Dispatch に増える処理は Enabled の 1 回の判定だけ。
//
// 【1 件の中身】
//   コマンド：時刻・タスク ID・連番・操作者・コマンド名・引数・書き込み範囲・成否・理由・所要時間。
//   runScenario / continueScenario には、その呼び出しで流れた項目の記録（流れの記録の行）を添える。
//   戻り値の本文は控えない（getRawData のように大きいものがあるため）。
//   タスクの区切り：beginTask（目標・条件の文）と endTask（判定と一言）を、それぞれ 1 件として書く。
//
// 【どこで何をするか】
//   引数はメインスレッドで文字列にする（RecordCommand の呼び出し側）。パネルは諸元のオブジェクトを
//   コマンドへ渡したあとも書き換えるので、後で別スレッドから読むと実行したときと違う値になる。
//   ファイルへの書き込みだけを別スレッドに任せる（メインスレッドは列に積んで戻る）。
//
// 【ファイル】
//   <persistentDataPath>/PolyLing/runlogs/<yyyyMMdd>/<タスク ID>.jsonl。タスクの外は _notask.jsonl。
//   1 本が巨大にならないよう、タスクごと・日ごとに分ける。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Poly_Ling.Remote;

namespace Poly_Ling.Data
{
    /// <summary>実行記録（学習用）。既定は切。</summary>
    public static class ExecutionLog
    {
        /// <summary>タスクの外で書く記録のファイル名（拡張子なし）。</summary>
        public const string NoTaskName = "_notask";

        private static readonly object _lock = new object();

        private static volatile bool _enabled;
        private static string _rootDir = "";
        private static string _taskId  = "";
        private static long   _seq;

        // ── 書き手（別スレッド） ──
        private static BlockingCollection<KeyValuePair<string, string>> _queue;
        private static Thread _writer;
        private static long   _pending;
        private static long   _written;
        private static string _lastError = "";

        // ── メインスレッドで使った時間（引数を文字列にする〜列に積むまで） ──
        private static long _mainTicks;
        private static long _records;

        /// <summary>入っているか。Dispatch はこれだけを見て、切なら何もしない。</summary>
        public static bool Enabled => _enabled;

        /// <summary>今のタスク ID。タスクの外なら空。</summary>
        public static string TaskId { get { lock (_lock) return _taskId; } }

        /// <summary>書き込み先のフォルダ（runlogs）。</summary>
        public static string RootDir { get { lock (_lock) return _rootDir; } }

        // ================================================================
        // 入・切
        // ================================================================

        /// <summary>
        /// 入れる・切る。rootDir は runlogs のフォルダ（メインスレッドで persistentDataPath から作って渡す）。
        /// 切っても書き手は止めない（積んだ分は書き終える）。
        /// </summary>
        public static void SetEnabled(bool on, string rootDir)
        {
            lock (_lock)
            {
                if (on)
                {
                    _rootDir = rootDir ?? "";
                    EnsureWriter();
                }
                _enabled = on;
            }
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;
            _queue  = new BlockingCollection<KeyValuePair<string, string>>();
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "PolyLing.ExecutionLog" };
            _writer.Start();
        }

        // ================================================================
        // タスクの区切り
        // ================================================================

        /// <summary>タスクを始める。taskId が空なら作る。前のタスクが閉じていなければ、閉じずに置き換える（その旨を書く）。</summary>
        public static string BeginTask(string taskId, string goal, string conditions)
        {
            string id = string.IsNullOrWhiteSpace(taskId)
                ? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)
                : Sanitize(taskId.Trim());

            string previous;
            lock (_lock) { previous = _taskId; _taskId = id; _seq = 0; }

            var jb = Begin("taskBegin", id);
            jb.KeyValue("goal", goal ?? "");
            jb.KeyValue("conditions", conditions ?? "");
            if (!string.IsNullOrEmpty(previous)) jb.KeyValue("replacedOpenTask", previous);
            Enqueue(id, jb);
            return id;
        }

        /// <summary>タスクを終える。判定は success / fixed / failed。開いていなければ false。</summary>
        public static bool EndTask(string verdict, string note, out string taskId)
        {
            lock (_lock) { taskId = _taskId; }
            if (string.IsNullOrEmpty(taskId)) return false;

            var jb = Begin("taskEnd", taskId);
            jb.KeyValue("verdict", verdict ?? "");
            jb.KeyValue("note", note ?? "");
            Enqueue(taskId, jb);

            lock (_lock) { if (_taskId == taskId) _taskId = ""; }
            return true;
        }

        // ================================================================
        // コマンド
        // ================================================================

        /// <summary>
        /// 一番外側で実行したコマンドを 1 件積む。args はメインスレッドで作ったもの（呼ぶ側で ToArgs）。
        /// runLog は runScenario / continueScenario で流れた項目の記録（無ければ null）。
        /// mainStartTicks は呼ぶ側がこの記録のために使い始めた時刻（Stopwatch.GetTimestamp）。
        /// </summary>
        public static void RecordCommand(string actor, string action, Dictionary<string, string> args, string writes,
                                         bool ok, string reason, double ms, IReadOnlyList<string> runLog,
                                         long mainStartTicks)
        {
            if (!_enabled) return;

            string task;
            lock (_lock) task = _taskId;

            var jb = Begin("command", task);
            jb.KeyValue("actor",  actor ?? "");
            jb.KeyValue("action", action ?? "");
            jb.Key("args").BeginObject();
            if (args != null)
            {
                var keys = new List<string>(args.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) jb.KeyValue(k, args[k] ?? "");
            }
            jb.EndObject();
            jb.KeyValue("writes", writes ?? "");
            jb.KeyValue("ok",     ok);
            if (!ok) jb.KeyValue("reason", reason ?? "");
            jb.KeyValue("ms", (float)ms);
            if (runLog != null && runLog.Count > 0)
            {
                jb.Key("runLog").BeginArray();
                foreach (var line in runLog) jb.Value(line ?? "");
                jb.EndArray();
            }
            Enqueue(task, jb);

            Interlocked.Add(ref _mainTicks, Stopwatch.GetTimestamp() - mainStartTicks);
            Interlocked.Increment(ref _records);
        }

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>状態の写し（queryExecutionLog 用）。</summary>
        public static (bool Enabled, string TaskId, string RootDir, long Records, long Written, long Pending,
                       double MainMsTotal, string LastError) Snapshot()
        {
            lock (_lock)
            {
                long recs  = Interlocked.Read(ref _records);
                long ticks = Interlocked.Read(ref _mainTicks);
                return (_enabled, _taskId, _rootDir, recs, Interlocked.Read(ref _written),
                        Interlocked.Read(ref _pending), ticks * 1000.0 / Stopwatch.Frequency, _lastError);
            }
        }

        /// <summary>メインスレッドの時間の集計を 0 に戻す（比べる前に使う）。</summary>
        public static void ResetStats()
        {
            Interlocked.Exchange(ref _mainTicks, 0);
            Interlocked.Exchange(ref _records, 0);
        }

        // ================================================================
        // 内部
        // ================================================================

        private static JsonBuilder Begin(string kind, string task)
        {
            long seq;
            lock (_lock) seq = ++_seq;

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("t",    DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            jb.KeyValue("kind", kind);
            jb.KeyValue("task", task ?? "");
            jb.KeyValue("seq",  (int)seq);
            return jb;
        }

        private static void Enqueue(string task, JsonBuilder jb)
        {
            jb.EndObject();
            string root;
            lock (_lock) root = _rootDir;
            if (string.IsNullOrEmpty(root) || _queue == null) return;

            string day  = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string file = Path.Combine(root, day, (string.IsNullOrEmpty(task) ? NoTaskName : task) + ".jsonl");

            Interlocked.Increment(ref _pending);
            _queue.Add(new KeyValuePair<string, string>(file, jb.ToString()));
        }

        /// <summary>別スレッド。1 件ずつ取り出し、続けて来た分はまとめて書く。ファイルは書くたびに閉じる。</summary>
        private static void WriterLoop()
        {
            var batch = new List<KeyValuePair<string, string>>();
            foreach (var first in _queue.GetConsumingEnumerable())
            {
                batch.Clear();
                batch.Add(first);
                while (batch.Count < 256 && _queue.TryTake(out var more)) batch.Add(more);

                // 同じファイルへの行をまとめて 1 回で書く。
                var byFile = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
                var order  = new List<string>();
                foreach (var kv in batch)
                {
                    if (!byFile.TryGetValue(kv.Key, out var sb))
                    { sb = new StringBuilder(); byFile[kv.Key] = sb; order.Add(kv.Key); }
                    sb.Append(kv.Value).Append('\n');
                }

                foreach (var path in order)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.AppendAllText(path, byFile[path].ToString(), new UTF8Encoding(false));
                    }
                    catch (Exception ex)
                    {
                        lock (_lock) _lastError = $"{path}: {ex.Message}";
                    }
                }

                Interlocked.Add(ref _written, batch.Count);
                Interlocked.Add(ref _pending, -batch.Count);
            }
        }

        /// <summary>ファイル名に使えない文字を _ にする。</summary>
        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
