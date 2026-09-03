// PlayerLayoutRoot.cs
// UIToolkit による3ペインレイアウト構築。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
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
        public Toggle TiltToggleFront  { get; private set; }   // Front/Side を水平45°斜めに
        public Toggle TiltToggleSide   { get; private set; }   // (Front/Side 連動・同じ共有値)

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
        // 「選択Mirror」トグルは廃止。選択メッシュのミラー表示は選択Mesh に従属する
        // （ViewportDisplaySettings.WithMirrorClamped）。
        // 【番号を繰り下げてよい理由】 2026-08-28
        //   非選Mirror の直下に 2 行を挿入したため VD_SEL_WIRE 以降が +2 されている。
        //   これらは全てシンボルで参照されており、表示設定の永続化は
        //   ViewportDisplaySettings.ToBits / FromBits が担う（トグルの並び順とは無関係）。
        //   したがって番号が変わっても保存データは壊れない。
        //   itemLabels / itemDefaults の並びは必ずこの順序に一致させること。
        public const int VD_CULLING      = 0;
        public const int VD_SEL_MESH     = 1;
        public const int VD_UNSEL_MESH   = 2;
        /// <summary>非選択ミラーのマスタ。下の 3 つを一括で落とす。独立（非選Mesh に従属しない）。</summary>
        public const int VD_UNSEL_MIRROR = 3;
        /// <summary>非選択ミラーの面。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_MESH = 4;
        /// <summary>非選択ミラーの辺。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_WIRE = 5;
        /// <summary>非選択ミラーの頂点。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_VERT = 6;
        public const int VD_SEL_WIRE     = 7;
        public const int VD_UNSEL_WIRE   = 8;
        public const int VD_SEL_VERT     = 9;
        public const int VD_UNSEL_VERT   = 10;
        public const int VD_SEL_BONE     = 11;
        public const int VD_UNSEL_BONE   = 12;
        public const int VD_SEL_MESH_ORIGIN   = 13;
        public const int VD_UNSEL_MESH_ORIGIN = 14;
        public const int VD_MIRROR_MESH_ORIGIN = 15;
        public const int VD_NORMAL       = 16;
        public const int VD_COUNT        = 17;

        /// <summary>左ペイン：ラッソ選択トグル。</summary>
        public Toggle LassoToggle { get; private set; }

        /// <summary>
        /// 左ペイン：性能ログ（CSV）の記録トグル。既定 OFF。
        ///
        /// ON の間だけ PLPerfLog が一定間隔で数値 1 行を CSV へ追記する。
        /// 出力先は Application.persistentDataPath で、開始時にログパネルへ通知される。
        /// 値は PlayerUiPrefs に永続化される。
        /// </summary>
        public Toggle PerfLogToggle { get; private set; }

        /// <summary>
        /// 左ペイン：軌道回転の中心をローカル原点（＝ピボット）にするトグル。既定 ON。
        ///
        /// ON のとき、メインビュー（透視ビューポート）の右ドラッグ回転は
        /// 選択オブジェクトのローカル原点の重心を軸に回る。ローカル原点は
        /// MeshContext.WorldMatrix の平行移動成分であり、PivotOffsetTool が
        /// 言う「ピボット原点」と同じ点。
        ///
        /// ON にした瞬間も視点は一切動かない。回した瞬間に軸が変わるだけである
        /// （Blender の Orbit Around Selection / Maya の Tumble Pivot と同じ扱い）。
        /// 視点を選択へ寄せる操作（Frame Selected 相当）とは別物。
        ///
        /// OrbitCenterToSelectionBtn と排他。釦を押すとここは自動的に OFF になり、
        /// 逆にここを ON に戻すと釦で確定した固定ピボットは解除される。
        /// </summary>
        public Toggle OrbitAroundLocalOriginToggle { get; private set; }

        /// <summary>
        /// 左ペイン：押した時点の選択の重心を軌道回転の中心として固定する押し釦。
        ///
        /// スナップショット動作。押した後に選択を変えても頂点を動かしても
        /// 中心は移動しない。更新したいときは再度押す。
        /// 要素（頂点/辺/面/線分）が未選択のときはローカル原点（ピボット）へ
        /// フォールバックする。押しても視点は動かない。
        /// </summary>
        public Button OrbitCenterToSelectionBtn { get; private set; }

        /// <summary>
        /// 左ペイン：法線自動計算トグル。既定 OFF（＝自動計算しない）。
        /// 選択メッシュの MeshObject.PreserveNormals を反転して書き込む
        /// （自動計算 ON ＝ PreserveNormals false）。
        /// </summary>
        public Toggle AutoRecalcNormalsToggle { get; private set; }

        /// <summary>左ペイン：法線の手動再計算ボタン。対象は選択メッシュ。</summary>
        public Button RecalcNormalsBtn { get; private set; }

        // 選択モード切替（頂点/辺/面/線分・非排他）。SelectionState.Mode を設定する。
        public Toggle SelModeVertexToggle { get; private set; }
        public Toggle SelModeEdgeToggle   { get; private set; }
        public Toggle SelModeFaceToggle   { get; private set; }
        public Toggle SelModeLineToggle   { get; private set; }

        // 辺／面／線分を選んだとき、その構成頂点も頂点選択へ入れるか（種別ごと）。
        // MoveToolHandler.ExpandLinkedVertices の展開対象を種別単位で切る。
        // OFF にすると「辺だけを選んだのに頂点まで選択色になる」状態を避けられる。
        public Toggle SelExpandEdgeToVertexToggle { get; private set; }
        public Toggle SelExpandFaceToVertexToggle { get; private set; }
        public Toggle SelExpandLineToVertexToggle { get; private set; }

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

        /// <summary>
        /// 右ペイン：新図形生成セクション（2つ目の PlayerPrimitiveMeshSubPanel を Build する対象）。
        /// 既存 PrimitiveSection とは別インスタンスで、状態を共有しない。
        /// </summary>
        public VisualElement LivePrimitiveSection { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（新しい基本）。</summary>
        public Button LivePrimitiveBtn { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（新しい高度）。新しい基本と同じ LivePrimitiveSection を開く。</summary>
        public Button LiveAdvancedPrimitiveBtn { get; private set; }

        /// <summary>左ペイン：ツール切り替えボタン群。</summary>
        public Button ToolVertexMoveBtn  { get; private set; }
        public Button ToolObjectMoveBtn  { get; private set; }
        public Button ToolPivotOffsetBtn { get; private set; }
        public Button ToolSculptBtn      { get; private set; }
        public Button ToolAdvancedSelBtn { get; private set; }
        public Button ToolSkinWeightPaintBtn { get; private set; }

        /// <summary>左ペイン：スキンウェイト数値設定ボタン。</summary>
        public Button SkinWeightNumericBtn { get; private set; }

        /// <summary>左ペイン：一時選択サブツール呼び出しボタン (デバッグ用。ショートカット R / G と同処理)。</summary>
        public Button SubToolBoxSelectBtn   { get; private set; }
        public Button SubToolLassoSelectBtn { get; private set; }
        public Button SubToolDeleteBtn      { get; private set; }
        public Button ToolDeleteFaceBtn     { get; private set; }

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

        /// <summary>左ペイン：MeshFilter→Skinnedボタン。</summary>
        public Button MeshFilterToSkinnedBtn { get; private set; }

        /// <summary>描画オブジェクト単位の種別変換（MeshFilter 系 ⇔ SkinnedMesh 系）。</summary>
        public Button SkinKindBtn { get; private set; }

        /// <summary>左ペイン：下絵ボタン（その他）。</summary>
        public Button UnderlayBtn { get; private set; }

        /// <summary>右ペイン：下絵設定セクション（ScrollView内）。</summary>
        public VisualElement UnderlaySection { get; private set; }

        /// <summary>左ペイン：軸/グリッドボタン（その他）。</summary>
        public Button GridAxisBtn { get; private set; }

        /// <summary>右ペイン：軸/グリッド設定セクション（ScrollView内）。</summary>
        public VisualElement GridAxisSection { get; private set; }

        /// <summary>左ペイン：カメラ調整ボタン（その他）。</summary>
        public Button CameraBtn { get; private set; }

        /// <summary>右ペイン：カメラ調整セクション（ScrollView内）。</summary>
        public VisualElement CameraSection { get; private set; }

        /// <summary>左ペイン：キャプチャボタン（その他）。</summary>
        public Button CaptureBtn { get; private set; }

        /// <summary>右ペイン：キャプチャ設定セクション（ScrollView内）。</summary>
        public VisualElement CaptureSection { get; private set; }

        /// <summary>
        /// 4ビューポート（Perspective / Right / Top / Front）を含む中央領域。
        /// 3面図を含むキャプチャの切り出し範囲に使う。
        /// </summary>
        public VisualElement ViewportArea { get; private set; }

        /// <summary>右ペイン：ブレンドセクション（ScrollView内）。</summary>
        public VisualElement BlendSection { get; private set; }

        /// <summary>左ペイン：ブレンドボタン。</summary>
        public Button BlendBtn { get; private set; }

        /// <summary>右ペイン：モデルブレンドセクション（ScrollView内）。</summary>
        public VisualElement ModelBlendSection { get; private set; }

        /// <summary>シュリンカー(頂点)セクション</summary>
        public VisualElement ShrinkSection { get; private set; }

        /// <summary>シュリンカー(頂点)ボタン</summary>
        public Button ShrinkBtn { get; private set; }

        /// <summary>シュリンカー(面)セクション</summary>
        public VisualElement ShrinkFaceSection { get; private set; }

        /// <summary>シュリンカー(面)ボタン</summary>
        public Button ShrinkFaceBtn { get; private set; }
        public Button ThinPlateMorphBtn { get; private set; }

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
        public VisualElement NormalExcludeSetSection { get; private set; }
        public Button        NormalExcludeSetBtn   { get; private set; }
        public VisualElement NormalEditSection     { get; private set; }
        public Button        NormalEditBtn         { get; private set; }
        public VisualElement NormalTransplantSection { get; private set; }
        public VisualElement ThinPlateMorphSection { get; private set; }
        public Button        NormalTransplantBtn   { get; private set; }
        public VisualElement FaceHideSection       { get; private set; }
        public Button        FaceHideBtn           { get; private set; }
        public VisualElement MergeMeshesSection    { get; private set; }
        public Button        MergeMeshesBtn        { get; private set; }
        public VisualElement BooleanSection        { get; private set; }
        public Button        BooleanBtn            { get; private set; }
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
        public VisualElement SmoothEdgesSection         { get; private set; }
        public Button        SmoothEdgesBtn             { get; private set; }
        /// <summary>右ペイン：パイプ群専用の左右対称化（パイプの整列）。</summary>
        public VisualElement PipeAlignSection           { get; private set; }
        public Button        PipeAlignBtn               { get; private set; }
        public VisualElement SurfaceSnapSection         { get; private set; }
        public Button        SurfaceSnapBtn             { get; private set; }
        /// <summary>右ペイン：藤壺（オブジェクト配置）の部品を原型の形へ張り直す。</summary>
        public VisualElement PlaceObjectReshapeSection  { get; private set; }
        public Button        PlaceObjectReshapeBtn      { get; private set; }
        public VisualElement MergeVerticesSection       { get; private set; }
        public Button        MergeVerticesBtn           { get; private set; }
        public VisualElement SplitVerticesSection       { get; private set; }
        public Button        SplitVerticesBtn           { get; private set; }
        public VisualElement VertexHoleSection          { get; private set; }
        public Button        VertexHoleBtn              { get; private set; }
        public VisualElement VertexDissolveSection      { get; private set; }
        public Button        VertexDissolveBtn          { get; private set; }

        /// <summary>右ペイン：穴頂点数合わせ（ブリッジの前処理）セクション。</summary>
        public VisualElement HoleRingCountSection       { get; private set; }
        public Button        HoleRingCountBtn           { get; private set; }

        /// <summary>右ペイン：辺群ブリッジ（2 か所の辺群の間に面を張る）セクション。</summary>
        public VisualElement EdgeBridgeSection          { get; private set; }
        public Button        EdgeBridgeBtn              { get; private set; }
        public VisualElement Tri4To1Section             { get; private set; }
        public Button        Tri4To1Btn                 { get; private set; }
        public VisualElement FaceMergeSection           { get; private set; }
        public Button        FaceMergeBtn               { get; private set; }
        public VisualElement Quad4To1Section            { get; private set; }
        public Button        Quad4To1Btn                { get; private set; }
        public VisualElement FaceMergeCollapseSection   { get; private set; }
        public Button        FaceMergeCollapseBtn       { get; private set; }

        /// <summary>右ペイン：頂点IDユーティリティ（診断 / 修復）セクション。</summary>
        public VisualElement VertexIdSection            { get; private set; }
        public Button        VertexIdBtn                { get; private set; }

        /// <summary>右ペイン：モデル間頂点データ転送セクション。</summary>
        public VisualElement VertexTransferSection      { get; private set; }
        public Button        VertexTransferBtn          { get; private set; }

        /// <summary>右ペイン：パーツID / サブID 採番セクション。</summary>
        public VisualElement PartsIdSection             { get; private set; }
        public Button        PartsIdBtn                 { get; private set; }
        public VisualElement AddFaceSection             { get; private set; }
        public Button        AddFaceBtn                 { get; private set; }
        public VisualElement FlipFaceSection            { get; private set; }
        public Button        FlipFaceBtn                { get; private set; }
        public VisualElement RotateSection              { get; private set; }
        public Button        RotateBtn                  { get; private set; }
        /// <summary>作業用ローカル軸（回転 / 曲げの基準フレーム）のセクションとボタン。</summary>
        public VisualElement WorkAxisSection            { get; private set; }
        public Button        WorkAxisBtn                { get; private set; }
        /// <summary>デフォーマ（回転 / 曲げ）のセクションとボタン。基準は作業軸。</summary>
        public VisualElement DeformSection              { get; private set; }
        public Button        DeformBtn                  { get; private set; }
        /// <summary>格子変形のセクションとボタン。格子フレームは作業軸。</summary>
        public VisualElement LatticeSection             { get; private set; }
        public Button        LatticeBtn                 { get; private set; }
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
        public Button        BridgeBtn                  { get; private set; }
        public VisualElement SolidifySection            { get; private set; }
        public Button        SolidifyBtn                { get; private set; }
        public VisualElement MediaPipeSection       { get; private set; }
        public Button        MediaPipeBtn           { get; private set; }
        public VisualElement VMDTestSection         { get; private set; }
        public Button        VMDTestBtn             { get; private set; }
        public VisualElement UnityClipTestSection    { get; private set; }
        public Button        UnityClipTestBtn        { get; private set; }
        public VisualElement MotionClipTestSection   { get; private set; }
        public Button        MotionClipTestBtn        { get; private set; }
        /// <summary>パイプライン自動検証（読み込み→スキン→ウェイト→マッピング→保存往復）。</summary>
        public VisualElement PipelineTestSection      { get; private set; }
        public Button        PipelineTestBtn          { get; private set; }
        public VisualElement OriginTestSection        { get; private set; }
        public Button        OriginTestBtn            { get; private set; }
        public VisualElement SkinTestSection          { get; private set; }

        /// <summary>スプリングボーン検証（ダミー揺れもの生成→割当→Tポーズ）。</summary>
        public VisualElement SpringBoneTestSection    { get; private set; }
        public Button        SpringBoneTestBtn        { get; private set; }

        /// <summary>
        /// ロボ組み立て自動検証。基本図形の生成から VRM 書き出しまでを 5 系統ぶん流す。
        /// 段ごとにフォルダへ保存するので、途中経過をあとから追える。
        /// </summary>
        public VisualElement RobotBuildTestSection    { get; private set; }
        public Button        RobotBuildTestBtn        { get; private set; }
        public Button        SkinTestBtn              { get; private set; }

        /// <summary>左ペイン：現在のタブの全オブジェクトを選択する。処理はメッシュリスト側と同じ。</summary>
        public Button        SelectAllObjectsBtn      { get; private set; }
        public VisualElement RemoteServerSection    { get; private set; }
        public Button        RemoteServerBtn        { get; private set; }
        public VisualElement LogSection             { get; private set; }
        public Button        LogBtn                 { get; private set; }

        /// <summary>右ペイン：エクスポートセクション（ScrollView内）。</summary>
        public VisualElement ExportSection { get; private set; }

        /// <summary>左ペイン：PMXフルエクスポートボタン。</summary>
        public Button FullExportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQOフルエクスポートボタン。</summary>
        public Button FullExportMqoBtn { get; private set; }

        /// <summary>左ペイン：VRM 1.0 フルエクスポートボタン。
        /// 実装は PolyLing.Vrm10 アセンブリ側にあり、VRM パッケージが無い環境では
        /// パネルが「利用できません」を表示する（規約は IVrm10Exporter.cs を正典とする）。</summary>
        public Button FullExportVrmBtn { get; private set; }

        /// <summary>右ペイン：プロジェクト保存 / 読込セクション（ScrollView内）。
        /// 押し間違いでデータを壊さないよう、保存と読込は別セクションに分けている。</summary>
        public VisualElement ProjectSaveSection { get; private set; }
        public VisualElement ProjectLoadSection { get; private set; }

        /// <summary>左ペイン：プロジェクト保存 / 読込ボタン（それぞれ別セクションを開く）。</summary>
        public Button ProjectSaveBtn { get; private set; }
        public Button ProjectLoadBtn { get; private set; }

        /// <summary>左ペイン：OBJ 読み込み / 保存ボタン（プロジェクトの各ボタンの横）。
        /// インポータ / エクスポータのセクションを OBJ モードで開く。</summary>
        public Button ObjLoadBtn { get; private set; }
        public Button ObjSaveBtn { get; private set; }

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

        /// <summary>描画オブジェクト単位の種別変換セクション。</summary>
        public VisualElement SkinKindSection { get; private set; }

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

            SideFlipBtn    = MakeFlipBtn("反転");
            TiltToggleSide = MakeTiltToggle("斜め45");
            _splitPerspSide.Add(BuildViewportPane("Right", out sidePanel, out var sideLbl, MakeHeaderRow(TiltToggleSide, SideFlipBtn)));
            SidePanel = sidePanel; SideViewLabel = sideLbl;

            _splitTopFront = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            _splitTopFront.style.flexGrow = 1;
            _splitCenter.Add(_splitTopFront);

            TopFlipBtn = MakeFlipBtn("反転");
            var topWrap = BuildViewportPane("TOP", out topPanel, out var topLbl, TopFlipBtn);
            TopViewLabel = topLbl;
            _splitTopFront.Add(topWrap); TopPanel = topPanel;
            _topPane = topWrap;

            FrontFlipBtn    = MakeFlipBtn("反転");
            TiltToggleFront = MakeTiltToggle("斜め45");
            _splitTopFront.Add(BuildViewportPane("Front", out frontPanel, out var frontLbl, MakeHeaderRow(TiltToggleFront, FrontFlipBtn)));
            FrontPanel = frontPanel; FrontViewLabel = frontLbl;

            // 4ビューポートを含む中央領域（キャプチャの切り出し範囲）。
            ViewportArea = _splitCenter;

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
            // 仕切りを潰した側は 0 になるため、そのまま保存対象外になる。
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
            listBtnRow.style.flexDirection = FlexDirection.Column;
            listBtnRow.style.marginTop     = 4;
            ModelListBtn = MakeBtn("モデルリスト");
            MeshListBtn  = MakeBtn("オブジェクトリスト");
            MaterialListBtn = MakeBtn("マテリアル（質感・色）");
            listBtnRow.Add(ModelListBtn);
            listBtnRow.Add(MeshListBtn);
            listBtnRow.Add(MaterialListBtn);
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

            // 辺／面／線分を選んだとき、その構成頂点も頂点選択へ入れるか（種別ごと）。
            // 既定は 3 つとも ON（従来どおり展開する）。
            scroll.Add(Header("選んだ要素の頂点も選択する"));
            var selExpandRow = new VisualElement();
            selExpandRow.style.flexDirection = FlexDirection.Row;
            selExpandRow.style.flexWrap      = Wrap.Wrap;   // 収まらない場合は折り返して見切れを防ぐ
            selExpandRow.style.marginBottom  = 4;
            SelExpandEdgeToVertexToggle = new Toggle("辺→頂点")   { value = true };
            SelExpandFaceToVertexToggle = new Toggle("面→頂点")   { value = true };
            SelExpandLineToVertexToggle = new Toggle("線分→頂点") { value = true };
            foreach (var t in new[] { SelExpandEdgeToVertexToggle,
                                      SelExpandFaceToVertexToggle,
                                      SelExpandLineToVertexToggle })
            {
                t.style.color      = new StyleColor(Color.white);
                t.style.flexGrow   = 0;
                t.style.flexShrink = 0;
                t.style.marginRight = 12;
                // 選択モードのトグルと同じ詰め方（既定の広い label min-width を解除する）。
                if (t.labelElement != null)
                {
                    t.labelElement.style.minWidth    = 0;
                    t.labelElement.style.flexGrow    = 0;
                    t.labelElement.style.marginRight = 3;
                }
                selExpandRow.Add(t);
            }
            scroll.Add(selExpandRow);

            LassoToggle = new Toggle("Lasso Select") { value = false };
            LassoToggle.style.marginBottom = 4;
            scroll.Add(LassoToggle);

            // 性能ログ（CSV）。長時間の操作で次第に重くなる現象を追うための数値記録。
            // ON の間だけ一定間隔で 1 行ずつ追記する。テキストログ（右ペインの「ログ」）
            // とは別系統で、上限行数で捨てられないため長期の傾きが残る。
            PerfLogToggle = new Toggle("性能ログを記録（CSV）") { value = false };
            PerfLogToggle.style.color        = new StyleColor(Color.white);
            PerfLogToggle.style.marginBottom = 4;
            if (PerfLogToggle.labelElement != null)
            {
                PerfLogToggle.labelElement.style.minWidth    = 0;
                PerfLogToggle.labelElement.style.flexGrow    = 0;
                PerfLogToggle.labelElement.style.marginRight = 3;
            }
            scroll.Add(PerfLogToggle);

            // 軌道回転の中心（既定＝ローカル原点）。Lasso Select の直下に置く。
            OrbitAroundLocalOriginToggle = new Toggle("回転はローカル原点中心") { value = true };
            OrbitAroundLocalOriginToggle.style.color        = new StyleColor(Color.white);
            OrbitAroundLocalOriginToggle.style.marginBottom = 2;
            // 既定の広い label min-width を解除し、ラベルとチェックの間隔を詰める
            // （選択モードのトグル群と同じ処理）。
            if (OrbitAroundLocalOriginToggle.labelElement != null)
            {
                OrbitAroundLocalOriginToggle.labelElement.style.minWidth    = 0;
                OrbitAroundLocalOriginToggle.labelElement.style.flexGrow    = 0;
                OrbitAroundLocalOriginToggle.labelElement.style.marginRight = 3;
            }
            scroll.Add(OrbitAroundLocalOriginToggle);

            // 押した時点の選択重心を回転中心として固定する（スナップショット）。
            OrbitCenterToSelectionBtn = MakeBtn("現在の選択を中心に");
            OrbitCenterToSelectionBtn.style.marginBottom = 4;
            scroll.Add(OrbitCenterToSelectionBtn);

            // 法線の自動計算（既定 OFF）と手動再計算。対象はどちらも選択メッシュ。
            var normalRecalcRow = new VisualElement();
            normalRecalcRow.style.flexDirection = FlexDirection.Row;
            normalRecalcRow.style.alignItems    = Align.Center;
            normalRecalcRow.style.marginBottom  = 4;

            AutoRecalcNormalsToggle = new Toggle("法線自動計算") { value = false };
            AutoRecalcNormalsToggle.style.color       = new StyleColor(Color.white);
            AutoRecalcNormalsToggle.style.flexGrow    = 0;
            AutoRecalcNormalsToggle.style.flexShrink  = 0;
            AutoRecalcNormalsToggle.style.marginRight = 8;
            // 既定の広い label min-width を解除し、ラベルとチェックの間隔を詰める
            // （選択モードのトグル群と同じ処理）。
            if (AutoRecalcNormalsToggle.labelElement != null)
            {
                AutoRecalcNormalsToggle.labelElement.style.minWidth    = 0;
                AutoRecalcNormalsToggle.labelElement.style.flexGrow    = 0;
                AutoRecalcNormalsToggle.labelElement.style.marginRight = 3;
            }
            normalRecalcRow.Add(AutoRecalcNormalsToggle);

            RecalcNormalsBtn = MakeBtn("再計算");
            RecalcNormalsBtn.style.flexGrow   = 0;
            RecalcNormalsBtn.style.flexShrink = 0;
            normalRecalcRow.Add(RecalcNormalsBtn);

            scroll.Add(normalRecalcRow);

            // 現在のタブの全オブジェクトを選択する。
            // メッシュリスト内の同名ボタンと同じ処理を呼ぶだけで、判定は増やさない。
            SelectAllObjectsBtn = MakeBtn("すべてのオブジェクトを選択");
            SelectAllObjectsBtn.style.marginBottom = 4;
            scroll.Add(SelectAllObjectsBtn);

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

            // 読み込み系 → 保存系 → 部分（折りたたみ）の順に並べる。
            // ボタンのインスタンスと代入先プロパティは変えないので core 側の結線は不変。

            // ── 読み込み ──
            // プロジェクト読み込みの横に OBJ 読み込みを並べる。
            var projectLoadRow = new VisualElement();
            projectLoadRow.style.flexDirection = FlexDirection.Row;
            projectLoadRow.style.marginBottom  = 2;
            ProjectLoadBtn = MakeBtn("プロジェクト読み込み");
            ProjectLoadBtn.style.flexGrow    = 1;
            ProjectLoadBtn.style.marginRight = 2;
            ObjLoadBtn = MakeBtn(".OBJファイル読込");
            ObjLoadBtn.style.flexGrow = 1;
            projectLoadRow.Add(ProjectLoadBtn);
            projectLoadRow.Add(ObjLoadBtn);
            foFile.Add(projectLoadRow);

            // PMX読み込み / MQO読み込み（PlayerLocalLoader.BuildUI が中身を作る）。
            foFile.Add(LocalLoaderSection);

            foFile.Add(Separator());

            // ── 保存 ──
            // プロジェクト保存の横に OBJ 保存を並べる。
            var projectSaveRow = new VisualElement();
            projectSaveRow.style.flexDirection = FlexDirection.Row;
            projectSaveRow.style.marginBottom  = 2;
            ProjectSaveBtn = MakeBtn("プロジェクト保存");
            ProjectSaveBtn.style.flexGrow    = 1;
            ProjectSaveBtn.style.marginRight = 2;
            ObjSaveBtn = MakeBtn(".OBJファイル保存");
            ObjSaveBtn.style.flexGrow = 1;
            projectSaveRow.Add(ProjectSaveBtn);
            projectSaveRow.Add(ObjSaveBtn);
            foFile.Add(projectSaveRow);

            var fullExportRow = new VisualElement();
            fullExportRow.style.flexDirection = FlexDirection.Row;
            fullExportRow.style.marginBottom  = 2;
            FullExportPmxBtn = MakeBtn("PMX保存"); FullExportPmxBtn.style.flexGrow = 1; FullExportPmxBtn.style.marginRight = 2;
            FullExportMqoBtn = MakeBtn("MQO保存"); FullExportMqoBtn.style.flexGrow = 1; FullExportMqoBtn.style.marginRight = 2;
            FullExportVrmBtn = MakeBtn("VRM保存"); FullExportVrmBtn.style.flexGrow = 1;
            fullExportRow.Add(FullExportPmxBtn); fullExportRow.Add(FullExportMqoBtn); fullExportRow.Add(FullExportVrmBtn);
            foFile.Add(fullExportRow);

            foFile.Add(Separator());

            // ── 部分インポート／エクスポート（既定 折りたたみ） ──
            var foFilePartial = MakeFoldout("部分インポートエクスポート", "FilePartial");

            var pImportRow = new VisualElement();
            pImportRow.style.flexDirection = FlexDirection.Row;
            pImportRow.style.marginBottom  = 2;
            PartialImportPmxBtn = MakeBtn("PMX部分import"); PartialImportPmxBtn.style.flexGrow = 1; PartialImportPmxBtn.style.marginRight = 2;
            PartialImportMqoBtn = MakeBtn("MQO部分import"); PartialImportMqoBtn.style.flexGrow = 1;
            pImportRow.Add(PartialImportPmxBtn); pImportRow.Add(PartialImportMqoBtn);
            foFilePartial.Add(pImportRow);

            var pExportRow = new VisualElement();
            pExportRow.style.flexDirection = FlexDirection.Row;
            pExportRow.style.marginBottom  = 2;
            PartialExportPmxBtn = MakeBtn("PMX部分export"); PartialExportPmxBtn.style.flexGrow = 1; PartialExportPmxBtn.style.marginRight = 2;
            PartialExportMqoBtn = MakeBtn("MQO部分export"); PartialExportMqoBtn.style.flexGrow = 1;
            pExportRow.Add(PartialExportPmxBtn); pExportRow.Add(PartialExportMqoBtn);
            foFilePartial.Add(pExportRow);

            foFile.Add(foFilePartial);


            // ── 図形生成 ───────────────────────────────────────────────
            var foPrimitive = MakeFoldout("図形生成", "Primitive");

            // 「基本図形」「高度な図形」ボタンは廃止した。PrimitiveSection 自体は
            // ショートカット（ShowPrimitiveShape）と「穴つなぎ」ボタンから開くため残す。

            // メイン3Dウインドウ連携版の入口。歪み複製も高度側に並ぶ
            // （PlayerPrimitiveMeshSubPanel.ObjectArray.cs）。
            LivePrimitiveBtn = MakeBtn("基本図形（3D連携）");
            foPrimitive.Add(LivePrimitiveBtn);

            LiveAdvancedPrimitiveBtn = MakeBtn("高度な図形（3D連携）");
            foPrimitive.Add(LiveAdvancedPrimitiveBtn);

            // 配置ギズモのサブモード切替ボタンは
            // PlayerPrimitiveMeshSubPanel（3D連携インスタンス）の中へ移設済み。

            // ── 選択・移動 ─────────────────────────────────────────────
            var foSelectMove = MakeFoldout("選択・移動/回転/拡大縮小", "SelectMove");

            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;
            toolRow.style.marginBottom  = 2;
            ToolVertexMoveBtn  = MakeBtn("頂点移動");     ToolVertexMoveBtn.style.flexGrow  = 1; ToolVertexMoveBtn.style.marginRight  = 2;
            ToolObjectMoveBtn  = MakeBtn("描画オブジェクトの姿勢"); ToolObjectMoveBtn.style.flexGrow  = 1;
            toolRow.Add(ToolVertexMoveBtn); toolRow.Add(ToolObjectMoveBtn);
            foSelectMove.Add(toolRow);

            var toolRow2 = new VisualElement();
            toolRow2.style.flexDirection = FlexDirection.Row;
            toolRow2.style.marginBottom  = 2;
            ToolPivotOffsetBtn = MakeBtn("ピボット位置");    ToolPivotOffsetBtn.style.flexGrow = 1; ToolPivotOffsetBtn.style.marginRight = 2;
            ToolSculptBtn      = MakeBtn("スカルプト");  ToolSculptBtn.style.flexGrow      = 1; ToolSculptBtn.style.marginRight      = 2;
            ToolAdvancedSelBtn = MakeBtn("詳細選択");    ToolAdvancedSelBtn.style.flexGrow = 1;
            toolRow2.Add(ToolPivotOffsetBtn); toolRow2.Add(ToolSculptBtn); toolRow2.Add(ToolAdvancedSelBtn);
            foSelectMove.Add(toolRow2);

            var rowRotScale = new VisualElement(); rowRotScale.style.flexDirection = FlexDirection.Row; rowRotScale.style.marginBottom = 2;
            RotateBtn = MakeBtn("回転");     RotateBtn.style.flexGrow = 1; RotateBtn.style.marginRight = 2;
            ScaleBtn  = MakeBtn("スケール"); ScaleBtn.style.flexGrow  = 1;
            rowRotScale.Add(RotateBtn); rowRotScale.Add(ScaleBtn); foSelectMove.Add(rowRotScale);

            // 作業用ローカル軸。回転 / 曲げの基準フレームを操作するサブツール。
            var rowWorkAxis = new VisualElement(); rowWorkAxis.style.flexDirection = FlexDirection.Row; rowWorkAxis.style.marginBottom = 2;
            WorkAxisBtn = MakeBtn("作業軸"); WorkAxisBtn.style.flexGrow = 1; WorkAxisBtn.style.marginRight = 2;
            DeformBtn   = MakeBtn("変形");   DeformBtn.style.flexGrow   = 1;
            rowWorkAxis.Add(WorkAxisBtn); rowWorkAxis.Add(DeformBtn); foSelectMove.Add(rowWorkAxis);

            // 格子変形。作業軸を格子フレームとして使う。
            var rowLattice = new VisualElement(); rowLattice.style.flexDirection = FlexDirection.Row; rowLattice.style.marginBottom = 2;
            LatticeBtn = MakeBtn("格子変形"); LatticeBtn.style.flexGrow = 1;
            rowLattice.Add(LatticeBtn); foSelectMove.Add(rowLattice);

            // 一時選択サブツール (デバッグ用)。ショートカット R / G と同じ処理を呼ぶ。
            var rowSubTool = new VisualElement(); rowSubTool.style.flexDirection = FlexDirection.Row; rowSubTool.style.marginBottom = 2;
            SubToolBoxSelectBtn   = MakeBtn("矩形選択(一時) R");   SubToolBoxSelectBtn.style.flexGrow   = 1; SubToolBoxSelectBtn.style.marginRight = 2;
            SubToolLassoSelectBtn = MakeBtn("投げ縄選択(一時) G"); SubToolLassoSelectBtn.style.flexGrow = 1;
            rowSubTool.Add(SubToolBoxSelectBtn); rowSubTool.Add(SubToolLassoSelectBtn); foSelectMove.Add(rowSubTool);

            var rowSelSet = new VisualElement(); rowSelSet.style.flexDirection = FlexDirection.Row; rowSelSet.style.marginBottom = 2;
            PartsSelectionSetBtn = MakeBtn("パーツ選択辞書"); PartsSelectionSetBtn.style.flexGrow = 1; PartsSelectionSetBtn.style.marginRight = 2;
            MeshSelectionSetBtn  = MakeBtn("オブジェクト選択辞書"); MeshSelectionSetBtn.style.flexGrow  = 1;
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
            SolidifyBtn = MakeBtn("厚み付け"); SolidifyBtn.style.flexGrow = 1;
            rowExtrude.Add(EdgeExtrudeBtn); rowExtrude.Add(FaceExtrudeBtn); rowExtrude.Add(SolidifyBtn); foTopology.Add(rowExtrude);

            var rowEdgeKnife = new VisualElement(); rowEdgeKnife.style.flexDirection = FlexDirection.Row; rowEdgeKnife.style.marginBottom = 2;
            EdgeTopologyBtn = MakeBtn("辺トポロジー"); EdgeTopologyBtn.style.flexGrow = 1; EdgeTopologyBtn.style.marginRight = 2;
            KnifeBtn        = MakeBtn("ナイフ");       KnifeBtn.style.flexGrow        = 1; KnifeBtn.style.marginRight     = 2;
            VertexHoleBtn   = MakeBtn("穴あけ");       VertexHoleBtn.style.flexGrow   = 1; VertexHoleBtn.style.marginRight = 2;
            BridgeBtn       = MakeBtn("穴つなぎブリッジ");     BridgeBtn.style.flexGrow       = 1;
            rowEdgeKnife.Add(EdgeTopologyBtn); rowEdgeKnife.Add(KnifeBtn); rowEdgeKnife.Add(VertexHoleBtn); rowEdgeKnife.Add(BridgeBtn); foTopology.Add(rowEdgeKnife);

            // 穴頂点数合わせ。ブリッジの「2つの穴の頂点数が同じ」制約を満たすための前処理。
            var rowHoleRing = new VisualElement(); rowHoleRing.style.flexDirection = FlexDirection.Row; rowHoleRing.style.marginBottom = 2;
            HoleRingCountBtn = MakeBtn("穴頂点数合わせ"); HoleRingCountBtn.style.flexGrow = 1; HoleRingCountBtn.style.marginRight = 2;
            // 辺群ブリッジ。穴（閉じた縁）に限らず、拾った 2 か所の辺群の間に面を張る。
            EdgeBridgeBtn    = MakeBtn("辺群ブリッジ");   EdgeBridgeBtn.style.flexGrow    = 1;
            rowHoleRing.Add(HoleRingCountBtn); rowHoleRing.Add(EdgeBridgeBtn); foTopology.Add(rowHoleRing);

            // 削除系。面削除モードは進入中にボタンがハイライトされる
            // (破壊的モードなので表示は必須)。
            var rowDelete = new VisualElement(); rowDelete.style.flexDirection = FlexDirection.Row; rowDelete.style.marginBottom = 2;
            SubToolDeleteBtn  = MakeBtn("選択削除 Del");   SubToolDeleteBtn.style.flexGrow  = 1; SubToolDeleteBtn.style.marginRight = 2;
            ToolDeleteFaceBtn = MakeBtn("面削除モード D"); ToolDeleteFaceBtn.style.flexGrow = 1;
            rowDelete.Add(SubToolDeleteBtn); rowDelete.Add(ToolDeleteFaceBtn); foTopology.Add(rowDelete);

            var rowNormalExclude = new VisualElement(); rowNormalExclude.style.flexDirection = FlexDirection.Row; rowNormalExclude.style.marginBottom = 2;
            NormalEditBtn = MakeBtn("法線編集"); NormalEditBtn.style.flexGrow = 1; NormalEditBtn.style.marginRight = 2;
            NormalExcludeSetBtn = MakeBtn("法線再計算 除外辞書"); NormalExcludeSetBtn.style.flexGrow = 1;
            rowNormalExclude.Add(NormalEditBtn); rowNormalExclude.Add(NormalExcludeSetBtn); foTopology.Add(rowNormalExclude);

            var rowNormalTransplant = new VisualElement(); rowNormalTransplant.style.flexDirection = FlexDirection.Row; rowNormalTransplant.style.marginBottom = 2;
            NormalTransplantBtn = MakeBtn("法線移植"); NormalTransplantBtn.style.flexGrow = 1;
            rowNormalTransplant.Add(NormalTransplantBtn); foTopology.Add(rowNormalTransplant);

            var rowFaceHide = new VisualElement(); rowFaceHide.style.flexDirection = FlexDirection.Row; rowFaceHide.style.marginBottom = 2;
            FaceHideBtn = MakeBtn("面の表示・非表示"); FaceHideBtn.style.flexGrow = 1;
            rowFaceHide.Add(FaceHideBtn); foTopology.Add(rowFaceHide);

            // ── 選択頂点位置 ───────────────────────────────────────────
            var foVertexPos = MakeFoldout("選択頂点位置", "VertexPos");

            var rowAlignPlanarize = new VisualElement(); rowAlignPlanarize.style.flexDirection = FlexDirection.Row; rowAlignPlanarize.style.marginBottom = 2;
            AlignVerticesBtn       = MakeBtn("頂点整列");   AlignVerticesBtn.style.flexGrow       = 1; AlignVerticesBtn.style.marginRight       = 2;
            PlanarizeAlongBonesBtn = MakeBtn("ボーン間平面化"); PlanarizeAlongBonesBtn.style.flexGrow = 1;
            rowAlignPlanarize.Add(AlignVerticesBtn); rowAlignPlanarize.Add(PlanarizeAlongBonesBtn); foVertexPos.Add(rowAlignPlanarize);

            var rowSmoothEdges = new VisualElement(); rowSmoothEdges.style.flexDirection = FlexDirection.Row; rowSmoothEdges.style.marginBottom = 2;
            SmoothEdgesBtn = MakeBtn("辺を滑らかに"); SmoothEdgesBtn.style.flexGrow = 1;
            rowSmoothEdges.Add(SmoothEdgesBtn); foVertexPos.Add(rowSmoothEdges);

            var rowPipeAlign = new VisualElement(); rowPipeAlign.style.flexDirection = FlexDirection.Row; rowPipeAlign.style.marginBottom = 2;
            PipeAlignBtn = MakeBtn("パイプの整列"); PipeAlignBtn.style.flexGrow = 1;
            rowPipeAlign.Add(PipeAlignBtn); foVertexPos.Add(rowPipeAlign);

            var rowSurfaceSnap = new VisualElement(); rowSurfaceSnap.style.flexDirection = FlexDirection.Row; rowSurfaceSnap.style.marginBottom = 2;
            SurfaceSnapBtn = MakeBtn("面に張り付け"); SurfaceSnapBtn.style.flexGrow = 1;
            rowSurfaceSnap.Add(SurfaceSnapBtn); foVertexPos.Add(rowSurfaceSnap);

            var rowPlaceObjectReshape = new VisualElement(); rowPlaceObjectReshape.style.flexDirection = FlexDirection.Row; rowPlaceObjectReshape.style.marginBottom = 2;
            PlaceObjectReshapeBtn = MakeBtn("藤壺の整形"); PlaceObjectReshapeBtn.style.flexGrow = 1;
            rowPlaceObjectReshape.Add(PlaceObjectReshapeBtn); foVertexPos.Add(rowPlaceObjectReshape);

            // ── 選択頂点トポロジー ─────────────────────────────────────
            var foVertexTopo = MakeFoldout("選択頂点トポロジー", "VertexTopo");

            var rowMergeSplit = new VisualElement(); rowMergeSplit.style.flexDirection = FlexDirection.Row; rowMergeSplit.style.marginBottom = 2;
            MergeVerticesBtn = MakeBtn("頂点マージ");  MergeVerticesBtn.style.flexGrow = 1; MergeVerticesBtn.style.marginRight = 2;
            SplitVerticesBtn = MakeBtn("頂点分割");    SplitVerticesBtn.style.flexGrow = 1;
            rowMergeSplit.Add(MergeVerticesBtn); rowMergeSplit.Add(SplitVerticesBtn); foVertexTopo.Add(rowMergeSplit);

            // 頂点IDユーティリティ。モデル間・オブジェクト間の突き合わせに使う ID を
            // 診断・修復する。ID を使う操作の前段に置く。
            var rowVertexId = new VisualElement(); rowVertexId.style.flexDirection = FlexDirection.Row; rowVertexId.style.marginBottom = 2;
            VertexIdBtn = MakeBtn("頂点ID"); VertexIdBtn.style.flexGrow = 1; VertexIdBtn.style.marginRight = 2;
            VertexTransferBtn = MakeBtn("頂点データ転送"); VertexTransferBtn.style.flexGrow = 1;
            rowVertexId.Add(VertexIdBtn); rowVertexId.Add(VertexTransferBtn); foVertexTopo.Add(rowVertexId);

            // パーツID / サブID の採番。頂点IDとは独立して掛ける。
            var rowPartsId = new VisualElement(); rowPartsId.style.flexDirection = FlexDirection.Row; rowPartsId.style.marginBottom = 2;
            PartsIdBtn = MakeBtn("パーツID / サブID"); PartsIdBtn.style.flexGrow = 1;
            rowPartsId.Add(PartsIdBtn); foVertexTopo.Add(rowPartsId);

            var rowQuad = new VisualElement(); rowQuad.style.flexDirection = FlexDirection.Row; rowQuad.style.marginBottom = 2;
            QuadDecimatorBtn = MakeBtn("Yet(Quad減面)"); QuadDecimatorBtn.style.flexGrow = 1;
            rowQuad.Add(QuadDecimatorBtn); foVertexTopo.Add(rowQuad);

            // ── ボーン・モーフ ─────────────────────────────────────────
            var foBoneMorph = MakeFoldout("ボーン・モーフ", "BoneMorph");

            MeshFilterToSkinnedBtn = MakeBtn("メッシュからボーンとスキンの生成");
            foBoneMorph.Add(MeshFilterToSkinnedBtn);

            SkinKindBtn = MakeBtn("描画オブジェクトの種別変換");
            foBoneMorph.Add(SkinKindBtn);

            BoneEditorBtn = MakeBtn("ボーンエディタ");
            foBoneMorph.Add(BoneEditorBtn);

            var rowTPoseHuman = new VisualElement(); rowTPoseHuman.style.flexDirection = FlexDirection.Row; rowTPoseHuman.style.marginBottom = 2;
            HumanoidMappingBtn = MakeBtn("アバター用ヒューマンマッピング"); HumanoidMappingBtn.style.flexGrow = 1; HumanoidMappingBtn.style.marginRight = 2;
            TPoseBtn          = MakeBtn("Tポーズ変換");   TPoseBtn.style.flexGrow          = 1;
            rowTPoseHuman.Add(HumanoidMappingBtn); rowTPoseHuman.Add(TPoseBtn); foBoneMorph.Add(rowTPoseHuman);

            var rowBlend = new VisualElement(); rowBlend.style.flexDirection = FlexDirection.Row; rowBlend.style.marginBottom = 2;
            BlendBtn      = MakeBtn("メッシュブレンド"); BlendBtn.style.flexGrow      = 1; BlendBtn.style.marginRight      = 2;
            ModelBlendBtn = MakeBtn("モデルブレンド");   ModelBlendBtn.style.flexGrow = 1;
            rowBlend.Add(BlendBtn); rowBlend.Add(ModelBlendBtn); foBoneMorph.Add(rowBlend);

            var rowShrink = new VisualElement(); rowShrink.style.flexDirection = FlexDirection.Row; rowShrink.style.marginBottom = 2;
            ShrinkBtn     = MakeBtn("シュリンカー(頂点)"); ShrinkBtn.style.flexGrow     = 1; ShrinkBtn.style.marginRight = 2;
            ShrinkFaceBtn = MakeBtn("シュリンカー(面)");   ShrinkFaceBtn.style.flexGrow = 1;
            rowShrink.Add(ShrinkBtn); rowShrink.Add(ShrinkFaceBtn); foBoneMorph.Add(rowShrink);

            ThinPlateMorphBtn = MakeBtn("TPSモーフ"); foBoneMorph.Add(ThinPlateMorphBtn);

            MorphCreateBtn = MakeBtn("モーフ生成・差分から");         foBoneMorph.Add(MorphCreateBtn);
            MorphBtn       = MakeBtn("モーフエクスプレッション編集"); foBoneMorph.Add(MorphBtn);

            ToolSkinWeightPaintBtn = MakeBtn("スキンWペイント");
            foBoneMorph.Add(ToolSkinWeightPaintBtn);

            SkinWeightNumericBtn = MakeBtn("スキンW数値設定");
            foBoneMorph.Add(SkinWeightNumericBtn);

            // ── UV・マテリアル ─────────────────────────────────────────
            var foUvMat = MakeFoldout("UV・マテリアル", "UvMat");

            var rowUv = new VisualElement(); rowUv.style.flexDirection = FlexDirection.Row; rowUv.style.marginBottom = 2;
            UVEditorBtn = MakeBtn("UVエディタ"); UVEditorBtn.style.flexGrow = 1; UVEditorBtn.style.marginRight = 2;
            UVUnwrapBtn = MakeBtn("UV展開");     UVUnwrapBtn.style.flexGrow = 1; UVUnwrapBtn.style.marginRight = 2;
            UVZBtn      = MakeBtn("UVZ");        UVZBtn.style.flexGrow      = 1;
            rowUv.Add(UVEditorBtn); rowUv.Add(UVUnwrapBtn); rowUv.Add(UVZBtn); foUvMat.Add(rowUv);

            MergeMeshesBtn  = MakeBtn("メッシュマージ");   foUvMat.Add(MergeMeshesBtn);
            BooleanBtn      = MakeBtn("ブーリアン");       foUvMat.Add(BooleanBtn);

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
            MotionClipTestBtn = MakeBtn("Yet（統合モーション)"); MotionClipTestBtn.style.flexGrow = 1;
            rowMisc2.Add(UnityClipTestBtn); rowMisc2.Add(MotionClipTestBtn); foOther.Add(rowMisc2);

            var rowMisc3 = new VisualElement(); rowMisc3.style.flexDirection = FlexDirection.Row; rowMisc3.style.marginBottom = 2;
            UnderlayBtn = MakeBtn("下絵");        UnderlayBtn.style.flexGrow = 1; UnderlayBtn.style.marginRight = 2;
            GridAxisBtn = MakeBtn("軸/グリッド"); GridAxisBtn.style.flexGrow = 1; GridAxisBtn.style.marginRight = 2;
            LogBtn      = MakeBtn("ログ");        LogBtn.style.flexGrow      = 1;
            rowMisc3.Add(UnderlayBtn); rowMisc3.Add(GridAxisBtn); rowMisc3.Add(LogBtn); foOther.Add(rowMisc3);

            var rowMisc4 = new VisualElement(); rowMisc4.style.flexDirection = FlexDirection.Row; rowMisc4.style.marginBottom = 2;
            CameraBtn  = MakeBtn("カメラ調整"); CameraBtn.style.flexGrow  = 1; CameraBtn.style.marginRight = 2;
            CaptureBtn = MakeBtn("キャプチャ"); CaptureBtn.style.flexGrow = 1;
            rowMisc4.Add(CameraBtn); rowMisc4.Add(CaptureBtn); foOther.Add(rowMisc4);

            // 一時ミラー（旧「ミラー編集」）。
            // 作業中だけ反対側の実体を生やす一時的な機能であり、ボーン・モーフの編集
            // 機能ではないため「その他」に置く。各ツール内の「一時ミラー」ボタンは
            // このパネルで指定したパラメータ（TempMirrorSettings）を使う。
            MirrorBtn = MakeBtn("一時ミラー"); foOther.Add(MirrorBtn);

            // ── 結合 ───────────────────────────────────────────────────
            var foMerge = MakeFoldout("結合", "Merge");

            var rowMerge = new VisualElement(); rowMerge.style.flexDirection = FlexDirection.Row; rowMerge.style.marginBottom = 2;
            VertexDissolveBtn = MakeBtn("頂点溶解"); VertexDissolveBtn.style.flexGrow = 1; VertexDissolveBtn.style.marginRight = 2;
            Tri4To1Btn        = MakeBtn("三角4→1"); Tri4To1Btn.style.flexGrow        = 1; Tri4To1Btn.style.marginRight        = 2;
            FaceMergeBtn      = MakeBtn("面結合");   FaceMergeBtn.style.flexGrow      = 1;
            rowMerge.Add(VertexDissolveBtn); rowMerge.Add(Tri4To1Btn); rowMerge.Add(FaceMergeBtn); foMerge.Add(rowMerge);

            var rowMerge2 = new VisualElement(); rowMerge2.style.flexDirection = FlexDirection.Row; rowMerge2.style.marginBottom = 2;
            Quad4To1Btn          = MakeBtn("四角4→1");   Quad4To1Btn.style.flexGrow          = 1; Quad4To1Btn.style.marginRight = 2;
            FaceMergeCollapseBtn = MakeBtn("面結合(頂点削除)"); FaceMergeCollapseBtn.style.flexGrow = 1;
            rowMerge2.Add(Quad4To1Btn); rowMerge2.Add(FaceMergeCollapseBtn); foMerge.Add(rowMerge2);

            // ── システムデバッグ ───────────────────────────────────────
            // 自動検証の入口。通常の編集操作ではないので独立させる。
            var foSysDebug = MakeFoldout("システムデバッグ", "SysDebug");

            var rowSysDebug = new VisualElement(); rowSysDebug.style.flexDirection = FlexDirection.Row; rowSysDebug.style.marginBottom = 2;
            PipelineTestBtn = MakeBtn("パイプライン自動検証"); PipelineTestBtn.style.flexGrow = 1;
            PipelineTestBtn.style.marginRight = 2;
            OriginTestBtn = MakeBtn("原点CSV自動検証"); OriginTestBtn.style.flexGrow = 1;
            rowSysDebug.Add(PipelineTestBtn); rowSysDebug.Add(OriginTestBtn); foSysDebug.Add(rowSysDebug);

            var rowSysDebug2 = new VisualElement(); rowSysDebug2.style.flexDirection = FlexDirection.Row; rowSysDebug2.style.marginBottom = 2;
            SkinTestBtn = MakeBtn("スキン生成自動検証"); SkinTestBtn.style.flexGrow = 1;
            SkinTestBtn.style.marginRight = 2;
            SpringBoneTestBtn = MakeBtn("スプリングボーン検証"); SpringBoneTestBtn.style.flexGrow = 1;
            rowSysDebug2.Add(SkinTestBtn); rowSysDebug2.Add(SpringBoneTestBtn); foSysDebug.Add(rowSysDebug2);

            var rowSysDebug3 = new VisualElement(); rowSysDebug3.style.flexDirection = FlexDirection.Row; rowSysDebug3.style.marginBottom = 2;
            RobotBuildTestBtn = MakeBtn("ロボ組み立て自動検証"); RobotBuildTestBtn.style.flexGrow = 1;
            rowSysDebug3.Add(RobotBuildTestBtn); foSysDebug.Add(rowSysDebug3);

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
            scroll.Add(foMerge);
            scroll.Add(foSysDebug);

            scroll.Add(Separator());

            // 中央4画面の仕切り再配置（押下した瞬間に1回だけ実行）
            scroll.Add(Header("画面分割"));
            scroll.Add(BuildSplitModeGrid());

            scroll.Add(Header("Display (P/T/F/S)"));

            // 4ビューポート × VD_COUNT 項目のグリッド
            // 列: P=Perspective(slot0), T=Top(slot1), F=Front(slot2), S=Side(slot3)
            // 行: VD_* 定数の順序と一致させること。
            var vpHeaders  = new string[] { "P", "T", "F", "S" };
            var itemLabels = new string[]
            {
                "カリング",
                "選択Mesh",  "非選Mesh", "ミラー",
                "ミラー面",  "ミラー辺", "ミラー頂点",
                "選択辺",    "非選辺",
                "選択頂点",  "非選頂点",
                "選択Bone",  "非選Bone",
                "選択M原点", "非選M原点", "ミラーM原点",
                "法線",
            };
            // ViewportDisplaySettings.Default と一致させる
            var itemDefaults = new bool[]
            {
                true,  // カリング
                true,  // 選択Mesh
                true,  // 非選Mesh
                true,  // ミラー
                true,  // ミラー面
                true,  // ミラー辺
                true,  // ミラー頂点
                true,  // 選択辺
                true,  // 非選辺
                true,  // 選択頂点
                true,  // 非選頂点
                true,  // 選択Bone
                false, // 非選Bone
                true,  // 選択M原点
                true,  // 非選M原点
                false, // ミラーM原点（実体側と重なるため既定 OFF）
                false, // 法線（線分数が多いため既定 OFF）
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

        private static Toggle MakeTiltToggle(string label)
        {
            var t = new Toggle(label) { value = false };
            t.style.fontSize     = 10;
            t.style.marginTop    = 0;
            t.style.marginBottom = 0;
            t.style.marginRight  = 4;
            return t;
        }

        private static VisualElement MakeHeaderRow(params VisualElement[] children)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            foreach (var c in children) if (c != null) row.Add(c);
            return row;
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

            // ── スキンウェイト数値設定セクション
            SkinWeightNumericSection = AddSection(visible: false);

            // ── ブレンドセクション
            BlendSection = AddSection(visible: false);

            // ── モデルブレンドセクション
            ModelBlendSection = AddSection(visible: false);

            // ── シュリンカー(頂点)セクション
            ShrinkSection = AddSection(visible: false);

            // ── シュリンカー(面)セクション
            ShrinkFaceSection = AddSection(visible: false);

            // ── TPSモーフセクション
            ThinPlateMorphSection = AddSection(visible: false);

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
            NormalExcludeSetSection    = AddSection(visible: false);
            NormalEditSection          = AddSection(visible: false);
            NormalTransplantSection    = AddSection(visible: false);
            FaceHideSection            = AddSection(visible: false);
            MergeMeshesSection         = AddSection(visible: false);
            BooleanSection             = AddSection(visible: false);
            MorphSection               = AddSection(visible: false);
            MorphCreateSection         = AddSection(visible: false);
            TPoseSection               = AddSection(visible: false);
            HumanoidMappingSection     = AddSection(visible: false);
            MirrorSection              = AddSection(visible: false);
            QuadDecimatorSection       = AddSection(visible: false);
            AlignVerticesSection       = AddSection(visible: false);
            PlanarizeAlongBonesSection = AddSection(visible: false);
            SmoothEdgesSection         = AddSection(visible: false);
            PipeAlignSection           = AddSection(visible: false);
            SurfaceSnapSection         = AddSection(visible: false);
            PlaceObjectReshapeSection  = AddSection(visible: false);
            MergeVerticesSection       = AddSection(visible: false);
            SplitVerticesSection       = AddSection(visible: false);
            VertexHoleSection          = AddSection(visible: false);
            VertexDissolveSection      = AddSection(visible: false);
            HoleRingCountSection       = AddSection(visible: false);
            EdgeBridgeSection          = AddSection(visible: false);
            Tri4To1Section             = AddSection(visible: false);
            FaceMergeSection           = AddSection(visible: false);
            Quad4To1Section            = AddSection(visible: false);
            FaceMergeCollapseSection   = AddSection(visible: false);
            VertexIdSection            = AddSection(visible: false);
            VertexTransferSection      = AddSection(visible: false);
            PartsIdSection             = AddSection(visible: false);
            AddFaceSection             = AddSection(visible: false);
            FlipFaceSection            = AddSection(visible: false);
            RotateSection              = AddSection(visible: false);
            WorkAxisSection            = AddSection(visible: false);
            DeformSection              = AddSection(visible: false);
            LatticeSection             = AddSection(visible: false);
            ScaleSection               = AddSection(visible: false);
            EdgeBevelSection           = AddSection(visible: false);
            EdgeExtrudeSection         = AddSection(visible: false);
            FaceExtrudeSection         = AddSection(visible: false);
            EdgeTopologySection        = AddSection(visible: false);
            KnifeSection               = AddSection(visible: false);
            SolidifySection            = AddSection(visible: false);
            MediaPipeSection           = AddSection(visible: false);
            VMDTestSection             = AddSection(visible: false);
            UnityClipTestSection       = AddSection(visible: false);
            MotionClipTestSection      = AddSection(visible: false);
            PipelineTestSection        = AddSection(visible: false);
            OriginTestSection          = AddSection(visible: false);
            SkinTestSection            = AddSection(visible: false);
            SpringBoneTestSection      = AddSection(visible: false);
            RobotBuildTestSection      = AddSection(visible: false);
            UnderlaySection            = AddSection(visible: false);
            GridAxisSection            = AddSection(visible: false);
            CameraSection              = AddSection(visible: false);
            CaptureSection             = AddSection(visible: false);
            RemoteServerSection        = AddSection(visible: false);
            LogSection                 = AddSection(visible: false);

            // ── エクスポートセクション
            ExportSection = AddSection(visible: false);

            // ── プロジェクト保存 / 読込セクション（別々に持つ）
            ProjectSaveSection = AddSection(visible: false);
            ProjectLoadSection = AddSection(visible: false);

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

            // ── 新図形生成セクション（検証用の2つ目のインスタンス）
            LivePrimitiveSection = AddSection(visible: false);

            // ── MeshFilter→Skinnedセクション（ScrollView内へ移動）
            MeshFilterToSkinnedSection = AddSection(visible: false);

            // ── 描画オブジェクト単位の種別変換セクション
            SkinKindSection = AddSection(visible: false);

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

        // ================================================================
        // ボタン色の共通定数
        //
        // ApplyDarkTheme は全 Button へ color = 白 / backgroundColor = 暗灰 を
        // 「インラインスタイル」で設定する。UIToolkit ではインラインが StyleSheet より
        // 優先されるため、非 active に戻すときに StyleColor(StyleKeyword.Null) を入れると
        // インライン背景だけが外れて USS 既定の明るい灰色になり、白のままの文字色と
        // 相まって「白地に白文字」になる。
        // 非 active へ戻すときは Null ではなく BtnInactiveColor を明示すること。
        //
        // 各パネルが独自に色定数を持つと同じ不具合が再発するため、ApplyDarkTheme と
        // 同じ場所に置いて全パネルから参照させる。
        // ================================================================

        /// <summary>非 active なボタンの背景色（ApplyDarkTheme が入れる値と同じ）。</summary>
        public static readonly StyleColor BtnInactiveColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));

        /// <summary>active なボタンの背景色（青）。</summary>
        public static readonly StyleColor BtnActiveColor   = new StyleColor(new Color(0.3f, 0.5f, 1.0f));

        /// <summary>
        /// VisualElement サブツリー全体にダークテーマを適用する。
        /// Build 後に動的再構築するコンテナに対しても呼び出すこと。
        ///
        /// 【重要: コントロールの型を列挙しないこと】
        /// 旧実装は Query&lt;TextField&gt; / Query&lt;FloatField&gt; / Query&lt;DropdownField&gt; …
        /// のように型を並べて塗っていた。この方式は新しいコントロールを使うたびに
        /// 塗り漏れ（白背景に白文字で読めない）が発生し、実際に EnumField・SliderInt・
        /// Foldout・RadioButtonGroup・ListView・TreeView が長く塗られないままだった。
        ///
        /// そのため現在は型ではなく「UIToolkit が部品へ付ける USS クラス名」で拾う。
        ///   ・文字色  … TextElement 一本（Label / Button の文字 / ポップアップの
        ///                表示文字はすべて TextElement の派生）
        ///   ・入力部  … unity-base-text-field__input / unity-base-popup-field__input 等
        /// これにより、今後どの型を追加しても自動的に塗られる。
        /// 型名を書き足す修正をしたくなったら、それは設計を戻す変更なので避けること。
        ///
        /// 【個々のパネルで色を指定しないこと】
        /// パネル側で style.color を書くと、ここでの一括指定と競合して読めない配色になる。
        /// 配色の決定はこの関数に集約する。
        /// </summary>
        public static void ApplyDarkTheme(UnityEngine.UIElements.VisualElement root)
        {
            if (root == null) return;
            var white   = new StyleColor(Color.white);
            var btnBg   = BtnInactiveColor;
            var fieldBg = new StyleColor(new Color(0.20f, 0.20f, 0.20f));
            var hbBg    = new StyleColor(new Color(0.18f, 0.18f, 0.22f));

            // ── 文字色（全コントロール共通） ─────────────────────────────
            // Label・Button の文字・DropdownField / EnumField の表示文字・
            // Foldout の見出しなど、テキストを持つ要素はすべて TextElement の派生。
            root.Query<TextElement>().ForEach(t => t.style.color = white);

            // ── ボタン ───────────────────────────────────────────────────
            // Button は BaseField ではないので背景を個別に指定する。
            root.Query<Button>().ForEach(b =>
            {
                b.style.color = white;
                b.style.backgroundColor = btnBg;
            });

            // ── フィールド本体の背景・文字色 ─────────────────────────────
            // BaseField<T> 派生はすべて unity-base-field を持つ。型を問わない。
            root.Query<VisualElement>(className: "unity-base-field").ForEach(f =>
            {
                f.style.color = white;
            });

            // ── 入力部の背景 ─────────────────────────────────────────────
            // 型ではなく部品クラス名で拾う。派生クラスが基底名を持つ場合と
            // 自分の名前しか持たない場合の両方に備えて候補を並べる。
            PaintInputParts(root, fieldBg, white,
                "unity-base-text-field__input",
                "unity-base-popup-field__input",
                "unity-popup-field__input",
                "unity-enum-field__input");

            // ── HelpBox ──────────────────────────────────────────────────
            root.Query<HelpBox>().ForEach(h =>
            {
                h.style.color = white;
                h.style.backgroundColor = hbBg;
            });

            // ── チェックマーク（Toggle / RadioButton） ───────────────────
            root.Query<VisualElement>(className: "unity-toggle__checkmark").ForEach(e =>
                e.style.backgroundColor = white);

            // ── スライダの溝（Slider / SliderInt / MinMaxSlider 共通） ───
            root.Query<VisualElement>(className: "unity-base-slider__tracker").ForEach(e =>
                e.style.backgroundColor = fieldBg);

            // ── 数値欄は確定時にだけ通知させる ───────────────────────────
            // 既定では 1 文字打つたびに ChangeEvent が飛び、入力途中の値
            // （"90" と打つ途中の "9"）で操作が走ってしまう。
            //
            // ここだけ型名が出るのは配色の話ではないため。isDelayed は
            // TextInputBaseField 派生にしか無いプロパティで、部品クラス名からは
            // 触れない。上の配色部分に型名を書き足してはならない。
            root.Query<FloatField>().ForEach(f   => f.isDelayed = true);
            root.Query<IntegerField>().ForEach(f => f.isDelayed = true);
        }

        /// <summary>
        /// 入力部（テキスト欄・ポップアップの表示部）へ背景色と文字色を塗る。
        /// クラス名の候補を順に走査するので、コントロールの型を知る必要がない。
        /// </summary>
        private static void PaintInputParts(
            UnityEngine.UIElements.VisualElement root,
            StyleColor bg, StyleColor fg, params string[] classNames)
        {
            foreach (string cn in classNames)
            {
                root.Query<VisualElement>(className: cn).ForEach(e =>
                {
                    e.style.backgroundColor = bg;
                    e.style.color           = fg;
                });
            }
        }

        // ================================================================
        // ボタン操作フィードバック
        // ================================================================

        /// <summary>押下確定フラッシュ用の一時クラス名。PolyLingButtonStates.uss と対応。</summary>
        private const string BtnFlashClass = "pl-btn-flash";

        /// <summary>InstallButtonFeedback の二重適用防止マーカ。</summary>
        private const string BtnFeedbackHostClass = "pl-btn-feedback-host";

        /// <summary>
        /// ボタンの操作フィードバック（ホバー / 押下中 / 押下確定 / 無効）を root へ一括導入する。
        /// ApplyDarkTheme が background-color / color をインライン設定しており、UIToolkit では
        /// インラインが StyleSheet より優先されるため、USS 側は border-color / scale / opacity
        /// のみで状態を表現する（PolyLingButtonStates.uss）。
        /// 押下確定は ClickEvent（バブリング）を root で1度だけ受け、対象ボタンへ BtnFlashClass を
        /// 一時付与して短時間だけ枠線を強調する。個々のボタンへの登録は不要。
        /// </summary>
        public static void InstallButtonFeedback(VisualElement root)
        {
            if (root == null) return;
            if (root.ClassListContains(BtnFeedbackHostClass)) return;
            root.AddToClassList(BtnFeedbackHostClass);

            var sheet = Resources.Load<StyleSheet>("PolyLingButtonStates");
            if (sheet != null && !root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);

            root.RegisterCallback<ClickEvent>(OnAnyButtonClicked);
        }

        /// <summary>
        /// root で受けたクリックを最寄りの Button へ遡り、押下確定フラッシュを掛ける。
        /// 無効(SetEnabled(false))の要素にはイベントが届かないため、押せなかった場合は発火しない。
        /// </summary>
        private static void OnAnyButtonClicked(ClickEvent evt)
        {
            var ve = evt.target as VisualElement;
            while (ve != null && !(ve is Button)) ve = ve.parent;
            if (ve == null) return;
            if (ve.ClassListContains(BtnFlashClass)) return;

            var btn = ve;
            btn.AddToClassList(BtnFlashClass);
            btn.schedule.Execute(() => btn.RemoveFromClassList(BtnFlashClass)).ExecuteLater(180);
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

        /// <summary>
        /// 右ペイン背景色。軽量クライアントが同一背景を再現するための共有アクセサ。
        /// （BuildRightPane の PaneBg(0.15f) と同値）
        /// </summary>
        public static Color RightPaneBackgroundColor => new Color(0.15f, 0.15f, 0.15f, 1f);
    }
}
