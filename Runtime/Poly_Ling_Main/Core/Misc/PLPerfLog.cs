// PLPerfLog.cs
// 性能・リーク調査用の長期数値ログ。
//
// 【PLDiag / PlayerLog との違い】
//   ・PLDiag  ... 事象のテキストを Debug.Log へ出す。何が起きたかを追う用途。
//   ・PlayerLog ... そのテキストの表示先。メモリ上に上限行数で保持し、超過分は捨てる。
//   ・PLPerfLog（ここ）... 数値の時系列を CSV へ直接追記する。上限で捨てないため、
//     数時間の傾き（＝進行性の劣化・リーク）が残る。Debug.Log は経由しない。
//
// 【採取方式】
//   VisualElement.schedule.Execute(...).Every(ms) で一定間隔に 1 行書く。
//   毎フレーム経路（PolyLingPlayerViewer.OnBeginCameraRendering）には一切触れない。
//   取得する値は全て O(1) か O(メッシュ数) で、頂点数には依存しない。
//   全オブジェクト走査（Resources.FindObjectsOfTypeAll）や VisualElement ツリー走査は行わない。
//
// 【行の種別】
//   S ... 周期サンプル。
//   E ... ツール／サブツール／右ペインの切替時に即時 1 行。ユーザ操作の頻度でしか出ない。
//   どちらも列は同一。frames / fps / cmds / topCmd は「直前の行からの差分」であり、
//   S 行 E 行の区別なく直前行を基準にする。これで時間軸が一様になり機械処理しやすい。
//
// 【出力形式】
//   先頭に '#' で始まるスキーマ説明行、その次に列名行、以降がデータ行。
//   数値は InvariantCulture（小数点は '.'、桁区切り無し）。時刻は ISO 8601。
//   列の順序と個数は行種別によらず常に同一。
//
// Runtime/Poly_Ling_Main/Core/Misc/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Diagnostics
{
    public static class PLPerfLog
    {
        // ================================================================
        // 定数
        // ================================================================

        /// <summary>既定の採取間隔（秒）。</summary>
        public const int DefaultIntervalSec = 10;

        /// <summary>CSV のスキーマ版。列を変えたら上げること（解析側の判別用）。</summary>
        private const string SchemaVersion = "2";

        private const string ColumnHeader =
            "kind,time,sec,frames,fps,gcHeapMB,gc0,gc1,gc2,bufMgrLive," +
            "undoVtx,redoVtx,undoMesh,redoMesh,undoProj,redoProj,pendVtx," +
            "models,meshes,verts,faces,logLines,nativeMB,gfxMB," +
            "tool,sub,panel,cmds,topCmd,topCmdN,hover,hoverMs,logAdd,note";

        // ================================================================
        // 外部依存（Player 側から差し込む。未設定なら該当列は 0 / 空になる）
        // ================================================================

        /// <summary>現在のプロジェクト。</summary>
        public static Func<ProjectContext> GetProject;

        /// <summary>Undo コントローラ。</summary>
        public static Func<MeshUndoController> GetUndoController;

        /// <summary>統合ログの保持行数。</summary>
        public static Func<int> GetLogLineCount;

        /// <summary>
        /// 統合ログへこれまでに投入した総行数（破棄したぶんも含む）。
        ///
        /// GetLogLineCount は上限で頭打ちになるため、区間内に何行出たかが判らない。
        /// 区間差分を取るためにこちらを別に取る。
        /// </summary>
        public static Func<long> GetLogTotalAdded;

        // ================================================================
        // 状態
        // ================================================================

        private static bool _running;
        private static StreamWriter _writer;
        private static IVisualElementScheduledItem _tick;

        /// <summary>記録中か。</summary>
        public static bool IsRunning => _running;

        /// <summary>現在の出力先パス。停止中は null。</summary>
        public static string CurrentPath { get; private set; }

        // 直前行の基準値（差分計算用）
        private static int   _lastFrame;
        private static float _lastTime;
        private static float _startTime;

        // ツール状態（push 方式。Player 側が切替時に書き込む）
        private static string _tool  = "-";
        private static string _sub   = "-";
        private static string _panel = "-";

        // 区間内のコマンド集計
        private static int _cmdCount;
        private static readonly Dictionary<string, int> _cmdTally = new Dictionary<string, int>();

        // 区間内のホバー集計（ポインタ移動由来の GPU ヒットテスト経路）
        private static int    _hoverCount;
        private static double _hoverMs;

        // 直前行時点の統合ログ総投入行数（差分計算用）
        private static long _lastLogAdded;

        // 行生成の使い回しバッファ（毎行の確保を避ける）
        private static readonly StringBuilder _sb = new StringBuilder(512);

        // ================================================================
        // 開始 / 停止
        // ================================================================

        /// <summary>
        /// 記録を開始する。既に記録中なら何もしない。
        /// scheduleHost には UI のルート VisualElement を渡す（スケジューラの寄生先）。
        /// </summary>
        public static void Start(VisualElement scheduleHost, int intervalSec = DefaultIntervalSec)
        {
            if (_running) return;
            if (scheduleHost == null) return;
            if (intervalSec < 1) intervalSec = 1;

            string path = Path.Combine(
                Application.persistentDataPath,
                "PolyLing_perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");

            try
            {
                _writer = new StreamWriter(path, append: true, encoding: new UTF8Encoding(false));
                _writer.AutoFlush = true;   // 強制終了しても直前の行まで残す
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PL/Perf] ログを開けません: " + path + " / " + ex.Message);
                _writer = null;
                return;
            }

            CurrentPath = path;

            WriteRaw("# PolyLing performance log  schema=" + SchemaVersion +
                     " intervalSec=" + intervalSec.ToString(CultureInfo.InvariantCulture) +
                     " started=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            WriteRaw("# kind: S=periodic sample, E=tool/subtool/panel change event");
            WriteRaw("# frames,fps,cmds,topCmd,topCmdN are deltas since the previous row (either kind)");
            WriteRaw("# sec=seconds since logging started. gcHeapMB=GC.GetTotalMemory(false)");
            WriteRaw("# bufMgrLive=live UnifiedBufferManager instances. nativeMB/gfxMB are 0 unless ENABLE_PROFILER");
            WriteRaw("# hover=pointer-hover updates in the interval. hoverMs=ms spent in the hover GPU hit-test path");
            WriteRaw("# logAdd=lines pushed into PlayerLog in the interval (logLines is the retained count, capped)");
            WriteRaw(ColumnHeader);

            _startTime = Time.realtimeSinceStartup;
            _lastTime  = _startTime;
            _lastFrame = Time.frameCount;
            _cmdCount  = 0;
            _cmdTally.Clear();
            _hoverCount   = 0;
            _hoverMs      = 0.0;
            _lastLogAdded = GetLogTotalAdded?.Invoke() ?? 0L;

            _running = true;
            _tick = scheduleHost.schedule.Execute(OnTick).Every(intervalSec * 1000);

            WriteRow("S", "start");
        }

        /// <summary>記録を停止してファイルを閉じる。記録中でなければ何もしない。</summary>
        public static void Stop()
        {
            if (!_running) return;

            WriteRow("S", "stop");

            _running = false;

            try { _tick?.Pause(); } catch { /* 停止失敗は無視 */ }
            _tick = null;

            try { _writer?.Dispose(); } catch { /* 閉じ損ねは無視 */ }
            _writer = null;

            CurrentPath = null;
            _cmdTally.Clear();
            _cmdCount   = 0;
            _hoverCount = 0;
            _hoverMs    = 0.0;
        }

        // ================================================================
        // Player 側からの状態通知（記録 OFF のときは即 return する）
        // ================================================================

        /// <summary>
        /// 現在のツールとサブツールを通知する。
        /// 値が変わったときだけ E 行を 1 行出す。
        /// </summary>
        public static void SetToolState(string tool, string sub)
        {
            if (!_running) return;

            tool = string.IsNullOrEmpty(tool) ? "-" : tool;
            sub  = string.IsNullOrEmpty(sub)  ? "-" : sub;
            if (tool == _tool && sub == _sub) return;

            string note = "tool:" + _tool + ">" + tool + " sub:" + _sub + ">" + sub;
            _tool = tool;
            _sub  = sub;
            WriteRow("E", note);
        }

        /// <summary>右ペインの表示中セクション名を通知する。変化時のみ E 行を出す。</summary>
        public static void SetPanel(string panel)
        {
            if (!_running) return;

            panel = string.IsNullOrEmpty(panel) ? "-" : panel;
            if (panel == _panel) return;

            string note = "panel:" + _panel + ">" + panel;
            _panel = panel;
            WriteRow("E", note);
        }

        /// <summary>
        /// 実行されたコマンドを 1 件数える。PlayerCommandDispatcher.Dispatch から呼ぶ。
        /// 記録 OFF のときは bool 判定 1 回で戻るため、常時呼んでよい。
        /// </summary>
        public static void CountCommand(string commandName)
        {
            if (!_running) return;
            if (string.IsNullOrEmpty(commandName)) return;

            _cmdCount++;
            _cmdTally.TryGetValue(commandName, out int n);
            _cmdTally[commandName] = n + 1;
        }

        /// <summary>
        /// ポインタ移動由来のホバー更新を 1 件数える。
        /// PlayerViewportManager.NotifyPointerHover から呼ぶ。
        /// 記録 OFF のときは bool 判定 1 回で戻るため、常時呼んでよい。
        /// </summary>
        public static void CountHover()
        {
            if (!_running) return;
            _hoverCount++;
        }

        /// <summary>
        /// ホバー経路（GPU ヒットテストパイプライン）に費やしたミリ秒を加算する。
        /// 呼出し側は IsRunning が false のとき計測自体を行わないこと。
        /// </summary>
        public static void AddHoverMs(double ms)
        {
            if (!_running) return;
            _hoverMs += ms;
        }

        // ================================================================
        // 採取
        // ================================================================

        private static void OnTick()
        {
            if (!_running) return;
            WriteRow("S", string.Empty);
        }

        /// <summary>1 行書く。例外は握り潰し、失敗が続く状態なら記録を止める。</summary>
        private static void WriteRow(string kind, string note)
        {
            var w = _writer;
            if (w == null) return;

            // ── 区間差分 ────────────────────────────────────────────
            int   frameNow = Time.frameCount;
            float now      = Time.realtimeSinceStartup;
            int   dFrames  = frameNow - _lastFrame;
            float dSec     = now - _lastTime;
            float fps      = (dSec > 0.0001f) ? (dFrames / dSec) : 0f;

            // ── 最多コマンド ────────────────────────────────────────
            string topCmd  = "-";
            int    topCmdN = 0;
            foreach (var kv in _cmdTally)
                if (kv.Value > topCmdN) { topCmd = kv.Key; topCmdN = kv.Value; }

            // ── Undo ────────────────────────────────────────────────
            int undoVtx = 0, redoVtx = 0, undoMesh = 0, redoMesh = 0;
            int undoProj = 0, redoProj = 0, pendVtx = 0;
            var uc = GetUndoController?.Invoke();
            if (uc != null)
            {
                var vs = uc.VertexEditStack;
                if (vs != null) { undoVtx = vs.UndoCount; redoVtx = vs.RedoCount; pendVtx = vs.PendingCount; }
                var ms = uc.MeshListStack;
                if (ms != null) { undoMesh = ms.UndoCount; redoMesh = ms.RedoCount; }
                var ps = uc.ProjectStack;
                if (ps != null) { undoProj = ps.UndoCount; redoProj = ps.RedoCount; }
            }

            // ── モデル規模 ──────────────────────────────────────────
            int models = 0, meshes = 0, verts = 0, faces = 0;
            var project = GetProject?.Invoke();
            if (project != null)
            {
                models = project.ModelCount;
                var model = project.CurrentModel;
                var list  = model?.MeshContextList;
                if (list != null)
                {
                    meshes = list.Count;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var mc = list[i];
                        if (mc == null) continue;
                        verts += mc.VertexCount;
                        faces += mc.FaceCount;
                    }
                }
            }

            // ── 統合ログの区間投入行数 ──────────────────────────────
            long logAddedNow = GetLogTotalAdded?.Invoke() ?? 0L;
            long dLogAdded   = logAddedNow - _lastLogAdded;
            if (dLogAdded < 0) dLogAdded = 0;   // Clear() で 0 に戻る

            // ── ネイティブ側（Profiler が無効なビルドでは 0） ────────
            long nativeBytes = 0;
            long gfxBytes    = 0;
#if ENABLE_PROFILER
            nativeBytes = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            gfxBytes    = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif

            var inv = CultureInfo.InvariantCulture;
            _sb.Clear();
            _sb.Append(kind).Append(',');
            _sb.Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", inv)).Append(',');
            _sb.Append((now - _startTime).ToString("F1", inv)).Append(',');
            _sb.Append(dFrames.ToString(inv)).Append(',');
            _sb.Append(fps.ToString("F2", inv)).Append(',');
            _sb.Append(ToMB(GC.GetTotalMemory(false)).ToString("F2", inv)).Append(',');
            _sb.Append(GC.CollectionCount(0).ToString(inv)).Append(',');
            _sb.Append(GC.CollectionCount(1).ToString(inv)).Append(',');
            _sb.Append(GC.CollectionCount(2).ToString(inv)).Append(',');
            _sb.Append(Poly_Ling.Core.UnifiedBufferManager.LiveCount.ToString(inv)).Append(',');
            _sb.Append(undoVtx.ToString(inv)).Append(',');
            _sb.Append(redoVtx.ToString(inv)).Append(',');
            _sb.Append(undoMesh.ToString(inv)).Append(',');
            _sb.Append(redoMesh.ToString(inv)).Append(',');
            _sb.Append(undoProj.ToString(inv)).Append(',');
            _sb.Append(redoProj.ToString(inv)).Append(',');
            _sb.Append(pendVtx.ToString(inv)).Append(',');
            _sb.Append(models.ToString(inv)).Append(',');
            _sb.Append(meshes.ToString(inv)).Append(',');
            _sb.Append(verts.ToString(inv)).Append(',');
            _sb.Append(faces.ToString(inv)).Append(',');
            _sb.Append((GetLogLineCount?.Invoke() ?? 0).ToString(inv)).Append(',');
            _sb.Append(ToMB(nativeBytes).ToString("F2", inv)).Append(',');
            _sb.Append(ToMB(gfxBytes).ToString("F2", inv)).Append(',');
            _sb.Append(Csv(_tool)).Append(',');
            _sb.Append(Csv(_sub)).Append(',');
            _sb.Append(Csv(_panel)).Append(',');
            _sb.Append(_cmdCount.ToString(inv)).Append(',');
            _sb.Append(Csv(topCmd)).Append(',');
            _sb.Append(topCmdN.ToString(inv)).Append(',');
            _sb.Append(_hoverCount.ToString(inv)).Append(',');
            _sb.Append(_hoverMs.ToString("F1", inv)).Append(',');
            _sb.Append(dLogAdded.ToString(inv)).Append(',');
            _sb.Append(Csv(note));

            WriteRaw(_sb.ToString());

            // 次の行の基準へ
            _lastFrame    = frameNow;
            _lastTime     = now;
            _cmdCount     = 0;
            _cmdTally.Clear();
            _hoverCount   = 0;
            _hoverMs      = 0.0;
            _lastLogAdded = logAddedNow;
        }

        /// <summary>1 行そのまま書く。失敗したら記録を止める（同じ例外を出し続けないため）。</summary>
        private static void WriteRaw(string line)
        {
            var w = _writer;
            if (w == null) return;
            try
            {
                w.WriteLine(line);
            }
            catch (Exception ex)
            {
                _running = false;
                try { _tick?.Pause(); } catch { }
                _tick = null;
                try { w.Dispose(); } catch { }
                _writer     = null;
                CurrentPath = null;
                Debug.LogWarning("[PL/Perf] 書き込みに失敗したため記録を停止しました: " + ex.Message);
            }
        }

        private static double ToMB(long bytes) => bytes / (1024.0 * 1024.0);

        /// <summary>CSV セル用のエスケープ。区切り・引用符・改行を含むときだけ引用する。</summary>
        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0)
                return s;
            return "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }
    }
}
