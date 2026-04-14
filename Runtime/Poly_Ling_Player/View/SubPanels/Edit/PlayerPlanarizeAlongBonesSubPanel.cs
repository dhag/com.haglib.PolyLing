// PlayerPlanarizeAlongBonesSubPanel.cs
// PlanarizeAlongBonesTool の Player 版サブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerPlanarizeAlongBonesSubPanel
    {
        public Func<PlanarizeAlongBonesToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _selectedLabel;
        private DropdownField _boneADropdown;
        private DropdownField _boneBDropdown;
        private Label         _sameBoneWarning;
        private DropdownField _planeModeDropdown;
        private Slider        _blendSlider;
        private FloatField    _blendField;
        private Label         _previewLabel;
        private Button        _planarizeBtn;

        private static readonly List<string> PlaneModeChoices =
            new List<string> { "Min Movement", "Anchor to A" };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Planarize Along Bones / ボーン間平面化"));
            _root.Add(new HelpBox(
                "選択頂点をボーンA→B方向に直交する平面に揃えます。\nブレンドで平面化の度合いを調整できます。",
                HelpBoxMessageType.Info));

            // 選択頂点数
            _selectedLabel = InfoLabel();
            _root.Add(_selectedLabel);

            // ボーン A
            _root.Add(SmallHeader("ボーン A（平面基点）:"));
            _boneADropdown = new DropdownField(new List<string> { "—" }, 0);
            _boneADropdown.style.marginBottom = 3;
            _boneADropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h?.BoneNames == null) return;
                int idx = System.Array.IndexOf(h.BoneNames, e.newValue);
                if (idx >= 0) h.BoneIndexA = idx;
                UpdateWarningAndPreview();
            });
            _root.Add(_boneADropdown);

            // ボーン B
            _root.Add(SmallHeader("ボーン B（方向）:"));
            _boneBDropdown = new DropdownField(new List<string> { "—" }, 0);
            _boneBDropdown.style.marginBottom = 3;
            _boneBDropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h?.BoneNames == null) return;
                int idx = System.Array.IndexOf(h.BoneNames, e.newValue);
                if (idx >= 0) h.BoneIndexB = idx;
                UpdateWarningAndPreview();
            });
            _root.Add(_boneBDropdown);

            // 同一ボーン警告
            _sameBoneWarning = new Label("A と B に異なるボーンを選択してください");
            _sameBoneWarning.style.color   = new StyleColor(new Color(1f, 0.6f, 0.2f));
            _sameBoneWarning.style.fontSize = 10;
            _sameBoneWarning.style.display = DisplayStyle.None;
            _root.Add(_sameBoneWarning);

            // 平面位置モード
            _root.Add(SmallHeader("平面位置:"));
            _planeModeDropdown = new DropdownField(PlaneModeChoices, 0);
            _planeModeDropdown.style.marginBottom = 4;
            _planeModeDropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.PlaneMode = (PlanePlacementMode)PlaneModeChoices.IndexOf(e.newValue);
            });
            _root.Add(_planeModeDropdown);

            // ブレンドスライダー + 数値フィールド
            _root.Add(SmallHeader("ブレンド（0=なし、1=完全）:"));
            var blendRow = new VisualElement();
            blendRow.style.flexDirection = FlexDirection.Row;
            blendRow.style.marginBottom  = 4;

            _blendSlider = new Slider(0f, 1f) { value = 1f };
            _blendSlider.style.flexGrow = 1;
            _blendSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.Blend = e.newValue;
                _blendField?.SetValueWithoutNotify(e.newValue);
            });

            _blendField = new FloatField { value = 1f };
            _blendField.style.width = 50;
            _blendField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp01(e.newValue);
                _blendField.SetValueWithoutNotify(v);
                var h = GetH();
                if (h != null) h.Blend = v;
                _blendSlider?.SetValueWithoutNotify(v);
            });

            blendRow.Add(_blendSlider);
            blendRow.Add(_blendField);
            _root.Add(blendRow);

            // プレビューラベル（ボーン位置・距離）
            _previewLabel = InfoLabel();
            _previewLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_previewLabel);

            // 実行ボタン
            _planarizeBtn = new Button(() => GetH()?.TriggerPlanarize()) { text = "平面化実行" };
            _planarizeBtn.style.height    = 30;
            _planarizeBtn.style.marginTop = 6;
            _root.Add(_planarizeBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            h.RebuildBoneList();

            // ボーンリスト更新
            var names = h.BoneNames;
            if (names != null && names.Length > 0)
            {
                var choices = new List<string>(names);
                _boneADropdown?.choices.Clear();
                _boneADropdown?.choices.AddRange(choices);
                _boneBDropdown?.choices.Clear();
                _boneBDropdown?.choices.AddRange(choices);

                int a = Mathf.Clamp(h.BoneIndexA, 0, names.Length - 1);
                int b = Mathf.Clamp(h.BoneIndexB, 0, names.Length - 1);
                _boneADropdown?.SetValueWithoutNotify(names[a]);
                _boneBDropdown?.SetValueWithoutNotify(names[b]);
            }
            else
            {
                _boneADropdown?.SetValueWithoutNotify("—");
                _boneBDropdown?.SetValueWithoutNotify("—");
            }

            _selectedLabel.text = $"選択中: {h.SelectedVertexCount} 頂点";
            _planeModeDropdown?.SetValueWithoutNotify(PlaneModeChoices[(int)h.PlaneMode]);
            _blendSlider?.SetValueWithoutNotify(h.Blend);
            _blendField?.SetValueWithoutNotify(h.Blend);

            UpdateWarningAndPreview();

            bool hasBones     = names != null && names.Length > 0;
            bool diffBones    = h.BoneIndexA != h.BoneIndexB;
            bool canPlanarize = hasBones && diffBones
                                && h.SelectedVertexCount >= 1
                                && h.Blend > 0f;
            if (_planarizeBtn != null)
                _planarizeBtn.SetEnabled(canPlanarize);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateWarningAndPreview()
        {
            var h = GetH();
            if (h == null || _sameBoneWarning == null || _previewLabel == null) return;

            bool same = h.BoneIndexA == h.BoneIndexB;
            _sameBoneWarning.style.display = same ? DisplayStyle.Flex : DisplayStyle.None;

            if (!same && h.BoneNames != null && h.BoneNames.Length > 0)
            {
                Vector3 posA = h.GetBoneWorldPosition(h.BoneIndexA);
                Vector3 posB = h.GetBoneWorldPosition(h.BoneIndexB);
                float   dist = (posB - posA).magnitude;
                _previewLabel.text =
                    $"A: ({posA.x:F3}, {posA.y:F3}, {posA.z:F3})\n" +
                    $"B: ({posB.x:F3}, {posB.y:F3}, {posB.z:F3})\n" +
                    $"距離: {dist:F4}";
            }
            else
            {
                _previewLabel.text = "";
            }
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
