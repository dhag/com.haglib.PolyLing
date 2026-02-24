// Assets/Editor/Poly_Ling/PMX/PMXPartialImportPanel.cs
// PMX部分インポートパネル
// PMXファイルから選択メッシュの頂点位置/メッシュ構造/材質内容を部分的にインポート
// PMXは展開済み（1頂点=1UV）なのでMQOのような展開辞書は不要

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;
using Poly_Ling.MQO; // PartialMeshEntry, PMXMQOTransferPanel

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX側メッシュエントリ
    /// </summary>
    public class PartialPMXEntry
    {
        public bool Selected;
        public int Index;
        public string Name;
        public int VertexCount;      // 頂点数（PMXは展開済みなのでExpandedと同じ）
        public MeshContext MeshContext;
    }

    /// <summary>
    /// PMX部分インポートパネル
    /// </summary>
    public class PMXPartialImportPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "PMX Partial Import", ["ja"] = "PMX部分インポート" },

            // セクション
            ["ReferencePMX"] = new() { ["en"] = "Reference PMX", ["ja"] = "リファレンスPMX" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ImportTarget"] = new() { ["en"] = "Import Target", ["ja"] = "インポート対象" },

            // ラベル
            ["PMXFile"] = new() { ["en"] = "PMX File", ["ja"] = "PMXファイル" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z反転" },
            ["PMXMeshes"] = new() { ["en"] = "PMX Meshes", ["ja"] = "PMXメッシュ" },
            ["ModelMeshes"] = new() { ["en"] = "Model Meshes", ["ja"] = "モデルメッシュ" },

            // チェックボックス
            ["VertexPosition"] = new() { ["en"] = "Vertex Position", ["ja"] = "頂点位置" },
            ["UV"] = new() { ["en"] = "UV", ["ja"] = "UV" },
            ["BoneWeight"] = new() { ["en"] = "BoneWeight", ["ja"] = "BoneWeight" },
            ["FaceStructure"] = new() { ["en"] = "Face Structure", ["ja"] = "面構成" },
            ["MaterialContent"] = new() { ["en"] = "Material Content (by name)", ["ja"] = "材質内容（名前マッチング）" },

            // ボタン
            ["Import"] = new() { ["en"] = "Import", ["ja"] = "インポート" },
            ["SelectAll"] = new() { ["en"] = "All", ["ja"] = "全" },
            ["SelectNone"] = new() { ["en"] = "None", ["ja"] = "無" },
            ["AutoMatch"] = new() { ["en"] = "Auto", ["ja"] = "自動" },

            // ステータス
            ["Selection"] = new() { ["en"] = "Selection: Model {0} ↔ PMX {1}", ["ja"] = "選択: モデル {0} ↔ PMX {1}" },
            ["VertexMismatch"] = new() { ["en"] = "Vertex mismatch: Model({0}) ≠ PMX({1}) — min imported", ["ja"] = "頂点数不一致: モデル({0}) ≠ PMX({1}) — 少ない方を転送" },
            ["NothingSelected"] = new() { ["en"] = "Select at least one import target", ["ja"] = "インポート対象を1つ以上選択してください" },

            // メッセージ
            ["ImportSuccess"] = new() { ["en"] = "Import: {0}", ["ja"] = "インポート完了: {0}" },
            ["ImportFailed"] = new() { ["en"] = "Import failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["MaterialMatchResult"] = new() { ["en"] = "Material: {0} / {1} matched", ["ja"] = "材質: {0} / {1} マッチ" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private ToolContext _context;
        private string _pmxFilePath = "";
        private PMXImportResult _pmxImportResult;

        // リスト
        private List<PartialMeshEntry> _modelMeshes = new();
        private List<PartialPMXEntry> _pmxMeshes = new();

        // オプション
        private float _importScale = 1.0f;
        private bool _flipZ = false;

        // インポート対象チェックボックス
        private bool _importVertexPosition = true;
        private bool _importUV = false;
        private bool _importBoneWeight = false;
        private bool _importFaceStructure = false;
        private bool _importMaterialContent = false;

        // UI状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string _lastResult = "";

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        /// <summary>頂点属性系（位置/UV/BoneWeight）または面構成が有効 → メッシュマッチング必要</summary>
        private bool NeedsMeshMatching => _importVertexPosition || _importUV || _importBoneWeight || _importFaceStructure;

        /// <summary>何か1つでもインポート対象が選択されているか</summary>
        private bool HasAnyTarget => _importVertexPosition || _importUV || _importBoneWeight || _importFaceStructure || _importMaterialContent;

        private int SelectedModelVertexCount => _modelMeshes.Where(m => m.Selected).Sum(m => m.ExpandedVertexCount);
        private int SelectedPMXVertexCount => _pmxMeshes.Where(m => m.Selected).Sum(m => m.VertexCount);

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<PMXPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(700, 500);
            panel._context = ctx;
            panel.InitFromContext();
            panel.BuildModelList();
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            InitFromContext();
            BuildModelList();
            if (_pmxImportResult != null)
                AutoMatch();
        }

        private void InitFromContext()
        {
            // PMXのデフォルト: Scale=1, FlipZ=false
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            DrawPMXFileSection();
            EditorGUILayout.Space(5);

            DrawImportTargetSection();
            EditorGUILayout.Space(5);

            DrawOptionsSection();
            EditorGUILayout.Space(5);

            if (NeedsMeshMatching)
            {
                DrawDualListSection();
                EditorGUILayout.Space(5);
            }

            if (_importMaterialContent)
            {
                DrawMaterialMatchPreview();
                EditorGUILayout.Space(5);
            }

            DrawImportSection();
        }

        // ================================================================
        // PMXファイルセクション
        // ================================================================

        private void DrawPMXFileSection()
        {
            EditorGUILayout.LabelField(T("ReferencePMX"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField(T("PMXFile"), _pmxFilePath);
                if (newPath != _pmxFilePath)
                {
                    _pmxFilePath = newPath;
                    if (File.Exists(_pmxFilePath)) LoadPMXAndMatch();
                }

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select PMX", "", "pmx");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMXAndMatch();
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath)))
                {
                    if (GUILayout.Button("↻", GUILayout.Width(25)))
                    {
                        LoadPMXAndMatch();
                    }
                }
            }

            // ドラッグ＆ドロップ
            var dropArea = GUILayoutUtility.GetLastRect();
            var evt = Event.current;
            if (evt.type == EventType.DragUpdated && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                foreach (var path in DragAndDrop.paths)
                {
                    if (path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                    {
                        _pmxFilePath = path;
                        LoadPMXAndMatch();
                        break;
                    }
                }
                evt.Use();
            }
        }

        // ================================================================
        // インポート対象セクション
        // ================================================================

        private void DrawImportTargetSection()
        {
            EditorGUILayout.LabelField(T("ImportTarget"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _importVertexPosition = EditorGUILayout.ToggleLeft(T("VertexPosition"), _importVertexPosition, GUILayout.Width(100));
                _importUV = EditorGUILayout.ToggleLeft(T("UV"), _importUV, GUILayout.Width(60));
                _importBoneWeight = EditorGUILayout.ToggleLeft(T("BoneWeight"), _importBoneWeight, GUILayout.Width(110));
                _importFaceStructure = EditorGUILayout.ToggleLeft(T("FaceStructure"), _importFaceStructure, GUILayout.Width(100));
                _importMaterialContent = EditorGUILayout.ToggleLeft(T("MaterialContent"), _importMaterialContent);
            }
        }

        // ================================================================
        // オプションセクション
        // ================================================================

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(T("Options"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _importScale = EditorGUILayout.FloatField(T("Scale"), _importScale, GUILayout.Width(200));
                _flipZ = EditorGUILayout.ToggleLeft(T("FlipZ"), _flipZ, GUILayout.Width(80));
            }
        }

        // ================================================================
        // 左右リスト
        // ================================================================

        private void DrawDualListSection()
        {
            if (_context == null || Model == null) return;
            if (_pmxImportResult == null)
            {
                EditorGUILayout.HelpBox("Select a PMX file first", MessageType.Info);
                return;
            }

            float halfWidth = (position.width - 30) / 2;

            // Auto Match ボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("AutoMatch"), GUILayout.Width(60)))
                    AutoMatch();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // 左: PMXメッシュ
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("PMXMeshes"), false);
                    _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft, GUILayout.Height(300));
                    DrawPMXList();
                    EditorGUILayout.EndScrollView();
                }

                // 右: モデルメッシュ
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("ModelMeshes"), true);
                    _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.Height(300));
                    DrawModelList();
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
                if (GUILayout.Button(T("SelectAll"), GUILayout.Width(50)))
                {
                    var list = isModel ? (IEnumerable<object>)_modelMeshes : _pmxMeshes;
                    foreach (var e in list)
                    {
                        if (e is PartialMeshEntry m) m.Selected = true;
                        else if (e is PartialPMXEntry p) p.Selected = true;
                    }
                }
                if (GUILayout.Button(T("SelectNone"), GUILayout.Width(50)))
                {
                    var list = isModel ? (IEnumerable<object>)_modelMeshes : _pmxMeshes;
                    foreach (var e in list)
                    {
                        if (e is PartialMeshEntry m) m.Selected = false;
                        else if (e is PartialPMXEntry p) p.Selected = false;
                    }
                }
            }
        }

        private void DrawPMXList()
        {
            foreach (var entry in _pmxMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                    EditorGUILayout.LabelField($"{entry.Name} ({entry.VertexCount})");
                }
            }
        }

        private void DrawModelList()
        {
            foreach (var entry in _modelMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string label;
                    if (entry.BakedMirrorPeer != null)
                    {
                        label = $"{entry.Name} (+ {entry.BakedMirrorPeer.Name}) [{entry.TotalExpandedVertexCount}]";
                        GUI.color = new Color(1f, 0.85f, 0.6f);
                    }
                    else
                    {
                        label = $"{entry.Name} ({entry.ExpandedVertexCount})";
                    }

                    EditorGUILayout.LabelField(label);
                    GUI.color = Color.white;
                }
            }
        }

        // ================================================================
        // 材質マッチングプレビュー
        // ================================================================

        private void DrawMaterialMatchPreview()
        {
            if (_pmxImportResult?.Document == null || Model == null) return;

            var matches = BuildMaterialMatches();
            EditorGUILayout.LabelField(T("MaterialMatchResult", matches.Count, _pmxImportResult.Document.Materials.Count),
                EditorStyles.miniLabel);

            foreach (var match in matches)
            {
                EditorGUILayout.LabelField($"  {match.pmxMatName} → {match.modelMatRef.Name}",
                    EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // インポートセクション
        // ================================================================

        private void DrawImportSection()
        {
            if (!HasAnyTarget)
            {
                EditorGUILayout.HelpBox(T("NothingSelected"), MessageType.Info);
                return;
            }

            if (NeedsMeshMatching)
            {
                int modelCount = _modelMeshes.Count(m => m.Selected);
                int pmxCount = _pmxMeshes.Count(m => m.Selected);
                int modelVerts = SelectedModelVertexCount;
                int pmxVerts = SelectedPMXVertexCount;

                EditorGUILayout.LabelField(T("Selection", modelCount, pmxCount) + $"  Verts: {modelVerts} ← {pmxVerts}");

                // 頂点属性系で面構成なし + 頂点数不一致 → 警告
                bool vertexAttrOnly = (_importVertexPosition || _importUV || _importBoneWeight) && !_importFaceStructure;
                if (vertexAttrOnly && modelVerts != pmxVerts && modelCount > 0 && pmxCount > 0)
                {
                    EditorGUILayout.HelpBox(T("VertexMismatch", modelVerts, pmxVerts), MessageType.Warning);
                }
            }

            bool canImport = _pmxImportResult != null;
            if (NeedsMeshMatching)
            {
                canImport &= _modelMeshes.Any(m => m.Selected) && _pmxMeshes.Any(m => m.Selected);
            }

            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button(T("Import"), GUILayout.Height(30)))
                {
                    ExecuteImport();
                }
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // PMX読み込みと自動照合
        // ================================================================

        private void LoadPMXAndMatch()
        {
            _pmxImportResult = null;
            _pmxMeshes.Clear();

            if (string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath))
                return;

            try
            {
                var settings = new PMXImportSettings
                {
                    ImportMode = PMXImportMode.NewModel,
                    ImportTarget = PMXImportTarget.Mesh,
                    ImportMaterials = false,
                    FlipZ = _flipZ,
                    Scale = _importScale,
                    RecalculateNormals = false, // 部分インポートでは法線そのまま
                    DetectNamedMirror = true,
                    UseObjectNameGrouping = true
                };

                _pmxImportResult = PMXImporter.ImportFile(_pmxFilePath, settings);

                if (_pmxImportResult == null || !_pmxImportResult.Success)
                {
                    Debug.LogError($"[PMXPartialImport] Import failed: {_pmxImportResult?.ErrorMessage}");
                    return;
                }

                BuildPMXList();

                if (_modelMeshes.Count == 0) BuildModelList();
                AutoMatch();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXPartialImport] Load failed: {ex.Message}");
            }

            Repaint();
        }

        // ================================================================
        // リスト構築
        // ================================================================

        private void BuildModelList()
        {
            _modelMeshes.Clear();
            if (Model == null) return;

            var drawables = Model.DrawableMeshes;
            if (drawables == null) return;

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                // PMXは個別マッチなのでペア統合しない
                var isolated = PMXMQOTransferPanel.GetIsolatedVertices(mo);
                int expandedCount = PMXMQOTransferPanel.CalculateExpandedVertexCount(mo, isolated);

                _modelMeshes.Add(new PartialMeshEntry
                {
                    Selected = false,
                    Index = i,
                    Name = ctx.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror = ctx.IsBakedMirror,
                    Context = ctx,
                    IsolatedVertices = isolated
                });
            }
        }

        private void BuildPMXList()
        {
            _pmxMeshes.Clear();
            if (_pmxImportResult == null) return;

            int idx = 0;
            foreach (var meshContext in _pmxImportResult.MeshContexts)
            {
                // ボーンはスキップ
                if (meshContext.Type == MeshType.Bone) continue;

                var mo = meshContext.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                _pmxMeshes.Add(new PartialPMXEntry
                {
                    Selected = false,
                    Index = idx++,
                    Name = meshContext.Name,
                    VertexCount = mo.VertexCount,
                    MeshContext = meshContext
                });
            }
        }

        // ================================================================
        // 自動マッチング（名前優先、頂点数フォールバック）
        // ================================================================

        private void AutoMatch()
        {
            foreach (var m in _modelMeshes) m.Selected = false;
            foreach (var p in _pmxMeshes) p.Selected = false;

            // Pass 1: 名前完全一致
            foreach (var model in _modelMeshes)
            {
                if (string.IsNullOrEmpty(model.Name)) continue;

                var match = _pmxMeshes.FirstOrDefault(p =>
                    !p.Selected && p.Name == model.Name);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }

            // Pass 2: 頂点数一致（未マッチのみ）
            foreach (var model in _modelMeshes)
            {
                if (model.Selected) continue;
                if (model.ExpandedVertexCount == 0) continue;

                var match = _pmxMeshes.FirstOrDefault(p =>
                    !p.Selected &&
                    p.VertexCount == model.ExpandedVertexCount);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }
        }

        // ================================================================
        // インポート実行
        // ================================================================

        private void ExecuteImport()
        {
            try
            {
                var results = new List<string>();

                var selectedModels = _modelMeshes.Where(m => m.Selected).ToList();
                var selectedPMXs = _pmxMeshes.Where(m => m.Selected).ToList();

                bool topologyChanged = false;

                // Undo用: before状態をキャプチャ
                var beforeSnapshot = MultiMeshVertexSnapshot.Capture(Model);

                if (selectedModels.Count > 0 && selectedPMXs.Count > 0)
                {
                    // 面構成（トポロジ変更）
                    if (_importFaceStructure)
                    {
                        int count = ExecuteFaceStructureImport(selectedModels, selectedPMXs);
                        results.Add($"Faces: {count} meshes");
                        topologyChanged = true;
                    }

                    // 頂点属性系（1:1マッピング）
                    int vertCount = ExecuteVertexAttributeImport(selectedModels, selectedPMXs,
                        _importVertexPosition, _importUV, _importBoneWeight);
                    if (vertCount > 0)
                    {
                        var attrs = new List<string>();
                        if (_importVertexPosition) attrs.Add("Pos");
                        if (_importUV) attrs.Add("UV");
                        if (_importBoneWeight) attrs.Add("BW");
                        results.Add($"{string.Join("+", attrs)}: {vertCount} verts");
                    }
                }

                // 材質インポート
                if (_importMaterialContent)
                {
                    int count = ExecuteMaterialImport();
                    results.Add(T("MaterialMatchResult", count, _pmxImportResult.Document.Materials.Count));
                }

                // 同期
                if (topologyChanged)
                {
                    _context?.OnTopologyChanged();
                }
                else if (_importVertexPosition || _importUV || _importBoneWeight)
                {
                    _context?.SyncMesh?.Invoke();
                }

                _context?.Repaint?.Invoke();
                SceneView.RepaintAll();

                // Undo記録
                var undo = _context?.UndoController;
                if (undo != null)
                {
                    var afterSnapshot = MultiMeshVertexSnapshot.Capture(Model);
                    var record = new MultiMeshVertexSnapshotRecord(beforeSnapshot, afterSnapshot, "PMX Partial Import");
                    undo.MeshListStack.Record(record, "PMX Partial Import");
                }

                _lastResult = T("ImportSuccess", string.Join(", ", results));
            }
            catch (Exception ex)
            {
                _lastResult = T("ImportFailed", ex.Message);
                Debug.LogError($"[PMXPartialImport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        // ================================================================
        // 頂点属性インポート（位置/UV/BoneWeight 独立）
        // 両方展開済みなので1:1マッピング。数が異なる場合はmin(N,M)個を転送
        // ================================================================

        private int ExecuteVertexAttributeImport(List<PartialMeshEntry> modelMeshes, List<PartialPMXEntry> pmxMeshes,
            bool position, bool uv, bool boneWeight)
        {
            if (!position && !uv && !boneWeight) return 0;

            int totalUpdated = 0;
            int pairCount = Math.Min(modelMeshes.Count, pmxMeshes.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelMo = modelMeshes[p].Context?.MeshObject;
                var pmxMo = pmxMeshes[p].MeshContext?.MeshObject;
                if (modelMo == null || pmxMo == null) continue;

                int count = Math.Min(modelMo.VertexCount, pmxMo.VertexCount);

                if (modelMo.VertexCount != pmxMo.VertexCount)
                {
                    Debug.LogWarning($"[PMXPartialImport] '{modelMeshes[p].Name}' model={modelMo.VertexCount} ≠ pmx={pmxMo.VertexCount}, importing {count}");
                }

                for (int i = 0; i < count; i++)
                {
                    var dst = modelMo.Vertices[i];
                    var src = pmxMo.Vertices[i];

                    if (position)
                        dst.Position = src.Position;

                    if (uv)
                    {
                        dst.UVs.Clear();
                        foreach (var v in src.UVs)
                            dst.UVs.Add(v);
                    }

                    if (boneWeight)
                    {
                        dst.BoneWeight = src.BoneWeight;
                    }
                }

                totalUpdated += count;
            }

            return totalUpdated;
        }

        // ================================================================
        // 面構成インポート（頂点数は変わらず、面のみ置き換え）
        // ================================================================

        private int ExecuteFaceStructureImport(List<PartialMeshEntry> modelMeshes, List<PartialPMXEntry> pmxMeshes)
        {
            int meshesUpdated = 0;
            int pairCount = Math.Min(modelMeshes.Count, pmxMeshes.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelMo = modelMeshes[p].Context?.MeshObject;
                var pmxMo = pmxMeshes[p].MeshContext?.MeshObject;
                if (modelMo == null || pmxMo == null) continue;

                int oldFaces = modelMo.FaceCount;

                modelMo.Faces.Clear();
                foreach (var f in pmxMo.Faces)
                    modelMo.Faces.Add(f.Clone());

                Debug.Log($"[PMXPartialImport] FaceStructure '{modelMeshes[p].Name}': {oldFaces} → {modelMo.FaceCount} faces");
                meshesUpdated++;
            }

            return meshesUpdated;
        }

        // ================================================================
        // 材質インポート
        // ================================================================

        private struct PMXMaterialMatch
        {
            public string pmxMatName;
            public PMXMaterial pmxMat;
            public MaterialReference modelMatRef;
        }

        private List<PMXMaterialMatch> BuildMaterialMatches()
        {
            var matches = new List<PMXMaterialMatch>();
            if (_pmxImportResult?.Document == null || Model == null) return matches;

            var modelMats = Model.MaterialReferences;
            if (modelMats == null) return matches;

            foreach (var pmxMat in _pmxImportResult.Document.Materials)
            {
                var modelRef = modelMats.FirstOrDefault(r => r.Name == pmxMat.Name);
                if (modelRef != null)
                {
                    matches.Add(new PMXMaterialMatch
                    {
                        pmxMatName = pmxMat.Name,
                        pmxMat = pmxMat,
                        modelMatRef = modelRef
                    });
                }
            }

            return matches;
        }

        private int ExecuteMaterialImport()
        {
            if (_pmxImportResult?.Document == null || Model == null) return 0;

            var matches = BuildMaterialMatches();
            int count = 0;

            foreach (var match in matches)
            {
                var mat = match.modelMatRef.Material;
                if (mat == null) continue;

                var pmxMat = match.pmxMat;

                // 基本色
                mat.color = pmxMat.Diffuse;

                count++;
            }

            return count;
        }
    }
}
