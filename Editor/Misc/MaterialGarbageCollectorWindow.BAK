using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class MaterialGarbageCollectorWindow : EditorWindow
{
    private string folderPath = "Assets/SavedMaterials";
    private List<MaterialEntry> scannedEntries = new List<MaterialEntry>();
    private Vector2 scrollPos;
    private bool hasScanned = false;

    private class MaterialEntry
    {
        public Material material;
        public string assetPath;
        public bool selected;
    }

    [MenuItem("Tools/Material Garbage Collector")]
    public static void ShowWindow()
    {
        GetWindow<MaterialGarbageCollectorWindow>("Material GC");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Material Garbage Collector", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // フォルダパス指定
        EditorGUILayout.BeginHorizontal();
        folderPath = EditorGUILayout.TextField("フォルダ", folderPath);
        if (GUILayout.Button("選択", GUILayout.Width(50)))
        {
            string selected = EditorUtility.OpenFolderPanel("マテリアルフォルダを選択", "Assets", "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    folderPath = "Assets" + selected.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // スキャンボタン
        if (GUILayout.Button("スキャン", GUILayout.Height(28)))
        {
            Scan();
        }

        if (!hasScanned) return;

        EditorGUILayout.Space(4);

        if (scannedEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("未参照のマテリアルはありません。", MessageType.Info);
            EditorGUILayout.Space(4);
            if (GUILayout.Button("空フォルダを削除", GUILayout.Height(28)))
            {
                DeleteEmptyFolders(folderPath);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("完了", "空フォルダの削除が完了しました。", "OK");
            }
            return;
        }

        EditorGUILayout.LabelField($"未参照マテリアル: {scannedEntries.Count} 件");

        // 全選択/全解除
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全選択", GUILayout.Width(60)))
        {
            foreach (var e in scannedEntries) e.selected = true;
        }
        if (GUILayout.Button("全解除", GUILayout.Width(60)))
        {
            foreach (var e in scannedEntries) e.selected = false;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // リスト表示
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        foreach (var entry in scannedEntries)
        {
            EditorGUILayout.BeginHorizontal();
            entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(20));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(entry.material, typeof(Material), false);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);

        // 削除ボタン
        int selectedCount = scannedEntries.Count(e => e.selected);
        EditorGUI.BeginDisabledGroup(selectedCount == 0);
        GUI.backgroundColor = selectedCount > 0 ? new Color(1f, 0.4f, 0.4f) : Color.white;
        if (GUILayout.Button($"選択したマテリアルを削除 ({selectedCount} 件)", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog(
                "マテリアル削除確認",
                $"{selectedCount} 件のマテリアルを削除します。\nこの操作は取り消せません。",
                "削除", "キャンセル"))
            {
                DeleteSelected();
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
    }

    private void Scan()
    {
        scannedEntries.Clear();
        hasScanned = true;

        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("エラー", $"フォルダが見つかりません:\n{folderPath}", "OK");
            return;
        }

        // 指定フォルダ内の全マテリアルを取得
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });
        if (guids.Length == 0) return;

        var folderMaterials = new Dictionary<string, Material>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                folderMaterials[path] = mat;
        }

        // シーン内の全Rendererが参照しているマテリアルを収集
        var referencedMaterials = new HashSet<Material>();
        var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var renderer in allRenderers)
        {
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat != null)
                    referencedMaterials.Add(mat);
            }
        }

        // 未参照マテリアルを抽出
        foreach (var kvp in folderMaterials)
        {
            if (!referencedMaterials.Contains(kvp.Value))
            {
                scannedEntries.Add(new MaterialEntry
                {
                    material = kvp.Value,
                    assetPath = kvp.Key,
                    selected = true
                });
            }
        }

        // パスでソート
        scannedEntries.Sort((a, b) => string.Compare(a.assetPath, b.assetPath));
    }

    private void DeleteSelected()
    {
        var toDelete = scannedEntries.Where(e => e.selected).ToList();
        foreach (var entry in toDelete)
        {
            AssetDatabase.DeleteAsset(entry.assetPath);
        }

        // AssetDatabase状態を同期してからフォルダ削除
        AssetDatabase.Refresh();

        // 空フォルダを末端から再帰的に削除（ファイルシステムレベル）
        DeleteEmptyFolders(folderPath);

        AssetDatabase.Refresh();

        // 削除済みをリストから除去
        scannedEntries.RemoveAll(e => e.selected);
    }

    /// <summary>
    /// 指定フォルダ内の空フォルダを末端から再帰的に削除する。
    /// .metaファイルのみ残っているフォルダも空として扱う。
    /// </summary>
    private void DeleteEmptyFolders(string folder)
    {
        string fullPath = AssetPathToFull(folder);
        Debug.Log($"[MaterialGC] Checking folder: {fullPath}, exists={Directory.Exists(fullPath)}");
        if (!Directory.Exists(fullPath)) return;

        // 末端から処理するため、先にサブフォルダを再帰
        string[] subDirs = Directory.GetDirectories(fullPath);
        foreach (string sub in subDirs)
        {
            string relativeSub = FullPathToAsset(sub);
            DeleteEmptyFolders(relativeSub);
        }

        // ルートフォルダ自体は削除しない
        if (folder == folderPath) return;

        // サブフォルダ削除後に再チェック
        if (!Directory.Exists(fullPath)) return;
        string[] files = Directory.GetFiles(fullPath);
        string[] dirs = Directory.GetDirectories(fullPath);

        Debug.Log($"[MaterialGC] {folder}: files={files.Length}, dirs={dirs.Length}");
        foreach (string f in files)
            Debug.Log($"[MaterialGC]   file: {Path.GetFileName(f)}");

        // ファイルが.metaのみ、またはファイルなし、かつサブフォルダなしなら空扱い
        bool isEmpty = dirs.Length == 0 && files.All(f => f.EndsWith(".meta"));
        Debug.Log($"[MaterialGC] {folder}: isEmpty={isEmpty}");
        if (isEmpty)
        {
            foreach (string f in files)
                File.Delete(f);

            Directory.Delete(fullPath, true);
            Debug.Log($"[MaterialGC] Deleted folder: {fullPath}");

            string folderMeta = fullPath + ".meta";
            if (File.Exists(folderMeta))
            {
                File.Delete(folderMeta);
                Debug.Log($"[MaterialGC] Deleted meta: {folderMeta}");
            }
        }
    }

    private static string AssetPathToFull(string assetPath)
    {
        // "Assets/xxx" → Application.dataPath + "/xxx"
        return Application.dataPath + assetPath.Substring("Assets".Length);
    }

    private static string FullPathToAsset(string fullPath)
    {
        // "/path/to/project/Assets/xxx" → "Assets/xxx"
        return "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
    }
}
