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

        // UI 自動操作の ID は "faceExtrude.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("bevelScale", Reveal = nameof(RevealBevel), Description = "ベベルの縮小率（Type が Bevel のときだけ表示）")]
        private Slider _bevelSlider;
        [UiControl(Ignore = true)]
        private VisualElement _bevelGroup;
        [UiControl("dragSensitivity", Description = "ドラッグ量に対する押し出し量の比例係数")]
        private FloatField _dragSensField;
        [UiControl("type", Description = "押し出しの種類（Normal / Bevel）")]
        private DropdownField _typeDropdown;
        [UiControl("individualNormals", Description = "面ごとの法線方向へ押し出す")]
        private Toggle _individualNormalsToggle;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Face Extrude"));
            _root.Add(new HelpBox("選択面をドラッグして押し出します", HelpBoxMessageType.Info));
            var typeChoices = new System.Collections.Generic.List<string> { "Normal", "Bevel" };
            var typeValues = new[] { FaceExtrudeSettings.ExtrudeType.Normal, FaceExtrudeSettings.ExtrudeType.Bevel };
            var typeDD = new DropdownField("Type", typeChoices, 0);
            typeDD.style.color = new StyleColor(Color.white);
            typeDD.RegisterValueChangedCallback(e => {
                int idx = typeChoices.IndexOf(e.newValue);
                if (idx >= 0 && GetH() != null) GetH().Type = typeValues[idx];
                if (_bevelGroup != null) _bevelGroup.style.display = idx == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            });
            _root.Add(typeDD);
            _typeDropdown = typeDD;
            _bevelGroup = new VisualElement(); _bevelGroup.style.display = DisplayStyle.None;
            _bevelSlider = MakeSlider("Bevel Scale", 0.01f, 1f, 0.8f, v => { if (GetH() != null) GetH().BevelScale = v; });
            _bevelGroup.Add(_bevelSlider); _root.Add(_bevelGroup);
            var normalToggle = new Toggle("Individual Normals") { value = false };
            normalToggle.style.color = new StyleColor(Color.white);
            normalToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().IndividualNormals = e.newValue; });
            _root.Add(normalToggle);
            _individualNormalsToggle = normalToggle;

            // Drag Sensitivity — テキストボックス（カメラ平面での実ドラッグ距離への比例係数）
            var sensRow = new VisualElement();
            sensRow.style.flexDirection = FlexDirection.Row;
            sensRow.style.marginBottom  = 4;
            var sensLbl = new Label("Drag Sens.");
            sensLbl.style.color = new StyleColor(Color.white);
            sensLbl.style.width = 70; sensLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _dragSensField = new FloatField { value = 1f };
            _dragSensField.style.flexGrow = 1;
            _dragSensField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                _dragSensField.SetValueWithoutNotify(v);
                var h = GetH(); if (h != null) h.DragSensitivity = v;
            });
            sensRow.Add(sensLbl); sensRow.Add(_dragSensField);
            _root.Add(sensRow);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _dragSensField?.SetValueWithoutNotify(h.DragSensitivity);
        }

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。Bevel Scale は Type が Bevel の
        /// ときだけ表示されるので、利用者と同じく Type を Bevel にする。既に Bevel なら false。
        /// </summary>
        private bool RevealBevel()
        {
            if (_typeDropdown == null || _typeDropdown.value == "Bevel") return false;
            _typeDropdown.value = "Bevel";
            return true;
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
