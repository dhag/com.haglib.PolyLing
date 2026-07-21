// PlayerIoUiKit.cs
// ファイル読込/保存パネルの共通UI部品（UIToolkit）。
// 読込専用は PlayerImportSubPanel（loadPMX）、読み書きは PlayerProjectFileSubPanel の
// デザインを単一のソースに集約する。各サブパネルはここを呼び出してデザインを統一する。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// ファイルIO系サブパネルの共通UI部品。
    /// 「読込専用（A）」「読み書き（B）」の両デザインを構成する原子部品を提供する。
    /// </summary>
    public static class PlayerIoUiKit
    {
        /// <summary>ステータス文字色（橙）。読込結果・エラー表示に用いる。</summary>
        public static readonly Color StatusColor = new Color(1f, 0.7f, 0.4f);

        /// <summary>セクションラベル色（薄青）。</summary>
        private static readonly Color SectionColor = new Color(0.6f, 0.8f, 1f);

        /// <summary>太字パネルタイトル（12px・白）。</summary>
        public static Label Title(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 12;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 6;
            return l;
        }

        /// <summary>セクションラベル（10px・薄青）。</summary>
        public static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.color        = new StyleColor(SectionColor);
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        /// <summary>[...]（左・幅28）＋パス用 TextField（右・flexGrow）の行。</summary>
        public static VisualElement PathRow(TextField field, Action onBrowse)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var browse = new Button(() => onBrowse?.Invoke()) { text = "..." };
            browse.style.width       = 28;
            browse.style.marginRight = 2;

            field.style.flexGrow = 1;

            row.Add(browse);
            row.Add(field);
            return row;
        }

        /// <summary>読込専用パネルの主ボタン「開く」相当（太字・h28）。</summary>
        public static Button OpenButton(string text, Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.style.marginTop    = 2;
            b.style.marginBottom = 4;
            b.style.height       = 28;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            return b;
        }

        /// <summary>読み書きパネルの幅広ボタン（h26・縦積み）。</summary>
        public static Button WideBtn(string text, Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.style.height       = 26;
            b.style.marginBottom = 2;
            return b;
        }

        /// <summary>橙色ステータスLabel（読込結果・エラー表示用）。</summary>
        public static Label StatusLabel()
        {
            var l = new Label("");
            l.style.color      = new StyleColor(StatusColor);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.fontSize   = 10;
            l.style.marginTop  = 4;
            return l;
        }

        /// <summary>区切り線。</summary>
        public static VisualElement Divider()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 6;
            v.style.marginBottom    = 6;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        /// <summary>縦スペーサ。</summary>
        public static VisualElement Spacer(int h = 8)
        {
            var s = new VisualElement();
            s.style.height = h;
            return s;
        }
    }
}
