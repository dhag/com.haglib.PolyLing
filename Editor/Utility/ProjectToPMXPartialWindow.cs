// Editor/Utility/ProjectToPMXPartialWindow.cs
// プロジェクトフォルダ(CSV) → PMX部分差し替えエクスポート
// PolyLing本体エディタ不要の独立ウィンドウ
// 既存PMXPartialExportPanelと同じ転送ロジック

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.PMX;
using Poly_Ling.Serialization.FolderSerializer;

public class ProjectToPMXPartialWindow : EditorWindow
{
    // ================================================================
    // プロジェクト読み込み
    // ================================================================

    private string _projectFolderPath = "";
    private ProjectContext _project;
    private int _selectedModelIndex;
    private string[] _modelNames = Array.Empty<string>();

    // ================================================================
    // リファレンスPMX
    // ================================================================

    private string _pmxFilePath = "";
    private PMXDocument _pmxDocument;

    // ================================================================
    // マッピング
    // ================================================================

    private List<MeshMaterialMapping> _mappings = new List<MeshMaterialMapping>();

    // ================================================================
    // オプション
    // ================================================================

    private float _scale = 10f; // Unity→PMX: 1/PmxUnityRatio
    private bool _flipZ = true;
    private bool _flipUV_V = true;
    private bool _replacePositions = true;
    private bool _replaceNormals = true;
    private bool _replaceUVs = false;
    private bool _replaceBoneWeights = false;
    private bool _outputCSV = false;

    // ================================================================
    // UI状態
    // ================================================================

    private Vector2 _scrollPos;
    private string _lastResult = "";
    private MessageType _lastResultType = MessageType.None;

    // ================================================================
    // データクラス
    // ================================================================

    private class MeshMaterialMapping
    {
        public bool Selected;
        public string MeshName;
        public int MeshExpandedVertexCount;
        public MeshContext MeshContext;

        public string PMXMaterialName;
        public int PMXVertexCount;
        public List<int> PMXVertexIndices;

        public bool IsMatched => MeshExpandedVertexCount == PMXVertexCount;
    }

    // ================================================================
    // メニュー
    // ================================================================

    [MenuItem("Tools/Poly_Ling/Utility/Export/PMX Partial")]
    public static void ShowWindow()
    {
        var w = GetWindow<ProjectToPMXPartialWindow>();
        w.titleContent = new GUIContent("Project → PMX Partial");
        w.minSize = new Vector2(700, 550);
    }

    // ================================================================
    // プロパティ
    // ================================================================

    private ModelContext CurrentModel
    {
        get
        {
            if (_project == null || _project.ModelCount == 0) return null;
            if (_selectedModelIndex < 0 || _selectedModelIndex >= _project.ModelCount) return null;
            return _project.Models[_selectedModelIndex];
        }
    }

    // ================================================================
    // GUI
    // ================================================================

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Project → PMX 部分エクスポート", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawProjectFolder();
        DrawModelSelector();
        DrawPMXFileSection();
        DrawMappingsSection();
        DrawOptionsSection();
        DrawExportSection();

        EditorGUILayout.EndScrollView();
    }

    // ================================================================
    // プロジェクトフォルダ
    // ================================================================

    private void DrawProjectFolder()
    {
        EditorGUILayout.LabelField("プロジェクトフォルダ", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
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
        }

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
        _mappings.Clear();
        _lastResult = "";

        if (string.IsNullOrEmpty(_projectFolderPath) || !Directory.Exists(_projectFolderPath))
        {
            _lastResult = "フォルダが存在しません";
            _lastResultType = MessageType.Error;
            return;
        }

        _project = CsvProjectSerializer.Import(_projectFolderPath, out var editorStates, out _);

        if (_project == null || _project.ModelCount == 0)
        {
            _lastResult = "モデルが見つかりません";
            _lastResultType = MessageType.Error;
            return;
        }

        // EditorStateからスケール取得（Unity→PMX = 1/PmxUnityRatio）
        if (editorStates != null && editorStates.Count > 0 && editorStates[0] != null)
        {
            float pmxUnityRatio = editorStates[0].pmxUnityRatio;
            _scale = pmxUnityRatio > 0f ? 1f / pmxUnityRatio : 10f;
        }

        _modelNames = new string[_project.ModelCount];
        for (int i = 0; i < _project.ModelCount; i++)
        {
            var m = _project.Models[i];
            _modelNames[i] = $"{m?.Name ?? "unnamed"} (Mesh: {m?.MeshContextCount ?? 0})";
        }

        if (_pmxDocument != null)
            BuildMappings();

        _lastResult = $"{_project.ModelCount} モデル読み込み完了";
        _lastResultType = MessageType.Info;
    }

    // ================================================================
    // モデル選択
    // ================================================================

    private void DrawModelSelector()
    {
        if (_project == null || _project.ModelCount == 0) return;

        EditorGUILayout.LabelField("モデル選択", EditorStyles.boldLabel);

        int prev = _selectedModelIndex;
        _selectedModelIndex = EditorGUILayout.Popup(_selectedModelIndex, _modelNames);

        if (prev != _selectedModelIndex && _pmxDocument != null)
            BuildMappings();

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // リファレンスPMX
    // ================================================================

    private void DrawPMXFileSection()
    {
        EditorGUILayout.LabelField("リファレンスPMX", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _pmxFilePath = EditorGUILayout.TextField(_pmxFilePath);
            if (GUILayout.Button("参照...", GUILayout.Width(60)))
            {
                string dir = string.IsNullOrEmpty(_pmxFilePath) ? "" : Path.GetDirectoryName(_pmxFilePath);
                string path = EditorUtility.OpenFilePanel("PMXファイルを選択", dir, "pmx");
                if (!string.IsNullOrEmpty(path))
                {
                    _pmxFilePath = path;
                    LoadPMX();
                    BuildMappings();
                }
            }
        }

        if (_pmxDocument != null)
        {
            EditorGUILayout.LabelField(
                $"Materials: {_pmxDocument.Materials.Count}, Vertices: {_pmxDocument.Vertices.Count}, Bones: {_pmxDocument.Bones.Count}",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // マッピングリスト
    // ================================================================

    private void DrawMappingsSection()
    {
        if (CurrentModel == null)
        {
            EditorGUILayout.HelpBox("プロジェクトを読み込んでください", MessageType.Info);
            return;
        }
        if (_pmxDocument == null)
        {
            EditorGUILayout.HelpBox("リファレンスPMXを選択してください", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("メッシュ ↔ 材質対応", EditorStyles.boldLabel);

        // 選択ボタン
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("全選択", GUILayout.Width(80)))
                foreach (var m in _mappings) m.Selected = true;
            if (GUILayout.Button("全解除", GUILayout.Width(80)))
                foreach (var m in _mappings) m.Selected = false;
            if (GUILayout.Button("一致のみ選択", GUILayout.Width(100)))
                foreach (var m in _mappings) m.Selected = m.IsMatched;
        }

        EditorGUILayout.Space(3);

        // ヘッダー
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("", GUILayout.Width(20));
            EditorGUILayout.LabelField("モデルメッシュ", EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("V", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("→", EditorStyles.miniLabel, GUILayout.Width(20));
            EditorGUILayout.LabelField("PMX材質", EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.LabelField("V", EditorStyles.miniLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("", GUILayout.Width(30));
        }

        // マッピングリスト
        foreach (var mapping in _mappings)
        {
            DrawMappingRow(mapping);
        }

        EditorGUILayout.Space(4);
    }

    private void DrawMappingRow(MeshMaterialMapping mapping)
    {
        Color originalColor = GUI.backgroundColor;

        if (!mapping.IsMatched && !string.IsNullOrEmpty(mapping.PMXMaterialName))
            GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            mapping.Selected = EditorGUILayout.Toggle(mapping.Selected, GUILayout.Width(20));
            EditorGUILayout.LabelField(mapping.MeshName, GUILayout.Width(150));
            EditorGUILayout.LabelField(mapping.MeshExpandedVertexCount.ToString(), GUILayout.Width(60));
            EditorGUILayout.LabelField("→", GUILayout.Width(20));
            EditorGUILayout.LabelField(mapping.PMXMaterialName ?? "(none)", GUILayout.Width(150));
            EditorGUILayout.LabelField(mapping.PMXVertexCount.ToString(), GUILayout.Width(60));

            if (!string.IsNullOrEmpty(mapping.PMXMaterialName))
            {
                if (mapping.IsMatched)
                {
                    GUI.contentColor = Color.green;
                    EditorGUILayout.LabelField("✓", GUILayout.Width(30));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    GUI.contentColor = Color.red;
                    EditorGUILayout.LabelField("✗", GUILayout.Width(30));
                    GUI.contentColor = Color.white;
                }
            }
            else
            {
                EditorGUILayout.LabelField("", GUILayout.Width(30));
            }
        }

        GUI.backgroundColor = originalColor;
    }

    // ================================================================
    // オプション
    // ================================================================

    private void DrawOptionsSection()
    {
        EditorGUILayout.LabelField("出力オプション", EditorStyles.boldLabel);

        EditorGUI.indentLevel++;

        _scale = EditorGUILayout.FloatField("スケール", _scale);
        _flipZ = EditorGUILayout.Toggle("Z軸反転", _flipZ);
        _flipUV_V = EditorGUILayout.Toggle("UV V反転", _flipUV_V);

        EditorGUILayout.Space(3);

        _replacePositions = EditorGUILayout.Toggle("座標を出力", _replacePositions);
        _replaceNormals = EditorGUILayout.Toggle("法線を出力", _replaceNormals);
        _replaceUVs = EditorGUILayout.Toggle("UVを出力", _replaceUVs);
        _replaceBoneWeights = EditorGUILayout.Toggle("ウェイトを出力", _replaceBoneWeights);

        EditorGUILayout.Space(3);

        _outputCSV = EditorGUILayout.Toggle("CSVも出力", _outputCSV);

        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // エクスポートセクション
    // ================================================================

    private void DrawExportSection()
    {
        int selectedCount = _mappings.Count(m => m.Selected);
        int matchedSelectedCount = _mappings.Count(m => m.Selected && m.IsMatched);

        if (selectedCount == 0)
        {
            EditorGUILayout.HelpBox("エクスポートするメッシュを選択してください", MessageType.Info);
        }
        else if (matchedSelectedCount < selectedCount)
        {
            EditorGUILayout.HelpBox($"選択: {selectedCount}, 一致: {matchedSelectedCount}", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(matchedSelectedCount == 0 || _pmxDocument == null))
        {
            if (GUILayout.Button("PMXエクスポート", GUILayout.Height(30)))
            {
                ExecuteExport();
            }
        }

        if (!string.IsNullOrEmpty(_lastResult))
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_lastResult, _lastResultType);
        }
    }

    // ================================================================
    // PMX読み込み
    // ================================================================

    private void LoadPMX()
    {
        _pmxDocument = null;

        if (string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath))
            return;

        try
        {
            _pmxDocument = PMXReader.Load(_pmxFilePath);
            Debug.Log($"[ProjectToPMXPartial] Loaded PMX: {_pmxDocument.Materials.Count} materials, {_pmxDocument.Vertices.Count} vertices");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectToPMXPartial] PMX読み込み失敗: {ex.Message}");
            _pmxDocument = null;
        }
    }

    // ================================================================
    // マッピング構築
    // ================================================================

    private void BuildMappings()
    {
        _mappings.Clear();

        var model = CurrentModel;
        if (model == null || _pmxDocument == null) return;

        var drawables = model.DrawableMeshes;
        if (drawables == null) return;

        var pmxObjectGroups = PMXHelper.GetObjectNameGroups(_pmxDocument);

        for (int i = 0; i < drawables.Count; i++)
        {
            var entry = drawables[i];
            var ctx = entry.Context;
            if (ctx?.MeshObject == null) continue;

            var vertexInfo = PMXHelper.GetVertexInfo(ctx);
            var mapping = new MeshMaterialMapping
            {
                MeshName = ctx.Name,
                MeshExpandedVertexCount = vertexInfo.ExpandedVertexCount,
                MeshContext = ctx
            };

            // ObjectNameとの対応を探す
            string baseName = ctx.Name;
            if (baseName.EndsWith("_L") || baseName.EndsWith("_R"))
                baseName = baseName.Substring(0, baseName.Length - 2);

            ObjectGroup matchedGroup = null;
            string matchedKey = null;

            if (pmxObjectGroups.TryGetValue(ctx.Name, out var group))
            {
                matchedGroup = group;
                matchedKey = ctx.Name;
            }
            else if (pmxObjectGroups.TryGetValue(baseName, out group))
            {
                matchedGroup = group;
                matchedKey = baseName;
            }
            else if (pmxObjectGroups.TryGetValue(ctx.Name + "+", out group))
            {
                matchedGroup = group;
                matchedKey = ctx.Name + "+";
            }
            else if (pmxObjectGroups.TryGetValue(baseName + "+", out group))
            {
                matchedGroup = group;
                matchedKey = baseName + "+";
            }

            if (matchedGroup != null)
            {
                mapping.PMXMaterialName = matchedKey;
                mapping.PMXVertexIndices = matchedGroup.VertexIndices;
                mapping.PMXVertexCount = matchedGroup.VertexCount;
            }

            _mappings.Add(mapping);
        }

        Debug.Log($"[ProjectToPMXPartial] Built {_mappings.Count} mappings");
    }

    // ================================================================
    // エクスポート実行
    // ================================================================

    private void ExecuteExport()
    {
        try
        {
            string defaultName = Path.GetFileNameWithoutExtension(_pmxFilePath) + "_modified.pmx";
            string savePath = EditorUtility.SaveFilePanel("PMX保存先", Path.GetDirectoryName(_pmxFilePath), defaultName, "pmx");

            if (string.IsNullOrEmpty(savePath))
                return;

            int totalTransferred = 0;

            foreach (var mapping in _mappings)
            {
                if (!mapping.Selected || !mapping.IsMatched)
                    continue;

                int transferred = TransferMeshToPMX(mapping);
                totalTransferred += transferred;
            }

            PMXWriter.Save(_pmxDocument, savePath);

            if (_outputCSV)
            {
                string csvPath = Path.ChangeExtension(savePath, ".csv");
                PMXCSVWriter.Save(_pmxDocument, csvPath);
            }

            _lastResult = $"エクスポート成功: {totalTransferred} vertices → {Path.GetFileName(savePath)}";
            _lastResultType = MessageType.Info;
            Debug.Log($"[ProjectToPMXPartial] Export completed: {totalTransferred} vertices");

            // PMXを再読み込み
            LoadPMX();
        }
        catch (Exception ex)
        {
            _lastResult = $"エクスポート失敗: {ex.Message}";
            _lastResultType = MessageType.Error;
            Debug.LogError($"[ProjectToPMXPartial] {ex.Message}\n{ex.StackTrace}");
        }

        Repaint();
    }

    // ================================================================
    // 転送ロジック（PMXPartialExportPanelと同じ）
    // ================================================================

    private int TransferMeshToPMX(MeshMaterialMapping mapping)
    {
        var mo = mapping.MeshContext?.MeshObject;
        if (mo == null) return 0;
        if (mapping.PMXVertexIndices == null || mapping.PMXVertexIndices.Count == 0) return 0;

        int transferred = 0;
        int localIndex = 0;

        foreach (var vertex in mo.Vertices)
        {
            if (localIndex >= mapping.PMXVertexIndices.Count)
                break;

            int pmxVertexIndex = mapping.PMXVertexIndices[localIndex];
            if (pmxVertexIndex >= _pmxDocument.Vertices.Count)
            {
                localIndex++;
                continue;
            }

            var pmxVertex = _pmxDocument.Vertices[pmxVertexIndex];

            if (_replacePositions)
            {
                Vector3 pos = vertex.Position;
                if (_flipZ) pos.z = -pos.z;
                pos *= _scale;
                pmxVertex.Position = pos;
            }

            if (_replaceNormals)
            {
                Vector3 normal = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
                if (_flipZ) normal.z = -normal.z;
                pmxVertex.Normal = normal;
            }

            if (_replaceUVs)
            {
                Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
                if (_flipUV_V) uv.y = 1f - uv.y;
                pmxVertex.UV = uv;
            }

            if (_replaceBoneWeights && vertex.BoneWeight.HasValue)
            {
                var bw = vertex.BoneWeight.Value;
                var boneWeights = new List<PMXBoneWeight>();

                if (bw.weight0 > 0)
                    boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex0, Weight = bw.weight0 });
                if (bw.weight1 > 0)
                    boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex1, Weight = bw.weight1 });
                if (bw.weight2 > 0)
                    boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex2, Weight = bw.weight2 });
                if (bw.weight3 > 0)
                    boneWeights.Add(new PMXBoneWeight { BoneIndex = bw.boneIndex3, Weight = bw.weight3 });

                pmxVertex.BoneWeights = boneWeights.ToArray();
                pmxVertex.WeightType = boneWeights.Count switch
                {
                    1 => 0,  // BDEF1
                    2 => 1,  // BDEF2
                    _ => 2   // BDEF4
                };
            }

            transferred++;
            localIndex++;
        }

        Debug.Log($"[ProjectToPMXPartial] Transferred '{mapping.MeshName}' → '{mapping.PMXMaterialName}': {transferred} vertices");
        return transferred;
    }
}
