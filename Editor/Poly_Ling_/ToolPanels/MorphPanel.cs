// Assets/Editor/Poly_Ling_/ToolPanels/MorphPanel.cs
// モーフ作成・管理・プレビューパネル
// v2: 基本モード（まとめてプレビュー）と詳細モード（個別選択）

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// モーフパネル設定
    /// </summary>
    [Serializable]
    public class MorphPanelSettings : IToolSettings
    {
        /// <summary>選択中のモーフセットインデックス</summary>
        public int SelectedMorphSetIndex = -1;

        /// <summary>選択中のモーフメッシュインデックス（セット内・詳細モード用）</summary>
        public int SelectedMorphMeshIndex = -1;

        /// <summary>プレビューウェイト</summary>
        [Range(0f, 1f)]
        public float PreviewWeight = 0f;

        /// <summary>詳細モード</summary>
        public bool DetailMode = false;

        public IToolSettings Clone()
        {
            return new MorphPanelSettings
            {
                SelectedMorphSetIndex = this.SelectedMorphSetIndex,
                SelectedMorphMeshIndex = this.SelectedMorphMeshIndex,
                PreviewWeight = this.PreviewWeight,
                DetailMode = this.DetailMode
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not MorphPanelSettings o)
                return true;

            return SelectedMorphSetIndex != o.SelectedMorphSetIndex ||
                   SelectedMorphMeshIndex != o.SelectedMorphMeshIndex ||
                   !Mathf.Approximately(PreviewWeight, o.PreviewWeight) ||
                   DetailMode != o.DetailMode;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not MorphPanelSettings o)
                return;

            SelectedMorphSetIndex = o.SelectedMorphSetIndex;
            SelectedMorphMeshIndex = o.SelectedMorphMeshIndex;
            PreviewWeight = o.PreviewWeight;
            DetailMode = o.DetailMode;
        }
    }

    /// <summary>
    /// モーフ作成・管理・プレビューパネル
    /// </summary>
    public class MorphPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "MorphPanel";
        public override string Title => "Morph";

        private MorphPanelSettings _settings = new MorphPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Morph Editor", ["ja"] = "モーフエディタ" },

            // メッセージ
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoMorphSets"] = new() { ["en"] = "No morph sets imported", ["ja"] = "モーフセットがありません" },
            ["SelectMorphSet"] = new() { ["en"] = "Select a morph set", ["ja"] = "モーフセットを選択" },
            ["SelectMorphMesh"] = new() { ["en"] = "Select a morph mesh", ["ja"] = "モーフメッシュを選択" },
            ["BaseMeshNotFound"] = new() { ["en"] = "Base mesh not found", ["ja"] = "ベースメッシュが見つかりません" },
            ["MorphDataInvalid"] = new() { ["en"] = "MorphBaseData is invalid", ["ja"] = "MorphBaseDataが無効です" },
            ["PreviewActive"] = new() { ["en"] = "Preview active - changes are temporary", ["ja"] = "プレビュー中 - 変更は一時的です" },
            ["NoValidPairs"] = new() { ["en"] = "No valid morph-base pairs found", ["ja"] = "有効なモーフ-ベースペアがありません" },

            // セクション
            ["SectionMorphSets"] = new() { ["en"] = "Morph Sets", ["ja"] = "モーフセット" },
            ["SectionPreview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["SectionDetail"] = new() { ["en"] = "Detail", ["ja"] = "詳細" },
            ["SectionInfo"] = new() { ["en"] = "Information", ["ja"] = "情報" },

            // ラベル
            ["MorphSet"] = new() { ["en"] = "Morph Set", ["ja"] = "モーフセット" },
            ["MorphMesh"] = new() { ["en"] = "Morph Mesh", ["ja"] = "モーフメッシュ" },
            ["BaseMesh"] = new() { ["en"] = "Base Mesh", ["ja"] = "ベースメッシュ" },
            ["Weight"] = new() { ["en"] = "Weight", ["ja"] = "ウェイト" },
            ["Type"] = new() { ["en"] = "Type", ["ja"] = "タイプ" },
            ["Panel"] = new() { ["en"] = "Panel", ["ja"] = "パネル" },
            ["MeshCount"] = new() { ["en"] = "Meshes", ["ja"] = "メッシュ数" },
            ["VertexOffsets"] = new() { ["en"] = "Vertex Offsets", ["ja"] = "頂点オフセット" },
            ["UVOffsets"] = new() { ["en"] = "UV Offsets", ["ja"] = "UVオフセット" },
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点数" },
            ["DetailMode"] = new() { ["en"] = "Detail Mode", ["ja"] = "詳細モード" },
            ["TargetMeshes"] = new() { ["en"] = "Target", ["ja"] = "対象" },

            // パネル名
            ["Panel_0"] = new() { ["en"] = "Brow", ["ja"] = "眉" },
            ["Panel_1"] = new() { ["en"] = "Eye", ["ja"] = "目" },
            ["Panel_2"] = new() { ["en"] = "Mouth", ["ja"] = "口" },
            ["Panel_3"] = new() { ["en"] = "Other", ["ja"] = "その他" },

            // ボタン
            ["EndPreview"] = new() { ["en"] = "End Preview", ["ja"] = "プレビュー終了" },
            ["ResetWeight"] = new() { ["en"] = "Reset", ["ja"] = "リセット" },

            // その他
            ["None"] = new() { ["en"] = "(None)", ["ja"] = "(なし)" },
            ["AllMeshes"] = new() { ["en"] = "All ({0} meshes)", ["ja"] = "すべて ({0}メッシュ)" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        private string GetPanelName(int panel)
        {
            return panel switch
            {
                0 => T("Panel_0"),
                1 => T("Panel_1"),
                2 => T("Panel_2"),
                _ => T("Panel_3")
            };
        }

        // ================================================================
        // プレビュー状態
        // ================================================================

        private bool _isPreviewActive = false;
        private bool _isDragging = false;

        // 複数ベースメッシュのバックアップ (baseMeshIndex -> positions)
        private Dictionary<int, Vector3[]> _previewBackups = new Dictionary<int, Vector3[]>();

        // 現在のプレビュー対象ペア (morphMeshIndex, baseMeshIndex)
        private List<(int morphIndex, int baseIndex)> _previewPairs = new List<(int, int)>();

        // プレビュー対象のモーフセットインデックス
        private int _previewMorphSetIndex = -1;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MorphPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(300, 400);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning())
                return;

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            if (!model.HasMorphSets)
            {
                EditorGUILayout.HelpBox(T("NoMorphSets"), MessageType.Info);
                return;
            }

            // ================================================================
            // モーフセット選択
            // ================================================================
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("SectionMorphSets"), EditorStyles.boldLabel);

            // モーフセットドロップダウン
            var setOptions = BuildMorphSetOptions(model);
            int newSetIndex = DrawDropdown(T("MorphSet"), _settings.SelectedMorphSetIndex, setOptions);
            if (newSetIndex != _settings.SelectedMorphSetIndex)
            {
                EndPreview();
                _settings.SelectedMorphSetIndex = newSetIndex;
                _settings.SelectedMorphMeshIndex = -1;
                _settings.PreviewWeight = 0f;
            }

            if (_settings.SelectedMorphSetIndex < 0 || _settings.SelectedMorphSetIndex >= model.MorphSetCount)
            {
                EditorGUILayout.HelpBox(T("SelectMorphSet"), MessageType.Info);
                return;
            }

            var selectedSet = model.MorphSets[_settings.SelectedMorphSetIndex];

            // セット情報表示
            EditorGUILayout.LabelField($"{T("Type")}: {selectedSet.Type}");
            EditorGUILayout.LabelField($"{T("Panel")}: {GetPanelName(selectedSet.Panel)}");
            EditorGUILayout.LabelField($"{T("MeshCount")}: {selectedSet.MeshCount}");

            EditorGUILayout.Space(10);

            // ================================================================
            // プレビュー（基本モード：まとめて適用）
            // ================================================================
            EditorGUILayout.LabelField(T("SectionPreview"), EditorStyles.boldLabel);

            // モーフ-ベースペアを構築
            var pairs = BuildMorphBasePairs(model, selectedSet);
            if (pairs.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoValidPairs"), MessageType.Warning);
                return;
            }

            // 対象メッシュ数表示
            EditorGUILayout.LabelField($"{T("TargetMeshes")}: {string.Format(T("AllMeshes"), pairs.Count)}");

            if (_isPreviewActive)
            {
                EditorGUILayout.HelpBox(T("PreviewActive"), MessageType.Info);
            }

            // プレビュー開始/更新
            if (!_isPreviewActive || _previewMorphSetIndex != _settings.SelectedMorphSetIndex)
            {
                StartBatchPreview(model, pairs);
            }

            // ウェイトスライダー
            EditorGUI.BeginChangeCheck();
            float newWeight = EditorGUILayout.Slider(T("Weight"), _settings.PreviewWeight, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                _isDragging = true;
                _settings.PreviewWeight = newWeight;
                ApplyBatchPreview(model, newWeight);
            }

            // マウスアップ検出
            if (_isDragging && Event.current.type == EventType.MouseUp)
            {
                _isDragging = false;
            }

            EditorGUILayout.Space(5);

            // ボタン
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(T("ResetWeight")))
            {
                _settings.PreviewWeight = 0f;
                ApplyBatchPreview(model, 0f);
            }

            if (GUILayout.Button(T("EndPreview")))
            {
                EndPreview();
                _settings.SelectedMorphSetIndex = -1;
                _settings.SelectedMorphMeshIndex = -1;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // ================================================================
            // 詳細モード切り替え
            // ================================================================
            _settings.DetailMode = EditorGUILayout.Toggle(T("DetailMode"), _settings.DetailMode);

            if (_settings.DetailMode)
            {
                DrawDetailMode(model, selectedSet, pairs);
            }
        }

        // ================================================================
        // 詳細モードUI
        // ================================================================

        private void DrawDetailMode(ModelContext model, MorphSet selectedSet, List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx)> pairs)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("SectionDetail"), EditorStyles.boldLabel);

            // モーフメッシュ選択
            var meshOptions = BuildMorphMeshOptions(model, selectedSet);
            int newMeshIndex = DrawDropdown(T("MorphMesh"), _settings.SelectedMorphMeshIndex, meshOptions);
            if (newMeshIndex != _settings.SelectedMorphMeshIndex)
            {
                _settings.SelectedMorphMeshIndex = newMeshIndex;
            }

            if (_settings.SelectedMorphMeshIndex < 0 || _settings.SelectedMorphMeshIndex >= selectedSet.MeshIndices.Count)
            {
                EditorGUILayout.HelpBox(T("SelectMorphMesh"), MessageType.Info);
                return;
            }

            // 選択中のペアを検索
            int morphMeshIndex = selectedSet.MeshIndices[_settings.SelectedMorphMeshIndex];
            var selectedPair = pairs.Find(p => p.morphIndex == morphMeshIndex);

            if (selectedPair.morphCtx == null)
            {
                EditorGUILayout.HelpBox(T("MorphDataInvalid"), MessageType.Error);
                return;
            }

            // ベースメッシュ表示
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(T("BaseMesh"), selectedPair.baseCtx?.Name ?? T("BaseMeshNotFound"));
            }

            if (selectedPair.baseCtx == null)
            {
                EditorGUILayout.HelpBox(T("BaseMeshNotFound"), MessageType.Error);
                return;
            }

            EditorGUILayout.Space(5);

            // 情報表示
            EditorGUILayout.LabelField(T("SectionInfo"), EditorStyles.boldLabel);

            var morphMesh = selectedPair.morphCtx.MeshObject;
            var baseMesh = selectedPair.baseCtx.MeshObject;

            EditorGUILayout.LabelField($"{T("Vertices")}: Base={baseMesh.VertexCount}, Morph={morphMesh.VertexCount}");

            var vertexOffsets = selectedPair.morphCtx.GetMorphOffsets();
            EditorGUILayout.LabelField($"{T("VertexOffsets")}: {vertexOffsets.Count}");

            if (selectedSet.IsUVMorph)
            {
                var uvOffsets = selectedPair.morphCtx.GetUVMorphOffsets();
                EditorGUILayout.LabelField($"{T("UVOffsets")}: {uvOffsets.Count}");
            }
        }

        // ================================================================
        // モーフ-ベースペア構築
        // ================================================================

        private List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx)> BuildMorphBasePairs(ModelContext model, MorphSet set)
        {
            var pairs = new List<(int, int, MeshContext, MeshContext)>();

            for (int i = 0; i < set.MeshIndices.Count; i++)
            {
                int morphIndex = set.MeshIndices[i];
                var morphCtx = model.GetMeshContext(morphIndex);

                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIndex = FindBaseMeshIndex(model, morphCtx);
                var baseCtx = baseIndex >= 0 ? model.GetMeshContext(baseIndex) : null;

                if (baseCtx?.MeshObject != null)
                {
                    pairs.Add((morphIndex, baseIndex, morphCtx, baseCtx));
                }
            }

            return pairs;
        }

        // ================================================================
        // ドロップダウン
        // ================================================================

        private List<(int index, string name)> BuildMorphSetOptions(ModelContext model)
        {
            var options = new List<(int, string)>();
            for (int i = 0; i < model.MorphSetCount; i++)
            {
                var set = model.MorphSets[i];
                options.Add((i, $"{set.Name} ({set.Type}, {set.MeshCount})"));
            }
            return options;
        }

        private List<(int index, string name)> BuildMorphMeshOptions(ModelContext model, MorphSet set)
        {
            var options = new List<(int, string)>();
            for (int i = 0; i < set.MeshIndices.Count; i++)
            {
                int meshIndex = set.MeshIndices[i];
                var ctx = model.GetMeshContext(meshIndex);
                string name = ctx?.Name ?? $"[{meshIndex}]";
                options.Add((i, name));
            }
            return options;
        }

        private int DrawDropdown(string label, int currentIndex, List<(int index, string name)> options)
        {
            var displayOptions = new string[options.Count + 1];
            displayOptions[0] = T("None");

            int selectedPopupIndex = 0;

            for (int i = 0; i < options.Count; i++)
            {
                displayOptions[i + 1] = options[i].name;
                if (options[i].index == currentIndex)
                {
                    selectedPopupIndex = i + 1;
                }
            }

            int newPopupIndex = EditorGUILayout.Popup(label, selectedPopupIndex, displayOptions);

            if (newPopupIndex == 0)
            {
                return -1;
            }
            else
            {
                return options[newPopupIndex - 1].index;
            }
        }

        // ================================================================
        // ベースメッシュ検索
        // ================================================================

        private int FindBaseMeshIndex(ModelContext model, MeshContext morphMeshContext)
        {
            if (morphMeshContext == null) return -1;

            string morphName = morphMeshContext.MorphName;
            string meshName = morphMeshContext.Name;

            // パターン1: "ベース名_モーフ名" から "ベース名" を抽出
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);

                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var ctx = model.GetMeshContext(i);
                    if (ctx != null && ctx.Type == MeshType.Mesh && ctx.Name == baseName)
                    {
                        return i;
                    }
                }
            }

            // パターン2: 同じ頂点数のMeshタイプを検索（最初に見つかったもの）
            int morphVertexCount = morphMeshContext.MeshObject?.VertexCount ?? 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx != null && ctx.Type == MeshType.Mesh &&
                    ctx.MeshObject != null && ctx.MeshObject.VertexCount == morphVertexCount)
                {
                    return i;
                }
            }

            return -1;
        }

        // ================================================================
        // バッチプレビュー（複数メッシュ同時）
        // ================================================================

        private void StartBatchPreview(ModelContext model, List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx)> pairs)
        {
            EndPreview();

            _previewBackups.Clear();
            _previewPairs.Clear();

            foreach (var (morphIndex, baseIndex, morphCtx, baseCtx) in pairs)
            {
                if (baseCtx?.MeshObject == null) continue;

                // 同じベースメッシュが複数のモーフから参照される場合は1回だけバックアップ
                if (!_previewBackups.ContainsKey(baseIndex))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                    {
                        backup[i] = baseMesh.Vertices[i].Position;
                    }
                    _previewBackups[baseIndex] = backup;
                }

                _previewPairs.Add((morphIndex, baseIndex));
            }

            _previewMorphSetIndex = _settings.SelectedMorphSetIndex;
            _isPreviewActive = true;

            Debug.Log($"[MorphPanel] Batch preview started: {_previewPairs.Count} pairs, {_previewBackups.Count} base meshes");
        }

        private void ApplyBatchPreview(ModelContext model, float weight)
        {
            if (!_isPreviewActive || _previewBackups.Count == 0) return;

            // まず全ベースメッシュをバックアップから復元
            foreach (var (baseIndex, backup) in _previewBackups)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                {
                    baseMesh.Vertices[i].Position = backup[i];
                }
            }

            // 全モーフのオフセットを適用
            foreach (var (morphIndex, baseIndex) in _previewPairs)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                var baseCtx = model.GetMeshContext(baseIndex);

                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                var offsets = morphCtx.GetMorphOffsets();

                foreach (var (vertexIndex, offset) in offsets)
                {
                    if (vertexIndex < baseMesh.VertexCount)
                    {
                        baseMesh.Vertices[vertexIndex].Position += offset * weight;
                    }
                }
            }

            // GPUバッファ更新（各ベースメッシュ）
            foreach (var baseIndex in _previewBackups.Keys)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx != null)
                {
                    _context?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }

            _context?.Repaint?.Invoke();
        }

        private void EndPreview()
        {
            if (!_isPreviewActive || _previewBackups.Count == 0)
            {
                _isPreviewActive = false;
                _previewBackups.Clear();
                _previewPairs.Clear();
                _previewMorphSetIndex = -1;
                return;
            }

            var model = Model;
            if (model != null)
            {
                // 全ベースメッシュを復元
                foreach (var (baseIndex, backup) in _previewBackups)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;

                    var baseMesh = baseCtx.MeshObject;
                    int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                    {
                        baseMesh.Vertices[i].Position = backup[i];
                    }

                    _context?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }

                Debug.Log($"[MorphPanel] Batch preview ended: restored {_previewBackups.Count} base meshes");
            }

            _previewBackups.Clear();
            _previewPairs.Clear();
            _previewMorphSetIndex = -1;
            _isPreviewActive = false;

            _context?.Repaint?.Invoke();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable()
        {
            EndPreview();
        }

        private void OnDestroy()
        {
            EndPreview();
        }

        protected override void OnContextSet()
        {
            EndPreview();
            _settings.SelectedMorphSetIndex = -1;
            _settings.SelectedMorphMeshIndex = -1;
            _settings.PreviewWeight = 0f;
        }
    }
}
