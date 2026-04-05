// PlayerAddFaceSubPanel.cs
// 面追加ツール用サブパネル。エディタ版 AddFaceTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerAddFaceSubPanel
    {
        public Func<AddFaceToolHandler> GetH;

        private VisualElement _root;
        private Label         _progressLabel;
        private Label         _placedHeader;
        private VisualElement _placedList;
        private Toggle        _continuousToggle;
        private VisualElement _continuousRow;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Add Face"));

            // モード選択
            var modeChoices = new List<string> { "Line", "Triangle", "Quad" };
            var modeValues  = new[] { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };
            var modeDD = new DropdownField("Mode", modeChoices, 2);
            modeDD.RegisterValueChangedCallback(e =>
            {
                int idx = modeChoices.IndexOf(e.newValue);
                var h = GetH(); if (h == null || idx < 0) return;
                h.ModePublic = modeValues[idx];
                UpdateConditionals();
            });
            _root.Add(modeDD);

            // ContinuousLine（Line mode 時のみ表示）
            _continuousRow = new VisualElement();
            _continuousToggle = new Toggle("Continuous Line") { value = true };
            _continuousToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.ContinuousLinePublic = e.newValue; });
            _continuousRow.Add(_continuousToggle);
            _root.Add(_continuousRow);

            // 進捗
            _progressLabel = InfoLabel(); _root.Add(_progressLabel);

            // 配置済み点
            _placedHeader = InfoLabel();
            _placedHeader.style.display = DisplayStyle.None;
            _root.Add(_placedHeader);
            _placedList = new VisualElement();
            _root.Add(_placedList);

            // Clear ボタン
            var clearBtn = new Button(() => { GetH()?.ClearPointsPublic(); Refresh(); }) { text = "Clear Points" };
            clearBtn.style.marginTop = 3;
            _root.Add(clearBtn);

            var helpBox = new HelpBox("クリックで点を配置して面を作成します。", HelpBoxMessageType.Info);
            helpBox.style.marginTop = 4;
            _root.Add(helpBox);

            UpdateConditionals();
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _progressLabel.text = $"Points: {h.PlacedPointCount} / {h.RequiredPointsPublic}";
            UpdateConditionals();

            // 配置済み点リスト更新
            if (_placedList != null)
            {
                _placedList.Clear();
                var labels = h.GetPointLabels();
                if (labels.Count > 0)
                {
                    if (_placedHeader != null)
                    {
                        _placedHeader.text    = "配置済み点:";
                        _placedHeader.style.display = DisplayStyle.Flex;
                    }
                    foreach (var label in labels)
                    {
                        var lbl = new Label(label);
                        lbl.style.fontSize = 10;
                        _placedList.Add(lbl);
                    }
                }
                else
                {
                    if (_placedHeader != null) _placedHeader.style.display = DisplayStyle.None;
                }
            }
        }

        private void UpdateConditionals()
        {
            var h = GetH();
            bool isLine = h?.ModePublic == AddFaceMode.Line;
            if (_continuousRow != null)
                _continuousRow.style.display = isLine ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
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
