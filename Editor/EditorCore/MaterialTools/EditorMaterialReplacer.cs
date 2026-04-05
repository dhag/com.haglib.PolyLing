using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// 選択GameObjectのマテリアルを指定フォルダ内の同名マテリアルに差し替えるロジック。
    /// MaterialReplacer（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorMaterialReplacer
    {
        /// <summary>差し替え情報を保持する構造体</summary>
        public class ReplacementInfo
        {
            public Renderer renderer;
            public int materialIndex;
            public Material currentMaterial;
            public Material newMaterial;
            public string currentPath;
            public string newPath;
        }

        /// <summary>
        /// 指定フォルダ内の同名マテリアルとのマッチングプレビューを生成する。
        /// エラー時は空リストを返す。
        /// </summary>
        public static List<ReplacementInfo> GeneratePreview(GameObject root, string targetFolderPath)
        {
            var result = new List<ReplacementInfo>();
            if (root == null) return result;

            if (!AssetDatabase.IsValidFolder(targetFolderPath))
            {
                EditorUtility.DisplayDialog("エラー", $"指定されたフォルダが見つかりません:\n{targetFolderPath}", "OK");
                return result;
            }

            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { targetFolderPath });
            var materialDict = new Dictionary<string, Material>();
            foreach (var guid in materialGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && !materialDict.ContainsKey(material.name))
                    materialDict[material.name] = material;
            }

            Debug.Log($"[EditorMaterialReplacer] フォルダ '{targetFolderPath}' 内のマテリアル数: {materialDict.Count}");

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var currentMat = materials[i];
                    if (currentMat == null) continue;
                    if (materialDict.TryGetValue(currentMat.name, out var newMaterial) && currentMat != newMaterial)
                    {
                        result.Add(new ReplacementInfo
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

            Debug.Log($"[EditorMaterialReplacer] 差し替え対象: {result.Count}件");
            return result;
        }

        /// <summary>プレビューリストに基づいてマテリアルを実際に差し替える</summary>
        public static void ExecuteReplacement(List<ReplacementInfo> previewList)
        {
            if (previewList == null || previewList.Count == 0) return;

            int replacedCount = 0;
            Undo.SetCurrentGroupName("Material Replacement");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var group in previewList.GroupBy(x => x.renderer))
            {
                var renderer = group.Key;
                Undo.RecordObject(renderer, "Replace Materials");
                var materials = renderer.sharedMaterials.ToArray();
                foreach (var info in group)
                {
                    materials[info.materialIndex] = info.newMaterial;
                    replacedCount++;
                    Debug.Log($"[EditorMaterialReplacer] 差し替え: {renderer.gameObject.name} [{info.materialIndex}] " +
                              $"'{info.currentMaterial.name}' → '{info.newMaterial.name}'");
                }
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.DisplayDialog("完了", $"{replacedCount}件のマテリアルを差し替えました。\n\nCtrl+Zで元に戻せます。", "OK");
        }
    }
}
