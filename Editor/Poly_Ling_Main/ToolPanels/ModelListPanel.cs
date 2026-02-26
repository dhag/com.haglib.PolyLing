// ModelListPanel.cs
// モデルリスト管理ウィンドウ
// モデルの一覧表示と選択、名前変更

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// モデルリスト管理ウィンドウ
    /// </summary>
    public class ModelListPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "ModelList";
        public override string Title => "Model List";
        public override IToolSettings Settings => null;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Model List", ["ja"] = "モデルリスト" },
            ["Models"] = new() { ["en"] = "Models", ["ja"] = "モデル" },
            ["NoProject"] = new() { ["en"] = "No project available", ["ja"] = "プロジェクトがありません" },
            ["NoModels"] = new() { ["en"] = "No models", ["ja"] = "モデルがありません" },
            ["Meshes"] = new() { ["en"] = "meshes", ["ja"] = "メッシュ" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除" },
            ["ConfirmDelete"] = new() { ["en"] = "Delete model \"{0}\"?", ["ja"] = "モデル「{0}」を削除しますか？" },
            ["ModelName"] = new() { ["en"] = "Model Name", ["ja"] = "モデル名" },
            ["Rename"] = new() { ["en"] = "Rename", ["ja"] = "改名" },
        };

        private static string T(string key)
        {
            if (_localize.TryGetValue(key, out var dict))
            {
                string lang = L.GetLanguageKey(L.CurrentLanguage);
                if (dict.TryGetValue(lang, out var text))
                    return text;
                if (dict.TryGetValue("en", out var fallback))
                    return fallback;
            }
            return key;
        }

        // ================================================================
        // フィールド
        // ================================================================

        private Vector2 _scrollPosition;
        private bool _isRenaming = false;
        private int _renamingIndex = -1;
        private string _renamingName = "";

        // ================================================================
        // ウィンドウを開く
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/Model List Panel")]
        public static void OpenFromMenu()
        {
            var panel = GetWindow<ModelListPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(280, 220);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<ModelListPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(280, 220);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI描画
        // ================================================================

        private void OnGUI()
        {
            var project = _context?.Project;

            if (project == null)
            {
                EditorGUILayout.HelpBox(T("NoProject"), MessageType.Info);
                return;
            }

            if (project.ModelCount == 0)
            {
                EditorGUILayout.HelpBox(T("NoModels"), MessageType.Info);
                return;
            }

            // ヘッダー
            EditorGUILayout.LabelField(T("Models"), EditorStyles.boldLabel);

            // モデル名変更（現在選択中のモデル）
            DrawModelNameEditor(project);

            EditorGUILayout.Space(3);

            // モデルリスト
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            int deleteIndex = -1;
            for (int i = 0; i < project.ModelCount; i++)
            {
                int result = DrawModelItem(project, i);
                if (result >= 0)
                    deleteIndex = result;
            }

            EditorGUILayout.EndScrollView();

            // ループ外で削除実行
            if (deleteIndex >= 0)
                DeleteModel(deleteIndex);
        }

        // ================================================================
        // モデル名編集
        // ================================================================

        private void DrawModelNameEditor(ProjectContext project)
        {
            int currentIndex = project.CurrentModelIndex;
            if (currentIndex < 0 || currentIndex >= project.ModelCount) return;

            var model = project.GetModel(currentIndex);
            if (model == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("ModelName"), GUILayout.Width(60));

            if (_isRenaming && _renamingIndex == currentIndex)
            {
                _renamingName = EditorGUILayout.TextField(_renamingName);
                if (GUILayout.Button("✓", GUILayout.Width(22)))
                {
                    if (!string.IsNullOrEmpty(_renamingName) && _renamingName != model.Name)
                    {
                        model.Name = _renamingName;
                        project.OnModelsChanged?.Invoke();
                    }
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
                EditorGUILayout.LabelField(model.Name, EditorStyles.boldLabel);
                if (GUILayout.Button("✎", GUILayout.Width(22)))
                {
                    _isRenaming = true;
                    _renamingIndex = currentIndex;
                    _renamingName = model.Name;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // モデルアイテム描画（リスト行クリック選択）
        // ================================================================

        /// <returns>削除要求があればそのインデックス、なければ-1</returns>
        private int DrawModelItem(ProjectContext project, int index)
        {
            var model = project.GetModel(index);
            if (model == null) return -1;

            bool isCurrent = (index == project.CurrentModelIndex);

            // 選択中は背景色を変える
            var style = isCurrent ? EditorStyles.helpBox : GUIStyle.none;
            EditorGUILayout.BeginHorizontal(style);

            // クリック可能なモデル名（ボタンとして描画）
            var labelStyle = isCurrent ? EditorStyles.boldLabel : EditorStyles.label;
            if (GUILayout.Button(model.Name, labelStyle, GUILayout.ExpandWidth(true)))
            {
                if (!isCurrent)
                    SelectModel(index);
            }

            // メッシュ数
            int meshCount = model.MeshContextCount;
            EditorGUILayout.LabelField($"{meshCount} {T("Meshes")}", EditorStyles.miniLabel, GUILayout.Width(80));

            // 削除ボタン
            int deleteRequest = -1;
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog(T("Delete"),
                    string.Format(T("ConfirmDelete"), model.Name), "OK", "Cancel"))
                {
                    deleteRequest = index;
                }
            }

            EditorGUILayout.EndHorizontal();
            return deleteRequest;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void SelectModel(int index)
        {
            if (_context?.SelectModel != null)
            {
                _context.SelectModel(index);
                Repaint();
            }
            else
            {
                var project = _context?.Project;
                if (project != null && project.SelectModel(index))
                    Repaint();
            }
        }

        private void DeleteModel(int index)
        {
            var project = _context?.Project;
            if (project == null) return;

            project.RemoveModelAt(index);
            Repaint();
        }
    }
}
