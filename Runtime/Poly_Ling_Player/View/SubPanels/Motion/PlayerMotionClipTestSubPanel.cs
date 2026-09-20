// PlayerMotionClipTestSubPanel.cs
// 統合 MotionClipDTO（float 秒）のテスト用 Player サブパネル（再生専用）。
// 入力: VMD / UnityClip JSON / 統合 MotionClip JSON を読み込み、内部で
//       MotionClipDTO に変換して秒スライダーで適用する。
// 旧 PlayerVMDTestSubPanel / PlayerUnityClipTestSubPanel は比較用に残置。
//
// 仕様: 値は Unity 左手系のまま・座標変換なし（MotionClipDTO 準拠）。
//       boneName は VMD 直接適用、path/humanoid は UnityClipApplier 経路（Applier 側で分岐）。
//
// ■ PositionScale（重要）
//   MotionClipApplier.PositionScale の既定は 1。ソースが VMD の場合、値は PMX 単位
//   （およそ 10cm/unit）なので EditorState.PmxUnityRatio（既定 0.1）を設定しないと
//   位置が 10 倍になる。UnityClip JSON は既に Unity メートルなので 1 のままが正しい。
//   ApplySourceDefaults() で _sourceKind に応じて設定する。
//
// ■ 既知の問題（未対応・恒久メモ）
//   (1) 統合経路には IK が無い。MotionClipApplier に CCDIKSolver.Solve の呼び出しが
//       存在しないため、VMD を読んでも足ＩＫ・つま先ＩＫ・髪ＩＫは一切解かれない。
//       旧 VMDApplier.ApplyFrame にはある（EnableIK / _ikSolver）。
//   (2) 付与親（GrantParentIndex / GrantRate）が未評価。PMXDocument には保持されるが
//       ポーズ適用側に評価コードが無いため、腕捩・手捩などの分散が反映されない。

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
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMotionClipTestSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        /// <summary>モデルの窓口（操作経路統一計画.md E）。</summary>
        public Func<Poly_Ling.View.IModelView> GetModel;
        /// <summary>ツールの窓口。クリップの読み込み・フレーム適用は "motionClip"（MotionClipHandler）で行う。</summary>
        public Poly_Ling.Data.IToolSurface Surface;

        /// <summary>現在のモデル番号（コマンドの宛先）。</summary>
        public Func<int> GetModelIndex;

        /// <summary>コマンドの発行口（JSON 保存は ExportMotionJsonCommand を通す）。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        // ── ソース種別 ────────────────────────────────────────────────────
        private static readonly List<string> SourceChoices =
            new List<string> { "VMD", "UnityClip JSON", "統合JSON" };

        // ── 状態 ──────────────────────────────────────────────────────────
        private float            _currentTime;   // 秒
        private float            _maxTime;       // 秒
        private string           _filePath;
        private int              _sourceKind;    // 0=VMD, 1=UnityClip JSON, 2=統合JSON

        // ── UI 要素 ───────────────────────────────────────────────────────
        // UI 自動操作の ID は "motionClipTest.<下の Id>"（UiControlAttribute.cs）。
        // ボーンの一覧は読み込んだクリップに合わせて作り直す行（Rows）。
        [UiControl("model", Safety = UiSafety.ReadOnly, Description = "対象モデル")]
        private Label         _modelLabel;
        [UiControl("file", Safety = UiSafety.ReadOnly, Description = "読み込んだクリップ")]
        private Label         _fileLabel;
        [UiControl("bindPoseFile", Safety = UiSafety.ReadOnly, Description = "読み込んだバインドポーズ")]
        private Label         _bindPoseLabel;
        [UiControl("source", Description = "読み込む種類")]
        private DropdownField _sourceField;
        [UiControl("path", Description = "クリップのパス（ダイアログの初期値として使う）")]
        private TextField     _pathField;
        [UiControl("bindPosePath", Description = "バインドポーズのパス（ダイアログの初期値として使う）")]
        private TextField     _bindPathField;
        [UiControl("clear", Safety = UiSafety.Destructive, Description = "読み込んだクリップを外す")]
        private Button        _btnClear;
        [UiControl("reload", Safety = UiSafety.SafeWrite, Description = "クリップを読み直す")]
        private Button        _btnReload;
        [UiControl("open", Safety = UiSafety.UserOnly, Description = "クリップを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnOpen;
        [UiControl("openBindPose", Safety = UiSafety.UserOnly, Description = "バインドポーズを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnOpenBind;
        [UiControl("browse", Safety = UiSafety.UserOnly, Description = "クリップの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowse;
        [UiControl("browseBindPose", Safety = UiSafety.UserOnly, Description = "バインドポーズの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseBind;
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
        [UiControl("saveJson", Safety = UiSafety.UserOnly, Description = "読み込んだクリップを PolyLing モーション JSON として保存する（保存ダイアログを開く）")]
        private Button        _btnSaveJson;
        [UiControl("report", Safety = UiSafety.ReadOnly, Description = "読込の検査結果と、モデルとの結び付き状況")]
        private Label         _reportLabel;

        // 直近の読込の検査結果（統合JSON のときだけ）。

        private const string PathKey     = "MotionClip.Path";
        private const string BindPathKey = "MotionClip.Bind.Path";
        private const string SourceKey   = "MotionClip.Source";

        private Poly_Ling.View.IModelView Model => GetModel?.Invoke();
        private float FrameRate => HasClip ? (Surface?.GetFloat(Tool, "frameRate") ?? 30f) : 30f;
        /// <summary>モーションの試し再生のツールの窓口名（MotionClipHandler）。</summary>
        private const string Tool = "motionClip";
        private bool HasClip => Surface != null && Surface.GetBool(Tool, "clipLoaded");

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
            root.Add(PlayerIoUiKit.PathRow(_pathField, OnBrowse, out _btnBrowse));
            _pathField.SetValueWithoutNotify(RecentPaths.Get(PathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowse);
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

            // ── バインドポーズ行（ソース rest = 外部 UnityBone CSV v2）───────
            root.Add(PlayerIoUiKit.SectionLabel("バインドポーズ CSV（UnityBone v2）"));
            _bindPathField = new TextField();
            _bindPathField.RegisterValueChangedCallback(e => RecentPaths.Set(BindPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_bindPathField, OnBrowseBind, out _btnBrowseBind));
            _bindPathField.SetValueWithoutNotify(RecentPaths.Get(BindPathKey));

            var btnBindPose = PlayerIoUiKit.OpenButton("開く", OnBrowseBind);
            _btnOpenBind = btnBindPose;
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

            _reportLabel = new Label();
            _reportLabel.style.fontSize     = 10;
            _reportLabel.style.whiteSpace   = WhiteSpace.Normal;
            _reportLabel.style.marginBottom = 4;
            root.Add(_reportLabel);

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
                if (!HasClip) return;
                _currentTime = Mathf.Clamp(e.newValue, 0f, _maxTime);
                UpdateSlider();
                UpdateTimeLabel();
                ApplyFrame();
            });
            frameRow.Add(_timeLabel); frameRow.Add(_timeInput);
            root.Add(frameRow);

            var nav1 = new VisualElement(); nav1.style.flexDirection = FlexDirection.Row; nav1.style.marginBottom = 2;
            _btnTimeFirst = MkNavBtn(nav1, "|◀",  () => { _currentTime = 0; Sync(); });
            _btnTimePrev  = MkNavBtn(nav1, "◀1", () => { if (HasClip) { _currentTime = Mathf.Max(0f, _currentTime - Step()); Sync(); } });
            _btnTime25    = MkNavBtn(nav1, "25%", () => { if (HasClip) { _currentTime = _maxTime * 0.25f; Sync(); } });
            _btnTime50    = MkNavBtn(nav1, "50%", () => { if (HasClip) { _currentTime = _maxTime * 0.5f;  Sync(); } });
            _btnTime75    = MkNavBtn(nav1, "75%", () => { if (HasClip) { _currentTime = _maxTime * 0.75f; Sync(); } });
            _btnTimeNext  = MkNavBtn(nav1, "1▶", () => { if (HasClip) { _currentTime = Mathf.Min(_maxTime, _currentTime + Step()); Sync(); } });
            _btnTimeLast  = MkNavBtn(nav1, "▶|", () => { if (HasClip) { _currentTime = _maxTime; Sync(); } });
            root.Add(nav1);

            var resetBtn = new Button(() => Surface?.Invoke(Tool, "resetPose")) { text = "ポーズリセット" };
            resetBtn.style.marginBottom = 4;
            root.Add(resetBtn);
            _btnResetPose = resetBtn;

            _btnSaveJson = new Button(OnSaveJson) { text = "JSON保存" };
            _btnSaveJson.style.marginBottom = 4;
            root.Add(_btnSaveJson);

            root.Add(SecLabel("オプション"));

            var scaleRow = new VisualElement();
            scaleRow.style.flexDirection = FlexDirection.Row;
            scaleRow.style.marginBottom  = 6;
            var scaleLbl = new Label("PositionScale");
            scaleLbl.style.width = 90; scaleLbl.style.fontSize = 10;
            scaleLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _scaleField = new FloatField { value = Surface?.GetFloat(Tool, "positionScale") ?? 1f };
            _scaleField.style.flexGrow = 1;
            _scaleField.RegisterValueChangedCallback(e =>
            {
                Surface?.Invoke(Tool, "setPositionScale", ("scale", e.newValue), ("time", _currentTime));
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
                    ? $"✓ {model.Name}  ({model.BoneCount} bones)"
                    : "(No model loaded)";

            if (_fileLabel != null)
                _fileLabel.text = string.IsNullOrEmpty(_filePath) ? "(None)" : Path.GetFileName(_filePath);
            if (_btnClear  != null) _btnClear.SetEnabled(HasClip);
            if (_btnReload != null) _btnReload.SetEnabled(!string.IsNullOrEmpty(_filePath));

            if (_clipSection == null) return;
            bool hasClip = HasClip;
            _clipSection.style.display = hasClip ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasClip) return;

            int trackCount = Surface.GetInt(Tool, "trackTotal");
            var counts = Surface.Get(Tool, "trackCounts", Array.Empty<int>());
            int C(int i) => i < counts.Length ? counts[i] : 0;
            if (_clipInfoLabel != null)
                _clipInfoLabel.text =
                    $"Clip: {Surface.GetString(Tool, "clipName")}\n" +
                    $"Length: {_maxTime:F2}s  (@ {FrameRate:F0}fps)\n" +
                    $"Bone: {C(0)}  Baked: {C(1)}  " +
                    $"Muscle: {C(2)}  Expr: {C(3)}";

            if (_clipMatchLabel != null && model != null)
            {
                int matched = Surface.GetInt(Tool, "matchedTrackCount");
                float rate = trackCount > 0 ? (float)matched / trackCount : 0f;
                _clipMatchLabel.text = $"Matched: {matched}/{trackCount} ({rate:P0})";
            }

            if (_reportLabel != null)
                _reportLabel.text = Surface.GetString(Tool, "reportText");

            UpdateSlider();
            UpdateTimeLabel();
            RefreshBoneList();
        }

        private void RefreshBoneList()
        {
            if (_boneListContainer == null || !HasClip) return;
            _boneListContainer.Clear();

            var lines   = Surface.Get(Tool, "trackLines", Array.Empty<string>());
            var matched = Surface.Get(Tool, "trackMatched", Array.Empty<bool>());
            int total   = Surface.GetInt(Tool, "trackTotal");
            for (int i = 0; i < lines.Length; i++)
            {
                bool ok = i < matched.Length && matched[i];
                var lbl = new Label(lines[i]);
                lbl.style.fontSize = 10;
                lbl.style.color    = new StyleColor(ok ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.8f, 0.4f, 0.4f));
                _boneListContainer.Add(lbl);
            }
            int rem = total - 50;
            if (rem > 0) { var l = new Label($"  ...他 {rem} トラック"); l.style.fontSize = 9; _boneListContainer.Add(l); }
            if (_boneListFoldout != null) _boneListFoldout.text = $"Tracks ({total})";
        }

        // ================================================================
        // 操作
        // ================================================================

        // 「開く」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowse()
        {
            string ext  = _sourceKind == 0 ? "vmd" : "json";
            string path = PlayerIoUiKit.AskLoadPath("Open Motion", PathKey, _pathField.value, ext);
            if (string.IsNullOrEmpty(path)) return;
            _pathField.value = path;
            Load(path);
        }

        private void Load(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            // 読み込み・対応付け・フレーム適用はツールの窓口 "motionClip" が本体側で行う。
            Surface?.Invoke(Tool, "load", ("path", path), ("sourceKind", _sourceKind), ("time", 0f));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"読込み失敗: {err}"); return; }

            _filePath    = path;
            _currentTime = 0f;
            _maxTime     = Surface.GetFloat(Tool, "clipLength");
            _scaleField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "positionScale"));
            SetStatus($"読込み完了: {Path.GetFileName(path)}");
            RefreshAll();
        }

        // ソース種別に応じて PositionScale の既定値を設定する。
        //
        //   0 = VMD          : 値は PMX 単位。EditorState.PmxUnityRatio（既定 0.1）を掛ける。
        //                      未設定だと位置が 10 倍になる（旧 PlayerVMDTestSubPanel は
        //                      読込時に同じ設定を行っている）。
        //   1 = UnityClip    : 値は既に Unity メートル。1 のままが正しい。
        //   2 = 統合JSON     : 生成元の単位系を DTO から判別できないため 1 を既定とする。
        //                      VMD 由来の統合 JSON を読む場合は手動で PositionScale を
        //                      0.1 に変更すること。※ MotionClipDTO に単位系フィールドが
        //                      無いことが根本原因。将来は DTO 側へ持たせて自動判別する。
        //
        // 注意: MotionClipApplier.PositionScale は boneName トラック（自前適用）と
        //       path/humanoid トラック（UnityClipApplier へ委譲）の両方に同じ値が掛かる。
        //       混在クリップでは単位系を揃えてから読み込むこと。
        // 位置の倍率の既定値（VMD は PmxUnityRatio、他は 1）と読み込みは MotionClipHandler.Load が行う。
        // 注意: MotionClipApplier.PositionScale は boneName トラック（自前適用）と
        //       path/humanoid トラック（UnityClipApplier へ委譲）の両方に同じ値が掛かる。
        //       混在クリップでは単位系を揃えてから読み込むこと。
        //       統合 JSON は生成元の単位系を DTO から判別できないため 1 を既定とする
        //       （MotionClipDTO に単位系フィールドが無いことが根本原因）。

        // 「JSON保存」: 読み込んだ元ファイルを ExportMotionJsonCommand で変換・書き出す。
        // パネル内の DTO は元ファイルから決まるので、コマンドの結果と同じものになる。
        private void OnSaveJson()
        {
            if (!HasClip || string.IsNullOrEmpty(_filePath)) { SetStatus("クリップを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string defName = Surface?.GetString(Tool, "clipName");
            if (string.IsNullOrEmpty(defName)) defName = Path.GetFileNameWithoutExtension(_filePath);
            string outPath = SaveDest.AskSavePath(
                "PolyLing モーション JSON の保存", SaveDest.Keys.Motion, "", defName + ".plmotion.json", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            // 元ファイルはこのパネルのダイアログで利用者が選んだもの。1 回許可を付け直す。
            PLSandbox.AllowOnceFromDialog(_filePath);

            var since = DateTime.Now.AddSeconds(-2);
            var kind  = _sourceKind == 0 ? Poly_Ling.Data.MotionSourceKind.Vmd
                      : _sourceKind == 1 ? Poly_Ling.Data.MotionSourceKind.UnityClipJson
                      :                    Poly_Ling.Data.MotionSourceKind.PolyLingMotionJson;

            SendCommand(new Poly_Ling.Data.ExportMotionJsonCommand(GetModelIndex?.Invoke() ?? 0, outPath, _filePath, kind));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            if (!ok) { SetStatus("保存に失敗しました。理由はコンソールログに出ています"); return; }

            var check = MotionClipSerializer.Load(outPath);
            SetStatus(check.Success
                ? $"保存しました: {Path.GetFileName(outPath)}（警告 {check.WarningCount}）"
                : $"保存したファイルを読み戻せません: {check.FormatIssues(3)}");
        }

        // 外部 UnityBone CSV v2（ソース rest）を読み、リターゲット経路を有効化。
        private void OnBrowseBind()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "Open UnityBone CSV (bind pose)", BindPathKey, _bindPathField.value, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _bindPathField.value = path;
            LoadBind(path);
        }

        private void LoadBind(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            Surface?.Invoke(Tool, "loadBind", ("path", path), ("time", _currentTime));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"バインドポーズ読込失敗: {err}"); return; }

            int n = Surface.GetInt(Tool, "bindBoneCount");
            if (_bindPoseLabel != null)
                _bindPoseLabel.text = n > 0 ? $"✓ {Path.GetFileName(path)} ({n} bones)" : "(0 bones)";
            SetStatus(n > 0
                ? $"バインドポーズ読込: {n} bones（リターゲット有効）"
                : "バインドポーズ: Humanoid 行が見つかりません");
            RefreshAll();
        }

        private void Clear()
        {
            Surface?.Invoke(Tool, "clear");
            _filePath = null; _currentTime = 0f; _maxTime = 0f;
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath;
            float  time = _currentTime;
            Clear();

            Surface?.Invoke(Tool, "load", ("path", path), ("sourceKind", _sourceKind), ("time", 0f));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"再読込み失敗: {err}"); return; }

            _filePath    = path;
            _maxTime     = Surface.GetFloat(Tool, "clipLength");
            _currentTime = Mathf.Clamp(time, 0f, _maxTime);
            _scaleField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "positionScale"));
            ApplyFrame();
            RefreshAll();
        }

        private void ApplyFrame()
        {
            if (!HasClip || Model == null) return;
            Surface?.Invoke(Tool, "setTime", ("time", _currentTime));
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
            if (_timeSlider == null || !HasClip) return;
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

        // 長さの正本は MotionClipSerializer.Length（duration とキー時刻の最大値の大きい方）。
        private static float ComputeMaxTime(MotionClipDTO dto) => MotionClipSerializer.Length(dto);

        private static int ParseInt(string s, int fallback)
        {
            return int.TryParse(s, out int v) ? v : fallback;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Button MkNavBtn(VisualElement row, string text, Action onClick) { var b = new Button(onClick) { text = text }; b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9; row.Add(b); return b; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
