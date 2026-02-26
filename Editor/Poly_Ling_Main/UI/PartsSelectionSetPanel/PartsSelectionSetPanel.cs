// Assets/Editor/Poly_Ling_Main/UI/PartsSelectionSetPanel/PartsSelectionSetPanel.cs
// パーツ選択辞書パネル（頂点・辺・面・線分の選択保存/復元）
// メッシュ選択辞書(MeshSelectionSetPanel)とは別物
// IToolPanelBase継承の独立ウィンドウ

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Localization;

namespace Poly_Ling.UI
{
    public class PartsSelectionSetPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "PartsSelectionSetPanel";
        public override string Title => "Parts Selection Dictionary";
        public override string GetLocalizedTitle() => L.Get("PartsSelectionDicPanel");

        // ================================================================
        // UI状態
        // ================================================================

        private Vector2 _scrollPos;
        private int _selectedIndex = -1;
        private string _newSetName = "";
        private bool _isRenaming = false;
        private int _renamingIndex = -1;
        private string _renamingName = "";

        // イベント購読管理
        private SelectionState _subscribedState;

        // ================================================================
        // ウィンドウ
        // ================================================================

        public static PartsSelectionSetPanel Open(ToolContext ctx)
        {
            var window = GetWindow<PartsSelectionSetPanel>();
            window.titleContent = new GUIContent(L.Get("PartsSelectionDicPanel"));
            window.minSize = new Vector2(300, 280);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // コンテキスト変更
        // ================================================================

        protected override void OnContextSet()
        {
            _selectedIndex = -1;
            _isRenaming = false;
            SubscribeSelectionState();
        }

        protected override void OnDestroy()
        {
            UnsubscribeSelectionState();
            base.OnDestroy();
        }

        private void SubscribeSelectionState()
        {
            UnsubscribeSelectionState();
            var sel = _context?.SelectionState;
            if (sel != null)
            {
                sel.OnSelectionChanged += OnSelectionChangedExternal;
                _subscribedState = sel;
            }
        }

        private void UnsubscribeSelectionState()
        {
            if (_subscribedState != null)
            {
                _subscribedState.OnSelectionChanged -= OnSelectionChangedExternal;
                _subscribedState = null;
            }
        }

        private void OnSelectionChangedExternal()
        {
            Repaint();
        }

        // ================================================================
        // 描画
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning()) return;

            var meshContext = FirstSelectedMeshContext;
            if (meshContext == null)
            {
                EditorGUILayout.HelpBox(L.Get("NoMeshSelected"), MessageType.Warning);
                return;
            }

            // ヘッダー：パネル名 + 対象メッシュ
            EditorGUILayout.LabelField(
                $"📐 {L.Get("PartsSelectionDicPanel")}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{L.Get("TargetMesh")}: {meshContext.Name}",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(2);

            // 現在の選択状態をリアルタイム表示
            DrawCurrentSelectionInfo();
            EditorGUILayout.Space(3);

            DrawSaveRow(meshContext);
            EditorGUILayout.Space(3);
            DrawSetList(meshContext);
            DrawOperationButtons(meshContext);
            DrawFileIOButtons(meshContext);
        }

        // ================================================================
        // 現在の選択状態表示（リアルタイム）
        // ================================================================

        private void DrawCurrentSelectionInfo()
        {
            var sel = _context?.SelectionState;
            if (sel == null) return;

            var parts = new List<string>();
            if (sel.Vertices.Count > 0) parts.Add($"V:{sel.Vertices.Count}");
            if (sel.Edges.Count > 0) parts.Add($"E:{sel.Edges.Count}");
            if (sel.Faces.Count > 0) parts.Add($"F:{sel.Faces.Count}");
            if (sel.Lines.Count > 0) parts.Add($"L:{sel.Lines.Count}");

            string info = parts.Count > 0
                ? string.Join("  ", parts)
                : L.Get("NoSelection");

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{L.Get("CurrentSelection")}: {info}");
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 保存行
        // ================================================================

        private void DrawSaveRow(MeshContext meshContext)
        {
            EditorGUILayout.BeginHorizontal();

            _newSetName = EditorGUILayout.TextField(_newSetName, GUILayout.MinWidth(80));

            var sel = _context.SelectionState;
            bool hasSelection = sel != null && sel.HasAnySelection;

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button(L.Get("HashingSelection"), GUILayout.Width(80)))
                {
                    SaveCurrentSelection(meshContext);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // セットリスト
        // ================================================================

        private void DrawSetList(MeshContext meshContext)
        {
            var sets = meshContext.PartsSelectionSetList;
            if (sets.Count == 0)
            {
                EditorGUILayout.HelpBox(L.Get("NoSelectionSets"), MessageType.Info);
                return;
            }

            if (_selectedIndex >= sets.Count)
                _selectedIndex = -1;

            float listHeight = Mathf.Min(sets.Count * 22f + 10f, 200f);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(listHeight));

            for (int i = 0; i < sets.Count; i++)
            {
                DrawSetItem(meshContext, sets[i], i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSetItem(MeshContext meshContext, PartsSelectionSet set, int index)
        {
            bool isSelected = (_selectedIndex == index);

            EditorGUILayout.BeginHorizontal();

            bool newSelected = GUILayout.Toggle(isSelected, "", GUILayout.Width(16));
            if (newSelected != isSelected)
                _selectedIndex = newSelected ? index : -1;

            if (_isRenaming && _renamingIndex == index)
            {
                _renamingName = EditorGUILayout.TextField(_renamingName, GUILayout.MinWidth(80));
                if (GUILayout.Button("✓", GUILayout.Width(22)))
                {
                    if (!string.IsNullOrEmpty(_renamingName))
                        set.Name = _renamingName;
                    _isRenaming = false;
                    _renamingIndex = -1;
                }
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    _isRenaming = false;
                    _renamingIndex = -1;
                }
            }
            else
            {
                var labelStyle = isSelected ? EditorStyles.boldLabel : EditorStyles.label;
                string modeIcon = GetModeIcon(set.Mode);

                if (GUILayout.Button($"{modeIcon} {set.Name}", labelStyle, GUILayout.MinWidth(80)))
                {
                    _selectedIndex = index;
                    if (Event.current.clickCount == 2)
                        LoadSetAtIndex(meshContext, index);
                }

                GUILayout.Label(set.Summary, EditorStyles.miniLabel, GUILayout.Width(70));

                if (GUILayout.Button("✎", GUILayout.Width(22)))
                {
                    _isRenaming = true;
                    _renamingIndex = index;
                    _renamingName = set.Name;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // 操作ボタン（呼出し / 追加 / 除外 / 削除）
        // ================================================================

        private void DrawOperationButtons(MeshContext meshContext)
        {
            if (meshContext.PartsSelectionSetList.Count == 0) return;

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(_selectedIndex < 0))
            {
                if (GUILayout.Button(L.Get("To Current"), GUILayout.Width(45)))
                    LoadSelectedSet(meshContext);
                if (GUILayout.Button(L.Get("Add"), GUILayout.Width(35)))
                    AddSelectedSet(meshContext);
                if (GUILayout.Button(L.Get("Subtract"), GUILayout.Width(45)))
                    SubtractSelectedSet(meshContext);
                if (GUILayout.Button(L.Get("Delete"), GUILayout.Width(45)))
                    DeleteSelectedSet(meshContext);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // ファイルI/Oボタン（CSV辞書のみ）
        // ================================================================

        private void DrawFileIOButtons(MeshContext meshContext)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();

            bool hasSets = meshContext.PartsSelectionSetList.Count > 0;

            using (new EditorGUI.DisabledScope(!hasSets))
            {
                if (GUILayout.Button(L.Get("SaveDicFile")))
                    ExportSetsToCSV(meshContext);
            }
            if (GUILayout.Button(L.Get("OpenDicFile")))
                ImportSetFromCSV(meshContext);

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // アイコン
        // ================================================================

        private static string GetModeIcon(MeshSelectMode mode)
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

        private void SaveCurrentSelection(MeshContext meshContext)
        {
            var sel = _context.SelectionState;
            if (sel == null) return;

            string name = string.IsNullOrEmpty(_newSetName)
                ? meshContext.GenerateUniqueSelectionSetName("Selection")
                : _newSetName;

            if (meshContext.FindSelectionSetByName(name) != null)
                name = meshContext.GenerateUniqueSelectionSetName(name);

            var set = PartsSelectionSet.FromCurrentSelection(
                name, sel.Vertices, sel.Edges, sel.Faces, sel.Lines, sel.Mode);

            meshContext.PartsSelectionSetList.Add(set);
            _newSetName = "";
            _selectedIndex = meshContext.PartsSelectionSetList.Count - 1;
            Repaint();
        }

        private void LoadSelectedSet(MeshContext meshContext)
        {
            if (_selectedIndex < 0 || _selectedIndex >= meshContext.PartsSelectionSetList.Count) return;
            LoadSetAtIndex(meshContext, _selectedIndex);
        }

        private void LoadSetAtIndex(MeshContext meshContext, int index)
        {
            if (index < 0 || index >= meshContext.PartsSelectionSetList.Count) return;

            var set = meshContext.PartsSelectionSetList[index];
            var snapshot = new SelectionSnapshot
            {
                Mode = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges = new HashSet<VertexPair>(set.Edges),
                Faces = new HashSet<int>(set.Faces),
                Lines = new HashSet<int>(set.Lines)
            };
            _context.SelectionState.RestoreFromSnapshot(snapshot);
            _context.Repaint?.Invoke();
            Repaint();
        }

        private void AddSelectedSet(MeshContext meshContext)
        {
            if (_selectedIndex < 0 || _selectedIndex >= meshContext.PartsSelectionSetList.Count) return;

            var set = meshContext.PartsSelectionSetList[_selectedIndex];
            var sel = _context.SelectionState;
            var snapshot = sel.CreateSnapshot();
            snapshot.Vertices.UnionWith(set.Vertices);
            snapshot.Edges.UnionWith(set.Edges);
            snapshot.Faces.UnionWith(set.Faces);
            snapshot.Lines.UnionWith(set.Lines);
            sel.RestoreFromSnapshot(snapshot);
            _context.Repaint?.Invoke();
            Repaint();
        }

        private void SubtractSelectedSet(MeshContext meshContext)
        {
            if (_selectedIndex < 0 || _selectedIndex >= meshContext.PartsSelectionSetList.Count) return;

            var set = meshContext.PartsSelectionSetList[_selectedIndex];
            var sel = _context.SelectionState;
            var snapshot = sel.CreateSnapshot();
            snapshot.Vertices.ExceptWith(set.Vertices);
            snapshot.Edges.ExceptWith(set.Edges);
            snapshot.Faces.ExceptWith(set.Faces);
            snapshot.Lines.ExceptWith(set.Lines);
            sel.RestoreFromSnapshot(snapshot);
            _context.Repaint?.Invoke();
            Repaint();
        }

        private void DeleteSelectedSet(MeshContext meshContext)
        {
            if (_selectedIndex < 0 || _selectedIndex >= meshContext.PartsSelectionSetList.Count) return;

            var set = meshContext.PartsSelectionSetList[_selectedIndex];
            string name = set.Name;

            if (EditorUtility.DisplayDialog(
                L.Get("DeleteSelectionSet"),
                string.Format(L.Get("DeleteSelectionSetConfirm"), name),
                L.Get("Delete"), L.Get("Cancel")))
            {
                meshContext.RemoveSelectionSet(set);
                _selectedIndex = -1;
                Repaint();
            }
            GUIUtility.ExitGUI();
        }

        // ================================================================
        // CSV エクスポート/インポート
        // ================================================================

        private enum CSVDataType { Vertex, VertexId, Edge, Face, Line }

        private void ExportSetsToCSV(MeshContext meshContext)
        {
            if (meshContext.PartsSelectionSetList.Count == 0) return;

            string folderPath = EditorUtility.SaveFolderPanel(
                "Select Folder for CSV Export",
                Application.dataPath,
                $"SelectionSets_{meshContext.Name}");

            if (string.IsNullOrEmpty(folderPath))
            {
                GUIUtility.ExitGUI();
                return;
            }

            try
            {
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                int exportedCount = 0;
                foreach (var set in meshContext.PartsSelectionSetList)
                {
                    string safeName = SanitizeFileName(set.Name);
                    string fileName = $"Selected_{safeName}.csv";
                    string filePath = Path.Combine(folderPath, fileName);

                    var lines = new List<string>();
                    lines.Add($"# {meshContext.Name}");

                    if (set.Vertices.Count > 0)
                    {
                        bool hasVertexIds = HasValidVertexIds(meshContext, set.Vertices);
                        if (hasVertexIds)
                        {
                            lines.Add("# vertexId");
                            foreach (int vIdx in set.Vertices)
                                lines.Add(GetVertexId(meshContext, vIdx).ToString());
                        }
                        else
                        {
                            lines.Add("# vertex");
                            foreach (int vIdx in set.Vertices)
                                lines.Add(vIdx.ToString());
                        }
                    }
                    else if (set.Edges.Count > 0)
                    {
                        lines.Add("# edge");
                        foreach (var edge in set.Edges)
                            lines.Add($"{edge.V1},{edge.V2}");
                    }
                    else if (set.Faces.Count > 0)
                    {
                        lines.Add("# face");
                        foreach (int fIdx in set.Faces)
                            lines.Add(fIdx.ToString());
                    }
                    else if (set.Lines.Count > 0)
                    {
                        lines.Add("# line");
                        foreach (int lIdx in set.Lines)
                            lines.Add(lIdx.ToString());
                    }
                    else
                    {
                        continue;
                    }

                    File.WriteAllLines(filePath, lines);
                    exportedCount++;
                }

                Debug.Log($"[PartsSelectionSet] Exported {exportedCount} sets to: {folderPath}");
                EditorUtility.DisplayDialog("Export Complete",
                    $"Exported {exportedCount} selection sets to:\n{folderPath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSelectionSet] CSV export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to export:\n{ex.Message}", "OK");
            }
            GUIUtility.ExitGUI();
        }

        private void ImportSetFromCSV(MeshContext meshContext)
        {
            string filePath = EditorUtility.OpenFilePanel(
                "Import Selection Set CSV", Application.dataPath, "csv");

            if (string.IsNullOrEmpty(filePath))
            {
                GUIUtility.ExitGUI();
                return;
            }

            try
            {
                string[] fileLines = File.ReadAllLines(filePath);
                if (fileLines.Length < 2)
                {
                    Debug.LogWarning("[PartsSelectionSet] CSV file is empty or invalid.");
                    return;
                }

                string setName = Path.GetFileNameWithoutExtension(filePath);
                CSVDataType dataType = CSVDataType.Vertex;
                var numbers = new List<int>();
                var edges = new List<VertexPair>();

                foreach (string line in fileLines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (trimmed.StartsWith("#"))
                    {
                        string comment = trimmed.Substring(1).Trim();
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
                        continue;
                    }

                    if (dataType == CSVDataType.Edge)
                    {
                        string[] parts = trimmed.Split(',');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[0].Trim(), out int v1) &&
                            int.TryParse(parts[1].Trim(), out int v2))
                            edges.Add(new VertexPair(v1, v2));
                    }
                    else
                    {
                        if (int.TryParse(trimmed, out int num))
                            numbers.Add(num);
                    }
                }

                var set = new PartsSelectionSet(setName);
                set.Mode = DataTypeToMode(dataType);

                switch (dataType)
                {
                    case CSVDataType.Vertex:
                        set.Vertices = new HashSet<int>(numbers);
                        break;
                    case CSVDataType.VertexId:
                        var indices = ConvertVertexIdsToIndices(meshContext, numbers);
                        set.Vertices = new HashSet<int>(indices);
                        if (indices.Count < numbers.Count)
                            Debug.LogWarning($"[PartsSelectionSet] {numbers.Count - indices.Count} vertex IDs not found.");
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

                if (meshContext.FindSelectionSetByName(set.Name) != null)
                    set.Name = meshContext.GenerateUniqueSelectionSetName(set.Name);

                meshContext.PartsSelectionSetList.Add(set);
                _selectedIndex = meshContext.PartsSelectionSetList.Count - 1;
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSelectionSet] CSV import failed: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to import:\n{ex.Message}", "OK");
            }
            GUIUtility.ExitGUI();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            return name;
        }

        private static bool HasValidVertexIds(MeshContext meshContext, HashSet<int> vertexIndices)
        {
            if (meshContext?.MeshObject == null) return false;
            foreach (int idx in vertexIndices)
            {
                if (idx >= 0 && idx < meshContext.MeshObject.VertexCount)
                    if (meshContext.MeshObject.Vertices[idx].Id != 0)
                        return true;
            }
            return false;
        }

        private static int GetVertexId(MeshContext meshContext, int index)
        {
            if (meshContext?.MeshObject == null) return index;
            if (index < 0 || index >= meshContext.MeshObject.VertexCount) return index;
            return meshContext.MeshObject.Vertices[index].Id;
        }

        private static List<int> ConvertVertexIdsToIndices(MeshContext meshContext, List<int> ids)
        {
            var result = new List<int>();
            if (meshContext?.MeshObject == null) return result;

            var idToIndex = new Dictionary<int, int>();
            for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
            {
                int id = meshContext.MeshObject.Vertices[i].Id;
                if (!idToIndex.ContainsKey(id))
                    idToIndex[id] = i;
            }
            foreach (int id in ids)
            {
                if (idToIndex.TryGetValue(id, out int index))
                    result.Add(index);
            }
            return result;
        }

        private static MeshSelectMode DataTypeToMode(CSVDataType dataType)
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
}
