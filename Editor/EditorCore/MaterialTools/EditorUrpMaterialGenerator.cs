using System.IO;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// URP標準色マテリアルを一括生成するロジックのEditorCore実装。
    /// UrpStandardMaterialGeneratorWindow（EditorWindow）はUIを保持し、本クラスを呼び出す。
    /// </summary>
    public static class EditorUrpMaterialGenerator
    {
        // ================================================================
        // 標準色データ（ウィンドウから移動）
        // ================================================================

        public static readonly Color[] StandardColors =
        {
            new Color(1.0f, 0.0f, 0.0f),       // 01 Red
            new Color(1.0f, 0.5f, 0.0f),       // 02 Orange
            new Color(1.0f, 1.0f, 0.0f),       // 03 Yellow
            new Color(0.5f, 1.0f, 0.0f),       // 04 Lime
            new Color(0.0f, 0.6f, 0.0f),       // 05 Green
            new Color(0.0f, 1.0f, 1.0f),       // 06 Cyan
            new Color(0.3f, 0.7f, 1.0f),       // 07 SkyBlue
            new Color(0.0f, 0.0f, 1.0f),       // 08 Blue
            new Color(0.0f, 0.0f, 0.5f),       // 09 Navy
            new Color(0.5f, 0.0f, 0.8f),       // 10 Purple
            new Color(1.0f, 0.0f, 1.0f),       // 11 Magenta
            new Color(0.6f, 0.3f, 0.1f),       // 12 Brown
            new Color(0.75f, 0.75f, 0.75f),    // 13 Gray
            new Color(0.4f, 0.4f, 0.4f),       // 14 DarkGray
            new Color(0.0f, 0.0f, 0.0f),       // 15 Black
            new Color(1.0f, 1.0f, 1.0f),       // 16 White
        };

        public static readonly string[] StandardColorNames =
        {
            "Red", "Orange", "Yellow", "Lime", "Green",
            "Cyan", "SkyBlue", "Blue", "Navy", "Purple",
            "Magenta", "Brown", "Gray", "DarkGray", "Black", "White",
        };

        // ================================================================
        // ロジック（ウィンドウから移動）
        // ================================================================

        /// <summary>標準色マテリアルを16個生成してfolderPathに保存する</summary>
        public static void GenerateMaterials(string folderPath, bool useUrpUnlit)
        {
            if (string.IsNullOrEmpty(folderPath) || !folderPath.StartsWith("Assets"))
            {
                EditorUtility.DisplayDialog("パスエラー", "フォルダパスは必ず \"Assets\" から始まる必要がある。", "OK");
                return;
            }

            string shaderName = useUrpUnlit
                ? "Universal Render Pipeline/Unlit"
                : "Universal Render Pipeline/Lit";

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("シェーダーエラー",
                    $"シェーダー \"{shaderName}\" が見つかない。\nURP がインストール・設定されているか確認する必要がある。", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
                CreateFolderRecursive(folderPath);

            string shaderShort = useUrpUnlit ? "URPUnlit" : "URPLit";

            for (int i = 0; i < StandardColors.Length; i++)
            {
                Color color = StandardColors[i];
                string colorName = StandardColorNames[i];
                string matName = $"{shaderShort}_{i + 1:00}_{colorName}.mat";
                string matPath = Path.Combine(folderPath, matName).Replace("\\", "/");

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = shader;
                }

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", color);

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完了", $"フォルダ:\n{folderPath}\n\nに標準色マテリアルを生成した。", "OK");
        }

        /// <summary>"Assets/..." 形式のフォルダパスを受け取り、存在しなければ再帰的に作成する</summary>
        public static void CreateFolderRecursive(string fullPath)
        {
            string[] parts = fullPath.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets") return;
            string currentPath = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = $"{currentPath}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                currentPath = nextPath;
            }
        }
    }
}
