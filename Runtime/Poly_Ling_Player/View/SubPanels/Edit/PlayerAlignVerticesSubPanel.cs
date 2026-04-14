// PlayerAlignVerticesSubPanel.cs
// AlignVerticesTool の Player 版サブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerAlignVerticesSubPanel
    {
        public Func<AlignVerticesToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _selectedLabel;
        private Label         _stdDevLabel;
        private Toggle        _toggleX, _toggleY, _toggleZ;
        private DropdownField _modeDropdown;
        private Label         _previewLabel;
        private Button        _alignBtn;

        private static readonly List<string> ModeChoices = new List<string> { "Average", "Min", "Max" };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Align Vertices / 頂点整列"));
            _root.Add(new HelpBox("選択頂点を指定軸上に整列します。標準偏差が最小の軸が自動選択されます。", HelpBoxMessageType.Info));

            // 選択頂点数
            _selectedLabel = InfoLabel();
            _root.Add(_selectedLabel);

            // 標準偏差
            _stdDevLabel = InfoLabel();
            _root.Add(_stdDevLabel);

            // 軸チェックボックス行
            _root.Add(SmallHeader("整列軸:"));
            var axisRow = new VisualElement();
            axisRow.style.flexDirection = FlexDirection.Row;
            axisRow.style.marginBottom  = 4;

            _toggleX = MakeToggle("X", v => { var h = GetH(); if (h != null) h.AlignX = v; UpdatePreview(); });
            _toggleY = MakeToggle("Y", v => { var h = GetH(); if (h != null) h.AlignY = v; UpdatePreview(); });
            _toggleZ = MakeToggle("Z", v => { var h = GetH(); if (h != null) h.AlignZ = v; UpdatePreview(); });
            axisRow.Add(_toggleX);
            axisRow.Add(_toggleY);
            axisRow.Add(_toggleZ);
            _root.Add(axisRow);

            // 自動選択ボタン
            var autoBtn = new Button(() =>
            {
                GetH()?.TriggerAutoSelect();
                RefreshToggles();
                UpdatePreview();
            }) { text = "Auto Select" };
            autoBtn.style.marginBottom = 4;
            _root.Add(autoBtn);

            // 整列モード
            _root.Add(SmallHeader("基準:"));
            _modeDropdown = new DropdownField(ModeChoices, 0);
            _modeDropdown.style.marginBottom = 4;
            _modeDropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.Mode = (AlignMode)ModeChoices.IndexOf(e.newValue);
                UpdatePreview();
            });
            _root.Add(_modeDropdown);

            // プレビューラベル
            _previewLabel = InfoLabel();
            _root.Add(_previewLabel);

            // 整列実行ボタン
            _alignBtn = new Button(() => GetH()?.TriggerAlign()) { text = "整列実行" };
            _alignBtn.style.height    = 30;
            _alignBtn.style.marginTop = 6;
            _root.Add(_alignBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            int selCount = h.SelectedVertexCount;
            _selectedLabel.text = $"選択中: {selCount} 頂点";

            if (h.StatsCalculated)
            {
                _stdDevLabel.text =
                    $"標準偏差  X:{h.StdDevX:F4}  Y:{h.StdDevY:F4}  Z:{h.StdDevZ:F4}";
            }
            else
            {
                _stdDevLabel.text = "";
            }

            RefreshToggles();
            UpdatePreview();

            bool canAlign = (h.AlignX || h.AlignY || h.AlignZ) && selCount >= 2;
            if (_alignBtn != null)
                _alignBtn.SetEnabled(canAlign);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void RefreshToggles()
        {
            var h = GetH();
            if (h == null) return;
            _toggleX?.SetValueWithoutNotify(h.AlignX);
            _toggleY?.SetValueWithoutNotify(h.AlignY);
            _toggleZ?.SetValueWithoutNotify(h.AlignZ);
            _modeDropdown?.SetValueWithoutNotify(ModeChoices[(int)h.Mode]);
        }

        private void UpdatePreview()
        {
            if (_previewLabel == null) return;
            var h = GetH();
            if (h == null || (!h.AlignX && !h.AlignY && !h.AlignZ) || h.SelectedVertexCount < 2)
            {
                _previewLabel.text = "";
                return;
            }
            var t    = h.GetAlignTarget();
            var parts = new List<string>();
            if (h.AlignX) parts.Add($"X={t.x:F3}");
            if (h.AlignY) parts.Add($"Y={t.y:F3}");
            if (h.AlignZ) parts.Add($"Z={t.z:F3}");
            _previewLabel.text = "-> " + string.Join("  ", parts);
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Toggle MakeToggle(string label, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = false };
            t.style.marginRight = 8;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }
    }
}
