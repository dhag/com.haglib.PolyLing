using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// 選択したゲームオブジェクト配下のマテリアルを、指定フォルダ内の同名マテリアルに差し替えるエディタ拡張
/// </summary>
public class MaterialReplacer : EditorWindow
{
    private string targetFolderPath = "Assets/model";
    private Vector2 scrollPosition;
    private List<ReplacementInfo> previewList = new List<ReplacementInfo>();
    private bool showPreview = false;

    private class ReplacementInfo
    {
        public Renderer renderer;
        public int materialIndex;
        public Material currentMaterial;
        public Material newMaterial;
        public string currentPath;
        public string newPath;
    }

    [MenuItem("Tools/Poly_Ling/Utility/Material Replacer")]
    public static void ShowWindow()
    {
        var window = GetWindow<MaterialReplacer>("Material Replacer");
        window.minSize = new Vector2(450, 300);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("マテリアル差し替えツール", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.HelpBox(
            "ヒエラルキーで選択したゲームオブジェクト配下の全てのRenderer（MeshRenderer, SkinnedMeshRenderer）が使用しているマテリアルを、" +
            "指定フォルダ内の同名マテリアルに差し替えます。",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // フォルダ選択
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("差し替え元フォルダ:", GUILayout.Width(120));
        targetFolderPath = EditorGUILayout.TextField(targetFolderPath);
        if (GUILayout.Button("選択", GUILayout.Width(50)))
        {
            string path = EditorUtility.OpenFolderPanel("マテリアルフォルダを選択", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                // 相対パスに変換
                if (path.StartsWith(Application.dataPath))
                {
                    targetFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内のフォルダを選択してください。", "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 選択中のオブジェクト表示
        var selectedObject = Selection.activeGameObject;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("選択中のオブジェクト:", GUILayout.Width(120));
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField(selectedObject, typeof(GameObject), true);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ボタン
        EditorGUILayout.BeginHorizontal();
        
        EditorGUI.BeginDisabledGroup(selectedObject == null);
        if (GUILayout.Button("プレビュー", GUILayout.Height(30)))
        {
            GeneratePreview(selectedObject);
            showPreview = true;
        }
        
        if (GUILayout.Button("差し替え実行", GUILayout.Height(30)))
        {
            if (previewList.Count == 0)
            {
                GeneratePreview(selectedObject);
            }
            
            if (previewList.Count > 0)
            {
                ExecuteReplacement();
            }
            else
            {
                EditorUtility.DisplayDialog("情報", "差し替え対象のマテリアルが見つかりませんでした。", "OK");
            }
        }
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.EndHorizontal();

        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("ヒエラルキーでゲームオブジェクトを選択してください。", MessageType.Warning);
        }

        // プレビュー表示
        if (showPreview)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField($"差し替え対象: {previewList.Count}件", EditorStyles.boldLabel);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            foreach (var info in previewList)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Renderer:", GUILayout.Width(70));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(info.renderer, typeof(Renderer), true);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Index:", GUILayout.Width(70));
                EditorGUILayout.LabelField(info.materialIndex.ToString(), GUILayout.Width(30));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("現在:", GUILayout.Width(70));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(info.currentMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("→ 新規:", GUILayout.Width(70));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(info.newMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
            
            EditorGUILayout.EndScrollView();

            if (previewList.Count == 0)
            {
                EditorGUILayout.HelpBox("差し替え対象のマテリアルが見つかりませんでした。\n" +
                    "・指定フォルダにマテリアルが存在するか確認してください。\n" +
                    "・マテリアル名が一致しているか確認してください。", MessageType.Info);
            }
        }
    }

    private void GeneratePreview(GameObject root)
    {
        previewList.Clear();

        if (root == null) return;
        if (!AssetDatabase.IsValidFolder(targetFolderPath))
        {
            EditorUtility.DisplayDialog("エラー", $"指定されたフォルダが見つかりません:\n{targetFolderPath}", "OK");
            return;
        }

        // 指定フォルダ内のマテリアルを取得（サブフォルダも含む）
        var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { targetFolderPath });
        var materialDict = new Dictionary<string, Material>();

        foreach (var guid in materialGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                // 同名マテリアルが複数ある場合は最初に見つかったものを使用
                if (!materialDict.ContainsKey(material.name))
                {
                    materialDict[material.name] = material;
                }
            }
        }

        Debug.Log($"[MaterialReplacer] フォルダ '{targetFolderPath}' 内のマテリアル数: {materialDict.Count}");

        // 全Rendererを取得（MeshRenderer, SkinnedMeshRenderer両方）
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            
            for (int i = 0; i < materials.Length; i++)
            {
                var currentMat = materials[i];
                if (currentMat == null) continue;

                // 同名のマテリアルを検索
                if (materialDict.TryGetValue(currentMat.name, out var newMaterial))
                {
                    // 同じマテリアルでなければ差し替え対象に追加
                    if (currentMat != newMaterial)
                    {
                        previewList.Add(new ReplacementInfo
                        {
                            renderer = renderer,
                            materialIndex = i,
                            currentMaterial = currentMat,
                            newMaterial = newMaterial,
                            currentPath = AssetDatabase.GetAssetPath(currentMat),
                            newPath = AssetDatabase.GetAssetPath(newMaterial)
                        });
                    }
                }
            }
        }

        Debug.Log($"[MaterialReplacer] 差し替え対象: {previewList.Count}件");
    }

    private void ExecuteReplacement()
    {
        if (previewList.Count == 0) return;

        int replacedCount = 0;
        
        Undo.SetCurrentGroupName("Material Replacement");
        int undoGroup = Undo.GetCurrentGroup();

        // Rendererごとにグループ化して処理
        var rendererGroups = previewList.GroupBy(x => x.renderer);

        foreach (var group in rendererGroups)
        {
            var renderer = group.Key;
            Undo.RecordObject(renderer, "Replace Materials");

            var materials = renderer.sharedMaterials.ToArray();
            
            foreach (var info in group)
            {
                materials[info.materialIndex] = info.newMaterial;
                replacedCount++;
                
                Debug.Log($"[MaterialReplacer] 差し替え: {renderer.gameObject.name} [{info.materialIndex}] " +
                    $"'{info.currentMaterial.name}' → '{info.newMaterial.name}'");
            }

            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }

        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("完了", $"{replacedCount}件のマテリアルを差し替えました。\n\nCtrl+Zで元に戻せます。", "OK");
        
        previewList.Clear();
        showPreview = false;
    }

    private void OnSelectionChange()
    {
        // 選択が変わったらプレビューをクリア
        previewList.Clear();
        showPreview = false;
        Repaint();
    }
}
