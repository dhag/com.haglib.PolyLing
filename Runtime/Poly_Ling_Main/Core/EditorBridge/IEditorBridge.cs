// IEditorBridge.cs
// UnityEditor依存APIをRuntimeから隔離するブリッジインターフェース
// Editor外から呼び出した場合はEditorBridgeNullが警告を出す

using System;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Poly_Ling.EditorBridge
{
    public interface IEditorBridge
    {
        // ================================================================
        // AssetDatabase 読み取り
        // ================================================================

        T        LoadAssetAtPath<T>(string path) where T : Object;
        Object[] LoadAllAssetsAtPath(string path);
        string   GetAssetPath(Object asset);
        bool     ContainsAsset(Object asset);
        bool     IsValidFolder(string path);
        string[] FindAssets(string filter, string[] searchInFolders);
        string   GUIDToAssetPath(string guid);

        // ================================================================
        // AssetDatabase 書き込み
        // ================================================================

        void CreateAsset(Object asset, string path);
        void DeleteAsset(string path);
        void CopySerialized(Object source, Object dest);
        void ImportAsset(string path);
        void SaveAssets();
        void Refresh();

        // ================================================================
        // 組み込みアセット
        // ================================================================

        /// <summary>
        /// マテリアル未割当のときに使う既定マテリアル。
        /// Editor では組み込みの Default-Diffuse を返す（プレファブへ参照が残る）。
        /// Player ではアセットが存在しないため、実行時に生成した共有インスタンスを返す。
        /// 呼び出し側で破棄しないこと。
        /// </summary>
        Material GetBuiltinDefaultMaterial();

        // ================================================================
        // PrefabUtility
        // ================================================================

        GameObject SaveAsPrefabAsset(GameObject go, string path);

        // ================================================================
        // EditorUtility ダイアログ
        // ================================================================

        string SaveFilePanel(string title, string directory, string defaultName, string extension);
        string SaveFilePanelInProject(string title, string defaultName, string extension, string message);
        string OpenFilePanel(string title, string directory, string extension);

        /// <summary>
        /// 初期ファイル名付きの読込ダイアログ。
        /// defaultName はダイアログのファイル名欄の初期値。
        /// Windows では Editor / Player とも Win32FileDialog を使い defaultName を反映する。
        /// Windows 以外の Editor は EditorUtility.OpenFilePanel へフォールバックし、defaultName は無視される。
        /// </summary>
        string OpenFilePanel(string title, string directory, string defaultName, string extension);
        string SaveFolderPanel(string title, string directory, string defaultName);
        string OpenFolderPanel(string title, string directory, string defaultName);
        bool   DisplayDialog(string title, string message, string ok);
        bool   DisplayDialogYesNo(string title, string message, string yes, string no);

        // ================================================================
        // EditorGUIUtility
        // ================================================================

        void PingObject(Object obj);

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

        Transform    GetActiveTransform();
        Object       GetActiveObject();
        GameObject   GetActiveGameObject();
        GameObject[] GetSelectedGameObjects();
        void         SetActiveObject(Object obj);
        void         SetActiveGameObject(GameObject go);

        // ================================================================
        // Undo
        // ================================================================

        void RecordObject(Object obj, string name);
        void RegisterCreatedObjectUndo(Object obj, string name);
        T    AddComponent<T>(GameObject go) where T : Component;

        // ================================================================
        // ウィンドウ再接続
        // ================================================================

        IToolContextReceiver[]  FindAllToolContextReceivers();
        IPanelContextReceiver[] FindAllPanelContextReceivers();

        // ================================================================
        // 時間
        // ================================================================

        double GetTimeSinceStartup();

        // ================================================================
        // GUI - Undo/Redoボタン描画
        // ================================================================

        /// <summary>
        /// Undo/Redoボタンを描画し、押下時にコールバックを呼ぶ。
        /// Editor: EditorGUI.DisabledScope使用。
        /// Runtime: GUI.enabled使用。
        /// </summary>
        void DrawUndoRedoButtons(bool canUndo, bool canRedo, Action onUndo, Action onRedo);
    }
}
