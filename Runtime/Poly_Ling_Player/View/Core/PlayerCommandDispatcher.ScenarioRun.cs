// PlayerCommandDispatcher.ScenarioRun.cs
// シナリオを上から順に流す。runScenario / continueScenario / queryScenarioRun / stopScenarioRun。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【止まる所】
//   判断が要る項目（ObjectGroupStep.RequiresJudgment：指示・確認）と、失敗した項目。
//   注意の項目は読み飛ばし、別のシナリオを呼ぶ項目では呼ばれたシナリオをその場で流す。
//   止める条件は ObjectGroupStep.RequiresJudgment の 1 か所だけを見る。
//
// 【途中の項目だけを撃つ口を置かない】
//   @prev は「直前に実行した項目」、@<項目 ID> は「その項目の結果」を指すので、
//   前の項目を飛ばして途中を撃つと、別の流れで残った値を黙って掴む。
//   流れは先頭から始めるか、止まった所から続けるかの 2 通りだけにする。
//
// 【控えは流れ 1 回ぶんに閉じる】
//   直前の項目の対象・戻り値と、項目ごとの戻り値は ScenarioRunState が持つ。
//   runScenario のたびに作り直すので、前の流れの結果は混ざらない。
//
// 【1 回に進める項目数】
//   パネルは maxSteps=1 で 1 項目ずつ呼び、呼ぶたびに画面を描き直す。
//   まとめて流すと終わるまで画面が固まり、何が起きているか見えない。
//   MCP からは省いて（0）止まるまで流す。
//
// 【シナリオは流し始めに写す】
//   流している途中でシナリオのファイルが書き換わっても、流れは写しを使う。
//
// 【グループと同じに扱う】（作り直し整備計画 段 11）
//   シナリオは疎なグループで、違いは密か疎か・処理時間だけにする。そのため項目ごとに
//     ・梯子・断面は取り込み元から読み直す（ObjectGroupOps.RefreshInputs。作り直しと同じ）
//     ・書き込みのある項目のあとに評価器を流す（RunModifierEvaluation）
//
// 【実行は Dispatch の再入】
//   Dispatch は _pendingResult を退避・復元するので（PlayerCommandDispatcher.cs の注記）、
//   内側の結果がこちらの応答を壊すことはない。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>流れが止まった理由。</summary>
    public enum ScenarioRunStop
    {
        /// <summary>止まっていない（項目数の上限で一旦返しただけ）。</summary>
        None,
        /// <summary>指示・確認の項目で、判断を待っている。</summary>
        Judgment,
        /// <summary>項目が失敗した。</summary>
        Failed,
        /// <summary>最後の項目まで終わった。</summary>
        Finished,
        /// <summary>
        /// 非同期の項目（invokePyAddon）の返事を待っている。返事が来たらディスパッチャが控えて None に戻す
        /// （失敗なら Failed）。毎フレームの見回りはしない（PyAddonHost.CallFinished で受ける）。
        /// </summary>
        Waiting,
    }

    /// <summary>流れの中での項目の状態。</summary>
    public enum ScenarioItemRunStatus
    {
        NotRun,
        /// <summary>別のシナリオを呼ぶ項目で、呼ばれたシナリオを流している最中。</summary>
        Running,
        Done,
        /// <summary>判断を待って止まっている。</summary>
        Stopped,
        Failed,
    }

    /// <summary>シナリオを流している途中の状態。1 回の流れにつき 1 つ。</summary>
    public sealed class ScenarioRunState
    {
        internal sealed class Frame
        {
            public string      Name;
            public ObjectGroup Group;
            public int         Index;

            /// <summary>
            /// このシナリオの引数に結んだ値（既定値＋渡された値）。呼び出しの段ごとに持ち、親子で混ざらない。
            /// 項目の引数の値が ${名前} 丸ごとなら、実行の直前にここから引く。
            /// </summary>
            public Dictionary<string, string> Params;
        }

        internal readonly List<Frame> Stack = new List<Frame>();

        internal readonly Dictionary<string, ScenarioItemRunStatus> StatusMap
            = new Dictionary<string, ScenarioItemRunStatus>(StringComparer.Ordinal);

        /// <summary>項目ごとの戻り値。鍵は「シナリオ名/項目 ID」。</summary>
        internal readonly Dictionary<string, (int[] Master, ulong[] Ids, string Data)> Results
            = new Dictionary<string, (int[], ulong[], string)>(StringComparer.Ordinal);

        /// <summary>直前に実行した項目の対象と戻り値。</summary>
        internal int[]   PrevMaster;
        internal ulong[] PrevIds;
        internal string  PrevData;

        /// <summary>判断待ちで止まった項目を、次の進行で越えてよいか。</summary>
        internal bool PassJudgment;

        /// <summary>次に実行するコマンドの項目で差し替える引数。</summary>
        internal string[] OverrideKeys;
        internal string[] OverrideValues;

        internal readonly List<string> LogLines = new List<string>();

        private const int LogLimit = 1000;

        public string          RootName         { get; internal set; } = "";
        public ScenarioRunStop Stop             { get; internal set; } = ScenarioRunStop.None;
        public string          StopMessage      { get; internal set; } = "";

        /// <summary>止まった項目の種別（判断待ちのとき、指示か確認かを表示で分けるため）。</summary>
        public ObjectGroupStepKind StopStepKind { get; internal set; } = ObjectGroupStepKind.Command;

        public int             ExecutedCommands { get; internal set; }

        /// <summary>
        /// 流し始めたシナリオの出力を評価した値（名前 → 値の JSON オブジェクト）。
        /// 最後まで流し終え、出力を宣言しているときだけ入る。それ以外は null。
        /// </summary>
        public string OutputsJson { get; internal set; }

        /// <summary>返事を待っている呼び出しの番号（PyAddonHost の CallState.No）。待っていなければ 0。</summary>
        public int WaitCallNo { get; internal set; }

        /// <summary>返事を待っている項目のシナリオ名と項目 ID、記録の頭。</summary>
        internal string WaitScenario;
        internal string WaitItemId;
        internal string WaitAt;

        /// <summary>
        /// 直前に済ませた、経路を持つ項目の経路。次の項目の経路を選ぶとき「近いもの」の基準にする
        /// （UiRouteCatalog.Choose）。経路の無い項目（注意・指示・UI なし）では変えない。
        /// </summary>
        public UiRoute PrevRoute { get; internal set; }

        /// <summary>
        /// 人がこの項目のコマンドを実行するのを待っているときの action 名。待っていなければ null。
        /// 案内バーの「人間の入力待ち」が立てる（PlayerCommandDispatcher.BeginAwaitUser）。
        /// </summary>
        public string AwaitUserAction { get; internal set; }

        /// <summary>いま居る項目のコマンドの型。コマンドの項目でなければ null。</summary>
        public Type CurrentCommandType
        {
            get
            {
                var st = CurrentStep;
                return st != null && st.IsExecutable ? PanelCommandFactory.ResolveType(st.Action) : null;
            }
        }

        /// <summary>いま居る項目のコマンドを画面で行う経路（前の項目に近いもの、無ければ既定）。無ければ null。</summary>
        public UiRoute CurrentRoute => UiRouteCatalog.Choose(CurrentCommandType, PrevRoute);

        /// <summary>いま居るシナリオの名前。呼び出し中のシナリオがあればそちら。</summary>
        public string CurrentScenario => Top?.Name ?? "";

        /// <summary>いま居るシナリオ（写し）。</summary>
        public ObjectGroup CurrentGroup => Top?.Group;

        /// <summary>いま居る項目の位置。0 始まり。終わっていれば項目数と同じ。</summary>
        public int CurrentIndex => Top?.Index ?? -1;

        /// <summary>いま居る項目。無ければ null。</summary>
        public ObjectGroupStep CurrentStep
        {
            get
            {
                var f = Top;
                if (f?.Group?.Steps == null) return null;
                return (f.Index >= 0 && f.Index < f.Group.Steps.Count) ? f.Group.Steps[f.Index] : null;
            }
        }

        /// <summary>呼び出しの積み重なり。先頭が流し始めたシナリオ。</summary>
        public List<string> StackNames
        {
            get
            {
                var list = new List<string>(Stack.Count);
                foreach (var f in Stack) list.Add(f.Name);
                return list;
            }
        }

        public IReadOnlyList<string> Log => LogLines;

        public ScenarioItemRunStatus StatusOf(string scenario, string scenarioItemId)
            => StatusMap.TryGetValue(Key(scenario, scenarioItemId), out var s) ? s : ScenarioItemRunStatus.NotRun;

        internal Frame Top => Stack.Count > 0 ? Stack[Stack.Count - 1] : null;

        internal static string Key(string scenario, string scenarioItemId)
            => (scenario ?? "") + "/" + (scenarioItemId ?? "");

        internal void SetStatus(string scenario, string scenarioItemId, ScenarioItemRunStatus s)
            => StatusMap[Key(scenario, scenarioItemId)] = s;

        internal void AddLog(string line)
        {
            LogLines.Add(line ?? "");
            if (LogLines.Count > LogLimit) LogLines.RemoveRange(0, LogLines.Count - LogLimit);
        }
    }

    public partial class PlayerCommandDispatcher
    {
        /// <summary>流している途中の状態。流していなければ null。</summary>
        private ScenarioRunState _scenarioRun;

        /// <summary>流している途中の状態（パネルの表示用）。流していなければ null。</summary>
        public ScenarioRunState ScenarioRun => _scenarioRun;

        // ================================================================
        // コマンド
        // ================================================================

        private void RunRunScenario(RunScenarioCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            // 引数を結ぶ。流し始める前に断るので、綴り違いで途中まで流れることはない。
            var keys   = cmd.ParamKeys   ?? new string[0];
            var values = cmd.ParamValues ?? new string[0];
            if (keys.Length != values.Length)
            { Fail($"paramKeys が {keys.Length} 件、paramValues が {values.Length} 件で長さが合いません"); return; }

            var supplied = new List<KeyValuePair<string, string>>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
                supplied.Add(new KeyValuePair<string, string>(keys[i] ?? "", values[i] ?? ""));
            if (!ScenarioParam.TryBind(g, supplied, out var bound, out string bindError)) { Fail(bindError); return; }

            var run = new ScenarioRunState { RootName = g.Name ?? "" };
            run.Stack.Add(new ScenarioRunState.Frame { Name = g.Name ?? "", Group = g, Index = 0, Params = bound });
            run.AddLog($"「{g.Name}」を先頭から流す（{g.StepCount} 項目）" + DescribeParams(bound));
            _scenarioRun = run;

            int logStart = run.LogLines.Count - 1;
            if (!cmd.PrepareOnly) AdvanceScenarioRun(run, cmd.ModelIndex, cmd.MaxSteps);
            ReportScenarioRun(run, logStart, allLog: false);
        }

        private void RunContinueScenario(ContinueScenarioCommand cmd)
        {
            var run = _scenarioRun;
            if (run == null) { Fail("流しているシナリオがありません。runScenario で始めてください"); return; }
            if (run.Stop == ScenarioRunStop.Finished) { Fail($"「{run.RootName}」は最後まで終わっています"); return; }

            // アドオンの返事を待っている間は進めない（同じ項目を 2 度送らない）。今の状態だけ返すので、
            // MCP はこれ（か queryScenarioRun）を繰り返して stop が waiting でなくなるのを待てる。
            if (run.Stop == ScenarioRunStop.Waiting)
            {
                ReportScenarioRun(run, run.LogLines.Count, allLog: false);
                return;
            }

            var keys   = cmd.ArgKeys   ?? new string[0];
            var values = cmd.ArgValues ?? new string[0];
            if (keys.Length != values.Length)
            { Fail($"argKeys が {keys.Length} 件、argValues が {values.Length} 件で長さが合いません"); return; }

            if (keys.Length > 0)
            {
                run.OverrideKeys   = keys;
                run.OverrideValues = values;
            }

            // 判断待ちで止まっていたなら、その項目を越える。失敗ならやり直す（位置は動かさない）。
            if (run.Stop == ScenarioRunStop.Judgment) run.PassJudgment = true;

            // 人の入力待ちの最中に続けるよう言われたら、待つのをやめてシナリオどおりに実行する。
            run.AwaitUserAction = null;
            run.Stop        = ScenarioRunStop.None;
            run.StopMessage = "";

            int logStart = run.LogLines.Count;
            AdvanceScenarioRun(run, cmd.ModelIndex, cmd.MaxSteps);
            ReportScenarioRun(run, logStart, allLog: false);
        }

        private void RunQueryScenarioRun(QueryScenarioRunCommand cmd)
        {
            var run = _scenarioRun;
            if (run == null)
            {
                ReportData(CommandDataJson.New()
                    .Text("rootName",         "")
                    .Text("stop",             "none")
                    .Text("stopMessage",      "")
                    .Text("scenario",         "")
                    .Text("scenarioItemId",        "")
                    .Int ("stepNumber",       0)
                    .Int ("stepCount",        0)
                    .Text("stepKind",         "")
                    .Text("purpose",          "")
                    .Text("usageScene",       "")
                    .Int ("executedCommands", 0)
                    .Text("routeName",        "")
                    .Texts("routeItems",      new List<string>())
                    .Text("routeNote",        "")
                    .Flag("awaitUser",        false)
                    .Build());
                return;
            }
            ReportScenarioRun(run, 0, allLog: true);
        }

        private void RunStopScenarioRun(StopScenarioRunCommand cmd)
        {
            var run = _scenarioRun;
            bool had = run != null;

            // やめた位置を返す。流しを消すと queryScenarioRun からは見えなくなるので、ここで残す。
            string root = "", scen = "", elem = "";
            int stepNo = 0, executed = 0;
            if (had)
            {
                root     = run.RootName ?? "";
                executed = run.ExecutedCommands;
                var fr   = run.Top;
                if (fr != null)
                {
                    scen   = fr.Group?.Name ?? "";
                    stepNo = fr.Index + 1;
                    if (fr.Group?.Steps != null && fr.Index >= 0 && fr.Index < fr.Group.Steps.Count)
                        elem = fr.Group.Steps[fr.Index]?.ScenarioItemId ?? "";
                }
            }

            _scenarioRun = null;
            ReportData(CommandDataJson.New()
                .Flag("stopped",          had)
                .Text("rootName",         root)
                .Text("scenario",         scen)
                .Text("scenarioItemId",        elem)
                .Int ("stepNumber",       stepNo)
                .Int ("executedCommands", executed)
                .Build());
        }

        // ================================================================
        // 進める
        // ================================================================

        /// <summary>
        /// 止まるか、項目数の上限に達するか、終わるまで項目を処理する。
        /// maxSteps が 0 以下なら上限なし。
        /// </summary>
        private void AdvanceScenarioRun(ScenarioRunState run, int modelIndex, int maxSteps)
        {
            int processed = 0;
            if (run.Stop == ScenarioRunStop.Waiting) return;

            while (true)
            {
                var frame = run.Top;
                if (frame == null)
                {
                    run.Stop        = ScenarioRunStop.Finished;
                    run.StopMessage = "最後の項目まで終わった";
                    run.AddLog($"「{run.RootName}」を最後まで流した（実行したコマンド {run.ExecutedCommands} 本）");
                    return;
                }

                var steps = frame.Group.Steps;

                // このシナリオを流し終えた。呼び出し元へ戻り、呼んだ項目を済みにする。
                if (steps == null || frame.Index >= steps.Count)
                {
                    // 出力を宣言していれば、この段の控えが残っているうちに評価する。
                    // 評価できなければ失敗で止める（段は外さないので、直して続けると評価し直す）。
                    string outJson = null;
                    Dictionary<string, string> outVals = null;
                    if (frame.Group?.Outputs != null && frame.Group.Outputs.Count > 0)
                    {
                        if (!TryEvaluateOutputs(run, frame, out outJson, out outVals, out string outError))
                        {
                            FailOutputs(run, frame, outError);
                            return;
                        }
                    }

                    run.Stack.RemoveAt(run.Stack.Count - 1);

                    var parent = run.Top;
                    if (parent != null)
                    {
                        var refStep = parent.Group.Steps[parent.Index];

                        // 子の出力を、呼んだ参照項目の戻り値として控える（@<参照項目 ID>.<名前> / @prev.<名前>）。
                        // 出力を宣言していない子では控えを変えない（@prev は子の最後の項目の戻り値のまま。旧来の動き）。
                        if (outJson != null)
                        {
                            int[]   master = OutputInts(outVals, "masterIndices");
                            ulong[] ids    = OutputIds(outVals, "objectIds");
                            run.PrevMaster = master;
                            run.PrevIds    = ids;
                            run.PrevData   = outJson;
                            run.Results[ScenarioRunState.Key(parent.Name, refStep.ScenarioItemId)] = (master, ids, outJson);
                        }

                        run.SetStatus(parent.Name, refStep.ScenarioItemId, ScenarioItemRunStatus.Done);
                        run.AddLog($"「{frame.Name}」が終わった。「{parent.Name}」へ戻る"
                                   + (outJson != null ? $" 出力: {outJson}" : ""));
                        parent.Index++;
                    }
                    else if (outJson != null)
                    {
                        run.OutputsJson = outJson;
                        run.AddLog($"「{frame.Name}」の出力: {outJson}");
                    }
                    continue;
                }

                if (maxSteps > 0 && processed >= maxSteps) return;

                var step  = steps[frame.Index];
                string at = $"[{frame.Name} {frame.Index + 1}/{steps.Count} {step.ScenarioItemId}]";

                // ── 指示・確認：判断を待つ ──
                if (step.RequiresJudgment)
                {
                    if (run.PassJudgment)
                    {
                        run.PassJudgment = false;
                        run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Done);
                        run.AddLog($"{at} {KindWord(step.Kind)}を済ませて先へ進む");
                        frame.Index++;
                        processed++;
                        continue;
                    }

                    run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Stopped);
                    run.Stop         = ScenarioRunStop.Judgment;
                    run.StopStepKind = step.Kind;
                    run.StopMessage  = step.Purpose ?? "";
                    run.AddLog($"{at} {KindWord(step.Kind)}で止まった: {step.Purpose}");
                    return;
                }

                // 判断待ちを越える印は、その項目で使わなければ捨てる。
                run.PassJudgment = false;

                // ── 注意：読むだけ ──
                if (step.Kind == ObjectGroupStepKind.Note)
                {
                    run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Done);
                    run.AddLog($"{at} 注意: {step.Purpose}");
                    frame.Index++;
                    processed++;
                    continue;
                }

                // ── 別のシナリオを呼ぶ ──
                if (step.IsScenarioRef)
                {
                    string refName = step.RefName ?? "";
                    var child = ScenarioLibrary.Get(refName);
                    if (child == null)
                    {
                        FailRunStep(run, frame, step, at, $"呼ぶシナリオがありません: {refName}");
                        return;
                    }
                    foreach (var f in run.Stack)
                    {
                        if (!string.Equals(f.Name, refName, StringComparison.Ordinal)) continue;
                        FailRunStep(run, frame, step, at, $"「{refName}」は呼び出しの途中にあるので、ここで呼ぶと終わらない");
                        return;
                    }

                    // 参照項目の Args を子の引数に結ぶ。値の親の ${名前} はこの段の値で置き換える。
                    if (!ScenarioParam.TryBindRef(step, child, frame.Params, out var childParams, out string bindError))
                    {
                        FailRunStep(run, frame, step, at, bindError);
                        return;
                    }

                    run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Running);
                    run.AddLog($"{at} 別のシナリオ「{refName}」を流す（{child.StepCount} 項目）" + DescribeParams(childParams));
                    run.Stack.Add(new ScenarioRunState.Frame { Name = refName, Group = child, Index = 0, Params = childParams });
                    processed++;
                    continue;
                }

                // ── コマンドを実行する ──
                if (!TryExecuteRunStep(run, frame.Name, frame.Params, step, modelIndex, out string reason, out int waitNo))
                {
                    FailRunStep(run, frame, step, at, reason);
                    return;
                }

                // ── 非同期の項目（invokePyAddon）：返事を待って止まる ──
                if (waitNo > 0)
                {
                    run.ExecutedCommands++;
                    NotePrevRoute(run, step);
                    run.WaitCallNo   = waitNo;
                    run.WaitScenario = frame.Name;
                    run.WaitItemId   = step.ScenarioItemId;
                    run.WaitAt       = at;
                    run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Running);
                    run.Stop         = ScenarioRunStop.Waiting;
                    run.StopStepKind = step.Kind;
                    run.StopMessage  = $"Python アドオンの返事を待っています（呼び出し #{waitNo}）";
                    run.AddLog($"{at} {Or(step.Purpose, step.Action)} → アドオンの返事を待つ（#{waitNo}）");
                    HookPyAddonCalls();

                    // 起動できなかったなど、送る前に結果が決まっていることがある。そのときはここで控える。
                    var already = GetPyAddonHost?.Invoke()?.GetCall(waitNo);
                    if (already == null || !IsCallFinished(already)) return;
                    if (!ApplyAddonCall(run, already)) return;
                    processed++;
                    continue;
                }

                run.ExecutedCommands++;
                NotePrevRoute(run, step);
                run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Done);
                run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 成功（{step.Action}）");
                frame.Index++;
                processed++;
            }
        }

        private static readonly Dictionary<string, string> EmptyParams
            = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>流れの記録に添える引数の一覧（「 引数: a=1, b=2」）。無ければ空。</summary>
        private static string DescribeParams(Dictionary<string, string> bound)
        {
            if (bound == null || bound.Count == 0) return "";
            var parts = new List<string>(bound.Count);
            foreach (var kv in bound) parts.Add($"{kv.Key}={kv.Value}");
            return " 引数: " + string.Join(", ", parts);
        }

        /// <summary>済ませた項目に経路があれば、次の項目の経路選びの基準として控える。</summary>
        private static void NotePrevRoute(ScenarioRunState run, ObjectGroupStep step)
        {
            var r = UiRouteCatalog.Choose(PanelCommandFactory.ResolveType(step?.Action), run.PrevRoute);
            if (r != null) run.PrevRoute = r;
        }

        // ================================================================
        // 人間の入力待ち（案内バー）
        // ================================================================
        //
        // 【判定】
        //   ボタンが押されたかではなく、いま居る項目と同じコマンドが一番外側で実行されたかで見る
        //   （Dispatch の末尾から AcceptUserStepIfAwaiting を呼ぶ）。人がどの経路
        //   （別のボタン・ショートカット・ビューポートで描く操作）で行っても先へ進む。
        //   シナリオの項目は実行し直さない。人が実行した結果を、この項目の結果（@prev / @<項目 ID>）として控える。
        //   引数は人が決めた値になる（シナリオの値とは違ってよい）。
        //
        // 【入れ子は数えない】
        //   流しが実行する項目は continueScenario の内側（入れ子）なので、ここへは来ない。

        /// <summary>入力待ちで人が項目を済ませたときに呼ぶ（案内バーが次の項目へ進むため）。</summary>
        public Action OnScenarioUserStepAccepted;

        /// <summary>
        /// いま居る項目のコマンドを人が実行するのを待つ。コマンドの項目で、止まっていないときだけ立つ。
        /// </summary>
        public bool BeginAwaitUser()
        {
            var run = _scenarioRun;
            if (run == null || run.Stop != ScenarioRunStop.None) return false;
            var st = run.CurrentStep;
            if (st == null || !st.IsExecutable) return false;
            run.AwaitUserAction = st.Action;
            return true;
        }

        /// <summary>入力待ちをやめる。</summary>
        public void EndAwaitUser()
        {
            if (_scenarioRun != null) _scenarioRun.AwaitUserAction = null;
        }

        /// <summary>
        /// 一番外側で実行されたコマンドが、入力待ちの項目と同じなら、その項目を済ませる。
        /// 失敗したコマンドでは進めない（人がやり直せるように待ち続ける）。
        /// </summary>
        private void AcceptUserStepIfAwaiting(PanelCommand cmd, CommandResult result)
        {
            var run = _scenarioRun;
            if (run == null || run.AwaitUserAction == null || cmd == null) return;
            if (result == null || !result.Success) return;

            string action = PanelCommandFactory.ActionOf(cmd.GetType());
            if (!string.Equals(action, run.AwaitUserAction, StringComparison.Ordinal)) return;

            var frame = run.Top;
            var step  = run.CurrentStep;
            run.AwaitUserAction = null;
            if (frame == null || step == null) return;

            string at = $"[{frame.Name} {frame.Index + 1}/{frame.Group.Steps.Count} {step.ScenarioItemId}]";

            run.OverrideKeys   = null;
            run.OverrideValues = null;
            run.PrevMaster = result.MasterIndices;
            run.PrevIds    = result.ObjectIds;
            run.PrevData   = result.Data;
            run.Results[ScenarioRunState.Key(frame.Name, step.ScenarioItemId)] =
                (result.MasterIndices, result.ObjectIds, result.Data);

            run.ExecutedCommands++;
            NotePrevRoute(run, step);
            run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Done);
            run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 人が実行した（{action}）");
            frame.Index++;

            OnScenarioUserStepAccepted?.Invoke();
        }

        private static void FailRunStep(
            ScenarioRunState run, ScenarioRunState.Frame frame, ObjectGroupStep step, string at, string reason)
        {
            run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Failed);
            run.Stop         = ScenarioRunStop.Failed;
            run.StopStepKind = step.Kind;
            run.StopMessage  = reason ?? "";
            run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 失敗: {reason}");
        }

        /// <summary>
        /// コマンドの項目を 1 つ実行する。引数はシナリオのものに、continueScenario で渡した差し替えを重ね、
        /// ${名前} をこの段の引数の値に、@prev / @&lt;項目 ID&gt; を流れの控えから直してから組み立てる。
        /// 引数を先に置き換えるので、引数の値に @prev を渡してもよい。
        /// 成功したら控えを更新し、差し替えを使い切る。
        /// </summary>
        private bool TryExecuteRunStep(
            ScenarioRunState run, string scenarioName, IReadOnlyDictionary<string, string> frameParams,
            ObjectGroupStep step, int modelIndex, out string reason, out int waitCallNo)
        {
            reason = null;
            waitCallNo = 0;

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in step.SortedArgs()) args[kv.Key] = kv.Value ?? "";

            if (run.OverrideKeys != null && run.OverrideValues != null)
            {
                for (int i = 0; i < run.OverrideKeys.Length && i < run.OverrideValues.Length; i++)
                {
                    if (string.IsNullOrEmpty(run.OverrideKeys[i])) continue;
                    args[run.OverrideKeys[i]] = run.OverrideValues[i] ?? "";
                }
            }

            var cmdType = PanelCommandFactory.ResolveType(step.Action);
            var keys = new List<string>(args.Keys);
            foreach (var key in keys)
            {
                // 文字列の配列（argKeys / argValues など）は、要素ごとに丸ごとの ${名前} / @… を置き換える。
                // 値の一部は置き換えない規則（ScenarioParam）は要素の単位で守る。
                if (PanelCommandFactory.IsStringArrayParam(cmdType, key)
                    && !ScenarioParam.TryParseRef(args[key], out _)
                    && (args[key].IndexOf(ScenarioParam.RefOpen, StringComparison.Ordinal) >= 0 || args[key].IndexOf('@') >= 0))
                {
                    var parts = PanelCommandFactory.SplitQuotedCsv(args[key]);
                    var outParts = new List<string>(parts.Length);
                    foreach (var part in parts)
                    {
                        if (!ScenarioParam.TrySubstitute(part, frameParams ?? EmptyParams, out string ps, out string pe))
                        { reason = pe; return false; }
                        if (!TryExpandPrev(run, ps, scenarioName, out string px, out string pw))
                        { reason = pw; return false; }
                        outParts.Add(PanelCommandFactory.EscCsvItem(px));
                    }
                    args[key] = string.Join(",", outParts);
                    continue;
                }

                // 引数が結ばれていない段（古い流れ）でも、${…} が残ったまま撃たないよう空の辞書で照合する。
                if (!ScenarioParam.TrySubstitute(args[key], frameParams ?? EmptyParams, out string substituted, out string perr))
                { reason = perr; return false; }
                args[key] = substituted;

                if (!TryExpandPrev(run, args[key], scenarioName, out string expanded, out string why))
                { reason = why; return false; }
                args[key] = expanded;
            }

            // モデル番号は項目を実行する時点のカレントモデルにする。
            //   流しの途中で createEmptyModel などがカレントを切り替える。ほとんどのコマンドは
            //   カレントモデルを対象にするが、renameModel / queryBone などは渡したモデル番号を使うため、
            //   runScenario の番号のままだと前の項目で作ったモデルと別のモデルに効いてしまう。
            int stepModelIndex = _getProject()?.CurrentModelIndex ?? modelIndex;
            if (stepModelIndex < 0) stepModelIndex = modelIndex;

            // 梯子・断面は、取り込み方と取り込み元が書いてあれば取り込み元から読み直す。
            // グループの作り直し（ObjectGroupOps.BuildCommand）と同じ読み方にする（作り直し整備計画 段 11）。
            // 取り込み方が Baked・取り込み元が -1 なら書いてある点列のまま。
            if (cmdType != null)
                Poly_Ling.Ops.ObjectGroupOps.RefreshInputs(_getProject(), stepModelIndex, cmdType, args, out _, out _);

            var inner = PanelCommandFactory.Create(step.Action, stepModelIndex, args, out string createError);
            if (inner == null) { reason = $"{step.Action} を組み立てられません: {createError}"; return false; }

            var result = Dispatch(inner);
            if (result != null && !result.Success)
            { reason = $"{step.Action} が失敗しました: {result.Reason}"; return false; }

            run.OverrideKeys   = null;
            run.OverrideValues = null;

            // invokePyAddon は呼び出しの番号だけを返す（返事は後で届く）。控えは返事が来てから入れる。
            if (inner is InvokePyAddonCommand)
            {
                if (result?.Data == null || !TryReadJsonValue(result.Data, "callNo", out string noText)
                    || !int.TryParse(noText, System.Globalization.NumberStyles.Integer,
                                     System.Globalization.CultureInfo.InvariantCulture, out waitCallNo)
                    || waitCallNo <= 0)
                { reason = "invokePyAddon が呼び出しの番号を返しませんでした"; waitCallNo = 0; return false; }
                return true;
            }

            // 書き込みのある項目のあとに、グループの項目（モディファイア）を依存の順に流す（作り直し整備計画 段 11）。
            // 項目は入れ子の Dispatch なので一番外側の終わり（DispatchAuthorized）の評価に乗らず、
            // 流しの中では画面更新のきっかけ（RebuildAdapter）も通らない。署名を取り直してから流す。
            // こうしないと、同じコマンドの並びを 1 つずつ撃ったときとシナリオで流したときで結果が変わる。
            if (ShouldEvaluateModifiersAfter(inner))
                RunModifierEvaluation($"シナリオ {scenarioName}/{step.ScenarioItemId}", recheckInputs: true);

            run.PrevMaster = result?.MasterIndices;
            run.PrevIds    = result?.ObjectIds;
            run.PrevData   = result?.Data;
            run.Results[ScenarioRunState.Key(scenarioName, step.ScenarioItemId)] =
                (result?.MasterIndices, result?.ObjectIds, result?.Data);
            return true;
        }

        // ================================================================
        // シナリオの出力
        // ================================================================

        /// <summary>
        /// 段（frame）のシナリオの出力を評価する。From は項目の引数の値と同じ規則で読む
        /// （${名前} をこの段の引数で、@prev / @&lt;項目 ID&gt; を流れの控えで）。
        /// 値は文字列にそろえる（配列はカンマ区切り。TryReadJsonValue と同じ形）。
        /// </summary>
        private static bool TryEvaluateOutputs(
            ScenarioRunState run, ScenarioRunState.Frame frame,
            out string json, out Dictionary<string, string> values, out string error)
        {
            json   = null;
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            error  = null;

            var obj = new Newtonsoft.Json.Linq.JObject();
            foreach (var o in frame.Group.Outputs)
            {
                if (o == null || string.IsNullOrEmpty(o.Name)) continue;
                string v = o.From ?? "";
                if (!ScenarioParam.TrySubstitute(v, frame.Params ?? EmptyParams, out string s1, out string perr))
                { error = $"出力 {o.Name}: {perr}"; return false; }
                if (!TryExpandPrev(run, s1, frame.Name, out string s2, out string why))
                { error = $"出力 {o.Name}: {why}"; return false; }
                values[o.Name] = s2 ?? "";
                obj[o.Name]    = s2 ?? "";
            }
            json = obj.ToString(Newtonsoft.Json.Formatting.None);
            return true;
        }

        /// <summary>出力を評価できなかった。呼んだ参照項目（無ければ流し始めのシナリオ）の失敗として止める。</summary>
        private static void FailOutputs(ScenarioRunState run, ScenarioRunState.Frame frame, string reason)
        {
            ScenarioRunState.Frame parent = run.Stack.Count >= 2 ? run.Stack[run.Stack.Count - 2] : null;
            if (parent != null)
            {
                var refStep = parent.Group.Steps[parent.Index];
                run.SetStatus(parent.Name, refStep.ScenarioItemId, ScenarioItemRunStatus.Failed);
                run.StopStepKind = refStep.Kind;
            }
            run.Stop        = ScenarioRunStop.Failed;
            run.StopMessage = $"「{frame.Name}」の出力を評価できません: {reason}";
            run.AddLog($"[{frame.Name}] 出力を評価できない: {reason}");
        }

        private static int[] OutputInts(Dictionary<string, string> vals, string key)
        {
            if (vals == null || !vals.TryGetValue(key, out string csv) || string.IsNullOrEmpty(csv)) return null;
            var list = new List<int>();
            foreach (var part in csv.Split(','))
                if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out int n))
                    list.Add(n);
            return list.Count > 0 ? list.ToArray() : null;
        }

        private static ulong[] OutputIds(Dictionary<string, string> vals, string key)
        {
            if (vals == null || !vals.TryGetValue(key, out string csv) || string.IsNullOrEmpty(csv)) return null;
            var list = new List<ulong>();
            foreach (var part in csv.Split(','))
                if (ulong.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer,
                                   System.Globalization.CultureInfo.InvariantCulture, out ulong n))
                    list.Add(n);
            return list.Count > 0 ? list.ToArray() : null;
        }

        // ================================================================
        // 非同期の項目（invokePyAddon）の返事
        // ================================================================
        //
        // 【待ち方】
        //   項目を実行したら stop=waiting で止まり、PyAddonHost.CallFinished で返事を受ける（見回りはしない）。
        //   返事が来たら控え（@prev / @<項目 ID>）を入れて項目を済ませ、stop を none に戻す。
        //   その先を流すのは呼ぶ側：案内バーは OnScenarioAsyncStepFinished で自動で続け、
        //   MCP は continueScenario で続ける。失敗なら stop=failed（続けると同じ項目をやり直す＝送り直す）。
        // 【控えの形】
        //   run の data はアドオンの返事 {message, log, result:{…}}。控えには result の欄を平らにして
        //   message を足したものを入れる（@prev.<result の欄>・@prev.message で引ける）。
        //   result に masterIndices（配列）か masterIndex（数）があれば対象として、objectIds があれば安定 ID として控える。
        //   describe の data（引数の宣言）はそのまま控える。

        /// <summary>非同期の項目の返事を受けて、ディスパッチャが控えを入れたとき（案内バーが続けるため）。</summary>
        public Action OnScenarioAsyncStepFinished;

        private PyAddonHost _hookedPyAddonHost;

        private void HookPyAddonCalls()
        {
            var h = GetPyAddonHost?.Invoke();
            if (h == null || h == _hookedPyAddonHost) return;
            if (_hookedPyAddonHost != null) _hookedPyAddonHost.CallFinished -= OnPyAddonCallFinished;
            _hookedPyAddonHost = h;
            h.CallFinished += OnPyAddonCallFinished;
        }

        private static bool IsCallFinished(PyAddonHost.CallState c)
            => c.Status == PyAddonHost.CallState.Done || c.Status == PyAddonHost.CallState.Failed;

        private void OnPyAddonCallFinished(PyAddonHost.CallState call)
        {
            var run = _scenarioRun;
            if (run == null || run.Stop != ScenarioRunStop.Waiting || run.WaitCallNo != call.No) return;
            ApplyAddonCall(run, call);
            OnScenarioAsyncStepFinished?.Invoke();
        }

        /// <summary>待っていた返事を控えて項目を済ませる（成功なら true）。失敗なら失敗で止めて false。</summary>
        private bool ApplyAddonCall(ScenarioRunState run, PyAddonHost.CallState call)
        {
            var frame = run.Top;
            string scen = run.WaitScenario ?? "", item = run.WaitItemId ?? "", at = run.WaitAt ?? "";
            run.WaitCallNo = 0;

            if (call.Status != PyAddonHost.CallState.Done)
            {
                run.SetStatus(scen, item, ScenarioItemRunStatus.Failed);
                run.Stop        = ScenarioRunStop.Failed;
                run.StopMessage = $"アドオン {call.AddonId} の {call.Action} が失敗しました: {call.Error}";
                run.AddLog($"{at} → アドオンが失敗: {call.Error}");
                return false;
            }

            string data = call.ResultJson ?? "{}";
            int[]   master = null;
            ulong[] ids    = null;
            if (call.Action == PyAddonHost.ActionRun)
                data = FlattenAddonResult(data, out master, out ids);

            run.PrevMaster = master;
            run.PrevIds    = ids;
            run.PrevData   = data;
            run.Results[ScenarioRunState.Key(scen, item)] = (master, ids, data);

            run.SetStatus(scen, item, ScenarioItemRunStatus.Done);
            run.Stop        = ScenarioRunStop.None;
            run.StopMessage = "";
            run.AddLog($"{at} → アドオンの返事を受けた（#{call.No}）");
            if (frame != null && string.Equals(frame.Name, scen, StringComparison.Ordinal)) frame.Index++;
            return true;
        }

        /// <summary>アドオンの返事 {message, log, result:{…}} を、result の欄＋message の平らな JSON にする。</summary>
        private static string FlattenAddonResult(string json, out int[] master, out ulong[] ids)
        {
            master = null;
            ids    = null;
            var flat = new Newtonsoft.Json.Linq.JObject();
            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
                if (o["result"] is Newtonsoft.Json.Linq.JObject r)
                {
                    foreach (var p in r.Properties())
                    {
                        if (p.Value is Newtonsoft.Json.Linq.JArray arr)
                        {
                            var parts = new List<string>();
                            foreach (var e in arr) parts.Add(e.Type == Newtonsoft.Json.Linq.JTokenType.String
                                ? (string)e : e.ToString(Newtonsoft.Json.Formatting.None));
                            flat[p.Name] = string.Join(",", parts);
                        }
                        else if (p.Value is Newtonsoft.Json.Linq.JValue v)
                        {
                            flat[p.Name] = v.Type == Newtonsoft.Json.Linq.JTokenType.Boolean
                                ? ((bool)v ? "true" : "false")
                                : Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        }
                    }
                }
                flat["message"] = (string)o["message"] ?? "";
            }
            catch (Exception e)
            {
                flat["message"] = "返事を読めません: " + e.Message;
            }

            var vals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in flat.Properties()) vals[p.Name] = (string)p.Value;
            master = OutputInts(vals, "masterIndices") ?? OutputInts(vals, "masterIndex");
            ids    = OutputIds(vals, "objectIds");
            return flat.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ================================================================
        // 報告
        // ================================================================

        private void ReportScenarioRun(ScenarioRunState run, int logStart, bool allLog)
        {
            var step  = run.CurrentStep;
            var group = run.CurrentGroup;

            var lines = new List<string>();
            int from  = allLog ? 0 : Math.Max(0, logStart);
            for (int i = from; i < run.LogLines.Count; i++) lines.Add(run.LogLines[i]);

            var route = run.Stop == ScenarioRunStop.Finished ? null : run.CurrentRoute;

            var b = CommandDataJson.New();
            if (run.Stop == ScenarioRunStop.Finished && run.OutputsJson != null)
                b.Text("outputs", run.OutputsJson);
            ReportData(b
                .Text ("routeName",        route?.Name ?? "")
                .Texts("routeItems",       route != null ? new List<string>(route.Items) : new List<string>())
                .Text ("routeNote",        route?.Note ?? "")
                .Flag ("awaitUser",        run.AwaitUserAction != null)
                .Text ("rootName",         run.RootName)
                .Text ("stop",             StopWord(run.Stop))
                .Text ("stopMessage",      run.StopMessage ?? "")
                .Text ("scenario",         run.CurrentScenario)
                .Text ("scenarioItemId",        step?.ScenarioItemId ?? "")
                .Int  ("stepNumber",       step != null ? run.CurrentIndex + 1 : 0)
                .Int  ("stepCount",        group?.StepCount ?? 0)
                .Text ("stepKind",         step != null ? step.Kind.ToString() : "")
                .Text ("purpose",          step?.Purpose ?? "")
                .Text ("usageScene",       step?.UsageScene ?? "")
                .Int  ("executedCommands", run.ExecutedCommands)
                .Texts(allLog ? "log" : "newLog", lines)
                .Build());
        }

        private static string StopWord(ScenarioRunStop s)
        {
            switch (s)
            {
                case ScenarioRunStop.Judgment: return "judgment";
                case ScenarioRunStop.Failed:   return "failed";
                case ScenarioRunStop.Finished: return "finished";
                case ScenarioRunStop.Waiting:  return "waiting";
                default:                       return "none";
            }
        }

        private static string KindWord(ObjectGroupStepKind k)
        {
            switch (k)
            {
                case ObjectGroupStepKind.Instruction: return "指示";
                case ObjectGroupStepKind.Observe:     return "確認";
                case ObjectGroupStepKind.Note:        return "注意";
                case ObjectGroupStepKind.ScenarioRef: return "別のシナリオ";
                default:                              return "実行";
            }
        }

        private static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
