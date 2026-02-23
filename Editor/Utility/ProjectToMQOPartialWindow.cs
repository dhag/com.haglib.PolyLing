// Editor/Utility/ProjectToMQOPartialWindow.cs
// プロジェクトフォルダ(CSV) → MQO部分書き戻しエクスポート
// PolyLing本体エディタ不要の独立ウィンドウ
// 既存MQOPartialExportPanelと同じ転送ロジック

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.MQO;
using Poly_Ling.MQO.Utility;
using Poly_Ling.PMX;
using Poly_Ling.Serialization.FolderSerializer;

public class ProjectToMQOPartialWindow : EditorWindow
{
    // ================================================================
    // プロジェクト読み込み
    // ================================================================

    private string _projectFolderPath = "";
    private ProjectContext _project;
    private int _selectedModelIndex;
    private string[] _modelNames = Array.Empty<string>();

    // ================================================================
    // リファレンスMQO
    // ================================================================

    private string _mqoFilePath = "";
    private MQODocument _mqoDocument;
    private MQOImportResult _mqoImportResult;

    // ================================================================
    // リスト
    // ================================================================

    private List<MeshEntry> _modelMeshes = new List<MeshEntry>();
    private List<MQOEntry> _mqoObjects = new List<MQOEntry>();

    // ================================================================
    // オプション
    // ================================================================

    private float _exportScale = 0.01f; // MqoUnityRatio: Unity→MQO = ÷0.01 = ×100
    private bool _flipZ = true;
    private bool _skipBakedMirror = true;
    private bool _skipNamedMirror = true;

    private bool _writeBackPosition = true;
    private bool _writeBackUV = false;
    private bool _writeBackBoneWeight = false;

    // ================================================================
    // UI状態
    // ================================================================

    private Vector2 _scrollLeft;
    private Vector2 _scrollRight;
    private string _lastResult = "";
    private MessageType _lastResultType = MessageType.None;

    // ================================================================
    // データクラス
    // ================================================================

    private class MeshEntry
    {
        public bool Selected;
        public string Name;
        public int ExpandedVertexCount;
        public bool IsBakedMirror;
        public MeshContext Context;
    }

    private class MQOEntry
    {
        public bool Selected;
        public string Name;
        public int ExpandedVertexCount;
        public MeshContext MeshContext;
    }

    // ================================================================
    // メニュー
    // ================================================================

    [MenuItem("Tools/Poly_Ling/Utility/Export/MQO Partial")]
    public static void ShowWindow()
    {
        var w = GetWindow<ProjectToMQOPartialWindow>();
        w.titleContent = new GUIContent("Project → MQO Partial");
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
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Project → MQO 部分エクスポート", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawProjectFolder();
        DrawModelSelector();
        DrawMQOFileSection();
        DrawOptionsSection();
        DrawDualListSection();
        DrawExportSection();
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
        _modelMeshes.Clear();
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

        // EditorStateからスケールを取得
        if (editorStates != null && editorStates.Count > 0 && editorStates[0] != null)
        {
            _exportScale = editorStates[0].mqoUnityRatio > 0f ? editorStates[0].mqoUnityRatio : 0.01f;
        }

        _modelNames = new string[_project.ModelCount];
        for (int i = 0; i < _project.ModelCount; i++)
        {
            var m = _project.Models[i];
            _modelNames[i] = $"{m?.Name ?? "unnamed"} (Mesh: {m?.MeshContextCount ?? 0})";
        }

        BuildModelList();

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

        if (prev != _selectedModelIndex)
        {
            BuildModelList();
            if (_mqoDocument != null) AutoMatch();
        }

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // リファレンスMQO
    // ================================================================

    private void DrawMQOFileSection()
    {
        EditorGUILayout.LabelField("リファレンスMQO", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _mqoFilePath = EditorGUILayout.TextField(_mqoFilePath);
            if (GUILayout.Button("参照...", GUILayout.Width(60)))
            {
                string dir = string.IsNullOrEmpty(_mqoFilePath) ? "" : Path.GetDirectoryName(_mqoFilePath);
                string path = EditorUtility.OpenFilePanel("MQOファイルを選択", dir, "mqo");
                if (!string.IsNullOrEmpty(path))
                {
                    _mqoFilePath = path;
                    LoadMQOAndMatch();
                }
            }
        }

        if (_mqoDocument != null)
        {
            EditorGUILayout.LabelField($"Objects: {_mqoObjects.Count} / {_mqoDocument.Objects.Count}", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // オプション
    // ================================================================

    private void DrawOptionsSection()
    {
        EditorGUILayout.LabelField("オプション", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            _exportScale = EditorGUILayout.FloatField("スケール", _exportScale, GUILayout.Width(200));
            _flipZ = EditorGUILayout.ToggleLeft("Z反転", _flipZ, GUILayout.Width(80));
        }

        bool prevSkip = _skipBakedMirror;
        _skipBakedMirror = EditorGUILayout.ToggleLeft("ベイクミラーをスキップ", _skipBakedMirror);
        if (prevSkip != _skipBakedMirror)
        {
            BuildModelList();
            if (_mqoDocument != null) AutoMatch();
        }

        bool prevSkipNamed = _skipNamedMirror;
        _skipNamedMirror = EditorGUILayout.ToggleLeft("名前ミラー(+)をスキップ", _skipNamedMirror);
        if (prevSkipNamed != _skipNamedMirror)
        {
            BuildModelList();
            if (_mqoDocument != null) AutoMatch();
        }

        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("書き戻し項目", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _writeBackPosition = EditorGUILayout.ToggleLeft("位置", _writeBackPosition, GUILayout.Width(80));
            _writeBackUV = EditorGUILayout.ToggleLeft("UV", _writeBackUV, GUILayout.Width(60));
            _writeBackBoneWeight = EditorGUILayout.ToggleLeft("ボーンウェイト", _writeBackBoneWeight, GUILayout.Width(140));
        }

        EditorGUILayout.Space(4);
    }

    // ================================================================
    // 左右リスト
    // ================================================================

    private void DrawDualListSection()
    {
        if (CurrentModel == null)
        {
            EditorGUILayout.HelpBox("プロジェクトを読み込んでください", MessageType.Info);
            return;
        }
        if (_mqoDocument == null)
        {
            EditorGUILayout.HelpBox("リファレンスMQOを選択してください", MessageType.Info);
            return;
        }

        float halfWidth = (position.width - 30) / 2;

        using (new EditorGUILayout.HorizontalScope())
        {
            // 左：モデル側
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
            {
                DrawListHeader("モデルメッシュ", true);
                _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft, GUILayout.Height(250));
                foreach (var entry in _modelMeshes)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                        if (entry.IsBakedMirror || (!string.IsNullOrEmpty(entry.Name) && entry.Name.EndsWith("+")))
                            GUI.color = new Color(0.7f, 0.7f, 1f);
                        EditorGUILayout.LabelField($"{entry.Name} ({entry.ExpandedVertexCount})");
                        GUI.color = Color.white;
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            // 右：MQO側
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
            {
                DrawListHeader("MQOオブジェクト", false);
                _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.Height(250));
                foreach (var entry in _mqoObjects)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                        EditorGUILayout.LabelField($"{entry.Name} ({entry.ExpandedVertexCount})");
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawListHeader(string title, bool isModel)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全選択", GUILayout.Width(50)))
            {
                if (isModel) foreach (var m in _modelMeshes) m.Selected = true;
                else foreach (var m in _mqoObjects) m.Selected = true;
            }
            if (GUILayout.Button("全解除", GUILayout.Width(50)))
            {
                if (isModel) foreach (var m in _modelMeshes) m.Selected = false;
                else foreach (var m in _mqoObjects) m.Selected = false;
            }
        }
    }

    // ================================================================
    // エクスポートセクション
    // ================================================================

    private void DrawExportSection()
    {
        EditorGUILayout.Space(4);

        int modelCount = _modelMeshes.Count(m => m.Selected);
        int mqoCount = _mqoObjects.Count(m => m.Selected);
        int modelVerts = _modelMeshes.Where(m => m.Selected).Sum(m => m.ExpandedVertexCount);
        int mqoVerts = _mqoObjects.Where(m => m.Selected).Sum(m => m.ExpandedVertexCount);

        EditorGUILayout.LabelField($"選択: モデル {modelCount} ↔ MQO {mqoCount}  Verts: {modelVerts} → {mqoVerts}");

        bool vertexMatch = modelVerts == mqoVerts;
        bool canExport = modelCount > 0 && mqoCount > 0 && _mqoDocument != null;

        if (!vertexMatch && canExport)
        {
            EditorGUILayout.HelpBox($"頂点数不一致: Model({modelVerts}) → MQO({mqoVerts})", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!canExport))
        {
            if (GUILayout.Button("MQOエクスポート", GUILayout.Height(30)))
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
    // データ構築
    // ================================================================

    private void BuildModelList()
    {
        _modelMeshes.Clear();

        var model = CurrentModel;
        if (model == null) return;

        var drawables = model.DrawableMeshes;
        if (drawables == null) return;

        for (int i = 0; i < drawables.Count; i++)
        {
            var entry = drawables[i];
            var ctx = entry.Context;
            if (ctx?.MeshObject == null) continue;

            var mo = ctx.MeshObject;
            if (mo.VertexCount == 0) continue;

            if (_skipBakedMirror && ctx.IsBakedMirror) continue;
            if (_skipNamedMirror && !ctx.IsBakedMirror &&
                !string.IsNullOrEmpty(ctx.Name) && ctx.Name.EndsWith("+")) continue;

            var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
            int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

            _modelMeshes.Add(new MeshEntry
            {
                Selected = false,
                Name = ctx.Name,
                ExpandedVertexCount = expandedCount,
                IsBakedMirror = ctx.IsBakedMirror,
                Context = ctx
            });
        }
    }

    private void BuildMQOList()
    {
        _mqoObjects.Clear();

        if (_mqoImportResult == null || !_mqoImportResult.Success) return;

        foreach (var meshContext in _mqoImportResult.MeshContexts)
        {
            var mo = meshContext.MeshObject;
            if (mo == null || mo.VertexCount == 0) continue;

            var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
            int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

            _mqoObjects.Add(new MQOEntry
            {
                Selected = false,
                Name = meshContext.Name,
                ExpandedVertexCount = expandedCount,
                MeshContext = meshContext
            });
        }
    }

    // ================================================================
    // MQO読み込みと自動照合
    // ================================================================

    private void LoadMQOAndMatch()
    {
        LoadMQO();
        BuildMQOList();

        if (_modelMeshes.Count == 0)
            BuildModelList();

        if (_mqoDocument != null)
            AutoMatch();

        Repaint();
    }

    private void LoadMQO()
    {
        _mqoDocument = null;
        _mqoImportResult = null;

        if (string.IsNullOrEmpty(_mqoFilePath) || !File.Exists(_mqoFilePath))
            return;

        try
        {
            _mqoDocument = MQOParser.ParseFile(_mqoFilePath);

            var settings = new MQOImportSettings
            {
                ImportMaterials = false,
                SkipHiddenObjects = true,
                MergeObjects = false,
                FlipZ = _flipZ,
                FlipUV_V = false,
                BakeMirror = false
            };
            _mqoImportResult = MQOImporter.ImportFile(_mqoFilePath, settings);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectToMQOPartial] MQO読み込み失敗: {ex.Message}");
            _mqoDocument = null;
            _mqoImportResult = null;
        }
    }

    private void AutoMatch()
    {
        foreach (var model in _modelMeshes)
        {
            model.Selected = false;
            if (model.ExpandedVertexCount == 0) continue;

            var match = _mqoObjects.FirstOrDefault(m =>
                m.ExpandedVertexCount == model.ExpandedVertexCount &&
                m.ExpandedVertexCount > 0);
            if (match != null)
            {
                model.Selected = true;
                match.Selected = true;
            }
        }
    }

    // ================================================================
    // エクスポート実行
    // ================================================================

    private void ExecuteExport()
    {
        try
        {
            string defaultName = Path.GetFileNameWithoutExtension(_mqoFilePath) + "_partial.mqo";
            string savePath = EditorUtility.SaveFilePanel("MQO保存先", Path.GetDirectoryName(_mqoFilePath), defaultName, "mqo");

            if (string.IsNullOrEmpty(savePath))
                return;

            var selectedModels = _modelMeshes.Where(m => m.Selected).ToList();
            var selectedMQOs = _mqoObjects.Where(m => m.Selected).ToList();

            int transferred = 0;
            int modelVertexOffset = 0;

            foreach (var mqoEntry in selectedMQOs)
            {
                int count = TransferToMQO(mqoEntry, selectedModels, ref modelVertexOffset);
                transferred += count;
            }

            MQOWriter.WriteToFile(_mqoDocument, savePath);

            _lastResult = $"エクスポート完了: {transferred} vertices → {Path.GetFileName(savePath)}";
            _lastResultType = MessageType.Info;

            // 再読み込み
            LoadMQO();
            BuildMQOList();
        }
        catch (Exception ex)
        {
            _lastResult = $"エクスポート失敗: {ex.Message}";
            _lastResultType = MessageType.Error;
            Debug.LogError($"[ProjectToMQOPartial] {ex.Message}\n{ex.StackTrace}");
        }

        Repaint();
    }

    // ================================================================
    // 転送ロジック（MQOPartialExportPanelと同じ）
    // ================================================================

    private int TransferToMQO(MQOEntry mqoEntry, List<MeshEntry> modelMeshes, ref int modelVertexOffset)
    {
        var mqoMeshContext = mqoEntry.MeshContext;
        var mqoMo = mqoMeshContext?.MeshObject;
        if (mqoMo == null) return 0;

        var mqoDocObj = _mqoDocument.Objects.FirstOrDefault(o => o.Name == mqoEntry.Name);
        if (mqoDocObj == null) return 0;

        // 面で使用されている頂点インデックスを収集
        var usedVertexIndices = new HashSet<int>();
        foreach (var face in mqoMo.Faces)
        {
            foreach (var vi in face.VertexIndices)
                usedVertexIndices.Add(vi);
        }

        int transferred = 0;
        int startOffset = modelVertexOffset;

        for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
        {
            var mqoVertex = mqoMo.Vertices[vIdx];
            int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

            // 孤立頂点スキップ
            if (!usedVertexIndices.Contains(vIdx))
                continue;

            if (_writeBackPosition)
            {
                Vector3? newPos = GetModelVertexPosition(modelMeshes, modelVertexOffset);
                if (newPos.HasValue)
                {
                    Vector3 pos = newPos.Value;
                    if (_flipZ) pos.z = -pos.z;
                    pos /= _exportScale;

                    mqoVertex.Position = pos;
                    if (vIdx < mqoDocObj.Vertices.Count)
                        mqoDocObj.Vertices[vIdx].Position = pos;

                    transferred++;
                }
            }

            modelVertexOffset += uvCount;
        }

        if (_writeBackUV)
            WriteBackUVsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);

        if (_writeBackBoneWeight)
            WriteBackBoneWeightsToMQO(mqoEntry, modelMeshes, startOffset, mqoDocObj, usedVertexIndices);

        return transferred;
    }

    private void WriteBackUVsToMQO(MQOEntry mqoEntry, List<MeshEntry> modelMeshes, int startOffset, MQOObject mqoDocObj, HashSet<int> usedVertexIndices)
    {
        var mqoMo = mqoEntry.MeshContext?.MeshObject;
        if (mqoMo == null) return;

        var vertexToExpandedStart = new Dictionary<int, int>();
        int expandedIdx = 0;
        for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
        {
            if (!usedVertexIndices.Contains(vIdx)) continue;
            vertexToExpandedStart[vIdx] = expandedIdx;
            int uvCount = mqoMo.Vertices[vIdx].UVs.Count > 0 ? mqoMo.Vertices[vIdx].UVs.Count : 1;
            expandedIdx += uvCount;
        }

        int mqoFaceIdx = 0;
        foreach (var mqoDocFace in mqoDocObj.Faces)
        {
            if (mqoDocFace.IsSpecialFace) continue;
            if (mqoDocFace.VertexIndices == null) continue;

            Face meshFace = null;
            while (mqoFaceIdx < mqoMo.FaceCount)
            {
                meshFace = mqoMo.Faces[mqoFaceIdx];
                mqoFaceIdx++;
                if (meshFace.VertexIndices.Count >= 3) break;
                meshFace = null;
            }
            if (meshFace == null) continue;

            if (mqoDocFace.UVs == null || mqoDocFace.UVs.Length != mqoDocFace.VertexIndices.Length)
                mqoDocFace.UVs = new Vector2[mqoDocFace.VertexIndices.Length];

            for (int i = 0; i < mqoDocFace.VertexIndices.Length && i < meshFace.VertexIndices.Count; i++)
            {
                int vIdx = mqoDocFace.VertexIndices[i];
                if (!vertexToExpandedStart.TryGetValue(vIdx, out int localExpStart)) continue;

                int uvSlot = (i < meshFace.UVIndices.Count) ? meshFace.UVIndices[i] : 0;
                int globalOffset = startOffset + localExpStart + uvSlot;
                Vector2? uv = GetModelVertexUV(modelMeshes, globalOffset);
                if (uv.HasValue)
                    mqoDocFace.UVs[i] = uv.Value;
            }
        }
    }

    private void WriteBackBoneWeightsToMQO(MQOEntry mqoEntry, List<MeshEntry> modelMeshes, int startOffset, MQOObject mqoDocObj, HashSet<int> usedVertexIndices)
    {
        var mqoMo = mqoEntry.MeshContext?.MeshObject;
        if (mqoMo == null) return;

        mqoDocObj.Faces.RemoveAll(f => f.IsSpecialFace);

        int localOffset = 0;
        for (int vIdx = 0; vIdx < mqoMo.VertexCount && vIdx < mqoDocObj.Vertices.Count; vIdx++)
        {
            var mqoVertex = mqoMo.Vertices[vIdx];
            int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

            if (!usedVertexIndices.Contains(vIdx))
                continue;

            int globalOffset = startOffset + localOffset;
            var vertexInfo = GetModelVertexInfo(modelMeshes, globalOffset);

            if (vertexInfo != null)
            {
                if (vertexInfo.Id != -1)
                {
                    mqoDocObj.Faces.Add(
                        VertexIdHelper.CreateSpecialFaceForVertexId(vIdx, vertexInfo.Id, 0));
                }

                if (vertexInfo.HasBoneWeight)
                {
                    var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.BoneWeight.Value);
                    mqoDocObj.Faces.Add(
                        VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, boneWeightData, false, 0));
                }

                if (vertexInfo.HasMirrorBoneWeight)
                {
                    var mirrorBoneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertexInfo.MirrorBoneWeight.Value);
                    mqoDocObj.Faces.Add(
                        VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, mirrorBoneWeightData, true, 0));
                }
            }

            localOffset += uvCount;
        }
    }

    // ================================================================
    // 頂点アクセスヘルパー（展開後インデックスベース）
    // ================================================================

    private Vector3? GetModelVertexPosition(List<MeshEntry> modelMeshes, int offset)
    {
        int currentOffset = 0;
        foreach (var model in modelMeshes)
        {
            var mo = model.Context?.MeshObject;
            if (mo == null) continue;

            int meshVertCount = model.ExpandedVertexCount;
            if (offset < currentOffset + meshVertCount)
            {
                int localIdx = offset - currentOffset;
                int expandedIdx = 0;
                for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                {
                    var v = mo.Vertices[vIdx];
                    int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;
                    if (localIdx < expandedIdx + uvCount)
                        return v.Position;
                    expandedIdx += uvCount;
                }
                return null;
            }
            currentOffset += meshVertCount;
        }
        return null;
    }

    private Vector2? GetModelVertexUV(List<MeshEntry> modelMeshes, int offset)
    {
        int currentOffset = 0;
        foreach (var model in modelMeshes)
        {
            var mo = model.Context?.MeshObject;
            if (mo == null) continue;

            int meshVertCount = model.ExpandedVertexCount;
            if (offset < currentOffset + meshVertCount)
            {
                int localIdx = offset - currentOffset;
                int expandedIdx = 0;
                for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                {
                    var v = mo.Vertices[vIdx];
                    int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;
                    if (localIdx < expandedIdx + uvCount)
                    {
                        int uvSlot = localIdx - expandedIdx;
                        return uvSlot < v.UVs.Count ? v.UVs[uvSlot] : (v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero);
                    }
                    expandedIdx += uvCount;
                }
                return null;
            }
            currentOffset += meshVertCount;
        }
        return null;
    }

    private Vertex GetModelVertexInfo(List<MeshEntry> modelMeshes, int offset)
    {
        int currentOffset = 0;
        foreach (var model in modelMeshes)
        {
            var mo = model.Context?.MeshObject;
            if (mo == null) continue;

            int meshVertCount = model.ExpandedVertexCount;
            if (offset < currentOffset + meshVertCount)
            {
                int localIdx = offset - currentOffset;
                int expandedIdx = 0;
                for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                {
                    var v = mo.Vertices[vIdx];
                    int uvCount = v.UVs.Count > 0 ? v.UVs.Count : 1;
                    if (localIdx < expandedIdx + uvCount)
                        return v;
                    expandedIdx += uvCount;
                }
                return null;
            }
            currentOffset += meshVertCount;
        }
        return null;
    }
}
