#if UNITY_EDITOR

using Poly_Ling.Player;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class PolyLingPlayerViewerSetupWindow : EditorWindow
{
    private const string DefaultPanelName = "New Panel Settings";
    private const string DefaultGameObjectName = "PolyLing Player Viewer";

    [SerializeField]
    private string panelName = DefaultPanelName;

    [SerializeField]
    private string gameObjectName = DefaultGameObjectName;

    [MenuItem("PolyLing/CreateRuntime/Create Player Viewer")]
    private static void Open()
    {
        var window = GetWindow<PolyLingPlayerViewerSetupWindow>();
        window.titleContent = new GUIContent("Player Viewer作成");
        window.minSize = new Vector2(420f, 180f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField(
            "PolyLing Player Viewer セットアップ",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(8);

        panelName = EditorGUILayout.TextField(
            "パネルセッティング名",
            panelName);

        gameObjectName = EditorGUILayout.TextField(
            "ゲームオブジェクト名",
            gameObjectName);

        EditorGUILayout.Space(8);

        string previewPath = GetPanelAssetPath(panelName);

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(panelName)))
        {
            EditorGUILayout.LabelField(
                "作成先",
                previewPath,
                EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.Space(12);

        if (GUILayout.Button("作成", GUILayout.Height(32)))
        {
            CreateSetup();
        }
    }

    private void CreateSetup()
    {
        panelName = panelName?.Trim();
        gameObjectName = gameObjectName?.Trim();

        /*
         * 重要：
         * 何かを作成する前に、すべての検査を完了する。
         * 検査に失敗した場合、アセットもGameObjectも作成しない。
         */

        if (!ValidateInput())
        {
            return;
        }

        string assetPath = GetPanelAssetPath(panelName);

        if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
        {
            ShowWarning(
                "作成中止",
                $"次のアセットは既に存在する。\n\n{assetPath}\n\n何も作成しなかった。");

            return;
        }

        GameObject existingGameObject = FindSceneGameObjectByExactName(
            gameObjectName);

        if (existingGameObject != null)
        {
            ShowWarning(
                "作成中止",
                $"同名のゲームオブジェクトが既に存在する。\n\n" +
                $"名前: {gameObjectName}\n" +
                $"シーン: {existingGameObject.scene.name}\n\n" +
                "何も作成しなかった。");

            return;
        }

        CreateAssetAndGameObject(assetPath);
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(panelName))
        {
            ShowWarning(
                "入力エラー",
                "パネルセッティング名は空欄にできない。");

            return false;
        }

        if (string.IsNullOrWhiteSpace(gameObjectName))
        {
            ShowWarning(
                "入力エラー",
                "ゲームオブジェクト名は空欄にできない。");

            return false;
        }

        if (!IsValidAssetFileName(panelName))
        {
            ShowWarning(
                "入力エラー",
                "パネルセッティング名にファイル名として使用できない文字が含まれている。\n\n" +
                @"使用できない例: \ / : * ? "" < > |");

            return false;
        }

        return true;
    }

    private void CreateAssetAndGameObject(string assetPath)
    {
        PanelSettings panelSettings = null;
        GameObject createdGameObject = null;

        try
        {
            // Panel Settingsアセットを作成する。
            panelSettings = CreateInstance<PanelSettings>();
            panelSettings.name = panelName;

            AssetDatabase.CreateAsset(panelSettings, assetPath);

            // ゲームオブジェクトを作成する。
            createdGameObject = new GameObject(gameObjectName);
            Undo.RegisterCreatedObjectUndo(
                createdGameObject,
                "Create PolyLing Player Viewer");

            // UI Documentを追加してPanel Settingsを割り当てる。
            UIDocument uiDocument =
                Undo.AddComponent<UIDocument>(createdGameObject);

            uiDocument.panelSettings = panelSettings;

            // PolyLingPlayerViewerを追加する。
            Undo.AddComponent<PolyLingPlayerViewer>(createdGameObject);

            EditorUtility.SetDirty(uiDocument);
            EditorUtility.SetDirty(createdGameObject);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.MarkSceneDirty(
                createdGameObject.scene);

            Selection.activeGameObject = createdGameObject;
            EditorGUIUtility.PingObject(createdGameObject);

            EditorUtility.DisplayDialog(
                "作成完了",
                $"以下を作成した。\n\n" +
                $"Panel Settings:\n{assetPath}\n\n" +
                $"GameObject:\n{gameObjectName}",
                "OK");
        }
        catch (Exception exception)
        {
            /*
             * 途中で例外が発生した場合も、多重作成や半端な状態を
             * 残さないようにロールバックする。
             */

            if (createdGameObject != null)
            {
                DestroyImmediate(createdGameObject);
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            else if (panelSettings != null)
            {
                DestroyImmediate(panelSettings);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogException(exception);

            EditorUtility.DisplayDialog(
                "作成失敗",
                "作成中にエラーが発生したため、作成内容を取り消した。\n\n" +
                exception.Message,
                "OK");
        }
    }

    private static GameObject FindSceneGameObjectByExactName(
        string targetName)
    {
        /*
         * 非アクティブなGameObjectも含めて検索する。
         * Project内のPrefabアセットなどは対象外とし、
         * 読み込まれているシーン内だけを検査する。
         */
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        return transforms
            .Where(transform => transform != null)
            .Where(transform => transform.gameObject.scene.IsValid())
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject =>
                string.Equals(
                    gameObject.name,
                    targetName,
                    StringComparison.Ordinal));
    }

    private static bool IsValidAssetFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        // macOSなども考慮し、Unityのパス区切り文字を明示的に禁止する。
        return !value.Contains("/") &&
               !value.Contains("\\") &&
               !value.EndsWith(".", StringComparison.Ordinal);
    }

    private static string GetPanelAssetPath(string name)
    {
        string trimmedName = name?.Trim();

        if (string.IsNullOrEmpty(trimmedName))
        {
            trimmedName = DefaultPanelName;
        }

        return $"Assets/{trimmedName}.asset";
    }

    private static void ShowWarning(
        string title,
        string message)
    {
        EditorUtility.DisplayDialog(
            title,
            message,
            "OK");
    }
}

#endif