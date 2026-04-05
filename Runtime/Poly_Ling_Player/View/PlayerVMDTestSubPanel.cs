// PlayerVMDTestSubPanel.cs
// VMDTestPanel の Player 版サブパネル（完全版）。
// IoExchangePanelBase / EditorWindow 除去、UIToolkit コード構築。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;
using Poly_Ling.VMD;

namespace Poly_Ling.Player
{
    public class PlayerVMDTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;

        // ── VMD 状態 ──────────────────────────────────────────────────────
        private VMDData    _vmd;
        private VMDApplier _applier;
        private float      _currentFrame;
        private string     _filePath;
        private bool       _applyCoordinateConversion = false;

        // ── UI 要素 ───────────────────────────────────────────────────────
        private Label         _modelLabel;
        private Label         _fileLabel;
        private Button        _btnClear, _btnReload;
        private VisualElement _vmdSection;
        private Label         _vmdInfoLabel;   // Model Name / Frames / Duration
        private Label         _vmdMatchLabel;  // Matched bones
        private Slider        _frameSlider;
        private Label         _frameLabel;
        private IntegerField  _frameInput;
        private FloatField    _scaleField;
        private Toggle        _coordToggle;
        private VisualElement _boneListContainer;
        private VisualElement _morphListContainer;
        private Foldout       _boneListFoldout;
        private Foldout       _morphListFoldout;
        private Label         _statusLabel;

        private ModelContext Model => GetModel?.Invoke();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("VMD モーションテスト"));

            // モデル情報
            _modelLabel = new Label();
            _modelLabel.style.fontSize     = 10;
            _modelLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _modelLabel.style.marginBottom = 3;
            root.Add(_modelLabel);

            // ── ファイル行 ─────────────────────────────────────────────────
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 3;
            _fileLabel = new Label(); _fileLabel.style.flexGrow = 1; _fileLabel.style.fontSize = 10;
            _fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            var btnOpen = new Button(OpenVMDFile) { text = "Open VMD..." }; btnOpen.style.width = 90;
            _btnClear  = new Button(ClearVMD)  { text = "クリア" };  _btnClear.style.width  = 52;
            _btnReload = new Button(ReloadVMD) { text = "再読込" }; _btnReload.style.width  = 52;
            fileRow.Add(_fileLabel); fileRow.Add(btnOpen); fileRow.Add(_btnClear); fileRow.Add(_btnReload);
            root.Add(fileRow);

            // ── VMD セクション（ロード後に表示）──────────────────────────
            _vmdSection = new VisualElement();
            _vmdSection.style.display = DisplayStyle.None;
            root.Add(_vmdSection);
            BuildVmdSection(_vmdSection);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            RefreshAll();
        }

        private void BuildVmdSection(VisualElement root)
        {
            // VMD 情報ラベル
            _vmdInfoLabel = new Label();
            _vmdInfoLabel.style.fontSize   = 10;
            _vmdInfoLabel.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _vmdInfoLabel.style.marginBottom = 3;
            _vmdInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_vmdInfoLabel);

            _vmdMatchLabel = new Label();
            _vmdMatchLabel.style.fontSize   = 10;
            _vmdMatchLabel.style.color      = new StyleColor(new Color(0.5f, 0.9f, 0.5f));
            _vmdMatchLabel.style.marginBottom = 4;
            root.Add(_vmdMatchLabel);

            // ── フレームスライダー ─────────────────────────────────────────
            _frameSlider = new Slider(0f, 1f) { value = 0f };
            _frameSlider.style.marginBottom = 2;
            _frameSlider.RegisterValueChangedCallback(e =>
            {
                _currentFrame = e.newValue;
                _frameInput?.SetValueWithoutNotify(Mathf.RoundToInt(_currentFrame));
                UpdateFrameLabel();
                ApplyFrame();
            });
            root.Add(_frameSlider);

            // フレームラベル + 直接入力
            var frameRow = new VisualElement();
            frameRow.style.flexDirection = FlexDirection.Row;
            frameRow.style.marginBottom  = 3;
            _frameLabel = new Label("Frame: 0 / 0");
            _frameLabel.style.flexGrow  = 1; _frameLabel.style.fontSize = 10;
            _frameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _frameInput = new IntegerField { value = 0 }; _frameInput.style.width = 60;
            _frameInput.RegisterValueChangedCallback(e =>
            {
                if (_vmd == null) return;
                _currentFrame = Mathf.Clamp(e.newValue, 0, _vmd.MaxFrameNumber);
                UpdateSlider();
                UpdateFrameLabel();
                ApplyFrame();
            });
            frameRow.Add(_frameLabel); frameRow.Add(_frameInput);
            root.Add(frameRow);

            // ── ナビゲーションボタン ───────────────────────────────────────
            var nav1 = new VisualElement(); nav1.style.flexDirection = FlexDirection.Row; nav1.style.marginBottom = 2;
            MkNavBtn(nav1, "|◀",  () => { _currentFrame = 0; Sync(); });
            MkNavBtn(nav1, "◀1", () => { if (_vmd != null) { _currentFrame = Mathf.Max(0, _currentFrame - 1); Sync(); } });
            MkNavBtn(nav1, "25%", () => { if (_vmd != null) { _currentFrame = _vmd.MaxFrameNumber * 0.25f; Sync(); } });
            MkNavBtn(nav1, "50%", () => { if (_vmd != null) { _currentFrame = _vmd.MaxFrameNumber * 0.5f;  Sync(); } });
            MkNavBtn(nav1, "75%", () => { if (_vmd != null) { _currentFrame = _vmd.MaxFrameNumber * 0.75f; Sync(); } });
            MkNavBtn(nav1, "1▶", () => { if (_vmd != null) { _currentFrame = Mathf.Min(_vmd.MaxFrameNumber, _currentFrame + 1); Sync(); } });
            MkNavBtn(nav1, "▶|", () => { if (_vmd != null) { _currentFrame = _vmd.MaxFrameNumber; Sync(); } });
            root.Add(nav1);

            var resetBtn = new Button(ResetPose) { text = "ポーズリセット" };
            resetBtn.style.marginBottom = 4;
            root.Add(resetBtn);

            // ── オプション ─────────────────────────────────────────────────
            root.Add(SecLabel("オプション"));

            _coordToggle = new Toggle("座標変換 (Z 反転)") { value = _applyCoordinateConversion };
            _coordToggle.style.marginBottom = 3;
            _coordToggle.RegisterValueChangedCallback(e =>
            {
                _applyCoordinateConversion = e.newValue;
                if (_applier != null) _applier.ApplyCoordinateConversion = e.newValue;
                if (_vmd != null) ApplyFrame();
            });
            root.Add(_coordToggle);

            var scaleRow = new VisualElement();
            scaleRow.style.flexDirection = FlexDirection.Row;
            scaleRow.style.marginBottom  = 6;
            var scaleLbl = new Label("PositionScale");
            scaleLbl.style.width = 90; scaleLbl.style.fontSize = 10;
            scaleLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _scaleField = new FloatField { value = _applier?.PositionScale ?? 1f };
            _scaleField.style.flexGrow = 1;
            _scaleField.RegisterValueChangedCallback(e =>
            {
                if (_applier != null) _applier.PositionScale = e.newValue;
                if (_vmd != null) ApplyFrame();
            });
            scaleRow.Add(scaleLbl); scaleRow.Add(_scaleField);
            root.Add(scaleRow);

            // ── ボーントラック Foldout ─────────────────────────────────────
            _boneListFoldout = new Foldout { text = "Bone Tracks (0)", value = false };
            _boneListContainer = new VisualElement();
            _boneListFoldout.Add(_boneListContainer);
            root.Add(_boneListFoldout);

            // ── モーフトラック Foldout ────────────────────────────────────
            _morphListFoldout = new Foldout { text = "Morph Tracks (0)", value = false };
            _morphListContainer = new VisualElement();
            _morphListFoldout.Add(_morphListContainer);
            root.Add(_morphListFoldout);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh() => RefreshAll();

        private void RefreshAll()
        {
            var model = Model;
            if (_modelLabel != null)
                _modelLabel.text = model != null
                    ? $"✓ {model.Name}  ({model.Bones.Count()} bones)"
                    : "(No model loaded)";

            if (_fileLabel != null)
                _fileLabel.text = string.IsNullOrEmpty(_filePath) ? "(None)" : Path.GetFileName(_filePath);
            if (_btnClear  != null) _btnClear.SetEnabled(_vmd != null);
            if (_btnReload != null) _btnReload.SetEnabled(!string.IsNullOrEmpty(_filePath));

            if (_vmdSection == null) return;
            bool hasVMD = _vmd != null;
            _vmdSection.style.display = hasVMD ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasVMD) return;

            // VMD 情報
            if (_vmdInfoLabel != null)
                _vmdInfoLabel.text =
                    $"Model: {_vmd.ModelName}\n" +
                    $"Frames: {_vmd.MaxFrameNumber}  ({_vmd.MaxFrameNumber / 30f:F1}s)\n" +
                    $"Bone tracks: {_vmd.BoneNames.Count()}  Morph tracks: {_vmd.MorphNames.Count()}";

            // マッチング情報
            if (_vmdMatchLabel != null && model != null && _applier != null)
            {
                var report = _applier.DiagnoseMatching(_vmd);
                _vmdMatchLabel.text = $"Matched: {report.MatchedBones.Count}/{_vmd.BoneNames.Count()} ({report.BoneMatchRate:P0})";
            }

            UpdateSlider();
            UpdateFrameLabel();
            RefreshBoneList();
            RefreshMorphList();
        }

        private void RefreshBoneList()
        {
            if (_boneListContainer == null || _vmd == null) return;
            _boneListContainer.Clear();
            var names = _vmd.BoneNames.Take(50).ToList();
            foreach (var name in names)
            {
                bool matched = Model != null && _applier != null && _applier.GetBoneIndex(name) >= 0;
                int  keys    = _vmd.BoneFramesByName[name].Count;
                var lbl = new Label($"{(matched ? "✓" : "✗")} {name} ({keys} keys)");
                lbl.style.fontSize = 10;
                lbl.style.color    = new StyleColor(matched ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.8f, 0.4f, 0.4f));
                _boneListContainer.Add(lbl);
            }
            int rem = _vmd.BoneNames.Count() - 50;
            if (rem > 0) { var l = new Label($"  ...他 {rem} トラック"); l.style.fontSize = 9; _boneListContainer.Add(l); }
            if (_boneListFoldout != null) _boneListFoldout.text = $"Bone Tracks ({_vmd.BoneNames.Count()})";
        }

        private void RefreshMorphList()
        {
            if (_morphListContainer == null || _vmd == null) return;
            _morphListContainer.Clear();
            var names = _vmd.MorphNames.Take(30).ToList();
            foreach (var name in names)
            {
                int keys = _vmd.MorphFramesByName[name].Count;
                var lbl = new Label($"{name} ({keys} keys)");
                lbl.style.fontSize = 10;
                _morphListContainer.Add(lbl);
            }
            int rem = _vmd.MorphNames.Count() - 30;
            if (rem > 0) { var l = new Label($"  ...他 {rem} トラック"); l.style.fontSize = 9; _morphListContainer.Add(l); }
            if (_morphListFoldout != null) _morphListFoldout.text = $"Morph Tracks ({_vmd.MorphNames.Count()})";
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
                _vmd          = VMDData.LoadFromFile(path);
                _filePath     = path;
                _currentFrame = 0;
                if (_applier == null) _applier = new VMDApplier();

                // EditorState から初期値を反映
                var tc = GetToolContext?.Invoke();
                var es = tc?.UndoController?.EditorState;
                if (es != null)
                {
                    _applier.PositionScale             = es.PmxUnityRatio;
                    _applyCoordinateConversion         = es.PmxFlipZ;
                    _applier.ApplyCoordinateConversion = es.PmxFlipZ;
                    _scaleField?.SetValueWithoutNotify(es.PmxUnityRatio);
                    _coordToggle?.SetValueWithoutNotify(es.PmxFlipZ);
                }

                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }

                SetStatus($"VMD 読込み完了: {Path.GetFileName(path)}");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"VMD 読込み失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerVMDTestSubPanel] {ex}");
            }
        }

        private void ClearVMD()
        {
            ResetPose();
            _vmd = null; _filePath = null; _currentFrame = 0;
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void ReloadVMD()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path  = _filePath;
            float  frame = _currentFrame;
            ClearVMD();
            try
            {
                _vmd = VMDData.LoadFromFile(path); _filePath = path; _currentFrame = frame;
                if (_applier == null) _applier = new VMDApplier();
                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }
                RefreshAll();
            }
            catch (Exception ex) { SetStatus($"再読込み失敗: {ex.Message}"); }
        }

        private void ApplyFrame()
        {
            if (_vmd == null || Model == null || _applier == null) return;
            _applier.ApplyFrame(Model, _vmd, _currentFrame);
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        private void ResetPose()
        {
            if (Model == null || _applier == null) return;
            _applier.ResetAllBones(Model);
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        private void Sync()
        {
            UpdateSlider();
            UpdateFrameLabel();
            ApplyFrame();
        }

        private void UpdateSlider()
        {
            if (_frameSlider == null || _vmd == null) return;
            _frameSlider.highValue = _vmd.MaxFrameNumber;
            _frameSlider.SetValueWithoutNotify(_currentFrame);
            _frameInput?.SetValueWithoutNotify(Mathf.RoundToInt(_currentFrame));
        }

        private void UpdateFrameLabel()
        {
            if (_frameLabel == null) return;
            int f   = Mathf.RoundToInt(_currentFrame);
            int max = _vmd != null ? (int)_vmd.MaxFrameNumber : 0;
            _frameLabel.text = $"Frame: {f} / {max}  ({_currentFrame / 30f:F2}s)";
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static void MkNavBtn(VisualElement row, string text, Action onClick) { var b = new Button(onClick) { text = text }; b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9; row.Add(b); }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
