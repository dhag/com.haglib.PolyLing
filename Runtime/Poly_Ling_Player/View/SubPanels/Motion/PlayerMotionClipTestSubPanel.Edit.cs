// PlayerMotionClipTestSubPanel.Edit.cs
// 「モーションの試し再生」パネルの編集の節（数値の欄）。PolyLing_モーション編集構造計画.md 段 5。
// Runtime/Poly_Ling_Player/View/SubPanels/Motion/ に配置
//
// 【操作の通し方】欄やボタンは段 4 の書き込みコマンド（PanelCommand.MotionEdit.cs）を RunCommand で送る。
//   受け口は窓口 motionClip の口を呼ぶだけで、取り消しの記録は口が積む 1 件だけ（パネルは積まない）。
//   数の欄は確定したときだけ送る（isDelayed）。1 回の確定でコマンド 1 本・取り消し 1 件。
// 【表示】窓口のクリップを MotionEditOps.EditView で見る。edit の無いクリップは、最初の書き込みで作られる形
//   （照会のコマンドと同じ。id も同じ）を出す。
// 【選択】トラック・クリップ・バンドルは id、キーはチャンネルとローカルフレームで持つ。作り直し（口・取り消し・
//   読み直し）の後、id がまだあればそのまま、無ければ先頭に移す（無い id は持ち続けない。id は edit の最大値＋1
//   で振るので、除いた直後に同じ id が別のものに振られることがある）。フレームはクリップの範囲内なら保ち、
//   範囲外ならそのチャンネルの先頭のキー（無ければ 0）にする。作る操作の後は作ったもの（lastCreatedId）を選ぶ。
// 【派生】派生のトラック（bakedBones が正本のときの muscles・body など）は印を付け、書き込みの欄を使えなくする。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Motion;

namespace Poly_Ling.Player
{
    public partial class PlayerMotionClipTestSubPanel
    {
        /// <summary>窓口 motionClip のクリップ（表示用。変えない）。</summary>
        public Func<MotionClipDTO> GetClip;

        /// <summary>パネルからコマンドを送り、失敗理由を返す（成功は null）。</summary>
        public Func<PanelCommand, string> RunCommand;

        // ── 選択と表示 ───────────────────────────────────────────────
        private MotionClipDTO _editView;
        private bool          _editHeld;
        private string _selTrack, _selClip, _selBundle;
        private string _selCh = MotionEditOps.ChRot;
        private int    _selF;
        private int    _selComp;
        private readonly List<string> _trackIds  = new List<string>();
        private readonly List<string> _clipIds   = new List<string>();
        private readonly List<string> _chIds     = new List<string>();
        private readonly List<int>    _keyFrames = new List<int>();
        private readonly List<string> _bundleIds = new List<string>();

        private static readonly List<string> RoleChoices = new List<string>
        {
            MotionEditDTO.RoleBones, MotionEditDTO.RoleBakedBones, MotionEditDTO.RoleBody,
            MotionEditDTO.RoleLookAt, MotionEditDTO.RoleExpressions, MotionEditDTO.RoleMuscles,
        };
        private static readonly List<string> KindChoices = new List<string> { MotionClipDTO.TargetBoneName, MotionClipDTO.TargetPath };
        private const string WrapUnset = "（未指定）";
        private static readonly List<string> WrapChoices = new List<string>
        {
            WrapUnset, MotionCurveMath.WrapClamp, MotionCurveMath.WrapClampForever, MotionCurveMath.WrapOnce,
            MotionCurveMath.WrapDefault, MotionCurveMath.WrapLoop, MotionCurveMath.WrapPingPong,
        };
        private static readonly List<string> TanModeChoices = new List<string>
        {
            MotionCurveMath.ModeConstant, MotionCurveMath.ModeLinear, MotionCurveMath.ModeAuto,
            MotionCurveMath.ModeClampedAuto, MotionCurveMath.ModeFree,
        };
        private static readonly List<string> WeightedChoices = new List<string>
        {
            MotionCurveMath.WeightNone, MotionCurveMath.WeightIn, MotionCurveMath.WeightOut, MotionCurveMath.WeightBoth,
        };
        private static readonly List<string> LayerModeChoices = new List<string> { MotionEditDTO.LayerOverride, MotionEditDTO.LayerAdditive };
        private const string NoneChoice = "（なし）";

        // ── ボタンや入力欄 ─────────────────────────────────────────────
        // UI 自動操作の ID は "motionClipTest.edit.<…>"。どれも折り畳みを開く下準備（RevealEdit）を持つ。
        [UiControl(Ignore = true)] private Foldout       _editFoldout;
        [UiControl(Ignore = true)] private VisualElement _editBody;
        [UiControl("edit.info", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "編集の構造の状態（edit の有無、edit に入っていない平らなトラック）")]
        private Label _editInfoLabel;

        // トラック
        [UiControl("edit.track", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetTrackSel), Description = "編集するトラックを選ぶ（id でも選べる）")]
        private DropdownField _trackField;
        [UiControl("edit.track.preWrap", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "トラックのラップ（前）")]
        private DropdownField _preWrapField;
        [UiControl("edit.track.postWrap", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "トラックのラップ（後。未指定はループのクリップがあれば Loop）")]
        private DropdownField _postWrapField;
        [UiControl("edit.track.remove", Safety = UiSafety.Destructive, Reveal = nameof(RevealEdit), Description = "選んだトラックを除く（平らなトラックも除く。取り消せる）")]
        private Button _btnTrackRemove;
        [UiControl("edit.newTrack.role", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すトラックの role")]
        private DropdownField _newRoleField;
        [UiControl("edit.newTrack.targetKind", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すトラックの targetKind（bones だけ。boneName / path）")]
        private DropdownField _newKindField;
        [UiControl("edit.newTrack.target", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すトラックの target（骨名・Humanoid 名・表情名・マッスル名）")]
        private TextField _newTargetField;
        [UiControl("edit.newTrack.name", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すトラックの名前（空なら target）")]
        private TextField _newTrackNameField;
        [UiControl("edit.newTrack.add", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "トラックを足す")]
        private Button _btnTrackAdd;

        // クリップ
        [UiControl("edit.clip", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetClipSel), Description = "編集するクリップを選ぶ（id でも選べる）")]
        private DropdownField _clipField;
        [UiControl("edit.clip.info", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "クリップのキーの数と所属バンドル")]
        private Label _clipEditInfoLabel;
        [UiControl("edit.clip.start", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "クリップの開始（フレーム）")]
        private IntegerField _clipStartField;
        [UiControl("edit.clip.len", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "クリップの長さ（フレーム。1 以上）")]
        private IntegerField _clipLenField;
        [UiControl("edit.clip.loop", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "クリップのループ（長さで折り返す）")]
        private Toggle _clipLoopToggle;
        [UiControl("edit.clip.weight", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "クリップの重み（0～1）")]
        private FloatField _clipWeightField;
        [UiControl("edit.clip.remove", Safety = UiSafety.Destructive, Reveal = nameof(RevealEdit), Description = "選んだクリップを除く（バンドルからも除く。取り消せる）")]
        private Button _btnClipRemove;
        [UiControl("edit.newClip.start", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すクリップの開始（フレーム）")]
        private IntegerField _newClipStartField;
        [UiControl("edit.newClip.len", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "足すクリップの長さ（フレーム）")]
        private IntegerField _newClipLenField;
        [UiControl("edit.newClip.add", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "選んだトラックに空のクリップを足す")]
        private Button _btnClipAdd;

        // キー
        [UiControl("edit.key.channel", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetChannelSel), Description = "チャンネル（rot / pos / scl / w）を選ぶ")]
        private DropdownField _chField;
        [UiControl("edit.key.select", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetKeySel), Description = "そのチャンネルにあるキーを選ぶ（フレームの数でも選べる）")]
        private DropdownField _keyField;
        [UiControl("edit.key.frame", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "クリップの先頭からのフレーム（キーを打つ・作る位置）")]
        private IntegerField _keyFrameField;
        [UiControl("edit.key.state", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "選んだフレームにキーがあるか")]
        private Label _keyStateLabel;
        [UiControl("edit.key.value.x", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "キーの値 x（回転は四元数の x、w チャンネルは値）。キーがあれば変えると書く")]
        private FloatField _valX;
        [UiControl("edit.key.value.y", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "キーの値 y")]
        private FloatField _valY;
        [UiControl("edit.key.value.z", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "キーの値 z")]
        private FloatField _valZ;
        [UiControl("edit.key.value.w", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "キーの値 w（回転だけ）")]
        private FloatField _valW;
        [UiControl("edit.key.capture", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "フレームに全チャンネルの今の評価値でキーを打つ（Studio の「キー追加」）")]
        private Button _btnCapture;
        [UiControl("edit.key.setChannel", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "選んだチャンネルのフレームに、値の欄の値でキーを作る（あれば値だけ変える）")]
        private Button _btnSetChannelKey;
        [UiControl("edit.key.removeChannel", Safety = UiSafety.Destructive, Reveal = nameof(RevealEdit), Description = "選んだチャンネルのフレームのキーだけを除く")]
        private Button _btnRemoveChannelKey;
        [UiControl("edit.key.remove", Safety = UiSafety.Destructive, Reveal = nameof(RevealEdit), Description = "フレームのキーを全チャンネルで除く")]
        private Button _btnRemoveKey;
        [UiControl("edit.key.moveTo", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "キーの移り先のフレーム")]
        private IntegerField _moveToField;
        [UiControl("edit.key.move", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "フレームのキー（全チャンネル）を移り先へ動かす")]
        private Button _btnMoveKey;

        // 接線
        [UiControl("edit.tan.comp", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetCompSel), Description = "接線の成分を選ぶ（傾き・重み方式・重みはこの成分に書く）")]
        private DropdownField _compField;
        [UiControl("edit.tan.inMode", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "入側の接線モード（全成分に当てる）")]
        private DropdownField _inModeField;
        [UiControl("edit.tan.outMode", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "出側の接線モード（全成分に当てる）")]
        private DropdownField _outModeField;
        [UiControl("edit.tan.inSlope", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "入側の傾き（値/秒。書くと Auto・ClampedAuto・線形は Free になる）")]
        private FloatField _inSlopeField;
        [UiControl("edit.tan.outSlope", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "出側の傾き（値/秒）")]
        private FloatField _outSlopeField;
        [UiControl("edit.tan.weightedMode", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "重み方式（None / In / Out / Both）")]
        private DropdownField _wmField;
        [UiControl("edit.tan.inWeight", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "入側の重み（0～1）")]
        private FloatField _inWeightField;
        [UiControl("edit.tan.outWeight", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "出側の重み（0～1）")]
        private FloatField _outWeightField;
        [UiControl("edit.tan.info", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "傾き・重みが保存された値か、モードから求めた値か")]
        private Label _tanInfoLabel;

        // バンドル・レイヤー
        [UiControl("edit.bundle", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Setter = nameof(SetBundleSel), Description = "バンドルを選ぶ（id でも選べる）")]
        private DropdownField _bundleField;
        [UiControl("edit.bundle.info", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "バンドルの中身・親子")]
        private Label _bundleInfoLabel;
        [UiControl("edit.layer.enabled", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "バンドルをレイヤーにする（中のクリップを基底の上に重ねる）")]
        private Toggle _layerToggle;
        [UiControl("edit.layer.mode", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "レイヤーの方式（override / additive）")]
        private DropdownField _layerModeField;
        [UiControl("edit.layer.weight", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "レイヤーの重み（0～1）")]
        private FloatField _layerWeightField;
        [UiControl("edit.layer.mute", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "レイヤーのミュート")]
        private Toggle _layerMuteToggle;
        [UiControl("edit.layer.solo", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "レイヤーのソロ（同じ親の兄弟レイヤーをミュート）")]
        private Toggle _layerSoloToggle;
        [UiControl("edit.layer.up", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "重ねる順：先に重ねる（↑）")]
        private Button _btnLayerUp;
        [UiControl("edit.layer.down", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "重ねる順：後に重ねる（↓）")]
        private Button _btnLayerDown;
        [UiControl("edit.layer.order", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealEdit), Description = "重ねる順と実効の重み・ミュート")]
        private Label _layerOrderLabel;
        [UiControl("edit.bundle.ungroup", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "バンドルだけを解く（クリップと子は残す）")]
        private Button _btnUngroup;
        [UiControl("edit.bundle.cloneShift", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "複製したクリップの開始をずらすフレーム数")]
        private IntegerField _cloneShiftField;
        [UiControl("edit.bundle.clone", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "バンドルを中身ごと複製する")]
        private Button _btnClone;
        [UiControl("edit.bundle.delete", Safety = UiSafety.Destructive, Reveal = nameof(RevealEdit), Description = "バンドルを中身のクリップと子ごと除く（取り消せる）")]
        private Button _btnBundleDelete;
        [UiControl("edit.newBundle.clipIds", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "束ねるクリップの id（, 区切り）")]
        private TextField _mkClipIdsField;
        [UiControl("edit.newBundle.bundleIds", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "束ねるバンドルの id（, 区切り。入れ子にする）")]
        private TextField _mkBundleIdsField;
        [UiControl("edit.newBundle.addClip", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "選んでいるクリップの id を束ねる欄に足す")]
        private Button _btnMkAddClip;
        [UiControl("edit.newBundle.addBundle", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "選んでいるバンドルの id を束ねる欄に足す")]
        private Button _btnMkAddBundle;
        [UiControl("edit.newBundle.name", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "作るバンドルの名前（空なら「バンドル」）")]
        private TextField _mkNameField;
        [UiControl("edit.newBundle.make", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealEdit), Description = "欄のクリップとバンドルを束ねたバンドルを作る")]
        private Button _btnMakeBundle;

        // ================================================================
        // Build
        // ================================================================

        private bool RevealEdit()
        {
            if (_editFoldout == null || _editFoldout.value) return false;
            _editFoldout.value = true;
            return true;
        }

        private void BuildEditSection(VisualElement root)
        {
            _editFoldout = new Foldout { text = "編集（数値）", value = false };
            _editFoldout.style.marginBottom = 4;
            root.Add(_editFoldout);
            _editBody = new VisualElement();
            _editFoldout.Add(_editBody);
            var b = _editBody;

            _editInfoLabel = SmallLabel();
            b.Add(_editInfoLabel);

            // ── トラック ──
            b.Add(SecLabel("トラック"));
            _trackField = Drop(b, "トラック", new List<string> { NoneChoice });
            _trackField.RegisterValueChangedCallback(e =>
            {
                int i = _trackField.choices.IndexOf(e.newValue);
                if (i < 0 || i >= _trackIds.Count) return;
                _selTrack = _trackIds[i]; _selClip = null;
                RefreshEdit();
            });
            _preWrapField  = Drop(b, "ラップ 前", WrapChoices);
            _postWrapField = Drop(b, "ラップ 後", WrapChoices);
            _preWrapField.RegisterValueChangedCallback(_ => SendWrap());
            _postWrapField.RegisterValueChangedCallback(_ => SendWrap());
            _btnTrackRemove = Btn(b, "トラックを除く", () =>
                Send(new MotionEditRemoveTrackCommand(ModelIdx, _selTrack, _currentTime), "トラックを除きました"));

            _newRoleField = Drop(b, "足す role", RoleChoices);
            _newKindField = Drop(b, "targetKind", KindChoices);
            _newRoleField.RegisterValueChangedCallback(e => _newKindField.SetEnabled(e.newValue == MotionEditDTO.RoleBones));
            _newTargetField    = Text(b, "target");
            _newTrackNameField = Text(b, "名前");
            _btnTrackAdd = Btn(b, "トラックを足す", () =>
            {
                string role = _newRoleField.value;
                string kind = role == MotionEditDTO.RoleBones ? _newKindField.value : "";
                if (SendCreate(new MotionEditAddTrackCommand(ModelIdx, role, _newTargetField.value, kind, _newTrackNameField.value, _currentTime),
                               "トラックを足しました", out string id))
                { _selTrack = id; _selClip = null; RefreshEdit(); }
            });

            // ── クリップ ──
            b.Add(SecLabel("クリップ"));
            _clipField = Drop(b, "クリップ", new List<string> { NoneChoice });
            _clipField.RegisterValueChangedCallback(e =>
            {
                int i = _clipField.choices.IndexOf(e.newValue);
                if (i < 0 || i >= _clipIds.Count) return;
                _selClip = _clipIds[i];
                RefreshEdit();
            });
            _clipEditInfoLabel = SmallLabel();
            b.Add(_clipEditInfoLabel);
            _clipStartField  = Int(b, "開始 f");
            _clipLenField    = Int(b, "長さ f");
            _clipLoopToggle  = new Toggle("ループ"); _clipLoopToggle.style.fontSize = 10; b.Add(_clipLoopToggle);
            _clipWeightField = Float(b, "重み");
            _clipStartField.RegisterValueChangedCallback(_ => SendClip());
            _clipLenField.RegisterValueChangedCallback(_ => SendClip());
            _clipLoopToggle.RegisterValueChangedCallback(_ => SendClip());
            _clipWeightField.RegisterValueChangedCallback(_ => SendClip());
            _btnClipRemove = Btn(b, "クリップを除く", () =>
                Send(new MotionEditRemoveClipCommand(ModelIdx, _selClip, _currentTime), "クリップを除きました"));
            var nr = Row(b);
            nr.Add(Lbl("足す 開始/長さ"));
            _newClipStartField = new IntegerField { isDelayed = true }; _newClipStartField.style.flexGrow = 1; nr.Add(_newClipStartField);
            _newClipLenField   = new IntegerField { isDelayed = true, value = 30 }; _newClipLenField.style.flexGrow = 1; nr.Add(_newClipLenField);
            _btnClipAdd = Btn(b, "クリップを足す", () =>
            {
                if (SendCreate(new MotionEditAddClipCommand(ModelIdx, _selTrack, _newClipStartField.value, _newClipLenField.value, "", _currentTime),
                               "クリップを足しました", out string id))
                { _selClip = id; RefreshEdit(); }
            });

            // ── キー ──
            b.Add(SecLabel("キー"));
            _chField = Drop(b, "チャンネル", new List<string> { NoneChoice });
            _chField.RegisterValueChangedCallback(e =>
            {
                int i = _chField.choices.IndexOf(e.newValue);
                if (i < 0 || i >= _chIds.Count) return;
                _selCh = _chIds[i];
                RefreshEdit();
            });
            _keyField = Drop(b, "キー", new List<string> { NoneChoice });
            _keyField.RegisterValueChangedCallback(e =>
            {
                int i = _keyField.choices.IndexOf(e.newValue);
                if (i < 0 || i >= _keyFrames.Count) return;
                _selF = _keyFrames[i];
                RefreshEdit();
            });
            _keyFrameField = Int(b, "フレーム");
            _keyFrameField.RegisterValueChangedCallback(e => { _selF = e.newValue; RefreshEdit(); });
            _keyStateLabel = SmallLabel();
            b.Add(_keyStateLabel);
            Row(b).Add(Lbl("値 x / y / z / w"));   // 欄を広く取るため、見出しは別の行
            var vr = Row(b);
            _valX = VecField(vr); _valY = VecField(vr); _valZ = VecField(vr); _valW = VecField(vr);
            foreach (var f in new[] { _valX, _valY, _valZ, _valW })
                f.RegisterValueChangedCallback(_ => OnValueEdited());
            var kr1 = Row(b);
            _btnCapture = RowBtn(kr1, "キーを打つ（全ch）", () =>
                Send(new MotionEditCaptureKeyCommand(ModelIdx, _selClip, _selF, _currentTime), "キーを打ちました"));
            _btnSetChannelKey = RowBtn(kr1, "このchにキーを作る", () =>
                Send(new MotionEditSetChannelKeyCommand(ModelIdx, _selClip, _selCh, _selF, Values(), _currentTime), "キーを作りました"));
            var kr2 = Row(b);
            _btnRemoveChannelKey = RowBtn(kr2, "このchのキーを除く", () =>
                Send(new MotionEditRemoveChannelKeyCommand(ModelIdx, _selClip, _selCh, _selF, _currentTime), "チャンネルのキーを除きました"));
            _btnRemoveKey = RowBtn(kr2, "キーを除く（全ch）", () =>
                Send(new MotionEditRemoveKeyCommand(ModelIdx, _selClip, _selF, _currentTime), "キーを除きました"));
            var mr = Row(b);
            mr.Add(Lbl("移り先 f"));
            _moveToField = new IntegerField { isDelayed = true }; _moveToField.style.flexGrow = 1; mr.Add(_moveToField);
            _btnMoveKey = RowBtn(mr, "動かす", () =>
            {
                int to = _moveToField.value;
                if (Send(new MotionEditMoveKeyCommand(ModelIdx, _selClip, _selF, to, _currentTime), "キーを動かしました"))
                { _selF = to; RefreshEdit(); }
            });

            // ── 接線 ──
            b.Add(SecLabel("接線（モードは全成分）"));
            _compField = Drop(b, "成分", new List<string> { "x" });
            _compField.RegisterValueChangedCallback(e =>
            {
                int i = _compField.choices.IndexOf(e.newValue);
                if (i < 0) return;
                _selComp = i;
                RefreshEdit();
            });
            _inModeField  = Drop(b, "モード 入", TanModeChoices);
            _outModeField = Drop(b, "モード 出", TanModeChoices);
            _inModeField.RegisterValueChangedCallback(e => SendTanMode(MotionEditOps.SideIn, e.newValue));
            _outModeField.RegisterValueChangedCallback(e => SendTanMode(MotionEditOps.SideOut, e.newValue));
            _inSlopeField  = Float(b, "傾き 入");
            _outSlopeField = Float(b, "傾き 出");
            _inSlopeField.RegisterValueChangedCallback(e => Send(new MotionEditSetTangentSlopeCommand(
                ModelIdx, _selClip, _selCh, _selF, _selComp, MotionEditOps.SideIn, e.newValue, _currentTime), "傾きを書きました"));
            _outSlopeField.RegisterValueChangedCallback(e => Send(new MotionEditSetTangentSlopeCommand(
                ModelIdx, _selClip, _selCh, _selF, _selComp, MotionEditOps.SideOut, e.newValue, _currentTime), "傾きを書きました"));
            _wmField = Drop(b, "重み方式", WeightedChoices);
            _wmField.RegisterValueChangedCallback(e => Send(new MotionEditSetWeightedModeCommand(
                ModelIdx, _selClip, _selCh, _selF, _selComp, e.newValue, _currentTime), "重み方式を書きました"));
            _inWeightField  = Float(b, "重み 入");
            _outWeightField = Float(b, "重み 出");
            _inWeightField.RegisterValueChangedCallback(e => Send(new MotionEditSetTangentWeightCommand(
                ModelIdx, _selClip, _selCh, _selF, _selComp, MotionEditOps.SideIn, e.newValue, _currentTime), "重みを書きました"));
            _outWeightField.RegisterValueChangedCallback(e => Send(new MotionEditSetTangentWeightCommand(
                ModelIdx, _selClip, _selCh, _selF, _selComp, MotionEditOps.SideOut, e.newValue, _currentTime), "重みを書きました"));
            _tanInfoLabel = SmallLabel();
            b.Add(_tanInfoLabel);

            // ── バンドル・レイヤー ──
            b.Add(SecLabel("バンドル・レイヤー"));
            _bundleField = Drop(b, "バンドル", new List<string> { NoneChoice });
            _bundleField.RegisterValueChangedCallback(e =>
            {
                int i = _bundleField.choices.IndexOf(e.newValue);
                if (i < 0 || i >= _bundleIds.Count) return;
                _selBundle = _bundleIds[i];
                RefreshEdit();
            });
            _bundleInfoLabel = SmallLabel();
            b.Add(_bundleInfoLabel);
            _layerToggle = new Toggle("レイヤーにする"); _layerToggle.style.fontSize = 10; b.Add(_layerToggle);
            _layerModeField   = Drop(b, "方式", LayerModeChoices);
            _layerWeightField = Float(b, "重み");
            var lr = Row(b);
            _layerMuteToggle = new Toggle("ミュート"); _layerMuteToggle.style.fontSize = 10; _layerMuteToggle.style.marginRight = 8; lr.Add(_layerMuteToggle);
            _layerSoloToggle = new Toggle("ソロ");     _layerSoloToggle.style.fontSize = 10; lr.Add(_layerSoloToggle);
            _layerToggle.RegisterValueChangedCallback(_ => SendLayer());
            _layerModeField.RegisterValueChangedCallback(_ => SendLayer());
            _layerWeightField.RegisterValueChangedCallback(_ => SendLayer());
            _layerMuteToggle.RegisterValueChangedCallback(_ => SendLayer());
            _layerSoloToggle.RegisterValueChangedCallback(_ => SendLayer());
            var ur = Row(b);
            _btnLayerUp   = RowBtn(ur, "↑ 先に重ねる", () => Send(new MotionEditSwapLayerCommand(ModelIdx, _selBundle, -1, _currentTime), "重ねる順を変えました"));
            _btnLayerDown = RowBtn(ur, "↓ 後に重ねる", () => Send(new MotionEditSwapLayerCommand(ModelIdx, _selBundle, 1, _currentTime), "重ねる順を変えました"));
            _layerOrderLabel = SmallLabel();
            b.Add(_layerOrderLabel);
            var br = Row(b);
            _btnUngroup = RowBtn(br, "解く（中身は残す）", () =>
                Send(new MotionEditUngroupCommand(ModelIdx, _selBundle, _currentTime), "バンドルを解きました"));
            _btnBundleDelete = RowBtn(br, "中身ごと除く", () =>
                Send(new MotionEditDeleteBundleCommand(ModelIdx, _selBundle, _currentTime), "バンドルを除きました"));
            var cr = Row(b);
            cr.Add(Lbl("複製 ずらす f"));
            _cloneShiftField = new IntegerField { isDelayed = true }; _cloneShiftField.style.flexGrow = 1; cr.Add(_cloneShiftField);
            _btnClone = RowBtn(cr, "複製", () =>
            {
                if (SendCreate(new MotionEditCloneBundleCommand(ModelIdx, _selBundle, _cloneShiftField.value, _currentTime),
                               "バンドルを複製しました", out string id))
                { _selBundle = id; RefreshEdit(); }
            });
            _mkClipIdsField   = Text(b, "束ねるクリップ");
            _mkBundleIdsField = Text(b, "束ねるバンドル");
            var ar = Row(b);
            _btnMkAddClip   = RowBtn(ar, "＋今のクリップ", () => AppendId(_mkClipIdsField, _selClip));
            _btnMkAddBundle = RowBtn(ar, "＋今のバンドル", () => AppendId(_mkBundleIdsField, _selBundle));
            _mkNameField = Text(b, "名前");
            _btnMakeBundle = Btn(b, "バンドルを作る", () =>
            {
                if (SendCreate(new MotionEditMakeBundleCommand(ModelIdx, SplitIds(_mkClipIdsField.value), SplitIds(_mkBundleIdsField.value),
                                                               _mkNameField.value, _currentTime), "バンドルを作りました", out string id))
                {
                    _selBundle = id;
                    _mkClipIdsField.SetValueWithoutNotify(""); _mkBundleIdsField.SetValueWithoutNotify("");
                    RefreshEdit();
                }
            });
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void RefreshEdit()
        {
            if (_editBody == null) return;
            var src = HasClip ? GetClip?.Invoke() : null;
            _editView = MotionEditOps.EditView(src, out _editHeld);
            var e = _editView?.edit;
            bool on = e != null;
            _editBody.SetEnabled(on);
            if (!on)
            {
                _editInfoLabel.text = "クリップを読み込んでください";
                return;
            }

            // 状態
            var uncovered = UncoveredFlat(_editView);
            _editInfoLabel.text = (_editHeld ? "編集の構造（edit）あり" : "編集の構造（edit）はまだありません。最初の操作で、この形（id もこのまま）で作ります")
                + (uncovered.Count > 0 ? "\nedit に入っていない平らなトラック: " + string.Join("、", uncovered) : "");

            // トラック
            var tracks = (e.tracks ?? new List<MotionEditTrackDTO>()).Where(t => t != null).ToList();
            var track = tracks.FirstOrDefault(t => t.id == _selTrack) ?? tracks.FirstOrDefault();
            _selTrack = track?.id;
            _trackIds.Clear();
            var tLabels = new List<string>();
            foreach (var t in tracks)
            {
                _trackIds.Add(t.id);
                tLabels.Add($"{t.id} {t.name}（{t.role}）" + (MotionEditOps.IsDerivedRole(_editView, t.role) ? " 派生・変更不可" : ""));
            }
            SetChoices(_trackField, tLabels, track == null ? -1 : _trackIds.IndexOf(track.id));
            bool derived = track != null && MotionEditOps.IsDerivedRole(_editView, track.role);
            bool trackOk = track != null && !derived;
            _preWrapField.SetValueWithoutNotify(string.IsNullOrEmpty(track?.preWrapMode) ? WrapUnset : WrapChoice(track.preWrapMode));
            _postWrapField.SetValueWithoutNotify(string.IsNullOrEmpty(track?.postWrapMode) ? WrapUnset : WrapChoice(track.postWrapMode));
            _preWrapField.SetEnabled(trackOk); _postWrapField.SetEnabled(trackOk); _btnTrackRemove.SetEnabled(trackOk);
            _newKindField.SetEnabled(_newRoleField.value == MotionEditDTO.RoleBones);

            // クリップ
            var clips = (track?.clips ?? new List<MotionEditClipDTO>()).Where(c => c != null).ToList();
            var clip = clips.FirstOrDefault(c => c.id == _selClip) ?? clips.FirstOrDefault();
            _selClip = clip?.id;
            _clipIds.Clear();
            var cLabels = new List<string>();
            foreach (var c in clips)
            {
                _clipIds.Add(c.id);
                cLabels.Add($"{c.id} {c.name} [{Fmt(c.start)}–{Fmt(c.start + c.len - 1)}]");
            }
            SetChoices(_clipField, cLabels, clip == null ? -1 : _clipIds.IndexOf(clip.id));
            bool clipOk = trackOk && clip != null;
            if (clip != null)
            {
                _clipStartField.SetValueWithoutNotify(Mathf.RoundToInt(clip.start));
                _clipLenField.SetValueWithoutNotify(Mathf.RoundToInt(clip.len));
                _clipLoopToggle.SetValueWithoutNotify(clip.loop);
                _clipWeightField.SetValueWithoutNotify(clip.weight);
                string bid = (e.bundles ?? new List<MotionEditBundleDTO>()).FirstOrDefault(x => x?.clipIds != null && x.clipIds.Contains(clip.id))?.id;
                _clipEditInfoLabel.text = $"キー rot {clip.rot?.Count ?? 0} / pos {clip.pos?.Count ?? 0} / scl {clip.scl?.Count ?? 0} / w {clip.w?.Count ?? 0}"
                                        + $"　所属バンドル {(bid ?? "なし")}";
            }
            else _clipEditInfoLabel.text = track == null ? "" : "クリップがありません";
            foreach (var x in new VisualElement[] { _clipStartField, _clipLenField, _clipLoopToggle, _clipWeightField, _btnClipRemove })
                x.SetEnabled(clipOk);
            _newClipStartField.SetEnabled(trackOk); _newClipLenField.SetEnabled(trackOk); _btnClipAdd.SetEnabled(trackOk);
            if (!clipOk && track != null) _newClipStartField.SetValueWithoutNotify(Mathf.RoundToInt(_currentTime * FrameRate));

            // チャンネルとキー
            _chIds.Clear();
            var chLabels = new List<string>();
            if (track != null)
                foreach (var ch in MotionEditDTO.IsBoneRole(track.role)
                                   ? new[] { MotionEditOps.ChRot, MotionEditOps.ChPos, MotionEditOps.ChScl }
                                   : new[] { MotionEditOps.ChW })
                { _chIds.Add(ch); chLabels.Add(ChLabel(ch)); }
            if (!_chIds.Contains(_selCh)) _selCh = _chIds.FirstOrDefault() ?? MotionEditOps.ChRot;
            SetChoices(_chField, chLabels, _chIds.IndexOf(_selCh));
            int dim = Dim(_selCh);

            var frames = clip == null ? new List<int>() : KeyFramesOf(clip, _selCh);
            if (clip == null) _selF = 0;
            else if (_selF < 0 || _selF >= clip.len) _selF = frames.Count > 0 ? frames[0] : 0;
            _keyFrames.Clear(); _keyFrames.AddRange(frames);
            SetChoices(_keyField, frames.Select(f => $"f {f}").ToList(), frames.IndexOf(_selF));
            _keyFrameField.SetValueWithoutNotify(_selF);
            bool hasKey = clip != null && frames.Contains(_selF);
            float[] val = null;
            if (clip != null) val = KeyValue(clip, _selCh, _selF)
                                  ?? MotionEditFlattener.SampleTrackChannel(e, _editView.frameRate, track.id, _selCh, clip.start + _selF);
            var vf = new[] { _valX, _valY, _valZ, _valW };
            for (int i = 0; i < 4; i++)
            {
                vf[i].SetValueWithoutNotify(val != null && i < val.Length ? val[i] : 0f);
                vf[i].SetEnabled(clipOk && i < dim);
            }
            _keyStateLabel.text = clip == null ? ""
                : hasKey ? $"{_selCh} のフレーム {_selF}（全体 {Fmt(clip.start + _selF)}）のキー。値を変えると書きます"
                         : $"{_selCh} のフレーム {_selF} にキーはありません（値の欄は今の評価値）";
            foreach (var x in new VisualElement[] { _chField, _keyField, _keyFrameField, _btnCapture, _btnSetChannelKey, _moveToField })
                x.SetEnabled(clipOk);
            _btnRemoveChannelKey.SetEnabled(clipOk && hasKey);
            bool anyKeyAt = clip != null && AnyKeyAt(clip, _selF);
            _btnRemoveKey.SetEnabled(clipOk && anyKeyAt);
            _btnMoveKey.SetEnabled(clipOk && anyKeyAt);
            _moveToField.SetValueWithoutNotify(_selF);

            // 接線
            var compNames = dim == 4 ? new List<string> { "x", "y", "z", "w" } : dim == 3 ? new List<string> { "x", "y", "z" } : new List<string> { "値" };
            _selComp = Mathf.Clamp(_selComp, 0, compNames.Count - 1);
            SetChoices(_compField, compNames, _selComp);
            MotionEditOps.TangentView tv = null;
            if (clipOk && hasKey) tv = MotionEditOps.KeyTangentView(_editView, clip.id, _selCh, _selF, _selComp, out _);
            bool tanOk = tv != null;
            foreach (var x in new VisualElement[] { _compField, _inModeField, _outModeField, _inSlopeField, _outSlopeField, _wmField, _inWeightField, _outWeightField })
                x.SetEnabled(tanOk);
            if (tanOk)
            {
                _inModeField.SetValueWithoutNotify(tv.InMode);
                _outModeField.SetValueWithoutNotify(tv.OutMode);
                _inSlopeField.SetValueWithoutNotify(tv.StoredIn ?? tv.In);
                _outSlopeField.SetValueWithoutNotify(tv.StoredOut ?? tv.Out);
                _wmField.SetValueWithoutNotify(tv.WeightedMode);
                _inWeightField.SetValueWithoutNotify(tv.InWeight);
                _outWeightField.SetValueWithoutNotify(tv.OutWeight);
                _tanInfoLabel.text = $"傾き 入 {(tv.StoredIn.HasValue ? "保存値" : "モードから求めた値")} / 出 {(tv.StoredOut.HasValue ? "保存値" : "モードから求めた値")}"
                                   + $"　重み 入 {(tv.StoredInWeight.HasValue ? "保存値" : "既定 1/3")} / 出 {(tv.StoredOutWeight.HasValue ? "保存値" : "既定 1/3")}";
            }
            else _tanInfoLabel.text = "キーを選ぶと接線を変えられます";

            // バンドル
            var bundles = (e.bundles ?? new List<MotionEditBundleDTO>()).Where(x => x != null).ToList();
            var bundle = bundles.FirstOrDefault(x => x.id == _selBundle) ?? bundles.FirstOrDefault();
            _selBundle = bundle?.id;
            _bundleIds.Clear();
            var bLabels = new List<string>();
            foreach (var x in bundles)
            {
                _bundleIds.Add(x.id);
                bLabels.Add($"{x.id} {x.name}" + (x.layer != null ? $" [レイヤー {x.layer.mode} {x.layer.weight.ToString("0.##", CultureInfo.InvariantCulture)}]" : ""));
            }
            SetChoices(_bundleField, bLabels, bundle == null ? -1 : _bundleIds.IndexOf(bundle.id));
            bool bOk = bundle != null;
            bool layer = bOk && bundle.layer != null;
            _bundleInfoLabel.text = bundle == null ? "バンドルはありません"
                : $"クリップ {string.Join(",", bundle.clipIds ?? new List<string>())}　子 {string.Join(",", bundle.childIds ?? new List<string>())}　親 {bundle.parentId ?? "なし"}";
            _layerToggle.SetValueWithoutNotify(layer);
            _layerModeField.SetValueWithoutNotify(layer && bundle.layer.mode == MotionEditDTO.LayerAdditive ? MotionEditDTO.LayerAdditive : MotionEditDTO.LayerOverride);
            _layerWeightField.SetValueWithoutNotify(layer ? bundle.layer.weight : 1f);
            _layerMuteToggle.SetValueWithoutNotify(layer && bundle.layer.mute);
            _layerSoloToggle.SetValueWithoutNotify(layer && bundle.layer.solo);
            _layerToggle.SetEnabled(bOk);
            foreach (var x in new VisualElement[] { _layerModeField, _layerWeightField, _layerMuteToggle, _layerSoloToggle, _btnLayerUp, _btnLayerDown })
                x.SetEnabled(layer);
            foreach (var x in new VisualElement[] { _btnUngroup, _btnBundleDelete, _btnClone, _cloneShiftField, _btnMkAddBundle })
                x.SetEnabled(bOk);
            _btnMkAddClip.SetEnabled(clipOk);
            var summary = MotionEditFlattener.LayerSummary(e.bundles);
            _layerOrderLabel.text = summary.Count == 0 ? "レイヤーはありません"
                : "重ねる順: " + string.Join(" → ", summary.Select(x => $"{x.id}（{x.weight.ToString("0.##", CultureInfo.InvariantCulture)}{(x.mute ? "・ミュート" : "")}）"));
        }

        // ================================================================
        // 送る
        // ================================================================

        private int ModelIdx => GetModelIndex?.Invoke() ?? 0;

        /// <summary>コマンドを送る。成功なら true。失敗の理由・成功の知らせは状態の欄に出す。</summary>
        private bool Send(PanelCommand cmd, string okMsg)
        {
            if (RunCommand == null) { SetStatus("コマンドの発行口がありません"); return false; }
            string r = RunCommand(cmd);
            if (r != null) { SetStatus($"失敗: {r}"); RefreshEdit(); return false; }
            SetStatus(okMsg);
            RefreshEdit();
            return true;
        }

        /// <summary>作るコマンドを送り、作った id（窓口の lastCreatedId）を返す。</summary>
        private bool SendCreate(PanelCommand cmd, string okMsg, out string id)
        {
            id = null;
            if (!Send(cmd, okMsg)) return false;
            id = Surface?.GetString(Tool, "lastCreatedId") ?? "";
            SetStatus($"{okMsg}（{id}）");
            return !string.IsNullOrEmpty(id);
        }

        private void SendWrap()
        {
            string Pre(DropdownField d) => d.value == WrapUnset ? "" : d.value;
            Send(new MotionEditSetWrapCommand(ModelIdx, _selTrack, Pre(_preWrapField), Pre(_postWrapField), _currentTime), "ラップを変えました");
        }

        private void SendClip()
            => Send(new MotionEditSetClipCommand(ModelIdx, _selClip, _clipStartField.value, _clipLenField.value,
                                                 _clipLoopToggle.value, _clipWeightField.value, _currentTime), "クリップを変えました");

        private void SendTanMode(string side, string mode)
            => Send(new MotionEditSetTangentModeCommand(ModelIdx, _selClip, _selCh, _selF, side, mode, _currentTime), "接線のモードを変えました");

        private void SendLayer()
            => Send(new MotionEditSetLayerCommand(ModelIdx, _selBundle, _layerToggle.value, _layerModeField.value,
                                                  _layerWeightField.value, _layerMuteToggle.value, _layerSoloToggle.value, _currentTime),
                    "レイヤーを変えました");

        private void OnValueEdited()
        {
            var clip = CurrentClip();
            if (clip == null) return;
            if (!KeyFramesOf(clip, _selCh).Contains(_selF))
            {
                SetStatus($"{_selCh} のフレーム {_selF} にキーはありません。「このchにキーを作る」で作ります");
                return;
            }
            Send(new MotionEditSetKeyValueCommand(ModelIdx, _selClip, _selCh, _selF, Values(), _currentTime), "キーの値を変えました");
        }

        private float[] Values()
        {
            var a = new[] { _valX.value, _valY.value, _valZ.value, _valW.value };
            return a.Take(Dim(_selCh)).ToArray();
        }

        private MotionEditClipDTO CurrentClip()
            => _editView?.edit?.tracks?.Where(t => t?.clips != null).SelectMany(t => t.clips).FirstOrDefault(c => c != null && c.id == _selClip);

        // ── 選択の Setter（UI 自動操作。id でもラベルでも選べる）──
        private string SetTrackSel(string s)  => SelectBy(_trackField, _trackIds, s);
        private string SetClipSel(string s)   => SelectBy(_clipField, _clipIds, s);
        private string SetBundleSel(string s) => SelectBy(_bundleField, _bundleIds, s);
        private string SetChannelSel(string s) => SelectBy(_chField, _chIds, s);
        private string SetKeySel(string s)
            => SelectBy(_keyField, _keyFrames.Select(f => f.ToString(CultureInfo.InvariantCulture)).ToList(), s);
        private string SetCompSel(string s)
        {
            int i = _compField.choices.IndexOf(s);
            if (i < 0 && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 && n < _compField.choices.Count) i = n;
            if (i < 0) return $"\"{s}\" は成分にありません（{string.Join(" / ", _compField.choices)}）";
            if (!_compField.enabledInHierarchy) return "キーを選んでいないので成分を選べません";
            _compField.value = _compField.choices[i];
            return null;
        }

        private static string SelectBy(DropdownField d, List<string> ids, string s)
        {
            if (d == null) return "欄がありません";
            int i = ids.IndexOf(s);
            if (i < 0) i = d.choices.IndexOf(s);
            if (i < 0 || i >= ids.Count) return $"\"{s}\" はありません（{string.Join(" / ", ids)}）";
            if (!d.enabledInHierarchy) return "今は選べません";
            d.value = d.choices[i];
            return null;
        }

        // ================================================================
        // 小物
        // ================================================================

        private static int Dim(string ch) => ch == MotionEditOps.ChRot ? 4 : ch == MotionEditOps.ChW ? 1 : 3;

        private static string ChLabel(string ch)
            => ch == MotionEditOps.ChRot ? "rot（回転 x,y,z,w）" : ch == MotionEditOps.ChPos ? "pos（位置）"
             : ch == MotionEditOps.ChScl ? "scl（拡大）" : "w（値）";

        private static string WrapChoice(string m)
            => WrapChoices.FirstOrDefault(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase)) ?? WrapUnset;

        private static string Fmt(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        private static List<int> KeyFramesOf(MotionEditClipDTO c, string ch)
        {
            IEnumerable<float> fs = ch == MotionEditOps.ChW ? (c.w?.Where(k => k != null).Select(k => k.f) ?? Enumerable.Empty<float>())
                : (ch == MotionEditOps.ChRot ? c.rot : ch == MotionEditOps.ChPos ? c.pos : c.scl)?.Where(k => k != null).Select(k => k.f)
                  ?? Enumerable.Empty<float>();
            return fs.Select(f => Mathf.RoundToInt(f)).Distinct().OrderBy(f => f).ToList();
        }

        private static bool AnyKeyAt(MotionEditClipDTO c, int f)
            => KeyFramesOf(c, MotionEditOps.ChRot).Contains(f) || KeyFramesOf(c, MotionEditOps.ChPos).Contains(f)
            || KeyFramesOf(c, MotionEditOps.ChScl).Contains(f) || KeyFramesOf(c, MotionEditOps.ChW).Contains(f);

        private static float[] KeyValue(MotionEditClipDTO c, string ch, int f)
        {
            if (ch == MotionEditOps.ChW)
            {
                var k = c.w?.FirstOrDefault(x => x != null && x.f == f);
                return k == null ? null : new[] { k.v };
            }
            var list = ch == MotionEditOps.ChRot ? c.rot : ch == MotionEditOps.ChPos ? c.pos : c.scl;
            return (float[])list?.FirstOrDefault(x => x != null && x.f == f)?.v?.Clone();
        }

        private static List<string> UncoveredFlat(MotionClipDTO dto)
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in dto.edit?.tracks ?? new List<MotionEditTrackDTO>())
                if (t != null) covered.Add(t.role + "\n" + (t.targetKind ?? "") + "\n" + t.target);
            var r = new List<string>();
            void Flat(string role, string kind, string target)
            {
                if (!covered.Contains(role + "\n" + (kind ?? "") + "\n" + target)) r.Add(role + ":" + target);
            }
            if (dto.bones != null) foreach (var x in dto.bones) if (x != null) Flat(MotionEditDTO.RoleBones, x.targetKind, x.id);
            if (dto.body != null) Flat(MotionEditDTO.RoleBody, MotionClipDTO.TargetBody, MotionClipDTO.BodyTrackId);
            if (dto.lookAt != null) Flat(MotionEditDTO.RoleLookAt, MotionClipDTO.TargetLookAt, MotionClipDTO.LookAtTrackId);
            if (dto.bakedBones != null) foreach (var x in dto.bakedBones) if (x != null) Flat(MotionEditDTO.RoleBakedBones, MotionClipDTO.TargetHumanoid, x.id);
            if (dto.expressions != null)
                foreach (var x in dto.expressions)
                    if (x != null)
                    {
                        if (string.IsNullOrEmpty(x.path)) Flat(MotionEditDTO.RoleExpressions, null, x.name);
                        else r.Add(MotionEditDTO.RoleExpressions + ":" + x.name + "（path あり）");
                    }
            if (dto.muscles != null) foreach (var x in dto.muscles) if (x != null) Flat(MotionEditDTO.RoleMuscles, null, x.name);
            return r;
        }

        private static string[] SplitIds(string s)
            => (s ?? "").Split(new[] { ',', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        private static void AppendId(TextField f, string id)
        {
            if (f == null || string.IsNullOrEmpty(id)) return;
            var ids = SplitIds(f.value).ToList();
            if (!ids.Contains(id)) ids.Add(id);
            f.value = string.Join(",", ids);
        }

        private static void SetChoices(DropdownField d, List<string> labels, int index)
        {
            if (labels.Count == 0) { d.choices = new List<string> { NoneChoice }; d.SetValueWithoutNotify(NoneChoice); return; }
            d.choices = labels;
            // 選んでいるものが一覧に無い（キーの無いフレームを選んでいる）ときは、どれも選んでいない表示にする。
            // 先頭を出すと、選んだように見えるうえ、その先頭を選び直しても値が変わらず選択が進まない。
            d.SetValueWithoutNotify(index >= 0 && index < labels.Count ? labels[index] : NoneChoice);
        }

        private static Label SmallLabel()
        {
            var l = new Label();
            l.style.fontSize = 10; l.style.whiteSpace = WhiteSpace.Normal; l.style.marginBottom = 3;
            return l;
        }

        private static Label Lbl(string t)
        {
            var l = new Label(t);
            l.style.width = 80; l.style.fontSize = 10; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row; r.style.marginBottom = 2;
            parent.Add(r);
            return r;
        }

        private static DropdownField Drop(VisualElement parent, string label, List<string> choices)
        {
            var r = Row(parent);
            r.Add(Lbl(label));
            var d = new DropdownField(new List<string>(choices), 0);
            d.style.flexGrow = 1;
            r.Add(d);
            return d;
        }

        private static IntegerField Int(VisualElement parent, string label)
        {
            var r = Row(parent);
            r.Add(Lbl(label));
            var f = new IntegerField { isDelayed = true };
            f.style.flexGrow = 1;
            r.Add(f);
            return f;
        }

        private static FloatField Float(VisualElement parent, string label)
        {
            var r = Row(parent);
            r.Add(Lbl(label));
            var f = new FloatField { isDelayed = true };
            f.style.flexGrow = 1;
            r.Add(f);
            return f;
        }

        private static FloatField VecField(VisualElement row)
        {
            var f = new FloatField { isDelayed = true };
            f.style.flexGrow = 1; f.style.width = 40;
            row.Add(f);
            return f;
        }

        private static TextField Text(VisualElement parent, string label)
        {
            var r = Row(parent);
            r.Add(Lbl(label));
            var f = new TextField { isDelayed = true };
            f.style.flexGrow = 1;
            r.Add(f);
            return f;
        }

        private static Button Btn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.marginBottom = 2;
            parent.Add(b);
            return b;
        }

        private static Button RowBtn(VisualElement row, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1; b.style.fontSize = 10;
            row.Add(b);
            return b;
        }
    }
}
