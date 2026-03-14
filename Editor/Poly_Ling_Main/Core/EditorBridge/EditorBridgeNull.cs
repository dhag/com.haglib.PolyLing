// EditorBridgeNull.cs
// Editor外で使用した場合の警告スタブ実装。
// 操作は何も行わず、Debug.LogErrorでメッセージを出力する。

using UnityEngine;

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

        public string GetAssetPath(Object asset)
        {
            Debug.LogError($"{Prefix}: アセットパスを取得することはできません");
            return null;
        }

        public void CreateAsset(Object asset, string path)
        {
            Debug.LogError($"{Prefix}: 保存することはできません ({path})");
        }

        public void SaveAssets()
        {
            Debug.LogError($"{Prefix}: 保存することはできません");
        }

        public void Refresh()
        {
            // AssetDatabase.Refreshは無害なので警告のみ
            Debug.LogWarning($"{Prefix}: AssetDatabase.Refresh は無効です");
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

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            Debug.LogError($"{Prefix}: ファイル保存ダイアログは表示できません");
            return null;
        }

        public string OpenFilePanel(string title, string directory, string extension)
        {
            Debug.LogError($"{Prefix}: ファイル読み込みダイアログは表示できません");
            return null;
        }

        public string SaveFolderPanel(string title, string directory, string defaultName)
        {
            Debug.LogError($"{Prefix}: フォルダ保存ダイアログは表示できません");
            return null;
        }

        public string OpenFolderPanel(string title, string directory, string defaultName)
        {
            Debug.LogError($"{Prefix}: フォルダ選択ダイアログは表示できません");
            return null;
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
    }
}
