// Assets/Editor/MeshFactory/Tools/Panels/PMX/Export/PMXBoneWeightExportPanel.cs
// PMXのボーンウェイトをMQO頂点と対応付けてCSV出力
// v2.0: 材質ごとの頂点数を列挙

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Localization;
using MeshFactory.MQO;

namespace MeshFactory.PMX
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
            ["PMXFile"] = new() { ["en"] = "PMX CSV File", ["ja"] = "PMX CSVファイル" },
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["DragDropPMX"] = new() { ["en"] = "Drag & Drop PMX CSV here", ["ja"] = "PMX CSVをここにドロップ" },
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
        // PMXデータ構造
        // ================================================================

        private class PMXVertexData
        {
            public int Index;
            public Vector3 Position;
            public Vector2 UV;
            public string[] BoneNames = new string[4];
            public float[] Weights = new float[4];
        }

        private class PMXFaceData
        {
            public string MaterialName;
            public int FaceIndex;
            public int VertexIndex1;
            public int VertexIndex2;
            public int VertexIndex3;
        }

        private class PMXMaterialData
        {
            public string Name;
            public int FaceCount;
            public HashSet<int> UsedVertexIndices = new HashSet<int>();
        }

        // ================================================================
        // フィールド
        // ================================================================

        private string _pmxFilePath = "";
        private string _mqoFilePath = "";

        // PMXデータ
        private List<PMXVertexData> _pmxVertices;
        private List<PMXFaceData> _pmxFaces;
        private List<PMXMaterialData> _pmxMaterials;
        private List<string> _pmxBoneNames;

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
                    // フォールバック
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

        [MenuItem("MeshFactory/Tools/PMX Bone Weight Export...")]
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

            // PMX CSV
            using (new EditorGUILayout.HorizontalScope())
            {
                _pmxFilePath = EditorGUILayout.TextField(T("PMXFile"), _pmxFilePath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select PMX CSV", "", "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMX();
                        CheckVertexCount();
                    }
                }
            }

            DrawDropArea(T("DragDropPMX"), "csv", path =>
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

            DrawDropArea(T("DragDropMQO"), "mqo", path =>
            {
                _mqoFilePath = path;
                LoadMQO();
                CheckVertexCount();
            });
        }

        private void DrawDropArea(string message, string extension, Action<string> onDrop)
        {
            var dropArea = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, message, EditorStyles.helpBox);

            var evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                switch (evt.type)
                {
                    case EventType.DragUpdated:
                        if (DragAndDrop.paths.Length > 0 &&
                            DragAndDrop.paths[0].EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            evt.Use();
                        }
                        break;

                    case EventType.DragPerform:
                        if (DragAndDrop.paths.Length > 0)
                        {
                            string path = DragAndDrop.paths[0];
                            if (path.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
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

        // ================================================================
        // プレビューセクション
        // ================================================================

        private void DrawPreviewSection()
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            EditorGUI.indentLevel++;

            if (_pmxVertices == null && _mqoImportResult == null)
            {
                EditorGUILayout.HelpBox(T("SelectFilesToPreview"), MessageType.Info);
            }
            else
            {
                // PMX情報
                if (_pmxVertices != null)
                {
                    EditorGUILayout.LabelField(T("PMXVertices"), _pmxVertices.Count.ToString());
                    EditorGUILayout.LabelField(T("PMXBones"), _pmxBoneNames?.Count.ToString() ?? "0");
                    EditorGUILayout.LabelField(T("PMXMaterials"), _pmxMaterials?.Count.ToString() ?? "0");

                    // 材質ごとの頂点数
                    if (_pmxMaterials != null && _pmxMaterials.Count > 0)
                    {
                        EditorGUILayout.Space(3);
                        _foldPMXMaterials = EditorGUILayout.Foldout(_foldPMXMaterials, $"PMX Materials ({_pmxMaterials.Count})", true);
                        if (_foldPMXMaterials)
                        {
                            // ボックス内に表示
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                            // ヘッダー（MQOと同じ順序: Verts, Tris）
                            EditorGUILayout.LabelField("Name                        Verts    Tris", MonoStyleBold);

                            int totalVertices = 0;
                            int totalFaces = 0;

                            foreach (var mat in _pmxMaterials)
                            {
                                string name = mat.Name.Length > 24 ? mat.Name.Substring(0, 21) + "..." : mat.Name;
                                string line = $"{name,-24} {mat.UsedVertexIndices.Count,8} {mat.FaceCount,7}";
                                EditorGUILayout.LabelField(line, MonoStyle);

                                totalVertices += mat.UsedVertexIndices.Count;
                                totalFaces += mat.FaceCount;
                            }

                            // 合計
                            EditorGUILayout.LabelField("───────────────────────────────────────────", MonoStyle);
                            EditorGUILayout.LabelField($"{"TOTAL",-24} {_pmxVertices.Count,8} {totalFaces,7}", MonoStyleBold);

                            EditorGUILayout.EndVertical();
                        }
                    }
                }

                EditorGUILayout.Space(5);

                // MQO情報
                if (_mqoImportResult != null && _mqoImportResult.Success)
                {
                    int totalVertices = _mqoImportResult.MeshContexts.Sum(m => m.MeshObject?.VertexCount ?? 0);
                    EditorGUILayout.LabelField(T("MQOVertices"), totalVertices.ToString());
                    EditorGUILayout.LabelField(T("MQOExpandedVertices"), _mqoExpandedVertexCount.ToString());
                    EditorGUILayout.LabelField(T("MQOObjects"), _mqoImportResult.MeshContexts.Count.ToString());

                    // 頂点数チェック結果
                    if (_pmxVertices != null)
                    {
                        EditorGUILayout.Space(3);
                        if (_pmxVertices.Count == _mqoExpandedVertexCount)
                        {
                            EditorGUILayout.HelpBox(T("VertexCountMatch"), MessageType.Info);
                        }
                        else if (_pmxVertices.Count == _mqoExpandedVertexCount * 2)
                        {
                            EditorGUILayout.HelpBox(
                                T("VertexCountMatchMirror", _pmxVertices.Count, _mqoExpandedVertexCount),
                                MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                T("VertexCountMismatch", _pmxVertices.Count, _mqoExpandedVertexCount),
                                MessageType.Error);
                        }
                    }

                    // MQOオブジェクト一覧
                    EditorGUILayout.Space(3);
                    _foldMQOObjects = EditorGUILayout.Foldout(_foldMQOObjects, $"MQO Objects ({_mqoImportResult.MeshContexts.Count})", true);
                    if (_foldMQOObjects)
                    {
                        // ボックス内に表示
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // ヘッダー
                        EditorGUILayout.LabelField("Name                    SrcV  Expand    Tris", MonoStyleBold);

                        int totalSrcVerts = 0;
                        int totalExpanded = 0;
                        int totalTris = 0;

                        foreach (var meshContext in _mqoImportResult.MeshContexts)
                        {
                            var mo = meshContext.MeshObject;
                            if (mo == null) continue;

                            int srcVerts = mo.VertexCount;
                            int expanded = CalculateExpandedVertexCountForMesh(mo);

                            // 三角形数 = 三角形の面数×1 + 四角形の面数×2
                            int triCount = 0;
                            int quadCount = 0;
                            foreach (var f in mo.Faces)
                            {
                                if (f.IsTriangle) triCount++;
                                else if (f.IsQuad) quadCount++;
                            }
                            int tris = triCount + quadCount * 2;

                            string name = meshContext.Name;
                            if (name.Length > 20) name = name.Substring(0, 17) + "...";

                            string line = $"{name,-20} {srcVerts,6} {expanded,7} {tris,7}";
                            EditorGUILayout.LabelField(line, MonoStyle);

                            totalSrcVerts += srcVerts;
                            totalExpanded += expanded;
                            totalTris += tris;

                            // ミラーの場合は別行で表示
                            if (meshContext.IsMirrored)
                            {
                                string mirrorLine = $"  (mirror)           {srcVerts,6} {expanded,7} {tris,7}";
                                EditorGUILayout.LabelField(mirrorLine, MonoStyle);

                                totalSrcVerts += srcVerts;
                                totalExpanded += expanded;
                                totalTris += tris;
                            }
                        }

                        // 合計
                        EditorGUILayout.LabelField("─────────────────────────────────────────────", MonoStyle);
                        EditorGUILayout.LabelField($"{"TOTAL",-20} {totalSrcVerts,6} {totalExpanded,7} {totalTris,7}", MonoStyleBold);

                        EditorGUILayout.EndVertical();
                    }
                }
                else if (_mqoImportResult != null && !_mqoImportResult.Success)
                {
                    EditorGUILayout.HelpBox(T("LoadError", _mqoImportResult.ErrorMessage), MessageType.Error);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポートボタン
        // ================================================================

        private void DrawExportButton()
        {
            bool canExport = _pmxVertices != null &&
                             _mqoImportResult != null &&
                             _mqoImportResult.Success &&
                             (_pmxVertices.Count == _mqoExpandedVertexCount ||
                              _pmxVertices.Count == _mqoExpandedVertexCount * 2);

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
        // PMX読み込み
        // ================================================================

        private void LoadPMX()
        {
            try
            {
                _pmxVertices = new List<PMXVertexData>();
                _pmxFaces = new List<PMXFaceData>();
                _pmxMaterials = new List<PMXMaterialData>();
                _pmxBoneNames = new List<string>();

                var materialDict = new Dictionary<string, PMXMaterialData>();

                var lines = File.ReadAllLines(_pmxFilePath, Encoding.UTF8);

                foreach (var line in lines)
                {
                    if (line.StartsWith("PmxVertex,"))
                    {
                        var v = ParsePMXVertex(line);
                        if (v != null) _pmxVertices.Add(v);
                    }
                    else if (line.StartsWith("PmxFace,"))
                    {
                        var f = ParsePMXFace(line);
                        if (f != null)
                        {
                            _pmxFaces.Add(f);

                            // 材質ごとの頂点を集計
                            if (!materialDict.TryGetValue(f.MaterialName, out var mat))
                            {
                                mat = new PMXMaterialData { Name = f.MaterialName };
                                materialDict[f.MaterialName] = mat;
                            }
                            mat.FaceCount++;
                            mat.UsedVertexIndices.Add(f.VertexIndex1);
                            mat.UsedVertexIndices.Add(f.VertexIndex2);
                            mat.UsedVertexIndices.Add(f.VertexIndex3);
                        }
                    }
                    else if (line.StartsWith("PmxBone,"))
                    {
                        var parts = SplitCSV(line);
                        if (parts.Length >= 2)
                        {
                            _pmxBoneNames.Add(parts[1].Trim('"'));
                        }
                    }
                    else if (line.StartsWith("PmxMaterial,"))
                    {
                        // マテリアル行からも名前を取得（順序を保持するため）
                        var parts = SplitCSV(line);
                        if (parts.Length >= 2)
                        {
                            string matName = parts[1].Trim('"');
                            if (!materialDict.ContainsKey(matName))
                            {
                                materialDict[matName] = new PMXMaterialData { Name = matName };
                            }
                        }
                    }
                }

                // 材質をPmxMaterial行の出現順（またはPmxFace出現順）でリスト化
                // 再度ファイルを読んでマテリアル順を取得
                _pmxMaterials.Clear();
                var orderedMaterialNames = new List<string>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("PmxMaterial,"))
                    {
                        var parts = SplitCSV(line);
                        if (parts.Length >= 2)
                        {
                            string matName = parts[1].Trim('"');
                            if (!orderedMaterialNames.Contains(matName))
                            {
                                orderedMaterialNames.Add(matName);
                            }
                        }
                    }
                }

                // 順序通りにリスト化
                foreach (var matName in orderedMaterialNames)
                {
                    if (materialDict.TryGetValue(matName, out var mat))
                    {
                        _pmxMaterials.Add(mat);
                    }
                }

                // PmxMaterial行がなかった場合はFace出現順
                if (_pmxMaterials.Count == 0)
                {
                    _pmxMaterials = materialDict.Values.ToList();
                }

                Debug.Log($"[PMXBoneWeightExport] Loaded PMX: {_pmxVertices.Count} vertices, {_pmxFaces.Count} faces, {_pmxMaterials.Count} materials, {_pmxBoneNames.Count} bones");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Failed to load PMX: {ex.Message}");
                _pmxVertices = null;
                _pmxFaces = null;
                _pmxMaterials = null;
                _pmxBoneNames = null;
            }
            Repaint();
        }

        private PMXVertexData ParsePMXVertex(string line)
        {
            var parts = SplitCSV(line);
            if (parts.Length < 36) return null;

            var v = new PMXVertexData
            {
                Index = int.Parse(parts[1])
            };

            // 位置 (2,3,4)
            if (parts.Length > 4)
            {
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
                float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
                float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
                v.Position = new Vector3(x, y, z);
            }

            // UV (11,12)
            if (parts.Length > 12)
            {
                float.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out float u);
                float.TryParse(parts[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float vCoord);
                v.UV = new Vector2(u, vCoord);
            }

            // ボーンウェイト（インデックス28-35）
            v.BoneNames[0] = parts[28].Trim('"');
            v.Weights[0] = float.Parse(parts[29], CultureInfo.InvariantCulture);
            v.BoneNames[1] = parts[30].Trim('"');
            v.Weights[1] = float.Parse(parts[31], CultureInfo.InvariantCulture);
            v.BoneNames[2] = parts[32].Trim('"');
            v.Weights[2] = float.Parse(parts[33], CultureInfo.InvariantCulture);
            v.BoneNames[3] = parts[34].Trim('"');
            v.Weights[3] = float.Parse(parts[35], CultureInfo.InvariantCulture);

            return v;
        }

        private PMXFaceData ParsePMXFace(string line)
        {
            var parts = SplitCSV(line);
            // PmxFace,MaterialName,FaceIndex,V1,V2,V3
            if (parts.Length < 6) return null;

            var f = new PMXFaceData
            {
                MaterialName = parts[1].Trim('"'),
                FaceIndex = int.Parse(parts[2]),
                VertexIndex1 = int.Parse(parts[3]),
                VertexIndex2 = int.Parse(parts[4]),
                VertexIndex3 = int.Parse(parts[5])
            };

            return f;
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
                    // 展開後頂点数を計算
                    CalculateExpandedVertexCount();

                    Debug.Log($"[PMXBoneWeightExport] Loaded MQO: {_mqoImportResult.MeshContexts.Count} objects, " +
                              $"expanded vertices: {_mqoExpandedVertexCount}");
                }
                else
                {
                    Debug.LogError($"[PMXBoneWeightExport] Failed to load MQO: {_mqoImportResult.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Failed to load MQO: {ex.Message}");
                _mqoImportResult = null;
            }
            Repaint();
        }

        /// <summary>
        /// 展開後頂点数を計算
        /// ToUnityMeshShared と完全に同じロジック
        /// </summary>
        private void CalculateExpandedVertexCount()
        {
            _mqoExpandedVertexCount = 0;

            foreach (var meshContext in _mqoImportResult.MeshContexts)
            {
                var meshObject = meshContext.MeshObject;
                if (meshObject == null) continue;

                int expanded = CalculateExpandedVertexCountForMesh(meshObject);
                _mqoExpandedVertexCount += expanded;

                // ミラーの場合は2倍
                if (meshContext.IsMirrored)
                {
                    _mqoExpandedVertexCount += expanded;
                }
            }
        }

        /// <summary>
        /// 単一メッシュの展開後頂点数を計算
        /// </summary>
        private int CalculateExpandedVertexCountForMesh(MeshObject meshObject)
        {
            if (meshObject == null) return 0;

            var vertexMapping = new HashSet<(int vIdx, int uvIdx, int normalIdx)>();

            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden) continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                            continue;

                        var vertex = meshObject.Vertices[vIdx];

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                        int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                        if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                            normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                        vertexMapping.Add((vIdx, uvSubIdx, normalSubIdx));
                    }
                }
            }

            return vertexMapping.Count;
        }

        /// <summary>
        /// 頂点数チェック（ミラーベイク検出）
        /// </summary>
        private void CheckVertexCount()
        {
            if (_pmxVertices == null || _mqoImportResult == null || !_mqoImportResult.Success)
            {
                _isMirrorBaked = false;
                return;
            }

            _isMirrorBaked = (_pmxVertices.Count == _mqoExpandedVertexCount * 2);
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
                // PMX頂点Index → PMXVertexData のマップ
                var pmxVertexMap = _pmxVertices.ToDictionary(v => v.Index, v => v);

                // MQO展開頂点Index → PMX頂点Index のマッピングを構築
                // PMXはミラーベイク済みなら前半がオリジナル
                int pmxVertexIndex = 0;

                // MQO頂点 → 最初に対応したPMX頂点Index
                var mqoToPmx = new Dictionary<(string objName, int vertexIndex), int>();

                foreach (var meshContext in _mqoImportResult.MeshContexts)
                {
                    var mo = meshContext.MeshObject;
                    if (mo == null) continue;

                    // 展開頂点を走査（ToUnityMeshSharedと同じ順序）
                    var vertexMapping = new Dictionary<(int vIdx, int uvIdx, int normalIdx), int>();

                    foreach (var face in mo.Faces)
                    {
                        if (face.VertexCount < 3 || face.IsHidden) continue;

                        var triangles = face.Triangulate();
                        foreach (var tri in triangles)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                int vIdx = tri.VertexIndices[i];
                                if (vIdx < 0 || vIdx >= mo.Vertices.Count) continue;

                                var vertex = mo.Vertices[vIdx];

                                int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                                if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                                    uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                                int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                                if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                                    normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                                var key = (vIdx, uvSubIdx, normalSubIdx);

                                if (!vertexMapping.ContainsKey(key))
                                {
                                    vertexMapping[key] = pmxVertexIndex;

                                    // MQO頂点 → PMX頂点 を記録（最初の出現のみ）
                                    var mqoKey = (meshContext.Name, vIdx);
                                    if (!mqoToPmx.ContainsKey(mqoKey))
                                    {
                                        mqoToPmx[mqoKey] = pmxVertexIndex;
                                    }

                                    pmxVertexIndex++;
                                }
                            }
                        }
                    }

                    // ミラーの場合はミラー分もカウント
                    if (meshContext.IsMirrored)
                    {
                        pmxVertexIndex += vertexMapping.Count;
                    }
                }

                // CSV出力
                var sb = new StringBuilder();
                sb.AppendLine("MqoObjectName,VertexID,VertexIndex,Bone0,Bone1,Bone2,Bone3,Weight0,Weight1,Weight2,Weight3");

                int exportedRows = 0;
                int skippedRows = 0;

                foreach (var meshContext in _mqoImportResult.MeshContexts)
                {
                    var mo = meshContext.MeshObject;
                    if (mo == null) continue;

                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var vertex = mo.Vertices[vIdx];
                        var mqoKey = (meshContext.Name, vIdx);

                        if (mqoToPmx.TryGetValue(mqoKey, out int pmxIdx))
                        {
                            if (pmxIdx < _pmxVertices.Count)
                            {
                                var pmxV = _pmxVertices[pmxIdx];

                                int vertexId = vertex.Id != 0 ? vertex.Id : -1;

                                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                    "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                                    meshContext.Name,
                                    vertexId,
                                    vIdx,
                                    pmxV.BoneNames[0],
                                    pmxV.BoneNames[1],
                                    pmxV.BoneNames[2],
                                    pmxV.BoneNames[3],
                                    pmxV.Weights[0],
                                    pmxV.Weights[1],
                                    pmxV.Weights[2],
                                    pmxV.Weights[3]
                                ));
                                exportedRows++;
                            }
                            else
                            {
                                Debug.LogWarning($"[PMXBoneWeightExport] PMX index out of range: {pmxIdx} >= {_pmxVertices.Count}");
                                skippedRows++;
                            }
                        }
                        else
                        {
                            // 孤立頂点
                            skippedRows++;
                        }
                    }
                }

                File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);

                _lastExportResult = T("ExportSuccess", exportedRows);
                if (skippedRows > 0)
                {
                    _lastExportResult += $" (Skipped: {skippedRows})";
                }
                Debug.Log($"[PMXBoneWeightExport] Export completed: {savePath} ({exportedRows} rows, {skippedRows} skipped)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Export failed: {ex.Message}\n{ex.StackTrace}");
                _lastExportResult = $"Error: {ex.Message}";
            }

            Repaint();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private string[] SplitCSV(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var sb = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    sb.Append(c);
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());

            return result.ToArray();
        }
    }
}