using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Poly_Ling.EditorCore;

public class MaterialGarbageCollectorWindow : EditorWindow
{
    private string folderPath = "Assets/SavedMaterials";
    private List<EditorMaterialGarbageCollector.MaterialEntry> scannedEntries = new List<EditorMaterialGarbageCollector.MaterialEntry>();
    private Vector2 scrollPos;
    private bool hasScanned = false;


    //単体コマンド
    [MenuItem("Tools/Utility/Misc/Material Garbage Collector")]
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
                EditorMaterialGarbageCollector.DeleteEmptyFolders(folderPath, folderPath);
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
        scannedEntries = EditorMaterialGarbageCollector.Scan(folderPath);
        hasScanned = true;
    }

    private void DeleteSelected()
    {
        EditorMaterialGarbageCollector.DeleteSelected(scannedEntries, folderPath);
    }

}
