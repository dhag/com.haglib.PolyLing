// PlayerEdgeExtrudeSubPanel.cs
// EdgeExtrudeToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerEdgeExtrudeSubPanel
    {
        public Func<EdgeExtrudeToolHandler> GetH;
        private VisualElement _root;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Edge Extrude"));
            _root.Add(new HelpBox("選択エッジをドラッグして押し出します", HelpBoxMessageType.Info));
            var modeChoices = new System.Collections.Generic.List<string> { "ViewPlane", "Normal", "Free" };
            var modeValues = new[] { EdgeExtrudeSettings.ExtrudeMode.ViewPlane, EdgeExtrudeSettings.ExtrudeMode.Normal, EdgeExtrudeSettings.ExtrudeMode.Free };
            var modeDD = new DropdownField("Mode", modeChoices, 0);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e => {
                int idx = modeChoices.IndexOf(e.newValue);
                if (idx >= 0 && GetH() != null) GetH().Mode = modeValues[idx];
            });
            _root.Add(modeDD);
            var snapToggle = new Toggle("Snap to Axis") { value = false };
            snapToggle.style.color = new StyleColor(Color.white);
            snapToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().SnapToAxis = e.newValue; });
            _root.Add(snapToggle);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
        }

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
