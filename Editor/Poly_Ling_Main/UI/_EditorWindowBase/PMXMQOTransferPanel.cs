// Assets/Editor/Poly_Ling_/ToolPanels/PMXMQOTransferPanel.cs
// PMX ⇔ MQO オブジェクト単位データ転送パネル
// 
// 【機能】
// - PMX材質とMQOオブジェクトの対応付け
// - 頂点位置の双方向転送
// 
// 【マッピングルール】
// - MQOオブジェクト1つ = PMX材質 N個（N = 使用マテリアル数 × ミラー係数）
// - ミラー係数: ミラーありなら2、なしなら1
// - 材質インデックス0は特殊面専用のため除外
// - 孤立頂点はPMXでは除去済み前提

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.MQO;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX-MQOオブジェクト単位データ転送パネル
    /// </summary>
    public class PMXMQOTransferPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "PMX-MQO Transfer", ["ja"] = "PMX-MQO転送" },
            ["InputFiles"] = new() { ["en"] = "Input Files", ["ja"] = "入力ファイル" },
            ["PMXFile"] = new() { ["en"] = "PMX File", ["ja"] = "PMXファイル" },
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["ObjectMapping"] = new() { ["en"] = "Object Mapping", ["ja"] = "オブジェクト対応" },
            ["MQOObjects"] = new() { ["en"] = "MQO Objects", ["ja"] = "MQOオブジェクト" },
            ["PMXMaterials"] = new() { ["en"] = "PMX Materials", ["ja"] = "PMX材質" },
            ["Transfer"] = new() { ["en"] = "Transfer", ["ja"] = "転送" },
            ["PMXToMQO"] = new() { ["en"] = "PMX → MQO", ["ja"] = "PMX → MQO" },
            ["MQOToPMX"] = new() { ["en"] = "MQO → PMX", ["ja"] = "MQO → PMX" },
            ["TransferPosition"] = new() { ["en"] = "Transfer Vertex Position", ["ja"] = "頂点位置を転送" },
            ["SelectFilesToPreview"] = new() { ["en"] = "Select both files", ["ja"] = "両方のファイルを選択してください" },
            ["VertexCount"] = new() { ["en"] = "Vertices", ["ja"] = "頂点数" },
            ["ExpandedCount"] = new() { ["en"] = "Expanded", ["ja"] = "展開後" },
            ["MaterialCount"] = new() { ["en"] = "Materials", ["ja"] = "材質数" },
            ["Mirror"] = new() { ["en"] = "Mirror", ["ja"] = "ミラー" },
            ["Match"] = new() { ["en"] = "Match", ["ja"] = "一致" },
            ["Mismatch"] = new() { ["en"] = "Mismatch", ["ja"] = "不一致" },
            ["SavePMX"] = new() { ["en"] = "Save PMX", ["ja"] = "PMX保存" },
            ["SaveMQO"] = new() { ["en"] = "Save MQO", ["ja"] = "MQO保存" },
            ["TransferSuccess"] = new() { ["en"] = "Transfer completed: {0} vertices", ["ja"] = "転送完了: {0}頂点" },
            ["TransferError"] = new() { ["en"] = "Transfer Error: {0}", ["ja"] = "転送エラー: {0}" },
            ["Scale"] = new() { ["en"] = "Scale (MQO→PMX)", ["ja"] = "スケール (MQO→PMX)" },
            ["ScaleTooltip"] = new() { ["en"] = "MQO coordinates × Scale = PMX coordinates (default: 0.1)", ["ja"] = "MQO座標 × スケール = PMX座標（通常0.1）" },
            ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z軸反転" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => string.Format(L.GetFrom(_localize, key), args);

        // ================================================================
        // フィールド
        // ================================================================

        private string _pmxFilePath = "";
        private string _mqoFilePath = "";

        // PMXデータ
        private PMXDocument _pmxDocument;

        // MQOデータ
        private MQOImportResult _mqoImportResult;
        private MQODocument _mqoDocument;

        // オブジェクトマッピング
        private List<ObjectMapping> _objectMappings = new List<ObjectMapping>();

        // オプション
        private float _scale = 0.1f;  // MQO→PMXで掛ける値（通常0.1）
        private bool _flipZ = true;

        // UI状態
        private Vector2 _scrollPosition;
        private string _lastResult = "";

        // スタイル
        private GUIStyle _monoStyle;
        private GUIStyle _headerStyle;

        // ================================================================
        // データクラス
        // ================================================================

        /// <summary>
        /// MQOオブジェクトとPMX材質群の対応
        /// </summary>
        private class ObjectMapping
        {
            public bool Selected = false;  // デフォルトでは転送しない

            // MQO側
            public string MqoObjectName;
            public int MqoVertexCount;          // 生頂点数
            public int MqoExpandedVertexCount;  // UV展開後頂点数（孤立点除外）
            public int MqoMaterialCount;        // 使用マテリアル数（材質0除外）
            public bool MqoIsMirrored;
            public MeshContext MeshContext;
            public HashSet<int> IsolatedVertices;  // 孤立頂点インデックス

            // PMX側（頂点範囲）
            public int PmxVertexStartIndex;     // PMX頂点配列内の開始インデックス
            public int PmxVertexCount;          // PMX頂点数（実体+ミラー）

            // PMX側（材質参照情報）
            public List<PMXMaterialInfo> PmxMaterials = new List<PMXMaterialInfo>();

            // 照合結果
            public int ExpectedPmxVertexCount => MqoExpandedVertexCount * (MqoIsMirrored ? 2 : 1);
            public bool IsMatched => ExpectedPmxVertexCount == PmxVertexCount;
        }

        /// <summary>
        /// PMX材質情報（参照用）
        /// </summary>
        private class PMXMaterialInfo
        {
            public string Name;
            public int VertexCount;
            public int FaceCount;
            public bool IsMirror;         // "+"サフィックス付きか
        }

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/PMX-MQO Transfer Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<PMXMQOTransferPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            InitStyles();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawFileSection();
            EditorGUILayout.Space(10);

            DrawMappingSection();
            EditorGUILayout.Space(10);

            DrawTransferSection();

            EditorGUILayout.EndScrollView();
        }

        private void InitStyles()
        {
            if (_monoStyle == null)
            {
                _monoStyle = new GUIStyle(EditorStyles.label)
                {
                    font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                    fontSize = 11
                };
            }
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
                };
            }
        }

        // ================================================================
        // ファイルセクション
        // ================================================================

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField(T("InputFiles"), EditorStyles.boldLabel);

            // PMX
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("PMXFile"));
                var pmxRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _pmxFilePath = EditorGUI.TextField(pmxRect, _pmxFilePath);
                HandleDropOnRect(pmxRect, ".pmx", path =>
                {
                    _pmxFilePath = path;
                    LoadPMX();
                    BuildMappings();
                });
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select PMX File", "", "pmx");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMX();
                        BuildMappings();
                    }
                }
            }

            // MQO
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("MQOFile"));
                var mqoRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _mqoFilePath = EditorGUI.TextField(mqoRect, _mqoFilePath);
                HandleDropOnRect(mqoRect, ".mqo", path =>
                {
                    _mqoFilePath = path;
                    LoadMQO();
                    BuildMappings();
                });
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select MQO", "", "mqo");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _mqoFilePath = path;
                        LoadMQO();
                        BuildMappings();
                    }
                }
            }

            // オプション
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                _scale = EditorGUILayout.FloatField(T("Scale"), _scale, GUILayout.Width(200));
                EditorGUILayout.LabelField(T("ScaleTooltip"), EditorStyles.miniLabel);
            }
            _flipZ = EditorGUILayout.Toggle(T("FlipZ"), _flipZ);
        }

        /// <summary>
        /// 指定矩形へのドロップを処理
        /// </summary>
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
        // マッピングセクション
        // ================================================================

        private void DrawMappingSection()
        {
            EditorGUILayout.LabelField(T("ObjectMapping"), EditorStyles.boldLabel);

            if (_pmxDocument == null || _mqoImportResult == null)
            {
                EditorGUILayout.HelpBox(T("SelectFilesToPreview"), MessageType.Info);
                return;
            }

            // ヘッダー
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(20);  // チェックボックス分
                EditorGUILayout.LabelField("MQO Object", _headerStyle, GUILayout.Width(150));
                EditorGUILayout.LabelField("Verts", _headerStyle, GUILayout.Width(50));
                EditorGUILayout.LabelField("Expand", _headerStyle, GUILayout.Width(50));
                EditorGUILayout.LabelField("Mat", _headerStyle, GUILayout.Width(30));
                EditorGUILayout.LabelField("Mir", _headerStyle, GUILayout.Width(30));
                EditorGUILayout.LabelField("→", _headerStyle, GUILayout.Width(20));
                EditorGUILayout.LabelField("PMX Materials", _headerStyle, GUILayout.Width(200));
                EditorGUILayout.LabelField("PMX Verts", _headerStyle, GUILayout.Width(60));
                EditorGUILayout.LabelField("Status", _headerStyle, GUILayout.Width(60));
            }

            // 各マッピング
            foreach (var mapping in _objectMappings)
            {
                DrawMappingRow(mapping);
            }
        }

        private void DrawMappingRow(ObjectMapping mapping)
        {
            bool hasMapping = mapping.PmxVertexCount > 0;
            Color bgColor;
            if (!hasMapping)
            {
                bgColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);  // グレー（対応なし）
            }
            else if (mapping.IsMatched)
            {
                bgColor = new Color(0.2f, 0.4f, 0.2f, 0.3f);  // 緑（一致）
            }
            else
            {
                bgColor = new Color(0.5f, 0.2f, 0.2f, 0.3f);  // 赤（不一致）
            }

            var rect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(rect, bgColor);

            // 対応なしの場合はチェック不可
            EditorGUI.BeginDisabledGroup(!hasMapping);
            mapping.Selected = EditorGUILayout.Toggle(mapping.Selected && hasMapping, GUILayout.Width(20));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField(mapping.MqoObjectName, _monoStyle, GUILayout.Width(150));
            EditorGUILayout.LabelField(mapping.MqoVertexCount.ToString(), _monoStyle, GUILayout.Width(50));

            int expectedExpand = mapping.ExpectedPmxVertexCount;
            EditorGUILayout.LabelField(expectedExpand.ToString(), _monoStyle, GUILayout.Width(50));

            EditorGUILayout.LabelField(mapping.MqoMaterialCount.ToString(), _monoStyle, GUILayout.Width(30));
            EditorGUILayout.LabelField(mapping.MqoIsMirrored ? "✓" : "", _monoStyle, GUILayout.Width(30));
            EditorGUILayout.LabelField("→", _monoStyle, GUILayout.Width(20));

            // PMX側（対応なしの場合は空欄）
            if (hasMapping)
            {
                string pmxMatNames = string.Join(", ", mapping.PmxMaterials.Select(m => m.Name));
                EditorGUILayout.LabelField(pmxMatNames, _monoStyle, GUILayout.Width(200));
                EditorGUILayout.LabelField(mapping.PmxVertexCount.ToString(), _monoStyle, GUILayout.Width(60));

                string statusText = mapping.IsMatched ? T("Match") : T("Mismatch");
                var statusStyle = new GUIStyle(_monoStyle)
                {
                    normal = { textColor = mapping.IsMatched ? Color.green : Color.red }
                };
                EditorGUILayout.LabelField(statusText, statusStyle, GUILayout.Width(60));
            }
            else
            {
                // 空欄
                EditorGUILayout.LabelField("", _monoStyle, GUILayout.Width(200));
                EditorGUILayout.LabelField("", _monoStyle, GUILayout.Width(60));
                EditorGUILayout.LabelField("", _monoStyle, GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 転送セクション
        // ================================================================

        private void DrawTransferSection()
        {
            EditorGUILayout.LabelField(T("Transfer"), EditorStyles.boldLabel);

            bool canTransfer = _pmxDocument != null && _mqoImportResult != null &&
                              _objectMappings.Any(m => m.Selected && m.IsMatched);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(!canTransfer);

                if (GUILayout.Button(T("PMXToMQO") + " - " + T("TransferPosition"), GUILayout.Height(30)))
                {
                    ExecutePMXToMQO();
                }

                if (GUILayout.Button(T("MQOToPMX") + " - " + T("TransferPosition"), GUILayout.Height(30)))
                {
                    ExecuteMQOToPMX();
                }

                EditorGUI.EndDisabledGroup();
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // ファイル読み込み
        // ================================================================

        private void LoadPMX()
        {
            try
            {
                _pmxDocument = PMXReader.Load(_pmxFilePath);
                Debug.Log($"[PMXMQOTransfer] Loaded PMX: {_pmxDocument.Vertices.Count} vertices, " +
                         $"{_pmxDocument.Materials.Count} materials");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXMQOTransfer] Failed to load PMX: {ex.Message}");
                _pmxDocument = null;
            }
            Repaint();
        }

        private void LoadMQO()
        {
            try
            {
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
                _mqoDocument = MQOParser.ParseFile(_mqoFilePath);

                if (_mqoImportResult.Success)
                {
                    Debug.Log($"[PMXMQOTransfer] Loaded MQO: {_mqoImportResult.MeshContexts.Count} objects");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXMQOTransfer] Failed to load MQO: {ex.Message}");
                _mqoImportResult = null;
                _mqoDocument = null;
            }
            Repaint();
        }

        // ================================================================
        // マッピング構築
        // ================================================================

        private void BuildMappings()
        {
            _objectMappings.Clear();

            if (_pmxDocument == null || _mqoImportResult == null || !_mqoImportResult.Success)
                return;

            // PMX材質情報を取得（参照用）
            var pmxMaterialInfos = CalculatePMXMaterialInfos();

            // PMX頂点インデックス（オブジェクト順に連続配置と仮定）
            int pmxVertexOffset = 0;
            int pmxMaterialIndex = 0;

            foreach (var meshContext in _mqoImportResult.MeshContexts)
            {
                var mo = meshContext.MeshObject;
                if (mo == null) continue;

                var mapping = new ObjectMapping
                {
                    MqoObjectName = meshContext.Name,
                    MqoVertexCount = mo.VertexCount,
                    MqoIsMirrored = meshContext.IsMirrored,
                    MeshContext = meshContext,
                    PmxVertexStartIndex = pmxVertexOffset
                };

                // 孤立頂点を検出
                mapping.IsolatedVertices = GetIsolatedVertices(mo);

                // UV展開後頂点数を計算（孤立点除外）
                mapping.MqoExpandedVertexCount = CalculateExpandedVertexCount(mo, mapping.IsolatedVertices);

                // 使用マテリアル数を計算（材質0除外）
                mapping.MqoMaterialCount = CountUsedMaterials(mo);

                // 期待されるPMX頂点数（実体 + ミラー）
                int expectedPmxVertexCount = mapping.MqoExpandedVertexCount * (mapping.MqoIsMirrored ? 2 : 1);

                // PMX頂点範囲が有効かチェック
                int pmxEndIndex = pmxVertexOffset + expectedPmxVertexCount;
                if (pmxEndIndex <= _pmxDocument.Vertices.Count)
                {
                    // 範囲内 - 対応あり
                    mapping.PmxVertexCount = expectedPmxVertexCount;

                    // 対応するPMX材質を割り当て
                    int pmxConsumeCount = mapping.MqoMaterialCount * (mapping.MqoIsMirrored ? 2 : 1);
                    for (int i = 0; i < pmxConsumeCount && pmxMaterialIndex < pmxMaterialInfos.Count; i++)
                    {
                        mapping.PmxMaterials.Add(pmxMaterialInfos[pmxMaterialIndex++]);
                    }

                    pmxVertexOffset = pmxEndIndex;
                }
                else
                {
                    // 範囲外 - 対応なし（空欄）
                    mapping.PmxVertexCount = 0;
                    // PmxMaterialsは空のまま
                }

                _objectMappings.Add(mapping);

                Debug.Log($"[PMXMQOTransfer] Mapping: '{mapping.MqoObjectName}' " +
                         $"(verts={mapping.MqoVertexCount}, expand={mapping.MqoExpandedVertexCount}, " +
                         $"mats={mapping.MqoMaterialCount}, mirror={mapping.MqoIsMirrored}) " +
                         $"PMX[{mapping.PmxVertexStartIndex}..{mapping.PmxVertexStartIndex + mapping.PmxVertexCount - 1}] " +
                         $"(expected={expectedPmxVertexCount}, actual={mapping.PmxVertexCount}) " +
                         $"→ materials: [{string.Join(", ", mapping.PmxMaterials.Select(m => m.Name))}] " +
                         $"[{(mapping.IsMatched ? "OK" : "MISMATCH")}]");
            }

            // 総頂点数チェック
            Debug.Log($"[PMXMQOTransfer] Total: MQO expanded={pmxVertexOffset}, PMX vertices={_pmxDocument.Vertices.Count}");

            Repaint();
        }

        /// <summary>
        /// PMX材質情報を取得（参照用）
        /// </summary>
        private List<PMXMaterialInfo> CalculatePMXMaterialInfos()
        {
            var result = new List<PMXMaterialInfo>();

            int faceOffset = 0;
            foreach (var mat in _pmxDocument.Materials)
            {
                // 材質が使用する頂点を集計
                var usedVertices = new HashSet<int>();
                for (int i = 0; i < mat.FaceCount && faceOffset + i < _pmxDocument.Faces.Count; i++)
                {
                    var face = _pmxDocument.Faces[faceOffset + i];
                    usedVertices.Add(face.VertexIndex1);
                    usedVertices.Add(face.VertexIndex2);
                    usedVertices.Add(face.VertexIndex3);
                }

                var info = new PMXMaterialInfo
                {
                    Name = mat.Name,
                    VertexCount = usedVertices.Count,
                    FaceCount = mat.FaceCount,
                    IsMirror = mat.Name.EndsWith("+")
                };

                result.Add(info);
                faceOffset += mat.FaceCount;
            }

            return result;
        }

        /// <summary>
        /// 孤立頂点を検出
        /// </summary>
        public static HashSet<int> GetIsolatedVertices(MeshObject mo)
        {
            var usedVertices = new HashSet<int>();

            foreach (var face in mo.Faces)
            {
                if (face.VertexIndices != null)
                {
                    foreach (var vi in face.VertexIndices)
                    {
                        usedVertices.Add(vi);
                    }
                }
            }

            var isolated = new HashSet<int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                if (!usedVertices.Contains(i))
                {
                    isolated.Add(i);
                }
            }

            return isolated;
        }

        /// <summary>
        /// UV展開後の頂点数を計算（孤立点除外）
        /// </summary>
        public static  int CalculateExpandedVertexCount(MeshObject mo, HashSet<int> excludeVertices)
        {
            int count = 0;
            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                if (excludeVertices != null && excludeVertices.Contains(i))
                    continue;

                var vertex = mo.Vertices[i];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                count += uvCount;
            }
            return count;
        }

        /// <summary>
        /// 使用マテリアル数をカウント（材質0除外）
        /// </summary>
        private int CountUsedMaterials(MeshObject mo)
        {
            var usedMaterials = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                // MaterialIndex > 0 のみカウント（0は特殊面）
                if (face.MaterialIndex > 0)
                {
                    usedMaterials.Add(face.MaterialIndex);
                }
            }
            return Math.Max(1, usedMaterials.Count);
        }

        // ================================================================
        // 転送実行: PMX → MQO
        // ================================================================

        private void ExecutePMXToMQO()
        {
            try
            {
                int totalTransferred = 0;

                foreach (var mapping in _objectMappings)
                {
                    if (!mapping.Selected || !mapping.IsMatched)
                        continue;

                    int transferred = TransferPMXToMQO(mapping);
                    totalTransferred += transferred;
                }

                // MQO保存ダイアログ
                string defaultName = Path.GetFileNameWithoutExtension(_mqoFilePath) + "_transferred.mqo";
                string savePath = EditorUtility.SaveFilePanel(T("SaveMQO"), Path.GetDirectoryName(_mqoFilePath), defaultName, "mqo");

                if (!string.IsNullOrEmpty(savePath))
                {
                    MQO.Utility.MQOWriter.WriteToFile(_mqoDocument, savePath);
                    _lastResult = T("TransferSuccess", totalTransferred) + $" → {savePath}";
                    Debug.Log($"[PMXMQOTransfer] PMX→MQO completed: {totalTransferred} vertices");
                }
            }
            catch (Exception ex)
            {
                _lastResult = T("TransferError", ex.Message);
                Debug.LogError($"[PMXMQOTransfer] PMX→MQO failed: {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        /// <summary>
        /// 1オブジェクト分のPMX→MQO頂点位置転送
        /// </summary>
        private int TransferPMXToMQO(ObjectMapping mapping)
        {
            var mo = mapping.MeshContext.MeshObject;
            var mqoObj = _mqoDocument.Objects.FirstOrDefault(o => o.Name == mapping.MqoObjectName);

            if (mo == null || mqoObj == null)
                return 0;

            int transferred = 0;
            int pmxVertexIndex = mapping.PmxVertexStartIndex;  // このオブジェクトのPMX頂点開始位置

            // 実体側のみ転送（ミラー側は無視）
            // PMX頂点はUV展開済みなので、各MQO頂点の先頭UVに対応するPMX頂点から位置を取得

            for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
            {
                // 孤立点はスキップ
                if (mapping.IsolatedVertices.Contains(vIdx))
                    continue;

                var vertex = mo.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                // 先頭UVに対応するPMX頂点から位置を取得
                if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                {
                    var pmxVertex = _pmxDocument.Vertices[pmxVertexIndex];
                    Vector3 newPos = pmxVertex.Position;

                    // 座標変換
                    // PMX → MQO: pmxPos / scale = mqoPos
                    if (_flipZ)
                    {
                        newPos.z = -newPos.z;
                    }
                    if (_scale != 0f)
                    {
                        newPos /= _scale;
                    }

                    // MQO頂点位置を更新
                    vertex.Position = newPos;

                    // MQODocument側も更新
                    if (vIdx < mqoObj.Vertices.Count)
                    {
                        mqoObj.Vertices[vIdx].Position = newPos;
                    }

                    transferred++;
                }

                // UV展開分だけPMXインデックスを進める
                pmxVertexIndex += uvCount;
            }

            Debug.Log($"[PMXMQOTransfer] PMX→MQO '{mapping.MqoObjectName}': {transferred} vertices " +
                     $"(PMX range: {mapping.PmxVertexStartIndex}..{pmxVertexIndex - 1})");
            return transferred;
        }

        // ================================================================
        // 転送実行: MQO → PMX
        // ================================================================

        private void ExecuteMQOToPMX()
        {
            try
            {
                int totalTransferred = 0;

                foreach (var mapping in _objectMappings)
                {
                    if (!mapping.Selected || !mapping.IsMatched)
                        continue;

                    int transferred = TransferMQOToPMX(mapping);
                    totalTransferred += transferred;
                }

                // PMX保存ダイアログ
                string defaultName = Path.GetFileNameWithoutExtension(_pmxFilePath) + "_transferred.pmx";
                string savePath = EditorUtility.SaveFilePanel(T("SavePMX"), Path.GetDirectoryName(_pmxFilePath), defaultName, "pmx");

                if (!string.IsNullOrEmpty(savePath))
                {
                    PMXWriter.Save(_pmxDocument, savePath);
                    _lastResult = T("TransferSuccess", totalTransferred) + $" → {savePath}";
                    Debug.Log($"[PMXMQOTransfer] MQO→PMX completed: {totalTransferred} vertices");
                }
            }
            catch (Exception ex)
            {
                _lastResult = T("TransferError", ex.Message);
                Debug.LogError($"[PMXMQOTransfer] MQO→PMX failed: {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        /// <summary>
        /// 1オブジェクト分のMQO→PMX頂点位置転送
        /// </summary>
        private int TransferMQOToPMX(ObjectMapping mapping)
        {
            var mo = mapping.MeshContext.MeshObject;
            if (mo == null)
                return 0;

            int transferred = 0;
            int pmxVertexIndex = mapping.PmxVertexStartIndex;  // このオブジェクトのPMX頂点開始位置

            // ミラー係数
            int mirrorFactor = mapping.MqoIsMirrored ? 2 : 1;

            for (int pass = 0; pass < mirrorFactor; pass++)
            {
                bool isMirrorPass = (pass == 1);

                for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                {
                    // 孤立点はスキップ
                    if (mapping.IsolatedVertices.Contains(vIdx))
                        continue;

                    var vertex = mo.Vertices[vIdx];
                    int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                    // MQO頂点位置を取得
                    Vector3 mqoPos = vertex.Position;

                    // 座標変換
                    // MQO → PMX: mqoPos * scale = pmxPos
                    if (_flipZ)
                    {
                        mqoPos.z = -mqoPos.z;
                    }
                    mqoPos *= _scale;

                    // ミラー側の場合はX軸反転
                    if (isMirrorPass)
                    {
                        mqoPos.x = -mqoPos.x;
                    }

                    // UV展開分のPMX頂点に同じ位置を設定
                    for (int iuv = 0; iuv < uvCount; iuv++)
                    {
                        if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                        {
                            _pmxDocument.Vertices[pmxVertexIndex].Position = mqoPos;
                            transferred++;
                        }
                        pmxVertexIndex++;
                    }
                }
            }

            Debug.Log($"[PMXMQOTransfer] MQO→PMX '{mapping.MqoObjectName}': {transferred} vertices " +
                     $"(PMX range: {mapping.PmxVertexStartIndex}..{pmxVertexIndex - 1})");
            return transferred;
        }
    }
}