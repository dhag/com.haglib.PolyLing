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
        // ダイアログ — TODO: Player用UI実装に差し替え
        // ================================================================
        public string SaveFilePanel(string title, string directory, string defaultName, string extension) => string.Empty;
        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message) => string.Empty;
        public string OpenFilePanel(string title, string directory, string extension) => string.Empty;
        public string SaveFolderPanel(string title, string directory, string defaultName) => string.Empty;
        public string OpenFolderPanel(string title, string directory, string defaultName) => string.Empty;
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
