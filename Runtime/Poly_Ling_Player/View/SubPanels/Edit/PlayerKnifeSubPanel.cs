// PlayerKnifeSubPanel.cs
// ナイフツール用サブパネル。Mode = ラダーカット / 一意分割。
// 「等分割」チェックで各モードを N 等分（オフは自由比率1本）。
// 辺消去(Erase)はアルゴリズムのみ保持し UI からは除外。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerKnifeSubPanel
    {
        public Func<KnifeToolHandler> GetH;

        private VisualElement _root;
        private DropdownField _modeDD;
        private Toggle        _equalToggle;
        private VisualElement _divRow;
        private IntegerField  _divField;
        private Label         _statusLabel;

        private static readonly List<string> ModeChoices =
            new List<string> { "ラダーカット", "一意分割" };
        private static readonly KnifeMode[] ModeValues =
            { KnifeMode.LadderCut, KnifeMode.BeltLoop };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Knife"));

            _modeDD = new DropdownField("Mode", ModeChoices, 0);
            _modeDD.style.color = new StyleColor(Color.white);
            _modeDD.RegisterValueChangedCallback(e =>
            {
                int idx = ModeChoices.IndexOf(e.newValue);
                var h = GetH(); if (h == null || idx < 0) return;
                h.Mode = ModeValues[idx];
                Refresh();
            });
            _root.Add(_modeDD);

            // 等分割チェック（両モード共通）
            _equalToggle = new Toggle("等分割");
            _equalToggle.style.color = new StyleColor(Color.white);
            _equalToggle.style.marginTop = 2;
            _equalToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h == null) return;
                h.EqualDivide = e.newValue;
                Refresh();
            });
            _root.Add(_equalToggle);

            // 分割数（等分割オン時のみ表示）
            _divRow = new VisualElement();
            _divRow.style.flexDirection = FlexDirection.Row;
            _divRow.style.marginTop = 2;
            _divField = new IntegerField("分割数") { value = 2 };
            _divField.style.flexGrow = 1;
            _divField.style.color = new StyleColor(Color.white);
            _divField.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h == null) return;
                int n = e.newValue < 2 ? 2 : e.newValue;
                h.Divisions = n;
                if (n != e.newValue) _divField.SetValueWithoutNotify(n);
                Refresh();
            });
            _divRow.Add(_divField);
            _root.Add(_divRow);

            _statusLabel = new Label();
            _statusLabel.style.color = new StyleColor(Color.white);
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);

            _root.Add(new HelpBox(
                "ラダーカット: 開始頂点→線分→終了頂点。\n一意分割: 辺を1回クリックでベルト/ループを切断。\n等分割: オンで分割数だけ等分、オフはクリック位置で1本。",
                HelpBoxMessageType.Info));

            Refresh();
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            int modeIdx = Array.IndexOf(ModeValues, h.Mode);
            _modeDD?.SetValueWithoutNotify(modeIdx >= 0 ? ModeChoices[modeIdx] : ModeChoices[0]);

            _equalToggle?.SetValueWithoutNotify(h.EqualDivide);
            if (_divRow != null)
                _divRow.style.display = h.EqualDivide ? DisplayStyle.Flex : DisplayStyle.None;
            _divField?.SetValueWithoutNotify(h.Divisions);

            if (_statusLabel == null) return;

            // 開始頂点／通過線分が確定していれば具体情報を表示。
            // 未確定（キャンセル・終了/実行後）は案内文のみ＝情報行はクリアされる。
            string info;
            if (h.Mode == KnifeMode.LadderCut && h.HasStart)
            {
                info = $"開始頂点: v{h.StartVertex}";
                if (h.HasSegment)
                {
                    info += $"\n通過線分: v{h.Segment.V1}–v{h.Segment.V2}";
                    if (!h.EqualDivide)
                        info += $"（切断比率 {h.CutRatio:0.00}）";
                }
            }
            else
            {
                info = h.StageText();
            }

            if (h.EqualDivide)
                info += $"\n分割数: {h.Divisions}";
            _statusLabel.text = info;
        }

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.marginTop = 4;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
