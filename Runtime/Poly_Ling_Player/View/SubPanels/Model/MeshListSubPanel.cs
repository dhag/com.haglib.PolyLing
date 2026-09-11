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
        private Button _btnOpNone, _btnOpSelect, _btnOpPose;

        /// <summary>現在のビューポート操作モード。ViewerCore が入場時に読む。</summary>
        public ViewportOpMode CurrentViewportOpMode => _viewportOpMode;

        /// <summary>ビューポート操作モードが変わったときに呼ぶ（ViewerCore が配線する）。</summary>
        public Action<ViewportOpMode> OnViewportOpModeChanged;

        /// <summary>ObjectMoveTool の共有設定を返す（姿勢調整チェックの実体）。</summary>
        public Func<ObjectMoveSettings> GetObjectMoveSettings;

        /// <summary>ギズモ表示チェックを変えた直後にビューポートのギズモを組み直す要求。</summary>
        public Action OnGizmoRefresh;

        // 姿勢調整チェック（ObjectMoveSettings と双方向同期）
        private Toggle _toggleOriginOnly, _toggleMoveWithChildren;
        private Toggle _toggleShowMoveGizmo, _toggleShowRotationGizmo;
        private bool   _suppressMoveSettings;

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素（エディタ版と同名）
        // ================================================================

        private VisualElement _root;
        private Button _tabDrawable, _tabBone, _tabMorph, _tabRigidBody, _tabJoint;
        private VisualElement _mainContent, _morphEditor;
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
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle, _showMirrorSideToggle;
        private TextField _filterField;

        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel, _faceCountLabel, _triCountLabel, _quadCountLabel, _ngonCountLabel;
        private Toggle _ignorePoseToggle;
        private Toggle _preserveNormalsToggle;
        private Toggle _mirrorBranchRootToggle;
        // ミラーモード（なし/分離/結合）。⇆ ボタンは有無だけを切り替えるので、
        // 結合(2) の指定はここで行う。
        private DropdownField _mirrorModeDropdown;
        private static readonly List<string> MirrorModeChoices =
            new List<string> { "なし", "分離", "結合" };
        private VisualElement _indexInfo;
        private Label _boneIndexLabel, _masterIndexLabel;

        private VisualElement _bonePoseSection;
        private Foldout _poseFoldout, _bindposeFoldout;
        private Toggle _poseActiveToggle;
        private FloatField _restPosX, _restPosY, _restPosZ;
        private FloatField _restRotX, _restRotY, _restRotZ;
        private Slider _restRotSliderX, _restRotSliderY, _restRotSliderZ;
        private FloatField _restSclX, _restSclY, _restSclZ;
        private VisualElement _poseLayersContainer;
        private Label _poseNoLayersLabel;
        private Label _poseResultPos, _poseResultRot;
        private Button _btnInitPose, _btnResetLayers;
        private Label _bindposePos, _bindposeRot, _bindposeScl;
        private Button _btnBakePose;
        private bool _isSyncingPoseUI;

        // 詳細モード切り替え（エディタ版「detail-mode-toggle」＝「スキンドメッシュ」に名称変更）
        private Toggle _detailModeToggle;
        private VisualElement _tabHeader;

        // ツリーのインデント幅。Unity 既定のままだと深い階層で名前が右へ寄りすぎるため、
        // 既定値を狭めに取り、スライダーで調整できるようにする。
        private const float TreeIndentDefault = 8f;
        private const float TreeIndentMin     = 0f;
        private const float TreeIndentMax     = 24f;
        private float  _treeIndentWidth = TreeIndentDefault;
        private Slider _indentSlider;
        private Label  _indentValueLabel;

        // 行内ボタンを押した瞬間の Ctrl 状態（MakeTreeItem で登録した PointerDown が更新する）
        private bool _rowCtrlDown;

        // 選択辞書（オブジェクト選択辞書）の適用
        private DropdownField _selDicDropdown;
        private Button _btnSelDicApply, _btnSelDicAdd;
        private readonly List<string> _selDicNames = new List<string>();

        // 名称一括変更（旧名→新名 CSV）
        private Foldout   _renameFoldout;
        private TextField _renamePathField;
        private Label     _renameStatusLabel;
        private Button    _btnRenameTemplate, _btnRenameLoad, _btnRenameApply;
        private int[]    _renameTargetIndices;
        private string[] _renameTargetNames;

        private Foldout _transformFoldout;
        private FloatField _localPosX, _localPosY, _localPosZ;
        private FloatField _localRotX, _localRotY, _localRotZ;
        private Slider _localRotSliderX, _localRotSliderY, _localRotSliderZ;
        private FloatField _localSclX, _localSclY, _localSclZ;
        private bool _isSyncingTransformUI;

        // モーフエディタ
        private Label _morphCountLabel, _morphStatusLabel;
        private ListView _morphListView;
        private Slider _morphTestWeight;
        private TextField _morphFilterField;
        private VisualElement _morphSourceMeshPopupContainer, _morphParentPopupContainer, _morphPanelPopupContainer;
        private TextField _morphNameField;
        private Button _btnMeshToMorph, _btnMorphToMesh;
        // PopupField<int> の代替：DropdownField + マスターインデックスリスト
        private DropdownField _morphSourceMeshDropdown, _morphParentDropdown, _morphPanelDropdown;
        private List<int> _morphSourceMeshIds = new List<int>();
        private List<int> _morphParentIds     = new List<int>();
        private VisualElement _morphSetTypePopupContainer;
        private TextField _morphSetNameField;
        private DropdownField _morphSetTypeDropdown;
        private Button _btnCreateMorphSet;

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
