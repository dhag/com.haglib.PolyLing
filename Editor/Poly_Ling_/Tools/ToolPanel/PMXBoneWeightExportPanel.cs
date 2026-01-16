// Assets/Editor/Poly_Ling/Tools/Panels/PMX/Export/PMXBoneWeightExportPanel.cs
// PMXのボーンウェイトをMQO頂点と対応付けてCSV出力
// v2.1: バイナリPMX対応

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.MQO;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXボーンウェイト抽出パネル
    /// </summary>
    public class PMXBoneWeightExportPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "PMX Bone Weight Export", ["ja"] = "PMXボーンウェイト出力" },
            ["InputFiles"] = new() { ["en"] = "Input Files", ["ja"] = "入力ファイル" },
            ["PMXFile"] = new() { ["en"] = "PMX File", ["ja"] = "PMXファイル" },
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["DragDropPMX"] = new() { ["en"] = "Drag & Drop PMX/CSV here", ["ja"] = "PMX/CSVをここにドロップ" },
            ["DragDropMQO"] = new() { ["en"] = "Drag & Drop MQO here", ["ja"] = "MQOをここにドロップ" },
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["PMXVertices"] = new() { ["en"] = "PMX Vertices", ["ja"] = "PMX頂点数" },
            ["PMXBones"] = new() { ["en"] = "PMX Bones", ["ja"] = "PMXボーン数" },
            ["PMXMaterials"] = new() { ["en"] = "PMX Materials", ["ja"] = "PMXマテリアル数" },
            ["MQOVertices"] = new() { ["en"] = "MQO Vertices", ["ja"] = "MQO頂点数" },
            ["MQOExpandedVertices"] = new() { ["en"] = "MQO Expanded Vertices", ["ja"] = "MQO展開後頂点数" },
            ["MQOObjects"] = new() { ["en"] = "MQO Objects", ["ja"] = "MQOオブジェクト数" },
            ["SelectFilesToPreview"] = new() { ["en"] = "Select files to preview", ["ja"] = "ファイルを選択してください" },
            ["Export"] = new() { ["en"] = "Export Bone Weights", ["ja"] = "ボーンウェイト出力" },
            ["ExportSuccess"] = new() { ["en"] = "Export completed! {0} rows", ["ja"] = "出力完了！ {0}行" },
            ["VertexCountMatch"] = new() { ["en"] = "OK: Vertex count matched", ["ja"] = "OK: 頂点数一致" },
            ["VertexCountMatchMirror"] = new() { ["en"] = "OK: Mirror baked PMX detected (PMX={0} = MQO={1} x 2)", ["ja"] = "OK: ミラーベイク済みPMX検出 (PMX={0} = MQO={1} x 2)" },
            ["VertexCountMismatch"] = new() { ["en"] = "ERROR: Vertex count mismatch! PMX={0}, MQO Expanded={1}", ["ja"] = "エラー: 頂点数不一致！ PMX={0}, MQO展開後={1}" },
            ["LoadError"] = new() { ["en"] = "Load Error: {0}", ["ja"] = "読み込みエラー: {0}" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => string.Format(L.GetFrom(_localize, key), args);

        // ================================================================
        // フィールド
        // ================================================================

        private string _pmxFilePath = "";
        private string _mqoFilePath = "";

        // PMXデータ（PMXDocument使用）
        private PMXDocument _pmxDocument;

        // MQOデータ（MQOImporter経由）
        private MQOImportResult _mqoImportResult;
        private int _mqoExpandedVertexCount;

        // ミラーベイク検出
        private bool _isMirrorBaked;

        private Vector2 _scrollPosition;
        private bool _foldPreview = true;
        private bool _foldPMXMaterials = true;
        private bool _foldMQOObjects = true;

        // 結果
        private string _lastExportResult = "";

        // 等幅フォントスタイル
        private GUIStyle _monoStyle;
        private GUIStyle _monoStyleBold;

        private GUIStyle MonoStyle
        {
            get
            {
                if (_monoStyle == null)
                {
                    _monoStyle = new GUIStyle(EditorStyles.label)
                    {
                        font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                        fontSize = 11
                    };
                    if (_monoStyle.font == null)
                    {
                        _monoStyle.font = Font.CreateDynamicFontFromOSFont("Courier New", 11);
                    }
                }
                return _monoStyle;
            }
        }

        private GUIStyle MonoStyleBold
        {
            get
            {
                if (_monoStyleBold == null)
                {
                    _monoStyleBold = new GUIStyle(MonoStyle)
                    {
                        fontStyle = FontStyle.Bold
                    };
                }
                return _monoStyleBold;
            }
        }

        // ================================================================
        // Open
        // ================================================================

        public static void ShowWindow()
        {
            var window = GetWindow<PMXBoneWeightExportPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(520, 600);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawFileSection();
            EditorGUILayout.Space(5);

            DrawPreviewSection();
            EditorGUILayout.Space(10);

            DrawExportButton();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // ファイルセクション
        // ================================================================

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField(T("InputFiles"), EditorStyles.boldLabel);

            // PMX (バイナリ/CSV両対応)
            using (new EditorGUILayout.HorizontalScope())
            {
                _pmxFilePath = EditorGUILayout.TextField(T("PMXFile"), _pmxFilePath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select PMX File", "", "pmx,csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMX();
                        CheckVertexCount();
                    }
                }
            }

            DrawDropArea(T("DragDropPMX"), new[] { ".pmx", ".csv" }, path =>
            {
                _pmxFilePath = path;
                LoadPMX();
                CheckVertexCount();
            });

            EditorGUILayout.Space(5);

            // MQO
            using (new EditorGUILayout.HorizontalScope())
            {
                _mqoFilePath = EditorGUILayout.TextField(T("MQOFile"), _mqoFilePath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select MQO", "", "mqo");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _mqoFilePath = path;
                        LoadMQO();
                        CheckVertexCount();
                    }
                }
            }

            DrawDropArea(T("DragDropMQO"), new[] { ".mqo" }, path =>
            {
                _mqoFilePath = path;
                LoadMQO();
                CheckVertexCount();
            });
        }

        private void DrawDropArea(string message, string[] extensions, Action<string> onDrop)
        {
            var dropArea = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, message, EditorStyles.helpBox);

            var evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                switch (evt.type)
                {
                    case EventType.DragUpdated:
                        if (DragAndDrop.paths.Length > 0 && HasValidExtension(DragAndDrop.paths[0], extensions))
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            evt.Use();
                        }
                        break;

                    case EventType.DragPerform:
                        if (DragAndDrop.paths.Length > 0)
                        {
                            string path = DragAndDrop.paths[0];
                            if (HasValidExtension(path, extensions))
                            {
                                DragAndDrop.AcceptDrag();
                                onDrop(path);
                                evt.Use();
                            }
                        }
                        break;
                }
            }
        }

        private bool HasValidExtension(string path, string[] extensions)
        {
            string ext = Path.GetExtension(path).ToLower();
            return extensions.Any(e => e.ToLower() == ext);
        }

        // ================================================================
        // プレビューセクション
        // ================================================================

        private void DrawPreviewSection()
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            EditorGUI.indentLevel++;

            if (_pmxDocument == null && _mqoImportResult == null)
            {
                EditorGUILayout.LabelField(T("SelectFilesToPreview"));
                EditorGUI.indentLevel--;
                return;
            }

            // PMX情報
            if (_pmxDocument != null)
            {
                EditorGUILayout.LabelField(T("PMXVertices"), _pmxDocument.Vertices.Count.ToString(), MonoStyleBold);
                EditorGUILayout.LabelField(T("PMXBones"), _pmxDocument.Bones.Count.ToString(), MonoStyle);
                EditorGUILayout.LabelField(T("PMXMaterials"), _pmxDocument.Materials.Count.ToString(), MonoStyle);

                // 材質詳細
                _foldPMXMaterials = EditorGUILayout.Foldout(_foldPMXMaterials, $"PMX Materials ({_pmxDocument.Materials.Count})", true);
                if (_foldPMXMaterials)
                {
                    EditorGUI.indentLevel++;
                    int faceOffset = 0;
                    foreach (var mat in _pmxDocument.Materials)
                    {
                        // 使用頂点を計算
                        var usedVertices = new HashSet<int>();
                        for (int i = 0; i < mat.FaceCount && faceOffset + i < _pmxDocument.Faces.Count; i++)
                        {
                            var face = _pmxDocument.Faces[faceOffset + i];
                            usedVertices.Add(face.VertexIndex1);
                            usedVertices.Add(face.VertexIndex2);
                            usedVertices.Add(face.VertexIndex3);
                        }
                        
                        string info = $"{mat.Name}: verts={usedVertices.Count}, faces={mat.FaceCount}";
                        EditorGUILayout.LabelField(info, MonoStyle);
                        faceOffset += mat.FaceCount;
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
            }

            // MQO情報
            if (_mqoImportResult != null && _mqoImportResult.Success)
            {
                int totalVerts = _mqoImportResult.MeshContexts.Sum(m => m.MeshObject?.VertexCount ?? 0);
                EditorGUILayout.LabelField(T("MQOVertices"), totalVerts.ToString(), MonoStyle);
                EditorGUILayout.LabelField(T("MQOExpandedVertices"), _mqoExpandedVertexCount.ToString(), MonoStyleBold);
                EditorGUILayout.LabelField(T("MQOObjects"), _mqoImportResult.MeshContexts.Count.ToString(), MonoStyle);

                // オブジェクト詳細
                _foldMQOObjects = EditorGUILayout.Foldout(_foldMQOObjects, $"MQO Objects ({_mqoImportResult.MeshContexts.Count})", true);
                if (_foldMQOObjects)
                {
                    EditorGUI.indentLevel++;
                    foreach (var meshContext in _mqoImportResult.MeshContexts)
                    {
                        var mo = meshContext.MeshObject;
                        if (mo == null) continue;

                        int uvExpand = CalculateExpandedVertexCount(mo);
                        string mirrorMark = meshContext.IsMirrored ? " [Mirror]" : "";
                        string info = $"{meshContext.Name}: verts={mo.VertexCount}, uvExpand={uvExpand}{mirrorMark}";
                        EditorGUILayout.LabelField(info, MonoStyle);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
            }

            // 頂点数チェック結果
            if (_pmxDocument != null && _mqoImportResult != null && _mqoImportResult.Success)
            {
                int pmxCount = _pmxDocument.Vertices.Count;
                if (pmxCount == _mqoExpandedVertexCount)
                {
                    EditorGUILayout.HelpBox(T("VertexCountMatch"), MessageType.Info);
                }
                else if (_isMirrorBaked)
                {
                    EditorGUILayout.HelpBox(T("VertexCountMatchMirror", pmxCount, _mqoExpandedVertexCount), MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(T("VertexCountMismatch", pmxCount, _mqoExpandedVertexCount), MessageType.Error);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポートボタン
        // ================================================================

        private void DrawExportButton()
        {
            bool canExport = _pmxDocument != null && 
                            _mqoImportResult != null && 
                            _mqoImportResult.Success &&
                            (_pmxDocument.Vertices.Count == _mqoExpandedVertexCount || _isMirrorBaked);

            EditorGUI.BeginDisabledGroup(!canExport);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            if (GUILayout.Button(T("Export"), buttonStyle))
            {
                ExecuteExport();
            }

            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_lastExportResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastExportResult, MessageType.Info);
            }
        }

        // ================================================================
        // PMX読み込み（バイナリ/CSV自動判定）
        // ================================================================

        private void LoadPMX()
        {
            try
            {
                string ext = Path.GetExtension(_pmxFilePath).ToLower();
                if (ext == ".pmx")
                {
                    // バイナリPMX
                    _pmxDocument = PMXReader.Load(_pmxFilePath);
                }
                else
                {
                    // CSV
                    _pmxDocument = PMXCSVParser.ParseFile(_pmxFilePath);
                }

                Debug.Log($"[PMXBoneWeightExport] Loaded PMX: {_pmxDocument.Vertices.Count} vertices, " +
                         $"{_pmxDocument.Faces.Count} faces, {_pmxDocument.Materials.Count} materials, " +
                         $"{_pmxDocument.Bones.Count} bones");

                // 材質ごとの詳細ログ
                int faceOffset = 0;
                foreach (var mat in _pmxDocument.Materials)
                {
                    var usedVertices = new HashSet<int>();
                    for (int i = 0; i < mat.FaceCount && faceOffset + i < _pmxDocument.Faces.Count; i++)
                    {
                        var face = _pmxDocument.Faces[faceOffset + i];
                        usedVertices.Add(face.VertexIndex1);
                        usedVertices.Add(face.VertexIndex2);
                        usedVertices.Add(face.VertexIndex3);
                    }
                    Debug.Log($"[PMXBoneWeightExport]   PMX Material '{mat.Name}': verts={usedVertices.Count}, faces={mat.FaceCount}");
                    faceOffset += mat.FaceCount;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Failed to load PMX: {ex.Message}");
                _pmxDocument = null;
            }
            Repaint();
        }

        // ================================================================
        // MQO読み込み（MQOImporter使用）
        // ================================================================

        private void LoadMQO()
        {
            try
            {
                var settings = new MQOImportSettings
                {
                    ImportMaterials = false,
                    SkipHiddenObjects = true,
                    MergeObjects = false,
                    FlipZ = true,
                    FlipUV_V = false
                };

                _mqoImportResult = MQOImporter.ImportFile(_mqoFilePath, settings);

                if (_mqoImportResult.Success)
                {
                    // MQO展開後頂点の総数を計算
                    CalculateExpandedVertexCountMQO();

                    Debug.Log($"[PMXBoneWeightExport] Loaded MQO: {_mqoImportResult.MeshContexts.Count} objects, " +
                             $"expanded vertices: {_mqoExpandedVertexCount}");

                    // 各オブジェクトの詳細
                    foreach (var meshContext in _mqoImportResult.MeshContexts)
                    {
                        var mo = meshContext.MeshObject;
                        if (mo == null) continue;

                        int uvExpand = CalculateExpandedVertexCount(mo);
                        string mirrorMark = meshContext.IsMirrored ? " [Mirror]" : "";
                        Debug.Log($"[PMXBoneWeightExport]   MQO Object '{meshContext.Name}': verts={mo.VertexCount}, " +
                                 $"uvExpand={uvExpand}{mirrorMark}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Failed to load MQO: {ex.Message}");
                _mqoImportResult = null;
            }
            Repaint();
        }

        // ================================================================
        // 頂点数計算
        // ================================================================

        private void CalculateExpandedVertexCountMQO()
        {
            _mqoExpandedVertexCount = 0;
            if (_mqoImportResult == null || !_mqoImportResult.Success) return;

            foreach (var meshContext in _mqoImportResult.MeshContexts)
            {
                var meshObject = meshContext.MeshObject;
                if (meshObject == null) continue;

                int expand = CalculateExpandedVertexCount(meshObject);
                _mqoExpandedVertexCount += expand;

                // ミラーの場合は2倍
                if (meshContext.IsMirrored)
                    _mqoExpandedVertexCount += expand;
            }
        }

        private int CalculateExpandedVertexCount(MeshObject meshObject)
        {
            int count = 0;
            foreach (var vertex in meshObject.Vertices)
            {
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                count += uvCount;
                /***
                緊急チェック用ログ（消したらダメ）
                if (1 < uvCount)
                {// UV展開の詳細ログ
                    foreach (var uv in vertex.UVs)
                    {
                        Debug.Log($"[PMXBoneWeightExport] UV詳細   Vertex ID={vertex.Id}, UV=({uv.x}, {uv.y})");
                    }
                }
                ***/
            }
            return count;
        }

        /// <summary>
        /// 頂点数チェック（ミラーベイク検出）
        /// </summary>
        private void CheckVertexCount()
        {
            if (_pmxDocument == null || _mqoImportResult == null || !_mqoImportResult.Success)
            {
                _isMirrorBaked = false;
                return;
            }

            _isMirrorBaked = (_pmxDocument.Vertices.Count == _mqoExpandedVertexCount * 2);
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            string defaultName = Path.GetFileNameWithoutExtension(_mqoFilePath) + "_weights.csv";
            string savePath = EditorUtility.SaveFilePanel("Save Bone Weights CSV", "", defaultName, "csv");

            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3");

                int exportedRows = 0;
                int skippedRows = 0;
                int pmxVertexIndex = 0;

                Debug.Log($"[PMXBoneWeightExport] === Export Start ===");
                Debug.Log($"[PMXBoneWeightExport] PMX Vertices: {_pmxDocument.Vertices.Count}");
                Debug.Log($"[PMXBoneWeightExport] MQO Objects: {_mqoImportResult.MeshContexts.Count}");

                foreach (var meshContext in _mqoImportResult.MeshContexts)
                {
                    var mo = meshContext.MeshObject;
                    if (mo == null) continue;

                    int objectStartIndex = pmxVertexIndex;
                    int objectVertexCount = 0;
                    int objectUVCount = 0;

                    // FPXと同じ順序：頂点順 → UV順
                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var vertex = mo.Vertices[vIdx];
                        int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                        objectUVCount += uvCount;

                        for (int iuv = 0; iuv < uvCount; iuv++)
                        {
                            if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                            {
                                var pmxV = _pmxDocument.Vertices[pmxVertexIndex];
                                int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                                // ボーン名とウェイトを取得
                                string[] boneNames = new string[4] { "", "", "", "" };
                                float[] weights = new float[4] { 0, 0, 0, 0 };
                                
                                if (pmxV.BoneWeights != null)
                                {
                                    for (int i = 0; i < Math.Min(4, pmxV.BoneWeights.Length); i++)
                                    {
                                        boneNames[i] = pmxV.BoneWeights[i].BoneName ?? "";
                                        weights[i] = pmxV.BoneWeights[i].Weight;
                                    }
                                }

                                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                    "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                                    meshContext.Name,
                                    vertexId,
                                    vIdx,
                                    boneNames[0],
                                    boneNames[1],
                                    boneNames[2],
                                    boneNames[3],
                                    weights[0],
                                    weights[1],
                                    weights[2],
                                    weights[3]
                                ));
                                exportedRows++;
                                objectVertexCount++;
                            }
                            else
                            {
                                skippedRows++;
                            }
                            pmxVertexIndex++;
                        }
                    }

                    Debug.Log($"[PMXBoneWeightExport] Object '{meshContext.Name}': verts={mo.VertexCount}, " +
                             $"uvExpand={objectUVCount}, pmxRange=[{objectStartIndex}..{pmxVertexIndex-1}], " +
                             $"mirror={meshContext.IsMirrored}");

                    // ミラーの場合：オブジェクト名に「+」を付けて出力
                    if (meshContext.IsMirrored)
                    {
                        int mirrorStart = pmxVertexIndex;
                        string mirrorObjectName = meshContext.Name + "+";
                        int mirrorVertexCount = 0;

                        for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                        {
                            var vertex = mo.Vertices[vIdx];
                            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                            for (int iuv = 0; iuv < uvCount; iuv++)
                            {
                                if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                                {
                                    var pmxV = _pmxDocument.Vertices[pmxVertexIndex];
                                    int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                                    // ボーン名とウェイトを取得
                                    string[] boneNames = new string[4] { "", "", "", "" };
                                    float[] weights = new float[4] { 0, 0, 0, 0 };
                                    
                                    if (pmxV.BoneWeights != null)
                                    {
                                        for (int i = 0; i < Math.Min(4, pmxV.BoneWeights.Length); i++)
                                        {
                                            boneNames[i] = pmxV.BoneWeights[i].BoneName ?? "";
                                            weights[i] = pmxV.BoneWeights[i].Weight;
                                        }
                                    }

                                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                        "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                                        mirrorObjectName,
                                        vertexId,
                                        vIdx,
                                        boneNames[0],
                                        boneNames[1],
                                        boneNames[2],
                                        boneNames[3],
                                        weights[0],
                                        weights[1],
                                        weights[2],
                                        weights[3]
                                    ));
                                    exportedRows++;
                                    mirrorVertexCount++;
                                }
                                else
                                {
                                    skippedRows++;
                                }
                                pmxVertexIndex++;
                            }
                        }
                        Debug.Log($"[PMXBoneWeightExport]   Mirror '{mirrorObjectName}': [{mirrorStart}..{pmxVertexIndex-1}], exported={mirrorVertexCount}");
                    }
                }

                Debug.Log($"[PMXBoneWeightExport] Final pmxVertexIndex: {pmxVertexIndex}, PMX count: {_pmxDocument.Vertices.Count}, diff: {pmxVertexIndex - _pmxDocument.Vertices.Count}");

                File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);

                _lastExportResult = T("ExportSuccess", exportedRows);
                if (skippedRows > 0)
                    _lastExportResult += $" (Skipped: {skippedRows})";

                Debug.Log($"[PMXBoneWeightExport] Export: {exportedRows} rows, {skippedRows} skipped");
                Debug.Log($"[PMXBoneWeightExport] === Export End ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Export failed: {ex.Message}\n{ex.StackTrace}");
                _lastExportResult = $"Error: {ex.Message}";
            }

            Repaint();
        }
    }
}
