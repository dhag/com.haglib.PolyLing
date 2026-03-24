// PlayerLayoutRoot.cs
// UIToolkit による3ペインレイアウト構築。
// 水平分割①: Left | (Center + Right)
// 水平分割②: Center | Right
// 水平分割③: Center-Left(3D) | Center-Right(Top + Front)
// 垂直分割④: Top | Front
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 3ペインレイアウトを UIToolkit で構築し、
    /// Viewer が配線するための UI 要素参照を公開する。
    /// </summary>
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

        // ================================================================
        // ビューポートパネル公開
        // ================================================================

        public PlayerViewportPanel PerspectivePanel { get; private set; }
        public PlayerViewportPanel TopPanel         { get; private set; }
        public PlayerViewportPanel FrontPanel       { get; private set; }

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

        // ================================================================
        // Build
        // ================================================================

        /// <summary>
        /// 全ペインを構築して root に追加する。
        /// </summary>
        public void Build(VisualElement root)
        {
            // ルートを画面全体に広げる
            root.style.flexDirection = FlexDirection.Row;
            root.style.width         = new StyleLength(new Length(100, LengthUnit.Percent));
            root.style.height        = new StyleLength(new Length(100, LengthUnit.Percent));

            // ① Left | (Center + Right) 水平分割
            var splitLCR = new TwoPaneSplitView(0, 200f, TwoPaneSplitViewOrientation.Horizontal);
            splitLCR.style.flexGrow = 1;
            root.Add(splitLCR);

            var leftPane = BuildLeftPane();
            splitLCR.Add(leftPane);

            // ② Center | Right 水平分割（固定幅は Right 側）
            var splitCR = new TwoPaneSplitView(1, 220f, TwoPaneSplitViewOrientation.Horizontal);
            splitCR.style.flexGrow = 1;
            splitLCR.Add(splitCR);

            // ③ Center-Left(3D) | Center-Right(Top+Front) 水平分割
            var splitCenter = new TwoPaneSplitView(1, 240f, TwoPaneSplitViewOrientation.Horizontal);
            splitCenter.style.flexGrow = 1;
            splitCR.Add(splitCenter);

            PlayerViewportPanel perspPanel, topPanel, frontPanel;
            var perspPane = BuildViewportPane("Perspective", out perspPanel);
            PerspectivePanel = perspPanel;
            splitCenter.Add(perspPane);

            // ④ Top | Front 垂直分割
            var splitTopFront = new TwoPaneSplitView(0, 0f, TwoPaneSplitViewOrientation.Vertical);
            splitTopFront.style.flexGrow = 1;
            splitCenter.Add(splitTopFront);

            var topPane   = BuildViewportPane("TOP",   out topPanel);   TopPanel   = topPanel;
            var frontPane = BuildViewportPane("Front", out frontPanel); FrontPanel = frontPanel;
            splitTopFront.Add(topPane);
            splitTopFront.Add(frontPane);

            var rightPane = BuildRightPane();
            splitCR.Add(rightPane);
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

            // ステータス
            StatusLabel = new Label("Status: -");
            StatusLabel.style.marginBottom   = 6;
            StatusLabel.style.color          = Col(0.85f);
            StatusLabel.style.whiteSpace     = WhiteSpace.Normal;
            pane.Add(StatusLabel);

            pane.Add(Separator());

            // ローカルロードセクション（Viewer が _localLoader.BuildUI を追加する場所）
            LocalLoaderSection = new VisualElement();
            LocalLoaderSection.style.marginBottom = 6;
            pane.Add(LocalLoaderSection);

            pane.Add(Separator());

            // リモートセクション
            RemoteSection = new VisualElement();
            RemoteSection.style.marginBottom = 4;

            ConnectBtn = MakeBtn("Connect");
            DisconnectBtn = MakeBtn("Disconnect");
            FetchBtn = MakeBtn("Fetch Project");
            RemoteSection.Add(ConnectBtn);
            RemoteSection.Add(DisconnectBtn);
            RemoteSection.Add(FetchBtn);
            pane.Add(RemoteSection);

            pane.Add(Separator());

            // Undo / Redo
            var undoRow = new VisualElement();
            undoRow.style.flexDirection  = FlexDirection.Row;
            undoRow.style.marginBottom   = 6;
            UndoBtn = MakeBtn("Undo");
            RedoBtn = MakeBtn("Redo");
            UndoBtn.style.flexGrow    = 1;
            UndoBtn.style.marginRight = 2;
            RedoBtn.style.flexGrow    = 1;
            RedoBtn.style.marginLeft  = 2;
            undoRow.Add(UndoBtn);
            undoRow.Add(RedoBtn);
            pane.Add(undoRow);

            pane.Add(Separator());

            // モデルリスト
            pane.Add(Header("Models"));
            ModelListContainer = new VisualElement();
            pane.Add(ModelListContainer);

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

            // ラベル（左上）
            var lbl = new Label(label);
            lbl.style.position        = Position.Absolute;
            lbl.style.top             = 4;
            lbl.style.left            = 6;
            lbl.style.color           = new StyleColor(new Color(0.7f, 0.9f, 1f, 0.8f));
            lbl.style.fontSize        = 11;
            lbl.pickingMode           = PickingMode.Ignore;

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
            pane.style.paddingTop      = 8;
            pane.style.paddingBottom   = 8;
            pane.style.paddingLeft     = 6;
            pane.style.paddingRight    = 6;
            pane.style.overflow        = Overflow.Hidden;

            pane.Add(Header("Display"));

            ShowSelectedMeshToggle       = MakeToggle("Selected Mesh",       true);
            ShowUnselectedMeshToggle     = MakeToggle("Unselected Mesh",     true);
            ShowSelectedVerticesToggle   = MakeToggle("Selected Vertices",   true);
            ShowUnselectedVerticesToggle = MakeToggle("Unselected Vertices", true);
            ShowSelectedWireToggle       = MakeToggle("Selected Wireframe",  true);
            ShowUnselectedWireToggle     = MakeToggle("Unselected Wireframe",true);

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
        // UIヘルパー
        // ================================================================

        private static VisualElement MakePane(float initialWidth)
        {
            var v = new VisualElement();
            v.style.width    = initialWidth;
            v.style.minWidth = 80f;
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
            t.style.color = Col(0.85f);
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
