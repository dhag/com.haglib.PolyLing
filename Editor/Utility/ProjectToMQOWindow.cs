// Editor/Utility/ProjectToMQOWindow.cs
// プロジェクトフォルダ(CSV) → MQOファイル直接エクスポート
// PolyLing本体エディタ不要の独立ウィンドウ

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.MQO;
using Poly_Ling.Serialization.FolderSerializer;

public class ProjectToMQOWindow : EditorWindow
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

    private MQOExportSettings _settings = new MQOExportSettings();

    // ================================================================
    // 結果表示
    // ================================================================

    private string _resultMessage = "";
    private MessageType _resultType = MessageType.None;
    private Vector2 _scrollPos;

    // ================================================================
    // メニュー
    // ================================================================

    [MenuItem("Tools/Poly_Ling/Utility/Export/MQO")]
    public static void ShowWindow()
    {
        GetWindow<ProjectToMQOWindow>("Project → MQO");
    }

    // ================================================================
    // GUI
    // ================================================================

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Project → MQO Export", EditorStyles.boldLabel);
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

        // EditorStateからスケール取得（Unity→MQO = 1/MqoUnityRatio）
        if (editorStates != null && editorStates.Count > 0 && editorStates[0] != null)
        {
            float mqoUnityRatio = editorStates[0].mqoUnityRatio;
            _settings.Scale = mqoUnityRatio > 0f ? 1f / mqoUnityRatio : 100f;
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
            _outputPath = Path.Combine(Path.GetDirectoryName(_projectFolderPath) ?? "", name + ".mqo");
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
            _outputPath = Path.Combine(dir, name + ".mqo");
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
            string file = Path.GetFileName(_outputPath) ?? "output.mqo";
            string selected = EditorUtility.SaveFilePanel("MQO出力先", dir, file, "mqo");
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
        _settings.SwapYZ = EditorGUILayout.Toggle("Y/Z軸入替", _settings.SwapYZ);

        EditorGUILayout.Space(2);

        // オプション
        _settings.ExportMaterials = EditorGUILayout.Toggle("マテリアル出力", _settings.ExportMaterials);
        _settings.ExportBones = EditorGUILayout.Toggle("ボーンをMQOに出力", _settings.ExportBones);
        _settings.EmbedBoneWeightsInMQO = EditorGUILayout.Toggle("ボーンウェイト埋め込み", _settings.EmbedBoneWeightsInMQO);
        _settings.SkipBakedMirror = EditorGUILayout.Toggle("ベイクミラーをスキップ", _settings.SkipBakedMirror);
        _settings.SkipNamedMirror = EditorGUILayout.Toggle("名前ミラー(+)をスキップ", _settings.SkipNamedMirror);
        _settings.SkipEmptyObjects = EditorGUILayout.Toggle("空オブジェクトをスキップ", _settings.SkipEmptyObjects);
        _settings.PreserveObjectAttributes = EditorGUILayout.Toggle("オブジェクト属性を保持", _settings.PreserveObjectAttributes);
        _settings.ExportLocalTransform = EditorGUILayout.Toggle("ローカルトランスフォーム出力", _settings.ExportLocalTransform);

        EditorGUILayout.Space(2);

        // 出力形式
        _settings.DecimalPrecision = EditorGUILayout.IntSlider("小数点以下桁数", _settings.DecimalPrecision, 1, 8);
        _settings.UseShiftJIS = EditorGUILayout.Toggle("Shift-JISエンコード", _settings.UseShiftJIS);
        _settings.TextureFolder = EditorGUILayout.TextField("テクスチャフォルダ", _settings.TextureFolder);

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // エクスポート実行
    // ================================================================

    private void DrawExportButton()
    {
        EditorGUI.BeginDisabledGroup(_project == null || _project.ModelCount == 0 || string.IsNullOrEmpty(_outputPath));

        if (GUILayout.Button("MQOエクスポート", GUILayout.Height(32)))
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

        // プロジェクトからの直接出力なので選択状態は無関係
        _settings.ExportSelectedOnly = false;

        var result = MQOExporter.ExportFile(_outputPath, model, _settings);

        if (result.Success)
        {
            _resultMessage = $"エクスポート成功: {_outputPath}\n"
                           + $"オブジェクト: {result.Stats.ObjectCount}, "
                           + $"頂点: {result.Stats.TotalVertices}, "
                           + $"面: {result.Stats.TotalFaces}, "
                           + $"材質: {result.Stats.MaterialCount}";
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
