// SaveDest.cs
// 「書き込み先」を決める唯一の規約。Editor / Player 共用。
//
// 【なぜ要るか】
//   保存先の持ち方がパネルごとに違っていた。フルパスを持つ欄、フォルダだけの欄、
//   ファイル名だけの欄、欄が無いもの。さらに [...] ボタンの意味も
//   「押すと即保存」「選択のみ」「存在しない」の 3 通りに分かれていた。
//   その結果、読み込んだファイル名や履歴のフルパスがそのまま書き込み先になり、
//   利用者が意図しないファイルを上書きする事故が起きていた。
//
// 【規約】
//   1. 保存先の欄には「フォルダだけ」を置く。ファイル名は置かない。
//   2. [...] は保存ダイアログを出すが、確定したパスの**フォルダ部分だけ**を欄に書く。
//      [...] では絶対に保存しない。
//   3. 保存ボタンは欄のフォルダを初期ディレクトリにして保存ダイアログを開く
//      （「名前を付けて保存」の動作）。ファイル名はそこで決まる。
//   4. 欄の内容は settings（RecentPaths）に保存する。キーは Keys に集約する。
//
//   この 3 と 4 のおかげで、書き込み先が「前回のファイル名」や
//   「読み込んだファイル名」に引きずられることが無くなる。
//   前回覚えているのはフォルダだけで、ファイル名は毎回ダイアログで確定する。
//
// Runtime/Poly_Ling_Main/Core/Config/ に配置（RecentPaths と同じ場所）

using System;
using System.IO;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Core
{
    /// <summary>保存先フォルダの読み書きと、保存ダイアログの入口。</summary>
    public static class SaveDest
    {
        /// <summary>
        /// 保存先フォルダの settings キー。
        /// 用途ごとに 1 つ。パネルが自前で固有名を作らないよう、ここに全部並べる。
        /// 値は必ず「フォルダ」であり、ファイル名は入らない。
        /// </summary>
        public static class Keys
        {
            /// <summary>PMX / MQO / OBJ / VRM の書き出し。</summary>
            public const string Export = "Save.Export.Folder";

            /// <summary>部分エクスポート。</summary>
            public const string PartialExport = "Save.PartialExport.Folder";

            /// <summary>プロジェクトファイル（.mfproj / CSV）の保存。</summary>
            public const string Project = "Save.Project.Folder";

            /// <summary>ログのファイル保存。</summary>
            public const string Log = "Save.Log.Folder";

            /// <summary>画面キャプチャ。</summary>
            public const string Capture = "Save.Capture.Folder";

            /// <summary>自動検証パネル群の出力（PMX / MQO / VRM）。</summary>
            public const string Pipeline = "Save.Pipeline.Folder";

            /// <summary>自動検証パネル群のレポート。</summary>
            public const string Report = "Save.Report.Folder";

            /// <summary>道具一覧（tools.json）の書き出し。</summary>
            public const string Schema = "Save.Schema.Folder";

            /// <summary>原点CSV（ボーン編集・T ポーズ）。</summary>
            public const string OriginCsv = "Save.OriginCsv.Folder";

            /// <summary>モーフ関係の CSV。</summary>
            public const string MorphCsv = "Save.MorphCsv.Folder";

            /// <summary>VRM アニメーション（.vrma）。</summary>
            public const string Vrma = "Save.Vrma.Folder";

            /// <summary>辞書・対応表の CSV（部品辞書 / 名称一括変更 / 作業軸辞書）。</summary>
            public const string Dictionary = "Save.Dictionary.Folder";

            /// <summary>断面・ベルト・回転体などの形状 CSV。</summary>
            public const string ProfileCsv = "Save.ProfileCsv.Folder";

            /// <summary>メッシュを JSON で出すもの。</summary>
            public const string MeshJson = "Save.MeshJson.Folder";

            /// <summary>VMD トレース CSV。</summary>
            public const string VmdTrace = "Save.VmdTrace.Folder";

            /// <summary>Editor: ヒエラルキー書き出しのプレファブ出力先（Assets 相対）。</summary>
            public const string HierarchyPrefab = "Save.HierarchyPrefab.Folder";

            /// <summary>Editor: リモート受信ファイルの保存先。</summary>
            public const string RemoteHierarchy = "Save.RemoteHierarchy.Folder";

            /// <summary>Editor: UnityClip 書き出し。</summary>
            public const string UnityClip = "Save.UnityClip.Folder";
        }

        // ================================================================
        // 読み書き
        // ================================================================

        /// <summary>
        /// 保存先フォルダを読む。未設定なら fallback。
        /// 旧版がフルパスを書き残していても、フォルダ部分だけを返す。
        /// </summary>
        public static string GetFolder(string key, string fallback = "")
        {
            EnsureMigrated();
            string v = RecentPaths.Get(key, "");
            if (string.IsNullOrEmpty(v)) return fallback;
            return NormalizeFolder(v);
        }

        /// <summary>
        /// 保存先フォルダを settings へ書く。
        /// ファイルを指す文字列を渡されてもフォルダ部分だけを保存する
        /// （欄にファイル名が入り込む経路を、書き込み側で塞ぐ）。
        /// </summary>
        public static void SetFolder(string key, string value)
        {
            EnsureMigrated();
            RecentPaths.Set(key, NormalizeFolder(value));
        }

        /// <summary>
        /// フォルダらしい文字列に正す。
        /// ・実在するフォルダ → そのまま
        /// ・拡張子が付いている → 親フォルダ
        /// ・それ以外 → そのまま（これから作るフォルダかもしれない）
        /// 末尾の区切りは落とす。
        /// </summary>
        public static string NormalizeFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            string v = value.Trim();
            try
            {
                if (Directory.Exists(v)) return TrimSeparator(v);

                string ext = Path.GetExtension(v);
                if (!string.IsNullOrEmpty(ext))
                {
                    string dir = Path.GetDirectoryName(v);
                    return string.IsNullOrEmpty(dir) ? "" : TrimSeparator(dir);
                }
                return TrimSeparator(v);
            }
            catch (ArgumentException)
            {
                // パスとして解釈できない文字列。欄の途中入力などで起こる。
                return v;
            }
        }

        private static string TrimSeparator(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string t = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // "C:" だけになると意味が変わるので区切りを戻す。
            if (t.Length == 2 && t[1] == ':') return t + Path.DirectorySeparatorChar;
            return t;
        }

        // ================================================================
        // ダイアログ
        // ================================================================

        /// <summary>
        /// [...] ボタン用。保存ダイアログを出し、確定したパスの**フォルダ部分だけ**を返す。
        /// ファイルは一切書かない。キャンセル時は空文字。
        /// 保存ダイアログを使うのは、フォルダ選択ダイアログだと
        /// 「これから作るファイルの隣」を選びにくいため。
        /// </summary>
        /// <param name="key">確定したフォルダを書き戻す settings キー。</param>
        /// <param name="currentFolder">欄の現在値。初期ディレクトリに使う。</param>
        /// <param name="defaultName">ダイアログのファイル名欄の初期値。</param>
        /// <param name="extension">拡張子（ドット無し。カンマ区切りで複数可）。</param>
        public static string PickFolder(
            string title, string key, string currentFolder, string defaultName, string extension)
        {
            string seed = !string.IsNullOrEmpty(currentFolder)
                ? NormalizeFolder(currentFolder)
                : GetFolder(key);

            string picked = PLEditorBridge.I.SaveFilePanel(title, seed ?? "", defaultName ?? "", extension ?? "");
            if (string.IsNullOrEmpty(picked)) return "";

            // ここで保存はしない。フォルダだけを取り出す。
            string folder = NormalizeFolder(picked);
            if (string.IsNullOrEmpty(folder)) return "";

            SetFolder(key, folder);
            return folder;
        }

        /// <summary>
        /// 保存ボタン用。欄のフォルダを初期ディレクトリにして保存ダイアログを開き、
        /// 確定した**フルパス**を返す（「名前を付けて保存」）。キャンセル時は空文字。
        /// 確定後、settings にはフォルダだけを書き戻す。ファイル名は覚えない。
        /// </summary>
        /// <param name="key">フォルダを書き戻す settings キー。</param>
        /// <param name="currentFolder">欄の現在値。</param>
        /// <param name="defaultName">ファイル名欄の初期値（拡張子込みでよい）。</param>
        /// <param name="extension">拡張子（ドット無し。カンマ区切りで複数可）。</param>
        public static string AskSavePath(
            string title, string key, string currentFolder, string defaultName, string extension)
        {
            string seed = !string.IsNullOrEmpty(currentFolder)
                ? NormalizeFolder(currentFolder)
                : GetFolder(key);

            string path = PLEditorBridge.I.SaveFilePanel(title, seed ?? "", defaultName ?? "", extension ?? "");
            if (string.IsNullOrEmpty(path)) return "";

            SetFolder(key, NormalizeFolder(path));

            // 利用者が選んだこと自体が許可の表明。作業フォルダの外でも
            // このパスだけを 1 回通す（PLSandbox の説明を参照）。
            PLSandbox.AllowOnceFromDialog(path);
            return path;
        }

        /// <summary>
        /// 欄のフォルダと確定済みのファイル名を繋ぐ。
        /// ダイアログを出さずに書く経路（連番キャプチャなど）だけが使う。
        /// フォルダが空なら空文字を返す。
        /// </summary>
        public static string Combine(string folder, string fileName)
        {
            string f = NormalizeFolder(folder);
            if (string.IsNullOrEmpty(f) || string.IsNullOrEmpty(fileName)) return "";
            try { return Path.Combine(f, fileName); }
            catch (ArgumentException) { return ""; }
        }

        // ================================================================
        // 旧キーからの移行
        // ================================================================

        // 旧版は保存先を「フルパス」で覚えていた。そのまま残すと、
        // 新しい欄に前回のファイル名が混ざる。1 回だけフォルダ部分を移して旧キーを消す。
        private const string MigrationFlagKey = "Save.FolderMigration.Done";

        private static readonly object _migrationLock = new object();
        private static bool _migrated;

        /// <summary>旧キー（フルパス）から新キー（フォルダ）へ 1 回だけ移す。</summary>
        public static void EnsureMigrated()
        {
            if (_migrated) return;
            lock (_migrationLock)
            {
                if (_migrated) return;
                _migrated = true;

                if (RecentPaths.Get(MigrationFlagKey, "") == "1") return;

                // 旧キー（フルパス）→ 新キー（フォルダ）。
                // 先に書かれている旧キーを優先する。
                MoveFirst(Keys.Export,        "Export.PMX.Path", "Export.MQO.Path", "Export.OBJ.Path", "Export.VRM.Path");
                MoveFirst(Keys.PartialExport, "PartialExport.PMX.OutPath", "PartialExport.MQO.OutPath");
                MoveFirst(Keys.Project,       "Project.CsvPath", "Project.JsonPath");
                MoveFirst(Keys.Log,           "Log.SavePath");
                MoveFirst(Keys.Capture,       "Capture.Folder");
                MoveFirst(Keys.Pipeline,      "SysDebug.MqoToPmx.OutPath", "SysDebug.PmxToMqo.OutPath");

                // 保存専用だった旧キーは消す。
                // Project.CsvPath / Project.JsonPath は読込側が使い続けるので残す。
                Drop("Export.PMX.Path", "Export.MQO.Path", "Export.OBJ.Path", "Export.VRM.Path",
                     "PartialExport.PMX.OutPath", "PartialExport.MQO.OutPath",
                     "Log.SavePath",
                     "Capture.Folder",
                     "SysDebug.MqoToPmx.OutPath", "SysDebug.PmxToMqo.OutPath");

                RecentPaths.Set(MigrationFlagKey, "1");
            }
        }

        /// <summary>oldKeys のうち最初に値を持つものを、フォルダに直して newKey へ書く。</summary>
        private static void MoveFirst(string newKey, params string[] oldKeys)
        {
            if (!string.IsNullOrEmpty(RecentPaths.Get(newKey, ""))) return;

            foreach (string k in oldKeys)
            {
                string v = RecentPaths.Get(k, "");
                if (string.IsNullOrEmpty(v)) continue;

                string folder = NormalizeFolder(v);
                if (string.IsNullOrEmpty(folder)) continue;

                RecentPaths.Set(newKey, folder);
                return;
            }
        }

        private static void Drop(params string[] keys)
        {
            foreach (string k in keys) RecentPaths.Set(k, "");
        }
    }
}
