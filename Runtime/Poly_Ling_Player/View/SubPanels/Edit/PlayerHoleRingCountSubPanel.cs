// PlayerHoleRingCountSubPanel.cs
// HoleRingCountTool の Player 版サブパネル（UIToolkit）。
// 「基準穴」「対象穴」をブリッジと同じやり方（エッジ上の頂点／辺を選んで取り込み）で
// 指定し、対象穴の頂点数を基準穴に合わせる。自動選択は持たない。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerHoleRingCountSubPanel : IHoleSeedSource
    {
        public Func<HoleRingCountToolHandler> GetH;

        /// <summary>「頂点数を合わせる」の実行。コマンドの組み立てと送信は Viewer 側が持つ。</summary>
        public Action OnExecute;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private VisualElement _sectionEl;
        private Label         _baseLabel;
        private Label         _targetLabel;
        private Label         _diffLabel;
        private Label         _statusLabel;
        private Toggle        _splitTriToggle;
        private Button        _executeBtn;

        /// <summary>直近の実行結果。Refresh で消さずに残す。</summary>
        private string _lastResult = "";

        /// <summary>実行結果の説明を外から入れる。コマンドの実行側が呼ぶ。</summary>
        public void SetResult(string message)
        {
            _lastResult = message ?? "";
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

            _root.Add(Header("Hole Ring Count / 穴頂点数合わせ"));
            _root.Add(new HelpBox(
                "ブリッジの「2つの穴の頂点数が同じ」制約を満たすための前処理です。\n" +
                "基準穴の頂点数に合わせて、対象穴の頂点数だけを増減させます。基準穴は変わりません。\n" +
                "エッジ（1面だけが使う辺）の上の頂点か辺をビューポートで選び、下のボタンで取り込みます。\n" +
                "対象が少ないときは辺の長い順に割り、多いときは辺の短い順に潰します。",
                HelpBoxMessageType.Info));

            // ── 基準穴 ──
            _root.Add(SectionLabel("基準穴（変更しない）"));
            _root.Add(ActionButton("基準穴を選択から取り込み", () =>
            {
                GetH?.Invoke()?.ImportBase();
                Refresh();
            }));
            _baseLabel = InfoLabel();
            _root.Add(_baseLabel);

            // ── 対象穴 ──
            _root.Add(SectionLabel("対象穴（頂点数を増減させる）"));
            _root.Add(ActionButton("対象穴を選択から取り込み", () =>
            {
                GetH?.Invoke()?.ImportTarget();
                Refresh();
            }));
            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            _root.Add(ActionButton("取り込みを破棄", () =>
            {
                GetH?.Invoke()?.ClearSeeds();
                _lastResult = "";
                Refresh();
            }));

            // ── 差分と設定 ──
            _diffLabel = InfoLabel();
            _diffLabel.style.marginTop = 6;
            _root.Add(_diffLabel);

            _splitTriToggle = new Toggle("三角形は三角形2枚に割る")
            {
                value = true,
            };
            _splitTriToggle.style.fontSize  = 11;
            _splitTriToggle.style.marginTop = 4;
            _splitTriToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.SplitTriangleIntoTriangles = e.newValue;
            });
            _root.Add(_splitTriToggle);

            var triHint = new Label(
                "OFF のときは、三角形へ中点を入れた結果を四角形のまま残します（面数が増えません）。\n" +
                "四角形以上の面は、常に四角形と三角形へ割り直します。");
            triHint.style.fontSize     = 10;
            triHint.style.whiteSpace   = WhiteSpace.Normal;
            triHint.style.marginBottom = 4;
            _root.Add(triHint);

            // ── 実行 ──
            _executeBtn = new Button(() =>
            {
                // 実行はコマンドへ流す。ここで Tool を直に叩くと、
                // ディスパッチャ側の欠陥が自動検査を素通りする。
                OnExecute?.Invoke();
                Refresh();
            })
            { text = "頂点数を合わせる" };
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
            var h = GetH?.Invoke();
            if (h == null) return;

            _splitTriToggle?.SetValueWithoutNotify(h.SplitTriangleIntoTriangles);

            var baseSeed   = h.BaseSeed;
            var targetSeed = h.TargetSeed;

            if (_baseLabel != null)
                _baseLabel.text = SeedText(baseSeed, "未取り込み");
            if (_targetLabel != null)
                _targetLabel.text = SeedText(targetSeed, "未取り込み");

            var sum = h.Inspect();

            if (_diffLabel != null)
            {
                if (sum.BaseCount > 0 && sum.TargetCount > 0)
                {
                    string arrow = sum.Delta == 0 ? "一致" : (sum.Delta > 0 ? $"+{sum.Delta}" : sum.Delta.ToString());
                    _diffLabel.text = $"基準 {sum.BaseCount} 頂点 / 対象 {sum.TargetCount} 頂点 → 対象を {arrow}";
                }
                else if (sum.BaseCount > 0)
                {
                    _diffLabel.text = $"基準 {sum.BaseCount} 頂点";
                }
                else
                {
                    _diffLabel.text = "";
                }
            }

            if (_statusLabel != null)
            {
                _statusLabel.text = string.IsNullOrEmpty(_lastResult)
                    ? (sum.Reason ?? "")
                    : _lastResult;
            }

            _executeBtn?.SetEnabled(sum.CanExecute);
        }

        private static string SeedText(Poly_Ling.Tools.HoleRingCountTool.Seed seed, string emptyText)
        {
            if (seed == null) return emptyText;
            if (seed.Valid)   return seed.Info;
            return string.IsNullOrEmpty(seed.Info) ? emptyText : seed.Info;
        }

        // ================================================================
        // IHoleSeedSource（ビューポートの種マーカー）
        // ================================================================

        /// <summary>このパネルのセクションが右ペインに表示されているか。</summary>
        private bool IsSectionVisible()
        {
            if (_sectionEl == null) return false;
            return _sectionEl.resolvedStyle.display != DisplayStyle.None;
        }

        public bool HoleSeedOverlayActive => IsSectionVisible();

        public int HoleSeedMeshIndexA => Valid(GetH?.Invoke()?.BaseSeed)   ? GetH.Invoke().BaseSeed.MeshIndex     : -1;
        public int HoleSeedVertexA    => Valid(GetH?.Invoke()?.BaseSeed)   ? GetH.Invoke().BaseSeed.Vertex        : -1;
        public int HoleSeedDirHintA   => Valid(GetH?.Invoke()?.BaseSeed)   ? GetH.Invoke().BaseSeed.DirectionHint : -1;

        public int HoleSeedMeshIndexB => Valid(GetH?.Invoke()?.TargetSeed) ? GetH.Invoke().TargetSeed.MeshIndex     : -1;
        public int HoleSeedVertexB    => Valid(GetH?.Invoke()?.TargetSeed) ? GetH.Invoke().TargetSeed.Vertex        : -1;
        public int HoleSeedDirHintB   => Valid(GetH?.Invoke()?.TargetSeed) ? GetH.Invoke().TargetSeed.DirectionHint : -1;

        private static bool Valid(Poly_Ling.Tools.HoleRingCountTool.Seed seed) => seed != null && seed.Valid;

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
