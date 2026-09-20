// PlayerBillboardProfileSubPanel.cs
// ビルボード上の 2D プロファイル編集（線分群）の Player 版サブパネル（UIToolkit）。
// 実処理は BillboardProfileToolHandler。モデルは線分群コマンドでだけ変える。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerBillboardProfileSubPanel
    {
        public Func<BillboardProfileToolHandler> GetH;
        /// <summary>対象オブジェクトの名前と線分群の数（表示用）。</summary>
        public Func<(string Name, int Groups)> GetTargetInfo;
        /// <summary>描画を終える（Escape と同じ）。</summary>
        public Action OnFinish;

        // UI 自動操作の ID は "billboardProfile.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;

        [UiControl("subMode", Description = "編集の仕方。Line = 面の追加の線分モードと同じ使い方")]
        private DropdownField _modeField;
        [UiControl("extendExisting", Description = "開いた線分群の終点から描き始めたとき、その群を伸ばす（OFF なら新しい群を作り始点を親にする）")]
        private Toggle _extendToggle;
        [UiControl("target", Safety = UiSafety.ReadOnly, Description = "編集対象のオブジェクトと線分群の数")]
        private Label _targetLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の状態")]
        private Label _statusLabel;
        [UiControl("finish", Safety = UiSafety.SafeWrite, Description = "描画を終える（Escape / 右クリックと同じ）")]
        private Button _finishBtn;

        private static readonly List<string> ModeChoices = new List<string>
        {
            "Line（面の追加の線分モード）",
            "Profile（回転体／2D押し出しの操作）",
            "Freeform（パワーポイント風の自由曲線）",
        };

        [UiControl("selection", Safety = UiSafety.ReadOnly, Description = "選択中の点の数（Profile）")]
        private Label _selLabel;
        [UiControl("deletePoints", Safety = UiSafety.SafeWrite, Description = "選択中の点を消す（Profile）")]
        private Button _deleteBtn;
        [UiControl("smooth", Safety = UiSafety.SafeWrite, Description = "選択中の点を滑らかにする（接線・弦の 1/3）")]
        private Button _smoothBtn;
        [UiControl("constraintSide", Description = "拘束を設定するハンドル（入り / 出）")]
        private DropdownField _sideField;
        [UiControl("constraintDirection", Description = "向きの拘束")]
        private DropdownField _dirField;
        [UiControl("constraintLength", Description = "長さの拘束")]
        private DropdownField _lenField;
        [UiControl("constraintRatio", Description = "弦の比率（長さ＝弦の比率のとき）")]
        private FloatField _ratioField;
        [UiControl("constraintGroup", Description = "長さの組の ID（長さ＝長さの組のとき）")]
        private IntegerField _groupField;
        [UiControl("applyConstraint", Safety = UiSafety.SafeWrite, Description = "選択中の点へ拘束を設定する")]
        private Button _applyConstraintBtn;
        [UiControl(Ignore = true)]
        private VisualElement _profileBox;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft =
            _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            var header = new Label("Line Group / 線分群の編集");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new StyleColor(Color.white);
            header.style.marginBottom = 4;
            _root.Add(header);

            _root.Add(new HelpBox(
                "選択中の描画オブジェクトのローカル XY 平面（ビルボードならカメラ正対面）に線分群を描きます。\n" +
                "クリックで点を置き、始点をクリックすると閉じます。Escape / 右クリックで描画を終えます。\n" +
                "既存の点の近くをクリックすると、その点に吸着します。",
                HelpBoxMessageType.Info));

            _modeField = new DropdownField("編集の仕方", ModeChoices, 0);
            _modeField.RegisterValueChangedCallback(_ =>
            {
                var h = GetH?.Invoke();
                if (h == null) return;
                h.FinishChain();
                h.Mode = IndexToMode(_modeField.index);
                Refresh();
            });
            _root.Add(_modeField);

            _extendToggle = new Toggle("既存の線分群を伸ばす") { value = false };
            _extendToggle.style.color = new StyleColor(Color.white);
            _extendToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.ExtendExisting = e.newValue;
            });
            _root.Add(_extendToggle);

            _targetLabel = new Label();
            _targetLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _root.Add(_targetLabel);

            _statusLabel = new Label();
            _statusLabel.style.color = new StyleColor(new Color(0.9f, 0.85f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);

            _finishBtn = new Button(() => { OnFinish?.Invoke(); Refresh(); }) { text = "描画を終える" };
            _root.Add(_finishBtn);

            // ── Profile（A）用 ──
            _profileBox = new VisualElement();
            _profileBox.Add(new HelpBox(
                "点をクリックで選択（Shift 追加・Ctrl 除外）、ドラッグで移動。ハンドルはドラッグで向きと長さを変えます。\n" +
                "弦をクリックすると点を挿入、空いた所のドラッグで矩形選択、Delete で選択点を消します。",
                HelpBoxMessageType.None));
            _selLabel = new Label();
            _selLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _profileBox.Add(_selLabel);

            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            _deleteBtn = new Button(() => { GetH?.Invoke()?.DeleteSelectedPoints(); Refresh(); }) { text = "選択点を削除" };
            _smoothBtn = new Button(() => { GetH?.Invoke()?.SmoothSelectedPoints(); Refresh(); }) { text = "滑らかにする" };
            _deleteBtn.style.flexGrow = 1; _smoothBtn.style.flexGrow = 1;
            row.Add(_deleteBtn); row.Add(_smoothBtn);
            _profileBox.Add(row);

            _sideField  = new DropdownField("ハンドル", new List<string> { "入り（前の点の側）", "出（後の点の側）" }, 1);
            _dirField   = new DropdownField("向き", new List<string> { "自由", "弦（隣の点を向く）", "接線（反対側と一直線）" }, 0);
            _lenField   = new DropdownField("長さ", new List<string> { "自由", "弦の比率", "反対側と等長", "長さの組" }, 0);
            _ratioField = new FloatField("比率") { value = 1f / 3f };
            _groupField = new IntegerField("長さの組 ID") { value = 1 };
            _applyConstraintBtn = new Button(() =>
            {
                var h = GetH?.Invoke();
                if (h == null) return;
                var c = new Poly_Ling.Data.HandleConstraint
                {
                    Direction     = (Poly_Ling.Data.HandleDirection)_dirField.index,
                    Length        = (Poly_Ling.Data.HandleLength)_lenField.index,
                    Ratio         = _ratioField.value,
                    LengthGroupId = _groupField.value,
                };
                h.SetSelectedConstraint(_sideField.index == 1, c);
                Refresh();
            }) { text = "選択点へ拘束を設定" };
            _profileBox.Add(_sideField);
            _profileBox.Add(_dirField);
            _profileBox.Add(_lenField);
            _profileBox.Add(_ratioField);
            _profileBox.Add(_groupField);
            _profileBox.Add(_applyConstraintBtn);
            _root.Add(_profileBox);

            Refresh();
        }

        private static BillboardProfileToolHandler.SubMode IndexToMode(int index)
        {
            switch (index)
            {
                case 1:  return BillboardProfileToolHandler.SubMode.Profile;
                case 2:  return BillboardProfileToolHandler.SubMode.Freeform;
                default: return BillboardProfileToolHandler.SubMode.Line;
            }
        }

        public void Refresh()
        {
            if (_root == null) return;
            var h = GetH?.Invoke();
            if (h != null)
            {
                if (_extendToggle.value != h.ExtendExisting) _extendToggle.SetValueWithoutNotify(h.ExtendExisting);
                _statusLabel.text = h.Status ?? "";
                _finishBtn.SetEnabled(h.ChainPoints.Count > 0);

                bool profile = h.Mode == BillboardProfileToolHandler.SubMode.Profile;
                _profileBox.style.display = profile ? DisplayStyle.Flex : DisplayStyle.None;
                _finishBtn.style.display  = profile ? DisplayStyle.None : DisplayStyle.Flex;
                _extendToggle.style.display = profile ? DisplayStyle.None : DisplayStyle.Flex;
                int sel = h.SelectedPoints.Count;
                _selLabel.text = $"選択中の点：{sel} 個";
                _deleteBtn.SetEnabled(sel > 0);
                _smoothBtn.SetEnabled(sel > 0);
                _applyConstraintBtn.SetEnabled(sel > 0);
                int idx = profile ? 1 : (h.Mode == BillboardProfileToolHandler.SubMode.Freeform ? 2 : 0);
                if (_modeField.index != idx) _modeField.SetValueWithoutNotify(ModeChoices[idx]);
                bool free = h.Mode == BillboardProfileToolHandler.SubMode.Freeform;
                _finishBtn.style.display  = profile ? DisplayStyle.None : DisplayStyle.Flex;
                _finishBtn.SetEnabled(h.ChainPoints.Count > 0 || h.FreeformPoints.Count > 0);
                _finishBtn.text = free ? "確定する（Enter と同じ）" : "描画を終える";
                _extendToggle.style.display = (profile || free) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            var info = GetTargetInfo?.Invoke();
            _targetLabel.text = info.HasValue && !string.IsNullOrEmpty(info.Value.Name)
                ? $"対象：{info.Value.Name}（線分群 {info.Value.Groups} 本）"
                : "対象：描画オブジェクトを 1 つ選んでください";
        }
    }
}
