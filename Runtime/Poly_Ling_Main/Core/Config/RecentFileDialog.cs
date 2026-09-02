// RecentFileDialog.cs
// パス入力欄を持たない読込／保存ボタン用のファイルダイアログ。
//
// 【なぜ必要か】
//   パス欄のあるパネルは、その欄の値をダイアログの初期フォルダ／初期ファイル名に使える。
//   欄を持たないボタン（原点CSV読込・CSV読込 等）は初期値の置き場が無く、
//   毎回ダイアログが既定フォルダから始まってしまう。
//   そこで RecentPaths（persistentDataPath/PolyLing/RecentPaths.csv）に
//   キー単位で「最後に使ったパス（最新1件）」を保存し、それを初期値として使う。
//
// 初期ファイル名が実際に反映されるのは Player 実装のみ。
// Editor 実装（EditorUtility.OpenFilePanel）は初期ファイル名を受け取れないため無視される。
//
// Runtime/Poly_Ling_Main/Core/Config/ に配置（RecentPaths と同じ場所）

using System;
using System.IO;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Core
{
    public static class RecentFileDialog
    {
        /// <summary>
        /// 読込ダイアログ。初期フォルダ／初期ファイル名は recentKey の履歴から取る。
        /// 確定したパスは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskLoad(string title, string recentKey, string extension)
        {
            string last = RecentPaths.Get(recentKey);
            SplitPath(last, out string dir, out string name);

            string path = PLEditorBridge.I.OpenFilePanel(title, dir, name, extension);
            if (!string.IsNullOrEmpty(path))
                RecentPaths.Set(recentKey, path);
            return path;
        }

        /// <summary>
        /// 保存ダイアログ。初期フォルダ／初期ファイル名は recentKey の履歴から取る。
        /// 履歴が無いときだけ defaultName を使う。確定したパスは履歴へ書き戻す。
        /// キャンセル時は空文字を返す。
        /// </summary>
        public static string AskSave(string title, string recentKey, string defaultName, string extension)
        {
            string last = RecentPaths.Get(recentKey);
            SplitPath(last, out string dir, out string name);
            if (string.IsNullOrEmpty(name)) name = defaultName;

            string path = PLEditorBridge.I.SaveFilePanel(title, dir, name, extension);
            if (!string.IsNullOrEmpty(path))
                RecentPaths.Set(recentKey, path);
            return path;
        }

        /// <summary>
        /// フォルダ選択ダイアログ。初期フォルダは recentKey の履歴から取る。
        /// 確定したフォルダは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskFolder(string title, string recentKey, string defaultName = "")
        {
            string dir = RecentPaths.Get(recentKey);

            string path = PLEditorBridge.I.OpenFolderPanel(title, dir, defaultName ?? "");
            if (!string.IsNullOrEmpty(path))
                RecentPaths.Set(recentKey, path);
            return path;
        }

        /// <summary>
        /// 読込ダイアログ。初期値は seedPath を優先し、空なら recentKey の履歴を使う。
        /// パス欄を持つパネル（欄の値を初期値にしたい）向けの入口。
        /// 確定したパスは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskLoadFrom(string title, string recentKey, string seedPath, string extension)
        {
            string seed = !string.IsNullOrEmpty(seedPath) ? seedPath : RecentPaths.Get(recentKey);
            SplitPath(seed, out string dir, out string name);

            string path = PLEditorBridge.I.OpenFilePanel(title, dir, name, extension);
            if (!string.IsNullOrEmpty(path))
                RecentPaths.Set(recentKey, path);
            return path;
        }

        /// <summary>
        /// 保存ダイアログ。初期値は seedPath を優先し、空なら recentKey の履歴を使う。
        /// どちらからもファイル名が取れないときだけ defaultName を使う。
        /// 確定したパスは履歴へ書き戻す。キャンセル時は空文字を返す。
        /// </summary>
        public static string AskSaveTo(
            string title, string recentKey, string seedPath, string defaultName, string extension)
        {
            string seed = !string.IsNullOrEmpty(seedPath) ? seedPath : RecentPaths.Get(recentKey);
            SplitPath(seed, out string dir, out string name);
            if (string.IsNullOrEmpty(name)) name = defaultName;

            string path = PLEditorBridge.I.SaveFilePanel(title, dir, name, extension);
            if (!string.IsNullOrEmpty(path))
                RecentPaths.Set(recentKey, path);
            return path;
        }

        /// <summary>履歴パスをフォルダ／ファイル名に分解する。不正な文字を含む場合は初期値として使わない。</summary>
        private static void SplitPath(string fullPath, out string dir, out string name)
        {
            dir  = "";
            name = "";
            if (string.IsNullOrEmpty(fullPath)) return;

            try
            {
                string d = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(d)) dir = d;
                string n = Path.GetFileName(fullPath);
                if (!string.IsNullOrEmpty(n)) name = n;
            }
            catch (ArgumentException)
            {
                dir  = "";
                name = "";
            }
        }
    }
}
