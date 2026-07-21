// PlayerMotionClipTestSubPanel.cs
// 統合 MotionClipDTO（float 秒）のテスト用 Player サブパネル（再生専用）。
// 入力: VMD / UnityClip JSON / 統合 MotionClip JSON を読み込み、内部で
//       MotionClipDTO に変換して秒スライダーで適用する。
// 旧 PlayerVMDTestSubPanel / PlayerUnityClipTestSubPanel は比較用に残置。
//
// 仕様: 値は Unity 左手系のまま・座標変換なし（MotionClipDTO 準拠）。
//       boneName は VMD 直接適用、path/humanoid は UnityClipApplier 経路（Applier 側で分岐）。

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;
using Poly_Ling.UndoSystem;
using Poly_Ling.VMD;
using Poly_Ling.UnityClip;
using Poly_Ling.Motion;

namespace Poly_Ling.Player
{
    public class PlayerMotionClipTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>フレーム適用後に呼ぶ。GPU メッシュ再スキンを core 側で起こすため。</summary>
        public Action OnFrameApplied;

        // ── ソース種別 ────────────────────────────────────────────────────
        private static readonly List<string> SourceChoices =
            new List<string> { "VMD", "UnityClip JSON", "統合JSON" };

        // ── 状態 ──────────────────────────────────────────────────────────
        private MotionClipDTO    _dto;
        private MotionClipApplier _applier;
        private float            _currentTime;   // 秒
        private float            _maxTime;       // 秒
        private string           _filePath;
        private int              _sourceKind;    // 0=VMD, 1=UnityClip JSON, 2=統合JSON

        // ── UI 要素 ───────────────────────────────────────────────────────
        private Label         _modelLabel;
        private Label         _fileLabel;
        private Label         _bindPoseLabel;
        private DropdownField _sourceField;
        private TextField     _pathField;
        private TextField     _bindPathField;
        private Button        _btnClear, _btnReload;
        private VisualElement _clipSection;
        private Label         _clipInfoLabel;
        private Label         _clipMatchLabel;
        private Slider        _timeSlider;
        private Label         _timeLabel;
        private FloatField    _timeInput;
        private FloatField    _scaleField;
        private VisualElement _boneListContainer;
        private Foldout       _boneListFoldout;
        private Label         _statusLabel;

        private const string PathKey     = "MotionClip.Path";
        private const string BindPathKey = "MotionClip.Bind.Path";
        private const string SourceKey   = "MotionClip.Source";

        private ModelContext Model => GetModel?.Invoke();
        private float FrameRate => _dto != null && _dto.frameRate > 0f ? _dto.frameRate : 30f;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("統合モーションテスト"));

            _modelLabel = new Label();
            _modelLabel.style.fontSize     = 10;
            _modelLabel.style.marginBottom = 3;
            root.Add(_modelLabel);

            // ── ソース種別 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("ソース種別"));
            _sourceKind  = Mathf.Clamp(ParseInt(RecentPaths.Get(SourceKey), 0), 0, SourceChoices.Count - 1);
            _sourceField = new DropdownField(SourceChoices, _sourceKind);
            _sourceField.style.marginBottom = 3;
            _sourceField.RegisterValueChangedCallback(e =>
            {
                _sourceKind = Mathf.Max(0, SourceChoices.IndexOf(e.newValue));
                RecentPaths.Set(SourceKey, _sourceKind.ToString());
            });
            root.Add(_sourceField);

            // ── ファイル行 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("モーションファイル"));
            _pathField = new TextField();
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(PathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_pathField, OnBrowse));
            _pathField.SetValueWithoutNotify(RecentPaths.Get(PathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", () => Load(_pathField.value));
            btnOpen.style.flexGrow = 1; btnOpen.style.marginRight = 2;
            _btnClear  = new Button(Clear)  { text = "クリア" };  _btnClear.style.width  = 52; _btnClear.style.marginRight = 2;
            _btnReload = new Button(Reload) { text = "再読込" }; _btnReload.style.width  = 52;
            opRow.Add(btnOpen); opRow.Add(_btnClear); opRow.Add(_btnReload);
            root.Add(opRow);

            _fileLabel = new Label(); _fileLabel.style.flexGrow = 1; _fileLabel.style.fontSize = 10;
            _fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _fileLabel.style.marginBottom = 3;
            root.Add(_fileLabel);

            // ── バインドポーズ行（ソース rest = 外部 UnityBone CSV v2）───────
            root.Add(PlayerIoUiKit.SectionLabel("バインドポーズ CSV（UnityBone v2）"));
            _bindPathField = new TextField();
            _bindPathField.RegisterValueChangedCallback(e => RecentPaths.Set(BindPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_bindPathField, OnBrowseBind));
            _bindPathField.SetValueWithoutNotify(RecentPaths.Get(BindPathKey));

            var btnBindPose = PlayerIoUiKit.OpenButton("開く", () => LoadBind(_bindPathField.value));
            root.Add(btnBindPose);
            _bindPoseLabel = new Label("(未読込)");
            _bindPoseLabel.style.fontSize = 10;
            _bindPoseLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _bindPoseLabel.style.marginBottom = 3;
            root.Add(_bindPoseLabel);

            // ── クリップセクション（ロード後に表示）──────────────────────
            _clipSection = new VisualElement();
            _clipSection.style.display = DisplayStyle.None;
            root.Add(_clipSection);
            BuildClipSection(_clipSection);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            RefreshAll();
        }

        private void BuildClipSection(VisualElement root)
        {
            _clipInfoLabel = new Label();
            _clipInfoLabel.style.fontSize   = 10;
            _clipInfoLabel.style.marginBottom = 3;
            _clipInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_clipInfoLabel);

            _clipMatchLabel = new Label();
            _clipMatchLabel.style.fontSize   = 10;
            _clipMatchLabel.style.color      = new StyleColor(new Color(0.5f, 0.9f, 0.5f));
            _clipMatchLabel.style.marginBottom = 4;
            root.Add(_clipMatchLabel);

            // ── 時刻スライダー（秒）─────────────────────────────────────────
            _timeSlider = new Slider(0f, 1f) { value = 0f };
            _timeSlider.style.marginBottom = 2;
            _timeSlider.RegisterValueChangedCallback(e =>
            {
                _currentTime = e.newValue;
                _timeInput?.SetValueWithoutNotify(_currentTime);
                UpdateTimeLabel();
                ApplyFrame();
            });
            root.Add(_timeSlider);

            var frameRow = new VisualElement();
            frameRow.style.flexDirection = FlexDirection.Row;
            frameRow.style.marginBottom  = 3;
            _timeLabel = new Label("Time: 0.00s / 0.00s");
            _timeLabel.style.flexGrow  = 1; _timeLabel.style.fontSize = 10;
            _timeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _timeInput = new FloatField { value = 0f }; _timeInput.style.width = 70;
            _timeInput.RegisterValueChangedCallback(e =>
            {
                if (_dto == null) return;
                _currentTime = Mathf.Clamp(e.newValue, 0f, _maxTime);
                UpdateSlider();
                UpdateTimeLabel();
                ApplyFrame();
            });
            frameRow.Add(_timeLabel); frameRow.Add(_timeInput);
            root.Add(frameRow);

            var nav1 = new VisualElement(); nav1.style.flexDirection = FlexDirection.Row; nav1.style.marginBottom = 2;
            MkNavBtn(nav1, "|◀",  () => { _currentTime = 0; Sync(); });
            MkNavBtn(nav1, "◀1", () => { if (_dto != null) { _currentTime = Mathf.Max(0f, _currentTime - Step()); Sync(); } });
            MkNavBtn(nav1, "25%", () => { if (_dto != null) { _currentTime = _maxTime * 0.25f; Sync(); } });
            MkNavBtn(nav1, "50%", () => { if (_dto != null) { _currentTime = _maxTime * 0.5f;  Sync(); } });
            MkNavBtn(nav1, "75%", () => { if (_dto != null) { _currentTime = _maxTime * 0.75f; Sync(); } });
            MkNavBtn(nav1, "1▶", () => { if (_dto != null) { _currentTime = Mathf.Min(_maxTime, _currentTime + Step()); Sync(); } });
            MkNavBtn(nav1, "▶|", () => { if (_dto != null) { _currentTime = _maxTime; Sync(); } });
            root.Add(nav1);

            var resetBtn = new Button(ResetPose) { text = "ポーズリセット" };
            resetBtn.style.marginBottom = 4;
            root.Add(resetBtn);

            root.Add(SecLabel("オプション"));

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
                if (_dto != null) ApplyFrame();
            });
            scaleRow.Add(scaleLbl); scaleRow.Add(_scaleField);
            root.Add(scaleRow);

            _boneListFoldout = new Foldout { text = "Tracks (0)", value = false };
            _boneListContainer = new VisualElement();
            _boneListFoldout.Add(_boneListContainer);
            root.Add(_boneListFoldout);
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
            if (_btnClear  != null) _btnClear.SetEnabled(_dto != null);
            if (_btnReload != null) _btnReload.SetEnabled(!string.IsNullOrEmpty(_filePath));

            if (_clipSection == null) return;
            bool hasClip = _dto != null;
            _clipSection.style.display = hasClip ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasClip) return;

            int trackCount = TrackCount(_dto);
            if (_clipInfoLabel != null)
                _clipInfoLabel.text =
                    $"Clip: {_dto.name}\n" +
                    $"Length: {_maxTime:F2}s  (@ {FrameRate:F0}fps)\n" +
                    $"Bone: {(_dto.bones?.Count ?? 0)}  Baked: {(_dto.bakedBones?.Count ?? 0)}  " +
                    $"Muscle: {(_dto.muscles?.Count ?? 0)}  Morph: {(_dto.morphs?.Count ?? 0)}";

            if (_clipMatchLabel != null && model != null && _applier != null)
            {
                float rate = trackCount > 0 ? (float)_applier.MatchedTrackCount / trackCount : 0f;
                _clipMatchLabel.text = $"Matched: {_applier.MatchedTrackCount}/{trackCount} ({rate:P0})";
            }

            UpdateSlider();
            UpdateTimeLabel();
            RefreshBoneList();
        }

        private void RefreshBoneList()
        {
            if (_boneListContainer == null || _dto == null) return;
            _boneListContainer.Clear();

            var all = new List<MotionTrackDTO>();
            if (_dto.bones != null)      all.AddRange(_dto.bones);
            if (_dto.bakedBones != null) all.AddRange(_dto.bakedBones);

            foreach (var track in all.Take(50))
            {
                if (track == null) continue;
                bool matched = Model != null && _applier != null && _applier.IsTrackMatched(track);
                int keys = track.keys?.Count ?? 0;
                var lbl = new Label($"{(matched ? "✓" : "✗")} [{track.targetKind}] {track.id} ({keys} keys)");
                lbl.style.fontSize = 10;
                lbl.style.color    = new StyleColor(matched ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.8f, 0.4f, 0.4f));
                _boneListContainer.Add(lbl);
            }
            int rem = all.Count - 50;
            if (rem > 0) { var l = new Label($"  ...他 {rem} トラック"); l.style.fontSize = 9; _boneListContainer.Add(l); }
            if (_boneListFoldout != null) _boneListFoldout.text = $"Tracks ({all.Count})";
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnBrowse()
        {
            string ext  = _sourceKind == 0 ? "vmd" : "json";
            string dir  = string.IsNullOrEmpty(_pathField.value) ? "" : Path.GetDirectoryName(_pathField.value);
            string path = PLEditorBridge.I.OpenFilePanel("Open Motion", dir, ext);
            if (string.IsNullOrEmpty(path)) return;
            _pathField.value = path;
            Load(path);
        }

        private void Load(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }
            try
            {
                _dto = LoadDtoBySource(path);
                if (_dto == null) { SetStatus("読込み結果が空です"); return; }

                _filePath    = path;
                _currentTime = 0f;
                _maxTime     = ComputeMaxTime(_dto);
                if (_applier == null) _applier = new MotionClipApplier();
                _applier.SetClip(_dto);

                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }

                SetStatus($"読込み完了: {Path.GetFileName(path)}");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"読込み失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerMotionClipTestSubPanel] {ex}");
            }
        }

        // ソース種別に応じて読み込み、MotionClipDTO へ変換する。
        private MotionClipDTO LoadDtoBySource(string path)
        {
            switch (_sourceKind)
            {
                case 0: // VMD
                    return MotionClipConverters.FromVMD(VMDData.LoadFromFile(path));
                case 1: // UnityClip JSON
                    return MotionClipConverters.FromUnityClipDTO(UnityClipSerializer.LoadJson(path));
                default: // 統合JSON
                    return MotionClipSerializer.LoadJson(path);
            }
        }

        // 外部 UnityBone CSV v2（ソース rest）を読み、リターゲット経路を有効化。
        private void OnBrowseBind()
        {
            string dir  = string.IsNullOrEmpty(_bindPathField.value) ? "" : Path.GetDirectoryName(_bindPathField.value);
            string path = PLEditorBridge.I.OpenFilePanel("Open UnityBone CSV (bind pose)", dir, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _bindPathField.value = path;
            LoadBind(path);
        }

        private void LoadBind(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }
            try
            {
                string text = File.ReadAllText(path);
                if (_applier == null) _applier = new MotionClipApplier();
                int n = _applier.LoadSourceRestCsv(text);

                var model = Model;
                if (model != null) _applier.BuildMapping(model);

                if (_bindPoseLabel != null)
                    _bindPoseLabel.text = n > 0 ? $"✓ {Path.GetFileName(path)} ({n} bones)" : "(0 bones)";

                if (_dto != null) ApplyFrame();
                SetStatus(n > 0
                    ? $"バインドポーズ読込: {n} bones（リターゲット有効）"
                    : "バインドポーズ: Humanoid 行が見つかりません");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"バインドポーズ読込失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerMotionClipTestSubPanel] {ex}");
            }
        }

        private void Clear()
        {
            ResetPose();
            _dto = null; _filePath = null; _currentTime = 0f; _maxTime = 0f;
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath;
            float  time = _currentTime;
            Clear();
            try
            {
                _dto = LoadDtoBySource(path); _filePath = path;
                _maxTime = ComputeMaxTime(_dto);
                _currentTime = Mathf.Clamp(time, 0f, _maxTime);
                if (_applier == null) _applier = new MotionClipApplier();
                _applier.SetClip(_dto);
                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }
                RefreshAll();
            }
            catch (Exception ex) { SetStatus($"再読込み失敗: {ex.Message}"); }
        }

        private void ApplyFrame()
        {
            if (_dto == null || Model == null || _applier == null) return;
            _applier.ApplyFrame(Model, _currentTime);
            OnFrameApplied?.Invoke();
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        private void ResetPose()
        {
            if (Model == null || _applier == null) return;
            _applier.ResetAllBones(Model);
            OnFrameApplied?.Invoke();
            GetToolContext?.Invoke()?.Repaint?.Invoke();
        }

        private void Sync()
        {
            UpdateSlider();
            UpdateTimeLabel();
            ApplyFrame();
        }

        private float Step() => FrameRate > 0f ? 1f / FrameRate : 1f / 30f;

        private void UpdateSlider()
        {
            if (_timeSlider == null || _dto == null) return;
            _timeSlider.highValue = Mathf.Max(0.0001f, _maxTime);
            _timeSlider.SetValueWithoutNotify(_currentTime);
            _timeInput?.SetValueWithoutNotify(_currentTime);
        }

        private void UpdateTimeLabel()
        {
            if (_timeLabel == null) return;
            _timeLabel.text = $"Time: {_currentTime:F2}s / {_maxTime:F2}s";
        }

        // ================================================================
        // ヘルパ
        // ================================================================

        private static int TrackCount(MotionClipDTO dto)
        {
            if (dto == null) return 0;
            return (dto.bones?.Count ?? 0) + (dto.bakedBones?.Count ?? 0);
        }

        private static float ComputeMaxTime(MotionClipDTO dto)
        {
            float max = 0f;
            if (dto == null) return 0f;
            max = Mathf.Max(max, MaxTimeTracks(dto.bones));
            max = Mathf.Max(max, MaxTimeTracks(dto.bakedBones));
            if (dto.body != null) max = Mathf.Max(max, MaxTimeKeys(dto.body.keys));
            max = Mathf.Max(max, MaxTimeScalar(dto.morphs));
            max = Mathf.Max(max, MaxTimeScalar(dto.muscles));
            return max;
        }

        private static float MaxTimeTracks(List<MotionTrackDTO> tracks)
        {
            float max = 0f;
            if (tracks == null) return 0f;
            foreach (var t in tracks)
                if (t != null) max = Mathf.Max(max, MaxTimeKeys(t.keys));
            return max;
        }

        private static float MaxTimeKeys(List<MotionKeyDTO> keys)
        {
            float max = 0f;
            if (keys == null) return 0f;
            foreach (var k in keys)
                if (k != null && k.t > max) max = k.t;
            return max;
        }

        private static float MaxTimeScalar(List<MotionScalarTrackDTO> tracks)
        {
            float max = 0f;
            if (tracks == null) return 0f;
            foreach (var t in tracks)
                if (t?.keys != null)
                    foreach (var k in t.keys)
                        if (k != null && k.t > max) max = k.t;
            return max;
        }

        private static int ParseInt(string s, int fallback)
        {
            return int.TryParse(s, out int v) ? v : fallback;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static void MkNavBtn(VisualElement row, string text, Action onClick) { var b = new Button(onClick) { text = text }; b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9; row.Add(b); }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
