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
using Poly_Ling.Core;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.VMD;

namespace Poly_Ling.Player
{
    public class PlayerVMDTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>フレーム適用後に呼ぶ。GPU メッシュ再スキン（UpdateTransform）を core 側で起こすため。</summary>
        public Action OnFrameApplied;

        // ── VMD 状態 ──────────────────────────────────────────────────────
        private VMDData    _vmd;
        private VMDApplier _applier;
        private float      _currentFrame;
        private string     _filePath;
        private bool       _applyCoordinateConversion = false;

        // ── デバッグ設定（VMD 復活手順書 段階 1）──────────────────────────
        // _applier は LoadVMD で生成されるため、値はここに保持して流し込む。
        private bool   _enableIK    = false;   // 既定 OFF
        private bool   _traceEnabled = false;  // 既定 OFF
        private string _traceBoneList = "センター,下半身,左足,左ひざ,左足首,右足,右ひざ,右足首";
        private bool   _ignoreAngleLimits = false;  // 既定 OFF（段階 3）
        private bool   _kneePreBend       = false;  // 既定 OFF
        private string _ikTraceBoneList = "";       // 空なら全 IK ボーン

        // ── UI 要素 ───────────────────────────────────────────────────────
        private Label         _modelLabel;
        private Label         _fileLabel;
        private TextField     _vmdPathField;
        private const string  VmdPathKey = "VMD.Path";
        private Button        _btnClear, _btnReload;
        private VisualElement _vmdSection;
        private Label         _vmdInfoLabel;   // Model Name / Frames / Duration
        private Label         _vmdMatchLabel;  // Matched bones
        private Slider        _frameSlider;
        private Label         _frameLabel;
        private IntegerField  _frameInput;
        private FloatField    _scaleField;
        private Toggle        _coordToggle;
        private Toggle        _ikToggle;
        private Toggle        _traceToggle;
        private Toggle        _ignoreLimitToggle;
        private Toggle        _kneePreBendToggle;
        private TextField     _traceBonesField;
        private TextField     _ikTraceBonesField;
        private Button        _btnTraceAll;
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
            _modelLabel.style.marginBottom = 3;
            root.Add(_modelLabel);

            // ── ファイル行 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("VMD ファイル"));
            _vmdPathField = new TextField();
            _vmdPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VmdPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_vmdPathField, OnBrowseVmd));
            _vmdPathField.SetValueWithoutNotify(RecentPaths.Get(VmdPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseVmd);
            btnOpen.style.flexGrow = 1; btnOpen.style.marginRight = 2;
            _btnClear  = new Button(ClearVMD)  { text = "クリア" };  _btnClear.style.width  = 52; _btnClear.style.marginRight = 2;
            _btnReload = new Button(ReloadVMD) { text = "再読込" }; _btnReload.style.width  = 52;
            opRow.Add(btnOpen); opRow.Add(_btnClear); opRow.Add(_btnReload);
            root.Add(opRow);

            _fileLabel = new Label(); _fileLabel.style.flexGrow = 1; _fileLabel.style.fontSize = 10;
            _fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _fileLabel.style.marginBottom = 3;
            root.Add(_fileLabel);

            // ── VMD セクション（ロード後に表示）──────────────────────────
            _vmdSection = new VisualElement();
            _vmdSection.style.display = DisplayStyle.None;
            root.Add(_vmdSection);
            BuildVmdSection(_vmdSection);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            RefreshAll();
        }

        private void BuildVmdSection(VisualElement root)
        {
            // VMD 情報ラベル
            _vmdInfoLabel = new Label();
            _vmdInfoLabel.style.fontSize   = 10;
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

            // ── IK / トレース ─────────────────────────────────────────────
            _ikToggle = new Toggle("IK を有効にする") { value = _enableIK };
            _ikToggle.style.marginBottom = 3;
            _ikToggle.RegisterValueChangedCallback(e =>
            {
                _enableIK = e.newValue;
                if (_applier != null) _applier.EnableIK = e.newValue;
                if (_vmd != null) ApplyFrame();
            });
            root.Add(_ikToggle);

            _ignoreLimitToggle = new Toggle("角度制限を無視") { value = _ignoreAngleLimits };
            _ignoreLimitToggle.style.marginBottom = 3;
            _ignoreLimitToggle.style.marginLeft   = 12;
            _ignoreLimitToggle.RegisterValueChangedCallback(e =>
            {
                _ignoreAngleLimits = e.newValue;
                if (_applier != null) _applier.IgnoreAngleLimits = e.newValue;
                if (_vmd != null) ApplyFrame();
            });
            root.Add(_ignoreLimitToggle);

            _kneePreBendToggle = new Toggle("ひざ初期屈曲 (KneePreBend)") { value = _kneePreBend };
            _kneePreBendToggle.style.marginBottom = 3;
            _kneePreBendToggle.style.marginLeft   = 12;
            _kneePreBendToggle.tooltip = "角度制限を無視している間は効きません";
            _kneePreBendToggle.RegisterValueChangedCallback(e =>
            {
                _kneePreBend = e.newValue;
                if (_applier != null) _applier.KneePreBend = e.newValue;
                if (_vmd != null) ApplyFrame();
            });
            root.Add(_kneePreBendToggle);

            _traceToggle = new Toggle("トレース出力") { value = _traceEnabled };
            _traceToggle.style.marginBottom = 3;
            _traceToggle.RegisterValueChangedCallback(e =>
            {
                _traceEnabled = e.newValue;
                if (_applier == null) return;
                if (e.newValue)
                {
                    _applier.TraceDirectory = TraceDir();
                    PushTraceBones();
                    PushIkTraceBones();
                    _applier.TraceEnabled = true;
                    SetStatus(string.IsNullOrEmpty(_applier.TraceDirectory)
                        ? "トレース出力先が特定できません（VMD 未読込み）"
                        : $"トレース出力: {Path.Combine(_applier.TraceDirectory, VMDApplier.TraceFileName)}");
                }
                else
                {
                    _applier.TraceEnabled = false;
                    _applier.CloseTrace();
                }
            });
            root.Add(_traceToggle);

            var traceBoneRow = new VisualElement();
            traceBoneRow.style.flexDirection = FlexDirection.Row;
            traceBoneRow.style.marginBottom  = 3;
            var traceBoneLbl = new Label("トレース対象");
            traceBoneLbl.style.width = 90; traceBoneLbl.style.fontSize = 10;
            traceBoneLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _traceBonesField = new TextField { value = _traceBoneList };
            _traceBonesField.style.flexGrow = 1;
            _traceBonesField.RegisterValueChangedCallback(e =>
            {
                _traceBoneList = e.newValue;
                PushTraceBones();
            });
            traceBoneRow.Add(traceBoneLbl); traceBoneRow.Add(_traceBonesField);
            root.Add(traceBoneRow);

            var ikTraceRow = new VisualElement();
            ikTraceRow.style.flexDirection = FlexDirection.Row;
            ikTraceRow.style.marginBottom  = 3;
            var ikTraceLbl = new Label("IK トレース対象");
            ikTraceLbl.style.width = 90; ikTraceLbl.style.fontSize = 10;
            ikTraceLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _ikTraceBonesField = new TextField { value = _ikTraceBoneList };
            _ikTraceBonesField.style.flexGrow = 1;
            _ikTraceBonesField.tooltip = "空欄なら全 IK ボーン";
            _ikTraceBonesField.RegisterValueChangedCallback(e =>
            {
                _ikTraceBoneList = e.newValue;
                PushIkTraceBones();
            });
            ikTraceRow.Add(ikTraceLbl); ikTraceRow.Add(_ikTraceBonesField);
            root.Add(ikTraceRow);

            _btnTraceAll = new Button(RunTraceAllFrames) { text = "全フレーム一括トレース" };
            _btnTraceAll.style.marginBottom = 4;
            root.Add(_btnTraceAll);

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
            PlayerLayoutRoot.ApplyDarkTheme(_morphListContainer);
        }

        // ================================================================
        // 操作
        // ================================================================

        // 「開く」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowseVmd()
        {
            string path = PlayerIoUiKit.AskLoadPath("Open VMD", VmdPathKey, _vmdPathField.value, "vmd");
            if (string.IsNullOrEmpty(path)) return;
            _vmdPathField.value = path;
            LoadVMD(path);
        }

        private void LoadVMD(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }
            try
            {
                _vmd          = VMDData.LoadFromFile(path);
                _filePath     = path;
                _currentFrame = 0;
                if (_applier == null) _applier = new VMDApplier();

                // EditorState から初期値を反映
                var undo = GetUndoController?.Invoke();
                var es   = undo?.EditorState;
                if (es != null)
                {
                    // 軸反転が1つでも有効なら座標変換を適用する。
                    // 使う反転そのものも EditorState の設定から作り、インポートと揃える。
                    bool applyConv = es.PmxFlipX || es.PmxFlipZ;
                    _applier.PositionScale             = es.PmxUnityRatio;
                    _applier.CoordinateFlip            = new AxisFlip(es.PmxFlipX, es.PmxFlipZ);
                    _applyCoordinateConversion         = applyConv;
                    _applier.ApplyCoordinateConversion = applyConv;
                    _scaleField?.SetValueWithoutNotify(es.PmxUnityRatio);
                    _coordToggle?.SetValueWithoutNotify(applyConv);
                }

                // デバッグ設定を流し込む（EnableIK は既定 OFF）
                _applier.EnableIK          = _enableIK;
                _applier.IgnoreAngleLimits = _ignoreAngleLimits;
                _applier.KneePreBend       = _kneePreBend;
                _applier.TraceDirectory    = TraceDir();
                PushTraceBones();
                PushIkTraceBones();
                _applier.TraceEnabled      = _traceEnabled;

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
            _applier?.CloseTrace();
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
                _applier.EnableIK          = _enableIK;
                _applier.IgnoreAngleLimits = _ignoreAngleLimits;
                _applier.KneePreBend       = _kneePreBend;
                _applier.TraceDirectory    = TraceDir();
                PushTraceBones();
                PushIkTraceBones();
                _applier.TraceEnabled      = _traceEnabled;
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
            OnFrameApplied?.Invoke();
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        private void ResetPose()
        {
            if (Model == null || _applier == null) return;
            _applier.ResetAllBones(Model);
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        // ================================================================
        // トレース
        // ================================================================

        /// <summary>トレース CSV の出力フォルダ。VMD ファイルと同じ場所に出す。</summary>
        private string TraceDir()
            => string.IsNullOrEmpty(_filePath) ? null : Path.GetDirectoryName(_filePath);

        /// <summary>カンマ区切りのトレース対象ボーン名を applier へ渡す。</summary>
        private void PushTraceBones()
        {
            if (_applier == null) return;
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var raw in (_traceBoneList ?? "").Split(','))
            {
                string name = raw.Trim();
                if (name.Length > 0) set.Add(name);
            }
            _applier.TraceBoneNames = set;
        }

        /// <summary>カンマ区切りの IK トレース対象名を applier へ渡す。空なら全 IK ボーン。</summary>
        private void PushIkTraceBones()
        {
            if (_applier == null) return;
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var raw in (_ikTraceBoneList ?? "").Split(','))
            {
                string name = raw.Trim();
                if (name.Length > 0) set.Add(name);
            }
            _applier.TraceIkBoneNames = set;
        }

        /// <summary>0 〜 MaxFrameNumber を通しでトレースする。完了後は元のフレームへ戻す。</summary>
        private void RunTraceAllFrames()
        {
            if (_vmd == null || Model == null || _applier == null) { SetStatus("VMD を読み込んでください"); return; }

            string dir = TraceDir();
            if (string.IsNullOrEmpty(dir)) { SetStatus("VMD のフォルダを特定できません"); return; }

            _applier.TraceDirectory = dir;
            PushTraceBones();
            PushIkTraceBones();

            float saved = _currentFrame;
            int   max   = (int)_vmd.MaxFrameNumber;
            try
            {
                _applier.TraceAllFrames(Model, _vmd, 0, max);
                string outs = _enableIK
                    ? $"{VMDApplier.TraceFileName} / {CCDIKSolver.TraceFileName} / {CCDIKSolver.SummaryFileName}"
                    : VMDApplier.TraceFileName;
                SetStatus($"一括トレース完了 (0-{max}): {dir} → {outs}");
            }
            catch (Exception ex)
            {
                SetStatus($"一括トレース失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerVMDTestSubPanel] {ex}");
            }

            // 一括出力した CSV を、以降のスライダ操作で上書きしないようトレースを OFF に戻す
            _traceEnabled = false;
            _traceToggle?.SetValueWithoutNotify(false);
            if (_applier != null) { _applier.TraceEnabled = false; _applier.CloseTrace(); }

            _currentFrame = saved;
            Sync();
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
