// PlayerStagedTestSubPanelBase.cs
// 「段を並べてボタン 1 回で流し、段ごとに 3 行のログを出す」検証パネルの共通部。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ実コマンドを送るか】
//   Ops を直接叩くと、ディスパッチャ側の欠陥（対象の解決を選択状態に頼る、
//   ミラーペアを解体する等）が検査を素通りする。パネルが押されたときに
//   飛ぶのと同じ PanelCommand を送ることで、実際の経路をそのまま通す。
//
// 【段の区切り】
//   コマンドはキュー経由で処理されることがあり、送信直後の状態は当てにならない。
//   1 段ごとに間を空けてから検査する。MonoBehaviour.Update は使わない
//   （毎フレーム駆動は置かない規約）。UIToolkit の schedule で、
//   段が終わるたびに次の段を予約する。テストが動いていない間は何も走らない。
//
// 【3 行ログ】
//   検証パネルはチュートリアルも兼ねる。段ごとに必ず次の 3 つを書く。
//     やったこと   … 送ったコマンドと主要な値
//     UI でやるなら … 相当する手操作（どのパネルのどのボタンか）
//     なぜ         … その段が要る理由と、失敗したときに疑う場所
//   成功時も省かない。結果表だけだと「何をしたか」が残らない。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>段を並べて 1 つずつ流す検証パネルの共通部。</summary>
    public abstract class PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected abstract string TitleText { get; }
        protected abstract string NoteText  { get; }

        /// <summary>タイトルと実行ボタンの間に置く設定 UI。</summary>
        protected abstract void BuildOptionsUI(VisualElement root);

        /// <summary>段を並べる。</summary>
        protected abstract void CollectStages(List<(string Name, Func<StageResult> Run)> stages);

        /// <summary>実行前の入力検査。false を返すと走らせない（理由は SetStatus で出す）。</summary>
        protected virtual bool CanRun() => true;

        /// <summary>実行開始時に持ち回りの値を初期化する。</summary>
        protected virtual void ResetRunState() { }

        /// <summary>全段が終わったあとに呼ぶ。レポート書き出しなど。</summary>
        protected virtual void OnFinished(bool aborted) { }

        // ================================================================
        // UI
        // ================================================================

        private VisualElement _root;
        private Label         _status;
        private ScrollView    _logView;
        private Button        _runButton;

        /// <summary>3 行ログの平文。レポートへ落とすために持つ。</summary>
        protected readonly List<string> PlainLog = new List<string>();

        // ================================================================
        // 実行状態
        // ================================================================

        /// <summary>段の結果。Retry は同じ段をもう一度呼ぶ。</summary>
        public enum StageResult { Ok, Fail, Retry }

        private readonly List<(string Name, Func<StageResult> Run)> _stages =
            new List<(string, Func<StageResult>)>();

        private int  _stageIndex;
        private int  _retryCount;
        private bool _running;

        /// <summary>段の間に空けるミリ秒。コマンドキューが捌けるのを待つ。</summary>
        protected virtual long StageIntervalMs => 120;

        /// <summary>同じ段を待ち直す上限。</summary>
        protected virtual int MaxRetry => 60;

        protected bool IsRunning => _running;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = parent;
            _root.Clear();

            var title = new Label(TitleText);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            _root.Add(title);

            var note = new Label(NoteText);
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            _root.Add(note);

            BuildOptionsUI(_root);

            _runButton = new Button(OnRun) { text = "実行" };
            _runButton.style.height       = 26;
            _runButton.style.marginTop    = 6;
            _runButton.style.marginBottom = 4;
            _root.Add(_runButton);

            _status = new Label("未実行");
            _status.style.whiteSpace   = WhiteSpace.Normal;
            _status.style.marginBottom = 4;
            _root.Add(_status);

            _logView = new ScrollView(ScrollViewMode.Vertical);
            _logView.style.minHeight = 220;
            _logView.style.maxHeight = 460;
            _root.Add(_logView);
        }

        public virtual void Refresh()
        {
            if (_runButton != null) _runButton.SetEnabled(!_running);
            OutDest?.Refresh();
        }

        // ================================================================
        // 書き込み先
        //
        // 検証パネルは以前、出力先を「フルパスの欄」で持ち、実行ボタンが
        // その値へ無確認で書いていた。欄の初期値は履歴か、読み込んだファイル名から
        // 作った別名だったため、前回の出力や別パネルの出力を黙って潰していた。
        // 他のパネルと同じ規約（SaveDest.cs）に揃える。
        //   ・欄はフォルダだけ
        //   ・ファイル名は実行のたびに保存ダイアログで確定する
        //   ・レポートも同じフォルダの下に置く
        // ================================================================

        /// <summary>出力先フォルダ欄。BuildOptionsUI で MakeOutDest を呼んで作る。</summary>
        protected PlayerSaveDestRow OutDest { get; private set; }

        /// <summary>出力先フォルダ欄を 1 本作る。派生は戻り値を root へ Add する。</summary>
        /// <param name="dialogTitle">保存ダイアログの見出し。</param>
        /// <param name="extension">拡張子（ドット無し）。</param>
        /// <param name="defaultName">ファイル名欄の初期値を返す関数。null 可。</param>
        protected VisualElement MakeOutDest(string dialogTitle, string extension, Func<string> defaultName = null)
        {
            OutDest = new PlayerSaveDestRow(
                "書き込み先フォルダ", Poly_Ling.Core.SaveDest.Keys.Pipeline,
                dialogTitle, extension, defaultName);
            return OutDest.Root;
        }

        /// <summary>
        /// 実行前に出力先ファイルを確定する（「名前を付けて保存」）。
        /// キャンセルなら空文字。CanRun から呼ぶこと。
        /// </summary>
        protected string AskOutPath() => OutDest?.AskSavePath() ?? "";

        /// <summary>
        /// レポートの書き出し先。出力先フォルダの下へ
        /// <c>&lt;testName&gt;/&lt;yyyyMMdd_HHmmss&gt;/report.txt</c> で作る。
        /// フォルダ欄が空のときだけ、従来どおり persistentDataPath 配下へ落とす。
        /// </summary>
        protected string BuildReportPath(string testName)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            string folder = OutDest?.Folder ?? "";
            if (string.IsNullOrEmpty(folder))
                folder = System.IO.Path.Combine(Application.persistentDataPath, "PolyLing");

            return System.IO.Path.Combine(folder, testName, stamp, "report.txt");
        }

        // ================================================================
        // ログ
        // ================================================================

        /// <summary>段 1 件のログ。3 つとも書くこと（クラス冒頭の注記を参照）。</summary>
        protected void Log(string stage, bool ok, string did, string ui, string why)
        {
            var box = new VisualElement();
            box.style.marginBottom    = 6;
            box.style.paddingLeft     = 6;
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = new StyleColor(ok
                ? new Color(0.45f, 0.85f, 0.5f)
                : new Color(0.95f, 0.45f, 0.4f));

            var head = new Label((ok ? "OK   " : "NG   ") + stage);
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.whiteSpace = WhiteSpace.Normal;
            box.Add(head);

            box.Add(Line("やったこと: " + did, new Color(0.90f, 0.90f, 0.90f)));
            if (!string.IsNullOrEmpty(ui))
                box.Add(Line("UI でやるなら: " + ui, new Color(0.65f, 0.85f, 1.00f)));
            if (!string.IsNullOrEmpty(why))
                box.Add(Line("なぜ: " + why, new Color(0.78f, 0.78f, 0.78f)));

            _logView.Add(box);
            _logView.scrollOffset = new Vector2(0, float.MaxValue);

            var sb = new StringBuilder();
            sb.Append(ok ? "[OK] " : "[NG] ").AppendLine(stage);
            sb.Append("    やったこと: ").AppendLine(did);
            if (!string.IsNullOrEmpty(ui))  sb.Append("    UI でやるなら: ").AppendLine(ui);
            if (!string.IsNullOrEmpty(why)) sb.Append("    なぜ: ").AppendLine(why);
            PlainLog.Add(sb.ToString());
        }

        private static Label Line(string t, Color c)
        {
            var l = new Label(t);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.fontSize   = 10;
            l.style.color      = new StyleColor(c);
            return l;
        }

        protected void SetStatus(string t)
        {
            if (_status != null) _status.text = t ?? "";
        }

        protected string CurrentStageName
            => (_stageIndex >= 0 && _stageIndex < _stages.Count) ? _stages[_stageIndex].Name : "?";

        protected StageResult Ok(string did, string ui, string why)
        { Log(CurrentStageName, true, did, ui, why); return StageResult.Ok; }

        protected StageResult Ng(string did, string ui, string why)
        { Log(CurrentStageName, false, did, ui, why); return StageResult.Fail; }

        // ================================================================
        // 実行
        // ================================================================

        private void OnRun()
        {
            if (_running) { SetStatus("実行中です。"); return; }
            if (!CanRun()) return;

            _logView.Clear();
            PlainLog.Clear();
            _stages.Clear();
            _stageIndex = 0;
            _retryCount = 0;
            _running    = true;
            _runButton?.SetEnabled(false);

            ResetRunState();
            CollectStages(_stages);

            SetStatus("実行中…");
            ScheduleNext();
        }

        /// <summary>次の段を予約する。Update は使わない（規約）。</summary>
        private void ScheduleNext()
            => _root.schedule.Execute(RunStage).StartingIn(StageIntervalMs);

        private void RunStage()
        {
            if (_stageIndex >= _stages.Count) { Finish(false, "完了しました。"); return; }

            var (name, run) = _stages[_stageIndex];

            StageResult r;
            try { r = run(); }
            catch (Exception e)
            {
                Log(name, false, "例外: " + e.Message, null, e.StackTrace ?? "");
                Debug.LogException(e);
                Finish(true, "例外で停止しました: " + e.Message);
                return;
            }

            switch (r)
            {
                case StageResult.Retry:
                    if (++_retryCount > MaxRetry)
                    {
                        Log(name, false, "時間内に完了しませんでした", null,
                            "コマンドの処理が終わるのを待ってから検査している。"
                          + "待ち切れないなら、前の段が実際には走っていない。");
                        Finish(true, "待機がタイムアウトしました。");
                        return;
                    }
                    ScheduleNext();
                    return;

                case StageResult.Fail:
                    Finish(true, "失敗で停止しました。");
                    return;

                default:
                    _stageIndex++;
                    _retryCount = 0;
                    ScheduleNext();
                    return;
            }
        }

        private void Finish(bool aborted, string message)
        {
            _running = false;
            _runButton?.SetEnabled(true);
            SetStatus(message);
            try { OnFinished(aborted); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ================================================================
        // 小物
        // ================================================================

        protected static Label Sec(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        protected static Label Hint(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            return l;
        }

        protected static FloatField F(string label, float v)
        {
            var f = new FloatField(label) { value = v };
            f.style.fontSize = 11;
            return f;
        }

        protected static IntegerField I(string label, int v)
        {
            var f = new IntegerField(label) { value = v };
            f.style.fontSize = 11;
            return f;
        }

        protected static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
