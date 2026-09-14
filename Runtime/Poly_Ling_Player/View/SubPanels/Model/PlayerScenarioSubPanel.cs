// PlayerScenarioSubPanel.cs
// 手本（シナリオ）の一覧と中身の表示、段 1 つの実行。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【下に並ぶ検証パネルとの違い】
//   「揺れもの→スキンド→VRM」などの検証パネルは段が C# のラムダで、
//   PlayerStagedTestSubPanelBase が上から順に流す。
//   こちらは ScenarioLibrary が読んだ scenarios.csv の手本を並べ、
//   段を 1 つ選んで実行する。段はデータなので編集できる。
//
// 【全部実行を置かない】
//   方針案「Scenario を固定実行手段にしない」「Scenario の自動実行は対象外」。
//   手本は記載どおりに完走させるものではなく、目的に合わせて読み替えるもの。
//   段を選ぶのは人（か AI）で、判断点はそちらに残す。
//
// 【実行はコマンドを送るだけ】
//   段の組み立て・引数の検査・実行は RunScenarioStepCommand が持つ。
//   ここで組み立て直すと、MCP から撃った場合と挙動が割れる。
//
// 【読み直し】
//   手本はファイルが正典。MCP や手編集で外から変わるので、
//   パネルを開いたときと「読み直す」を押したときに ScenarioLibrary.Reload を通す。

using System;
using System.Collections.Generic;
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
        public Func<ProjectContext> GetProject;

        /// <summary>コマンド送信。</summary>
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private int ModelIndex => GetProject?.Invoke()?.CurrentModelIndex ?? 0;

        // ================================================================
        // 内部状態
        // ================================================================

        // UI 自動操作の ID は "scenario.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("list", Description = "手本の一覧（行番号）")]
        private ListView _listView;
        [UiControl("steps", Description = "選んだ手本の段（行番号）")]
        private ListView _stepView;
        [UiControl("detail", Safety = UiSafety.ReadOnly, Description = "選んだ手本の目的・前提・成功条件・由来")]
        private Label    _detailLabel;
        [UiControl("stepDetail", Safety = UiSafety.ReadOnly, Description = "選んだ段の中身")]
        private Label    _stepDetailLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label    _statusLabel;
        [UiControl("reload", Safety = UiSafety.SafeWrite, Description = "手本をファイルから読み直す")]
        private Button   _btnReload;
        [UiControl("runStep", Safety = UiSafety.Destructive, Description = "選んだ段を 1 つ実行する")]
        private Button   _btnRunStep;
        [UiControl("expand", Safety = UiSafety.SafeWrite, Description = "選んだ参照段を参照先の段の列で置き換える")]
        private Button   _btnExpand;

        private readonly List<ObjectGroup> _scenarios  = new List<ObjectGroup>();
        private readonly List<string>      _labels     = new List<string>();
        private readonly List<string>      _stepLabels = new List<string>();

        private int _selected     = -1;
        private int _selectedStep = -1;

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("シナリオ（手本）"));

            var hint = new Label(
                "目的つきの操作レシピです。段には実行するもの（Command）と、"
              + "実行しないもの（Note・Instruction・Observe）、別の手本への参照（ScenarioRef）があります。\n"
              + "段を 1 つ選んで「ステップ実行」を押します。まとめて流す口はありません。"
              + "手本は記載どおりに完走させるものではなく、対象に合わせて読み替えるものだからです。\n"
              + "中身の編集は MCP から行います（createScenario / addScenarioStep ほか）。");
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.fontSize     = 10;
            hint.style.marginBottom = 4;
            root.Add(hint);

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
                _selected     = _listView.selectedIndex;
                _selectedStep = -1;
                RefreshSteps();
            };
            root.Add(_listView);

            _detailLabel = new Label();
            _detailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _detailLabel.style.fontSize     = 10;
            _detailLabel.style.marginBottom = 4;
            root.Add(_detailLabel);

            root.Add(SecLabel("段"));

            _stepView = new ListView(_stepLabels, 22,
                () => { var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft; return l; },
                (e, i) =>
                {
                    if (!(e is Label l) || i < 0 || i >= _stepLabels.Count) return;
                    l.text = _stepLabels[i];

                    // 実行できる段だけ白。実行しない段は落とす。
                    var st = StepAt(i);
                    l.style.color = new StyleColor(
                        st != null && st.IsExecutable ? Color.white : new Color(0.68f, 0.68f, 0.68f));
                });
            _stepView.selectionType   = SelectionType.Single;
            _stepView.style.minHeight = 70;
            _stepView.style.maxHeight = 160;
            _stepView.style.marginBottom = 4;
            _stepView.selectionChanged += _ =>
            {
                _selectedStep = _stepView.selectedIndex;
                UpdateStepDetail();
            };
            root.Add(_stepView);

            _stepDetailLabel = new Label();
            _stepDetailLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stepDetailLabel.style.fontSize     = 10;
            _stepDetailLabel.style.marginBottom = 4;
            root.Add(_stepDetailLabel);

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;

            _btnRunStep = MkBtn("ステップ実行", OnRunStep); _btnRunStep.style.flexGrow = 1; _btnRunStep.style.marginRight = 2;
            _btnExpand  = MkBtn("参照を展開",   OnExpand);  _btnExpand.style.flexGrow  = 1;
            opRow.Add(_btnRunStep); opRow.Add(_btnExpand);
            root.Add(opRow);

            _btnReload = MkBtn("読み直す", OnReload);
            _btnReload.style.marginBottom = 3;
            root.Add(_btnReload);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>
        /// 一覧を作り直す。手本はファイルが正典で、MCP や手編集で外から変わるため、
        /// パネルを開くたびに読み直す。
        /// </summary>
        public void Refresh()
        {
            ScenarioLibrary.Reload();
            RefreshFromLibrary();
        }

        private void RefreshFromLibrary()
        {
            string keep = (_selected >= 0 && _selected < _scenarios.Count) ? _scenarios[_selected].Name : null;

            _scenarios.Clear();
            _labels.Clear();

            foreach (var g in ScenarioLibrary.GetAll())
            {
                if (g == null) continue;
                _scenarios.Add(g);
                _labels.Add($"{g.Name}  [{g.StepCount} 段]"
                          + (string.IsNullOrEmpty(g.Goal) ? "" : $"  {g.Goal}"));
            }

            // 選択は名前で取り直す。並びが変わっても同じ手本を指し続ける。
            _selected = -1;
            if (keep != null)
                for (int i = 0; i < _scenarios.Count; i++)
                    if (string.Equals(_scenarios[i].Name, keep, StringComparison.Ordinal)) { _selected = i; break; }

            _listView?.Rebuild();
            if (_listView != null) _listView.selectedIndex = _selected;

            RefreshSteps();
        }

        private ObjectGroup Selected()
            => (_selected >= 0 && _selected < _scenarios.Count) ? _scenarios[_selected] : null;

        private ObjectGroupStep StepAt(int index)
        {
            var g = Selected();
            if (g?.Steps == null) return null;
            return (index >= 0 && index < g.Steps.Count) ? g.Steps[index] : null;
        }

        private ObjectGroupStep SelectedStep() => StepAt(_selectedStep);

        private void RefreshSteps()
        {
            _stepLabels.Clear();

            var g = Selected();
            if (g?.Steps != null)
            {
                for (int i = 0; i < g.Steps.Count; i++)
                {
                    var st = g.Steps[i];
                    if (st == null) continue;

                    string what = st.IsScenarioRef ? $"→ {st.RefName}"
                                : st.IsExecutable  ? st.Action
                                : "";

                    _stepLabels.Add($"{i + 1}. [{st.Kind}] {st.ElementId}"
                                  + (string.IsNullOrEmpty(what) ? "" : $"  {what}"));
                }
            }

            if (_selectedStep >= _stepLabels.Count) _selectedStep = -1;

            _stepView?.Rebuild();
            if (_stepView != null) _stepView.selectedIndex = _selectedStep;

            UpdateDetail();
            UpdateStepDetail();
        }

        private void UpdateDetail()
        {
            var g = Selected();
            if (g == null)
            {
                if (_detailLabel != null) _detailLabel.text = "";
                return;
            }

            var prov = g.Provenance ?? new ObjectGroupProvenance();

            _detailLabel.text =
                  $"目的: {Or(g.Goal, "（無し）")}\n"
                + $"前提: {Join(g.Preconditions)}\n"
                + $"成功条件: {Join(g.SuccessCriteria)}\n"
                + $"札: {Join(g.Tags)}\n"
                + $"由来: {Or(prov.ParentName, "（元なし）")}"
                + (string.IsNullOrEmpty(prov.ChangeSummary) ? "" : $" / {prov.ChangeSummary}")
                + (string.IsNullOrEmpty(prov.CreatedBy)     ? "" : $" / {prov.CreatedBy}");
        }

        private void UpdateStepDetail()
        {
            var st = SelectedStep();

            bool canRun    = st != null && st.IsExecutable;
            bool canExpand = st != null && st.IsScenarioRef;

            _btnRunStep?.SetEnabled(canRun);
            _btnExpand?.SetEnabled(canExpand);

            if (st == null)
            {
                if (_stepDetailLabel != null) _stepDetailLabel.text = "";
                return;
            }

            var lines = new List<string>
            {
                $"種別: {st.Kind}",
                $"理由: {Or(st.Purpose, "（無し）")}",
            };

            if (st.IsScenarioRef)
                lines.Add($"参照先: {st.RefName}  ({st.ExpansionPolicy})");

            if (st.IsExecutable)
            {
                lines.Add($"コマンド: {st.Action}");
                foreach (var kv in st.SortedArgs())
                    lines.Add($"  {kv.Key} = {kv.Value}");
            }

            _stepDetailLabel.text = string.Join("\n", lines);
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnReload()
        {
            Refresh();
            SetStatus($"{ScenarioLibrary.Count} 件を読み直しました（{ScenarioLibrary.StorePath}）");
        }

        /// <summary>
        /// 選んだ段を 1 つ実行する。
        /// 組み立てと検査は RunScenarioStepCommand が持つので、ここは送るだけ。
        /// </summary>
        private void OnRunStep()
        {
            var g  = Selected();
            var st = SelectedStep();
            if (g == null || st == null) { SetStatus("段を選んでください"); return; }

            if (!st.IsExecutable)
            {
                SetStatus($"{st.ElementId} は {st.Kind} の段です。実行するものではありません: {st.Purpose}");
                return;
            }

            SendCmd(new RunScenarioStepCommand(ModelIndex, g.Name, st.ElementId));
            SetStatus($"「{g.Name}」の {st.ElementId}（{st.Action}）を実行しました");
        }

        private void OnExpand()
        {
            var g  = Selected();
            var st = SelectedStep();
            if (g == null || st == null) { SetStatus("段を選んでください"); return; }

            if (!st.IsScenarioRef)
            {
                SetStatus($"{st.ElementId} は {st.Kind} の段で、参照段ではありません");
                return;
            }

            string refName = st.RefName;
            SendCmd(new ExpandScenarioRefCommand(ModelIndex, g.Name, st.ElementId));
            SetStatus($"「{g.Name}」の {st.ElementId} を {refName} の段で置き換えました");

            _selectedStep = -1;
            Refresh();
        }

        // ================================================================
        // 小物
        // ================================================================

        private static string Or(string s, string fallback)
            => string.IsNullOrEmpty(s) ? fallback : s;

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
