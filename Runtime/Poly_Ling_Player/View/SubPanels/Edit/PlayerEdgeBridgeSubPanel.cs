// PlayerEdgeBridgeSubPanel.cs
// 辺群ブリッジの Player 版サブパネル（UIToolkit）。
// ビューポートで辺を拾い（1 辺クリック／矩形選択）、2 か所の辺群の間に面を張る。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// A / B を別々に取り込むボタンは持たない。拾った辺の連結関係で 2 領域を判別する
// （EdgeChainOps.SplitIntoTwoChains）。3 群以上・分岐ありは実行できない。

using System;
using Poly_Ling.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerEdgeBridgeSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public Poly_Ling.Data.IToolSurface Surface;
        private const string Tool = "edgeBridge";

        /// <summary>「面を張る」を押したときに Viewer が実行する。</summary>
        public Action OnExecute;

        /// <summary>「頂点数を合わせる」を押したときに Viewer が実行する。</summary>
        public Action OnMatchCount;

        // ================================================================
        // UI 要素
        // ================================================================

        // UI 自動操作の ID は "edgeBridge.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl(Ignore = true)]
        private VisualElement _sectionEl;

        [UiControl("boundaryOnly", Description = "境界辺のみを対象にする")]
        private Toggle       _boundaryOnlyToggle;
        [UiControl("autoCorrespondence", Description = "2 つの辺群の対応を自動で合わせる")]
        private Toggle       _autoCorrespToggle;
        [UiControl("flipCorrespondence", Description = "対応を反転する")]
        private Toggle       _flipCorrespToggle;
        [UiControl("flipFaces", Description = "張る面の裏表を反転する")]
        private Toggle       _flipFacesToggle;
        [UiControl("subdivisions", Description = "ブリッジ面の分割数")]
        private IntegerField _subdivField;

        [UiControl("picks", Safety = UiSafety.ReadOnly, Description = "拾った辺の数")]
        private Label  _pickLabel;
        [UiControl("groups", Safety = UiSafety.ReadOnly, Description = "拾った辺のまとまりの判定結果")]
        private Label  _groupLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "実行できない理由、または直近の実行結果")]
        private Label  _statusLabel;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "拾った 2 か所の辺群の間に面を張る")]
        private Button _executeBtn;
        [UiControl("clearPicks", Safety = UiSafety.SafeWrite, Description = "拾った辺を捨てる（メッシュは変えない）")]
        private Button _clearPicksBtn;

        [UiControl("matchBase", Description = "頂点数合わせの基準（辺群① / 辺群②）")]
        private RadioButtonGroup _matchBaseGroup;
        [UiControl("matchSplitTri", Description = "頂点数合わせで三角形を三角形へ分割する")]
        private Toggle _matchSplitTriToggle;
        [UiControl("matchInfo", Safety = UiSafety.ReadOnly, Description = "頂点数合わせの内容、または実行できない理由")]
        private Label  _matchLabel;
        [UiControl("matchCount", Safety = UiSafety.SafeWrite, Description = "基準側に合わせてもう一方の辺群の頂点数を揃える")]
        private Button _matchBtn;

        /// <summary>直近の実行結果。Refresh で消さずに残す。</summary>
        private string _lastResult = "";

        /// <summary>実行結果を外から差し込む（Viewer が使う）。</summary>
        public void SetStatus(string text)
        {
            _lastResult = text ?? "";
            Refresh();
        }

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _sectionEl = parent;

            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Edge Bridge / 辺群ブリッジ"));
            _root.Add(new HelpBox(
                "2 か所の辺群の間に面を張ります。\n" +
                "ビューポートで辺をクリックすると 1 本ずつ拾えます（もう一度押すと外れます）。\n" +
                "ドラッグで矩形選択でき、A 側と B 側をまとめて囲っても構いません。\n" +
                "拾った辺は連結関係で 2 つの辺群に分けます。3 か所以上や枝分かれは実行できません。\n" +
                "Shift ドラッグで追加、Ctrl ドラッグで反転、Escape で全て捨てます。",
                HelpBoxMessageType.Info));

            // ── 対象の辺 ──
            _root.Add(SectionLabel("拾う辺"));

            _boundaryOnlyToggle = new Toggle("境界辺のみを対象にする") { value = true };
            _boundaryOnlyToggle.style.fontSize = 11;
            _boundaryOnlyToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "boundaryEdgeOnly", e.newValue);
                Refresh();
            });
            _root.Add(_boundaryOnlyToggle);

            var boundaryHint = new Label(
                "ON: 1 面だけが使う辺（穴の縁・開いた面の外周）だけを拾います。\n" +
                "OFF: 2 面が共有する内部の辺も拾えます。この場合は面の裏表を自動判定できないため、\n" +
                "下の「面の裏表を反転」で合わせてください。");
            boundaryHint.style.fontSize     = 10;
            boundaryHint.style.whiteSpace   = WhiteSpace.Normal;
            boundaryHint.style.marginBottom = 4;
            _root.Add(boundaryHint);

            _pickLabel = InfoLabel();
            _root.Add(_pickLabel);

            _groupLabel = InfoLabel();
            _root.Add(_groupLabel);

            _root.Add(_clearPicksBtn = ActionButton("拾った辺を捨てる", () =>
            {
                Surface?.Invoke(Tool, "clearPicks");
                _lastResult = "";
                Refresh();
            }));

            // ── 面の張り方 ──
            _root.Add(SectionLabel("面の張り方"));

            _autoCorrespToggle = new Toggle("対応を自動で合わせる") { value = true };
            _autoCorrespToggle.style.fontSize = 11;
            _autoCorrespToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "autoCorrespondence", e.newValue);
                Refresh();
            });
            _root.Add(_autoCorrespToggle);

            var autoHint = new Label(
                "開いた辺群は端どうしが近くなる向きに、閉じた辺群は最も近い頂点どうしが\n" +
                "先頭に来るように合わせます。ねじれるときは下の反転で直してください。");
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 4;
            _root.Add(autoHint);

            _flipCorrespToggle = new Toggle("対応を反転") { value = false };
            _flipCorrespToggle.style.fontSize = 11;
            _flipCorrespToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "flipCorrespondence", e.newValue);
                Refresh();
            });
            _root.Add(_flipCorrespToggle);

            _flipFacesToggle = new Toggle("面の裏表を反転") { value = false };
            _flipFacesToggle.style.fontSize = 11;
            _flipFacesToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "flipFaces", e.newValue);
                Refresh();
            });
            _root.Add(_flipFacesToggle);

            _subdivField = new IntegerField("分割数") { value = 0 };
            _subdivField.style.fontSize  = 11;
            _subdivField.style.marginTop = 4;
            if (_subdivField.labelElement != null)
            {
                _subdivField.labelElement.style.minWidth = 0;
                _subdivField.labelElement.style.flexGrow = 0;
            }
            _subdivField.RegisterValueChangedCallback(e =>
            {
                if (Surface == null) return;
                _subdivField.SetValueWithoutNotify(Surface.Set(Tool, "subdivisions", e.newValue));
                Refresh();
            });
            _root.Add(_subdivField);

            var subdivHint = new Label("A→B の間に中間の列を足します。0 で分割なし。");
            subdivHint.style.fontSize     = 10;
            subdivHint.style.whiteSpace   = WhiteSpace.Normal;
            subdivHint.style.marginBottom = 4;
            _root.Add(subdivHint);

            // ── 頂点数合わせ ──
            _root.Add(SectionLabel("頂点数合わせ"));

            var matchHint = new Label(
                "頂点数が違っても面は張れますが、余りが三角形になります。\n" +
                "基準側に合わせてもう一方の辺を割る（長い辺から）か潰す（短い辺から）と、全部四角形になります。\n" +
                "開いた辺群の端点は動かしません。境界辺だけの辺群で使えます。");
            matchHint.style.fontSize     = 10;
            matchHint.style.whiteSpace   = WhiteSpace.Normal;
            matchHint.style.marginBottom = 4;
            _root.Add(matchHint);

            _matchBaseGroup = new RadioButtonGroup("基準",
                new System.Collections.Generic.List<string> { "辺群①", "辺群②" }) { value = 0 };
            _matchBaseGroup.style.fontSize = 11;
            _matchBaseGroup.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "matchBaseIsB", e.newValue == 1);
                Refresh();
            });
            _root.Add(_matchBaseGroup);

            _matchSplitTriToggle = new Toggle("三角形は三角形に割る") { value = true };
            _matchSplitTriToggle.style.fontSize = 11;
            _matchSplitTriToggle.RegisterValueChangedCallback(e =>
            {
                Surface.Set(Tool, "splitTriangleIntoTriangles", e.newValue);
                Refresh();
            });
            _root.Add(_matchSplitTriToggle);

            _matchLabel = InfoLabel();
            _root.Add(_matchLabel);

            _root.Add(_matchBtn = ActionButton("頂点数を合わせる", () =>
            {
                OnMatchCount?.Invoke();
                Refresh();
            }));

            // ── 実行 ──
            _executeBtn = new Button(() =>
            {
                OnExecute?.Invoke();
                Refresh();
            })
            { text = "面を張る" };
            _executeBtn.style.height    = 30;
            _executeBtn.style.marginTop = 6;
            _root.Add(_executeBtn);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);

            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (Surface == null) return;

            bool auto = Surface.GetBool(Tool, "autoCorrespondence", true);
            int picked = Surface.GetInt(Tool, "pickedEdgeCount");

            _boundaryOnlyToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "boundaryEdgeOnly", true));
            _autoCorrespToggle ?.SetValueWithoutNotify(auto);
            _flipCorrespToggle ?.SetValueWithoutNotify(Surface.GetBool(Tool, "flipCorrespondence"));
            _flipFacesToggle   ?.SetValueWithoutNotify(Surface.GetBool(Tool, "flipFaces"));
            _subdivField       ?.SetValueWithoutNotify(Surface.GetInt(Tool, "subdivisions"));
            _matchBaseGroup    ?.SetValueWithoutNotify(Surface.GetBool(Tool, "matchBaseIsB") ? 1 : 0);
            _matchSplitTriToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "splitTriangleIntoTriangles", true));

            // 自動判定は境界辺でしか効かない面反転を含まないため、
            // 内部辺を含む場合でも自動対応そのものは使える。表示だけ補足する。
            if (_flipCorrespToggle != null)
                _flipCorrespToggle.label = auto ? "対応を反転（自動判定の上書き）" : "対応を反転";

            if (_pickLabel != null)
                _pickLabel.text = picked == 0
                    ? "拾った辺：なし"
                    : $"拾った辺：{picked} 本";

            var sum = Surface.GetGroup(Tool, "inspect");
            bool ok = sum.Item("ok", false);
            string message = sum.Item("message", "");

            if (_groupLabel != null)
                _groupLabel.text = ok ? message : "";

            if (_statusLabel != null)
            {
                string reject = Surface.GetString(Tool, "lastRejectReason");
                if (!string.IsNullOrEmpty(reject))          _statusLabel.text = reject;
                else if (!string.IsNullOrEmpty(_lastResult)) _statusLabel.text = _lastResult;
                else if (!ok)                                _statusLabel.text = message;
                else                                         _statusLabel.text = "";
            }

            _executeBtn?.SetEnabled(ok);

            bool canMatch = ok && sum.Item("canMatch", false);
            if (_matchLabel != null)
                _matchLabel.text = ok ? sum.Item("matchMessage", "") : "";
            _matchBtn?.SetEnabled(canMatch);
        }

        // ================================================================
        // セクション可視判定（オーバーレイの出し分けに使う）
        // ================================================================

        /// <summary>このパネルのセクションが右ペインに表示されているか。</summary>
        public bool IsSectionVisible()
        {
            if (_sectionEl == null) return false;
            return _sectionEl.resolvedStyle.display != DisplayStyle.None;
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize     = 11;
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            return l;
        }

        private static Button ActionButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height    = 22;
            b.style.marginTop = 2;
            return b;
        }
    }
}
