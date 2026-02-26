// Assets/Editor/PolyLing.SelectionSets.cs
// 選択セット管理UI
// メッシュ単位で選択状態を保存・復元
// ファイル保存/読み込み対応

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Localization;

public partial class PolyLing
{
    // ================================================================
    // 選択セットUI設定
    // ================================================================

    private bool _foldSelectionSets = true;
    private Vector2 _selectionSetsScroll;
    private int _selectedSelectionSetIndex = -1;
    private string _newSelectionSetName = "";
    private bool _isRenamingSelectionSet = false;
    private int _renamingSelectionSetIndex = -1;
    private string _renamingName = "";

    // ================================================================
    // 選択セットUI描画
    // ================================================================

    /// <summary>
    /// 選択セットUIを描画
    /// DrawMeshListのSelection Sets セクションで呼び出す
    /// </summary>
    private void DrawSelectionSetsUI()
    {
        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext == null) return;

        _foldSelectionSets = EditorGUILayout.Foldout(_foldSelectionSets, L.Get("SelectionDics"), true);
        if (!_foldSelectionSets) return;

        EditorGUI.indentLevel++;

        // ================================================================
        // 保存ボタン行
        // ================================================================
        EditorGUILayout.BeginHorizontal();

        // 新規名前入力
        _newSelectionSetName = EditorGUILayout.TextField(_newSelectionSetName, GUILayout.MinWidth(80));

        // 保存ボタン（グローバル選択状態をチェック）
        bool hasSelection = _selectionState.Vertices.Count > 0 || _selectionState.HasSelection;

        using (new EditorGUI.DisabledScope(!hasSelection))
        {
            if (GUILayout.Button(L.Get("HashingSelection"), GUILayout.Width(80)))
            {
                SaveCurrentSelectionAsSet(meshContext);
            }
        }

        EditorGUILayout.EndHorizontal();

        // ================================================================
        // 選択セットリスト
        // ================================================================
        if (meshContext.PartsSelectionSetList.Count > 0)
        {
            EditorGUILayout.Space(3);

            // スクロールビュー
            float listHeight = Mathf.Min(meshContext.PartsSelectionSetList.Count * 22f + 10f, 150f);
            _selectionSetsScroll = EditorGUILayout.BeginScrollView(
                _selectionSetsScroll,
                GUILayout.Height(listHeight));

            for (int i = 0; i < meshContext.PartsSelectionSetList.Count; i++)
            {
                var set = meshContext.PartsSelectionSetList[i];
                DrawSelectionSetItem(meshContext, set, i);
            }

            EditorGUILayout.EndScrollView();

            // ================================================================
            // 操作ボタン行（Load/Add/Sub/Delete）
            // ================================================================
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_selectedSelectionSetIndex < 0))
            {
                if (GUILayout.Button(L.Get("To Current"), GUILayout.Width(45)))
                {
                    LoadSelectedPartsSelectionSet(meshContext);
                }
                if (GUILayout.Button(L.Get("Add"), GUILayout.Width(35)))
                {
                    AddSelectedPartsSelectionSet(meshContext);
                }
                if (GUILayout.Button(L.Get("Subtract"), GUILayout.Width(45)))
                {
                    SubtractSelectedPartsSelectionSet(meshContext);
                }
                if (GUILayout.Button(L.Get("Delete"), GUILayout.Width(45)))
                {
                    DeleteSelectedPartsSelectionSet(meshContext);
                }
            }

            EditorGUILayout.EndHorizontal();

            // ================================================================
            // ファイル入出力ボタン行（JSON/CSV）
            // ================================================================
            EditorGUILayout.BeginHorizontal();

            // JSON書出
            if (GUILayout.Button("JSON↓", GUILayout.Width(50)))
            {
                SaveSelectionSetsToFile(meshContext);
            }
            // JSON読込
            if (GUILayout.Button("JSON↑", GUILayout.Width(50)))
            {
                LoadSelectionSetsFromFile(meshContext);
            }
            // CSV書出
            if (GUILayout.Button("CSV↓", GUILayout.Width(45)))
            {
                ExportSelectionSetsToCSV(meshContext);
            }
            // CSV読込
            if (GUILayout.Button("CSV↑", GUILayout.Width(45)))
            {
                ImportSelectionSetFromCSV(meshContext);
            }

            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox(L.Get("NoSelectionSets"), MessageType.Info);

            // セットがなくてもファイル読み込みは可能
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("JSON↑", GUILayout.Width(50)))
            {
                LoadSelectionSetsFromFile(meshContext);
            }
            if (GUILayout.Button("CSV↑", GUILayout.Width(45)))
            {
                ImportSelectionSetFromCSV(meshContext);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 選択セットアイテムを描画
    /// </summary>
    private void DrawSelectionSetItem(MeshContext meshContext, PartsSelectionSet set, int index)
    {
        bool isSelected = (_selectedSelectionSetIndex == index);

        EditorGUILayout.BeginHorizontal();

        // 選択ラジオボタン風トグル
        bool newSelected = GUILayout.Toggle(isSelected, "", GUILayout.Width(16));
        if (newSelected != isSelected)
        {
            _selectedSelectionSetIndex = newSelected ? index : -1;
        }

        // リネーム中
        if (_isRenamingSelectionSet && _renamingSelectionSetIndex == index)
        {
            _renamingName = EditorGUILayout.TextField(_renamingName, GUILayout.MinWidth(80));

            if (GUILayout.Button("✓", GUILayout.Width(22)))
            {
                if (!string.IsNullOrEmpty(_renamingName))
                {
                    set.Name = _renamingName;
                }
                _isRenamingSelectionSet = false;
                _renamingSelectionSetIndex = -1;
            }

            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                _isRenamingSelectionSet = false;
                _renamingSelectionSetIndex = -1;
            }
        }
        else
        {
            // 名前ラベル（ダブルクリックでリネーム）
            var labelStyle = isSelected ? EditorStyles.boldLabel : EditorStyles.label;

            // モードアイコン
            string modeIcon = GetModeIcon(set.Mode);

            if (GUILayout.Button($"{modeIcon} {set.Name}", labelStyle, GUILayout.MinWidth(80)))
            {
                // シングルクリック：選択
                _selectedSelectionSetIndex = index;

                // ダブルクリック判定
                if (Event.current.clickCount == 2)
                {
                    // ダブルクリック：ロード
                    LoadSelectionSetAtIndex(meshContext, index);
                }
            }

            // サマリー
            GUILayout.Label(set.Summary, EditorStyles.miniLabel, GUILayout.Width(70));

            // リネームボタン
            if (GUILayout.Button("✎", GUILayout.Width(22)))
            {
                _isRenamingSelectionSet = true;
                _renamingSelectionSetIndex = index;
                _renamingName = set.Name;
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// モードアイコンを取得
    /// </summary>
    private string GetModeIcon(MeshSelectMode mode)
    {
        return mode switch
        {
            MeshSelectMode.Vertex => "●",
            MeshSelectMode.Edge => "━",
            MeshSelectMode.Face => "■",
            MeshSelectMode.Line => "╱",
            _ => "○"
        };
    }

    // ================================================================
    // 選択セット操作
    // ================================================================

    /// <summary>
    /// 現在の選択をセットとして保存
    /// </summary>
    private void SaveCurrentSelectionAsSet(MeshContext meshContext)
    {
        // 名前を決定
        string name = string.IsNullOrEmpty(_newSelectionSetName)
            ? meshContext.GenerateUniqueSelectionSetName("Selection")
            : _newSelectionSetName;

        // 重複チェック
        if (meshContext.FindSelectionSetByName(name) != null)
        {
            name = meshContext.GenerateUniqueSelectionSetName(name);
        }

        // グローバル選択状態から保存
        var set = PartsSelectionSet.FromCurrentSelection(
            name,
            _selectionState.Vertices,
            _selectionState?.Edges,
            _selectionState?.Faces,
            _selectionState?.Lines,
            _selectionState?.Mode ?? MeshSelectMode.Vertex
        );

        meshContext.PartsSelectionSetList.Add(set);

        // 入力欄クリア
        _newSelectionSetName = "";

        // 新しいセットを選択
        _selectedSelectionSetIndex = meshContext.PartsSelectionSetList.Count - 1;

        Debug.Log($"[SelectionSets] Saved: {name} ({set.Summary})");
        Repaint();
    }

    /// <summary>
    /// 選択中のセットをロード
    /// </summary>
    private void LoadSelectedPartsSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.PartsSelectionSetList.Count)
            return;

        LoadSelectionSetAtIndex(meshContext, _selectedSelectionSetIndex);
    }

    /// <summary>
    /// 指定インデックスのセットをロード
    /// </summary>
    private void LoadSelectionSetAtIndex(MeshContext meshContext, int index)
    {
        if (index < 0 || index >= meshContext.PartsSelectionSetList.Count)
            return;

        var set = meshContext.PartsSelectionSetList[index];

        // グローバル選択状態に直接復元
        _selectionState.Vertices.Clear();
        _selectionState.Vertices.UnionWith(set.Vertices);

        if (_selectionState != null)
        {
            _selectionState.Edges.Clear();
            _selectionState.Edges.UnionWith(set.Edges);
            _selectionState.Faces.Clear();
            _selectionState.Faces.UnionWith(set.Faces);
            _selectionState.Lines.Clear();
            _selectionState.Lines.UnionWith(set.Lines);
            _selectionState.Mode = set.Mode;
        }

        Debug.Log($"[SelectionSets] Loaded: {set.Name}");
        _unifiedAdapter?.RequestNormal();
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを現在の選択に追加
    /// </summary>
    private void AddSelectedPartsSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.PartsSelectionSetList.Count)
            return;

        var set = meshContext.PartsSelectionSetList[_selectedSelectionSetIndex];

        // グローバル選択状態に直接追加
        if (set.Vertices != null && set.Vertices.Count > 0)
        {
            _selectionState.Vertices.UnionWith(set.Vertices);
        }
        if (_selectionState != null)
        {
            _selectionState.Edges.UnionWith(set.Edges);
            _selectionState.Faces.UnionWith(set.Faces);
            _selectionState.Lines.UnionWith(set.Lines);
        }

        Debug.Log($"[SelectionSets] Added: {set.Name}");
        _unifiedAdapter?.RequestNormal();
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを現在の選択から除外
    /// </summary>
    private void SubtractSelectedPartsSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.PartsSelectionSetList.Count)
            return;

        var set = meshContext.PartsSelectionSetList[_selectedSelectionSetIndex];

        // グローバル選択状態から直接除外
        if (set.Vertices != null && set.Vertices.Count > 0)
        {
            _selectionState.Vertices.ExceptWith(set.Vertices);
        }
        if (_selectionState != null)
        {
            _selectionState.Edges.ExceptWith(set.Edges);
            _selectionState.Faces.ExceptWith(set.Faces);
            _selectionState.Lines.ExceptWith(set.Lines);
        }

        Debug.Log($"[SelectionSets] Subtracted: {set.Name}");
        _unifiedAdapter?.RequestNormal();
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを削除
    /// </summary>
    private void DeleteSelectedPartsSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.PartsSelectionSetList.Count)
            return;

        var set = meshContext.PartsSelectionSetList[_selectedSelectionSetIndex];
        string name = set.Name;

        if (EditorUtility.DisplayDialog(
            L.Get("DeleteSelectionSet"),
            string.Format(L.Get("DeleteSelectionSetConfirm"), name),
            L.Get("Delete"),
            L.Get("Cancel")))
        {
            meshContext.RemoveSelectionSet(set);
            _selectedSelectionSetIndex = -1;

            Debug.Log($"[SelectionSets] Deleted: {name}");
            Repaint();
        }
        
        GUIUtility.ExitGUI();
    }

    // ================================================================
    // ファイル保存/読み込み
    // ================================================================

    /// <summary>
    /// 選択セット群をJSONファイルに保存
    /// </summary>
    private void SaveSelectionSetsToFile(MeshContext meshContext)
    {
        if (meshContext.PartsSelectionSetList.Count == 0)
        {
            Debug.LogWarning("[SelectionSets] No selection sets to save.");
            return;
        }

        string defaultName = $"{meshContext.Name}_SelectionSets";
        string path = EditorUtility.SaveFilePanel(
            "Save Selection Sets",
            Application.dataPath,
            defaultName,
            "json"
        );

        if (string.IsNullOrEmpty(path))
        {
            GUIUtility.ExitGUI();
            return;
        }

        try
        {
            var fileData = new SelectionSetsFileData
            {
                meshName = meshContext.Name,
                savedAt = DateTime.Now.ToString("o"),
                sets = new List<SelectionSetDTO>()
            };

            foreach (var set in meshContext.PartsSelectionSetList)
            {
                var dto = SelectionSetDTO.FromSelectionSet(set);
                if (dto != null)
                {
                    fileData.sets.Add(dto);
                }
            }

            string json = JsonUtility.ToJson(fileData, true);
            File.WriteAllText(path, json);

            Debug.Log($"[SelectionSets] Saved {fileData.sets.Count} sets to: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelectionSets] Failed to save: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to save selection sets:\n{ex.Message}", "OK");
        }
        
        GUIUtility.ExitGUI();
    }

    /// <summary>
    /// JSONファイルから選択セット群を読み込み
    /// </summary>
    private void LoadSelectionSetsFromFile(MeshContext meshContext)
    {
        string path = EditorUtility.OpenFilePanel(
            "Load Selection Sets",
            Application.dataPath,
            "json"
        );

        if (string.IsNullOrEmpty(path))
        {
            GUIUtility.ExitGUI();
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            var fileData = JsonUtility.FromJson<SelectionSetsFileData>(json);

            if (fileData == null || fileData.sets == null || fileData.sets.Count == 0)
            {
                Debug.LogWarning("[SelectionSets] No valid sets found in file.");
                GUIUtility.ExitGUI();
                return;
            }

            // 既存セットとのマージ確認
            bool replace = false;
            if (meshContext.PartsSelectionSetList.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Load Selection Sets",
                    $"Found {fileData.sets.Count} sets in file.\nCurrent mesh has {meshContext.PartsSelectionSetList.Count} sets.\n\nHow do you want to load?",
                    "Replace All",
                    "Cancel",
                    "Merge (Add)"
                );

                if (choice == 1) // Cancel
                {
                    GUIUtility.ExitGUI();
                    return;
                }

                replace = (choice == 0);
            }

            if (replace)
            {
                meshContext.PartsSelectionSetList.Clear();
            }

            int loadedCount = 0;
            foreach (var dto in fileData.sets)
            {
                var set = dto?.ToSelectionSet();
                if (set != null)
                {
                    // 重複名チェック
                    if (meshContext.FindSelectionSetByName(set.Name) != null)
                    {
                        set.Name = meshContext.GenerateUniqueSelectionSetName(set.Name);
                    }
                    meshContext.PartsSelectionSetList.Add(set);
                    loadedCount++;
                }
            }

            _selectedSelectionSetIndex = -1;
            Debug.Log($"[SelectionSets] Loaded {loadedCount} sets from: {path}");
            Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelectionSets] Failed to load: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to load selection sets:\n{ex.Message}", "OK");
        }
        
        GUIUtility.ExitGUI();
    }

    // ================================================================
    // 名前で選択セットを呼び出し（スクリプトAPI）
    // ================================================================

    /// <summary>
    /// 名前を指定して選択セットをロード
    /// </summary>
    /// <param name="setName">セット名</param>
    /// <returns>成功したらtrue</returns>
    public bool LoadSelectionSetByName(string setName)
    {
        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext == null)
        {
            Debug.LogWarning("[SelectionSets] No current mesh context.");
            return false;
        }

        var set = meshContext.FindSelectionSetByName(setName);
        if (set == null)
        {
            Debug.LogWarning($"[SelectionSets] Set not found: {setName}");
            return false;
        }

        // グローバル選択状態に復元
        _selectionState.Vertices.Clear();
        _selectionState.Vertices.UnionWith(set.Vertices);

        if (_selectionState != null)
        {
            _selectionState.Edges.Clear();
            _selectionState.Edges.UnionWith(set.Edges);
            _selectionState.Faces.Clear();
            _selectionState.Faces.UnionWith(set.Faces);
            _selectionState.Lines.Clear();
            _selectionState.Lines.UnionWith(set.Lines);
            _selectionState.Mode = set.Mode;
        }

        Debug.Log($"[SelectionSets] Loaded by name: {setName}");
        _unifiedAdapter?.RequestNormal();
        Repaint();
        return true;
    }

    /// <summary>
    /// 選択セット名一覧を取得
    /// </summary>
    public List<string> GetSelectionSetNames()
    {
        var meshContext = _model?.FirstSelectedMeshContext;
        if (meshContext == null)
            return new List<string>();

        var names = new List<string>();
        foreach (var set in meshContext.PartsSelectionSetList)
        {
            names.Add(set.Name);
        }
        return names;
    }

    // ================================================================
    // ファイルデータ構造
    // ================================================================

    /// <summary>
    /// 選択セットファイルのルートデータ
    /// </summary>
    [Serializable]
    private class SelectionSetsFileData
    {
        public string meshName;
        public string savedAt;
        public List<SelectionSetDTO> sets;
    }

    // ================================================================
    // CSV エクスポート/インポート
    // ================================================================

    /// <summary>
    /// CSV種別
    /// </summary>
    private enum CSVDataType
    {
        Vertex,
        VertexId,
        Edge,
        Face,
        Line
    }

    /// <summary>
    /// 選択セット群をCSVフォルダにエクスポート
    /// </summary>
    private void ExportSelectionSetsToCSV(MeshContext meshContext)
    {
        if (meshContext.PartsSelectionSetList.Count == 0)
        {
            Debug.LogWarning("[SelectionSets] No selection sets to export.");
            return;
        }

        // フォルダ選択
        string folderPath = EditorUtility.SaveFolderPanel(
            "Select Folder for CSV Export",
            Application.dataPath,
            $"SelectionSets_{meshContext.Name}"
        );

        if (string.IsNullOrEmpty(folderPath))
        {
            GUIUtility.ExitGUI();
            return;
        }

        try
        {
            // フォルダが存在しなければ作成
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            int exportedCount = 0;
            foreach (var set in meshContext.PartsSelectionSetList)
            {
                // ファイル名: Selected_セット名.csv
                string safeName = SanitizeFileName(set.Name);
                string fileName = $"Selected_{safeName}.csv";
                string filePath = Path.Combine(folderPath, fileName);

                // 種別とデータを決定
                //CSVDataType dataType;
                List<string> lines = new List<string>();

                // ヘッダー
                lines.Add($"# {meshContext.Name}");

                if (set.Vertices.Count > 0)
                {
                    // 頂点IDがあるかチェック（MeshObjectから取得）
                    bool hasVertexIds = HasValidVertexIds(meshContext, set.Vertices);
                    
                    if (hasVertexIds)
                    {
                        //dataType = CSVDataType.VertexId;
                        lines.Add("# vertexId");
                        foreach (int vIdx in set.Vertices)
                        {
                            int id = GetVertexId(meshContext, vIdx);
                            lines.Add(id.ToString());
                        }
                    }
                    else
                    {
                        //dataType = CSVDataType.Vertex;
                        lines.Add("# vertex");
                        foreach (int vIdx in set.Vertices)
                        {
                            lines.Add(vIdx.ToString());
                        }
                    }
                }
                else if (set.Edges.Count > 0)
                {
                    //dataType = CSVDataType.Edge;
                    lines.Add("# edge");
                    foreach (var edge in set.Edges)
                    {
                        lines.Add($"{edge.V1},{edge.V2}");
                    }
                }
                else if (set.Faces.Count > 0)
                {
                    //dataType = CSVDataType.Face;
                    lines.Add("# face");
                    foreach (int fIdx in set.Faces)
                    {
                        lines.Add(fIdx.ToString());
                    }
                }
                else if (set.Lines.Count > 0)
                {
                    //dataType = CSVDataType.Line;
                    lines.Add("# line");
                    foreach (int lIdx in set.Lines)
                    {
                        lines.Add(lIdx.ToString());
                    }
                }
                else
                {
                    // 空のセットはスキップ
                    continue;
                }

                File.WriteAllLines(filePath, lines);
                exportedCount++;
                Debug.Log($"[SelectionSets] Exported: {filePath}");
            }

            Debug.Log($"[SelectionSets] Exported {exportedCount} sets to: {folderPath}");
            EditorUtility.DisplayDialog("Export Complete", $"Exported {exportedCount} selection sets to:\n{folderPath}", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelectionSets] CSV export failed: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to export:\n{ex.Message}", "OK");
        }
        
        GUIUtility.ExitGUI();
    }

    /// <summary>
    /// CSVファイルから選択セットをインポート
    /// </summary>
    private void ImportSelectionSetFromCSV(MeshContext meshContext)
    {
        string filePath = EditorUtility.OpenFilePanel(
            "Import Selection Set CSV",
            Application.dataPath,
            "csv"
        );

        if (string.IsNullOrEmpty(filePath))
        {
            GUIUtility.ExitGUI();
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
            {
                Debug.LogWarning("[SelectionSets] CSV file is empty or invalid.");
                return;
            }

            // ファイル名からセット名を取得
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string setName = fileName;
            // "Selected_" プレフィックスがあればそのまま使用
            
            // ヘッダー解析
            string meshName = "";
            CSVDataType dataType = CSVDataType.Vertex;
            var numbers = new List<int>();
            var edges = new List<VertexPair>();

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                
                // 空行スキップ
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // コメント行
                if (trimmed.StartsWith("#"))
                {
                    string comment = trimmed.Substring(1).Trim();
                    
                    // 種別判定
                    if (comment.Equals("vertex", StringComparison.OrdinalIgnoreCase))
                        dataType = CSVDataType.Vertex;
                    else if (comment.Equals("vertexId", StringComparison.OrdinalIgnoreCase))
                        dataType = CSVDataType.VertexId;
                    else if (comment.Equals("edge", StringComparison.OrdinalIgnoreCase))
                        dataType = CSVDataType.Edge;
                    else if (comment.Equals("face", StringComparison.OrdinalIgnoreCase))
                        dataType = CSVDataType.Face;
                    else if (comment.Equals("line", StringComparison.OrdinalIgnoreCase))
                        dataType = CSVDataType.Line;
                    else if (string.IsNullOrEmpty(meshName))
                        meshName = comment; // 最初のコメントはメッシュ名
                    
                    continue;
                }

                // データ行
                if (dataType == CSVDataType.Edge)
                {
                    // エッジ: v1,v2
                    string[] parts = trimmed.Split(',');
                    if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out int v1) && int.TryParse(parts[1].Trim(), out int v2))
                    {
                        edges.Add(new VertexPair(v1, v2));
                    }
                }
                else
                {
                    // 単一数値
                    if (int.TryParse(trimmed, out int num))
                    {
                        numbers.Add(num);
                    }
                }
            }

            // SelectionSet作成
            var set = new PartsSelectionSet(setName);
            set.Mode = DataTypeToMode(dataType);

            switch (dataType)
            {
                case CSVDataType.Vertex:
                    set.Vertices = new HashSet<int>(numbers);
                    break;
                    
                case CSVDataType.VertexId:
                    // VertexIdからインデックスに変換
                    var indices = ConvertVertexIdsToIndices(meshContext, numbers);
                    set.Vertices = new HashSet<int>(indices);
                    if (indices.Count < numbers.Count)
                    {
                        Debug.LogWarning($"[SelectionSets] {numbers.Count - indices.Count} vertex IDs not found in current mesh.");
                    }
                    break;
                    
                case CSVDataType.Edge:
                    set.Edges = new HashSet<VertexPair>(edges);
                    break;
                    
                case CSVDataType.Face:
                    set.Faces = new HashSet<int>(numbers);
                    break;
                    
                case CSVDataType.Line:
                    set.Lines = new HashSet<int>(numbers);
                    break;
            }

            // 重複名チェック
            if (meshContext.FindSelectionSetByName(set.Name) != null)
            {
                set.Name = meshContext.GenerateUniqueSelectionSetName(set.Name);
            }

            meshContext.PartsSelectionSetList.Add(set);
            _selectedSelectionSetIndex = meshContext.PartsSelectionSetList.Count - 1;

            Debug.Log($"[SelectionSets] Imported: {set.Name} ({set.Summary})");
            Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelectionSets] CSV import failed: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to import:\n{ex.Message}", "OK");
        }
        
        GUIUtility.ExitGUI();
    }

    /// <summary>
    /// 頂点に有効なIDがあるかチェック
    /// </summary>
    private bool HasValidVertexIds(MeshContext meshContext, HashSet<int> vertexIndices)
    {
        if (meshContext?.MeshObject == null)
            return false;

        foreach (int idx in vertexIndices)
        {
            if (idx >= 0 && idx < meshContext.MeshObject.VertexCount)
            {
                // ID が 0 でない（デフォルト値でない）ものがあれば有効とみなす
                if (meshContext.MeshObject.Vertices[idx].Id != 0)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 頂点インデックスからIDを取得
    /// </summary>
    private int GetVertexId(MeshContext meshContext, int index)
    {
        if (meshContext?.MeshObject == null)
            return index;
        if (index < 0 || index >= meshContext.MeshObject.VertexCount)
            return index;
        return meshContext.MeshObject.Vertices[index].Id;
    }

    /// <summary>
    /// 頂点IDリストをインデックスリストに変換
    /// </summary>
    private List<int> ConvertVertexIdsToIndices(MeshContext meshContext, List<int> ids)
    {
        var result = new List<int>();
        if (meshContext?.MeshObject == null)
            return result;

        // ID→インデックスの辞書を構築
        var idToIndex = new Dictionary<int, int>();
        for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
        {
            int id = meshContext.MeshObject.Vertices[i].Id;
            if (!idToIndex.ContainsKey(id))
            {
                idToIndex[id] = i;
            }
        }

        foreach (int id in ids)
        {
            if (idToIndex.TryGetValue(id, out int index))
            {
                result.Add(index);
            }
        }

        return result;
    }

    /// <summary>
    /// CSVデータ種別から選択モードに変換
    /// </summary>
    private MeshSelectMode DataTypeToMode(CSVDataType dataType)
    {
        return dataType switch
        {
            CSVDataType.Vertex => MeshSelectMode.Vertex,
            CSVDataType.VertexId => MeshSelectMode.Vertex,
            CSVDataType.Edge => MeshSelectMode.Edge,
            CSVDataType.Face => MeshSelectMode.Face,
            CSVDataType.Line => MeshSelectMode.Line,
            _ => MeshSelectMode.Vertex
        };
    }
}