// PlayerMotionClipTestSubPanel.cs
// モーション（MotionClipDTO・float 秒）の試し再生と VRMA 書き出しの Player サブパネル。
// 入力: VMD / PolyLing モーション（.plmotion.json）/ VRMA / BVH / FBX を読み込み、内部で
//       MotionClipDTO に変換して秒スライダーで適用する。FBX はレスト用 FBX とテイク番号を指定できる。
// 出力: JSON保存（.plmotion.json）、VRMA、BVH（モデルの骨格）、FBX（モデル＋モーション）。
// 可動域 CSV（UnityLimit）を読むと、マッスルをその実測で再構成する（ベイク元アバターの limits.csv）。
// VRMA 書き出しは PolyLing モーションを読んだときだけ（ConvertMotionToVrmaCommand。モデルは使わない）。
// .vrma の読込はソース種別「VRM アニメーション」、.plmotion への変換は「JSON保存」。
// 旧 PlayerVMDTestSubPanel は比較用に残置。
//
// 仕様: 値は Unity 左手系のまま・座標変換なし（MotionClipDTO 準拠）。
//       boneName は VMD 直接適用、path・マッスルは UnityClipApplier 経路（Applier 側で分岐）。
//       本体ボーンは rotationSource が "bakedBones"（BVH・FBX・VRMA の読込）なら bakedBones、
//       そうでなければマッスルで当てる（MotionClipApplier）。
//
// ■ PositionScale（重要）
//   MotionClipApplier.PositionScale の既定は 1。ソースが VMD の場合、値は PMX 単位
//   （およそ 10cm/unit）なので EditorState.PmxUnityRatio（既定 0.1）を設定しないと
//   位置が 10 倍になる。Unity クリップから書き出したモーションは既に Unity メートルなので 1 のままが正しい。
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
using Poly_Ling.Motion;
using Poly_Ling.Data;
using Poly_Ling.Vrm;

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
        // 並びは MotionSourceKind の値と同じ（0=VMD / 1=PolyLing モーション / 2=VRM アニメーション / 3=BVH / 4=FBX）。
        private static readonly List<string> SourceChoices =
            new List<string> { "VMD", "PolyLing モーション", "VRM アニメーション (.vrma)", "BVH (.bvh)", "FBX (.fbx)" };
        private const int SourceFbx = (int)Poly_Ling.Data.MotionSourceKind.Fbx;

        // ── 状態 ──────────────────────────────────────────────────────────
        private float            _currentTime;   // 秒
        private float            _maxTime;       // 秒
        private int              _sourceKind;    // 読み込む種類（ドロップダウン。MotionSourceKind の値）

        // 読み込んだクリップの元ファイルと種類は窓口 motionClip（MotionClipHandler）だけが持つ。
        //   パネルは自分で持たずに読む（外から受けたクリップを saveMotionClipJson で保存すると
        //   ここにも反映される。PolyLing_Blender不具合修正計画.md の B）。
        private string _filePath   => Surface?.GetString(Tool, "sourcePath") ?? "";
        private int    _loadedKind => Surface?.GetInt(Tool, "sourceKind") ?? 0;

        // ── ボタンや入力欄 ───────────────────────────────────────────────────────
        // UI 自動操作の ID は "motionClipTest.<下の Id>"（UiControlAttribute.cs）。
        // ボーンの一覧は読み込んだクリップに合わせて作り直す行（Rows）。
        [UiControl("model", Safety = UiSafety.ReadOnly, Description = "対象モデル")]
        private Label         _modelLabel;
        [UiControl("file", Safety = UiSafety.ReadOnly, Description = "読み込んだクリップ")]
        private Label         _fileLabel;
        [UiControl("source", Description = "読み込む種類")]
        private DropdownField _sourceField;
        [UiControl("path", Description = "クリップのパス（ダイアログの初期値として使う）")]
        private TextField     _pathField;
        [UiControl("clear", Safety = UiSafety.Destructive, Description = "読み込んだクリップを外す")]
        private Button        _btnClear;
        [UiControl("reload", Safety = UiSafety.SafeWrite, Description = "クリップを読み直す")]
        private Button        _btnReload;
        [UiControl("open", Safety = UiSafety.UserOnly, Description = "クリップを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnOpen;
        [UiControl("browse", Safety = UiSafety.UserOnly, Description = "クリップの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowse;
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
        [UiControl("absentMuscles", Description = "動かすボーンのうちトラックの無い方向の値（0 / T ポーズの値）。切り替えると再生に反映し、JSON 保存でファイルに書く")]
        private DropdownField _absentField;
        private static readonly List<string> AbsentChoices =
            new List<string> { "0（マッスル 0）", "T ポーズの値" };
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
        [UiControl("play", Safety = UiSafety.SafeWrite, Description = "再生／停止（今の時刻から末尾まで実時間で進める。ライブ受信の受け入れ中は配信する）")]
        private Button        _btnPlay;

        // 再生（今の時刻から末尾まで。schedule の一発予約を再生中だけ繰り返す＝毎フレームの見回りはしない）
        private bool   _playing;
        private double _playStartReal;   // 再生開始時の実時間 [秒]
        private float  _playStartTime;   // 再生開始時のクリップ時刻 [秒]
        [UiControl("saveJson", Safety = UiSafety.UserOnly, Description = "読み込んだクリップを PolyLing モーション JSON として保存する（保存ダイアログを開く）")]
        private Button        _btnSaveJson;
        [UiControl("report", Safety = UiSafety.ReadOnly, Description = "読込の検査結果と、モデルとの結び付き状況")]
        private Label         _reportLabel;

        // 可動域 CSV（UnityLimit）
        [UiControl("limitFile", Safety = UiSafety.ReadOnly, Description = "読み込んだ可動域ファイル")]
        private Label         _limitLabel;
        [UiControl("limitPath", Description = "可動域ファイルのパス（ダイアログの初期値として使う）")]
        private TextField     _limitPathField;
        [UiControl("limitOpen", Safety = UiSafety.UserOnly, Description = "可動域ファイルを開く（ファイル選択ダイアログを開く）")]
        private Button        _btnLimitOpen;
        [UiControl("browseLimit", Safety = UiSafety.UserOnly, Description = "可動域ファイルの [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseLimit;
        [UiControl("limitClear", Safety = UiSafety.Destructive, Description = "読み込んだ可動域を外す")]
        private Button        _btnLimitClear;

        // VRMA 書き出し
        [UiControl("vrma.path", Description = "VRMA の書き出し先（ダイアログの初期値として使う）")]
        private TextField     _vrmaPathField;
        [UiControl("vrma.browse", Safety = UiSafety.UserOnly, Description = "VRMA の [...]（保存ダイアログを開く）")]
        private Button        _btnBrowseVrma;
        [UiControl("vrma.fps", Description = "VRMA 書き出しのフレームレート（0 以下ならクリップの frameRate）")]
        private FloatField    _vrmaFpsField;
        [UiControl("vrma.startTime", Description = "VRMA 書き出しの開始時刻")]
        private FloatField    _vrmaStartField;
        [UiControl("vrma.endTime", Description = "VRMA 書き出しの終了時刻")]
        private FloatField    _vrmaEndField;
        [UiControl("vrma.export", Safety = UiSafety.UserOnly, Description = "VRMA を書き出す（保存ダイアログを開く）")]
        private Button        _btnVrmaExport;
        [UiControl("vrma.status", Safety = UiSafety.ReadOnly, Description = "VRMA 書き出しの状態")]
        private Label         _vrmaLabel;

        // BVH 書き出し（モデルの骨格）
        [UiControl("bvh.path", Description = "BVH の書き出し先（ダイアログの初期値として使う）")]
        private TextField     _bvhPathField;
        [UiControl("bvh.browse", Safety = UiSafety.UserOnly, Description = "BVH の [...]（保存ダイアログを開く）")]
        private Button        _btnBrowseBvh;
        [UiControl("bvh.fps", Description = "BVH 書き出しのフレームレート（0 以下ならクリップの frameRate）")]
        private FloatField    _bvhFpsField;
        [UiControl("bvh.scale", Description = "BVH 書き出しでモデルの長さに掛ける倍率")]
        private FloatField    _bvhScaleField;
        [UiControl("bvh.export", Safety = UiSafety.UserOnly, Description = "読み込んだモーションを今のモデルへ当てて、モデルの骨格の BVH を書き出す（保存ダイアログを開く）")]
        private Button        _btnBvhExport;
        [UiControl("bvh.status", Safety = UiSafety.ReadOnly, Description = "BVH 書き出しの状態")]
        private Label         _bvhLabel;

        // FBX 書き出し（モデル＋モーション）
        [UiControl("fbx.path", Description = "FBX の書き出し先（ダイアログの初期値として使う）")]
        private TextField     _fbxPathField;
        [UiControl("fbx.browse", Safety = UiSafety.UserOnly, Description = "FBX の [...]（保存ダイアログを開く）")]
        private Button        _btnBrowseFbx;
        [UiControl("fbx.fps", Description = "FBX 書き出しのフレームレート（0 以下ならクリップの frameRate）")]
        private FloatField    _fbxFpsField;
        [UiControl("fbx.includeMesh", Description = "FBX にメッシュ・材質・スキンも書く（切ると骨とモーションだけ）")]
        private Toggle        _fbxIncludeMeshToggle;
        [UiControl("fbx.export", Safety = UiSafety.UserOnly, Description = "読み込んだモーションを今のモデルへ当てて、モデルとモーションの FBX を書き出す（保存ダイアログを開く）")]
        private Button        _btnFbxExport;
        [UiControl("fbx.status", Safety = UiSafety.ReadOnly, Description = "FBX 書き出しの状態")]
        private Label         _fbxLabel;

        // FBX を読むときの設定（ソース種別が FBX のときだけ出す）
        [UiControl(Ignore = true)]
        private VisualElement _fbxReadSection;
        [UiControl("fbxRest.path", Description = "レスト（T ポーズ）として使う同じ骨格のモデル FBX。空なら読み込むファイル自身のバインド姿勢")]
        private TextField     _fbxRestPathField;
        [UiControl("fbxRest.browse", Safety = UiSafety.UserOnly, Description = "レスト用 FBX の [...]（ファイル選択ダイアログを開く）")]
        private Button        _btnBrowseFbxRest;
        [UiControl("fbxRest.clear", Safety = UiSafety.SafeWrite, Description = "レスト用 FBX の指定を外す")]
        private Button        _btnClearFbxRest;
        [UiControl("fbxTake", Description = "読むテイク（アニメーションスタック）の番号。0 から")]
        private IntegerField  _fbxTakeField;

        // ライブ受信（ツールの窓口 "motionLive"）
        [UiControl("alignLegs", Description = "T ポーズ合わせを脚（太もも・すね）にも掛ける。切は腕だけ。再生・BVH 書き出し・ライブ受信に効く")]
        private Toggle        _alignLegsToggle;
        private const string  AlignLegsKey = "MotionClip.AlignLegs";

        [UiControl("live.port", Description = "ライブ受信の待ち受けポート（WebSocket。送る側も画面も同じポートへつなぐ）")]
        private IntegerField  _livePortField;
        [UiControl("live.lan", Description = "ライブ受信を LAN 上の別の PC からも受ける（オフは同じ PC の中だけ）")]
        private Toggle        _liveLanToggle;
        [UiControl("live.accept", Safety = UiSafety.SafeWrite, Description = "ライブ受信の受け入れを許可する（待ち受け開始）")]
        private Button        _btnLiveAccept;
        [UiControl("live.reject", Safety = UiSafety.SafeWrite, Description = "ライブ受信の受け入れを不許可にする（待ち受け停止）")]
        private Button        _btnLiveReject;
        [UiControl("live.resetPose", Safety = UiSafety.SafeWrite, Description = "ライブ受信で当てた姿勢を初期へ戻す")]
        private Button        _btnLiveResetPose;
        [UiControl("live.status", Safety = UiSafety.ReadOnly, Description = "ライブ受信の状態")]
        private Label         _liveStatusLabel;

        // 直近の読込の検査結果（統合JSON のときだけ）。

        private const string PathKey     = "MotionClip.Path";
        private const string SourceKey   = "MotionClip.Source";
        private const string LimitPathKey = "MotionClip.Limit.Path";
        private const string LivePortKey  = "MotionClip.Live.WsPort";
        private const string LiveLanKey   = "MotionClip.Live.Lan";
        /// <summary>ライブ受信のツールの窓口名（MotionLiveHandler）。</summary>
        private const string LiveTool = "motionLive";

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
                RefreshVrmaSection();
                RefreshFbxReadSection();
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

            // ── FBX を読むときの設定（ソース種別が FBX のときだけ）──────────
            BuildFbxReadSection(root);

            // ── 可動域 CSV（UnityLimit。ベイク元アバターの limits.csv）──────
            root.Add(PlayerIoUiKit.SectionLabel("可動域 CSV（UnityLimit）"));
            _limitPathField = new TextField();
            _limitPathField.RegisterValueChangedCallback(e => RecentPaths.Set(LimitPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_limitPathField, OnBrowseLimit, out _btnBrowseLimit));
            _limitPathField.SetValueWithoutNotify(RecentPaths.Get(LimitPathKey));

            var limitRow = new VisualElement();
            limitRow.style.flexDirection = FlexDirection.Row;
            limitRow.style.marginBottom  = 3;
            var btnLimitOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseLimit);
            _btnLimitOpen = btnLimitOpen;
            btnLimitOpen.style.flexGrow = 1; btnLimitOpen.style.marginRight = 2;
            _btnLimitClear = new Button(ClearLimit) { text = "クリア" }; _btnLimitClear.style.width = 52;
            limitRow.Add(btnLimitOpen); limitRow.Add(_btnLimitClear);
            root.Add(limitRow);
            _limitLabel = new Label("(未読込)");
            _limitLabel.style.fontSize = 10;
            _limitLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _limitLabel.style.marginBottom = 3;
            root.Add(_limitLabel);

            // ── T ポーズ合わせの範囲（再生・BVH 書き出し・ライブ受信で共通）──────
            _alignLegsToggle = new Toggle("脚も T ポーズに合わせる（切は腕だけ）")
            {
                value = RecentPaths.Get(AlignLegsKey) == "1",
            };
            _alignLegsToggle.style.fontSize = 10; _alignLegsToggle.style.marginBottom = 4;
            _alignLegsToggle.RegisterValueChangedCallback(e =>
            {
                RecentPaths.Set(AlignLegsKey, e.newValue ? "1" : "0");
                PushAlignLegs();
                SetStatus(e.newValue ? "T ポーズ合わせ：腕＋脚" : "T ポーズ合わせ：腕だけ");
            });
            root.Add(_alignLegsToggle);
            PushAlignLegs();

            // ── ライブ受信（UDP でマッスルを受けて当てる）────────────────
            BuildLiveSection(root);

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

        // チェックの値を再生（motionClip）とライブ受信（motionLive）の両方へ渡す。
        private void PushAlignLegs()
        {
            bool on = _alignLegsToggle != null && _alignLegsToggle.value;
            Surface?.Invoke(Tool, "setAlignLegs", ("enabled", on), ("time", _currentTime));
            Surface?.Invoke(LiveTool, "setAlignLegs", ("enabled", on));
        }

        private void BuildLiveSection(VisualElement root)
        {
            root.Add(PlayerIoUiKit.SectionLabel("ライブ受信（WebSocket・マッスル）"));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;
            var portLbl = new Label("ポート");
            portLbl.style.width = 40; portLbl.style.fontSize = 10;
            portLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _livePortField = new IntegerField
            {
                value = ParseInt(RecentPaths.Get(LivePortKey), MotionLiveHandler.DefaultPort),
            };
            _livePortField.style.width = 70; _livePortField.style.marginRight = 2;
            _livePortField.RegisterValueChangedCallback(e => RecentPaths.Set(LivePortKey, e.newValue.ToString()));
            _btnLiveAccept = new Button(() =>
            {
                Surface?.Invoke(LiveTool, "accept", ("port", _livePortField.value), ("lan", _liveLanToggle.value));
                RefreshLiveStatus();
            }) { text = "受け入れ許可" };
            _btnLiveAccept.style.flexGrow = 1; _btnLiveAccept.style.marginRight = 2;
            _btnLiveReject = new Button(() =>
            {
                Surface?.Invoke(LiveTool, "reject");
                RefreshLiveStatus();
            }) { text = "不許可" };
            _btnLiveReject.style.width = 52;
            row.Add(portLbl); row.Add(_livePortField); row.Add(_btnLiveAccept); row.Add(_btnLiveReject);
            root.Add(row);

            _liveLanToggle = new Toggle("LAN 上の別の PC からも受ける")
            {
                value = RecentPaths.Get(LiveLanKey) == "1",
            };
            _liveLanToggle.style.fontSize = 10; _liveLanToggle.style.marginBottom = 3;
            _liveLanToggle.RegisterValueChangedCallback(e => RecentPaths.Set(LiveLanKey, e.newValue ? "1" : "0"));
            root.Add(_liveLanToggle);

            _btnLiveResetPose = new Button(() => Surface?.Invoke(LiveTool, "resetPose")) { text = "ポーズリセット（ライブ）" };
            _btnLiveResetPose.style.marginBottom = 3;
            root.Add(_btnLiveResetPose);

            _liveStatusLabel = new Label();
            _liveStatusLabel.style.fontSize     = 10;
            _liveStatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _liveStatusLabel.style.marginBottom = 4;
            root.Add(_liveStatusLabel);
        }

        /// <summary>ライブ受信の状態表示を更新する（許可・不許可の操作時と、フレームを当てるたびに呼ぶ）。</summary>
        public void RefreshLiveStatus()
        {
            if (_liveStatusLabel == null || Surface == null) return;
            bool listening = Surface.GetBool(LiveTool, "listening");
            string err = Surface.GetString(LiveTool, "error");
            string text = listening
                ? $"待ち受け中（ws://{(Surface.GetBool(LiveTool, "lan") ? "<この PC の IP>" : "localhost")}:{Surface.GetInt(LiveTool, "port")}/ 接続 {Surface.GetInt(LiveTool, "clientCount")}）\n" +
                  $"受信 {Surface.GetInt(LiveTool, "receivedCount")} / 適用 {Surface.GetInt(LiveTool, "appliedCount")} / 破棄 {Surface.GetInt(LiveTool, "rejectedCount")} / 転送見送り {Surface.GetInt(LiveTool, "skippedSendCount")}\n" +
                  $"送信元 {Surface.GetString(LiveTool, "lastSender")}\n" +
                  $"MediaPipe 受信 {Surface.GetInt(LiveTool, "mediaPipeCount")} / 保持中の連番 {Surface.GetInt(LiveTool, "mediaPipeSeq")} / 部位 {Surface.GetString(LiveTool, "mediaPipeParts")}"
                : "停止中";
            string rej = Surface.GetString(LiveTool, "lastReject");
            if (listening && !string.IsNullOrEmpty(rej)) text += $"\n最後に捨てた理由: {rej}";
            if (!string.IsNullOrEmpty(err)) text += $"\n{err}";
            _liveStatusLabel.text = text;
            _btnLiveAccept?.SetEnabled(!listening);
            _btnLiveReject?.SetEnabled(listening);
            _livePortField?.SetEnabled(!listening);
            _liveLanToggle?.SetEnabled(!listening);
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

            _btnPlay = new Button(TogglePlay) { text = "▶ 再生" };
            _btnPlay.style.marginBottom = 2;
            root.Add(_btnPlay);

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

            // トラックの無い dof の値（absentMuscles）。JSON 保存でファイルにも書く。
            _absentField = new DropdownField("無い dof の値", AbsentChoices, 0);
            _absentField.style.marginBottom = 6;
            _absentField.RegisterValueChangedCallback(e =>
            {
                string mode = AbsentChoices.IndexOf(e.newValue) == 1 ? "rest" : "zero";
                Surface?.Invoke(Tool, "setAbsentMuscles", ("mode", mode), ("time", _currentTime));
                string err = Surface?.GetString(Tool, "error") ?? "";
                SetStatus(string.IsNullOrEmpty(err) ? $"トラックの無い dof: {e.newValue}" : err);
            });
            root.Add(_absentField);

            BuildVrmaSection(root);
            BuildBvhSection(root);
            BuildFbxSection(root);

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
            if (_btnLimitClear != null) _btnLimitClear.SetEnabled((Surface?.GetInt(Tool, "limitBoneCount") ?? 0) > 0);
            if (_absentField != null)
            {
                _absentField.SetEnabled(HasClip);
                if (HasClip)
                    _absentField.SetValueWithoutNotify(
                        AbsentChoices[Surface.GetString(Tool, "absentMuscles") == "rest" ? 1 : 0]);
            }
            RefreshLiveStatus();

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

            RefreshVrmaSection();

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
            string ext  = _sourceKind == 0 ? "vmd" : _sourceKind == 2 ? "vrma" : _sourceKind == 3 ? "bvh"
                        : _sourceKind == SourceFbx ? "fbx" : "json";
            string path = PlayerIoUiKit.AskLoadPath("Open Motion", PathKey, _pathField.value, ext);
            if (string.IsNullOrEmpty(path)) return;
            _pathField.value = path;
            Load(path);
        }

        private void Load(string path)
        {
            StopPlay();
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            // 読み込み・対応付け・フレーム適用はツールの窓口 "motionClip" が本体側で行う。
            Surface?.Invoke(Tool, "load", ("path", path), ("sourceKind", _sourceKind), ("time", 0f),
                ("fbxRestPath", FbxRestPath ?? ""), ("fbxTake", FbxTake));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"読込み失敗: {err}"); return; }

            _currentTime = 0f;
            _maxTime     = Surface.GetFloat(Tool, "clipLength");
            _scaleField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "positionScale"));
            SetStatus($"読込み完了: {Path.GetFileName(path)}");
            RefreshAll();
        }

        /// <summary>
        /// パネルを通さずに窓口 motionClip へ読み込まれたクリップ（コマンド loadMotionClipJson。Blender ブリッジの送信）に
        /// 表示を合わせる。元ファイルが無いので、JSON 保存（窓口のクリップを書く）までは読み直し・VRMA・BVH・FBX 書き出しは使えない。
        /// </summary>
        public void AdoptExternalClip(float time)
        {
            StopPlay();
            _maxTime     = Surface?.GetFloat(Tool, "clipLength") ?? 0f;
            _currentTime = Mathf.Clamp(time, 0f, _maxTime);
            if (Surface != null) _scaleField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "positionScale"));
            SetStatus($"外から受けたクリップ: {Surface?.GetString(Tool, "clipName")}");
            RefreshAll();
        }

        // ソース種別に応じて PositionScale の既定値を設定する。
        //
        //   0 = VMD          : 値は PMX 単位。EditorState.PmxUnityRatio（既定 0.1）を掛ける。
        //                      未設定だと位置が 10 倍になる（旧 PlayerVMDTestSubPanel は
        //                      読込時に同じ設定を行っている）。
        //   1 = PolyLing モーション : 生成元の単位系を DTO から判別できないため 1 を既定とする。
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
            if (!HasClip) { SetStatus("クリップを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string defName = Surface?.GetString(Tool, "clipName");
            if (string.IsNullOrEmpty(defName) && !string.IsNullOrEmpty(_filePath)) defName = Path.GetFileNameWithoutExtension(_filePath);
            if (string.IsNullOrEmpty(defName)) defName = "motion";
            string outPath = SaveDest.AskSavePath(
                "PolyLing モーション JSON の保存", SaveDest.Keys.Motion, "", defName + ".plmotion.json", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            // 元ファイルの無いクリップ（外から受けたもの）は、窓口のクリップをそのまま書き、
            // そのファイルを元ファイルにする（SaveMotionClipJsonCommand）。
            if (string.IsNullOrEmpty(_filePath))
            {
                var since0 = DateTime.Now.AddSeconds(-2);
                SendCommand(new Poly_Ling.Data.SaveMotionClipJsonCommand(GetModelIndex?.Invoke() ?? 0, outPath));
                bool ok0 = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since0;
                SetStatus(ok0 ? $"保存しました: {Path.GetFileName(outPath)}（このファイルを元ファイルにしました）"
                              : "保存に失敗しました。理由はコンソールログに出ています");
                RefreshAll();
                return;
            }

            // 元ファイルはこのパネルのダイアログで利用者が選んだもの。1 回許可を付け直す。
            PLSandbox.AllowOnceFromDialog(_filePath);

            var since = DateTime.Now.AddSeconds(-2);
            var kind  = (Poly_Ling.Data.MotionSourceKind)_loadedKind;   // 読み込んだクリップの種類（窓口の状態）

            string absent = Surface?.GetString(Tool, "absentMuscles");
            SendCommand(new Poly_Ling.Data.ExportMotionJsonCommand(GetModelIndex?.Invoke() ?? 0, outPath, _filePath, kind,
                string.IsNullOrEmpty(absent) ? null : absent,
                fbxRestPath: AllowFbxRestPath(), fbxTake: FbxTake));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            if (!ok) { SetStatus("保存に失敗しました。理由はコンソールログに出ています"); return; }

            var check = MotionClipSerializer.Load(outPath);
            SetStatus(check.Success
                ? $"保存しました: {Path.GetFileName(outPath)}（警告 {check.WarningCount}）"
                : $"保存したファイルを読み戻せません: {check.FormatIssues(3)}");
        }

        // ── 可動域 CSV ────────────────────────────────────────────────
        private void OnBrowseLimit()
        {
            string path = PlayerIoUiKit.AskLoadPath("Open UnityLimit CSV", LimitPathKey, _limitPathField.value, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _limitPathField.value = path;
            LoadLimit(path);
        }

        private void LoadLimit(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))        { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            Surface?.Invoke(Tool, "loadLimit", ("path", path), ("time", _currentTime));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"可動域読込失敗: {err}"); return; }

            int n = Surface.GetInt(Tool, "limitBoneCount");
            bool measured = Surface.GetBool(Tool, "limitMeasured");
            if (_limitLabel != null)
                _limitLabel.text = n > 0 ? $"✓ {Path.GetFileName(path)} ({n} bones{(measured ? " / 実測あり" : "")})" : "(0 bones)";
            SetStatus(n > 0 ? $"可動域読込: {n} bones" : "可動域: 有効な行が見つかりません");
            RefreshAll();
        }

        // 別モデルの CSV を誤って読んだときの復帰口。パス欄と記憶パスも消す。
        private void ClearLimit()
        {
            Surface?.Invoke(Tool, "clearLimit", ("time", _currentTime));
            _limitPathField?.SetValueWithoutNotify(string.Empty);
            RecentPaths.Set(LimitPathKey, string.Empty);
            if (_limitLabel != null) _limitLabel.text = "(未読込)";
            SetStatus("可動域をクリアしました（既定値を使用）");
            RefreshAll();
        }

        // ── VRMA 書き出し ─────────────────────────────────────────────
        //   読み込んだ PolyLing モーションを ConvertMotionToVrmaCommand（VrmaCodec）で .vrma にする。
        //   モデルは使わない。出るのは Humanoid 骨の回転・Hips の位置・表情・視線。二次骨は VRMA に載らない。
        private void BuildVrmaSection(VisualElement root)
        {
            root.Add(SecLabel("VRM アニメーション書き出し（.vrma）"));

            _vrmaPathField = new TextField();
            _vrmaPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VrmaPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_vrmaPathField, OnBrowseVrma, out _btnBrowseVrma));
            _vrmaPathField.SetValueWithoutNotify(RecentPaths.Get(VrmaPathKey));

            var row1 = new VisualElement(); row1.style.flexDirection = FlexDirection.Row; row1.style.marginBottom = 2;
            row1.Add(NumField("FPS", out _vrmaFpsField, 0f));
            root.Add(row1);

            var row2 = new VisualElement(); row2.style.flexDirection = FlexDirection.Row; row2.style.marginBottom = 3;
            row2.Add(NumField("Start", out _vrmaStartField, 0f));
            row2.Add(NumField("End", out _vrmaEndField, 0f));
            root.Add(row2);

            _btnVrmaExport = new Button(OnExportVrma) { text = "VRMA 書き出し" };
            _btnVrmaExport.style.marginBottom = 2;
            root.Add(_btnVrmaExport);

            _vrmaLabel = new Label();
            _vrmaLabel.style.fontSize = 10; _vrmaLabel.style.whiteSpace = WhiteSpace.Normal; _vrmaLabel.style.marginBottom = 4;
            root.Add(_vrmaLabel);
        }

        private const string VrmaPathKey = "MotionClip.Vrma.Path";

        private static VisualElement NumField(string label, out FloatField field, float initial)
        {
            var box = new VisualElement(); box.style.flexDirection = FlexDirection.Row; box.style.flexGrow = 1; box.style.marginRight = 4;
            var lbl = new Label(label); lbl.style.width = 44; lbl.style.fontSize = 10; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            field = new FloatField { value = initial }; field.style.flexGrow = 1;
            box.Add(lbl); box.Add(field);
            return box;
        }

        // 書き出しの可否と理由。VRMA への出口は「.plmotion → .vrma」1 本（PolyLing_VRMA整備計画.md 2.5）。
        private void RefreshVrmaSection()
        {
            if (_btnVrmaExport == null || _vrmaLabel == null) return;
            string reason = null;
            if (!HasClip)              reason = "PolyLing モーションを読み込んでください";
            else if (_loadedKind == 0) reason = "VMD は「VMD→モーション・VRMA書き出し」で .plmotion を作ってから書き出します";
            else if (_loadedKind == 2) reason = "VRM アニメーションを読み込んでいます（JSON保存で .plmotion にできます）";
            else if (_loadedKind == 3) reason = "BVH を読み込んでいます（JSON保存で .plmotion にしてから書き出します）";
            else if (_loadedKind == SourceFbx) reason = "FBX を読み込んでいます（JSON保存で .plmotion にしてから書き出します）";
            else if (string.IsNullOrEmpty(_filePath)) reason = "外から受けたクリップです（JSON保存でファイルにしてから書き出します）";
            _btnVrmaExport.SetEnabled(reason == null && SendCommand != null);
            _vrmaLabel.text = reason ?? "モデルは使いません。Humanoid 骨の回転・Hips の位置・表情・視線を出します";
        }

        private void OnBrowseVrma()
        {
            string path = AskVrmaSavePath();
            if (!string.IsNullOrEmpty(path)) _vrmaPathField.value = path;
        }

        // 書き出しは必ず保存ダイアログを通す。パス欄の値は初期値としてだけ使う。
        private string AskVrmaSavePath()
        {
            string defName = Surface?.GetString(Tool, "clipName");
            if (string.IsNullOrEmpty(defName)) defName = "motion";
            return SaveDest.AskSavePath("VRM アニメーションの書き出し", SaveDest.Keys.Vrma, "", defName, "vrma");
        }

        private void OnExportVrma()
        {
            if (!HasClip || string.IsNullOrEmpty(_filePath) || _loadedKind != 1)
            { SetStatus("PolyLing モーションを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string outPath = AskVrmaSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _vrmaPathField.value = outPath;

            // モーションはこのパネルのダイアログで利用者が選んだもの。
            // PLSandbox の 1 回許可は解決した時点で消えるので、コマンドを送る直前に付け直す。
            PLSandbox.AllowOnceFromDialog(_filePath);

            var since = DateTime.Now.AddSeconds(-2);
            SendCommand(new Poly_Ling.Data.ConvertMotionToVrmaCommand(
                GetModelIndex?.Invoke() ?? 0, outPath, _filePath,
                _vrmaFpsField?.value ?? 0f, _vrmaStartField?.value ?? 0f, _vrmaEndField?.value ?? 0f));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            SetStatus(ok ? $"VRMA を書き出しました: {Path.GetFileName(outPath)}" : "VRMA 書き出しに失敗しました（ログを参照）");
            RefreshAll();
        }

        // ── BVH 書き出し ──────────────────────────────────────────────
        //   読み込んだモーションを今のモデルへ当て、モデルの骨格（IK ボーンを除く）の BVH を
        //   ExportMotionToBvhCommand（BvhCodec.ExportFromModel）で書く。PolyLing_BVH対応計画.md 5。
        private const string BvhPathKey  = "MotionClip.Bvh.Path";
        private const string BvhScaleKey = "MotionClip.Bvh.Scale";

        private void BuildBvhSection(VisualElement root)
        {
            root.Add(SecLabel("BVH 書き出し（今のモデルの骨格）"));

            _bvhPathField = new TextField();
            _bvhPathField.RegisterValueChangedCallback(e => RecentPaths.Set(BvhPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_bvhPathField, OnBrowseBvh, out _btnBrowseBvh));
            _bvhPathField.SetValueWithoutNotify(RecentPaths.Get(BvhPathKey));

            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            row.Add(NumField("FPS", out _bvhFpsField, 0f));
            float savedScale = float.TryParse(RecentPaths.Get(BvhScaleKey), System.Globalization.NumberStyles.Float,
                                              System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f ? s : 1f;
            row.Add(NumField("倍率", out _bvhScaleField, savedScale));
            _bvhScaleField.RegisterValueChangedCallback(e =>
                RecentPaths.Set(BvhScaleKey, e.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            root.Add(row);

            _btnBvhExport = new Button(OnExportBvh) { text = "BVH 書き出し" };
            _btnBvhExport.style.marginBottom = 2;
            root.Add(_btnBvhExport);

            _bvhLabel = new Label("読み込んだモーションを今のモデルへ当てて書き出します（IK ボーンは除きます）");
            _bvhLabel.style.fontSize = 10; _bvhLabel.style.whiteSpace = WhiteSpace.Normal; _bvhLabel.style.marginBottom = 4;
            root.Add(_bvhLabel);
        }

        private void OnBrowseBvh()
        {
            string path = AskBvhSavePath();
            if (!string.IsNullOrEmpty(path)) _bvhPathField.value = path;
        }

        // 書き出しは必ず保存ダイアログを通す。パス欄の値は初期値としてだけ使う。
        private string AskBvhSavePath()
        {
            string defName = Surface?.GetString(Tool, "clipName");
            if (string.IsNullOrEmpty(defName)) defName = "motion";
            return SaveDest.AskSavePath("BVH の書き出し", SaveDest.Keys.Motion, "", defName + ".bvh", "bvh");
        }

        private void OnExportBvh()
        {
            if (!HasClip || string.IsNullOrEmpty(_filePath)) { SetStatus("モーションを読み込んでください"); return; }
            if (Model == null) { SetStatus("モデルを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string outPath = AskBvhSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _bvhPathField.value = outPath;

            // モーションはこのパネルのダイアログで利用者が選んだもの。1 回許可を送る直前に付け直す。
            PLSandbox.AllowOnceFromDialog(_filePath);

            var since = DateTime.Now.AddSeconds(-2);
            SendCommand(new Poly_Ling.Data.ExportMotionToBvhCommand(
                GetModelIndex?.Invoke() ?? 0, outPath, _filePath, (Poly_Ling.Data.MotionSourceKind)_loadedKind,
                Poly_Ling.Data.BvhSkeletonKind.Auto, null,
                _bvhFpsField?.value ?? 0f, 0f, 0f, _bvhScaleField != null && _bvhScaleField.value > 0f ? _bvhScaleField.value : 1f,
                _alignLegsToggle != null && _alignLegsToggle.value,
                AllowFbxRestPath(), FbxTake));

            // Dispatch は同期なので、書き出し結果をここで確かめる。書き出しはポーズ層を戻すので、表示中のフレームを当て直す。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            ApplyFrame();
            string msg = ok ? $"BVH を書き出しました: {Path.GetFileName(outPath)}" : "BVH 書き出しに失敗しました（ログを参照）";
            if (_bvhLabel != null) _bvhLabel.text = msg;
            SetStatus(msg);
        }

        // ── FBX 書き出し ──────────────────────────────────────────────
        //   読み込んだモーションを今のモデルへ当て、モデル（メッシュ・材質・スキン）とモーションの FBX を
        //   ExportMotionToFbxCommand で書く。PolyLing_FBX対応計画.md 段 3。
        private const string FbxPathKey        = "MotionClip.Fbx.Path";
        private const string FbxIncludeMeshKey = "MotionClip.Fbx.IncludeMesh";

        private void BuildFbxSection(VisualElement root)
        {
            root.Add(SecLabel("FBX 書き出し（今のモデル＋モーション）"));

            _fbxPathField = new TextField();
            _fbxPathField.RegisterValueChangedCallback(e => RecentPaths.Set(FbxPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_fbxPathField, OnBrowseFbx, out _btnBrowseFbx));
            _fbxPathField.SetValueWithoutNotify(RecentPaths.Get(FbxPathKey));

            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            row.Add(NumField("FPS", out _fbxFpsField, 0f));
            root.Add(row);

            _fbxIncludeMeshToggle = new Toggle("メッシュ・材質・スキンも書く")
            {
                value = RecentPaths.Get(FbxIncludeMeshKey) != "0",
            };
            _fbxIncludeMeshToggle.style.fontSize = 10; _fbxIncludeMeshToggle.style.marginBottom = 3;
            _fbxIncludeMeshToggle.RegisterValueChangedCallback(e => RecentPaths.Set(FbxIncludeMeshKey, e.newValue ? "1" : "0"));
            root.Add(_fbxIncludeMeshToggle);

            _btnFbxExport = new Button(OnExportFbx) { text = "FBX 書き出し" };
            _btnFbxExport.style.marginBottom = 2;
            root.Add(_btnFbxExport);

            _fbxLabel = new Label("読み込んだモーションを今のモデルへ当てて書き出します（IK ボーンは除きます）");
            _fbxLabel.style.fontSize = 10; _fbxLabel.style.whiteSpace = WhiteSpace.Normal; _fbxLabel.style.marginBottom = 4;
            root.Add(_fbxLabel);
        }

        private void OnBrowseFbx()
        {
            string path = AskFbxSavePath();
            if (!string.IsNullOrEmpty(path)) _fbxPathField.value = path;
        }

        // 書き出しは必ず保存ダイアログを通す。パス欄の値は初期値としてだけ使う。
        private string AskFbxSavePath()
        {
            string defName = Surface?.GetString(Tool, "clipName");
            if (string.IsNullOrEmpty(defName)) defName = "motion";
            return SaveDest.AskSavePath("FBX の書き出し", SaveDest.Keys.Motion, "", defName + ".fbx", "fbx");
        }

        private void OnExportFbx()
        {
            if (!HasClip || string.IsNullOrEmpty(_filePath)) { SetStatus("モーションを読み込んでください"); return; }
            if (Model == null) { SetStatus("モデルを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string outPath = AskFbxSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _fbxPathField.value = outPath;

            // モーションはこのパネルのダイアログで利用者が選んだもの。1 回許可を送る直前に付け直す。
            // 受け口はテクスチャの書き出し先（「ファイル名_textures」）も関門に通すので、それも許可する。
            PLSandbox.AllowOnceFromDialog(_filePath);
            PLSandbox.AllowOnceFromDialog(Poly_Ling.Fbx.FbxExportSettings.DefaultTextureFolder(outPath));

            var since = DateTime.Now.AddSeconds(-2);
            SendCommand(new Poly_Ling.Data.ExportMotionToFbxCommand(
                GetModelIndex?.Invoke() ?? 0, outPath, _filePath, (Poly_Ling.Data.MotionSourceKind)_loadedKind,
                Poly_Ling.Data.BvhSkeletonKind.Auto, null,
                _fbxFpsField?.value ?? 0f, 0f, 0f,
                _alignLegsToggle != null && _alignLegsToggle.value,
                _fbxIncludeMeshToggle == null || _fbxIncludeMeshToggle.value,
                null,
                AllowFbxRestPath(), FbxTake));

            // Dispatch は同期なので、書き出し結果をここで確かめる。書き出しはポーズ層を戻すので、表示中のフレームを当て直す。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            ApplyFrame();
            string msg = ok ? $"FBX を書き出しました: {Path.GetFileName(outPath)}" : "FBX 書き出しに失敗しました（ログを参照）";
            if (_fbxLabel != null) _fbxLabel.text = msg;
            SetStatus(msg);
        }

        // ── FBX を読むときの設定 ──────────────────────────────────────
        //   レスト用 FBX：モーションだけの FBX はバインドポーズを持たず、ノードの既定の姿勢が
        //   T ポーズとは限らない。同じ骨格のモデル FBX を渡すと、そのバインドポーズをレストにする
        //   （PolyLing_FBX対応計画.md 段 5）。読み込み・JSON保存・BVH / FBX 書き出しのすべてに渡す。
        private const string FbxRestPathKey = "MotionClip.FbxRest.Path";
        private const string FbxTakeKey     = "MotionClip.Fbx.Take";

        private void BuildFbxReadSection(VisualElement root)
        {
            _fbxReadSection = new VisualElement();
            root.Add(_fbxReadSection);

            _fbxReadSection.Add(PlayerIoUiKit.SectionLabel("レスト用 FBX（T ポーズのモデル。空なら読むファイル自身）"));
            _fbxRestPathField = new TextField();
            _fbxRestPathField.RegisterValueChangedCallback(e => RecentPaths.Set(FbxRestPathKey, e.newValue));
            _fbxReadSection.Add(PlayerIoUiKit.PathRow(_fbxRestPathField, OnBrowseFbxRest, out _btnBrowseFbxRest));
            _fbxRestPathField.SetValueWithoutNotify(RecentPaths.Get(FbxRestPathKey));

            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            var takeLbl = new Label("テイク");
            takeLbl.style.width = 44; takeLbl.style.fontSize = 10; takeLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _fbxTakeField = new IntegerField { value = Mathf.Max(0, ParseInt(RecentPaths.Get(FbxTakeKey), 0)) };
            _fbxTakeField.style.width = 60; _fbxTakeField.style.marginRight = 4;
            _fbxTakeField.RegisterValueChangedCallback(e =>
            {
                int v = Mathf.Max(0, e.newValue);
                if (v != e.newValue) _fbxTakeField.SetValueWithoutNotify(v);
                RecentPaths.Set(FbxTakeKey, v.ToString());
            });
            _btnClearFbxRest = new Button(() =>
            {
                _fbxRestPathField.value = string.Empty;
                SetStatus("レスト用 FBX の指定を外しました（再読込で反映）");
            }) { text = "レスト指定を外す" };
            _btnClearFbxRest.style.flexGrow = 1;
            row.Add(takeLbl); row.Add(_fbxTakeField); row.Add(_btnClearFbxRest);
            _fbxReadSection.Add(row);

            RefreshFbxReadSection();
        }

        private void RefreshFbxReadSection()
        {
            if (_fbxReadSection != null)
                _fbxReadSection.style.display = _sourceKind == SourceFbx ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnBrowseFbxRest()
        {
            string path = PlayerIoUiKit.AskLoadPath("Open Rest FBX", FbxRestPathKey, _fbxRestPathField.value, "fbx");
            if (!string.IsNullOrEmpty(path)) _fbxRestPathField.value = path;
        }

        /// <summary>FBX を読むときのレスト用 FBX（FBX 以外・未指定は null）。</summary>
        private string FbxRestPath
            => _sourceKind == SourceFbx && _fbxRestPathField != null && !string.IsNullOrEmpty(_fbxRestPathField.value)
                ? _fbxRestPathField.value : null;

        /// <summary>FBX を読むときのテイク番号（FBX 以外は 0）。</summary>
        private int FbxTake
            => _sourceKind == SourceFbx && _fbxTakeField != null ? Mathf.Max(0, _fbxTakeField.value) : 0;

        /// <summary>
        /// コマンドへ渡すレスト用 FBX。このパネルのダイアログで利用者が選んだものなので、
        /// 送る直前に 1 回許可を付け直す。無ければ null。
        /// </summary>
        private string AllowFbxRestPath()
        {
            string rest = FbxRestPath;
            if (rest != null) PLSandbox.AllowOnceFromDialog(rest);
            return rest;
        }

        /// <summary>
        /// 表示中のフレームをモデルへ当て直す。VRMA 書き出しがポーズ層を戻したあとに受け口から呼ばれる。
        /// </summary>
        public void ReapplyCurrentFrame() => ApplyFrame();

        private void Clear()
        {
            StopPlay();
            Surface?.Invoke(Tool, "clear");
            _currentTime = 0f; _maxTime = 0f;
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            string path = _filePath;
            int    kind = _loadedKind;   // 読み込んだときの種類で読み直す（ドロップダウンの今の値ではない）
            float  time = _currentTime;
            Clear();

            Surface?.Invoke(Tool, "load", ("path", path), ("sourceKind", kind), ("time", 0f),
                ("fbxRestPath", FbxRestPath ?? ""), ("fbxTake", FbxTake));
            string err = Surface?.GetString(Tool, "error") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus($"再読込み失敗: {err}"); return; }

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

        // ================================================================
        // 再生
        // ================================================================

        private void TogglePlay()
        {
            if (_playing) { StopPlay(); SetStatus("停止しました"); return; }
            if (!HasClip || Model == null) { SetStatus("クリップとモデルが必要です"); return; }
            if (_currentTime >= _maxTime) { SetStatus("末尾にいます。先頭へ戻してから再生してください"); return; }

            _playing       = true;
            _playStartTime = _currentTime;
            _playStartReal = Time.realtimeSinceStartupAsDouble;
            SetPlayingUi(true);

            bool listening = Surface?.GetBool(LiveTool, "listening") ?? false;
            bool muscles   = Surface?.GetBool(Tool, "hasMuscles") ?? false;
            SetStatus(!listening ? "再生中"
                    : muscles    ? "再生中（受け入れ中の接続へ配信しています）"
                                 : "再生中（このクリップはマッスルを持たないため配信しません）");
            PlayTick();
        }

        private void PlayTick()
        {
            if (!_playing) return;
            if (!HasClip || Model == null) { StopPlay(); return; }

            float t = _playStartTime + (float)(Time.realtimeSinceStartupAsDouble - _playStartReal);
            bool atEnd = t >= _maxTime;
            _currentTime = Mathf.Min(t, _maxTime);
            UpdateSlider();
            UpdateTimeLabel();
            Surface?.Invoke(Tool, "playFrame", ("time", _currentTime));

            if (atEnd) { StopPlay(); SetStatus("末尾まで再生しました"); return; }
            // 次の 1 回だけを予約する（クリップのフレーム間隔。10〜100ms に収める）
            int ms = Mathf.Clamp(Mathf.RoundToInt(1000f / Mathf.Max(1f, FrameRate)), 10, 100);
            _timeSlider?.schedule.Execute(PlayTick).StartingIn(ms);
        }

        private void StopPlay()
        {
            if (!_playing) return;
            _playing = false;
            SetPlayingUi(false);
        }

        // 再生中は時刻を動かす操作を止める
        private void SetPlayingUi(bool playing)
        {
            if (_btnPlay != null) _btnPlay.text = playing ? "■ 停止" : "▶ 再生";
            _timeSlider?.SetEnabled(!playing);
            _timeInput?.SetEnabled(!playing);
            _btnTimeFirst?.SetEnabled(!playing);
            _btnTimePrev?.SetEnabled(!playing);
            _btnTime25?.SetEnabled(!playing);
            _btnTime50?.SetEnabled(!playing);
            _btnTime75?.SetEnabled(!playing);
            _btnTimeNext?.SetEnabled(!playing);
            _btnTimeLast?.SetEnabled(!playing);
        }

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
            return dto.bones?.Count ?? 0;
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
