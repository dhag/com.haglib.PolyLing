// PlayerCommandDispatcher.Scenario.cs
// 手本（シナリオ）コマンド（PanelCommand.Scenario.cs）の振り分けと実処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【プロジェクトの null 門より前で捌く】
//   手本は ScenarioLibrary が持ち、プロジェクトにもモデルにも属さない。
//   DispatchCore の _getProject() より前で DispatchScenario を呼ぶ
//   （QueryCommandAuditCommand・UI 自動操作と同じ扱い）。
//   何も読み込んでいない状態でも手本を作れる。
//
// 【受け口を置かない】
//   UI 自動操作は Viewer 側に実体（UiAutomationService）があるので
//   Func<T, CommandResult> のフックを通すが、ScenarioLibrary は
//   UnityEngine 以外に何も要らない静的クラスで、Viewer を経由する理由がない。
//   QueryCommandAuditCommand と同じく、ここで直に処理して ReportData する。
//
// 【手本は実体に縛らない】
//   saveScenarioFromGroup は MeshRefIds と OutputObjectIds を落としてから登録する。
//   焼き付けると、そのモデルを閉じた時点で死んだ ObjectId が手本に残る。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>手本コマンドなら処理して true を返す。</summary>
        private bool DispatchScenario(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryScenariosCommand c:        RunQueryScenarios(c);        return true;
                case DescribeScenarioCommand c:      RunDescribeScenario(c);      return true;
                case CreateScenarioCommand c:        RunCreateScenario(c);        return true;
                case DeleteScenarioCommand c:        RunDeleteScenario(c);        return true;
                case ForkScenarioCommand c:          RunForkScenario(c);          return true;
                case SaveScenarioFromGroupCommand c: RunSaveScenarioFromGroup(c); return true;
                case SetScenarioMetaCommand c:       RunSetScenarioMeta(c);       return true;
                case AddScenarioStepCommand c:       RunAddScenarioStep(c);       return true;
                case SetScenarioStepCommand c:       RunSetScenarioStep(c);       return true;
                case RemoveScenarioStepCommand c:    RunRemoveScenarioStep(c);    return true;
                case SetScenarioStepArgCommand c:    RunSetScenarioStepArg(c);    return true;
                case MoveScenarioStepCommand c:      RunMoveScenarioStep(c);      return true;
                case ExpandScenarioRefCommand c:     RunExpandScenarioRef(c);     return true;
                case RunScenarioCommand c:           RunRunScenario(c);           return true;
                case ContinueScenarioCommand c:      RunContinueScenario(c);      return true;
                case QueryScenarioRunCommand c:      RunQueryScenarioRun(c);      return true;
                case StopScenarioRunCommand c:       RunStopScenarioRun(c);       return true;
                case StartScenarioRecordingCommand c: RunStartScenarioRecording(c); return true;
                case StopScenarioRecordingCommand c:  RunStopScenarioRecording(c);  return true;
                case QueryScenarioAuditCommand c:     RunQueryScenarioAudit(c);     return true;
                default: return false;
            }
        }

        // ================================================================
        // 記録する（ScenarioRecorder）
        // ================================================================

        /// <summary>
        /// Dispatch の一番外側で捌いたコマンドが、記録の対象外の口（手本・UI 自動操作・
        /// コマンド定義の検査）で処理されたか。Dispatch が一番外側に入るたびに下ろす。
        /// </summary>
        private bool _dispatchNotRecorded;

        /// <summary>今の一番外側のコマンドを記録の対象外にする。入れ子からは立てない。</summary>
        private void MarkNotRecorded()
        {
            if (_dispatchDepth == 1) _dispatchNotRecorded = true;
        }

        private void RunStartScenarioRecording(StartScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.Start(out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Flag("recording", true)
                .Build());
        }

        private void RunStopScenarioRecording(StopScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.IsRecording) { Fail("記録していません"); return; }

            if (cmd.Discard)
            {
                ScenarioRecorder.Discard();
                ReportData(CommandDataJson.New()
                    .Text("name",      "")
                    .Int ("steps",     0)
                    .Int ("count",     ScenarioLibrary.Count)
                    .Flag("discarded", true)
                    .Build());
                return;
            }

            if (!ScenarioRecorder.Stop(cmd.Name, cmd.Goal, cmd.Overwrite, out int steps, out string error))
            { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      cmd.Name)
                .Int ("steps",     steps)
                .Int ("count",     ScenarioLibrary.Count)
                .Flag("discarded", false)
                .Build());
        }

        // ================================================================
        // 点検する
        // ================================================================

        /// <summary>
        /// 手本の段を点検する。
        ///
        /// 【見るもの】
        ///   literalMeshIndex … IsMeshRef の印が付いた引数に 0 以上の索引が直に入っている。
        ///                      索引は描画オブジェクトの増減でずれるので、撃ち直すと別物を指す。
        ///   unknownAction    … action を型へ解決できない。
        ///   badRef / missingRef / forwardRef
        ///                    … @&lt;段の名前&gt;.&lt;キー&gt; の書き方違い・存在しない段・後ろの段。
        ///                      判定は TryExpandPrev と同じ規則で行う（あちらが読めない値を指摘する）。
        ///
        /// 【見ないもの】
        ///   頂点・面の番号。印が無いので、ここでは数値か索引かを区別できない。
        /// </summary>
        private void RunQueryScenarioAudit(QueryScenarioAuditCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            var elementIds = new List<string>();
            var issueKinds = new List<string>();
            var keys       = new List<string>();
            var details    = new List<string>();

            void Add(string id, string kind, string key, string detail)
            {
                elementIds.Add(id ?? "");
                issueKinds.Add(kind);
                keys.Add(key ?? "");
                details.Add(detail ?? "");
            }

            for (int i = 0; i < g.StepCount; i++)
            {
                var step = g.Steps[i];
                if (step == null) continue;
                string id = step.ElementId ?? "";

                if (step.IsExecutable)
                {
                    Type t = PanelCommandFactory.ResolveType(step.Action);
                    if (t == null)
                    {
                        Add(id, "unknownAction", "", $"action {step.Action} を解決できない");
                    }
                    else
                    {
                        foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
                        {
                            string v = step.GetArg(mr.Key);
                            if (string.IsNullOrEmpty(v) || v[0] == '@') continue;
                            if (!HasNonNegativeIndex(v)) continue;

                            Add(id, "literalMeshIndex", mr.Key,
                                $"索引 {v} が直に入っている。名前から引く照会（selectDrawablesByName など）と @ 参照に置き換える");
                        }
                    }
                }

                if (step.Args == null) continue;

                foreach (var kv in step.SortedArgs())
                {
                    string v = kv.Value;
                    if (string.IsNullOrEmpty(v) || v.Length < 2 || v[0] != '@') continue;
                    if (v.StartsWith(PrevPrefix, StringComparison.Ordinal)) continue;

                    int dot = v.IndexOf('.');
                    if (dot <= 1)
                    {
                        Add(id, "badRef", kv.Key,
                            $"{v} は @ で始まるので、流したとき参照として読まれるが、@prev.<キー> か @<段の名前>.<キー> の形になっていない");
                        continue;
                    }

                    string refId = v.Substring(1, dot - 1);
                    int at = g.IndexOfStep(refId);
                    if (at < 0)
                        Add(id, "missingRef", kv.Key, $"{v} が指す段 {refId} がこの手本に無い");
                    else if (at >= i)
                        Add(id, "forwardRef", kv.Key, $"{v} が指す段 {refId} はこの段より後ろにある");
                }
            }

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name ?? "")
                .Int  ("issues",     elementIds.Count)
                .Texts("elementIds", elementIds)
                .Texts("issueKinds", issueKinds)
                .Texts("keys",       keys)
                .Texts("details",    details)
                .Build());
        }

        /// <summary>カンマ区切りの整数列に 0 以上の値が 1 つでもあるか。-1 は「対象なし」なので数えない。</summary>
        private static bool HasNonNegativeIndex(string csv)
        {
            foreach (var part in csv.Split(','))
            {
                if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0)
                    return true;
            }
            return false;
        }

        // ================================================================
        // 読む
        // ================================================================

        private void RunQueryScenarios(QueryScenariosCommand cmd)
        {
            if (cmd.Reload) ScenarioLibrary.Reload();

            var all        = ScenarioLibrary.GetAll();
            var names      = new List<string>(all.Count);
            var goals      = new List<string>(all.Count);
            var stepCounts = new List<int>(all.Count);

            foreach (var g in all)
            {
                // query の照合はコマンド検索と同じ方式（PanelCommandSchemaIndex.ScoreText）。
                // 空の query は全部通る。
                if (PanelCommandFactory.ScoreText(cmd.Query, g.Name, ScenarioHaystack(g)) <= 0f) continue;

                names.Add(g.Name ?? "");
                goals.Add(g.Goal ?? "");
                stepCounts.Add(g.StepCount);
            }

            ReportData(CommandDataJson.New()
                .Int  ("count",      names.Count)
                .Text ("storePath",  ScenarioLibrary.StorePath)
                .Texts("names",      names)
                .Texts("goals",      goals)
                .Ints ("stepCounts", stepCounts)
                .Build());
        }

        /// <summary>手本の検索で照合する本文。目的・札・満たすべきこと・段の目的とコマンド名。</summary>
        private static string ScenarioHaystack(ObjectGroup g)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(g.Goal ?? "").Append('\n');
            if (g.Tags != null)            foreach (var t in g.Tags)            sb.Append(t).Append('\n');
            if (g.Preconditions != null)   foreach (var t in g.Preconditions)   sb.Append(t).Append('\n');
            if (g.SuccessCriteria != null) foreach (var t in g.SuccessCriteria) sb.Append(t).Append('\n');
            if (g.Steps != null)
                foreach (var s in g.Steps)
                {
                    if (s == null) continue;
                    sb.Append(s.Action ?? "").Append('\n').Append(s.Purpose ?? "").Append('\n');
                }
            return sb.ToString();
        }

        private void RunDescribeScenario(DescribeScenarioCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            // 段の並び。resolve のときは参照を辿って平たくしたもの。
            var ordered = new List<ObjectGroupStep>();
            var owners  = new List<string>();
            var depths  = new List<int>();

            if (cmd.Resolve)
            {
                if (!ScenarioLibrary.TryFlatten(cmd.Name, out var flat, out string flatError))
                { Fail(flatError); return; }

                foreach (var f in flat)
                {
                    ordered.Add(f.Step);
                    owners.Add(f.ScenarioName ?? "");
                    depths.Add(f.Depth);
                }
            }
            else if (g.Steps != null)
            {
                foreach (var step in g.Steps)
                    if (step != null) ordered.Add(step);
            }

            var elementIds = new List<string>();
            var kinds      = new List<string>();
            var actions    = new List<string>();
            var purposes   = new List<string>();
            var refNames   = new List<string>();
            var policies   = new List<string>();
            var argCounts  = new List<int>();
            var argKeys    = new List<string>();
            var argValues  = new List<string>();

            foreach (var step in ordered)
            {
                elementIds.Add(step.ElementId ?? "");
                kinds.Add(step.Kind.ToString());
                actions.Add(step.Action ?? "");
                purposes.Add(step.Purpose ?? "");
                refNames.Add(step.IsScenarioRef ? (step.RefName ?? "") : "");
                policies.Add(step.IsScenarioRef ? step.ExpansionPolicy.ToString() : "");

                var sorted = step.SortedArgs();
                argCounts.Add(sorted.Count);
                foreach (var kv in sorted)
                {
                    argKeys.Add(kv.Key ?? "");
                    argValues.Add(kv.Value ?? "");
                }
            }

            var prov = g.Provenance ?? new ObjectGroupProvenance();

            ReportData(CommandDataJson.New()
                .Text ("name",              g.Name ?? "")
                .Text ("goal",              g.Goal ?? "")
                .Text ("parentName",        prov.ParentName ?? "")
                .Text ("changeSummary",     prov.ChangeSummary ?? "")
                .Text ("createdBy",         prov.CreatedBy ?? "")
                .Int  ("steps",             ordered.Count)
                .Texts("preconditions",     g.Preconditions)
                .Texts("successCriteria",   g.SuccessCriteria)
                .Texts("tags",              g.Tags)
                .Texts("elementIds",        elementIds)
                .Texts("kinds",             kinds)
                .Texts("actions",           actions)
                .Texts("purposes",          purposes)
                .Texts("refNames",          refNames)
                .Texts("expansionPolicies", policies)
                .Texts("scenarioNames",     owners)
                .Ints ("depths",            depths)
                .Ints ("argCounts",         argCounts)
                .Texts("argKeys",           argKeys)
                .Texts("argValues",         argValues)
                .Build());
        }

        // ================================================================
        // 作る・消す
        // ================================================================

        private void RunCreateScenario(CreateScenarioCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.Name)) { Fail("手本の名前が空です"); return; }

            var g = new ObjectGroup(cmd.Name) { Goal = cmd.Goal ?? "" };
            g.Steps.Clear();   // 段は addScenarioStep で足す

            if (!ScenarioLibrary.Register(g, cmd.Overwrite, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Int ("count",     ScenarioLibrary.Count)
                .Text("storePath", ScenarioLibrary.StorePath)
                .Build());
        }

        private void RunDeleteScenario(DeleteScenarioCommand cmd)
        {
            if (!ScenarioLibrary.Remove(cmd.Name, out string removeError)) { Fail(removeError); return; }

            ReportData(CommandDataJson.New()
                .Flag("removed", true)
                .Int ("count",   ScenarioLibrary.Count)
                .Build());
        }

        private void RunForkScenario(ForkScenarioCommand cmd)
        {
            var src = ScenarioLibrary.Get(cmd.SourceName);
            if (src == null) { Fail($"手本がありません: {cmd.SourceName}"); return; }
            if (string.IsNullOrEmpty(cmd.NewName)) { Fail("複製の名前が空です"); return; }

            src.Name = cmd.NewName;
            src.Provenance = new ObjectGroupProvenance
            {
                ParentName    = cmd.SourceName,
                ChangeSummary = cmd.ChangeSummary ?? "",
                CreatedBy     = src.Provenance?.CreatedBy ?? "",
            };

            if (!ScenarioLibrary.Register(src, cmd.Overwrite, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  src.Name)
                .Int ("steps", src.StepCount)
                .Int ("count", ScenarioLibrary.Count)
                .Build());
        }

        private void RunSaveScenarioFromGroup(SaveScenarioFromGroupCommand cmd)
        {
            // この 1 本だけは現在のモデルを読む。
            var model = _getProject()?.CurrentModel;
            if (model == null) { Fail("no current model"); return; }

            var group = model.FindObjectGroupByName(cmd.GroupName);
            if (group == null) { Fail($"オブジェクトグループがありません: {cmd.GroupName}"); return; }
            if (string.IsNullOrEmpty(cmd.ScenarioName)) { Fail("手本の名前が空です"); return; }

            var g = group.Clone();
            g.Name = cmd.ScenarioName;
            if (!string.IsNullOrEmpty(cmd.Goal)) g.Goal = cmd.Goal;

            // 実体への参照は落とす。手本は特定のオブジェクトに縛らない。
            g.StashObjectId = 0UL;
            g.SourceDigest  = "";
            g.AutoUpdate    = false;
            if (g.Steps != null)
            {
                foreach (var step in g.Steps)
                {
                    if (step == null) continue;
                    step.OutputObjectIds.Clear();
                    step.MeshRefIds.Clear();
                }
            }

            g.Provenance = new ObjectGroupProvenance
            {
                ParentName    = "",
                ChangeSummary = $"オブジェクトグループ {cmd.GroupName} から起こした",
                CreatedBy     = "",
            };

            if (!ScenarioLibrary.Register(g, cmd.Overwrite, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  g.Name)
                .Int ("steps", g.StepCount)
                .Int ("count", ScenarioLibrary.Count)
                .Build());
        }

        // ================================================================
        // 書き換える
        // ================================================================

        private void RunSetScenarioMeta(SetScenarioMetaCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            if (!string.IsNullOrEmpty(cmd.Goal)) g.Goal = cmd.Goal;

            if (cmd.Preconditions   != null && cmd.Preconditions.Length   > 0)
                g.Preconditions   = new List<string>(cmd.Preconditions);
            if (cmd.SuccessCriteria != null && cmd.SuccessCriteria.Length > 0)
                g.SuccessCriteria = new List<string>(cmd.SuccessCriteria);
            if (cmd.Tags            != null && cmd.Tags.Length            > 0)
                g.Tags            = new List<string>(cmd.Tags);

            if (g.Provenance == null) g.Provenance = new ObjectGroupProvenance();
            if (!string.IsNullOrEmpty(cmd.ChangeSummary)) g.Provenance.ChangeSummary = cmd.ChangeSummary;
            if (!string.IsNullOrEmpty(cmd.CreatedBy))     g.Provenance.CreatedBy     = cmd.CreatedBy;

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  g.Name)
                .Int ("steps", g.StepCount)
                .Build());
        }

        private void RunAddScenarioStep(AddScenarioStepCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            if (!TryBuildScenarioStep(
                    cmd.Kind, cmd.Action, cmd.Purpose, cmd.ArgKeys, cmd.ArgValues,
                    cmd.RefName, cmd.ExpansionPolicy,
                    out ObjectGroupStep step, out string reason))
            { Fail(reason); return; }

            if (!string.IsNullOrEmpty(cmd.ElementId))
            {
                if (g.IndexOfStep(cmd.ElementId) >= 0)
                { Fail($"その段の名前は既に使われています: {cmd.ElementId}"); return; }
                step.ElementId = cmd.ElementId;
            }

            if (!string.IsNullOrEmpty(cmd.AfterElementId) && !string.IsNullOrEmpty(cmd.BeforeElementId))
            { Fail("afterElementId と beforeElementId は同時に指定できません"); return; }

            if (!string.IsNullOrEmpty(cmd.BeforeElementId))
            {
                int at = g.IndexOfStep(cmd.BeforeElementId);
                if (at < 0) { Fail($"段がありません: {cmd.BeforeElementId}"); return; }

                g.Steps.Insert(at, step);
                g.EnsureElementIds();
            }
            else if (string.IsNullOrEmpty(cmd.AfterElementId))
            {
                g.AddStep(step);
            }
            else
            {
                int at = g.IndexOfStep(cmd.AfterElementId);
                if (at < 0) { Fail($"段がありません: {cmd.AfterElementId}"); return; }

                g.Steps.Insert(at + 1, step);
                g.EnsureElementIds();
            }

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("elementId", step.ElementId)
                .Int ("steps",     g.StepCount)
                .Build());
        }

        private void RunSetScenarioStep(SetScenarioStepCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ElementId);
            if (at < 0) { Fail($"段がありません: {cmd.ElementId}"); return; }

            if (!TryBuildScenarioStep(
                    cmd.Kind, cmd.Action, cmd.Purpose, cmd.ArgKeys, cmd.ArgValues,
                    cmd.RefName, cmd.ExpansionPolicy,
                    out ObjectGroupStep step, out string reason))
            { Fail(reason); return; }

            step.ElementId = cmd.ElementId;
            g.Steps[at] = step;

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("elementId", step.ElementId)
                .Int ("steps",     g.StepCount)
                .Build());
        }

        private void RunRemoveScenarioStep(RemoveScenarioStepCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ElementId);
            if (at < 0) { Fail($"段がありません: {cmd.ElementId}"); return; }

            g.Steps.RemoveAt(at);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  g.Name)
                .Int ("steps", g.StepCount)
                .Build());
        }

        /// <summary>
        /// 段の引数を 1 つだけ書く。
        ///
        /// addScenarioStep / setScenarioStep の argKeys / argValues は配列なので、
        /// MCP 経由ではカンマで割れる。値そのものにカンマを含む引数
        /// （masterIndices、点列、pivot など）はあちらでは書けないため、
        /// キーと値を単独の文字列で受けるこの口を通す。
        /// </summary>
        private void RunSetScenarioStepArg(SetScenarioStepArgCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ElementId);
            if (at < 0) { Fail($"段がありません: {cmd.ElementId}"); return; }

            var step = g.Steps[at];

            if (!step.IsExecutable)
            { Fail($"{cmd.ElementId} は {step.Kind} の段で、引数を持ちません"); return; }

            if (string.IsNullOrEmpty(cmd.Key)) { Fail("key が空です"); return; }

            if (cmd.Remove) step.Args.Remove(cmd.Key);
            else            step.SetArg(cmd.Key, cmd.Value ?? "");

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("elementId", step.ElementId ?? "")
                .Text("key",       cmd.Key)
                .Text("value",     cmd.Remove ? "" : (cmd.Value ?? ""))
                .Int ("args",      step.Args?.Count ?? 0)
                .Build());
        }

        /// <summary>
        /// 段を別の位置へ動かす。段の名前と中身は変えない。
        ///
        /// 消して足し直すと名前が変わり、他の段の Instruction が指す先を失う。
        /// 並びだけを変える口を分けてある。
        /// </summary>
        private void RunMoveScenarioStep(MoveScenarioStepCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            int from = g.IndexOfStep(cmd.ElementId);
            if (from < 0) { Fail($"段がありません: {cmd.ElementId}"); return; }

            bool hasBefore = !string.IsNullOrEmpty(cmd.BeforeElementId);
            bool hasAfter  = !string.IsNullOrEmpty(cmd.AfterElementId);

            if (hasBefore && hasAfter)
            { Fail("beforeElementId と afterElementId は同時に指定できません"); return; }

            if (!hasBefore && !hasAfter && !cmd.ToTop && !cmd.ToBottom)
            { Fail("beforeElementId / afterElementId / toTop / toBottom のどれかを指定してください"); return; }

            var step = g.Steps[from];
            g.Steps.RemoveAt(from);

            int to;
            if (hasBefore)
            {
                to = g.IndexOfStep(cmd.BeforeElementId);
                if (to < 0) { Fail($"段がありません: {cmd.BeforeElementId}"); return; }
            }
            else if (hasAfter)
            {
                int at = g.IndexOfStep(cmd.AfterElementId);
                if (at < 0) { Fail($"段がありません: {cmd.AfterElementId}"); return; }
                to = at + 1;
            }
            else
            {
                to = cmd.ToTop ? 0 : g.Steps.Count;
            }

            g.Steps.Insert(to, step);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            var ids = new List<string>(g.Steps.Count);
            foreach (var s in g.Steps) if (s != null) ids.Add(s.ElementId ?? "");

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name)
                .Text ("elementId",  step.ElementId ?? "")
                .Int  ("fromIndex",  from)
                .Int  ("toIndex",    to)
                .Texts("elementIds", ids)
                .Build());
        }

        /// <summary>参照段を参照先の段の列で置き換える。</summary>
        private void RunExpandScenarioRef(ExpandScenarioRefCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"手本がありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ElementId);
            if (at < 0) { Fail($"段がありません: {cmd.ElementId}"); return; }

            var refStep = g.Steps[at];
            if (!refStep.IsScenarioRef)
            { Fail($"{cmd.ElementId} は {refStep.Kind} の段で、参照段ではありません"); return; }

            var src = ScenarioLibrary.Get(refStep.RefName);
            if (src == null) { Fail($"参照先の手本がありません: {refStep.RefName}"); return; }

            // 参照先の段をそのまま写す。参照先がさらに参照段を持つ場合は
            // 参照段のまま入る。深い段を開くにはもう一度この口を使う。
            var inserted = new List<ObjectGroupStep>();
            var oldIds   = new List<string>();
            if (src.Steps != null)
            {
                foreach (var s in src.Steps)
                {
                    if (s == null) continue;
                    var copy = s.Clone();
                    oldIds.Add(copy.ElementId ?? "");
                    copy.ElementId = "";   // 名前は入れた先で振り直す
                    inserted.Add(copy);
                }
            }

            g.Steps.RemoveAt(at);
            g.Steps.InsertRange(at, inserted);
            g.EnsureElementIds();

            // 写した段どうしの @<段の名前> 参照を、振り直した名前へ付け替える。
            // 付け替えないと参照先での名前のまま残り、入れた先の別の段を指すか、無い段を指す。
            var rename = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < inserted.Count; i++)
                if (!string.IsNullOrEmpty(oldIds[i])) rename[oldIds[i]] = inserted[i].ElementId;

            foreach (var copy in inserted)
            {
                if (copy.Args == null) continue;
                var args = new List<KeyValuePair<string, string>>(copy.SortedArgs());
                foreach (var kv in args)
                {
                    string v = kv.Value;
                    if (string.IsNullOrEmpty(v) || v.Length < 2 || v[0] != '@' || v.StartsWith(PrevPrefix, StringComparison.Ordinal)) continue;
                    int dot = v.IndexOf('.');
                    if (dot <= 1) continue;
                    if (rename.TryGetValue(v.Substring(1, dot - 1), out string newId))
                        copy.SetArg(kv.Key, "@" + newId + v.Substring(dot));
                }
            }

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            var ids = new List<string>(inserted.Count);
            foreach (var s in inserted) ids.Add(s.ElementId);

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name)
                .Text ("refName",    refStep.RefName ?? "")
                .Int  ("inserted",   inserted.Count)
                .Int  ("steps",      g.StepCount)
                .Texts("elementIds", ids)
                .Build());
        }

        // ================================================================
        // 流す（PlayerCommandDispatcher.ScenarioRun.cs）
        // ================================================================

        // ================================================================
        // @prev / @<段の名前>
        // ================================================================

        // 控え（直前の段の対象・戻り値、段ごとの戻り値）は ScenarioRunState が持つ。
        // 流れ 1 回ぶんの器に閉じ込め、別の流れや別の手本の結果が混ざらないようにする。

        private const string PrevPrefix            = "@prev.";
        private const string PrevMasterIndicesToken = "@prev.masterIndices";
        private const string PrevObjectIdsToken     = "@prev.objectIds";

        /// <summary>
        /// 引数の値が @prev / @&lt;段の名前&gt; なら、流れの控えから実際の値へ直す。
        /// どちらでもなければそのまま返す。
        /// 控えが空のときは失敗にする。空文字を黙って入れると、対象なしで
        /// 実行されて原因が見えなくなる。
        /// </summary>
        private static bool TryExpandPrev(ScenarioRunState run, string value, string scenarioName, out string expanded, out string reason)
        {
            expanded = value;
            reason   = null;

            if (string.IsNullOrEmpty(value)) return true;

            if (string.Equals(value, PrevMasterIndicesToken, StringComparison.Ordinal))
            {
                if (run.PrevMaster == null || run.PrevMaster.Length == 0)
                { reason = $"{PrevMasterIndicesToken} を使いましたが、直前に実行した段の対象がありません"; return false; }

                var parts = new List<string>(run.PrevMaster.Length);
                foreach (int i in run.PrevMaster)
                    parts.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                expanded = string.Join(",", parts);
                return true;
            }

            if (string.Equals(value, PrevObjectIdsToken, StringComparison.Ordinal))
            {
                if (run.PrevIds == null || run.PrevIds.Length == 0)
                { reason = $"{PrevObjectIdsToken} を使いましたが、直前に実行した段の対象がありません"; return false; }

                expanded = string.Join(",", IdTexts(run.PrevIds));
                return true;
            }

            // @prev.<キー> は直前の段の戻り値から引く。
            // 照会が返す faceIndices / v1 / modelIndices などを次の段へ渡すため。
            // 対象（masterIndices / objectIds）は上で先に処理しており、
            // ここへは来ない。
            if (value.StartsWith(PrevPrefix, StringComparison.Ordinal))
            {
                string key = value.Substring(PrevPrefix.Length);
                if (string.IsNullOrEmpty(key))
                { reason = "@prev. の後ろにキーがありません"; return false; }

                if (string.IsNullOrEmpty(run.PrevData))
                { reason = $"{value} を使いましたが、直前に実行した段が戻り値を返していません"; return false; }

                if (!TryReadJsonValue(run.PrevData, key, out string got))
                { reason = $"{value} を使いましたが、直前の戻り値に {key} がありません"; return false; }

                expanded = got;
                return true;
            }

            // @<段の名前>.<キー> は名指しした段の戻り値から引く。
            // 照会と使用の間に別の段を挟めるので、2 本の値を渡すときに要る。
            if (value.Length > 1 && value[0] == '@')
            {
                int dot = value.IndexOf('.');
                if (dot <= 1)
                { reason = $"{value} の書き方が違います。@prev.<キー> か @<段の名前>.<キー>"; return false; }

                string id  = value.Substring(1, dot - 1);
                string k2  = value.Substring(dot + 1);
                if (string.IsNullOrEmpty(k2))
                { reason = $"{value} にキーがありません"; return false; }

                if (!run.Results.TryGetValue(StepResultKey(scenarioName, id), out var rec))
                { reason = $"{value} を使いましたが、この流れで段 {id} をまだ実行していません"; return false; }

                if (string.Equals(k2, "masterIndices", StringComparison.Ordinal))
                {
                    if (rec.Master == null || rec.Master.Length == 0)
                    { reason = $"{value} を使いましたが、段 {id} は対象を返していません"; return false; }

                    var parts = new List<string>(rec.Master.Length);
                    foreach (int i in rec.Master)
                        parts.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    expanded = string.Join(",", parts);
                    return true;
                }

                if (string.Equals(k2, "objectIds", StringComparison.Ordinal))
                {
                    if (rec.Ids == null || rec.Ids.Length == 0)
                    { reason = $"{value} を使いましたが、段 {id} は対象を返していません"; return false; }

                    expanded = string.Join(",", IdTexts(rec.Ids));
                    return true;
                }

                if (string.IsNullOrEmpty(rec.Data))
                { reason = $"{value} を使いましたが、段 {id} は戻り値を返していません"; return false; }

                if (!TryReadJsonValue(rec.Data, k2, out string v2))
                { reason = $"{value} を使いましたが、段 {id} の戻り値に {k2} がありません"; return false; }

                expanded = v2;
                return true;
            }

            return true;
        }

        /// <summary>段ごとの控えの鍵。手本が違えば同じ段の名前でもぶつからない。</summary>
        private static string StepResultKey(string scenarioName, string elementId)
            => (scenarioName ?? "") + "/" + (elementId ?? "");

        /// <summary>
        /// 戻り値の JSON から 1 つのキーを取り出し、引数に渡せる文字列にする。
        ///
        /// 配列はカンマ区切りへ潰す。TryParse 側が配列をカンマで割るので、
        /// これで faceIndices などをそのまま次の段の引数に入れられる。
        /// 入れ子の配列や連想配列は扱わない。
        ///
        /// CommandDataJson が作る平たい JSON だけを相手にするため、
        /// 汎用の JSON 構文解析はしない。深い入れ子が来たら失敗にして、
        /// 黙って誤った値を渡さない。
        /// </summary>
        private static bool TryReadJsonValue(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;

            string needle = "\"" + key + "\":";
            int at = json.IndexOf(needle, StringComparison.Ordinal);
            if (at < 0) return false;

            int p = at + needle.Length;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length) return false;

            // 配列
            if (json[p] == '[')
            {
                int end = json.IndexOf(']', p);
                if (end < 0) return false;

                string body = json.Substring(p + 1, end - p - 1);
                if (body.IndexOf('[') >= 0 || body.IndexOf('{') >= 0) return false;

                var parts = new List<string>();
                foreach (string raw in body.Split(','))
                {
                    string s = raw.Trim();
                    if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                        s = s.Substring(1, s.Length - 2);
                    parts.Add(s);
                }
                value = string.Join(",", parts);
                return true;
            }

            // 文字列
            if (json[p] == '"')
            {
                int end = json.IndexOf('"', p + 1);
                if (end < 0) return false;
                value = json.Substring(p + 1, end - p - 1);
                return true;
            }

            // 入れ子は扱わない
            if (json[p] == '{') return false;

            // 数と真偽
            int stop = p;
            while (stop < json.Length && json[stop] != ',' && json[stop] != '}' && json[stop] != ']') stop++;
            value = json.Substring(p, stop - p).Trim();
            return value.Length > 0;
        }

        /// <summary>安定 ID を 10 進の文字列にする。null は空の列。</summary>
        private static List<string> IdTexts(ulong[] ids)
        {
            var list = new List<string>(ids?.Length ?? 0);
            if (ids == null) return list;
            foreach (ulong id in ids)
                list.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return list;
        }

        // ================================================================
        // 段の組み立て
        // ================================================================

        /// <summary>
        /// 引数から段を 1 つ作る。実体への参照は持たせない。
        /// </summary>
        private static bool TryBuildScenarioStep(
            ObjectGroupStepKind kind, string action, string purpose,
            string[] argKeys, string[] argValues,
            string refName, ScenarioExpansionPolicy expansionPolicy,
            out ObjectGroupStep step, out string reason)
        {
            step   = null;
            reason = null;

            argKeys   = argKeys   ?? new string[0];
            argValues = argValues ?? new string[0];

            if (argKeys.Length != argValues.Length)
            {
                reason = $"argKeys が {argKeys.Length} 件、argValues が {argValues.Length} 件で長さが合いません";
                return false;
            }

            bool executable  = kind == ObjectGroupStepKind.Command;
            bool scenarioRef = kind == ObjectGroupStepKind.ScenarioRef;

            if (executable && string.IsNullOrEmpty(action))
            {
                reason = "Kind が Command の段には action が要ります";
                return false;
            }

            if (!executable && !string.IsNullOrEmpty(action))
            {
                reason = $"Kind が {kind} の段は実行しないので action を持てません";
                return false;
            }

            if (scenarioRef && string.IsNullOrEmpty(refName))
            {
                reason = "Kind が ScenarioRef の段には refName が要ります";
                return false;
            }

            if (!scenarioRef && !string.IsNullOrEmpty(refName))
            {
                reason = $"Kind が {kind} の段は refName を持てません";
                return false;
            }

            if (executable && PanelCommandFactory.ResolveType(action) == null)
            {
                reason = $"未対応の action: {action}";
                return false;
            }

            var made = new ObjectGroupStep
            {
                Kind            = kind,
                Action          = action ?? "",
                Purpose         = purpose ?? "",
                RefName         = scenarioRef ? refName : "",
                ExpansionPolicy = scenarioRef ? expansionPolicy : ScenarioExpansionPolicy.Reference,
            };

            for (int i = 0; i < argKeys.Length; i++)
            {
                if (string.IsNullOrEmpty(argKeys[i])) continue;
                made.SetArg(argKeys[i], argValues[i] ?? "");
            }

            step = made;
            return true;
        }
    }
}
