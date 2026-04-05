// PlayerAdvancedSelectSubPanel.cs
// 詳細選択ツール用サブパネル（Player ビルド用）。
// エディタ版 AdvancedSelectTool.DrawSettingsUI() と同等の内容を UIToolkit で実装する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

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
        private VisualElement _shortestPathGroup;
        private Label         _firstVertexLabel;
        private Button        _clearFirstBtn;

        private static readonly AdvancedSelectMode[] ModeValues =
        {
            AdvancedSelectMode.Connected,
            AdvancedSelectMode.Belt,
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath,
        };

        private static readonly string[] ModeLabels =
        {
            "接続", "ベルト", "辺ループ", "最短",
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
            title.style.marginBottom = 4;
            title.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _root.Add(title);

            // ── モード選択 ───────────────────────────────────────────
            _modeDropdown = new DropdownField("モード",
                new List<string>(ModeLabels), 0);
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
            _helpBox.style.marginBottom = 4;
            _root.Add(_helpBox);

            // ── EdgeLoop しきい値（EdgeLoop モード時のみ表示）────────
            _edgeLoopGroup = new VisualElement();
            _root.Add(_edgeLoopGroup);

            _edgeLoopThresholdSlider = new Slider("方向しきい値", 0f, 1f) { value = 0.5f };
            _edgeLoopThresholdSlider.style.marginBottom = 3;
            _edgeLoopThresholdSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.EdgeLoopThreshold = e.newValue;
            });
            _edgeLoopGroup.Add(_edgeLoopThresholdSlider);

            // ── 追加/削除 ────────────────────────────────────────────
            var actionLabel = new Label("動作:");
            actionLabel.style.marginTop    = 4;
            actionLabel.style.marginBottom = 2;
            actionLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
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

            // ── ShortestPath 始点情報（ShortestPath モード時のみ表示）
            _shortestPathGroup = new VisualElement();
            _root.Add(_shortestPathGroup);

            _firstVertexLabel = new Label();
            _firstVertexLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
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
                    _ => "",
                };
            }

            // EdgeLoop しきい値グループ
            if (_edgeLoopGroup != null)
                _edgeLoopGroup.style.display =
                    mode == AdvancedSelectMode.EdgeLoop ? DisplayStyle.Flex : DisplayStyle.None;

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

            var activeColor   = new StyleColor(Color.white);
            var inactiveColor = new StyleColor(StyleKeyword.Null);

            if (_addBtn    != null) _addBtn.style.backgroundColor    = adding  ? activeColor : inactiveColor;
            if (_removeBtn != null) _removeBtn.style.backgroundColor = !adding ? activeColor : inactiveColor;
        }
    }
}
