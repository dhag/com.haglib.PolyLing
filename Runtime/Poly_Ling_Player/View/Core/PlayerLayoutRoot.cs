// PlayerLayoutRoot.cs
// UIToolkit による3ペインレイアウト構築。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerLayoutRoot
    {
        // ================================================================
        // Left ペイン公開要素
        // ================================================================

        public Label         StatusLabel        { get; private set; }
        public VisualElement LocalLoaderSection { get; private set; }
        public Button        ConnectBtn         { get; private set; }
        public Button        DisconnectBtn      { get; private set; }
        public Button        FetchBtn           { get; private set; }
        public Button        UndoBtn            { get; private set; }
        public Button        RedoBtn            { get; private set; }
        public VisualElement RemoteSection      { get; private set; }
        public Foldout       RemoteFoldout      { get; private set; }
        public VisualElement ModelListContainer  { get; private set; }
        public DropdownField ModelSelectDropdown { get; private set; }
        public Button        ModelListBtn        { get; private set; }
        public Button        MeshListBtn         { get; private set; }

        // ================================================================
        // ビューポートパネル公開
        // ================================================================

        public PlayerViewportPanel PerspectivePanel { get; private set; }
        public PlayerViewportPanel TopPanel         { get; private set; }
        public PlayerViewportPanel FrontPanel       { get; private set; }
        public PlayerViewportPanel SidePanel        { get; private set; }

        // 中央ペイン ビューポート操作UI
        public Toggle PerspOrthoToggle { get; private set; }   // Perspective をオルソ表示に切替
        public Button TopFlipBtn       { get; private set; }   // Top ↔ Bottom
        public Button FrontFlipBtn     { get; private set; }   // Front ↔ Back
        public Button SideFlipBtn      { get; private set; }   // Right ↔ Left
        public Label  TopViewLabel     { get; private set; }
        public Label  FrontViewLabel   { get; private set; }
        public Label  SideViewLabel    { get; private set; }

        // ================================================================
        // ビューポート表示フラグ（面ごと）
        // ================================================================

        /// <summary>
        /// 面ごとの表示トグル配列。[viewportSlot, itemIndex]
        ///
        /// viewportSlot: 0=Perspective、1=Top、2=Front、3=Side
        ///   （PlayerViewportManager の SlotPerspective 等と対応）
        ///
        /// itemIndex 定数は VD_* を参照。
        /// </summary>
        public Toggle[,] ViewportDisplayToggles { get; private set; }

        // itemIndex 定数
        public const int VD_CULLING    = 0;
        public const int VD_SEL_MESH   = 1;
        public const int VD_SEL_WIRE   = 2;
        public const int VD_SEL_VERT   = 3;
        public const int VD_SEL_BONE   = 4;
        public const int VD_UNSEL_MESH = 5;
        public const int VD_UNSEL_WIRE = 6;
        public const int VD_UNSEL_VERT = 7;
        public const int VD_UNSEL_BONE = 8;
        public const int VD_SEL_MIRROR   = 9;
        public const int VD_UNSEL_MIRROR = 10;
        public const int VD_COUNT      = 11;

        /// <summary>左ペイン：ラッソ選択トグル。</summary>
        public Toggle LassoToggle { get; private set; }

        // 選択モード切替（頂点/辺/面/線分・非排他）。SelectionState.Mode を設定する。
        public Toggle SelModeVertexToggle { get; private set; }
        public Toggle SelModeEdgeToggle   { get; private set; }
        public Toggle SelModeFaceToggle   { get; private set; }
        public Toggle SelModeLineToggle   { get; private set; }

        /// <summary>右ペイン内の動的コンテンツ領域（ScrollView の contentContainer）。</summary>
        public VisualElement RightPaneContent { get; private set; }

        /// <summary>右ペイン：モデルリストセクション（ModelListSubPanel を Build する対象）。</summary>
        public VisualElement ModelListSection { get; private set; }

        /// <summary>右ペイン：メッシュリストセクション（MeshListSubPanel を Build する対象）。</summary>
        public VisualElement MeshListSection { get; private set; }

        /// <summary>右ペイン：インポートセクション（PlayerImportSubPanel を Build する対象）。</summary>
        public VisualElement ImportSection { get; private set; }

        /// <summary>右ペイン：図形生成セクション（PlayerPrimitiveMeshSubPanel を Build する対象）。</summary>
        public VisualElement PrimitiveSection { get; private set; }

        /// <summary>左ペイン：図形生成ボタン（基本図形）。</summary>
        public Button PrimitiveBtn { get; private set; }

        /// <summary>左ペイン：図形生成ボタン（高度な図形）。基本図形と同じ PrimitiveSection を開く。</summary>
        public Button AdvancedPrimitiveBtn { get; private set; }

        /// <summary>左ペイン：ツール切り替えボタン群。</summary>
        public Button ToolVertexMoveBtn  { get; private set; }
        public Button ToolObjectMoveBtn  { get; private set; }
        public Button ToolPivotOffsetBtn { get; private set; }
        public Button ToolSculptBtn      { get; private set; }
        public Button ToolAdvancedSelBtn { get; private set; }
        public Button ToolSkinWeightPaintBtn { get; private set; }

        /// <summary>右ペイン：スキンウェイトペイントセクション（ScrollView内）。</summary>
        public VisualElement SkinWeightPaintSection { get; private set; }

        /// <summary>右ペイン：頂点移動サブパネルセクション（ScrollView内）。</summary>
        public VisualElement VertexMoveSection { get; private set; }

        /// <summary>右ペイン：ピボットオフセットサブパネルセクション（ScrollView内）。</summary>
        public VisualElement PivotSection { get; private set; }

        /// <summary>右ペイン：スカルプトサブパネルセクション（ScrollView内）。</summary>
        public VisualElement SculptSection { get; private set; }

        /// <summary>右ペイン：詳細選択サブパネルセクション（ScrollView内）。</summary>
        public VisualElement AdvancedSelectSection { get; private set; }

        /// <summary>左ペイン：MeshFilter→Skinnedボタン。</summary>
        public Button MeshFilterToSkinnedBtn { get; private set; }

        /// <summary>左ペイン：下絵ボタン（その他）。</summary>
        public Button UnderlayBtn { get; private set; }

        /// <summary>右ペイン：下絵設定セクション（ScrollView内）。</summary>
        public VisualElement UnderlaySection { get; private set; }

        /// <summary>右ペイン：ブレンドセクション（ScrollView内）。</summary>
        public VisualElement BlendSection { get; private set; }

        /// <summary>左ペイン：ブレンドボタン。</summary>
        public Button BlendBtn { get; private set; }

        /// <summary>右ペイン：モデルブレンドセクション（ScrollView内）。</summary>
        public VisualElement ModelBlendSection { get; private set; }

        /// <summary>左ペイン：モデルブレンドボタン。</summary>
        public Button ModelBlendBtn { get; private set; }

        public VisualElement BoneEditorSection { get; private set; }
        public Button BoneEditorBtn { get; private set; }

        public VisualElement UVEditorSection { get; private set; }
        public Button UVEditorBtn { get; private set; }

        public VisualElement UVUnwrapSection { get; private set; }
        public Button UVUnwrapBtn { get; private set; }

        // ── 追加パネル ────────────────────────────────────────────────────
        public VisualElement MaterialListSection   { get; private set; }
        public Button        MaterialListBtn       { get; private set; }
        public VisualElement UVZSection            { get; private set; }
        public Button        UVZBtn                { get; private set; }
        public VisualElement PartsSelectionSetSection { get; private set; }
        public Button        PartsSelectionSetBtn  { get; private set; }
        public VisualElement MeshSelectionSetSection  { get; private set; }
        public Button        MeshSelectionSetBtn   { get; private set; }
        public VisualElement MergeMeshesSection    { get; private set; }
        public Button        MergeMeshesBtn        { get; private set; }
        public VisualElement MorphSection          { get; private set; }
        public Button        MorphBtn              { get; private set; }
        public VisualElement MorphCreateSection    { get; private set; }
        public Button        MorphCreateBtn        { get; private set; }
        public VisualElement TPoseSection          { get; private set; }
        public Button        TPoseBtn              { get; private set; }
        public VisualElement HumanoidMappingSection { get; private set; }
        public Button        HumanoidMappingBtn    { get; private set; }
        public VisualElement MirrorSection         { get; private set; }
        public Button        MirrorBtn             { get; private set; }
        // ── 追加パネル（最終残件） ─────────────────────────────────────────
        public VisualElement QuadDecimatorSection   { get; private set; }
        public Button        QuadDecimatorBtn       { get; private set; }

        public VisualElement AlignVerticesSection       { get; private set; }
        public Button        AlignVerticesBtn           { get; private set; }
        public VisualElement PlanarizeAlongBonesSection { get; private set; }
        public Button        PlanarizeAlongBonesBtn     { get; private set; }
        public VisualElement MergeVerticesSection       { get; private set; }
        public Button        MergeVerticesBtn           { get; private set; }
        public VisualElement SplitVerticesSection       { get; private set; }
        public Button        SplitVerticesBtn           { get; private set; }
        public VisualElement AddFaceSection             { get; private set; }
        public Button        AddFaceBtn                 { get; private set; }
        public VisualElement FlipFaceSection            { get; private set; }
        public Button        FlipFaceBtn                { get; private set; }
        public VisualElement RotateSection              { get; private set; }
        public Button        RotateBtn                  { get; private set; }
        public VisualElement ScaleSection               { get; private set; }
        public Button        ScaleBtn                   { get; private set; }
        public VisualElement EdgeBevelSection           { get; private set; }
        public Button        EdgeBevelBtn               { get; private set; }
        public VisualElement EdgeExtrudeSection         { get; private set; }
        public Button        EdgeExtrudeBtn             { get; private set; }
        public VisualElement FaceExtrudeSection         { get; private set; }
        public Button        FaceExtrudeBtn             { get; private set; }
        public VisualElement EdgeTopologySection        { get; private set; }
        public Button        EdgeTopologyBtn            { get; private set; }
        public VisualElement KnifeSection               { get; private set; }
        public Button        KnifeBtn                   { get; private set; }
        public VisualElement LineExtrudeSection         { get; private set; }
        public Button        LineExtrudeBtn             { get; private set; }
        public VisualElement MediaPipeSection       { get; private set; }
        public Button        MediaPipeBtn           { get; private set; }
        public VisualElement VMDTestSection         { get; private set; }
        public Button        VMDTestBtn             { get; private set; }
        public VisualElement UnityClipTestSection    { get; private set; }
        public Button        UnityClipTestBtn        { get; private set; }
        public VisualElement MotionClipTestSection   { get; private set; }
        public Button        MotionClipTestBtn        { get; private set; }
        public VisualElement RemoteServerSection    { get; private set; }
        public Button        RemoteServerBtn        { get; private set; }

        /// <summary>右ペイン：エクスポートセクション（ScrollView内）。</summary>
        public VisualElement ExportSection { get; private set; }

        /// <summary>左ペイン：PMXフルエクスポートボタン。</summary>
        public Button FullExportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQOフルエクスポートボタン。</summary>
        public Button FullExportMqoBtn { get; private set; }

        /// <summary>右ペイン：プロジェクトファイルセクション（ScrollView内）。</summary>
        public VisualElement ProjectFileSection { get; private set; }

        /// <summary>左ペイン：プロジェクトファイルボタン。</summary>
        public Button ProjectFileBtn { get; private set; }

        /// <summary>右ペイン：部分インポートセクション（ScrollView内）。</summary>
        public VisualElement PartialImportSection { get; private set; }

        /// <summary>左ペイン：PMX部分インポートボタン。</summary>
        public Button PartialImportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQO部分インポートボタン。</summary>
        public Button PartialImportMqoBtn { get; private set; }

        /// <summary>右ペイン：部分エクスポートセクション（ScrollView内）。</summary>
        public VisualElement PartialExportSection { get; private set; }

        /// <summary>左ペイン：PMX部分エクスポートボタン。</summary>
        public Button PartialExportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQO部分エクスポートボタン。</summary>
        public Button PartialExportMqoBtn { get; private set; }

        /// <summary>右ペイン：MeshFilter→Skinnedセクション（ScrollView外）。</summary>
        public VisualElement MeshFilterToSkinnedSection { get; private set; }

        /// <summary>右ペイン：オブジェクト移動TRSセクション（ScrollView内、MeshListSectionの直後）。</summary>
        public VisualElement ObjectMoveTRSSection { get; private set; }

        // ================================================================
        // 上下分割スプリッター・クロスハンドル（連動用）
        // ================================================================

        private TwoPaneSplitView _splitCenter;
        private TwoPaneSplitView _splitPerspSide;
        private TwoPaneSplitView _splitTopFront;
        private TwoPaneSplitView _splitLCR;   // 左ペイン | (中央+右)
        private TwoPaneSplitView _splitCR;    // 中央 | 右ペイン
        private VisualElement    _perspPane;
        private VisualElement    _topPane;
        private VisualElement    _leftPaneEl;   // _splitLCR の左固定ペイン（幅保存用）
        private VisualElement    _rightPaneEl;  // _splitCR の右固定ペイン（幅保存用）
        private float            _lastSyncedHeight = -1f;

        // クロスドラッグ領域
        private VisualElement _crossDragRegion;
        private VisualElement _centerDraglineAnchor;   // _splitCenter 専用 dragline（Build中にキャッシュ）
        private VisualElement _lcrDraglineAnchor;       // _splitLCR 専用 dragline（Build中にキャッシュ）
        private VisualElement _crDraglineAnchor;        // _splitCR 専用 dragline（Build中にキャッシュ）
        private VisualElement _rootRef;
        private float         _dragStartVH;
        private float         _dragStartHW;
        private float         _currentRightW;
        private Vector2       _dragStartPanelPos;
        private bool          _crossDragging;

        // ── レイアウト永続化（端末ローカル: PlayerPrefs）─────────────────
        private const string PrefLeftW       = "PolyLing.Player.Layout.LeftW";
        private const string PrefRightW      = "PolyLing.Player.Layout.RightW";
        private const string PrefCenterRight = "PolyLing.Player.Layout.CenterRightW";
        private const string PrefCenterH     = "PolyLing.Player.Layout.CenterH";
        private const float  DefLeftW   = 200f;
        private const float  DefRightW  = 220f;
        private const float  DefCenterW = 240f;
        private bool         _layoutRestored;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.width         = new StyleLength(new Length(100, LengthUnit.Percent));
            root.style.height        = new StyleLength(new Length(100, LengthUnit.Percent));

            // 保存済みレイアウト（端末ローカル）を読み込む。未保存時は既定値。
            float savedLeftW   = LoadPref(PrefLeftW,       DefLeftW);
            float savedRightW  = LoadPref(PrefRightW,      DefRightW);
            float savedCenterW = LoadPref(PrefCenterRight, DefCenterW);

            _splitLCR = new TwoPaneSplitView(0, savedLeftW, TwoPaneSplitViewOrientation.Horizontal);
            _splitLCR.style.flexGrow = 1;
            root.Add(_splitLCR);

            var leftPaneEl = BuildLeftPane();
            _leftPaneEl = leftPaneEl;
            _splitLCR.Add(leftPaneEl);
            // 子 TwoPaneSplitView を Add する前に自身の dragline-anchor をキャッシュする
            // （後から Q() すると子の anchor を誤って返すため）。
            _lcrDraglineAnchor = _splitLCR.Q(className: "unity-two-pane-split-view__dragline-anchor");

            _splitCR = new TwoPaneSplitView(1, savedRightW, TwoPaneSplitViewOrientation.Horizontal);
            _splitCR.style.flexGrow = 1;
            _splitLCR.Add(_splitCR);

            _splitCenter = new TwoPaneSplitView(1, savedCenterW, TwoPaneSplitViewOrientation.Horizontal);
            _splitCenter.style.flexGrow = 1;
            _splitCR.Add(_splitCenter);
            // _splitCR の dragline-anchor は _splitCenter を Add した後だと混同するため、
            // この時点でキャッシュする。
            _crDraglineAnchor = _splitCR.Q(className: "unity-two-pane-split-view__dragline-anchor");
            // 子 TwoPaneSplitView を追加する前にキャッシュする。
            // 後から Q() すると _splitPerspSide の dragline を誤って返す。
            _centerDraglineAnchor = _splitCenter.Q(className: "unity-two-pane-split-view__dragline-anchor");

            PlayerViewportPanel perspPanel, topPanel, frontPanel, sidePanel;

            _splitPerspSide = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            _splitPerspSide.style.flexGrow = 1;
            _splitCenter.Add(_splitPerspSide);
            PerspOrthoToggle = new Toggle("オルソ") { value = false };
            PerspOrthoToggle.style.fontSize = 10;
            PerspOrthoToggle.style.color    = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            var perspWrap = BuildViewportPane("Perspective", out perspPanel, out _, PerspOrthoToggle);
            _splitPerspSide.Add(perspWrap); PerspectivePanel = perspPanel;
            _perspPane = perspWrap;

            SideFlipBtn = MakeFlipBtn("反転");
            _splitPerspSide.Add(BuildViewportPane("Right", out sidePanel, out var sideLbl, SideFlipBtn));
            SidePanel = sidePanel; SideViewLabel = sideLbl;

            _splitTopFront = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            _splitTopFront.style.flexGrow = 1;
            _splitCenter.Add(_splitTopFront);

            TopFlipBtn = MakeFlipBtn("反転");
            var topWrap = BuildViewportPane("TOP", out topPanel, out var topLbl, TopFlipBtn);
            TopViewLabel = topLbl;
            _splitTopFront.Add(topWrap); TopPanel = topPanel;
            _topPane = topWrap;

            FrontFlipBtn = MakeFlipBtn("反転");
            _splitTopFront.Add(BuildViewportPane("Front", out frontPanel, out var frontLbl, FrontFlipBtn));
            FrontPanel = frontPanel; FrontViewLabel = frontLbl;

            var rightPaneEl = BuildRightPane();
            _rightPaneEl = rightPaneEl;
            _splitCR.Add(rightPaneEl);

            SetupVerticalSplitSync();

            _rootRef = root;
            SetupCrossDragRegion(root);
            SetupLayoutPersistence(root);
        }

        // ================================================================
        // レイアウト永続化（端末ローカル: PlayerPrefs）
        // ================================================================

        private void SetupLayoutPersistence(VisualElement root)
        {
            // 外側スプリッター（左幅・右幅）のドラッグ確定で保存。
            if (_lcrDraglineAnchor != null)
                _lcrDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());
            if (_crDraglineAnchor != null)
                _crDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // 中央の左右区切り（標準ドラッグ）の確定で保存。
            if (_centerDraglineAnchor != null)
                _centerDraglineAnchor.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // 中央の上下区切り（persp/top の縦スプリッター標準ドラッグ）の確定で保存。
            // 各 split は最下層で子に split を持たないため、自身の anchor が取れる。
            var dlPersp = _splitPerspSide?.Q(className: "unity-two-pane-split-view__dragline-anchor");
            if (dlPersp != null) dlPersp.RegisterCallback<PointerUpEvent>(_ => SaveLayout());
            var dlTop = _splitTopFront?.Q(className: "unity-two-pane-split-view__dragline-anchor");
            if (dlTop != null) dlTop.RegisterCallback<PointerUpEvent>(_ => SaveLayout());

            // レイアウト確定後に中央の左右・上下区切りを復元する（初回のみ）。
            // 中央の左右区切りはカスタムドラッグ機構（_currentRightW + dragline 再配置）、
            // 上下区切りは persp/top の height 同期のため、コンストラクタ初期値だけでは
            // 内部状態が揃わない。resolvedStyle が確定する初回 GeometryChanged で適用する。
            root.RegisterCallback<GeometryChangedEvent>(OnRootFirstGeometry);

            // ウィンドウ破棄時に最終保存。
            root.RegisterCallback<DetachFromPanelEvent>(_ => SaveLayout());
        }

        private void OnRootFirstGeometry(GeometryChangedEvent evt)
        {
            if (_layoutRestored) return;
            float w = _rootRef != null ? _rootRef.resolvedStyle.width : 0f;
            if (float.IsNaN(w) || w <= 0f) return;   // レイアウト未確定
            _layoutRestored = true;
            _rootRef.UnregisterCallback<GeometryChangedEvent>(OnRootFirstGeometry);

            float savedCenterW = LoadPref(PrefCenterRight, DefCenterW);
            ApplyHorizontalSplitWidth(Mathf.Max(50f, savedCenterW));

            float savedCenterH = LoadPref(PrefCenterH, -1f);
            if (savedCenterH > 0f)
                ApplyVerticalSplitHeight(Mathf.Max(50f, savedCenterH));
        }

        private void SaveLayout()
        {
            // resolvedStyle から実寸を取得し、異常値（NaN/0以下）は保存しない。
            if (_leftPaneEl != null)
            {
                float v = _leftPaneEl.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefLeftW, v);
            }
            if (_rightPaneEl != null)
            {
                float v = _rightPaneEl.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefRightW, v);
            }
            if (_splitTopFront != null)
            {
                float v = _splitTopFront.resolvedStyle.width;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefCenterRight, v);
            }
            if (_perspPane != null)
            {
                float v = _perspPane.resolvedStyle.height;
                if (!float.IsNaN(v) && v > 0f) PlayerPrefs.SetFloat(PrefCenterH, v);
            }
            PlayerPrefs.Save();
        }

        private static float LoadPref(string key, float def)
        {
            float v = PlayerPrefs.GetFloat(key, def);
            return (float.IsNaN(v) || v <= 0f) ? def : v;
        }

        // ================================================================
        // クロスドラッグ領域（4分割交差点の同時ドラッグ）
        // ================================================================

        private void SetupCrossDragRegion(VisualElement root)
        {
            _crossDragRegion = new VisualElement();
            _crossDragRegion.style.position        = Position.Absolute;
            _crossDragRegion.style.width           = 16f;
            _crossDragRegion.style.height          = 16f;
            _crossDragRegion.style.backgroundColor = new StyleColor(Color.clear);
            _crossDragRegion.pickingMode           = PickingMode.Position;
            root.Add(_crossDragRegion);

            // _perspPane の右下が交差点座標。両分割の GeometryChanged で追従する。
            _perspPane.RegisterCallback<GeometryChangedEvent>(_ => UpdateCrossRegionPosition());
            _splitCenter.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateCrossRegionPosition();
                if (_crossDragging) ReapplyHorizontalDragline();
            });

            _crossDragRegion.RegisterCallback<PointerDownEvent>(OnCrossPointerDown);
            _crossDragRegion.RegisterCallback<PointerMoveEvent>(OnCrossPointerMove);
            _crossDragRegion.RegisterCallback<PointerUpEvent>(OnCrossPointerUp);
            _crossDragRegion.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                _crossDragging = false;
            });

            // クロスドラッグ中に TwoPaneSplitView が内部で _topPane/_perspPane の
            // style.height を初期値にリセットするのを上書きする。
            // GeometryChangedEvent は TwoPaneSplitView の内部コールバック後に発火するため、
            // ここで _lastSyncedHeight を再適用することで正しい位置に戻る。
            _splitPerspSide.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_crossDragging || _lastSyncedHeight <= 0f) return;
                _perspPane.style.height = _lastSyncedHeight;
                var dl = _splitPerspSide.Q(className: "unity-two-pane-split-view__dragline-anchor");
                if (dl != null) dl.style.top = _lastSyncedHeight;
            });
            _splitTopFront.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!_crossDragging || _lastSyncedHeight <= 0f) return;
                _topPane.style.height = _lastSyncedHeight;
                var dl = _splitTopFront.Q(className: "unity-two-pane-split-view__dragline-anchor");
                if (dl != null) dl.style.top = _lastSyncedHeight;
            });
        }

        private void UpdateCrossRegionPosition()
        {
            if (_rootRef == null || _crossDragRegion == null) return;
            var wb = _perspPane.worldBound;
            if (float.IsNaN(wb.xMax) || float.IsNaN(wb.yMax) || wb.xMax <= 0f) return;
            // worldBound（パネル座標）→ root ローカル座標
            var localPos = _rootRef.WorldToLocal(new Vector2(wb.xMax, wb.yMax));
            const float half = 8f;
            _crossDragRegion.style.left = localPos.x - half;
            _crossDragRegion.style.top  = localPos.y - half;
        }

        /// <summary>
        /// 横分割（_splitCenter）の左右列幅を直接設定する。
        /// _splitCenter は fixedPaneIndex=1（右列固定）Horizontal。
        /// 右列（_splitTopFront）と左列（_splitPerspSide）の両方を同フレームで設定することで
        /// 左横線の右端ズレを防ぐ。
        /// </summary>
        private void ApplyHorizontalSplitWidth(float rightW)
        {
            rightW = Mathf.Max(50f, rightW);
            _currentRightW = rightW;
            _splitTopFront.style.width = rightW;
            ReapplyHorizontalDragline();
        }

        private void ReapplyHorizontalDragline()
        {
            if (_currentRightW <= 0f || _centerDraglineAnchor == null) return;
            float containerW = _splitCenter.resolvedStyle.width;
            if (float.IsNaN(containerW) || containerW <= 0f) return;
            _centerDraglineAnchor.style.left = containerW - _currentRightW;
        }

        private void OnCrossPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _crossDragging     = true;
            _dragStartPanelPos = evt.position;
            _dragStartVH       = _perspPane.resolvedStyle.height;
            _dragStartHW       = _splitTopFront.resolvedStyle.width;
            _crossDragRegion.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnCrossPointerMove(PointerMoveEvent evt)
        {
            if (!_crossDragging) return;
            Vector2 delta = (Vector2)evt.position - _dragStartPanelPos;
            // 横を先に適用し、縦を後から上書きする。
            // TwoPaneSplitView は横幅変更時に縦の固定ペイン高を内部リセットするため、
            // 縦を後に適用することで上書きが有効になる。
            ApplyHorizontalSplitWidth(Mathf.Max(50f, _dragStartHW - delta.x));
            ApplyVerticalSplitHeight(Mathf.Max(30f, _dragStartVH + delta.y));
            evt.StopPropagation();
        }

        private void OnCrossPointerUp(PointerUpEvent evt)
        {
            if (!_crossDragging) return;
            _crossDragging = false;
            if (_crossDragRegion.HasPointerCapture(evt.pointerId))
                _crossDragRegion.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
            SaveLayout();   // 交差ドラッグ確定（中央の左右＋上下）を保存
        }

        // ================================================================
        // 上下連動
        // ================================================================

        private void ApplyVerticalSplitHeight(float h)
        {
            _lastSyncedHeight = h;
            _perspPane.style.height = h;
            _topPane.style.height   = h;
            var dlL = _splitPerspSide.Q(className: "unity-two-pane-split-view__dragline-anchor");
            var dlR = _splitTopFront.Q(className:  "unity-two-pane-split-view__dragline-anchor");
            if (dlL != null) dlL.style.top = h;
            if (dlR != null) dlR.style.top = h;
        }

        private void SetupVerticalSplitSync()
        {
            _perspPane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_crossDragging) return;
                float h = _perspPane.resolvedStyle.height;
                if (float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Approximately(h, _lastSyncedHeight)) return;
                ApplyVerticalSplitHeight(h);
            });

            _topPane.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_crossDragging) return;
                float h = _topPane.resolvedStyle.height;
                if (float.IsNaN(h) || h <= 0f) return;
                if (Mathf.Approximately(h, _lastSyncedHeight)) return;
                ApplyVerticalSplitHeight(h);
            });
        }

        // ================================================================
        // Left ペイン
        // ================================================================

        private VisualElement BuildLeftPane()
        {
            var pane = MakePane(200f);
            pane.style.backgroundColor = PaneBg(0.15f);
            pane.style.color           = Col(1f);
            pane.style.flexDirection   = FlexDirection.Column;
            pane.style.overflow        = Overflow.Hidden;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow     = 1;
            scroll.style.paddingTop   = 6;
            scroll.style.paddingLeft  = 6;
            scroll.style.paddingRight = 6;
            pane.Add(scroll);

            StatusLabel = new Label("Status: -");
            StatusLabel.style.marginBottom = 6;
            StatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            scroll.Add(StatusLabel);

            var undoRow = new VisualElement();
            undoRow.style.flexDirection = FlexDirection.Row;
            undoRow.style.marginBottom  = 6;
            UndoBtn = MakeBtn("Undo"); UndoBtn.style.flexGrow = 1; UndoBtn.style.marginRight = 2;
            RedoBtn = MakeBtn("Redo"); RedoBtn.style.flexGrow = 1; RedoBtn.style.marginLeft  = 2;
            undoRow.Add(UndoBtn); undoRow.Add(RedoBtn);
            scroll.Add(undoRow);

            scroll.Add(Separator());
            scroll.Add(Header("Models"));

            ModelSelectDropdown = new DropdownField();
            ModelSelectDropdown.style.marginBottom = 4;
            scroll.Add(ModelSelectDropdown);

            ModelListContainer = new VisualElement();
            scroll.Add(ModelListContainer);

            var listBtnRow = new VisualElement();
            listBtnRow.style.flexDirection = FlexDirection.Row;
            listBtnRow.style.marginTop     = 4;
            ModelListBtn = MakeBtn("モデルリスト"); ModelListBtn.style.flexGrow = 1; ModelListBtn.style.marginRight = 2;
            MeshListBtn  = MakeBtn("オブジェクトリスト"); MeshListBtn.style.flexGrow = 1;
            listBtnRow.Add(ModelListBtn);
            listBtnRow.Add(MeshListBtn);
            scroll.Add(listBtnRow);

            scroll.Add(Separator());

            // 選択モード（頂点/辺/面/線分・非排他）— Lasso Select の上に配置。
            scroll.Add(Header("選択モード"));
            var selModeRow = new VisualElement();
            selModeRow.style.flexDirection = FlexDirection.Row;
            selModeRow.style.flexWrap      = Wrap.Wrap;   // 収まらない場合は折り返して見切れを防ぐ
            selModeRow.style.marginBottom  = 4;
            SelModeVertexToggle = new Toggle("頂点") { value = true };
            SelModeEdgeToggle   = new Toggle("辺")   { value = false };
            SelModeFaceToggle   = new Toggle("面")   { value = false };
            SelModeLineToggle   = new Toggle("線分") { value = false };
            foreach (var t in new[] { SelModeVertexToggle, SelModeEdgeToggle, SelModeFaceToggle, SelModeLineToggle })
            {
                t.style.color      = new StyleColor(Color.white);
                t.style.flexGrow   = 0;
                t.style.flexShrink = 0;
                t.style.marginRight = 12;
                // 既定の広い label min-width を解除し、ラベルとチェックの間隔を詰める
                // （これが無いとラベルとチェックが大きく離れ、右が見切れる）。
                if (t.labelElement != null)
                {
                    t.labelElement.style.minWidth    = 0;
                    t.labelElement.style.flexGrow    = 0;
                    t.labelElement.style.marginRight = 3;
                }
                selModeRow.Add(t);
            }
            scroll.Add(selModeRow);

            LassoToggle = new Toggle("Lasso Select") { value = false };
            LassoToggle.style.marginBottom = 4;
            scroll.Add(LassoToggle);

            scroll.Add(Separator());

            LocalLoaderSection = new VisualElement();
            LocalLoaderSection.style.marginBottom = 6;
            // ※ LocalLoaderSection（Load PMX / Load MQO）は「ファイル」foldout の先頭へ移動する（下記）。

            // ================================================================
            // ここから下はカテゴリ別 Foldout（既定折りたたみ）にまとめる。
            // ボタンのインスタンス・代入先プロパティは一切変更せず、
            // 所属コンテナのみ Foldout に変更する（core 側の参照は不変）。
            // ================================================================

            // ── ファイル ───────────────────────────────────────────────
            var foFile = MakeFoldout("ファイル", "File");

            ProjectFileBtn = MakeBtn("プロジェクト保存/読込");
            foFile.Add(ProjectFileBtn);

            // Load PMX / Load MQO（旧: foldout の外・上）を「ファイル」の先頭に配置する。
            foFile.Add(LocalLoaderSection);

            var pImportRow = new VisualElement();
            pImportRow.style.flexDirection = FlexDirection.Row;
            pImportRow.style.marginBottom  = 2;
            PartialImportPmxBtn = MakeBtn("PMX部分Import"); PartialImportPmxBtn.style.flexGrow = 1; PartialImportPmxBtn.style.marginRight = 2;
            PartialImportMqoBtn = MakeBtn("MQO部分Import"); PartialImportMqoBtn.style.flexGrow = 1;
            pImportRow.Add(PartialImportPmxBtn); pImportRow.Add(PartialImportMqoBtn);
            foFile.Add(pImportRow);

            var pExportRow = new VisualElement();
            pExportRow.style.flexDirection = FlexDirection.Row;
            pExportRow.style.marginBottom  = 2;
            PartialExportPmxBtn = MakeBtn("PMX部分Export"); PartialExportPmxBtn.style.flexGrow = 1; PartialExportPmxBtn.style.marginRight = 2;
            PartialExportMqoBtn = MakeBtn("MQO部分Export"); PartialExportMqoBtn.style.flexGrow = 1;
            pExportRow.Add(PartialExportPmxBtn); pExportRow.Add(PartialExportMqoBtn);
            foFile.Add(pExportRow);

            var fullExportRow = new VisualElement();
            fullExportRow.style.flexDirection = FlexDirection.Row;
            fullExportRow.style.marginBottom  = 2;
            FullExportPmxBtn = MakeBtn("PMXフルExport"); FullExportPmxBtn.style.flexGrow = 1; FullExportPmxBtn.style.marginRight = 2;
            FullExportMqoBtn = MakeBtn("MQOフルExport"); FullExportMqoBtn.style.flexGrow = 1;
            fullExportRow.Add(FullExportPmxBtn); fullExportRow.Add(FullExportMqoBtn);
            foFile.Add(fullExportRow);


            // ── 図形生成 ───────────────────────────────────────────────
            var foPrimitive = MakeFoldout("図形生成", "Primitive");

            PrimitiveBtn = MakeBtn("基本図形");
            foPrimitive.Add(PrimitiveBtn);

            AdvancedPrimitiveBtn = MakeBtn("高度な図形");
            foPrimitive.Add(AdvancedPrimitiveBtn);

            // ── 選択・移動 ─────────────────────────────────────────────
            var foSelectMove = MakeFoldout("選択・移動/回転/拡大縮小", "SelectMove");

            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;
            toolRow.style.marginBottom  = 2;
            ToolVertexMoveBtn  = MakeBtn("頂点移動");     ToolVertexMoveBtn.style.flexGrow  = 1; ToolVertexMoveBtn.style.marginRight  = 2;
            ToolObjectMoveBtn  = MakeBtn("オブジェクト姿勢"); ToolObjectMoveBtn.style.flexGrow  = 1;
            toolRow.Add(ToolVertexMoveBtn); toolRow.Add(ToolObjectMoveBtn);
            foSelectMove.Add(toolRow);

            var toolRow2 = new VisualElement();
            toolRow2.style.flexDirection = FlexDirection.Row;
            toolRow2.style.marginBottom  = 2;
            ToolPivotOffsetBtn = MakeBtn("ピボット");    ToolPivotOffsetBtn.style.flexGrow = 1; ToolPivotOffsetBtn.style.marginRight = 2;
            ToolSculptBtn      = MakeBtn("スカルプト");  ToolSculptBtn.style.flexGrow      = 1; ToolSculptBtn.style.marginRight      = 2;
            ToolAdvancedSelBtn = MakeBtn("詳細選択");    ToolAdvancedSelBtn.style.flexGrow = 1;
            toolRow2.Add(ToolPivotOffsetBtn); toolRow2.Add(ToolSculptBtn); toolRow2.Add(ToolAdvancedSelBtn);
            foSelectMove.Add(toolRow2);

            var rowRotScale = new VisualElement(); rowRotScale.style.flexDirection = FlexDirection.Row; rowRotScale.style.marginBottom = 2;
            RotateBtn = MakeBtn("回転");     RotateBtn.style.flexGrow = 1; RotateBtn.style.marginRight = 2;
            ScaleBtn  = MakeBtn("スケール"); ScaleBtn.style.flexGrow  = 1;
            rowRotScale.Add(RotateBtn); rowRotScale.Add(ScaleBtn); foSelectMove.Add(rowRotScale);

            var rowSelSet = new VisualElement(); rowSelSet.style.flexDirection = FlexDirection.Row; rowSelSet.style.marginBottom = 2;
            PartsSelectionSetBtn = MakeBtn("パーツ選択辞書"); PartsSelectionSetBtn.style.flexGrow = 1; PartsSelectionSetBtn.style.marginRight = 2;
            MeshSelectionSetBtn  = MakeBtn("メッシュ選択辞書"); MeshSelectionSetBtn.style.flexGrow  = 1;
            rowSelSet.Add(PartsSelectionSetBtn); rowSelSet.Add(MeshSelectionSetBtn); foSelectMove.Add(rowSelSet);

            // ── トポロジー編集 ─────────────────────────────────────────
            var foTopology = MakeFoldout("トポロジー編集", "Topology");

            AddFaceBtn = MakeBtn("面追加"); foTopology.Add(AddFaceBtn);

            var rowFlipBevel = new VisualElement(); rowFlipBevel.style.flexDirection = FlexDirection.Row; rowFlipBevel.style.marginBottom = 2;
            FlipFaceBtn  = MakeBtn("面反転");   FlipFaceBtn.style.flexGrow  = 1; FlipFaceBtn.style.marginRight  = 2;
            EdgeBevelBtn = MakeBtn("辺ベベル"); EdgeBevelBtn.style.flexGrow = 1;
            rowFlipBevel.Add(FlipFaceBtn); rowFlipBevel.Add(EdgeBevelBtn); foTopology.Add(rowFlipBevel);

            var rowExtrude = new VisualElement(); rowExtrude.style.flexDirection = FlexDirection.Row; rowExtrude.style.marginBottom = 2;
            EdgeExtrudeBtn = MakeBtn("辺押し出し"); EdgeExtrudeBtn.style.flexGrow = 1; EdgeExtrudeBtn.style.marginRight = 2;
            FaceExtrudeBtn = MakeBtn("面押し出し"); FaceExtrudeBtn.style.flexGrow = 1; FaceExtrudeBtn.style.marginRight = 2;
            LineExtrudeBtn = MakeBtn("プロファイル立体化"); LineExtrudeBtn.style.flexGrow = 1;
            rowExtrude.Add(EdgeExtrudeBtn); rowExtrude.Add(FaceExtrudeBtn); rowExtrude.Add(LineExtrudeBtn); foTopology.Add(rowExtrude);

            var rowEdgeKnife = new VisualElement(); rowEdgeKnife.style.flexDirection = FlexDirection.Row; rowEdgeKnife.style.marginBottom = 2;
            EdgeTopologyBtn = MakeBtn("辺トポロジー"); EdgeTopologyBtn.style.flexGrow = 1; EdgeTopologyBtn.style.marginRight = 2;
            KnifeBtn        = MakeBtn("ナイフ");       KnifeBtn.style.flexGrow        = 1;
            rowEdgeKnife.Add(EdgeTopologyBtn); rowEdgeKnife.Add(KnifeBtn); foTopology.Add(rowEdgeKnife);

            // ── 選択頂点位置 ───────────────────────────────────────────
            var foVertexPos = MakeFoldout("選択頂点位置", "VertexPos");

            var rowAlignPlanarize = new VisualElement(); rowAlignPlanarize.style.flexDirection = FlexDirection.Row; rowAlignPlanarize.style.marginBottom = 2;
            AlignVerticesBtn       = MakeBtn("頂点整列");   AlignVerticesBtn.style.flexGrow       = 1; AlignVerticesBtn.style.marginRight       = 2;
            PlanarizeAlongBonesBtn = MakeBtn("ボーン間平面化"); PlanarizeAlongBonesBtn.style.flexGrow = 1;
            rowAlignPlanarize.Add(AlignVerticesBtn); rowAlignPlanarize.Add(PlanarizeAlongBonesBtn); foVertexPos.Add(rowAlignPlanarize);

            // ── 選択頂点トポロジー ─────────────────────────────────────
            var foVertexTopo = MakeFoldout("選択頂点トポロジー", "VertexTopo");

            var rowMergeSplit = new VisualElement(); rowMergeSplit.style.flexDirection = FlexDirection.Row; rowMergeSplit.style.marginBottom = 2;
            MergeVerticesBtn = MakeBtn("頂点マージ");  MergeVerticesBtn.style.flexGrow = 1; MergeVerticesBtn.style.marginRight = 2;
            SplitVerticesBtn = MakeBtn("頂点分割");    SplitVerticesBtn.style.flexGrow = 1;
            rowMergeSplit.Add(MergeVerticesBtn); rowMergeSplit.Add(SplitVerticesBtn); foVertexTopo.Add(rowMergeSplit);

            var rowQuad = new VisualElement(); rowQuad.style.flexDirection = FlexDirection.Row; rowQuad.style.marginBottom = 2;
            QuadDecimatorBtn = MakeBtn("Quad減面"); QuadDecimatorBtn.style.flexGrow = 1;
            rowQuad.Add(QuadDecimatorBtn); foVertexTopo.Add(rowQuad);

            // ── ボーン・モーフ ─────────────────────────────────────────
            var foBoneMorph = MakeFoldout("ボーン・モーフ", "BoneMorph");

            MeshFilterToSkinnedBtn = MakeBtn("メッシュからボーンとスキンの生成");
            foBoneMorph.Add(MeshFilterToSkinnedBtn);

            BoneEditorBtn = MakeBtn("ボーンエディタ");
            foBoneMorph.Add(BoneEditorBtn);

            var rowTPoseHuman = new VisualElement(); rowTPoseHuman.style.flexDirection = FlexDirection.Row; rowTPoseHuman.style.marginBottom = 2;
            HumanoidMappingBtn = MakeBtn("ヒューマノイド"); HumanoidMappingBtn.style.flexGrow = 1; HumanoidMappingBtn.style.marginRight = 2;
            TPoseBtn          = MakeBtn("Tポーズ変換");   TPoseBtn.style.flexGrow          = 1;
            rowTPoseHuman.Add(HumanoidMappingBtn); rowTPoseHuman.Add(TPoseBtn); foBoneMorph.Add(rowTPoseHuman);

            MirrorBtn = MakeBtn("ミラー編集"); foBoneMorph.Add(MirrorBtn);

            var rowBlend = new VisualElement(); rowBlend.style.flexDirection = FlexDirection.Row; rowBlend.style.marginBottom = 2;
            BlendBtn      = MakeBtn("メッシュブレンド"); BlendBtn.style.flexGrow      = 1; BlendBtn.style.marginRight      = 2;
            ModelBlendBtn = MakeBtn("モデルブレンド");   ModelBlendBtn.style.flexGrow = 1;
            rowBlend.Add(BlendBtn); rowBlend.Add(ModelBlendBtn); foBoneMorph.Add(rowBlend);

            MorphBtn       = MakeBtn("モーフエクスプレッション編集"); foBoneMorph.Add(MorphBtn);
            MorphCreateBtn = MakeBtn("差分からモーフ生成");         foBoneMorph.Add(MorphCreateBtn);

            ToolSkinWeightPaintBtn = MakeBtn("スキンWペイント");
            foBoneMorph.Add(ToolSkinWeightPaintBtn);

            // ── UV・マテリアル ─────────────────────────────────────────
            var foUvMat = MakeFoldout("UV・マテリアル", "UvMat");

            var rowUv = new VisualElement(); rowUv.style.flexDirection = FlexDirection.Row; rowUv.style.marginBottom = 2;
            UVEditorBtn = MakeBtn("UVエディタ"); UVEditorBtn.style.flexGrow = 1; UVEditorBtn.style.marginRight = 2;
            UVUnwrapBtn = MakeBtn("UV展開");     UVUnwrapBtn.style.flexGrow = 1; UVUnwrapBtn.style.marginRight = 2;
            UVZBtn      = MakeBtn("UVZ");        UVZBtn.style.flexGrow      = 1;
            rowUv.Add(UVEditorBtn); rowUv.Add(UVUnwrapBtn); rowUv.Add(UVZBtn); foUvMat.Add(rowUv);

            MaterialListBtn = MakeBtn("マテリアル（質感・色）"); foUvMat.Add(MaterialListBtn);
            MergeMeshesBtn  = MakeBtn("メッシュマージ");   foUvMat.Add(MergeMeshesBtn);

            // ── サーバと連携 ───────────────────────────────────────────
            // クライアントモードでのサーバとのやり取り。
            // RemoteSection の表示制御・ボタン配線は core が担う（プロパティ名・
            // インスタンスは不変）。Foldout はコンテナのみを提供する。
            var foRemote = MakeFoldout("サーバと連携", "Remote");
            RemoteFoldout = foRemote;

            RemoteSection = new VisualElement();
            RemoteSection.style.marginBottom = 4;
            ConnectBtn    = MakeBtn("Connect");
            DisconnectBtn = MakeBtn("Disconnect");
            FetchBtn      = MakeBtn("プロジェクト取得");
            RemoteSection.Add(ConnectBtn);
            RemoteSection.Add(DisconnectBtn);
            RemoteSection.Add(FetchBtn);
            foRemote.Add(RemoteSection);

            // ── その他 ─────────────────────────────────────────────────
            var foOther = MakeFoldout("その他", "Other");

            var rowMisc = new VisualElement(); rowMisc.style.flexDirection = FlexDirection.Row; rowMisc.style.marginBottom = 2;
            MediaPipeBtn    = MakeBtn("MediaPipe");   MediaPipeBtn.style.flexGrow    = 1; MediaPipeBtn.style.marginRight    = 2;
            VMDTestBtn      = MakeBtn("VMDテスト");    VMDTestBtn.style.flexGrow      = 1; VMDTestBtn.style.marginRight      = 2;
            RemoteServerBtn = MakeBtn("リモートサーバ"); RemoteServerBtn.style.flexGrow = 1;
            rowMisc.Add(MediaPipeBtn); rowMisc.Add(VMDTestBtn); rowMisc.Add(RemoteServerBtn); foOther.Add(rowMisc);

            var rowMisc2 = new VisualElement(); rowMisc2.style.flexDirection = FlexDirection.Row; rowMisc2.style.marginBottom = 2;
            UnityClipTestBtn = MakeBtn("Unityクリップ"); UnityClipTestBtn.style.flexGrow = 1; UnityClipTestBtn.style.marginRight = 2;
            MotionClipTestBtn = MakeBtn("統合モーション"); MotionClipTestBtn.style.flexGrow = 1;
            rowMisc2.Add(UnityClipTestBtn); rowMisc2.Add(MotionClipTestBtn); foOther.Add(rowMisc2);

            UnderlayBtn = MakeBtn("下絵");
            foOther.Add(UnderlayBtn);

            // ── 左ペイン カテゴリ表示順 ───────────────────────────────
            // サーバと連携（クライアントモード時のみ表示。表示制御は core）を先頭に置く。
            scroll.Add(foRemote);
            scroll.Add(foFile);
            scroll.Add(foPrimitive);
            scroll.Add(foSelectMove);
            scroll.Add(foVertexPos);
            scroll.Add(foTopology);
            scroll.Add(foVertexTopo);
            scroll.Add(foUvMat);
            scroll.Add(foBoneMorph);
            scroll.Add(foOther);

            scroll.Add(Separator());

            scroll.Add(Header("Display (P/T/F/S)"));

            // 4ビューポート × 9項目のグリッド
            // 列: P=Perspective(slot0), T=Top(slot1), F=Front(slot2), S=Side(slot3)
            // 行: カリング / 選択Mesh / 選択辺 / 選択頂点 / 選択Bone
            //      非選Mesh / 非選辺 / 非選頂点 / 非選Bone
            var vpHeaders  = new string[] { "P", "T", "F", "S" };
            var itemLabels = new string[]
            {
                "カリング", "選択Mesh", "選択辺", "選択頂点", "選択Bone",
                "非選Mesh", "非選辺",  "非選頂点", "非選Bone",
                "選択Mirror", "非選Mirror",
            };
            // ViewportDisplaySettings.Default と一致させる
            var itemDefaults = new bool[]
            {
                true,  // カリング
                true,  // 選択Mesh
                true,  // 選択辺
                true,  // 選択頂点
                true,  // 選択Bone
                true,  // 非選Mesh
                true,  // 非選辺
                true,  // 非選頂点
                false, // 非選Bone
                true,  // 選択Mirror
                true,  // 非選Mirror
            };

            // ヘッダ行
            var vpHeaderRow = new VisualElement();
            vpHeaderRow.style.flexDirection = FlexDirection.Row;
            vpHeaderRow.style.marginBottom  = 1;
            var vpHeaderSpacer = new VisualElement();
            vpHeaderSpacer.style.width = 54;
            vpHeaderRow.Add(vpHeaderSpacer);
            foreach (var h in vpHeaders)
            {
                var lbl = new Label(h);
                lbl.style.width             = 22;
                lbl.style.fontSize          = 9;
                lbl.style.unityTextAlign    = TextAnchor.MiddleCenter;
                vpHeaderRow.Add(lbl);
            }
            scroll.Add(vpHeaderRow);

            // トグル配列確保 [slot, item]
            ViewportDisplayToggles = new Toggle[4, VD_COUNT];
            for (int item = 0; item < VD_COUNT; item++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.height        = 18;
                row.style.marginBottom  = 1;

                var lbl = new Label(itemLabels[item]);
                lbl.style.width            = 54;
                lbl.style.fontSize         = 9;
                lbl.style.unityTextAlign   = TextAnchor.MiddleLeft;
                row.Add(lbl);

                for (int vp = 0; vp < 4; vp++)
                {
                    var t = new Toggle { value = itemDefaults[item] };
                    t.style.width      = 22;
                    t.style.height     = 18;
                    t.style.minWidth   = 0;
                    t.style.flexShrink = 0;
                    t.style.marginLeft = 0;
                    t.style.marginRight= 0;
                    // パネルアタッチ後に内部 Label を非表示にする
                    // （コンストラクタ直後は内部子要素が未初期化のため Q<Label>() が null を返す）
                    t.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var inner = t.Q<Label>();
                        if (inner != null)
                        {
                            inner.style.display  = DisplayStyle.None;
                            inner.style.minWidth = 0;
                            inner.style.width    = 0;
                        }
                    });
                    ViewportDisplayToggles[vp, item] = t;
                    row.Add(t);
                }
                scroll.Add(row);
            }

            return pane;
        }

        // ================================================================
        // ビューポートペイン
        // ================================================================

        private VisualElement BuildViewportPane(string label, out PlayerViewportPanel panel, out Label lbl, VisualElement headerRight = null)
        {
            var wrap = new VisualElement();
            wrap.style.flexGrow        = 1;
            wrap.style.flexDirection   = FlexDirection.Column;
            wrap.style.backgroundColor = new StyleColor(Color.white);

            lbl = new Label(label);
            lbl.style.position  = Position.Absolute;
            lbl.style.top       = 4;
            lbl.style.left      = 6;
            lbl.style.color     = new StyleColor(new Color(0.7f, 0.9f, 1f, 0.8f));
            lbl.style.fontSize  = 11;
            lbl.pickingMode     = PickingMode.Ignore;

            panel = new PlayerViewportPanel();
            wrap.Add(panel);
            wrap.Add(lbl);

            // 任意のヘッダ操作UI（オルソトグル／フリップボタン）を右上に絶対配置。
            if (headerRight != null)
            {
                headerRight.style.position = Position.Absolute;
                headerRight.style.top      = 2;
                headerRight.style.right    = 4;
                wrap.Add(headerRight);
            }
            return wrap;
        }

        /// <summary>ビューポート右上に置く小型フリップボタン。</summary>
        private static Button MakeFlipBtn(string text)
        {
            var b = new Button { text = text };
            b.style.fontSize      = 10;
            b.style.height        = 18;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.paddingLeft   = 5;
            b.style.paddingRight  = 5;
            b.style.marginTop     = 0;
            b.style.marginBottom  = 0;
            return b;
        }

        // ================================================================
        // Right ペイン
        // ================================================================

        private VisualElement BuildRightPane()
        {
            var pane = MakePane(220f);
            pane.style.backgroundColor = PaneBg(0.15f);
            pane.style.flexDirection   = FlexDirection.Column;
            pane.style.overflow        = Overflow.Hidden;

            // ── ScrollView（メッシュリスト・モデルリスト・インポート）
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow     = 1;
            scroll.style.paddingTop   = 4;
            scroll.style.paddingLeft  = 4;
            scroll.style.paddingRight = 4;
            pane.Add(scroll);

            RightPaneContent = scroll.contentContainer;
            RightPaneContent.style.color = new StyleColor(Color.white);

            // 各セクションを区切り線（上ボーダー）付きで ScrollView 内に追加する。
            // 独立 Separator 要素を廃止し、ボーダーをセクション自身に持たせることで、
            // 非表示セクションでは区切り線も一緒に消える（線分残り対策）。
            //
            // visible=true:  既定で表示（ModelList / MeshList / Import）
            // visible=false: 既定で非表示（display=None）

            // ── モデルリストセクション（先頭：区切り線なし）
            ModelListSection = AddSection(visible: true, topBorder: false);

            // ── メッシュリストセクション
            MeshListSection = AddSection(visible: true);

            // ── オブジェクト移動TRSセクション
            ObjectMoveTRSSection = AddSection(visible: false);

            // ── 頂点移動サブパネルセクション
            VertexMoveSection = AddSection(visible: false);

            // ── ピボットオフセットサブパネルセクション
            PivotSection = AddSection(visible: false);

            // ── スカルプトサブパネルセクション
            SculptSection = AddSection(visible: false);

            // ── 詳細選択サブパネルセクション
            AdvancedSelectSection = AddSection(visible: false);

            // ── スキンウェイトペイントセクション
            SkinWeightPaintSection = AddSection(visible: false);

            // ── ブレンドセクション
            BlendSection = AddSection(visible: false);

            // ── モデルブレンドセクション
            ModelBlendSection = AddSection(visible: false);

            // ── ボーンエディタセクション
            BoneEditorSection = AddSection(visible: false);

            // ── UVエディタセクション
            UVEditorSection = AddSection(visible: false);
            UVEditorSection.style.flexGrow = 1;

            // ── UV展開セクション
            UVUnwrapSection = AddSection(visible: false);

            // ── 追加パネルセクション群（デフォルト非表示）────────────────
            MaterialListSection        = AddSection(visible: false);
            UVZSection                 = AddSection(visible: false);
            PartsSelectionSetSection   = AddSection(visible: false);
            MeshSelectionSetSection    = AddSection(visible: false);
            MergeMeshesSection         = AddSection(visible: false);
            MorphSection               = AddSection(visible: false);
            MorphCreateSection         = AddSection(visible: false);
            TPoseSection               = AddSection(visible: false);
            HumanoidMappingSection     = AddSection(visible: false);
            MirrorSection              = AddSection(visible: false);
            QuadDecimatorSection       = AddSection(visible: false);
            AlignVerticesSection       = AddSection(visible: false);
            PlanarizeAlongBonesSection = AddSection(visible: false);
            MergeVerticesSection       = AddSection(visible: false);
            SplitVerticesSection       = AddSection(visible: false);
            AddFaceSection             = AddSection(visible: false);
            FlipFaceSection            = AddSection(visible: false);
            RotateSection              = AddSection(visible: false);
            ScaleSection               = AddSection(visible: false);
            EdgeBevelSection           = AddSection(visible: false);
            EdgeExtrudeSection         = AddSection(visible: false);
            FaceExtrudeSection         = AddSection(visible: false);
            EdgeTopologySection        = AddSection(visible: false);
            KnifeSection               = AddSection(visible: false);
            LineExtrudeSection         = AddSection(visible: false);
            MediaPipeSection           = AddSection(visible: false);
            VMDTestSection             = AddSection(visible: false);
            UnityClipTestSection       = AddSection(visible: false);
            MotionClipTestSection      = AddSection(visible: false);
            UnderlaySection            = AddSection(visible: false);
            RemoteServerSection        = AddSection(visible: false);

            // ── エクスポートセクション
            ExportSection = AddSection(visible: false);

            // ── プロジェクトファイルセクション
            ProjectFileSection = AddSection(visible: false);

            // ── 部分インポートセクション
            PartialImportSection = AddSection(visible: false);

            // ── 部分エクスポートセクション
            PartialExportSection = AddSection(visible: false);

            // ── インポートセクション（既定表示）
            ImportSection = AddSection(visible: true);

            // ── 図形生成セクション
            // 以前は ScrollView 外（pane 直下・flexShrink=0）に置いていたが、
            // 内容がペイン高を超えると下端が overflow:Hidden で切られ、
            // 最下部の生成ボタンが隠れていた。ScrollView 内へ移し、
            // 内容超過時はスクロールで生成ボタンへ到達できるようにする。
            // プレビュー／回転体／プロファイル2D の各キャンバスは WheelEvent を
            // StopPropagation 済みのため、親 ScrollView がホイール操作を奪うことはない。
            PrimitiveSection = AddSection(visible: false);

            // ── MeshFilter→Skinnedセクション（ScrollView内へ移動）
            MeshFilterToSkinnedSection = AddSection(visible: false);

            return pane;
        }

        /// <summary>
        /// 右ペイン ScrollView 内にセクションを追加する。
        /// 区切り線はセクション自身の上ボーダーで表現するため、
        /// 非表示時（display=None）には区切り線も一緒に消える。
        /// </summary>
        /// <param name="visible">true で既定表示、false で display=None</param>
        /// <param name="topBorder">上ボーダー（区切り線）を付けるか</param>
        private VisualElement AddSection(bool visible, bool topBorder = true)
        {
            var v = new VisualElement();
            v.style.display      = visible ? DisplayStyle.Flex : DisplayStyle.None;
            v.style.marginBottom = 4;
            if (topBorder)
            {
                v.style.borderTopWidth = 1;
                v.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
                v.style.paddingTop     = 4;
                v.style.marginTop      = 4;
            }
            RightPaneContent.Add(v);
            return v;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static VisualElement MakePane(float initialWidth)
        {
            var v = new VisualElement();
            v.style.width    = initialWidth;
            v.style.minWidth = 80f;
            return v;
        }

        /// <summary>
        /// 全Build()完了後に呼ぶ。
        /// ボタン・入力フィールドに白文字・暗背景を一括設定する。
        /// </summary>
        public void PostBuildButtonColors(UnityEngine.UIElements.VisualElement root)
        {
            ApplyDarkTheme(root);
        }

        /// <summary>
        /// VisualElement サブツリー全体にダークテーマを適用する。
        /// Build 後に動的再構築するコンテナに対しても呼び出すこと。
        /// </summary>
        public static void ApplyDarkTheme(UnityEngine.UIElements.VisualElement root)
        {
            if (root == null) return;
            var white   = new StyleColor(Color.white);
            var btnBg   = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            var fieldBg = new StyleColor(new Color(0.20f, 0.20f, 0.20f));
            var hbBg    = new StyleColor(new Color(0.18f, 0.18f, 0.22f));

            root.Query<Button>().ForEach(b =>
            {
                b.style.color = white;
                b.style.backgroundColor = btnBg;
            });

            root.Query<Label>().ForEach(l => l.style.color = white);

            root.Query<HelpBox>().ForEach(h =>
            {
                h.style.color = white;
                h.style.backgroundColor = hbBg;
            });

            root.Query<TextField>().ForEach(t =>
            {
                t.style.color = white;
                t.style.backgroundColor = fieldBg;
                var inp = t.Q(className: "unity-base-text-field__input");
                if (inp != null) { inp.style.backgroundColor = fieldBg; inp.style.color = white; }
            });
            root.Query<FloatField>().ForEach(t =>
            {
                t.style.color = white;
                t.style.backgroundColor = fieldBg;
                var inp = t.Q(className: "unity-base-text-field__input");
                if (inp != null) { inp.style.backgroundColor = fieldBg; inp.style.color = white; }
            });
            root.Query<IntegerField>().ForEach(t =>
            {
                t.style.color = white;
                t.style.backgroundColor = fieldBg;
                var inp = t.Q(className: "unity-base-text-field__input");
                if (inp != null) { inp.style.backgroundColor = fieldBg; inp.style.color = white; }
            });
            root.Query<DropdownField>().ForEach(t =>
            {
                t.style.color = white;
                t.style.backgroundColor = fieldBg;
                var inp = t.Q(className: "unity-base-popup-field__input");
                if (inp != null) { inp.style.backgroundColor = fieldBg; inp.style.color = white; }
            });

            root.Query<Toggle>().ForEach(t =>
            {
                t.style.color = white;
                var checkmark = t.Q(className: "unity-toggle__checkmark");
                if (checkmark != null) checkmark.style.backgroundColor = new StyleColor(Color.white);
            });

            root.Query<VisualElement>(className: "unity-base-slider__tracker").ForEach(e =>
                e.style.backgroundColor = fieldBg);
        }

        private static Button MakeBtn(string text)
        {
            var b = new Button { text = text };
            b.style.marginBottom  = 2;
            b.style.fontSize      = 10;
            b.style.height        = 20;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            return b;
        }

        private static Toggle MakeToggle(string label, bool initial)
        {
            var t = new Toggle(label) { value = initial };
            t.style.marginBottom = 2;
            return t;
        }

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 3;
            l.style.fontSize     = 10;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 4;
            v.style.marginBottom    = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        /// <summary>
        /// 左ペインのカテゴリ折りたたみを作る。既定は折りたたみ（未保存時 value=false）。
        /// 開閉状態は PlayerUiPrefs（RecentPaths ファイル永続ストア）にキー
        /// "LeftPane.Fold.&lt;prefKey&gt;" で保存・復元する（選択モード永続化と同方式）。
        /// 見出しフォントを小さめにして縦スペースを節約する。
        /// </summary>
        private static Foldout MakeFoldout(string title, string prefKey)
        {
            string key = "LeftPane.Fold." + prefKey;
            var f = new Foldout { text = title };
            // 復元（未保存は既定＝折りたたみ）
            f.SetValueWithoutNotify(Poly_Ling.Player.PlayerUiPrefs.GetBool(key, false));
            // 保存（開閉のたびに write-through）
            f.RegisterValueChangedCallback(evt =>
                Poly_Ling.Player.PlayerUiPrefs.SetBool(key, evt.newValue));
            f.style.marginTop    = 2;
            f.style.marginBottom = 2;
            // 見出しトグルのフォントサイズを縮小
            f.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var toggle = f.Q<Toggle>(className: "unity-foldout__toggle");
                if (toggle != null) toggle.style.fontSize = 10;
            });
            return f;
        }

        private static StyleColor PaneBg(float v) => new StyleColor(new Color(v, v, v, 1f));
        private static StyleColor Col(float v)    => new StyleColor(new Color(v, v, v, 1f));
    }
}
