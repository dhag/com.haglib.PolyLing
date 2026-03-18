// QuadDecimatorPanel.cs
// Quad保持減数化パネル (UIToolkit)

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Panels.QuadDecimator;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    /// <summary>
    /// Quad保持減数化パネル（UIToolkit版）
    /// </summary>
    public class QuadDecimatorPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/QuadDecimatorPanel/QuadDecimatorPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/QuadDecimatorPanel/QuadDecimatorPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/QuadDecimatorPanel/QuadDecimatorPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/QuadDecimatorPanel/QuadDecimatorPanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["Header"] = new() { ["en"] = "Quad-Preserving Decimator", ["ja"] = "Quad保持 減数化" },
            ["Description"] = new() {
                ["en"] = "Reduces polygon count while preserving quad grid topology.",
                ["ja"] = "Quadグリッドのトポロジを保持しながらポリゴン数を削減します。"
            },
            ["TargetRatio"] = new() { ["en"] = "Target Ratio", ["ja"] = "目標比率" },
            ["MaxPasses"] = new() { ["en"] = "Max Passes", ["ja"] = "最大パス数" },
            ["NormalAngle"] = new() { ["en"] = "Normal Angle (°)", ["ja"] = "法線角度 (°)" },
            ["HardAngle"] = new() { ["en"] = "Hard Edge Angle (°)", ["ja"] = "ハードエッジ角度 (°)" },
            ["UvSeamThreshold"] = new() { ["en"] = "UV Seam Threshold", ["ja"] = "UVシーム閾値" },
            ["Execute"] = new() { ["en"] = "Decimate", ["ja"] = "減数化実行" },
            ["ResultLabel"] = new() { ["en"] = "Result", ["ja"] = "結果" },
            ["OriginalFaces"] = new() { ["en"] = "Original faces: {0}", ["ja"] = "元の面数: {0}" },
            ["ResultFaces"] = new() { ["en"] = "Result faces: {0}", ["ja"] = "結果面数: {0}" },
            ["Reduction"] = new() { ["en"] = "Reduction: {0:F1}%", ["ja"] = "削減率: {0:F1}%" },
            ["Passes"] = new() { ["en"] = "Passes: {0}", ["ja"] = "パス数: {0}" },
            ["NoMesh"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが選択されていません" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoQuads"] = new() { ["en"] = "Selected mesh has no quad faces.", ["ja"] = "選択メッシュにQuad面がありません。" },
            ["MeshInfo"] = new() { ["en"] = "Mesh Info", ["ja"] = "メッシュ情報" },
            ["QuadCount"] = new() { ["en"] = "Quads: {0}", ["ja"] = "Quad数: {0}" },
            ["TriCount"] = new() { ["en"] = "Triangles: {0}", ["ja"] = "三角形数: {0}" },
            ["TotalFaces"] = new() { ["en"] = "Total faces: {0}", ["ja"] = "総面数: {0}" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // Settings
        // ================================================================

        private const string SettingsName = "QuadDecimator";

        [Serializable]
        private class DecimatorSettings : ToolSettingsBase
        {
            public float TargetRatio = 0.5f;
            public int MaxPasses = 5;
            public float NormalAngleDeg = 15f;
            public float HardAngleDeg = 25f;
            public float UvSeamThreshold = 0.01f;

            public override IToolSettings Clone()
            {
                return new DecimatorSettings
                {
                    TargetRatio = TargetRatio,
                    MaxPasses = MaxPasses,
                    NormalAngleDeg = NormalAngleDeg,
                    HardAngleDeg = HardAngleDeg,
                    UvSeamThreshold = UvSeamThreshold,
                };
            }

            public override bool IsDifferentFrom(IToolSettings other)
            {
                if (other is not DecimatorSettings o) return true;
                return TargetRatio != o.TargetRatio || MaxPasses != o.MaxPasses ||
                       NormalAngleDeg != o.NormalAngleDeg || HardAngleDeg != o.HardAngleDeg ||
                       UvSeamThreshold != o.UvSeamThreshold;
            }

            public override void CopyFrom(IToolSettings other)
            {
                if (other is not DecimatorSettings o) return;
                TargetRatio = o.TargetRatio;
                MaxPasses = o.MaxPasses;
                NormalAngleDeg = o.NormalAngleDeg;
                HardAngleDeg = o.HardAngleDeg;
                UvSeamThreshold = o.UvSeamThreshold;
            }
        }

        private readonly DecimatorSettings _settings = new();

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;
        private MeshContext FirstSelectedMeshContext => _toolContext?.FirstSelectedMeshContext;
        private MeshObject FirstSelectedMeshObject => FirstSelectedMeshContext?.MeshObject;

        // ================================================================
        // 状態
        // ================================================================

        private QuadDecimator.DecimatorResult _lastResult;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private ScrollView _mainContent;
        private Label _headerLabel, _descriptionLabel;
        private Label _meshInfoHeader, _noMeshLabel;
        private VisualElement _meshInfoDetail;
        private Label _totalFacesLabel, _quadCountLabel, _triCountLabel;
        private Slider _sliderTargetRatio, _sliderNormalAngle, _sliderHardAngle, _sliderUvSeam;
        private SliderInt _sliderMaxPasses;
        private Button _btnExecute;
        private Label _noQuadsLabel;
        private VisualElement _resultSection;
        private Label _resultHeader, _originalFacesLabel, _resultFacesLabel, _reductionLabel, _passesLabel;
        private VisualElement _passLogsContainer;

        // ================================================================
        // Open
        // ================================================================

        public static QuadDecimatorPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<QuadDecimatorPanel>();
            panel.titleContent = new GUIContent(L.Get("Window_QuadDecimator"));
            panel.minSize = new Vector2(320, 400);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            UnsubscribeUndo();
        }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeUndo();
            _toolContext = ctx;
            SubscribeUndo();
            RestoreSettings();
            _lastResult = null;
            Refresh();
        }

        // ================================================================
        // Undo購読
        // ================================================================

        private void SubscribeUndo()
        {
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        private void UnsubscribeUndo()
        {
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed()
        {
            RestoreSettings();
            SyncSlidersFromSettings();
            Refresh();
        }

        // ================================================================
        // Settings Undo
        // ================================================================

        private void RecordSettingsChange(string operationName)
        {
            if (_toolContext?.UndoController == null) return;

            var editorState = _toolContext.UndoController.EditorState;
            if (editorState.ToolSettings == null)
                editorState.ToolSettings = new ToolSettingsStorage();

            IToolSettings before = _settings.Clone();
            editorState.ToolSettings.Set(SettingsName, before);
            _toolContext.UndoController.BeginEditorStateDrag();

            editorState.ToolSettings.Set(SettingsName, _settings);
            _toolContext.UndoController.EndEditorStateDrag(operationName);
        }

        private void RestoreSettings()
        {
            if (_toolContext?.UndoController == null) return;

            var stored = _toolContext.UndoController.EditorState.ToolSettings?.Get<IToolSettings>(SettingsName);
            if (stored != null)
                _settings.CopyFrom(stored);
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
            SyncSlidersFromSettings();
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<ScrollView>("main-content");
            _headerLabel = root.Q<Label>("header-label");
            _descriptionLabel = root.Q<Label>("description-label");
            _meshInfoHeader = root.Q<Label>("mesh-info-header");
            _noMeshLabel = root.Q<Label>("no-mesh-label");
            _meshInfoDetail = root.Q<VisualElement>("mesh-info-detail");
            _totalFacesLabel = root.Q<Label>("total-faces-label");
            _quadCountLabel = root.Q<Label>("quad-count-label");
            _triCountLabel = root.Q<Label>("tri-count-label");
            _sliderTargetRatio = root.Q<Slider>("slider-target-ratio");
            _sliderMaxPasses = root.Q<SliderInt>("slider-max-passes");
            _sliderNormalAngle = root.Q<Slider>("slider-normal-angle");
            _sliderHardAngle = root.Q<Slider>("slider-hard-angle");
            _sliderUvSeam = root.Q<Slider>("slider-uv-seam");
            _btnExecute = root.Q<Button>("btn-execute");
            _noQuadsLabel = root.Q<Label>("no-quads-label");
            _resultSection = root.Q<VisualElement>("result-section");
            _resultHeader = root.Q<Label>("result-header");
            _originalFacesLabel = root.Q<Label>("original-faces-label");
            _resultFacesLabel = root.Q<Label>("result-faces-label");
            _reductionLabel = root.Q<Label>("reduction-label");
            _passesLabel = root.Q<Label>("passes-label");
            _passLogsContainer = root.Q<VisualElement>("pass-logs-container");

            // スライダーイベント
            _sliderTargetRatio.RegisterValueChangedCallback(e => { _settings.TargetRatio = e.newValue; RecordSettingsChange("QuadDecimator Settings"); });
            _sliderMaxPasses.RegisterValueChangedCallback(e => { _settings.MaxPasses = e.newValue; RecordSettingsChange("QuadDecimator Settings"); });
            _sliderNormalAngle.RegisterValueChangedCallback(e => { _settings.NormalAngleDeg = e.newValue; RecordSettingsChange("QuadDecimator Settings"); });
            _sliderHardAngle.RegisterValueChangedCallback(e => { _settings.HardAngleDeg = e.newValue; RecordSettingsChange("QuadDecimator Settings"); });
            _sliderUvSeam.RegisterValueChangedCallback(e => { _settings.UvSeamThreshold = e.newValue; RecordSettingsChange("QuadDecimator Settings"); });

            // 実行ボタン
            _btnExecute.clicked += OnExecute;
        }

        // ================================================================
        // スライダー同期
        // ================================================================

        private void SyncSlidersFromSettings()
        {
            if (_sliderTargetRatio == null) return;

            _sliderTargetRatio.SetValueWithoutNotify(_settings.TargetRatio);
            _sliderMaxPasses.SetValueWithoutNotify(_settings.MaxPasses);
            _sliderNormalAngle.SetValueWithoutNotify(_settings.NormalAngleDeg);
            _sliderHardAngle.SetValueWithoutNotify(_settings.HardAngleDeg);
            _sliderUvSeam.SetValueWithoutNotify(_settings.UvSeamThreshold);
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            if (Model == null)
            {
                ShowWarning(T("ModelNotAvailable"));
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            // ローカライズ
            _headerLabel.text = T("Header");
            _descriptionLabel.text = T("Description");
            _meshInfoHeader.text = T("MeshInfo");
            _sliderTargetRatio.label = T("TargetRatio");
            _sliderMaxPasses.label = T("MaxPasses");
            _sliderNormalAngle.label = T("NormalAngle");
            _sliderHardAngle.label = T("HardAngle");
            _sliderUvSeam.label = T("UvSeamThreshold");
            _btnExecute.text = T("Execute");

            // メッシュ情報
            RefreshMeshInfo();

            // 結果
            RefreshResult();
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // メッシュ情報
        // ================================================================

        private void RefreshMeshInfo()
        {
            var meshObj = FirstSelectedMeshObject;
            if (meshObj == null)
            {
                _noMeshLabel.text = T("NoMesh");
                _noMeshLabel.style.display = DisplayStyle.Flex;
                _meshInfoDetail.style.display = DisplayStyle.None;
                _btnExecute.SetEnabled(false);
                _noQuadsLabel.style.display = DisplayStyle.None;
                return;
            }

            _noMeshLabel.style.display = DisplayStyle.None;
            _meshInfoDetail.style.display = DisplayStyle.Flex;

            int quads = 0, tris = 0;
            foreach (var f in meshObj.Faces)
            {
                if (f.IsQuad) quads++;
                else if (f.IsTriangle) tris++;
            }

            _totalFacesLabel.text = T("TotalFaces", meshObj.Faces.Count);
            _quadCountLabel.text = T("QuadCount", quads);
            _triCountLabel.text = T("TriCount", tris);

            bool hasQuads = quads > 0;
            _btnExecute.SetEnabled(hasQuads);

            if (!hasQuads)
            {
                _noQuadsLabel.text = T("NoQuads");
                _noQuadsLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _noQuadsLabel.style.display = DisplayStyle.None;
            }
        }

        // ================================================================
        // 実行
        // ================================================================

        private void OnExecute()
        {
            var sourceMeshObj = FirstSelectedMeshObject;
            if (sourceMeshObj == null) return;

            var sourceMeshContext = FirstSelectedMeshContext;

            var prms = new Poly_Ling.UI.QuadDecimator.DecimatorParams
            {
                TargetRatio = _settings.TargetRatio,
                MaxPasses = _settings.MaxPasses,
                NormalAngleDeg = _settings.NormalAngleDeg,
                HardAngleDeg = _settings.HardAngleDeg,
                UvSeamThreshold = _settings.UvSeamThreshold,
            };

            _lastResult = QuadPreservingDecimator.Decimate(sourceMeshObj, prms, out MeshObject resultMesh);
            resultMesh.Name = sourceMeshObj.Name + "_decimated";

            // 新しいMeshContextとして追加
            var newMeshContext = new MeshContext
            {
                Name = resultMesh.Name,
                MeshObject = resultMesh,
                Materials = new List<Material>(sourceMeshContext.Materials ?? new List<Material>()),
            };

            newMeshContext.UnityMesh = resultMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name = resultMesh.Name;
            newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            _toolContext.AddMeshContext?.Invoke(newMeshContext);
            _toolContext.Repaint?.Invoke();

            RefreshResult();
        }

        // ================================================================
        // 結果表示
        // ================================================================

        private void RefreshResult()
        {
            if (_lastResult == null)
            {
                _resultSection.style.display = DisplayStyle.None;
                return;
            }

            _resultSection.style.display = DisplayStyle.Flex;
            _resultHeader.text = T("ResultLabel");

            _originalFacesLabel.text = T("OriginalFaces", _lastResult.OriginalFaceCount);
            _resultFacesLabel.text = T("ResultFaces", _lastResult.ResultFaceCount);

            float reduction = (_lastResult.OriginalFaceCount > 0)
                ? (1f - (float)_lastResult.ResultFaceCount / _lastResult.OriginalFaceCount) * 100f
                : 0;
            _reductionLabel.text = T("Reduction", reduction);
            _passesLabel.text = T("Passes", _lastResult.PassCount);

            // パスログ
            _passLogsContainer.Clear();
            if (_lastResult.PassLogs != null)
            {
                foreach (var log in _lastResult.PassLogs)
                {
                    var logLabel = new Label(log);
                    logLabel.AddToClassList("qd-pass-log-entry");
                    _passLogsContainer.Add(logLabel);
                }
            }
        }
    }
}
