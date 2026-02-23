// Editor/Utility/ProjectToPMXWindow.cs
// プロジェクトフォルダ(CSV) → PMXファイル直接エクスポート
// PolyLing本体エディタ不要の独立ウィンドウ

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.PMX;
using Poly_Ling.Serialization.FolderSerializer;

public class ProjectToPMXWindow : EditorWindow
{
    // ================================================================
    // フォルダ・モデル選択
    // ================================================================

    private string _projectFolderPath = "";
    private ProjectContext _project;
    private int _selectedModelIndex;
    private string[] _modelNames = Array.Empty<string>();

    // ================================================================
    // 出力先
    // ================================================================

    private string _outputPath = "";

    // ================================================================
    // エクスポート設定
    // ================================================================

    private PMXExportSettings _settings = PMXExportSettings.CreateFullExport();

    // ================================================================
    // 結果表示
    // ================================================================

    private string _resultMessage = "";
    private MessageType _resultType = MessageType.None;
    private Vector2 _scrollPos;

    // ================================================================
    // メニュー
    // ================================================================

    [MenuItem("Tools/Poly_Ling/Utility/Export/PMX")]
    public static void ShowWindow()
    {
        GetWindow<ProjectToPMXWindow>("Project → PMX");
    }

    // ================================================================
    // GUI
    // ================================================================

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Project → PMX Export", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawProjectFolder();
        DrawModelSelector();
        DrawOutputPath();
        DrawSettings();
        DrawExportButton();
        DrawResult();

        EditorGUILayout.EndScrollView();
    }

    // ================================================================
    // プロジェクトフォルダ選択
    // ================================================================

    private void DrawProjectFolder()
    {
        EditorGUILayout.LabelField("プロジェクトフォルダ", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _projectFolderPath = EditorGUILayout.TextField(_projectFolderPath);
        if (GUILayout.Button("参照...", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("プロジェクトフォルダを選択", _projectFolderPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                _projectFolderPath = selected;
                LoadProject();
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("読み込み", GUILayout.Height(24)))
        {
            LoadProject();
        }

        EditorGUILayout.Space(4);
    }

    private void LoadProject()
    {
        _project = null;
        _modelNames = Array.Empty<string>();
        _selectedModelIndex = 0;
        _resultMessage = "";

        if (string.IsNullOrEmpty(_projectFolderPath) || !Directory.Exists(_projectFolderPath))
        {
            _resultMessage = "フォルダが存在しません: " + _projectFolderPath;
            _resultType = MessageType.Error;
            return;
        }

        _project = CsvProjectSerializer.Import(_projectFolderPath, out var editorStates, out _);

        if (_project == null || _project.ModelCount == 0)
        {
            _resultMessage = "モデルが見つかりません。project.csv または model.csv を含むフォルダを指定してください。";
            _resultType = MessageType.Error;
            return;
        }

        // EditorStateからスケール取得（Unity→PMX = 1/PmxUnityRatio）
        if (editorStates != null && editorStates.Count > 0 && editorStates[0] != null)
        {
            float pmxUnityRatio = editorStates[0].pmxUnityRatio;
            _settings.Scale = pmxUnityRatio > 0f ? 1f / pmxUnityRatio : 10f;
        }

        _modelNames = new string[_project.ModelCount];
        for (int i = 0; i < _project.ModelCount; i++)
        {
            var m = _project.Models[i];
            int meshCount = m?.MeshContextCount ?? 0;
            _modelNames[i] = $"{m?.Name ?? "unnamed"} (Mesh: {meshCount})";
        }

        _resultMessage = $"{_project.ModelCount} モデル読み込み完了";
        _resultType = MessageType.Info;

        // 出力パスの自動設定
        if (string.IsNullOrEmpty(_outputPath) && _project.ModelCount > 0)
        {
            var model = _project.Models[0];
            string name = model?.Name ?? "output";
            _outputPath = Path.Combine(Path.GetDirectoryName(_projectFolderPath) ?? "", name + ".pmx");
        }
    }

    // ================================================================
    // モデル選択
    // ================================================================

    private void DrawModelSelector()
    {
        if (_project == null || _project.ModelCount == 0)
            return;

        EditorGUILayout.LabelField("モデル選択", EditorStyles.boldLabel);

        int prev = _selectedModelIndex;
        _selectedModelIndex = EditorGUILayout.Popup(_selectedModelIndex, _modelNames);

        // モデル切り替え時に出力パスを更新
        if (prev != _selectedModelIndex)
        {
            var model = _project.Models[_selectedModelIndex];
            string name = model?.Name ?? "output";
            string dir = Path.GetDirectoryName(_outputPath) ?? Path.GetDirectoryName(_projectFolderPath) ?? "";
            _outputPath = Path.Combine(dir, name + ".pmx");
        }

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // 出力先
    // ================================================================

    private void DrawOutputPath()
    {
        EditorGUILayout.LabelField("出力先", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _outputPath = EditorGUILayout.TextField(_outputPath);
        if (GUILayout.Button("参照...", GUILayout.Width(60)))
        {
            string dir = Path.GetDirectoryName(_outputPath) ?? "";
            string file = Path.GetFileName(_outputPath) ?? "output.pmx";
            string selected = EditorUtility.SaveFilePanel("PMX出力先", dir, file, "pmx");
            if (!string.IsNullOrEmpty(selected))
                _outputPath = selected;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // 設定
    // ================================================================

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);

        // 座標系
        _settings.Scale = EditorGUILayout.FloatField("スケール", _settings.Scale);
        _settings.FlipZ = EditorGUILayout.Toggle("Z軸反転", _settings.FlipZ);
        _settings.FlipUV_V = EditorGUILayout.Toggle("UV V反転", _settings.FlipUV_V);

        EditorGUILayout.Space(2);

        // 出力内容
        _settings.ExportMaterials = EditorGUILayout.Toggle("マテリアル出力", _settings.ExportMaterials);
        _settings.ExportBones = EditorGUILayout.Toggle("ボーン出力", _settings.ExportBones);
        _settings.ExportMorphs = EditorGUILayout.Toggle("モーフ出力", _settings.ExportMorphs);
        _settings.UseRelativeTexturePath = EditorGUILayout.Toggle("テクスチャ相対パス", _settings.UseRelativeTexturePath);

        EditorGUILayout.Space(2);

        // 出力形式
        _settings.OutputBinaryPMX = EditorGUILayout.Toggle("バイナリPMX出力", _settings.OutputBinaryPMX);
        _settings.OutputCSV = EditorGUILayout.Toggle("CSVも出力", _settings.OutputCSV);
        _settings.DecimalPrecision = EditorGUILayout.IntSlider("小数点以下桁数", _settings.DecimalPrecision, 1, 8);

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // エクスポート実行
    // ================================================================

    private void DrawExportButton()
    {
        EditorGUI.BeginDisabledGroup(_project == null || _project.ModelCount == 0 || string.IsNullOrEmpty(_outputPath));

        if (GUILayout.Button("PMXエクスポート", GUILayout.Height(32)))
        {
            DoExport();
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DoExport()
    {
        _resultMessage = "";

        if (_project == null || _selectedModelIndex < 0 || _selectedModelIndex >= _project.ModelCount)
        {
            _resultMessage = "モデルが選択されていません。";
            _resultType = MessageType.Error;
            return;
        }

        var model = _project.Models[_selectedModelIndex];
        if (model == null)
        {
            _resultMessage = "選択されたモデルがnullです。";
            _resultType = MessageType.Error;
            return;
        }

        // フルエクスポートモード固定
        _settings.ExportMode = PMXExportMode.Full;

        var result = PMXExporter.Export(model, _outputPath, _settings);

        if (result.Success)
        {
            _resultMessage = $"エクスポート成功: {result.OutputPath}\n"
                           + $"頂点: {result.VertexCount}, "
                           + $"面: {result.FaceCount}, "
                           + $"材質: {result.MaterialCount}, "
                           + $"ボーン: {result.BoneCount}, "
                           + $"モーフ: {result.MorphCount}";
            _resultType = MessageType.Info;
        }
        else
        {
            _resultMessage = $"エクスポート失敗: {result.ErrorMessage}";
            _resultType = MessageType.Error;
        }
    }

    // ================================================================
    // 結果表示
    // ================================================================

    private void DrawResult()
    {
        if (string.IsNullOrEmpty(_resultMessage))
            return;

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(_resultMessage, _resultType);

        if (_resultType == MessageType.Info && !string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
        {
            if (GUILayout.Button("出力フォルダを開く"))
            {
                EditorUtility.RevealInFinder(_outputPath);
            }
        }
    }
}
