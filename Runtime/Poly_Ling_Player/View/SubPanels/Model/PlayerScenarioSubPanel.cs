// PlayerScenarioSubPanel.cs
// 手本（シナリオ）を選んで流し、いま何をしているかを表示する。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【流し方は 2 通りだけ】
//   「流す」は先頭の段から、「続きを流す」は止まった所から。
//   途中の段を選んで実行する口は置かない。@prev と @<段の名前> は
//   前の段の結果を指すので、途中だけ撃つと別の流れの値を黙って掴む。
//
// 【止まる所】
//   指示・確認の段と、失敗した段（ObjectGroupStep.RequiresJudgment と実行結果）。
//   判定と実行は runScenario / continueScenario（PlayerCommandDispatcher.ScenarioRun.cs）が持つ。
//   パネルは送って結果を表示するだけ。ここで組み立て直すと MCP と挙動が割れる。
//
// 【1 段ずつ描き直す】
//   maxSteps=1 で呼び、呼ぶたびに画面を更新してから次を予約する。
//   まとめて流すと終わるまで画面が固まり、いま何をしているか見えない。
//   毎フレーム駆動は置かない規約なので、UIToolkit の schedule で次を予約する。
//
// 【結果を見る】
//   コマンドは RunCommand（Dispatch の戻り値が返る口）で送る。
//   SendCommand は戻り値を捨てるので、失敗しても成功と区別が付かない。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerScenarioSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>プロジェクト取得。modelIndex を封筒へ入れるために要る。</summary>
        public Func<Poly_Ling.View.IProjectView> GetProject;

        /// <summary>コマンドを実行して結果を返す口。</summary>
        public Func<PanelCommand, CommandResult> RunCommand;

        /// <summary>流している途中の状態。流していなければ null。</summary>
        public Func<ScenarioRunState> GetRun;

        private int ModelIndex => GetProject?.Invoke()?.CurrentModelIndex ?? 0;

        // ================================================================
        // 内部状態
        // ================================================================

        // UI 自動操作の ID は "scenario.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("list", Description = "手本の一覧（行番号）")]
        private ListView _listView;
        [UiControl("detail", Safety = UiSafety.ReadOnly, Description = "選んだ手本の目的・前提・成功条件")]
        private Label    _detailLabel;
        [UiControl("run", Safety = UiSafety.Destructive, Description = "選んだ手本を先頭の段から流す")]
        private Button   _btnRun;
        [UiControl("continue", Safety = UiSafety.Destructive, Description = "止まった所から続きを流す")]
        private Button   _btnContinue;
        [UiControl("stop", Safety = UiSafety.SafeWrite, Description = "流すのをやめる")]
        private Button   _btnStop;
        [UiControl("now", Safety = UiSafety.ReadOnly, Description = "いま居る手本と段")]
        private Label    _nowLabel;
        [UiControl("stopInfo", Safety = UiSafety.ReadOnly, Description = "止まった理由と次にすること")]
        private Label    _stopLabel;
        [UiControl("steps", Safety = UiSafety.ReadOnly, Description = "段の一覧と状態（表示だけ）")]
        private ListView _stepView;
        [UiControl("stepDetail", Safety = UiSafety.ReadOnly, Description = "いま居る段の中身")]
        private Label    _stepDetailLabel;
        [UiControl(Ignore = true)]
        private ScrollView _logScroll;
        [UiControl("log", Safety = UiSafety.ReadOnly, Description = "流した段の記録")]
        private Label    _logLabel;

        private readonly List<ObjectGroup> _scenarios  = new List<ObjectGroup>();
        private readonly List<string>      _labels     = new List<string>();
        private readonly List<string>      _stepLabels = new List<string>();
        private readonly List<ScenarioStepRunStatus> _stepStatus = new List<ScenarioStepRunStatus>();

        [UiControl(Ignore = true)]
        private VisualElement _root;
        private int  _selected = -1;
        private bool _ticking;

        /// <summary>1 段処理してから次を予約するまでの間。</summary>
        private const long TickIntervalMs = 60;

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight =
            _root.style.paddingTop  = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(SecLabel("シナリオ（手本）"));

            var hint = new Label(
                "手本を選んで「流す」を押すと、上の段から順に実行します。\n"
              + "「指示」「確認」の段と、失敗した段で止まります。止まったら内容を読み、「続きを流す」を押してください。");
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.fontSize     = 10;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            _listView = new ListView(_labels, 22,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _labels.Count) return;
                    l.text = _labels[i];
                });
            _listView.selectionType   = SelectionType.Single;
            _listView.style.minHeight = 70;
            _listView.style.maxHeight = 130;
            _listView.style.marginBottom = 4;
            _listView.selectionChanged += _ =>
            {
                _selected = _listView.selectedIndex;
                UpdateView();
            };
            _root.Add(_listView);

            _detailLabel = new Label();
            _detailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _detailLabel.style.fontSize     = 10;
            _detailLabel.style.marginBottom = 4;
            _root.Add(_detailLabel);

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 4;
            _btnRun      = MkBtn("流す",       OnRun);      _btnRun.style.flexGrow      = 1; _btnRun.style.marginRight      = 2;
            _btnContinue = MkBtn("続きを流す", OnContinue); _btnContinue.style.flexGrow = 1; _btnContinue.style.marginRight = 2;
            _btnStop     = MkBtn("やめる",     OnStop);     _btnStop.style.flexGrow     = 1;
            opRow.Add(_btnRun); opRow.Add(_btnContinue); opRow.Add(_btnStop);
            _root.Add(opRow);

            _nowLabel = new Label();
            _nowLabel.style.whiteSpace   = WhiteSpace.Normal;
            _nowLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nowLabel.style.marginBottom = 2;
            _root.Add(_nowLabel);

            _stopLabel = new Label();
            _stopLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stopLabel.style.marginBottom = 4;
            _root.Add(_stopLabel);

            _root.Add(SecLabel("段"));

            _stepView = new ListView(_stepLabels, 20,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _stepLabels.Count) return;
                    l.text = _stepLabels[i];
                    l.style.color = new StyleColor(StatusColor(i < _stepStatus.Count ? _stepStatus[i] : ScenarioStepRunStatus.NotRun));
                });
            _stepView.selectionType   = SelectionType.None;
            _stepView.style.minHeight = 90;
            _stepView.style.maxHeight = 200;
            _stepView.style.marginBottom = 4;
            _root.Add(_stepView);

            _stepDetailLabel = new Label();
            _stepDetailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stepDetailLabel.style.fontSize     = 10;
            _stepDetailLabel.style.marginBottom = 4;
            _root.Add(_stepDetailLabel);

            _root.Add(SecLabel("記録"));

            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.minHeight = 80;
            _logScroll.style.maxHeight = 200;
            _logLabel = new Label();
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.style.fontSize   = 10;
            _logScroll.Add(_logLabel);
            _root.Add(_logScroll);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>
        /// 手本を読み直して表示を作り直す。手本はファイルが正典で外から変わるので、
        /// パネルを開くたびに読み直す。流している途中の状態は写しを使うので影響しない。
        /// </summary>
        public void Refresh()
        {
            ScenarioLibrary.Reload();

            string keep = (_selected >= 0 && _selected < _scenarios.Count) ? _scenarios[_selected].Name : null;

            _scenarios.Clear();
            _labels.Clear();
            foreach (var g in ScenarioLibrary.GetAll())
            {
                if (g == null) continue;
                _scenarios.Add(g);
                _labels.Add($"{g.Name}  [{g.StepCount} 段]" + (string.IsNullOrEmpty(g.Goal) ? "" : $"  {g.Goal}"));
            }

            _selected = -1;
            if (keep != null)
                for (int i = 0; i < _scenarios.Count; i++)
                    if (string.Equals(_scenarios[i].Name, keep, StringComparison.Ordinal)) { _selected = i; break; }

            _listView?.Rebuild();
            if (_listView != null) _listView.selectedIndex = _selected;

            UpdateView();
        }

        private ObjectGroup Selected()
            => (_selected >= 0 && _selected < _scenarios.Count) ? _scenarios[_selected] : null;

        /// <summary>流している状態（無ければ選んだ手本）から表示を全部作り直す。</summary>
        private void UpdateView()
        {
            if (_root == null) return;

            var run = GetRun?.Invoke();
            var sel = Selected();

            // ── 手本の説明 ──
            if (sel == null) _detailLabel.text = "";
            else _detailLabel.text =
                  $"目的: {Or(sel.Goal, "（無し）")}\n"
                + $"前提: {Join(sel.Preconditions)}\n"
                + $"成功条件: {Join(sel.SuccessCriteria)}";

            // ── いま ──
            ObjectGroup shown;
            if (run != null)
            {
                shown = run.CurrentGroup;
                var st = run.CurrentStep;

                string where = string.Join(" → ", run.StackNames);
                if (run.Stop == ScenarioRunStop.Finished || st == null)
                    _nowLabel.text = $"「{run.RootName}」を流し終えました（実行したコマンド {run.ExecutedCommands} 本）";
                else
                    _nowLabel.text =
                          $"いま: {where}　{run.CurrentIndex + 1} / {shown?.StepCount ?? 0} 段目\n"
                        + $"［{KindText(st.Kind)}］ {Or(st.Purpose, st.Action)}";

                _stopLabel.text = StopText(run);
                _stopLabel.style.color = new StyleColor(StopColor(run.Stop));
            }
            else
            {
                shown = sel;
                _nowLabel.text  = sel == null ? "手本を選んでください" : $"「{sel.Name}」はまだ流していません";
                _stopLabel.text = "";
            }

            // ── 段の一覧 ──
            _stepLabels.Clear();
            _stepStatus.Clear();
            if (shown?.Steps != null)
            {
                string scenarioName = run != null ? run.CurrentScenario : shown.Name;
                for (int i = 0; i < shown.Steps.Count; i++)
                {
                    var st = shown.Steps[i];
                    if (st == null) continue;

                    var status = run != null ? run.StatusOf(scenarioName, st.ElementId) : ScenarioStepRunStatus.NotRun;
                    bool here  = run != null && i == run.CurrentIndex && run.Stop != ScenarioRunStop.Finished;

                    string what = st.IsScenarioRef ? $"「{st.RefName}」を呼ぶ" : Or(st.Purpose, st.Action);
                    _stepLabels.Add($"{(here ? "▶" : "　")}{StatusText(status)} {i + 1}. ［{KindText(st.Kind)}］ {Shorten(what, 48)}");
                    _stepStatus.Add(here && status == ScenarioStepRunStatus.NotRun ? ScenarioStepRunStatus.Running : status);
                }
            }
            _stepView.Rebuild();
            if (run != null && run.CurrentIndex >= 0 && run.CurrentIndex < _stepLabels.Count)
                _stepView.ScrollToItem(run.CurrentIndex);

            // ── いま居る段の中身 ──
            var cur = run?.CurrentStep;
            if (cur == null) _stepDetailLabel.text = "";
            else
            {
                var sb = new StringBuilder();
                sb.Append("種類: ").Append(KindText(cur.Kind)).Append('\n');
                sb.Append("内容: ").Append(Or(cur.Purpose, "（無し）"));
                if (cur.IsScenarioRef) sb.Append('\n').Append("呼ぶ手本: ").Append(cur.RefName);
                if (cur.IsExecutable)
                {
                    sb.Append('\n').Append("コマンド: ").Append(cur.Action);
                    foreach (var kv in cur.SortedArgs())
                        sb.Append('\n').Append("  ").Append(kv.Key).Append(" = ").Append(Shorten(kv.Value, 80));
                }
                _stepDetailLabel.text = sb.ToString();
            }

            // ── 記録 ──
            if (run == null) _logLabel.text = "";
            else
            {
                var log   = run.Log;
                int start = Math.Max(0, log.Count - 200);
                var sb    = new StringBuilder();
                for (int i = start; i < log.Count; i++) { if (sb.Length > 0) sb.Append('\n'); sb.Append(log[i]); }
                _logLabel.text = sb.ToString();
                _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
            }

            // ── ボタン ──
            bool canContinue = run != null && !_ticking && run.Stop != ScenarioRunStop.Finished;
            _btnRun.SetEnabled(!_ticking && sel != null);
            _btnContinue.SetEnabled(canContinue);
            _btnStop.SetEnabled(run != null);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnRun()
        {
            var sel = Selected();
            if (sel == null) { SetStop("手本を選んでください"); return; }

            var r = RunCommand?.Invoke(new RunScenarioCommand(ModelIndex, sel.Name, 1));
            if (r == null || !r.Success) { SetStop($"流せませんでした: {r?.Reason ?? "実行の口が配線されていません"}"); return; }

            AfterStep();
        }

        private void OnContinue()
        {
            var r = RunCommand?.Invoke(new ContinueScenarioCommand(ModelIndex, 1));
            if (r == null || !r.Success) { SetStop($"続けられませんでした: {r?.Reason ?? "実行の口が配線されていません"}"); return; }

            AfterStep();
        }

        private void OnStop()
        {
            _ticking = false;

            // やめると流しが消えて UpdateView からは位置が見えなくなるので、消す前に控えて表示する。
            var run = GetRun?.Invoke();
            string where = null;
            if (run != null)
            {
                var fr = run.Top;
                string scen = fr?.Group?.Name ?? run.RootName;
                int no = fr != null ? fr.Index + 1 : 0;
                int count = fr?.Group?.Steps?.Count ?? 0;
                string elem = (fr?.Group?.Steps != null && fr.Index >= 0 && fr.Index < count)
                    ? fr.Group.Steps[fr.Index]?.ElementId : "";
                where = $"やめました: 「{scen}」 {no} / {count} 段目（{elem}）の手前。実行したコマンド {run.ExecutedCommands} 本。"
                      + "それまでの結果はモデルに残っています（戻すなら Undo）。";
            }

            RunCommand?.Invoke(new StopScenarioRunCommand(ModelIndex));
            UpdateView();
            if (where != null && _stopLabel != null) _stopLabel.text = where;
        }

        /// <summary>1 段処理したあと。止まっていなければ次の 1 段を予約する。</summary>
        private void AfterStep()
        {
            var run = GetRun?.Invoke();
            _ticking = run != null && run.Stop == ScenarioRunStop.None;
            UpdateView();
            if (_ticking) _root.schedule.Execute(Tick).StartingIn(TickIntervalMs);
        }

        private void Tick()
        {
            if (!_ticking) return;

            var r = RunCommand?.Invoke(new ContinueScenarioCommand(ModelIndex, 1));
            if (r == null || !r.Success)
            {
                _ticking = false;
                UpdateView();
                SetStop($"続けられませんでした: {r?.Reason ?? "実行の口が配線されていません"}");
                return;
            }
            AfterStep();
        }

        private void SetStop(string text)
        {
            if (_stopLabel == null) return;
            _stopLabel.text = text ?? "";
            _stopLabel.style.color = new StyleColor(new Color(1f, 0.55f, 0.45f));
        }

        // ================================================================
        // 表示の文言
        // ================================================================

        private static string StopText(ScenarioRunState run)
        {
            switch (run.Stop)
            {
                case ScenarioRunStop.Judgment:
                    return run.StopStepKind == ObjectGroupStepKind.Observe
                        ? $"確認の段で止まっています。\n確かめること: {run.StopMessage}\n→ 画面で確かめてから「続きを流す」を押してください。おかしければ「やめる」を押してください。"
                        : $"指示の段で止まっています。\n指示: {run.StopMessage}\n→ 指示の作業を済ませてから「続きを流す」を押してください。次の段の値を変えるときは MCP の continueScenario を使います。";
                case ScenarioRunStop.Failed:
                    return $"失敗して止まっています。\n理由: {run.StopMessage}\n→ 原因を直してから「続きを流す」を押すと、同じ段をやり直します。";
                case ScenarioRunStop.Finished:
                    return "最後の段まで終わりました。";
                default:
                    return "流しています…";
            }
        }

        private static Color StopColor(ScenarioRunStop s)
        {
            switch (s)
            {
                case ScenarioRunStop.Judgment: return new Color(1f, 0.85f, 0.4f);
                case ScenarioRunStop.Failed:   return new Color(1f, 0.55f, 0.45f);
                case ScenarioRunStop.Finished: return new Color(0.6f, 0.9f, 0.6f);
                default:                       return new Color(0.75f, 0.85f, 1f);
            }
        }

        private static string KindText(ObjectGroupStepKind k)
        {
            switch (k)
            {
                case ObjectGroupStepKind.Command:     return "実行";
                case ObjectGroupStepKind.Note:        return "注意";
                case ObjectGroupStepKind.Instruction: return "指示・止まる";
                case ObjectGroupStepKind.Observe:     return "確認・止まる";
                case ObjectGroupStepKind.ScenarioRef: return "別の手本";
                default:                              return k.ToString();
            }
        }

        private static string StatusText(ScenarioStepRunStatus s)
        {
            switch (s)
            {
                case ScenarioStepRunStatus.Done:    return "済";
                case ScenarioStepRunStatus.Running: return "中";
                case ScenarioStepRunStatus.Stopped: return "止";
                case ScenarioStepRunStatus.Failed:  return "×";
                default:                            return "・";
            }
        }

        private static Color StatusColor(ScenarioStepRunStatus s)
        {
            switch (s)
            {
                case ScenarioStepRunStatus.Done:    return new Color(0.6f, 0.9f, 0.6f);
                case ScenarioStepRunStatus.Running: return new Color(0.75f, 0.85f, 1f);
                case ScenarioStepRunStatus.Stopped: return new Color(1f, 0.85f, 0.4f);
                case ScenarioStepRunStatus.Failed:  return new Color(1f, 0.55f, 0.45f);
                default:                            return new Color(0.75f, 0.75f, 0.75f);
            }
        }

        // ================================================================
        // 小物
        // ================================================================

        private static string Or(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;

        private static string Shorten(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";

        private static string Join(List<string> values)
            => (values == null || values.Count == 0) ? "（無し）" : string.Join(" / ", values);

        private static Button MkBtn(string t, Action a)
        {
            var b = new Button(a) { text = t };
            b.style.height = 22;
            return b;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
