using System.IO;
using UnityEditor;
using UnityEngine;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

/// <summary>
/// URP向け標準色マテリアルを一括生成するエディタウインドウである。
/// PowerPointの標準色イメージで16色のマテリアルを作成する。
/// </summary>
public class UrpStandardMaterialGeneratorWindow : EditorWindow
{
    // 出力先フォルダのプロジェクト相対パス（Assets から始まる）
    private string folderPath = "Assets/Resources/Materials/URPStandardColors";

    // シェーダ種類
    private bool useUrpLit = true;
    private bool useUrpUnlit = false;

    // 標準色16色
    private static readonly Color[] StandardColors =
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

    // マテリアル名に付ける色名（記号的に付けているだけである）
    private static readonly string[] StandardColorNames =
    {
        "Red",
        "Orange",
        "Yellow",
        "Lime",
        "Green",
        "Cyan",
        "SkyBlue",
        "Blue",
        "Navy",
        "Purple",
        "Magenta",
        "Brown",
        "Gray",
        "DarkGray",
        "Black",
        "White",
    };

    [MenuItem("Tools/Poly_Ling/Utility/URP Standard Material Generator")]
    public static void ShowWindow()
    {
        GetWindow<UrpStandardMaterialGeneratorWindow>(
            title: "URP Standard Materials");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("URP 標準色マテリアル生成", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawFolderField();
        EditorGUILayout.Space(4);

        DrawShaderSelection();
        EditorGUILayout.Space(8);

        DrawColorPreview();
        EditorGUILayout.Space(8);

        DrawGenerateButton();
    }

    /// <summary>
    /// 出力フォルダの指定UIを描画する。
    /// </summary>
    private void DrawFolderField()
    {
        EditorGUILayout.LabelField("出力先フォルダ（Assets からの相対パス）");

        EditorGUILayout.BeginHorizontal();
        folderPath = EditorGUILayout.TextField(folderPath);

        if (GUILayout.Button("フォルダ選択...", GUILayout.MaxWidth(100)))
        {
            string selected = EditorUtility.OpenFolderPanel(
                "マテリアル出力先フォルダを選択",
                Application.dataPath,
                "");

            if (!string.IsNullOrEmpty(selected))
            {
                // 絶対パスから "Assets/..." 相対パスに変換する。
                if (selected.StartsWith(Application.dataPath))
                {
                    folderPath = "Assets" + selected.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "フォルダエラー",
                        "選択したフォルダがプロジェクトの Assets フォルダ外である。",
                        "OK");
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// 使用するURPシェーダの選択UIを描画する。
    /// Lit と Unlit は排他的である。
    /// </summary>
    private void DrawShaderSelection()
    {
        EditorGUILayout.LabelField("使用シェーダ");

        EditorGUI.BeginChangeCheck();
        bool lit = EditorGUILayout.ToggleLeft("URP/Lit", useUrpLit);
        bool unlit = EditorGUILayout.ToggleLeft("URP/Unlit", useUrpUnlit);
        if (EditorGUI.EndChangeCheck())
        {
            // どちらか一方だけをオンにする
            if (lit && !useUrpLit)
            {
                useUrpLit = true;
                useUrpUnlit = false;
            }
            else if (unlit && !useUrpUnlit)
            {
                useUrpUnlit = true;
                useUrpLit = false;
            }
            else if (!lit && !unlit)
            {
                // 両方オフになった場合は Lit をデフォルトでオンに戻す
                useUrpLit = true;
                useUrpUnlit = false;
            }
        }
    }

    /// <summary>
    /// 生成される16色の簡易プレビューを描画する。
    /// </summary>
    private void DrawColorPreview()
    {
        EditorGUILayout.LabelField("標準色16色プレビュー");

        int columns = 8; // 8x2 のグリッドにする
        int size = 20;

        for (int row = 0; row < 2; row++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                if (index >= StandardColors.Length)
                    break;

                Color c = StandardColors[index];

                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                UnityEditor_Handles.BeginGUI();
                UnityEditor_Handles.DrawRect(r, c);//?
                UnityEditor_Handles.EndGUI();
                // ツールチップ用ラベル（見た目は小さく）
                GUI.Label(r, new GUIContent("", $"{index + 1:00} {StandardColorNames[index]}"));
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// 生成ボタンを描画し、押されたらマテリアルを生成する。
    /// </summary>
    private void DrawGenerateButton()
    {
        GUI.enabled = !string.IsNullOrEmpty(folderPath);

        if (GUILayout.Button("標準色16マテリアルを生成"))
        {
            GenerateMaterials();
        }

        GUI.enabled = true;
    }

    /// <summary>
    /// 実際にマテリアルを16個生成する処理である。
    /// </summary>
    private void GenerateMaterials()
    {
        // 出力先フォルダ確認
        if (string.IsNullOrEmpty(folderPath) || !folderPath.StartsWith("Assets"))
        {
            EditorUtility.DisplayDialog(
                "パスエラー",
                "フォルダパスは必ず \"Assets\" から始まる必要がある。",
                "OK");
            return;
        }

        // シェーダ取得
        string shaderName = useUrpUnlit
            ? "Universal Render Pipeline/Unlit"
            : "Universal Render Pipeline/Lit";

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "シェーダエラー",
                $"シェーダ \"{shaderName}\" が見つからない。\n" +
                "URP が正しく導入・設定されているか確認する必要がある。",
                "OK");
            return;
        }

        // フォルダが存在しなければ作成
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            // "Assets" から始まるパスを分解してフォルダを逐次作成する
            CreateFolderRecursive(folderPath);
        }

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
                // 既存がある場合はシェーダを差し替える
                mat.shader = shader;
            }

            // URP Lit / Unlit 共通のベースカラープロパティ
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                // 念のため _Color も見る
                mat.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "生成完了",
            $"フォルダ:\n{folderPath}\n\nに標準色マテリアルを生成した。",
            "OK");
    }

    /// <summary>
    /// "Assets/..." 形式のフォルダパスを受け取り、存在しない部分を再帰的に作成する。
    /// </summary>
    private static void CreateFolderRecursive(string fullPath)
    {
        // 例: Assets/Materials/URPStandardColors
        string[] parts = fullPath.Split('/');
        if (parts.Length < 2 || parts[0] != "Assets")
            return;

        string currentPath = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = $"{currentPath}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                string folderName = parts[i];
                AssetDatabase.CreateFolder(currentPath, folderName);
            }
            currentPath = nextPath;
        }
    }
}
