// VMDTestPanel.cs
// VMDモーションテスト用パネル V2
// IoExchangePanelBase 継承、UXML+UIElements、IPanelContextReceiver でリコネクト対応

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Model;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.VMD
{
    public class VMDTestPanel : IoExchangePanelBase
    {
        // ================================================================
        // UXML/USS パス
        // ================================================================

        protected override string UxmlPackagePath =>
            "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/VMD/Adapters/VMDTestPanel.uxml";
        protected override string UxmlAssetsPath =>
            "Assets/Editor/Poly_Ling_Main/VMD/Adapters/VMDTestPanel.uxml";
        protected override string UssPackagePath =>
            "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/VMD/Adapters/VMDTestPanel.uss";
        protected override string UssAssetsPath =>
            "Assets/Editor/Poly_Ling_Main/VMD/Adapters/VMDTestPanel.uss";

        // ================================================================
        // 状態
        // ================================================================

        private VMDData _vmd;
        private string _filePath;
        private float _currentFrame;
        private VMDApplier _applier;
        private bool _applyCoordinateConversion;

        // ================================================================
        // UIElements 参照
        // ================================================================

        private Label _modelLabel;
        private Label _fileLabel;
        private HelpBox _noCtxBox;
        private Button _btnOpen, _btnClear, _btnReload;
        private VisualElement _vmdSection;
        private Label _vmdModelName, _vmdFrames, _vmdDuration, _vmdBoneTracks, _vmdMorphTracks, _vmdMatched;
        private Slider _frameSlider;
        private Label _frameLabel;
        private IntegerField _frameInput;
        private Toggle _coordToggle;
        private FloatField _scaleField;
        private Foldout _boneListFoldout, _morphListFoldout;
        private VisualElement _boneListContainer, _morphListContainer;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<VMDTestPanel>("VMD Test");
            w.minSize = new Vector2(320, 400);
            if (panelCtx != null) w.SetContext(panelCtx);
            if (toolCtx  != null) w.SetContext(toolCtx);
            w.Show();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_applier == null) _applier = new VMDApplier();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Model != null && _applier != null)
                _applier.ResetAllBones(Model);
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        protected override void OnToolContextSet()
        {
            if (_applier == null) _applier = new VMDApplier();

            if (Model != null)
            {
                _applier.BuildMapping(Model);

                var es = _toolCtx?.UndoController?.EditorState;
                if (es != null)
                {
                    _applier.PositionScale = es.PmxUnityRatio;
                    _applyCoordinateConversion = es.PmxFlipZ;
                    _applier.ApplyCoordinateConversion = _applyCoordinateConversion;
                }

                if (_vmd != null) ApplyFrame();
            }
        }

        // ================================================================
        // OnCreateGUI
        // ================================================================

        protected override void OnCreateGUI(VisualElement root)
        {
            _noCtxBox         = root.Q<HelpBox>("no-ctx-box");
            _modelLabel       = root.Q<Label>("model-label");
            _fileLabel        = root.Q<Label>("file-label");
            _btnOpen          = root.Q<Button>("btn-open");
            _btnClear         = root.Q<Button>("btn-clear");
            _btnReload        = root.Q<Button>("btn-reload");
            _vmdSection       = root.Q<VisualElement>("vmd-section");
            _vmdModelName     = root.Q<Label>("vmd-model-name");
            _vmdFrames        = root.Q<Label>("vmd-frames");
            _vmdDuration      = root.Q<Label>("vmd-duration");
            _vmdBoneTracks    = root.Q<Label>("vmd-bone-tracks");
            _vmdMorphTracks   = root.Q<Label>("vmd-morph-tracks");
            _vmdMatched       = root.Q<Label>("vmd-matched");
            _frameSlider      = root.Q<Slider>("frame-slider");
            _frameLabel       = root.Q<Label>("frame-label");
            _frameInput       = root.Q<IntegerField>("frame-input");
            _coordToggle      = root.Q<Toggle>("coord-toggle");
            _scaleField       = root.Q<FloatField>("scale-field");
            _boneListFoldout  = root.Q<Foldout>("bone-list-foldout");
            _morphListFoldout = root.Q<Foldout>("morph-list-foldout");
            _boneListContainer  = root.Q<VisualElement>("bone-list");
            _morphListContainer = root.Q<VisualElement>("morph-list");

            // ファイルボタン
            _btnOpen?.RegisterCallback<ClickEvent>(_ => OpenVMDFile());
            _btnClear?.RegisterCallback<ClickEvent>(_ => ClearVMD());
            _btnReload?.RegisterCallback<ClickEvent>(_ => ReloadVMD());

            // フレームスライダー
            _frameSlider?.RegisterValueChangedCallback(evt =>
            {
                _currentFrame = evt.newValue;
                UpdateFrameLabel();
                ApplyFrame();
            });

            // フレーム直接入力
            _frameInput?.RegisterValueChangedCallback(evt =>
            {
                if (_vmd == null) return;
                _currentFrame = Mathf.Clamp(evt.newValue, 0, _vmd.MaxFrameNumber);
                UpdateSliderWithoutNotify();
                UpdateFrameLabel();
                ApplyFrame();
            });

            // クイックジャンプボタン
            RegisterJumpBtn(root, "btn-0",   () => 0);
            RegisterJumpBtn(root, "btn-25",  () => _vmd != null ? _vmd.MaxFrameNumber * 0.25f : 0);
            RegisterJumpBtn(root, "btn-50",  () => _vmd != null ? _vmd.MaxFrameNumber * 0.5f  : 0);
            RegisterJumpBtn(root, "btn-75",  () => _vmd != null ? _vmd.MaxFrameNumber * 0.75f : 0);
            RegisterJumpBtn(root, "btn-100", () => _vmd != null ? _vmd.MaxFrameNumber : 0);

            // ステップボタン
            root.Q<Button>("btn-prev1")?.RegisterCallback<ClickEvent>(_ => { if (_vmd != null) { _currentFrame = Mathf.Max(0, _currentFrame - 1); Sync(); } });
            root.Q<Button>("btn-next1")?.RegisterCallback<ClickEvent>(_ => { if (_vmd != null) { _currentFrame = Mathf.Min(_vmd.MaxFrameNumber, _currentFrame + 1); Sync(); } });
            root.Q<Button>("btn-first")?.RegisterCallback<ClickEvent>(_ => { _currentFrame = 0; Sync(); });
            root.Q<Button>("btn-last" )?.RegisterCallback<ClickEvent>(_ => { if (_vmd != null) { _currentFrame = _vmd.MaxFrameNumber; Sync(); } });
            root.Q<Button>("btn-reset")?.RegisterCallback<ClickEvent>(_ => ResetPose());

            // オプション
            _coordToggle?.RegisterValueChangedCallback(evt =>
            {
                _applyCoordinateConversion = evt.newValue;
                if (_applier != null) _applier.ApplyCoordinateConversion = evt.newValue;
                if (_vmd != null) ApplyFrame();
            });
            _scaleField?.RegisterValueChangedCallback(evt =>
            {
                if (_applier != null) _applier.PositionScale = evt.newValue;
                if (_vmd != null) ApplyFrame();
            });
        }

        private void RegisterJumpBtn(VisualElement root, string name, Func<float> frameGetter)
        {
            root.Q<Button>(name)?.RegisterCallback<ClickEvent>(_ =>
            {
                _currentFrame = frameGetter();
                Sync();
            });
        }

        private void Sync()
        {
            UpdateSliderWithoutNotify();
            UpdateFrameLabel();
            ApplyFrame();
        }

        private void UpdateSliderWithoutNotify()
        {
            if (_frameSlider == null || _vmd == null) return;
            _frameSlider.highValue = _vmd.MaxFrameNumber;
            _frameSlider.SetValueWithoutNotify(_currentFrame);
        }

        private void UpdateFrameLabel()
        {
            if (_frameLabel == null) return;
            int f = Mathf.RoundToInt(_currentFrame);
            int max = _vmd != null ? (int)_vmd.MaxFrameNumber : 0;
            _frameLabel.text = $"Frame: {f} / {max}  ({_currentFrame / 30f:F2}s)";
        }

        // ================================================================
        // RefreshAll
        // ================================================================

        protected override void RefreshAll()
        {
            RefreshNoCtxBox();
            RefreshModelLabel();
            RefreshFileLabel();
            RefreshVMDSection();
            RefreshOptions();
        }

        private void RefreshNoCtxBox()
        {
            if (_noCtxBox == null) return;
            bool noCtx = _toolCtx == null && _panelCtx == null;
            _noCtxBox.style.display = noCtx ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshModelLabel()
        {
            if (_modelLabel == null) return;
            _modelLabel.text = Model != null
                ? $"✓ {Model.Name}  ({Model.BoneCount} bones)"
                : "(No model loaded)";
        }

        private void RefreshFileLabel()
        {
            if (_fileLabel == null) return;
            _fileLabel.text = string.IsNullOrEmpty(_filePath) ? "(None)" : Path.GetFileName(_filePath);
            if (_btnClear  != null) _btnClear.SetEnabled(_vmd != null);
            if (_btnReload != null) _btnReload.SetEnabled(!string.IsNullOrEmpty(_filePath));
        }

        private void RefreshVMDSection()
        {
            if (_vmdSection == null) return;
            bool hasVMD = _vmd != null;
            _vmdSection.style.display = hasVMD ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasVMD) return;

            if (_vmdModelName  != null) _vmdModelName.text  = $"Model Name: {_vmd.ModelName}";
            if (_vmdFrames     != null) _vmdFrames.text     = $"Total Frames: {_vmd.MaxFrameNumber}";
            if (_vmdDuration   != null) _vmdDuration.text   = $"Duration: {_vmd.MaxFrameNumber / 30f:F1} sec";
            if (_vmdBoneTracks != null) _vmdBoneTracks.text = $"Bone Tracks: {_vmd.BoneNames.Count()}";
            if (_vmdMorphTracks != null) _vmdMorphTracks.text = $"Morph Tracks: {_vmd.MorphNames.Count()}";

            if (_vmdMatched != null && Model != null && _applier != null)
            {
                var report = _applier.DiagnoseMatching(_vmd);
                _vmdMatched.text = $"Matched Bones: {report.MatchedBones.Count}/{_vmd.BoneNames.Count()} ({report.BoneMatchRate:P0})";
            }

            if (_frameSlider != null)
            {
                _frameSlider.highValue = _vmd.MaxFrameNumber;
                _frameSlider.SetValueWithoutNotify(_currentFrame);
            }
            UpdateFrameLabel();

            RefreshBoneList();
            RefreshMorphList();
        }

        private void RefreshBoneList()
        {
            if (_boneListContainer == null || _vmd == null) return;
            _boneListContainer.Clear();
            foreach (var name in _vmd.BoneNames.Take(50))
            {
                bool matched = Model != null && _applier != null && _applier.GetBoneIndex(name) >= 0;
                var frames = _vmd.BoneFramesByName[name];
                _boneListContainer.Add(new Label($"{(matched ? "✓" : "✗")} {name} ({frames.Count} keys)")
                    { style = { fontSize = 10 } });
            }
            int remaining = _vmd.BoneNames.Count() - 50;
            if (remaining > 0)
                _boneListContainer.Add(new Label($"... and {remaining} more") { style = { fontSize = 10 } });

            if (_boneListFoldout != null)
                _boneListFoldout.text = $"Bone Tracks ({_vmd.BoneNames.Count()})";
        }

        private void RefreshMorphList()
        {
            if (_morphListContainer == null || _vmd == null) return;
            _morphListContainer.Clear();
            foreach (var name in _vmd.MorphNames.Take(30))
            {
                var frames = _vmd.MorphFramesByName[name];
                _morphListContainer.Add(new Label($"{name} ({frames.Count} keys)")
                    { style = { fontSize = 10 } });
            }
            int remaining = _vmd.MorphNames.Count() - 30;
            if (remaining > 0)
                _morphListContainer.Add(new Label($"... and {remaining} more") { style = { fontSize = 10 } });

            if (_morphListFoldout != null)
                _morphListFoldout.text = $"Morph Tracks ({_vmd.MorphNames.Count()})";
        }

        private void RefreshOptions()
        {
            if (_coordToggle != null) _coordToggle.SetValueWithoutNotify(_applyCoordinateConversion);
            if (_scaleField  != null && _applier != null) _scaleField.SetValueWithoutNotify(_applier.PositionScale);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OpenVMDFile()
        {
            string path = PLEditorBridge.I.OpenFilePanel("Open VMD", "", "vmd");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                _vmd = VMDData.LoadFromFile(path);
                _filePath = path;
                _currentFrame = 0;

                if (Model != null && _applier != null)
                {
                    _applier.BuildMapping(Model);
                    ApplyFrame();
                }

                Debug.Log($"[VMDTest] Loaded: {Path.GetFileName(path)}, {_vmd.MaxFrameNumber} frames");
                RefreshAll();
            }
            catch (Exception ex)
            {
                PLEditorBridge.I.DisplayDialog("Error", $"VMD読み込み失敗:\n{ex.Message}", "OK");
                Debug.LogError($"[VMDTest] Load failed: {ex}");
            }
        }

        private void ClearVMD()
        {
            ResetPose();
            _vmd = null;
            _filePath = null;
            _currentFrame = 0;
            RefreshAll();
        }

        private void ReloadVMD()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath;
            float frame = _currentFrame;
            ClearVMD();
            try
            {
                _vmd = VMDData.LoadFromFile(path);
                _filePath = path;
                _currentFrame = frame;
                if (Model != null && _applier != null)
                {
                    _applier.BuildMapping(Model);
                    ApplyFrame();
                }
                RefreshAll();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VMDTest] Reload failed: {ex}");
            }
        }

        private void ApplyFrame()
        {
            if (_vmd == null || Model == null || _applier == null) return;
            _applier.ApplyFrame(Model, _vmd, _currentFrame);
            _toolCtx?.Repaint?.Invoke();
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        private void ResetPose()
        {
            if (Model == null || _applier == null) return;
            _applier.ResetAllBones(Model);
            _toolCtx?.Repaint?.Invoke();
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }
    }
}
