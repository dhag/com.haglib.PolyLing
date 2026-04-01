// PlayerFaceExtrudeSubPanel.cs
// FaceExtrudeToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerFaceExtrudeSubPanel
    {
        public Func<FaceExtrudeToolHandler> GetH;
        private VisualElement _root;
        private Slider _bevelSlider;
        private VisualElement _bevelGroup;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Face Extrude"));
            _root.Add(new HelpBox("選択面をドラッグして押し出します", HelpBoxMessageType.Info));
            var typeChoices = new System.Collections.Generic.List<string> { "Normal", "Bevel" };
            var typeValues = new[] { FaceExtrudeSettings.ExtrudeType.Normal, FaceExtrudeSettings.ExtrudeType.Bevel };
            var typeDD = new DropdownField("Type", typeChoices, 0);
            typeDD.RegisterValueChangedCallback(e => {
                int idx = typeChoices.IndexOf(e.newValue);
                if (idx >= 0 && GetH() != null) GetH().Type = typeValues[idx];
                if (_bevelGroup != null) _bevelGroup.style.display = idx == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            });
            _root.Add(typeDD);
            _bevelGroup = new VisualElement(); _bevelGroup.style.display = DisplayStyle.None;
            _bevelSlider = MakeSlider("Bevel Scale", 0.01f, 1f, 0.8f, v => { if (GetH() != null) GetH().BevelScale = v; });
            _bevelGroup.Add(_bevelSlider); _root.Add(_bevelGroup);
            var normalToggle = new Toggle("Individual Normals") { value = false };
            normalToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().IndividualNormals = e.newValue; });
            _root.Add(normalToggle);
        }

        public void Refresh() {}

        // ── ヘルパー ──────────────────────────────────────────────────────

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.marginTop = 4; l.style.marginBottom = 3;
            l.style.color = new StyleColor(new UnityEngine.Color(0.85f, 0.85f, 0.85f));
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color = new StyleColor(new UnityEngine.Color(0.7f, 0.7f, 0.7f));
            l.style.fontSize = 10; l.style.marginBottom = 2;
            return l;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static SliderInt MakeIntSlider(string label, int min, int max, int init, Action<int> onChange)
        {
            var s = new SliderInt(label, min, max) { value = init };
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
