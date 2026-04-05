// PlayerMergeVerticesSubPanel.cs
// 頂点マージツール用サブパネル。エディタ版 MergeVerticesTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerMergeVerticesSubPanel
    {
        public Func<MergeVerticesToolHandler> GetH;

        private VisualElement _root;
        private FloatField    _threshField;
        private Toggle        _previewToggle;
        private Label         _groupsLabel;
        private Label         _vertsLabel;
        private VisualElement _detailList;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Merge Vertices"));
            _root.Add(new HelpBox("しきい値以内の頂点を結合します", HelpBoxMessageType.Info));

            // Threshold FloatField
            var threshRow = new VisualElement();
            threshRow.style.flexDirection = FlexDirection.Row;
            threshRow.style.marginBottom  = 3;
            var threshLbl = new Label("Threshold");
            threshLbl.style.width = 70; threshLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _threshField = new FloatField { value = 0.001f };
            _threshField.style.flexGrow = 1;
            _threshField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.0001f, e.newValue);
                _threshField.SetValueWithoutNotify(v);
                var h = GetH(); if (h != null) h.Threshold = v;
            });
            threshRow.Add(threshLbl); threshRow.Add(_threshField);
            _root.Add(threshRow);

            // プリセットボタン
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            foreach (var (label, val) in new[] { ("0.001", 0.001f), ("0.01", 0.01f), ("0.1", 0.1f) })
            {
                float v = val;
                var b = new Button(() =>
                {
                    _threshField?.SetValueWithoutNotify(v);
                    var h = GetH(); if (h != null) h.Threshold = v;
                }) { text = label };
                b.style.flexGrow = 1;
                presetRow.Add(b);
            }
            _root.Add(presetRow);

            _previewToggle = new Toggle("Show Preview") { value = true };
            _previewToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.ShowPreview = e.newValue; });
            _root.Add(_previewToggle);

            _groupsLabel = InfoLabel(); _root.Add(_groupsLabel);
            _vertsLabel  = InfoLabel(); _root.Add(_vertsLabel);

            // グループ詳細リスト（最大5件）
            _detailList = new VisualElement();
            _root.Add(_detailList);

            var mergeBtn = new Button(() => GetH()?.TriggerMerge()) { text = "Merge" };
            mergeBtn.style.height    = 30;
            mergeBtn.style.marginTop = 6;
            _root.Add(mergeBtn);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _threshField?.SetValueWithoutNotify(h.Threshold);
            _previewToggle?.SetValueWithoutNotify(h.ShowPreview);

            var info = h.PreviewInfo;
            if (info.GroupCount > 0)
            {
                _groupsLabel.text = $"Groups: {info.GroupCount}";
                _vertsLabel.text  = $"Vertices to remove: {info.TotalVerticesToMerge}";

                // 詳細リスト（最大5グループ）
                _detailList.Clear();
                int showCount = Mathf.Min(info.Groups.Count, 5);
                for (int i = 0; i < showCount; i++)
                {
                    var group = info.Groups[i];
                    var take  = group.Take(8).ToArray();
                    string indices = string.Join(", ", take) + (group.Count > 8 ? "..." : "");
                    var lbl = new Label($"  [{i}] {group.Count} verts: {indices}");
                    lbl.style.fontSize = 9;
                    lbl.style.color    = new StyleColor(Color.white);
                    _detailList.Add(lbl);
                }
                if (info.Groups.Count > 5)
                {
                    var more = new Label($"  ...他 {info.Groups.Count - 5} グループ");
                    more.style.fontSize = 9;
                    more.style.color    = new StyleColor(Color.white);
                    _detailList.Add(more);
                }
            }
            else
            {
                _groupsLabel.text = "No merge candidates";
                _vertsLabel.text  = "";
                _detailList.Clear();
            }
        }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; l.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f)); return l; }
        private static Label InfoLabel() { var l = new Label(); l.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f)); l.style.fontSize = 10; l.style.marginBottom = 2; return l; }
    }
}
