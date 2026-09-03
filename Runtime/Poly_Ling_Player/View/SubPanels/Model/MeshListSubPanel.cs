// MeshListSubPanel.cs
// MeshListPanelV2 のランタイムポート。
// エディタ依存APIを以下のように置換:
//   EditorApplication.delayCall     → _root.schedule.Execute
//   EditorUtility.DisplayDialog     → 確認なし即実行
//   PopupField<int>                 → DropdownField + インデックス変換
//   AssetDatabase / UXML / USS      → コードによる UI 構築
//   EditorWindow / CreateGUI        → Build(VisualElement) + SetContext
// Runtime/Poly_Ling_Player/View/ に配置

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
    public class MeshListSubPanel
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

        // ================================================================
        // UI構築（エディタ版はUXMLだが、ここではコードで同等構造を構築）
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft = 4; root.style.paddingRight  = 4;
            root.style.paddingTop  = 4; root.style.paddingBottom = 4;

            // ── パネル名
            var panelNameLabel = new Label("オブジェクトリスト");
            panelNameLabel.style.color = new StyleColor(Color.white);
            panelNameLabel.style.fontSize = 12;
            panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panelNameLabel.style.marginBottom = 3;
            root.Add(panelNameLabel);

            // ── ビューポート操作（3択）
            root.Add(BuildViewportOpModeRow());

            // ── スキンドメッシュ（= 詳細モード）トグル
            _detailModeToggle = new Toggle("スキンドメッシュ") { value = false, name = "detail-mode-toggle" };
            _detailModeToggle.style.color = new StyleColor(Color.white);
            _detailModeToggle.style.marginBottom = 3;
            root.Add(_detailModeToggle);

            // ── タブヘッダー（詳細モード時のみ表示）
            _tabHeader = new VisualElement { name = "tab-header" };
            _tabHeader.style.flexDirection = FlexDirection.Row;
            _tabHeader.style.marginBottom  = 3;
            _tabDrawable = MakeTabBtn("Mesh",  "tab-drawable");
            _tabBone     = MakeTabBtn("Bone",  "tab-bone");
            _tabMorph    = MakeTabBtn("Morph", "tab-morph");
            _tabRigidBody = MakeTabBtn("剛体",  "tab-rigidbody");
            _tabJoint    = MakeTabBtn("Joint", "tab-joint");
            _tabHeader.Add(_tabDrawable); _tabHeader.Add(_tabBone); _tabHeader.Add(_tabMorph);
            _tabHeader.Add(_tabRigidBody); _tabHeader.Add(_tabJoint);
            root.Add(_tabHeader);

            // ── カウント・フィルター行
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems    = Align.Center;
            topRow.style.marginBottom  = 3;

            _countLabel = new Label { name = "count-label" };
            _countLabel.style.color = new StyleColor(Color.white);
            topRow.Add(_countLabel);

            _showInfoToggle = new Toggle("情報を表示") { name = "show-info-toggle", value = true };
            _showInfoToggle.style.color = new StyleColor(Color.white);
            _showInfoToggle.tooltip = "情報表示"; _showInfoToggle.style.marginLeft = 4;
            topRow.Add(_showInfoToggle);

            _showMirrorSideToggle = new Toggle("ミラーも表示") { name = "show-mirror-toggle", value = false };
            _showMirrorSideToggle.style.color = new StyleColor(Color.white);
            _showMirrorSideToggle.tooltip = "ミラー側表示"; _showMirrorSideToggle.style.marginLeft = 2;
            topRow.Add(_showMirrorSideToggle);
            root.Add(topRow);

            // ── インデント幅
            var indentRow = new VisualElement();
            indentRow.style.flexDirection = FlexDirection.Row;
            indentRow.style.alignItems    = Align.Center;
            indentRow.style.marginBottom  = 3;

            var indentLabel = new Label("インデント:");
            indentLabel.style.color    = new StyleColor(Color.white);
            indentLabel.style.fontSize = 10;
            indentRow.Add(indentLabel);

            _indentSlider = new Slider(TreeIndentMin, TreeIndentMax)
            { name = "indent-slider", value = _treeIndentWidth };
            _indentSlider.style.flexGrow   = 1;
            _indentSlider.style.marginLeft = 4;
            _indentSlider.style.color      = new StyleColor(Color.white);
            indentRow.Add(_indentSlider);

            _indentValueLabel = new Label($"{(int)_treeIndentWidth}px") { name = "indent-value-label" };
            _indentValueLabel.style.color    = new StyleColor(Color.white);
            _indentValueLabel.style.fontSize = 10;
            _indentValueLabel.style.width    = 30;
            _indentValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            indentRow.Add(_indentValueLabel);
            root.Add(indentRow);

            // ── すべてのオブジェクトを選択
            var selectAllRow = new VisualElement();
            selectAllRow.style.flexDirection = FlexDirection.Row;
            selectAllRow.style.marginBottom  = 3;
            selectAllRow.Add(MakeSmallBtn("すべてのオブジェクトを選択", "btn-select-all",
                                          "リストにある全オブジェクトを選択する"));
            root.Add(selectAllRow);

            var filterLabel = new Label("フィルタ:");
            filterLabel.style.color    = new StyleColor(Color.white);
            filterLabel.style.fontSize = 10;
            filterLabel.style.marginTop = 2;
            root.Add(filterLabel);

            _filterField = new TextField { name = "filter-field" };
            _filterField.style.marginBottom = 3;
            root.Add(_filterField);

            // ── メインコンテンツ（ツリー + 詳細 + BonePose + Transform）
            _mainContent = new VisualElement { name = "main-content" };
            // flexGrow=1 だと親高いっぱいに伸び、内部 flexShrink でツリーの明示 height が縮み効かない。
            // モーフ側コンテナ(_morphEditor)と同じ自然高にして、ツリーの明示 height をそのまま効かせる。
            _mainContent.style.flexGrow = 0;

            _treeView = new TreeView { name = "mesh-tree" };
            _treeView.style.flexGrow  = 0;
            // TreeView は style.height を無視し minHeight/maxHeight で高さが決まるため、
            // 3つとも _treeHeight にして高さを厳密固定する（ドラッグ時も同様に設定）。
            _treeView.style.height    = _treeHeight;
            _treeView.style.minHeight = _treeHeight;
            _treeView.style.maxHeight = _treeHeight;
            _mainContent.Add(_treeView);
            AddListResizeHandle(_mainContent, _treeView,
                () => _treeHeight, h => { _treeHeight = h; _treeHeightUserAdjusted = true; }, TreeMinHeight);

            // 操作ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.flexWrap     = Wrap.Wrap;
            btnRow.style.marginTop    = 3;
            btnRow.Add(MakeSmallBtn("+",   "btn-add"));
            btnRow.Add(MakeSmallBtn("▲",  "btn-up"));
            btnRow.Add(MakeSmallBtn("▼",  "btn-down"));
            btnRow.Add(MakeSmallBtn("←",  "btn-outdent"));
            btnRow.Add(MakeSmallBtn("→",  "btn-indent"));
            btnRow.Add(MakeSmallBtn("Dup", "btn-duplicate"));
            btnRow.Add(MakeSmallBtn("Del", "btn-delete"));
            // 一括操作。対象は「選択されている行すべて」。
            // 行内のボタンは押した行 1 件だけなので、押す場所で対象が分かれる。
            btnRow.Add(MakeSmallBtn("◉",  "btn-show",        "選択を可視にする"));
            btnRow.Add(MakeSmallBtn("−",   "btn-hide",        "選択を不可視にする"));
            btnRow.Add(MakeSmallBtn("■",  "btn-lock",        "選択をロックする"));
            btnRow.Add(MakeSmallBtn("□",  "btn-unlock",      "選択のロックを解除する"));
            btnRow.Add(MakeSmallBtn("⇆",   "btn-mirror-on",   "選択のミラーを有効にする"));
            btnRow.Add(MakeSmallBtn("⇆×",   "btn-mirror-off",  "選択のミラーを無効にする"));
            _mainContent.Add(btnRow);

            // ── 選択辞書（オブジェクト選択辞書）からの読み込み
            var selDicRow = new VisualElement();
            selDicRow.style.flexDirection = FlexDirection.Row;
            selDicRow.style.alignItems    = Align.Center;
            selDicRow.style.marginTop     = 2;
            selDicRow.style.marginBottom  = 2;

            var selDicLabel = new Label("選択辞書:");
            selDicLabel.style.color    = new StyleColor(Color.white);
            selDicLabel.style.fontSize = 10;
            selDicRow.Add(selDicLabel);

            _selDicDropdown = new DropdownField { name = "seldic-dropdown" };
            _selDicDropdown.style.flexGrow   = 1;
            _selDicDropdown.style.marginLeft = 4;
            _selDicDropdown.style.marginRight = 2;
            selDicRow.Add(_selDicDropdown);

            _btnSelDicApply = MakeSmallBtn("適用", "btn-seldic-apply");
            _btnSelDicAdd   = MakeSmallBtn("追加", "btn-seldic-add");
            selDicRow.Add(_btnSelDicApply); selDicRow.Add(_btnSelDicAdd);
            _mainContent.Add(selDicRow);

            // ── 名称一括変更（旧名→新名 CSV）
            _renameFoldout = new Foldout { text = "名称一括変更", value = false, name = "rename-foldout" };
            _renameFoldout.style.marginTop = 2;
            BuildRenameSection(_renameFoldout.contentContainer);
            _mainContent.Add(_renameFoldout);

            // 詳細Foldout
            _detailFoldout = new Foldout { text = "詳細", value = true, name = "detail-foldout" };
            _detailFoldout.style.marginTop = 4;
            BuildDetailFoldout(_detailFoldout.contentContainer);
            _mainContent.Add(_detailFoldout);

            // indexInfo（ボーンタブ用）
            _indexInfo = new VisualElement { name = "index-info" };
            _boneIndexLabel   = MakeInfoLabel("bone-index-label");
            _masterIndexLabel = MakeInfoLabel("master-index-label");
            _indexInfo.Add(_boneIndexLabel); _indexInfo.Add(_masterIndexLabel);
            _mainContent.Add(_indexInfo);

            root.Add(_mainContent);

            // ── モーフエディタ（詳細モード+Morphタブ時のみ表示）
            _morphEditor = new VisualElement { name = "morph-editor" };
            _morphEditor.style.display = DisplayStyle.None;
            BuildMorphEditor(_morphEditor);
            root.Add(_morphEditor);

            // ── 姿勢調整（常に表示。ビューポート操作モードに関わらず出す）
            root.Add(BuildObjectPoseSection());

            // ── ステータス
            _statusLabel = new Label("") { name = "status-label" };
            _statusLabel.style.color = new StyleColor(Color.white);
            root.Add(_statusLabel);
        }

        // ================================================================
        // ビューポート操作モード（3択）
        // ================================================================

        private VisualElement BuildViewportOpModeRow()
        {
            _viewportOpMode = (ViewportOpMode)PlayerUiPrefs.GetInt(
                ViewportOpModeKey, (int)ViewportOpMode.ObjectPose);

            var box = new VisualElement();
            box.style.marginBottom = 3;

            var lbl = new Label("ビューポート操作:");
            lbl.style.color    = new StyleColor(Color.white);
            lbl.style.fontSize = 10;
            box.Add(lbl);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Button MakeOpBtn(string text, string tip, ViewportOpMode mode)
            {
                var b = new Button(() => SetViewportOpMode(mode)) { text = text, tooltip = tip };
                b.style.flexGrow      = 1;
                b.style.height        = 20;
                b.style.fontSize      = 9;
                b.style.marginRight   = 2;
                b.style.paddingLeft   = 2;
                b.style.paddingRight  = 2;
                row.Add(b);
                return b;
            }

            _btnOpNone   = MakeOpBtn("操作なし", "3D 操作を受け付けない（視点操作だけ）",
                                     ViewportOpMode.None);
            _btnOpSelect = MakeOpBtn("要素選択", "頂点・辺・面の選択だけを行う（移動ギズモは出さない）",
                                     ViewportOpMode.SelectElem);
            _btnOpPose   = MakeOpBtn("姿勢調整", "オブジェクト原点を選び、姿勢を調整する（描画オブジェクトの姿勢と同じ）",
                                     ViewportOpMode.ObjectPose);
            _btnOpPose.style.marginRight = 0;

            box.Add(row);
            UpdateViewportOpModeButtons();
            return box;
        }

        private void SetViewportOpMode(ViewportOpMode mode)
        {
            _viewportOpMode = mode;
            PlayerUiPrefs.SetInt(ViewportOpModeKey, (int)mode);
            UpdateViewportOpModeButtons();
            OnViewportOpModeChanged?.Invoke(mode);
        }

        private void UpdateViewportOpModeButtons()
        {
            void Style(Button b, bool active)
            {
                if (b == null) return;
                b.style.backgroundColor = new StyleColor(
                    active ? new Color(0.25f, 0.45f, 0.7f) : new Color(0.2f, 0.2f, 0.2f));
                b.style.color = new StyleColor(Color.white);
            }
            Style(_btnOpNone,   _viewportOpMode == ViewportOpMode.None);
            Style(_btnOpSelect, _viewportOpMode == ViewportOpMode.SelectElem);
            Style(_btnOpPose,   _viewportOpMode == ViewportOpMode.ObjectPose);
        }

        // ================================================================
        // 姿勢調整（ObjectMoveSettings と双方向同期）
        // ================================================================

        /// <summary>
        /// 「描画オブジェクトの姿勢」タブと同じ操作チェックを、オブジェクトリストにも置く。
        /// 実体は ObjectMoveTool の共有 ObjectMoveSettings 1 個なので、
        /// どちらのパネルで変えても同じ設定を触る。
        /// 数値での位置・回転・スケールは既存の「トランスフォーム」に集約してあるため、
        /// ここには置かない（同じ値の入力欄を 2 か所に作らない）。
        /// </summary>
        private VisualElement BuildObjectPoseSection()
        {
            var box = new VisualElement { name = "object-pose-section" };
            box.Add(Separator());
            box.Add(SectionHeader("姿勢調整"));

            _toggleOriginOnly        = new Toggle("原点だけ移動") { value = false };
            _toggleMoveWithChildren  = new Toggle("子を一緒に移動") { value = true };
            _toggleShowMoveGizmo     = new Toggle("移動ギズモを表示") { value = true };
            _toggleShowRotationGizmo = new Toggle("回転ギズモを表示") { value = false };

            _toggleOriginOnly.style.color        = new StyleColor(Color.white);
            _toggleMoveWithChildren.style.color  = new StyleColor(Color.white);
            _toggleShowMoveGizmo.style.color     = new StyleColor(Color.white);
            _toggleShowRotationGizmo.style.color = new StyleColor(Color.white);

            _toggleShowMoveGizmo.tooltip =
                "OFF にすると矢印と中央ハンドルを消し、当たり判定も止める"
                + "（オブジェクト原点をクリックで選びやすくする）";
            _toggleShowRotationGizmo.tooltip =
                "OFF にすると回転リングを消し、当たり判定も止める";

            _toggleOriginOnly.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.OriginOnly = e.newValue;

                // 「原点だけ移動」を ON にしたときだけ「子を一緒に移動」を OFF にする。
                // OFF にしたときは連動しない。
                if (e.newValue)
                {
                    if (s != null) s.MoveWithChildren = false;
                    _suppressMoveSettings = true;
                    try { _toggleMoveWithChildren?.SetValueWithoutNotify(false); }
                    finally { _suppressMoveSettings = false; }
                }
            });
            _toggleMoveWithChildren.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveWithChildren = e.newValue;
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

            box.Add(_toggleOriginOnly);
            box.Add(_toggleMoveWithChildren);
            box.Add(_toggleShowMoveGizmo);
            box.Add(_toggleShowRotationGizmo);
            box.Add(BuildQuickOffsetRow());
            return box;
        }

        /// <summary>
        /// 選択対象のローカル姿勢を決め打ちの量だけ動かすボタン行。
        /// 「描画オブジェクトの姿勢」タブと同じ並び・同じ動きにする。
        /// </summary>
        private VisualElement BuildQuickOffsetRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;

            Button Make(string text, string tip, Action onClick)
            {
                var b = new Button(onClick) { text = text, tooltip = tip };
                b.style.flexGrow     = 1;
                b.style.height       = 20;
                b.style.fontSize     = 9;
                b.style.marginRight  = 2;
                b.style.paddingLeft  = 2;
                b.style.paddingRight = 2;
                row.Add(b);
                return b;
            }

            Make("Z90度回転", "選択対象のローカル Z 回転に +90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, 90f, "Z+90度回転"));
            Make("Y0.1移動", "選択対象のローカル Y 位置に +0.1 を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.PositionY, 0.1f, "Y+0.1移動"));
            var last = Make("Z-90度回転", "選択対象のローカル Z 回転に −90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, -90f, "Z-90度回転"));
            last.style.marginRight = 0;

            return row;
        }

        /// <summary>
        /// ローカル姿勢の 1 軸を相対で動かす。
        ///
        /// 「原点だけ移動」の自頂点再ローカル化・スキン固定の BindPose 追従・Undo は
        /// PlayerCommandDispatcher の Begin → Set → End 経路が持っている。
        /// ここでは現在値に差分を足した絶対値を求めて既存経路へ渡すだけにする
        /// （後処理を書き写さない）。
        ///
        /// SetBoneTransformValueCommand は配列全員へ同じ値を代入するため、
        /// 現在値がオブジェクトごとに違う相対操作では 1 件ずつ送る必要がある。
        /// 対象はポーズを持たないもの（＝トランスフォーム欄と同じ集合）。
        /// </summary>
        private void OffsetTransform(
            SetBoneTransformValueCommand.Field field, float delta, string undoLabel)
        {
            if (_ctx == null) return;

            var targets = _selectedAdapters
                .Where(a => !a.MeshView.BonePose.HasPose && a.MasterIndex >= 0)
                .ToList();
            if (targets.Count == 0) return;

            var indices = targets.Select(a => a.MasterIndex).ToArray();
            var s = GetObjectMoveSettings?.Invoke();

            SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, indices)
            {
                Mode       = s?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind,
                OriginOnly = s?.OriginOnly ?? false,
            });

            bool isRotation =
                field == SetBoneTransformValueCommand.Field.RotationX ||
                field == SetBoneTransformValueCommand.Field.RotationY ||
                field == SetBoneTransformValueCommand.Field.RotationZ;

            foreach (var a in targets)
            {
                var v = a.MeshView;
                float cur;
                switch (field)
                {
                    case SetBoneTransformValueCommand.Field.PositionX: cur = v.LocalPosition.x; break;
                    case SetBoneTransformValueCommand.Field.PositionY: cur = v.LocalPosition.y; break;
                    case SetBoneTransformValueCommand.Field.PositionZ: cur = v.LocalPosition.z; break;
                    case SetBoneTransformValueCommand.Field.RotationX: cur = v.LocalRotationEuler.x; break;
                    case SetBoneTransformValueCommand.Field.RotationY: cur = v.LocalRotationEuler.y; break;
                    case SetBoneTransformValueCommand.Field.RotationZ: cur = v.LocalRotationEuler.z; break;
                    default: continue;
                }

                float next = cur + delta;
                // 回転は押すたびに 360 を超えて伸びていくので毎回畳む。
                if (isRotation) next = NormAngle(next);

                SendCmd(new SetBoneTransformValueCommand(
                    ModelIndex, new[] { a.MasterIndex }, field, next));
            }

            SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, undoLabel));
            UpdateTransformPanel();
        }

        /// <summary>ObjectMoveSettings の現在値をチェックへ映す。パネル表示時に呼ぶ。</summary>
        public void SyncObjectPoseToggles()
        {
            var s = GetObjectMoveSettings?.Invoke();
            if (s == null) return;
            _suppressMoveSettings = true;
            try
            {
                _toggleOriginOnly?.SetValueWithoutNotify(s.OriginOnly);
                _toggleMoveWithChildren?.SetValueWithoutNotify(s.MoveWithChildren);
                _toggleShowMoveGizmo?.SetValueWithoutNotify(s.AllowMoveGizmo);
                _toggleShowRotationGizmo?.SetValueWithoutNotify(s.AllowRotationGizmo);
            }
            finally { _suppressMoveSettings = false; }
        }

        /// <summary>
        /// このパネルが担当する間の ObjectMoveTool のピック対象を決める。
        /// ボーンタブならボーン、それ以外は描画メッシュ（スキンドは除く）。
        /// </summary>
        public void ApplyPickFilter()
        {
            var s = GetObjectMoveSettings?.Invoke();
            if (s == null) return;
            bool boneTab = !IsSimpleMode && _currentTab == TabType.Bone;
            s.PickBones         = boneTab;
            s.PickMeshesNoSkin  = !boneTab;
            s.PickMeshesSkinned = false;
        }

        private void BuildDetailFoldout(VisualElement c)
        {
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 3;

            var nameLabel = new Label("名前:");
            nameLabel.style.color    = new StyleColor(Color.white);
            nameLabel.style.fontSize = 10;
            nameLabel.style.width    = 34;
            nameRow.Add(nameLabel);

            _meshNameField = new TextField { name = "mesh-name-field" };
            _meshNameField.style.flexGrow = 1;
            nameRow.Add(_meshNameField);

            var applyBtn = new Button(() => ApplyMeshName()) { text = "変更" };
            applyBtn.style.width       = 36;
            applyBtn.style.height      = 18;
            applyBtn.style.fontSize    = 9;
            applyBtn.style.marginLeft  = 2;
            applyBtn.style.paddingTop  = 0;
            applyBtn.style.paddingBottom = 0;
            nameRow.Add(applyBtn);

            c.Add(nameRow);
            _vertexCountLabel = MakeInfoLabel("vertex-count-label"); c.Add(_vertexCountLabel);
            _faceCountLabel   = MakeInfoLabel("face-count-label");   c.Add(_faceCountLabel);
            _triCountLabel    = MakeInfoLabel("tri-count-label");     c.Add(_triCountLabel);
            _quadCountLabel   = MakeInfoLabel("quad-count-label");    c.Add(_quadCountLabel);
            _ngonCountLabel   = MakeInfoLabel("ngon-count-label");    c.Add(_ngonCountLabel);

            _ignorePoseToggle = new Toggle("姿勢無視(アーマチャ)") { name = "ignore-pose-toggle" };
            _ignorePoseToggle.style.color        = new StyleColor(Color.white);
            _ignorePoseToggle.style.marginTop    = 4;
            _ignorePoseToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetIgnorePoseCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_ignorePoseToggle);

            _mirrorModeDropdown = new DropdownField { name = "mirror-mode-dropdown" };
            _mirrorModeDropdown.choices = MirrorModeChoices;
            _mirrorModeDropdown.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                int mode = MirrorModeChoices.IndexOf(e.newValue);
                if (mode < 0) return;
                var indices = _selectedAdapters
                    .Where(a => !IsMirrorLocked(a))
                    .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
                if (indices.Length == 0) return;

                // 「なし」への切り替えはミラー側メッシュの始末を伴う。
                // 「なし」からの切り替えも同様に生成が要る。
                // 分離(1) ↔ 結合(2) は MQO の属性が変わるだけなので属性コマンドで足りる。
                //
                // 生成・始末の対象は、ミラー側メッシュをまだ持たない行だけに絞る。
                // 実在するペアをここで解体させない。
                // 判定は行ごとに行う。以前は _selectedAdapters[0].MirrorType を
                // 全行の分岐に使っており、先頭行の状態で対象全部が引きずられていた。
                var enableTargets = _selectedAdapters
                    .Where(a => !HasLiveMirrorPeer(a))
                    .Where(a => mode == 0 ? a.MirrorType != 0 : a.MirrorType == 0)
                    .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

                if (enableTargets.Length > 0)
                    SendCmd(new SetMirrorEnabledCommand(ModelIndex, enableTargets, mode != 0));
                if (mode != 0)
                    SendCmd(new SetBatchMirrorTypeCommand(ModelIndex, indices, mode));
            });
            c.Add(LabeledRow("ミラー", _mirrorModeDropdown));

            // ── ミラー分岐ルート ──────────────────────────────────────
            //   エクスポート／スキンド変換で、このノードを含む配下を実体側と
            //   ミラー側の2本の枝に分割する起点。ボーン生成より前に決めるメッシュ
            //   属性なので、ボーンエディタではなくここで設定する。
            _mirrorBranchRootToggle = new Toggle("ミラー分岐ルート") { name = "mirror-branch-root-toggle" };
            _mirrorBranchRootToggle.style.color     = new StyleColor(Color.white);
            _mirrorBranchRootToggle.style.marginTop = 2;
            _mirrorBranchRootToggle.tooltip =
                "このオブジェクトを含む配下を、実体側とミラー側の2本の枝に分割する。\n"
                + "半身モデルの反対側を生成する起点。\n"
                + "枝の中のオブジェクトは、ミラー設定の有無に関わらずボーンがミラー化される。";
            _mirrorBranchRootToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetMirrorBranchRootCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_mirrorBranchRootToggle);

            _preserveNormalsToggle = new Toggle("法線を保持(再計算しない)") { name = "preserve-normals-toggle" };
            _preserveNormalsToggle.style.color     = new StyleColor(Color.white);
            _preserveNormalsToggle.style.marginTop = 2;
            _preserveNormalsToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetPreserveNormalsCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_preserveNormalsToggle);
        }

        // ================================================================
        // 名称一括変更（旧名→新名 CSV）
        // ================================================================

        private void BuildRenameSection(VisualElement c)
        {
            var hint = new Label("CSV は「旧名,新名」の2列。'#' 始まりはコメント。");
            hint.style.color      = new StyleColor(Color.white);
            hint.style.fontSize   = 9;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            _renamePathField = new TextField { name = "rename-path-field" };
            _renamePathField.RegisterValueChangedCallback(e => RecentPaths.Set(RenamePathKey(), e.newValue));
            c.Add(PlayerIoUiKit.PathRow(_renamePathField, OnRenameBrowse));
            _renamePathField.SetValueWithoutNotify(ResolveRenamePath());

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;
            _btnRenameTemplate = MakeSmallBtn("雛形書出", "btn-rename-template");
            _btnRenameLoad     = MakeSmallBtn("読込",     "btn-rename-load");
            _btnRenameApply    = MakeSmallBtn("適用",     "btn-rename-apply");
            _btnRenameTemplate.style.flexGrow = 1;
            _btnRenameLoad.style.flexGrow     = 1;
            _btnRenameApply.style.flexGrow    = 1;
            row.Add(_btnRenameTemplate); row.Add(_btnRenameLoad); row.Add(_btnRenameApply);
            c.Add(row);

            _renameStatusLabel = PlayerIoUiKit.StatusLabel();
            c.Add(_renameStatusLabel);

            UpdateRenameButtonStates();
        }

        private void BuildMorphEditor(VisualElement parent)
        {
            // カウント・フィルター
            var topRow = new VisualElement(); topRow.style.flexDirection = FlexDirection.Row;
            _morphCountLabel = new Label("モーフ: 0") { name = "morph-count-label" };
            _morphCountLabel.style.color    = new StyleColor(Color.white);
            _morphCountLabel.style.fontSize = 11;
            topRow.Add(_morphCountLabel);
            parent.Add(topRow);

            _morphFilterField = new TextField(); _morphFilterField.style.marginBottom = 3;
            parent.Add(_morphFilterField);

            // リスト
            _morphListView = new ListView(_morphFilteredData, 20, MorphMake, MorphBind);
            _morphListView.style.flexGrow  = 0; _morphListView.style.height = _morphListHeight; _morphListView.style.minHeight = _morphListHeight; _morphListView.style.maxHeight = _morphListHeight;
            _morphListView.selectionType   = SelectionType.Multiple;
            _morphListView.selectionChanged += OnMorphSel;
            parent.Add(_morphListView);
            AddListResizeHandle(parent, _morphListView,
                () => _morphListHeight, h => _morphListHeight = h, MorphListMinHeight);

            // テストウェイト
            // レンジの実体は ParameterLimits（persistentDataPath の CSV）にある。
            // 同じキーを ApplyMorphPreviewCommand.Weight の PLParam(LimitKey) が指す。
            var wRow = new VisualElement(); wRow.style.flexDirection = FlexDirection.Row; wRow.style.marginTop = 4; wRow.style.alignItems = Align.Center;
            _morphTestWeight = new Slider(MorphWeightMin, MorphWeightMax); _morphTestWeight.style.flexGrow = 1; wRow.Add(_morphTestWeight);
            _morphTestWeight.style.color = new StyleColor(Color.white);
            parent.Add(wRow);

            // 選択操作ボタン
            var selRow = new VisualElement(); selRow.style.flexDirection = FlexDirection.Row; selRow.style.marginTop = 3;
            selRow.Add(MakeSmallBtn("全選択",   "btn-morph-test-select-all"));
            selRow.Add(MakeSmallBtn("全解除",   "btn-morph-test-deselect-all"));
            selRow.Add(MakeSmallBtn("リセット", "btn-morph-test-reset"));
            parent.Add(selRow);

            parent.Add(Separator());

            // モーフ変換
            parent.Add(SectionHeader("メッシュ→モーフ"));

            _morphSourceMeshPopupContainer = new VisualElement { name = "morph-source-mesh-container" };
            parent.Add(LabeledRow("元メッシュ", _morphSourceMeshPopupContainer));

            _morphParentPopupContainer = new VisualElement { name = "morph-parent-container" };
            parent.Add(LabeledRow("親", _morphParentPopupContainer));

            _morphPanelPopupContainer = new VisualElement { name = "morph-panel-container" };
            parent.Add(LabeledRow("パネル", _morphPanelPopupContainer));

            _morphNameField = new TextField(); _morphNameField.name = "morph-name-field";
            parent.Add(LabeledRow("名前", _morphNameField));

            var convRow = new VisualElement(); convRow.style.flexDirection = FlexDirection.Row;
            _btnMeshToMorph = MakeSmallBtn("Mesh→Morph", "btn-mesh-to-morph");
            _btnMorphToMesh = MakeSmallBtn("Morph→Mesh", "btn-morph-to-mesh");
            convRow.Add(_btnMeshToMorph); convRow.Add(_btnMorphToMesh);
            parent.Add(convRow);

            parent.Add(Separator());

            // モーフセット作成
            parent.Add(SectionHeader("モーフセット作成"));
            _morphSetNameField = new TextField(); _morphSetNameField.name = "morph-set-name-field";
            parent.Add(LabeledRow("セット名", _morphSetNameField));

            _morphSetTypePopupContainer = new VisualElement { name = "morph-set-type-container" };
            parent.Add(LabeledRow("種別", _morphSetTypePopupContainer));

            _btnCreateMorphSet = MakeSmallBtn("セット作成", "btn-create-morph-set");
            parent.Add(_btnCreateMorphSet);

            _morphStatusLabel = new Label(""); _morphStatusLabel.style.fontSize = 10; _morphStatusLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.4f)); _morphStatusLabel.style.marginTop = 3;
            _morphStatusLabel.style.color = new StyleColor(Color.white);
            parent.Add(_morphStatusLabel);
        }

        // ================================================================
        // SetupTreeView（エディタ版と同一）
        // ================================================================

        private void SetupTreeView()
        {
            if (_treeView == null) return;
            _treeView.fixedItemHeight    = 20;
            _treeView.horizontalScrollingEnabled = true;
            SetupScrollerStability();
            _treeView.makeItem           = MakeTreeItem;
            _treeView.bindItem           = BindTreeItem;
            _treeView.selectionType      = SelectionType.Multiple;
            _treeView.selectionChanged   += OnSelectionChanged;
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
        }

        // ================================================================
        // 横スクロールバーのちらつき対策
        // ================================================================

        // 縦操作中は横スクロールバーの出入りを止める。
        // 既定の Auto は内容幅の変化で出たり消えたりし、そのたびに
        // 表示領域の高さが変わってリストが揺れる。
        private ScrollView _treeScroll;
        private bool _hScrollerLocked;
        private IVisualElementScheduledItem _hScrollerUnlock;

        private void SetupScrollerStability()
        {
            _treeScroll = _treeView?.Q<ScrollView>();
            if (_treeScroll == null) return;

            var vs = _treeScroll.verticalScroller;
            if (vs != null)
            {
                vs.RegisterCallback<PointerDownEvent>(_ => LockHorizontalScroller(), TrickleDown.TrickleDown);
                vs.RegisterCallback<PointerUpEvent>(_ => UnlockHorizontalScroller(), TrickleDown.TrickleDown);
                vs.RegisterCallback<PointerCaptureOutEvent>(_ => UnlockHorizontalScroller());
            }

            // ホイールは終端イベントが無いので、途切れてから解除する。
            _treeScroll.RegisterCallback<WheelEvent>(_ =>
            {
                LockHorizontalScroller();
                _hScrollerUnlock?.Pause();
                _hScrollerUnlock = _treeScroll.schedule.Execute(UnlockHorizontalScroller).StartingIn(200);
            }, TrickleDown.TrickleDown);
        }

        private void LockHorizontalScroller()
        {
            if (_hScrollerLocked || _treeScroll == null) return;
            var hs = _treeScroll.horizontalScroller;
            bool shown = hs != null && hs.resolvedStyle.display == DisplayStyle.Flex;
            _treeScroll.horizontalScrollerVisibility =
                shown ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Hidden;
            _hScrollerLocked = true;
        }

        private void UnlockHorizontalScroller()
        {
            if (!_hScrollerLocked || _treeScroll == null) return;
            _treeScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _hScrollerLocked = false;
        }

        // ================================================================
        // タブ切り替え（エディタ版と同一）
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            if (_currentTab == TabType.Morph && tab != TabType.Morph) SendEndMorphPreview();
            _currentTab = tab;
            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone,     tab == TabType.Bone);
            SetTabActive(_tabMorph,    tab == TabType.Morph);
            SetTabActive(_tabRigidBody, tab == TabType.RigidBody);
            SetTabActive(_tabJoint,    tab == TabType.Joint);

            bool simpleMode = IsSimpleMode;
            if (_tabHeader != null) _tabHeader.style.display = simpleMode ? DisplayStyle.None : DisplayStyle.Flex;

            if (simpleMode)
            {
                if (_indexInfo     != null) _indexInfo.style.display     = DisplayStyle.None;
                if (_bonePoseSection != null) _bonePoseSection.style.display = DisplayStyle.Flex;
                if (_mainContent   != null) _mainContent.style.display   = DisplayStyle.Flex;
                if (_morphEditor   != null) _morphEditor.style.display   = DisplayStyle.None;
            }
            else
            {
                if (_indexInfo != null)
                    _indexInfo.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                if (_bonePoseSection != null)
                    _bonePoseSection.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                bool isMorph = tab == TabType.Morph;
                if (_mainContent != null) _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
                if (_morphEditor != null) _morphEditor.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _selectedAdapters.Clear();
            bool showMorph = !simpleMode && tab == TabType.Morph;
            if (!showMorph) CreateTreeRoot();
            if (showMorph) RefreshMorphEditor();
            RefreshAllImmediate();
            // 名称一括変更はタブ（メッシュ／ボーン／モーフ…）ごとに独立させる
            ResetRenameState();
            RefreshSelectionDictionary();
            // タブでピック対象（ボーン / 描画メッシュ）が変わる。
            ApplyPickFilter();
            Log($"{tab} タブ");
        }

        private void SetTabActive(Button btn, bool active) => btn?.EnableInClassList("tab-active", active);

        private void OnDetailModeChanged()
        {
            _selectedAdapters.Clear();
            if (IsSimpleMode) SendEndMorphPreview();
            SwitchTab(IsSimpleMode ? TabType.Drawable : _currentTab);
            UpdateBonePosePanel();
            UpdateTransformPanel();
        }

        // ================================================================
        // CreateTreeRoot（エディタ版と同一）
        // ================================================================

        private void CreateTreeRoot()
        {
            var model = CurrentModel;
            if (model == null) return;

            IReadOnlyList<IMeshView> sourceList;
            MeshCategory category;

            if (IsSimpleMode)
            {
                var filtered = model.DrawableList?.Where(v => !v.HasBoneWeight).ToList() ?? new List<IMeshView>();
                sourceList = filtered;
                category   = MeshCategory.Drawable;
            }
            else
            {
                sourceList = _currentTab switch
                {
                    TabType.Drawable => model.DrawableList,
                    TabType.Bone     => model.BoneList,
                    TabType.RigidBody => model.RigidBodyList,
                    TabType.Joint    => model.RigidBodyJointList,
                    _                => null
                };
                category = CurrentCategory;
            }

            if (sourceList == null) return;

            bool   excludeMirror = !(_showMirrorSideToggle?.value ?? false);
            string filter        = _filterField?.value;

            _treeRoot = new SummaryTreeRoot();
            _treeRoot.ModelIndex   = ModelIndex;
            _treeRoot.SendCommand  = cmd =>
            {
                _genBeforeReorder = _applyTreeGeneration;
                _ctx?.SendCommand(cmd);
            };
            _treeRoot.OnChanged    = () =>
            {
                // SendCommand の同期通知で既にツリーを作り直していれば再構築しない。
                bool alreadyRebuilt = _genBeforeReorder >= 0 && _applyTreeGeneration != _genBeforeReorder;
                _genBeforeReorder = -1;
                _isReceiving = true;
                try
                {
                    if (!alreadyRebuilt) RefreshTreeImmediate();
                    SyncTreeViewSelection();
                    UpdateDetailPanel();
                }
                finally { _isReceiving = false; }
            };
            _treeRoot.Build(sourceList, category, excludeMirror, filter);
            SetupDragDrop();
        }

        // ================================================================
        // MakeItem / BindItem（エディタ版と同一）
        // ================================================================

        /// <summary>D&D が行要素からアイテムを引くための目印名。</summary>
        private const string TreeItemName = "pl-tree-item";

        private VisualElement MakeTreeItem()
        {
            var c = new VisualElement { name = TreeItemName };
            c.style.flexDirection = FlexDirection.Row;
            c.style.flexGrow = 1; c.style.alignItems = Align.Center;
            c.style.paddingLeft = 2; c.style.paddingRight = 4;

            var nameLabel = new Label { name = "name" };
            nameLabel.style.color = new StyleColor(Color.white);
            nameLabel.style.flexGrow = 1; nameLabel.style.flexShrink = 0;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft; nameLabel.style.marginRight = 4;
            c.Add(nameLabel);

            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80; infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new StyleColor(Color.white);
            infoLabel.style.fontSize = 11; infoLabel.style.marginRight = 4;
            c.Add(infoLabel);

            // 担当者バッジ（名前の直後・情報ラベルの手前）
            var editorBadge = new Label { name = "editor-badge" };
            editorBadge.style.flexShrink = 0;
            editorBadge.style.fontSize = 10;
            editorBadge.style.marginRight = 4;
            editorBadge.style.paddingLeft = 4; editorBadge.style.paddingRight = 4;
            editorBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            editorBadge.style.display = DisplayStyle.None;
            c.Insert(1, editorBadge);

            // 行内ボタンを押した瞬間の Ctrl 状態を控える。
            // Clickable はクリックを消費するので行の選択は起きない。Ctrl のときだけ
            // ボタンの機能を無視して選択の反転に振り替える（下の RunRowButton）。
            // MakeTreeItem は行要素の生成時に1回だけ呼ばれるので、ここで登録する
            // （BindTreeItem で登録するとハンドラが行の再利用のたびに積み重なる）。
            c.RegisterCallback<PointerDownEvent>(
                e => _rowCtrlDown = e.ctrlKey || e.commandKey, TrickleDown.TrickleDown);

            var attr = new VisualElement(); attr.style.flexDirection = FlexDirection.Row; attr.style.flexShrink = 0;
            var editorBtn = MkAttrBtn("editor-btn", "\u25CB", "編集者の取得／解放");
            attr.Add(editorBtn);
            var visBtn = MkAttrBtn("vis-btn", "◉", "可視性切り替え");
            var lockBtn = MkAttrBtn("lock-btn", "■", "ロック切り替え");
            var symBtn  = MkAttrBtn("sym-btn", "⇆", "対称切り替え");
            attr.Add(visBtn); attr.Add(lockBtn); attr.Add(symBtn);
            c.Add(attr);

            c.userData = new TreeItemCache
            {
                NameLabel = nameLabel, InfoLabel = infoLabel,
                VisBtn = visBtn, LockBtn = lockBtn, SymBtn = symBtn,
                EditorBadge = editorBadge, EditorBtn = editorBtn,
            };
            return c;
        }

        private Button MkAttrBtn(string name, string icon, string tip)
        {
            var b = new Button { name = name, text = icon, tooltip = tip };
            b.style.width = 24; b.style.height = 18;
            b.style.marginLeft = 1; b.style.marginRight = 1;
            b.style.paddingLeft = 0; b.style.paddingRight = 0; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.fontSize = 12;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            return b;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<SummaryTreeAdapter>(index);
            if (adapter == null) return;
            var cache = element.userData as TreeItemCache;
            if (cache == null) return;
            cache.Adapter = adapter;

            if (cache.NameLabel != null)
            {
                if (adapter.IsMirrorSide)
                {
                    // 選択できる行を薄く描かない。薄さは「選べない（生成ミラー）」の印。
                    cache.NameLabel.text = $"\u25C7 {adapter.DisplayName}";
                    cache.NameLabel.style.opacity = adapter.IsSelectionBlocked ? 0.4f : 1f;
                }
                else if (adapter.IsRealSide)
                { cache.NameLabel.text = $"\u21C6 {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else if (adapter.HasBakedMirrorChild)
                { cache.NameLabel.text = $"\u21C6B {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else
                { cache.NameLabel.text = adapter.DisplayName; cache.NameLabel.style.opacity = 1f; }
            }

            if (cache.InfoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                cache.InfoLabel.text = showInfo
                    ? (_currentTab == TabType.Bone ? $"Bone:{adapter.MeshView.BoneIndex}" : adapter.GetInfoString())
                    : "";
                cache.InfoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            BindEditor(cache, adapter);
            ApplyIndentWidth(element);

            BindAttrBtn(cache.VisBtn, adapter, adapter.IsVisible, "◉", "−",
                () => ToggleVisibilityFromRow(adapter));
            BindAttrBtn(cache.LockBtn, adapter, adapter.IsLocked, "■", "□",
                () => ToggleLockFromRow(adapter));

            if (cache.SymBtn != null)
            {
                bool show = _currentTab == TabType.Drawable;
                cache.SymBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) BindMirrorBtn(cache.SymBtn, adapter);
            }
        }

        // ================================================================
        // 行内ボタン
        //
        // 【役割の分離】
        //   行内のボタン  … 押した行の反転値を対象全件へ揃える。
        //                   押した行が選択に含まれれば選択全件、含まれなければその行だけ。
        //                   どちらの場合も選択そのものは変えない。
        //   ボタン行の一括 … 選択されている行すべてに、決まった値を設定する。
        //
        // 【Ctrl の例外】
        //   Ctrl を押しながら行内ボタンを押した場合、ボタンの機能は実行せず、
        //   その行の選択だけを反転する。行のどこを押しても Ctrl の意味が同じになる。
        // ================================================================

        /// <summary>
        /// 行内ボタンの共通入口。Ctrl 押下時は action を捨てて選択の反転に振り替える。
        /// action が null のボタン（ロック中）は、クリックを消費するだけで何もしない。
        /// </summary>
        private void RunRowButton(SummaryTreeAdapter adapter, Action action)
        {
            if (adapter == null) return;
            if (_rowCtrlDown) { FlipRowSelection(adapter); return; }
            action?.Invoke();
        }

        /// <summary>
        /// その行の選択状態だけを反転する。他の行の選択はそのまま残す。
        /// TreeView 側の表示は Attributes/Selection 通知後の SyncTreeViewSelection が追従する。
        /// </summary>
        private void FlipRowSelection(SummaryTreeAdapter adapter)
        {
            if (adapter == null || adapter.MasterIndex < 0) return;

            var indices = new List<int>(SelIndices());
            if (!indices.Remove(adapter.MasterIndex))
                indices.Add(adapter.MasterIndex);

            SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices.ToArray()));
        }

        /// <summary>
        /// 行内ボタンの適用対象を決める。
        ///   押した行が選択に含まれる → 選択されている行すべて
        ///   含まれない             → 押した行だけ
        /// exclude を渡すと、その条件に合う行を対象から外す（ミラーのロック行など）。
        /// </summary>
        private int[] RowTargets(SummaryTreeAdapter adapter, Func<SummaryTreeAdapter, bool> exclude = null)
        {
            if (adapter == null || adapter.MasterIndex < 0) return Array.Empty<int>();

            bool inSelection = _selectedAdapters.Any(a => a.MasterIndex == adapter.MasterIndex);
            if (!inSelection)
                return (exclude != null && exclude(adapter))
                    ? Array.Empty<int>()
                    : new[] { adapter.MasterIndex };

            return _selectedAdapters
                .Where(a => a.MasterIndex >= 0 && (exclude == null || !exclude(a)))
                .Select(a => a.MasterIndex)
                .ToArray();
        }

        /// <summary>
        /// 可視性。押した行の反転値を対象全件へ揃える。
        /// 「見えている行の目を押したら、選択全部の目を閉じる」という動きになる。
        /// </summary>
        private void ToggleVisibilityFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return;
            var targets = RowTargets(adapter);
            if (targets.Length == 0) return;
            SendCmd(new SetBatchVisibilityCommand(ModelIndex, targets, !adapter.IsVisible));
        }

        /// <summary>ロック。押した行の反転値を対象全件へ揃える。</summary>
        private void ToggleLockFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return;
            var targets = RowTargets(adapter);
            if (targets.Length == 0) return;
            SendCmd(new SetBatchLockCommand(ModelIndex, targets, !adapter.IsLocked));
        }

        /// <summary>
        /// ミラーの有無。押した行の反転値を対象全件へ揃える。
        /// 0 → 1（分離）、1 または 2 → 0。結合(2) はここでは作らない。
        /// ロック行（ミラー側・PMX 由来）は対象から外す。
        /// </summary>
        private void SetMirrorFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null || HasLiveMirrorPeer(adapter)) return;
            // ミラー側メッシュが実在する行は対象から外す。ペアを解体させないため。
            var targets = RowTargets(adapter, HasLiveMirrorPeer);
            if (targets.Length == 0) return;
            // 属性を書くだけでなく、ミラー側メッシュの生成・始末まで行う。
            SendCmd(new SetMirrorEnabledCommand(ModelIndex, targets, adapter.MirrorType == 0));
        }

        // ================================================================
        // ミラー欄の表示
        // ================================================================

        /// <summary>
        /// ミラーボタンの表示と操作を組み立てる。
        ///
        /// このボタンは「ミラーの有無」だけを扱うフラグとする。
        ///   ⇆    : MirrorType が 1（分離）または 2（結合）
        ///   空欄 : MirrorType が 0（なし）
        ///   🪞   : ミラー側そのもの。実体側の従属なので操作させない
        ///
        /// 押すと 0 → 1、1 または 2 → 0。結合(2) にするのは詳細欄のモード選択で行う。
        /// モード・軸・ミラー側メッシュの有無はツールチップで確認できる。
        ///
        /// 【操作できる範囲】
        /// ミラー側メッシュが実在する行（MirrorPair の実体側 / ベイクミラーの実体側 /
        /// ミラー側そのもの）はロックする。これらは MirrorType を変えても
        /// ミラー実体との対応が変わらず、属性だけが実体と食い違うため。
        /// 操作できるのは「MQO 由来の実体なしミラー」＝ミラー側メッシュを持たない行だけ。
        /// </summary>
        private void BindMirrorBtn(Button btn, SummaryTreeAdapter adapter)
        {
            if (btn == null || adapter == null) return;

            bool locked = IsMirrorLocked(adapter);
            bool on     = adapter.MirrorType > 0;

            // OFF のときもラベルを出す。空文字だと「押せるボタンがそこにある」ことが
            // 見えず、ミラーを掛ける入口が存在しないように見えてしまう。
            // 状態の区別は不透明度で付ける（ON=不透明 / OFF=薄い / ロック=中間）。
            btn.text = adapter.IsMirrorSide ? "\u25C7" : "\u21C6";
            btn.style.opacity = locked ? 0.5f : (on ? 1f : 0.28f);
            btn.tooltip = adapter.IsMirrorSide
                ? "ミラー側メッシュ。実体側の従属なのでここでは変更できません。"
                : MirrorTooltip(adapter);

            // ロック中も SetEnabled(false) にはしない。無効な要素はクリックを処理しないため、
            // イベントが行へ渡って選択が差し替わってしまう。
            // 有効なままクリックだけ消費し、何もしないようにする。
            btn.SetEnabled(true);
            btn.clickable = new Clickable(
                () => RunRowButton(adapter, locked ? (Action)null : () => SetMirrorFromRow(adapter)));
        }

        /// <summary>
        /// ミラーの変更をロックする行か。
        ///
        /// ミラー側そのもの（IsMirrorSide）だけをロックする。実体側の従属で、
        /// ここで切り替えても実体側との対応が変わらないため。
        ///
        /// スキニング済みメッシュは特別扱いしない。
        ///   ・分岐の中にあるメッシュは、変換時に反対側ボーンが作られ
        ///     MirrorBoneWeight を持つ。ミラーを掛ければ PMX 型ミラーが
        ///     正しいボーンに紐づいて生成される（CreateDerivedMirrorContext）。
        ///   ・分岐の外にあるメッシュは反対側ボーンが存在しない。
        ///     実体側と同じボーンで動く鏡像になるが、これは中心線上の
        ///     オブジェクトを鏡像化する MQO 系ミラーと同じ結果であり、
        ///     分岐を張らなかったという指定どおりの挙動。
        /// </summary>
        private bool IsMirrorLocked(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return true;
            return adapter.IsMirrorSide;
        }

        /// <summary>
        /// ミラーの有無を切り替える操作（SetMirrorEnabledCommand）の対象から外す行か。
        ///
        /// ミラー側メッシュが実在する行を外す。実体側で切ると DisableMirror が
        /// ミラー側を Mesh へ降格させ MirrorPairs から外す（＝ペアの解体）。
        /// ミラー側はウェイトを持つ独立メッシュなので、選択・編集の途中で
        /// ペアが解体されてはならない。ミラーの有無を扱えるのは、ミラー側メッシュを
        /// まだ持たない行だけ。
        ///
        /// 選択できるかどうかとは別の判定であり、混ぜないこと。
        /// 選択の可否は SummaryTreeAdapter.IsSelectionBlocked。
        /// </summary>
        private static bool HasLiveMirrorPeer(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return true;
            return adapter.IsMirrorSide || adapter.IsBakedMirror
                || adapter.IsRealSide  || adapter.HasBakedMirrorChild;
        }

        private string MirrorTooltip(SummaryTreeAdapter adapter)
        {
            string entity =
                adapter.IsRealSide          ? "ミラー側メッシュあり（ペア同期）" :
                adapter.HasBakedMirrorChild ? "ミラー側メッシュあり（ベイク済み）" :
                                              "ミラー側メッシュなし";
            string howto = IsMirrorLocked(adapter)
                ? "ミラー側メッシュ。実体側の従属なので変更できません"
                : "クリックでミラーの有無を切り替え（結合は詳細欄で設定）";
            return $"ミラー: {MirrorViewUtil.TypeName(adapter.MirrorType)}"
                 + $" / 軸: {MirrorViewUtil.AxisLetter(adapter.MirrorAxis)}"
                 + $"\n{entity}"
                 + $"\n{howto}";
        }

        // ================================================================
        // インデント幅
        // ================================================================

        /// <summary>
        /// 行のインデントを現在値に合わせる。
        ///
        /// TreeView 既定のインデント段組みは、段数と要素幅の対応が実装依存で、
        /// 要素ごとに幅を上書きしても段数を保てない。そこで既定の段組みは
        /// 幅0＋非表示にして無効化し、インデントは自分が生成した行要素
        /// （MakeTreeItem の pl-tree-item）の marginLeft で与える。
        /// この要素は自前で作っているので、必ず反映される。
        ///
        /// 深さは SummaryTreeAdapter.GetDepth()（Parent 連鎖）から取る。
        /// フィルタで親が落ちた場合もツリー実体に従うため、表示とずれない。
        ///
        /// なお既定の段組みを消したぶん、開閉トグルの矢印は深さによらず
        /// 左端に揃う。名前だけが深さぶん右へずれる。
        /// </summary>
        private void ApplyIndentWidth(VisualElement rowContent)
        {
            if (rowContent == null) return;
            var adapter = (rowContent.userData as TreeItemCache)?.Adapter;
            if (adapter == null) return;

            // 既定の段組みを無効化する。祖先を遡って最初に見つかった階層で打ち切る
            // （兄弟行は子孫に含まれないので、他行を巻き込むことはない）。
            var e = rowContent.parent;
            for (int up = 0; e != null && up < 4; up++, e = e.parent)
            {
                bool found = false;
                e.Query(className: BaseTreeView.itemIndentUssClassName)
                 .ForEach(x => { x.style.width = 0f; x.style.display = DisplayStyle.None; found = true; });
                if (found) break;
            }

            rowContent.style.marginLeft = adapter.GetDepth() * _treeIndentWidth;
        }

        /// <summary>表示中の全行のインデントを更新する（スライダー操作時）。</summary>
        private void ApplyIndentWidthToVisibleRows()
        {
            if (_treeView == null) return;
            _treeView.Query(name: TreeItemName).ForEach(ApplyIndentWidth);
        }

        /// <summary>
        /// 担当者バッジと取得／解放ボタンを行に反映する。
        ///
        /// 表示規則:
        ///   担当者なし → バッジ非表示 / ボタン「✋」（押すと取得）
        ///   自分が担当 → バッジ着色   / ボタン「✔」（押すと解放）
        ///   他人が担当 → バッジ着色   / ボタン無効・行を淡色化
        /// </summary>
        private void BindEditor(TreeItemCache cache, SummaryTreeAdapter adapter)
        {
            string editor = adapter.EditorName ?? "";
            bool hasEditor = editor.Length > 0;
            bool isMine    = hasEditor && editor == LocalUserName;
            bool isOthers  = hasEditor && !isMine;

            if (cache.EditorBadge != null)
            {
                cache.EditorBadge.style.display = hasEditor ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasEditor)
                {
                    cache.EditorBadge.text = editor;
                    var col = EditorColor(editor);
                    cache.EditorBadge.style.color = new StyleColor(col);
                    cache.EditorBadge.style.backgroundColor =
                        new StyleColor(new Color(col.r, col.g, col.b, 0.15f));
                }
            }

            // 他人の担当は編集できないので行を淡くして触れないことを示す
            if (cache.NameLabel != null && isOthers)
                cache.NameLabel.style.opacity = 0.55f;

            if (cache.EditorBtn == null) return;

            bool canOperate = !string.IsNullOrEmpty(LocalUserName) && !isOthers;
            // SetEnabled(false) にはしない。無効な要素はクリックを処理しないため、
            // イベントが行へ渡って選択が差し替わってしまう。
            // 有効なままクリックを消費し、下のハンドラが canOperate で弾く。
            cache.EditorBtn.SetEnabled(true);
            cache.EditorBtn.tooltip = isOthers
                ? $"{editor} が編集中です"
                : (isMine ? "編集者を解放する" : "自分を編集者に設定する");
            cache.EditorBtn.style.opacity = canOperate ? 1f : 0.35f;

            // 取得なら自分の名前、解放なら空文字を送る。
            // ObjectIds を添えることでサーバ側がリスト構造のズレを検出できる。
            string icon = isMine ? "\u25CF" : "\u25CB";
            string next = isMine ? "" : LocalUserName;
            BindAttrBtn(cache.EditorBtn, adapter, true, icon, icon,
                () =>
                {
                    if (!canOperate) return;
                    SendCmd(new SetObjectEditorCommand(
                        ModelIndex, new[] { adapter.MasterIndex }, next,
                        new[] { adapter.ObjectId }));
                });
        }

        /// <summary>選択中のオブジェクトをまとめて取得／解放する（パネルの一括操作用）。</summary>
        public void ClaimSelected(bool claim)
        {
            if (string.IsNullOrEmpty(LocalUserName)) return;
            var indices = SelIndices();
            if (indices.Length == 0) return;

            var ids = new ulong[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                var a = _selectedAdapters.FirstOrDefault(x => x.MasterIndex == indices[i]);
                ids[i] = a?.ObjectId ?? 0UL;
            }

            SendCmd(new SetObjectEditorCommand(
                ModelIndex, indices, claim ? LocalUserName : "", ids));
        }

        /// <summary>
        /// 行内ボタンの見た目とクリック処理を設定する。
        /// クリックは必ず RunRowButton を通し、Ctrl 押下時は選択の反転に振り替える。
        /// </summary>
        private void BindAttrBtn(Button btn, SummaryTreeAdapter adapter,
                                 bool active, string onIcon, string offIcon, Action click)
        {
            if (btn == null) return;
            btn.text = active ? onIcon : offIcon;
            btn.style.opacity = active ? 1f : 0.3f;
            btn.clickable = new Clickable(() => RunRowButton(adapter, click));
        }

        // ================================================================
        // 選択（エディタ版と同一）
        // ================================================================

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (_isReceiving || _ctx == null) return;
            _selectedAdapters.Clear();
            // 除外するのは生成ミラーだけ。ミラー側でもスキンド変換後の独立メッシュは
            // 自分のウェイトを保存するため、選択できないとウェイトを塗れない。
            foreach (var item in selection)
                if (item is SummaryTreeAdapter a && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);

            _isReceiving = true;
            try
            {
                var indices = _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
                // 現在のモデルの選択と同じなら送らない。
                // 送ると PlayerCommandDispatcher が EnterTopologyChanged を呼び、
                // GPU バッファの全再構築が走る。
                if (!SameAsCurrentSelection(indices))
                    SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices));
            }
            finally { _isReceiving = false; }

            UpdateDetailPanel();
            UpdateBonePosePanel();
            UpdateTransformPanel();
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            // _isReceiving: OnViewChanged 経由で TreeView をプログラム的に展開/折りたたみ
            // した場合にここへ再入する。Undo/Redo 連鎖記録を防ぐためスキップする。
            if (_isReceiving) return;
            var a = _treeRoot?.FindById(args.id);
            if (a == null) return;
            bool newExpanded = _treeView.IsExpanded(args.id);
            // UI 側アダプタ状態を更新
            a.IsExpanded = newExpanded;
            // データモデル (MeshContext.IsFolding) にも反映 + Undo 記録
            // IsFolding は IsExpanded の反転値 (folding = true で折りたたみ)
            if (a.MasterIndex >= 0)
                SendCmd(new SetMeshFoldingCommand(ModelIndex, a.MasterIndex, !newExpanded));
        }

        /// <summary>
        /// 左ペインのボタンから「すべてのオブジェクトを選択」を実行する。
        /// リスト内のボタン（btn-select-all）と同じ処理へ委ねるだけで、判定は持たない。
        /// </summary>
        public void SelectAllObjectsFromExternal() => SelectAllObjects();

        /// <summary>
        /// 現在のタブのリストにある全オブジェクトを選択する。
        /// ツリーの展開状態は見ないため、折りたたまれている子も対象になる。
        /// ミラー側 / ベイク済みミラーは選択対象外。
        /// </summary>
        private void SelectAllObjects()
        {
            var model = CurrentModel;
            if (model == null) return;

            IReadOnlyList<IMeshView> list = CurrentCategory switch
            {
                MeshCategory.Drawable       => model.DrawableList,
                MeshCategory.Bone           => model.BoneList,
                MeshCategory.RigidBody      => model.RigidBodyList,
                MeshCategory.RigidBodyJoint => model.RigidBodyJointList,
                _                           => null,
            };
            if (list == null) return;

            var indices = new List<int>();
            foreach (var v in list)
            {
                if (v == null) continue;
                // 生成ミラーだけ外す（OnSelectionChanged と同じ判定）。
                if ((v.IsBakedMirror || v.IsMirrorSide) && v.MirrorGeometryDerived) continue;
                if (v.MasterIndex < 0) continue;
                indices.Add(v.MasterIndex);
            }
            if (indices.Count == 0) return;

            var arr = indices.ToArray();

            // 同じ選択を送り直すと GPU 側が丸ごと作り直されるだけなので送らない。
            if (SameAsCurrentSelection(arr)) return;

            SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, arr));
        }

        /// <summary>
        /// indices が現在のモデルの選択と同一か（順序は問わない）。
        /// </summary>
        private bool SameAsCurrentSelection(int[] indices)
        {
            if (CurrentModel == null) return false;
            int[] cur = CurrentCategory switch
            {
                MeshCategory.Drawable => CurrentModel.SelectedDrawableIndices,
                MeshCategory.Bone     => CurrentModel.SelectedBoneIndices,
                MeshCategory.Morph    => CurrentModel.SelectedMorphIndices,
                _                     => null,
            };
            if (cur == null || indices == null) return false;
            if (cur.Length != indices.Length) return false;
            if (indices.Length == 0) return true;

            var set = new HashSet<int>(cur);
            foreach (int i in indices)
                if (!set.Contains(i)) return false;
            return true;
        }

        /// <summary>直前に同期したツリー内 id。展開・スクロールを「変わったときだけ」に絞る判定に使う。</summary>
        private readonly List<int> _lastSyncedSelIds = new List<int>();

        /// <summary>
        /// モデル側の選択をツリーの選択へ反映する。
        ///
        /// 【id と index を取り違えないこと】
        ///   TreeView は BaseVerticalCollectionView から
        ///   SetSelectionWithoutNotify(IEnumerable&lt;int&gt;) を継承しており、これは
        ///   「今表示されている行の index」を取る。SummaryTreeAdapter.Id は
        ///   TreeViewItemData に渡した item id であって index ではない。
        ///   ここへ id を渡すと、折り畳みやフィルタで行数がずれた瞬間に
        ///   まったく別の行が選ばれる（すべて展開・フィルタなしのときだけ
        ///   id と index が一致するので、症状が出たり出なかったりする）。
        ///   id で指定するときは必ず SetSelectionByIdWithoutNotify を使う。
        ///   モーフ側 (SyncMorphSel) は ListView なので index で正しい。
        /// </summary>
        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || CurrentModel == null)
            {
                PLDiag.SelList($"sync skip tree={_treeView != null} root={_treeRoot != null} model={CurrentModel != null}");
                return;
            }

            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
            };
            if (sel == null)
            {
                PLDiag.SelList($"sync clear tab={_currentTab}");
                _treeView.ClearSelection();
                _lastSyncedSelIds.Clear();
                return;
            }

            var ids     = new List<int>();
            var missing = new List<int>();
            foreach (var idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a == null) { missing.Add(idx); continue; }
                ids.Add(a.Id);
            }

            bool changed = !SameIntList(ids, _lastSyncedSelIds);

            PLDiag.SelList(
                $"sync tab={_currentTab} simple={IsSimpleMode} rows={_treeRoot.TotalCount} " +
                $"master=[{string.Join(",", sel)}] ids=[{string.Join(",", ids)}] " +
                $"missing=[{string.Join(",", missing)}] changed={changed}");

            // ここは OnViewChanged の中からも呼ばれる。無条件に false へ戻すと
            // 外側の受信中フラグを途中で落としてしまうため、元の値へ戻す。
            bool prevReceiving = _isReceiving;
            _isReceiving = true;
            try
            {
                // 折り畳みを開くのは「選択が変わった」ときだけ。
                // 属性変更などの通知でも同期は走るので、無条件に開くと
                // 利用者が閉じた枝を勝手に開き直してしまう。
                if (changed)
                    foreach (int id in ids) ExpandAncestorsIfCollapsed(id);

                _treeView.SetSelectionByIdWithoutNotify(ids);
            }
            finally { _isReceiving = prevReceiving; }

            // 選択行が表示範囲の外だと、選ばれていること自体が見えない。
            // 先頭の 1 件までスクロールする（複数選択でも基準を 1 つに決める）。
            if (changed && ids.Count > 0)
            {
                int firstId = ids[0];
                _root?.schedule.Execute(() =>
                {
                    try { _treeView?.ScrollToItemById(firstId); }
                    catch (System.Exception) { /* 行が消えている場合は何もしない */ }
                });
            }

            _lastSyncedSelIds.Clear();
            _lastSyncedSelIds.AddRange(ids);

            RebuildSelectedAdaptersFromCurrentModel();
        }

        private static bool SameIntList(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ================================================================
        // D&D（エディタ版と同一）
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();
            if (_treeView == null || _treeRoot == null) return;
            _dragDropHelper = new TreeViewDragDropHelper<SummaryTreeAdapter>(
                _treeView, _treeRoot, new SummaryDragValidator(), new MeshListRowResolver());
            _dragDropHelper.Setup();
        }

        /// <summary>
        /// 行要素 → アダプタ / 内容要素 の解決器。
        /// 行の並び順から index を数える方法は仮想化リストでずれるため、
        /// BindTreeItem が書き込んだ値を直接読む。
        /// </summary>
        private class MeshListRowResolver : ITreeRowResolver<SummaryTreeAdapter>
        {
            public SummaryTreeAdapter ResolveItem(VisualElement rowElement)
                => (ResolveContent(rowElement)?.userData as TreeItemCache)?.Adapter;

            public VisualElement ResolveContent(VisualElement rowElement)
            {
                if (rowElement == null) return null;
                if (rowElement.userData is TreeItemCache) return rowElement;
                return rowElement.Q<VisualElement>(TreeItemName);
            }
        }

        private void CleanupDragDrop() { _dragDropHelper?.Cleanup(); _dragDropHelper = null; }

        // ================================================================
        // ボタンイベント（エディタ版と同一ロジック、DisplayDialog除去）
        // ================================================================

        private void RegisterButtonEvents()
        {
            _tabDrawable?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone    ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph   ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));
            _tabRigidBody?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.RigidBody));
            _tabJoint   ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Joint));

            Q<Button>("btn-add")      ?.RegisterCallback<ClickEvent>(_ => OnAdd());
            Q<Button>("btn-up")       ?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            Q<Button>("btn-down")     ?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));
            Q<Button>("btn-outdent")  ?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            Q<Button>("btn-indent")   ?.RegisterCallback<ClickEvent>(_ => IndentSelected());
            Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            Q<Button>("btn-delete")   ?.RegisterCallback<ClickEvent>(_ => DeleteSelected());
            Q<Button>("btn-select-all")?.RegisterCallback<ClickEvent>(_ => SelectAllObjects());

            Q<Button>("btn-show")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(true));
            Q<Button>("btn-hide")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(false));

            Q<Button>("btn-lock")      ?.RegisterCallback<ClickEvent>(_ => SetSelectedLock(true));
            Q<Button>("btn-unlock")    ?.RegisterCallback<ClickEvent>(_ => SetSelectedLock(false));
            Q<Button>("btn-mirror-on") ?.RegisterCallback<ClickEvent>(_ => SetSelectedMirror(1));
            Q<Button>("btn-mirror-off")?.RegisterCallback<ClickEvent>(_ => SetSelectedMirror(0));

            Q<Button>("btn-seldic-apply")?.RegisterCallback<ClickEvent>(_ => ApplySelectionDictionary(false));
            Q<Button>("btn-seldic-add")  ?.RegisterCallback<ClickEvent>(_ => ApplySelectionDictionary(true));

            Q<Button>("btn-rename-template")?.RegisterCallback<ClickEvent>(_ => OnRenameSaveTemplate());
            Q<Button>("btn-rename-load")    ?.RegisterCallback<ClickEvent>(_ => OnRenameLoad());
            Q<Button>("btn-rename-apply")   ?.RegisterCallback<ClickEvent>(_ => OnRenameApply());

            _indentSlider?.RegisterValueChangedCallback(e =>
            {
                _treeIndentWidth = Mathf.Round(e.newValue);
                if (_indentValueLabel != null) _indentValueLabel.text = $"{(int)_treeIndentWidth}px";
                ApplyIndentWidthToVisibleRows();
            });

            _detailModeToggle?.RegisterValueChangedCallback(_ => OnDetailModeChanged());
            _showInfoToggle?.RegisterValueChangedCallback(_ => RefreshTree());
            _showMirrorSideToggle?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _filterField?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _meshNameField?.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    ApplyMeshName();
            });
            _meshNameField?.RegisterCallback<FocusOutEvent>(_ => ApplyMeshName());
        }

        private void ApplyMeshName()
        {
            if (_isReceiving || _ctx == null) return;
            if (_selectedAdapters.Count == 1 && _meshNameField != null)
            {
                var newName = _meshNameField.value;
                if (!string.IsNullOrEmpty(newName) && newName != _selectedAdapters[0].MeshView.Name)
                    SendCmd(new RenameMeshCommand(ModelIndex, _selectedAdapters[0].MasterIndex, newName));
            }
        }

        private void OnAdd() => SendCmd(new AddMeshCommand(ModelIndex));

        // ================================================================
        // 選択辞書（オブジェクト選択辞書）
        // ================================================================

        /// <summary>
        /// 辞書名の一覧をドロップダウンへ反映する。並びは
        /// ModelContext.MeshSelectionSets と同じで、位置がそのまま SetIndex になる。
        /// </summary>
        private void RefreshSelectionDictionary()
        {
            if (_selDicDropdown == null) return;

            string prev = _selDicDropdown.value;
            _selDicNames.Clear();
            var names = CurrentModel?.MeshSelectionSetNames;
            if (names != null)
                foreach (var n in names) _selDicNames.Add(n ?? "");

            _selDicDropdown.choices = _selDicNames;
            if (_selDicNames.Count == 0)
            {
                _selDicDropdown.SetValueWithoutNotify("");
                _selDicDropdown.index = -1;
            }
            else
            {
                int idx = prev != null ? _selDicNames.IndexOf(prev) : -1;
                _selDicDropdown.index = idx >= 0 ? idx : 0;
            }

            bool has = _selDicNames.Count > 0;
            _btnSelDicApply?.SetEnabled(has);
            _btnSelDicAdd?.SetEnabled(has);
        }

        /// <summary>辞書を選択へ適用する。addToExisting=true なら現在の選択へ追加。</summary>
        private void ApplySelectionDictionary(bool addToExisting)
        {
            if (_selDicDropdown == null) return;
            int idx = _selDicDropdown.index;
            if (idx < 0 || idx >= _selDicNames.Count) { Log("選択辞書が選ばれていません"); return; }
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, idx, addToExisting));
            Log(addToExisting ? $"辞書を選択に追加: {_selDicNames[idx]}" : $"辞書を選択に適用: {_selDicNames[idx]}");
        }

        // ================================================================
        // 名称一括変更
        // ================================================================

        /// <summary>タブごとに辞書を分けるためのキー要素。</summary>
        private string RenameCategoryKey() => IsSimpleMode ? "mesh" : _currentTab switch
        {
            TabType.Bone      => "bone",
            TabType.Morph     => "morph",
            TabType.RigidBody => "rigidbody",
            TabType.Joint     => "joint",
            _                 => "mesh",
        };

        private string RenamePathKey()         => $"MeshRename.{RenameCategoryKey()}.CsvPath";
        private string RenameDefaultFileName() => $"rename_{RenameCategoryKey()}.csv";

        /// <summary>
        /// 対応表 CSV のパス。手入力があればそれを使い、無ければ
        /// partsDictionary/rename_<カテゴリ>.csv を既定にする。
        /// </summary>
        private string ResolveRenamePath()
        {
            string saved = RecentPaths.Get(RenamePathKey());
            if (!string.IsNullOrEmpty(saved)) return saved;
            return System.IO.Path.Combine(PartsDictionaryPath.Resolve(), RenameDefaultFileName());
        }

        /// <summary>現在のタブが対象にするビュー一覧。</summary>
        private IReadOnlyList<IMeshView> RenameSourceList()
        {
            var model = CurrentModel;
            if (model == null) return null;
            if (IsSimpleMode) return model.DrawableList;
            return _currentTab switch
            {
                TabType.Drawable  => model.DrawableList,
                TabType.Bone      => model.BoneList,
                TabType.Morph     => model.MorphList,
                TabType.RigidBody => model.RigidBodyList,
                TabType.Joint     => model.RigidBodyJointList,
                _                 => null,
            };
        }

        /// <summary>[...] は読込用。書き出し先は雛形書出ボタン側で保存ダイアログを出す。</summary>
        private void OnRenameBrowse()
        {
            string cur = _renamePathField?.value ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveRenamePath();
            string path = PlayerIoUiKit.AskLoadPath("名称一括変更 対応表の読込", RenamePathKey(), cur, "csv");
            if (!string.IsNullOrEmpty(path)) _renamePathField.value = path;
        }

        /// <summary>現在のタブの名前を「旧名,新名（同じ）」で書き出す。</summary>
        private void OnRenameSaveTemplate()
        {
            var source = RenameSourceList();
            if (source == null || source.Count == 0) { RenameStatus("対象がありません"); return; }

            // パス欄は読込用。書き出しは毎回ダイアログを出し、パス欄の値は初期値としてだけ使う。
            string cur = _renamePathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveRenamePath();

            string path = PlayerIoUiKit.AskSavePath(
                "名称一括変更 対応表の書き出し", RenamePathKey(), cur, RenameDefaultFileName(), "csv");
            if (string.IsNullOrEmpty(path)) return;

            _renamePathField.value = path;

            // 既定の受け渡しフォルダを使う場合は作成しておく
            PartsDictionaryPath.ResolveForWrite();

            var names = new List<string>(source.Count);
            foreach (var v in source)
                if (v != null && !string.IsNullOrEmpty(v.Name)) names.Add(v.Name);

            int written = MeshRenameCsvHelper.SaveTemplate(names, path);
            RenameStatus(written >= 0
                ? $"雛形を書き出しました: {written} 行 → {path}"
                : "雛形の書き出しに失敗しました（ログを参照）");
        }

        /// <summary>
        /// 対応表を読み込み、現在のタブの名前と突き合わせて適用対象を決める。
        /// 実際の重複回避は適用時に受け側で行う。
        /// </summary>
        private void OnRenameLoad()
        {
            _renameTargetIndices = null;
            _renameTargetNames   = null;

            // 読込は必ずダイアログを通す。パス欄の値（無ければ既定の受け渡しファイル）は
            // 初期フォルダ／初期ファイル名としてだけ使う。
            string cur = _renamePathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveRenamePath();

            string path = PlayerIoUiKit.AskLoadPath("名称一括変更 対応表の読込", RenamePathKey(), cur, "csv");
            if (string.IsNullOrEmpty(path)) { UpdateRenameButtonStates(); return; }

            _renamePathField.value = path;

            var pairs = MeshRenameCsvHelper.LoadPairs(path);
            if (pairs == null) { RenameStatus("読込に失敗しました（ログを参照）"); UpdateRenameButtonStates(); return; }
            if (pairs.Count == 0) { RenameStatus("有効な行がありません"); UpdateRenameButtonStates(); return; }

            var source = RenameSourceList();
            if (source == null) { RenameStatus("対象がありません"); UpdateRenameButtonStates(); return; }

            var indices  = new List<int>();
            var newNames = new List<string>();
            var used     = new HashSet<int>();
            int unmatched = 0, skipped = 0;

            foreach (var pair in pairs)
            {
                bool hit = false;
                foreach (var v in source)
                {
                    if (v == null || v.Name != pair.OldName) continue;
                    hit = true;
                    if (!used.Add(v.MasterIndex)) { skipped++; continue; }
                    indices.Add(v.MasterIndex);
                    newNames.Add(pair.NewName);
                }
                if (!hit) unmatched++;
            }

            _renameTargetIndices = indices.ToArray();
            _renameTargetNames   = newNames.ToArray();

            string msg = $"対象 {indices.Count} 件 / 未一致 {unmatched} 件";
            if (skipped > 0) msg += $" / 重複指定 {skipped} 件";
            RenameStatus(msg);
            UpdateRenameButtonStates();
        }

        private void OnRenameApply()
        {
            if (_renameTargetIndices == null || _renameTargetIndices.Length == 0)
            {
                RenameStatus("先に読込を実行してください");
                return;
            }
            SendCmd(new RenameMeshesCommand(ModelIndex, _renameTargetIndices, _renameTargetNames));
            RenameStatus($"適用しました: {_renameTargetIndices.Length} 件（重複名は自動回避）");
        }

        /// <summary>タブ切り替え時など、読込済みの対応表を捨ててパスを引き直す。</summary>
        private void ResetRenameState()
        {
            _renameTargetIndices = null;
            _renameTargetNames   = null;
            _renamePathField?.SetValueWithoutNotify(ResolveRenamePath());
            RenameStatus("");
            UpdateRenameButtonStates();
        }

        private void UpdateRenameButtonStates()
        {
            bool hasTarget = _renameTargetIndices != null && _renameTargetIndices.Length > 0;
            _btnRenameApply?.SetEnabled(hasTarget);
        }

        private void RenameStatus(string m) { if (_renameStatusLabel != null) _renameStatusLabel.text = m; }

        private void MoveSelected(int dir)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            if (TreeViewHelper.MoveItems(_selectedAdapters, _treeRoot.RootItems, dir))
                _treeRoot.OnTreeChanged();
        }

        private void OutdentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Outdent(_selectedAdapters[0], _treeRoot.RootItems))
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void IndentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Indent(_selectedAdapters[0], _treeRoot.RootItems))
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void DuplicateSelected()
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DuplicateMeshesCommand(ModelIndex, SelIndices()));
        }

        private void DeleteSelected()
        {
            // Player: EditorUtility.DisplayDialog なし、即実行
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DeleteMeshesCommand(ModelIndex,
                _selectedAdapters.OrderByDescending(a => a.MasterIndex).Select(a => a.MasterIndex).ToArray()));
            _selectedAdapters.Clear();
        }

        private void SetSelectedVisibility(bool visible)
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new SetBatchVisibilityCommand(ModelIndex, SelIndices(), visible));
        }

        /// <summary>選択されている行すべてのロックを設定する。</summary>
        private void SetSelectedLock(bool locked)
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new SetBatchLockCommand(ModelIndex, SelIndices(), locked));
        }

        /// <summary>
        /// 選択されている行すべてのミラーを設定する。
        /// ロック対象（ミラー側・PMX 由来）は除外する。
        /// </summary>
        private void SetSelectedMirror(int mirrorType)
        {
            // ミラー側メッシュが実在する行は対象から外す。ペアを解体させないため。
            var targets = _selectedAdapters
                .Where(a => !HasLiveMirrorPeer(a))
                .Where(a => mirrorType == 0 ? a.MirrorType != 0 : a.MirrorType == 0)
                .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
            if (targets.Length == 0) { Log("ミラーを変更できる行が選択されていません"); return; }
            SendCmd(new SetMirrorEnabledCommand(ModelIndex, targets, mirrorType != 0));
        }

        private void MoveToEdge(bool toTop)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            var item = _selectedAdapters[0];
            var siblings = item.Parent?.Children ?? _treeRoot.RootItems;
            int pos = siblings.IndexOf(item);
            if (toTop && pos > 0) { siblings.Remove(item); siblings.Insert(0, item); _treeRoot.OnTreeChanged(); }
            else if (!toTop && pos < siblings.Count - 1) { siblings.Remove(item); siblings.Add(item); _treeRoot.OnTreeChanged(); }
        }

        // ================================================================
        // OnViewChanged（エディタ版と同一、EditorApplication.delayCall を schedule.Execute に）
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                switch (kind)
                {
                    case ChangeKind.Selection:
                        if (_currentTab != TabType.Morph) SyncTreeViewSelection(); else SyncMorphSel();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.Attributes:
                        if (_currentTab != TabType.Morph)
                        {
                            // MeshContext.IsFolding の変化 (Undo/Redo 等) を TreeView 展開状態に反映
                            if (_treeRoot != null)
                                SyncExpandedFromData(_treeRoot.RootItems);
                            RefreshAllAdapterViews();
                            _treeView?.RefreshItems();
                            SyncTreeViewSelection();
                        }
                        else RefreshMorphEditor();
                        // Bone タブは BonePoseData 等が変化した可能性があるため
                        // _selectedAdapters のビューを最新スナップショットで更新する
                        if (_currentTab == TabType.Bone) RefreshSelectedAdapterViews();
                        // 辞書の追加・削除・改名は Attributes で通知される
                        RefreshSelectionDictionary();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.ListStructure:
                    case ChangeKind.ModelSwitch:
                    default:
                        // スキンドメッシュの自動設定
                        if (_detailModeToggle != null)
                        {
                            var model = view?.CurrentModel;
                            bool hasSkinned = model?.DrawableList?.Any(v => v.HasBoneWeight) ?? false;
                            _detailModeToggle.SetValueWithoutNotify(hasSkinned);
                            OnDetailModeChanged();
                        }
                        if (_currentTab != TabType.Morph) { CreateTreeRoot(); RefreshAllImmediate(); SyncTreeViewSelection(); }
                        if (_currentTab == TabType.Morph) RefreshMorphEditor();
                        RefreshSelectionDictionary();
                        UpdateRenameButtonStates();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                }
            }
            finally { _root?.schedule.Execute(() => _isReceiving = false); }
        }

        // ================================================================
        // 更新（RefreshTree の delayCall → schedule.Execute）
        // ================================================================

        /// <summary>
        /// 初期高さ = min(基準の4倍, 現在の展開状態で見える行がすべて収まる高さ)。
        /// 下端ドラッグで手動調整された後は何もしない。
        /// </summary>
        private void ApplyAutoTreeHeight()
        {
            if (_treeView == null || _treeRoot == null) return;
            if (_treeHeightUserAdjusted) return;

            int visibleRows = CountVisibleRows(_treeRoot.RootItems);
            if (visibleRows <= 0) return;

            float rowH   = _treeView.fixedItemHeight > 0f ? _treeView.fixedItemHeight : 20f;
            float needed = visibleRows * rowH + 4f;                 // 4f = 上下の枠ぶん
            float h      = Mathf.Max(TreeMinHeight,
                                     Mathf.Min(TreeBaseHeight * TreeInitialScale, needed));
            if (Mathf.Approximately(h, _treeHeight)) return;

            _treeHeight = h;
            // TreeView は height を無視するため min/max も同値にする。
            _treeView.style.height    = h;
            _treeView.style.minHeight = h;
            _treeView.style.maxHeight = h;
        }

        /// <summary>展開状態を加味した可視行数。畳まれた子は数えない。</summary>
        private int CountVisibleRows(List<SummaryTreeAdapter> items)
        {
            if (items == null) return 0;
            int n = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                n++;
                if (it.Children != null && it.Children.Count > 0 && _treeView.IsExpanded(it.Id))
                    n += CountVisibleRows(it.Children);
            }
            return n;
        }

        private void RefreshAllImmediate() { RefreshTreeImmediate(); UpdateHeader(); UpdateDetailPanel(); }

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null || _refreshScheduled) return;
            _refreshScheduled = true;
            _root?.schedule.Execute(() => { _refreshScheduled = false; ApplyTreeToView(); });
        }

        private void RefreshTreeImmediate()
        {
            if (_treeView == null || _treeRoot == null) return;
            _refreshScheduled = false;
            ApplyTreeToView();
        }

        private void ApplyTreeToView()
        {
            if (_treeView == null || _treeRoot == null) return;

            // Rebuild() の前後でスクロール位置を保持する。
            // 保持処理を入れないと、▲▼移動 / Indent / Outdent / D&D のたびに
            // リストが先頭まで戻ってしまう。
            var scroll = _treeView.Q<ScrollView>();
            Vector2 keep = scroll != null ? scroll.scrollOffset : Vector2.zero;

            // 表示の作り直しは「操作」ではない。Rebuild() は selectionChanged を、
            // RestoreExpanded() は itemExpandedChanged を発火させるため、囲わないと
            // OnSelectionChanged が SelectMeshCommand を送ってしまい、
            // GPU バッファの全再構築が走る。選択の再同期は SyncTreeViewSelection が行う。
            bool prevReceiving = _isReceiving;
            _isReceiving = true;
            try
            {
                _treeView.SetRootItems(TreeViewHelper.BuildTreeData(_treeRoot.RootItems));
                _treeView.Rebuild();
                RestoreExpanded(_treeRoot.RootItems);
            }
            finally { _isReceiving = prevReceiving; }

            _applyTreeGeneration++;
            ApplyAutoTreeHeight();

            if (scroll != null && (keep.x > 0f || keep.y > 0f))
            {
                scroll.scrollOffset = keep;
                // Rebuild 直後はコンテンツ高がまだ確定しておらず、代入値が
                // クランプされることがある。レイアウト確定後に再度セットする。
                _root?.schedule.Execute(() =>
                {
                    var s = _treeView?.Q<ScrollView>();
                    if (s != null) s.scrollOffset = keep;
                });
            }
        }

        /// <summary>
        /// この行を表示するために本当に必要な祖先だけを開く。
        /// 既に開いている枝には触らない。
        ///
        /// データ側 (MeshContext.IsFolding) にも同じ操作を送る。送らないと、
        /// 次の属性通知で SyncExpandedFromData が IsFolding を見て閉じ直してしまう。
        /// 呼び出し元で _isReceiving を立てているため、この送信で OnViewChanged へ
        /// 再入しても弾かれる。
        /// </summary>
        private void ExpandAncestorsIfCollapsed(int id)
        {
            if (_treeView == null || _treeRoot == null) return;
            var item = _treeRoot.FindById(id);
            if (item == null) return;

            for (var p = item.Parent; p != null; p = p.Parent)
            {
                if (_treeView.IsExpanded(p.Id)) continue;   // 開いている枝は触らない

                p.IsExpanded = true;
                _treeView.ExpandItem(p.Id, false);
                if (p.MasterIndex >= 0)
                    SendCmd(new SetMeshFoldingCommand(ModelIndex, p.MasterIndex, false));

                PLDiag.SelList($"expand ancestor id={p.Id} master={p.MasterIndex} name={p.DisplayName}");
            }
        }

        private void RestoreExpanded(List<SummaryTreeAdapter> items)
        {
            foreach (var i in items)
            {
                if (i.IsExpanded) _treeView.ExpandItem(i.Id, false);
                if (i.HasChildren) RestoreExpanded(i.Children);
            }
        }

        /// <summary>
        /// MeshContext.IsFolding (データ側) の最新値を SummaryTreeAdapter.IsExpanded (UI 側)
        /// および TreeView の展開状態に反映する。
        /// Undo/Redo 経由で IsFolding が書き換わった際に呼び、UI を追従させる。
        /// </summary>
        private void SyncExpandedFromData(List<SummaryTreeAdapter> items)
        {
            if (items == null || _treeView == null) return;
            foreach (var i in items)
            {
                bool shouldExpand = !i.MeshView.IsFolding;
                if (i.IsExpanded != shouldExpand)
                {
                    i.IsExpanded = shouldExpand;
                    if (shouldExpand) _treeView.ExpandItem(i.Id, false);
                    else              _treeView.CollapseItem(i.Id, false);
                }
                if (i.HasChildren) SyncExpandedFromData(i.Children);
            }
        }

        private void UpdateHeader()
        {
            if (_countLabel == null) return;
            if (IsSimpleMode) { _countLabel.text = $"メッシュ+ボーン: {_treeRoot?.TotalCount ?? 0}"; return; }
            string label = _currentTab switch { TabType.Drawable => "メッシュ", TabType.Bone => "ボーン", TabType.RigidBody => "剛体", TabType.Joint => "Joint", _ => "モーフ" };
            _countLabel.text = $"{label}: {_treeRoot?.TotalCount ?? 0}";
        }

        // ================================================================
        // 詳細パネル（エディタ版と同一）
        // ================================================================

        /// <summary>
        /// 詳細欄のミラーモード表示を更新する。
        /// mixed=true は選択内で値が揃っていないことを示す。
        /// </summary>
        private void SetMirrorMode(int mirrorType, bool enabled, bool mixed = false)
        {
            if (_mirrorModeDropdown == null) return;
            int idx = MirrorViewUtil.ClampType(mirrorType);
            _mirrorModeDropdown.SetValueWithoutNotify(MirrorModeChoices[idx]);
            _mirrorModeDropdown.showMixedValue = mixed;
            _mirrorModeDropdown.SetEnabled(enabled);
        }

        private void UpdateDetailPanel()
        {
            if (_currentTab == TabType.Morph) return;
            if (_selectedAdapters.Count == 0)
            {
                _meshNameField?.SetValueWithoutNotify("");
                SL(_vertexCountLabel, "頂点: -"); SL(_faceCountLabel, "面: -");
                SL(_triCountLabel, "三角形: -"); SL(_quadCountLabel, "四角形: -"); SL(_ngonCountLabel, "多角形: -");
                SL(_boneIndexLabel, "ボーンIdx: -"); SL(_masterIndexLabel, "マスターIdx: -");
                _ignorePoseToggle?.SetValueWithoutNotify(false);
                _preserveNormalsToggle?.SetValueWithoutNotify(false);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(false);
                _mirrorBranchRootToggle?.SetEnabled(false);
                SetMirrorMode(0, false);
                _detailFoldout?.SetEnabled(false);
                return;
            }
            _detailFoldout?.SetEnabled(true);
            if (_selectedAdapters.Count == 1)
            {
                var s = _selectedAdapters[0].MeshView;
                _meshNameField?.SetValueWithoutNotify(s.Name); _meshNameField?.SetEnabled(true);
                SL(_vertexCountLabel, $"頂点: {s.VertexCount}"); SL(_faceCountLabel, $"面: {s.FaceCount}");
                SL(_triCountLabel, $"三角形: {s.TriCount}"); SL(_quadCountLabel, $"四角形: {s.QuadCount}"); SL(_ngonCountLabel, $"多角形: {s.NgonCount}");
                SL(_boneIndexLabel, $"ボーンIdx: {s.BoneIndex}"); SL(_masterIndexLabel, $"マスターIdx: {s.MasterIndex}");
                _ignorePoseToggle?.SetValueWithoutNotify(s.IgnorePoseInArmature);
                _ignorePoseToggle?.SetEnabled(true);
                _preserveNormalsToggle?.SetValueWithoutNotify(s.PreserveNormals);
                _preserveNormalsToggle?.SetEnabled(true);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(s.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetEnabled(true);
                // ミラー側と PMX 由来のミラーは変更させない
                SetMirrorMode(s.MirrorType, !IsMirrorLocked(_selectedAdapters[0]));
            }
            else
            {
                _meshNameField?.SetValueWithoutNotify($"({_selectedAdapters.Count}個選択)"); _meshNameField?.SetEnabled(false);
                SL(_vertexCountLabel, $"頂点: {_selectedAdapters.Sum(a => a.VertexCount)} (合計)");
                SL(_faceCountLabel,   $"面: {_selectedAdapters.Sum(a => a.FaceCount)} (合計)");
                // 複数選択: 全て同値なら表示、異なればfalse表示
                bool allSame = _selectedAdapters.All(a => a.MeshView.IgnorePoseInArmature == _selectedAdapters[0].MeshView.IgnorePoseInArmature);
                _ignorePoseToggle?.SetValueWithoutNotify(allSame && _selectedAdapters[0].MeshView.IgnorePoseInArmature);
                _ignorePoseToggle?.SetEnabled(true);
                bool pnAllSame = _selectedAdapters.All(a => a.MeshView.PreserveNormals == _selectedAdapters[0].MeshView.PreserveNormals);
                _preserveNormalsToggle?.SetValueWithoutNotify(pnAllSame && _selectedAdapters[0].MeshView.PreserveNormals);
                _preserveNormalsToggle?.SetEnabled(true);
                bool mbAllSame = _selectedAdapters.All(a => a.MeshView.IsMirrorBranchRoot == _selectedAdapters[0].MeshView.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(mbAllSame && _selectedAdapters[0].MeshView.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetEnabled(true);
                bool mtAllSame = _selectedAdapters.All(a => a.MirrorType == _selectedAdapters[0].MirrorType);
                bool anyEditable = _selectedAdapters.Any(a => !IsMirrorLocked(a));
                SetMirrorMode(_selectedAdapters[0].MirrorType, anyEditable, mixed: !mtAllSame);
            }
        }

        // ================================================================
        // BonePose（エディタ版と同一）
        // ================================================================

        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = new VisualElement { name = "bone-pose-section" };
            _bonePoseSection.style.marginTop = 4;

            _poseFoldout = new Foldout { text = "ボーンポーズ", value = true, name = "pose-foldout" };

            _poseActiveToggle = new Toggle("アクティブ") { name = "pose-active-toggle" };
            _poseActiveToggle.style.color = new StyleColor(Color.white);
            _poseActiveToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                SendCmd(new SetBonePoseActiveCommand(ModelIndex, SelIndices(), e.newValue));
            });
            _poseFoldout.Add(_poseActiveToggle);

            _poseFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_poseFoldout, out _restPosX, out _restPosY, out _restPosZ, "rest-pos");
            RegRestTF(_restPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegRestTF(_restPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegRestTF(_restPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _poseFoldout.Add(SectionHeader("回転"));
            AddRotFields(_poseFoldout,
                out _restRotX, out _restRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _restRotY, out _restRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _restRotZ, out _restRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: true);

            _poseFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_poseFoldout, out _restSclX, out _restSclY, out _restSclZ, "rest-scl");
            RegRestTF(_restSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegRestTF(_restSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegRestTF(_restSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _poseFoldout.Add(_poseResultPos); _poseFoldout.Add(_poseResultRot);

            _poseLayersContainer = new VisualElement { name = "pose-layers-container" };
            _poseNoLayersLabel = new Label("(レイヤーなし)") { name = "pose-no-layers-label" };
            _poseNoLayersLabel.style.color = new StyleColor(Color.white);
            _poseLayersContainer.Add(_poseNoLayersLabel);
            _poseFoldout.Add(_poseLayersContainer);

            var poseRow = new VisualElement(); poseRow.style.flexDirection = FlexDirection.Row; poseRow.style.marginTop = 4;
            _btnInitPose     = MakeSmallBtn("初期化", "btn-init-pose");
            _btnResetLayers  = MakeSmallBtn("レイヤーリセット", "btn-reset-layers");
            poseRow.Add(_btnInitPose); poseRow.Add(_btnResetLayers);
            _poseFoldout.Add(poseRow);
            _bonePoseSection.Add(_poseFoldout);

            _bindposeFoldout = new Foldout { text = "バインドポーズ", value = false, name = "bindpose-foldout" };
            _bindposeFoldout.Add(_bindposePos); _bindposeFoldout.Add(_bindposeRot); _bindposeFoldout.Add(_bindposeScl);
            _btnBakePose = MakeSmallBtn("ポーズベイク", "btn-bake-pose");
            _bindposeFoldout.Add(_btnBakePose);
            _bonePoseSection.Add(_bindposeFoldout);

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new InitBonePoseCommand(ModelIndex, i)); });
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new ResetBonePoseLayersCommand(ModelIndex, i)); });
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new BakePoseToBindPoseCommand(ModelIndex, i)); });

            _mainContent?.Add(_bonePoseSection);
        }

        private void RegRestTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegRestRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingPoseUI = false; }
            });
        }

        private void RegRestRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingPoseUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "ボーン回転変更")));
        }

        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;

            if (IsSimpleMode)
            {
                bool show = _selectedAdapters.Any(a => a.MeshView.BonePose.HasPose);
                _bonePoseSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) return;
            }

            if (_currentTab != TabType.Bone) return;
            _isSyncingPoseUI = true;
            try
            {
                if (_selectedAdapters.Count == 0) { SetPoseEmpty(); return; }
                var poses = _selectedAdapters.Select(a => a.MeshView.BonePose).Where(bp => bp.HasPose).ToList();
                bool all  = poses.Count == _selectedAdapters.Count;
                bool none = poses.Count == 0;

                if (all) { bool f = poses[0].IsActive; bool same = poses.TrueForAll(p => p.IsActive == f); _poseActiveToggle?.SetValueWithoutNotify(same ? f : false); SMV(_poseActiveToggle, !same); }
                else { _poseActiveToggle?.SetValueWithoutNotify(false); SMV(_poseActiveToggle, !none); }
                _poseActiveToggle?.SetEnabled(true);

                if (all && poses.Count > 0)
                {
                    var views = _selectedAdapters.Select(a => a.MeshView).ToList();
                    MixFT(_restPosX, views, v => v.LocalPosition.x); MixFT(_restPosY, views, v => v.LocalPosition.y); MixFT(_restPosZ, views, v => v.LocalPosition.z);
                    MixRTF(_restRotX, _restRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_restRotY, _restRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_restRotZ, _restRotSliderZ, views, v => v.LocalRotationEuler.z);
                    MixFT(_restSclX, views, v => v.LocalScale.x); MixFT(_restSclY, views, v => v.LocalScale.y); MixFT(_restSclZ, views, v => v.LocalScale.z);
                }
                else
                {
                    SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
                    SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
                    SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
                    SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
                }

                var single = (_selectedAdapters.Count == 1 && all) ? poses[0] : null;
                UpdateLayers(single);
                if (single != null)
                {
                    SL(_poseResultPos, $"Pos: ({single.ResultPosition.x:F3}, {single.ResultPosition.y:F3}, {single.ResultPosition.z:F3})");
                    SL(_poseResultRot, $"Rot: ({single.ResultRotationEuler.x:F1}, {single.ResultRotationEuler.y:F1}, {single.ResultRotationEuler.z:F1})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_poseResultPos, $"Pos: {m}"); SL(_poseResultRot, $"Rot: {m}"); }

                _btnInitPose?.SetEnabled(false); if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(all && poses.Any(p => p.LayerCount > 0));

                if (_selectedAdapters.Count == 1 && all)
                {
                    var bp = poses[0];
                    SL(_bindposePos, $"Pos: ({bp.BindPosePosition.x:F3}, {bp.BindPosePosition.y:F3}, {bp.BindPosePosition.z:F3})");
                    SL(_bindposeRot, $"Rot: ({bp.BindPoseRotationEuler.x:F1}, {bp.BindPoseRotationEuler.y:F1}, {bp.BindPoseRotationEuler.z:F1})");
                    SL(_bindposeScl, $"Scl: ({bp.BindPoseScale.x:F3}, {bp.BindPoseScale.y:F3}, {bp.BindPoseScale.z:F3})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_bindposePos, $"Pos: {m}"); SL(_bindposeRot, $"Rot: {m}"); SL(_bindposeScl, $"Scl: {m}"); }
                _btnBakePose?.SetEnabled(all);
            }
            finally { _isSyncingPoseUI = false; }
        }

        private void SetPoseEmpty()
        {
            _poseActiveToggle?.SetValueWithoutNotify(false); _poseActiveToggle?.SetEnabled(false); SMV(_poseActiveToggle, false);
            SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
            SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
            SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
            SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
            UpdateLayers(null);
            SL(_poseResultPos,"Pos: -"); SL(_poseResultRot,"Rot: -");
            if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
            _btnResetLayers?.SetEnabled(false);
            SL(_bindposePos,"Pos: -"); SL(_bindposeRot,"Rot: -"); SL(_bindposeScl,"Scl: -");
            _btnBakePose?.SetEnabled(false);
        }

        private void UpdateLayers(IBonePoseView pose)
        {
            if (_poseLayersContainer == null) return;
            var rm = _poseLayersContainer.Children().Where(c => c.ClassListContains("pose-layer-row")).ToList();
            foreach (var e in rm) _poseLayersContainer.Remove(e);
            bool has = pose != null && pose.LayerCount > 0;
            if (_poseNoLayersLabel != null) _poseNoLayersLabel.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            if (has)
            {
                var row = new VisualElement(); row.AddToClassList("pose-layer-row");
                row.Add(new Label($"({pose.LayerCount} layers)") { style = { fontSize = 11 } });
                _poseLayersContainer.Add(row);
            }
        }

        // ================================================================
        // モーフエディタ（エディタ版と同一、PopupField<int>→DropdownField）
        // ================================================================

        private void BindMorphEditorUI(VisualElement root)
        {
            if (_morphListView != null)
            {
                _morphListView.makeItem  = MorphMake;
                _morphListView.bindItem  = MorphBind;
                _morphListView.fixedItemHeight = 20;
                _morphListView.itemsSource     = _morphFilteredData;
                _morphListView.selectionType   = SelectionType.Multiple;
                _morphListView.selectionChanged += OnMorphSel;
            }
            Q<Button>("btn-morph-test-reset")       ?.RegisterCallback<ClickEvent>(_ => OnMorphTestReset());
            Q<Button>("btn-morph-test-select-all")  ?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(true));
            Q<Button>("btn-morph-test-deselect-all")?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(false));
            _morphTestWeight?.RegisterValueChangedCallback(OnMorphWeight);
            _morphFilterField?.RegisterValueChangedCallback(_ => RefreshMorphListData());
            _btnMeshToMorph?.RegisterCallback<ClickEvent>(_ => OnMeshToMorph());
            _btnMorphToMesh?.RegisterCallback<ClickEvent>(_ => OnMorphToMesh());
            _btnCreateMorphSet?.RegisterCallback<ClickEvent>(_ => OnCreateMorphSet());
        }

        // USS クラス名はエディタ版と揃えてあるが、Player は USS を読み込まないため
        // 行の並び・色・寸法はここでインラインに指定する。
        // 指定しないと既定の Column 並びで名前と情報が縦積みになり、
        // fixedItemHeight = 20 に収まらず表示が崩れる。
        private VisualElement MorphMake()
        {
            var r = new VisualElement(); r.AddToClassList("morph-list-row");
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems    = Align.Center;
            r.style.paddingLeft   = 2; r.style.paddingRight = 4;

            var nl = new Label { name = "n" }; nl.AddToClassList("morph-list-name");
            nl.style.color         = new StyleColor(Color.white);
            nl.style.flexGrow      = 1;
            nl.style.flexShrink    = 1;
            nl.style.marginRight   = 4;
            nl.style.unityTextAlign = TextAnchor.MiddleLeft;
            r.Add(nl);

            var il = new Label { name = "i" }; il.AddToClassList("morph-list-info");
            il.style.color         = new StyleColor(Color.white);
            il.style.width         = 90;
            il.style.flexShrink    = 0;
            il.style.fontSize      = 11;
            il.style.unityTextAlign = TextAnchor.MiddleRight;
            r.Add(il);
            return r;
        }

        private void MorphBind(VisualElement el, int idx)
        {
            if (idx < 0 || idx >= _morphFilteredData.Count) return;
            var s  = _morphFilteredData[idx];
            var nl = el.Q<Label>("n"); if (nl != null) nl.text = s.Name;
            var il = el.Q<Label>("i");
            if (il != null)
            {
                if (s.MorphParentIndex >= 0) { var pn = FindDrawableName(s.MorphParentIndex); il.text = pn != null ? $"→{pn}" : $"→[{s.MorphParentIndex}]"; }
                else if (!string.IsNullOrEmpty(s.MorphName)) il.text = s.MorphName;
                else il.text = "";
            }
        }

        private void RefreshMorphEditor() { if (CurrentModel == null) return; RefreshMorphListData(); RefreshMorphConvert(); RefreshMorphSet(); }

        private void RefreshMorphListData()
        {
            // ================================================================
            // ミラー側モーフは一覧に出さない。
            //   Real 側から自動同期される派生物で、ユーザーが選んで編集する対象ではない
            //   （規約は MorphMirrorPolicy.cs を正典とする）。
            //   一覧に並べるとモーフ1つにつき2行になり、管理対象が倍に見えてしまう。
            //   隠した数は件数ラベルに「(派生 N)」として出し、消えたわけではないと分かるようにする。
            // ================================================================
            _morphListData.Clear(); _morphFilteredData.Clear();

            int derivedHidden = 0;
            if (CurrentModel?.MorphList != null)
            {
                foreach (var s in CurrentModel.MorphList)
                {
                    if (s == null) continue;
                    if (s.IsMirrorSide) { derivedHidden++; continue; }
                    _morphListData.Add(s);
                }
            }

            string f = _morphFilterField?.value;
            foreach (var s in _morphListData)
                if (string.IsNullOrEmpty(f) || s.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    _morphFilteredData.Add(s);

            if (_morphCountLabel != null)
            {
                _morphCountLabel.text = derivedHidden > 0
                    ? $"モーフ: {_morphFilteredData.Count} (派生 {derivedHidden})"
                    : $"モーフ: {_morphFilteredData.Count}";
            }

            _morphListView?.RefreshItems();
            SyncMorphSel();
        }

        private void OnMorphSel(IEnumerable<object> _)
        {
            if (_isSyncingMorphSelection || _isReceiving || _ctx == null) return;
            if (_isMorphPreviewStarted) { SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f); }
            var ids = new List<int>();
            foreach (int i in _morphListView.selectedIndices)
                if (i >= 0 && i < _morphFilteredData.Count) ids.Add(_morphFilteredData[i].MasterIndex);
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Morph, ids.ToArray()));
        }

        private void SyncMorphSel()
        {
            if (_morphListView == null || CurrentModel == null) return;
            _isSyncingMorphSelection = true;
            try
            {
                var set = new HashSet<int>(CurrentModel.SelectedMorphIndices ?? Array.Empty<int>());
                var li  = new List<int>();
                for (int i = 0; i < _morphFilteredData.Count; i++)
                    if (set.Contains(_morphFilteredData[i].MasterIndex)) li.Add(i);
                _morphListView.SetSelectionWithoutNotify(li);
            }
            finally { _isSyncingMorphSelection = false; }
        }

        // RefreshMorphConvert: PopupField<int> → DropdownField
        private void RefreshMorphConvert()
        {
            if (CurrentModel == null) return;
            var labels = new List<string> { "(なし)" };
            _morphSourceMeshIds = new List<int> { -1 };
            _morphParentIds     = new List<int> { -1 };
            foreach (var s in CurrentModel.DrawableList ?? (IReadOnlyList<IMeshView>)Array.Empty<IMeshView>())
            {
                labels.Add($"[{s.MasterIndex}] {s.Name}");
                _morphSourceMeshIds.Add(s.MasterIndex);
                _morphParentIds.Add(s.MasterIndex);
            }
            RebuildDropdown(ref _morphSourceMeshDropdown, _morphSourceMeshPopupContainer, labels);
            RebuildDropdown(ref _morphParentDropdown,     _morphParentPopupContainer,     labels);

            var panelLabels = new List<string> { "眉", "目", "口", "その他" };
            RebuildDropdown(ref _morphPanelDropdown, _morphPanelPopupContainer, panelLabels, 3);
        }

        private void RefreshMorphSet()
        {
            if (CurrentModel == null) return;
            var stLabels = new List<string> { "Vertex", "UV" };
            RebuildDropdown(ref _morphSetTypeDropdown, _morphSetTypePopupContainer, stLabels, 0);
        }

        private static void RebuildDropdown(ref DropdownField df, VisualElement container, List<string> choices, int initial = 0)
        {
            if (container == null) return;
            if (df == null)
            {
                df = new DropdownField(choices, initial);
                df.style.color = new StyleColor(Color.white);
                df.AddToClassList("morph-popup"); df.style.flexGrow = 1;
                container.Add(df);
            }
            else
            {
                df.choices = choices;
                df.SetValueWithoutNotify(choices.Count > 0 ? choices[Mathf.Clamp(initial, 0, choices.Count - 1)] : "");
            }
        }

        private void OnMeshToMorph()
        {
            int srcIdx = (_morphSourceMeshDropdown?.index ?? 0) - 1; // 0=(なし)
            int src = (srcIdx >= 0 && srcIdx < _morphSourceMeshIds.Count - 1) ? _morphSourceMeshIds[srcIdx + 1] : -1;
            int parIdx = (_morphParentDropdown?.index ?? 0) - 1;
            int par = (parIdx >= 0 && parIdx < _morphParentIds.Count - 1) ? _morphParentIds[parIdx + 1] : -1;
            int pan = _morphPanelDropdown?.index ?? 3;
            string nm = _morphNameField?.value?.Trim() ?? "";
            if (src < 0) { ML("対象メッシュを選択してください"); return; }
            SendEndMorphPreview();
            SendCmd(new ConvertMeshToMorphCommand(ModelIndex, src, par, nm, pan));
        }

        private void OnMorphToMesh()
        {
            if (CurrentModel == null) return;
            var ids = CurrentModel.SelectedMorphIndices;
            if (ids == null || ids.Length == 0) { ML("モーフが選択されていません"); return; }
            SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f);
            SendCmd(new ConvertMorphToMeshCommand(ModelIndex, ids));
        }

        private void OnCreateMorphSet()
        {
            if (CurrentModel == null) return;
            string nm = _morphSetNameField?.value?.Trim() ?? "";
            int ty = (_morphSetTypeDropdown?.index == 1) ? 3 : 1;
            var mi = CurrentModel.SelectedMorphIndices;
            if (mi == null || mi.Length == 0) { ML("モーフが選択されていません"); return; }
            SendCmd(new CreateMorphSetCommand(ModelIndex, nm, ty, mi));
        }

        private void OnMorphWeight(ChangeEvent<float> e)
        {
            if (_isReceiving || _ctx == null || CurrentModel == null) return;
            var mi = CurrentModel.SelectedMorphIndices;
            if (mi == null || mi.Length == 0) return;
            if (!_isMorphPreviewStarted) { SendCmd(new StartMorphPreviewCommand(ModelIndex, mi)); _isMorphPreviewStarted = true; }
            SendCmd(new ApplyMorphPreviewCommand(ModelIndex, e.newValue));
        }

        private void OnMorphTestReset() { SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f); }

        private void OnMorphSelAll(bool sel)
        {
            if (CurrentModel == null) return;
            SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f);
            if (sel) SendCmd(new SelectAllMorphsCommand(ModelIndex, _morphFilteredData.Select(s => s.MasterIndex).ToArray()));
            else     SendCmd(new DeselectAllMorphsCommand(ModelIndex));
        }

        private void SendEndMorphPreview()
        {
            if (_ctx != null && _isMorphPreviewStarted) SendCmd(new EndMorphPreviewCommand(ModelIndex));
            _isMorphPreviewStarted = false;
        }

        // ================================================================
        // BoneTransform（エディタ版と同一）
        // ================================================================

        private void BindTransformUI(VisualElement root)
        {
            _transformFoldout = new Foldout { text = "トランスフォーム", value = false, name = "transform-foldout" };
            _transformFoldout.style.marginTop  = 4;
            _transformFoldout.style.display    = DisplayStyle.None;

            _transformFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_transformFoldout, out _localPosX, out _localPosY, out _localPosZ, "local-pos");
            RegTF(_localPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_localPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_localPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _transformFoldout.Add(SectionHeader("回転"));
            AddRotFields(_transformFoldout,
                out _localRotX, out _localRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _localRotY, out _localRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _localRotZ, out _localRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: false);

            _transformFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_transformFoldout, out _localSclX, out _localSclY, out _localSclZ, "local-scl");
            RegTF(_localSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_localSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_localSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _mainContent?.Add(_transformFoldout);
        }

        private void RegTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegTRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingTransformUI = false; }
            });
        }

        private void RegTRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingTransformUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "トランスフォーム回転変更")));
        }

        private void UpdateTransformPanel()
        {
            if (_transformFoldout == null) return;
            if (!IsSimpleMode) { _transformFoldout.style.display = DisplayStyle.None; return; }
            bool show = _selectedAdapters.Any(a => !a.MeshView.BonePose.HasPose);
            _transformFoldout.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _isSyncingTransformUI = true;
            try
            {
                var views = _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MeshView).ToList();
                MixFT(_localPosX, views, v => v.LocalPosition.x); MixFT(_localPosY, views, v => v.LocalPosition.y); MixFT(_localPosZ, views, v => v.LocalPosition.z);
                MixRTF(_localRotX, _localRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_localRotY, _localRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_localRotZ, _localRotSliderZ, views, v => v.LocalRotationEuler.z);
                MixFT(_localSclX, views, v => v.LocalScale.x); MixFT(_localSclY, views, v => v.LocalScale.y); MixFT(_localSclZ, views, v => v.LocalScale.z);
            }
            finally { _isSyncingTransformUI = false; }
        }

        // ================================================================
        // ヘルパー（エディタ版と同一）
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private int[] SelIndices() => _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
        private int[] SelTransformIndices() => _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

        private void RebuildSelectedAdaptersFromTreeView()
        {
            _selectedAdapters.Clear();
            if (_treeView == null) return;
            foreach (var item in _treeView.selectedItems)
                if (item is SummaryTreeAdapter a && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);
        }

        private void RebuildSelectedAdaptersFromCurrentModel()
        {
            _selectedAdapters.Clear();
            if (_treeRoot == null || CurrentModel == null) return;
            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
            };
            if (sel == null) return;
            foreach (int idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a != null && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);
            }
        }

        /// <summary>
        /// _selectedAdapters の IMeshView を CurrentModel から最新スナップショットで更新する。
        /// BonePoseData 等が後から変化した場合に HasPose 等を正しく反映するため。
        /// </summary>
        private void RefreshSelectedAdapterViews()
        {
            if (CurrentModel == null || _selectedAdapters.Count == 0) return;
            var freshList = _currentTab == TabType.Bone
                ? CurrentModel.BoneList
                : CurrentModel.DrawableList;
            if (freshList == null) return;
            var freshMap = new System.Collections.Generic.Dictionary<int, IMeshView>();
            foreach (var v in freshList) freshMap[v.MasterIndex] = v;
            foreach (var a in _selectedAdapters)
                if (freshMap.TryGetValue(a.MasterIndex, out var fresh))
                    a.UpdateView(fresh);
        }

        // ツリー内の全アダプタのビュー（名前等）を現在のモデルから最新化する。
        // 名前変更等の属性変更を即座にリストへ反映するため。
        private void RefreshAllAdapterViews()
        {
            if (CurrentModel == null || _treeRoot == null) return;
            var freshList = _currentTab == TabType.Bone
                ? CurrentModel.BoneList
                : CurrentModel.DrawableList;
            if (freshList == null) return;
            foreach (var v in freshList)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(v.MasterIndex);
                if (a != null) a.UpdateView(v);
            }
        }

        private string FindDrawableName(int mi)
        {
            if (CurrentModel?.DrawableList != null) foreach (var s in CurrentModel.DrawableList) if (s.MasterIndex == mi) return s.Name;
            if (CurrentModel?.BoneList     != null) foreach (var s in CurrentModel.BoneList)     if (s.MasterIndex == mi) return s.Name;
            return null;
        }

        private void SL(Label l, string t)    { if (l != null) l.text = t; }
        private void Log(string m)            { if (_statusLabel != null) _statusLabel.text = m; }
        private void ML(string m)             { if (_morphStatusLabel != null) _morphStatusLabel.text = m; Log(m); }

        private static float NormAngle(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }
        private static void SMV(Toggle t, bool m) { if (t != null) t.showMixedValue = m; }

        private void MixFT(FloatField f, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.0001f);
            f.SetValueWithoutNotify(same ? (float)System.Math.Round(v0, 4) : 0f);
            f.showMixedValue = !same; f.SetEnabled(true);
        }

        private void MixRTF(FloatField f, Slider s, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.01f);
            float val = same ? v0 : 0f;
            f.SetValueWithoutNotify((float)System.Math.Round(val, 4)); f.showMixedValue = !same; f.SetEnabled(true);
            if (s != null) { s.SetValueWithoutNotify(same ? NormAngle(val) : 0f); s.SetEnabled(same); }
        }

        private static void SF(FloatField f, float v, bool e) { if (f == null) return; f.SetValueWithoutNotify((float)System.Math.Round(v, 4)); f.showMixedValue = false; f.SetEnabled(e); }
        private static void SS(Slider s, float v, bool e)     { if (s != null) { s.SetValueWithoutNotify(v); s.SetEnabled(e); } }

        // ================================================================
        // UIパーツ生成ヘルパー
        // ================================================================

        private T Q<T>(string name) where T : VisualElement => _root?.Q<T>(name);

        private static Button MakeTabBtn(string label, string name)
        {
            var b = new Button { text = label, name = name };
            b.style.flexGrow = 1; b.style.height = 20; b.style.marginRight = 2; b.style.fontSize = 10;
            return b;
        }

        private static Button MakeSmallBtn(string label, string name, string tooltip = null)
        {
            var b = new Button { text = label, name = name };
            b.style.height = 18; b.style.marginRight = 2; b.style.marginBottom = 2; b.style.fontSize = 10;
            b.style.paddingLeft = 4; b.style.paddingRight = 4; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            if (!string.IsNullOrEmpty(tooltip)) b.tooltip = tooltip;
            return b;
        }

        private static Label MakeInfoLabel(string name = "")
        {
            var l = new Label { name = name };
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 1;
            return l;
        }

        private static Label SectionHeader(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 1;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        // ツリー/リストの下端ドラッグリサイズ用ハンドル。
        // PlayerPrimitiveMeshSubPanel.AddProfileResizeHandle と同方式:
        // 6px バーを PointerDown/Move/Up + CapturePointer でドラッグし、Mathf.Clamp で高さ変更。
        private static void AddListResizeHandle(
            VisualElement container, VisualElement target,
            Func<float> getHeight, Action<float> setHeight,
            float min)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginTop       = 2;
            handle.style.marginBottom    = 4;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            bool  dragging    = false;
            float startY      = 0f;
            float startHeight = 0f;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                dragging    = true;
                startY      = e.position.y;
                startHeight = getHeight();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - startY;
                float h = Mathf.Max(min, startHeight + delta);   // 上限なし
                setHeight(h);
                // TreeView は height を無視するため min/max も同値にして高さを厳密固定する。
                target.style.height    = h;
                target.style.minHeight = h;
                target.style.maxHeight = h;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                dragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }

        private static VisualElement LabeledRow(string label, VisualElement content)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2; row.style.alignItems = Align.Center;
            var lbl = new Label(label); lbl.style.width = 70; lbl.style.fontSize = 10;
            lbl.style.color = new StyleColor(Color.white);
            row.Add(lbl); content.style.flexGrow = 1; row.Add(content);
            return row;
        }

        private static StyleColor Col(float v) => new StyleColor(new Color(v, v, v));

        private static void AddXYZFields(VisualElement parent, out FloatField fx, out FloatField fy, out FloatField fz, string prefix)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            fx = new FloatField("X") { name = $"{prefix}-x" }; fx.style.flexGrow = 1;
            fy = new FloatField("Y") { name = $"{prefix}-y" }; fy.style.flexGrow = 1;
            fz = new FloatField("Z") { name = $"{prefix}-z" }; fz.style.flexGrow = 1;
            row.Add(fx); row.Add(fy); row.Add(fz); parent.Add(row);
        }

        private void AddRotFields(
            VisualElement parent,
            out FloatField fx, out Slider sx, SetBoneTransformValueCommand.Field tfx,
            out FloatField fy, out Slider sy, SetBoneTransformValueCommand.Field tfy,
            out FloatField fz, out Slider sz, SetBoneTransformValueCommand.Field tfz,
            bool isPose)
        {
            var frow = new VisualElement(); frow.style.flexDirection = FlexDirection.Row; frow.style.marginBottom = 1;
            fx = new FloatField("X"); fx.style.flexGrow = 1;
            fy = new FloatField("Y"); fy.style.flexGrow = 1;
            fz = new FloatField("Z"); fz.style.flexGrow = 1;
            frow.Add(fx); frow.Add(fy); frow.Add(fz); parent.Add(frow);

            var srow = new VisualElement(); srow.style.flexDirection = FlexDirection.Row; srow.style.marginBottom = 2;
            sx = new Slider(-180f, 180f); sx.style.flexGrow = 1;
            sx.style.color = new StyleColor(Color.white);
            sy = new Slider(-180f, 180f); sy.style.flexGrow = 1;
            sy.style.color = new StyleColor(Color.white);
            sz = new Slider(-180f, 180f); sz.style.flexGrow = 1;
            sz.style.color = new StyleColor(Color.white);
            srow.Add(sx); srow.Add(sy); srow.Add(sz); parent.Add(srow);

            if (isPose)
            {
                RegRestRotField(fx, sx, tfx); RegRestRotField(fy, sy, tfy); RegRestRotField(fz, sz, tfz);
                RegRestRotSlider(sx, fx, tfx); RegRestRotSlider(sy, fy, tfy); RegRestRotSlider(sz, fz, tfz);
            }
            else
            {
                RegTRotField(fx, sx, tfx); RegTRotField(fy, sy, tfy); RegTRotField(fz, sz, tfz);
                RegTRotSlider(sx, fx, tfx); RegTRotSlider(sy, fy, tfy); RegTRotSlider(sz, fz, tfz);
            }
        }
    }

    public class SummaryDragValidator : IDragDropValidator<SummaryTreeAdapter>
    {
        public bool CanDrag(SummaryTreeAdapter item) => true;
        public bool CanDrop(SummaryTreeAdapter dragged, SummaryTreeAdapter target, DropPosition position) => true;
    }
}
