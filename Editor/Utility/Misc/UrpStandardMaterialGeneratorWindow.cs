using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.EditorCore;

/// <summary>
/// URP標準色マテリアルを一括生成するエディタウィンドウ。
/// 生成ロジックは EditorUrpMaterialGenerator に委譲。
/// </summary>
public class UrpStandardMaterialGeneratorWindow : EditorWindow
{
    private string folderPath = "Assets/Resources/Materials/URPStandardColors";
    private bool useUrpLit = true;
    private bool useUrpUnlit = false;

    [MenuItem("Tools/Utility/Misc/Material Generator for URP Standard Material")]
    public static void ShowWindow()
    {
        GetWindow<UrpStandardMaterialGeneratorWindow>(title: "URP Standard Materials");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("URP 標準色マテリアル生成", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);
        DrawFolderField();
        EditorGUILayout.Space(4);
        DrawShaderSelection();
        EditorGUILayout.Space(8);
        DrawGenerateButton();
    }

    private void DrawFolderField()
    {
        EditorGUILayout.LabelField("出力先フォルダ（Assets からの相対パス）");
        EditorGUILayout.BeginHorizontal();
        folderPath = EditorGUILayout.TextField(folderPath);
        if (GUILayout.Button("フォルダ選択...", GUILayout.MaxWidth(100)))
        {
            string selected = EditorUtility.OpenFolderPanel("マテリアル出力先フォルダを選択", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                    folderPath = "Assets" + selected.Substring(Application.dataPath.Length);
                else
                    EditorUtility.DisplayDialog("フォルダエラー", "選択されたフォルダがプロジェクトの Assets フォルダ外です。", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawShaderSelection()
    {
        EditorGUILayout.LabelField("使用シェーダー");
        EditorGUI.BeginChangeCheck();
        bool lit   = EditorGUILayout.ToggleLeft("URP/Lit",   useUrpLit);
        bool unlit = EditorGUILayout.ToggleLeft("URP/Unlit", useUrpUnlit);
        if (EditorGUI.EndChangeCheck())
        {
            if      (lit   && !useUrpLit)   { useUrpLit = true;  useUrpUnlit = false; }
            else if (unlit && !useUrpUnlit) { useUrpUnlit = true; useUrpLit   = false; }
            else if (!lit  && !unlit)       { useUrpLit = true;  useUrpUnlit = false; }
        }
    }

    private void DrawGenerateButton()
    {
        GUI.enabled = !string.IsNullOrEmpty(folderPath);
        if (GUILayout.Button("標準色16マテリアルを生成"))
            GenerateMaterials();
        GUI.enabled = true;
    }

    private void GenerateMaterials()
        => EditorUrpMaterialGenerator.GenerateMaterials(folderPath, useUrpUnlit);

    private static void CreateFolderRecursive(string fullPath)
        => EditorUrpMaterialGenerator.CreateFolderRecursive(fullPath);
}
