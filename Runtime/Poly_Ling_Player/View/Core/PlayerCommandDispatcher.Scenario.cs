// PlayerCommandDispatcher.Scenario.cs
// シナリオコマンド（PanelCommand.Scenario.cs）の振り分けと実処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【プロジェクトの null 門より前で捌く】
//   シナリオは ScenarioLibrary が持ち、プロジェクトにもモデルにも属さない。
//   DispatchCore の _getProject() より前で DispatchScenario を呼ぶ
//   （QueryCommandAuditCommand・UI 自動操作と同じ扱い）。
//   何も読み込んでいない状態でもシナリオを作れる。
//
// 【受け口を置かない】
//   UI 自動操作は Viewer 側に実体（UiAutomationService）があるので
//   Func<T, CommandResult> のフックを通すが、ScenarioLibrary は
//   UnityEngine 以外に何も要らない静的クラスで、Viewer を経由する理由がない。
//   QueryCommandAuditCommand と同じく、ここで直に処理して ReportData する。
//
// 【シナリオは実体に縛らない】
//   saveScenarioFromGroup は MeshRefIds と OutputObjectIds を落としてから登録する。
//   焼き付けると、そのモデルを閉じた時点で死んだ ObjectId がシナリオに残る。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>シナリオコマンドなら処理して true を返す。</summary>
        private bool DispatchScenario(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryScenariosCommand c:        RunQueryScenarios(c);        return true;
                case DescribeScenarioCommand c:      RunDescribeScenario(c);      return true;
                case CreateScenarioCommand c:        RunCreateScenario(c);        return true;
                case MoveScenariosCommand c:         RunMoveScenarios(c);         return true;
                case CreateScenarioFolderCommand c:  RunCreateScenarioFolder(c);  return true;
                case RenameScenarioFolderCommand c:  RunRenameScenarioFolder(c);  return true;
                case DeleteScenarioFolderCommand c:  RunDeleteScenarioFolder(c);  return true;
                case CreateScenarioFromFolderCommand c: RunCreateScenarioFromFolder(c); return true;
                case DeleteScenarioCommand c:        RunDeleteScenario(c);        return true;
                case ForkScenarioCommand c:          RunForkScenario(c);          return true;
                case SaveScenarioFromGroupCommand c: RunSaveScenarioFromGroup(c); return true;
                case SetScenarioMetaCommand c:       RunSetScenarioMeta(c);       return true;
                case AddScenarioItemCommand c:       RunAddScenarioItem(c);       return true;
                case SetScenarioItemCommand c:       RunSetScenarioItem(c);       return true;
                case RemoveScenarioItemCommand c:    RunRemoveScenarioItem(c);    return true;
                case SetScenarioItemArgCommand c:    RunSetScenarioItemArg(c);    return true;
                case SetScenarioParamCommand c:      RunSetScenarioParam(c);      return true;
                case RemoveScenarioParamCommand c:   RunRemoveScenarioParam(c);   return true;
                case SetScenarioOutputCommand c:     RunSetScenarioOutput(c);     return true;
                case RemoveScenarioOutputCommand c:  RunRemoveScenarioOutput(c);  return true;
                case MoveScenarioItemCommand c:      RunMoveScenarioItem(c);      return true;
                case ExpandScenarioRefCommand c:     RunExpandScenarioRef(c);     return true;
                case RunScenarioCommand c:           RunRunScenario(c);           return true;
                case ContinueScenarioCommand c:      RunContinueScenario(c);      return true;
                case QueryScenarioRunCommand c:      RunQueryScenarioRun(c);      return true;
                case StopScenarioRunCommand c:       RunStopScenarioRun(c);       return true;
                case StartScenarioRecordingCommand c: RunStartScenarioRecording(c); return true;
                case PauseScenarioRecordingCommand c:   RunPauseScenarioRecording(c);   return true;
                case ResumeScenarioRecordingCommand c:  RunResumeScenarioRecording(c);  return true;
                case StopScenarioRecordingCommand c:    RunStopScenarioRecording(c);    return true;
                case SaveScenarioRecordingCommand c:    RunSaveScenarioRecording(c);    return true;
                case DiscardScenarioRecordingCommand c: RunDiscardScenarioRecording(c); return true;
                case QueryScenarioRecordingCommand c:   RunQueryScenarioRecording(c);   return true;
                case QueryScenarioAuditCommand c:     RunQueryScenarioAudit(c);     return true;
                case ExportScenariosCommand c:        RunExportScenarios(c);        return true;
                case ImportScenariosCommand c:        RunImportScenarios(c);        return true;
                case SetRemoteScenarioImportCommand c: RunSetRemoteScenarioImport(c); return true;
                case SetExecutionLogCommand c:        RunSetExecutionLog(c);        return true;
                case BeginTaskCommand c:              RunBeginTask(c);              return true;
                case EndTaskCommand c:                RunEndTask(c);                return true;
                case QueryExecutionLogCommand c:      RunQueryExecutionLog(c);      return true;
                default: return false;
            }
        }

        // ================================================================
        // 実行記録（ExecutionLog.cs）
        // ================================================================

        /// <summary>実行記録の書き込み先。persistentDataPath はメインスレッドでしか読めないのでここで作る。</summary>
        private static string ExecutionLogDir()
            => System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "PolyLing", "runlogs");

        /// <summary>
        /// 入切の設定を覚えるキー（PlayerPrefs）。既定は入（2026-10-21 決定。1 件あたり約 0.2 ms）。
        /// setExecutionLog で変えると覚え、次の起動もその状態で始まる。
        /// </summary>
        private const string PrefExecutionLog = "PolyLing.ExecutionLog.Enabled";

        /// <summary>起動時に、覚えている入切（無ければ入）で実行記録を始める。ビューアがディスパッチャを作った直後に呼ぶ。</summary>
        public void ApplyExecutionLogPreference()
        {
            bool on = UnityEngine.PlayerPrefs.GetInt(PrefExecutionLog, 1) != 0;
            ExecutionLog.SetEnabled(on, ExecutionLogDir());
        }

        private void RunSetExecutionLog(SetExecutionLogCommand cmd)
        {
            ExecutionLog.SetEnabled(cmd.Enabled, ExecutionLogDir());
            UnityEngine.PlayerPrefs.SetInt(PrefExecutionLog, cmd.Enabled ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
            ReportData(CommandDataJson.New()
                .Flag("enabled", ExecutionLog.Enabled)
                .Text("dir",     ExecutionLog.RootDir)
                .Build());
        }

        private void RunBeginTask(BeginTaskCommand cmd)
        {
            if (!ExecutionLog.Enabled) { Fail("実行記録が切です。setExecutionLog enabled=true で入れてください"); return; }
            if (string.IsNullOrWhiteSpace(cmd.Goal)) { Fail("goal が空です"); return; }
            string id = ExecutionLog.BeginTask(cmd.TaskId, cmd.Goal, cmd.Conditions);
            ReportData(CommandDataJson.New().Text("taskId", id).Build());
        }

        private void RunEndTask(EndTaskCommand cmd)
        {
            string v = (cmd.Verdict ?? "").Trim().ToLowerInvariant();
            if (v != "success" && v != "fixed" && v != "failed")
            { Fail($"verdict は success / fixed / failed のどれかです: {cmd.Verdict}"); return; }
            if (!ExecutionLog.EndTask(v, cmd.Note, out string id)) { Fail("開いているタスクがありません"); return; }
            ReportData(CommandDataJson.New().Text("taskId", id).Build());
        }

        private void RunQueryExecutionLog(QueryExecutionLogCommand cmd)
        {
            var s = ExecutionLog.Snapshot();
            if (cmd.Reset) ExecutionLog.ResetStats();
            ReportData(CommandDataJson.New()
                .Flag("enabled",     s.Enabled)
                .Text("taskId",      s.TaskId ?? "")
                .Text("dir",         s.RootDir ?? "")
                .Int ("records",     (int)s.Records)
                .Int ("written",     (int)s.Written)
                .Int ("pending",     (int)s.Pending)
                .Num ("mainMsTotal", s.MainMsTotal)
                .Num ("mainMsPer",   s.Records > 0 ? s.MainMsTotal / s.Records : 0.0)
                .Text("lastError",   s.LastError ?? "")
                .Build());
        }

        /// <summary>実行記録の操作そのもの（記録しない）。</summary>
        private static bool IsExecutionLogControl(PanelCommand cmd)
            => cmd is SetExecutionLogCommand || cmd is BeginTaskCommand
            || cmd is EndTaskCommand || cmd is QueryExecutionLogCommand;

        // ================================================================
        // 別ファイルとの出し入れ
        // ================================================================

        // 【ファイルか本文か】
        //   どちらも ScenarioLibrary.TryExport / TryImport を通す。違うのは CSV をファイルに置くか
        //   戻り値・引数で運ぶかだけで、検査（全部か無しか・参照・循環・名前の重なり）は同じ。
        //   本文で運ぶ口は、作業フォルダへファイルを置けない経路（MCP の Player 接続・リモート接続）のため。

        private void RunExportScenarios(ExportScenariosCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.FilePath))
            {
                if (!ScenarioLibrary.TryExport(cmd.Names, out string text, out var textNames, out string textError)) { Fail(textError); return; }
                if (text.Length > ExportScenariosCommand.InlineCsvMaxChars)
                {
                    Fail($"CSV の本文が {text.Length} 文字で、戻り値に載せられる上限 {ExportScenariosCommand.InlineCsvMaxChars} 文字を超えます。"
                       + "names で書き出すシナリオを絞るか、filePath を渡してファイルへ書いてください");
                    return;
                }
                ReportData(CommandDataJson.New()
                    .Text ("csv",   text)
                    .Int  ("count", textNames.Count)
                    .Texts("names", textNames)
                    .Build());
                return;
            }

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(cmd.FilePath, out string path, out string reason)) { Fail(reason); return; }

            if (!ScenarioLibrary.TryExport(cmd.Names, out string csv, out var names, out string error)) { Fail(error); return; }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
            }
            catch (Exception e) { Fail($"書き出しに失敗しました: {e.Message}"); return; }

            ReportData(CommandDataJson.New()
                .Text ("path",  path)
                .Int  ("count", names.Count)
                .Texts("names", names)
                .Build());
        }

        private void RunImportScenarios(ImportScenariosCommand cmd)
        {
            bool hasPath = !string.IsNullOrEmpty(cmd.FilePath);
            bool hasText = !string.IsNullOrEmpty(cmd.CsvText);
            if (hasPath && hasText) { Fail("filePath と csvText はどちらか一方だけ渡してください"); return; }
            if (!hasPath && !hasText) { Fail("filePath か csvText のどちらかを渡してください"); return; }

            string[] lines;
            if (hasText)
            {
                lines = SplitLinesLikeReadAllLines(cmd.CsvText);
            }
            else
            {
                if (!Poly_Ling.Core.PLSandbox.TryResolveRead(cmd.FilePath, out string path, out string reason)) { Fail(reason); return; }
                if (!System.IO.File.Exists(path)) { Fail($"ファイルがありません: {cmd.FilePath}"); return; }

                try { lines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8); }
                catch (Exception e) { Fail($"読み込みに失敗しました: {e.Message}"); return; }
            }

            if (!ScenarioLibrary.TryImport(lines, cmd.Overwrite, out var added, out var replaced, out string error, cmd.Folder)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Texts("added",    added)
                .Texts("replaced", replaced)
                .Int  ("count",    ScenarioLibrary.Count)
                .Build());
        }

        /// <summary>
        /// 本文を File.ReadAllLines と同じ規則で行に割る。改行は \r\n / \n / \r、
        /// 末尾の改行は空行を作らない（StringReader.ReadLine が同じ規則）。
        /// ReadAllLines は UTF-8 の BOM を落とすので、先頭の U+FEFF も落とす。
        /// </summary>
        private static string[] SplitLinesLikeReadAllLines(string text)
        {
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var lines = new List<string>();
            using (var reader = new System.IO.StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            return lines.ToArray();
        }

        private void RunSetRemoteScenarioImport(SetRemoteScenarioImportCommand cmd)
        {
            Poly_Ling.Remote.RemoteOwnership.AllowRemoteScenarioImport = cmd.Enabled;
            ReportData(CommandDataJson.New()
                .Flag("enabled", Poly_Ling.Remote.RemoteOwnership.AllowRemoteScenarioImport)
                .Build());
        }

        // ================================================================
        // 記録する（ScenarioRecorder）
        // ================================================================

        /// <summary>
        /// Dispatch の一番外側で捌いたコマンドが、記録の対象外の口（シナリオ・UI 自動操作・
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

        private void RunPauseScenarioRecording(PauseScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.Pause(out string error)) { Fail(error); return; }
            ReportRecordingState();
        }

        private void RunResumeScenarioRecording(ResumeScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.Resume(out string error)) { Fail(error); return; }
            ReportRecordingState();
        }

        private void RunStopScenarioRecording(StopScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.End(out string error)) { Fail(error); return; }
            ReportRecordingState();
        }

        private void RunSaveScenarioRecording(SaveScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.Save(cmd.Name, cmd.Goal, cmd.Overwrite, out int steps, out string error,
                                       string.IsNullOrWhiteSpace(cmd.Folder) ? null : cmd.Folder))
            { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  cmd.Name)
                .Int ("steps", steps)
                .Int ("count", ScenarioLibrary.Count)
                .Build());
        }

        private void RunDiscardScenarioRecording(DiscardScenarioRecordingCommand cmd)
        {
            if (!ScenarioRecorder.HasDraft) { Fail("破棄する記録がありません"); return; }
            int steps = ScenarioRecorder.StepCount;
            ScenarioRecorder.Discard();
            ReportData(CommandDataJson.New()
                .Int("steps", steps)
                .Build());
        }

        private void RunQueryScenarioRecording(QueryScenarioRecordingCommand cmd)
        {
            ReportData(CommandDataJson.New()
                .Text("state",    ScenarioRecorder.StateId(ScenarioRecorder.State))
                .Int ("steps",    ScenarioRecorder.StepCount)
                .Flag("hasDraft", ScenarioRecorder.HasDraft)
                .Build());
        }

        private void ReportRecordingState()
        {
            ReportData(CommandDataJson.New()
                .Text("state", ScenarioRecorder.StateId(ScenarioRecorder.State))
                .Int ("steps", ScenarioRecorder.StepCount)
                .Build());
        }

        // ================================================================
        // 点検する
        // ================================================================

        /// <summary>
        /// シナリオの項目を点検する。
        ///
        /// 【見るもの】
        ///   literalMeshIndex … IsMeshRef の印が付いた引数に 0 以上の索引が直に入っている。
        ///                      索引は描画オブジェクトの増減でずれるので、撃ち直すと別物を指す。
        ///   unknownAction    … action を型へ解決できない。
        ///   badRef / missingRef / forwardRef
        ///                    … @&lt;項目 ID&gt;.&lt;キー&gt; の書き方違い・存在しない項目・後ろの項目。
        ///                      判定は TryExpandPrev と同じ規則で行う（あちらが読めない値を指摘する）。
        ///
        /// 【見ないもの】
        ///   頂点・面の番号。印が無いので、ここでは数値か索引かを区別できない。
        /// </summary>
        private void RunQueryScenarioAudit(QueryScenarioAuditCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            var scenarioItemIds = new List<string>();
            var issueKinds = new List<string>();
            var keys       = new List<string>();
            var details    = new List<string>();

            void Add(string id, string kind, string key, string detail)
            {
                scenarioItemIds.Add(id ?? "");
                issueKinds.Add(kind);
                keys.Add(key ?? "");
                details.Add(detail ?? "");
            }

            // ── 引数の宣言 ──
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var used     = new HashSet<string>(StringComparer.Ordinal);
            if (g.Params != null)
                foreach (var p in g.Params)
                {
                    if (p == null) continue;
                    if (!ScenarioParam.IsValidName(p.Name))
                        Add("", "badParamName", p.Name, $"引数の名前 {p.Name} は使えない（英数字と _、先頭は数字以外、64 文字まで）");
                    else if (!declared.Add(p.Name))
                        Add("", "duplicateParam", p.Name, $"引数 {p.Name} が 2 回以上宣言されている");
                }

            for (int i = 0; i < g.StepCount; i++)
            {
                var step = g.Steps[i];
                if (step == null) continue;
                string id = step.ScenarioItemId ?? "";

                // ── 引数の参照 ──
                Type stepType = step.IsExecutable ? PanelCommandFactory.ResolveType(step.Action) : null;
                if (step.Args != null)
                    foreach (var kv in step.SortedArgs())
                    {
                        string v = kv.Value;
                        if (string.IsNullOrEmpty(v)) continue;

                        // 文字列の配列は要素ごとに丸ごとの ${名前} を置き換える（流す側と同じ規則）。
                        if (PanelCommandFactory.IsStringArrayParam(stepType, kv.Key) && !ScenarioParam.TryParseRef(v, out _))
                        {
                            foreach (var part in PanelCommandFactory.SplitQuotedCsv(v))
                            {
                                if (ScenarioParam.TryParseRef(part, out string en))
                                {
                                    if (!declared.Contains(en)) Add(id, "unknownParam", kv.Key, $"{part} の引数 {en} が宣言されていない");
                                    else used.Add(en);
                                }
                                else if (part.IndexOf(ScenarioParam.RefOpen, StringComparison.Ordinal) >= 0)
                                {
                                    Add(id, "partialParamRef", kv.Key,
                                        $"{part} は ${{ を含むが、要素の丸ごとが ${{名前}} ではないので置き換わらない");
                                }
                            }
                            continue;
                        }

                        if (ScenarioParam.TryParseRef(v, out string pname))
                        {
                            if (!declared.Contains(pname))
                                Add(id, "unknownParam", kv.Key, $"{v} の引数 {pname} が宣言されていない");
                            else
                                used.Add(pname);
                        }
                        else if (v.IndexOf(ScenarioParam.RefOpen, StringComparison.Ordinal) >= 0)
                        {
                            Add(id, "partialParamRef", kv.Key,
                                $"{v} は ${{ を含むが、値の丸ごとが ${{名前}} ではないので置き換わらない");
                        }
                    }

                // 参照項目の Args は呼ぶシナリオの引数へ渡す値。キーが子の宣言に無ければ、流したとき失敗する。
                if (step.IsScenarioRef && step.Args != null && step.Args.Count > 0)
                {
                    var child = ScenarioLibrary.Get(step.RefName);
                    if (child != null)
                        foreach (var kv in step.SortedArgs())
                            if (child.FindParam(kv.Key) == null)
                                Add(id, "unknownChildParam", kv.Key, $"呼ぶシナリオ {step.RefName} に引数 {kv.Key} が宣言されていない");
                }

                // 項目の利用シーンが消えた・改名された。区間の意味が失われるので指摘する。
                if (!string.IsNullOrEmpty(step.UsageScene) && SceneLibrary.Get(step.UsageScene) == null)
                    Add(id, "unknownUsageScene", "", $"利用シーン {step.UsageScene} が登録されていない");

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
                            if (ScenarioParam.TryParseRef(v, out _)) continue;   // 引数で渡す
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
                            $"{v} は @ で始まるので、流したとき参照として読まれるが、@prev.<キー> か @<項目 ID>.<キー> の形になっていない");
                        continue;
                    }

                    string refId = v.Substring(1, dot - 1);
                    int at = g.IndexOfStep(refId);
                    if (at < 0)
                        Add(id, "missingRef", kv.Key, $"{v} が指す項目 {refId} がこのシナリオに無い");
                    else if (at >= i)
                        Add(id, "forwardRef", kv.Key, $"{v} が指す項目 {refId} はこの項目より後ろにある");
                    else
                        AuditChildOutputRef(g.Steps[at], v, v.Substring(dot + 1), id, kv.Key);
                }
            }

            // 参照項目の戻り値は、呼ぶシナリオが宣言した出力だけ（ScenarioOutput）。宣言の無い名前は流したとき失敗する。
            void AuditChildOutputRef(ObjectGroupStep target, string v, string key, string id, string argKey)
            {
                if (target == null || !target.IsScenarioRef) return;
                var child = ScenarioLibrary.Get(target.RefName);
                if (child == null) return;
                if (child.Outputs == null || child.Outputs.Count == 0)
                    Add(id, "refWithoutOutputs", argKey, $"{v} が指す参照項目の呼ぶシナリオ {target.RefName} は出力を宣言していない");
                else if (child.FindOutput(key) == null)
                    Add(id, "unknownChildOutput", argKey, $"{v} の {key} は、呼ぶシナリオ {target.RefName} の出力に無い");
            }

            // ── 出力の宣言 ──
            //   from は流し終えた時点で評価するので、@<項目 ID> はこのシナリオのどの項目でもよい（後ろの項目という制限は無い）。
            var outDeclared = new HashSet<string>(StringComparer.Ordinal);
            if (g.Outputs != null)
                foreach (var o in g.Outputs)
                {
                    if (o == null) continue;
                    if (!ScenarioParam.IsValidName(o.Name))
                        Add("", "badOutputName", o.Name, $"出力の名前 {o.Name} は使えない（英数字と _、先頭は数字以外、64 文字まで）");
                    else if (!outDeclared.Add(o.Name))
                        Add("", "duplicateOutput", o.Name, $"出力 {o.Name} が 2 回以上宣言されている");

                    string f = o.From ?? "";
                    if (f.Length == 0)
                    {
                        Add("", "emptyOutputFrom", o.Name, $"出力 {o.Name} の出どころが空");
                        continue;
                    }
                    if (ScenarioParam.TryParseRef(f, out string pn))
                    {
                        if (!declared.Contains(pn)) Add("", "unknownParam", o.Name, $"出力 {o.Name} の {f} の引数 {pn} が宣言されていない");
                        else used.Add(pn);
                        continue;
                    }
                    if (f[0] != '@' || f.Length < 2 || f.StartsWith(PrevPrefix, StringComparison.Ordinal)) continue;

                    int odot = f.IndexOf('.');
                    if (odot <= 1)
                    {
                        Add("", "badOutputRef", o.Name, $"出力 {o.Name} の {f} は @prev.<キー> か @<項目 ID>.<キー> の形になっていない");
                        continue;
                    }
                    string oid = f.Substring(1, odot - 1);
                    int oat = g.IndexOfStep(oid);
                    if (oat < 0)
                        Add("", "missingOutputRef", o.Name, $"出力 {o.Name} の {f} が指す項目 {oid} がこのシナリオに無い");
                    else
                        AuditChildOutputRef(g.Steps[oat], f, f.Substring(odot + 1), "", o.Name);
                }

            foreach (var name in declared)
                if (!used.Contains(name))
                    Add("", "unusedParam", name, $"引数 {name} を使う項目が無い");

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name ?? "")
                .Int  ("issues",     scenarioItemIds.Count)
                .Texts("scenarioItemIds", scenarioItemIds)
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

            // 利用シーンからの逆引き。登録されていない名前は、黙って 0 件にせず断る。
            SceneDefinition usage = null;
            if (!string.IsNullOrWhiteSpace(cmd.UsageScene))
            {
                usage = SceneLibrary.Get(cmd.UsageScene.Trim());
                if (usage == null) { Fail($"利用シーンがありません: {cmd.UsageScene}"); return; }
            }

            // 作り置きの索引で照合する（件数が増えても、検索のたびに全件を複製しない）。
            // 照合はコマンド検索と同じ方式（PanelCommandSchemaIndex.ScoreTerms）。空の query は全部通る。
            var index = ScenarioLibrary.SearchIndex();
            var terms = PanelCommandFactory.QueryTerms(cmd.Query);

            var names      = new List<string>();
            var folders    = new List<string>();
            var goals      = new List<string>();
            var stepCounts = new List<int>();
            int total      = 0;

            foreach (var e in index)
            {
                if (!ScenarioLibrary.FolderMatches(e.Folder, cmd.Folder)) continue;
                if (usage != null && !ScenarioLibrary.UsesScene(e, usage)) continue;
                if (PanelCommandFactory.ScoreTerms(e.NameLower, e.Haystack, terms) <= 0f) continue;

                int at = total++;
                if (at < cmd.Offset) continue;
                if (cmd.Limit > 0 && names.Count >= cmd.Limit) continue;

                names.Add(e.Name);
                folders.Add(e.Folder);
                goals.Add(e.Goal);
                stepCounts.Add(e.StepCount);
            }

            ReportData(CommandDataJson.New()
                .Int  ("count",      names.Count)
                .Int  ("total",      total)
                .Flag ("hasMore",    cmd.Offset + names.Count < total)
                .Text ("storePath",  ScenarioLibrary.StorePath)
                .Texts("names",      names)
                .Texts("folders",    folders)
                .Texts("allFolders", ScenarioLibrary.Folders())
                .Texts("goals",      goals)
                .Ints ("stepCounts", stepCounts)
                .Build());
        }

        private void RunDescribeScenario(DescribeScenarioCommand cmd)
        {
            string json = BuildScenarioDescribeJson(cmd.Name, cmd.Resolve, cmd.Detail, cmd.Items, out string error);
            if (json == null) { Fail(error); return; }
            ReportData(json);
        }

        /// <summary>describe の詳しさ。</summary>
        public const string DescribeDetailOutline = "outline";
        public const string DescribeDetailFull    = "full";

        /// <summary>
        /// describeScenario の結果（JSON オブジェクト）を組み立てる。
        /// MCP の polyling_describe（PolyLingControlProtocol.HandleToolsDescribe）も同じものを返す。
        /// 失敗したら null を返し、理由を error に入れる。
        ///
        /// 【詳しさ】
        ///   outline（既定）… 引数の値（argValues）を返さない。値には頂点座標の列などが
        ///                     そのまま入り、項目数と関係なく応答が膨らむため。
        ///                     流す（runScenario）には値は要らない。
        ///   full           … 値も返す。項目を直すときに、items で直す項目だけに絞って使う。
        /// </summary>
        /// <param name="detail">outline / full。空は outline。</param>
        /// <param name="items">返す項目 ID のカンマ区切り。空なら全項目。resolve とは併用できない。</param>
        public static string BuildScenarioDescribeJson(string name, bool resolve, string detail, string items, out string error)
        {
            error = null;

            string d = (detail ?? "").Trim().ToLowerInvariant();
            if (d.Length == 0) d = DescribeDetailOutline;
            if (d != DescribeDetailOutline && d != DescribeDetailFull)
            { error = $"detail は {DescribeDetailOutline} / {DescribeDetailFull} のどちらかです: {detail}"; return null; }
            bool full = d == DescribeDetailFull;

            var wanted = new List<string>();
            if (!string.IsNullOrWhiteSpace(items))
                foreach (var part in items.Split(','))
                {
                    var id = part.Trim();
                    if (id.Length > 0 && !wanted.Contains(id)) wanted.Add(id);
                }
            if (wanted.Count > 0 && resolve)
            { error = "items は resolve と一緒に使えません（項目 ID はシナリオごとの名前です。子のシナリオは名前で直接指定してください）"; return null; }

            var g = ScenarioLibrary.Get(name);
            if (g == null) { error = $"シナリオがありません: {name}"; return null; }

            // 項目の並び。resolve のときは参照を辿って平たくしたもの。
            var ordered = new List<ObjectGroupStep>();
            var owners  = new List<string>();
            var depths  = new List<int>();

            if (resolve)
            {
                if (!ScenarioLibrary.TryFlatten(name, out var flat, out string flatError))
                { error = flatError; return null; }

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

            // items の指定。並びは元の順のまま。無い ID は黙って落とさず断る。
            int totalSteps = ordered.Count;
            if (wanted.Count > 0)
            {
                var picked = new List<ObjectGroupStep>();
                var found  = new HashSet<string>();
                foreach (var step in ordered)
                    if (wanted.Contains(step.ScenarioItemId ?? "")) { picked.Add(step); found.Add(step.ScenarioItemId); }

                var missing = new List<string>();
                foreach (var id in wanted) if (!found.Contains(id)) missing.Add(id);
                if (missing.Count > 0)
                { error = $"{name} に項目がありません: {string.Join(",", missing)}"; return null; }

                ordered = picked;
            }

            var scenarioItemIds = new List<string>();
            var kinds      = new List<string>();
            var actions    = new List<string>();
            var purposes   = new List<string>();
            var usages     = new List<string>();
            var refNames   = new List<string>();
            var policies   = new List<string>();
            var argCounts  = new List<int>();
            var argKeys    = new List<string>();
            var argValues  = new List<string>();

            foreach (var step in ordered)
            {
                scenarioItemIds.Add(step.ScenarioItemId ?? "");
                kinds.Add(step.Kind.ToString());
                actions.Add(step.Action ?? "");
                purposes.Add(step.Purpose ?? "");
                usages.Add(step.UsageScene ?? "");
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

            // 引数の宣言。outline でも載せる（流す前に何を渡せるか分かるように。量は宣言の数だけ）。
            var paramNames    = new List<string>();
            var paramDefaults = new List<string>();
            var paramDescs    = new List<string>();
            if (g.Params != null)
                foreach (var p in g.Params)
                {
                    if (p == null) continue;
                    paramNames.Add(p.Name ?? "");
                    paramDefaults.Add(p.Default ?? "");
                    paramDescs.Add(p.Description ?? "");
                }

            // 出力の宣言。引数と同じく outline でも載せる（呼ぶ側が何を受け取れるか分かるように）。
            var outNames = new List<string>();
            var outFroms = new List<string>();
            var outDescs = new List<string>();
            if (g.Outputs != null)
                foreach (var o in g.Outputs)
                {
                    if (o == null) continue;
                    outNames.Add(o.Name ?? "");
                    outFroms.Add(o.From ?? "");
                    outDescs.Add(o.Description ?? "");
                }

            var builder = CommandDataJson.New()
                .Text ("name",              g.Name ?? "")
                .Text ("detail",            d);
            if (wanted.Count > 0) builder.Int("totalSteps", totalSteps);

            return builder
                .Text ("folder",            ScenarioLibrary.FolderOf(g.Name) ?? "")
                .Text ("goal",              g.Goal ?? "")
                .Text ("parentName",        prov.ParentName ?? "")
                .Text ("changeSummary",     prov.ChangeSummary ?? "")
                .Text ("createdBy",         prov.CreatedBy ?? "")
                .Int  ("steps",             ordered.Count)
                .Texts("preconditions",     g.Preconditions)
                .Texts("successCriteria",   g.SuccessCriteria)
                .Texts("tags",              g.Tags)
                .Texts("paramNames",        paramNames)
                .Texts("paramDefaults",     paramDefaults)
                .Texts("paramDescriptions", paramDescs)
                .Texts("outputNames",        outNames)
                .Texts("outputFroms",        outFroms)
                .Texts("outputDescriptions", outDescs)
                .Texts("scenarioItemIds",        scenarioItemIds)
                .Texts("kinds",             kinds)
                .Texts("actions",           actions)
                .Texts("purposes",          purposes)
                .Texts("usageScenes",       usages)
                .Texts("refNames",          refNames)
                .Texts("expansionPolicies", policies)
                .Texts("scenarioNames",     owners)
                .Ints ("depths",            depths)
                .Ints ("argCounts",         argCounts)
                .Texts("argKeys",           argKeys)
                .Texts("argValues",         full ? argValues : null)
                .Build();
        }

        // ================================================================
        // 作る・消す
        // ================================================================

        private void RunCreateScenario(CreateScenarioCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.Name)) { Fail("シナリオの名前が空です"); return; }

            var g = new ObjectGroup(cmd.Name) { Goal = cmd.Goal ?? "" };
            g.Steps.Clear();   // 項目は addScenarioItem で足す

            string folder = string.IsNullOrWhiteSpace(cmd.Folder) ? null : cmd.Folder;
            if (!ScenarioLibrary.Register(g, cmd.Overwrite, out string error, folder)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Int ("count",     ScenarioLibrary.Count)
                .Text("folder",    ScenarioLibrary.FolderOf(g.Name) ?? "")
                .Text("storePath", ScenarioLibrary.StorePath)
                .Build());
        }

        // ================================================================
        // フォルダ
        // ================================================================

        private void RunMoveScenarios(MoveScenariosCommand cmd)
        {
            if (!ScenarioLibrary.Move(cmd.Names, cmd.Folder, out string folder, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("folder", folder)
                .Texts("names",  cmd.Names)
                .Build());
        }

        private void RunCreateScenarioFolder(CreateScenarioFolderCommand cmd)
        {
            if (!ScenarioLibrary.CreateFolder(cmd.Folder, out string folder, out string error)) { Fail(error); return; }
            ReportData(CommandDataJson.New().Text("folder", folder).Build());
        }

        private void RunRenameScenarioFolder(RenameScenarioFolderCommand cmd)
        {
            if (!ScenarioLibrary.RenameFolder(cmd.Folder, cmd.NewFolder, out string folder, out string error)) { Fail(error); return; }
            ReportData(CommandDataJson.New().Text("folder", folder).Build());
        }

        private void RunDeleteScenarioFolder(DeleteScenarioFolderCommand cmd)
        {
            if (!ScenarioLibrary.DeleteFolder(cmd.Folder, out string error)) { Fail(error); return; }
            ReportData(CommandDataJson.New().Text("folder", cmd.Folder).Build());
        }

        /// <summary>
        /// フォルダの中のシナリオを順に呼ぶ親を作る。
        /// 名前の指定が無ければ、フォルダ直下のシナリオを名前順に呼ぶ（作る親自身は除く）。
        /// 循環と参照先の不在は登録（ScenarioLibrary.Register）が見る。
        /// </summary>
        private void RunCreateScenarioFromFolder(CreateScenarioFromFolderCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.Name)) { Fail("親シナリオの名前が空です"); return; }

            if (!ScenarioLibrary.TryFindFolder(cmd.Folder, out string folder)) { Fail($"フォルダがありません: {cmd.Folder}"); return; }

            var calls = new List<string>();
            if (cmd.Names != null && cmd.Names.Length > 0)
            {
                foreach (var n in cmd.Names)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!ScenarioLibrary.Contains(n)) { Fail($"シナリオがありません: {n}"); return; }
                    if (string.Equals(n, cmd.Name, StringComparison.Ordinal)) { Fail("親が自分自身を呼ぶことはできません"); return; }
                    calls.Add(n);
                }
            }
            else
            {
                foreach (var n in ScenarioLibrary.Names())
                    if (string.Equals(ScenarioLibrary.FolderOf(n), folder, StringComparison.Ordinal)
                        && !string.Equals(n, cmd.Name, StringComparison.Ordinal))
                        calls.Add(n);
                calls.Sort(StringComparer.Ordinal);
            }
            if (calls.Count == 0) { Fail($"呼ぶシナリオがありません: {folder}"); return; }

            var g = new ObjectGroup(cmd.Name) { Goal = cmd.Goal ?? "" };
            g.Steps.Clear();
            foreach (var n in calls)
            {
                var child = ScenarioLibrary.Get(n);
                g.AddStep(new ObjectGroupStep
                {
                    Kind    = ObjectGroupStepKind.ScenarioRef,
                    RefName = n,
                    Purpose = child?.Goal ?? "",
                });
            }
            g.Provenance = new ObjectGroupProvenance
            {
                ParentName    = "",
                ChangeSummary = $"フォルダ {folder} のシナリオを順に呼ぶ親として作った",
                CreatedBy     = "",
            };

            if (!ScenarioLibrary.Register(g, cmd.Overwrite, out string error, folder)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("name",   g.Name)
                .Text ("folder", folder)
                .Int  ("steps",  g.StepCount)
                .Texts("calls",  calls)
                .Int  ("count",  ScenarioLibrary.Count)
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
            if (src == null) { Fail($"シナリオがありません: {cmd.SourceName}"); return; }
            if (string.IsNullOrEmpty(cmd.NewName)) { Fail("複製の名前が空です"); return; }

            src.Name = cmd.NewName;
            src.Provenance = new ObjectGroupProvenance
            {
                ParentName    = cmd.SourceName,
                ChangeSummary = cmd.ChangeSummary ?? "",
                CreatedBy     = src.Provenance?.CreatedBy ?? "",
            };

            if (!ScenarioLibrary.Register(src, cmd.Overwrite, out string error, ScenarioLibrary.FolderOf(cmd.SourceName))) { Fail(error); return; }

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
            if (string.IsNullOrEmpty(cmd.ScenarioName)) { Fail("シナリオの名前が空です"); return; }

            var g = group.Clone();
            g.Name = cmd.ScenarioName;
            if (!string.IsNullOrEmpty(cmd.Goal)) g.Goal = cmd.Goal;

            // 実体への参照は落とす。シナリオは特定のオブジェクトに縛らない。
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
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

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

        private void RunAddScenarioItem(AddScenarioItemCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            if (!TryBuildScenarioItem(
                    cmd.Kind, cmd.Action, cmd.Purpose, cmd.ArgKeys, cmd.ArgValues,
                    cmd.RefName, cmd.ExpansionPolicy, cmd.UsageScene,
                    out ObjectGroupStep step, out string reason))
            { Fail(reason); return; }

            if (!string.IsNullOrEmpty(cmd.ScenarioItemId))
            {
                if (g.IndexOfStep(cmd.ScenarioItemId) >= 0)
                { Fail($"その項目 ID は既に使われています: {cmd.ScenarioItemId}"); return; }
                step.ScenarioItemId = cmd.ScenarioItemId;
            }

            if (!string.IsNullOrEmpty(cmd.AfterScenarioItemId) && !string.IsNullOrEmpty(cmd.BeforeScenarioItemId))
            { Fail("afterScenarioItemId と beforeScenarioItemId は同時に指定できません"); return; }

            if (!string.IsNullOrEmpty(cmd.BeforeScenarioItemId))
            {
                int at = g.IndexOfStep(cmd.BeforeScenarioItemId);
                if (at < 0) { Fail($"項目がありません: {cmd.BeforeScenarioItemId}"); return; }

                g.Steps.Insert(at, step);
                g.EnsureScenarioItemIds();
            }
            else if (string.IsNullOrEmpty(cmd.AfterScenarioItemId))
            {
                g.AddStep(step);
            }
            else
            {
                int at = g.IndexOfStep(cmd.AfterScenarioItemId);
                if (at < 0) { Fail($"項目がありません: {cmd.AfterScenarioItemId}"); return; }

                g.Steps.Insert(at + 1, step);
                g.EnsureScenarioItemIds();
            }

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("scenarioItemId", step.ScenarioItemId)
                .Int ("steps",     g.StepCount)
                .Build());
        }

        private void RunSetScenarioItem(SetScenarioItemCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ScenarioItemId);
            if (at < 0) { Fail($"項目がありません: {cmd.ScenarioItemId}"); return; }

            if (!TryBuildScenarioItem(
                    cmd.Kind, cmd.Action, cmd.Purpose, cmd.ArgKeys, cmd.ArgValues,
                    cmd.RefName, cmd.ExpansionPolicy, cmd.UsageScene,
                    out ObjectGroupStep step, out string reason))
            { Fail(reason); return; }

            step.ScenarioItemId = cmd.ScenarioItemId;
            g.Steps[at] = step;

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("scenarioItemId", step.ScenarioItemId)
                .Int ("steps",     g.StepCount)
                .Build());
        }

        private void RunRemoveScenarioItem(RemoveScenarioItemCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ScenarioItemId);
            if (at < 0) { Fail($"項目がありません: {cmd.ScenarioItemId}"); return; }

            g.Steps.RemoveAt(at);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",  g.Name)
                .Int ("steps", g.StepCount)
                .Build());
        }

        /// <summary>
        /// 項目の引数を 1 つだけ書く。
        ///
        /// addScenarioItem / setScenarioItem の argKeys / argValues は配列なので、
        /// MCP 経由ではカンマで割れる。値そのものにカンマを含む引数
        /// （masterIndices、点列、pivot など）はあちらでは書けないため、
        /// キーと値を単独の文字列で受けるこの口を通す。
        /// </summary>
        private void RunSetScenarioParam(SetScenarioParamCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }
            if (!ScenarioParam.IsValidName(cmd.Param))
            { Fail($"引数の名前 {cmd.Param} は使えません（英数字と _、先頭は数字以外、64 文字まで）"); return; }

            if (g.Params == null) g.Params = new List<ScenarioParam>();
            var p = g.FindParam(cmd.Param);
            bool added = p == null;
            if (added) { p = new ScenarioParam { Name = cmd.Param }; g.Params.Add(p); }
            p.Default     = cmd.DefaultValue ?? "";
            p.Description = cmd.Description ?? "";

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("name",   g.Name)
                .Text ("param",  cmd.Param)
                .Flag ("added",  added)
                .Texts("params", ParamNames(g))
                .Build());
        }

        private void RunRemoveScenarioParam(RemoveScenarioParamCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            var p = g.FindParam(cmd.Param);
            if (p == null) { Fail($"{cmd.Name} に引数 {cmd.Param} はありません"); return; }
            g.Params.Remove(p);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("name",   g.Name)
                .Text ("param",  cmd.Param)
                .Texts("params", ParamNames(g))
                .Build());
        }

        private void RunSetScenarioOutput(SetScenarioOutputCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }
            if (!ScenarioParam.IsValidName(cmd.Output))
            { Fail($"出力の名前 {cmd.Output} は使えません（英数字と _、先頭は数字以外、64 文字まで）"); return; }
            if (string.IsNullOrEmpty(cmd.From)) { Fail("from が空です"); return; }

            if (g.Outputs == null) g.Outputs = new List<ScenarioOutput>();
            var o = g.FindOutput(cmd.Output);
            bool added = o == null;
            if (added) { o = new ScenarioOutput { Name = cmd.Output }; g.Outputs.Add(o); }
            o.From        = cmd.From;
            o.Description = cmd.Description ?? "";

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("name",    g.Name)
                .Text ("output",  cmd.Output)
                .Flag ("added",   added)
                .Texts("outputs", OutputNames(g))
                .Build());
        }

        private void RunRemoveScenarioOutput(RemoveScenarioOutputCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            var o = g.FindOutput(cmd.Output);
            if (o == null) { Fail($"{cmd.Name} に出力 {cmd.Output} はありません"); return; }
            g.Outputs.Remove(o);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text ("name",    g.Name)
                .Text ("output",  cmd.Output)
                .Texts("outputs", OutputNames(g))
                .Build());
        }

        private static List<string> OutputNames(ObjectGroup g)
        {
            var list = new List<string>();
            if (g?.Outputs != null)
                foreach (var o in g.Outputs) if (o != null) list.Add(o.Name ?? "");
            return list;
        }

        private static List<string> ParamNames(ObjectGroup g)
        {
            var list = new List<string>();
            if (g?.Params != null)
                foreach (var p in g.Params) if (p != null) list.Add(p.Name ?? "");
            return list;
        }

        private void RunSetScenarioItemArg(SetScenarioItemArgCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ScenarioItemId);
            if (at < 0) { Fail($"項目がありません: {cmd.ScenarioItemId}"); return; }

            var step = g.Steps[at];

            // 参照項目の引数は、呼ぶシナリオの引数へ渡す値（キーが子の引数名）。
            if (!step.IsExecutable && !step.IsScenarioRef)
            { Fail($"{cmd.ScenarioItemId} は {step.Kind} の項目で、引数を持ちません"); return; }

            if (string.IsNullOrEmpty(cmd.Key)) { Fail("key が空です"); return; }

            if (cmd.Remove) step.Args.Remove(cmd.Key);
            else            step.SetArg(cmd.Key, cmd.Value ?? "");

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            ReportData(CommandDataJson.New()
                .Text("name",      g.Name)
                .Text("scenarioItemId", step.ScenarioItemId ?? "")
                .Text("key",       cmd.Key)
                .Text("value",     cmd.Remove ? "" : (cmd.Value ?? ""))
                .Int ("args",      step.Args?.Count ?? 0)
                .Build());
        }

        /// <summary>
        /// 項目を別の位置へ動かす。項目 ID と中身は変えない。
        ///
        /// 消して足し直すと名前が変わり、他の項目の Instruction が指す先を失う。
        /// 並びだけを変える口を分けてある。
        /// </summary>
        private void RunMoveScenarioItem(MoveScenarioItemCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            int from = g.IndexOfStep(cmd.ScenarioItemId);
            if (from < 0) { Fail($"項目がありません: {cmd.ScenarioItemId}"); return; }

            bool hasBefore = !string.IsNullOrEmpty(cmd.BeforeScenarioItemId);
            bool hasAfter  = !string.IsNullOrEmpty(cmd.AfterScenarioItemId);

            if (hasBefore && hasAfter)
            { Fail("beforeScenarioItemId と afterScenarioItemId は同時に指定できません"); return; }

            if (!hasBefore && !hasAfter && !cmd.ToTop && !cmd.ToBottom)
            { Fail("beforeScenarioItemId / afterScenarioItemId / toTop / toBottom のどれかを指定してください"); return; }

            var step = g.Steps[from];
            g.Steps.RemoveAt(from);

            int to;
            if (hasBefore)
            {
                to = g.IndexOfStep(cmd.BeforeScenarioItemId);
                if (to < 0) { Fail($"項目がありません: {cmd.BeforeScenarioItemId}"); return; }
            }
            else if (hasAfter)
            {
                int at = g.IndexOfStep(cmd.AfterScenarioItemId);
                if (at < 0) { Fail($"項目がありません: {cmd.AfterScenarioItemId}"); return; }
                to = at + 1;
            }
            else
            {
                to = cmd.ToTop ? 0 : g.Steps.Count;
            }

            g.Steps.Insert(to, step);

            if (!ScenarioLibrary.Register(g, overwrite: true, out string error)) { Fail(error); return; }

            var ids = new List<string>(g.Steps.Count);
            foreach (var s in g.Steps) if (s != null) ids.Add(s.ScenarioItemId ?? "");

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name)
                .Text ("scenarioItemId",  step.ScenarioItemId ?? "")
                .Int  ("fromIndex",  from)
                .Int  ("toIndex",    to)
                .Texts("scenarioItemIds", ids)
                .Build());
        }

        /// <summary>参照項目を参照先の項目の列で置き換える。</summary>
        private void RunExpandScenarioRef(ExpandScenarioRefCommand cmd)
        {
            var g = ScenarioLibrary.Get(cmd.Name);
            if (g == null) { Fail($"シナリオがありません: {cmd.Name}"); return; }

            int at = g.IndexOfStep(cmd.ScenarioItemId);
            if (at < 0) { Fail($"項目がありません: {cmd.ScenarioItemId}"); return; }

            var refStep = g.Steps[at];
            if (!refStep.IsScenarioRef)
            { Fail($"{cmd.ScenarioItemId} は {refStep.Kind} の項目で、参照項目ではありません"); return; }

            var src = ScenarioLibrary.Get(refStep.RefName);
            if (src == null) { Fail($"参照先のシナリオがありません: {refStep.RefName}"); return; }

            // 参照項目の Args を参照先の引数に結ぶ。写した項目の ${参照先の引数} は結んだ値に置き換える
            // （参照項目が消えると結び付けも消えるので、値として写す）。結んだ値が親の ${名前} なら、
            // そのまま親の引数を指す項目になる。親の側は結ばない（親の引数は流すときに決まる）。
            if (!ScenarioParam.TryBindRef(refStep, src, null, out var bound, out string bindError))
            { Fail(bindError); return; }

            // 参照先の項目をそのまま写す。参照先がさらに参照項目を持つ場合は
            // 参照項目のまま入る（その Args も置き換わる）。深い項目を開くにはもう一度この口を使う。
            var inserted = new List<ObjectGroupStep>();
            var oldIds   = new List<string>();
            if (src.Steps != null)
            {
                foreach (var s in src.Steps)
                {
                    if (s == null) continue;
                    var copy = ScenarioParam.SubstituteStep(s, bound, out string substError);
                    if (copy == null) { Fail($"{refStep.RefName} の {s.ScenarioItemId}: {substError}"); return; }
                    oldIds.Add(copy.ScenarioItemId ?? "");
                    copy.ScenarioItemId = "";   // 名前は入れた先で振り直す
                    inserted.Add(copy);
                }
            }

            g.Steps.RemoveAt(at);
            g.Steps.InsertRange(at, inserted);
            g.EnsureScenarioItemIds();

            // 写した項目どうしの @<項目 ID> 参照を、振り直した名前へ付け替える。
            // 付け替えないと参照先での名前のまま残り、入れた先の別の項目を指すか、無い項目を指す。
            var rename = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < inserted.Count; i++)
                if (!string.IsNullOrEmpty(oldIds[i])) rename[oldIds[i]] = inserted[i].ScenarioItemId;

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
            foreach (var s in inserted) ids.Add(s.ScenarioItemId);

            ReportData(CommandDataJson.New()
                .Text ("name",       g.Name)
                .Text ("refName",    refStep.RefName ?? "")
                .Int  ("inserted",   inserted.Count)
                .Int  ("steps",      g.StepCount)
                .Texts("scenarioItemIds", ids)
                .Build());
        }

        // ================================================================
        // 流す（PlayerCommandDispatcher.ScenarioRun.cs）
        // ================================================================

        // ================================================================
        // @prev / @<項目 ID>
        // ================================================================

        // 控え（直前の項目の対象・戻り値、項目ごとの戻り値）は ScenarioRunState が持つ。
        // 流れ 1 回ぶんの器に閉じ込め、別の流れや別のシナリオの結果が混ざらないようにする。

        private const string PrevPrefix            = "@prev.";
        private const string PrevMasterIndicesToken = "@prev.masterIndices";
        private const string PrevObjectIdsToken     = "@prev.objectIds";

        /// <summary>
        /// 引数の値が @prev / @&lt;項目 ID&gt; なら、流れの控えから実際の値へ直す。
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
                { reason = $"{PrevMasterIndicesToken} を使いましたが、直前に実行した項目の対象がありません"; return false; }

                var parts = new List<string>(run.PrevMaster.Length);
                foreach (int i in run.PrevMaster)
                    parts.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                expanded = string.Join(",", parts);
                return true;
            }

            if (string.Equals(value, PrevObjectIdsToken, StringComparison.Ordinal))
            {
                if (run.PrevIds == null || run.PrevIds.Length == 0)
                { reason = $"{PrevObjectIdsToken} を使いましたが、直前に実行した項目の対象がありません"; return false; }

                expanded = string.Join(",", IdTexts(run.PrevIds));
                return true;
            }

            // @prev.<キー> は直前の項目の戻り値から引く。
            // 照会が返す faceIndices / v1 / modelIndices などを次の項目へ渡すため。
            // 対象（masterIndices / objectIds）は上で先に処理しており、
            // ここへは来ない。
            if (value.StartsWith(PrevPrefix, StringComparison.Ordinal))
            {
                string key = value.Substring(PrevPrefix.Length);
                if (string.IsNullOrEmpty(key))
                { reason = "@prev. の後ろにキーがありません"; return false; }

                if (string.IsNullOrEmpty(run.PrevData))
                { reason = $"{value} を使いましたが、直前に実行した項目が戻り値を返していません"; return false; }

                if (!TryReadJsonValue(run.PrevData, key, out string got))
                { reason = $"{value} を使いましたが、直前の戻り値に {key} がありません"; return false; }

                expanded = got;
                return true;
            }

            // @<項目 ID>.<キー> は名指しした項目の戻り値から引く。
            // 照会と使用の間に別の項目を挟めるので、2 本の値を渡すときに要る。
            if (value.Length > 1 && value[0] == '@')
            {
                int dot = value.IndexOf('.');
                if (dot <= 1)
                { reason = $"{value} の書き方が違います。@prev.<キー> か @<項目 ID>.<キー>"; return false; }

                string id  = value.Substring(1, dot - 1);
                string k2  = value.Substring(dot + 1);
                if (string.IsNullOrEmpty(k2))
                { reason = $"{value} にキーがありません"; return false; }

                if (!run.Results.TryGetValue(StepResultKey(scenarioName, id), out var rec))
                { reason = $"{value} を使いましたが、この流れで項目 {id} をまだ実行していません"; return false; }

                if (string.Equals(k2, "masterIndices", StringComparison.Ordinal))
                {
                    if (rec.Master == null || rec.Master.Length == 0)
                    { reason = $"{value} を使いましたが、項目 {id} は対象を返していません"; return false; }

                    var parts = new List<string>(rec.Master.Length);
                    foreach (int i in rec.Master)
                        parts.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    expanded = string.Join(",", parts);
                    return true;
                }

                if (string.Equals(k2, "objectIds", StringComparison.Ordinal))
                {
                    if (rec.Ids == null || rec.Ids.Length == 0)
                    { reason = $"{value} を使いましたが、項目 {id} は対象を返していません"; return false; }

                    expanded = string.Join(",", IdTexts(rec.Ids));
                    return true;
                }

                if (string.IsNullOrEmpty(rec.Data))
                { reason = $"{value} を使いましたが、項目 {id} は戻り値を返していません"; return false; }

                if (!TryReadJsonValue(rec.Data, k2, out string v2))
                { reason = $"{value} を使いましたが、項目 {id} の戻り値に {k2} がありません"; return false; }

                expanded = v2;
                return true;
            }

            return true;
        }

        /// <summary>項目ごとの控えの鍵。シナリオが違えば同じ項目 ID でもぶつからない。</summary>
        private static string StepResultKey(string scenarioName, string scenarioItemId)
            => (scenarioName ?? "") + "/" + (scenarioItemId ?? "");

        /// <summary>
        /// 戻り値の JSON から 1 つのキーを取り出し、引数に渡せる文字列にする。
        ///
        /// 配列はカンマ区切りへ潰す。TryParse 側が配列をカンマで割るので、
        /// これで faceIndices などをそのまま次の項目の引数に入れられる。
        /// 入れ子の配列や連想配列は扱わない。
        ///
        /// 値は JSON として読み、文字列のエスケープ（\\ や \"）を戻してから渡す。
        /// 戻さずに渡すと、Windows のパスなどが次の項目で「\\」のまま使われ、
        /// 段を重ねるたびにエスケープが倍になる（シナリオの出力で起きた）。
        ///
        /// キーは最上位を先に見て、無ければ入れ子の中から最初に見つかったものを使う
        /// （以前の文字列検索と同じく、どの深さのキーでも引ける）。
        /// 値が配列なら中身は数・文字列・真偽だけ（入れ子の配列・連想配列は失敗）。連想配列そのものも失敗にして、
        /// 黙って誤った値を渡さない。
        /// </summary>
        private static bool TryReadJsonValue(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;

            Newtonsoft.Json.Linq.JObject root;
            try { root = Newtonsoft.Json.Linq.JObject.Parse(json); }
            catch { return false; }

            var token = root[key];
            if (token == null)
            {
                foreach (var d in root.Descendants())
                    if (d is Newtonsoft.Json.Linq.JProperty jp && string.Equals(jp.Name, key, StringComparison.Ordinal))
                    { token = jp.Value; break; }
            }
            if (token == null) return false;

            if (token is Newtonsoft.Json.Linq.JArray arr)
            {
                var parts = new List<string>(arr.Count);
                foreach (var e in arr)
                {
                    if (!(e is Newtonsoft.Json.Linq.JValue ev)) return false;
                    parts.Add(JsonScalarText(ev));
                }
                value = string.Join(",", parts);
                return true;
            }

            if (token is Newtonsoft.Json.Linq.JValue v)
            {
                value = JsonScalarText(v);
                return v.Type == Newtonsoft.Json.Linq.JTokenType.String || value.Length > 0;
            }

            return false;   // 連想配列は扱わない
        }

        /// <summary>JSON の値 1 つを引数の文字列にする（文字列はそのまま、真偽は true/false、数は JSON の書き方）。</summary>
        private static string JsonScalarText(Newtonsoft.Json.Linq.JValue v)
        {
            switch (v.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.String:  return (string)v ?? "";
                case Newtonsoft.Json.Linq.JTokenType.Boolean: return (bool)v ? "true" : "false";
                case Newtonsoft.Json.Linq.JTokenType.Null:    return "";
                default: return v.ToString(Newtonsoft.Json.Formatting.None);
            }
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
        // 項目の組み立て
        // ================================================================

        /// <summary>
        /// 引数から項目を 1 つ作る。実体への参照は持たせない。
        /// </summary>
        private static bool TryBuildScenarioItem(
            ObjectGroupStepKind kind, string action, string purpose,
            string[] argKeys, string[] argValues,
            string refName, ScenarioExpansionPolicy expansionPolicy, string usageScene,
            out ObjectGroupStep step, out string reason)
        {
            step   = null;
            reason = null;

            // 項目の利用シーンは、登録済みの名前だけを受ける（綴り違いのまま区間ができるのを防ぐ）。
            // 登録名の大小文字に揃えて持つ。
            usageScene = (usageScene ?? "").Trim();
            if (usageScene.Length > 0)
            {
                var sc = SceneLibrary.Get(usageScene);
                if (sc == null)
                {
                    reason = $"利用シーンがありません: {usageScene}（polyling_scenes の名前を指定する）";
                    return false;
                }
                usageScene = sc.Name;
            }

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
                reason = "Kind が Command の項目には action が要ります";
                return false;
            }

            if (!executable && !string.IsNullOrEmpty(action))
            {
                reason = $"Kind が {kind} の項目は実行しないので action を持てません";
                return false;
            }

            if (scenarioRef && string.IsNullOrEmpty(refName))
            {
                reason = "Kind が ScenarioRef の項目には refName が要ります";
                return false;
            }

            if (!scenarioRef && !string.IsNullOrEmpty(refName))
            {
                reason = $"Kind が {kind} の項目は refName を持てません";
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
                UsageScene      = usageScene,
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
