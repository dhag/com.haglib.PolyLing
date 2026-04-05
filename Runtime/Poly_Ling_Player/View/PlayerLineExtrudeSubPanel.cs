// PlayerLineExtrudeSubPanel.cs
// LineExtrudeToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerLineExtrudeSubPanel
    {
        public Func<LineExtrudeToolHandler> GetH;
        private VisualElement _root;
        private Label         _infoLabel;
        private VisualElement _loopListContainer;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Line Extrude"));
            _root.Add(new HelpBox("ライン（辺数=2の面）を選択してループを解析します", HelpBoxMessageType.Info));
            _infoLabel = InfoLabel(); _root.Add(_infoLabel);
            var analyzeBtn = new Button(() => { GetH()?.AnalyzeLoops(); Refresh(); }) { text = "Analyze Loops" };
            analyzeBtn.style.marginTop = 4; analyzeBtn.style.marginBottom = 3;
            _root.Add(analyzeBtn);
            var saveBtn = new Button(() => GetH()?.SaveAsCSV()) { text = "Save as CSV" };
            _root.Add(saveBtn);

            // ループ詳細リスト
            _loopListContainer = new VisualElement();
            _loopListContainer.style.marginTop = 4;
            _root.Add(_loopListContainer);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _infoLabel.text = $"Lines: {h.SelectedLineCount}  Loops: {h.DetectedLoopCount}";

            // ループ詳細リスト更新
            if (_loopListContainer != null)
            {
                _loopListContainer.Clear();
                var summaries = h.GetLoopSummaries();
                if (summaries.Count > 0)
                {
                    var header = new Label("ループ詳細:");
                    header.style.fontSize = 10;
                    header.style.color    = new StyleColor(new UnityEngine.Color(0.65f, 0.8f, 1f));
                    header.style.marginBottom = 2;
                    _loopListContainer.Add(header);

                    foreach (var loop in summaries)
                    {
                        string typeStr = loop.IsHole ? "Hole" : "Outer";
                        var lbl = new Label($"  Loop {loop.Index + 1}: {loop.VertexCount} vertices  ({typeStr})");
                        lbl.style.fontSize = 10;
                        _loopListContainer.Add(lbl);
                    }
                }
            }
        }

        // ── ヘルパー ──────────────────────────────────────────────────────

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.marginTop = 4; l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
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
