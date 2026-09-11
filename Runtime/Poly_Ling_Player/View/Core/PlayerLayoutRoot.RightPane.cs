// PlayerLayoutRoot.RightPane.cs
// 右ペイン：各サブパネルのセクションの宣言と組み立て。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // 右ペイン セクション（公開要素）
        // ================================================================

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
        public VisualElement VMDTestSection         { get; private set; }
        public VisualElement UnityClipTestSection    { get; private set; }
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
            ObjectGroupSection         = AddSection(visible: false);
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
            LineExtrudeSection         = AddSection(visible: false);
            MediaPipeSection           = AddSection(visible: false);
            VMDTestSection             = AddSection(visible: false);
            UnityClipTestSection       = AddSection(visible: false);
            UnityClipToVrmaSection     = AddSection(visible: false);
            VmdToVrmaSection           = AddSection(visible: false);
            MotionClipTestSection      = AddSection(visible: false);
            CommandSchemaSection       = AddSection(visible: false);
            OriginTestSection          = AddSection(visible: false);
            SkinTestSection            = AddSection(visible: false);
            SpringBoneTestSection      = AddSection(visible: false);
            SpringBoneSection          = AddSection(visible: false);
            SpringBoneColliderSection  = AddSection(visible: false);
            HumanLimitSection          = AddSection(visible: false);
            VrmSettingsSection         = AddSection(visible: false);
            RobotBuildTestSection      = AddSection(visible: false);
            FrillSkirtTestSection      = AddSection(visible: false);
            SpringSkinScenarioSection  = AddSection(visible: false);
            SpringSkinPipeScenarioSection = AddSection(visible: false);
            PipeHairTestSection        = AddSection(visible: false);
            BarnacleTestSection        = AddSection(visible: false);
            RevolutionTestSection      = AddSection(visible: false);
            Profile2DTestSection       = AddSection(visible: false);
            PmxToMqoTestSection        = AddSection(visible: false);
            MqoToPmxTestSection        = AddSection(visible: false);
            UnderlaySection            = AddSection(visible: false);
            GridAxisSection            = AddSection(visible: false);
            WorkFolderSection          = AddSection(visible: false);
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
    }
}
