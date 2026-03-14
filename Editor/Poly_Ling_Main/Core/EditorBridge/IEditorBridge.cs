// IEditorBridge.cs
// UnityEditor依存APIをRuntimeから隔離するブリッジインターフェース
// Editor外から呼び出した場合はEditorBridgeNullが警告を出す

using UnityEngine;

namespace Poly_Ling.EditorBridge
{
    public interface IEditorBridge
    {
        // ================================================================
        // AssetDatabase
        // ================================================================

        T      LoadAssetAtPath<T>(string path) where T : Object;
        string GetAssetPath(Object asset);
        void   CreateAsset(Object asset, string path);
        void   SaveAssets();
        void   Refresh();
        bool   ContainsAsset(Object asset);
        bool   IsValidFolder(string path);

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        string SaveFilePanel(string title, string directory, string defaultName, string extension);
        string OpenFilePanel(string title, string directory, string extension);
        string SaveFolderPanel(string title, string directory, string defaultName);
        string OpenFolderPanel(string title, string directory, string defaultName);

        // ================================================================
        // EditorPrefs
        // ================================================================

        int  GetPrefInt(string key, int defaultValue);
        void SetPrefInt(string key, int value);
        bool GetPrefBool(string key, bool defaultValue);
        void SetPrefBool(string key, bool value);

        // ================================================================
        // Selection
        // ================================================================

        Transform GetActiveTransform();
        Object    GetActiveObject();
    }
}
