// PlayerScenarioGuideBar.cs
// シナリオの案内バー。右ペインの中区画に、シナリオを流している間だけ出す。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【なぜ中区画か】
//   項目のコマンドに当たるボタンを見せるため、下区画のパネルを切り替える（uiReveal と同じ処理）。
//   下区画は排他なのでシナリオパネルは隠れる。流しの駆動と表示をシナリオパネルに置くと、
//   隠れた所で止まって見えなくなる。中区画は下区画を切り替えても消えない（作業空間バーと同じ置き場）。
//
// 【見せ方】
//   枠なし           … 今まで通り。枠を出さずに 1 項目ずつ流す。
//   今なにをしているか … コマンドの項目の前に、その項目の経路のボタンや入力欄へ枠を出し、少し待ってから実行する。
//   次に押すボタン     … 指示の項目で止まったとき、次のコマンドの項目の経路へ枠を出す。
//   人間の入力待ち     … コマンドの項目で経路へ枠を出して止まり、人が同じコマンドを実行したら先へ進む
//                       （判定はディスパッチャ。PlayerCommandDispatcher.ScenarioRun.cs の AcceptUserStepIfAwaiting）。
//                       経路の無い項目・UI なしの項目はそのまま実行する。
//   経路は前の項目の経路に近いものを選ぶ（ScenarioRunState.CurrentRoute → UiRouteCatalog.Choose）。
//
// 【1 項目ずつ描き直す】
//   continueScenario を maxSteps=1 で呼び、呼ぶたびに画面を更新してから次を予約する。
//   毎フレーム駆動は置かない規約なので、UIToolkit の schedule で次を予約する。
//
// 【判定と実行はディスパッチャ】
//   止まる所・項目の実行・入力待ちの受け付けは runScenario / continueScenario とディスパッチャが持つ。
//   バーは送って表示するだけ。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerScenarioGuideBar
    {
        /// <summary>見せ方。</summary>
        public enum GuideMode
        {
            None     = 0,
            Doing    = 1,
            NextStep = 2,
            AwaitUser = 3,
        }

        private static readonly List<string> ModeLabels = new List<string>
        {
            "枠なし", "今なにをしているか", "次に押すボタン", "人間の入力待ち",
        };

        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<int> GetModelIndex;
        public Func<PanelCommand, CommandResult> RunCommand;
        public Func<ScenarioRunState> GetRun;
        public Func<bool> BeginAwaitUser;
        public Action     EndAwaitUser;

        /// <summary>経路のボタンや入力欄を見える状態にして枠で囲む。うまくいかないボタンや入力欄があれば理由（null なら全部できた）。</summary>
        public Func<UiRoute, string> ShowRoute;

        /// <summary>枠を全部消す。</summary>
        public Action ClearRoute;

        /// <summary>流しの状態が変わったとき（シナリオパネルの表示を合わせる）。</summary>
        public Action OnRunChanged;

        /// <summary>バーの表示・非表示を変えたあと、右ペインの区画を組み直す。</summary>
        public Action RefreshAreas;

        // ================================================================
        // 内部状態
        // ================================================================

        // UI 自動操作の ID は "scenarioGuide.<下の Id>"。
        [UiControl(Ignore = true)]
        private VisualElement _section;
        [UiControl("now", Safety = UiSafety.ReadOnly, Description = "いま居るシナリオと項目")]
        private Label _nowLabel;
        [UiControl("route", Safety = UiSafety.ReadOnly, Description = "いま枠で示している経路とボタンで表せない操作の説明")]
        private Label _routeLabel;
        [UiControl("stopInfo", Safety = UiSafety.ReadOnly, Description = "止まった理由・待っていること")]
        private Label _stopLabel;
        [UiControl("mode", Description = "見せ方（枠なし / 今なにをしているか / 次に押すボタン / 人間の入力待ち）")]
        private DropdownField _modeField;
        [UiControl("continue", Safety = UiSafety.Destructive, Description = "止まった所から続きを流す。入力待ちの項目はシナリオどおりに実行して進む")]
        private Button _btnContinue;
        [UiControl("stop", Safety = UiSafety.SafeWrite, Description = "流すのをやめて案内を閉じる")]
        private Button _btnStop;

        private GuideMode _mode = GuideMode.Doing;
        private bool _ticking;
        private bool _awaiting;
        private string _routeError;
        private UiRoute _shownRoute;

        /// <summary>今なにをしているか：枠を出した項目（同じ項目で 2 度出さない）。</summary>
        private string _shownStepKey;

        private const long TickIntervalMs = 60;
        private const long DoingDelayMs   = 1200;

        /// <summary>流している途中か（項目を予約しているか、人の入力を待っているか）。</summary>
        public bool IsBusy => _ticking || _awaiting;

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement section)
        {
            _section = section;

            var title = new Label("シナリオの案内");
            title.style.color    = new StyleColor(new Color(0.65f, 0.8f, 1f));
            title.style.fontSize = 10;
            title.style.marginBottom = 2;
            section.Add(title);

            _nowLabel = new Label();
            _nowLabel.style.whiteSpace = WhiteSpace.Normal;
            _nowLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nowLabel.style.marginBottom = 2;
            section.Add(_nowLabel);

            _routeLabel = new Label();
            _routeLabel.style.whiteSpace = WhiteSpace.Normal;
            _routeLabel.style.fontSize   = 10;
            _routeLabel.style.color      = new StyleColor(new Color(1f, 0.6f, 0.6f));
            _routeLabel.style.marginBottom = 2;
            section.Add(_routeLabel);

            _stopLabel = new Label();
            _stopLabel.style.whiteSpace = WhiteSpace.Normal;
            _stopLabel.style.fontSize   = 10;
            _stopLabel.style.marginBottom = 2;
            section.Add(_stopLabel);

            _modeField = new DropdownField("見せ方", ModeLabels, (int)_mode);
            // 既定の見出し幅だと中区画の幅では選択肢が 1 文字しか見えないので、見出しを詰める。
            _modeField.labelElement.style.minWidth = 0;
            _modeField.labelElement.style.width    = 60;
            _modeField.RegisterValueChangedCallback(e =>
            {
                int i = ModeLabels.IndexOf(e.newValue);
                if (i < 0) return;
                SetMode((GuideMode)i);
            });
            section.Add(_modeField);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2;
            _btnContinue = new Button(Continue) { text = "続きを流す" };
            _btnContinue.style.flexGrow = 1;
            _btnContinue.style.marginRight = 2;
            _btnStop = new Button(Stop) { text = "やめる" };
            _btnStop.style.flexGrow = 1;
            row.Add(_btnContinue);
            row.Add(_btnStop);
            section.Add(row);

            SetVisible(false);
        }

        // ================================================================
        // 操作（シナリオパネルとバーのボタンから）
        // ================================================================

        /// <summary>シナリオを先頭から流し始める。うまくいかなければ理由。</summary>
        public string Start(string scenarioName)
        {
            CancelAwait();
            _shownStepKey = null;
            var r = RunCommand?.Invoke(new RunScenarioCommand(ModelIndex, scenarioName, 1, prepareOnly: true));
            if (r == null || !r.Success) return r?.Reason ?? "実行の口が配線されていません";
            SetVisible(true);
            Advance();
            return null;
        }

        /// <summary>止まった所から続ける。</summary>
        public void Continue()
        {
            var run = GetRun?.Invoke();
            if (run == null || run.Stop == ScenarioRunStop.Finished) return;
            CancelAwait();
            ExecStep();
        }

        /// <summary>流すのをやめて案内を閉じる。</summary>
        public void Stop()
        {
            _ticking = false;
            CancelAwait();
            ClearShown();
            RunCommand?.Invoke(new StopScenarioRunCommand(ModelIndex));
            SetVisible(false);
            OnRunChanged?.Invoke();
        }

        /// <summary>入力待ちで人が項目を済ませた（ディスパッチャから）。</summary>
        public void OnUserStepAccepted()
        {
            if (!_awaiting) return;
            _awaiting = false;
            ClearShown();
            ScheduleAdvance(TickIntervalMs);
        }

        private void SetMode(GuideMode m)
        {
            if (_mode == m) return;
            _mode = m;
            // 待ち方が変わるので、今の項目から決め直す。
            if (_awaiting)
            {
                CancelAwait();
                ClearShown();
                Advance();
            }
            else UpdateView();
        }

        // ================================================================
        // 進める
        // ================================================================

        private int ModelIndex => GetModelIndex?.Invoke() ?? 0;

        /// <summary>いま居る項目を見て、枠を出す・待つ・実行するのどれかをする。</summary>
        private void Advance()
        {
            _ticking = false;
            var run = GetRun?.Invoke();
            if (run == null) { SetVisible(false); OnRunChanged?.Invoke(); return; }

            if (run.Stop != ScenarioRunStop.None)
            {
                // 止まった。次に押すボタンなら、指示の項目で次のコマンドの項目の経路を示す。
                ClearShown();
                if (_mode == GuideMode.NextStep && run.Stop == ScenarioRunStop.Judgment)
                    Show(NextCommandRoute(run));
                UpdateView();
                return;
            }

            var step  = run.CurrentStep;
            var route = step != null && step.IsExecutable ? run.CurrentRoute : null;
            string key = StepKey(run);

            if (route != null && _mode == GuideMode.Doing && _shownStepKey != key)
            {
                _shownStepKey = key;
                Show(route);
                UpdateView();
                _ticking = true;
                _section.schedule.Execute(() => { if (_ticking) ExecStep(); }).StartingIn(DoingDelayMs);
                return;
            }

            if (route != null && _mode == GuideMode.AwaitUser && BeginAwaitUser != null && BeginAwaitUser())
            {
                _awaiting = true;
                Show(route);
                UpdateView();
                return;
            }

            ExecStep();
        }

        /// <summary>1 項目処理して、次を予約する。</summary>
        private void ExecStep()
        {
            _ticking = false;
            var r = RunCommand?.Invoke(new ContinueScenarioCommand(ModelIndex, 1));
            // 示していた枠はこの項目（または止まった指示の項目）のもの。項目が進んだので消す。
            ClearShown();
            if (r == null || !r.Success)
            {
                UpdateView();
                _stopLabel.text = $"続けられませんでした: {r?.Reason ?? "実行の口が配線されていません"}";
                return;
            }
            ScheduleAdvance(TickIntervalMs);
        }

        private void ScheduleAdvance(long ms)
        {
            _ticking = true;
            UpdateView();
            _section.schedule.Execute(() => { if (_ticking) Advance(); }).StartingIn(ms);
        }

        private void CancelAwait()
        {
            if (!_awaiting) return;
            _awaiting = false;
            EndAwaitUser?.Invoke();
        }

        /// <summary>止まった項目より後で、最初のコマンドの項目の経路。無ければ null。</summary>
        private static UiRoute NextCommandRoute(ScenarioRunState run)
        {
            var g = run.CurrentGroup;
            if (g?.Steps == null) return null;
            for (int i = run.CurrentIndex + 1; i < g.Steps.Count; i++)
            {
                var st = g.Steps[i];
                if (st == null || !st.IsExecutable) continue;
                return UiRouteCatalog.Choose(PanelCommandFactory.ResolveType(st.Action), run.PrevRoute);
            }
            return null;
        }

        private static string StepKey(ScenarioRunState run)
            => $"{run.CurrentScenario}/{run.CurrentStep?.ScenarioItemId}/{run.ExecutedCommands}";

        private void Show(UiRoute route)
        {
            _shownRoute = route;
            _routeError = route != null ? ShowRoute?.Invoke(route) : null;
        }

        private void ClearShown()
        {
            if (_shownRoute == null) return;
            _shownRoute = null;
            _routeError = null;
            ClearRoute?.Invoke();
        }

        // ================================================================
        // 表示
        // ================================================================

        private void SetVisible(bool on)
        {
            if (_section == null) return;
            var want = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (_section.style.display == want) return;
            _section.style.display = want;
            RefreshAreas?.Invoke();
        }

        private void UpdateView()
        {
            OnRunChanged?.Invoke();
            if (_section == null) return;

            var run = GetRun?.Invoke();
            if (run == null) { SetVisible(false); return; }

            var st = run.CurrentStep;
            if (run.Stop == ScenarioRunStop.Finished || st == null)
                _nowLabel.text = $"「{run.RootName}」を流し終えました（実行したコマンド {run.ExecutedCommands} 本）";
            else
                _nowLabel.text = $"{run.CurrentScenario}　{run.CurrentIndex + 1} / {run.CurrentGroup?.StepCount ?? 0} 番目\n"
                               + (string.IsNullOrEmpty(st.Purpose) ? st.Action : st.Purpose);

            if (_shownRoute == null) _routeLabel.text = "";
            else
            {
                string t = $"赤枠: {_shownRoute.Name}";
                if (!string.IsNullOrEmpty(_shownRoute.Note)) t += $"\n{_shownRoute.Note}";
                if (!string.IsNullOrEmpty(_routeError)) t += $"\n（示せないボタンや入力欄があります: {_routeError}）";
                _routeLabel.text = t;
            }

            string stop;
            if (_awaiting)
                stop = "人間の入力待ち：赤枠の操作をしてください。済んだら次の項目へ進みます。シナリオどおりに進めるなら「続きを流す」。";
            else switch (run.Stop)
            {
                case ScenarioRunStop.Judgment:
                    stop = run.StopStepKind == ObjectGroupStepKind.Observe
                        ? $"確認の項目で止まっています: {run.StopMessage}"
                        : $"指示の項目で止まっています: {run.StopMessage}";
                    break;
                case ScenarioRunStop.Failed:   stop = $"失敗して止まっています: {run.StopMessage}"; break;
                case ScenarioRunStop.Finished: stop = "最後の項目まで終わりました。「やめる」で閉じます。"; break;
                default:                       stop = "流しています…"; break;
            }
            _stopLabel.text = stop;

            _btnContinue.SetEnabled(run.Stop != ScenarioRunStop.Finished && (!_ticking || _awaiting));
            _btnStop.SetEnabled(true);
        }
    }
}
