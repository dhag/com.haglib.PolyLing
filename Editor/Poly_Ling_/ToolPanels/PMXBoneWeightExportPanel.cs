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
using Poly_Ling.MQO.Utility;

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
            ["AssumeMirrorBaked"] = new() { ["en"] = "Assume mirror is baked", ["ja"] = "ミラーはベイク済みとみなす" },
            ["MatchResults"] = new() { ["en"] = "Match Results", ["ja"] = "照合結果" },
            ["MatchOK"] = new() { ["en"] = "OK", ["ja"] = "OK" },
            ["MatchMismatch"] = new() { ["en"] = "Mismatch", ["ja"] = "不一致" },
            ["MatchSkipped"] = new() { ["en"] = "Skipped (0 verts)", ["ja"] = "スキップ (0頂点)" },
            ["AllMatched"] = new() { ["en"] = "All objects matched", ["ja"] = "全オブジェクト一致" },
            ["MismatchFound"] = new() { ["en"] = "{0} mismatches found", ["ja"] = "{0}件の不一致" },
            ["EmbedToMQO"] = new() { ["en"] = "Embed to MQO", ["ja"] = "MQOに埋め込み" },
            ["EmbedSuccess"] = new() { ["en"] = "Embed completed! {0} vertices, {1} bones", ["ja"] = "埋め込み完了！ {0}頂点, {1}ボーン" },
            ["EmbedError"] = new() { ["en"] = "Embed Error: {0}", ["ja"] = "埋め込みエラー: {0}" },
            ["EmbedBones"] = new() { ["en"] = "Embed bone positions", ["ja"] = "ボーン位置を埋め込む" },
            ["BoneScale"] = new() { ["en"] = "Bone Scale", ["ja"] = "ボーンスケール" },
            ["BoneFlipZ"] = new() { ["en"] = "Flip Z axis", ["ja"] = "Z軸反転" },
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

        // MQOデータ（MQOParser経由 - 埋め込み用）
        private MQODocument _mqoDocument;

        // ミラーベイク検出
        private bool _isMirrorBaked;

        // オプション
        private bool _assumeMirrorBaked = true;  // ミラーはベイク済みとみなす
        private bool _embedBones = true;          // ボーン位置を埋め込むか
        private float _boneScale = 10f;           // ボーンスケール（PMX→MQO変換用）
        private bool _boneFlipZ = true;           // ボーンZ軸反転

        // 照合結果
        private List<ObjectMatchResult> _objectMatchResults = new List<ObjectMatchResult>();

        private Vector2 _scrollPosition;
        private bool _foldPreview = true;
        private bool _foldPMXMaterials = true;
        private bool _foldMQOObjects = true;
        private bool _foldMatchResults = true;

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

            DrawEmbedOptions();
            EditorGUILayout.Space(5);

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
                EditorGUILayout.PrefixLabel(T("PMXFile"));
                var pmxRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _pmxFilePath = EditorGUI.TextField(pmxRect, _pmxFilePath);
                HandleDropOnRect(pmxRect, new[] { ".pmx", ".csv" }, path =>
                {
                    _pmxFilePath = path;
                    LoadPMX();
                    CheckVertexCount();
                });
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

            EditorGUILayout.Space(5);

            // MQO
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("MQOFile"));
                var mqoRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _mqoFilePath = EditorGUI.TextField(mqoRect, _mqoFilePath);
                HandleDropOnRect(mqoRect, new[] { ".mqo" }, path =>
                {
                    _mqoFilePath = path;
                    LoadMQO();
                    CheckVertexCount();
                });
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
        }

        /// <summary>
        /// 指定矩形へのドロップを処理
        /// </summary>
        private void HandleDropOnRect(Rect rect, string[] extensions, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

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

                // "+"サフィックスのマテリアル数をカウント（ミラーベイク結果）
                int plusCount = _pmxDocument.Materials.Count(m => m.Name.EndsWith("+"));
                string matCountStr = plusCount > 0
                    ? $"{_pmxDocument.Materials.Count} ({plusCount} '+' mirror baked)"
                    : _pmxDocument.Materials.Count.ToString();
                EditorGUILayout.LabelField(T("PMXMaterials"), matCountStr, MonoStyle);

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

                        // "+"マテリアルはミラーベイク結果
                        bool isPlus = mat.Name.EndsWith("+");
                        string plusMark = isPlus ? " [Mirror+]" : "";
                        string info = $"{mat.Name}: verts={usedVertices.Count}, faces={mat.FaceCount}{plusMark}";

                        // "+"マテリアルは色を変えて表示
                        if (isPlus)
                        {
                            var plusStyle = new GUIStyle(MonoStyle) { normal = { textColor = new Color(0.5f, 0.7f, 1f) } };
                            EditorGUILayout.LabelField(info, plusStyle);
                        }
                        else
                        {
                            EditorGUILayout.LabelField(info, MonoStyle);
                        }
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

                        // 使用マテリアル数
                        var usedMats = new HashSet<int>();
                        foreach (var face in mo.Faces)
                            if (face.MaterialIndex >= 0) usedMats.Add(face.MaterialIndex);
                        int matCount = Math.Max(1, usedMats.Count);

                        string mirrorMark = meshContext.IsMirrored ? " [M]" : "";
                        string info = $"{meshContext.Name}: expand={uvExpand}, mats={matCount}{mirrorMark}";
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

                EditorGUILayout.Space(5);

                // オプション
                EditorGUI.BeginChangeCheck();
                _assumeMirrorBaked = EditorGUILayout.Toggle(T("AssumeMirrorBaked"), _assumeMirrorBaked);
                if (EditorGUI.EndChangeCheck())
                {
                    // オプション変更時に再計算
                    CalculateExpandedVertexCountMQO();
                    CheckVertexCount();
                }

                EditorGUILayout.Space(5);

                // 照合結果表示
                DrawMatchResults();
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 照合結果の表示
        /// </summary>
        private void DrawMatchResults()
        {
            if (_objectMatchResults.Count == 0) return;

            int mismatchCount = _objectMatchResults.Count(r => r.Status == MatchStatus.Mismatch);
            string headerText = mismatchCount > 0
                ? $"{T("MatchResults")} - {T("MismatchFound", mismatchCount)}"
                : $"{T("MatchResults")} - {T("AllMatched")}";

            _foldMatchResults = EditorGUILayout.Foldout(_foldMatchResults, headerText, true);
            if (!_foldMatchResults) return;

            EditorGUI.indentLevel++;

            // 不一致のみ表示するか、全て表示するか
            foreach (var result in _objectMatchResults)
            {
                string statusIcon;
                GUIStyle style = MonoStyle;

                switch (result.Status)
                {
                    case MatchStatus.OK:
                        statusIcon = "✓";
                        break;
                    case MatchStatus.Mismatch:
                        statusIcon = "✗";
                        style = new GUIStyle(MonoStyle) { normal = { textColor = Color.red } };
                        break;
                    default:
                        statusIcon = "-";
                        break;
                }

                string mirrorMark = result.IsMirror ? "[M]" : "";
                string line = $"{statusIcon} [{result.Index}] MQO: {result.MqoName} ({result.MqoVertices}){mirrorMark} <-> PMX: {result.PmxName} ({result.PmxVertices})";

                if (result.Status == MatchStatus.Mismatch)
                {
                    EditorGUILayout.LabelField(line, style);
                }
                else
                {
                    EditorGUILayout.LabelField(line, MonoStyle);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポートボタン
        // ================================================================

        // ================================================================
        // 埋め込みオプション
        // ================================================================

        private void DrawEmbedOptions()
        {
            EditorGUILayout.LabelField("MQO埋め込みオプション", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                _embedBones = EditorGUILayout.Toggle(T("EmbedBones"), _embedBones);

                EditorGUI.BeginDisabledGroup(!_embedBones);
                _boneScale = EditorGUILayout.FloatField(T("BoneScale"), _boneScale);
                _boneFlipZ = EditorGUILayout.Toggle(T("BoneFlipZ"), _boneFlipZ);
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawExportButton()
        {
            bool canExport = _pmxDocument != null &&
                            _mqoImportResult != null &&
                            _mqoImportResult.Success &&
                            (_pmxDocument.Vertices.Count == _mqoExpandedVertexCount || _isMirrorBaked);

            bool canEmbed = canExport && _mqoDocument != null;

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(!canExport);
                if (GUILayout.Button(T("Export"), buttonStyle))
                {
                    ExecuteExport();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!canEmbed);
                if (GUILayout.Button(T("EmbedToMQO"), buttonStyle))
                {
                    ExecuteEmbedToMQO();
                }
                EditorGUI.EndDisabledGroup();
            }

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
                    FlipUV_V = false,
                    BakeMirror = false  // PMX頂点数と一致させるためミラーベイクを無効化
                };

                _mqoImportResult = MQOImporter.ImportFile(_mqoFilePath, settings);

                // MQODocument も読み込み（埋め込み用）
                _mqoDocument = MQOParser.ParseFile(_mqoFilePath);

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
                _mqoDocument = null;
            }
            Repaint();
        }

        // ================================================================
        // 頂点数計算
        // ================================================================

        private void CalculateExpandedVertexCountMQO()
        {
            // PerformObjectMatching() 内で積み上げるので、ここでは初期化のみ
            _mqoExpandedVertexCount = 0;
        }

        private int CalculateExpandedVertexCount(MeshObject meshObject, HashSet<int> excludeVertices = null)
        {
            int count = 0;
            for (int i = 0; i < meshObject.Vertices.Count; i++)
            {
                // 除外頂点（孤立点）はスキップ
                if (excludeVertices != null && excludeVertices.Contains(i))
                    continue;

                var vertex = meshObject.Vertices[i];
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
        /// 頂点数チェック（ミラーベイク検出）と順次照合
        /// </summary>
        private void CheckVertexCount()
        {
            _objectMatchResults.Clear();
            _isMirrorBaked = false;

            if (_pmxDocument == null || _mqoImportResult == null || !_mqoImportResult.Success)
            {
                return;
            }

            // 順次照合を実行（_mqoExpandedVertexCountもここで積み上げる）
            PerformObjectMatching();

            // ミラーベイク検出（PerformObjectMatchingの後）
            _isMirrorBaked = (_pmxDocument.Vertices.Count == _mqoExpandedVertexCount * 2);
        }

        /// <summary>
        /// PMXマテリアルとMQOオブジェクトを順次照合
        /// </summary>
        private void PerformObjectMatching()
        {
            _objectMatchResults.Clear();

            // MQOオブジェクトをフィルタリング（頂点数0を除外）
            // 各オブジェクトの実マテリアル数も計算
            var mqoObjects = new List<(string Name, int ExpandedVerts, bool IsMirror, int MaterialCount)>();
            foreach (var meshContext in _mqoImportResult.MeshContexts)
            {
                var mo = meshContext.MeshObject;
                if (mo == null) continue;

                // 孤立点を検出して除外した頂点数を計算
                var isolatedVertices = GetIsolatedVertices(mo);
                int expand = CalculateExpandedVertexCount(mo, isolatedVertices);

                if (isolatedVertices.Count > 0)
                {
                    Debug.LogWarning($"[PMXBoneWeightExport] Object '{meshContext.Name}': {isolatedVertices.Count} isolated vertices detected (excluded from matching): [{string.Join(", ", isolatedVertices.OrderBy(x => x))}]");
                }

                if (expand == 0)
                {
                    Debug.Log($"[PMXBoneWeightExport] Skipped (0 verts): '{meshContext.Name}'");
                    continue;
                }

                // 使用マテリアル数をカウント（MaterialIndex=0は特殊マテリアル"頂点"なので除外）
                var usedMaterials = new HashSet<int>();
                foreach (var face in mo.Faces)
                {
                    if (face.MaterialIndex > 0)
                        usedMaterials.Add(face.MaterialIndex);
                }
                int matCount = Math.Max(1, usedMaterials.Count);

                mqoObjects.Add((meshContext.Name, expand, meshContext.IsMirrored, matCount));
                Debug.Log($"[PMXBoneWeightExport] MQO '{meshContext.Name}': verts={expand} (isolated={isolatedVertices.Count}), mirror={meshContext.IsMirrored}, mats={matCount}");
            }

            // PMXマテリアルから各マテリアルの頂点数を計算
            var pmxMaterialVerts = new List<(string Name, int VertCount)>();
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
                faceOffset += mat.FaceCount;

                if (usedVertices.Count > 0)
                {
                    pmxMaterialVerts.Add((mat.Name, usedVertices.Count));
                }
            }

            Debug.Log($"[PMXBoneWeightExport] === Object Matching ===");
            Debug.Log($"[PMXBoneWeightExport] MQO objects (non-zero): {mqoObjects.Count}, PMX materials (non-zero): {pmxMaterialVerts.Count}");
            Debug.Log($"[PMXBoneWeightExport] _assumeMirrorBaked={_assumeMirrorBaked}");

            // 順次照合
            // _assumeMirrorBaked=true: ミラーオブジェクトはPMXで頂点2倍、マテリアル2倍
            // _assumeMirrorBaked=false: ミラーでもそのまま比較
            int pmxIdx = 0;
            int matchIndex = 0;
            _mqoExpandedVertexCount = 0;  // ここで積み上げる

            foreach (var mqo in mqoObjects)
            {
                // このMQOオブジェクトが消費するPMXマテリアル数
                // _assumeMirrorBaked=true かつ ミラーなら2倍
                int pmxConsumeCount = (_assumeMirrorBaked && mqo.IsMirror)
                    ? mqo.MaterialCount * 2
                    : mqo.MaterialCount;

                // MQO頂点数（_assumeMirrorBaked=true かつ ミラーなら2倍）
                int mqoVertexCount = (_assumeMirrorBaked && mqo.IsMirror)
                    ? mqo.ExpandedVerts * 2
                    : mqo.ExpandedVerts;

                var result = new ObjectMatchResult { Index = matchIndex++ };
                result.MqoName = mqo.Name;
                result.MqoVertices = mqoVertexCount;
                result.IsMirror = mqo.IsMirror;

                // 総頂点数に積み上げ
                _mqoExpandedVertexCount += mqoVertexCount;

                // PMXマテリアルを消費
                var pmxNames = new List<string>();
                int pmxVertSum = 0;
                for (int i = 0; i < pmxConsumeCount && pmxIdx < pmxMaterialVerts.Count; i++)
                {
                    var pmx = pmxMaterialVerts[pmxIdx++];
                    pmxNames.Add(pmx.Name);
                    pmxVertSum += pmx.VertCount;
                }

                if (pmxNames.Count > 0)
                {
                    result.PmxName = string.Join(" + ", pmxNames);
                    result.PmxVertices = pmxVertSum;
                }
                else
                {
                    result.PmxName = "(なし)";
                    result.PmxVertices = 0;
                }

                // 照合判定
                if (result.MqoVertices == 0 || result.PmxVertices == 0)
                {
                    result.Status = MatchStatus.Mismatch;
                }
                else if (result.MqoVertices == result.PmxVertices)
                {
                    result.Status = MatchStatus.OK;
                }
                else
                {
                    result.Status = MatchStatus.Mismatch;
                }

                _objectMatchResults.Add(result);

                // デバッグ出力
                string mirrorMark = result.IsMirror ? $" [Mirror, mats={mqo.MaterialCount}]" : $" [mats={mqo.MaterialCount}]";
                string statusStr = result.Status == MatchStatus.OK ? "OK" : "MISMATCH";
                Debug.Log($"[PMXBoneWeightExport] [{matchIndex - 1}] {statusStr}: MQO '{result.MqoName}' ({result.MqoVertices}){mirrorMark} <-> PMX '{result.PmxName}' ({result.PmxVertices})");
            }

            // 残りのPMXマテリアルがあれば出力
            while (pmxIdx < pmxMaterialVerts.Count)
            {
                var pmx = pmxMaterialVerts[pmxIdx++];
                Debug.LogWarning($"[PMXBoneWeightExport] Unmatched PMX material: '{pmx.Name}' ({pmx.VertCount})");
            }

            // サマリー出力
            int mismatchCount = _objectMatchResults.Count(r => r.Status == MatchStatus.Mismatch);
            if (mismatchCount > 0)
            {
                Debug.LogWarning($"[PMXBoneWeightExport] {mismatchCount} mismatches found!");
            }
            else
            {
                Debug.Log($"[PMXBoneWeightExport] All {_objectMatchResults.Count} objects matched.");
            }
            Debug.Log($"[PMXBoneWeightExport] === End Matching ===");
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

                    // 孤立点を検出
                    var isolatedVertices = GetIsolatedVertices(mo);
                    if (isolatedVertices.Count > 0)
                    {
                        Debug.LogWarning($"[PMXBoneWeightExport] Object '{meshContext.Name}': {isolatedVertices.Count} isolated vertices: [{string.Join(", ", isolatedVertices.OrderBy(x => x))}]");
                    }

                    // FPXと同じ順序：頂点順 → UV順
                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var vertex = mo.Vertices[vIdx];
                        int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                        objectUVCount += uvCount;

                        // 孤立点の場合：ボーン-1、重み0で出力、pmxVertexIndexはインクリメントしない
                        if (isolatedVertices.Contains(vIdx))
                        {
                            for (int iuv = 0; iuv < uvCount; iuv++)
                            {
                                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                    "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                                    meshContext.Name,
                                    -1,
                                    vIdx,
                                    "", "", "", "",
                                    0f, 0f, 0f, 0f
                                ));
                                exportedRows++;
                            }
                            continue;
                        }

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
                             $"uvExpand={objectUVCount}, isolated={isolatedVertices.Count}, pmxRange=[{objectStartIndex}..{pmxVertexIndex - 1}], " +
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

                            // 孤立点の場合：ボーン-1、重み0で出力、pmxVertexIndexはインクリメントしない
                            if (isolatedVertices.Contains(vIdx))
                            {
                                for (int iuv = 0; iuv < uvCount; iuv++)
                                {
                                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                        "{0},{1},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F6}",
                                        mirrorObjectName,
                                        -1,
                                        vIdx,
                                        "", "", "", "",
                                        0f, 0f, 0f, 0f
                                    ));
                                    exportedRows++;
                                }
                                continue;
                            }

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
                        Debug.Log($"[PMXBoneWeightExport]   Mirror '{mirrorObjectName}': [{mirrorStart}..{pmxVertexIndex - 1}], exported={mirrorVertexCount}, isolated={isolatedVertices.Count}");
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

        // ================================================================
        // MQO埋め込み実行
        // ================================================================

        /// <summary>
        /// PMXのボーンウェイトをMQOの四角形特殊面として埋め込み
        /// </summary>
        private void ExecuteEmbedToMQO()
        {
            if (_pmxDocument == null || _mqoDocument == null || _mqoImportResult == null)
            {
                _lastExportResult = T("EmbedError", "Data not loaded");
                return;
            }

            // 保存先を選択
            string defaultPath = Path.Combine(
                Path.GetDirectoryName(_mqoFilePath),
                Path.GetFileNameWithoutExtension(_mqoFilePath) + "_weight.mqo");
            string savePath = EditorUtility.SaveFilePanel("Save MQO with Bone Weights",
                Path.GetDirectoryName(defaultPath),
                Path.GetFileName(defaultPath), "mqo");

            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                Debug.Log($"[PMXBoneWeightExport] === Embed to MQO Start ===");

                // ボーン名→インデックスのマッピングを作成
                var boneNameToIndex = new Dictionary<string, int>();
                for (int i = 0; i < _pmxDocument.Bones.Count; i++)
                {
                    string boneName = _pmxDocument.Bones[i].Name;
                    if (!boneNameToIndex.ContainsKey(boneName))
                        boneNameToIndex[boneName] = i;
                }

                int pmxVertexIndex = 0;
                int embeddedCount = 0;

                foreach (var meshContext in _mqoImportResult.MeshContexts)
                {
                    var mo = meshContext.MeshObject;
                    if (mo == null) continue;

                    // MQODocument内の対応オブジェクトを探す
                    var mqoObj = _mqoDocument.Objects.FirstOrDefault(o => o.Name == meshContext.Name);
                    if (mqoObj == null)
                    {
                        Debug.LogWarning($"[PMXBoneWeightExport] MQO object not found: {meshContext.Name}");
                        continue;
                    }

                    // 孤立点を検出
                    var isolatedVertices = GetIsolatedVertices(mo);

                    // 既存の四角形特殊面（ボーンウェイト用）を削除
                    var facesToKeep = mqoObj.Faces.Where(f =>
                        !(f.IsSpecialFace && f.VertexCount == 4)).ToList();

                    // 新しいボーンウェイト特殊面を作成
                    var newBoneWeightFaces = new List<MQOFace>();

                    for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                    {
                        var vertex = mo.Vertices[vIdx];
                        int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                        // 孤立点はスキップ
                        if (isolatedVertices.Contains(vIdx))
                            continue;

                        // UV展開分ループ（最初のUVのウェイトのみ使用）
                        for (int iuv = 0; iuv < uvCount; iuv++)
                        {
                            if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                            {
                                var pmxV = _pmxDocument.Vertices[pmxVertexIndex];

                                // 最初のUVのときだけボーンウェイト特殊面を作成
                                if (iuv == 0 && pmxV.BoneWeights != null && pmxV.BoneWeights.Length > 0)
                                {
                                    var boneWeightData = new VertexIdHelper.BoneWeightData();

                                    for (int i = 0; i < Math.Min(4, pmxV.BoneWeights.Length); i++)
                                    {
                                        string boneName = pmxV.BoneWeights[i].BoneName ?? "";
                                        float weight = pmxV.BoneWeights[i].Weight;
                                        int boneIndex = boneNameToIndex.TryGetValue(boneName, out int idx) ? idx : 0;

                                        switch (i)
                                        {
                                            case 0:
                                                boneWeightData.BoneIndex0 = boneIndex;
                                                boneWeightData.Weight0 = weight;
                                                break;
                                            case 1:
                                                boneWeightData.BoneIndex1 = boneIndex;
                                                boneWeightData.Weight1 = weight;
                                                break;
                                            case 2:
                                                boneWeightData.BoneIndex2 = boneIndex;
                                                boneWeightData.Weight2 = weight;
                                                break;
                                            case 3:
                                                boneWeightData.BoneIndex3 = boneIndex;
                                                boneWeightData.Weight3 = weight;
                                                break;
                                        }
                                    }

                                    if (boneWeightData.HasWeight)
                                    {
                                        var specialFace = VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, boneWeightData, isMirror: false);
                                        newBoneWeightFaces.Add(specialFace);
                                        embeddedCount++;
                                    }
                                }
                            }
                            pmxVertexIndex++;
                        }
                    }

                    // 面リストを更新
                    mqoObj.Faces.Clear();
                    mqoObj.Faces.AddRange(facesToKeep);
                    mqoObj.Faces.AddRange(newBoneWeightFaces);

                    Debug.Log($"[PMXBoneWeightExport] Embedded to '{meshContext.Name}': {newBoneWeightFaces.Count} bone weight faces (entity)");

                    // ミラーの場合：ミラー側のボーンウェイトも埋め込む
                    if (meshContext.IsMirrored)
                    {
                        var mirrorBoneWeightFaces = new List<MQOFace>();
                        int mirrorEmbeddedCount = 0;

                        for (int vIdx = 0; vIdx < mo.VertexCount; vIdx++)
                        {
                            if (isolatedVertices.Contains(vIdx))
                                continue;
                            var vertex = mo.Vertices[vIdx];
                            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                            for (int iuv = 0; iuv < uvCount; iuv++)
                            {
                                if (pmxVertexIndex < _pmxDocument.Vertices.Count)
                                {
                                    var pmxV = _pmxDocument.Vertices[pmxVertexIndex];

                                    // 最初のUVのときだけミラー側ボーンウェイト特殊面を作成
                                    if (iuv == 0 && pmxV.BoneWeights != null && pmxV.BoneWeights.Length > 0)
                                    {
                                        var boneWeightData = new VertexIdHelper.BoneWeightData();

                                        for (int i = 0; i < Math.Min(4, pmxV.BoneWeights.Length); i++)
                                        {
                                            string boneName = pmxV.BoneWeights[i].BoneName ?? "";
                                            float weight = pmxV.BoneWeights[i].Weight;
                                            int boneIndex = boneNameToIndex.TryGetValue(boneName, out int idx) ? idx : 0;

                                            switch (i)
                                            {
                                                case 0:
                                                    boneWeightData.BoneIndex0 = boneIndex;
                                                    boneWeightData.Weight0 = weight;
                                                    break;
                                                case 1:
                                                    boneWeightData.BoneIndex1 = boneIndex;
                                                    boneWeightData.Weight1 = weight;
                                                    break;
                                                case 2:
                                                    boneWeightData.BoneIndex2 = boneIndex;
                                                    boneWeightData.Weight2 = weight;
                                                    break;
                                                case 3:
                                                    boneWeightData.BoneIndex3 = boneIndex;
                                                    boneWeightData.Weight3 = weight;
                                                    break;
                                            }
                                        }

                                        if (boneWeightData.HasWeight)
                                        {
                                            // isMirror=true で特殊面を作成
                                            var specialFace = VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, boneWeightData, isMirror: true);
                                            mirrorBoneWeightFaces.Add(specialFace);
                                            mirrorEmbeddedCount++;
                                        }
                                    }
                                }
                                pmxVertexIndex++;
                            }
                        }

                        mqoObj.Faces.AddRange(mirrorBoneWeightFaces);
                        embeddedCount += mirrorEmbeddedCount;
                        Debug.Log($"[PMXBoneWeightExport] Embedded to '{meshContext.Name}': {mirrorBoneWeightFaces.Count} bone weight faces (mirror)");
                    }
                }

                // ================================================================
                // ボーン位置埋め込み（MQOExporter互換）
                // ================================================================
                // 【暫定実装】
                // 現在はPMXBoneのPositionのみをtranslationとして出力。
                // PMXにはボーンの回転情報がないため、位置のみ。
                // 将来的には回転・スケールも含む完全なトランスフォームに対応予定。
                // 
                // 注意: MQOExporterも同様のボーン出力機能を持っているが、
                // 現時点ではVertexIdHelperを使用していない。
                // 将来的にはMQOExporterもVertexIdHelperのメソッドを使うように統合予定。
                // ================================================================

                int embeddedBoneCount = 0;

                if (_embedBones && _pmxDocument.Bones.Count > 0)
                {
                    // PMXBone → BoneData に変換
                    var boneDataList = new List<VertexIdHelper.BoneData>();

                    foreach (var pmxBone in _pmxDocument.Bones)
                    {
                        var boneData = new VertexIdHelper.BoneData
                        {
                            Name = pmxBone.Name,
                            ParentIndex = pmxBone.ParentIndex,
                            Position = pmxBone.Position,
                            Rotation = Vector3.zero,  // 【暫定】PMXにはボーン回転情報がない
                            Scale = Vector3.one,      // 【暫定】PMXにはボーンスケール情報がない
                            IsVisible = true,
                            IsLocked = false
                        };
                        boneDataList.Add(boneData);
                    }

                    // VertexIdHelperでMQOオブジェクトを生成
                    var boneObjects = VertexIdHelper.CreateBoneObjectsForMQO(
                        boneDataList,
                        _boneScale,
                        flipZ: _boneFlipZ
                    );

                    // MQODocumentに追加
                    foreach (var boneObj in boneObjects)
                    {
                        _mqoDocument.Objects.Add(boneObj);
                    }

                    embeddedBoneCount = _pmxDocument.Bones.Count;
                    Debug.Log($"[PMXBoneWeightExport] Embedded {embeddedBoneCount} bones");
                }

                // MQOWriterで保存（VertexIdTool.csにあるMQOWriterを使用）
                Poly_Ling.MQO.Utility.MQOWriter.WriteToFile(_mqoDocument, savePath);

                _lastExportResult = T("EmbedSuccess", embeddedCount, embeddedBoneCount);
                Debug.Log($"[PMXBoneWeightExport] Embed completed: {embeddedCount} vertices, {embeddedBoneCount} bones");
                Debug.Log($"[PMXBoneWeightExport] Saved to: {savePath}");
                Debug.Log($"[PMXBoneWeightExport] === Embed to MQO End ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXBoneWeightExport] Embed failed: {ex.Message}\n{ex.StackTrace}");
                _lastExportResult = T("EmbedError", ex.Message);
            }

            Repaint();
        }

        // ================================================================
        // 孤立点検出
        // ================================================================

        /// <summary>
        /// オブジェクト内の孤立点（どのfaceでも使われていない頂点）を検出
        /// </summary>
        private static HashSet<int> GetIsolatedVertices(MeshObject mo)
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

        // ================================================================
        // 照合結果構造体
        // ================================================================

        private enum MatchStatus { OK, Mismatch, Skipped }

        private struct ObjectMatchResult
        {
            public int Index;
            public string MqoName;
            public string PmxName;
            public int MqoVertices;
            public int PmxVertices;
            public MatchStatus Status;
            public bool IsMirror;
        }
    }
}