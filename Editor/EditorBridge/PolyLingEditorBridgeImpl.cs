// Editor/EditorBridge/PolyLingEditorBridgeImpl.cs
// ============================================================
// Editor 用 IEditorBridge 実装
// ============================================================
//
// 【役割】
//   PLEditorBridge に登録される「本物の」Editor 実装。
//   AssetDatabase / EditorUtility / PrefabUtility / Undo / Selection を実呼び出しする。
//   これが未登録だと EditorBridgeNull（エラーを出すだけ）か
//   PolyLingPlayerBridge（AssetDatabase 系が空実装）が使われ、
//   Hierarchy Export のメッシュ/マテリアルのアセット化が失敗する。
//
// 【登録タイミング】
//   [InitializeOnLoad] でエディタ起動時に登録する。
//   プレイモード中は Player 側 MonoBehaviour が PolyLingPlayerBridge を
//   登録して上書きする（Player 挙動の再現が目的）。
//   プレイモード終了時に本実装へ戻す（EditorApplication.playModeStateChanged）。
//
// ============================================================

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Tools;
using Object = UnityEngine.Object;

namespace Poly_Ling.EditorIO
{
    /// <summary>エディタ起動時／プレイモード終了時に Editor 実装を登録する。</summary>
    [InitializeOnLoad]
    public static class PolyLingEditorBridgeInstaller
    {
        static PolyLingEditorBridgeInstaller()
        {
            Install();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>Editor 実装を PLEditorBridge に登録する。</summary>
        public static void Install()
        {
            PLEditorBridge.Register(new PolyLingEditorBridgeImpl());
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // プレイモード中は Player 側が上書きするため、編集モードへ戻った時点で復帰させる。
            if (state == PlayModeStateChange.EnteredEditMode)
                Install();
        }
    }

    /// <summary>UnityEditor API を実呼び出しする IEditorBridge 実装。</summary>
    public class PolyLingEditorBridgeImpl : IEditorBridge
    {
        // ================================================================
        // AssetDatabase 読み取り
        // ================================================================

        public T LoadAssetAtPath<T>(string path) where T : Object
            => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);

        public Object[] LoadAllAssetsAtPath(string path)
            => string.IsNullOrEmpty(path) ? Array.Empty<Object>() : AssetDatabase.LoadAllAssetsAtPath(path);

        public string GetAssetPath(Object asset)
            => asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);

        public bool ContainsAsset(Object asset)
            => asset != null && AssetDatabase.Contains(asset);

        public bool IsValidFolder(string path)
            => !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);

        public string[] FindAssets(string filter, string[] searchInFolders)
        {
            if (string.IsNullOrEmpty(filter)) return Array.Empty<string>();

            return (searchInFolders == null || searchInFolders.Length == 0)
                ? AssetDatabase.FindAssets(filter)
                : AssetDatabase.FindAssets(filter, searchInFolders);
        }

        public string GUIDToAssetPath(string guid)
            => string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);

        // ================================================================
        // AssetDatabase 書き込み
        // ================================================================

        public void CreateAsset(Object asset, string path)
        {
            if (asset == null || string.IsNullOrEmpty(path)) return;
            AssetDatabase.CreateAsset(asset, path);
        }

        public void DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.DeleteAsset(path);
        }

        public void CopySerialized(Object source, Object dest)
        {
            if (source == null || dest == null) return;
            EditorUtility.CopySerialized(source, dest);
        }

        public void ImportAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.ImportAsset(path);
        }

        public void SaveAssets() => AssetDatabase.SaveAssets();

        public void Refresh() => AssetDatabase.Refresh();

        // ================================================================
        // 組み込みアセット
        // ================================================================

        /// <summary>
        /// 組み込みの Default-Diffuse を返す。プレファブ化したときに
        /// 実体のあるアセットへの参照が残る（実行時生成のマテリアルだと残らない）。
        /// </summary>
        public Material GetBuiltinDefaultMaterial()
            => AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

        // ================================================================
        // PrefabUtility
        // ================================================================

        public GameObject SaveAsPrefabAsset(GameObject go, string path)
        {
            if (go == null || string.IsNullOrEmpty(path)) return null;
            return PrefabUtility.SaveAsPrefabAsset(go, path);
        }

        // ================================================================
        // EditorUtility ダイアログ
        //
        // 【FileDialogGuard で包む理由】
        //   ネイティブのモーダル表示中もエディタのループは回り続ける。
        //   そこでウィンドウが Repaint すると UIToolkit が保留イベントを処理し直し、
        //   ダイアログを開かせたクリックが再配送されて同じダイアログが二重に開く。
        //   ダイアログの入口はここだけなので、全メソッドを門番でくぐらせる。
        //   （PolyLingEditorWindow 側でも、開いている間は Repaint を止める）
        // ================================================================

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
            => FileDialogGuard.Run(() => EditorUtility.SaveFilePanel(title, directory, defaultName, extension));

        public string SaveFilePanelInProject(string title, string defaultName, string extension, string message)
            => FileDialogGuard.Run(() => EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message));

        public string OpenFilePanel(string title, string directory, string extension)
            => FileDialogGuard.Run(() => EditorUtility.OpenFilePanel(title, directory, extension));

        // EditorUtility.OpenFilePanel は初期ファイル名の引数を持たないため、
        // Windows では Player と同じ Win32FileDialog を使って defaultName を反映する。
        // それ以外のプラットフォームは EditorUtility にフォールバックし、defaultName は無視される。
        public string OpenFilePanel(string title, string directory, string defaultName, string extension)
            => FileDialogGuard.Run(() =>
            {
                if (!Win32FileDialog.Supported)
                    return EditorUtility.OpenFilePanel(title, directory, extension);

                // Win32 は '\\' 区切りで返すため、EditorUtility と同じ '/' 区切りに揃える。
                string path = Win32FileDialog.OpenFile(title, directory, defaultName, extension);
                return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
            });

        public string SaveFolderPanel(string title, string directory, string defaultName)
            => FileDialogGuard.Run(() => EditorUtility.SaveFolderPanel(title, directory, defaultName));

        public string OpenFolderPanel(string title, string directory, string defaultName)
            => FileDialogGuard.Run(() => EditorUtility.OpenFolderPanel(title, directory, defaultName));

        public bool DisplayDialog(string title, string message, string ok)
            => EditorUtility.DisplayDialog(title, message, ok);

        public bool DisplayDialogYesNo(string title, string message, string yes, string no)
            => EditorUtility.DisplayDialog(title, message, yes, no);

        // ================================================================
        // EditorGUIUtility
        // ================================================================

        public void PingObject(Object obj)
        {
            if (obj == null) return;
            EditorGUIUtility.PingObject(obj);
        }

        // ================================================================
        // EditorPrefs
        // ================================================================

        public int  GetPrefInt(string key, int defaultValue)   => EditorPrefs.GetInt(key, defaultValue);
        public void SetPrefInt(string key, int value)          => EditorPrefs.SetInt(key, value);
        public bool GetPrefBool(string key, bool defaultValue) => EditorPrefs.GetBool(key, defaultValue);
        public void SetPrefBool(string key, bool value)        => EditorPrefs.SetBool(key, value);

        // ================================================================
        // Selection
        // ================================================================

        // Poly_Ling.Selection 名前空間と衝突するため UnityEditor.Selection を完全修飾する。
        public Transform    GetActiveTransform()     => UnityEditor.Selection.activeTransform;
        public Object       GetActiveObject()        => UnityEditor.Selection.activeObject;
        public GameObject   GetActiveGameObject()    => UnityEditor.Selection.activeGameObject;
        public GameObject[] GetSelectedGameObjects() => UnityEditor.Selection.gameObjects ?? Array.Empty<GameObject>();

        public void SetActiveObject(Object obj)        => UnityEditor.Selection.activeObject = obj;
        public void SetActiveGameObject(GameObject go) => UnityEditor.Selection.activeGameObject = go;

        // ================================================================
        // Undo
        // ================================================================

        public void RecordObject(Object obj, string name)
        {
            if (obj == null) return;
            Undo.RecordObject(obj, name);
        }

        public void RegisterCreatedObjectUndo(Object obj, string name)
        {
            if (obj == null) return;
            Undo.RegisterCreatedObjectUndo(obj, name);
        }

        public T AddComponent<T>(GameObject go) where T : Component
            => go == null ? null : Undo.AddComponent<T>(go);

        // ================================================================
        // RemoteServer
        // ================================================================

        /// <summary>
        /// Editor 側 RemoteServer ウィンドウは本パッケージに存在しないため無操作。
        /// Player 側は PolyLingPlayerServer が担当する。
        /// </summary>
        public void SetupRemoteServer(Action<PanelCommand> dispatch)
        {
        }

        // ================================================================
        // ウィンドウ再接続
        // ================================================================

        public IToolContextReceiver[] FindAllToolContextReceivers()
            => Resources.FindObjectsOfTypeAll<EditorWindow>()
                        .OfType<IToolContextReceiver>()
                        .ToArray();

        public IPanelContextReceiver[] FindAllPanelContextReceivers()
            => Resources.FindObjectsOfTypeAll<EditorWindow>()
                        .OfType<IPanelContextReceiver>()
                        .ToArray();

        // ================================================================
        // 時間
        // ================================================================

        public double GetTimeSinceStartup() => EditorApplication.timeSinceStartup;

        // ================================================================
        // GUI - Undo/Redoボタン描画（Editor実装）
        // ================================================================

        public void DrawUndoRedoButtons(bool canUndo, bool canRedo, Action onUndo, Action onRedo)
        {
            GUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!canUndo))
            {
                if (GUILayout.Button("Undo", GUILayout.Width(60)))
                    onUndo?.Invoke();
            }

            using (new EditorGUI.DisabledScope(!canRedo))
            {
                if (GUILayout.Button("Redo", GUILayout.Width(60)))
                    onRedo?.Invoke();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}
