#if UNITY_EDITOR

using Poly_Ling.ListClient;
using Poly_Ling.Player;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// ランタイム生成で追加できるコンポーネントの種類。
/// </summary>
public enum PolyLingRuntimeObjectKind
{
    /// <summary>PolyLing本体（Poly_Ling.Player.PolyLingPlayerViewer）</summary>
    PlayerViewer = 0,

    /// <summary>モデルリストクライアント（Poly_Ling.ListClient.ModelListClient）</summary>
    ModelListClient = 1,

    /// <summary>オブジェクトリストクライアント（Poly_Ling.ListClient.MeshListClient）</summary>
    ObjectListClient = 2,

    /// <summary>マテリアルリストクライアント（Poly_Ling.ListClient.MaterialListClient）</summary>
    MaterialListClient = 3,

    /// <summary>プローブクライアント（Poly_Ling.ListClient.ProbeClient）</summary>
    ProbeClient = 4,
}

public sealed class PolyLingPlayerViewerSetupWindow : EditorWindow
{
    private const string DefaultPanelName = "New Panel Settings";
    private const string DefaultGameObjectName = "PolyLing Player Viewer";

    /*
     * 種類ごとの既定名。
     * 既定値を共通にすると、2つ目以降の生成が
     * 「アセット重複」「同名GameObject」で必ず中止になるため、
     * 種類ごとに別名を与える。
     * 配列の並びは PolyLingRuntimeObjectKind の値と一致させること。
     */

    private static readonly string[] KindDisplayNames =
    {
        "PolyLing本体（Player Viewer）",
        "モデルリストクライアント",
        "オブジェクトリストクライアント",
        "マテリアルリストクライアント",
        "プローブクライアント",
    };

    private static readonly string[] KindDefaultPanelNames =
    {
        DefaultPanelName,
        "PolyLing Model List Panel Settings",
        "PolyLing Object List Panel Settings",
        "PolyLing Material List Panel Settings",
        "PolyLing Probe Panel Settings",
    };

    private static readonly string[] KindDefaultGameObjectNames =
    {
        DefaultGameObjectName,
        "PolyLing Model List Client",
        "PolyLing Object List Client",
        "PolyLing Material List Client",
        "PolyLing Probe Client",
    };

    [SerializeField]
    private PolyLingRuntimeObjectKind kind = PolyLingRuntimeObjectKind.PlayerViewer;

    [SerializeField]
    private string panelName = DefaultPanelName;

    [SerializeField]
    private string gameObjectName = DefaultGameObjectName;

    [MenuItem("PolyLing/CreateRuntime/Create Runtime Object")]
    private static void Open()
    {
        var window = GetWindow<PolyLingPlayerViewerSetupWindow>();
        window.titleContent = new GUIContent("ランタイム生成");
        window.minSize = new Vector2(420f, 210f);
        window.Show();
    }

    private static int KindIndex(PolyLingRuntimeObjectKind value)
    {
        int index = (int)value;

        if (index < 0 || index >= KindDisplayNames.Length)
        {
            return 0;
        }

        return index;
    }

    private static string KindDisplayName(PolyLingRuntimeObjectKind value)
    {
        return KindDisplayNames[KindIndex(value)];
    }

    private static string KindDefaultPanelName(PolyLingRuntimeObjectKind value)
    {
        return KindDefaultPanelNames[KindIndex(value)];
    }

    private static string KindDefaultGameObjectName(PolyLingRuntimeObjectKind value)
    {
        return KindDefaultGameObjectNames[KindIndex(value)];
    }

    /// <summary>
    /// 種類変更に伴って既定名を追従させる。
    /// 入力欄が「変更前の種類の既定値のまま」のときだけ書き換え、
    /// ユーザーが手で入れた名前は保持する。
    /// </summary>
    private void ApplyKindDefaults(
        PolyLingRuntimeObjectKind previousKind,
        PolyLingRuntimeObjectKind nextKind)
    {
        if (string.Equals(
                panelName,
                KindDefaultPanelName(previousKind),
                StringComparison.Ordinal))
        {
            panelName = KindDefaultPanelName(nextKind);
        }

        if (string.Equals(
                gameObjectName,
                KindDefaultGameObjectName(previousKind),
                StringComparison.Ordinal))
        {
            gameObjectName = KindDefaultGameObjectName(nextKind);
        }

        GUI.FocusControl(null);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField(
            "PolyLing ランタイム生成",
            EditorStyles.boldLabel);

        EditorGUILayout.Space(8);

        int selectedIndex = EditorGUILayout.Popup(
            "種類",
            KindIndex(kind),
            KindDisplayNames);

        var selectedKind = (PolyLingRuntimeObjectKind)selectedIndex;

        if (selectedKind != kind)
        {
            PolyLingRuntimeObjectKind previousKind = kind;
            kind = selectedKind;
            ApplyKindDefaults(previousKind, selectedKind);
        }

        EditorGUILayout.Space(4);

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
                $"Create {KindDisplayName(kind)}");

            // UI Documentを追加してPanel Settingsを割り当てる。
            UIDocument uiDocument =
                Undo.AddComponent<UIDocument>(createdGameObject);

            uiDocument.panelSettings = panelSettings;

            /*
             * 種類に応じた本体コンポーネントを追加する。
             * リスト系クライアントは ListClientBase / ProbeClient が
             * UIDocument に PanelSettings が入っていれば
             * Resources へのフォールバックを行わない。
             * そのため上で割り当てた PanelSettings がそのまま使われる。
             */
            AddMainComponent(createdGameObject);

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
                $"種類:\n{KindDisplayName(kind)}\n\n" +
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

    /// <summary>
    /// 選択された種類に対応する本体コンポーネントを追加する。
    /// </summary>
    private void AddMainComponent(GameObject target)
    {
        switch (kind)
        {
            case PolyLingRuntimeObjectKind.ModelListClient:
                Undo.AddComponent<ModelListClient>(target);
                break;

            case PolyLingRuntimeObjectKind.ObjectListClient:
                Undo.AddComponent<MeshListClient>(target);
                break;

            case PolyLingRuntimeObjectKind.MaterialListClient:
                Undo.AddComponent<MaterialListClient>(target);
                break;

            case PolyLingRuntimeObjectKind.ProbeClient:
                Undo.AddComponent<ProbeClient>(target);
                break;

            case PolyLingRuntimeObjectKind.PlayerViewer:
            default:
                Undo.AddComponent<PolyLingPlayerViewer>(target);
                break;
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