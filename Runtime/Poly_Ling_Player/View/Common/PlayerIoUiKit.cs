// PlayerIoUiKit.cs
// ファイル読込/保存パネルの共通UI部品（UIToolkit）。
// 読込専用は PlayerImportSubPanel（loadPMX）、読み書きは PlayerProjectFileSubPanel の
// デザインを単一のソースに集約する。各サブパネルはここを呼び出してデザインを統一する。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using RecentFileDialog = Poly_Ling.Core.RecentFileDialog;

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

        // ================================================================
        // ファイル選択ダイアログ
        //
        // 読込パス欄と保存パス欄を兼ねる画面では、パスが埋まっているときに
        // 保存ダイアログを省くと、読み込んだファイルを無確認で上書きしてしまう。
        // 保存は必ず AskSavePath を通し、パス欄の値はダイアログの初期値としてだけ使う。
        //
        // 【recentKey が必須である理由】
        //   以前ここは PLEditorBridge を直接叩くだけで、履歴（RecentPaths）へ
        //   一切書いていなかった。初期値の置き場をパネルの TextField 任せに
        //   していたため、欄を自前で永続化していないパネルでは、次に開いた
        //   ときにフォルダもファイル名も消えていた。
        //   履歴の責務を呼び出し側へ配ると必ず実装漏れが出るので、
        //   キーを必須引数にして RecentFileDialog へ一本化する。
        //   キーは他パネルと衝突しない固有名にすること（例 "Export.PMX"）。
        // ================================================================

        /// <summary>
        /// 保存先を保存ダイアログで確定する。
        /// 初期値は currentPath を優先し、空なら recentKey の履歴を使う。
        /// どちらからもファイル名が取れないときだけ defaultName を使う。
        /// 確定したパスは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskSavePath(
            string title, string recentKey, string currentPath, string defaultName, string extension)
            => RecentFileDialog.AskSaveTo(title, recentKey, currentPath, defaultName, extension);

        /// <summary>
        /// 読込元をファイル選択ダイアログで確定する。
        /// 初期値は currentPath を優先し、空なら recentKey の履歴を使う。
        /// 確定したパスは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// 初期ファイル名が反映されるのは Windows のみ。
        /// </summary>
        public static string AskLoadPath(
            string title, string recentKey, string currentPath, string extension)
            => RecentFileDialog.AskLoadFrom(title, recentKey, currentPath, extension);

        /// <summary>
        /// フォルダをフォルダ選択ダイアログで確定する。
        /// 初期フォルダは currentPath を優先し、空なら recentKey の履歴を使う。
        /// 確定したフォルダは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskFolderPath(string title, string recentKey, string currentPath)
        {
            string seed = !string.IsNullOrEmpty(currentPath)
                ? currentPath
                : Poly_Ling.Core.RecentPaths.Get(recentKey);

            string path = PLEditorBridge.I.OpenFolderPanel(title, seed ?? "", "");
            if (!string.IsNullOrEmpty(path))
                Poly_Ling.Core.RecentPaths.Set(recentKey, path);
            return path;
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
