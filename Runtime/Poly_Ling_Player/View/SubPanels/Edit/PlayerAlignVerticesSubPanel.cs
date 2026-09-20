// PlayerAlignVerticesSubPanel.cs
// AlignVerticesTool の Player 版サブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerAlignVerticesSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface                   Surface;
        private const string Tool = "alignVertices";
        public Func<Poly_Ling.View.IProjectView>           GetView;
        public Action<PanelCommand>           SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null（呼び出し側が送信を止める）。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            int idx = GetView?.Invoke()?.CurrentModel?.ActiveMeshIndex ?? -1;
            return idx >= 0 ? new[] { idx } : null;
        }

        // ================================================================
        // UI 要素
        // ================================================================

        // UI 自動操作の ID は "alignVertices.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("stats.selected", Safety = UiSafety.ReadOnly, Description = "選択中の頂点数")]
        private Label         _selectedLabel;
        [UiControl("stats.stdDev", Safety = UiSafety.ReadOnly, Description = "選択頂点の座標の軸ごとの標準偏差")]
        private Label         _stdDevLabel;
        [UiControl("axis.x", Description = "X 座標を揃える")]
        private Toggle        _toggleX;
        [UiControl("axis.y", Description = "Y 座標を揃える")]
        private Toggle        _toggleY;
        [UiControl("axis.z", Description = "Z 座標を揃える")]
        private Toggle        _toggleZ;
        [UiControl("mode", Description = "揃える基準")]
        private DropdownField _modeDropdown;
        [UiControl("preview", Safety = UiSafety.ReadOnly, Description = "揃えた後の座標（揃える軸だけ）")]
        private Label         _previewLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "選択頂点の座標を揃える。軸を 1 つ以上選び、頂点 2 つ以上の選択が要る")]
        private Button        _alignBtn;
        [UiControl("autoSelect", Safety = UiSafety.SafeWrite, Description = "揃える軸を自動で選ぶ")]
        private Button        _autoSelectBtn;

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

            _toggleX = MakeToggle("X", v => { Surface.Set(Tool, "alignX", v); UpdatePreview(); });
            _toggleY = MakeToggle("Y", v => { Surface.Set(Tool, "alignY", v); UpdatePreview(); });
            _toggleZ = MakeToggle("Z", v => { Surface.Set(Tool, "alignZ", v); UpdatePreview(); });
            axisRow.Add(_toggleX);
            axisRow.Add(_toggleY);
            axisRow.Add(_toggleZ);
            _root.Add(axisRow);

            // 自動選択ボタン
            var autoBtn = new Button(() =>
            {
                Surface?.Invoke(Tool, "triggerAutoSelect");
                RefreshToggles();
                UpdatePreview();
            }) { text = "Auto Select" };
            autoBtn.style.marginBottom = 4;
            _root.Add(autoBtn);
            _autoSelectBtn = autoBtn;

            // 整列モード
            _root.Add(SmallHeader("基準:"));
            _modeDropdown = new DropdownField(ModeChoices, 0);
            _modeDropdown.style.marginBottom = 4;
            _modeDropdown.RegisterValueChangedCallback(e =>
            {
                if (Surface == null) return;
                Surface.Set(Tool, "mode", (AlignMode)ModeChoices.IndexOf(e.newValue));
                UpdatePreview();
            });
            _root.Add(_modeDropdown);

            // プレビューラベル
            _previewLabel = InfoLabel();
            _root.Add(_previewLabel);

            // 整列実行ボタン
            _alignBtn = new Button(() =>
            {
                var targets = ActiveMasterIndices();
                if (Surface == null || targets == null) return;

                // 設定値はコマンドが正典。パネルの現在値を載せて送る。
                // ハンドラ側は実行後に元の値へ戻すので、表示は変わらない。
                SendCommand?.Invoke(new AlignVerticesCommand(
                    ModelIndex, targets,
                    Surface.GetBool(Tool, "alignX"), Surface.GetBool(Tool, "alignY"), Surface.GetBool(Tool, "alignZ"),
                    Surface.Get(Tool, "mode", default(AlignMode))));
                Refresh();
            })
            { text = "整列実行" };
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
            if (Surface == null) return;

            int selCount = Surface.GetInt(Tool, "selectedVertexCount");
            _selectedLabel.text = $"選択中: {selCount} 頂点";

            if (Surface.GetBool(Tool, "statsCalculated"))
            {
                _stdDevLabel.text =
                    $"標準偏差  X:{Surface.GetFloat(Tool, "stdDevX"):F4}  Y:{Surface.GetFloat(Tool, "stdDevY"):F4}  Z:{Surface.GetFloat(Tool, "stdDevZ"):F4}";
            }
            else
            {
                _stdDevLabel.text = "";
            }

            RefreshToggles();
            UpdatePreview();

            bool anyAxis = Surface.GetBool(Tool, "alignX") || Surface.GetBool(Tool, "alignY") || Surface.GetBool(Tool, "alignZ");
            if (_alignBtn != null)
                _alignBtn.SetEnabled(anyAxis && selCount >= 2);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void RefreshToggles()
        {
            if (Surface == null) return;
            _toggleX?.SetValueWithoutNotify(Surface.GetBool(Tool, "alignX"));
            _toggleY?.SetValueWithoutNotify(Surface.GetBool(Tool, "alignY"));
            _toggleZ?.SetValueWithoutNotify(Surface.GetBool(Tool, "alignZ"));
            _modeDropdown?.SetValueWithoutNotify(ModeChoices[(int)Surface.Get(Tool, "mode", default(AlignMode))]);
        }

        private void UpdatePreview()
        {
            if (_previewLabel == null) return;
            if (Surface == null) { _previewLabel.text = ""; return; }
            bool ax = Surface.GetBool(Tool, "alignX");
            bool ay = Surface.GetBool(Tool, "alignY");
            bool az = Surface.GetBool(Tool, "alignZ");
            if ((!ax && !ay && !az) || Surface.GetInt(Tool, "selectedVertexCount") < 2)
            {
                _previewLabel.text = "";
                return;
            }
            var t    = Surface.Get(Tool, "alignTarget", Vector3.zero);
            var parts = new List<string>();
            if (ax) parts.Add($"X={t.x:F3}");
            if (ay) parts.Add($"Y={t.y:F3}");
            if (az) parts.Add($"Z={t.z:F3}");
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
