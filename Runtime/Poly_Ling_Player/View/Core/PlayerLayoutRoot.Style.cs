// PlayerLayoutRoot.Style.cs
// 部品生成ヘルパ（ボタン・見出し・Foldout 等）と配色・押下フィードバック。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
    {
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
