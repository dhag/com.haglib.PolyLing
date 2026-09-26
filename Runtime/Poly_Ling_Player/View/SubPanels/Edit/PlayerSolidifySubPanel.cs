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
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface              Surface;
        private const string Tool = "solidify";
        public Func<Poly_Ling.View.IProjectView>      GetView;
        public Action<PanelCommand>      SendCommand;

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

        // UI 自動操作の ID は "solidify.<下の Id>"（UiControlAttribute.cs）。
        // 名前欄は「既存の描画オブジェクトに追加」オフ、追加先はオンのときだけ表示される。
        // エッジサイズと内向きエッジは分割数が 1 以上のときだけ表示される。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("info", Safety = UiSafety.ReadOnly, Description = "選択面の数")]
        private Label         _infoLabel;
        [UiControl("result", Safety = UiSafety.ReadOnly, Description = "直近の実行結果")]
        private Label         _resultLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "厚み付けを実行する")]
        private Button        _runBtn;

        [UiControl("thickness", Description = "厚み")]
        private FloatField    _thicknessField;
        [UiControl("addToExisting", Description = "結果を既存の描画オブジェクトに追加する（オフは新しいオブジェクト）")]
        private Toggle        _addToExistingToggle;

        // 名前欄。「既存メッシュに追加」のときは追加先ドロップダウンへ差し替える
        // （図形生成パネルの名前欄と同じ扱い）。
        [UiControl("meshName", Reveal = nameof(RevealNewMesh), Description = "新しいオブジェクトの名前")]
        private TextField     _nameField;
        [UiControl("addTarget", Reveal = nameof(RevealAddTarget), Description = "追加先の描画オブジェクト")]
        private DropdownField _addTargetField;
        private readonly System.Collections.Generic.List<int> _addTargetIndices =
            new System.Collections.Generic.List<int>();

        /// <summary>描画オブジェクト一覧（表示名, MeshContextList インデックス）。</summary>
        public Func<System.Collections.Generic.List<(string Label, int MasterIndex)>> GetDrawableIndexList;

        /// <summary>選択オブジェクトリストの先頭。追加先ドロップダウンの既定選択に使う。</summary>
        public Func<int> GetFirstSelectedDrawableIndex;

        [UiControl("segmentsFront", Description = "前面エッジの分割数（0=無効 / 1=面取り / 2 以上=ラウンド）")]
        private SliderInt     _segFrontSlider;
        [UiControl("segmentsBack", Description = "背面エッジの分割数（0=無効 / 1=面取り / 2 以上=ラウンド）")]
        private SliderInt     _segBackSlider;
        [UiControl(Ignore = true)]
        private VisualElement _edgeParamsGroup;
        [UiControl("edgeSizeFront", Reveal = nameof(RevealEdgeParams), Description = "前面エッジサイズ（厚みの半分未満に丸める）")]
        private FloatField    _edgeFrontField;
        [UiControl("edgeSizeBack", Reveal = nameof(RevealEdgeParams), Description = "背面エッジサイズ（厚みの半分未満に丸める）")]
        private FloatField    _edgeBackField;
        [UiControl("edgeInward", Reveal = nameof(RevealEdgeParams), Description = "内向きエッジ")]
        private Toggle        _edgeInwardToggle;

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。利用者と同じくチェック・分割数を切り替える。
        /// 名前欄は「既存の描画オブジェクトに追加」オフのときだけ表示される。
        /// </summary>
        private bool RevealNewMesh()
        {
            if (_addToExistingToggle == null || !_addToExistingToggle.value) return false;
            _addToExistingToggle.value = false;
            return true;
        }

        /// <summary>追加先は「既存の描画オブジェクトに追加」オンのときだけ表示される。</summary>
        private bool RevealAddTarget()
        {
            if (_addToExistingToggle == null || _addToExistingToggle.value) return false;
            _addToExistingToggle.value = true;
            return true;
        }

        /// <summary>エッジのサイズと向きは、前面か背面の分割数が 1 以上のときだけ表示される。</summary>
        private bool RevealEdgeParams()
        {
            if (_segFrontSlider == null) return false;
            if (_segFrontSlider.value > 0 || (_segBackSlider != null && _segBackSlider.value > 0)) return false;
            _segFrontSlider.value = 1;
            return true;
        }

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
                "境界辺を側面でつなぎます。元の面はそのまま残ります。",
                HelpBoxMessageType.Info));

            _infoLabel = InfoLabel("選択面: 0");
            _root.Add(_infoLabel);

            // ── 厚み ───────────────────────────────────────────────────
            var thickRow = MakeLabeledRow("厚み:");
            _thicknessField = new FloatField { value = 0.1f };
            _thicknessField.style.flexGrow = 1;
            _thicknessField.RegisterValueChangedCallback(e =>
                _thicknessField.SetValueWithoutNotify(Surface.Set(Tool, "thickness", e.newValue)));
            thickRow.Add(_thicknessField);
            _root.Add(thickRow);

            // ── 追加先 ─────────────────────────────────────────────────
            _root.Add(SectionLabel("追加先"));

            _addToExistingToggle = new Toggle("既存の描画オブジェクトに追加") { value = false };
            _addToExistingToggle.style.marginTop = 3;
            _addToExistingToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "addToExisting", e.newValue);
                RefreshAddTargetChoices();
                RefreshNameFieldMode();
            });
            _root.Add(_addToExistingToggle);

            // 名前欄と追加先ドロップダウンは同じ行に置き、display で見せ分ける。
            var nameRow = MakeLabeledRow("名前:");

            _nameField = new TextField { value = Surface?.GetString(Tool, "meshName", "Solidify") ?? "Solidify" };
            _nameField.style.flexGrow = 1;
            _nameField.RegisterValueChangedCallback(e => Surface.Set(Tool, "meshName", e.newValue));
            nameRow.Add(_nameField);

            _addTargetField = new DropdownField(new System.Collections.Generic.List<string>(), -1);
            _addTargetField.style.flexGrow = 1;
            _addTargetField.RegisterValueChangedCallback(e =>
            {
                int i = _addTargetField.index;
                Surface.Set(Tool, "addTargetIndex", (i >= 0 && i < _addTargetIndices.Count) ? _addTargetIndices[i] : -1);
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
                _segFrontSlider.SetValueWithoutNotify(Surface.Set(Tool, "segmentsFront", e.newValue));
                UpdateEdgeParamVisibility();
            });
            _root.Add(_segFrontSlider);

            _root.Add(SmallLabel("背面エッジ分割数:"));
            _segBackSlider = new SliderInt(0, 8) { value = 0 };
            _segBackSlider.RegisterValueChangedCallback(e =>
            {
                _segBackSlider.SetValueWithoutNotify(Surface.Set(Tool, "segmentsBack", e.newValue));
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
                _edgeFrontField.SetValueWithoutNotify(Surface.Set(Tool, "edgeSizeFront", e.newValue)));
            efRow.Add(_edgeFrontField);
            _edgeParamsGroup.Add(efRow);

            var ebRow = MakeLabeledRow("背面エッジサイズ:");
            _edgeBackField = new FloatField { value = 0.02f };
            _edgeBackField.style.flexGrow = 1;
            _edgeBackField.RegisterValueChangedCallback(e =>
                _edgeBackField.SetValueWithoutNotify(Surface.Set(Tool, "edgeSizeBack", e.newValue)));
            ebRow.Add(_edgeBackField);
            _edgeParamsGroup.Add(ebRow);

            _edgeInwardToggle = new Toggle("内向きエッジ") { value = false };
            _edgeInwardToggle.style.marginTop = 3;
            _edgeInwardToggle.RegisterValueChangedCallback(e => Surface.Set(Tool, "edgeInward", e.newValue));
            _edgeParamsGroup.Add(_edgeInwardToggle);

            _edgeParamsGroup.Add(SmallLabel(
                "エッジサイズは厚みの半分未満に丸められます。"));

            UpdateEdgeParamVisibility();

            // ── 実行 ───────────────────────────────────────────────────
            var execBtn = new Button(() =>
            {
                var targets = ActiveMasterIndices();
                if (Surface == null || targets == null) return;

                SendCommand?.Invoke(new SolidifyCommand(
                    ModelIndex, targets, Surface.GetFloat(Tool, "thickness"),
                    segmentsFront:  Surface.GetInt(Tool, "segmentsFront"),
                    segmentsBack:   Surface.GetInt(Tool, "segmentsBack"),
                    edgeSizeFront:  Surface.GetFloat(Tool, "edgeSizeFront"),
                    edgeSizeBack:   Surface.GetFloat(Tool, "edgeSizeBack"),
                    edgeInward:     Surface.GetBool(Tool, "edgeInward"),
                    meshName:       Surface.GetString(Tool, "meshName"),
                    addToExisting:  Surface.GetBool(Tool, "addToExisting"),
                    addTargetIndex: Surface.GetInt(Tool, "addTargetIndex", -1)));
                Refresh();
            }) { text = "厚み付け実行" };
            execBtn.style.height    = 30;
            execBtn.style.marginTop = 6;
            _root.Add(execBtn);
            _runBtn = execBtn;

            _resultLabel = InfoLabel("");
            _root.Add(_resultLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (Surface == null) return;

            if (_infoLabel != null)
                _infoLabel.text = $"選択面: {Surface.GetInt(Tool, "selectedFaceCount")}";

            if (_resultLabel != null)
                _resultLabel.text = Surface.GetString(Tool, "lastMessage");

            _thicknessField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "thickness"));
            _addToExistingToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "addToExisting"));
            _nameField?.SetValueWithoutNotify(Surface.GetString(Tool, "meshName"));
            RefreshAddTargetChoices();
            RefreshNameFieldMode();
            _segFrontSlider?.SetValueWithoutNotify(Surface.GetInt(Tool, "segmentsFront"));
            _segBackSlider?.SetValueWithoutNotify(Surface.GetInt(Tool, "segmentsBack"));
            _edgeFrontField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "edgeSizeFront"));
            _edgeBackField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "edgeSizeBack"));
            _edgeInwardToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "edgeInward"));

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

            // 設定は値が変わるときだけ送る。Refresh から呼ばれるので、毎回送ると
            // 設定 → パネルへの通知 → Refresh → 設定 … と回り続ける。
            int current = Surface?.GetInt(Tool, "addTargetIndex", -1) ?? -1;

            if (labels.Count == 0)
            {
                if (current != -1) Surface?.Set(Tool, "addTargetIndex", -1);
                _addTargetField.SetValueWithoutNotify(string.Empty);
                return;
            }

            int want = _addTargetIndices.IndexOf(current);
            if (want < 0)
            {
                int first = GetFirstSelectedDrawableIndex?.Invoke() ?? -1;
                want = _addTargetIndices.IndexOf(first);
            }
            if (want < 0) want = 0;

            if (current != _addTargetIndices[want]) Surface?.Set(Tool, "addTargetIndex", _addTargetIndices[want]);
            _addTargetField.SetValueWithoutNotify(labels[want]);
        }

        /// <summary>名前欄と追加先ドロップダウンの見せ分けを現在の追加先へ合わせる。</summary>
        private void RefreshNameFieldMode()
        {
            bool existing = Surface?.GetBool(Tool, "addToExisting") ?? false;
            if (_nameField      != null) _nameField.style.display      = existing ? DisplayStyle.None : DisplayStyle.Flex;
            if (_addTargetField != null) _addTargetField.style.display = existing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateEdgeParamVisibility()
        {
            if (_edgeParamsGroup == null) return;

            int segF = Surface?.GetInt(Tool, "segmentsFront") ?? (_segFrontSlider?.value ?? 0);
            int segB = Surface?.GetInt(Tool, "segmentsBack")  ?? (_segBackSlider?.value  ?? 0);

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
