// Assets/Editor/Poly_Ling/Tools/Panels/ModelBlendPanel.cs
// モデルブレンドパネル
// プロジェクト内の複数モデルをブレンドしてカレントモデルに適用
// 全メッシュを丸ごとブレンド（同じメッシュ数・頂点数を前提）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// モデルブレンドパネル設定
    /// </summary>
    [Serializable]
    public class ModelBlendSettings : IToolSettings
    {
        /// <summary>各モデルのブレンドウェイト（モデルインデックス順）</summary>
        public List<float> Weights = new List<float>();

        /// <summary>適用後に法線を再計算</summary>
        public bool RecalculateNormals = true;

        /// <summary>リアルタイムプレビュー</summary>
        public bool RealtimePreview = true;

        public IToolSettings Clone()
        {
            return new ModelBlendSettings
            {
                Weights = new List<float>(this.Weights),
                RecalculateNormals = this.RecalculateNormals,
                RealtimePreview = this.RealtimePreview
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not ModelBlendSettings o)
                return true;

            if (Weights.Count != o.Weights.Count)
                return true;

            for (int i = 0; i < Weights.Count; i++)
            {
                if (!Mathf.Approximately(Weights[i], o.Weights[i]))
                    return true;
            }

            return RecalculateNormals != o.RecalculateNormals ||
                   RealtimePreview != o.RealtimePreview;
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not ModelBlendSettings o)
                return;

            Weights = new List<float>(o.Weights);
            RecalculateNormals = o.RecalculateNormals;
            RealtimePreview = o.RealtimePreview;
        }
    }

    /// <summary>
    /// モデルブレンドパネル
    /// プロジェクト内の全モデルをウェイト付きでブレンド
    /// </summary>
    public class ModelBlendPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "ModelBlend";
        public override string Title => "Model Blend";

        private ModelBlendSettings _settings = new ModelBlendSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Model Blend", ["ja"] = "モデルブレンド", ["hi"] = "もでるぶれんど" },

            // メッセージ
            ["NoProject"] = new() { ["en"] = "No project available", ["ja"] = "プロジェクトがありません", ["hi"] = "ぷろじぇくとがない" },
            ["NeedMultipleModels"] = new() { ["en"] = "Need 2+ models in project", ["ja"] = "2つ以上のモデルが必要", ["hi"] = "2こいじょうひつよう" },
            ["MeshCountMismatch"] = new() { ["en"] = "Mesh count differs between models", ["ja"] = "モデル間でメッシュ数が異なります", ["hi"] = "めっしゅすうがちがう" },
            ["VertexCountMismatch"] = new() { ["en"] = "Vertex count differs in mesh [{0}]: min={1}", ["ja"] = "メッシュ[{0}]の頂点数が異なります: 最小={1}", ["hi"] = "[{0}]てんすうちがう" },

            // ラベル
            ["Target"] = new() { ["en"] = "Target (Current Model)", ["ja"] = "ターゲット（カレントモデル）", ["hi"] = "たーげっと" },
            ["Models"] = new() { ["en"] = "Models", ["ja"] = "モデル", ["hi"] = "もでる" },
            ["Weight"] = new() { ["en"] = "Weight", ["ja"] = "ウェイト", ["hi"] = "うぇいと" },
            ["TotalWeight"] = new() { ["en"] = "Total", ["ja"] = "合計", ["hi"] = "ごうけい" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算", ["hi"] = "ほうせん" },
            ["RealtimePreview"] = new() { ["en"] = "Realtime preview", ["ja"] = "リアルタイムプレビュー", ["hi"] = "りあるたいむ" },
            ["Meshes"] = new() { ["en"] = "meshes", ["ja"] = "メッシュ", ["hi"] = "めっしゅ" },
            ["Vertices"] = new() { ["en"] = "V", ["ja"] = "V", ["hi"] = "V" },

            // ボタン
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用", ["hi"] = "てきよう" },
            ["Normalize"] = new() { ["en"] = "Normalize", ["ja"] = "正規化", ["hi"] = "せいきか" },
            ["EqualWeights"] = new() { ["en"] = "Equal", ["ja"] = "均等", ["hi"] = "きんとう" },
            ["ResetFirst"] = new() { ["en"] = "Reset to 1st", ["ja"] = "1番目にリセット", ["hi"] = "りせっと" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // UI状態
        // ================================================================

        private bool _isDragging = false;
        private Vector2 _scrollPosition;

        // ================================================================
        // オリジナル頂点位置キャッシュ
        // [modelIndex][drawableIndex][vertexIndex] → Position
        // ================================================================

        private List<List<Vector3[]>> _originalPositions = new List<List<Vector3[]>>();
        private bool _cacheValid = false;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<ModelBlendPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(350, 300);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private int _lastModelCount = -1;

        private void OnGUI()
        {
            if (!DrawNoContextWarning())
                return;

            var project = _context?.Project;
            if (project == null)
            {
                EditorGUILayout.HelpBox(T("NoProject"), MessageType.Warning);
                return;
            }

            int modelCount = project.ModelCount;
            if (modelCount < 2)
            {
                EditorGUILayout.HelpBox(T("NeedMultipleModels"), MessageType.Info);
                return;
            }

            // モデル数が変わったらキャッシュを無効化
            if (_lastModelCount != modelCount)
            {
                InvalidateCache();
                _lastModelCount = modelCount;
            }

            // ウェイトリストを同期
            SyncWeightsToModelCount(modelCount);

            var currentModel = project.CurrentModel;
            int currentModelIndex = project.CurrentModelIndex;

            // ターゲット表示（Drawableメッシュ数を表示）
            int drawableCount = currentModel?.DrawableMeshes?.Count ?? 0;
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Target"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField($"{currentModel?.Name ?? "---"} ({drawableCount} {T("Meshes")})");
            }

            EditorGUILayout.Space(10);

            // オプション
            _settings.RecalculateNormals = EditorGUILayout.Toggle(T("RecalcNormals"), _settings.RecalculateNormals);
            _settings.RealtimePreview = EditorGUILayout.Toggle(T("RealtimePreview"), _settings.RealtimePreview);

            EditorGUILayout.Space(10);

            // モデルリストとスライダー
            EditorGUILayout.LabelField(T("Models"), EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(250));

            // 互換性チェック用（Drawableメッシュ数で比較）
            int baseDrawableCount = currentModel?.DrawableMeshes?.Count ?? 0;
            bool hasMismatch = false;
            List<string> mismatchMessages = new List<string>();

            for (int i = 0; i < modelCount; i++)
            {
                var model = project.GetModel(i);
                if (model == null) continue;

                bool isCurrent = (i == currentModelIndex);
                int modelDrawableCount = model.DrawableMeshes?.Count ?? 0;

                EditorGUILayout.BeginHorizontal();

                // モデル情報
                string modelLabel = isCurrent
                    ? $"★ {model.Name}"
                    : model.Name;

                EditorGUILayout.LabelField(modelLabel, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{modelDrawableCount} {T("Meshes")}", EditorStyles.miniLabel, GUILayout.Width(70));

                // Drawableメッシュ数チェック
                if (modelDrawableCount != baseDrawableCount)
                {
                    hasMismatch = true;
                }

                // ウェイトスライダー
                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.Slider(_settings.Weights[i], 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!_isDragging)
                    {
                        _isDragging = true;
                        // 全メッシュのスナップショットを取る（Undo用）
                        CaptureAllMeshSnapshots();
                        // オリジナル頂点位置をキャッシュ（プレビュー用）
                        BuildOriginalPositionCache(project);
                    }

                    _settings.Weights[i] = newWeight;

                    if (_settings.RealtimePreview)
                        ApplyBlendPreview(project);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // マウスアップ検出
            if (_isDragging && Event.current.type == EventType.MouseUp)
            {
                _isDragging = false;
                // キャッシュを無効化（次回ドラッグ時に再構築）
                InvalidateCache();
                if (_settings.RecalculateNormals)
                {
                    RecalculateAllNormals(currentModel);
                    _context?.SyncMesh?.Invoke();
                    _context?.Repaint?.Invoke();
                }
            }

            // 警告表示
            if (hasMismatch)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(T("MeshCountMismatch"), MessageType.Warning);
            }

            // 頂点数チェック
            var vertexMismatches = CheckVertexCounts(project);
            foreach (var msg in vertexMismatches)
            {
                EditorGUILayout.HelpBox(msg, MessageType.Warning);
            }

            // 合計ウェイト表示
            float totalWeight = _settings.Weights.Sum();
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"{T("TotalWeight")}: {totalWeight:F3}");

            EditorGUILayout.Space(10);

            // ボタン行
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(T("EqualWeights")))
            {
                SetEqualWeights();
                if (_settings.RealtimePreview)
                    ApplyBlendPreview(project);
            }

            if (GUILayout.Button(T("Normalize")))
            {
                NormalizeWeights();
                if (_settings.RealtimePreview)
                    ApplyBlendPreview(project);
            }

            if (GUILayout.Button(T("ResetFirst")))
            {
                ResetToFirstModel();
                if (_settings.RealtimePreview)
                    ApplyBlendPreview(project);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 適用ボタン
            if (GUILayout.Button(T("Apply"), GUILayout.Height(30)))
            {
                ApplyBlend(project);
            }
        }

        // ================================================================
        // ブレンド処理
        // ================================================================

        private void ApplyBlend(ProjectContext project)
        {
            var currentModel = project.CurrentModel;
            if (currentModel == null) return;

            // 正規化されたウェイト
            float[] normalizedWeights = NormalizeWeightArray(_settings.Weights);

            // 全メッシュのスナップショットを記録
            CaptureAllMeshSnapshots();

            // Drawableメッシュのみを対象にブレンド
            var targetDrawables = currentModel.DrawableMeshes;
            int drawableCount = targetDrawables.Count;

            for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
            {
                var targetEntry = targetDrawables[drawIdx];
                var targetMesh = targetEntry.Context?.MeshObject;
                if (targetMesh == null) continue;

                // 各モデルから対応するDrawableメッシュを収集
                var sourceMeshes = new List<MeshObject>();
                var weights = new List<float>();

                for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
                {
                    var model = project.GetModel(modelIdx);
                    if (model == null) continue;

                    var modelDrawables = model.DrawableMeshes;
                    if (drawIdx >= modelDrawables.Count) continue;

                    var srcMesh = modelDrawables[drawIdx].Context?.MeshObject;
                    if (srcMesh != null)
                    {
                        sourceMeshes.Add(srcMesh);
                        weights.Add(normalizedWeights[modelIdx]);
                    }
                }

                if (sourceMeshes.Count == 0) continue;

                // 頂点数の最小値
                int minVertexCount = sourceMeshes.Min(m => m.VertexCount);
                minVertexCount = Mathf.Min(minVertexCount, targetMesh.VertexCount);

                // ブレンド実行
                BlendVertices(sourceMeshes.ToArray(), weights.ToArray(), targetMesh, minVertexCount);

                // 法線再計算
                if (_settings.RecalculateNormals)
                {
                    targetMesh.RecalculateSmoothNormals();
                }
            }

            // Unity Meshに反映
            _context?.SyncMesh?.Invoke();
            _context?.Repaint?.Invoke();
            Repaint();
        }

        private void ApplyBlendPreview(ProjectContext project)
        {
            var currentModel = project.CurrentModel;
            if (currentModel == null) return;

            // キャッシュが無効な場合は構築
            if (!_cacheValid)
            {
                BuildOriginalPositionCache(project);
            }

            // 正規化されたウェイト
            float[] normalizedWeights = NormalizeWeightArray(_settings.Weights);

            // Drawableメッシュのみを対象にブレンド
            var targetDrawables = currentModel.DrawableMeshes;
            int drawableCount = targetDrawables.Count;

            for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
            {
                var targetEntry = targetDrawables[drawIdx];
                var targetMesh = targetEntry.Context?.MeshObject;
                if (targetMesh == null) continue;

                // キャッシュからオリジナル位置を収集してブレンド
                var sourcePositions = new List<Vector3[]>();
                var weights = new List<float>();

                for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
                {
                    if (modelIdx >= _originalPositions.Count) continue;
                    if (drawIdx >= _originalPositions[modelIdx].Count) continue;

                    var positions = _originalPositions[modelIdx][drawIdx];
                    if (positions != null)
                    {
                        sourcePositions.Add(positions);
                        weights.Add(normalizedWeights[modelIdx]);
                    }
                }

                if (sourcePositions.Count == 0) continue;

                // 頂点数の最小値
                int minVertexCount = sourcePositions.Min(p => p.Length);
                minVertexCount = Mathf.Min(minVertexCount, targetMesh.VertexCount);

                // ブレンド実行（キャッシュから）
                BlendVerticesFromCache(sourcePositions, weights.ToArray(), targetMesh, minVertexCount);
            }

            // Unity Meshに反映
            _context?.SyncMesh?.Invoke();
            _context?.Repaint?.Invoke();
        }

        private void BlendVertices(MeshObject[] sources, float[] weights, MeshObject target, int vertexCount)
        {
            // ウェイトを正規化
            float totalWeight = weights.Sum();
            if (totalWeight <= 0f) return;

            float[] normalizedWeights = weights.Select(w => w / totalWeight).ToArray();

            for (int vi = 0; vi < vertexCount; vi++)
            {
                Vector3 blendedPos = Vector3.zero;

                for (int si = 0; si < sources.Length; si++)
                {
                    if (vi < sources[si].VertexCount)
                    {
                        blendedPos += sources[si].Vertices[vi].Position * normalizedWeights[si];
                    }
                }

                target.Vertices[vi].Position = blendedPos;
            }
        }

        private void BlendVerticesFromCache(List<Vector3[]> sourcePositions, float[] weights, MeshObject target, int vertexCount)
        {
            // ウェイトを正規化
            float totalWeight = weights.Sum();
            if (totalWeight <= 0f) return;

            float[] normalizedWeights = weights.Select(w => w / totalWeight).ToArray();

            for (int vi = 0; vi < vertexCount; vi++)
            {
                Vector3 blendedPos = Vector3.zero;

                for (int si = 0; si < sourcePositions.Count; si++)
                {
                    if (vi < sourcePositions[si].Length)
                    {
                        blendedPos += sourcePositions[si][vi] * normalizedWeights[si];
                    }
                }

                target.Vertices[vi].Position = blendedPos;
            }
        }

        // ================================================================
        // オリジナル頂点位置キャッシュ
        // ================================================================

        private void BuildOriginalPositionCache(ProjectContext project)
        {
            _originalPositions.Clear();

            for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
            {
                var model = project.GetModel(modelIdx);
                var modelPositions = new List<Vector3[]>();

                if (model != null)
                {
                    var drawables = model.DrawableMeshes;
                    foreach (var entry in drawables)
                    {
                        var mesh = entry.Context?.MeshObject;
                        if (mesh != null)
                        {
                            // 頂点位置をコピー
                            var positions = new Vector3[mesh.VertexCount];
                            for (int vi = 0; vi < mesh.VertexCount; vi++)
                            {
                                positions[vi] = mesh.Vertices[vi].Position;
                            }
                            modelPositions.Add(positions);
                        }
                        else
                        {
                            modelPositions.Add(new Vector3[0]);
                        }
                    }
                }

                _originalPositions.Add(modelPositions);
            }

            _cacheValid = true;
            Debug.Log($"[ModelBlend] Built position cache: {_originalPositions.Count} models");
        }

        private void InvalidateCache()
        {
            _cacheValid = false;
            _originalPositions.Clear();
        }

        // ================================================================
        // ウェイト操作
        // ================================================================

        private void SyncWeightsToModelCount(int modelCount)
        {
            while (_settings.Weights.Count < modelCount)
            {
                // 新しいモデルには均等ウェイトを設定
                _settings.Weights.Add(1f / modelCount);
            }
            while (_settings.Weights.Count > modelCount)
            {
                _settings.Weights.RemoveAt(_settings.Weights.Count - 1);
            }
        }

        private void NormalizeWeights()
        {
            float total = _settings.Weights.Sum();
            if (total <= 0f)
            {
                SetEqualWeights();
                return;
            }

            for (int i = 0; i < _settings.Weights.Count; i++)
            {
                _settings.Weights[i] /= total;
            }
        }

        private void SetEqualWeights()
        {
            int count = _settings.Weights.Count;
            if (count == 0) return;

            float equalWeight = 1f / count;
            for (int i = 0; i < count; i++)
            {
                _settings.Weights[i] = equalWeight;
            }
        }

        private float[] NormalizeWeightArray(List<float> weights)
        {
            float total = weights.Sum();
            if (total <= 0f)
            {
                float eq = 1f / weights.Count;
                return Enumerable.Repeat(eq, weights.Count).ToArray();
            }

            return weights.Select(w => w / total).ToArray();
        }

        private void ResetToFirstModel()
        {
            for (int i = 0; i < _settings.Weights.Count; i++)
            {
                _settings.Weights[i] = (i == 0) ? 1f : 0f;
            }
        }

        // ================================================================
        // Undo用スナップショット
        // ================================================================

        private void CaptureAllMeshSnapshots()
        {
            // UndoControllerがある場合は全メッシュのスナップショットを取る
            _context?.UndoController?.CaptureMeshObjectSnapshot();
        }

        private void RecalculateAllNormals(ModelContext model)
        {
            if (model == null) return;

            // Drawableメッシュのみ法線再計算
            var drawables = model.DrawableMeshes;
            for (int i = 0; i < drawables.Count; i++)
            {
                var mesh = drawables[i].Context?.MeshObject;
                mesh?.RecalculateSmoothNormals();
            }
        }

        // ================================================================
        // 互換性チェック
        // ================================================================

        private List<string> CheckVertexCounts(ProjectContext project)
        {
            var messages = new List<string>();
            var currentModel = project.CurrentModel;
            if (currentModel == null) return messages;

            var targetDrawables = currentModel.DrawableMeshes;
            int drawableCount = targetDrawables.Count;

            for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
            {
                var baseMesh = targetDrawables[drawIdx].Context?.MeshObject;
                if (baseMesh == null) continue;

                int baseVertexCount = baseMesh.VertexCount;
                int minVertexCount = baseVertexCount;

                for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
                {
                    if (modelIdx == project.CurrentModelIndex) continue;

                    var model = project.GetModel(modelIdx);
                    if (model == null) continue;

                    var modelDrawables = model.DrawableMeshes;
                    if (drawIdx >= modelDrawables.Count) continue;

                    var srcMesh = modelDrawables[drawIdx].Context?.MeshObject;
                    if (srcMesh != null && srcMesh.VertexCount != baseVertexCount)
                    {
                        minVertexCount = Mathf.Min(minVertexCount, srcMesh.VertexCount);
                    }
                }

                if (minVertexCount < baseVertexCount)
                {
                    messages.Add(T("VertexCountMismatch", drawIdx, minVertexCount));
                }
            }

            return messages;
        }

        // ================================================================
        // コンテキスト変更時
        // ================================================================

        protected override void OnContextSet()
        {
            // 前のプロジェクトのイベント解除
            UnsubscribeFromProject();

            // ウェイトリストをリセット
            _settings.Weights.Clear();
            // キャッシュを無効化
            InvalidateCache();
            // モデル数カウントをリセット
            _lastModelCount = -1;

            // 新しいプロジェクトのイベント購読
            SubscribeToProject();
        }

        private void SubscribeToProject()
        {
            var project = _context?.Project;
            if (project != null)
            {
                project.OnModelsChanged += OnModelsChanged;
            }
        }

        private void UnsubscribeFromProject()
        {
            var project = _context?.Project;
            if (project != null)
            {
                project.OnModelsChanged -= OnModelsChanged;
            }
        }

        private void OnModelsChanged()
        {
            InvalidateCache();
            _lastModelCount = -1;
            Repaint();
        }

        private void OnDestroy()
        {
            UnsubscribeFromProject();
        }
    }
}