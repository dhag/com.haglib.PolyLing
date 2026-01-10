// Assets/Editor/SimpleMeshFactory.SelectionSets.cs
// 選択セット管理UI
// メッシュ単位で選択状態を保存・復元

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using MeshFactory.Localization;

public partial class SimpleMeshFactory
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
        var meshContext = _model?.CurrentMeshContext;
        if (meshContext == null) return;

        _foldSelectionSets = EditorGUILayout.Foldout(_foldSelectionSets, L.Get("SelectionSets"), true);
        if (!_foldSelectionSets) return;

        EditorGUI.indentLevel++;

        // ================================================================
        // 保存ボタン行
        // ================================================================
        EditorGUILayout.BeginHorizontal();

        // 新規名前入力
        _newSelectionSetName = EditorGUILayout.TextField(_newSelectionSetName, GUILayout.MinWidth(80));

        // 保存ボタン（グローバル選択状態をチェック）
        bool hasSelection = (_selectedVertices != null && _selectedVertices.Count > 0) ||
                            (_selectionState != null && _selectionState.HasSelection);

        using (new EditorGUI.DisabledScope(!hasSelection))
        {
            if (GUILayout.Button(L.Get("SaveSelection"), GUILayout.Width(80)))
            {
                SaveCurrentSelectionAsSet(meshContext);
            }
        }

        EditorGUILayout.EndHorizontal();

        // ================================================================
        // 選択セットリスト
        // ================================================================
        if (meshContext.SelectionSets.Count > 0)
        {
            EditorGUILayout.Space(3);

            // スクロールビュー
            float listHeight = Mathf.Min(meshContext.SelectionSets.Count * 22f + 10f, 150f);
            _selectionSetsScroll = EditorGUILayout.BeginScrollView(
                _selectionSetsScroll,
                GUILayout.Height(listHeight));

            for (int i = 0; i < meshContext.SelectionSets.Count; i++)
            {
                var set = meshContext.SelectionSets[i];
                DrawSelectionSetItem(meshContext, set, i);
            }

            EditorGUILayout.EndScrollView();

            // ================================================================
            // 操作ボタン行
            // ================================================================
            EditorGUILayout.BeginHorizontal();

            // Load
            using (new EditorGUI.DisabledScope(_selectedSelectionSetIndex < 0))
            {
                if (GUILayout.Button(L.Get("Load"), GUILayout.Width(50)))
                {
                    LoadSelectedSelectionSet(meshContext);
                }

                // Add
                if (GUILayout.Button(L.Get("Add"), GUILayout.Width(40)))
                {
                    AddSelectedSelectionSet(meshContext);
                }

                // Subtract
                if (GUILayout.Button(L.Get("Subtract"), GUILayout.Width(60)))
                {
                    SubtractSelectedSelectionSet(meshContext);
                }

                GUILayout.FlexibleSpace();

                // Delete
                if (GUILayout.Button(L.Get("Delete"), GUILayout.Width(50)))
                {
                    DeleteSelectedSelectionSet(meshContext);
                }
            }

            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.HelpBox(L.Get("NoSelectionSets"), MessageType.Info);
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 選択セットアイテムを描画
    /// </summary>
    private void DrawSelectionSetItem(MeshContext meshContext, SelectionSet set, int index)
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
        var set = SelectionSet.FromCurrentSelection(
            name,
            _selectedVertices,
            _selectionState?.Edges,
            _selectionState?.Faces,
            _selectionState?.Lines,
            _selectionState?.Mode ?? MeshSelectMode.Vertex
        );

        meshContext.SelectionSets.Add(set);

        // 入力欄クリア
        _newSelectionSetName = "";

        // 新しいセットを選択
        _selectedSelectionSetIndex = meshContext.SelectionSets.Count - 1;

        Debug.Log($"[SelectionSets] Saved: {name} ({set.Summary})");
        Repaint();
    }

    /// <summary>
    /// 選択中のセットをロード
    /// </summary>
    private void LoadSelectedSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.SelectionSets.Count)
            return;

        LoadSelectionSetAtIndex(meshContext, _selectedSelectionSetIndex);
    }

    /// <summary>
    /// 指定インデックスのセットをロード
    /// </summary>
    private void LoadSelectionSetAtIndex(MeshContext meshContext, int index)
    {
        if (index < 0 || index >= meshContext.SelectionSets.Count)
            return;

        var set = meshContext.SelectionSets[index];

        // グローバル選択状態に直接復元
        _selectedVertices.Clear();
        _selectedVertices.UnionWith(set.Vertices);

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
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを現在の選択に追加
    /// </summary>
    private void AddSelectedSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.SelectionSets.Count)
            return;

        var set = meshContext.SelectionSets[_selectedSelectionSetIndex];

        // グローバル選択状態に直接追加
        if (set.Vertices != null && set.Vertices.Count > 0)
        {
            _selectedVertices.UnionWith(set.Vertices);
        }
        if (_selectionState != null)
        {
            _selectionState.Edges.UnionWith(set.Edges);
            _selectionState.Faces.UnionWith(set.Faces);
            _selectionState.Lines.UnionWith(set.Lines);
        }

        Debug.Log($"[SelectionSets] Added: {set.Name}");
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを現在の選択から除外
    /// </summary>
    private void SubtractSelectedSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.SelectionSets.Count)
            return;

        var set = meshContext.SelectionSets[_selectedSelectionSetIndex];

        // グローバル選択状態から直接除外
        if (set.Vertices != null && set.Vertices.Count > 0)
        {
            _selectedVertices.ExceptWith(set.Vertices);
        }
        if (_selectionState != null)
        {
            _selectionState.Edges.ExceptWith(set.Edges);
            _selectionState.Faces.ExceptWith(set.Faces);
            _selectionState.Lines.ExceptWith(set.Lines);
        }

        Debug.Log($"[SelectionSets] Subtracted: {set.Name}");
        Repaint();
    }

    /// <summary>
    /// 選択中のセットを削除
    /// </summary>
    private void DeleteSelectedSelectionSet(MeshContext meshContext)
    {
        if (_selectedSelectionSetIndex < 0 || _selectedSelectionSetIndex >= meshContext.SelectionSets.Count)
            return;

        var set = meshContext.SelectionSets[_selectedSelectionSetIndex];
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
    }
}