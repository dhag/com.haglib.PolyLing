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
using Poly_Ling.Vrm;

namespace Poly_Ling.Player
{
    public class PlayerUnityClipTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>コマンドの発行口。VRMA 書き出しで使う。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>現在のモデル索引。</summary>
        public Func<int> GetModelIndex;

        /// <summary>フレーム適用後に呼ぶ。GPU メッシュ再スキン（UpdateTransform）を core 側で起こすため。</summary>
        public Action OnFrameApplied;

        // ── 状態 ──────────────────────────────────────────────────────────
        private UnityClipDTO    _clip;
        private UnityClipApplier _applier;
        private float           _currentTime;   // 秒
        private float           _maxTime;       // 秒
        private string          _filePath;

        // ── UI 要素 ───────────────────────────────────────────────────────
        // UI 自動操作の ID は "unityClipTest.<下の Id>"（UiControlAttribute.cs）。
        // ボーンの一覧は読み込んだクリップに合わせて作り直す行（Rows）。
        [UiControl("model", Safety = UiSafety.ReadOnly, Description = "対象モデル")]
        private Label         _modelLabel;
        [UiControl("file", Safety = UiSafety.ReadOnly, Description = "読み込んだクリップ")]
        private Label         _fileLabel;
        [UiControl("limitFile", Safety = UiSafety.ReadOnly, Description = "読み込んだ可動域ファイル")]
        private Label         _limitLabel;
        [UiControl("clipPath", Description = "クリップのパス（ダイアログの初期値として使う）")]
        private TextField     _clipPathField;
        [UiControl("limitPath", Description = "可動域ファイルのパス（ダイアログの初期値として使う）")]
        private TextField     _limitPathField;
        [UiControl("clear", Safety = UiSafety.Destructive, Description = "読み込んだクリップを外す")]
        private Button        _btnClear;
        [UiControl("reload", Safety = UiSafety.SafeWrite, Description = "クリップを読み直す")]
        private Button        _btnReload;
        [UiControl("limitClear", Safety = UiSafety.Destructive, Description = "読み込んだ可動域を外す")]
        private Button        _btnLimitClear;
        [UiControl("open", Safety = UiSafety.UserOnly, Description = "クリップを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnOpen;
        [UiControl("limitOpen", Safety = UiSafety.UserOnly, Description = "可動域ファイルを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnLimitOpen;
        [UiControl("browseClip", Safety = UiSafety.UserOnly, Description = "クリップの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseClip;
        [UiControl("browseLimit", Safety = UiSafety.UserOnly, Description = "可動域ファイルの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseLimit;
        [UiControl(Ignore = true)]
        private VisualElement _clipSection;
        [UiControl("clipInfo", Safety = UiSafety.ReadOnly, Description = "クリップの情報")]
        private Label         _clipInfoLabel;
        [UiControl("matchedBones", Safety = UiSafety.ReadOnly, Description = "名前が一致したボーン数")]
        private Label         _clipMatchLabel;
        [UiControl("time", Description = "現在の時刻（スライダー）")]
        private Slider        _timeSlider;
        [UiControl("timeText", Safety = UiSafety.ReadOnly, Description = "現在の時刻の表示")]
        private Label         _timeLabel;
        [UiControl("timeValue", Description = "現在の時刻（数値入力）")]
        private FloatField    _timeInput;
        [UiControl("scale", Description = "取り込みの倍率")]
        private FloatField    _scaleField;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _boneListContainer;
        [UiControl(Ignore = true)]
        private Foldout       _boneListFoldout;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        [UiControl("time.first", Safety = UiSafety.SafeWrite, Description = "先頭へ（|◀）")]
        private Button        _btnTimeFirst;
        [UiControl("time.previous", Safety = UiSafety.SafeWrite, Description = "1 コマ戻る（◀1）")]
        private Button        _btnTimePrev;
        [UiControl("time.at25Percent", Safety = UiSafety.SafeWrite, Description = "25% の位置へ")]
        private Button        _btnTime25;
        [UiControl("time.at50Percent", Safety = UiSafety.SafeWrite, Description = "50% の位置へ")]
        private Button        _btnTime50;
        [UiControl("time.at75Percent", Safety = UiSafety.SafeWrite, Description = "75% の位置へ")]
        private Button        _btnTime75;
        [UiControl("time.next", Safety = UiSafety.SafeWrite, Description = "1 コマ進む（1▶）")]
        private Button        _btnTimeNext;
        [UiControl("time.last", Safety = UiSafety.SafeWrite, Description = "末尾へ（▶|）")]
        private Button        _btnTimeLast;
        [UiControl("resetPose", Safety = UiSafety.SafeWrite, Description = "ポーズをリセットする")]
        private Button        _btnResetPose;

        // VRMA 書き出し
        [UiControl("vrma.path", Description = "VRMA の書き出し先（ダイアログの初期値として使う）")]
        private TextField  _vrmaPathField;
        [UiControl("vrma.browse", Safety = UiSafety.UserOnly, Description = "VRMA の [...]（保存ダイアログを開く）")]
        private Button     _btnBrowseVrma;
        [UiControl("vrma.fps", Description = "VRMA 書き出しのフレームレート")]
        private FloatField _vrmaFpsField;
        [UiControl("vrma.scale", Description = "VRMA 書き出しの倍率")]
        private FloatField _vrmaScaleField;
        [UiControl("vrma.startTime", Description = "VRMA 書き出しの開始時刻")]
        private FloatField _vrmaStartField;
        [UiControl("vrma.endTime", Description = "VRMA 書き出しの終了時刻")]
        private FloatField _vrmaEndField;
        [UiControl("vrma.export", Safety = UiSafety.UserOnly, Description = "VRMA を書き出す（保存ダイアログを開く）")]
        private Button     _btnVrmaExport;
        [UiControl("vrma.status", Safety = UiSafety.ReadOnly, Description = "VRMA 書き出しの状態")]
        private Label      _vrmaLabel;

        private const string ClipPathKey  = "UnityClip.Clip.Path";
        private const string LimitPathKey = "UnityClip.Limit.Path";
        private const string VrmaPathKey  = "UnityClip.Vrma.Path";

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
            root.Add(PlayerIoUiKit.PathRow(_clipPathField, OnBrowseClip, out _btnBrowseClip));
            _clipPathField.SetValueWithoutNotify(RecentPaths.Get(ClipPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseClip);
            _btnOpen = btnOpen;
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
            root.Add(PlayerIoUiKit.PathRow(_limitPathField, OnBrowseLimit, out _btnBrowseLimit));
            _limitPathField.SetValueWithoutNotify(RecentPaths.Get(LimitPathKey));

            var limitRow = new VisualElement();
            limitRow.style.flexDirection = FlexDirection.Row;
            limitRow.style.marginBottom  = 3;
            var btnLimit = PlayerIoUiKit.OpenButton("開く", OnBrowseLimit);
            _btnLimitOpen = btnLimit;
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
            _btnTimeFirst = MkNavBtn(nav1, "|◀",  () => { _currentTime = 0; Sync(); });
            _btnTimePrev  = MkNavBtn(nav1, "◀1", () => { if (_clip != null) { _currentTime = Mathf.Max(0f, _currentTime - Step()); Sync(); } });
            _btnTime25    = MkNavBtn(nav1, "25%", () => { if (_clip != null) { _currentTime = _maxTime * 0.25f; Sync(); } });
            _btnTime50    = MkNavBtn(nav1, "50%", () => { if (_clip != null) { _currentTime = _maxTime * 0.5f;  Sync(); } });
            _btnTime75    = MkNavBtn(nav1, "75%", () => { if (_clip != null) { _currentTime = _maxTime * 0.75f; Sync(); } });
            _btnTimeNext  = MkNavBtn(nav1, "1▶", () => { if (_clip != null) { _currentTime = Mathf.Min(_maxTime, _currentTime + Step()); Sync(); } });
            _btnTimeLast  = MkNavBtn(nav1, "▶|", () => { if (_clip != null) { _currentTime = _maxTime; Sync(); } });
            root.Add(nav1);

            var resetBtn = new Button(ResetPose) { text = "ポーズリセット" };
            resetBtn.style.marginBottom = 4;
            root.Add(resetBtn);
            _btnResetPose = resetBtn;

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

            BuildVrmaSection(root);

            _boneListFoldout = new Foldout { text = "Bone Tracks (0)", value = false };
            _boneListContainer = new VisualElement();
            _boneListFoldout.Add(_boneListContainer);
            root.Add(_boneListFoldout);
        }

        // ── VRM アニメーション書き出し ───────────────────────
        //   出るのは Hips の平行移動と Humanoid 骨の回転だけ。
        //   二次骨（髪・スカート等）と表情は VRMA には載らない。
        private void BuildVrmaSection(VisualElement root)
        {
            root.Add(SecLabel("VRM アニメーション書き出し（.vrma）"));

            _vrmaPathField = new TextField();
            _vrmaPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VrmaPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_vrmaPathField, OnBrowseVrma, out _btnBrowseVrma));
            _vrmaPathField.SetValueWithoutNotify(RecentPaths.Get(VrmaPathKey));

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom  = 2;
            row1.Add(VrmaNumField("FPS", out _vrmaFpsField, 30f));
            row1.Add(VrmaNumField("Scale", out _vrmaScaleField, 1f));
            root.Add(row1);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginBottom  = 3;
            row2.Add(VrmaNumField("Start", out _vrmaStartField, 0f));
            row2.Add(VrmaNumField("End", out _vrmaEndField, 0f));
            root.Add(row2);

            _btnVrmaExport = new Button(OnExportVrma) { text = "VRMA 書き出し" };
            _btnVrmaExport.style.marginBottom = 2;
            root.Add(_btnVrmaExport);

            _vrmaLabel = new Label();
            _vrmaLabel.style.fontSize     = 10;
            _vrmaLabel.style.whiteSpace   = WhiteSpace.Normal;
            _vrmaLabel.style.marginBottom = 4;
            root.Add(_vrmaLabel);
        }

        private static VisualElement VrmaNumField(string label, out FloatField field, float initial)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.flexGrow      = 1;
            box.style.marginRight   = 4;

            var lbl = new Label(label);
            lbl.style.width          = 44;
            lbl.style.fontSize       = 10;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;

            field = new FloatField { value = initial };
            field.style.flexGrow = 1;

            box.Add(lbl);
            box.Add(field);
            return box;
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

            RefreshVrmaSection(model);

            UpdateSlider();
            UpdateTimeLabel();
            RefreshBoneList();
        }

        // VRMA 書き出しの可否と理由を出す。
        //   実装未登録（VRM パッケージ無し）と Humanoid 未割当を区別する。
        private void RefreshVrmaSection(ModelContext model)
        {
            if (_btnVrmaExport == null || _vrmaLabel == null) return;

            if (!PLVrmAnimationBridge.I.IsAvailable)
            {
                _btnVrmaExport.SetEnabled(false);
                _vrmaLabel.text = "VRM パッケージ (com.vrmc.vrm) が無いため書き出せません";
                return;
            }

            int mapped = model?.HumanoidMapping != null ? model.HumanoidMapping.Count : 0;
            bool hasModel = model != null && mapped > 0;

            _btnVrmaExport.SetEnabled(hasModel && SendCommand != null);
            _vrmaLabel.text = hasModel
                ? $"Humanoid {mapped} bones。Hips 位置と Humanoid 骨の回転だけを出します"
                : "Humanoid 割り当てがありません";
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
            string path = PlayerIoUiKit.AskLoadPath("Open Unity Clip", ClipPathKey, _clipPathField.value, "json");
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
            string path = PlayerIoUiKit.AskLoadPath("Open UnityLimit CSV", LimitPathKey, _limitPathField.value, "csv");
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

        // ── VRMA 書き出し ─────────────────────────────────

        private void OnBrowseVrma()
        {
            string path = AskVrmaSavePath();
            if (string.IsNullOrEmpty(path)) return;
            _vrmaPathField.value = path;
        }

        // 書き出しは必ず保存ダイアログを通す。パス欄の値は初期値としてだけ使う
        //（PlayerMeshSelectionSetSubPanel の書き出しと同じ規則）。
        private string AskVrmaSavePath()
        {
            // 書き込み先はフォルダだけを覚える。ファイル名は毎回この既定から始める。
            string defName = (_clip != null && !string.IsNullOrEmpty(_clip.name)) ? _clip.name : "motion";
            return SaveDest.AskSavePath(
                "VRM アニメーションの書き出し", SaveDest.Keys.Vrma, "", defName, "vrma");
        }

        private void OnExportVrma()
        {
            if (Model == null)      { SetStatus("モデルがありません"); return; }
            if (_clip == null || string.IsNullOrEmpty(_filePath))
                                    { SetStatus("クリップを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }
            if (!PLVrmAnimationBridge.I.IsAvailable)
                                    { SetStatus("VRM パッケージが無いため書き出せません"); return; }

            string outPath = AskVrmaSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _vrmaPathField.value = outPath;

            // クリップと CSV はこのパネルのダイアログで利用者が選んだもの。
            // PLSandbox の 1 回許可は解決した時点で消える（PLSandbox.cs:224-226）ので、
            // コマンドを送る直前に付け直す。
            string limitPath = _limitPathField?.value?.Trim() ?? string.Empty;
            PLSandbox.AllowOnceFromDialog(_filePath);
            if (!string.IsNullOrEmpty(limitPath)) PLSandbox.AllowOnceFromDialog(limitPath);

            var since = DateTime.Now.AddSeconds(-2);

            SendCommand(new Poly_Ling.Data.ExportVrmAnimationCommand(
                GetModelIndex?.Invoke() ?? 0,
                outPath,
                _filePath,
                limitPath,
                _vrmaFpsField?.value   ?? 30f,
                _vrmaStartField?.value ?? 0f,
                _vrmaEndField?.value   ?? 0f,
                _vrmaScaleField?.value ?? 1f));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            SetStatus(ok
                ? $"VRMA を書き出しました: {Path.GetFileName(outPath)}"
                : "VRMA 書き出しに失敗しました（ログを参照）");
            RefreshAll();
        }

        /// <summary>
        /// 表示中のフレームをモデルへ引き直す。
        /// VRMA 書き出しがポーズ層を戻したあとに受け口から呼ばれる。
        /// 再描画は呼び出し側が行う。
        /// </summary>
        public void ReapplyCurrentFrame()
        {
            if (_clip == null || Model == null || _applier == null) return;
            _applier.ApplyFrame(Model, _clip, _currentTime);
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
        private static Button MkNavBtn(VisualElement row, string text, Action onClick) { var b = new Button(onClick) { text = text }; b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9; row.Add(b); return b; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
