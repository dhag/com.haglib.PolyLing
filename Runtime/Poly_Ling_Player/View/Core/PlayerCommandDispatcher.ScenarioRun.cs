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

            var run = new ScenarioRunState { RootName = g.Name ?? "" };
            run.Stack.Add(new ScenarioRunState.Frame { Name = g.Name ?? "", Group = g, Index = 0 });
            run.AddLog($"「{g.Name}」を先頭から流す（{g.StepCount} 項目）");
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
                    run.Stack.RemoveAt(run.Stack.Count - 1);

                    var parent = run.Top;
                    if (parent != null)
                    {
                        var refStep = parent.Group.Steps[parent.Index];
                        run.SetStatus(parent.Name, refStep.ScenarioItemId, ScenarioItemRunStatus.Done);
                        run.AddLog($"「{frame.Name}」が終わった。「{parent.Name}」へ戻る");
                        parent.Index++;
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

                    run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Running);
                    run.AddLog($"{at} 別のシナリオ「{refName}」を流す（{child.StepCount} 項目）");
                    run.Stack.Add(new ScenarioRunState.Frame { Name = refName, Group = child, Index = 0 });
                    processed++;
                    continue;
                }

                // ── コマンドを実行する ──
                if (!TryExecuteRunStep(run, frame.Name, step, modelIndex, out string reason))
                {
                    FailRunStep(run, frame, step, at, reason);
                    return;
                }

                run.ExecutedCommands++;
                NotePrevRoute(run, step);
                run.SetStatus(frame.Name, step.ScenarioItemId, ScenarioItemRunStatus.Done);
                run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 成功（{step.Action}）");
                frame.Index++;
                processed++;
            }
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
        /// @prev / @&lt;項目 ID&gt; を流れの控えから直してから組み立てる。
        /// 成功したら控えを更新し、差し替えを使い切る。
        /// </summary>
        private bool TryExecuteRunStep(
            ScenarioRunState run, string scenarioName, ObjectGroupStep step, int modelIndex, out string reason)
        {
            reason = null;

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

            var keys = new List<string>(args.Keys);
            foreach (var key in keys)
            {
                if (!TryExpandPrev(run, args[key], scenarioName, out string expanded, out string why))
                { reason = why; return false; }
                args[key] = expanded;
            }

            var inner = PanelCommandFactory.Create(step.Action, modelIndex, args, out string createError);
            if (inner == null) { reason = $"{step.Action} を組み立てられません: {createError}"; return false; }

            var result = Dispatch(inner);
            if (result != null && !result.Success)
            { reason = $"{step.Action} が失敗しました: {result.Reason}"; return false; }

            run.OverrideKeys   = null;
            run.OverrideValues = null;

            run.PrevMaster = result?.MasterIndices;
            run.PrevIds    = result?.ObjectIds;
            run.PrevData   = result?.Data;
            run.Results[ScenarioRunState.Key(scenarioName, step.ScenarioItemId)] =
                (result?.MasterIndices, result?.ObjectIds, result?.Data);
            return true;
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

            ReportData(CommandDataJson.New()
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
