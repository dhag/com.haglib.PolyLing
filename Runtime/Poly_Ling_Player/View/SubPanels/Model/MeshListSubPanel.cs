// MeshListSubPanel.cs
// MeshListPanelV2 のランタイムポート。
// エディタ依存APIを以下のように置換:
//   EditorApplication.delayCall     → _root.schedule.Execute
//   EditorUtility.DisplayDialog     → 確認なし即実行
//   PopupField<int>                 → DropdownField + インデックス変換
//   AssetDatabase / UXML / USS      → コードによる UI 構築
//   EditorWindow / CreateGUI        → Build(VisualElement) + SetContext
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【分割先】このファイルから次へ分けてある。
//   MeshListSubPanel.BoneMorph.cs  オブジェクトリストのサブパネル：BonePose・モーフエディタ・BoneTransform。
//   MeshListSubPanel.BuildUI.cs    オブジェクトリストのサブパネル：UI 構築（ビューポート操作モード・姿勢調整・名称一括変更の UI）。
//   MeshListSubPanel.Helpers.cs    オブジェクトリストのサブパネル：ヘルパーと UI パーツ生成。
//   MeshListSubPanel.Refresh.cs    オブジェクトリストのサブパネル：表示変更への追従・ツリー更新・詳細パネル。
//   MeshListSubPanel.Selection.cs  オブジェクトリストのサブパネル：選択・D&D・ボタンイベント・選択辞書・名称一括変更。
//   MeshListSubPanel.TreeView.cs   オブジェクトリストのサブパネル：ツリービューの構築（タブ・行の生成とバインド・行内ボタン・ミラー欄・インデント）。
//   SummaryDragValidator.cs        概要ツリーの D&D 検証（MeshListSubPanel から分離）。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;
using PlayerIoUiKit        = Poly_Ling.Player.PlayerIoUiKit;
using PlayerUiPrefs        = Poly_Ling.Player.PlayerUiPrefs;
using ObjectMoveSettings   = Poly_Ling.Tools.ObjectMoveSettings;
using ParameterLimits      = Poly_Ling.Core.ParameterLimits;
using RecentPaths          = Poly_Ling.Core.RecentPaths;
using PartsDictionaryPath  = Poly_Ling.Core.PartsDictionaryPath;
using MeshRenameCsvHelper  = Poly_Ling.UI.MeshRenameCsvHelper;
// UI 自動操作の属性（Poly_Ling.Player にある）。このファイルだけ名前空間が MeshListV2 なので別名で取り込む。
using UiControl            = Poly_Ling.Player.UiControlAttribute;
using UiSafety             = Poly_Ling.Player.UiSafety;

namespace Poly_Ling.MeshListV2
{
    public partial class MeshListSubPanel
    {
        // ================================================================
        // レンジ（上下限）
        //
        // 実体は ParameterLimits（persistentDataPath の CSV）にあり、ここでは
        // キーを引くだけにする。同じキーを PanelCommand の PLParam(LimitKey) が
        // 指すので、UI とスキーマで範囲の定義が1箇所になる。
        // ================================================================

        private static float MorphWeightMin => ParameterLimits.GetF("MorphPreview.Weight.Min");
        private static float MorphWeightMax => ParameterLimits.GetF("MorphPreview.Weight.Max");

        private enum TabType { Drawable, Bone, Morph, RigidBody, Joint }

        // ================================================================
        // ビューポート操作モード（3択）
        // ================================================================

        /// <summary>
        /// オブジェクトリストを開いている間、ビューポートの左ドラッグを何に使うか。
        /// None       : 3D 操作なし（視点操作だけ）
        /// SelectElem : 頂点・辺・面の選択専用（従来の「ビューポートで選択する」ON 相当）
        /// ObjectPose : オブジェクト原点の選択と姿勢調整（描画オブジェクトの姿勢と同じ）
        /// </summary>
        public enum ViewportOpMode { None = 0, SelectElem = 1, ObjectPose = 2 }

        private const string ViewportOpModeKey = "MeshList.ViewportOpMode";

        private ViewportOpMode _viewportOpMode = ViewportOpMode.ObjectPose;
        [UiControl("viewportOp.none", Safety = UiSafety.SafeWrite, Description = "ビューポート操作なし（視点操作だけ）")]
        private Button _btnOpNone;
        [UiControl("viewportOp.selectElement", Safety = UiSafety.SafeWrite, Description = "ビューポートで頂点・辺・面を選ぶ")]
        private Button _btnOpSelect;
        [UiControl("viewportOp.objectPose", Safety = UiSafety.SafeWrite, Description = "ビューポートでオブジェクト原点の選択と姿勢調整をする")]
        private Button _btnOpPose;

        /// <summary>現在のビューポート操作モード。ViewerCore が入場時に読む。</summary>
        public ViewportOpMode CurrentViewportOpMode => _viewportOpMode;

        /// <summary>ビューポート操作モードが変わったときに呼ぶ（ViewerCore が配線する）。</summary>
        public Action<ViewportOpMode> OnViewportOpModeChanged;

        /// <summary>ObjectMoveTool の共有設定を返す（姿勢調整チェックの実体）。</summary>
        public Func<ObjectMoveSettings> GetObjectMoveSettings;

        /// <summary>ギズモ表示チェックを変えた直後にビューポートのギズモを組み直す要求。</summary>
        public Action OnGizmoRefresh;

        // 姿勢調整チェック（ObjectMoveSettings と双方向同期）
        // UI 自動操作の ID は "meshList.<下の Id>"（UiControlAttribute.cs）。
        // このパネルは「スキンドメッシュ」チェックで簡易表示と詳細表示が変わり、
        // 詳細表示のときだけタブ（メッシュ／ボーン／モーフ／剛体／Joint）が出る。
        // タブでだけ出る欄には、詳細表示にしてそのタブへ切り替える表示の下準備を付ける。
        [UiControl("originOnly", Description = "原点だけ移動する")]
        private Toggle _toggleOriginOnly;
        [UiControl("moveWithChildren", Description = "子を一緒に移動する")]
        private Toggle _toggleMoveWithChildren;
        [UiControl("showMoveGizmo", Description = "移動ギズモを表示する")]
        private Toggle _toggleShowMoveGizmo;
        [UiControl("showRotationGizmo", Description = "回転ギズモを表示する")]
        private Toggle _toggleShowRotationGizmo;
        private bool   _suppressMoveSettings;

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素（エディタ版と同名）
        // ================================================================

        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("tab.drawable", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDetailMode), Description = "「メッシュ」タブ（詳細表示のときだけ出る）")]
        private Button _tabDrawable;
        [UiControl("tab.bone", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDetailMode), Description = "「ボーン」タブ")]
        private Button _tabBone;
        [UiControl("tab.morph", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDetailMode), Description = "「モーフ」タブ")]
        private Button _tabMorph;
        [UiControl("tab.rigidBody", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDetailMode), Description = "「剛体」タブ")]
        private Button _tabRigidBody;
        [UiControl("tab.joint", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDetailMode), Description = "「Joint」タブ")]
        private Button _tabJoint;
        [UiControl(Ignore = true)]
        private VisualElement _mainContent;
        [UiControl(Ignore = true)]
        private VisualElement _morphEditor;
        [UiControl("tree", Description = "オブジェクトの一覧（一覧の行番号）")]
        private TreeView _treeView;

        // リスト高さ（下端ドラッグで手動リサイズ）: PlayerPrimitiveMeshSubPanel の AddProfileResizeHandle 準拠
        // 上限は設けない。下限のみ。
        private const float TreeBaseHeight   = 200f;   // 従来の初期値
        private const float TreeInitialScale = 4f;     // 初期は基準の4倍を上限に、可視行が収まる高さへ
        private float _treeHeight = TreeBaseHeight;
        private const float TreeMinHeight = 80f;
        private bool  _treeHeightUserAdjusted;         // 手動リサイズ後は自動調整しない
        private float _morphListHeight = 140f;
        private const float MorphListMinHeight = 60f;
        [UiControl("count", Safety = UiSafety.ReadOnly, Description = "件数")]
        private Label _countLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;
        [UiControl("showInfo", Description = "情報を表示する")]
        private Toggle _showInfoToggle;
        [UiControl("showMirrorSide", Description = "ミラーも表示する")]
        private Toggle _showMirrorSideToggle;
        [UiControl("filter", Description = "一覧の絞り込み")]
        private TextField _filterField;

        [UiControl(Ignore = true)]
        private Foldout _detailFoldout;
        [UiControl("detail.name", Description = "選択中のオブジェクトの名前（「変更」で確定）")]
        private TextField _meshNameField;
        [UiControl("detail.applyName", Safety = UiSafety.SafeWrite, Description = "名前の変更を確定する")]
        private Button _btnApplyMeshName;
        [UiControl("detail.vertexCount", Safety = UiSafety.ReadOnly, Description = "頂点数")]
        private Label _vertexCountLabel;
        [UiControl("detail.faceCount", Safety = UiSafety.ReadOnly, Description = "面数")]
        private Label _faceCountLabel;
        [UiControl("detail.triangleCount", Safety = UiSafety.ReadOnly, Description = "三角形の数")]
        private Label _triCountLabel;
        [UiControl("detail.quadCount", Safety = UiSafety.ReadOnly, Description = "四角形の数")]
        private Label _quadCountLabel;
        [UiControl("detail.ngonCount", Safety = UiSafety.ReadOnly, Description = "5 角以上の面の数")]
        private Label _ngonCountLabel;
        [UiControl("detail.ignorePose", Description = "姿勢無視（アーマチャ）")]
        private Toggle _ignorePoseToggle;
        [UiControl("detail.preserveNormals", Description = "法線を保持する（再計算しない）")]
        private Toggle _preserveNormalsToggle;
        [UiControl("detail.mirrorBranchRoot", Description = "ミラー分岐ルートにする")]
        private Toggle _mirrorBranchRootToggle;
        // ミラーモード（なし/分離/結合）。⇆ ボタンは有無だけを切り替えるので、
        // 結合(2) の指定はここで行う。
        [UiControl("detail.mirrorMode", Description = "ミラーモード（なし / 分離 / 結合）")]
        private DropdownField _mirrorModeDropdown;
        private static readonly List<string> MirrorModeChoices =
            new List<string> { "なし", "分離", "結合" };
        [UiControl(Ignore = true)]
        private VisualElement _indexInfo;
        [UiControl("detail.boneIndex", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "boneIndex（ボーンタブ）")]
        private Label _boneIndexLabel;
        [UiControl("detail.masterIndex", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "masterIndex（ボーンタブ）")]
        private Label _masterIndexLabel;

        [UiControl(Ignore = true)]
        private VisualElement _bonePoseSection;
        [UiControl(Ignore = true)]
        private Foldout _poseFoldout;
        [UiControl(Ignore = true)]
        private Foldout _bindposeFoldout;
        [UiControl("pose.active", Reveal = nameof(RevealBoneTab), Description = "ポーズを有効にする")]
        private Toggle _poseActiveToggle;
        [UiControl("pose.rest.positionX", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の位置 X")]
        private FloatField _restPosX;
        [UiControl("pose.rest.positionY", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の位置 Y")]
        private FloatField _restPosY;
        [UiControl("pose.rest.positionZ", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の位置 Z")]
        private FloatField _restPosZ;
        [UiControl("pose.rest.rotationX", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 X（度）")]
        private FloatField _restRotX;
        [UiControl("pose.rest.rotationY", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 Y（度）")]
        private FloatField _restRotY;
        [UiControl("pose.rest.rotationZ", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 Z（度）")]
        private FloatField _restRotZ;
        [UiControl("pose.rest.rotationSliderX", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 X のスライダー")]
        private Slider _restRotSliderX;
        [UiControl("pose.rest.rotationSliderY", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 Y のスライダー")]
        private Slider _restRotSliderY;
        [UiControl("pose.rest.rotationSliderZ", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の回転 Z のスライダー")]
        private Slider _restRotSliderZ;
        [UiControl("pose.rest.scaleX", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の拡大縮小 X")]
        private FloatField _restSclX;
        [UiControl("pose.rest.scaleY", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の拡大縮小 Y")]
        private FloatField _restSclY;
        [UiControl("pose.rest.scaleZ", Reveal = nameof(RevealBoneTab), Description = "静止姿勢の拡大縮小 Z")]
        private FloatField _restSclZ;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _poseLayersContainer;
        [UiControl("pose.noLayers", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "レイヤーが無いときの表示")]
        private Label _poseNoLayersLabel;
        [UiControl("pose.resultPosition", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "合成後の位置")]
        private Label _poseResultPos;
        [UiControl("pose.resultRotation", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "合成後の回転")]
        private Label _poseResultRot;
        [UiControl("pose.init", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealBoneTab), Description = "ポーズを初期化する")]
        private Button _btnInitPose;
        [UiControl("pose.resetLayers", Safety = UiSafety.Destructive, Reveal = nameof(RevealBoneTab), Description = "ポーズのレイヤーをクリアする")]
        private Button _btnResetLayers;
        [UiControl("bindPose.position", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "BindPose の位置")]
        private Label _bindposePos;
        [UiControl("bindPose.rotation", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "BindPose の回転")]
        private Label _bindposeRot;
        [UiControl("bindPose.scale", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealBoneTab), Description = "BindPose の拡大縮小")]
        private Label _bindposeScl;
        [UiControl("pose.bakeToBindPose", Safety = UiSafety.Destructive, Reveal = nameof(RevealBoneTab), Description = "今のポーズを BindPose へベイクする")]
        private Button _btnBakePose;
        private bool _isSyncingPoseUI;

        // 詳細モード切り替え（エディタ版「detail-mode-toggle」＝「スキンドメッシュ」に名称変更）
        [UiControl("detailMode", Description = "スキンドメッシュ（オンでタブ付きの詳細表示になる）")]
        private Toggle _detailModeToggle;
        [UiControl(Ignore = true)]
        private VisualElement _tabHeader;

        // ツリーのインデント幅。Unity 既定のままだと深い階層で名前が右へ寄りすぎるため、
        // 既定値を狭めに取り、スライダーで調整できるようにする。
        private const float TreeIndentDefault = 8f;
        private const float TreeIndentMin     = 0f;
        private const float TreeIndentMax     = 24f;
        private float  _treeIndentWidth = TreeIndentDefault;
        [UiControl("indentWidth", Description = "ツリーのインデント幅")]
        private Slider _indentSlider;
        [UiControl("indentValue", Safety = UiSafety.ReadOnly, Description = "インデント幅の表示")]
        private Label  _indentValueLabel;

        // 行内ボタンを押した瞬間の Ctrl 状態（MakeTreeItem で登録した PointerDown が更新する）
        private bool _rowCtrlDown;

        // 選択辞書（オブジェクト選択辞書）の適用
        [UiControl("selectionSet", Description = "オブジェクト選択辞書")]
        private DropdownField _selDicDropdown;
        [UiControl("selectionSet.apply", Safety = UiSafety.SafeWrite, Description = "選んだ選択辞書を適用する")]
        private Button _btnSelDicApply;
        [UiControl("selectionSet.add", Safety = UiSafety.SafeWrite, Description = "選んだ選択辞書を今の選択へ足す")]
        private Button _btnSelDicAdd;
        private readonly List<string> _selDicNames = new List<string>();

        // 名称一括変更（旧名→新名 CSV）
        [UiControl(Ignore = true)]
        private Foldout   _renameFoldout;
        [UiControl("rename.path", Description = "名称一括変更の CSV パス（ダイアログの初期値として使う）")]
        private TextField _renamePathField;
        [UiControl("rename.status", Safety = UiSafety.ReadOnly, Description = "名称一括変更の状態")]
        private Label     _renameStatusLabel;
        [UiControl("rename.template", Safety = UiSafety.UserOnly, Description = "名称一括変更のひな形 CSV を書き出す（保存ダイアログを開く）")]
        private Button    _btnRenameTemplate;
        [UiControl("rename.load", Safety = UiSafety.UserOnly, Description = "名称一括変更の CSV を読み込む（ファイル選択ダイアログを開く）")]
        private Button    _btnRenameLoad;
        [UiControl("rename.apply", Safety = UiSafety.SafeWrite, Description = "読み込んだ名称一括変更を適用する")]
        private Button    _btnRenameApply;
        private int[]    _renameTargetIndices;
        private string[] _renameTargetNames;

        [UiControl(Ignore = true)]
        private Foldout _transformFoldout;
        [UiControl("transform.positionX", Description = "ローカル位置 X")]
        private FloatField _localPosX;
        [UiControl("transform.positionY", Description = "ローカル位置 Y")]
        private FloatField _localPosY;
        [UiControl("transform.positionZ", Description = "ローカル位置 Z")]
        private FloatField _localPosZ;
        [UiControl("transform.rotationX", Description = "ローカル回転 X（度）")]
        private FloatField _localRotX;
        [UiControl("transform.rotationY", Description = "ローカル回転 Y（度）")]
        private FloatField _localRotY;
        [UiControl("transform.rotationZ", Description = "ローカル回転 Z（度）")]
        private FloatField _localRotZ;
        [UiControl("transform.rotationSliderX", Description = "ローカル回転 X のスライダー")]
        private Slider _localRotSliderX;
        [UiControl("transform.rotationSliderY", Description = "ローカル回転 Y のスライダー")]
        private Slider _localRotSliderY;
        [UiControl("transform.rotationSliderZ", Description = "ローカル回転 Z のスライダー")]
        private Slider _localRotSliderZ;
        [UiControl("transform.scaleX", Description = "ローカル拡大縮小 X")]
        private FloatField _localSclX;
        [UiControl("transform.scaleY", Description = "ローカル拡大縮小 Y")]
        private FloatField _localSclY;
        [UiControl("transform.scaleZ", Description = "ローカル拡大縮小 Z")]
        private FloatField _localSclZ;
        [UiControl("transform.quickRotateZPlus", Safety = UiSafety.SafeWrite, Description = "選択対象のローカル Z 回転に +90 度を足す")]
        private Button _btnQuickRotZPlus;
        [UiControl("transform.quickMoveYPlus", Safety = UiSafety.SafeWrite, Description = "選択対象のローカル Y 位置に +0.1 を足す")]
        private Button _btnQuickMoveYPlus;
        [UiControl("transform.quickRotateZMinus", Safety = UiSafety.SafeWrite, Description = "選択対象のローカル Z 回転に −90 度を足す")]
        private Button _btnQuickRotZMinus;
        [UiControl("selectAllObjects", Safety = UiSafety.SafeWrite, Description = "リストにある全オブジェクトを選択する")]
        private Button _btnSelectAllObjects;
        [UiControl("add", Safety = UiSafety.SafeWrite, Description = "追加する（+）")]
        private Button _btnAdd;
        [UiControl("moveUp", Safety = UiSafety.SafeWrite, Description = "選択を 1 つ上へ動かす（▲）")]
        private Button _btnMoveUp;
        [UiControl("moveDown", Safety = UiSafety.SafeWrite, Description = "選択を 1 つ下へ動かす（▼）")]
        private Button _btnMoveDown;
        [UiControl("outdent", Safety = UiSafety.SafeWrite, Description = "選択の階層を 1 つ浅くする（←）")]
        private Button _btnOutdent;
        [UiControl("indent", Safety = UiSafety.SafeWrite, Description = "選択の階層を 1 つ深くする（→）")]
        private Button _btnIndent;
        [UiControl("duplicate", Safety = UiSafety.SafeWrite, Description = "選択を複製する（Dup）")]
        private Button _btnDuplicate;
        [UiControl("delete", Safety = UiSafety.Destructive, Description = "選択を削除する（Del）")]
        private Button _btnDelete;
        [UiControl("show", Safety = UiSafety.SafeWrite, Description = "選択を可視にする（◉）")]
        private Button _btnShow;
        [UiControl("hide", Safety = UiSafety.SafeWrite, Description = "選択を不可視にする（−）")]
        private Button _btnHide;
        [UiControl("lock", Safety = UiSafety.SafeWrite, Description = "選択をロックする（■）")]
        private Button _btnLock;
        [UiControl("unlock", Safety = UiSafety.SafeWrite, Description = "選択のロックを解除する（□）")]
        private Button _btnUnlock;
        [UiControl("mirrorOn", Safety = UiSafety.SafeWrite, Description = "選択のミラーを有効にする（⇆）")]
        private Button _btnMirrorOn;
        [UiControl("mirrorOff", Safety = UiSafety.SafeWrite, Description = "選択のミラーを無効にする（⇆×）")]
        private Button _btnMirrorOff;
        [UiControl("rename.browse", Safety = UiSafety.UserOnly, Description = "名称一括変更の CSV を選ぶ（ファイル選択ダイアログを開く）")]
        private Button _btnRenameBrowse;
        [UiControl("morph.testSelectAll", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "モーフ試用の全選択")]
        private Button _btnMorphTestSelectAll;
        [UiControl("morph.testDeselectAll", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "モーフ試用の全解除")]
        private Button _btnMorphTestDeselectAll;
        [UiControl("morph.testReset", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "モーフ試用のリセット")]
        private Button _btnMorphTestReset;
        private bool _isSyncingTransformUI;

        // モーフエディタ
        [UiControl("morph.count", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealMorphTab), Description = "モーフの件数")]
        private Label _morphCountLabel;
        [UiControl("morph.status", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealMorphTab), Description = "モーフ編集の結果")]
        private Label _morphStatusLabel;
        [UiControl("morph.list", Reveal = nameof(RevealMorphTab), Description = "モーフの一覧（一覧の行番号）")]
        private ListView _morphListView;
        [UiControl("morph.testWeight", Reveal = nameof(RevealMorphTab), Description = "モーフの試用ウェイト")]
        private Slider _morphTestWeight;
        [UiControl("morph.filter", Reveal = nameof(RevealMorphTab), Description = "モーフ一覧の絞り込み")]
        private TextField _morphFilterField;
        [UiControl(Ignore = true)]
        private VisualElement _morphSourceMeshPopupContainer;
        [UiControl(Ignore = true)]
        private VisualElement _morphParentPopupContainer;
        [UiControl(Ignore = true)]
        private VisualElement _morphPanelPopupContainer;
        [UiControl("morph.name", Reveal = nameof(RevealMorphTab), Description = "作るモーフの名前")]
        private TextField _morphNameField;
        [UiControl("morph.meshToMorph", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "メッシュをモーフにする")]
        private Button _btnMeshToMorph;
        [UiControl("morph.morphToMesh", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "モーフをメッシュにする")]
        private Button _btnMorphToMesh;
        // PopupField<int> の代替：DropdownField + マスターインデックスリスト
        [UiControl("morph.sourceMesh", Reveal = nameof(RevealMorphTab), Description = "モーフの元メッシュ")]
        private DropdownField _morphSourceMeshDropdown;
        [UiControl("morph.parent", Reveal = nameof(RevealMorphTab), Description = "モーフの親")]
        private DropdownField _morphParentDropdown;
        [UiControl("morph.panel", Reveal = nameof(RevealMorphTab), Description = "モーフのパネル")]
        private DropdownField _morphPanelDropdown;
        private List<int> _morphSourceMeshIds = new List<int>();
        private List<int> _morphParentIds     = new List<int>();
        [UiControl(Ignore = true)]
        private VisualElement _morphSetTypePopupContainer;
        [UiControl("morph.setName", Reveal = nameof(RevealMorphTab), Description = "作るモーフセットの名前")]
        private TextField _morphSetNameField;
        [UiControl("morph.setType", Reveal = nameof(RevealMorphTab), Description = "作るモーフセットのタイプ")]
        private DropdownField _morphSetTypeDropdown;
        [UiControl("morph.createSet", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealMorphTab), Description = "モーフセットを作る")]
        private Button _btnCreateMorphSet;

        /// <summary>
        /// UI 自動操作の表示の下準備。タブは「スキンドメッシュ」がオンのときだけ出る。
        /// </summary>
        private bool RevealDetailMode()
        {
            if (_detailModeToggle == null || _detailModeToggle.value) return false;
            _detailModeToggle.value = true;
            return true;
        }

        /// <summary>ボーンの欄は詳細表示の「ボーン」タブのときだけ出る。</summary>
        private bool RevealBoneTab() => RevealTab(TabType.Bone);

        /// <summary>モーフエディタは詳細表示の「モーフ」タブのときだけ出る。</summary>
        private bool RevealMorphTab() => RevealTab(TabType.Morph);

        private bool RevealTab(TabType tab)
        {
            bool changed = RevealDetailMode();
            if (_currentTab == tab) return changed;
            SwitchTab(tab);
            return true;
        }

        // ================================================================
        // データ
        // ================================================================

        private TabType _currentTab = TabType.Drawable;
        private SummaryTreeRoot _treeRoot;
        private TreeViewDragDropHelper<SummaryTreeAdapter> _dragDropHelper;
        private List<SummaryTreeAdapter> _selectedAdapters = new List<SummaryTreeAdapter>();
        private bool _refreshScheduled;

        // ApplyTreeToView() の実行回数。SummaryTreeRoot.OnTreeChanged() は
        // SendCommand → OnChanged の順に呼ぶため、SendCommand が同期で
        // ChangeKind.ListStructure を通知してツリーを作り直したかどうかを
        // この世代番号の変化で判定し、OnChanged 側の二重リビルドを避ける。
        private int _applyTreeGeneration;
        private int _genBeforeReorder = -1;

        private List<IMeshView> _morphListData     = new List<IMeshView>();
        private List<IMeshView> _morphFilteredData = new List<IMeshView>();
        private bool _isSyncingMorphSelection;
        private bool _isMorphPreviewStarted;

        private class TreeItemCache
        {
            public Label NameLabel, InfoLabel;
            public Button VisBtn, LockBtn, SymBtn;
            // 協働編集: 担当者バッジ と 取得/解放ボタン
            public Label  EditorBadge;
            public Button EditorBtn;
            // D&D 用: この行が今どのアイテムを表示しているか。
            // BindTreeItem で毎回更新する。行要素からアイテムを直接引くために使う。
            public SummaryTreeAdapter Adapter;
        }

        // ================================================================
        // 協働編集（担当者）
        // ================================================================

        /// <summary>
        /// 自分のユーザー名。サーバへ register したものと一致させる必要がある。
        /// ListClientBase.UserName / PolyLingPlayerViewer 側から設定する。
        /// 空のままだと取得ボタンは無効化される。
        /// </summary>
        public string LocalUserName { get; set; } = "";

        /// <summary>担当者名ごとに安定した色を返す（誰の担当か一目で分かるように）。</summary>
        private static Color EditorColor(string editorName)
        {
            if (string.IsNullOrEmpty(editorName)) return new Color(1f, 1f, 1f, 0.35f);
            int h = 0;
            foreach (char ch in editorName) h = unchecked(h * 31 + ch);
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.45f, 1f);
        }

        // ================================================================
        // プロパティ（エディタ版と同一）
        // ================================================================

        private MeshCategory CurrentCategory => _currentTab switch
        {
            TabType.Drawable => MeshCategory.Drawable,
            TabType.Bone     => MeshCategory.Bone,
            TabType.Morph    => MeshCategory.Morph,
            TabType.RigidBody => MeshCategory.RigidBody,
            TabType.Joint    => MeshCategory.RigidBodyJoint,
            _                => MeshCategory.All
        };

        private bool IsSimpleMode => !(_detailModeToggle?.value ?? false);
        private int ModelIndex    => _ctx?.CurrentView?.CurrentModelIndex ?? 0;
        private IModelView CurrentModel => _ctx?.CurrentView?.CurrentModel;

        // ================================================================
        // Build / SetContext
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();
            _root = parent;
            BuildUI(parent);
            SetupTreeView();
            RegisterButtonEvents();
            BindBonePoseUI(parent);
            BindTransformUI(parent);
            BindMorphEditorUI(parent);
            SwitchTab(TabType.Drawable);
        }

        public void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
            }
        }

        public void Detach()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            SendEndMorphPreview();
            CleanupDragDrop();
        }
    }
}
