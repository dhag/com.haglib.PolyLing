// Assets/Editor/SimpleMeshFactory.GUI.Tools.cs
// ToolManager統合版
// Phase 1: UI描画をToolManagerと連携
// ToolButtonLayoutを削除し、登録順で2列自動レイアウト
// Phase 4: ToolWindow対応

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Tools;
using MeshFactory.Tools.Windows;
using MeshFactory.Selection;
using MeshFactory.Localization;


public partial class SimpleMeshFactory
{
    // ================================================================
    // Tools セクション描画
    // ================================================================

    private void DrawToolsSection()
    {
        _foldTools = DrawFoldoutWithUndo("Tools", L.Get("Tools"), true);
        if (!_foldTools)
            return;

        EditorGUI.indentLevel++;

        // ツールボタン（登録順で2列自動レイアウト）
        DrawToolButtons();

        // 現在のツールの設定UI
        EditorGUILayout.Space(3);

        // ツール設定UIの描画とUndo処理
        DrawCurrentToolSettingsWithUndo();

        // ツール設定をシリアライズ用に同期
        SyncSettingsFromTool();

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// ツールボタンを描画（登録順で2列自動レイアウト）
    /// </summary>
    private void DrawToolButtons()
    {
        if (_toolManager == null) return;

        var toolNames = _toolManager.ToolNames;
        int count = toolNames.Count;

        for (int i = 0; i < count; i += 2)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 1列目
                var tool1 = _toolManager.GetTool(toolNames[i]);
                if (tool1 != null)
                {
                    DrawToolButton(tool1);
                }

                // 2列目（存在する場合）
                if (i + 1 < count)
                {
                    var tool2 = _toolManager.GetTool(toolNames[i + 1]);
                    if (tool2 != null)
                    {
                        DrawToolButton(tool2);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 個別ツールボタンを描画
    /// </summary>
    private void DrawToolButton(IEditTool tool)
    {
        if (tool == null) return;

        bool isActive = _toolManager?.CurrentTool == tool;
        var oldColor = GUI.backgroundColor;
        if (isActive)
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);

        // ツール名をローカライズ（Tool_XXX形式で辞書を検索）
        string displayName = L.Get("Tool_" + tool.Name);

        if (GUILayout.Toggle(isActive, displayName, "Button") && !isActive)
        {
            // Undo記録付きでツール切り替え
            SwitchToolWithUndo(tool);
        }

        GUI.backgroundColor = oldColor;
    }

    private void SwitchToolWithUndo(IEditTool newTool)
    {
        if (_undoController != null)
        {
            string oldToolName = _toolManager?.CurrentToolName ?? "Select";
            _undoController.EditorState.CurrentToolName = oldToolName;
            _undoController.BeginEditorStateDrag();
        }

        _toolManager.SetTool(newTool);

        if (_undoController != null)
        {
            _undoController.EditorState.CurrentToolName = newTool.Name;
            _undoController.EndEditorStateDrag($"Switch to {newTool.DisplayName} Tool");
        }
    }

    // ================================================================
    // ツール設定UI描画 + Undo処理
    // ================================================================

    /// <summary>
    /// 現在のツールの設定UIを描画し、変更があればUndo記録
    /// </summary>
    private void DrawCurrentToolSettingsWithUndo()
    {
        var currentTool = _toolManager?.CurrentTool;
        if (currentTool == null)
            return;

        // --------------------------------------------------------
        // IToolSettings対応ツール: 汎用Undo処理
        // --------------------------------------------------------
        var settings = currentTool.Settings;
        if (settings != null)
        {
            // Before: スナップショット取得
            IToolSettings before = settings.Clone();

            // UI描画（ツール側で自由に実装）
            currentTool.DrawSettingsUI();

            // After: 変更検出 & Undo記録
            if (settings.IsDifferentFrom(before))
            {
                RecordToolSettingsChange(currentTool, before, settings.Clone());
            }
        }
        else
        {
            // 設定を持たないツール（SelectToolなど）
            currentTool.DrawSettingsUI();
        }
    }

    /// <summary>
    /// ツール設定変更をUndo記録
    /// </summary>
    private void RecordToolSettingsChange(IEditTool tool, IToolSettings before, IToolSettings after)
    {
        if (_undoController == null || tool == null || before == null || after == null)
            return;

        var editorState = _undoController.EditorState;
        if (editorState.ToolSettings == null)
            editorState.ToolSettings = new ToolSettingsStorage();

        // Before: 変更前の設定をEditorStateContextに設定
        editorState.ToolSettings.Set(tool.Name, before);

        // BeginDrag: スナップショット取得
        _undoController.BeginEditorStateDrag();

        // After: 変更後の設定をEditorStateContextに設定
        editorState.ToolSettings.Set(tool.Name, after);

        // EndDrag: 差異検出 & Undo記録
        _undoController.EndEditorStateDrag($"Change {tool.DisplayName} Settings");

        Repaint();
    }

    // ================================================================
    // ToolWindows セクション（Phase 4追加）
    // ================================================================

    private bool _foldToolWindows = false;

    /// <summary>
    /// ToolWindowsセクションを描画
    /// </summary>
    private void DrawToolWindowsSection()
    {
        _foldToolWindows = DrawFoldoutWithUndo("ToolWindows", L.Get("ToolWindows"), false);
        if (!_foldToolWindows)
            return;

        EditorGUI.indentLevel++;

        // UnityMesh List Window（統合版）
        if (GUILayout.Button(L.Get("OpenMeshListWindow")))
        {
            MeshListWindow.Open(_toolManager?.toolContext);
        }

        // 登録されたToolWindow
        var windows = ToolWindowRegistry.Windows;
        if (windows != null && windows.Length > 0)
        {
            EditorGUILayout.Space(3);

            foreach (var (title, open) in windows)
            {
                if (GUILayout.Button($"Open {title}"))
                {
                    open?.Invoke(_toolManager?.toolContext);
                }
            }
        }

        EditorGUI.indentLevel--;
    }

}