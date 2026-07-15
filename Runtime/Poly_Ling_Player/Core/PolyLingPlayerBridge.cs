// PolyLingPlayerBridge.cs
// プレイヤービルド用 IEditorBridge 実装
// ファイルダイアログ等はUnityEngine側の代替で実装
// Runtime/Poly_Ling_Player/Core/ に配置

using System;
using UnityEngine;
using Object = UnityEngine.Object;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.EditorBridge
{
    /// <summary>
    /// プレイヤービルド用 IEditorBridge 実装。
    /// ファイルダイアログ等はPlayer環境では使用不可のためスタブ。
    /// PMXImporter等の AssetDatabase 依存処理もスタブ。
    /// </summary>
    public class PolyLingPlayerBridge : IEditorBridge
    {
        // ================================================================
        // AssetDatabase — Playerでは使用不可
        // ================================================================
        public T LoadAssetAtPath<T>(string path) where T : Object => null;
        public Object[] LoadAllAssetsAtPath(string path) => System.Array.Empty<Object>();
        public string GetAssetPath(Object asset) => string.Empty;
        public bool ContainsAsset(Object asset) => false;
        public bool IsValidFolder(string path) => false;
        public string[] FindAssets(string filter, string[] searchInFolders) => System.Array.Empty<string>();
        public string GUIDToAssetPath(string guid) => string.Empty;

        public void CreateAsset(Object asset, string path) { }
        public void DeleteAsset(string path) { }
        public void CopySerialized(Object source, Object dest) { }
        public void ImportAsset(string path) { }
        public void SaveAssets() { }
        public void Refresh() { }

        // ================================================================
        // PrefabUtility — Playerでは使用不可
        // ================================================================
        public GameObject SaveAsPrefabAsset(GameObject go, string path) => null;

        // ================================================================
        // ダイアログ — Windows P/Invoke 実装
        // ================================================================

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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

        private const uint BFFM_INITIALIZED  = 1;
        private const uint BFFM_SETSELECTIONW = 0x0400 + 103; // WM_USER+103（Unicode パス指定）

        private delegate int BrowseCallbackProc(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData);
        // マーシャルした関数ポインタが GC 回収されないよう static 保持する。
        private static readonly BrowseCallbackProc _browseCallback = OnBrowseEvent;

        private static int OnBrowseEvent(IntPtr hwnd, uint uMsg, IntPtr lParam, IntPtr lpData)
        {
            // lpData は OpenFolderPanel で bi.lParam に渡した初期パス（Unicode）ポインタ。
            if (uMsg == BFFM_INITIALIZED && lpData != IntPtr.Zero)
                SendMessage(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, lpData);
            return 0;
        }

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

        public string OpenFilePanel(string title, string directory, string extension)
        {
            var ofn = new OpenFileName();
            ofn.lpstrTitle      = title;
            ofn.lpstrFilter     = BuildFilter(extension);
            ofn.lpstrFile       = new string('\0', 512);
            ofn.nMaxFile        = ofn.lpstrFile.Length;
            ofn.lpstrInitialDir = directory;
            ofn.lpstrDefExt     = FirstExt(extension);
            ofn.Flags           = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008; // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            return GetOpenFileName(ofn) ? ofn.lpstrFile.TrimEnd('\0') : string.Empty;
        }

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
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

        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message)
            => SaveFilePanel(title, UnityEngine.Application.dataPath, defaultName, extension);

        public string OpenFolderPanel(string title, string directory, string defaultName)
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

        public string SaveFolderPanel(string title, string directory, string defaultName)
            => OpenFolderPanel(title, directory, defaultName);

#else
        public string OpenFilePanel(string title, string directory, string extension) => string.Empty;
        public string SaveFilePanel(string title, string directory, string defaultName, string extension) => string.Empty;
        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message) => string.Empty;
        public string OpenFolderPanel(string title, string directory, string defaultName) => string.Empty;
        public string SaveFolderPanel(string title, string directory, string defaultName) => string.Empty;
#endif
        public bool DisplayDialog(string title, string message, string ok) { Debug.Log($"[Dialog] {title}: {message}"); return true; }
        public bool DisplayDialogYesNo(string title, string message, string yes, string no) { Debug.Log($"[Dialog] {title}: {message}"); return false; }

        // ================================================================
        // EditorGUIUtility
        // ================================================================
        public void PingObject(Object obj) { }

        // ================================================================
        // EditorPrefs — PlayerPrefsで代替
        // ================================================================
        public int GetPrefInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
        public void SetPrefInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public bool GetPrefBool(string key, bool defaultValue) => PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
        public void SetPrefBool(string key, bool value) => PlayerPrefs.SetInt(key, value ? 1 : 0);

        // ================================================================
        // Selection — Playerでは使用不可
        // ================================================================
        public Transform GetActiveTransform() => null;
        public Object GetActiveObject() => null;
        public GameObject GetActiveGameObject() => null;
        public GameObject[] GetSelectedGameObjects() => System.Array.Empty<GameObject>();
        public void SetActiveObject(Object obj) { }
        public void SetActiveGameObject(GameObject go) { }

        // ================================================================
        // Undo — Playerでは使用不可（PolyLing独自Undoシステムで代替）
        // ================================================================
        public void RecordObject(Object obj, string name) { }
        public void RegisterCreatedObjectUndo(Object obj, string name) { }
        public T AddComponent<T>(GameObject go) where T : Component => go.AddComponent<T>();

        // ================================================================
        // RemoteServer — Playerでは不要
        // ================================================================
        public void SetupRemoteServer(System.Action<Poly_Ling.Data.PanelCommand> dispatch) { }

        // ================================================================
        // ウィンドウ再接続 — Playerでは不要
        // ================================================================
        public IToolContextReceiver[] FindAllToolContextReceivers() => System.Array.Empty<IToolContextReceiver>();
        public IPanelContextReceiver[] FindAllPanelContextReceivers() => System.Array.Empty<IPanelContextReceiver>();

        // ================================================================
        // 時間
        // ================================================================
        public double GetTimeSinceStartup() => Time.realtimeSinceStartupAsDouble;

        // ================================================================
        // GUI - Undo/Redoボタン描画（Runtime実装）
        // ================================================================
        public void DrawUndoRedoButtons(bool canUndo, bool canRedo, Action onUndo, Action onRedo)
        {
            GUILayout.BeginHorizontal();

            bool prevUndo = GUI.enabled;
            GUI.enabled = canUndo;
            if (GUILayout.Button("Undo", GUILayout.Width(60)))
                onUndo?.Invoke();
            GUI.enabled = prevUndo;

            bool prevRedo = GUI.enabled;
            GUI.enabled = canRedo;
            if (GUILayout.Button("Redo", GUILayout.Width(60)))
                onRedo?.Invoke();
            GUI.enabled = prevRedo;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
