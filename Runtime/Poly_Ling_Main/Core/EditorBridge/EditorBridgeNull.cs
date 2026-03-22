// EditorBridgeNull.cs
// Editor外で使用した場合の警告スタブ実装。
// 操作は何も行わず、Debug.LogErrorでメッセージを出力する。

using System;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Poly_Ling.EditorBridge
{
    public class EditorBridgeNull : IEditorBridge
    {
        private const string Prefix = "[PolyLing] Editor外では使用できません";

        // ================================================================
        // AssetDatabase
        // ================================================================

        public T LoadAssetAtPath<T>(string path) where T : Object
        {
            Debug.LogError($"{Prefix}: アセットを読み込むことはできません ({path})");
            return null;
        }

        public Object[] LoadAllAssetsAtPath(string path)
        {
            Debug.LogError($"{Prefix}: アセットを読み込むことはできません ({path})");
            return new Object[0];
        }

        public string GetAssetPath(Object asset)
        {
            Debug.LogError($"{Prefix}: アセットパスを取得することはできません");
            return null;
        }

        public bool ContainsAsset(Object asset)
        {
            Debug.LogError($"{Prefix}: アセット存在確認はできません");
            return false;
        }

        public bool IsValidFolder(string path)
        {
            Debug.LogError($"{Prefix}: フォルダ確認はできません");
            return false;
        }

        public string[] FindAssets(string filter, string[] searchInFolders)
        {
            Debug.LogWarning($"{Prefix}: FindAssetsは使用できません");
            return new string[0];
        }

        public string GUIDToAssetPath(string guid)
        {
            Debug.LogWarning($"{Prefix}: GUIDToAssetPathは使用できません");
            return string.Empty;
        }

        // ================================================================
        // AssetDatabase 書き込み
        // ================================================================

        public void CreateAsset(Object asset, string path)
        {
            Debug.LogError($"{Prefix}: 保存することはできません ({path})");
        }

        public void DeleteAsset(string path)
        {
            Debug.LogError($"{Prefix}: アセット削除はできません ({path})");
        }

        public void CopySerialized(Object source, Object dest)
        {
            Debug.LogError($"{Prefix}: CopySerializedはできません");
        }

        public void ImportAsset(string path)
        {
            Debug.LogWarning($"{Prefix}: ImportAssetは無効です ({path})");
        }

        public void SaveAssets()
        {
            Debug.LogError($"{Prefix}: 保存することはできません");
        }

        public void Refresh()
        {
            Debug.LogWarning($"{Prefix}: AssetDatabase.Refresh は無効です");
        }

        // ================================================================
        // PrefabUtility
        // ================================================================

        public GameObject SaveAsPrefabAsset(GameObject go, string path)
        {
            Debug.LogError($"{Prefix}: Prefab保存はできません ({path})");
            return null;
        }

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            Debug.LogWarning($"{Prefix}: ファイル保存ダイアログは表示できません (title={title})");
            return string.Empty;
        }

        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message)
        {
            Debug.LogWarning($"{Prefix}: ファイル保存ダイアログは表示できません (title={title})");
            return string.Empty;
        }

        public string OpenFilePanel(string title, string directory, string extension)
        {
            Debug.LogWarning($"{Prefix}: ファイル読み込みダイアログは表示できません (title={title})");
            return string.Empty;
        }

        public string SaveFolderPanel(string title, string directory, string defaultName)
        {
            Debug.LogWarning($"{Prefix}: フォルダ保存ダイアログは表示できません (title={title})");
            return string.Empty;
        }

        public string OpenFolderPanel(string title, string directory, string defaultName)
        {
            Debug.LogError($"{Prefix}: フォルダ選択ダイアログは表示できません");
            return null;
        }

        public bool DisplayDialog(string title, string message, string ok)
        {
            Debug.LogWarning($"{Prefix}: ダイアログは表示できません [{title}] {message}");
            return false;
        }

        public bool DisplayDialogYesNo(string title, string message, string yes, string no)
        {
            Debug.LogWarning($"{Prefix}: ダイアログは表示できません [{title}] {message}");
            return false;
        }

        // ================================================================
        // EditorGUIUtility
        // ================================================================

        public void PingObject(Object obj)
        {
            // Runtimeでは無視（警告不要）
        }

        // ================================================================
        // EditorPrefs
        // ================================================================

        public int GetPrefInt(string key, int defaultValue)
        {
            Debug.LogWarning($"{Prefix}: EditorPrefsは使用できません。デフォルト値を返します ({key})");
            return defaultValue;
        }

        public void SetPrefInt(string key, int value)
        {
            Debug.LogWarning($"{Prefix}: EditorPrefsへの書き込みはできません ({key})");
        }

        public bool GetPrefBool(string key, bool defaultValue)
        {
            Debug.LogWarning($"{Prefix}: EditorPrefsは使用できません。デフォルト値を返します ({key})");
            return defaultValue;
        }

        public void SetPrefBool(string key, bool value)
        {
            Debug.LogWarning($"{Prefix}: EditorPrefsへの書き込みはできません ({key})");
        }

        // ================================================================
        // Selection
        // ================================================================

        public Transform GetActiveTransform()
        {
            Debug.LogWarning($"{Prefix}: Selection.activeTransformは使用できません");
            return null;
        }

        public Object GetActiveObject()
        {
            Debug.LogWarning($"{Prefix}: Selection.activeObjectは使用できません");
            return null;
        }

        public GameObject GetActiveGameObject()
        {
            Debug.LogWarning($"{Prefix}: Selection.activeGameObjectは使用できません");
            return null;
        }

        public GameObject[] GetSelectedGameObjects()
        {
            Debug.LogWarning($"{Prefix}: Selection.gameObjectsは使用できません");
            return new GameObject[0];
        }

        public void SetActiveObject(Object obj)
        {
            // Runtimeでは無視
        }

        public void SetActiveGameObject(GameObject go)
        {
            // Runtimeでは無視
        }

        // ================================================================
        // Undo
        // ================================================================

        public void RecordObject(Object obj, string name)
        {
            // Runtimeでは無視
        }

        public void RegisterCreatedObjectUndo(Object obj, string name)
        {
            // Runtimeでは無視
        }

        public T AddComponent<T>(GameObject go) where T : Component
        {
            return go.AddComponent<T>();
        }

        // ================================================================
        // RemoteServer
        // ================================================================

        public void SetupRemoteServer(Action<PanelCommand> dispatch)
        {
            // Runtime環境では無操作
        }

        // ================================================================
        // ウィンドウ再接続
        // ================================================================

        public IToolContextReceiver[] FindAllToolContextReceivers()
            => new IToolContextReceiver[0];

        public IPanelContextReceiver[] FindAllPanelContextReceivers()
            => new IPanelContextReceiver[0];

        // ================================================================
        // 時間
        // ================================================================

        public double GetTimeSinceStartup()
            => UnityEngine.Time.realtimeSinceStartupAsDouble;

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
