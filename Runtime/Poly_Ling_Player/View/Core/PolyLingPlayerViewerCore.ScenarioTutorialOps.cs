// PolyLingPlayerViewerCore.ScenarioTutorialOps.cs
// Player ビューアのコア：チュートリアルの差し込み（[[route:]]・[[args:]]・[[run:]]）と下書き（exportScenarioTutorialDraft）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【正本】PolyLing_チュートリアル計画.md 3.3・3.4・3.5・4.2・段 5
//
// 【いつ読むか】
//   画面の文字は UiOperationCatalog（queryCommandUiOperation と同じ）からその時点の表示を読む。
//   欄はパネルを開くまで作られず、@prev などの値は前の項目の結果で決まるので、項目の実行の直前に読む。
//   処理列の引数欄は実行のあとで項目を選ぶと出るので、実行のあとで読む。
//   実行前の検査は宣言だけを見る（項目 ID・経路・実行のボタン・引数の宣言）。
//
// 【初期値のままの欄を出さない】
//   比べる相手は、読者の画面にあるはずの値。
//     ・前の項目で読者が入れた値（_tutorialHumanValues。パネルの値は残るので、前の値から変えるときは書く）
//     ・なければこの Play で最初に読んだときの欄の値（_tutorialInitialValues）
//   作成の前に手でパネルを変えていると、その値が初期値に見える。確実に作るには Play を入れ直す。
//   引数はコマンドの既定値まで広げて比べる（Create → ToArgs）。項目に書いていない引数でも、
//   パネルの初期値と違えば書かないとコマンドと同じ結果にならない。
//
// 【when=before の画像に値を入れて写す（6 章の決定）】
//   実行の直前の欄に値を入れる。本文の [[args:]] と画像が食い違わず、読者が押す直前の画面と同じになる。
//   シナリオの実行は欄を読まないので、結果は変わらない。作成の終わりに元の値へ戻す。
//   入れないもの：可変の引数の欄（処理列）。実行の前は前の項目の欄なので、入れるとその項目を書き換える。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>画面の操作を読む項目 1 つ。</summary>
        private sealed class TutorialOps
        {
            public string Scenario = "", ItemId = "", ItemRef = "";
            public ModifierStackStep Step;
            public Type CommandType;
            public UiRoute Route;
            public PLUiManualAttribute Manual;
            public readonly List<TutorialInsert> Inserts = new List<TutorialInsert>();
            public bool HasArgsInsert;
            public IReadOnlyDictionary<string, string> Effective;
            public readonly List<string> Rows = new List<string>();
            public bool Prepared, Finished;
        }

        /// <summary>この作成で画面の操作を読む項目。鍵は「シナリオ名/項目 ID」。</summary>
        private readonly Dictionary<string, TutorialOps> _tutorialOps = new Dictionary<string, TutorialOps>(StringComparer.Ordinal);

        /// <summary>この Play で最初に読んだ欄の値。作成をまたいで残す。</summary>
        private readonly Dictionary<string, string> _tutorialInitialValues = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>この作成で読者が欄に入れたはずの値。</summary>
        private readonly Dictionary<string, string> _tutorialHumanValues = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>入れた欄と元の値。作成の終わりに逆順で戻す。</summary>
        private readonly List<KeyValuePair<string, string>> _tutorialFilled = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// 人の操作への置き換えが決めた欄。次の項目の同じ欄の引数より置き換えを優先する
        /// （空のモデルの項目と読み込みの項目は、画面では読み込み 1 回にまとまる）。
        /// </summary>
        private readonly HashSet<string> _tutorialManualHold = new HashSet<string>(StringComparer.Ordinal);

        // ================================================================
        // 実行前の検査
        // ================================================================

        private static TutorialPass FindTutorialPass(List<TutorialPass> passes, string scenario, string itemId)
        {
            foreach (var p in passes)
                if (string.Equals(p.Scenario, scenario, StringComparison.Ordinal)
                    && string.Equals(p.Step.ScenarioItemId, itemId, StringComparison.Ordinal))
                    return p;
            return null;
        }

        private TutorialOps TutorialOpsOf(TutorialPass pass, string itemRef)
        {
            string key = ScenarioRunState.Key(pass.Scenario, pass.Step.ScenarioItemId);
            if (_tutorialOps.TryGetValue(key, out var ops)) return ops;

            var t = PanelCommandFactory.ResolveType(pass.Step.Action);
            ops = new TutorialOps
            {
                Scenario    = pass.Scenario,
                ItemId      = pass.Step.ScenarioItemId,
                ItemRef     = itemRef,
                Step        = pass.Step,
                CommandType = t,
                Route       = pass.Route,
                Manual      = t?.GetCustomAttribute<PLUiManualAttribute>(inherit: false),
            };
            _tutorialOps[key] = ops;
            return ops;
        }

        /// <summary>差し込みの項目 ID と宣言を確かめ、読む項目を決める。何も実行しない。</summary>
        private void ResolveTutorialOperations(ScenarioTutorialManuscript m, List<TutorialPass> passes, string rootName)
        {
            _tutorialOps.Clear();

            foreach (var insert in m.Inserts)
            {
                if (insert.ItemId.Length == 0) continue;   // 解釈の時点で問題に入っている
                if (insert.Scenario.Length == 0) insert.Scenario = rootName;

                var pass = FindTutorialPass(passes, insert.Scenario, insert.ItemId);
                if (pass == null)
                {
                    bool known = false;
                    foreach (var p in passes)
                        if (string.Equals(p.Scenario, insert.Scenario, StringComparison.Ordinal)) { known = true; break; }
                    m.Problems.Add(new TutorialProblem(insert.ItemRef, known
                        ? $"シナリオ「{insert.Scenario}」にこの項目がありません"
                        : $"シナリオ「{insert.Scenario}」は「{rootName}」の流れにありません"));
                    continue;
                }

                if (!pass.Step.IsExecutable)
                {
                    m.Problems.Add(new TutorialProblem(insert.ItemRef, "実行する項目ではないので、画面の操作がありません"));
                    continue;
                }

                var ops = TutorialOpsOf(pass, insert.ItemRef);
                ops.Inserts.Add(insert);
                if (insert.Kind == TutorialInsertKind.Args) ops.HasArgsInsert = true;

                string why = TutorialDeclarationProblem(insert.Kind, ops);
                if (why != null) m.Problems.Add(new TutorialProblem(insert.ItemRef, why));
            }

            foreach (var shot in m.Shots)
            {
                if (shot.When != TutorialShotWhen.Before || shot.ItemId.Length == 0) continue;
                var pass = FindTutorialPass(passes, shot.Scenario, shot.ItemId);
                if (pass != null && pass.Step.IsExecutable) TutorialOpsOf(pass, shot.ItemRef);
            }
        }

        private string TutorialDeclarationProblem(TutorialInsertKind kind, TutorialOps ops)
        {
            string action = ops.Step.Action;
            bool routed = ops.Route != null && ops.Route.Items.Length > 0;
            if (ops.CommandType == null) return $"コマンドがありません: {action}";

            switch (kind)
            {
                case TutorialInsertKind.Route:
                    if (!routed && ops.Manual == null) return $"{action} に画面の経路の宣言がありません";
                    break;

                case TutorialInsertKind.Run:
                    if (!routed && ops.Manual != null) return "人の操作への置き換えの項目なので、実行のボタンはありません";
                    if (!routed || string.IsNullOrEmpty(ops.Route.Run)) return $"{action} に実行のボタンの宣言がありません";
                    break;

                case TutorialInsertKind.Args:
                    if (!routed && ops.Manual == null) return $"{action} に画面の経路の宣言がありません";
                    if (ops.Manual == null)
                    {
                        var missing = PanelCommandFactoryAudit.UndeclaredUiArgs(ops.CommandType);
                        if (missing.Count > 0) return $"{action} の引数に入力欄の宣言がありません: {string.Join(", ", missing)}";
                    }
                    break;
            }

            if (routed)
            {
                foreach (var id in ops.Route.Items)
                    if (_uiAutomationRegistry.StatusOf(id) == UiAutomationRegistry.IdStatus.Unknown)
                        return $"経路の ID がありません: {id}";
            }
            else if (ops.Manual != null)
            {
                foreach (var id in ops.Manual.Items)
                    if (_uiAutomationRegistry.StatusOf(id) == UiAutomationRegistry.IdStatus.Unknown)
                        return $"置き換えの ID がありません: {id}";
            }
            return null;
        }

        // ================================================================
        // 項目の実行の前：経路を開き、文字と値を読み、欄に入れる
        // ================================================================

        private IEnumerator PrepareTutorialOps(ScenarioTutorialJobs.Job job, TutorialOps ops)
        {
            ops.Prepared    = true;
            job.Stage       = "reading";
            job.CurrentItem = ops.ItemRef;
            bool must = ops.Inserts.Count > 0;

            // ── 開く ──
            string openError = null;
            bool routed = ops.Route != null && ops.Route.Items.Length > 0;
            if (routed)
                openError = ShowScenarioRoute(ops.Route, selectShape: true);
            else if (ops.Manual != null)
                foreach (var id in ops.Manual.Items)
                {
                    var r = _uiAutomation.Reveal(id, highlight: false);
                    if (!r.Success && openError == null) openError = $"{id}: {r.Reason}";
                }
            if (openError != null && must)
            {
                ScenarioTutorialJobs.Fail(job, ops.ItemRef, $"経路を開けません: {openError}");
                yield break;
            }

            for (int i = 0; i < TutorialSettleMaxFrames; i++)
            {
                yield return null;
                if (i >= 2 && !_uiAutomation.IsScrollPending) break;
            }
            _uiAutomation.ClearHighlights();

            // ── 経路と実行のボタンの文字 ──
            foreach (var insert in ops.Inserts)
            {
                string text = null, problem = null;
                if (insert.Kind == TutorialInsertKind.Route)
                    UiOperationCatalog.TryRouteText(_uiAutomationRegistry,
                        routed ? ops.Route.Items : ops.Manual.Items, routed ? ops.Route.Run : null, out text, out problem);
                else if (insert.Kind == TutorialInsertKind.Run)
                    UiOperationCatalog.TryButtonText(_uiAutomationRegistry, ops.Route.Run, out text, out problem);
                else
                    continue;

                if (problem != null)
                {
                    ScenarioTutorialJobs.Fail(job, ops.ItemRef, problem);
                    yield break;
                }
                insert.Text = text;
            }

            // ── 入力欄 ──
            var hold = new HashSet<string>(_tutorialManualHold, StringComparer.Ordinal);
            _tutorialManualHold.Clear();

            var fields = new List<UiOperationCatalog.ArgOp>();
            if (ops.Manual == null && ops.CommandType != null
                && _commandDispatcher != null && _commandDispatcher.TryResolveCurrentStepArgs(out var args, out string resolveWhy))
            {
                var cmd = PanelCommandFactory.Create(ops.Step.Action, TutorialModelIndex, args, out string createWhy);
                if (cmd == null)
                {
                    if (ops.HasArgsInsert)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, createWhy);
                        yield break;
                    }
                }
                else
                {
                    ops.Effective = PanelCommandFactory.ToArgs(cmd);
                    fields.AddRange(UiOperationCatalog.ReadArgs(_uiAutomationRegistry, ops.CommandType, ops.Effective));
                }
            }
            else if (ops.Manual == null && ops.HasArgsInsert)
            {
                ScenarioTutorialJobs.Fail(job, ops.ItemRef, "項目の引数を読めません");
                yield break;
            }

            if (ops.CommandType != null)
                fields.AddRange(UiOperationCatalog.ReadFixed(_uiAutomationRegistry, ops.CommandType));

            foreach (var op in fields)
            {
                bool isFixed = op.Arg.Length == 0;
                if (!isFixed && hold.Contains(op.Control)) continue;

                // 別の欄の値で隠れている欄は、読者の画面に無いので書かない（前の欄へ値を入れて出ることはある）。
                if (op.Built && _uiAutomationRegistry.TryGetControl(op.Control, out var entry)
                    && UiAutomationService.IsHiddenByPanel(entry.Element)) continue;

                if (!op.Built || op.Problem.Length > 0)
                {
                    string message = $"{(isFixed ? op.Control : op.Arg)}: {op.Problem}";
                    if (ops.HasArgsInsert)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, message);
                        yield break;
                    }
                    job.Warnings.Add($"{ops.ItemRef}: {message}");
                    continue;
                }

                if (!_tutorialInitialValues.ContainsKey(op.Control)) _tutorialInitialValues[op.Control] = op.Current;
                string baseline = _tutorialHumanValues.TryGetValue(op.Control, out string human)
                    ? human : _tutorialInitialValues[op.Control];
                _tutorialHumanValues[op.Control] = op.UiValue;

                if (!UiOperationCatalog.SameValue(baseline, op.UiValue))
                {
                    if (op.Label.Length == 0 && ops.HasArgsInsert)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, $"{op.Control} の見出しを読めません");
                        yield break;
                    }
                    ops.Rows.Add(TutorialRow(op.Label, op.ValueLabel));
                }

                if (!UiOperationCatalog.SameValue(op.Current, op.UiValue))
                {
                    var set = _uiAutomation.SetValue(op.Control, op.UiValue, allowDestructive: false);
                    if (set.Success) _tutorialFilled.Add(new KeyValuePair<string, string>(op.Control, op.Current));
                    else job.Warnings.Add($"{ops.ItemRef}: 「{op.Label}」へ値を入れられません: {set.Reason}");
                }

                if (isFixed && ops.Manual != null) _tutorialManualHold.Add(op.Control);
            }

            job.Stage = "running";
        }

        private static string TutorialRow(string label, string value)
            => $"- {label}：{(string.IsNullOrEmpty(value) ? "（空）" : value)}";

        // ================================================================
        // 項目の実行のあと：実行のあとで作られる欄と結果を読み、差し込みを仕上げる
        // ================================================================

        private IEnumerator FinishTutorialOps(ScenarioTutorialJobs.Job job, TutorialOps ops, ScenarioRunState run)
        {
            ops.Finished = true;
            if (!ops.HasArgsInsert) yield break;

            job.Stage       = "reading";
            job.CurrentItem = ops.ItemRef;

            var after = new List<string>();

            // ── 可変の引数の欄 ──
            var list = ops.CommandType?.GetCustomAttribute<PLUiArgListAttribute>(inherit: false);
            if (list != null && ops.Effective != null
                && ops.Effective.TryGetValue(list.KeysArg, out string keysText) && !string.IsNullOrEmpty(keysText))
            {
                if (!string.IsNullOrEmpty(list.SelectLast))
                {
                    var reveal = _uiAutomation.Reveal(list.SelectLast, highlight: false);
                    if (!reveal.Success)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, $"{list.SelectLast}: {reveal.Reason}");
                        yield break;
                    }
                    yield return null;

                    int count = _uiAutomationRegistry.TryGetControl(list.SelectLast, out var listControl)
                                && listControl.Element is BaseListView lv ? lv.itemsSource?.Count ?? 0 : 0;
                    if (count == 0)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, "処理の項目が一覧にありません。編集対象のオブジェクトが違います");
                        yield break;
                    }
                    var select = _uiAutomation.SetValue(list.SelectLast, (count - 1).ToString(), allowDestructive: false);
                    if (!select.Success)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef, $"{list.SelectLast}: {select.Reason}");
                        yield break;
                    }
                    yield return null;
                }

                foreach (var op in UiOperationCatalog.ReadListArgs(_uiAutomationRegistry, ops.CommandType, ops.Effective))
                {
                    if (!op.Built || op.Problem.Length > 0 || op.Label.Length == 0)
                    {
                        ScenarioTutorialJobs.Fail(job, ops.ItemRef,
                            $"{op.Arg}: {(op.Problem.Length > 0 ? op.Problem : "見出しを読めません")}");
                        yield break;
                    }
                    string fallback = UiOperationCatalog.ListArgDefault(ops.CommandType, ops.Effective, op.Arg);
                    if (fallback != null && UiOperationCatalog.SameValue(fallback, op.Raw)) continue;
                    after.Add(TutorialRow(op.Label, op.ValueLabel));
                }
            }

            // ── 人が選ぶもの（置き換え）──
            if (ops.Manual != null && !string.IsNullOrEmpty(ops.Manual.ResultList)
                && run.Results.TryGetValue(ScenarioRunState.Key(ops.Scenario, ops.ItemId), out var result)
                && !string.IsNullOrEmpty(result.Data))
            {
                var o = JObject.Parse(result.Data)[ops.Manual.ResultList];
                if (o is JArray arr)
                    foreach (var e in arr) ops.Rows.Add($"- {(string)e}");
            }

            // ── 差し込みの文字 ──
            var text = new StringBuilder();
            foreach (var row in ops.Rows) text.Append(text.Length > 0 ? "\n" : "").Append(row);
            if (after.Count > 0)
            {
                string runText = "実行のボタン";
                if (ops.Route != null && !string.IsNullOrEmpty(ops.Route.Run)
                    && UiOperationCatalog.TryButtonText(_uiAutomationRegistry, ops.Route.Run, out string label, out _))
                    runText = label;
                text.Append(text.Length > 0 ? "\n" : "").Append($"- {runText}を押したあとで、次を入れる");
                foreach (var row in after) text.Append("\n  ").Append(row);
            }
            if (text.Length == 0) text.Append("初期値のままで進めます。");

            foreach (var insert in ops.Inserts)
                if (insert.Kind == TutorialInsertKind.Args) insert.Text = text.ToString();

            job.Stage = "running";
        }

        /// <summary>入れた欄を元の値へ戻す。戻せない欄（パネルが作り直され、選択肢が変わった）は飛ばす。</summary>
        private void RestoreTutorialFields()
        {
            for (int i = _tutorialFilled.Count - 1; i >= 0; i--)
            {
                var r = _uiAutomation?.SetValue(_tutorialFilled[i].Key, _tutorialFilled[i].Value, allowDestructive: false);
                if (r != null && !r.Success)
                    Debug.LogWarning($"[ScenarioTutorial] 欄を元に戻せません: {_tutorialFilled[i].Key}: {r.Reason}");
            }
            _tutorialFilled.Clear();
            _tutorialHumanValues.Clear();
            _tutorialManualHold.Clear();
        }

        // ================================================================
        // 下書き
        // ================================================================

        private CommandResult ExecuteExportScenarioTutorialDraft(ExportScenarioTutorialDraftCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            var root = ScenarioLibrary.Get(cmd.ScenarioName);
            if (root == null) return CommandResult.Fail($"シナリオがありません: {cmd.ScenarioName}");

            var draft = ScenarioTutorialDraft.Build(root);
            if (draft.Error != null) return CommandResult.Fail(draft.Error);

            string written = "";
            if (!string.IsNullOrEmpty(cmd.OutPath))
            {
                if (!string.Equals(Path.GetExtension(cmd.OutPath), ".md", StringComparison.OrdinalIgnoreCase))
                    return CommandResult.Fail($"outPath の拡張子は .md です: {cmd.OutPath}");
                if (!PLSandbox.TryResolveWrite(cmd.OutPath, out written, out string reason))
                    return CommandResult.Fail(reason);
                try
                {
                    string dir = Path.GetDirectoryName(written);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(written, new UTF8Encoding(false).GetBytes(draft.Markdown));
                }
                catch (Exception e)
                {
                    return CommandResult.Fail($"下書きを書けません: {e.Message}");
                }
            }

            return CommandResult.Ok(data: CommandDataJson.New()
                .Text ("markdown",     draft.Markdown)
                .Text ("outPath",      written)
                .Int  ("itemCount",    draft.ItemCount)
                .Int  ("routedCount",  draft.RoutedCount)
                .Int  ("manualCount",  draft.ManualCount)
                .Int  ("noRouteCount", draft.NoRouteCount)
                .Texts("gaps",         draft.Gaps)
                .Build());
        }
    }
}
