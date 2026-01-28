// HumanoidMappingPanel.cs
// HumanoidBoneMappingをCSVから取り込むツールパネル
// モデルのボーン名とCSVのマッピングを照合して設定

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Model;
using Poly_Ling.Tools;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// Humanoidボーンマッピング設定パネル
    /// </summary>
    public class HumanoidMappingPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "HumanoidMapping";
        public override string Title => "Humanoid Mapping";
        public override IToolSettings Settings => null;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Humanoid Bone Mapping", ["ja"] = "Humanoidボーンマッピング", ["hi"] = "ひゅーまのいどぼーん" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルが読み込まれていません", ["hi"] = "もでるがない" },
            ["NoBones"] = new() { ["en"] = "No bones in model", ["ja"] = "モデルにボーンがありません", ["hi"] = "ぼーんがない" },
            ["MappingSource"] = new() { ["en"] = "Mapping Source", ["ja"] = "マッピングソース", ["hi"] = "まっぴんぐもと" },
            ["CSVFile"] = new() { ["en"] = "CSV File", ["ja"] = "CSVファイル", ["hi"] = "CSVふぁいる" },
            ["DragDropCSV"] = new() { ["en"] = "Drag & Drop CSV here, or click [...] to browse", ["ja"] = "CSVをここにドロップ、または[...]で選択", ["hi"] = "CSVをどろっぷ" },
            ["AutoMapPMX"] = new() { ["en"] = "Auto Map (PMX Standard)", ["ja"] = "自動マッピング（PMX標準）", ["hi"] = "じどうまっぴんぐ" },
            ["LoadCSV"] = new() { ["en"] = "Load from CSV", ["ja"] = "CSVから読み込み", ["hi"] = "CSVからよみこみ" },
            ["Preview"] = new() { ["en"] = "Mapping Preview", ["ja"] = "マッピングプレビュー", ["hi"] = "ぷれびゅー" },
            ["MappedCount"] = new() { ["en"] = "Mapped: {0} / {1}", ["ja"] = "マッピング済: {0} / {1}", ["hi"] = "まっぴんぐ: {0} / {1}" },
            ["RequiredMissing"] = new() { ["en"] = "Required bones missing: {0}", ["ja"] = "必須ボーン不足: {0}", ["hi"] = "ひっすぼーんがない: {0}" },
            ["CanCreateAvatar"] = new() { ["en"] = "✓ Can create Humanoid Avatar", ["ja"] = "✓ Humanoid Avatar作成可能", ["hi"] = "✓ あばたーつくれる" },
            ["CannotCreateAvatar"] = new() { ["en"] = "✗ Cannot create Avatar (missing required bones)", ["ja"] = "✗ Avatar作成不可（必須ボーン不足）", ["hi"] = "✗ あばたーつくれない" },
            ["Apply"] = new() { ["en"] = "Apply to Model", ["ja"] = "モデルに適用", ["hi"] = "てきよう" },
            ["Clear"] = new() { ["en"] = "Clear Mapping", ["ja"] = "マッピングをクリア", ["hi"] = "くりあ" },
            ["ApplySuccess"] = new() { ["en"] = "Mapping applied: {0} bones", ["ja"] = "マッピング適用: {0}ボーン", ["hi"] = "てきようした: {0}" },
            ["Bones"] = new() { ["en"] = "Bones", ["ja"] = "ボーン", ["hi"] = "ぼーん" },
            ["NotMapped"] = new() { ["en"] = "(not mapped)", ["ja"] = "（未設定）", ["hi"] = "（みせってい）" },
            ["Required"] = new() { ["en"] = "*", ["ja"] = "*", ["hi"] = "*" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private string _csvFilePath = "";
        private Dictionary<string, string> _csvMapping; // Unity名 → ボーン名（CSV読み込み結果）
        private HumanoidBoneMapping _previewMapping;    // プレビュー用マッピング
        private Vector2 _scrollPosition;
        private bool _foldPreview = true;
        private bool _foldBoneList = false;

        // ================================================================
        // ウィンドウ表示
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var window = GetWindow<HumanoidMappingPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(350, 400);
            window.SetContext(ctx);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            // コンテキストチェック
            if (!DrawNoContextWarning())
                return;

            if (Model == null)
            {
                EditorGUILayout.HelpBox(T("NoModel"), MessageType.Warning);
                return;
            }

            // ボーンリスト取得
            var boneNames = GetBoneNames();
            if (boneNames.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoBones"), MessageType.Warning);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(5);

            // ================================================================
            // マッピングソース選択
            // ================================================================
            
            EditorGUILayout.LabelField(T("MappingSource"), EditorStyles.boldLabel);
            
            // CSVファイル選択
            EditorGUILayout.LabelField(T("CSVFile"));
            using (new EditorGUILayout.HorizontalScope())
            {
                var csvRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _csvFilePath = EditorGUI.TextField(csvRect, _csvFilePath);
                HandleDropOnRect(csvRect, path =>
                {
                    _csvFilePath = path;
                    LoadCSVMapping(boneNames);
                });

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_csvFilePath)
                        ? Application.dataPath
                        : Path.GetDirectoryName(_csvFilePath);

                    string path = EditorUtility.OpenFilePanel("Select Bone Mapping CSV", dir, "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _csvFilePath = path;
                        LoadCSVMapping(boneNames);
                    }
                }
            }

            if (string.IsNullOrEmpty(_csvFilePath))
            {
                EditorGUILayout.HelpBox(T("DragDropCSV"), MessageType.Info);
            }

            EditorGUILayout.Space(5);

            // ボタン群
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("AutoMapPMX")))
                {
                    AutoMapFromPMX(boneNames);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_csvFilePath)))
                {
                    if (GUILayout.Button(T("LoadCSV")))
                    {
                        LoadCSVMapping(boneNames);
                    }
                }
            }

            EditorGUILayout.Space(10);

            // ================================================================
            // プレビュー
            // ================================================================
            
            DrawPreviewSection(boneNames);

            EditorGUILayout.Space(10);

            // ================================================================
            // 適用ボタン
            // ================================================================
            
            DrawApplySection();

            EditorGUILayout.Space(10);

            // ================================================================
            // ボーンリスト表示
            // ================================================================
            
            DrawBoneListSection(boneNames);

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // ドラッグ＆ドロップ処理
        // ================================================================

        private void HandleDropOnRect(Rect rect, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (DragAndDrop.paths.Length > 0 && 
                        DragAndDrop.paths[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;

                case EventType.DragPerform:
                    if (DragAndDrop.paths.Length > 0)
                    {
                        string path = DragAndDrop.paths[0];
                        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            DragAndDrop.AcceptDrag();
                            onDrop(path);
                            evt.Use();
                        }
                    }
                    break;
            }
        }

        // ================================================================
        // ボーン名リスト取得
        // ================================================================

        private List<string> GetBoneNames()
        {
            var names = new List<string>();
            if (Model?.MeshContextList == null) return names;

            for (int i = 0; i < Model.MeshContextList.Count; i++)
            {
                var ctx = Model.MeshContextList[i];
                if (ctx?.Type == MeshType.Bone)
                {
                    names.Add(ctx.Name ?? $"Bone_{i}");
                }
            }

            return names;
        }

        /// <summary>
        /// ボーン名からMeshContextListのインデックスを取得
        /// </summary>
        private Dictionary<string, int> GetBoneNameToIndexMap()
        {
            var map = new Dictionary<string, int>();
            if (Model?.MeshContextList == null) return map;

            for (int i = 0; i < Model.MeshContextList.Count; i++)
            {
                var ctx = Model.MeshContextList[i];
                if (ctx?.Type == MeshType.Bone && !string.IsNullOrEmpty(ctx.Name))
                {
                    if (!map.ContainsKey(ctx.Name))
                    {
                        map[ctx.Name] = i;
                    }
                }
            }

            return map;
        }

        // ================================================================
        // CSV読み込み
        // ================================================================

        private void LoadCSVMapping(List<string> boneNames)
        {
            if (string.IsNullOrEmpty(_csvFilePath) || !File.Exists(_csvFilePath))
            {
                Debug.LogWarning("[HumanoidMappingPanel] CSV file not found");
                return;
            }

            try
            {
                // CSV読み込み（Unity名 → ボーン名）
                _csvMapping = new Dictionary<string, string>();
                var lines = File.ReadAllLines(_csvFilePath, Encoding.UTF8);

                bool isHeader = true;
                foreach (var line in lines)
                {
                    if (isHeader)
                    {
                        isHeader = false;
                        continue;
                    }

                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        string unityName = parts[0].Trim();
                        string boneName = parts[1].Trim();

                        if (!string.IsNullOrEmpty(unityName) && !string.IsNullOrEmpty(boneName))
                        {
                            _csvMapping[unityName] = boneName;
                        }
                    }
                }

                Debug.Log($"[HumanoidMappingPanel] Loaded CSV: {_csvMapping.Count} entries");

                // プレビューマッピングを作成
                CreatePreviewMapping(boneNames);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HumanoidMappingPanel] Failed to load CSV: {ex.Message}");
                _csvMapping = null;
                _previewMapping = null;
            }

            Repaint();
        }

        // ================================================================
        // 自動マッピング（PMX標準）
        // ================================================================

        private void AutoMapFromPMX(List<string> boneNames)
        {
            _previewMapping = new HumanoidBoneMapping();
            int count = _previewMapping.AutoMapFromPMX(boneNames);
            Debug.Log($"[HumanoidMappingPanel] Auto-mapped {count} bones from PMX standard names");
            Repaint();
        }

        // ================================================================
        // プレビューマッピング作成
        // ================================================================

        private void CreatePreviewMapping(List<string> boneNames)
        {
            if (_csvMapping == null || _csvMapping.Count == 0)
            {
                _previewMapping = null;
                return;
            }

            _previewMapping = new HumanoidBoneMapping();
            
            // CSV行をリストに変換してLoadFromCSVを使用
            var csvLines = new List<string> { "UnityName,BoneName" }; // ヘッダー
            foreach (var kvp in _csvMapping)
            {
                csvLines.Add($"{kvp.Key},{kvp.Value}");
            }

            int count = _previewMapping.LoadFromCSV(csvLines, boneNames);
            Debug.Log($"[HumanoidMappingPanel] Preview mapping created: {count} bones");
        }

        // ================================================================
        // プレビュー描画
        // ================================================================

        private void DrawPreviewSection(List<string> boneNames)
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            EditorGUI.indentLevel++;

            if (_previewMapping == null || _previewMapping.IsEmpty)
            {
                EditorGUILayout.LabelField("(No mapping loaded)");
                EditorGUI.indentLevel--;
                return;
            }

            // マッピング数
            int totalHumanoid = HumanoidBoneMapping.AllHumanoidBones.Length;
            int mappedCount = _previewMapping.Count;
            EditorGUILayout.LabelField(T("MappedCount", mappedCount, totalHumanoid));

            // 必須ボーン不足
            var missing = _previewMapping.GetMissingRequiredBones();
            if (missing.Count > 0)
            {
                GUI.color = new Color(1f, 0.7f, 0.5f);
                EditorGUILayout.LabelField(T("RequiredMissing", missing.Count));
                EditorGUI.indentLevel++;
                foreach (var bone in missing)
                {
                    EditorGUILayout.LabelField($"• {bone}");
                }
                EditorGUI.indentLevel--;
                GUI.color = Color.white;
            }

            // Avatar作成可否
            EditorGUILayout.Space(3);
            if (_previewMapping.CanCreateAvatar)
            {
                GUI.color = new Color(0.5f, 1f, 0.5f);
                EditorGUILayout.LabelField(T("CanCreateAvatar"));
            }
            else
            {
                GUI.color = new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField(T("CannotCreateAvatar"));
            }
            GUI.color = Color.white;

            // マッピング詳細
            EditorGUILayout.Space(5);
            var boneIndexMap = GetBoneNameToIndexMap();
            
            foreach (var humanoidBone in HumanoidBoneMapping.AllHumanoidBones)
            {
                int boneIndex = _previewMapping.Get(humanoidBone);
                if (boneIndex < 0) continue;

                // インデックスからボーン名を取得
                string boneName = "(invalid index)";
                if (boneIndex < Model.MeshContextList.Count)
                {
                    boneName = Model.MeshContextList[boneIndex]?.Name ?? boneName;
                }

                bool isRequired = HumanoidBoneMapping.RequiredBones.Contains(humanoidBone);
                string label = isRequired ? $"{humanoidBone} {T("Required")}" : humanoidBone;
                
                EditorGUILayout.LabelField(label, boneName);
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // 適用ボタン
        // ================================================================

        private void DrawApplySection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 適用ボタン
                using (new EditorGUI.DisabledScope(_previewMapping == null || _previewMapping.IsEmpty))
                {
                    if (GUILayout.Button(T("Apply"), GUILayout.Height(30)))
                    {
                        ApplyMapping();
                    }
                }

                // クリアボタン
                using (new EditorGUI.DisabledScope(Model?.HumanoidMapping == null || Model.HumanoidMapping.IsEmpty))
                {
                    if (GUILayout.Button(T("Clear"), GUILayout.Width(80), GUILayout.Height(30)))
                    {
                        ClearMapping();
                    }
                }
            }
        }

        private void ApplyMapping()
        {
            if (_previewMapping == null || Model == null) return;

            // モデルのHumanoidMappingにコピー
            Model.HumanoidMapping.CopyFrom(_previewMapping);
            Model.IsDirty = true;

            Debug.Log($"[HumanoidMappingPanel] {T("ApplySuccess", _previewMapping.Count)}");
            Repaint();
        }

        private void ClearMapping()
        {
            if (Model == null) return;

            Model.HumanoidMapping.ClearAll();
            Model.IsDirty = true;

            _previewMapping = null;
            Debug.Log("[HumanoidMappingPanel] Mapping cleared");
            Repaint();
        }

        // ================================================================
        // ボーンリスト表示
        // ================================================================

        private void DrawBoneListSection(List<string> boneNames)
        {
            _foldBoneList = EditorGUILayout.Foldout(_foldBoneList, $"{T("Bones")} ({boneNames.Count})", true);
            if (!_foldBoneList) return;

            EditorGUI.indentLevel++;

            for (int i = 0; i < boneNames.Count; i++)
            {
                EditorGUILayout.LabelField($"[{i}] {boneNames[i]}");
            }

            EditorGUI.indentLevel--;
        }
    }
}
