// PlayerAdvancedSelectSubPanel.cs
// 詳細選択ツール用サブパネル（Player ビルド用）。
// エディタ版 AdvancedSelectTool.DrawSettingsUI() と同等の内容を UIToolkit で実装する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 詳細選択ツールのサブパネル。
    /// モード選択・モード別ヘルプ・EdgeLoopThreshold・追加/削除・ShortestPath始点クリアを提供する。
    /// エディタ版 AdvancedSelectTool.DrawSettingsUI() と同等の内容。
    /// </summary>
    public class PlayerAdvancedSelectSubPanel
    {
        // ================================================================
        // 外部注入（Viewer から設定）
        // ================================================================

        public Func<AdvancedSelectToolHandler> GetHandler;
        public Func<ProjectContext>            GetView;
        public Action<PanelCommand>            SendCommand;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private DropdownField _modeDropdown;
        private HelpBox       _helpBox;
        private Slider        _edgeLoopThresholdSlider;
        private VisualElement _edgeLoopGroup;
        private VisualElement _addRemoveRow;
        private Button        _addBtn;
        private Button        _removeBtn;
        private Button        _clearAllBtn;
        private VisualElement _shortestPathGroup;
        private Label         _firstVertexLabel;
        private Button        _clearFirstBtn;

        // 属性選択（UV/法線数・軸近傍）
        private VisualElement _attrGroup;
        private VisualElement _uvNormalGroup;
        private IntegerField  _uvNormalThresholdField;
        private VisualElement _nearAxisGroup;
        private DropdownField _axisDropdown;
        private FloatField    _axisThresholdField;
        private Toggle        _limitToSelectionToggle;
        private Button        _executeBtn;

        // エッジ（1面だけが使う辺）選択
        private VisualElement _boundaryEdgeGroup;
        private Button        _boundaryEdgeExecuteBtn;

        // 反転 / 辞書化
        private Button        _invertBtn;
        private TextField     _setNameField;

        private static readonly SymmetryAxis[] AxisValues =
        {
            SymmetryAxis.X, SymmetryAxis.Y, SymmetryAxis.Z,
        };

        private static readonly string[] AxisLabels = { "X", "Y", "Z" };

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private static readonly AdvancedSelectMode[] ModeValues =
        {
            AdvancedSelectMode.Connected,
            AdvancedSelectMode.Belt,
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath,
            AdvancedSelectMode.UvNormalCount,
            AdvancedSelectMode.NearAxis,
            AdvancedSelectMode.BoundaryEdgeGroup,
            AdvancedSelectMode.BoundaryEdgeInSelection,
        };

        private static readonly string[] ModeLabels =
        {
            "接続", "ベルト", "辺ループ", "最短", "UV/法線数", "軸近傍",
            "エッジ群", "選択内エッジ",
        };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            var title = new Label("詳細選択");
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;
            _root.Add(title);

            // ── モード選択 ───────────────────────────────────────────
            _modeDropdown = new DropdownField("モード",
                new List<string>(ModeLabels), 0);
            _modeDropdown.style.color = new StyleColor(Color.white);
            _modeDropdown.style.marginBottom = 4;
            _modeDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = System.Array.IndexOf(ModeLabels, e.newValue);
                if (idx < 0) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.Mode = ModeValues[idx];
                UpdateModeUI(ModeValues[idx]);
            });
            _root.Add(_modeDropdown);

            // ── ヘルプ ───────────────────────────────────────────────
            _helpBox = new HelpBox("", HelpBoxMessageType.Info);
            _helpBox.style.color = new StyleColor(Color.white);
            _helpBox.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _helpBox.style.marginBottom = 4;
            _root.Add(_helpBox);

            // ── EdgeLoop しきい値（EdgeLoop モード時のみ表示）────────
            _edgeLoopGroup = new VisualElement();
            _root.Add(_edgeLoopGroup);

            _edgeLoopThresholdSlider = new Slider("方向しきい値", 0f, 1f) { value = 0.5f };
            _edgeLoopThresholdSlider.style.color = new StyleColor(Color.white);
            _edgeLoopThresholdSlider.style.marginBottom = 3;
            _edgeLoopThresholdSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.EdgeLoopThreshold = e.newValue;
            });
            _edgeLoopGroup.Add(_edgeLoopThresholdSlider);

            // ── 属性選択（UV/法線数・軸近傍）─────────────────────────
            // クリックではなく「実行」ボタンで動作するモード用。
            _attrGroup = new VisualElement();
            _root.Add(_attrGroup);

            _uvNormalGroup = new VisualElement();
            _attrGroup.Add(_uvNormalGroup);

            _uvNormalThresholdField = new IntegerField("データ数しきい値") { value = 0 };
            _uvNormalThresholdField.style.color = new StyleColor(Color.white);
            _uvNormalThresholdField.style.marginBottom = 3;
            _uvNormalThresholdField.tooltip =
                "頂点の UV/法線スロット数がこの値より大きい頂点を選択します。";
            _uvNormalThresholdField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.UvNormalCountThreshold = e.newValue;
            });
            _uvNormalGroup.Add(_uvNormalThresholdField);

            _nearAxisGroup = new VisualElement();
            _attrGroup.Add(_nearAxisGroup);

            _axisDropdown = new DropdownField("軸",
                new List<string>(AxisLabels), 0);
            _axisDropdown.style.color = new StyleColor(Color.white);
            _axisDropdown.style.marginBottom = 3;
            _axisDropdown.tooltip = "X なら YZ 平面（|X|）までの距離を見ます。";
            _axisDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = System.Array.IndexOf(AxisLabels, e.newValue);
                if (idx < 0) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.AxisKind = AxisValues[idx];
            });
            _nearAxisGroup.Add(_axisDropdown);

            _axisThresholdField = new FloatField("距離しきい値") { value = 0.00001f };
            _axisThresholdField.style.color = new StyleColor(Color.white);
            _axisThresholdField.style.marginBottom = 3;
            _axisThresholdField.tooltip =
                "軸に対応する平面までの距離がこの値未満の頂点を選択します。";
            _axisThresholdField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.AxisDistanceThreshold = e.newValue;
            });
            _nearAxisGroup.Add(_axisThresholdField);

            _limitToSelectionToggle = new Toggle("選択中の頂点内から");
            _limitToSelectionToggle.style.color = new StyleColor(Color.white);
            _limitToSelectionToggle.style.marginBottom = 3;
            _limitToSelectionToggle.tooltip =
                "ON かつ動作=追加 のとき、現在の選択のうち条件に合わない頂点を解除します（絞り込み）。\n"
                + "ON かつ動作=削除 のとき、条件に合った頂点を選択から外します。";
            _limitToSelectionToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.LimitToCurrentSelection = e.newValue;
            });
            _attrGroup.Add(_limitToSelectionToggle);

            _executeBtn = new Button { text = "実行" };
            _executeBtn.style.marginBottom = 4;
            _executeBtn.clicked += () =>
            {
                GetHandler?.Invoke()?.ExecuteAttributeSelect();
                Refresh();
            };
            _attrGroup.Add(_executeBtn);

            // ── 選択内エッジ（クリック不要・実行ボタン）────────────────
            _boundaryEdgeGroup = new VisualElement();
            _root.Add(_boundaryEdgeGroup);

            _boundaryEdgeExecuteBtn = new Button { text = "実行" };
            _boundaryEdgeExecuteBtn.style.marginBottom = 4;
            _boundaryEdgeExecuteBtn.tooltip =
                "両端点が現在の頂点選択に含まれるエッジ（1つの面だけが使う辺）を、動作（追加/削除）に従って辺選択に反映します。";
            _boundaryEdgeExecuteBtn.clicked += () =>
            {
                GetHandler?.Invoke()?.ExecuteBoundaryEdgeInSelection();
                Refresh();
            };
            _boundaryEdgeGroup.Add(_boundaryEdgeExecuteBtn);

            // ── 追加/削除 ────────────────────────────────────────────
            var actionLabel = new Label("動作:");
            actionLabel.style.color = new StyleColor(Color.white);
            actionLabel.style.marginTop    = 4;
            actionLabel.style.marginBottom = 2;
            actionLabel.style.fontSize     = 10;
            _root.Add(actionLabel);

            _addRemoveRow = new VisualElement();
            _addRemoveRow.style.flexDirection = FlexDirection.Row;
            _addRemoveRow.style.marginBottom  = 4;
            _root.Add(_addRemoveRow);

            _addBtn = new Button { text = "追加" };
            _addBtn.style.flexGrow    = 1;
            _addBtn.style.marginRight = 2;
            _addBtn.clicked += () =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.AddToSelection = true;
                UpdateAddRemoveStyle();
            };
            _addRemoveRow.Add(_addBtn);

            _removeBtn = new Button { text = "削除" };
            _removeBtn.style.flexGrow = 1;
            _removeBtn.clicked += () =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.AddToSelection = false;
                UpdateAddRemoveStyle();
            };
            _addRemoveRow.Add(_removeBtn);

            // ── 全選択解除（全モード共通）─────────────────────────────
            _clearAllBtn = new Button { text = "全選択解除" };
            _clearAllBtn.style.marginTop    = 2;
            _clearAllBtn.style.marginBottom = 4;
            _clearAllBtn.clicked += () =>
            {
                GetHandler?.Invoke()?.ClearAllSelection();
                Refresh();
            };
            _root.Add(_clearAllBtn);

            // ── 現在の選択を反転（全モード共通）───────────────────────
            // 反転対象は SelectionState.Mode で有効な 頂点/辺/面/線 のみ。
            _invertBtn = new Button { text = "現在の選択を反転" };
            _invertBtn.style.marginBottom = 4;
            _invertBtn.tooltip =
                "有効な選択モード（頂点/辺/面/線）だけを反転します。無効なモードの選択は変更しません。";
            _invertBtn.clicked += () =>
            {
                GetHandler?.Invoke()?.InvertSelection();
                Refresh();
            };
            _root.Add(_invertBtn);

            // ── 現在の選択を辞書化（全モード共通）─────────────────────
            // PlayerPartsSelectionSetSubPanel と同じ SavePartsSetCommand を送る。
            var dictRow = new VisualElement();
            dictRow.style.flexDirection = FlexDirection.Row;
            dictRow.style.marginBottom  = 4;
            _root.Add(dictRow);

            _setNameField = new TextField();
            _setNameField.style.flexGrow = 1;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            dictRow.Add(_setNameField);

            var dictBtn = new Button { text = "辞書化" };
            dictBtn.style.width = 52;
            dictBtn.clicked += () =>
            {
                SendCommand?.Invoke(
                    new SavePartsSetCommand(ModelIndex, _setNameField?.value?.Trim() ?? ""));
            };
            dictRow.Add(dictBtn);

            // ── ShortestPath 始点情報（ShortestPath モード時のみ表示）
            _shortestPathGroup = new VisualElement();
            _root.Add(_shortestPathGroup);

            _firstVertexLabel = new Label();
            _firstVertexLabel.style.color = new StyleColor(Color.white);
            _firstVertexLabel.style.fontSize     = 10;
            _firstVertexLabel.style.marginBottom = 2;
            _shortestPathGroup.Add(_firstVertexLabel);

            _clearFirstBtn = new Button { text = "始点をクリア" };
            _clearFirstBtn.style.marginBottom = 3;
            _clearFirstBtn.clicked += () =>
            {
                GetHandler?.Invoke()?.ClearShortestPathFirst();
                Refresh();
            };
            _shortestPathGroup.Add(_clearFirstBtn);

            UpdateModeUI(AdvancedSelectMode.Connected);
            UpdateAddRemoveStyle();
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            var h = GetHandler?.Invoke();
            if (h == null) return;

            int modeIdx = System.Array.IndexOf(ModeValues, h.Mode);
            _modeDropdown?.SetValueWithoutNotify(
                modeIdx >= 0 ? ModeLabels[modeIdx] : ModeLabels[0]);

            _edgeLoopThresholdSlider?.SetValueWithoutNotify(h.EdgeLoopThreshold);

            _uvNormalThresholdField?.SetValueWithoutNotify(h.UvNormalCountThreshold);
            _axisThresholdField?.SetValueWithoutNotify(h.AxisDistanceThreshold);
            _limitToSelectionToggle?.SetValueWithoutNotify(h.LimitToCurrentSelection);

            int axisIdx = System.Array.IndexOf(AxisValues, h.AxisKind);
            _axisDropdown?.SetValueWithoutNotify(
                axisIdx >= 0 ? AxisLabels[axisIdx] : AxisLabels[0]);

            UpdateModeUI(h.Mode);
            UpdateAddRemoveStyle();

            // ShortestPath 始点表示
            if (h.Mode == AdvancedSelectMode.ShortestPath && _firstVertexLabel != null)
            {
                int fv = h.GetShortestPathFirstVertex();
                _firstVertexLabel.text    = fv >= 0 ? $"始点: {fv}" : "";
                _clearFirstBtn.style.display =
                    fv >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateModeUI(AdvancedSelectMode mode)
        {
            // ヘルプテキスト
            if (_helpBox != null)
            {
                _helpBox.text = mode switch
                {
                    AdvancedSelectMode.Connected   =>
                        "要素をクリックして接続領域を選択\n出力: 有効な全モード（頂点/エッジ/面/線）",
                    AdvancedSelectMode.Belt        =>
                        "エッジをクリックしてベルトを選択\n・頂点: ベルト上の頂点\n・エッジ: 横方向エッジ\n・面: ベルト上の面",
                    AdvancedSelectMode.EdgeLoop    =>
                        "エッジをクリックしてエッジループを選択\n・頂点: ループ上の頂点\n・エッジ: ループ上のエッジ\n・面: 隣接する面",
                    AdvancedSelectMode.ShortestPath =>
                        "2つの頂点をクリックして最短経路を選択\n・頂点: 経路上の頂点\n・エッジ: 経路上のエッジ\n・面: 隣接する面",
                    AdvancedSelectMode.UvNormalCount =>
                        "UV/法線スロット数がしきい値より大きい頂点を選択\nクリック不要。「実行」ボタンで適用",
                    AdvancedSelectMode.NearAxis =>
                        "軸に対応する平面までの距離がしきい値未満の頂点を選択\nクリック不要。「実行」ボタンで適用",
                    AdvancedSelectMode.BoundaryEdgeGroup =>
                        "エッジ上の頂点・辺、またはエッジに接する面をクリック\n同じグループのエッジ全部と構成頂点を選択します\nエッジ＝1つの面だけが使う辺（穴の縁など）",
                    AdvancedSelectMode.BoundaryEdgeInSelection =>
                        "両端点が選択済みのエッジを選択\nクリック不要。「実行」ボタンで適用",
                    _ => "",
                };
            }

            // EdgeLoop しきい値グループ
            if (_edgeLoopGroup != null)
                _edgeLoopGroup.style.display =
                    mode == AdvancedSelectMode.EdgeLoop ? DisplayStyle.Flex : DisplayStyle.None;

            // 属性選択グループ
            bool isAttr = AdvancedSelectTool.IsAttributeMode(mode);
            if (_attrGroup != null)
                _attrGroup.style.display = isAttr ? DisplayStyle.Flex : DisplayStyle.None;
            if (_uvNormalGroup != null)
                _uvNormalGroup.style.display =
                    mode == AdvancedSelectMode.UvNormalCount ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nearAxisGroup != null)
                _nearAxisGroup.style.display =
                    mode == AdvancedSelectMode.NearAxis ? DisplayStyle.Flex : DisplayStyle.None;

            // 選択内エッジグループ
            if (_boundaryEdgeGroup != null)
                _boundaryEdgeGroup.style.display =
                    mode == AdvancedSelectMode.BoundaryEdgeInSelection
                        ? DisplayStyle.Flex : DisplayStyle.None;

            // ShortestPath 始点グループ
            if (_shortestPathGroup != null)
            {
                bool show = mode == AdvancedSelectMode.ShortestPath;
                _shortestPathGroup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

                if (show && _firstVertexLabel != null)
                {
                    int fv = GetHandler?.Invoke()?.GetShortestPathFirstVertex() ?? -1;
                    _firstVertexLabel.text = fv >= 0 ? $"始点: {fv}" : "";
                    if (_clearFirstBtn != null)
                        _clearFirstBtn.style.display =
                            fv >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        private void UpdateAddRemoveStyle()
        {
            var h = GetHandler?.Invoke();
            bool adding = h?.AddToSelection ?? true;

            // active に Color.white、非 active に StyleKeyword.Null を入れると、
            // ApplyDarkTheme が入れた白文字がどちらの背景でも読めなくなる
            // （Null はインライン背景を外すだけで USS 既定の明るい灰色に戻る）。
            var activeColor   = PlayerLayoutRoot.BtnActiveColor;
            var inactiveColor = PlayerLayoutRoot.BtnInactiveColor;

            if (_addBtn    != null) _addBtn.style.backgroundColor    = adding  ? activeColor : inactiveColor;
            if (_removeBtn != null) _removeBtn.style.backgroundColor = !adding ? activeColor : inactiveColor;
        }
    }
}
