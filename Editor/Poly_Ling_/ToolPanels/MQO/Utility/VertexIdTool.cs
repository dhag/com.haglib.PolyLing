// Assets/Editor/Poly_Ling/MQO/Utility/VertexIdTool.cs
// =====================================================================
// 頂点ID診断・割り振りツール
// 
// 【機能】
// - MQO/CSVファイルの頂点ID診断
// - 頂点IDの問題検出（未設定、全て同一値、重複など）
// - オブジェクトごとのベースID指定による自動割り振り
// - MQO/CSV両形式への保存
// 
// 【使い方】
// 1. MQOファイルまたはCSVファイルを読み込む
// 2. 「診断」ボタンで頂点IDの状態を確認
// 3. 問題があればオブジェクトごとにベースIDを設定
// 4. 「ID割り振り」で連番IDを生成
// 5. MQOまたはCSVに保存
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.MQO.CSV;

namespace Poly_Ling.MQO.Utility
{
    /// <summary>
    /// 頂点ID診断・割り振りツール
    /// </summary>
    public class VertexIdTool : EditorWindow
    {
        // ================================================================
        // メニュー
        // ================================================================

        [MenuItem("Tools/Poly_Ling/Vertex ID Tool")]
        public static void ShowWindow()
        {
            var window = GetWindow<VertexIdTool>();
            window.titleContent = new GUIContent("頂点ID診断");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        // ================================================================
        // データ
        // ================================================================

        /// <summary>オブジェクトごとの診断情報</summary>
        private class ObjectDiagnostic
        {
            public string Name;
            public int VertexCount;
            public int ValidIdCount;      // 有効なID数（>= 0）
            public int UnsetIdCount;      // 未設定数（-1）
            public int MinId = -1;
            public int MaxId = -1;
            public bool AllSameValue;     // 全て同じ値か
            public int SameValue;         // 同じ値の場合その値
            public HashSet<int> DuplicateIds = new HashSet<int>();
            public List<int> VertexIds = new List<int>();  // 現在のID一覧
            public List<int> UnsetVertexIndices = new List<int>();  // 未設定の頂点インデックス一覧
            public Dictionary<int, List<int>> DuplicateIdVertices = new Dictionary<int, List<int>>();  // 重複ID → 頂点インデックス一覧

            // 割り振り設定
            public int BaseId;            // ベースID（割り振り開始値）
            public bool AssignEnabled = false;  // 割り振り対象か（デフォルトはオフ）

            /// <summary>問題があるか（頂点数0は問題なし）</summary>
            public bool HasProblem => VertexCount > 0 && (UnsetIdCount == VertexCount || AllSameValue || DuplicateIds.Count > 0);

            /// <summary>一部未設定があるか</summary>
            public bool HasPartialUnset => UnsetIdCount > 0 && UnsetIdCount < VertexCount;

            /// <summary>重複があるか</summary>
            public bool HasDuplicate => DuplicateIds.Count > 0;

            /// <summary>診断サマリー</summary>
            public string GetSummary()
            {
                if (VertexCount == 0)
                    return "（空）";
                if (UnsetIdCount == VertexCount)
                    return "IDなし";
                if (AllSameValue)
                    return $"全て同一値({SameValue})";
                if (DuplicateIds.Count > 0)
                    return $"重複あり({DuplicateIds.Count}件)";
                if (UnsetIdCount > 0)
                    return $"一部未設定({UnsetIdCount}/{VertexCount})";
                return $"OK ({MinId}-{MaxId})";
            }
        }

        // ファイルパス
        private string _mqoFilePath = "";
        private string _csvFilePath = "";

        // 読み込みデータ
        private MQODocument _mqoDocument;
        private BoneWeightCSVData _csvData;

        // 診断結果
        private List<ObjectDiagnostic> _diagnostics = new List<ObjectDiagnostic>();
        private bool _diagnosed = false;

        // UI状態
        private Vector2 _scrollPosition;
        private bool _foldDiagnostics = true;
        private bool _foldAssign = true;

        // 一部未設定詳細表示用
        private int _selectedPartialUnsetIndex = -1;
        private string _partialUnsetDetailText = "";
        private Vector2 _partialUnsetScrollPosition;
        private int _partialUnsetBaseId = 0;  // 未設定部分割り振り用ベースID

        // 重複詳細表示用
        private int _selectedDuplicateIndex = -1;
        private string _duplicateDetailText = "";
        private Vector2 _duplicateScrollPosition;

        // 保存パス
        private string _saveMqoPath = "";
        private string _saveCsvPath = "";

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawFileSection();
            EditorGUILayout.Space(10);

            DrawDiagnoseSection();
            EditorGUILayout.Space(10);

            if (_diagnosed && _diagnostics.Count > 0)
            {
                DrawDiagnosticsResult();
                EditorGUILayout.Space(10);

                DrawAssignSection();
                EditorGUILayout.Space(10);

                DrawSaveSection();
            }

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // ファイル読み込みセクション
        // ================================================================

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField("ファイル読み込み", EditorStyles.boldLabel);

            // MQOファイル
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect textFieldRect = EditorGUILayout.GetControlRect();
                _mqoFilePath = EditorGUI.TextField(textFieldRect, "MQOファイル", _mqoFilePath);
                HandleDragAndDrop(textFieldRect, "mqo", path => _mqoFilePath = path);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_mqoFilePath) ? "" : Path.GetDirectoryName(_mqoFilePath);
                    string path = EditorUtility.OpenFilePanel("MQOファイルを選択", dir, "mqo");
                    if (!string.IsNullOrEmpty(path))
                        _mqoFilePath = path;
                }
            }

            // CSVファイル
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect textFieldRect = EditorGUILayout.GetControlRect();
                _csvFilePath = EditorGUI.TextField(textFieldRect, "ウェイトCSV", _csvFilePath);
                HandleDragAndDrop(textFieldRect, "csv", path => _csvFilePath = path);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_csvFilePath) ? "" : Path.GetDirectoryName(_csvFilePath);
                    string path = EditorUtility.OpenFilePanel("CSVファイルを選択", dir, "csv");
                    if (!string.IsNullOrEmpty(path))
                        _csvFilePath = path;
                }
            }

            // 読み込みボタン
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool hasMqo = !string.IsNullOrEmpty(_mqoFilePath) && File.Exists(_mqoFilePath);
                bool hasCsv = !string.IsNullOrEmpty(_csvFilePath) && File.Exists(_csvFilePath);

                EditorGUI.BeginDisabledGroup(!hasMqo);
                if (GUILayout.Button("MQO読み込み"))
                {
                    LoadMQO();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!hasCsv);
                if (GUILayout.Button("CSV読み込み"))
                {
                    LoadCSV();
                }
                EditorGUI.EndDisabledGroup();
            }

            // 読み込み状態表示
            if (_mqoDocument != null)
            {
                EditorGUILayout.HelpBox($"MQO: {_mqoDocument.Objects.Count} オブジェクト読み込み済み", MessageType.Info);
            }
            if (_csvData != null)
            {
                EditorGUILayout.HelpBox($"CSV: {_csvData.ObjectWeights.Count} オブジェクト読み込み済み", MessageType.Info);
            }
        }

        // ================================================================
        // 診断セクション
        // ================================================================

        private void DrawDiagnoseSection()
        {
            EditorGUILayout.LabelField("診断", EditorStyles.boldLabel);

            // ファイルパスがあるか、既に読み込み済みなら診断可能
            bool hasMqoPath = !string.IsNullOrEmpty(_mqoFilePath) && File.Exists(_mqoFilePath);
            bool hasCsvPath = !string.IsNullOrEmpty(_csvFilePath) && File.Exists(_csvFilePath);
            bool canDiagnose = hasMqoPath || hasCsvPath || _mqoDocument != null || _csvData != null;

            EditorGUI.BeginDisabledGroup(!canDiagnose);
            if (GUILayout.Button("頂点ID診断を実行", GUILayout.Height(30)))
            {
                // 必要に応じて自動読み込み
                if (_mqoDocument == null && hasMqoPath)
                {
                    LoadMQO();
                }
                if (_csvData == null && hasCsvPath)
                {
                    LoadCSV();
                }

                // 読み込み後に診断実行
                if (_mqoDocument != null || _csvData != null)
                {
                    RunDiagnosis();
                }
            }
            EditorGUI.EndDisabledGroup();

            if (!canDiagnose)
            {
                EditorGUILayout.HelpBox("MQOまたはCSVファイルを指定してください", MessageType.Warning);
            }
        }

        // ================================================================
        // 診断結果セクション
        // ================================================================

        private void DrawDiagnosticsResult()
        {
            _foldDiagnostics = EditorGUILayout.Foldout(_foldDiagnostics, $"診断結果 ({_diagnostics.Count} オブジェクト)", true);
            if (!_foldDiagnostics) return;

            EditorGUI.indentLevel++;

            // 全体サマリー
            int problemCount = _diagnostics.Count(d => d.HasProblem);
            if (problemCount == 0)
            {
                EditorGUILayout.HelpBox("すべてのオブジェクトで頂点IDが正常です", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"{problemCount} オブジェクトで問題が検出されました", MessageType.Warning);
            }

            // オブジェクトごとの結果
            foreach (var diag in _diagnostics)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    // 問題アイコン
                    if (diag.HasProblem)
                    {
                        GUILayout.Label("⚠", GUILayout.Width(20));
                    }
                    else
                    {
                        GUILayout.Label("✓", GUILayout.Width(20));
                    }

                    // オブジェクト名
                    EditorGUILayout.LabelField(diag.Name, GUILayout.Width(150));

                    // 頂点数
                    EditorGUILayout.LabelField($"{diag.VertexCount} 頂点", GUILayout.Width(70));

                    // 診断結果
                    var style = diag.HasProblem ? EditorStyles.boldLabel : EditorStyles.label;
                    EditorGUILayout.LabelField(diag.GetSummary(), style);
                }
            }

            // 一部未設定の詳細表示
            DrawPartialUnsetDetail();

            // 重複IDの詳細表示
            DrawDuplicateDetail();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 一部未設定オブジェクトの詳細表示
        /// </summary>
        private void DrawPartialUnsetDetail()
        {
            // 一部未設定があるオブジェクトを抽出
            var partialUnsetObjects = _diagnostics.Where(d => d.HasPartialUnset).ToList();
            if (partialUnsetObjects.Count == 0)
                return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("一部未設定の詳細", EditorStyles.boldLabel);

            // ドロップダウン用の表示名リスト
            var displayNames = partialUnsetObjects.Select(d => $"{d.Name} ({d.UnsetIdCount}件)").ToArray();

            // 選択インデックスの補正と初期化
            if (_selectedPartialUnsetIndex < 0 || _selectedPartialUnsetIndex >= partialUnsetObjects.Count)
            {
                _selectedPartialUnsetIndex = 0;
                var selectedDiag = partialUnsetObjects[0];
                UpdatePartialUnsetDetailText(selectedDiag);
                // ベースIDを最大値+1に設定
                _partialUnsetBaseId = selectedDiag.MaxId >= 0 ? selectedDiag.MaxId + 1 : 0;
            }

            // ドロップダウン
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("オブジェクト", GUILayout.Width(80));
                int newIndex = EditorGUILayout.Popup(_selectedPartialUnsetIndex, displayNames);
                if (newIndex != _selectedPartialUnsetIndex)
                {
                    _selectedPartialUnsetIndex = newIndex;
                    var selectedDiag = partialUnsetObjects[newIndex];
                    UpdatePartialUnsetDetailText(selectedDiag);
                    // ベースIDを最大値+1に設定
                    _partialUnsetBaseId = selectedDiag.MaxId >= 0 ? selectedDiag.MaxId + 1 : 0;
                }
            }

            // 詳細テキスト（コピー可能なテキストエリア）
            EditorGUILayout.LabelField("未設定頂点インデックス一覧（コピー可能）", EditorStyles.miniLabel);
            _partialUnsetScrollPosition = EditorGUILayout.BeginScrollView(
                _partialUnsetScrollPosition,
                GUILayout.Height(100));
            EditorGUILayout.TextArea(_partialUnsetDetailText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // クリップボードにコピーボタン
            if (GUILayout.Button("クリップボードにコピー", GUILayout.Width(150)))
            {
                EditorGUIUtility.systemCopyBuffer = _partialUnsetDetailText;
                Debug.Log("[VertexIdTool] クリップボードにコピーしました");
            }

            EditorGUILayout.Space(5);

            // 未設定部分へのID割り振り
            EditorGUILayout.LabelField("未設定部分へのID割り振り", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("開始ID", GUILayout.Width(50));
                _partialUnsetBaseId = EditorGUILayout.IntField(_partialUnsetBaseId, GUILayout.Width(80));

                if (GUILayout.Button("未設定部分へID割り振り"))
                {
                    AssignIdsToPartialUnset(partialUnsetObjects[_selectedPartialUnsetIndex]);
                }
            }

            // 再診断ボタン
            EditorGUILayout.Space(5);
            if (GUILayout.Button("再診断"))
            {
                RunDiagnosis();
            }
        }

        /// <summary>
        /// 重複IDの詳細表示
        /// </summary>
        private void DrawDuplicateDetail()
        {
            // 重複があるオブジェクトを抽出
            var duplicateObjects = _diagnostics.Where(d => d.HasDuplicate).ToList();
            if (duplicateObjects.Count == 0)
                return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("重複IDの詳細", EditorStyles.boldLabel);

            // ドロップダウン用の表示名リスト
            var displayNames = duplicateObjects.Select(d => $"{d.Name} ({d.DuplicateIds.Count}件)").ToArray();

            // 選択インデックスの補正と初期化
            if (_selectedDuplicateIndex < 0 || _selectedDuplicateIndex >= duplicateObjects.Count)
            {
                _selectedDuplicateIndex = 0;
                UpdateDuplicateDetailText(duplicateObjects[0]);
            }

            // ドロップダウン
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("オブジェクト", GUILayout.Width(80));
                int newIndex = EditorGUILayout.Popup(_selectedDuplicateIndex, displayNames);
                if (newIndex != _selectedDuplicateIndex)
                {
                    _selectedDuplicateIndex = newIndex;
                    UpdateDuplicateDetailText(duplicateObjects[newIndex]);
                }
            }

            // 詳細テキスト（コピー可能なテキストエリア）
            EditorGUILayout.LabelField("重複ID一覧（コピー可能）", EditorStyles.miniLabel);
            _duplicateScrollPosition = EditorGUILayout.BeginScrollView(
                _duplicateScrollPosition,
                GUILayout.Height(100));
            EditorGUILayout.TextArea(_duplicateDetailText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // クリップボードにコピーボタン
            if (GUILayout.Button("クリップボードにコピー", GUILayout.Width(150)))
            {
                EditorGUIUtility.systemCopyBuffer = _duplicateDetailText;
                Debug.Log("[VertexIdTool] 重複情報をクリップボードにコピーしました");
            }
        }

        /// <summary>
        /// 重複詳細テキストを更新
        /// </summary>
        private void UpdateDuplicateDetailText(ObjectDiagnostic diag)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"オブジェクト: {diag.Name}");
            sb.AppendLine($"重複ID数: {diag.DuplicateIds.Count}");
            sb.AppendLine();

            foreach (var dupId in diag.DuplicateIds.OrderBy(id => id))
            {
                if (diag.DuplicateIdVertices.TryGetValue(dupId, out var vertices))
                {
                    string vertexList = string.Join(", ", vertices);
                    sb.AppendLine($"ID {dupId}: 頂点[{vertexList}]");
                }
            }

            _duplicateDetailText = sb.ToString();
        }

        /// <summary>
        /// 選択したオブジェクトの未設定頂点のみにIDを割り振る
        /// </summary>
        private void AssignIdsToPartialUnset(ObjectDiagnostic diag)
        {
            if (diag.UnsetVertexIndices.Count == 0)
            {
                Debug.Log("[VertexIdTool] 未設定頂点がありません");
                return;
            }

            // 新しいIDを生成して未設定頂点に割り振り
            int currentId = _partialUnsetBaseId;
            foreach (int vertexIndex in diag.UnsetVertexIndices)
            {
                if (vertexIndex >= 0 && vertexIndex < diag.VertexIds.Count)
                {
                    diag.VertexIds[vertexIndex] = currentId;
                    currentId++;
                }
            }

            // MQOに反映
            if (_mqoDocument != null)
            {
                var obj = _mqoDocument.Objects.FirstOrDefault(o => o.Name == diag.Name);
                if (obj != null)
                {
                    ApplyPartialIdsToMQOObject(obj, diag);
                }
            }

            // CSVに反映
            if (_csvData != null && _csvData.ObjectWeights.TryGetValue(diag.Name, out var objWeights))
            {
                foreach (int vertexIndex in diag.UnsetVertexIndices)
                {
                    if (vertexIndex >= 0 && vertexIndex < objWeights.Entries.Count && vertexIndex < diag.VertexIds.Count)
                    {
                        objWeights.Entries[vertexIndex].VertexID = diag.VertexIds[vertexIndex];
                    }
                }
            }

            int assignedCount = diag.UnsetVertexIndices.Count;
            int endId = _partialUnsetBaseId + assignedCount - 1;

            Debug.Log($"[VertexIdTool] {diag.Name}: 未設定頂点 {assignedCount} 件にID {_partialUnsetBaseId}~{endId} を割り振りました");
            EditorUtility.DisplayDialog("完了",
                $"{diag.Name} の未設定頂点 {assignedCount} 件に\nID {_partialUnsetBaseId} ~ {endId} を割り振りました\n\n再診断で結果を確認してください",
                "OK");
        }

        /// <summary>
        /// 一部の頂点IDのみをMQOオブジェクトに反映（既存の特殊面は保持）
        /// </summary>
        private void ApplyPartialIdsToMQOObject(MQOObject obj, ObjectDiagnostic diag)
        {
            // 既存の特殊面からvertexIndex→faceのマップを作成
            var specialFaceMap = new Dictionary<int, MQOFace>();
            var normalFaces = new List<MQOFace>();

            foreach (var face in obj.Faces)
            {
                if (face.IsSpecialFace)
                {
                    int vertexIndex = face.VertexIndices[0];
                    specialFaceMap[vertexIndex] = face;
                }
                else
                {
                    normalFaces.Add(face);
                }
            }

            // 未設定だった頂点に対して特殊面を追加/更新
            foreach (int vertexIndex in diag.UnsetVertexIndices)
            {
                if (vertexIndex >= 0 && vertexIndex < diag.VertexIds.Count)
                {
                    int newId = diag.VertexIds[vertexIndex];
                    var specialFace = VertexIdHelper.CreateSpecialFaceForVertexId(vertexIndex, newId);
                    specialFaceMap[vertexIndex] = specialFace;
                }
            }

            // 面リストを再構築
            obj.Faces.Clear();
            obj.Faces.AddRange(normalFaces);
            obj.Faces.AddRange(specialFaceMap.Values);
        }

        /// <summary>
        /// 一部未設定の詳細テキストを更新
        /// </summary>
        private void UpdatePartialUnsetDetailText(ObjectDiagnostic diag)
        {
            var sb = new System.Text.StringBuilder();

            // 未設定頂点インデックス一覧
            sb.AppendLine($"オブジェクト: {diag.Name}");
            sb.AppendLine($"未設定頂点数: {diag.UnsetIdCount} / {diag.VertexCount}");
            sb.AppendLine();
            sb.AppendLine("未設定頂点インデックス:");
            sb.AppendLine(string.Join(", ", diag.UnsetVertexIndices));
            sb.AppendLine();

            // 使用中のID範囲
            if (diag.MinId >= 0 && diag.MaxId >= 0)
            {
                sb.AppendLine($"使用中ID範囲: {diag.MinId} ~ {diag.MaxId}");
            }
            else
            {
                sb.AppendLine("使用中ID範囲: なし");
            }

            _partialUnsetDetailText = sb.ToString();
        }

        // ================================================================
        // ID割り振りセクション
        // ================================================================

        // 重複確認結果
        private string _overlapCheckResult = "";
        private bool _hasOverlap = false;

        private void DrawAssignSection()
        {
            _foldAssign = EditorGUILayout.Foldout(_foldAssign, "ID割り振り設定", true);
            if (!_foldAssign) return;

            EditorGUI.indentLevel++;

            // 一括設定
            EditorGUILayout.LabelField("一括設定", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自動計算（重複なし）"))
                {
                    AutoCalculateBaseIds();
                    _overlapCheckResult = "";  // 確認結果をクリア
                }
                if (GUILayout.Button("0から連番"))
                {
                    SetSequentialBaseIds(0);
                    _overlapCheckResult = "";  // 確認結果をクリア
                }
            }

            EditorGUILayout.Space(5);

            // オブジェクトごとの設定
            EditorGUILayout.LabelField("オブジェクトごとのベースID", EditorStyles.miniLabel);

            foreach (var diag in _diagnostics)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 有効/無効トグル
                    bool prevEnabled = diag.AssignEnabled;
                    diag.AssignEnabled = EditorGUILayout.Toggle(diag.AssignEnabled, GUILayout.Width(20));
                    if (prevEnabled != diag.AssignEnabled)
                        _overlapCheckResult = "";  // 設定変更時に確認結果をクリア

                    // オブジェクト名
                    EditorGUILayout.LabelField(diag.Name, GUILayout.Width(150));

                    // ベースID入力
                    EditorGUI.BeginDisabledGroup(!diag.AssignEnabled);
                    int prevBaseId = diag.BaseId;
                    diag.BaseId = EditorGUILayout.IntField(diag.BaseId, GUILayout.Width(80));
                    if (prevBaseId != diag.BaseId)
                        _overlapCheckResult = "";  // 設定変更時に確認結果をクリア
                    EditorGUI.EndDisabledGroup();

                    // 範囲プレビュー
                    if (diag.AssignEnabled && diag.VertexCount > 0)
                    {
                        int endId = diag.BaseId + diag.VertexCount - 1;
                        EditorGUILayout.LabelField($"→ {diag.BaseId} ~ {endId}", EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.Space(10);

            // 重複確認ボタン
            if (GUILayout.Button("既存IDとの重複を確認"))
            {
                CheckOverlapWithExistingIds();
            }

            // 重複確認結果表示
            if (!string.IsNullOrEmpty(_overlapCheckResult))
            {
                EditorGUILayout.HelpBox(_overlapCheckResult, _hasOverlap ? MessageType.Warning : MessageType.Info);
            }

            EditorGUILayout.Space(5);

            // 割り振り実行ボタン
            bool hasEnabledAssign = _diagnostics.Any(d => d.AssignEnabled && d.VertexCount > 0);
            EditorGUI.BeginDisabledGroup(!hasEnabledAssign);
            if (GUILayout.Button("ID割り振りを実行", GUILayout.Height(30)))
            {
                AssignVertexIds();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 割り振り予定IDと既存IDの重複を確認
        /// </summary>
        private void CheckOverlapWithExistingIds()
        {
            // 既存のID（割り振り対象外のオブジェクトから収集）
            var existingIds = new HashSet<int>();
            foreach (var diag in _diagnostics)
            {
                if (!diag.AssignEnabled)
                {
                    foreach (var id in diag.VertexIds)
                    {
                        if (id >= 0)
                            existingIds.Add(id);
                    }
                }
            }

            // 割り振り予定のID範囲
            var plannedIds = new HashSet<int>();
            var plannedRanges = new List<(string name, int start, int end)>();

            foreach (var diag in _diagnostics)
            {
                if (diag.AssignEnabled && diag.VertexCount > 0)
                {
                    int start = diag.BaseId;
                    int end = diag.BaseId + diag.VertexCount - 1;
                    plannedRanges.Add((diag.Name, start, end));

                    for (int id = start; id <= end; id++)
                    {
                        plannedIds.Add(id);
                    }
                }
            }

            // 既存IDとの重複チェック
            var overlappingWithExisting = plannedIds.Intersect(existingIds).ToList();

            // 割り振り予定同士の重複チェック
            var selfOverlaps = new List<string>();
            for (int i = 0; i < plannedRanges.Count; i++)
            {
                for (int j = i + 1; j < plannedRanges.Count; j++)
                {
                    var a = plannedRanges[i];
                    var b = plannedRanges[j];

                    // 範囲が重なるか
                    if (a.start <= b.end && b.start <= a.end)
                    {
                        int overlapStart = Math.Max(a.start, b.start);
                        int overlapEnd = Math.Min(a.end, b.end);
                        selfOverlaps.Add($"{a.name} と {b.name} ({overlapStart}~{overlapEnd})");
                    }
                }
            }

            // 結果を構築
            var results = new List<string>();

            if (overlappingWithExisting.Count > 0)
            {
                if (overlappingWithExisting.Count <= 10)
                {
                    results.Add($"既存IDと重複: {string.Join(", ", overlappingWithExisting)}");
                }
                else
                {
                    results.Add($"既存IDと重複: {overlappingWithExisting.Count}件 (例: {string.Join(", ", overlappingWithExisting.Take(5))}...)");
                }
            }

            if (selfOverlaps.Count > 0)
            {
                results.Add($"割り振り範囲の重複: {string.Join(", ", selfOverlaps)}");
            }

            if (results.Count == 0)
            {
                _overlapCheckResult = "重複なし - 割り振り可能です";
                _hasOverlap = false;
            }
            else
            {
                _overlapCheckResult = string.Join("\n", results);
                _hasOverlap = true;
            }
        }

        // ================================================================
        // 保存セクション
        // ================================================================

        private void DrawSaveSection()
        {
            EditorGUILayout.LabelField("保存", EditorStyles.boldLabel);

            // MQO保存
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect textFieldRect = EditorGUILayout.GetControlRect();
                _saveMqoPath = EditorGUI.TextField(textFieldRect, "MQO保存先", _saveMqoPath);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_saveMqoPath) ? "" : Path.GetDirectoryName(_saveMqoPath);
                    string defaultName = string.IsNullOrEmpty(_saveMqoPath) ? "output.mqo" : Path.GetFileName(_saveMqoPath);
                    string path = EditorUtility.SaveFilePanel("MQO保存先", dir, defaultName, "mqo");
                    if (!string.IsNullOrEmpty(path))
                        _saveMqoPath = path;
                }
            }

            EditorGUI.BeginDisabledGroup(_mqoDocument == null || string.IsNullOrEmpty(_saveMqoPath));
            if (GUILayout.Button("MQOに保存"))
            {
                SaveToMQO();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // CSV保存
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect textFieldRect = EditorGUILayout.GetControlRect();
                _saveCsvPath = EditorGUI.TextField(textFieldRect, "CSV保存先", _saveCsvPath);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_saveCsvPath) ? "" : Path.GetDirectoryName(_saveCsvPath);
                    string defaultName = string.IsNullOrEmpty(_saveCsvPath) ? "weights.csv" : Path.GetFileName(_saveCsvPath);
                    string path = EditorUtility.SaveFilePanel("CSV保存先", dir, defaultName, "csv");
                    if (!string.IsNullOrEmpty(path))
                        _saveCsvPath = path;
                }
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_saveCsvPath));
            if (GUILayout.Button("CSVに保存"))
            {
                SaveToCSV();
            }
            EditorGUI.EndDisabledGroup();
        }

        // ================================================================
        // ファイル読み込み処理
        // ================================================================

        private void LoadMQO()
        {
            try
            {
                _mqoDocument = MQOParser.ParseFile(_mqoFilePath);
                if (_mqoDocument != null)
                {
                    Debug.Log($"[VertexIdTool] MQO読み込み完了: {_mqoDocument.Objects.Count} オブジェクト");
                    _diagnosed = false;
                    _diagnostics.Clear();

                    // 保存パスのデフォルト設定
                    if (string.IsNullOrEmpty(_saveMqoPath))
                    {
                        string dir = Path.GetDirectoryName(_mqoFilePath);
                        string name = Path.GetFileNameWithoutExtension(_mqoFilePath);
                        _saveMqoPath = Path.Combine(dir, name + "_id.mqo");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VertexIdTool] MQO読み込み失敗: {e.Message}");
                EditorUtility.DisplayDialog("エラー", $"MQO読み込みに失敗しました:\n{e.Message}", "OK");
            }
        }

        private void LoadCSV()
        {
            try
            {
                _csvData = MQOBoneWeightCSVParser.ParseFile(_csvFilePath);
                if (_csvData != null)
                {
                    Debug.Log($"[VertexIdTool] CSV読み込み完了: {_csvData.ObjectWeights.Count} オブジェクト");
                    _diagnosed = false;
                    _diagnostics.Clear();

                    // 保存パスのデフォルト設定
                    if (string.IsNullOrEmpty(_saveCsvPath))
                    {
                        string dir = Path.GetDirectoryName(_csvFilePath);
                        string name = Path.GetFileNameWithoutExtension(_csvFilePath);
                        _saveCsvPath = Path.Combine(dir, name + "_id.csv");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VertexIdTool] CSV読み込み失敗: {e.Message}");
                EditorUtility.DisplayDialog("エラー", $"CSV読み込みに失敗しました:\n{e.Message}", "OK");
            }
        }

        // ================================================================
        // 診断処理
        // ================================================================

        private void RunDiagnosis()
        {
            _diagnostics.Clear();

            // 詳細表示をリセット
            _selectedPartialUnsetIndex = -1;
            _partialUnsetDetailText = "";
            _selectedDuplicateIndex = -1;
            _duplicateDetailText = "";

            // MQOから診断
            if (_mqoDocument != null)
            {
                DiagnoseFromMQO();
            }
            // CSVから診断（MQOがない場合）
            else if (_csvData != null)
            {
                DiagnoseFromCSV();
            }

            _diagnosed = true;
            Debug.Log($"[VertexIdTool] 診断完了: {_diagnostics.Count} オブジェクト");
        }

        private void DiagnoseFromMQO()
        {
            foreach (var obj in _mqoDocument.Objects)
            {
                var diag = new ObjectDiagnostic
                {
                    Name = obj.Name,
                    VertexCount = obj.Vertices.Count
                };

                // 特殊面から頂点IDを抽出
                var idMap = VertexIdHelper.ExtractIdsFromSpecialFaces(obj.Faces);

                // 各頂点のIDを収集
                var ids = new List<int>();
                for (int i = 0; i < obj.Vertices.Count; i++)
                {
                    int id = idMap.TryGetValue(i, out int vid) ? vid : -1;
                    ids.Add(id);
                    diag.VertexIds.Add(id);
                }

                AnalyzeIds(diag, ids);
                _diagnostics.Add(diag);
            }
        }

        private void DiagnoseFromCSV()
        {
            foreach (var kvp in _csvData.ObjectWeights)
            {
                var objWeights = kvp.Value;

                var diag = new ObjectDiagnostic
                {
                    Name = objWeights.ObjectName,
                    VertexCount = objWeights.Entries.Count
                };

                // 各エントリのIDを収集
                var ids = objWeights.Entries.Select(e => e.VertexID).ToList();
                diag.VertexIds.AddRange(ids);

                AnalyzeIds(diag, ids);
                _diagnostics.Add(diag);
            }
        }

        private void AnalyzeIds(ObjectDiagnostic diag, List<int> ids)
        {
            var seenIds = new Dictionary<int, List<int>>();  // ID → 出現した頂点インデックス一覧
            diag.UnsetVertexIndices.Clear();
            diag.DuplicateIdVertices.Clear();

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                if (id < 0)
                {
                    diag.UnsetIdCount++;
                    diag.UnsetVertexIndices.Add(i);  // 未設定頂点のインデックスを記録
                }
                else
                {
                    diag.ValidIdCount++;

                    if (diag.MinId < 0 || id < diag.MinId) diag.MinId = id;
                    if (id > diag.MaxId) diag.MaxId = id;

                    if (!seenIds.ContainsKey(id))
                    {
                        seenIds[id] = new List<int>();
                    }
                    seenIds[id].Add(i);
                }
            }

            // 重複IDを抽出
            foreach (var kvp in seenIds)
            {
                if (kvp.Value.Count > 1)
                {
                    diag.DuplicateIds.Add(kvp.Key);
                    diag.DuplicateIdVertices[kvp.Key] = kvp.Value;
                }
            }

            // 全て同じ値チェック
            if (diag.ValidIdCount > 0 && seenIds.Count == 1)
            {
                diag.AllSameValue = true;
                diag.SameValue = seenIds.Keys.First();
            }

            // ベースIDのデフォルト設定
            diag.BaseId = diag.MinId >= 0 ? diag.MinId : 0;
        }

        // ================================================================
        // ID割り振り処理
        // ================================================================

        private void AutoCalculateBaseIds()
        {
            int currentBase = 0;
            foreach (var diag in _diagnostics)
            {
                if (diag.AssignEnabled)
                {
                    diag.BaseId = currentBase;
                    currentBase += diag.VertexCount;
                }
            }
        }

        private void SetSequentialBaseIds(int startId)
        {
            int currentBase = startId;
            foreach (var diag in _diagnostics)
            {
                if (diag.AssignEnabled)
                {
                    diag.BaseId = currentBase;
                    currentBase += diag.VertexCount;
                }
            }
        }

        private void AssignVertexIds()
        {
            foreach (var diag in _diagnostics)
            {
                if (!diag.AssignEnabled) continue;

                // 新しいIDを生成
                var newIds = VertexIdHelper.GenerateSequentialIds(diag.VertexCount, diag.BaseId);
                diag.VertexIds.Clear();
                diag.VertexIds.AddRange(newIds);

                // MQOに反映
                if (_mqoDocument != null)
                {
                    var obj = _mqoDocument.Objects.FirstOrDefault(o => o.Name == diag.Name);
                    if (obj != null)
                    {
                        ApplyIdsToMQOObject(obj, newIds);
                    }
                }

                // CSVに反映
                if (_csvData != null && _csvData.ObjectWeights.TryGetValue(diag.Name, out var objWeights))
                {
                    for (int i = 0; i < objWeights.Entries.Count && i < newIds.Length; i++)
                    {
                        objWeights.Entries[i].VertexID = newIds[i];
                    }
                }
            }

            // 診断結果を更新
            RunDiagnosis();

            Debug.Log("[VertexIdTool] ID割り振り完了");
            EditorUtility.DisplayDialog("完了", "頂点IDの割り振りが完了しました", "OK");
        }

        private void ApplyIdsToMQOObject(MQOObject obj, int[] newIds)
        {
            // 既存の特殊面を削除
            var normalFaces = obj.Faces.Where(f => !f.IsSpecialFace).ToList();

            // 新しい特殊面を追加
            var specialFaces = new List<MQOFace>();
            for (int i = 0; i < newIds.Length && i < obj.Vertices.Count; i++)
            {
                var specialFace = VertexIdHelper.CreateSpecialFaceForVertexId(i, newIds[i]);
                specialFaces.Add(specialFace);
            }

            // 面リストを更新
            obj.Faces.Clear();
            obj.Faces.AddRange(normalFaces);
            obj.Faces.AddRange(specialFaces);
        }

        // ================================================================
        // 保存処理
        // ================================================================

        private void SaveToMQO()
        {
            if (_mqoDocument == null)
            {
                EditorUtility.DisplayDialog("エラー", "MQOデータがありません", "OK");
                return;
            }

            try
            {
                MQOWriter.WriteToFile(_mqoDocument, _saveMqoPath);
                Debug.Log($"[VertexIdTool] MQO保存完了: {_saveMqoPath}");
                EditorUtility.DisplayDialog("完了", $"MQOファイルを保存しました:\n{_saveMqoPath}", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VertexIdTool] MQO保存失敗: {e.Message}");
                EditorUtility.DisplayDialog("エラー", $"保存に失敗しました:\n{e.Message}", "OK");
            }
        }

        private void SaveToCSV()
        {
            try
            {
                var writer = new CSVWriter();

                // ヘッダー
                writer.AddHeader("MqoObjectName", "VertexID", "VertexIndex",
                    "Bone0", "Bone1", "Bone2", "Bone3",
                    "Weight0", "Weight1", "Weight2", "Weight3");

                // データ行
                foreach (var diag in _diagnostics)
                {
                    // CSVデータがある場合はそれを使用
                    if (_csvData != null && _csvData.ObjectWeights.TryGetValue(diag.Name, out var objWeights))
                    {
                        for (int i = 0; i < objWeights.Entries.Count; i++)
                        {
                            var entry = objWeights.Entries[i];
                            int vertexId = i < diag.VertexIds.Count ? diag.VertexIds[i] : entry.VertexID;

                            writer.AddRow(
                                entry.MqoObjectName,
                                vertexId,
                                entry.VertexIndex,
                                entry.BoneNames[0] ?? "",
                                entry.BoneNames[1] ?? "",
                                entry.BoneNames[2] ?? "",
                                entry.BoneNames[3] ?? "",
                                entry.Weights[0],
                                entry.Weights[1],
                                entry.Weights[2],
                                entry.Weights[3]
                            );
                        }
                    }
                    // CSVデータがない場合はMQOから生成
                    else
                    {
                        for (int i = 0; i < diag.VertexCount; i++)
                        {
                            int vertexId = i < diag.VertexIds.Count ? diag.VertexIds[i] : -1;

                            writer.AddRow(
                                diag.Name,
                                vertexId,
                                i,
                                "", "", "", "",
                                0f, 0f, 0f, 0f
                            );
                        }
                    }
                }

                writer.WriteToFile(_saveCsvPath);
                Debug.Log($"[VertexIdTool] CSV保存完了: {_saveCsvPath}");
                EditorUtility.DisplayDialog("完了", $"CSVファイルを保存しました:\n{_saveCsvPath}", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VertexIdTool] CSV保存失敗: {e.Message}");
                EditorUtility.DisplayDialog("エラー", $"保存に失敗しました:\n{e.Message}", "OK");
            }
        }

        // ================================================================
        // ドラッグ&ドロップ処理
        // ================================================================

        private void HandleDragAndDrop(Rect dropArea, string extension, Action<string> onDropped)
        {
            Event evt = Event.current;

            if (!dropArea.Contains(evt.mousePosition))
                return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    bool hasTarget = DragAndDrop.paths.Any(p =>
                        p.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase));
                    DragAndDrop.visualMode = hasTarget ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    evt.Use();
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
                        {
                            onDropped?.Invoke(path);
                            break;
                        }
                    }
                    evt.Use();
                    break;
            }
        }
    }

    // ================================================================
    // MQO書き出しクラス（簡易版）
    // ================================================================

    /// <summary>
    /// MQOファイル書き出し
    /// </summary>
    public static class MQOWriter
    {
        public static void WriteToFile(MQODocument doc, string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, System.Text.Encoding.GetEncoding("shift_jis")))
            {
                // ヘッダー
                writer.WriteLine("Metasequoia Document");
                writer.WriteLine("Format Text Ver 1.1");
                writer.WriteLine();

                // シーン
                writer.WriteLine("Scene {");
                writer.WriteLine("\tpos 0.0000 0.0000 1500.0000");
                writer.WriteLine("\tlookat 0.0000 0.0000 0.0000");
                writer.WriteLine("\thead -0.5236");
                writer.WriteLine("\tpich 0.5236");
                writer.WriteLine("\tortho 0");
                writer.WriteLine("\tzoom2 5.0000");
                writer.WriteLine("\tamb 0.250 0.250 0.250");
                writer.WriteLine("}");
                writer.WriteLine();

                // マテリアル
                if (doc.Materials.Count > 0)
                {
                    writer.WriteLine($"Material {doc.Materials.Count} {{");
                    foreach (var mat in doc.Materials)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.Append($"\t\"{mat.Name}\"");
                        sb.Append($" col({mat.Color.r:F3} {mat.Color.g:F3} {mat.Color.b:F3} {mat.Color.a:F3})");
                        sb.Append($" dif({mat.Diffuse:F3})");
                        sb.Append($" amb({mat.Ambient:F3})");
                        sb.Append($" emi({mat.Emissive:F3})");
                        sb.Append($" spc({mat.Specular:F3})");
                        sb.Append($" power({mat.Power:F2})");

                        // テクスチャパス（重要！）
                        if (!string.IsNullOrEmpty(mat.TexturePath))
                        {
                            sb.Append($" tex(\"{mat.TexturePath}\")");
                        }

                        // アルファマップパス
                        if (!string.IsNullOrEmpty(mat.AlphaMapPath))
                        {
                            sb.Append($" aplane(\"{mat.AlphaMapPath}\")");
                        }

                        // バンプマップパス
                        if (!string.IsNullOrEmpty(mat.BumpMapPath))
                        {
                            sb.Append($" bump(\"{mat.BumpMapPath}\")");
                        }

                        writer.WriteLine(sb.ToString());
                    }
                    writer.WriteLine("}");
                    writer.WriteLine();
                }

                // オブジェクト
                foreach (var obj in doc.Objects)
                {
                    WriteObject(writer, obj);
                }

                writer.WriteLine("Eof");
            }
        }

        private static void WriteObject(StreamWriter writer, MQOObject obj)
        {
            writer.WriteLine($"Object \"{obj.Name}\" {{");

            // 元の属性をすべて出力（順序を維持）
            foreach (var attr in obj.Attributes)
            {
                if (attr.Values == null || attr.Values.Length == 0)
                {
                    writer.WriteLine($"\t{attr.Name}");
                }
                else if (attr.Values.Length == 1)
                {
                    // 整数値の場合は整数として出力
                    float v = attr.Values[0];
                    if (v == (int)v)
                        writer.WriteLine($"\t{attr.Name} {(int)v}");
                    else
                        writer.WriteLine($"\t{attr.Name} {v:F4}");
                }
                else
                {
                    // 複数値の場合
                    var values = string.Join(" ", attr.Values.Select(v =>
                        v == (int)v ? ((int)v).ToString() : v.ToString("F4")));
                    writer.WriteLine($"\t{attr.Name} {values}");
                }
            }

            // 属性がない場合のデフォルト
            if (obj.Attributes.Count == 0)
            {
                writer.WriteLine($"\tvisible 15");
                writer.WriteLine($"\tlocking 0");
                writer.WriteLine($"\tshading 1");
                writer.WriteLine($"\tfacet 59.5");
                writer.WriteLine($"\tcolor 0.898 0.498 0.698");
                writer.WriteLine($"\tcolor_type 0");
            }

            // 頂点属性（vertexattr）があれば出力
            if (!string.IsNullOrEmpty(obj.VertexAttrRaw))
            {
                writer.WriteLine("\tvertexattr {");
                writer.Write(obj.VertexAttrRaw);
                // VertexAttrRawに閉じ括弧が含まれていなければ追加
                if (!obj.VertexAttrRaw.TrimEnd().EndsWith("}"))
                {
                    writer.WriteLine("\t}");
                }
            }

            // 頂点
            writer.WriteLine($"\tvertex {obj.Vertices.Count} {{");
            foreach (var v in obj.Vertices)
            {
                writer.WriteLine($"\t\t{v.Position.x:F4} {v.Position.y:F4} {v.Position.z:F4}");
            }
            writer.WriteLine("\t}");

            // 面
            var faces = obj.Faces.Where(f => f != null).ToList();
            writer.WriteLine($"\tface {faces.Count} {{");
            foreach (var face in faces)
            {
                WriteFace(writer, face);
            }
            writer.WriteLine("\t}");

            writer.WriteLine("}");
            writer.WriteLine();
        }

        private static void WriteFace(StreamWriter writer, MQOFace face)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"\t\t{face.VertexIndices.Length} V(");
            sb.Append(string.Join(" ", face.VertexIndices));
            sb.Append(")");

            // マテリアル
            if (face.MaterialIndex >= 0)
            {
                sb.Append($" M({face.MaterialIndex})");
            }

            // UV
            if (face.UVs != null && face.UVs.Length > 0)
            {
                sb.Append(" UV(");
                for (int i = 0; i < face.UVs.Length; i++)
                {
                    if (i > 0) sb.Append(" ");
                    sb.Append($"{face.UVs[i].x:F5} {face.UVs[i].y:F5}");
                }
                sb.Append(")");
            }

            // 頂点カラー（特殊面のID格納用）
            if (face.VertexColors != null && face.VertexColors.Length > 0)
            {
                sb.Append(" COL(");
                sb.Append(string.Join(" ", face.VertexColors));
                sb.Append(")");
            }

            writer.WriteLine(sb.ToString());
        }
    }
}