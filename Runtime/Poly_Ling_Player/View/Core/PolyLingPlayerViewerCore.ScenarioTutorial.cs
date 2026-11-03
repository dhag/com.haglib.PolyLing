// PolyLingPlayerViewerCore.ScenarioTutorial.cs
// Player ビューアのコア：チュートリアルの作成（buildScenarioTutorial / queryScenarioTutorialBuild）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【正本】PolyLing_チュートリアル計画.md 3.3・3.4・段 4
//
// 【流れ】
//   1. 実行前に検査する。項目 ID、経路と実行のボタン、撮影範囲、画像のパス、出力先。問題があれば何も実行しない
//   2. シナリオを 1 項目ずつ流し（案内バーと同じく continueScenario の maxSteps=1）、
//      原稿の項目の前後で撮る。指示・確認の項目は通過し、アドオンの項目は返事を待つ
//   3. 全部撮れたら BASE64 で埋め込んで MD を書く。失敗したら MD は書かず、撮った画像は残す
//
// 【同じ項目を複数回通る】
//   項目を検査の時点で一度並べ、最初の通過を撮影の対象にする。
//   経路は前の項目で変わる（UiRouteCatalog.Choose）ので、流しと同じ順に前の経路を持ち回って求める。
//
// 【カメラと選択】
//   fit はシナリオの項目にせず、撮影の間だけカメラを動かして戻す。範囲は名前から直接引き、選択を変えない。
//
// 【待ち方】
//   フレーム数で数える。Unity が背面でフレームが進まない間は待ちの回数も進まないので、前面に戻ったときに
//   時間切れで誤って失敗しない。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private readonly ScenarioTutorialJobs _tutorialJobs = new ScenarioTutorialJobs();
        private bool _tutorialBusy;

        /// <summary>スクロールと枠が落ち着くのを待つフレームの上限。超えても撮り、矢印の検査で落とす。</summary>
        private const int TutorialSettleMaxFrames = 60;

        /// <summary>撮影の完了を待つフレームの上限。</summary>
        private const int TutorialCaptureMaxFrames = 600;

        /// <summary>メッシュの表示位置が読めるようになるのを待つフレームの上限。</summary>
        private const int TutorialFitMaxFrames = 30;

        /// <summary>アドオンの返事を待つ上限[秒]。背面の間は進まない。</summary>
        private const float TutorialAddonTimeoutSeconds = 600f;

        private int TutorialModelIndex => ActiveProject?.CurrentModelIndex ?? 0;

        // ================================================================
        // 受け口
        // ================================================================

        private CommandResult ExecuteBuildScenarioTutorial(BuildScenarioTutorialCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiAutomation == null || _uiAutomationRegistry == null) return CommandResult.Fail(UiAutomationNotReady);
            if (_tutorialBusy) return CommandResult.Fail("チュートリアルを作っている途中です。queryScenarioTutorialBuild で終わりを確かめてください");
            if (_scenarioGuideBar != null && _scenarioGuideBar.IsBusy)
                return CommandResult.Fail("案内バーでシナリオを流している途中です");

            var active = _commandDispatcher?.ScenarioRun;
            if (active != null && active.Stop != ScenarioRunStop.Finished && active.Stop != ScenarioRunStop.Failed)
                return CommandResult.Fail($"シナリオ「{active.RootName}」を流している途中です。stopScenarioRun でやめてから作ってください");

            var root = ScenarioLibrary.Get(cmd.ScenarioName);
            if (root == null) return CommandResult.Fail($"シナリオがありません: {cmd.ScenarioName}");

            if (cmd.ParamKeys.Length != cmd.ParamValues.Length)
                return CommandResult.Fail($"paramKeys が {cmd.ParamKeys.Length} 件、paramValues が {cmd.ParamValues.Length} 件で長さが合いません");
            if (!ScenarioTutorialManuscript.IsKnownFormat(cmd.Format))
                return CommandResult.Fail($"format は png か jpeg です: {cmd.Format}");

            // ── 原稿 ──
            bool hasFile = !string.IsNullOrEmpty(cmd.FilePath);
            bool hasText = !string.IsNullOrEmpty(cmd.MarkdownText);
            if (hasFile == hasText) return CommandResult.Fail("filePath と markdownText はどちらか一方だけ渡してください");

            string text = cmd.MarkdownText;
            string baseDir = "";
            if (hasFile)
            {
                if (!PLSandbox.TryResolveRead(cmd.FilePath, out string manuscriptPath, out string readReason))
                    return CommandResult.Fail(readReason);
                if (!File.Exists(manuscriptPath)) return CommandResult.Fail($"原稿のファイルがありません: {cmd.FilePath}");
                try { text = File.ReadAllText(manuscriptPath, Encoding.UTF8); }
                catch (Exception e) { return CommandResult.Fail($"原稿を読めません: {e.Message}"); }
                baseDir = Path.GetDirectoryName(manuscriptPath);
            }

            // ── 出力先 ──
            if (!string.Equals(Path.GetExtension(cmd.OutPath), ".md", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Fail($"outPath の拡張子は .md です: {cmd.OutPath}");
            if (!PLSandbox.TryResolveWrite(cmd.OutPath, out string outPath, out string outReason))
                return CommandResult.Fail(outReason);

            string imagesFolderPath = Path.Combine(
                Path.GetDirectoryName(outPath) ?? "", Path.GetFileNameWithoutExtension(outPath) + "_images");
            if (!PLSandbox.TryResolveFolder(imagesFolderPath, out string imagesFolder, out string folderReason))
                return CommandResult.Fail(folderReason);

            // ── 実行前の検査 ──
            var m = ScenarioTutorialManuscript.Parse(text);
            ResolveTutorialShots(root, m);
            ResolveTutorialImages(m, baseDir);

            var job = _tutorialJobs.Start();
            job.OutPath      = outPath;
            job.ImagesFolder = imagesFolder;
            job.ShotsTotal   = m.Shots.Count;

            var reply = CommandDataJson.New().Text("buildId", job.Id);

            if (m.Problems.Count > 0)
            {
                var lines = new List<string>(m.Problems.Count);
                foreach (var p in m.Problems) lines.Add($"{p.Item}: {p.Reason}");
                ScenarioTutorialJobs.Fail(job, m.Problems[0].Item, string.Join("\n", lines));
                Debug.LogWarning($"[ScenarioTutorial] 実行前に断った: {job.Error}");
                return CommandResult.Ok(data: reply
                    .Text("status",     ScenarioTutorialJobs.StatusText(job.Status))
                    .Text("failedItem", job.FailedItem)
                    .Text("error",      job.Error)
                    .Build());
            }

            _tutorialBusy = true;
            if (_commandDispatcher != null) _commandDispatcher.ScenarioRunOwner = "チュートリアルの作成";
            Debug.Log($"[ScenarioTutorial] 開始: id={job.Id} scenario={root.Name} shots={m.Shots.Count} images={m.Images.Count} out={outPath}");
            PlayerCaptureRunner.Instance.Run(GuardTutorial(job, TutorialRoutine(job, root.Name, m, cmd)));

            return CommandResult.Ok(data: reply
                .Text("status", ScenarioTutorialJobs.StatusText(job.Status))
                .Build());
        }

        private CommandResult ExecuteQueryScenarioTutorialBuild(QueryScenarioTutorialBuildCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (!_tutorialJobs.TryGet(cmd.BuildId, out var job))
                return CommandResult.Fail($"作成の受付がありません: {cmd.BuildId}");

            var b = CommandDataJson.New()
                .Text ("buildId",      job.Id)
                .Text ("status",       ScenarioTutorialJobs.StatusText(job.Status))
                .Text ("stage",        job.Stage)
                .Text ("currentItem",  job.CurrentItem)
                .Int  ("shotsDone",    job.ShotsDone)
                .Int  ("shotsTotal",   job.ShotsTotal)
                .Text ("outPath",      job.OutPath)
                .Text ("imagesFolder", job.ImagesFolder)
                .Texts("imagePaths",   job.ImagePaths)
                .Texts("warnings",     job.Warnings);
            if (job.Status == ScenarioTutorialJobs.JobStatus.Completed)
            {
                b.Int("imageCount", job.ImageCount);
                b.Int("byteCount",  (int)job.ByteCount);
            }
            if (job.Status == ScenarioTutorialJobs.JobStatus.Failed)
            {
                b.Text("failedItem", job.FailedItem);
                b.Text("error",      job.Error);
            }
            return CommandResult.Ok(data: b.Build());
        }

        // ================================================================
        // 実行前の検査
        // ================================================================

        /// <summary>流れの中の項目 1 回ぶん。</summary>
        private sealed class TutorialPass
        {
            public string Scenario;
            public ModifierStackStep Step;
            public UiRoute Route;
        }

        /// <summary>シナリオの項目を流す順に並べる。呼ばれたシナリオの中も展開する。</summary>
        private static void FlattenTutorialPasses(
            ModifierStack group, string name, List<string> stack, List<TutorialPass> passes,
            ref UiRoute prev, ScenarioTutorialManuscript m)
        {
            if (group?.Steps == null) return;
            stack.Add(name);
            foreach (var step in group.Steps)
            {
                if (step == null) continue;
                var pass = new TutorialPass { Scenario = name, Step = step };
                passes.Add(pass);

                if (step.IsExecutable)
                {
                    pass.Route = UiRouteCatalog.Choose(PanelCommandFactory.ResolveType(step.Action), prev);
                    if (pass.Route != null) prev = pass.Route;
                }

                if (!step.IsScenarioRef) continue;
                string refName = step.RefName ?? "";
                var child = ScenarioLibrary.Get(refName);
                if (child == null)
                {
                    m.Problems.Add(new TutorialProblem($"{name}:{step.ScenarioItemId}", $"呼ぶシナリオがありません: {refName}"));
                    continue;
                }
                if (stack.Contains(refName))
                {
                    m.Problems.Add(new TutorialProblem($"{name}:{step.ScenarioItemId}", $"呼び出しが循環しています: {refName}"));
                    continue;
                }
                FlattenTutorialPasses(child, refName, stack, passes, ref prev, m);
            }
            stack.RemoveAt(stack.Count - 1);
        }

        private void ResolveTutorialShots(ModifierStack root, ScenarioTutorialManuscript m)
        {
            string rootName = root.Name ?? "";
            var passes = new List<TutorialPass>();
            UiRoute prev = null;
            FlattenTutorialPasses(root, rootName, new List<string>(), passes, ref prev, m);

            foreach (var shot in m.Shots)
            {
                if (shot.ItemId.Length == 0) continue;     // 解釈の時点で問題に入っている
                if (shot.Scenario.Length == 0) shot.Scenario = rootName;

                TutorialPass pass = null;
                foreach (var p in passes)
                {
                    if (string.Equals(p.Scenario, shot.Scenario, StringComparison.Ordinal)
                        && string.Equals(p.Step.ScenarioItemId, shot.ItemId, StringComparison.Ordinal))
                    { pass = p; break; }
                }
                if (pass == null)
                {
                    bool knownScenario = false;
                    foreach (var p in passes)
                        if (string.Equals(p.Scenario, shot.Scenario, StringComparison.Ordinal)) { knownScenario = true; break; }
                    m.Problems.Add(new TutorialProblem(shot.ItemRef, knownScenario
                        ? $"シナリオ「{shot.Scenario}」にこの項目がありません"
                        : $"シナリオ「{shot.Scenario}」は「{rootName}」の流れにありません"));
                    continue;
                }

                shot.Route = pass.Route;
                ResolveTutorialTarget(shot, m);
                ResolveTutorialCursor(shot, pass, m);
            }

            ResolveTutorialOperations(m, passes, rootName);
        }

        private void ResolveTutorialTarget(TutorialShot shot, ScenarioTutorialManuscript m)
        {
            string t = shot.Target.Length > 0 ? shot.Target
                     : shot.When == TutorialShotWhen.Before ? "Window" : "MainView";

            if (TryParseCaptureTarget(t, out CaptureTarget target))
            {
                shot.CaptureTarget = target.ToString();
                return;
            }
            if (_uiAutomationRegistry.TryGetPanel(t, out _))
            {
                shot.PanelId = t;
                return;
            }
            m.Problems.Add(new TutorialProblem(shot.ItemRef, $"target が撮影範囲にもパネルにもありません: {t}"));
        }

        private void ResolveTutorialCursor(TutorialShot shot, TutorialPass pass, ScenarioTutorialManuscript m)
        {
            bool isBefore = shot.When == TutorialShotWhen.Before;

            if (shot.Cursor == TutorialCursorKind.Default)
            {
                if (!isBefore || !pass.Step.IsExecutable)
                {
                    shot.Cursor = TutorialCursorKind.None;
                }
                else if (pass.Route == null || pass.Route.Items.Length == 0)
                {
                    m.Problems.Add(new TutorialProblem(shot.ItemRef,
                        $"{pass.Step.Action} に経路の宣言が無いので、before の矢印を決められません。cursor=none か cursor=control:ID を書いてください"));
                    return;
                }
                else shot.Cursor = TutorialCursorKind.Route;
            }

            switch (shot.Cursor)
            {
                case TutorialCursorKind.Route:
                    if (pass.Route == null || pass.Route.Items.Length == 0)
                    {
                        m.Problems.Add(new TutorialProblem(shot.ItemRef, $"{Describe(pass.Step)} に経路がないので cursor=route を使えません"));
                        return;
                    }
                    shot.CursorControl = pass.Route.Items[pass.Route.Items.Length - 1];
                    break;

                case TutorialCursorKind.Run:
                    if (pass.Route == null || string.IsNullOrEmpty(pass.Route.Run))
                    {
                        m.Problems.Add(new TutorialProblem(shot.ItemRef, $"{Describe(pass.Step)} に実行のボタンの宣言がないので cursor=run を使えません"));
                        return;
                    }
                    shot.CursorControl = pass.Route.Run;
                    break;

                case TutorialCursorKind.Vertex:
                    if (shot.PanelId.Length > 0)
                        m.Problems.Add(new TutorialProblem(shot.ItemRef, "cursor=vertex はパネルの撮影では使えません（MainView / TriView / Window）"));
                    break;
            }

            if (shot.CursorControl.Length > 0
                && _uiAutomationRegistry.StatusOf(shot.CursorControl) == UiAutomationRegistry.IdStatus.Unknown)
                m.Problems.Add(new TutorialProblem(shot.ItemRef, $"ボタンや入力欄の ID がありません: {shot.CursorControl}"));

            if (isBefore && shot.Route != null)
                foreach (var id in shot.Route.Items)
                    if (_uiAutomationRegistry.StatusOf(id) == UiAutomationRegistry.IdStatus.Unknown)
                        m.Problems.Add(new TutorialProblem(shot.ItemRef, $"経路の ID がありません: {id}"));
        }

        private static string Describe(ModifierStackStep step)
            => step.IsExecutable ? step.Action : $"{step.Kind} の項目";

        private static void ResolveTutorialImages(ScenarioTutorialManuscript m, string baseDir)
        {
            foreach (var image in m.Images)
            {
                if (image.Invalid) continue;

                string path = image.Path;
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(baseDir))
                    path = Path.GetFullPath(Path.Combine(baseDir, path));

                if (!PLSandbox.TryResolveRead(path, out string full, out string reason))
                {
                    m.Problems.Add(new TutorialProblem(image.Dest, $"画像を読めません: {reason}"));
                    continue;
                }
                if (!File.Exists(full))
                {
                    m.Problems.Add(new TutorialProblem(image.Dest, $"画像のファイルがありません: {full}"));
                    continue;
                }
                image.FullPath = full;
            }
        }

        // ================================================================
        // 流す
        // ================================================================

        /// <summary>
        /// 例外で受付が pending のまま残らないよう、1 歩ずつ捕まえる。
        /// 終わったら成否によらず、カメラ・枠・流しの後始末をする。
        /// </summary>
        private IEnumerator GuardTutorial(ScenarioTutorialJobs.Job job, IEnumerator body)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) break;
                    current = body.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    ScenarioTutorialJobs.Fail(job, job.CurrentItem, $"作成の途中で例外が出ました: {e.Message}");
                    break;
                }
                yield return current;
            }

            RestoreTutorialCamera();
            RestoreTutorialFields();
            _uiAutomation?.ClearHighlights();
            DispatchTutorialRun(new StopScenarioRunCommand(TutorialModelIndex));
            if (_commandDispatcher != null) _commandDispatcher.ScenarioRunOwner = null;
            _tutorialBusy = false;

            if (job.Status == ScenarioTutorialJobs.JobStatus.Pending)
                ScenarioTutorialJobs.Fail(job, job.CurrentItem, "作成が結果を出さずに終わりました");

            Debug.Log(job.Status == ScenarioTutorialJobs.JobStatus.Completed
                ? $"[ScenarioTutorial] 完了: id={job.Id} images={job.ImageCount} bytes={job.ByteCount} out={job.OutPath}"
                : $"[ScenarioTutorial] 失敗: id={job.Id} item={job.FailedItem} reason={job.Error}");
        }

        private IEnumerator TutorialRoutine(
            ScenarioTutorialJobs.Job job, string scenarioName, ScenarioTutorialManuscript m, BuildScenarioTutorialCommand cmd)
        {
            var start = DispatchTutorialRun(new RunScenarioCommand(
                TutorialModelIndex, scenarioName, 1, prepareOnly: true,
                paramKeys: cmd.ParamKeys, paramValues: cmd.ParamValues));
            if (start == null || !start.Success)
            {
                ScenarioTutorialJobs.Fail(job, "", start?.Reason ?? "シナリオを流し始められません");
                yield break;
            }

            var pending = new List<TutorialShot>(m.Shots);
            while (true)
            {
                var run = _commandDispatcher?.ScenarioRun;
                if (run == null)
                {
                    ScenarioTutorialJobs.Fail(job, job.CurrentItem, "シナリオの流しが途中で閉じられました");
                    yield break;
                }

                // ── 済んだ項目：実行のあとで作られる欄と結果を読む ──
                foreach (var ops in new List<TutorialOps>(_tutorialOps.Values))
                {
                    if (!ops.Prepared || ops.Finished) continue;
                    if (run.StatusOf(ops.Scenario, ops.ItemId) != ScenarioItemRunStatus.Done) continue;

                    var e = FinishTutorialOps(job, ops, run);
                    while (e.MoveNext()) yield return e.Current;
                    if (job.Status != ScenarioTutorialJobs.JobStatus.Pending) yield break;
                }

                // ── after：済んだ項目を撮る ──
                foreach (var shot in new List<TutorialShot>(pending))
                {
                    if (shot.When != TutorialShotWhen.After) continue;
                    if (run.StatusOf(shot.Scenario, shot.ItemId) != ScenarioItemRunStatus.Done) continue;

                    var e = ShootTutorial(job, shot, cmd);
                    while (e.MoveNext()) yield return e.Current;
                    if (job.Status != ScenarioTutorialJobs.JobStatus.Pending) yield break;
                    pending.Remove(shot);
                }

                run = _commandDispatcher?.ScenarioRun;
                if (run == null)
                {
                    ScenarioTutorialJobs.Fail(job, job.CurrentItem, "シナリオの流しが途中で閉じられました");
                    yield break;
                }

                if (run.Stop == ScenarioRunStop.Finished) break;

                if (run.Stop == ScenarioRunStop.Failed)
                {
                    ScenarioTutorialJobs.Fail(job, RunItemName(run, scenarioName), run.StopMessage);
                    yield break;
                }

                // ── 非同期の項目：返事を待つ ──
                if (run.Stop == ScenarioRunStop.Waiting)
                {
                    job.Stage = "waitingAddon";
                    float waited = 0f;
                    while (run.Stop == ScenarioRunStop.Waiting)
                    {
                        waited += Mathf.Min(Time.unscaledDeltaTime, 1f);
                        if (waited > TutorialAddonTimeoutSeconds)
                        {
                            ScenarioTutorialJobs.Fail(job, RunItemName(run, scenarioName),
                                $"アドオンの返事が {TutorialAddonTimeoutSeconds} 秒来ませんでした");
                            yield break;
                        }
                        yield return null;
                    }
                    job.Stage = "running";
                    continue;
                }

                // ── before：項目の実行の前に撮る ──
                var step = run.CurrentStep;
                if (step != null)
                {
                    job.CurrentItem = RunItemName(run, scenarioName);
                    string scen = run.CurrentScenario, id = step.ScenarioItemId;

                    // 差し込みの文字を読み、before の画像のために欄へ値を入れる。
                    if (_tutorialOps.TryGetValue(ScenarioRunState.Key(scen, id), out var opsHere) && !opsHere.Prepared)
                    {
                        var e = PrepareTutorialOps(job, opsHere);
                        while (e.MoveNext()) yield return e.Current;
                        if (job.Status != ScenarioTutorialJobs.JobStatus.Pending) yield break;
                    }

                    foreach (var shot in new List<TutorialShot>(pending))
                    {
                        if (shot.When != TutorialShotWhen.Before) continue;
                        if (!string.Equals(shot.Scenario, scen, StringComparison.Ordinal)
                            || !string.Equals(shot.ItemId, id, StringComparison.Ordinal)) continue;

                        var e = ShootTutorial(job, shot, cmd);
                        while (e.MoveNext()) yield return e.Current;
                        if (job.Status != ScenarioTutorialJobs.JobStatus.Pending) yield break;
                        pending.Remove(shot);
                    }
                }

                var r = DispatchTutorialRun(new ContinueScenarioCommand(TutorialModelIndex, 1));
                if (r == null || !r.Success)
                {
                    ScenarioTutorialJobs.Fail(job, RunItemName(run, scenarioName), r?.Reason ?? "シナリオを進められません");
                    yield break;
                }
            }

            if (pending.Count > 0)
            {
                ScenarioTutorialJobs.Fail(job, pending[0].ItemRef, "流し終えましたが、この項目を撮れませんでした");
                yield break;
            }

            job.Stage = "writing";
            WriteTutorialMarkdown(job, m);
        }

        /// <summary>流しを占有している作成自身の呼び出し。外からの流しの操作は断られる。</summary>
        private CommandResult DispatchTutorialRun(PanelCommand cmd)
        {
            if (_commandDispatcher == null) return null;
            _commandDispatcher.ScenarioRunOwnerCalling = true;
            try { return DispatchHost(cmd); }
            finally { _commandDispatcher.ScenarioRunOwnerCalling = false; }
        }

        /// <summary>項目の名前。流し始めたシナリオの項目はそのまま、呼ばれたシナリオの項目は シナリオ名:項目 ID。</summary>
        private static string RunItemName(ScenarioRunState run, string rootName)
        {
            string id = run.CurrentStep?.ScenarioItemId ?? "";
            return string.Equals(run.CurrentScenario, rootName, StringComparison.Ordinal) ? id : $"{run.CurrentScenario}:{id}";
        }

        // ================================================================
        // 撮る
        // ================================================================

        private IEnumerator ShootTutorial(ScenarioTutorialJobs.Job job, TutorialShot shot, BuildScenarioTutorialCommand cmd)
        {
            job.Stage       = "shooting";
            job.CurrentItem = shot.ItemRef;
            _uiAutomation.ClearHighlights();

            // 項目の結果がメッシュとパネルへ行き渡るのを待つ。
            for (int i = 0; i < 3; i++) yield return null;

            // ── 撮るパネルを開く ──
            if (shot.PanelId.Length > 0)
            {
                var show = _uiAutomation.ShowPanelIfHidden(shot.PanelId);
                if (!show.Success) { ScenarioTutorialJobs.Fail(job, shot.ItemRef, show.Reason); yield break; }
            }

            // ── fit ──
            if (shot.Fit.Length > 0)
            {
                var model = ActiveProject?.CurrentModel;
                if (model == null) { ScenarioTutorialJobs.Fail(job, shot.ItemRef, "モデルがありません"); yield break; }
                var indices = TutorialDrawablesNamed(model, shot.Fit);
                if (indices.Count == 0)
                {
                    ScenarioTutorialJobs.Fail(job, shot.ItemRef, $"fit の名前の描画オブジェクトがありません: {shot.Fit}");
                    yield break;
                }

                SaveTutorialCamera();
                string fitError = null;
                for (int i = 0; i < TutorialFitMaxFrames; i++)
                {
                    fitError = FitCamera(model, indices, CameraViews.All, CommandDataJson.New());
                    if (fitError == null) break;
                    yield return null;
                }
                if (fitError != null)
                {
                    ScenarioTutorialJobs.Fail(job, shot.ItemRef, $"fit {shot.Fit}: {fitError}");
                    yield break;
                }
            }

            // ── ボタンや入力欄を出す。赤枠は before だけ ──
            // 矢印のボタンを先に出す。経路を出すとパネルの切り替えで枠が消えるので、枠は最後。
            bool routeHasCursor = shot.When == TutorialShotWhen.Before && shot.Route != null
                                  && Array.IndexOf(shot.Route.Items, shot.CursorControl) >= 0;
            if (shot.CursorControl.Length > 0 && !routeHasCursor)
            {
                var reveal = _uiAutomation.Reveal(shot.CursorControl, highlight: false);
                if (!reveal.Success) { ScenarioTutorialJobs.Fail(job, shot.ItemRef, $"{shot.CursorControl}: {reveal.Reason}"); yield break; }
            }
            if (shot.When == TutorialShotWhen.Before && shot.Route != null)
            {
                string routeError = ShowScenarioRoute(shot.Route, selectShape: true);
                if (routeError != null) { ScenarioTutorialJobs.Fail(job, shot.ItemRef, $"経路を出せません: {routeError}"); yield break; }
            }

            // ── スクロールと枠が落ち着くのを待つ ──
            var settle = SettleTutorialUi(shot);
            while (settle.MoveNext()) yield return settle.Current;

            // 経路を出すと経路の側へスクロールするので、経路の外の矢印の先は最後に見せる。
            if (shot.CursorControl.Length > 0 && !routeHasCursor && shot.When == TutorialShotWhen.Before && shot.Route != null)
            {
                _uiAutomation.ScrollControlIntoView(shot.CursorControl);
                settle = SettleTutorialUi(shot);
                while (settle.MoveNext()) yield return settle.Current;
            }

            // ── 矢印 ──
            string cursor = "none", control = "";
            float cx = -1f, cy = -1f;
            switch (shot.Cursor)
            {
                case TutorialCursorKind.Route:
                case TutorialCursorKind.Run:
                case TutorialCursorKind.Control:
                    cursor  = "control";
                    control = shot.CursorControl;
                    break;
                case TutorialCursorKind.Position:
                    cursor = "position";
                    cx = shot.CursorX;
                    cy = shot.CursorY;
                    break;
                case TutorialCursorKind.Vertex:
                    if (!TryResolveTutorialVertex(shot, out cx, out cy, out string vertexError))
                    {
                        ScenarioTutorialJobs.Fail(job, shot.ItemRef, vertexError);
                        yield break;
                    }
                    cursor = "position";
                    break;
            }

            // ── 撮る ──
            string baseName = $"shot_{shot.Order:00}_{SanitizeTutorialName(shot.ItemId)}_{(shot.When == TutorialShotWhen.Before ? "before" : "after")}";
            int maxLongEdge = shot.MaxLongEdge >= 0 ? shot.MaxLongEdge : cmd.MaxLongEdge;
            string format   = shot.Format.Length > 0 ? shot.Format : cmd.Format;

            var started = ExecuteUiCapture(new UiCaptureCommand(
                TutorialModelIndex,
                shot.CaptureTarget.Length > 0 ? shot.CaptureTarget : "Window",
                baseName, job.ImagesFolder, shot.PanelId, maxLongEdge, format, 90,
                cursor, control, cx, cy));
            if (!started.Success) { ScenarioTutorialJobs.Fail(job, shot.ItemRef, started.Reason); yield break; }

            string captureId = (string)JObject.Parse(started.Data)["captureId"];
            if (!_uiCaptureJobs.TryGet(captureId, out var capture))
            {
                ScenarioTutorialJobs.Fail(job, shot.ItemRef, "撮影の受付が見つかりません");
                yield break;
            }

            int waited = 0;
            while (capture.Status == UiAutomationCaptureJobs.JobStatus.Pending)
            {
                if (++waited > TutorialCaptureMaxFrames)
                {
                    ScenarioTutorialJobs.Fail(job, shot.ItemRef, $"撮影が {TutorialCaptureMaxFrames} フレーム終わりませんでした");
                    yield break;
                }
                yield return null;
            }

            if (capture.Status == UiAutomationCaptureJobs.JobStatus.Failed)
            {
                ScenarioTutorialJobs.Fail(job, shot.ItemRef, capture.Error);
                yield break;
            }

            if (!TryFixTutorialImageName(capture.Path, baseName, out string finalPath, out string nameError))
            {
                ScenarioTutorialJobs.Fail(job, shot.ItemRef, nameError);
                yield break;
            }
            shot.ImagePath = finalPath;
            job.ImagePaths.Add(finalPath);
            job.ShotsDone++;

            // 矢印を頼まれたのに描けていなければ、黙って進めない。画像は調べられるように残す。
            if (cursor != "none" && capture.Cursor.HasValue && !capture.Cursor.Value.Inside)
            {
                ScenarioTutorialJobs.Fail(job, shot.ItemRef,
                    $"矢印の先が撮影範囲の外です（x={capture.Cursor.Value.X:0.###}, y={capture.Cursor.Value.Y:0.###}）。target か cursor を直してください");
                yield break;
            }

            // ── 撮影専用の状態を戻す ──
            RestoreTutorialCamera();
            _uiAutomation.ClearHighlights();
            yield return null;
            job.Stage = "running";
        }

        /// <summary>保留のスクロールが無く、対象の位置が 2 フレーム続けて同じになるまで待つ。</summary>
        private IEnumerator SettleTutorialUi(TutorialShot shot)
        {
            var ids = new List<string>();
            if (shot.When == TutorialShotWhen.Before && shot.Route != null) ids.AddRange(shot.Route.Items);
            if (shot.CursorControl.Length > 0) ids.Add(shot.CursorControl);

            Rect[] last = null;
            int stable = 0;
            for (int i = 0; i < TutorialSettleMaxFrames; i++)
            {
                yield return null;

                var now = new Rect[ids.Count];
                for (int k = 0; k < ids.Count; k++)
                    if (_uiAutomationRegistry.TryGetControl(ids[k], out var c) && c.Element != null)
                        now[k] = c.Element.worldBound;

                bool same = last != null && !_uiAutomation.IsScrollPending;
                if (same)
                    for (int k = 0; k < now.Length; k++)
                        if (now[k] != last[k]) { same = false; break; }
                last = now;

                stable = same ? stable + 1 : 0;
                if (stable >= 2) yield break;
            }
        }

        private static List<int> TutorialDrawablesNamed(ModelContext model, string name)
        {
            var list = new List<int>();
            var drawables = model.DrawableMeshes;
            for (int i = 0; i < drawables.Count; i++)
                if (string.Equals(drawables[i].Name ?? "", name, StringComparison.Ordinal))
                    list.Add(drawables[i].MasterIndex);
            return list;
        }

        /// <summary>cursor=vertex を撮影範囲に対する比率へ読み替える（queryCapturePosition）。</summary>
        private bool TryResolveTutorialVertex(TutorialShot shot, out float x, out float y, out string error)
        {
            x = y = -1f;
            error = null;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { error = "モデルがありません"; return false; }

            var named = TutorialDrawablesNamed(model, shot.CursorObject);
            if (named.Count == 0) { error = $"cursor=vertex のオブジェクトがありません: {shot.CursorObject}"; return false; }
            if (named.Count > 1)  { error = $"cursor=vertex の名前のオブジェクトが {named.Count} 個あります: {shot.CursorObject}"; return false; }

            var r = ExecuteQueryCapturePosition(new QueryCapturePositionCommand(
                TutorialModelIndex, named[0], shot.CursorVertex, shot.CaptureTarget));
            if (r == null || !r.Success) { error = r?.Reason ?? "頂点の位置を読めません"; return false; }

            var o = JObject.Parse(r.Data);
            x = (float)o["x"];
            y = (float)o["y"];
            string at = $"{shot.CursorObject} の頂点 {shot.CursorVertex}";
            if (!(bool)o["inside"])  { error = $"{at} が撮影範囲の外です（x={x:0.###}, y={y:0.###}）"; return false; }
            if (!(bool)o["visible"]) { error = $"{at} は裏向きです。カメラか頂点を変えてください"; return false; }
            return true;
        }

        private static string SanitizeTutorialName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>撮影の連番を外した名前へ移す。作り直すと上書きされ、古い画像が溜まらない。</summary>
        private static bool TryFixTutorialImageName(string captured, string baseName, out string finalPath, out string error)
        {
            finalPath = null;
            error = null;
            try
            {
                string wanted = Path.Combine(Path.GetDirectoryName(captured) ?? "", baseName + Path.GetExtension(captured));
                if (!PLSandbox.TryResolveWrite(wanted, out string full, out string reason)) { error = reason; return false; }
                File.Copy(captured, full, overwrite: true);
                File.Delete(captured);
                finalPath = full;
                return true;
            }
            catch (Exception e)
            {
                error = $"画像の名前を変えられません: {e.Message}";
                return false;
            }
        }

        // ================================================================
        // fit のカメラ
        // ================================================================

        private struct TutorialCameraState
        {
            public Vector3 MainTarget;
            public float   MainDistance;
            public Vector3 TriTarget;
            public float   TriWorldHeightPerPixel;
        }

        private TutorialCameraState? _tutorialCamera;

        /// <summary>FitCamera が変える値だけを控える。</summary>
        private void SaveTutorialCamera()
        {
            if (_tutorialCamera.HasValue) return;
            var orbit = _viewportManager.PerspectiveViewport?.Orbit;
            var ortho = _viewportManager.FrontViewport?.Ortho;
            var state = new TutorialCameraState();
            if (orbit != null) { state.MainTarget = orbit.Target; state.MainDistance = orbit.Distance; }
            if (ortho != null) { state.TriTarget = ortho.Target; state.TriWorldHeightPerPixel = ortho.WorldHeightPerPixel; }
            _tutorialCamera = state;
        }

        private void RestoreTutorialCamera()
        {
            if (!_tutorialCamera.HasValue) return;
            var state = _tutorialCamera.Value;
            _tutorialCamera = null;

            var orbit = _viewportManager.PerspectiveViewport?.Orbit;
            var ortho = _viewportManager.FrontViewport?.Ortho;
            if (orbit != null) { orbit.Target = state.MainTarget; orbit.Distance = state.MainDistance; }
            if (ortho != null) { ortho.Target = state.TriTarget; ortho.WorldHeightPerPixel = state.TriWorldHeightPerPixel; }
            CommitCamera(true, true);
        }

        // ================================================================
        // 書く
        // ================================================================

        private static void WriteTutorialMarkdown(ScenarioTutorialJobs.Job job, ScenarioTutorialManuscript m)
        {
            var sb = new StringBuilder();
            int images = 0;
            foreach (var seg in m.Segments)
            {
                if (seg.Text != null) { sb.Append(seg.Text); continue; }

                if (seg.Insert != null)
                {
                    if (seg.Insert.Text == null)
                    {
                        ScenarioTutorialJobs.Fail(job, seg.Insert.ItemRef, "差し込みの文字を読めませんでした");
                        return;
                    }
                    sb.Append(seg.Insert.Text);
                    continue;
                }

                string path, alt, suffix = "", name;
                if (seg.Shot != null)  { path = seg.Shot.ImagePath;  alt = seg.Shot.Alt;  name = seg.Shot.ItemRef; }
                else                   { path = seg.Image.FullPath;  alt = seg.Image.Alt; name = seg.Image.Dest; suffix = seg.Image.TitleSuffix; }

                string mime = ScenarioTutorialManuscript.MimeOf(path);
                byte[] bytes;
                try
                {
                    if (mime == null) throw new InvalidOperationException($"画像の形式が分かりません: {path}");
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    ScenarioTutorialJobs.Fail(job, name, $"画像を読めません: {e.Message}");
                    return;
                }

                sb.Append("![").Append(alt).Append("](data:").Append(mime).Append(";base64,")
                  .Append(Convert.ToBase64String(bytes)).Append(suffix).Append(')');
                images++;
            }

            if (!PLSandbox.TryResolveWrite(job.OutPath, out string full, out string reason))
            {
                ScenarioTutorialJobs.Fail(job, "", reason);
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                byte[] data = new UTF8Encoding(false).GetBytes(sb.ToString());
                File.WriteAllBytes(full, data);
                job.ByteCount  = data.Length;
                job.ImageCount = images;
            }
            catch (Exception e)
            {
                ScenarioTutorialJobs.Fail(job, "", $"MD を書けません: {e.Message}");
                return;
            }

            job.OutPath = full;
            job.Status  = ScenarioTutorialJobs.JobStatus.Completed;
            job.Stage   = "done";
        }
    }
}
