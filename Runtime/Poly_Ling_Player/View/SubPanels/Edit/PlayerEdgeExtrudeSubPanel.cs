// PlayerEdgeExtrudeSubPanel.cs
// EdgeExtrudeToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerEdgeExtrudeSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface Surface;

        private const string Tool = "edgeExtrude";

        // UI 自動操作の ID は "edgeExtrude.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("dragSensitivity", Description = "ドラッグ量に対する押し出し量の比例係数")]
        private FloatField _dragSensField;
        [UiControl("mode", Description = "押し出す方向（ViewPlane / Normal / Free）")]
        private DropdownField _modeDropdown;
        [UiControl("snapToAxis", Description = "軸方向へ吸着する")]
        private Toggle _snapToggle;
        [UiControl("gizmo", Description = "押し出しに使うギズモ（なし／移動／回転／拡大縮小）")]
        private DropdownField _gizmoDropdown;
        [UiControl("extrudePaused", Description = "押し出しの一時停止（チェック中は普通の移動・回転・拡大縮小）")]
        private Toggle _pauseToggle;

        // 並びは EdgeExtrudeToolHandler.GizmoKind の値と同じ（None=0, Move=1, Rotate=2, Scale=3）
        private static readonly System.Collections.Generic.List<string> GizmoChoices =
            new System.Collections.Generic.List<string> { "なし", "移動", "回転", "拡大縮小" };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Edge Extrude"));
            _root.Add(new HelpBox("選択エッジをドラッグして押し出します。ギズモを掴んでも、選択中の辺・線分を押し出して移動・回転・拡大縮小できます", HelpBoxMessageType.Info));

            _gizmoDropdown = new DropdownField("ギズモ", GizmoChoices, 1);
            _gizmoDropdown.style.color = new StyleColor(Color.white);
            _gizmoDropdown.RegisterValueChangedCallback(e =>
            {
                int idx = GizmoChoices.IndexOf(e.newValue);
                if (idx >= 0) Surface.Set(Tool, "gizmo", (EdgeExtrudeToolHandler.GizmoKind)idx);
            });
            _root.Add(_gizmoDropdown);

            _pauseToggle = new Toggle("押し出しの一時停止") { value = false };
            _pauseToggle.style.color = new StyleColor(Color.white);
            _pauseToggle.RegisterValueChangedCallback(e => Surface.Set(Tool, "extrudePaused", e.newValue));
            _root.Add(_pauseToggle);
            var modeChoices = new System.Collections.Generic.List<string> { "ViewPlane", "Normal", "Free" };
            var modeValues = new[] { EdgeExtrudeSettings.ExtrudeMode.ViewPlane, EdgeExtrudeSettings.ExtrudeMode.Normal, EdgeExtrudeSettings.ExtrudeMode.Free };
            var modeDD = new DropdownField("Mode", modeChoices, 0);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e => {
                int idx = modeChoices.IndexOf(e.newValue);
                if (idx >= 0) Surface.Set(Tool, "mode", modeValues[idx]);
            });
            _root.Add(modeDD);
            _modeDropdown = modeDD;
            var snapToggle = new Toggle("Snap to Axis") { value = false };
            snapToggle.style.color = new StyleColor(Color.white);
            snapToggle.RegisterValueChangedCallback(e => Surface.Set(Tool, "snapToAxis", e.newValue));
            _root.Add(snapToggle);
            _snapToggle = snapToggle;

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
                Surface.Set(Tool, "dragSensitivity", v);
            });
            sensRow.Add(sensLbl); sensRow.Add(_dragSensField);
            _root.Add(sensRow);
        }

        public void Refresh()
        {
            if (Surface == null) return;
            _dragSensField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "dragSensitivity", 1f));
            int gi = (int)Surface.Get(Tool, "gizmo", EdgeExtrudeToolHandler.GizmoKind.Move);
            if (_gizmoDropdown != null && gi >= 0 && gi < GizmoChoices.Count && _gizmoDropdown.index != gi)
                _gizmoDropdown.SetValueWithoutNotify(GizmoChoices[gi]);
            bool paused = Surface.GetBool(Tool, "extrudePaused");
            if (_pauseToggle != null && _pauseToggle.value != paused) _pauseToggle.SetValueWithoutNotify(paused);
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
