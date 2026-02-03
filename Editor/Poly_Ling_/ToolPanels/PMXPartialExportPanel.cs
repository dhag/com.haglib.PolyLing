// Assets/Editor/Poly_Ling_/ToolPanels/PMXCSV/PMXPartialExportPanel.cs
// PMX部分エクスポートパネル
// リファレンスPMXを指定し、選択したDrawableメッシュの頂点データを転送して出力
// PMX←→MQO転送パネルと同様のイメージ

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

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX部分エクスポートパネル
    /// </summary>
    public class PMXPartialExportPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "PMXPartialExport";
        public override string Title => "PMX Partial Export";
        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "PMX Partial Export", ["ja"] = "PMX部分エクスポート" },

            // セクション
            ["ReferencePMX"] = new() { ["en"] = "Reference PMX", ["ja"] = "リファレンスPMX" },
            ["MeshMapping"] = new() { ["en"] = "Mesh ↔ Material Mapping", ["ja"] = "メッシュ ↔ 材質対応" },
            ["ExportOptions"] = new() { ["en"] = "Export Options", ["ja"] = "出力オプション" },

            // ラベル
            ["PMXFile"] = new() { ["en"] = "PMX File", ["ja"] = "PMXファイル" },
            ["ModelMeshes"] = new() { ["en"] = "Model Meshes", ["ja"] = "モデルメッシュ" },
            ["PMXMaterials"] = new() { ["en"] = "PMX Materials", ["ja"] = "PMX材質" },
            ["Vertices"] = new() { ["en"] = "V", ["ja"] = "V" },
            ["Faces"] = new() { ["en"] = "F", ["ja"] = "F" },
            ["Match"] = new() { ["en"] = "✓", ["ja"] = "✓" },
            ["Mismatch"] = new() { ["en"] = "✗", ["ja"] = "✗" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z軸反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["ReplacePositions"] = new() { ["en"] = "Replace Positions", ["ja"] = "座標を出力" },
            ["ReplaceNormals"] = new() { ["en"] = "Replace Normals", ["ja"] = "法線を出力" },
            ["ReplaceUVs"] = new() { ["en"] = "Replace UVs", ["ja"] = "UVを出力" },
            ["ReplaceBoneWeights"] = new() { ["en"] = "Replace Weights", ["ja"] = "ウェイトを出力" },
            ["OutputCSV"] = new() { ["en"] = "Also output CSV", ["ja"] = "CSVも出力" },

            // ボタン
            ["SelectAll"] = new() { ["en"] = "Select All", ["ja"] = "全選択" },
            ["SelectNone"] = new() { ["en"] = "Select None", ["ja"] = "全解除" },
            ["SelectMatched"] = new() { ["en"] = "Select Matched", ["ja"] = "一致のみ選択" },
            ["Export"] = new() { ["en"] = "Export PMX", ["ja"] = "PMXエクスポート" },

            // メッセージ
            ["NoContext"] = new() { ["en"] = "No context set. Open from Poly_Ling window.", ["ja"] = "コンテキスト未設定" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルがありません" },
            ["NoDrawableMesh"] = new() { ["en"] = "No drawable meshes", ["ja"] = "Drawableメッシュがありません" },
            ["SelectPMXFirst"] = new() { ["en"] = "Select reference PMX file", ["ja"] = "リファレンスPMXを選択してください" },
            ["NoMeshSelected"] = new() { ["en"] = "Select meshes to export", ["ja"] = "エクスポートするメッシュを選択" },
            ["VertexCountMismatch"] = new() { ["en"] = "Vertex count mismatch: Model={0}, PMX={1}", ["ja"] = "頂点数不一致: モデル={0}, PMX={1}" },
            ["ExportSuccess"] = new() { ["en"] = "Export successful: {0}", ["ja"] = "エクスポート成功: {0}" },
            ["ExportFailed"] = new() { ["en"] = "Export failed: {0}", ["ja"] = "エクスポート失敗: {0}" },

            // ツールチップ
            ["ScaleTooltip"] = new() { ["en"] = "Model coordinates × Scale = PMX coordinates", ["ja"] = "モデル座標 × スケール = PMX座標" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private string _pmxFilePath = "";
        private PMXDocument _pmxDocument;

        // マッピングデータ
        private List<MeshMaterialMapping> _mappings = new List<MeshMaterialMapping>();

        // 出力オプション
        private float _scale = 0.1f;
        private bool _flipZ = true;
        private bool _flipUV_V = true;
        private bool _replacePositions = true;
        private bool _replaceNormals = true;
        private bool _replaceUVs = true;
        private bool _replaceBoneWeights = true;
        private bool _outputCSV = false;

        // UI状態
        private Vector2 _scrollPosition;
        private string _lastResult = "";

        // ================================================================
        // データクラス
        // ================================================================

        /// <summary>
        /// モデルメッシュとPMX材質の対応
        /// </summary>
        private class MeshMaterialMapping
        {
            public bool Selected = false;

            // モデル側（Drawableメッシュ）
            public int DrawableIndex;           // DrawableMeshes内のインデックス
            public int MasterIndex;             // MeshContextList内のインデックス
            public string MeshName;
            public int MeshVertexCount;         // 生頂点数
            public int MeshExpandedVertexCount; // UV展開後頂点数
            public MeshContext MeshContext;

            // PMX側
            public string PMXMaterialName;
            public int PMXVertexStartIndex;
            public int PMXVertexCount;
            public List<int> PMXVertexIndices;  // 実際のPMX頂点インデックスリスト

            // 照合結果
            public bool IsMatched => MeshExpandedVertexCount == PMXVertexCount;
        }

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("Tools/Poly_Ling/PMX Partial Export")]
        public static void ShowWindow()
        {
            Open(null);
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<PMXPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(550, 450);
            if (ctx != null) panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawPMXFileSection();
            EditorGUILayout.Space(10);

            DrawMappingSection();
            EditorGUILayout.Space(10);

            DrawOptionsSection();
            EditorGUILayout.Space(10);

            DrawExportSection();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // PMXファイルセクション
        // ================================================================

        private void DrawPMXFileSection()
        {
            EditorGUILayout.LabelField(T("ReferencePMX"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("PMXFile"));
                var pmxRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _pmxFilePath = EditorGUI.TextField(pmxRect, _pmxFilePath);

                // ドロップ対応
                HandleDropOnRect(pmxRect, ".pmx", path =>
                {
                    _pmxFilePath = path;
                    LoadPMX();
                    BuildMappings();
                });

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_pmxFilePath) ? Application.dataPath : Path.GetDirectoryName(_pmxFilePath);
                    string path = EditorUtility.OpenFilePanel("Select Reference PMX", dir, "pmx");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMX();
                        BuildMappings();
                    }
                }
            }

            // PMX情報表示
            if (_pmxDocument != null)
            {
                EditorGUILayout.LabelField($"Materials: {_pmxDocument.Materials.Count}, Vertices: {_pmxDocument.Vertices.Count}", EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // マッピングセクション
        // ================================================================

        private void DrawMappingSection()
        {
            EditorGUILayout.LabelField(T("MeshMapping"), EditorStyles.boldLabel);

            if (_context == null)
            {
                EditorGUILayout.HelpBox(T("NoContext"), MessageType.Warning);
                return;
            }

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("NoModel"), MessageType.Warning);
                return;
            }

            var drawables = model.DrawableMeshes;
            if (drawables == null || drawables.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoDrawableMesh"), MessageType.Info);
                return;
            }

            if (_pmxDocument == null)
            {
                EditorGUILayout.HelpBox(T("SelectPMXFirst"), MessageType.Info);
                return;
            }

            // マッピングが空の場合は構築
            if (_mappings.Count == 0)
            {
                BuildMappings();
            }

            // 選択ボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("SelectAll"), GUILayout.Width(80)))
                {
                    foreach (var m in _mappings) m.Selected = true;
                }
                if (GUILayout.Button(T("SelectNone"), GUILayout.Width(80)))
                {
                    foreach (var m in _mappings) m.Selected = false;
                }
                if (GUILayout.Button(T("SelectMatched"), GUILayout.Width(100)))
                {
                    foreach (var m in _mappings) m.Selected = m.IsMatched;
                }
            }

            EditorGUILayout.Space(5);

            // ヘッダー
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("", GUILayout.Width(20));
                EditorGUILayout.LabelField(T("ModelMeshes"), EditorStyles.miniLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(T("Vertices"), EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("→", EditorStyles.miniLabel, GUILayout.Width(20));
                EditorGUILayout.LabelField(T("PMXMaterials"), EditorStyles.miniLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(T("Vertices"), EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("", GUILayout.Width(30));
            }

            // マッピングリスト
            foreach (var mapping in _mappings)
            {
                DrawMappingRow(mapping);
            }
        }

        private void DrawMappingRow(MeshMaterialMapping mapping)
        {
            Color originalColor = GUI.backgroundColor;

            // 不一致の場合は背景を変える
            if (!mapping.IsMatched && !string.IsNullOrEmpty(mapping.PMXMaterialName))
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // チェックボックス
                mapping.Selected = EditorGUILayout.Toggle(mapping.Selected, GUILayout.Width(20));

                // モデルメッシュ名
                EditorGUILayout.LabelField(mapping.MeshName, GUILayout.Width(150));

                // モデル頂点数（展開後）
                EditorGUILayout.LabelField(mapping.MeshExpandedVertexCount.ToString(), GUILayout.Width(60));

                // 矢印
                EditorGUILayout.LabelField("→", GUILayout.Width(20));

                // PMX材質名
                EditorGUILayout.LabelField(mapping.PMXMaterialName ?? "(none)", GUILayout.Width(150));

                // PMX頂点数
                EditorGUILayout.LabelField(mapping.PMXVertexCount.ToString(), GUILayout.Width(60));

                // 一致/不一致アイコン
                if (!string.IsNullOrEmpty(mapping.PMXMaterialName))
                {
                    if (mapping.IsMatched)
                    {
                        GUI.contentColor = Color.green;
                        EditorGUILayout.LabelField(T("Match"), GUILayout.Width(30));
                        GUI.contentColor = Color.white;
                    }
                    else
                    {
                        GUI.contentColor = Color.red;
                        EditorGUILayout.LabelField(T("Mismatch"), GUILayout.Width(30));
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
        // オプションセクション
        // ================================================================

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(T("ExportOptions"), EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            _scale = EditorGUILayout.FloatField(new GUIContent(T("Scale"), T("ScaleTooltip")), _scale);
            _flipZ = EditorGUILayout.Toggle(T("FlipZ"), _flipZ);
            _flipUV_V = EditorGUILayout.Toggle(T("FlipUV_V"), _flipUV_V);

            EditorGUILayout.Space(3);

            _replacePositions = EditorGUILayout.Toggle(T("ReplacePositions"), _replacePositions);
            _replaceNormals = EditorGUILayout.Toggle(T("ReplaceNormals"), _replaceNormals);
            _replaceUVs = EditorGUILayout.Toggle(T("ReplaceUVs"), _replaceUVs);
            _replaceBoneWeights = EditorGUILayout.Toggle(T("ReplaceBoneWeights"), _replaceBoneWeights);

            EditorGUILayout.Space(3);

            _outputCSV = EditorGUILayout.Toggle(T("OutputCSV"), _outputCSV);

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポートセクション
        // ================================================================

        private void DrawExportSection()
        {
            // 選択数チェック
            int selectedCount = _mappings.Count(m => m.Selected);
            int matchedSelectedCount = _mappings.Count(m => m.Selected && m.IsMatched);

            if (selectedCount == 0)
            {
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Info);
            }
            else if (matchedSelectedCount < selectedCount)
            {
                EditorGUILayout.HelpBox($"Selected: {selectedCount}, Matched: {matchedSelectedCount}", MessageType.Warning);
            }

            // エクスポートボタン
            using (new EditorGUI.DisabledScope(matchedSelectedCount == 0 || _pmxDocument == null))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                {
                    ExecuteExport();
                }
            }

            // 結果表示
            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // PMX読み込み
        // ================================================================

        private void LoadPMX()
        {
            if (string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath))
            {
                _pmxDocument = null;
                return;
            }

            try
            {
                _pmxDocument = PMXReader.Load(_pmxFilePath);
                Debug.Log($"[PMXPartialExport] Loaded PMX: {_pmxDocument.Materials.Count} materials, {_pmxDocument.Vertices.Count} vertices");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXPartialExport] Failed to load PMX: {ex.Message}");
                _pmxDocument = null;
            }
        }

        // ================================================================
        // マッピング構築
        // ================================================================

        private void BuildMappings()
        {
            _mappings.Clear();

            var model = Model;
            if (model == null || _pmxDocument == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            // PMXのObjectNameグループを取得（Memo欄ベース）
            var pmxObjectGroups = PMXHelper.GetObjectNameGroups(_pmxDocument);

            // Drawableメッシュごとにマッピングを作成
            for (int i = 0; i < drawables.Count; i++)
            {
                var entry = drawables[i];
                var ctx = entry.Context;
                if (ctx?.MeshObject == null) continue;

                var vertexInfo = PMXHelper.GetVertexInfo(ctx);
                var mapping = new MeshMaterialMapping
                {
                    DrawableIndex = i,
                    MasterIndex = entry.MasterIndex,
                    MeshName = ctx.Name,
                    MeshVertexCount = vertexInfo.VertexCount,
                    MeshExpandedVertexCount = vertexInfo.ExpandedVertexCount,
                    MeshContext = ctx
                };

                // PMXのObjectNameとの対応を探す（ObjectName = メッシュ名）
                // メッシュ名と一致するObjectName、または "_L" / "_R" サフィックスを除いた名前で検索
                string baseName = ctx.Name;
                if (baseName.EndsWith("_L") || baseName.EndsWith("_R"))
                {
                    baseName = baseName.Substring(0, baseName.Length - 2);
                }

                ObjectGroup matchedGroup = null;
                string matchedKey = null;

                // 完全一致を優先（ObjectNameベース）
                if (pmxObjectGroups.TryGetValue(ctx.Name, out var group))
                {
                    matchedGroup = group;
                    matchedKey = ctx.Name;
                }
                // ベース名で検索
                else if (pmxObjectGroups.TryGetValue(baseName, out group))
                {
                    matchedGroup = group;
                    matchedKey = baseName;
                }
                // "+"サフィックス付き（ミラー）で検索
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
                    // StartIndexは表示用（実際の転送にはVertexIndicesを使用）
                    mapping.PMXVertexStartIndex = matchedGroup.VertexIndices.Count > 0 
                        ? matchedGroup.VertexIndices.Min() : 0;
                }

                _mappings.Add(mapping);
            }

            Debug.Log($"[PMXPartialExport] Built {_mappings.Count} mappings");
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            try
            {
                // 出力パス選択
                string defaultName = Path.GetFileNameWithoutExtension(_pmxFilePath) + "_modified.pmx";
                string savePath = EditorUtility.SaveFilePanel("Save PMX", Path.GetDirectoryName(_pmxFilePath), defaultName, "pmx");

                if (string.IsNullOrEmpty(savePath))
                    return;

                // PMXドキュメントをコピー（元を変更しない）
                // 注: 実際には元のPMXDocumentを直接編集してしまう
                // より安全にするにはクローンが必要だが、ここでは簡略化

                int totalTransferred = 0;

                // 選択されたマッピングを処理
                foreach (var mapping in _mappings)
                {
                    if (!mapping.Selected || !mapping.IsMatched)
                        continue;

                    int transferred = TransferMeshToPMX(mapping);
                    totalTransferred += transferred;
                }

                // ファイル出力
                PMXWriter.Save(_pmxDocument, savePath);

                if (_outputCSV)
                {
                    string csvPath = Path.ChangeExtension(savePath, ".csv");
                    PMXCSVWriter.Save(_pmxDocument, csvPath, 6);
                }

                _lastResult = T("ExportSuccess", $"{totalTransferred} vertices → {Path.GetFileName(savePath)}");
                Debug.Log($"[PMXPartialExport] Export completed: {totalTransferred} vertices");

                // PMXを再読み込み（編集した内容をリセット）
                LoadPMX();
            }
            catch (Exception ex)
            {
                _lastResult = T("ExportFailed", ex.Message);
                Debug.LogError($"[PMXPartialExport] Export failed: {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        /// <summary>
        /// 1メッシュ分のデータをPMXに転送
        /// PMXVertexIndicesに基づいて正確な位置に転送
        /// </summary>
        private int TransferMeshToPMX(MeshMaterialMapping mapping)
        {
            var mo = mapping.MeshContext?.MeshObject;
            if (mo == null) return 0;
            if (mapping.PMXVertexIndices == null || mapping.PMXVertexIndices.Count == 0) return 0;

            int transferred = 0;
            int localIndex = 0;

            // MeshObjectの頂点をPMXVertexIndicesに基づいて転送
            // MeshObjectの頂点順序 = PMXVertexIndices順序（昇順）
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

                // 座標
                if (_replacePositions)
                {
                    Vector3 pos = vertex.Position;
                    if (_flipZ) pos.z = -pos.z;
                    pos *= _scale;
                    pmxVertex.Position = pos;
                }

                // 法線
                if (_replaceNormals)
                {
                    Vector3 normal = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
                    if (_flipZ) normal.z = -normal.z;
                    pmxVertex.Normal = normal;
                }

                // UV
                if (_replaceUVs)
                {
                    Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
                    if (_flipUV_V) uv.y = 1f - uv.y;
                    pmxVertex.UV = uv;
                }

                // ボーンウェイト
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

            Debug.Log($"[PMXPartialExport] Transferred '{mapping.MeshName}' → '{mapping.PMXMaterialName}': {transferred} vertices");
            return transferred;
        }

        // ================================================================
        // ドロップ処理
        // ================================================================

        private void HandleDropOnRect(Rect rect, string extension, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (DragAndDrop.paths.Length > 0 &&
                        Path.GetExtension(DragAndDrop.paths[0]).ToLower() == extension)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;

                case EventType.DragPerform:
                    if (DragAndDrop.paths.Length > 0)
                    {
                        string path = DragAndDrop.paths[0];
                        if (Path.GetExtension(path).ToLower() == extension)
                        {
                            DragAndDrop.AcceptDrag();
                            onDrop(path);
                            evt.Use();
                        }
                    }
                    break;
            }
        }

        // ================================================================
        // コンテキスト変更時
        // ================================================================

        protected override void OnContextSet()
        {
            _mappings.Clear();
            if (_pmxDocument != null)
            {
                BuildMappings();
            }
        }
    }
}