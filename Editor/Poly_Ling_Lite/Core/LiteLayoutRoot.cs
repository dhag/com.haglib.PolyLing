// LiteLayoutRoot.cs
// PolyLing Lite ウィンドウの UIToolkit レイアウト構築
// 左ペイン（ボタン群）+ 右ペイン（サブパネルセクション）の 2 ペイン構造
//
// Editor/Poly_Ling_Lite/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Lite
{
    public class LiteLayoutRoot
    {
        // ================================================================
        // 左ペイン ボタン
        // ================================================================

        public Button BtnImport          { get; private set; }
        public Button BtnImportMqo       { get; private set; }
        public Button BtnHierarchyExport { get; private set; }
        public Button BtnModelList       { get; private set; }
        public Button BtnMeshList        { get; private set; }

        // ================================================================
        // 右ペイン セクション
        // ================================================================

        public VisualElement ImportSection          { get; private set; }
        public VisualElement HierarchyExportSection { get; private set; }
        public VisualElement ModelListSection       { get; private set; }
        public VisualElement MeshListSection        { get; private set; }

        // ================================================================
        // ステータスバー
        // ================================================================

        private Label _statusLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement root)
        {
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow      = 1;

            // ── ステータスバー（最上部）──────────────────────────────
            _statusLabel = new Label("未ロード");
            _statusLabel.style.height          = 20;
            _statusLabel.style.paddingLeft     = 6;
            _statusLabel.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
            _statusLabel.style.color           = new StyleColor(new Color(0.7f, 0.8f, 1f));
            _statusLabel.style.fontSize        = 10;
            _statusLabel.style.unityTextAlign  = TextAnchor.MiddleLeft;
            root.Add(_statusLabel);

            // ── 水平 2 ペイン ─────────────────────────────────────────
            var hPane = new VisualElement();
            hPane.style.flexDirection = FlexDirection.Row;
            hPane.style.flexGrow      = 1;
            root.Add(hPane);

            // 左ペイン
            var leftPane = BuildLeftPane();
            hPane.Add(leftPane);

            // セパレータ
            var sep = new VisualElement();
            sep.style.width           = 1;
            sep.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.1f));
            hPane.Add(sep);

            // 右ペイン
            var rightPane = BuildRightPane();
            rightPane.style.flexGrow = 1;
            hPane.Add(rightPane);
        }

        // ================================================================
        // 左ペイン構築
        // ================================================================

        private VisualElement BuildLeftPane()
        {
            var pane = new VisualElement();
            pane.style.width         = 110;
            pane.style.paddingTop    = 6;
            pane.style.paddingBottom = 6;
            pane.style.paddingLeft   = 4;
            pane.style.paddingRight  = 4;
            pane.style.flexShrink    = 0;
            pane.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));

            void AddSectionLabel(string text)
            {
                var l = new Label(text);
                l.style.marginTop    = 8;
                l.style.marginBottom = 2;
                l.style.color        = new StyleColor(new Color(0.55f, 0.75f, 1f));
                l.style.fontSize     = 9;
                pane.Add(l);
            }

            // ── ファイル ──────────────────────────────────────────
            AddSectionLabel("ファイル");
            BtnImport    = AddLeftBtn(pane, "PMX読込");
            BtnImportMqo = AddLeftBtn(pane, "MQO読込");

            // ── エクスポート ──────────────────────────────────────
            AddSectionLabel("エクスポート");
            BtnHierarchyExport = AddLeftBtn(pane, "ヒエラルキー");

            // ── リスト ────────────────────────────────────────────
            AddSectionLabel("リスト");
            BtnModelList = AddLeftBtn(pane, "モデル一覧");
            BtnMeshList  = AddLeftBtn(pane, "メッシュ一覧");

            return pane;
        }

        private static Button AddLeftBtn(VisualElement parent, string label)
        {
            var btn = new Button { text = label };
            btn.style.marginBottom = 2;
            btn.style.height       = 24;
            btn.style.fontSize     = 10;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            parent.Add(btn);
            return btn;
        }

        // ================================================================
        // 右ペイン構築
        // ================================================================

        private VisualElement BuildRightPane()
        {
            var pane = new ScrollView();
            pane.style.flexGrow    = 1;
            pane.style.paddingLeft = 8;
            pane.style.paddingTop  = 6;

            ImportSection          = AddSection(pane);
            HierarchyExportSection = AddSection(pane);
            ModelListSection       = AddSection(pane);
            MeshListSection        = AddSection(pane);

            return pane;
        }

        private static VisualElement AddSection(VisualElement parent)
        {
            var s = new VisualElement();
            s.style.display = DisplayStyle.None;
            s.style.flexGrow = 1;
            parent.Add(s);
            return s;
        }

        // ================================================================
        // ステータス更新
        // ================================================================

        public void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
        }
    }
}
