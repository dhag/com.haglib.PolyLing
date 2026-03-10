// PresetUI.cs
// IMGUI用プリセット保存・ロードUIヘルパー
// MeshCreatorWindowBaseおよびIToolPanelBaseから呼び出す

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public static class PresetUI
    {
        // ================================================================
        // 内部状態（キーごとに独立）
        // ================================================================

        private class State
        {
            public List<ToolPresetStore.PresetEntry> Entries = new List<ToolPresetStore.PresetEntry>();
            public int SelectedIndex = 0;
            public string SaveName = "";
            public bool Dirty = true; // 次回Reload必要
        }

        private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();

        private static State GetState(string key)
        {
            if (!_states.TryGetValue(key, out var state))
            {
                state = new State();
                _states[key] = state;
            }
            if (state.Dirty)
            {
                state.Entries = ToolPresetStore.Load(key);
                state.SelectedIndex = Mathf.Clamp(state.SelectedIndex, 0,
                    Mathf.Max(0, state.Entries.Count - 1));
                state.Dirty = false;
            }
            return state;
        }

        // ================================================================
        // 外部からDirtyフラグを立てる（保存・削除後に呼ぶ）
        // ================================================================

        public static void MarkDirty(string key)
        {
            if (_states.TryGetValue(key, out var s)) s.Dirty = true;
        }

        // ================================================================
        // Draw: 1行のプリセットUI
        // ================================================================

        /// <summary>
        /// プリセットUIを描画する。
        /// 戻り値: ロードが要求された場合のJSON文字列（ロード不要の場合null）
        /// </summary>
        public static string Draw(string key, string currentJson)
        {
            var state = GetState(key);
            string loadedJson = null;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("プリセット", EditorStyles.miniBoldLabel);

            // --- 保存行 ---
            EditorGUILayout.BeginHorizontal();
            state.SaveName = EditorGUILayout.TextField(state.SaveName, GUILayout.ExpandWidth(true));
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(state.SaveName));
            if (GUILayout.Button("保存", GUILayout.Width(44)))
            {
                ToolPresetStore.Save(key, state.SaveName.Trim(), currentJson);
                MarkDirty(key);
                state = GetState(key); // リロード
                // 保存後、今保存した項目を選択状態にする
                state.SelectedIndex = state.Entries.FindIndex(e => e.Name == state.SaveName.Trim());
                if (state.SelectedIndex < 0) state.SelectedIndex = 0;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // --- ロード・削除行 ---
            if (state.Entries.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();

                // プリセット名Popup
                var names = new string[state.Entries.Count];
                for (int i = 0; i < state.Entries.Count; i++)
                    names[i] = state.Entries[i].Name;
                state.SelectedIndex = Mathf.Clamp(state.SelectedIndex, 0, names.Length - 1);
                state.SelectedIndex = EditorGUILayout.Popup(state.SelectedIndex, names,
                    GUILayout.ExpandWidth(true));

                if (GUILayout.Button("ロード", GUILayout.Width(52)))
                {
                    loadedJson = state.Entries[state.SelectedIndex].Json;
                    // 保存名フィールドに選択中の名前を反映
                    state.SaveName = state.Entries[state.SelectedIndex].Name;
                }

                if (GUILayout.Button("削除", GUILayout.Width(44)))
                {
                    if (EditorUtility.DisplayDialog("プリセット削除",
                        $"「{names[state.SelectedIndex]}」を削除しますか？", "削除", "キャンセル"))
                    {
                        ToolPresetStore.Delete(key, names[state.SelectedIndex]);
                        MarkDirty(key);
                        state = GetState(key);
                        state.SelectedIndex = Mathf.Clamp(state.SelectedIndex, 0,
                            Mathf.Max(0, state.Entries.Count - 1));
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("(プリセットなし)", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();

            return loadedJson;
        }
    }
}
