// PlayerFlipFaceSubPanel.cs
// FlipFaceToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerFlipFaceSubPanel
    {
        public Func<FlipFaceToolHandler> GetH;
        private VisualElement _root;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Flip Face"));
            _root.Add(new HelpBox("選択面の法線方向を反転します", HelpBoxMessageType.Info));
            var flipSelBtn = new Button(() => GetH()?.FlipSelected()) { text = "Flip Selected" };
            flipSelBtn.style.height = 30; flipSelBtn.style.marginTop = 6; flipSelBtn.style.marginBottom = 3;
            _root.Add(flipSelBtn);
            var flipAllBtn = new Button(() => GetH()?.FlipAll()) { text = "Flip All" };
            _root.Add(flipAllBtn);
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
