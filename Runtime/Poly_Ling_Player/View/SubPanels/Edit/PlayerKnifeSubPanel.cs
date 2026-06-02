// PlayerKnifeSubPanel.cs
// ナイフツール用サブパネル（一新版）。Mode = ラダー切断 / Erase。
// 操作: 開始頂点 → セグメント(1辺) → 終了頂点（端点は既存頂点）。

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
        private Label         _statusLabel;

        private static readonly List<string> ModeChoices = new List<string> { "Ladder Cut", "Erase" };
        private static readonly KnifeMode[]   ModeValues  = { KnifeMode.LadderCut, KnifeMode.Erase };

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

            _statusLabel = new Label();
            _statusLabel.style.color = new StyleColor(Color.white);
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);

            _root.Add(new HelpBox(
                "ラダー切断: 開始頂点 → セグメント辺 → 終了頂点 の順にクリック。\n端点は既存頂点。解決できない場合は何もしません。",
                HelpBoxMessageType.Info));

            Refresh();
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            int modeIdx = Array.IndexOf(ModeValues, h.Mode);
            _modeDD?.SetValueWithoutNotify(modeIdx >= 0 ? ModeChoices[modeIdx] : ModeChoices[0]);
            if (_statusLabel != null) _statusLabel.text = h.StageText();
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
