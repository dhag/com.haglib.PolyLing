// PlayerQuadDecimatorSubPanel.cs
// QuadDecimatorPanel の Player 版サブパネル（完全版）。UXML/AssetDatabase 除去。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Panels.QuadDecimator;
using Poly_Ling.UI.QuadDecimator;

namespace Poly_Ling.Player
{
    public class PlayerQuadDecimatorSubPanel
    {
        public Func<ToolContext> GetToolContext;

        // 設定
        private float _targetRatio     = 0.5f;
        private int   _maxPasses       = 5;
        private float _normalAngleDeg  = 15f;
        private float _hardAngleDeg    = 25f;
        private float _uvSeamThreshold = 0.01f;

        // UI
        private Label         _warningLabel;
        private Label         _meshInfoLabel;
        private Label         _noMeshLabel;
        private Slider        _sliderTargetRatio, _sliderNormalAngle, _sliderHardAngle, _sliderUvSeam;
        private SliderInt     _sliderMaxPasses;
        private Button        _btnExecute;
        private Label         _noQuadsLabel;
        private VisualElement _resultSection;
        private Label         _resultSummary;
        private VisualElement _passLogsContainer;

        private DecimatorResult _lastResult;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Quad保持 減数化"));
            root.Add(new HelpBox("Quadグリッドのトポロジを保持しながらポリゴン数を削減します。", HelpBoxMessageType.None));

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            // メッシュ情報
            root.Add(SecLabel("メッシュ情報"));
            _noMeshLabel = new Label("メッシュが選択されていません");
            _noMeshLabel.style.fontSize = 10; _noMeshLabel.style.color = new StyleColor(Color.white);
            root.Add(_noMeshLabel);

            _meshInfoLabel = new Label();
            _meshInfoLabel.style.fontSize     = 10;
            _meshInfoLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _meshInfoLabel.style.marginBottom = 4;
            _meshInfoLabel.style.display      = DisplayStyle.None;
            root.Add(_meshInfoLabel);

            _noQuadsLabel = new Label("選択メッシュにQuad面がありません");
            _noQuadsLabel.style.fontSize = 10;
            _noQuadsLabel.style.color    = new StyleColor(new Color(1f, 0.6f, 0.2f));
            _noQuadsLabel.style.display  = DisplayStyle.None;
            root.Add(_noQuadsLabel);

            // パラメータ
            root.Add(SecLabel("パラメータ"));
            _sliderTargetRatio = MkSlider("目標比率",           0.1f, 0.9f, _targetRatio,     v => _targetRatio     = v);
            _sliderMaxPasses   = MkSliderInt("最大パス数",      1,    10,   _maxPasses,        v => _maxPasses       = v);
            _sliderNormalAngle = MkSlider("法線角度 (°)",       0f,   180f, _normalAngleDeg,   v => _normalAngleDeg  = v);
            _sliderHardAngle   = MkSlider("ハードエッジ角度(°)", 0f,  180f, _hardAngleDeg,     v => _hardAngleDeg    = v);
            _sliderUvSeam      = MkSlider("UVシーム閾値",       0f,   0.1f, _uvSeamThreshold,  v => _uvSeamThreshold = v);
            root.Add(_sliderTargetRatio);
            root.Add(_sliderMaxPasses);
            root.Add(_sliderNormalAngle);
            root.Add(_sliderHardAngle);
            root.Add(_sliderUvSeam);

            _btnExecute = new Button(OnExecute) { text = "減数化実行" };
            _btnExecute.style.height    = 28;
            _btnExecute.style.marginTop = 6;
            root.Add(_btnExecute);

            // 結果セクション
            _resultSection = new VisualElement();
            _resultSection.style.display   = DisplayStyle.None;
            _resultSection.style.marginTop = 6;
            root.Add(_resultSection);
            BuildResultSection(_resultSection);
        }

        private void BuildResultSection(VisualElement root)
        {
            root.Add(SecLabel("結果"));

            _resultSummary = new Label();
            _resultSummary.style.fontSize   = 10;
            _resultSummary.style.color      = new StyleColor(new Color(0.5f, 0.9f, 0.5f));
            _resultSummary.style.whiteSpace = WhiteSpace.Normal;
            _resultSummary.style.marginBottom = 4;
            root.Add(_resultSummary);

            var logFoldout = new Foldout { text = "Pass Logs", value = false };
            _passLogsContainer = new VisualElement();
            logFoldout.Add(_passLogsContainer);
            root.Add(logFoldout);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var tc  = GetToolContext?.Invoke();
            var mc  = tc?.FirstSelectedMeshContext;
            var obj = mc?.MeshObject;

            if (tc == null)  { ShowWarning("ToolContext 未設定"); return; }
            _warningLabel.style.display = DisplayStyle.None;

            if (obj == null)
            {
                _noMeshLabel.style.display    = DisplayStyle.Flex;
                _meshInfoLabel.style.display  = DisplayStyle.None;
                _noQuadsLabel.style.display   = DisplayStyle.None;
                _btnExecute.SetEnabled(false);
                return;
            }

            int quads = 0, tris = 0;
            foreach (var f in obj.Faces) { if (f.IsQuad) quads++; else if (f.IsTriangle) tris++; }
            _noMeshLabel.style.display    = DisplayStyle.None;
            _meshInfoLabel.style.display  = DisplayStyle.Flex;
            _meshInfoLabel.text = $"総面数: {obj.Faces.Count}  Quad: {quads}  Tri: {tris}";

            bool hasQuads = quads > 0;
            _btnExecute.SetEnabled(hasQuads);
            _noQuadsLabel.style.display = hasQuads ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnExecute()
        {
            var tc = GetToolContext?.Invoke();
            var mc = tc?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null) { ShowWarning("メッシュが選択されていません"); return; }

            var prms = new DecimatorParams
            {
                TargetRatio     = _targetRatio,
                MaxPasses       = _maxPasses,
                NormalAngleDeg  = _normalAngleDeg,
                HardAngleDeg    = _hardAngleDeg,
                UvSeamThreshold = _uvSeamThreshold,
            };

            _lastResult = QuadDecimatorOperation.Execute(mc, prms, tc);
            RefreshResult();
            tc?.Repaint?.Invoke();
        }

        private void RefreshResult()
        {
            if (_resultSection == null) return;
            if (_lastResult == null) { _resultSection.style.display = DisplayStyle.None; return; }

            _resultSection.style.display = DisplayStyle.Flex;

            float reduction = _lastResult.OriginalFaceCount > 0
                ? (1f - (float)_lastResult.ResultFaceCount / _lastResult.OriginalFaceCount) * 100f : 0f;
            if (_resultSummary != null)
                _resultSummary.text =
                    $"元: {_lastResult.OriginalFaceCount} 面  →  結果: {_lastResult.ResultFaceCount} 面\n" +
                    $"削減率: {reduction:F1}%  パス数: {_lastResult.PassCount}";

            // パスログ
            if (_passLogsContainer != null)
            {
                _passLogsContainer.Clear();
                if (_lastResult.PassLogs != null)
                {
                    foreach (var log in _lastResult.PassLogs)
                    {
                        var lbl = new Label(log);
                        lbl.style.fontSize = 9;
                        lbl.style.color    = new StyleColor(Color.white);
                        lbl.style.whiteSpace = WhiteSpace.Normal;
                        _passLogsContainer.Add(lbl);
                    }
                }
            }
        }

        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
        }

        private static Slider MkSlider(string label, float min, float max, float init, Action<float> onChange)
        { var s = new Slider(label, min, max) { value = init }; s.style.marginBottom = 3; s.RegisterValueChangedCallback(e => onChange(e.newValue)); return s; }

        private static SliderInt MkSliderInt(string label, int min, int max, int init, Action<int> onChange)
        { var s = new SliderInt(label, min, max) { value = init }; s.style.marginBottom = 3; s.RegisterValueChangedCallback(e => onChange(e.newValue)); return s; }

        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
