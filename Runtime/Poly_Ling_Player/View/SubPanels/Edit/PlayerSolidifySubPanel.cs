// PlayerSolidifySubPanel.cs
// SolidifyToolHandler（厚み付け）用のサブパネル（UIToolkit）。
// エッジ（角処理）のパラメータ構成は 2D押し出し（Profile2D）と同じ。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerSolidifySubPanel
    {
        public Func<SolidifyToolHandler> GetH;
        public Func<ProjectContext>      GetView;
        public Action<PanelCommand>      SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null（呼び出し側が送信を止める）。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            var model = GetView?.Invoke()?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc == null) return null;
            return new[] { model.IndexOf(mc) };
        }

        private VisualElement _root;
        private Label         _infoLabel;
        private Label         _resultLabel;

        private FloatField    _thicknessField;
        private Toggle        _addToExistingToggle;

        // 名前欄。「既存メッシュに追加」のときは追加先ドロップダウンへ差し替える
        // （図形生成パネルの名前欄と同じ扱い）。
        private TextField     _nameField;
        private DropdownField _addTargetField;
        private readonly System.Collections.Generic.List<int> _addTargetIndices =
            new System.Collections.Generic.List<int>();

        /// <summary>描画オブジェクト一覧（表示名, MeshContextList インデックス）。</summary>
        public Func<System.Collections.Generic.List<(string Label, int MasterIndex)>> GetDrawableIndexList;

        /// <summary>選択オブジェクトリストの先頭。追加先ドロップダウンの既定選択に使う。</summary>
        public Func<int> GetFirstSelectedDrawableIndex;

        private SliderInt     _segFrontSlider;
        private SliderInt     _segBackSlider;
        private VisualElement _edgeParamsGroup;
        private FloatField    _edgeFrontField;
        private FloatField    _edgeBackField;
        private Toggle        _edgeInwardToggle;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft =
            _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Solidify / 厚み付け"));
            _root.Add(new HelpBox(
                "選択した薄い面群に厚みを付けます。表裏2枚のコピーを厚みの半分ずつ移動し、" +
                "孤立エッジを側面でつなぎます。元の面はそのまま残ります。",
                HelpBoxMessageType.Info));

            _infoLabel = InfoLabel("選択面: 0");
            _root.Add(_infoLabel);

            // ── 厚み ───────────────────────────────────────────────────
            var thickRow = MakeLabeledRow("厚み:");
            _thicknessField = new FloatField { value = 0.1f };
            _thicknessField.style.flexGrow = 1;
            _thicknessField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.Thickness = e.newValue;
                _thicknessField.SetValueWithoutNotify(h.Thickness);
            });
            thickRow.Add(_thicknessField);
            _root.Add(thickRow);

            // ── 追加先 ─────────────────────────────────────────────────
            _root.Add(SectionLabel("追加先"));

            _addToExistingToggle = new Toggle("既存の描画オブジェクトに追加") { value = false };
            _addToExistingToggle.style.marginTop = 3;
            _addToExistingToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.AddToExisting = e.newValue;
                RefreshAddTargetChoices();
                RefreshNameFieldMode();
            });
            _root.Add(_addToExistingToggle);

            // 名前欄と追加先ドロップダウンは同じ行に置き、display で見せ分ける。
            var nameRow = MakeLabeledRow("名前:");

            _nameField = new TextField { value = GetH()?.MeshName ?? "Solidify" };
            _nameField.style.flexGrow = 1;
            _nameField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.MeshName = e.newValue;
            });
            nameRow.Add(_nameField);

            _addTargetField = new DropdownField(new System.Collections.Generic.List<string>(), -1);
            _addTargetField.style.flexGrow = 1;
            _addTargetField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                int i = _addTargetField.index;
                h.AddTargetIndex = (i >= 0 && i < _addTargetIndices.Count) ? _addTargetIndices[i] : -1;
            });
            nameRow.Add(_addTargetField);

            _root.Add(nameRow);

            RefreshAddTargetChoices();
            RefreshNameFieldMode();

            // ── エッジ（角処理） ───────────────────────────────────────
            _root.Add(SectionLabel("エッジ（0=無効 / 1=面取り / 2以上=ラウンド）"));

            _root.Add(SmallLabel("前面エッジ分割数:"));
            _segFrontSlider = new SliderInt(0, 8) { value = 0 };
            _segFrontSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.SegmentsFront = e.newValue;
                _segFrontSlider.SetValueWithoutNotify(h.SegmentsFront);
                UpdateEdgeParamVisibility();
            });
            _root.Add(_segFrontSlider);

            _root.Add(SmallLabel("背面エッジ分割数:"));
            _segBackSlider = new SliderInt(0, 8) { value = 0 };
            _segBackSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.SegmentsBack = e.newValue;
                _segBackSlider.SetValueWithoutNotify(h.SegmentsBack);
                UpdateEdgeParamVisibility();
            });
            _root.Add(_segBackSlider);

            // 分割数が両方 0 のときは以下を隠す
            _edgeParamsGroup = new VisualElement();
            _root.Add(_edgeParamsGroup);

            var efRow = MakeLabeledRow("前面エッジサイズ:");
            _edgeFrontField = new FloatField { value = 0.02f };
            _edgeFrontField.style.flexGrow = 1;
            _edgeFrontField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.EdgeSizeFront = e.newValue;
                _edgeFrontField.SetValueWithoutNotify(h.EdgeSizeFront);
            });
            efRow.Add(_edgeFrontField);
            _edgeParamsGroup.Add(efRow);

            var ebRow = MakeLabeledRow("背面エッジサイズ:");
            _edgeBackField = new FloatField { value = 0.02f };
            _edgeBackField.style.flexGrow = 1;
            _edgeBackField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.EdgeSizeBack = e.newValue;
                _edgeBackField.SetValueWithoutNotify(h.EdgeSizeBack);
            });
            ebRow.Add(_edgeBackField);
            _edgeParamsGroup.Add(ebRow);

            _edgeInwardToggle = new Toggle("内向きエッジ") { value = false };
            _edgeInwardToggle.style.marginTop = 3;
            _edgeInwardToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.EdgeInward = e.newValue;
            });
            _edgeParamsGroup.Add(_edgeInwardToggle);

            _edgeParamsGroup.Add(SmallLabel(
                "エッジサイズは厚みの半分未満に丸められます。"));

            UpdateEdgeParamVisibility();

            // ── 実行 ───────────────────────────────────────────────────
            var execBtn = new Button(() =>
            {
                var h = GetH();
                var targets = ActiveMasterIndices();
                if (h == null || targets == null) return;

                SendCommand?.Invoke(new SolidifyCommand(
                    ModelIndex, targets, h.Thickness,
                    segmentsFront:  h.SegmentsFront,
                    segmentsBack:   h.SegmentsBack,
                    edgeSizeFront:  h.EdgeSizeFront,
                    edgeSizeBack:   h.EdgeSizeBack,
                    edgeInward:     h.EdgeInward,
                    meshName:       h.MeshName,
                    addToExisting:  h.AddToExisting,
                    addTargetIndex: h.AddTargetIndex));
                Refresh();
            }) { text = "厚み付け実行" };
            execBtn.style.height    = 30;
            execBtn.style.marginTop = 6;
            _root.Add(execBtn);

            _resultLabel = InfoLabel("");
            _root.Add(_resultLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            if (_infoLabel != null)
                _infoLabel.text = $"選択面: {h.SelectedFaceCount}";

            if (_resultLabel != null)
                _resultLabel.text = h.LastMessage ?? "";

            _thicknessField?.SetValueWithoutNotify(h.Thickness);
            _addToExistingToggle?.SetValueWithoutNotify(h.AddToExisting);
            _nameField?.SetValueWithoutNotify(h.MeshName ?? "");
            RefreshAddTargetChoices();
            RefreshNameFieldMode();
            _segFrontSlider?.SetValueWithoutNotify(h.SegmentsFront);
            _segBackSlider?.SetValueWithoutNotify(h.SegmentsBack);
            _edgeFrontField?.SetValueWithoutNotify(h.EdgeSizeFront);
            _edgeBackField?.SetValueWithoutNotify(h.EdgeSizeBack);
            _edgeInwardToggle?.SetValueWithoutNotify(h.EdgeInward);

            UpdateEdgeParamVisibility();
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 追加先ドロップダウンの選択肢を作り直す。
        /// 既定選択は選択オブジェクトリストの先頭。前回の選択が残っていればそちらを優先。
        /// </summary>
        private void RefreshAddTargetChoices()
        {
            if (_addTargetField == null) return;

            var h = GetH();

            _addTargetIndices.Clear();
            var labels = new System.Collections.Generic.List<string>();

            var list = GetDrawableIndexList?.Invoke();
            if (list != null)
            {
                foreach (var (label, masterIndex) in list)
                {
                    labels.Add(label);
                    _addTargetIndices.Add(masterIndex);
                }
            }

            _addTargetField.choices = labels;

            if (labels.Count == 0)
            {
                if (h != null) h.AddTargetIndex = -1;
                _addTargetField.SetValueWithoutNotify(string.Empty);
                return;
            }

            int want = (h != null) ? _addTargetIndices.IndexOf(h.AddTargetIndex) : -1;
            if (want < 0)
            {
                int first = GetFirstSelectedDrawableIndex?.Invoke() ?? -1;
                want = _addTargetIndices.IndexOf(first);
            }
            if (want < 0) want = 0;

            if (h != null) h.AddTargetIndex = _addTargetIndices[want];
            _addTargetField.SetValueWithoutNotify(labels[want]);
        }

        /// <summary>名前欄と追加先ドロップダウンの見せ分けを現在の追加先へ合わせる。</summary>
        private void RefreshNameFieldMode()
        {
            bool existing = GetH()?.AddToExisting ?? false;
            if (_nameField      != null) _nameField.style.display      = existing ? DisplayStyle.None : DisplayStyle.Flex;
            if (_addTargetField != null) _addTargetField.style.display = existing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateEdgeParamVisibility()
        {
            if (_edgeParamsGroup == null) return;

            var h = GetH();
            int segF = h?.SegmentsFront ?? (_segFrontSlider?.value ?? 0);
            int segB = h?.SegmentsBack  ?? (_segBackSlider?.value  ?? 0);

            bool show = segF > 0 || segB > 0;
            _edgeParamsGroup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 8;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label SmallLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 2;
            return l;
        }

        private static Label InfoLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 3;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeLabeledRow(string labelText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 3;

            var l = new Label(labelText);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 11;
            l.style.width = 110;
            row.Add(l);

            return row;
        }
    }
}
