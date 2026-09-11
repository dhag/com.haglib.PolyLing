// PlayerBoneEditorSubPanel.cs
// ボーン・描画メッシュ統合 TRS エディタ（旧 BoneEditorSubPanel + ObjectMoveTRSPanel の統合）
// SubPanelScope でボーンのみ / 描画メッシュのみ / 両方 を切り替え可能。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【分割先】このファイルから次へ分けてある。
//   PlayerBoneEditorSubPanel.Actions.cs  ボーンエディタ：クイック操作・スコープ切替・対象取得・Refresh・並べ替え・原点 CSV・姿勢くさび。
//   PlayerBoneEditorSubPanel.Helpers.cs  ボーンエディタ：対象選択ドロップダウン・ボタンアクション・TRS ヘルパー・UI ヘルパー。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class PlayerBoneEditorSubPanel
    {
        // ================================================================
        // スコープ
        // ================================================================

        private enum SubPanelScope { BonesOnly, MeshesOnly, Both }
        private SubPanelScope _scope = SubPanelScope.BonesOnly;

        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext>        GetModel;
        public Func<MeshUndoController>  GetUndoController;
        public Action                    OnRepaint;
        public Action<Vector3>           OnFocusCamera;
        public Func<int>                 GetModelIndex;

        /// <summary>
        /// ObjectMoveTool の共有設定インスタンスを返すコールバック。
        /// 設定: MoveWithChildren / PickBones / PickMeshesNoSkin / PickMeshesSkinned。
        /// サブパネルのチェックボックスとこの設定を双方向で同期する。
        /// </summary>
        public Func<Poly_Ling.Tools.ObjectMoveSettings> GetObjectMoveSettings;

        /// <summary>
        /// ギズモ表示チェックを変えた直後にビューポートのギズモを組み直す要求。
        /// OnRepaint（パネル再描画）とは別経路で、ViewerCore の UpdateGizmoOverlay を呼ぶ。
        /// </summary>
        public Action OnGizmoRefresh;

        /// <summary>
        /// 「拡大縮小をベイク」実行要求。戻り値は結果メッセージ
        /// （適用件数とスキップ理由）で、そのままパネル内に表示する。
        /// </summary>
        public Func<string> RequestBakeObjectScale;

        // PanelContext 経由でコマンドを送信
        private PanelContext _panelContext;

        public void SetContext(PanelContext ctx) => _panelContext = ctx;

        private void SendCommand(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ================================================================
        // UI 要素
        // ================================================================

        // スコープタブ
        private Button _tabBones, _tabMeshes;
        private VisualElement _moveOptionsSection;      // ボーン専用: スキンモード A/B/C
        private VisualElement _commonMoveSection;      // 両タブ共通: 子を一緒に移動
        private VisualElement _meshMoveOptionsSection; // メッシュ専用: 原点だけ移動

        // 共通
        private Label         _warningLabel;
        private Label         _selectionCountLabel;

        // ── 対象選択（スコープ追従・全タブ共通）────────────────────
        //   3D 上でピックしにくい対象をリストから選ぶための入口。
        //   選択は 3D ピックやメッシュリストと同じ SelectMeshCommand 経由で行う。
        private VisualElement _targetSection;
        private DropdownField _boneDropdown;             // 対象選択ドロップダウン
        private bool          _suppressBoneDropdown;
        private readonly List<int>          _targetChoiceMasters    = new List<int>();
        private readonly List<MeshCategory> _targetChoiceCategories = new List<MeshCategory>();

        // ── ボーン専用 ──────────────────────────────────────────────
        private VisualElement _boneSection;

        private Label         _boneNameLabel;
        private IntegerField  _masterIndexField;
        private Label         _boneIndexLabel;
        private DropdownField _parentBoneDropdown;
        private List<int>     _parentChoiceMasters = new List<int>();
        private bool          _suppressBoneEdit;
        private Label         _worldPosLabel;

        private Toggle        _bonePoseActiveToggle;
        private Button        _btnInitPose;
        private Button        _btnResetLayers;
        private Button        _btnBakePose;
        private Button        _btnFreezePose;
        private VisualElement _bonePoseSection;

        private Button        _btnReset;
        private Button        _btnFocus;

        // ── 共通 TRS ────────────────────────────────────────────────
        private FloatField _posX, _posY, _posZ;
        private FloatField _rotX, _rotY, _rotZ;
        private Slider     _rotSliderX, _rotSliderY, _rotSliderZ;
        private FloatField _sclX, _sclY, _sclZ;
        private VisualElement _rotSection, _sclSection;
        private bool       _suppressTRS;

        // IgnorePose（描画メッシュ含む場合）
        private Toggle        _ignorePoseToggle;
        private VisualElement _ignorePoseRow;


        // 原点CSV（オブジェクト姿勢タブ専用）
        private Toggle        _originIncludeRotToggle;

        // 姿勢くさび（オブジェクト姿勢タブ専用）
        private FloatField    _wedgeLengthField;

        // ── ObjectMoveSettings 連動チェックボックス ───────────────
        // BoneInputHandler 廃止に伴い、ObjectMoveTool のピック対象を
        // ここから操作する。GetObjectMoveSettings() 経由で同一インスタンスを共有。
        private Toggle        _toggleMoveWithChildren;
        private Toggle        _toggleOriginOnly;
        private Toggle        _toggleShowMoveGizmo;
        private Toggle        _toggleShowRotationGizmo;
        private Toggle        _toggleModeA;
        private Toggle        _toggleModeB;
        private Toggle        _toggleModeC;
        private bool          _suppressMoveSettings;

        private Label _statusLabel;
        private Label _bakeScaleMsgLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            // ── スコープタブ ─────────────────────────────────────────
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom  = 6;
            _tabBones  = MakeScopeTab("ボーン",               () => SetScope(SubPanelScope.BonesOnly));
            _tabMeshes = MakeScopeTab("描画オブジェクトの姿勢", () => SetScope(SubPanelScope.MeshesOnly));
            _tabBones.style.flexGrow = _tabMeshes.style.flexGrow = 1;
            tabRow.Add(_tabBones); tabRow.Add(_tabMeshes);
            root.Add(tabRow);
            UpdateTabHighlight();

            // ── ピック対象・挙動オプション ───────────────────────────
            // ObjectMoveTool の ObjectMoveSettings と同期するチェックボックス 4 個。
            // BoneInputHandler 廃止後、ボーンエディタ表示中のピック対象を
            // このサブパネルから操作する。
            _moveOptionsSection = new VisualElement();
            _moveOptionsSection.style.marginBottom = 6;

            _toggleMoveWithChildren  = new Toggle("子を一緒に移動") { value = true };
            _toggleOriginOnly        = new Toggle("原点だけ移動") { value = false };
            _toggleShowMoveGizmo     = new Toggle("移動ギズモを表示") { value = true };
            _toggleShowRotationGizmo = new Toggle("回転ギズモを表示") { value = false };
            _toggleModeA             = new Toggle("ボーンだけ動かす（スキン固定）") { value = true };
            _toggleModeB             = new Toggle("スキンごと動かして確定（焼き込み）") { value = false };
            _toggleModeC             = new Toggle("ポーズ（一時）") { value = false };
            _toggleMoveWithChildren.style.color  = new StyleColor(Color.white);
            _toggleOriginOnly.style.color        = new StyleColor(Color.white);
            _toggleShowMoveGizmo.style.color     = new StyleColor(Color.white);
            _toggleShowRotationGizmo.style.color = new StyleColor(Color.white);
            _toggleModeA.style.color             = new StyleColor(Color.white);
            _toggleModeB.style.color             = new StyleColor(Color.white);
            _toggleModeC.style.color             = new StyleColor(Color.white);

            _toggleShowMoveGizmo.tooltip =
                "OFF にすると矢印と中央ハンドルを消し、当たり判定も止める"
                + "（オブジェクト原点をクリックで選びやすくする）";
            _toggleShowRotationGizmo.tooltip =
                "OFF にすると回転リングを消し、当たり判定も止める";

            _toggleMoveWithChildren.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveWithChildren = e.newValue;
            });
            _toggleOriginOnly.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.OriginOnly = e.newValue;

                // 「原点だけ移動」を ON にしたときだけ「子を一緒に移動」を OFF にする。
                // OFF にしたときは連動しない（子ごと動かすかは利用者が決める）。
                if (e.newValue)
                {
                    if (s != null) s.MoveWithChildren = false;
                    _suppressMoveSettings = true;
                    try { _toggleMoveWithChildren?.SetValueWithoutNotify(false); }
                    finally { _suppressMoveSettings = false; }
                }

                ApplyOriginOnlyVisibility();
            });
            _toggleShowMoveGizmo.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.AllowMoveGizmo = e.newValue;
                OnGizmoRefresh?.Invoke();
            });
            _toggleShowRotationGizmo.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.AllowRotationGizmo = e.newValue;
                OnGizmoRefresh?.Invoke();
            });
            _toggleModeA.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeA.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind;
                _toggleModeB.SetValueWithoutNotify(false);
                _toggleModeC.SetValueWithoutNotify(false);
                Refresh();
            });
            _toggleModeB.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeB.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.SkinBakeRebind;
                _toggleModeA.SetValueWithoutNotify(false);
                _toggleModeC.SetValueWithoutNotify(false);
                Refresh();
            });
            _toggleModeC.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeC.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.PoseLayer;
                _toggleModeA.SetValueWithoutNotify(false);
                _toggleModeB.SetValueWithoutNotify(false);
                Refresh();
            });

            // 原点だけ移動: MeshFilter 対象 → 「描画オブジェクトの姿勢」タブ専用。
            // 「子を一緒に移動」より上に置く（下の連動と読み順を合わせるため）。
            _meshMoveOptionsSection = new VisualElement();
            _meshMoveOptionsSection.style.marginBottom = 6;
            _meshMoveOptionsSection.Add(_toggleOriginOnly);
            root.Add(_meshMoveOptionsSection);

            // 子を一緒に移動: ボーン/メッシュ両方に適用 → 共通（両タブで表示）
            // ギズモ表示チェックも同じ ObjectMoveTool を対象にするので同居させる。
            _commonMoveSection = new VisualElement();
            _commonMoveSection.style.marginBottom = 6;
            _commonMoveSection.Add(_toggleMoveWithChildren);
            _commonMoveSection.Add(_toggleShowMoveGizmo);
            _commonMoveSection.Add(_toggleShowRotationGizmo);
            _commonMoveSection.Add(BuildQuickOffsetRow());
            root.Add(_commonMoveSection);

            // スキンモード A/B/C: 「ボーン」タブ専用
            _moveOptionsSection.Add(MakeSecLabel("移動モード"));
            _moveOptionsSection.Add(_toggleModeA);
            _moveOptionsSection.Add(_toggleModeB);
            _moveOptionsSection.Add(_toggleModeC);
            root.Add(_moveOptionsSection);

            // ── 警告・選択カウント ───────────────────────────────────
            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            _selectionCountLabel = new Label();
            _selectionCountLabel.style.color        = new StyleColor(Color.white);
            _selectionCountLabel.style.marginBottom = 4;
            _selectionCountLabel.style.fontSize     = 10;
            root.Add(_selectionCountLabel);

            // ── 対象選択（全タブ共通）───────────────────────────────
            _targetSection = new VisualElement();

            var dropLabel = new Label("対象選択:");
            dropLabel.style.color    = new StyleColor(Color.white);
            dropLabel.style.fontSize = 10;
            _targetSection.Add(dropLabel);

            _boneDropdown = new DropdownField();
            _boneDropdown.style.marginBottom = 4;
            _boneDropdown.RegisterValueChangedCallback(_ => OnTargetDropdownChanged());
            _targetSection.Add(_boneDropdown);
            root.Add(_targetSection);

            // ── ボーン専用セクション ─────────────────────────────────
            _boneSection = new VisualElement();

            // リセット / フォーカス
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            _btnReset = new Button(OnResetPose) { text = "ポーズリセット" };
            _btnReset.style.flexGrow    = 1;
            _btnReset.style.marginRight = 4;
            _btnReset.style.height      = 22;
            _btnFocus = new Button(OnFocusBone) { text = "フォーカス" };
            _btnFocus.style.flexGrow = 1;
            _btnFocus.style.height   = 22;
            btnRow.Add(_btnFocus);
            _boneSection.Add(btnRow);

            // ボーンポーズ
            _bonePoseSection = new VisualElement();
            _bonePoseSection.Add(MakeSep());
            _bonePoseSection.Add(MakeSecLabel("ボーンポーズ"));
            _bonePoseSection.Add(_btnReset);

            _bonePoseActiveToggle = new Toggle("ポーズ有効") { value = false };
            _bonePoseActiveToggle.style.marginBottom = 4;
            _bonePoseActiveToggle.RegisterValueChangedCallback(e =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new SetBonePoseActiveCommand(
                    GetModelIndex?.Invoke() ?? 0, model.SelectedBoneIndices.ToArray(), e.newValue));
            });
            _bonePoseSection.Add(_bonePoseActiveToggle);

            var poseRow = new VisualElement();
            poseRow.style.flexDirection = FlexDirection.Row;
            poseRow.style.marginBottom  = 4;
            _btnInitPose    = MakeSmallBtn("初期化");
            _btnResetLayers = MakeSmallBtn("レイヤークリア");
            _btnBakePose    = MakeSmallBtn("BindPoseへベイク");
            _btnInitPose.style.flexGrow     = 1; _btnInitPose.style.marginRight    = 2;
            _btnResetLayers.style.flexGrow  = 1; _btnResetLayers.style.marginRight = 2;
            _btnBakePose.style.flexGrow     = 1;
            _btnInitPose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new InitBonePoseCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            _btnResetLayers.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new ResetBonePoseLayersCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            _btnBakePose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new BakePoseToBindPoseCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            poseRow.Add(_btnInitPose); poseRow.Add(_btnResetLayers); poseRow.Add(_btnBakePose);
            _bonePoseSection.Add(poseRow);

            _btnFreezePose = new Button(() =>
            {
                var model = GetModel?.Invoke();
                if (model == null) return;
                SendCommand(new FreezeCurrentPoseCommand(GetModelIndex?.Invoke() ?? 0));
            }) { text = "この姿勢で確定（焼き込み）" };
            _btnFreezePose.style.height    = 22;
            _btnFreezePose.style.marginTop = 4;
            _bonePoseSection.Add(_btnFreezePose);

            _boneSection.Add(_bonePoseSection);

            // ボーン詳細情報
            _boneSection.Add(MakeSep());
            AddRow(_boneSection, "ボーン名",    out _boneNameLabel);
            AddIntRow(_boneSection, "マスターIdx", out _masterIndexField, OnMasterIndexChanged);
            AddRow(_boneSection, "ボーンIdx",   out _boneIndexLabel);
            AddDropdownRow(_boneSection, "親ボーン", out _parentBoneDropdown, OnParentBoneChanged);
            _boneSection.Add(MakeSecLabel("ワールド座標"));
            AddRow(_boneSection, "位置",        out _worldPosLabel);

            root.Add(_boneSection);

            // ── TRS ─────────────────────────────────────────────────
            root.Add(MakeSep());
            root.Add(MakeSecLabel("位置"));
            AddXYZFields(root, "pos", out _posX, out _posY, out _posZ);
            RegTF(_posX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_posY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_posZ, SetBoneTransformValueCommand.Field.PositionZ);

            _rotSection = new VisualElement();
            _rotSection.Add(MakeSecLabel("回転 (°)"));
            AddXYZFields(_rotSection, "rot", out _rotX, out _rotY, out _rotZ);
            RegTF(_rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegTF(_rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegTF(_rotZ, SetBoneTransformValueCommand.Field.RotationZ);
            _rotSliderX = MakeRotSlider(); _rotSection.Add(_rotSliderX);
            _rotSliderY = MakeRotSlider(); _rotSection.Add(_rotSliderY);
            _rotSliderZ = MakeRotSlider(); _rotSection.Add(_rotSliderZ);
            RegRotSlider(_rotSliderX, _rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegRotSlider(_rotSliderY, _rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegRotSlider(_rotSliderZ, _rotZ, SetBoneTransformValueCommand.Field.RotationZ);
            root.Add(_rotSection);

            _sclSection = new VisualElement();
            _sclSection.Add(MakeSecLabel("スケール"));
            AddXYZFields(_sclSection, "scl", out _sclX, out _sclY, out _sclZ);
            RegTF(_sclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_sclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_sclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            // ローカル拡大縮小を頂点位置へ畳み込み、スケールを (1,1,1) に戻す。
            // _sclSection に入れているため「原点だけ移動」時は一緒に隠れる。
            var bakeScaleBtn = new Button(() =>
            {
                string msg = RequestBakeObjectScale?.Invoke() ?? "";
                if (_bakeScaleMsgLabel != null) _bakeScaleMsgLabel.text = msg;
                OnRepaint?.Invoke();
            })
            { text = "拡大縮小をベイク" };
            bakeScaleBtn.style.marginTop = 4;
            bakeScaleBtn.tooltip =
                "選択中メッシュのローカル拡大縮小を頂点位置へ畳み込み、スケールを 1,1,1 に戻す" +
                "（子を持つメッシュ・スキンドメッシュは対象外）";
            _sclSection.Add(bakeScaleBtn);

            _bakeScaleMsgLabel = new Label();
            _bakeScaleMsgLabel.style.fontSize  = 10;
            _bakeScaleMsgLabel.style.color     = new StyleColor(new Color(1f, 0.75f, 0.4f));
            _bakeScaleMsgLabel.style.whiteSpace = WhiteSpace.Normal;
            _bakeScaleMsgLabel.style.marginTop = 2;
            _sclSection.Add(_bakeScaleMsgLabel);

            root.Add(_sclSection);

            // IgnorePose
            _ignorePoseRow = new VisualElement();
            _ignorePoseRow.Add(MakeSep());
            _ignorePoseToggle = new Toggle("姿勢無視(アーマチャ)");
            _ignorePoseToggle.style.color = new StyleColor(Color.white);
            _ignorePoseToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var indices = GetTargetIndices();
                if (indices.Length > 0)
                    SendCommand(new SetIgnorePoseCommand(GetModelIndex?.Invoke() ?? 0, indices, e.newValue));
            });
            _ignorePoseRow.Add(_ignorePoseToggle);


            // 原点の途中経過を CSV で退避・復元する（オブジェクト姿勢タブ専用）
            var originIoRow = new VisualElement();
            originIoRow.style.flexDirection = FlexDirection.Row;
            originIoRow.style.marginTop     = 2;

            var originExportBtn = new Button(ExportObjectOriginsCsv) { text = "原点CSV書出" };
            var originImportBtn = new Button(ImportObjectOriginsCsv) { text = "原点CSV読込" };
            originExportBtn.style.flexGrow = 1;
            originImportBtn.style.flexGrow = 1;
            originExportBtn.tooltip = "全メッシュの原点(位置)を CSV に書き出す（回転は下のチェックで任意）";
            originImportBtn.tooltip = "CSV の原点(位置)を名前一致で適用する（原点だけ移動・子は動かさない）\n" +
                                      "CSV に載っていないオブジェクトと、モデルに無い名前はどちらも無視する";

            originIoRow.Add(originExportBtn);
            originIoRow.Add(originImportBtn);

            // 回転列（rotX,rotY,rotZ）を書出・読込の対象にするか。
            // 位置だけの旧 CSV も読めるよう、行ごとに列があるかを見て判断する。
            _originIncludeRotToggle = new Toggle("回転(°)も対象") { value = false };
            _originIncludeRotToggle.style.color = new StyleColor(Color.white);
            _originIncludeRotToggle.tooltip =
                "書出: rotX,rotY,rotZ 列を足す / 読込: 回転列がある行だけ回転も適用する" +
                "（列が無い行・オフのときは位置だけ）";

            _ignorePoseRow.Add(_originIncludeRotToggle);
            _ignorePoseRow.Add(originIoRow);

            // ── 姿勢くさび: UI のみ抑止（2026-09 時点）────────────────
            //
            // 【なぜ出さないか】
            //   姿勢の可視化は既に MeshSceneRenderer が持っている。
            //   同ファイルの「くさび形状（ボーン表示・メッシュオブジェクト原点表示で共用）」は
            //   ObjectPoseWedgeShape と同じ軸規約・同じ「+Z を広くして 180 度の自己対称を壊す」
            //   設計で、表示トグルの「選択M原点 / 非選M原点」で既に描かれている。
            //   こちらは同じ形状を実メッシュとして生成し直す二重実装だった。
            //   運ぶ情報（位置＋回転）も原点CSVと同じで、CSV の方が対象範囲が広い。
            //
            // 【何を残したか】
            //   ボタンとフィールドを画面へ足すのをやめただけで、
            //   ObjectPoseWedgeGenerator / Inserter / Reader / Shape、
            //   Generate/ApplyObjectPoseWedgesCommand、ディスパッチャの処理、
            //   原点CSV・Tポーズ側のくさび除外規則はすべてそのまま残してある。
            //
            // 【戻すとき】
            //   下のコメントアウトを解除するだけでよい。_wedgeLengthField は
            //   生成だけ済ませてあるので、GenerateObjectPoseWedges は今のまま動く。
            _wedgeLengthField = new FloatField("長さ")
            {
                value = Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeGenerator.DefaultWedgeLength
            };
            _wedgeLengthField.tooltip =
                "くさびの全長。実際の大きさは、これに各オブジェクトの拡大率平均を掛けたものになる";
            _wedgeLengthField.style.marginBottom = 2;

            // _ignorePoseRow.Add(MakeSecLabel("姿勢くさび"));
            // _ignorePoseRow.Add(_wedgeLengthField);
            //
            // var wedgeIoRow = new VisualElement();
            // wedgeIoRow.style.flexDirection = FlexDirection.Row;
            //
            // var wedgeGenBtn = new Button(GenerateObjectPoseWedges) { text = "姿勢くさび生成" };
            // var wedgeGetBtn = new Button(ImportObjectPoseWedges)   { text = "姿勢くさび取込" };
            // wedgeGenBtn.style.flexGrow = 1;
            // wedgeGetBtn.style.flexGrow = 1;
            // wedgeIoRow.Add(wedgeGenBtn);
            // wedgeIoRow.Add(wedgeGetBtn);
            // _ignorePoseRow.Add(wedgeIoRow);

            root.Add(_ignorePoseRow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 6;
            root.Add(_statusLabel);
        }
    }
}
