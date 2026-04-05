using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// 未参照マテリアルのスキャン・削除処理のEditorCore実装。
    /// MaterialGarbageCollectorWindow（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorMaterialGarbageCollector
    {
        /// <summary>スキャン結果エントリ</summary>
        public class MaterialEntry
        {
            public Material material;
            public string assetPath;
            public bool selected;
        }

        /// <summary>
        /// 指定フォルダ内のマテリアルをスキャンし、シーン上で未参照のものを返す。
        /// エラー時は空リストを返す。
        /// </summary>
        public static List<MaterialEntry> Scan(string folderPath)
        {
            var result = new List<MaterialEntry>();

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("エラー", $"フォルダが見つかりません:\n{folderPath}", "OK");
                return result;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });
            if (guids.Length == 0) return result;

            var folderMaterials = new Dictionary<string, Material>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) folderMaterials[path] = mat;
            }

            var referencedMaterials = new HashSet<Material>();
            var allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var renderer in allRenderers)
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null) referencedMaterials.Add(mat);

            foreach (var kvp in folderMaterials)
            {
                if (!referencedMaterials.Contains(kvp.Value))
                    result.Add(new MaterialEntry { material = kvp.Value, assetPath = kvp.Key, selected = true });
            }

            result.Sort((a, b) => string.Compare(a.assetPath, b.assetPath));
            return result;
        }

        /// <summary>選択済みエントリを削除し、空フォルダも後片付けする</summary>
        public static void DeleteSelected(List<MaterialEntry> entries, string rootFolder)
        {
            var toDelete = entries.Where(e => e.selected).ToList();
            foreach (var entry in toDelete)
                AssetDatabase.DeleteAsset(entry.assetPath);

            AssetDatabase.Refresh();
            DeleteEmptyFolders(rootFolder, rootFolder);
            AssetDatabase.Refresh();

            entries.RemoveAll(e => e.selected);
        }

        /// <summary>
        /// 指定フォルダ内の空フォルダを末端から再帰的に削除する。
        /// .metaファイルのみ残っているフォルダも空として扱う。
        /// </summary>
        public static void DeleteEmptyFolders(string folder, string rootFolder)
        {
            string fullPath = AssetPathToFull(folder);
            Debug.Log($"[EditorMaterialGarbageCollector] Checking folder: {fullPath}, exists={Directory.Exists(fullPath)}");
            if (!Directory.Exists(fullPath)) return;

            string[] subDirs = Directory.GetDirectories(fullPath);
            foreach (string sub in subDirs)
            {
                string relativeSub = FullPathToAsset(sub);
                DeleteEmptyFolders(relativeSub, rootFolder);
            }

            if (folder == rootFolder) return;

            if (!Directory.Exists(fullPath)) return;
            string[] files = Directory.GetFiles(fullPath);
            string[] dirs = Directory.GetDirectories(fullPath);

            Debug.Log($"[EditorMaterialGarbageCollector] {folder}: files={files.Length}, dirs={dirs.Length}");
            foreach (string f in files)
                Debug.Log($"[EditorMaterialGarbageCollector]   file: {Path.GetFileName(f)}");

            bool isEmpty = dirs.Length == 0 && files.All(f => f.EndsWith(".meta"));
            Debug.Log($"[EditorMaterialGarbageCollector] {folder}: isEmpty={isEmpty}");
            if (isEmpty)
            {
                foreach (string f in files) File.Delete(f);
                Directory.Delete(fullPath, true);
                Debug.Log($"[EditorMaterialGarbageCollector] Deleted folder: {fullPath}");
                string folderMeta = fullPath + ".meta";
                if (File.Exists(folderMeta))
                {
                    File.Delete(folderMeta);
                    Debug.Log($"[EditorMaterialGarbageCollector] Deleted meta: {folderMeta}");
                }
            }
        }

        private static string AssetPathToFull(string assetPath)
            => Application.dataPath + assetPath.Substring("Assets".Length);

        private static string FullPathToAsset(string fullPath)
            => "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
    }
}
