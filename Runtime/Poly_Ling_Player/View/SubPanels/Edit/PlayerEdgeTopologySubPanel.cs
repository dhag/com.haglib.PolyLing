// PlayerEdgeTopologySubPanel.cs
// EdgeTopologyToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerEdgeTopologySubPanel
    {
        public Func<EdgeTopologyToolHandler> GetH;
        private VisualElement _root;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Edge Topology"));
            var modeChoices = new System.Collections.Generic.List<string> { "Flip", "Split", "Dissolve" };
            var modeValues = new[] { EdgeTopoMode.Flip, EdgeTopoMode.Split, EdgeTopoMode.Dissolve };
            var modeDD = new DropdownField("Mode", modeChoices, 0);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e => {
                int idx = modeChoices.IndexOf(e.newValue);
                if (idx >= 0 && GetH() != null) GetH().ModePublic = modeValues[idx];
                UpdateHelp(idx);
            });
            _root.Add(modeDD);
            var help = new HelpBox("エッジをクリックして操作", HelpBoxMessageType.Info);
            help.style.color = new StyleColor(Color.white);
            help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(help);
        }

        private void UpdateHelp(int idx)
        {
            // help text update handled by HelpBox above
        }

        public void Refresh() {}

        // ── ヘルパー ──────────────────────────────────────────────────────

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop = 4; l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 2;
            return l;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static SliderInt MakeIntSlider(string label, int min, int max, int init, Action<int> onChange)
        {
            var s = new SliderInt(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
