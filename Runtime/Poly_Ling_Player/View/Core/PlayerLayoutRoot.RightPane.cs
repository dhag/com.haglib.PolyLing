// PlayerLayoutRoot.RightPane.cs
// 右ペイン：各サブパネルのセクションの宣言と組み立て。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインのパネル種別。セクションを作るとき（AddSection）に必ず宣言し、
    /// 種別で表示区画が自動で決まる。
    ///
    ///   General … 3D 操作を持たないパネル（一覧・入出力・メッシュ処理など）。上区画。
    ///   Tool3D  … ビューポートの 3D 操作を使うパネル（頂点移動・選択を使う編集など）。下区画。
    ///   Pinned  … 常駐のリスト（モデル／オブジェクト／マテリアル）。上区画の先頭に置き、
    ///             右ペイン最上部のボタンでそれぞれ独立に開閉する（排他にしない）。
    ///
    /// 上区画は「常駐のリスト（開いているものを縦に並べる）＋その下に一般パネル 1 つ」、
    /// 下区画は 3D 操作パネル 1 つを表示する。
    /// </summary>
    public enum RightPanelKind
    {
        General,
        Tool3D,
        Pinned,
    }

    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // 右ペイン セクション（公開要素）
        // ================================================================

        /// <summary>右ペイン上区画（一般パネル）の ScrollView の contentContainer。</summary>
        public VisualElement GeneralPaneContent { get; private set; }

        /// <summary>右ペイン下区画（3D 操作パネル）の ScrollView の contentContainer。</summary>
        public VisualElement ToolPaneContent { get; private set; }

        /// <summary>下区画を閉じる（3D 操作パネルを 1 つも出さない状態にする）ボタン。</summary>
        public Button ToolAreaCloseBtn { get; private set; }

        /// <summary>右ペイン最上部：常駐リストの開閉ボタン（モデル／オブジェクト／マテリアル）。</summary>
        public Button ModelListBtn    { get; private set; }
        public Button MeshListBtn     { get; private set; }
        public Button MaterialListBtn { get; private set; }

        /// <summary>AddSection で作った全セクション（作成順）。</summary>
        public System.Collections.Generic.IReadOnlyList<VisualElement> RightSections => _rightSections;

        private readonly System.Collections.Generic.List<VisualElement> _rightSections
            = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.Dictionary<VisualElement, RightPanelKind> _rightSectionKinds
            = new System.Collections.Generic.Dictionary<VisualElement, RightPanelKind>();

        /// <summary>
        /// セクションの種別。AddSection で作っていない要素（null を含む）は null。
        /// </summary>
        public RightPanelKind? GetRightPanelKind(VisualElement section)
        {
            if (section != null && _rightSectionKinds.TryGetValue(section, out var k)) return k;
            return null;
        }

        /// <summary>下区画（3D 操作）の外枠。空のときは display=None で隠す。</summary>
        private VisualElement _toolArea;
        /// <summary>上下区画の仕切り（ドラッグで下区画の高さを変える）。</summary>
        private VisualElement _rightAreaSplitter;
        /// <summary>右ペインの外枠（下区画の上限計算に使う）。</summary>
        private VisualElement _rightPaneRoot;
        /// <summary>最上部の常駐リスト開閉ボタン行（下区画の上限計算に使う）。</summary>
        private VisualElement _rightPinnedBar;
        /// <summary>下区画の希望の高さ（保存値・ドラッグ結果）。実際の高さはこれを上限で抑えた値。</summary>
        private float _toolAreaDesiredH;
        private bool  _rightSplitterHover;
        private bool  _rightSplitterDragging;

        private const string PrefRightToolH = "PolyLing.Player.Layout.RightToolH";
        private const float  DefRightToolH  = 360f;
        private const float  MinRightAreaH  = 60f;
        private const float  RightSplitterH = 8f;
        private static readonly Color RightSplitterColor      = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color RightSplitterHoverColor = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>右ペイン：モデルリストセクション（ModelListSubPanel を Build する対象）。</summary>
        public VisualElement ModelListSection { get; private set; }

        /// <summary>右ペイン：メッシュリストセクション（MeshListSubPanel を Build する対象）。</summary>
        public VisualElement MeshListSection { get; private set; }

        /// <summary>右ペイン：インポートセクション（PlayerImportSubPanel を Build する対象）。</summary>
        public VisualElement ImportSection { get; private set; }

        /// <summary>右ペイン：図形生成セクション（PlayerPrimitiveMeshSubPanel を Build する対象）。</summary>
        public VisualElement PrimitiveSection { get; private set; }

        /// <summary>
        /// 右ペイン：新図形生成セクション（2つ目の PlayerPrimitiveMeshSubPanel を Build する対象）。
        /// 既存 PrimitiveSection とは別インスタンスで、状態を共有しない。
        /// </summary>
        public VisualElement LivePrimitiveSection { get; private set; }

        /// <summary>右ペイン：スキンウェイトペイントセクション（ScrollView内）。</summary>
        public VisualElement SkinWeightPaintSection { get; private set; }

        /// <summary>右ペイン：スキンウェイト数値設定セクション（ScrollView内）。</summary>
        public VisualElement SkinWeightNumericSection { get; private set; }

        /// <summary>右ペイン：頂点移動サブパネルセクション（ScrollView内）。</summary>
        public VisualElement VertexMoveSection { get; private set; }

        /// <summary>右ペイン：ピボットオフセットサブパネルセクション（ScrollView内）。</summary>
        public VisualElement PivotSection { get; private set; }

        /// <summary>右ペイン：スカルプトサブパネルセクション（ScrollView内）。</summary>
        public VisualElement SculptSection { get; private set; }

        /// <summary>右ペイン：詳細選択サブパネルセクション（ScrollView内）。</summary>
        public VisualElement AdvancedSelectSection { get; private set; }

        /// <summary>右ペイン：下絵設定セクション（ScrollView内）。</summary>
        public VisualElement UnderlaySection { get; private set; }

        /// <summary>右ペイン：作業フォルダ設定セクション（ScrollView内）。</summary>
        public VisualElement WorkFolderSection { get; private set; }

        /// <summary>右ペイン：軸/グリッド設定セクション（ScrollView内）。</summary>
        public VisualElement GridAxisSection { get; private set; }

        /// <summary>右ペイン：カメラ調整セクション（ScrollView内）。</summary>
        public VisualElement CameraSection { get; private set; }

        /// <summary>右ペイン：キャプチャ設定セクション（ScrollView内）。</summary>
        public VisualElement CaptureSection { get; private set; }

        /// <summary>右ペイン：ブレンドセクション（ScrollView内）。</summary>
        public VisualElement BlendSection { get; private set; }

        /// <summary>右ペイン：モデルブレンドセクション（ScrollView内）。</summary>
        public VisualElement ModelBlendSection { get; private set; }

        /// <summary>右ペイン：対称な参照メッシュに基づく臨時の対称化。</summary>
        public VisualElement ReferenceSymmetrySection { get; private set; }

        /// <summary>シュリンカー(頂点)セクション</summary>
        public VisualElement ShrinkSection { get; private set; }

        /// <summary>シュリンカー(面)セクション</summary>
        public VisualElement ShrinkFaceSection { get; private set; }

        public VisualElement BoneEditorSection { get; private set; }

        public VisualElement UVEditorSection { get; private set; }

        public VisualElement UVUnwrapSection { get; private set; }

        public VisualElement MaterialListSection   { get; private set; }
        public VisualElement UVZSection            { get; private set; }
        public VisualElement PartsSelectionSetSection { get; private set; }
        public VisualElement MeshSelectionSetSection  { get; private set; }
        public VisualElement ObjectGroupSection    { get; private set; }
        public VisualElement NormalExcludeSetSection { get; private set; }
        public VisualElement NormalEditSection     { get; private set; }
        public VisualElement NormalTransplantSection { get; private set; }
        public VisualElement ThinPlateMorphSection { get; private set; }
        public VisualElement FaceHideSection       { get; private set; }
        public VisualElement MergeMeshesSection    { get; private set; }
        public VisualElement BooleanSection        { get; private set; }
        public VisualElement MorphSection          { get; private set; }
        public VisualElement MorphCreateSection    { get; private set; }
        public VisualElement TPoseSection          { get; private set; }
        public VisualElement HumanoidMappingSection { get; private set; }
        public VisualElement MirrorSection         { get; private set; }
        public VisualElement QuadDecimatorSection   { get; private set; }

        public VisualElement AlignVerticesSection       { get; private set; }
        public VisualElement PlanarizeAlongBonesSection { get; private set; }
        public VisualElement SmoothEdgesSection         { get; private set; }

        /// <summary>右ペイン：パイプ群専用の左右対称化（パイプの整列）。</summary>
        public VisualElement PipeAlignSection           { get; private set; }

        public VisualElement SurfaceSnapSection         { get; private set; }

        /// <summary>右ペイン：藤壺（オブジェクト配置）の部品を原型の形へ張り直す。</summary>
        public VisualElement PlaceObjectReshapeSection  { get; private set; }

        public VisualElement MergeVerticesSection       { get; private set; }
        public VisualElement SplitVerticesSection       { get; private set; }
        public VisualElement VertexHoleSection          { get; private set; }
        public VisualElement VertexDissolveSection      { get; private set; }

        /// <summary>右ペイン：穴頂点数合わせ（ブリッジの前処理）セクション。</summary>
        public VisualElement HoleRingCountSection       { get; private set; }

        /// <summary>右ペイン：辺群ブリッジ（2 か所の辺群の間に面を張る）セクション。</summary>
        public VisualElement EdgeBridgeSection          { get; private set; }
        public VisualElement BillboardProfileSection    { get; private set; }

        public VisualElement Tri4To1Section             { get; private set; }
        public VisualElement FaceMergeSection           { get; private set; }
        public VisualElement Quad4To1Section            { get; private set; }

        /// <summary>右ペイン：頂点IDユーティリティ（診断 / 修復）セクション。</summary>
        public VisualElement VertexIdSection            { get; private set; }

        /// <summary>右ペイン：モデル間頂点データ転送セクション。</summary>
        public VisualElement VertexTransferSection      { get; private set; }

        /// <summary>右ペイン：パーツID / サブID 採番セクション。</summary>
        public VisualElement PartsIdSection             { get; private set; }

        public VisualElement AddFaceSection             { get; private set; }
        public VisualElement FlipFaceSection            { get; private set; }
        public VisualElement RotateSection              { get; private set; }

        /// <summary>作業用ローカル軸（回転 / 曲げの基準フレーム）のセクションとボタン。</summary>
        public VisualElement WorkAxisSection            { get; private set; }

        /// <summary>デフォーマ（回転 / 曲げ）のセクションとボタン。基準は作業軸。</summary>
        public VisualElement DeformSection              { get; private set; }

        /// <summary>格子変形のセクションとボタン。格子フレームは作業軸。</summary>
        public VisualElement LatticeSection             { get; private set; }

        public VisualElement ScaleSection               { get; private set; }
        public VisualElement EdgeBevelSection           { get; private set; }
        public VisualElement EdgeExtrudeSection         { get; private set; }
        public VisualElement FaceExtrudeSection         { get; private set; }
        public VisualElement EdgeTopologySection        { get; private set; }
        public VisualElement KnifeSection               { get; private set; }
        public VisualElement SolidifySection            { get; private set; }
        public VisualElement LineExtrudeSection         { get; private set; }
        public VisualElement MediaPipeSection       { get; private set; }
        public VisualElement MediaPipeFingerSection { get; private set; }
        public VisualElement MediaPipeBodySection   { get; private set; }
        public VisualElement VMDTestSection         { get; private set; }
        public VisualElement UnityClipToVrmaSection { get; private set; }
        public VisualElement VmdToVrmaSection      { get; private set; }
        public VisualElement MotionClipTestSection   { get; private set; }

        /// <summary>パイプライン自動検証（読み込み→スキン→ウェイト→マッピング→保存往復）。</summary>
        /// <summary>右ペイン：コマンド定義の検査セクション（ScrollView内）。</summary>
        public VisualElement CommandSchemaSection    { get; private set; }

        public VisualElement OriginTestSection        { get; private set; }
        public VisualElement SkinTestSection          { get; private set; }

        /// <summary>スプリングボーン検証（ダミー揺れもの生成→割当→Tポーズ）。</summary>
        public VisualElement SpringBoneTestSection    { get; private set; }

        /// <summary>
        /// 右ペイン：揺れもの編集（VRM SpringBone のオーサリング）。
        /// 検証パネル（SpringBoneTest）とは別物で、こちらが通常の編集機能。
        /// </summary>
        public VisualElement SpringBoneSection        { get; private set; }

        /// <summary>
        /// 右ペイン：当たり判定（VRM SpringBone の collider）の作成と編集。
        /// 揺れもの編集がまとまり（グループ）の名前しか扱えなかったため分けた。
        /// </summary>
        public VisualElement SpringBoneColliderSection { get; private set; }

        /// <summary>
        /// 右ペイン：Humanoid マッスル可動域（HumanLimit）の編集。
        /// Humanoid 割当と同じ「ボーンに属性を付ける」系なので隣に並べる。
        /// </summary>
        public VisualElement HumanLimitSection         { get; private set; }

        /// <summary>
        /// 右ペイン：VRM 出力設定（作者情報・許諾・視線・一人称）。
        /// 出力ごとの上書きは「エクスポート」側にあり、こちらは保存される値。
        /// </summary>
        public VisualElement VrmSettingsSection        { get; private set; }


        /// <summary>
        /// ロボ組み立て自動検証。基本図形の生成から VRM 書き出しまでを 5 系統ぶん流す。
        /// 段ごとにフォルダへ保存するので、途中経過をあとから追える。
        /// </summary>
        public VisualElement RobotBuildTestSection    { get; private set; }

        public VisualElement FrillSkirtTestSection    { get; private set; }
        public VisualElement SpringSkinScenarioSection { get; private set; }

        /// <summary>手本（シナリオ）を選んで先頭から流す。指示・確認の段と失敗で止まる。</summary>
        public VisualElement ScenarioSection          { get; private set; }

        /// <summary>
        /// 揺れもの（パイプ）→スキンド→VRM 自動検証。
        /// フリル版と同じ MQO・同じ順で、フリルの段だけをパイプへ置き換えたもの。
        /// </summary>
        public VisualElement SpringSkinPipeScenarioSection { get; private set; }

        public VisualElement PipeHairTestSection      { get; private set; }
        public VisualElement BarnacleTestSection      { get; private set; }
        public VisualElement RevolutionTestSection    { get; private set; }
        public VisualElement Profile2DTestSection     { get; private set; }

        /// <summary>
        /// PMX位置→MQO保存 自動検証。PMX をソースにして MQO の頂点位置だけを
        /// 差し替え、別名の MQO として書き出すまでを流す。
        /// </summary>
        public VisualElement PmxToMqoTestSection      { get; private set; }

        /// <summary>
        /// MQO位置UV→PMX保存 自動検証。MQO をソースにして、頂点数の一致した
        /// オブジェクトだけ頂点位置と UV を差し替え、別名の PMX として書き出す。
        /// </summary>
        public VisualElement MqoToPmxTestSection      { get; private set; }

        public VisualElement RemoteServerSection    { get; private set; }
        public VisualElement LogSection             { get; private set; }

        /// <summary>右ペイン：エクスポートセクション（ScrollView内）。</summary>
        public VisualElement ExportSection { get; private set; }

        /// <summary>右ペイン：プロジェクト保存 / 読込セクション（ScrollView内）。
        /// 押し間違いでデータを壊さないよう、保存と読込は別セクションに分けている。</summary>
        public VisualElement ProjectSaveSection { get; private set; }
        public VisualElement ProjectLoadSection { get; private set; }

        /// <summary>右ペイン：部分インポートセクション（ScrollView内）。</summary>
        public VisualElement PartialImportSection { get; private set; }

        /// <summary>右ペイン：部分エクスポートセクション（ScrollView内）。</summary>
        public VisualElement PartialExportSection { get; private set; }

        /// <summary>右ペイン：MeshFilter→Skinnedセクション（ScrollView外）。</summary>
        public VisualElement MeshFilterToSkinnedSection { get; private set; }

        /// <summary>描画オブジェクト単位の種別変換セクション。</summary>
        public VisualElement SkinKindSection { get; private set; }

        /// <summary>右ペイン：オブジェクト移動TRSセクション（ScrollView内、MeshListSectionの直後）。</summary>
        public VisualElement ObjectMoveTRSSection { get; private set; }

        // ================================================================
        // Right ペイン
        // ================================================================

        private VisualElement BuildRightPane()
        {
            var pane = MakePane(220f);
            pane.style.backgroundColor = PaneBg(0.15f);
            pane.style.flexDirection   = FlexDirection.Column;
            pane.style.overflow        = Overflow.Hidden;

            // 最上部：常駐リストの開閉ボタン。スクロールの外に置き、常に見えるようにする。
            var pinnedBar = new VisualElement();
            pinnedBar.style.flexDirection = FlexDirection.Row;
            pinnedBar.style.flexShrink    = 0;
            pinnedBar.style.paddingTop    = 4;
            pinnedBar.style.paddingLeft   = 4;
            pinnedBar.style.paddingRight  = 4;
            ModelListBtn    = MakeBtn("モデルリスト");
            MeshListBtn     = MakeBtn("オブジェクトリスト");
            MaterialListBtn = MakeBtn("マテリアルリスト");
            foreach (var b in new[] { ModelListBtn, MeshListBtn, MaterialListBtn })
            {
                b.style.flexGrow  = 1;
                b.style.flexBasis = 0;
                b.style.minWidth  = 0;
                pinnedBar.Add(b);
            }
            pane.Add(pinnedBar);
            _rightPaneRoot  = pane;
            _rightPinnedBar = pinnedBar;

            // 上区画：常駐リスト（RightPanelKind.Pinned）＋一般パネル（RightPanelKind.General）。残りの高さを全部使う。
            var generalScroll = new ScrollView(ScrollViewMode.Vertical);
            generalScroll.style.flexGrow     = 1;
            generalScroll.style.flexShrink   = 1;
            generalScroll.style.minHeight    = MinRightAreaH;
            generalScroll.style.paddingTop   = 4;
            generalScroll.style.paddingLeft  = 4;
            generalScroll.style.paddingRight = 4;
            pane.Add(generalScroll);
            GeneralPaneContent = generalScroll.contentContainer;
            GeneralPaneContent.style.color = new StyleColor(Color.white);

            // 上下の仕切り。下区画が空のときは下区画と一緒に隠す。
            _rightAreaSplitter = new VisualElement();
            _rightAreaSplitter.style.height          = RightSplitterH;
            _rightAreaSplitter.style.flexShrink      = 0;
            _rightAreaSplitter.style.backgroundColor = new StyleColor(RightSplitterColor);
            _rightAreaSplitter.style.display         = DisplayStyle.None;
            pane.Add(_rightAreaSplitter);

            // 下区画：3D 操作パネル（RightPanelKind.Tool3D）。見出し行（閉じる）＋ ScrollView。
            _toolArea = new VisualElement();
            _toolArea.style.flexDirection = FlexDirection.Column;
            _toolArea.style.flexShrink    = 0;
            _toolAreaDesiredH             = LoadPref(PrefRightToolH, DefRightToolH);
            _toolArea.style.height        = _toolAreaDesiredH;
            _toolArea.style.minHeight     = MinRightAreaH;
            _toolArea.style.display       = DisplayStyle.None;
            pane.Add(_toolArea);

            var toolHeader = new VisualElement();
            toolHeader.style.flexDirection = FlexDirection.Row;
            toolHeader.style.alignItems    = Align.Center;
            toolHeader.style.flexShrink    = 0;
            toolHeader.style.paddingLeft   = 4;
            toolHeader.style.paddingRight  = 4;
            var toolLabel = new Label("3D操作");
            toolLabel.style.flexGrow = 1;
            toolLabel.style.color    = new StyleColor(Color.white);
            toolHeader.Add(toolLabel);
            ToolAreaCloseBtn = MakeBtn("閉じる");
            ToolAreaCloseBtn.style.flexGrow = 0;
            toolHeader.Add(ToolAreaCloseBtn);
            _toolArea.Add(toolHeader);

            var toolScroll = new ScrollView(ScrollViewMode.Vertical);
            toolScroll.style.flexGrow     = 1;
            toolScroll.style.paddingTop   = 4;
            toolScroll.style.paddingLeft  = 4;
            toolScroll.style.paddingRight = 4;
            _toolArea.Add(toolScroll);
            ToolPaneContent = toolScroll.contentContainer;
            ToolPaneContent.style.color = new StyleColor(Color.white);

            SetupRightAreaSplitterDrag(pane);

            // 各セクションは区切り線（上ボーダー）付きで、種別に応じた区画の ScrollView へ入る。
            // 独立 Separator 要素を廃止し、ボーダーをセクション自身に持たせることで、
            // 非表示セクションでは区切り線も一緒に消える（線分残り対策）。
            //
            // visible=true:  既定で表示（起動時はオブジェクトリストだけ）
            // visible=false: 既定で非表示（display=None）

            // ── 常駐リスト（上区画の先頭。この順で縦に並ぶ）
            // モデルリスト（先頭：区切り線なし）
            ModelListSection    = AddSection(visible: false, kind: RightPanelKind.Pinned, topBorder: false);
            // オブジェクトリスト（メッシュリスト）
            MeshListSection     = AddSection(visible: true,  kind: RightPanelKind.Pinned);
            // マテリアルリスト
            MaterialListSection = AddSection(visible: false, kind: RightPanelKind.Pinned);

            // ── オブジェクト移動TRSセクション
            ObjectMoveTRSSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── 頂点移動サブパネルセクション
            VertexMoveSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── ピボットオフセットサブパネルセクション
            PivotSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── スカルプトサブパネルセクション
            SculptSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── 詳細選択サブパネルセクション
            AdvancedSelectSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── スキンウェイトペイントセクション
            SkinWeightPaintSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── スキンウェイト数値設定セクション
            SkinWeightNumericSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── ブレンドセクション
            BlendSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── モデルブレンドセクション
            ModelBlendSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── リファレンスに基づく対称化（臨時）
            ReferenceSymmetrySection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── シュリンカー(頂点)セクション
            ShrinkSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── シュリンカー(面)セクション
            ShrinkFaceSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── TPSモーフセクション
            ThinPlateMorphSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── ボーンエディタセクション
            BoneEditorSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── UVエディタセクション
            UVEditorSection = AddSection(visible: false, kind: RightPanelKind.General);
            UVEditorSection.style.flexGrow = 1;

            // ── UV展開セクション
            UVUnwrapSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── 追加パネルセクション群（デフォルト非表示）────────────────
            UVZSection                 = AddSection(visible: false, kind: RightPanelKind.General);
            PartsSelectionSetSection   = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            MeshSelectionSetSection    = AddSection(visible: false, kind: RightPanelKind.General);
            ObjectGroupSection         = AddSection(visible: false, kind: RightPanelKind.General);
            NormalExcludeSetSection    = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            NormalEditSection          = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            NormalTransplantSection    = AddSection(visible: false, kind: RightPanelKind.General);
            FaceHideSection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            MergeMeshesSection         = AddSection(visible: false, kind: RightPanelKind.General);
            BooleanSection             = AddSection(visible: false, kind: RightPanelKind.General);
            MorphSection               = AddSection(visible: false, kind: RightPanelKind.General);
            MorphCreateSection         = AddSection(visible: false, kind: RightPanelKind.General);
            TPoseSection               = AddSection(visible: false, kind: RightPanelKind.General);
            HumanoidMappingSection     = AddSection(visible: false, kind: RightPanelKind.General);
            MirrorSection              = AddSection(visible: false, kind: RightPanelKind.General);
            QuadDecimatorSection       = AddSection(visible: false, kind: RightPanelKind.General);
            AlignVerticesSection       = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            PlanarizeAlongBonesSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            SmoothEdgesSection         = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            PipeAlignSection           = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            SurfaceSnapSection         = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            PlaceObjectReshapeSection  = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            MergeVerticesSection       = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            SplitVerticesSection       = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            VertexHoleSection          = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            VertexDissolveSection      = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            HoleRingCountSection       = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            EdgeBridgeSection          = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            BillboardProfileSection    = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            Tri4To1Section             = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            FaceMergeSection           = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            Quad4To1Section            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            VertexIdSection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            VertexTransferSection      = AddSection(visible: false, kind: RightPanelKind.General);
            PartsIdSection             = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            AddFaceSection             = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            FlipFaceSection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            RotateSection              = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            WorkAxisSection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            DeformSection              = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            LatticeSection             = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            ScaleSection               = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            EdgeBevelSection           = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            EdgeExtrudeSection         = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            FaceExtrudeSection         = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            EdgeTopologySection        = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            KnifeSection               = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            SolidifySection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            LineExtrudeSection         = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            MediaPipeSection           = AddSection(visible: false, kind: RightPanelKind.General);
            MediaPipeFingerSection     = AddSection(visible: false, kind: RightPanelKind.General);
            MediaPipeBodySection       = AddSection(visible: false, kind: RightPanelKind.General);
            VMDTestSection             = AddSection(visible: false, kind: RightPanelKind.General);
            UnityClipToVrmaSection     = AddSection(visible: false, kind: RightPanelKind.General);
            VmdToVrmaSection           = AddSection(visible: false, kind: RightPanelKind.General);
            MotionClipTestSection      = AddSection(visible: false, kind: RightPanelKind.General);
            CommandSchemaSection       = AddSection(visible: false, kind: RightPanelKind.General);
            OriginTestSection          = AddSection(visible: false, kind: RightPanelKind.General);
            SkinTestSection            = AddSection(visible: false, kind: RightPanelKind.General);
            SpringBoneTestSection      = AddSection(visible: false, kind: RightPanelKind.General);
            SpringBoneSection          = AddSection(visible: false, kind: RightPanelKind.General);
            SpringBoneColliderSection  = AddSection(visible: false, kind: RightPanelKind.General);
            HumanLimitSection          = AddSection(visible: false, kind: RightPanelKind.General);
            VrmSettingsSection         = AddSection(visible: false, kind: RightPanelKind.General);
            RobotBuildTestSection      = AddSection(visible: false, kind: RightPanelKind.General);
            ScenarioSection            = AddSection(visible: false, kind: RightPanelKind.General);
            FrillSkirtTestSection      = AddSection(visible: false, kind: RightPanelKind.General);
            SpringSkinScenarioSection  = AddSection(visible: false, kind: RightPanelKind.General);
            SpringSkinPipeScenarioSection = AddSection(visible: false, kind: RightPanelKind.General);
            PipeHairTestSection        = AddSection(visible: false, kind: RightPanelKind.General);
            BarnacleTestSection        = AddSection(visible: false, kind: RightPanelKind.General);
            RevolutionTestSection      = AddSection(visible: false, kind: RightPanelKind.General);
            Profile2DTestSection       = AddSection(visible: false, kind: RightPanelKind.General);
            PmxToMqoTestSection        = AddSection(visible: false, kind: RightPanelKind.General);
            MqoToPmxTestSection        = AddSection(visible: false, kind: RightPanelKind.General);
            UnderlaySection            = AddSection(visible: false, kind: RightPanelKind.Tool3D);   // 表示中はビューポートの左ドラッグで下絵を動かす
            GridAxisSection            = AddSection(visible: false, kind: RightPanelKind.General);
            WorkFolderSection          = AddSection(visible: false, kind: RightPanelKind.General);
            CameraSection              = AddSection(visible: false, kind: RightPanelKind.Tool3D);
            CaptureSection             = AddSection(visible: false, kind: RightPanelKind.General);
            RemoteServerSection        = AddSection(visible: false, kind: RightPanelKind.General);
            LogSection                 = AddSection(visible: false, kind: RightPanelKind.General);

            // ── エクスポートセクション
            ExportSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── プロジェクト保存 / 読込セクション（別々に持つ）
            ProjectSaveSection = AddSection(visible: false, kind: RightPanelKind.General);
            ProjectLoadSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── 部分インポートセクション
            PartialImportSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── 部分エクスポートセクション
            PartialExportSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── インポートセクション（既定表示）
            ImportSection = AddSection(visible: true, kind: RightPanelKind.General);

            // ── 図形生成セクション
            // 以前は ScrollView 外（pane 直下・flexShrink=0）に置いていたが、
            // 内容がペイン高を超えると下端が overflow:Hidden で切られ、
            // 最下部の生成ボタンが隠れていた。ScrollView 内へ移し、
            // 内容超過時はスクロールで生成ボタンへ到達できるようにする。
            // プレビュー／回転体／プロファイル2D の各キャンバスは WheelEvent を
            // StopPropagation 済みのため、親 ScrollView がホイール操作を奪うことはない。
            PrimitiveSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── 新図形生成セクション（検証用の2つ目のインスタンス）
            LivePrimitiveSection = AddSection(visible: false, kind: RightPanelKind.Tool3D);

            // ── MeshFilter→Skinnedセクション（ScrollView内へ移動）
            MeshFilterToSkinnedSection = AddSection(visible: false, kind: RightPanelKind.General);

            // ── 描画オブジェクト単位の種別変換セクション
            SkinKindSection = AddSection(visible: false, kind: RightPanelKind.General);

            return pane;
        }

        /// <summary>
        /// 右ペインにセクションを追加する。種別（kind）で入る区画が決まる
        /// （General → 上区画、Tool3D → 下区画）。種別は台帳に記録し、
        /// パネル切替（ShowRightPanel）は台帳から種別を引いて同じ区画だけを切り替える。
        /// 区切り線はセクション自身の上ボーダーで表現するため、
        /// 非表示時（display=None）には区切り線も一緒に消える。
        /// </summary>
        /// <param name="visible">true で既定表示、false で display=None</param>
        /// <param name="kind">パネル種別（表示区画）</param>
        /// <param name="topBorder">上ボーダー（区切り線）を付けるか</param>
        private VisualElement AddSection(bool visible, RightPanelKind kind, bool topBorder = true)
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
            (kind == RightPanelKind.Tool3D ? ToolPaneContent : GeneralPaneContent).Add(v);
            _rightSections.Add(v);
            _rightSectionKinds[v] = kind;
            return v;
        }

        /// <summary>
        /// 下区画（3D 操作）の表示／非表示。空のときは仕切りごと隠し、上区画が全高を使う。
        /// </summary>
        public void SetToolAreaOpen(bool open)
        {
            var d = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (_toolArea != null)          _toolArea.style.display          = d;
            if (_rightAreaSplitter != null) _rightAreaSplitter.style.display = d;
            if (open) ApplyToolAreaHeight();
        }

        /// <summary>
        /// 下区画の高さの上限。ペイン高からボタン行・仕切り・上区画の最小高を引いた値。
        /// ペイン高が未確定のときは上限なし。
        /// </summary>
        private float ToolAreaMaxH()
        {
            if (_rightPaneRoot == null) return float.MaxValue;
            float paneH = _rightPaneRoot.resolvedStyle.height;
            if (float.IsNaN(paneH) || paneH <= 0f) return float.MaxValue;
            float barH = _rightPinnedBar != null ? _rightPinnedBar.resolvedStyle.height : 0f;
            if (float.IsNaN(barH) || barH < 0f) barH = 0f;
            return Mathf.Max(MinRightAreaH, paneH - barH - RightSplitterH - MinRightAreaH);
        }

        /// <summary>希望の高さを上限で抑えて下区画に適用する（保存値は変えない）。</summary>
        private void ApplyToolAreaHeight()
        {
            if (_toolArea == null) return;
            float h   = Mathf.Clamp(_toolAreaDesiredH, MinRightAreaH, ToolAreaMaxH());
            float cur = _toolArea.style.height.value.value;
            if (!Mathf.Approximately(cur, h)) _toolArea.style.height = h;
        }

        private void UpdateRightSplitterColor()
        {
            if (_rightAreaSplitter == null) return;
            _rightAreaSplitter.style.backgroundColor = new StyleColor(
                (_rightSplitterHover || _rightSplitterDragging) ? RightSplitterHoverColor : RightSplitterColor);
        }

        /// <summary>
        /// 上下区画の仕切りのドラッグ。下区画の高さを変え、離したときに保存する。
        /// </summary>
        private void SetupRightAreaSplitterDrag(VisualElement pane)
        {
            float startY = 0f;
            float startH = 0f;

            // ペインの高さが変わったら（ウィンドウのリサイズ等）下区画を上限内に収め直す。
            pane.RegisterCallback<GeometryChangedEvent>(_ => ApplyToolAreaHeight());

            _rightAreaSplitter.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _rightSplitterHover = true;
                UpdateRightSplitterColor();
            });
            _rightAreaSplitter.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _rightSplitterHover = false;
                UpdateRightSplitterColor();
            });

            _rightAreaSplitter.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _rightSplitterDragging = true;
                startY = evt.position.y;
                startH = _toolArea.resolvedStyle.height;
                _rightAreaSplitter.CapturePointer(evt.pointerId);
                UpdateRightSplitterColor();
                evt.StopPropagation();
            });
            _rightAreaSplitter.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_rightSplitterDragging) return;
                float h = Mathf.Clamp(startH - (evt.position.y - startY), MinRightAreaH, ToolAreaMaxH());
                _toolAreaDesiredH      = h;
                _toolArea.style.height = h;
                evt.StopPropagation();
            });
            _rightAreaSplitter.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_rightSplitterDragging) return;
                _rightSplitterDragging = false;
                if (_rightAreaSplitter.HasPointerCapture(evt.pointerId))
                    _rightAreaSplitter.ReleasePointer(evt.pointerId);
                _rightSplitterHover = _rightAreaSplitter.worldBound.Contains(evt.position);
                UpdateRightSplitterColor();
                PlayerPrefs.SetFloat(PrefRightToolH, _toolAreaDesiredH);
                PlayerPrefs.Save();
                evt.StopPropagation();
            });
            _rightAreaSplitter.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                _rightSplitterDragging = false;
                UpdateRightSplitterColor();
            });
        }
    }
}
