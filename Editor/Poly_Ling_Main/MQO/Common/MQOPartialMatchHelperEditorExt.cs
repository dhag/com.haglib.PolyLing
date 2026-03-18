// Editor/Poly_Ling_Main/MQO/Common/MQOPartialMatchHelperEditorExt.cs
// MQOPartialMatchHelper の Editor GUI 拡張
// DrawDualListSection: 拡張メソッド（Runtime クラスに GUI を追加）
// HandleDropOnRect: static ユーティリティ（パネルから直接呼ぶ）

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Localization;
using Poly_Ling.Tools;

namespace Poly_Ling.MQO
{
    public static class MQOPartialMatchHelperEditorExt
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["ModelMeshes"]    = new() { ["en"] = "Model Meshes",                      ["ja"] = "モデルメッシュ" },
            ["MQOObjects"]     = new() { ["en"] = "MQO Objects",                       ["ja"] = "MQOオブジェクト" },
            ["SelectAll"]      = new() { ["en"] = "All",                               ["ja"] = "全選択" },
            ["SelectNone"]     = new() { ["en"] = "None",                              ["ja"] = "全解除" },
            ["NoContext"]      = new() { ["en"] = "No context. Open from Poly_Ling.",  ["ja"] = "コンテキスト未設定" },
            ["NoModel"]        = new() { ["en"] = "No model loaded",                   ["ja"] = "モデルなし" },
            ["SelectMQOFirst"] = new() { ["en"] = "Select MQO file",                   ["ja"] = "MQOファイルを選択" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);

        // ================================================================
        // 拡張メソッド: DrawDualListSection
        // ================================================================

        public static bool DrawDualListSection(this MQOPartialMatchHelper self,
            ToolContext context, float windowWidth,
            ref Vector2 scrollLeft, ref Vector2 scrollRight)
        {
            if (context == null)
            {
                EditorGUILayout.HelpBox(T("NoContext"), MessageType.Warning);
                return false;
            }
            if (context.Model == null)
            {
                EditorGUILayout.HelpBox(T("NoModel"), MessageType.Warning);
                return false;
            }
            if (self.MQODocument == null)
            {
                EditorGUILayout.HelpBox(T("SelectMQOFirst"), MessageType.Info);
                return false;
            }

            float halfWidth = (windowWidth - 30) / 2;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("MQOObjects"), false, self);
                    scrollLeft = EditorGUILayout.BeginScrollView(scrollLeft, GUILayout.Height(300));
                    DrawMQOList(self);
                    EditorGUILayout.EndScrollView();
                }

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("ModelMeshes"), true, self);
                    scrollRight = EditorGUILayout.BeginScrollView(scrollRight, GUILayout.Height(300));
                    DrawModelList(self);
                    EditorGUILayout.EndScrollView();
                }
            }

            return true;
        }

        // ================================================================
        // 内部描画ヘルパー
        // ================================================================

        private static void DrawListHeader(string title, bool isModel, MQOPartialMatchHelper self)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("SelectAll"), GUILayout.Width(50)))
                {
                    if (isModel) foreach (var m in self.ModelMeshes) m.Selected = true;
                    else         foreach (var m in self.MQOObjects)  m.Selected = true;
                }
                if (GUILayout.Button(T("SelectNone"), GUILayout.Width(50)))
                {
                    if (isModel) foreach (var m in self.ModelMeshes) m.Selected = false;
                    else         foreach (var m in self.MQOObjects)  m.Selected = false;
                }
            }
        }

        private static void DrawModelList(MQOPartialMatchHelper self)
        {
            foreach (var entry in self.ModelMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string label;
                    if (entry.BakedMirrorPeer != null)
                    {
                        label = $"{entry.Name} (+ {entry.BakedMirrorPeer.Name}) [{entry.TotalExpandedVertexCount}]";
                        GUI.color = new Color(1f, 0.85f, 0.6f);
                    }
                    else
                    {
                        label = $"{entry.Name} ({entry.ExpandedVertexCount})";
                    }

                    EditorGUILayout.LabelField(label);
                    GUI.color = Color.white;
                }
            }
        }

        private static void DrawMQOList(MQOPartialMatchHelper self)
        {
            foreach (var entry in self.MQOObjects)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string mirror   = entry.IsMirrored ? " [M]" : "";
                    string countStr = entry.IsMirrored
                        ? $"{entry.ExpandedVertexCount}×2={entry.ExpandedVertexCountWithMirror}"
                        : $"{entry.ExpandedVertexCount}";

                    if (entry.IsMirrored) GUI.color = new Color(1f, 0.85f, 0.6f);
                    EditorGUILayout.LabelField($"{entry.Name}{mirror} ({countStr})");
                    GUI.color = Color.white;
                }
            }
        }

        // ================================================================
        // static ユーティリティ: ファイルドロップ処理
        // ================================================================

        public static void HandleDropOnRect(Rect rect, string ext, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated)
            {
                if (DragAndDrop.paths.Length > 0 &&
                    Path.GetExtension(DragAndDrop.paths[0]).ToLower() == ext)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.DragPerform)
            {
                if (DragAndDrop.paths.Length > 0 &&
                    Path.GetExtension(DragAndDrop.paths[0]).ToLower() == ext)
                {
                    DragAndDrop.AcceptDrag();
                    onDrop(DragAndDrop.paths[0]);
                    evt.Use();
                }
            }
        }
    }
}
