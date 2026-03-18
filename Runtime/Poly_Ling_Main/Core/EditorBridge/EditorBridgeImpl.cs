// EditorBridgeImpl.cs
// UnityEditor APIの実装。#if UNITY_EDITORガード内。
// [InitializeOnLoad]によりEditor起動時に自動登録される。

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using Poly_Ling.Tools;

namespace Poly_Ling.EditorBridge
{
    [InitializeOnLoad]
    public class EditorBridgeImpl : IEditorBridge
    {




        //ToolContext _ToolContext








        static EditorBridgeImpl()
        {
            PLEditorBridge.Register(new EditorBridgeImpl());
        }

        // ================================================================
        // AssetDatabase
        // ================================================================

        public T LoadAssetAtPath<T>(string path) where T : Object
            => AssetDatabase.LoadAssetAtPath<T>(path);

        public Object[] LoadAllAssetsAtPath(string path)
            => AssetDatabase.LoadAllAssetsAtPath(path);

        public string GetAssetPath(Object asset)
            => AssetDatabase.GetAssetPath(asset);

        public bool ContainsAsset(Object asset)
            => AssetDatabase.Contains(asset);

        public bool IsValidFolder(string path)
            => AssetDatabase.IsValidFolder(path);

        public string[] FindAssets(string filter, string[] searchInFolders)
            => AssetDatabase.FindAssets(filter, searchInFolders);

        public string GUIDToAssetPath(string guid)
            => AssetDatabase.GUIDToAssetPath(guid);

        // ================================================================
        // AssetDatabase 書き込み
        // ================================================================

        public void CreateAsset(Object asset, string path)
            => AssetDatabase.CreateAsset(asset, path);

        public void DeleteAsset(string path)
            => AssetDatabase.DeleteAsset(path);

        public void CopySerialized(Object source, Object dest)
            => EditorUtility.CopySerialized(source, dest);

        public void ImportAsset(string path)
            => AssetDatabase.ImportAsset(path);

        public void SaveAssets()
            => AssetDatabase.SaveAssets();

        public void Refresh()
            => AssetDatabase.Refresh();

        // ================================================================
        // PrefabUtility
        // ================================================================

        public GameObject SaveAsPrefabAsset(GameObject go, string path)
            => PrefabUtility.SaveAsPrefabAsset(go, path);

        // ================================================================
        // AssetDatabase 書き込み
        // ================================================================

        public void CreateAsset(Object asset, string path)
            => AssetDatabase.CreateAsset(asset, path);

        public void DeleteAsset(string path)
            => AssetDatabase.DeleteAsset(path);

        public void CopySerialized(Object source, Object dest)
            => EditorUtility.CopySerialized(source, dest);

        public void ImportAsset(string path)
            => AssetDatabase.ImportAsset(path);

        public void SaveAssets()
            => AssetDatabase.SaveAssets();

        public void Refresh()
            => AssetDatabase.Refresh();

        // ================================================================
        // PrefabUtility
        // ================================================================

        public GameObject SaveAsPrefabAsset(GameObject go, string path)
            => PrefabUtility.SaveAsPrefabAsset(go, path);

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
            => EditorUtility.SaveFilePanel(title, directory, defaultName, extension);

        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message)
            => EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message);

        public string OpenFilePanel(string title, string directory, string extension)
            => EditorUtility.OpenFilePanel(title, directory, extension);

        public string SaveFolderPanel(string title, string directory, string defaultName)
            => EditorUtility.SaveFolderPanel(title, directory, defaultName);

        public string OpenFolderPanel(string title, string directory, string defaultName)
            => EditorUtility.OpenFolderPanel(title, directory, defaultName);

        public bool DisplayDialog(string title, string message, string ok)
            => EditorUtility.DisplayDialog(title, message, ok);

        public bool DisplayDialogYesNo(string title, string message, string yes, string no)
            => EditorUtility.DisplayDialog(title, message, yes, no);

        // ================================================================
        // EditorGUIUtility
        // ================================================================

        public void PingObject(Object obj)
            => EditorGUIUtility.PingObject(obj);

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
        // Selection
        // ================================================================

        public Transform GetActiveTransform()
            => UnityEditor.Selection.activeTransform;

        public Object GetActiveObject()
            => UnityEditor.Selection.activeObject;

        public GameObject GetActiveGameObject()
            => UnityEditor.Selection.activeGameObject;

        public GameObject[] GetSelectedGameObjects()
            => UnityEditor.Selection.gameObjects;

        public void SetActiveObject(Object obj)
            => UnityEditor.Selection.activeObject = obj;

        public void SetActiveGameObject(GameObject go)
            => UnityEditor.Selection.activeGameObject = go;

        // ================================================================
        // Undo
        // ================================================================

        public void RecordObject(Object obj, string name)
            => UnityEditor.Undo.RecordObject(obj, name);

        public void RegisterCreatedObjectUndo(Object obj, string name)
            => UnityEditor.Undo.RegisterCreatedObjectUndo(obj, name);

        public T AddComponent<T>(GameObject go) where T : Component
            => UnityEditor.Undo.AddComponent<T>(go);
    }
}

#endif
