// PlayerKnifeSubPanel.cs
// ナイフツール用サブパネル。エディタ版 KnifeTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

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
        private Toggle        _edgeSelToggle, _chainToggle;
        // EdgeSelect + Cut 時
        private VisualElement _bisectGroup;
        private Toggle        _edgeBisectToggle;
        private Slider        _cutRatioSlider;
        private VisualElement _cutRatioRow;
        // Vertex mode 時
        private VisualElement _vertexBisectGroup;
        private Toggle        _vertexBisectToggle;
        // 選択状態
        private Label         _statusLabel;

        private static readonly List<string>    ModeChoices = new List<string> { "Cut", "Vertex", "Erase" };
        private static readonly KnifeMode[]     ModeValues  = { KnifeMode.Cut, KnifeMode.Vertex, KnifeMode.Erase };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Knife"));

            _modeDD = new DropdownField("Mode", ModeChoices, 0);
            _modeDD.RegisterValueChangedCallback(e =>
            {
                int idx = ModeChoices.IndexOf(e.newValue);
                var h = GetH(); if (h == null || idx < 0) return;
                h.Mode = ModeValues[idx];
                UpdateConditionals();
            });
            _root.Add(_modeDD);

            _edgeSelToggle = new Toggle("Edge Select") { value = false };
            _edgeSelToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.EdgeSelect = e.newValue; UpdateConditionals(); });
            _root.Add(_edgeSelToggle);

            _chainToggle = new Toggle("Auto Chain") { value = true };
            _chainToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.AutoChain = e.newValue; });
            _root.Add(_chainToggle);

            // Bisect グループ（Cut + EdgeSelect 時）
            _bisectGroup = new VisualElement();
            _edgeBisectToggle = new Toggle("Bisect") { value = false };
            _edgeBisectToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.EdgeBisectMode = e.newValue; UpdateConditionals(); });
            _bisectGroup.Add(_edgeBisectToggle);
            _cutRatioRow = new VisualElement();
            _cutRatioSlider = new Slider("Cut Position", 0.1f, 0.9f) { value = 0.5f };
            _cutRatioSlider.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.CutRatio = e.newValue; });
            _cutRatioRow.Add(_cutRatioSlider);
            _bisectGroup.Add(_cutRatioRow);
            _root.Add(_bisectGroup);

            // VertexBisect グループ（Vertex mode 時）
            _vertexBisectGroup = new VisualElement();
            _vertexBisectToggle = new Toggle("Bisect") { value = false };
            _vertexBisectToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.VertexBisectMode = e.newValue; });
            _vertexBisectGroup.Add(_vertexBisectToggle);
            _root.Add(_vertexBisectGroup);

            // 選択状態ラベル
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.marginTop = 3;
            _root.Add(_statusLabel);

            _root.Add(new HelpBox("クリックで切断位置を指定します", HelpBoxMessageType.Info));

            UpdateConditionals();
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            int modeIdx = System.Array.IndexOf(ModeValues, h.Mode);
            _modeDD?.SetValueWithoutNotify(modeIdx >= 0 ? ModeChoices[modeIdx] : ModeChoices[0]);
            _edgeSelToggle?.SetValueWithoutNotify(h.EdgeSelect);
            _chainToggle?.SetValueWithoutNotify(h.AutoChain);
            _edgeBisectToggle?.SetValueWithoutNotify(h.EdgeBisectMode);
            _cutRatioSlider?.SetValueWithoutNotify(h.CutRatio);
            _vertexBisectToggle?.SetValueWithoutNotify(h.VertexBisectMode);
            UpdateConditionals();
        }

        private void UpdateConditionals()
        {
            var h = GetH();
            bool isCut    = h?.Mode == KnifeMode.Cut;
            bool isVertex = h?.Mode == KnifeMode.Vertex;
            bool isErase  = h?.Mode == KnifeMode.Erase;
            bool edgeSel  = h?.EdgeSelect ?? false;
            bool bisect   = h?.EdgeBisectMode ?? false;

            // EdgeSelect と AutoChain は Erase 以外
            if (_edgeSelToggle != null) _edgeSelToggle.style.display = !isErase ? DisplayStyle.Flex : DisplayStyle.None;
            if (_chainToggle   != null) _chainToggle.style.display   = !isErase ? DisplayStyle.Flex : DisplayStyle.None;

            // Bisect グループ：Cut + EdgeSelect 時
            if (_bisectGroup != null) _bisectGroup.style.display = (isCut && edgeSel) ? DisplayStyle.Flex : DisplayStyle.None;
            // CutRatio：Bisect 有効時のみ
            if (_cutRatioRow != null) _cutRatioRow.style.display = bisect ? DisplayStyle.Flex : DisplayStyle.None;

            // VertexBisect：Vertex mode 時
            if (_vertexBisectGroup != null) _vertexBisectGroup.style.display = isVertex ? DisplayStyle.Flex : DisplayStyle.None;

            // 選択状態表示
            if (_statusLabel != null && h != null)
            {
                if (edgeSel && isCut)
                {
                    if (h.HasFirstEdge)
                        _statusLabel.text = h.BeltEdgeCount > 0
                            ? $"切断エッジ: {h.BeltEdgeCount}"
                            : "最初のエッジを選択済み";
                    else
                        _statusLabel.text = "";
                }
                else if (isVertex && h.HasFirstVertex)
                {
                    _statusLabel.text = "頂点を選択済み";
                }
                else
                {
                    _statusLabel.text = "";
                }
            }
        }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }

    }
}