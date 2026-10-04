// PolyLingPlayerViewerCore.ScenarioGuide.cs
// Player ビューアのコア：シナリオの案内バー（右ペイン中区画）の組み立てと、経路の赤枠。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【赤枠の出し方】
//   経路のボタンや入力欄を全部「見える状態」にしてから（uiReveal と同じ処理。パネルを開き、折り畳みを開き、スクロールする）、
//   全部をまとめて枠で囲む。パネルを開くと前の枠は消える（UiAutomationService.OnRightPanelsHidden）ので、
//   表示の下準備を先に全部済ませ、枠は最後に付ける。
//   図形ボタンのように、パネルを開き直すと作り直されるボタンや入力欄もあるため、枠を付けるときに要素を引き直す（Highlight が引く）。

using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private PlayerScenarioGuideBar _scenarioGuideBar;

        /// <summary>案内バーを作り、シナリオパネルとつなぐ。シナリオパネルを作った直後に呼ぶ。</summary>
        private void BuildScenarioGuideBar()
        {
            _scenarioGuideBar = new PlayerScenarioGuideBar
            {
                GetModelIndex  = () => ActiveProject?.CurrentModelIndex ?? 0,
                RunCommand     = cmd => DispatchHost(cmd),
                GetRun         = () => _commandDispatcher?.ScenarioRun,
                BeginAwaitUser = () => _commandDispatcher != null && _commandDispatcher.BeginAwaitUser(),
                EndAwaitUser   = () => _commandDispatcher?.EndAwaitUser(),
                ShowRoute      = ShowScenarioRoute,
                ClearRoute     = () => _uiAutomation?.ClearHighlights(),
                OnRunChanged   = () => _scenarioSubPanel?.RefreshRunView(),
                RefreshAreas   = () => _layoutRoot?.RefreshRightAreas(),
            };
            _scenarioGuideBar.Build(_layoutRoot.ScenarioGuideSection);

            if (_scenarioSubPanel != null)
            {
                _scenarioSubPanel.StartRun    = name => _scenarioGuideBar.Start(name);
                _scenarioSubPanel.ContinueRun = _scenarioGuideBar.Continue;
                _scenarioSubPanel.StopRun     = _scenarioGuideBar.Stop;
                _scenarioSubPanel.IsBusy      = () => _scenarioGuideBar.IsBusy;
            }
        }

        /// <summary>案内バーのセクションは流している間だけ出る。UI 自動操作で開く処理は表示を合わせ直すだけ。</summary>
        private void ShowScenarioGuideBar() => _layoutRoot?.RefreshRightAreas();

        /// <summary>経路のボタンや入力欄を見える状態にして、全部を枠で囲む。示せないボタンや入力欄があれば最初の理由。</summary>
        private string ShowScenarioRoute(UiRoute route)
        {
            if (_uiAutomation == null) return UiAutomationNotReady;
            if (route == null || route.Items.Length == 0) return null;

            string err = null;
            foreach (var id in route.Items)
            {
                var r = _uiAutomation.Reveal(id, highlight: false, add: false);
                if (r != null && !r.Success && err == null) err = $"{id}: {r.Reason}";
            }

            bool first = true;
            foreach (var id in route.Items)
            {
                var r = _uiAutomation.Highlight(id, enabled: true, add: !first);
                if (r != null && r.Success) { first = false; continue; }
                if (err == null) err = $"{id}: {r?.Reason}";
            }
            return err;
        }
    }
}
