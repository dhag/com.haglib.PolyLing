// ModelListPanel.cs
// モデルリスト管理ウィンドウ
// モデルの一覧表示と選択

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
            ["Current"] = new() { ["en"] = "Current", ["ja"] = "選択中" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除" },
            ["ConfirmDelete"] = new() { ["en"] = "Delete model \"{0}\"?", ["ja"] = "モデル「{0}」を削除しますか？" },
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

        // ================================================================
        // ウィンドウを開く
        // ================================================================

        [MenuItem("Tools/Poly_Ling/Model List Panel")]
        public static void OpenFromMenu()
        {
            var panel = GetWindow<ModelListPanel>();
            panel.titleContent = new GUIContent("Model List");
            panel.minSize = new Vector2(250, 200);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<ModelListPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(250, 200);
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

            EditorGUILayout.Space(5);

            // モデルリスト
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            int deleteIndex = -1;
            for (int i = 0; i < project.ModelCount; i++)
            {
                int result = DrawModelItem(project, i);
                if (result >= 0)
                {
                    deleteIndex = result;
                }
            }

            EditorGUILayout.EndScrollView();

            // ループ外で削除実行
            if (deleteIndex >= 0)
            {
                DeleteModel(deleteIndex);
            }
        }

        /// <summary>
        /// モデルアイテムを描画
        /// </summary>
        /// <returns>削除要求があればそのインデックス、なければ-1</returns>
        private int DrawModelItem(ProjectContext project, int index)
        {
            var model = project.GetModel(index);
            if (model == null) return -1;

            bool isCurrent = (index == project.CurrentModelIndex);

            // 背景色
            if (isCurrent)
            {
                var rect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
            }

            // 選択ボタン（ラジオボタン風）
            bool selected = GUILayout.Toggle(isCurrent, "", GUILayout.Width(20));
            if (selected && !isCurrent)
            {
                SelectModel(index);
            }

            // モデル名
            EditorGUILayout.LabelField(model.Name, GUILayout.ExpandWidth(true));

            // メッシュ数
            int meshCount = model.MeshContextCount;
            EditorGUILayout.LabelField($"{meshCount} {T("Meshes")}", EditorStyles.miniLabel, GUILayout.Width(80));

            // 削除ボタン
            int deleteRequest = -1;
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog(T("Delete"), string.Format(T("ConfirmDelete"), model.Name), "OK", "Cancel"))
                {
                    deleteRequest = index;
                }
            }

            EditorGUILayout.EndHorizontal();
            return deleteRequest;
        }

        /// <summary>
        /// モデルを選択
        /// </summary>
        private void SelectModel(int index)
        {
            if (_context?.SelectModel != null)
            {
                // ToolContext経由で選択（Undo対応）
                _context.SelectModel(index);
                Repaint();
            }
            else
            {
                // 直接選択（フォールバック）
                var project = _context?.Project;
                if (project != null && project.SelectModel(index))
                {
                    Repaint();
                }
            }
        }

        /// <summary>
        /// モデルを削除
        /// </summary>
        private void DeleteModel(int index)
        {
            var project = _context?.Project;
            if (project == null) return;

            project.RemoveModelAt(index);
            Repaint();
        }
    }
}