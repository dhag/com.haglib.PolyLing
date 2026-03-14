// EditorBridgeImpl.cs
// UnityEditor APIの実装。#if UNITY_EDITORガード内。
// [InitializeOnLoad]によりEditor起動時に自動登録される。

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

namespace Poly_Ling.EditorBridge
{
    [InitializeOnLoad]
    public class EditorBridgeImpl : IEditorBridge
    {
        static EditorBridgeImpl()
        {
            PLEditorBridge.Register(new EditorBridgeImpl());
        }

        // ================================================================
        // AssetDatabase
        // ================================================================

        public T LoadAssetAtPath<T>(string path) where T : Object
            => AssetDatabase.LoadAssetAtPath<T>(path);

        public string GetAssetPath(Object asset)
            => AssetDatabase.GetAssetPath(asset);

        public void CreateAsset(Object asset, string path)
            => AssetDatabase.CreateAsset(asset, path);

        public void SaveAssets()
            => AssetDatabase.SaveAssets();

        public void Refresh()
            => AssetDatabase.Refresh();

        public bool ContainsAsset(Object asset)
            => AssetDatabase.Contains(asset);

        public bool IsValidFolder(string path)
            => AssetDatabase.IsValidFolder(path);

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
            => EditorUtility.SaveFilePanel(title, directory, defaultName, extension);

        public string OpenFilePanel(string title, string directory, string extension)
            => EditorUtility.OpenFilePanel(title, directory, extension);

        public string SaveFolderPanel(string title, string directory, string defaultName)
            => EditorUtility.SaveFolderPanel(title, directory, defaultName);

        public string OpenFolderPanel(string title, string directory, string defaultName)
            => EditorUtility.OpenFolderPanel(title, directory, defaultName);

        // ================================================================
        // EditorPrefs
        // ================================================================

        public int GetPrefInt(string key, int defaultValue)
            => EditorPrefs.GetInt(key, defaultValue);

        public void SetPrefInt(string key, int value)
            => EditorPrefs.SetInt(key, value);

        public bool GetPrefBool(string key, bool defaultValue)
            => EditorPrefs.GetBool(key, defaultValue);

        public void SetPrefBool(string key, bool value)
            => EditorPrefs.SetBool(key, value);

        // ================================================================
        // Selection (完全修飾でPoly_Ling.Selectionとの衝突を回避)
        // ================================================================

        public Transform GetActiveTransform()
            => UnityEditor.Selection.activeTransform;

        public Object GetActiveObject()
            => UnityEditor.Selection.activeObject;
    }
}

#endif
