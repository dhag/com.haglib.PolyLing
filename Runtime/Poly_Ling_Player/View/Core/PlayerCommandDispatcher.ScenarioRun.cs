// PlayerCommandDispatcher.ScenarioRun.cs
// 手本（シナリオ）を上から順に流す。runScenario / continueScenario / queryScenarioRun / stopScenarioRun。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【止まる所】
//   判断が要る段（ObjectGroupStep.RequiresJudgment：指示・確認）と、失敗した段。
//   注意の段は読み飛ばし、別の手本を呼ぶ段では呼ばれた手本をその場で流す。
//   止める条件は ObjectGroupStep.RequiresJudgment の 1 か所だけを見る。
//
// 【途中の段だけを撃つ口を置かない】
//   @prev は「直前に実行した段」、@<段の名前> は「その段の結果」を指すので、
//   前の段を飛ばして途中を撃つと、別の流れで残った値を黙って掴む。
//   流れは先頭から始めるか、止まった所から続けるかの 2 通りだけにする。
//
// 【控えは流れ 1 回ぶんに閉じる】
//   直前の段の対象・戻り値と、段ごとの戻り値は ScenarioRunState が持つ。
//   runScenario のたびに作り直すので、前の流れの結果は混ざらない。
//
// 【1 回に進める段数】
//   パネルは maxSteps=1 で 1 段ずつ呼び、呼ぶたびに画面を描き直す。
//   まとめて流すと終わるまで画面が固まり、何が起きているか見えない。
//   MCP からは省いて（0）止まるまで流す。
//
// 【手本は流し始めに写す】
//   流している途中で scenarios.csv が書き換わっても、流れは写しを使う。
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
        /// <summary>止まっていない（段数の上限で一旦返しただけ）。</summary>
        None,
        /// <summary>指示・確認の段で、判断を待っている。</summary>
        Judgment,
        /// <summary>段が失敗した。</summary>
        Failed,
        /// <summary>最後の段まで終わった。</summary>
        Finished,
    }

    /// <summary>流れの中での段の状態。</summary>
    public enum ScenarioStepRunStatus
    {
        NotRun,
        /// <summary>別の手本を呼ぶ段で、呼ばれた手本を流している最中。</summary>
        Running,
        Done,
        /// <summary>判断を待って止まっている。</summary>
        Stopped,
        Failed,
    }

    /// <summary>手本を流している途中の状態。1 回の流れにつき 1 つ。</summary>
    public sealed class ScenarioRunState
    {
        internal sealed class Frame
        {
            public string      Name;
            public ObjectGroup Group;
            public int         Index;
        }

        internal readonly List<Frame> Stack = new List<Frame>();

        internal readonly Dictionary<string, ScenarioStepRunStatus> StatusMap
            = new Dictionary<string, ScenarioStepRunStatus>(StringComparer.Ordinal);

        /// <summary>段ごとの戻り値。鍵は「手本名/段の名前」。</summary>
        internal readonly Dictionary<string, (int[] Master, ulong[] Ids, string Data)> Results
            = new Dictionary<string, (int[], ulong[], string)>(StringComparer.Ordinal);

        /// <summary>直前に実行した段の対象と戻り値。</summary>
        internal int[]   PrevMaster;
        internal ulong[] PrevIds;
        internal string  PrevData;

        /// <summary>判断待ちで止まった段を、次の進行で越えてよいか。</summary>
        internal bool PassJudgment;

        /// <summary>次に実行するコマンドの段で差し替える引数。</summary>
        internal string[] OverrideKeys;
        internal string[] OverrideValues;

        internal readonly List<string> LogLines = new List<string>();

        private const int LogLimit = 1000;

        public string          RootName         { get; internal set; } = "";
        public ScenarioRunStop Stop             { get; internal set; } = ScenarioRunStop.None;
        public string          StopMessage      { get; internal set; } = "";

        /// <summary>止まった段の種別（判断待ちのとき、指示か確認かを表示で分けるため）。</summary>
        public ObjectGroupStepKind StopStepKind { get; internal set; } = ObjectGroupStepKind.Command;

        public int             ExecutedCommands { get; internal set; }

        /// <summary>いま居る手本の名前。呼び出し中の手本があればそちら。</summary>
        public string CurrentScenario => Top?.Name ?? "";

        /// <summary>いま居る手本（写し）。</summary>
        public ObjectGroup CurrentGroup => Top?.Group;

        /// <summary>いま居る段の位置。0 始まり。終わっていれば段数と同じ。</summary>
        public int CurrentIndex => Top?.Index ?? -1;

        /// <summary>いま居る段。無ければ null。</summary>
        public ObjectGroupStep CurrentStep
        {
            get
            {
                var f = Top;
                if (f?.Group?.Steps == null) return null;
                return (f.Index >= 0 && f.Index < f.Group.Steps.Count) ? f.Group.Steps[f.Index] : null;
            }
        }

        /// <summary>呼び出しの積み重なり。先頭が流し始めた手本。</summary>
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

        public ScenarioStepRunStatus StatusOf(string scenario, string elementId)
            => StatusMap.TryGetValue(Key(scenario, elementId), out var s) ? s : ScenarioStepRunStatus.NotRun;

        internal Frame Top => Stack.Count > 0 ? Stack[Stack.Count - 1] : null;

        internal static string Key(string scenario, string elementId)
            => (scenario ?? "") + "/" + (elementId ?? "");

        internal void SetStatus(string scenario, string elementId, ScenarioStepRunStatus s)
            => StatusMap[Key(scenario, elementId)] = s;

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
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            var run = new ScenarioRunState { RootName = g.Name ?? "" };
            run.Stack.Add(new ScenarioRunState.Frame { Name = g.Name ?? "", Group = g, Index = 0 });
            run.AddLog($"「{g.Name}」を先頭から流す（{g.StepCount} 段）");
            _scenarioRun = run;

            int logStart = run.LogLines.Count - 1;
            AdvanceScenarioRun(run, cmd.ModelIndex, cmd.MaxSteps);
            ReportScenarioRun(run, logStart, allLog: false);
        }

        private void RunContinueScenario(ContinueScenarioCommand cmd)
        {
            var run = _scenarioRun;
            if (run == null) { Fail("流している手本がありません。runScenario で始めてください"); return; }
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

            // 判断待ちで止まっていたなら、その段を越える。失敗ならやり直す（位置は動かさない）。
            if (run.Stop == ScenarioRunStop.Judgment) run.PassJudgment = true;
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
                    .Text("elementId",        "")
                    .Int ("stepNumber",       0)
                    .Int ("stepCount",        0)
                    .Text("stepKind",         "")
                    .Text("purpose",          "")
                    .Int ("executedCommands", 0)
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
                        elem = fr.Group.Steps[fr.Index]?.ElementId ?? "";
                }
            }

            _scenarioRun = null;
            ReportData(CommandDataJson.New()
                .Flag("stopped",          had)
                .Text("rootName",         root)
                .Text("scenario",         scen)
                .Text("elementId",        elem)
                .Int ("stepNumber",       stepNo)
                .Int ("executedCommands", executed)
                .Build());
        }

        // ================================================================
        // 進める
        // ================================================================

        /// <summary>
        /// 止まるか、段数の上限に達するか、終わるまで段を処理する。
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
                    run.StopMessage = "最後の段まで終わった";
                    run.AddLog($"「{run.RootName}」を最後まで流した（実行したコマンド {run.ExecutedCommands} 本）");
                    return;
                }

                var steps = frame.Group.Steps;

                // この手本を流し終えた。呼び出し元へ戻り、呼んだ段を済みにする。
                if (steps == null || frame.Index >= steps.Count)
                {
                    run.Stack.RemoveAt(run.Stack.Count - 1);

                    var parent = run.Top;
                    if (parent != null)
                    {
                        var refStep = parent.Group.Steps[parent.Index];
                        run.SetStatus(parent.Name, refStep.ElementId, ScenarioStepRunStatus.Done);
                        run.AddLog($"「{frame.Name}」が終わった。「{parent.Name}」へ戻る");
                        parent.Index++;
                    }
                    continue;
                }

                if (maxSteps > 0 && processed >= maxSteps) return;

                var step  = steps[frame.Index];
                string at = $"[{frame.Name} {frame.Index + 1}/{steps.Count} {step.ElementId}]";

                // ── 指示・確認：判断を待つ ──
                if (step.RequiresJudgment)
                {
                    if (run.PassJudgment)
                    {
                        run.PassJudgment = false;
                        run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Done);
                        run.AddLog($"{at} {KindWord(step.Kind)}を済ませて先へ進む");
                        frame.Index++;
                        processed++;
                        continue;
                    }

                    run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Stopped);
                    run.Stop         = ScenarioRunStop.Judgment;
                    run.StopStepKind = step.Kind;
                    run.StopMessage  = step.Purpose ?? "";
                    run.AddLog($"{at} {KindWord(step.Kind)}で止まった: {step.Purpose}");
                    return;
                }

                // 判断待ちを越える印は、その段で使わなければ捨てる。
                run.PassJudgment = false;

                // ── 注意：読むだけ ──
                if (step.Kind == ObjectGroupStepKind.Note)
                {
                    run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Done);
                    run.AddLog($"{at} 注意: {step.Purpose}");
                    frame.Index++;
                    processed++;
                    continue;
                }

                // ── 別の手本を呼ぶ ──
                if (step.IsScenarioRef)
                {
                    string refName = step.RefName ?? "";
                    var child = ScenarioLibrary.Get(refName);
                    if (child == null)
                    {
                        FailRunStep(run, frame, step, at, $"呼ぶ手本がありません: {refName}");
                        return;
                    }
                    foreach (var f in run.Stack)
                    {
                        if (!string.Equals(f.Name, refName, StringComparison.Ordinal)) continue;
                        FailRunStep(run, frame, step, at, $"「{refName}」は呼び出しの途中にあるので、ここで呼ぶと終わらない");
                        return;
                    }

                    run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Running);
                    run.AddLog($"{at} 別の手本「{refName}」を流す（{child.StepCount} 段）");
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
                run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Done);
                run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 成功（{step.Action}）");
                frame.Index++;
                processed++;
            }
        }

        private static void FailRunStep(
            ScenarioRunState run, ScenarioRunState.Frame frame, ObjectGroupStep step, string at, string reason)
        {
            run.SetStatus(frame.Name, step.ElementId, ScenarioStepRunStatus.Failed);
            run.Stop         = ScenarioRunStop.Failed;
            run.StopStepKind = step.Kind;
            run.StopMessage  = reason ?? "";
            run.AddLog($"{at} {Or(step.Purpose, step.Action)} → 失敗: {reason}");
        }

        /// <summary>
        /// コマンドの段を 1 つ実行する。引数は手本のものに、continueScenario で渡した差し替えを重ね、
        /// @prev / @&lt;段の名前&gt; を流れの控えから直してから組み立てる。
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
            run.Results[ScenarioRunState.Key(scenarioName, step.ElementId)] =
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

            ReportData(CommandDataJson.New()
                .Text ("rootName",         run.RootName)
                .Text ("stop",             StopWord(run.Stop))
                .Text ("stopMessage",      run.StopMessage ?? "")
                .Text ("scenario",         run.CurrentScenario)
                .Text ("elementId",        step?.ElementId ?? "")
                .Int  ("stepNumber",       step != null ? run.CurrentIndex + 1 : 0)
                .Int  ("stepCount",        group?.StepCount ?? 0)
                .Text ("stepKind",         step != null ? step.Kind.ToString() : "")
                .Text ("purpose",          step?.Purpose ?? "")
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
                case ObjectGroupStepKind.ScenarioRef: return "別の手本";
                default:                              return "実行";
            }
        }

        private static string Or(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
