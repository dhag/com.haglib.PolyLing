// Win32FileDialog.cs
// Windows コモンダイアログ（comdlg32 / shell32）の P/Invoke 実装。
// Player 実装（PolyLingPlayerBridge）と Editor 実装（PolyLingEditorBridgeImpl）の
// 双方から呼べるよう Runtime 側に切り出したもの。
//
// EditorUtility.OpenFilePanel は初期ファイル名の引数を持たないため、
// 読込ダイアログの初期ファイル名を Editor / Player で統一する目的で共有する。
//
// 返すパスは Win32 の仕様どおり '\' 区切り。'/' 区切りが必要な呼び出し側で正規化すること。
// Runtime/Poly_Ling_Main/Core/EditorBridge/ に配置

using System;

namespace Poly_Ling.EditorBridge
{
    /// <summary>
    /// Windows コモンダイアログのラッパー。
    /// Supported が false のプラットフォームでは全メソッドが空文字を返す。
    /// </summary>
    public static class Win32FileDialog
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

        /// <summary>この環境で Win32 ダイアログが使えるか。</summary>
        public static bool Supported => true;

        // ================================================================
        // P/Invoke 定義
        // ================================================================

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private class OpenFileName
        {
            public int    lStructSize       = System.Runtime.InteropServices.Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner         = IntPtr.Zero;
            public IntPtr hInstance         = IntPtr.Zero;
            public string lpstrFilter       = null;
            public string lpstrCustomFilter = null;
            public int    nMaxCustFilter    = 0;
            public int    nFilterIndex      = 0;
            public string lpstrFile         = null;
            public int    nMaxFile          = 0;
            public string lpstrFileTitle    = null;
            public int    nMaxFileTitle     = 0;
            public string lpstrInitialDir   = null;
            public string lpstrTitle        = null;
            public int    Flags             = 0;
            public short  nFileOffset       = 0;
            public short  nFileExtension    = 0;
            public string lpstrDefExt       = null;
            public IntPtr lCustData         = IntPtr.Zero;
            public IntPtr lpfnHook          = IntPtr.Zero;
            public string lpTemplateName    = null;
            public IntPtr pvReserved        = IntPtr.Zero;
            public int    dwReserved        = 0;
            public int    FlagsEx           = 0;
        }

        [System.Runtime.InteropServices.DllImport("comdlg32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] OpenFileName ofn);

        [System.Runtime.InteropServices.DllImport("comdlg32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool GetSaveFileName([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] OpenFileName ofn);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct BrowseInfo
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public string pszDisplayName;
            public string lpszTitle;
            public uint   ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int    iImage;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SHBrowseForFolder([System.Runtime.InteropServices.In] ref BrowseInfo bi);

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder pszPath);

        // 初期フォルダ設定用（SHBrowseForFolder は directory 引数を直接取れないため
        // コールバックで BFFM_INITIALIZED 時に BFFM_SETSELECTION を送って初期選択する）。
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint BFFM_INITIALIZED   = 1;
        private const uint BFFM_SETSELECTIONW = 0x0400 + 103; // WM_USER+103（Unicode パス指定）

        private delegate int BrowseCallbackProc(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData);
        // マーシャルした関数ポインタが GC 回収されないよう static 保持する。
        private static readonly BrowseCallbackProc _browseCallback = OnBrowseEvent;

        private static int OnBrowseEvent(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData)
        {
            // lpData は OpenFolder で bi.lParam に渡した初期パス（Unicode）ポインタ。
            if (uMsg == BFFM_INITIALIZED && lpData != IntPtr.Zero)
                SendMessage(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, lpData);
            return 0;
        }

        // ================================================================
        // フィルタ組み立て
        // ================================================================

        private static string BuildFilter(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "All Files\0*.*\0\0";

            // カンマ区切りの複数拡張子に対応（例 "png,jpg,jpeg"）。
            // Win32 のパターンはセミコロン区切り（*.png;*.jpg;…）。大小文字は非依存。
            var pattern = new System.Text.StringBuilder();
            var label   = new System.Text.StringBuilder();
            foreach (var part in extension.Split(','))
            {
                string e = part.Trim().TrimStart('.');
                if (string.IsNullOrEmpty(e)) continue;
                if (pattern.Length > 0) { pattern.Append(';'); label.Append(", "); }
                pattern.Append("*.").Append(e);
                label.Append("*.").Append(e);
            }
            if (pattern.Length == 0) return "All Files\0*.*\0\0";
            return $"{label}\0{pattern}\0All Files\0*.*\0\0";
        }

        // カンマ区切りの先頭拡張子（既定拡張子用）。null/空は null。
        private static string FirstExt(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;
            foreach (var part in extension.Split(','))
            {
                string e = part.Trim().TrimStart('.');
                if (!string.IsNullOrEmpty(e)) return e;
            }
            return null;
        }

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>
        /// 読込ダイアログ。directory を初期フォルダ、defaultName をファイル名欄の初期値にする。
        /// キャンセル時は空文字。
        /// </summary>
        public static string OpenFile(string title, string directory, string defaultName, string extension)
        {
            var ofn = new OpenFileName();
            ofn.lpstrTitle      = title;
            ofn.lpstrFilter     = BuildFilter(extension);
            ofn.lpstrFile       = (defaultName ?? "") + new string('\0', 512);
            ofn.nMaxFile        = 512;
            ofn.lpstrInitialDir = directory;
            ofn.lpstrDefExt     = FirstExt(extension);
            ofn.Flags           = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008; // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            return GetOpenFileName(ofn) ? ofn.lpstrFile.TrimEnd('\0') : string.Empty;
        }

        /// <summary>
        /// 保存ダイアログ。既存ファイル選択時は上書き確認が出る。キャンセル時は空文字。
        /// </summary>
        public static string SaveFile(string title, string directory, string defaultName, string extension)
        {
            var ofn = new OpenFileName();
            ofn.lpstrTitle      = title;
            ofn.lpstrFilter     = BuildFilter(extension);
            ofn.lpstrFile       = (defaultName ?? "") + new string('\0', 512);
            ofn.nMaxFile        = 512;
            ofn.lpstrInitialDir = directory;
            ofn.lpstrDefExt     = FirstExt(extension);
            ofn.Flags           = 0x00080000 | 0x00000002 | 0x00000008; // OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR
            return GetSaveFileName(ofn) ? ofn.lpstrFile.TrimEnd('\0') : string.Empty;
        }

        /// <summary>
        /// フォルダ選択ダイアログ。directory が実在する場合のみ初期選択する。キャンセル時は空文字。
        /// </summary>
        public static string OpenFolder(string title, string directory)
        {
            IntPtr dirPtr = IntPtr.Zero;
            try
            {
                var bi = new BrowseInfo();
                bi.lpszTitle = title;
                bi.ulFlags   = 0x0001 | 0x0010; // BIF_RETURNONLYFSDIRS | BIF_EDITBOX

                // 初期フォルダを設定（存在する場合のみ）。コールバック経由で BFFM_SETSELECTION。
                if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
                {
                    dirPtr    = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(directory);
                    bi.lpfn   = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_browseCallback);
                    bi.lParam = dirPtr;
                }

                var pidl = SHBrowseForFolder(ref bi);
                if (pidl == IntPtr.Zero) return string.Empty;
                var sb = new System.Text.StringBuilder(512);
                return SHGetPathFromIDList(pidl, sb) ? sb.ToString() : string.Empty;
            }
            finally
            {
                if (dirPtr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dirPtr);
            }
        }

#else

        /// <summary>この環境で Win32 ダイアログが使えるか。</summary>
        public static bool Supported => false;

        public static string OpenFile(string title, string directory, string defaultName, string extension) => string.Empty;
        public static string SaveFile(string title, string directory, string defaultName, string extension) => string.Empty;
        public static string OpenFolder(string title, string directory) => string.Empty;

#endif
    }
}
