using System.IO;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Assets/Resources/photos 以下の画像から、同名マテリアルを Assets/Resources/mat に生成するユーティリティである。
/// </summary>
public static class GenerateMaterialsFromPhotos
{
    [MenuItem("Tools/Poly_Ling/Utility/Generate Materials From Texture Assets")]
    public static void GenerateMaterials()
    {
        string sourceFolder = "Assets/Resources/photos";
        string targetFolder = "Assets/Resources/mat";

        // フォルダの存在チェック
        if (!AssetDatabase.IsValidFolder(sourceFolder))
        {
            Debug.LogError($"フォルダが存在しない: {sourceFolder}");
            return;
        }

        // 出力フォルダがなければ作成
        CreateFolderRecursive(targetFolder);

        // Texture2D をすべて検索
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { sourceFolder });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning($"Texture2D が見つからなかった: {sourceFolder}");
            return;
        }

        // シェーダを取得（URP → Standard の順で試す）
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null)
        {
            Debug.LogError("使用可能なシェーダが見つからない（URP Lit も Standard も取得できない）");
            return;
        }

        int createdCount = 0;
        int updatedCount = 0;

        foreach (string guid in guids)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
                continue;

            string fileName = Path.GetFileNameWithoutExtension(texPath);
            string matPath = $"{targetFolder}/{fileName}.mat";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader)
                {
                    name = fileName,
                    mainTexture = tex
                };
                AssetDatabase.CreateAsset(mat, matPath);
                createdCount++;
            }
            else
            {
                mat.shader = shader;
                mat.mainTexture = tex;
                EditorUtility.SetDirty(mat);
                updatedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"マテリアル生成完了: 新規 {createdCount} 件 / 更新 {updatedCount} 件 / 出力先 {targetFolder}");
    }

    private static void CreateFolderRecursive(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string folderName = Path.GetFileName(path);

        if (!AssetDatabase.IsValidFolder(parent))
        {
            CreateFolderRecursive(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
        Debug.Log($"フォルダを作成した: {path}");
    }
}