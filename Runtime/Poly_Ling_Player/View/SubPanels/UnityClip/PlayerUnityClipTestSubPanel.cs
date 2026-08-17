// PlayerUnityClipTestSubPanel.cs
// Unity モーションクリップ（UnityClipDTO JSON）のテスト用 Player サブパネル。
// PlayerVMDTestSubPanel に倣う。Generic（bones）のみ対応。
// 仕様: 値は Unity 左手系のまま・座標変換なし（UnityClipDTO 準拠）。
//
// ■ 手持ちファイルと精度（あれば使う・なければそれなり）
//   clip のみ          … muscles から再構成。T ポーズ基準の Unity 定義値
//                        （CanonMuscleTable）をモデルのレスト枠へ移して使う。
//   + UnityLimit CSV   … ソースアバターの実可動域＋マッスル実測で再構成（最も高精度）
//   方式は UnityClipApplier.BodyMode = Auto が自動選択する。
//
//   バインドポーズ CSV（UnityBone）の行は削除した。使い道が bakedBones 入りの
//   クリップに限られ、現行のエクスポータが bakedBones を出さないため。
//   UnityClipApplier 側の実装は統合モーションパネルが委譲しているため残してある。

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;
using Poly_Ling.UndoSystem;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Player
{
    public class PlayerUnityClipTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>フレーム適用後に呼ぶ。GPU メッシュ再スキン（UpdateTransform）を core 側で起こすため。</summary>
        public Action OnFrameApplied;

        // ── 状態 ──────────────────────────────────────────────────────────
        private UnityClipDTO    _clip;
        private UnityClipApplier _applier;
        private float           _currentTime;   // 秒
        private float           _maxTime;       // 秒
        private string          _filePath;

        // ── UI 要素 ───────────────────────────────────────────────────────
        private Label         _modelLabel;
        private Label         _fileLabel;
        private Label         _limitLabel;
        private TextField     _clipPathField;
        private TextField     _limitPathField;
        private Button        _btnClear, _btnReload;
        private Button        _btnLimitClear;
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

        private const string ClipPathKey  = "UnityClip.Clip.Path";
        private const string LimitPathKey = "UnityClip.Limit.Path";

        private ModelContext Model => GetModel?.Invoke();
        private float FrameRate => _clip != null && _clip.frameRate > 0f ? _clip.frameRate : 30f;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Unity クリップテスト"));

            _modelLabel = new Label();
            _modelLabel.style.fontSize     = 10;
            _modelLabel.style.marginBottom = 3;
            root.Add(_modelLabel);

            // ── ファイル行（loadPMX デザインに統一）───────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("Unity Clip (JSON)"));
            _clipPathField = new TextField();
            _clipPathField.RegisterValueChangedCallback(e => RecentPaths.Set(ClipPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_clipPathField, OnBrowseClip));
            _clipPathField.SetValueWithoutNotify(RecentPaths.Get(ClipPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseClip);
            btnOpen.style.flexGrow = 1; btnOpen.style.marginRight = 2;
            _btnClear  = new Button(Clear)  { text = "クリア" };  _btnClear.style.width  = 52; _btnClear.style.marginRight = 2;
            _btnReload = new Button(Reload) { text = "再読込" }; _btnReload.style.width  = 52;
            opRow.Add(btnOpen); opRow.Add(_btnClear); opRow.Add(_btnReload);
            root.Add(opRow);

            _fileLabel = new Label(); _fileLabel.style.flexGrow = 1; _fileLabel.style.fontSize = 10;
            _fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _fileLabel.style.marginBottom = 3;
            root.Add(_fileLabel);

            // ── マッスル可動域・実測 CSV（UnityLimit）────────────────────
            //   読込むと muscles 経路の再構成精度が上がる。無くても動く。
            //   別モデルのものを誤って読んだときのために「クリア」を置く。
            root.Add(PlayerIoUiKit.SectionLabel("マッスル可動域・実測 CSV（UnityLimit）"));
            _limitPathField = new TextField();
            _limitPathField.RegisterValueChangedCallback(e => RecentPaths.Set(LimitPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_limitPathField, OnBrowseLimit));
            _limitPathField.SetValueWithoutNotify(RecentPaths.Get(LimitPathKey));

            var limitRow = new VisualElement();
            limitRow.style.flexDirection = FlexDirection.Row;
            limitRow.style.marginBottom  = 3;
            var btnLimit = PlayerIoUiKit.OpenButton("開く", OnBrowseLimit);
            btnLimit.style.flexGrow = 1; btnLimit.style.marginRight = 2;
            _btnLimitClear = new Button(ClearLimits) { text = "クリア" };
            _btnLimitClear.style.width = 64;
            limitRow.Add(btnLimit); limitRow.Add(_btnLimitClear);
            root.Add(limitRow);
            _limitLabel = new Label("(未読込)");
            _limitLabel.style.fontSize = 10;
            _limitLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _limitLabel.style.marginBottom = 3;
            root.Add(_limitLabel);

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
                if (_clip == null) return;
                _currentTime = Mathf.Clamp(e.newValue, 0f, _maxTime);
                UpdateSlider();
                UpdateTimeLabel();
                ApplyFrame();
            });
            frameRow.Add(_timeLabel); frameRow.Add(_timeInput);
            root.Add(frameRow);

            var nav1 = new VisualElement(); nav1.style.flexDirection = FlexDirection.Row; nav1.style.marginBottom = 2;
            MkNavBtn(nav1, "|◀",  () => { _currentTime = 0; Sync(); });
            MkNavBtn(nav1, "◀1", () => { if (_clip != null) { _currentTime = Mathf.Max(0f, _currentTime - Step()); Sync(); } });
            MkNavBtn(nav1, "25%", () => { if (_clip != null) { _currentTime = _maxTime * 0.25f; Sync(); } });
            MkNavBtn(nav1, "50%", () => { if (_clip != null) { _currentTime = _maxTime * 0.5f;  Sync(); } });
            MkNavBtn(nav1, "75%", () => { if (_clip != null) { _currentTime = _maxTime * 0.75f; Sync(); } });
            MkNavBtn(nav1, "1▶", () => { if (_clip != null) { _currentTime = Mathf.Min(_maxTime, _currentTime + Step()); Sync(); } });
            MkNavBtn(nav1, "▶|", () => { if (_clip != null) { _currentTime = _maxTime; Sync(); } });
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
                if (_clip != null) ApplyFrame();
            });
            scaleRow.Add(scaleLbl); scaleRow.Add(_scaleField);
            root.Add(scaleRow);

            _boneListFoldout = new Foldout { text = "Bone Tracks (0)", value = false };
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
            {
                if (model == null)
                {
                    _modelLabel.text = "(No model loaded)";
                }
                else if (_applier == null || _applier.BoneNodeCount == 0)
                {
                    _modelLabel.text = $"✓ {model.Name}  ({model.Bones.Count()} bones)";
                }
                else
                {
                    // MeshFilter ツリーを骨格に使うモデルでは、ボーン数ではなく
                    // 適用対象のノード内訳（実体／仮想ミラー）を出す。
                    string kind = _applier.MeshFilterSkeleton ? "MeshFilter骨格" : "ボーン";
                    _modelLabel.text =
                        $"✓ {model.Name}  ({kind} 実体 {_applier.RealBoneNodeCount}" +
                        (_applier.MirrorBoneNodeCount > 0
                            ? $" / 仮想ミラー {_applier.MirrorBoneNodeCount}"
                            : string.Empty) + ")";
                }
            }

            if (_fileLabel != null)
                _fileLabel.text = string.IsNullOrEmpty(_filePath) ? "(None)" : Path.GetFileName(_filePath);
            if (_btnClear  != null) _btnClear.SetEnabled(_clip != null);
            if (_btnReload != null) _btnReload.SetEnabled(!string.IsNullOrEmpty(_filePath));
            if (_btnLimitClear != null)
                _btnLimitClear.SetEnabled(_applier != null && _applier.HasMuscleLimits);

            if (_clipSection == null) return;
            bool hasClip = _clip != null;
            _clipSection.style.display = hasClip ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasClip) return;

            int trackCount  = _clip.bones?.Count ?? 0;
            int muscleCount = _clip.muscles?.Count ?? 0;
            int bakedCount  = _clip.bakedBones?.Count ?? 0;
            if (_clipInfoLabel != null)
                _clipInfoLabel.text =
                    $"Clip: {_clip.name}  ({_clip.clipType})\n" +
                    $"Length: {_maxTime:F2}s  (@ {FrameRate:F0}fps)\n" +
                    $"Bone tracks: {trackCount}  Muscles: {muscleCount}  Baked: {bakedCount}\n" +
                    $"Body: {(_applier != null ? _applier.ResolvedBodyMode.ToString() : "-")}" +
                    $"{(_applier != null && _applier.HasMuscleMeasured ? " (実測)" : "")}";

            if (_clipMatchLabel != null && model != null && _applier != null)
            {
                // path（二次骨）と本体ボーンを分けて出す。合算した率は経路別の欠落を隠すため使わない。
                int pm = _applier.PathMatchedCount,   pt = _applier.PathTrackCount;
                int mm = _applier.MuscleMatchedCount, mt = _applier.MuscleTargetCount;
                var txt = $"Path: {pm}/{pt}   Muscle: {mm}/{mt}";
                if (_applier.SupplementedHumanoidNames.Count > 0)
                    txt += "\n左右補完: " +
                           string.Join(", ", _applier.SupplementedHumanoidNames.ToArray());
                if (_applier.UnresolvedMuscleBones.Count > 0)
                    txt += "\n未解決(本体): " + string.Join(", ", _applier.UnresolvedMuscleBones.ToArray());
                _clipMatchLabel.text = txt;
                _clipMatchLabel.style.whiteSpace = WhiteSpace.Normal;
            }

            UpdateSlider();
            UpdateTimeLabel();
            RefreshBoneList();
        }

        private void RefreshBoneList()
        {
            if (_boneListContainer == null || _clip?.bones == null) return;
            _boneListContainer.Clear();
            var tracks = _clip.bones.Take(50).ToList();
            foreach (var track in tracks)
            {
                // 純仮想ミラーノード（実体なし）も解決済みとして扱うため
                // ResolveMasterIndex ではなく ResolveNode で判定する。
                bool matched = Model != null && _applier != null && _applier.ResolveNode(track.path) >= 0;
                int keys = track.keys?.Count ?? 0;
                var lbl = new Label($"{(matched ? "✓" : "✗")} {track.path} ({keys} keys)");
                lbl.style.fontSize = 10;
                lbl.style.color    = new StyleColor(matched ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.8f, 0.4f, 0.4f));
                _boneListContainer.Add(lbl);
            }
            int rem = (_clip.bones.Count) - 50;
            if (rem > 0) { var l = new Label($"  ...他 {rem} トラック"); l.style.fontSize = 9; _boneListContainer.Add(l); }
            if (_boneListFoldout != null) _boneListFoldout.text = $"Bone Tracks ({_clip.bones.Count})";
        }

        // ================================================================
        // 操作
        // ================================================================

        // 「開く」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowseClip()
        {
            string path = PlayerIoUiKit.AskLoadPath("Open Unity Clip", _clipPathField.value, "json");
            if (string.IsNullOrEmpty(path)) return;
            _clipPathField.value = path;
            LoadClip(path);
        }

        private void LoadClip(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }
            try
            {
                _clip         = UnityClipSerializer.LoadJson(path);
                _filePath     = path;
                _currentTime  = 0f;
                _maxTime      = ComputeMaxTime(_clip);
                if (_applier == null) _applier = new UnityClipApplier();

                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }

                SetStatus($"クリップ読込み完了: {Path.GetFileName(path)}");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"読込み失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerUnityClipTestSubPanel] {ex}");
            }
        }

        // 外部 UnityLimit CSV（マッスル可動域・実測）を読む。無くても動作する。
        private void OnBrowseLimit()
        {
            string path = PlayerIoUiKit.AskLoadPath("Open UnityLimit CSV", _limitPathField.value, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _limitPathField.value = path;
            LoadLimits(path);
        }

        private void LoadLimits(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }
            try
            {
                string text = File.ReadAllText(path);
                if (_applier == null) _applier = new UnityClipApplier();
                int n = _applier.LoadMuscleLimitCsv(text);

                if (_limitLabel != null)
                    _limitLabel.text = n > 0
                        ? $"✓ {Path.GetFileName(path)} ({n} bones{(_applier.HasMuscleMeasured ? " / 実測あり" : "")})"
                        : "(0 bones)";

                if (_clip != null) ApplyFrame();
                SetStatus(n > 0
                    ? $"マッスル可動域読込: {n} bones"
                    : "マッスル可動域: 有効な行が見つかりません");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"マッスル可動域読込失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerUnityClipTestSubPanel] {ex}");
            }
        }

        // 読み込んだ UnityLimit CSV を破棄して既定（T ポーズ基準の Unity 定義値）へ戻す。
        // 別モデルの CSV を誤って読んだときの復帰口。パス欄と記憶パスも消す。
        private void ClearLimits()
        {
            if (_applier != null) _applier.ClearMuscleLimits();

            if (_limitPathField != null) _limitPathField.SetValueWithoutNotify(string.Empty);
            RecentPaths.Set(LimitPathKey, string.Empty);
            if (_limitLabel != null) _limitLabel.text = "(未読込)";

            // 姿勢は即座に既定へ戻す。読込側と対称にしておかないと、
            // 画面上はクリアされたのにポーズだけ古い CSV のまま残る。
            if (_clip != null) ApplyFrame();

            SetStatus("マッスル可動域をクリアしました（既定値を使用）");
            RefreshAll();
        }

        private void Clear()
        {
            ResetPose();
            _clip = null; _filePath = null; _currentTime = 0f; _maxTime = 0f;
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path  = _filePath;
            float  time  = _currentTime;
            Clear();
            try
            {
                _clip = UnityClipSerializer.LoadJson(path); _filePath = path;
                _maxTime = ComputeMaxTime(_clip);
                _currentTime = Mathf.Clamp(time, 0f, _maxTime);
                if (_applier == null) _applier = new UnityClipApplier();
                var model = Model;
                if (model != null) { _applier.BuildMapping(model); ApplyFrame(); }
                RefreshAll();
            }
            catch (Exception ex) { SetStatus($"再読込み失敗: {ex.Message}"); }
        }

        private void ApplyFrame()
        {
            if (_clip == null || Model == null || _applier == null) return;
            _applier.ApplyFrame(Model, _clip, _currentTime);
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

        // 1 フレーム分の秒（ナビの ◀1 / 1▶ 用）
        private float Step() => FrameRate > 0f ? 1f / FrameRate : 1f / 30f;

        private void UpdateSlider()
        {
            if (_timeSlider == null || _clip == null) return;
            _timeSlider.highValue = Mathf.Max(0.0001f, _maxTime);
            _timeSlider.SetValueWithoutNotify(_currentTime);
            _timeInput?.SetValueWithoutNotify(_currentTime);
        }

        private void UpdateTimeLabel()
        {
            if (_timeLabel == null) return;
            _timeLabel.text = $"Time: {_currentTime:F2}s / {_maxTime:F2}s";
        }

        private static float ComputeMaxTime(UnityClipDTO clip)
        {
            float max = 0f;
            if (clip?.bones == null) return 0f;
            foreach (var track in clip.bones)
            {
                if (track?.keys == null) continue;
                foreach (var key in track.keys)
                    if (key != null && key.t > max) max = key.t;
            }
            return max;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static void MkNavBtn(VisualElement row, string text, Action onClick) { var b = new Button(onClick) { text = text }; b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9; row.Add(b); }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
