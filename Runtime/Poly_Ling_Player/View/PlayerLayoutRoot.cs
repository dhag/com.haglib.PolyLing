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

        /// <summary>左ペイン：MeshFilter→Skinnedボタン。</summary>
        public Button MeshFilterToSkinnedBtn { get; private set; }

        /// <summary>右ペイン：MeshFilter→Skinnedセクション（ScrollView外）。</summary>
        public VisualElement MeshFilterToSkinnedSection { get; private set; }

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
            pane.style.paddingTop      = 8;
            pane.style.paddingBottom   = 8;
            pane.style.paddingLeft     = 6;
            pane.style.paddingRight    = 6;
            pane.style.overflow        = Overflow.Hidden;

            StatusLabel = new Label("Status: -");
            StatusLabel.style.marginBottom = 6;
            StatusLabel.style.color        = Col(0.85f);
            StatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            pane.Add(StatusLabel);

            pane.Add(Separator());

            LocalLoaderSection = new VisualElement();
            LocalLoaderSection.style.marginBottom = 6;
            pane.Add(LocalLoaderSection);

            pane.Add(Separator());

            RemoteSection = new VisualElement();
            RemoteSection.style.marginBottom = 4;
            ConnectBtn    = MakeBtn("Connect");
            DisconnectBtn = MakeBtn("Disconnect");
            FetchBtn      = MakeBtn("Fetch Project");
            RemoteSection.Add(ConnectBtn);
            RemoteSection.Add(DisconnectBtn);
            RemoteSection.Add(FetchBtn);
            pane.Add(RemoteSection);

            pane.Add(Separator());

            // ── ツール切り替えボタン（頂点移動 / オブジェクト移動）
            var toolRow = new VisualElement();
            toolRow.style.flexDirection = FlexDirection.Row;
            toolRow.style.marginBottom  = 2;
            ToolVertexMoveBtn = MakeBtn("頂点移動"); ToolVertexMoveBtn.style.flexGrow = 1; ToolVertexMoveBtn.style.marginRight = 2;
            ToolObjectMoveBtn = MakeBtn("オブジェ移動"); ToolObjectMoveBtn.style.flexGrow = 1;
            toolRow.Add(ToolVertexMoveBtn); toolRow.Add(ToolObjectMoveBtn);
            pane.Add(toolRow);

            pane.Add(Separator());

            var undoRow = new VisualElement();
            undoRow.style.flexDirection = FlexDirection.Row;
            undoRow.style.marginBottom  = 6;
            UndoBtn = MakeBtn("Undo"); UndoBtn.style.flexGrow = 1; UndoBtn.style.marginRight = 2;
            RedoBtn = MakeBtn("Redo"); RedoBtn.style.flexGrow = 1; RedoBtn.style.marginLeft  = 2;
            undoRow.Add(UndoBtn); undoRow.Add(RedoBtn);
            pane.Add(undoRow);

            pane.Add(Separator());
            PrimitiveBtn = MakeBtn("図形生成");
            pane.Add(PrimitiveBtn);

            MeshFilterToSkinnedBtn = MakeBtn("MF→Skinned");
            pane.Add(MeshFilterToSkinnedBtn);

            pane.Add(Separator());
            pane.Add(Header("Models"));
            ModelListContainer = new VisualElement();
            pane.Add(ModelListContainer);

            var listBtnRow = new VisualElement();
            listBtnRow.style.flexDirection = FlexDirection.Row;
            listBtnRow.style.marginTop     = 4;
            ModelListBtn = MakeBtn("モデルリスト"); ModelListBtn.style.flexGrow = 1; ModelListBtn.style.marginRight = 2;
            MeshListBtn  = MakeBtn("オブジェクトリスト"); MeshListBtn.style.flexGrow = 1;
            listBtnRow.Add(ModelListBtn);
            listBtnRow.Add(MeshListBtn);
            pane.Add(listBtnRow);

            pane.Add(Separator());
            pane.Add(Header("Display"));
            ShowSelectedMeshToggle       = MakeToggle("Selected Mesh",        true);
            ShowUnselectedMeshToggle     = MakeToggle("Unselected Mesh",      true);
            ShowSelectedVerticesToggle   = MakeToggle("Selected Vertices",    true);
            ShowUnselectedVerticesToggle = MakeToggle("Unselected Vertices",  true);
            ShowSelectedWireToggle       = MakeToggle("Selected Wireframe",   true);
            ShowUnselectedWireToggle     = MakeToggle("Unselected Wireframe", true);
            pane.Add(ShowSelectedMeshToggle);
            pane.Add(ShowUnselectedMeshToggle);
            pane.Add(ShowSelectedVerticesToggle);
            pane.Add(ShowUnselectedVerticesToggle);
            pane.Add(ShowSelectedWireToggle);
            pane.Add(ShowUnselectedWireToggle);

            pane.Add(Separator());
            pane.Add(Header("Bone"));
            ShowSelectedBoneToggle   = MakeToggle("Selected Bone",   true);
            ShowUnselectedBoneToggle = MakeToggle("Unselected Bone", false);
            pane.Add(ShowSelectedBoneToggle);
            pane.Add(ShowUnselectedBoneToggle);

            pane.Add(Separator());
            pane.Add(Header("Rendering"));
            BackfaceCullingToggle = MakeToggle("Backface Culling", true);
            pane.Add(BackfaceCullingToggle);

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
            wrap.style.backgroundColor = new StyleColor(Color.black);

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

        private static VisualElement MakePane(float initialWidth)
        {
            var v = new VisualElement();
            v.style.width      = initialWidth;
            v.style.minWidth   = 80f;
            v.style.flexShrink = 0;
            return v;
        }

        private static Button MakeBtn(string text)
        {
            var b = new Button { text = text };
            b.style.marginBottom = 3;
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
