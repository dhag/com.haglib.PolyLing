// PlayerLayoutRoot.LeftPaneButtons.cs
// 左ペイン：通常ボタン（カテゴリ別 Foldout）の宣言と組み立て。
// ボタンを増やすときはこのファイルだけを編集する（配線は PolyLingPlayerViewerCore）。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // 左ペイン 通常ボタン（公開要素）
        // ================================================================

        public VisualElement LocalLoaderSection { get; private set; }
        public Button        ConnectBtn         { get; private set; }
        public Button        DisconnectBtn      { get; private set; }
        public Button        FetchBtn           { get; private set; }
        public VisualElement RemoteSection      { get; private set; }
        public Foldout       RemoteFoldout      { get; private set; }

        /// <summary>
        /// 左ペイン：法線自動計算トグル。既定 OFF（＝自動計算しない）。
        /// 選択メッシュの MeshObject.PreserveNormals を反転して書き込む
        /// （自動計算 ON ＝ PreserveNormals false）。
        /// </summary>
        public Toggle AutoRecalcNormalsToggle { get; private set; }

        /// <summary>左ペイン：法線の手動再計算ボタン。対象は選択メッシュ。</summary>
        public Button RecalcNormalsBtn { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（新しい基本）。</summary>
        public Button LivePrimitiveBtn { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（新しい高度）。新しい基本と同じ LivePrimitiveSection を開く。</summary>
        public Button LiveAdvancedPrimitiveBtn { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（機構部品）。同じ LivePrimitiveSection を開く。</summary>
        public Button LiveMechanismPrimitiveBtn { get; private set; }

        /// <summary>左ペイン：新図形生成ボタン（揺れものボーン）。同じ LivePrimitiveSection を開く。</summary>
        public Button LiveSpringBonePrimitiveBtn { get; private set; }

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

        /// <summary>左ペイン：MeshFilter→Skinnedボタン。</summary>
        public Button MeshFilterToSkinnedBtn { get; private set; }

        /// <summary>描画オブジェクト単位の種別変換（MeshFilter 系 ⇔ SkinnedMesh 系）。</summary>
        public Button SkinKindBtn { get; private set; }

        /// <summary>左ペイン：下絵ボタン（その他）。</summary>
        public Button UnderlayBtn { get; private set; }

        /// <summary>左ペイン：作業フォルダ設定ボタン（その他）。</summary>
        public Button WorkFolderBtn { get; private set; }

        /// <summary>左ペイン：軸/グリッドボタン（その他）。</summary>
        public Button GridAxisBtn { get; private set; }

        /// <summary>左ペイン：カメラ調整ボタン（その他）。</summary>
        public Button CameraBtn { get; private set; }

        /// <summary>左ペイン：キャプチャボタン（その他）。</summary>
        public Button CaptureBtn { get; private set; }

        /// <summary>左ペイン：ブレンドボタン。</summary>
        public Button BlendBtn { get; private set; }

        /// <summary>シュリンカー(頂点)ボタン</summary>
        public Button ShrinkBtn { get; private set; }

        /// <summary>シュリンカー(面)ボタン</summary>
        public Button ShrinkFaceBtn { get; private set; }
        public Button ThinPlateMorphBtn { get; private set; }

        /// <summary>左ペイン：モデルブレンドボタン。</summary>
        public Button ModelBlendBtn { get; private set; }

        public Button BoneEditorBtn { get; private set; }
        public Button UVEditorBtn { get; private set; }
        public Button UVUnwrapBtn { get; private set; }
        public Button        UVZBtn                { get; private set; }
        public Button        PartsSelectionSetBtn  { get; private set; }
        public Button        MeshSelectionSetBtn   { get; private set; }
        public Button        ObjectGroupBtn        { get; private set; }
        public Button        NormalExcludeSetBtn   { get; private set; }
        public Button        NormalEditBtn         { get; private set; }
        public Button        NormalTransplantBtn   { get; private set; }
        public Button        FaceHideBtn           { get; private set; }
        public Button        MergeMeshesBtn        { get; private set; }
        public Button        BooleanBtn            { get; private set; }
        public Button        MorphBtn              { get; private set; }
        public Button        MorphCreateBtn        { get; private set; }
        public Button        TPoseBtn              { get; private set; }
        public Button        HumanoidMappingBtn    { get; private set; }
        public Button        MirrorBtn             { get; private set; }
        public Button        QuadDecimatorBtn       { get; private set; }
        public Button        AlignVerticesBtn           { get; private set; }
        public Button        PlanarizeAlongBonesBtn     { get; private set; }
        public Button        SmoothEdgesBtn             { get; private set; }
        public Button        PipeAlignBtn               { get; private set; }
        public Button        SurfaceSnapBtn             { get; private set; }
        public Button        PlaceObjectReshapeBtn      { get; private set; }
        public Button        MergeVerticesBtn           { get; private set; }
        public Button        SplitVerticesBtn           { get; private set; }
        public Button        VertexHoleBtn              { get; private set; }
        public Button        VertexDissolveBtn          { get; private set; }
        public Button        HoleRingCountBtn           { get; private set; }
        public Button        EdgeBridgeBtn              { get; private set; }
        public Button        Tri4To1Btn                 { get; private set; }
        public Button        FaceMergeBtn               { get; private set; }
        public Button        Quad4To1Btn                { get; private set; }
        public Button        VertexIdBtn                { get; private set; }
        public Button        VertexTransferBtn          { get; private set; }
        public Button        PartsIdBtn                 { get; private set; }
        public Button        AddFaceBtn                 { get; private set; }
        public Button        FlipFaceBtn                { get; private set; }
        public Button        RotateBtn                  { get; private set; }
        public Button        WorkAxisBtn                { get; private set; }
        public Button        DeformBtn                  { get; private set; }
        public Button        LatticeBtn                 { get; private set; }
        public Button        ScaleBtn                   { get; private set; }
        public Button        EdgeBevelBtn               { get; private set; }
        public Button        EdgeExtrudeBtn             { get; private set; }
        public Button        FaceExtrudeBtn             { get; private set; }
        public Button        EdgeTopologyBtn            { get; private set; }
        public Button        KnifeBtn                   { get; private set; }
        public Button        BridgeBtn                  { get; private set; }
        public Button        SolidifyBtn                { get; private set; }
        public Button        LineExtrudeBtn             { get; private set; }
        public Button        MediaPipeBtn           { get; private set; }
        public Button        VMDTestBtn             { get; private set; }
        public Button        VmdToVrmaBtn          { get; private set; }
        public Button        UnityClipTestBtn        { get; private set; }
        public Button        UnityClipToVrmaBtn      { get; private set; }
        public Button        MotionClipTestBtn        { get; private set; }

        /// <summary>左ペイン：コマンド定義の検査ボタン（システムデバッグ）。</summary>
        public Button        CommandSchemaBtn        { get; private set; }

        /// <summary>
        /// 左ペイン：MCP用サンドボックスを開くボタン。
        /// 3D連携の図形生成（LivePrimitiveSection）を「サンドボックス」カテゴリで開く。
        /// </summary>
        public Button        McpSandboxBtn           { get; private set; }

        public Button        OriginTestBtn            { get; private set; }
        public Button        SpringBoneTestBtn        { get; private set; }

        /// <summary>左ペイン：揺れもの編集ボタン（ボーン・モーフ）。</summary>
        public Button        SpringBoneBtn            { get; private set; }

        /// <summary>左ペイン：当たり判定の作成と編集ボタン（ボーン・モーフ）。</summary>
        public Button        SpringBoneColliderBtn     { get; private set; }

        /// <summary>左ペイン：マッスル可動域の編集ボタン（ボーン・モーフ）。</summary>
        public Button        HumanLimitBtn             { get; private set; }

        /// <summary>左ペイン：VRM 出力設定ボタン（ファイル）。</summary>
        public Button        VrmSettingsBtn            { get; private set; }

        public Button        RobotBuildTestBtn        { get; private set; }
        public Button        FrillSkirtTestBtn        { get; private set; }
        public Button        SpringSkinScenarioBtn     { get; private set; }
        public Button        SpringSkinPipeScenarioBtn     { get; private set; }
        public Button        PipeHairTestBtn          { get; private set; }
        public Button        BarnacleTestBtn          { get; private set; }
        public Button        RevolutionTestBtn        { get; private set; }
        public Button        Profile2DTestBtn         { get; private set; }
        public Button        PmxToMqoTestBtn          { get; private set; }
        public Button        MqoToPmxTestBtn          { get; private set; }
        public Button        SkinTestBtn              { get; private set; }
        public Button        RemoteServerBtn        { get; private set; }
        public Button        LogBtn                 { get; private set; }

        /// <summary>左ペイン：PMXフルエクスポートボタン。</summary>
        public Button FullExportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQOフルエクスポートボタン。</summary>
        public Button FullExportMqoBtn { get; private set; }

        /// <summary>左ペイン：VRM 1.0 フルエクスポートボタン。
        /// 実装は PolyLing.Vrm10 アセンブリ側にあり、VRM パッケージが無い環境では
        /// パネルが「利用できません」を表示する（規約は IVrm10Exporter.cs を正典とする）。</summary>
        public Button FullExportVrmBtn { get; private set; }

        /// <summary>左ペイン：プロジェクト保存 / 読込ボタン（それぞれ別セクションを開く）。</summary>
        public Button ProjectSaveBtn { get; private set; }
        public Button ProjectLoadBtn { get; private set; }

        /// <summary>左ペイン：OBJ 読み込み / 保存ボタン（プロジェクトの各ボタンの横）。
        /// インポータ / エクスポータのセクションを OBJ モードで開く。</summary>
        public Button ObjLoadBtn { get; private set; }
        public Button ObjSaveBtn { get; private set; }

        /// <summary>左ペイン：VRM 読み込みボタン。インポータのセクションを VRM モードで開く。</summary>
        public Button VrmLoadBtn { get; private set; }

        /// <summary>左ペイン：PMX部分インポートボタン。</summary>
        public Button PartialImportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQO部分インポートボタン。</summary>
        public Button PartialImportMqoBtn { get; private set; }

        /// <summary>左ペイン：PMX部分エクスポートボタン。</summary>
        public Button PartialExportPmxBtn { get; private set; }

        /// <summary>左ペイン：MQO部分エクスポートボタン。</summary>
        public Button PartialExportMqoBtn { get; private set; }

        // ================================================================
        // 左ペイン 通常ボタンの組み立て
        // ================================================================

        /// <summary>
        /// 左ペインの通常ボタン（カテゴリ別 Foldout）を組み立てて scroll へ並べる。
        /// ボタンを増やすときはここへ足し、代入先プロパティは
        /// このファイル先頭の宣言群へ足す。配線（clicked）は PolyLingPlayerViewerCore 側。
        /// </summary>
        private void BuildLeftPaneToolButtons(VisualElement scroll)
        {
            LocalLoaderSection = new VisualElement();
            LocalLoaderSection.style.marginBottom = 6;
            // ※ LocalLoaderSection（Load PMX / Load MQO）は「ファイル」foldout の先頭へ移動する（下記）。

            // ================================================================
            // カテゴリ別 Foldout（既定折りたたみ）にまとめる。
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

            // VRM 読み込み（VRM 1.0 / 0.x）。インポータのセクションを VRM モードで開く。
            VrmLoadBtn = MakeBtn(".VRMファイル読込");
            VrmLoadBtn.style.marginBottom = 2;
            foFile.Add(VrmLoadBtn);

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

            // PMX / MQO は 1 行。VRM は「保存」と「出力設定」を対にして次の行へ置く。
            var fullExportRow = new VisualElement();
            fullExportRow.style.flexDirection = FlexDirection.Row;
            fullExportRow.style.marginBottom  = 2;
            FullExportPmxBtn = MakeBtn("PMX保存"); FullExportPmxBtn.style.flexGrow = 1; FullExportPmxBtn.style.marginRight = 2;
            FullExportMqoBtn = MakeBtn("MQO保存"); FullExportMqoBtn.style.flexGrow = 1;
            fullExportRow.Add(FullExportPmxBtn); fullExportRow.Add(FullExportMqoBtn);
            foFile.Add(fullExportRow);

            // VRM に載せる作者情報・許諾・視線・一人称。保存されるモデルの値で、
            // 出力ごとの上書きは「エクスポート」側にある。VRM 保存の隣に置く。
            var vrmRow = new VisualElement();
            vrmRow.style.flexDirection = FlexDirection.Row;
            vrmRow.style.marginBottom  = 2;
            FullExportVrmBtn = MakeBtn("VRM保存");     FullExportVrmBtn.style.flexGrow = 1; FullExportVrmBtn.style.marginRight = 2;
            VrmSettingsBtn   = MakeBtn("VRM出力設定"); VrmSettingsBtn.style.flexGrow   = 1;
            vrmRow.Add(FullExportVrmBtn); vrmRow.Add(VrmSettingsBtn);
            foFile.Add(vrmRow);

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

            // 歯車まわり（かみ合う部品）はここへ集める。
            LiveMechanismPrimitiveBtn = MakeBtn("機構部品（3D連携）");
            foPrimitive.Add(LiveMechanismPrimitiveBtn);

            // 揺れもの用のボーン鎖。作るのはボーンでメッシュではないが、
            // 形の指定（1 本 / 円筒 / 回転体）とプロファイル編集は図形生成と同じなので
            // 同じパネルのカテゴリとして置く。
            LiveSpringBonePrimitiveBtn = MakeBtn("揺れものボーン（3D連携）");
            foPrimitive.Add(LiveSpringBonePrimitiveBtn);

            // 配置ギズモのサブモード切替ボタンは
            // PlayerPrimitiveMeshSubPanel（3D連携インスタンス）の中へ移設済み。

            // ── 選択 ───────────────────────────────────────────────────
            var foSelect = MakeFoldout("選択", "Select");

            var rowAdvSel = new VisualElement(); rowAdvSel.style.flexDirection = FlexDirection.Row; rowAdvSel.style.marginBottom = 2;
            ToolAdvancedSelBtn = MakeBtn("詳細選択"); ToolAdvancedSelBtn.style.flexGrow = 1;
            rowAdvSel.Add(ToolAdvancedSelBtn); foSelect.Add(rowAdvSel);

            // 一時選択サブツール (デバッグ用)。ショートカット R / G と同じ処理を呼ぶ。
            var rowSubTool = new VisualElement(); rowSubTool.style.flexDirection = FlexDirection.Row; rowSubTool.style.marginBottom = 2;
            SubToolBoxSelectBtn   = MakeBtn("矩形選択(一時) R");   SubToolBoxSelectBtn.style.flexGrow   = 1; SubToolBoxSelectBtn.style.marginRight = 2;
            SubToolLassoSelectBtn = MakeBtn("投げ縄選択(一時) G"); SubToolLassoSelectBtn.style.flexGrow = 1;
            rowSubTool.Add(SubToolBoxSelectBtn); rowSubTool.Add(SubToolLassoSelectBtn); foSelect.Add(rowSubTool);

            var rowSelSet = new VisualElement(); rowSelSet.style.flexDirection = FlexDirection.Row; rowSelSet.style.marginBottom = 2;
            PartsSelectionSetBtn = MakeBtn("パーツ選択辞書"); PartsSelectionSetBtn.style.flexGrow = 1; PartsSelectionSetBtn.style.marginRight = 2;
            MeshSelectionSetBtn  = MakeBtn("オブジェクト選択辞書"); MeshSelectionSetBtn.style.flexGrow  = 1;
            rowSelSet.Add(PartsSelectionSetBtn); rowSelSet.Add(MeshSelectionSetBtn); foSelect.Add(rowSelSet);

            // オブジェクトグループ（帯・断面・パラメータと出力先のまとまり）。
            // 選択辞書と同じ「オブジェクトのまとまりを管理するもの」なので隣に置く。
            ObjectGroupBtn = MakeBtn("オブジェクトグループ"); ObjectGroupBtn.style.flexGrow = 1;
            var rowObjGroup = new VisualElement(); rowObjGroup.style.flexDirection = FlexDirection.Row; rowObjGroup.style.marginBottom = 2;
            rowObjGroup.Add(ObjectGroupBtn); foSelect.Add(rowObjGroup);

            // ── 移動/回転/拡大縮小 ─────────────────────────────────────
            var foTransform = MakeFoldout("移動/回転/拡大縮小", "Transform");

            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;
            toolRow.style.marginBottom  = 2;
            ToolVertexMoveBtn  = MakeBtn("頂点移動");     ToolVertexMoveBtn.style.flexGrow  = 1; ToolVertexMoveBtn.style.marginRight  = 2;
            ToolObjectMoveBtn  = MakeBtn("描画オブジェクトの姿勢"); ToolObjectMoveBtn.style.flexGrow  = 1;
            toolRow.Add(ToolVertexMoveBtn); toolRow.Add(ToolObjectMoveBtn);
            foTransform.Add(toolRow);

            var toolRow2 = new VisualElement();
            toolRow2.style.flexDirection = FlexDirection.Row;
            toolRow2.style.marginBottom  = 2;
            ToolPivotOffsetBtn = MakeBtn("ピボット位置"); ToolPivotOffsetBtn.style.flexGrow = 1; ToolPivotOffsetBtn.style.marginRight = 2;
            ToolSculptBtn      = MakeBtn("スカルプト");   ToolSculptBtn.style.flexGrow      = 1;
            toolRow2.Add(ToolPivotOffsetBtn); toolRow2.Add(ToolSculptBtn);
            foTransform.Add(toolRow2);

            var rowRotScale = new VisualElement(); rowRotScale.style.flexDirection = FlexDirection.Row; rowRotScale.style.marginBottom = 2;
            RotateBtn = MakeBtn("回転");     RotateBtn.style.flexGrow = 1; RotateBtn.style.marginRight = 2;
            ScaleBtn  = MakeBtn("スケール"); ScaleBtn.style.flexGrow  = 1;
            rowRotScale.Add(RotateBtn); rowRotScale.Add(ScaleBtn); foTransform.Add(rowRotScale);

            // 作業用ローカル軸。回転 / 曲げの基準フレームを操作するサブツール。
            var rowWorkAxis = new VisualElement(); rowWorkAxis.style.flexDirection = FlexDirection.Row; rowWorkAxis.style.marginBottom = 2;
            WorkAxisBtn = MakeBtn("作業軸"); WorkAxisBtn.style.flexGrow = 1; WorkAxisBtn.style.marginRight = 2;
            DeformBtn   = MakeBtn("変形");   DeformBtn.style.flexGrow   = 1;
            rowWorkAxis.Add(WorkAxisBtn); rowWorkAxis.Add(DeformBtn); foTransform.Add(rowWorkAxis);

            // ── 特殊な変形 ─────────────────────────────────────────────
            // 頂点を「掴んで動かす」以外の変形。基準となる別の形（ブレンド先・
            // 対応点・格子）を与えて全体を作り替えるものをここへ集める。
            var foSpecialDeform = MakeFoldout("特殊な変形", "SpecialDeform");

            var rowBlend = new VisualElement(); rowBlend.style.flexDirection = FlexDirection.Row; rowBlend.style.marginBottom = 2;
            BlendBtn      = MakeBtn("メッシュブレンド"); BlendBtn.style.flexGrow      = 1; BlendBtn.style.marginRight = 2;
            ModelBlendBtn = MakeBtn("モデルブレンド");   ModelBlendBtn.style.flexGrow = 1;
            rowBlend.Add(BlendBtn); rowBlend.Add(ModelBlendBtn); foSpecialDeform.Add(rowBlend);

            var rowShrink = new VisualElement(); rowShrink.style.flexDirection = FlexDirection.Row; rowShrink.style.marginBottom = 2;
            ShrinkBtn     = MakeBtn("シュリンカー(頂点)"); ShrinkBtn.style.flexGrow     = 1; ShrinkBtn.style.marginRight = 2;
            ShrinkFaceBtn = MakeBtn("シュリンカー(面)");   ShrinkFaceBtn.style.flexGrow = 1;
            rowShrink.Add(ShrinkBtn); rowShrink.Add(ShrinkFaceBtn); foSpecialDeform.Add(rowShrink);

            // 格子変形は作業軸を格子フレームとして使うが、操作の性質は
            // 「与えた枠へ全体を追随させる」側なのでここへ置く。
            var rowTpsLattice = new VisualElement(); rowTpsLattice.style.flexDirection = FlexDirection.Row; rowTpsLattice.style.marginBottom = 2;
            ThinPlateMorphBtn = MakeBtn("TPSモーフ"); ThinPlateMorphBtn.style.flexGrow = 1; ThinPlateMorphBtn.style.marginRight = 2;
            LatticeBtn        = MakeBtn("格子変形");  LatticeBtn.style.flexGrow        = 1;
            rowTpsLattice.Add(ThinPlateMorphBtn); rowTpsLattice.Add(LatticeBtn); foSpecialDeform.Add(rowTpsLattice);

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

            // 線分押し出しは選択線分からループを検出して新しいメッシュを作る。
            // 押し出し系と並べたいが 1 行 3 つで幅が詰まるため行を分ける。
            var rowLineExtrude = new VisualElement(); rowLineExtrude.style.flexDirection = FlexDirection.Row; rowLineExtrude.style.marginBottom = 2;
            LineExtrudeBtn = MakeBtn("線分押し出し"); LineExtrudeBtn.style.flexGrow = 1;
            rowLineExtrude.Add(LineExtrudeBtn); foTopology.Add(rowLineExtrude);

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

            // ブーリアン。2 つのメッシュから新しい面構成を作り直す操作なので
            // UV・マテリアルではなくトポロジー編集に置く。
            var rowBoolean = new VisualElement(); rowBoolean.style.flexDirection = FlexDirection.Row; rowBoolean.style.marginBottom = 2;
            BooleanBtn = MakeBtn("ブーリアン"); BooleanBtn.style.flexGrow = 1;
            rowBoolean.Add(BooleanBtn); foTopology.Add(rowBoolean);

            // 削除系。面削除モードは進入中にボタンがハイライトされる
            // (破壊的モードなので表示は必須)。
            var rowDelete = new VisualElement(); rowDelete.style.flexDirection = FlexDirection.Row; rowDelete.style.marginBottom = 2;
            SubToolDeleteBtn  = MakeBtn("選択削除 Del");   SubToolDeleteBtn.style.flexGrow  = 1; SubToolDeleteBtn.style.marginRight = 2;
            ToolDeleteFaceBtn = MakeBtn("面削除モード D"); ToolDeleteFaceBtn.style.flexGrow = 1;
            rowDelete.Add(SubToolDeleteBtn); rowDelete.Add(ToolDeleteFaceBtn); foTopology.Add(rowDelete);

            var rowFaceHide = new VisualElement(); rowFaceHide.style.flexDirection = FlexDirection.Row; rowFaceHide.style.marginBottom = 2;
            FaceHideBtn = MakeBtn("面の表示・非表示"); FaceHideBtn.style.flexGrow = 1;
            rowFaceHide.Add(FaceHideBtn); foTopology.Add(rowFaceHide);

            // ── 法線 ───────────────────────────────────────────────────
            // 法線に関する操作と設定をここへ集める。自動計算トグルと手動再計算は
            // 以前は左ペイン上部の常時表示部にあったが、他の法線機能と離れていた。
            var foNormal = MakeFoldout("法線", "Normal");

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

            foNormal.Add(normalRecalcRow);

            var rowNormalExclude = new VisualElement(); rowNormalExclude.style.flexDirection = FlexDirection.Row; rowNormalExclude.style.marginBottom = 2;
            NormalEditBtn = MakeBtn("法線編集"); NormalEditBtn.style.flexGrow = 1; NormalEditBtn.style.marginRight = 2;
            NormalExcludeSetBtn = MakeBtn("法線再計算 除外辞書"); NormalExcludeSetBtn.style.flexGrow = 1;
            rowNormalExclude.Add(NormalEditBtn); rowNormalExclude.Add(NormalExcludeSetBtn); foNormal.Add(rowNormalExclude);

            var rowNormalTransplant = new VisualElement(); rowNormalTransplant.style.flexDirection = FlexDirection.Row; rowNormalTransplant.style.marginBottom = 2;
            NormalTransplantBtn = MakeBtn("法線移植"); NormalTransplantBtn.style.flexGrow = 1;
            rowNormalTransplant.Add(NormalTransplantBtn); foNormal.Add(rowNormalTransplant);

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

            // マッスル可動域。Humanoid 割当と T ポーズが前提なので、その直下に置く。
            HumanLimitBtn = MakeBtn("マッスル可動域編集");
            foBoneMorph.Add(HumanLimitBtn);

            ToolSkinWeightPaintBtn = MakeBtn("スキンWペイント");
            foBoneMorph.Add(ToolSkinWeightPaintBtn);

            SkinWeightNumericBtn = MakeBtn("スキンW数値設定");
            foBoneMorph.Add(SkinWeightNumericBtn);

            // ブレンド / シュリンカー / TPSモーフ / 格子変形は「特殊な変形」へ移動した。

            MorphCreateBtn = MakeBtn("モーフ生成・差分から");         foBoneMorph.Add(MorphCreateBtn);
            MorphBtn       = MakeBtn("モーフエクスプレッション編集"); foBoneMorph.Add(MorphBtn);

            // 揺れもの（VRM SpringBone）の編集。Humanoid 割当・T ポーズと同じ
            // 「ボーンに属性を付ける」系の操作なのでここに置く。
            SpringBoneBtn = MakeBtn("揺れもの編集");
            foBoneMorph.Add(SpringBoneBtn);

            // 当たり判定そのものを作る画面。揺れもの編集からは
            // まとまり（グループ）の名前しか触れないので、隣に並べる。
            SpringBoneColliderBtn = MakeBtn("当たり判定の作成と編集");
            foBoneMorph.Add(SpringBoneColliderBtn);

            // ── UV・マテリアル ─────────────────────────────────────────
            var foUvMat = MakeFoldout("UV・マテリアル", "UvMat");

            var rowUv = new VisualElement(); rowUv.style.flexDirection = FlexDirection.Row; rowUv.style.marginBottom = 2;
            UVEditorBtn = MakeBtn("UVエディタ"); UVEditorBtn.style.flexGrow = 1; UVEditorBtn.style.marginRight = 2;
            UVUnwrapBtn = MakeBtn("UV展開");     UVUnwrapBtn.style.flexGrow = 1; UVUnwrapBtn.style.marginRight = 2;
            UVZBtn      = MakeBtn("UVZ");        UVZBtn.style.flexGrow      = 1;
            rowUv.Add(UVEditorBtn); rowUv.Add(UVUnwrapBtn); rowUv.Add(UVZBtn); foUvMat.Add(rowUv);

            MergeMeshesBtn  = MakeBtn("メッシュマージ");   foUvMat.Add(MergeMeshesBtn);
            // ブーリアンは「トポロジー編集」へ移動した。

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

            // 作業フォルダ（PLSandbox の根）。設定なので「その他」の先頭に置く。
            // コマンド経由のファイル入出力はここを決めないと一切通らないため、
            // 未設定時の拒否理由にもこの置き場所を書いてある（PLSandbox 参照）。
            WorkFolderBtn = MakeBtn("作業フォルダ");
            WorkFolderBtn.style.marginBottom = 2;
            foOther.Add(WorkFolderBtn);

            var rowMisc = new VisualElement(); rowMisc.style.flexDirection = FlexDirection.Row; rowMisc.style.marginBottom = 2;
            MediaPipeBtn    = MakeBtn("MediaPipe");   MediaPipeBtn.style.flexGrow    = 1; MediaPipeBtn.style.marginRight    = 2;
            VMDTestBtn      = MakeBtn("VMDテスト");    VMDTestBtn.style.flexGrow      = 1; VMDTestBtn.style.marginRight      = 2;
            RemoteServerBtn = MakeBtn("リモートサーバ"); RemoteServerBtn.style.flexGrow = 1;
            rowMisc.Add(MediaPipeBtn); rowMisc.Add(VMDTestBtn); rowMisc.Add(RemoteServerBtn); foOther.Add(rowMisc);

            var rowMisc2 = new VisualElement(); rowMisc2.style.flexDirection = FlexDirection.Row; rowMisc2.style.marginBottom = 2;
            UnityClipTestBtn = MakeBtn("Unityクリップ"); UnityClipTestBtn.style.flexGrow = 1; UnityClipTestBtn.style.marginRight = 2;
            MotionClipTestBtn = MakeBtn("Yet（統合モーション)"); MotionClipTestBtn.style.flexGrow = 1;
            rowMisc2.Add(UnityClipTestBtn); rowMisc2.Add(MotionClipTestBtn); foOther.Add(rowMisc2);

            // モデルを使わない変換専用の道具。名前が長いので 1 行使う。
            UnityClipToVrmaBtn = MakeBtn("Unityクリップ→VRMA変換");
            UnityClipToVrmaBtn.style.marginBottom = 2;
            foOther.Add(UnityClipToVrmaBtn);

            // こちらはモデルへ VMD を適用しながら書き出す。名前が長いので 1 行使う。
            VmdToVrmaBtn = MakeBtn("VMD→VRMA書き出し");
            VmdToVrmaBtn.style.marginBottom = 2;
            foOther.Add(VmdToVrmaBtn);

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
            var foMerge = MakeFoldout("面の結合", "Merge");

            var rowMerge = new VisualElement(); rowMerge.style.flexDirection = FlexDirection.Row; rowMerge.style.marginBottom = 2;
            VertexDissolveBtn = MakeBtn("頂点溶解"); VertexDissolveBtn.style.flexGrow = 1; VertexDissolveBtn.style.marginRight = 2;
            Tri4To1Btn        = MakeBtn("三角4→1"); Tri4To1Btn.style.flexGrow        = 1; Tri4To1Btn.style.marginRight        = 2;
            // 旧「面結合（頂点は削除しない）」「面結合(頂点削除)」を 1 つにまとめた。
            // 頂点を外すかはパネルの「頂点を削除する」チェックボックスで選ぶ。
            FaceMergeBtn      = MakeBtn("面結合（辺指定）");   FaceMergeBtn.style.flexGrow      = 1;
            rowMerge.Add(VertexDissolveBtn); rowMerge.Add(Tri4To1Btn); rowMerge.Add(FaceMergeBtn); foMerge.Add(rowMerge);

            var rowMerge2 = new VisualElement(); rowMerge2.style.flexDirection = FlexDirection.Row; rowMerge2.style.marginBottom = 2;
            Quad4To1Btn          = MakeBtn("四角4→1");   Quad4To1Btn.style.flexGrow          = 1;
            rowMerge2.Add(Quad4To1Btn); foMerge.Add(rowMerge2);

            // ── システムデバッグ ───────────────────────────────────────
            // 自動検証の入口。通常の編集操作ではないので独立させる。
            var foSysDebug = MakeFoldout("参考手順・システムデバッグ", "SysDebug");

            // 2 個並びの行は左ボタンに marginRight = 2 を付け、右ボタンには余白を付けない。
            // 単独行のボタンは flexGrow = 1 のみ。全行でこの規則にそろえること。

            // 1) ロボ組み立て（名前が長いので単独行）
            var rowSysDebug1 = new VisualElement(); rowSysDebug1.style.flexDirection = FlexDirection.Row; rowSysDebug1.style.marginBottom = 2;
            RobotBuildTestBtn = MakeBtn("ロボ組み立て自動検証"); RobotBuildTestBtn.style.flexGrow = 1;
            rowSysDebug1.Add(RobotBuildTestBtn); foSysDebug.Add(rowSysDebug1);

            // 2) 回転体 / 2D押し出し
            var rowSysDebug2 = new VisualElement(); rowSysDebug2.style.flexDirection = FlexDirection.Row; rowSysDebug2.style.marginBottom = 2;
            RevolutionTestBtn = MakeBtn("回転体生成自動検証");     RevolutionTestBtn.style.flexGrow = 1; RevolutionTestBtn.style.marginRight = 2;
            Profile2DTestBtn  = MakeBtn("2D押し出し自動検証"); Profile2DTestBtn.style.flexGrow  = 1;
            rowSysDebug2.Add(RevolutionTestBtn); rowSysDebug2.Add(Profile2DTestBtn); foSysDebug.Add(rowSysDebug2);

            // 3a) フリルスカート（名前が長いので単独行）
            var rowSysDebug3 = new VisualElement(); rowSysDebug3.style.flexDirection = FlexDirection.Row; rowSysDebug3.style.marginBottom = 2;
            FrillSkirtTestBtn = MakeBtn("フリル・プリーツ自動検証"); FrillSkirtTestBtn.style.flexGrow = 1;
            rowSysDebug3.Add(FrillSkirtTestBtn); foSysDebug.Add(rowSysDebug3);
            // 3b) 前髪パイプ / 藤壺
            var rowSysDebug4 = new VisualElement(); rowSysDebug4.style.flexDirection = FlexDirection.Row; rowSysDebug4.style.marginBottom = 2;
            PipeHairTestBtn = MakeBtn("前髪パイプ自動検証"); PipeHairTestBtn.style.flexGrow = 1; PipeHairTestBtn.style.marginRight = 2;
            BarnacleTestBtn = MakeBtn("藤壺自動検証");       BarnacleTestBtn.style.flexGrow = 1;
            rowSysDebug4.Add(PipeHairTestBtn); rowSysDebug4.Add(BarnacleTestBtn); foSysDebug.Add(rowSysDebug4);

            // 4a) 揺れもの→スキンド→VRM（名前が長いので単独行）
            var rowSysDebug3b = new VisualElement(); rowSysDebug3b.style.flexDirection = FlexDirection.Row; rowSysDebug3b.style.marginBottom = 2;
            SpringSkinScenarioBtn = MakeBtn("揺れもの（フリル）→スキンド→VRM 自動検証"); SpringSkinScenarioBtn.style.flexGrow = 1;
            rowSysDebug3b.Add(SpringSkinScenarioBtn); foSysDebug.Add(rowSysDebug3b);

            // 4b) 揺れもの（パイプ）→スキンド→VRM（名前が長いので単独行）
            var rowSysDebug3c = new VisualElement(); rowSysDebug3c.style.flexDirection = FlexDirection.Row; rowSysDebug3c.style.marginBottom = 2;
            SpringSkinPipeScenarioBtn = MakeBtn("揺れもの（パイプ）→スキンド→VRM 自動検証"); SpringSkinPipeScenarioBtn.style.flexGrow = 1;
            rowSysDebug3c.Add(SpringSkinPipeScenarioBtn); foSysDebug.Add(rowSysDebug3c);


            // 5) 原点CSV / スキン生成
            var rowSysDebug5 = new VisualElement(); rowSysDebug5.style.flexDirection = FlexDirection.Row; rowSysDebug5.style.marginBottom = 2;
            OriginTestBtn = MakeBtn("原点CSV自動検証");   OriginTestBtn.style.flexGrow = 1; OriginTestBtn.style.marginRight = 2;
            SkinTestBtn   = MakeBtn("スキン生成自動検証"); SkinTestBtn.style.flexGrow   = 1;
            rowSysDebug5.Add(OriginTestBtn); rowSysDebug5.Add(SkinTestBtn); foSysDebug.Add(rowSysDebug5);

            // 6) スプリングボーン検証（単独行）
            var rowSysDebug6 = new VisualElement(); rowSysDebug6.style.flexDirection = FlexDirection.Row; rowSysDebug6.style.marginBottom = 2;
            SpringBoneTestBtn = MakeBtn("スプリングボーン検証"); SpringBoneTestBtn.style.flexGrow = 1;
            rowSysDebug6.Add(SpringBoneTestBtn); foSysDebug.Add(rowSysDebug6);

            // 7) PMX をソースにして MQO の頂点位置を差し替え、別名で保存する検証。
            // 名前が長いので 1 行使う。
            var rowSysDebug7 = new VisualElement(); rowSysDebug7.style.flexDirection = FlexDirection.Row; rowSysDebug7.style.marginBottom = 2;
            PmxToMqoTestBtn = MakeBtn("PMX位置→MQO保存 自動検証"); PmxToMqoTestBtn.style.flexGrow = 1;
            rowSysDebug7.Add(PmxToMqoTestBtn); foSysDebug.Add(rowSysDebug7);

            // 8) MQO をソースにして PMX の頂点位置と UV を差し替え、別名で保存する検証。
            // 名前が長いので 1 行使う。
            var rowSysDebug8 = new VisualElement(); rowSysDebug8.style.flexDirection = FlexDirection.Row; rowSysDebug8.style.marginBottom = 2;
            MqoToPmxTestBtn = MakeBtn("MQO位置UV→PMX保存 自動検証"); MqoToPmxTestBtn.style.flexGrow = 1;
            rowSysDebug8.Add(MqoToPmxTestBtn); foSysDebug.Add(rowSysDebug8);

            // 9) コマンド定義の検査。PLParam の付け忘れ・action 衝突・
            // スキーマに出せない型を調べ、道具一覧（JSON）を書き出す。
            var rowSysDebug9 = new VisualElement(); rowSysDebug9.style.flexDirection = FlexDirection.Row; rowSysDebug9.style.marginBottom = 2;
            CommandSchemaBtn = MakeBtn("コマンド定義の検査"); CommandSchemaBtn.style.flexGrow = 1;
            rowSysDebug9.Add(CommandSchemaBtn); foSysDebug.Add(rowSysDebug9);

            // ── MCP用サンドボックス ───────────────────────────────────
            // 3D連携の図形生成を「サンドボックス」カテゴリで開く。
            // ここで検証した図形は PlayerPrimitiveMeshSubPanel.Shapes.cs の
            // カテゴリ配列を付け替えるだけで基本図形・高度な図形へ昇格できる。
            var foMcpSandbox = MakeFoldout("MCP用サンドボックス", "McpSandbox");

            var rowMcpSandbox = new VisualElement(); rowMcpSandbox.style.flexDirection = FlexDirection.Row; rowMcpSandbox.style.marginBottom = 2;
            McpSandboxBtn = MakeBtn("MCPテスト"); McpSandboxBtn.style.flexGrow = 1;
            rowMcpSandbox.Add(McpSandboxBtn); foMcpSandbox.Add(rowMcpSandbox);

            // ── 左ペイン カテゴリ表示順 ───────────────────────────────
            // サーバと連携（クライアントモード時のみ表示。表示制御は core）を先頭に置く。
            scroll.Add(foRemote);
            scroll.Add(foFile);
            scroll.Add(foPrimitive);
            scroll.Add(foSelect);
            scroll.Add(foTransform);
            scroll.Add(foSpecialDeform);
            scroll.Add(foVertexPos);
            scroll.Add(foTopology);
            scroll.Add(foNormal);
            scroll.Add(foVertexTopo);
            scroll.Add(foUvMat);
            scroll.Add(foBoneMorph);
            scroll.Add(foOther);
            scroll.Add(foMerge);
            scroll.Add(foSysDebug);
            scroll.Add(foMcpSandbox);
        }
    }
}
