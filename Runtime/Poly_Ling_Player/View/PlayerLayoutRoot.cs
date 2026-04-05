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
        public VisualElement ModelListContainer { get; private set; }
        public Button        ModelListBtn       { get; private set; }
        public Button        MeshListBtn        { get; private set; }

        // ================================================================
        // ビューポートパネル公開
        // ================================================================

        public PlayerViewportPanel PerspectivePanel { get; private set; }
        public PlayerViewportPanel TopPanel         { get; private set; }
        public PlayerViewportPanel FrontPanel       { get; private set; }
        public PlayerViewportPanel SidePanel        { get; private set; }

        // ================================================================
        // Right ペイン公開要素（表示フラグ）
        // ================================================================

        public Toggle ShowSelectedMeshToggle       { get; private set; }
        public Toggle ShowUnselectedMeshToggle     { get; private set; }
        public Toggle ShowSelectedVerticesToggle   { get; private set; }
        public Toggle ShowUnselectedVerticesToggle { get; private set; }
        public Toggle ShowSelectedWireToggle       { get; private set; }
        public Toggle ShowUnselectedWireToggle     { get; private set; }
        public Toggle ShowSelectedBoneToggle       { get; private set; }
        public Toggle ShowUnselectedBoneToggle     { get; private set; }
        public Toggle BackfaceCullingToggle        { get; private set; }

        /// <summary>左ペイン：ラッソ選択トグル。</summary>
        public Toggle LassoToggle { get; private set; }

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

        /// <summary>左ペイン：図形生成ボタン。</summary>
        public Button PrimitiveBtn { get; private set; }

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
        public VisualElement MediaPipeSection       { get; private set; }
        public Button        MediaPipeBtn           { get; private set; }
        public VisualElement VMDTestSection         { get; private set; }
        public Button        VMDTestBtn             { get; private set; }
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
        // Build
        // ================================================================

        public void Build(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Row;
            root.style.width         = new StyleLength(new Length(100, LengthUnit.Percent));
            root.style.height        = new StyleLength(new Length(100, LengthUnit.Percent));


            var splitLCR = new TwoPaneSplitView(0, 200f, TwoPaneSplitViewOrientation.Horizontal);
            splitLCR.style.flexGrow = 1;
            root.Add(splitLCR);

            splitLCR.Add(BuildLeftPane());

            var splitCR = new TwoPaneSplitView(1, 220f, TwoPaneSplitViewOrientation.Horizontal);
            splitCR.style.flexGrow = 1;
            splitLCR.Add(splitCR);

            var splitCenter = new TwoPaneSplitView(1, 240f, TwoPaneSplitViewOrientation.Horizontal);
            splitCenter.style.flexGrow = 1;
            splitCR.Add(splitCenter);

            PlayerViewportPanel perspPanel, topPanel, frontPanel, sidePanel;

            var splitPerspSide = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Vertical);
            splitPerspSide.style.flexGrow = 1;
            splitCenter.Add(splitPerspSide);
            splitPerspSide.Add(BuildViewportPane("Perspective", out perspPanel)); PerspectivePanel = perspPanel;
            splitPerspSide.Add(BuildViewportPane("Side",        out sidePanel));  SidePanel        = sidePanel;

            var splitTopFront = new TwoPaneSplitView(0, 200f, TwoPaneSplitViewOrientation.Vertical);
            splitTopFront.style.flexGrow = 1;
            splitCenter.Add(splitTopFront);
            splitTopFront.Add(BuildViewportPane("TOP",   out topPanel));   TopPanel   = topPanel;
            splitTopFront.Add(BuildViewportPane("Front", out frontPanel)); FrontPanel = frontPanel;

            splitCR.Add(BuildRightPane());
        }

        // ================================================================
        // Left ペイン
        // ================================================================

        private VisualElement BuildLeftPane()
        {
            var pane = MakePane(200f);
            pane.style.backgroundColor = PaneBg(0.15f);
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
            StatusLabel.style.color        = Col(0.85f);
            StatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            scroll.Add(StatusLabel);

            LassoToggle = new Toggle("Lasso Select") { value = false };
            LassoToggle.style.marginBottom = 4;
            scroll.Add(LassoToggle);

            scroll.Add(Separator());

            LocalLoaderSection = new VisualElement();
            LocalLoaderSection.style.marginBottom = 6;
            scroll.Add(LocalLoaderSection);

            // ── 部分インポートボタン（2列）
            var pImportRow = new VisualElement();
            pImportRow.style.flexDirection = FlexDirection.Row;
            pImportRow.style.marginBottom  = 2;
            PartialImportPmxBtn = MakeBtn("PMX部分Import"); PartialImportPmxBtn.style.flexGrow = 1; PartialImportPmxBtn.style.marginRight = 2;
            PartialImportMqoBtn = MakeBtn("MQO部分Import"); PartialImportMqoBtn.style.flexGrow = 1;
            pImportRow.Add(PartialImportPmxBtn); pImportRow.Add(PartialImportMqoBtn);
            scroll.Add(pImportRow);

            // ── 部分エクスポートボタン（2列）
            var pExportRow = new VisualElement();
            pExportRow.style.flexDirection = FlexDirection.Row;
            pExportRow.style.marginBottom  = 2;
            PartialExportPmxBtn = MakeBtn("PMX部分Export"); PartialExportPmxBtn.style.flexGrow = 1; PartialExportPmxBtn.style.marginRight = 2;
            PartialExportMqoBtn = MakeBtn("MQO部分Export"); PartialExportMqoBtn.style.flexGrow = 1;
            pExportRow.Add(PartialExportPmxBtn); pExportRow.Add(PartialExportMqoBtn);
            scroll.Add(pExportRow);

            // ── フルエクスポート / プロジェクトファイル
            // ── フルエクスポートボタン（2列）
            var fullExportRow = new VisualElement();
            fullExportRow.style.flexDirection = FlexDirection.Row;
            fullExportRow.style.marginBottom  = 2;
            FullExportPmxBtn = MakeBtn("PMXフルExport"); FullExportPmxBtn.style.flexGrow = 1; FullExportPmxBtn.style.marginRight = 2;
            FullExportMqoBtn = MakeBtn("MQOフルExport"); FullExportMqoBtn.style.flexGrow = 1;
            fullExportRow.Add(FullExportPmxBtn); fullExportRow.Add(FullExportMqoBtn);
            scroll.Add(fullExportRow);

            ProjectFileBtn = MakeBtn("プロジェクト保存/読込");
            scroll.Add(ProjectFileBtn);

            scroll.Add(Separator());

            RemoteSection = new VisualElement();
            RemoteSection.style.marginBottom = 4;
            ConnectBtn    = MakeBtn("Connect");
            DisconnectBtn = MakeBtn("Disconnect");
            FetchBtn      = MakeBtn("Fetch Project");
            RemoteSection.Add(ConnectBtn);
            RemoteSection.Add(DisconnectBtn);
            RemoteSection.Add(FetchBtn);
            scroll.Add(RemoteSection);

            scroll.Add(Separator());

            // ── ツール切り替えボタン（1行目：頂点移動 / オブジェ移動）
            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;
            toolRow.style.marginBottom  = 2;
            ToolVertexMoveBtn  = MakeBtn("頂点移動");     ToolVertexMoveBtn.style.flexGrow  = 1; ToolVertexMoveBtn.style.marginRight  = 2;
            ToolObjectMoveBtn  = MakeBtn("オブジェ移動"); ToolObjectMoveBtn.style.flexGrow  = 1;
            toolRow.Add(ToolVertexMoveBtn); toolRow.Add(ToolObjectMoveBtn);
            scroll.Add(toolRow);

            // ── ツール切り替えボタン（2行目：ピボット / スカルプト / 詳細選択）
            var toolRow2 = new VisualElement();
            toolRow2.style.flexDirection = FlexDirection.Row;
            toolRow2.style.marginBottom  = 2;
            ToolPivotOffsetBtn = MakeBtn("ピボット");    ToolPivotOffsetBtn.style.flexGrow = 1; ToolPivotOffsetBtn.style.marginRight = 2;
            ToolSculptBtn      = MakeBtn("スカルプト");  ToolSculptBtn.style.flexGrow      = 1; ToolSculptBtn.style.marginRight      = 2;
            ToolAdvancedSelBtn = MakeBtn("詳細選択");    ToolAdvancedSelBtn.style.flexGrow = 1;
            toolRow2.Add(ToolPivotOffsetBtn); toolRow2.Add(ToolSculptBtn); toolRow2.Add(ToolAdvancedSelBtn);
            scroll.Add(toolRow2);

            // ── ツール切り替えボタン（3行目：スキンウェイトペイント）
            ToolSkinWeightPaintBtn = MakeBtn("スキンWペイント");
            scroll.Add(ToolSkinWeightPaintBtn);

            scroll.Add(Separator());

            var undoRow = new VisualElement();
            undoRow.style.flexDirection = FlexDirection.Row;
            undoRow.style.marginBottom  = 6;
            UndoBtn = MakeBtn("Undo"); UndoBtn.style.flexGrow = 1; UndoBtn.style.marginRight = 2;
            RedoBtn = MakeBtn("Redo"); RedoBtn.style.flexGrow = 1; RedoBtn.style.marginLeft  = 2;
            undoRow.Add(UndoBtn); undoRow.Add(RedoBtn);
            scroll.Add(undoRow);

            scroll.Add(Separator());
            PrimitiveBtn = MakeBtn("図形生成");
            scroll.Add(PrimitiveBtn);

            MeshFilterToSkinnedBtn = MakeBtn("MF→Skinned");
            scroll.Add(MeshFilterToSkinnedBtn);

            BlendBtn = MakeBtn("メッシュブレンド");
            scroll.Add(BlendBtn);

            ModelBlendBtn = MakeBtn("モデルブレンド");
            scroll.Add(ModelBlendBtn);

            BoneEditorBtn = MakeBtn("ボーンエディタ");
            scroll.Add(BoneEditorBtn);

            UVEditorBtn = MakeBtn("UVエディタ");
            scroll.Add(UVEditorBtn);

            UVUnwrapBtn = MakeBtn("UV展開");
            scroll.Add(UVUnwrapBtn);

            scroll.Add(Separator());

            // ── 追加パネルボタン ──────────────────────────────────────────
            MaterialListBtn = MakeBtn("マテリアルリスト"); scroll.Add(MaterialListBtn);
            UVZBtn          = MakeBtn("UVZ");              scroll.Add(UVZBtn);

            var row9a = new VisualElement(); row9a.style.flexDirection = FlexDirection.Row; row9a.style.marginBottom = 2;
            PartsSelectionSetBtn = MakeBtn("パーツ選択辞書"); PartsSelectionSetBtn.style.flexGrow = 1; PartsSelectionSetBtn.style.marginRight = 2;
            MeshSelectionSetBtn  = MakeBtn("メッシュ選択辞書"); MeshSelectionSetBtn.style.flexGrow  = 1;
            row9a.Add(PartsSelectionSetBtn); row9a.Add(MeshSelectionSetBtn); scroll.Add(row9a);

            MergeMeshesBtn = MakeBtn("メッシュマージ"); scroll.Add(MergeMeshesBtn);
            MorphBtn       = MakeBtn("モーフエクスプレッション編集"); scroll.Add(MorphBtn);
            MorphCreateBtn = MakeBtn("差分からモーフ生成");         scroll.Add(MorphCreateBtn);

            var row9b = new VisualElement(); row9b.style.flexDirection = FlexDirection.Row; row9b.style.marginBottom = 2;
            TPoseBtn          = MakeBtn("Tポーズ変換");   TPoseBtn.style.flexGrow          = 1; TPoseBtn.style.marginRight          = 2;
            HumanoidMappingBtn = MakeBtn("ヒューマノイド"); HumanoidMappingBtn.style.flexGrow = 1;
            row9b.Add(TPoseBtn); row9b.Add(HumanoidMappingBtn); scroll.Add(row9b);

            MirrorBtn = MakeBtn("ミラー編集"); scroll.Add(MirrorBtn);

            // ── 追加パネルボタン（最終残件）─────────────────────────────────
            var rowFinal = new VisualElement(); rowFinal.style.flexDirection = FlexDirection.Row; rowFinal.style.marginBottom = 2;
            QuadDecimatorBtn = MakeBtn("Quad減面"); QuadDecimatorBtn.style.flexGrow = 1; QuadDecimatorBtn.style.marginRight = 2;
            MediaPipeBtn     = MakeBtn("MediaPipe"); MediaPipeBtn.style.flexGrow     = 1;
            rowFinal.Add(QuadDecimatorBtn); rowFinal.Add(MediaPipeBtn); scroll.Add(rowFinal);

            var rowFinal2 = new VisualElement(); rowFinal2.style.flexDirection = FlexDirection.Row; rowFinal2.style.marginBottom = 2;
            VMDTestBtn      = MakeBtn("VMDテスト");  VMDTestBtn.style.flexGrow      = 1; VMDTestBtn.style.marginRight      = 2;
            RemoteServerBtn = MakeBtn("リモートサーバ"); RemoteServerBtn.style.flexGrow = 1;
            rowFinal2.Add(VMDTestBtn); rowFinal2.Add(RemoteServerBtn); scroll.Add(rowFinal2);

            scroll.Add(Separator());
            scroll.Add(Header("Models"));
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
            scroll.Add(Header("Display"));
            ShowSelectedMeshToggle       = MakeToggle("Selected Mesh",        true);
            ShowUnselectedMeshToggle     = MakeToggle("Unselected Mesh",      true);
            ShowSelectedVerticesToggle   = MakeToggle("Selected Vertices",    true);
            ShowUnselectedVerticesToggle = MakeToggle("Unselected Vertices",  true);
            ShowSelectedWireToggle       = MakeToggle("Selected Wireframe",   true);
            ShowUnselectedWireToggle     = MakeToggle("Unselected Wireframe", true);
            scroll.Add(ShowSelectedMeshToggle);
            scroll.Add(ShowUnselectedMeshToggle);
            scroll.Add(ShowSelectedVerticesToggle);
            scroll.Add(ShowUnselectedVerticesToggle);
            scroll.Add(ShowSelectedWireToggle);
            scroll.Add(ShowUnselectedWireToggle);

            scroll.Add(Separator());
            scroll.Add(Header("Bone"));
            ShowSelectedBoneToggle   = MakeToggle("Selected Bone",   true);
            ShowUnselectedBoneToggle = MakeToggle("Unselected Bone", false);
            scroll.Add(ShowSelectedBoneToggle);
            scroll.Add(ShowUnselectedBoneToggle);

            scroll.Add(Separator());
            scroll.Add(Header("Rendering"));
            BackfaceCullingToggle = MakeToggle("Backface Culling", true);
            scroll.Add(BackfaceCullingToggle);

            return pane;
        }

        // ================================================================
        // ビューポートペイン
        // ================================================================

        private VisualElement BuildViewportPane(string label, out PlayerViewportPanel panel)
        {
            var wrap = new VisualElement();
            wrap.style.flexGrow        = 1;
            wrap.style.flexDirection   = FlexDirection.Column;
            wrap.style.backgroundColor = new StyleColor(Color.white);

            var lbl = new Label(label);
            lbl.style.position  = Position.Absolute;
            lbl.style.top       = 4;
            lbl.style.left      = 6;
            lbl.style.color     = new StyleColor(new Color(0.7f, 0.9f, 1f, 0.8f));
            lbl.style.fontSize  = 11;
            lbl.pickingMode     = PickingMode.Ignore;

            panel = new PlayerViewportPanel();
            wrap.Add(panel);
            wrap.Add(lbl);
            return wrap;
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

            // ── モデルリストセクション
            ModelListSection = new VisualElement();
            ModelListSection.style.marginBottom = 4;
            RightPaneContent.Add(ModelListSection);

            RightPaneContent.Add(Separator());

            // ── メッシュリストセクション
            MeshListSection = new VisualElement();
            MeshListSection.style.marginBottom = 4;
            RightPaneContent.Add(MeshListSection);

            RightPaneContent.Add(Separator());

            // ── オブジェクト移動TRSセクション（デフォルト非表示）
            ObjectMoveTRSSection = new VisualElement();
            ObjectMoveTRSSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(ObjectMoveTRSSection);

            RightPaneContent.Add(Separator());

            // ── 頂点移動サブパネルセクション（デフォルト非表示）
            VertexMoveSection = new VisualElement();
            VertexMoveSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(VertexMoveSection);

            RightPaneContent.Add(Separator());

            // ── ピボットオフセットサブパネルセクション（デフォルト非表示）
            PivotSection = new VisualElement();
            PivotSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(PivotSection);

            RightPaneContent.Add(Separator());

            // ── スカルプトサブパネルセクション（デフォルト非表示）
            SculptSection = new VisualElement();
            SculptSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(SculptSection);

            RightPaneContent.Add(Separator());

            // ── 詳細選択サブパネルセクション（デフォルト非表示）
            AdvancedSelectSection = new VisualElement();
            AdvancedSelectSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(AdvancedSelectSection);

            RightPaneContent.Add(Separator());

            // ── スキンウェイトペイントセクション（デフォルト非表示）
            SkinWeightPaintSection = new VisualElement();
            SkinWeightPaintSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(SkinWeightPaintSection);

            RightPaneContent.Add(Separator());

            // ── ブレンドセクション（デフォルト非表示）
            BlendSection = new VisualElement();
            BlendSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(BlendSection);

            RightPaneContent.Add(Separator());

            // ── モデルブレンドセクション（デフォルト非表示）
            ModelBlendSection = new VisualElement();
            ModelBlendSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(ModelBlendSection);

            RightPaneContent.Add(Separator());

            // ── ボーンエディタセクション（デフォルト非表示）
            BoneEditorSection = new VisualElement();
            BoneEditorSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(BoneEditorSection);

            RightPaneContent.Add(Separator());

            UVEditorSection = new VisualElement();
            UVEditorSection.style.display = DisplayStyle.None;
            UVEditorSection.style.flexGrow = 1;
            RightPaneContent.Add(UVEditorSection);

            RightPaneContent.Add(Separator());

            UVUnwrapSection = new VisualElement();
            UVUnwrapSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(UVUnwrapSection);

            RightPaneContent.Add(Separator());

            // ── 追加パネルセクション群（デフォルト非表示）────────────────
            MaterialListSection    = MakeHiddenSection(); RightPaneContent.Add(MaterialListSection);    RightPaneContent.Add(Separator());
            UVZSection             = MakeHiddenSection(); RightPaneContent.Add(UVZSection);             RightPaneContent.Add(Separator());
            PartsSelectionSetSection = MakeHiddenSection(); RightPaneContent.Add(PartsSelectionSetSection); RightPaneContent.Add(Separator());
            MeshSelectionSetSection  = MakeHiddenSection(); RightPaneContent.Add(MeshSelectionSetSection);  RightPaneContent.Add(Separator());
            MergeMeshesSection     = MakeHiddenSection(); RightPaneContent.Add(MergeMeshesSection);     RightPaneContent.Add(Separator());
            MorphSection           = MakeHiddenSection(); RightPaneContent.Add(MorphSection);           RightPaneContent.Add(Separator());
            MorphCreateSection     = MakeHiddenSection(); RightPaneContent.Add(MorphCreateSection);     RightPaneContent.Add(Separator());
            TPoseSection           = MakeHiddenSection(); RightPaneContent.Add(TPoseSection);           RightPaneContent.Add(Separator());
            HumanoidMappingSection = MakeHiddenSection(); RightPaneContent.Add(HumanoidMappingSection); RightPaneContent.Add(Separator());
            MirrorSection          = MakeHiddenSection(); RightPaneContent.Add(MirrorSection);          RightPaneContent.Add(Separator());
            QuadDecimatorSection   = MakeHiddenSection(); RightPaneContent.Add(QuadDecimatorSection);   RightPaneContent.Add(Separator());
            MediaPipeSection       = MakeHiddenSection(); RightPaneContent.Add(MediaPipeSection);       RightPaneContent.Add(Separator());
            VMDTestSection         = MakeHiddenSection(); RightPaneContent.Add(VMDTestSection);         RightPaneContent.Add(Separator());
            RemoteServerSection    = MakeHiddenSection(); RightPaneContent.Add(RemoteServerSection);    RightPaneContent.Add(Separator());
            ExportSection = new VisualElement();
            ExportSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(ExportSection);

            RightPaneContent.Add(Separator());

            // ── プロジェクトファイルセクション（デフォルト非表示）
            ProjectFileSection = new VisualElement();
            ProjectFileSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(ProjectFileSection);

            RightPaneContent.Add(Separator());

            // ── 部分インポートセクション（デフォルト非表示）
            PartialImportSection = new VisualElement();
            PartialImportSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(PartialImportSection);

            RightPaneContent.Add(Separator());

            // ── 部分エクスポートセクション（デフォルト非表示）
            PartialExportSection = new VisualElement();
            PartialExportSection.style.display = DisplayStyle.None;
            RightPaneContent.Add(PartialExportSection);

            RightPaneContent.Add(Separator());

            // ── インポートセクション
            ImportSection = new VisualElement();
            RightPaneContent.Add(ImportSection);

            // ── 図形生成セクション（ScrollView外に配置してWheelEventを確保）
            PrimitiveSection = new VisualElement();
            PrimitiveSection.style.flexShrink = 0;
            pane.Add(PrimitiveSection);

            // ── MeshFilter→Skinnedセクション（ScrollView外）
            MeshFilterToSkinnedSection = new VisualElement();
            MeshFilterToSkinnedSection.style.flexShrink  = 0;
            MeshFilterToSkinnedSection.style.paddingTop  = 4;
            MeshFilterToSkinnedSection.style.paddingLeft = 4;
            MeshFilterToSkinnedSection.style.paddingRight= 4;
            pane.Add(MeshFilterToSkinnedSection);

            return pane;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static VisualElement MakeHiddenSection()
        {
            var v = new VisualElement();
            v.style.display = DisplayStyle.None;
            return v;
        }

        private static VisualElement MakePane(float initialWidth)
        {
            var v = new VisualElement();
            v.style.width    = initialWidth;
            v.style.minWidth = 80f;
            return v;
        }

        /// <summary>
        /// 全Build()完了後に呼ぶ。
        /// RightPaneContent の color=white 継承でボタン文字も白になるため、
        /// レイアウト全体のButtonを明示的に暗色に戻す。
        /// </summary>
        public void PostBuildButtonColors(UnityEngine.UIElements.VisualElement root)
        {
            var dark = new StyleColor(new Color(0.12f, 0.12f, 0.12f));
            root.Query<Button>().ForEach(b => b.style.color = dark);
            root.Query<TextField>().ForEach(t => t.style.color = dark);
            root.Query<FloatField>().ForEach(t => t.style.color = dark);
            root.Query<IntegerField>().ForEach(t => t.style.color = dark);
            root.Query<DropdownField>().ForEach(t => t.style.color = dark);
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
            t.style.color        = Col(0.85f);
            return t;
        }

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 3;
            l.style.color        = Col(0.6f);
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

        private static StyleColor PaneBg(float v) => new StyleColor(new Color(v, v, v, 1f));
        private static StyleColor Col(float v)    => new StyleColor(new Color(v, v, v, 1f));
    }
}
