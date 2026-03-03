// MultiModelBlendPanel.cs
// モデルブレンドパネル (UIToolkit)
// プロジェクト内の複数モデルをブレンドしてカレントモデルに適用

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    /// <summary>
    /// モデルブレンドパネル（UIToolkit版）
    /// </summary>
    public class MultiModelBlendPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MultiModelBlendPanel/MultiModelBlendPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MultiModelBlendPanel/MultiModelBlendPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MultiModelBlendPanel/MultiModelBlendPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MultiModelBlendPanel/MultiModelBlendPanel.uss";

        // ================================================================
        // ローカライズ
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Model Blend", ["ja"] = "モデルブレンド" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["NoProject"] = new() { ["en"] = "No project available", ["ja"] = "プロジェクトがありません" },
            ["NeedMultipleModels"] = new() { ["en"] = "Need 2+ models in project", ["ja"] = "2つ以上のモデルが必要" },
            ["MeshCountMismatch"] = new() { ["en"] = "Mesh count differs between models", ["ja"] = "モデル間でメッシュ数が異なります" },
            ["VertexCountMismatch"] = new() { ["en"] = "Vertex count differs in mesh [{0}]: min={1}", ["ja"] = "メッシュ[{0}]の頂点数が異なります: 最小={1}" },
            ["Target"] = new() { ["en"] = "Target (Current Model)", ["ja"] = "ターゲット（カレントモデル）" },
            ["Models"] = new() { ["en"] = "Models", ["ja"] = "モデル" },
            ["TotalWeight"] = new() { ["en"] = "Total", ["ja"] = "合計" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算" },
            ["RealtimePreview"] = new() { ["en"] = "Realtime preview", ["ja"] = "リアルタイムプレビュー" },
            ["BlendMeshes"] = new() { ["en"] = "Blend Meshes", ["ja"] = "合成メッシュ" },
            ["All"] = new() { ["en"] = "All", ["ja"] = "全て" },
            ["None"] = new() { ["en"] = "None", ["ja"] = "なし" },
            ["Meshes"] = new() { ["en"] = "meshes", ["ja"] = "メッシュ" },
            ["Apply"] = new() { ["en"] = "Apply", ["ja"] = "適用" },
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["Normalize"] = new() { ["en"] = "Normalize", ["ja"] = "正規化" },
            ["EqualWeights"] = new() { ["en"] = "Equal", ["ja"] = "均等" },
            ["ResetFirst"] = new() { ["en"] = "Reset to 1st", ["ja"] = "1番目にリセット" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // Settings
        // ================================================================

        private List<float> _weights = new List<float>();
        private bool _recalculateNormals = true;
        private bool _realtimePreview = false;

        // ================================================================
        // 状態
        // ================================================================

        private bool _isDragging = false;
        private MultiMeshVertexSnapshot _dragBeforeSnapshot;
        private List<bool> _meshEnabled = new List<bool>();
        private int _lastModelCount = -1;

        // オリジナル頂点位置キャッシュ
        private List<List<Vector3[]>> _originalPositions = new List<List<Vector3[]>>();
        private bool _cacheValid = false;

        // スライダー参照（ドラッグ終了検出用）
        private List<Slider> _weightSliders = new List<Slider>();

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private ScrollView _mainContent;
        private Label _targetHeader, _targetInfoLabel;
        private Foldout _foldoutMeshSelection;
        private Button _btnMeshAll, _btnMeshNone;
        private ScrollView _meshToggleScroll;
        private Toggle _toggleRecalcNormals, _toggleRealtimePreview;
        private Label _modelsHeader;
        private VisualElement _modelSlidersContainer;
        private VisualElement _warningsContainer;
        private Label _totalWeightLabel;
        private Button _btnEqual, _btnNormalize, _btnResetFirst;
        private Button _btnPreview, _btnApply;

        // ================================================================
        // Open
        // ================================================================

        public static MultiModelBlendPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<MultiModelBlendPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(350, 300);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();

        private void OnDestroy()
        {
            UnsubscribeFromProject();
            Cleanup();
        }

        private void Cleanup()
        {
            InvalidateCache();
        }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromProject();
            _toolContext = ctx;
            _weights.Clear();
            InvalidateCache();
            _lastModelCount = -1;
            SubscribeToProject();
            Refresh();
        }

        private void SubscribeToProject()
        {
            var project = _toolContext?.Project;
            if (project != null)
                project.OnModelsChanged += OnModelsChanged;
        }

        private void UnsubscribeFromProject()
        {
            var project = _toolContext?.Project;
            if (project != null)
                project.OnModelsChanged -= OnModelsChanged;
        }

        private void OnModelsChanged()
        {
            InvalidateCache();
            _lastModelCount = -1;
            Refresh();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<ScrollView>("main-content");
            _targetHeader = root.Q<Label>("target-header");
            _targetInfoLabel = root.Q<Label>("target-info-label");
            _foldoutMeshSelection = root.Q<Foldout>("foldout-mesh-selection");
            _btnMeshAll = root.Q<Button>("btn-mesh-all");
            _btnMeshNone = root.Q<Button>("btn-mesh-none");
            _meshToggleScroll = root.Q<ScrollView>("mesh-toggle-scroll");
            _toggleRecalcNormals = root.Q<Toggle>("toggle-recalc-normals");
            _toggleRealtimePreview = root.Q<Toggle>("toggle-realtime-preview");
            _modelsHeader = root.Q<Label>("models-header");
            _modelSlidersContainer = root.Q<VisualElement>("model-sliders-container");
            _warningsContainer = root.Q<VisualElement>("warnings-container");
            _totalWeightLabel = root.Q<Label>("total-weight-label");
            _btnEqual = root.Q<Button>("btn-equal");
            _btnNormalize = root.Q<Button>("btn-normalize");
            _btnResetFirst = root.Q<Button>("btn-reset-first");
            _btnPreview = root.Q<Button>("btn-preview");
            _btnApply = root.Q<Button>("btn-apply");

            _toggleRecalcNormals.value = _recalculateNormals;
            _toggleRecalcNormals.RegisterValueChangedCallback(e => _recalculateNormals = e.newValue);
            _toggleRealtimePreview.value = _realtimePreview;
            _toggleRealtimePreview.RegisterValueChangedCallback(e => _realtimePreview = e.newValue);

            _btnMeshAll.clicked += () => SetAllMeshEnabled(true);
            _btnMeshNone.clicked += () => SetAllMeshEnabled(false);

            _btnEqual.clicked += OnEqualWeights;
            _btnNormalize.clicked += OnNormalize;
            _btnResetFirst.clicked += OnResetFirst;
            _btnPreview.clicked += OnPreview;
            _btnApply.clicked += OnApplyClicked;
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            var project = _toolContext.Project;
            if (project == null)
            {
                ShowWarning(T("NoProject"));
                return;
            }

            int modelCount = project.ModelCount;
            if (modelCount < 2)
            {
                ShowWarning(T("NeedMultipleModels"));
                return;
            }

            if (_lastModelCount != modelCount)
            {
                InvalidateCache();
                _lastModelCount = modelCount;
            }

            SyncWeightsToModelCount(modelCount);

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            // ローカライズ
            _targetHeader.text = T("Target");
            _modelsHeader.text = T("Models");
            _toggleRecalcNormals.label = T("RecalcNormals");
            _toggleRealtimePreview.label = T("RealtimePreview");
            _btnEqual.text = T("EqualWeights");
            _btnNormalize.text = T("Normalize");
            _btnResetFirst.text = T("ResetFirst");
            _btnPreview.text = T("Preview");
            _btnApply.text = T("Apply");
            _btnMeshAll.text = T("All");
            _btnMeshNone.text = T("None");

            var currentModel = project.CurrentModel;
            int drawableCount = currentModel?.DrawableMeshes?.Count ?? 0;

            // ターゲット情報
            _targetInfoLabel.text = $"{currentModel?.Name ?? "---"} ({drawableCount} {T("Meshes")})";

            // メッシュ選択
            RefreshMeshSelection(currentModel);

            // モデルスライダー
            RefreshModelSliders(project);

            // 警告
            RefreshWarnings(project);

            // 合計ウェイト
            float totalWeight = _weights.Sum();
            _totalWeightLabel.text = $"{T("TotalWeight")}: {totalWeight:F3}";

            // プレビューボタン表示制御
            _btnPreview.style.display = _realtimePreview ? DisplayStyle.None : DisplayStyle.Flex;

            // メッシュ選択フォールドアウトテキスト
            int enabledCount = _meshEnabled.Count(e => e);
            _foldoutMeshSelection.text = $"{T("BlendMeshes")} ({enabledCount}/{drawableCount})";
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // メッシュ選択
        // ================================================================

        private void RefreshMeshSelection(ModelContext currentModel)
        {
            _meshToggleScroll.Clear();

            var drawables = currentModel?.DrawableMeshes;
            int drawableCount = drawables?.Count ?? 0;
            if (drawableCount == 0) return;

            SyncMeshEnabledToDrawableCount(drawableCount);

            for (int i = 0; i < drawableCount; i++)
            {
                var entry = drawables[i];
                string name = entry.Name;
                if (string.IsNullOrEmpty(name)) name = $"Mesh [{i}]";
                int vertCount = entry.MeshObject?.VertexCount ?? 0;

                int capturedIdx = i;
                var toggle = new Toggle($"{name} ({vertCount}V)");
                toggle.value = _meshEnabled[i];
                toggle.AddToClassList("mb-toggle");
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (capturedIdx < _meshEnabled.Count)
                        _meshEnabled[capturedIdx] = e.newValue;
                    UpdateMeshSelectionFoldoutText();
                });
                _meshToggleScroll.Add(toggle);
            }
        }

        private void SyncMeshEnabledToDrawableCount(int count)
        {
            while (_meshEnabled.Count < count)
                _meshEnabled.Add(true);
            if (_meshEnabled.Count > count)
                _meshEnabled.RemoveRange(count, _meshEnabled.Count - count);
        }

        private void SetAllMeshEnabled(bool enabled)
        {
            for (int i = 0; i < _meshEnabled.Count; i++)
                _meshEnabled[i] = enabled;
            RefreshMeshSelection(_toolContext?.Project?.CurrentModel);
            UpdateMeshSelectionFoldoutText();
        }

        private void UpdateMeshSelectionFoldoutText()
        {
            int total = _meshEnabled.Count;
            int enabled = _meshEnabled.Count(e => e);
            _foldoutMeshSelection.text = $"{T("BlendMeshes")} ({enabled}/{total})";
        }

        // ================================================================
        // モデルスライダー
        // ================================================================

        private void RefreshModelSliders(ProjectContext project)
        {
            _modelSlidersContainer.Clear();
            _weightSliders.Clear();

            int modelCount = project.ModelCount;
            int currentModelIndex = project.CurrentModelIndex;
            int baseDrawableCount = project.CurrentModel?.DrawableMeshes?.Count ?? 0;

            for (int i = 0; i < modelCount; i++)
            {
                var model = project.GetModel(i);
                if (model == null) continue;

                bool isCurrent = (i == currentModelIndex);
                int modelDrawableCount = model.DrawableMeshes?.Count ?? 0;

                var row = new VisualElement();
                row.AddToClassList("mb-model-row");

                // モデル名
                string modelLabel = isCurrent ? $"★ {model.Name}" : model.Name;
                var nameLabel = new Label(modelLabel);
                nameLabel.AddToClassList("mb-model-name");
                if (isCurrent) nameLabel.AddToClassList("mb-model-name--current");
                row.Add(nameLabel);

                // メッシュ数
                var meshCountLabel = new Label($"{modelDrawableCount} {T("Meshes")}");
                meshCountLabel.AddToClassList("mb-model-mesh-count");
                row.Add(meshCountLabel);

                // ウェイトスライダー
                int capturedIdx = i;
                var slider = new Slider(0f, 1f);
                slider.value = _weights[i];
                slider.showInputField = true;
                slider.AddToClassList("mb-model-slider");

                slider.RegisterValueChangedCallback(e =>
                    OnWeightSliderChanged(capturedIdx, e.newValue));

                slider.RegisterCallback<PointerUpEvent>(e =>
                    OnWeightSliderDragEnd(), TrickleDown.TrickleDown);

                row.Add(slider);
                _modelSlidersContainer.Add(row);
                _weightSliders.Add(slider);
            }
        }

        // ================================================================
        // ウェイトスライダー操作
        // ================================================================

        private void OnWeightSliderChanged(int modelIndex, float newValue)
        {
            var project = _toolContext?.Project;
            if (project == null) return;

            if (!_isDragging)
            {
                _isDragging = true;
                _dragBeforeSnapshot = MultiMeshVertexSnapshot.Capture(project.CurrentModel);
                BuildOriginalPositionCache(project);
            }

            _weights[modelIndex] = newValue;

            // 合計ウェイト更新
            float totalWeight = _weights.Sum();
            _totalWeightLabel.text = $"{T("TotalWeight")}: {totalWeight:F3}";

            if (_realtimePreview)
                ApplyBlendPreview(project);
        }

        private void OnWeightSliderDragEnd()
        {
            if (!_isDragging) return;
            _isDragging = false;

            InvalidateCache();

            var project = _toolContext?.Project;
            var currentModel = project?.CurrentModel;

            if (_recalculateNormals && currentModel != null)
            {
                RecalculateAllNormals(currentModel);
                _toolContext?.SyncMesh?.Invoke();
                _toolContext?.Repaint?.Invoke();
            }

            // Undo記録
            var undo = _toolContext?.UndoController;
            if (undo != null && _dragBeforeSnapshot != null && currentModel != null)
            {
                var afterSnapshot = MultiMeshVertexSnapshot.Capture(currentModel);
                var record = new MultiMeshVertexSnapshotRecord(_dragBeforeSnapshot, afterSnapshot, "Model Blend");
                undo.MeshListStack.Record(record, "Model Blend");
                _dragBeforeSnapshot = null;
            }
        }

        // ================================================================
        // 警告
        // ================================================================

        private void RefreshWarnings(ProjectContext project)
        {
            _warningsContainer.Clear();

            var currentModel = project.CurrentModel;
            if (currentModel == null) return;

            int baseDrawableCount = currentModel.DrawableMeshes?.Count ?? 0;
            bool hasMismatch = false;

            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.GetModel(i);
                if (model == null) continue;
                int modelDrawableCount = model.DrawableMeshes?.Count ?? 0;
                if (modelDrawableCount != baseDrawableCount)
                    hasMismatch = true;
            }

            if (hasMismatch)
            {
                var warn = new Label(T("MeshCountMismatch"));
                warn.AddToClassList("mb-warning-entry");
                _warningsContainer.Add(warn);
            }

            var vertexMismatches = CheckVertexCounts(project);
            foreach (var msg in vertexMismatches)
            {
                var warn = new Label(msg);
                warn.AddToClassList("mb-warning-entry");
                _warningsContainer.Add(warn);
            }
        }

        // ================================================================
        // ボタンハンドラ
        // ================================================================

        private void OnEqualWeights()
        {
            SetEqualWeights();
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview)
                ApplyBlendPreview(_toolContext?.Project);
        }

        private void OnNormalize()
        {
            NormalizeWeights();
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview)
                ApplyBlendPreview(_toolContext?.Project);
        }

        private void OnResetFirst()
        {
            ResetToFirstModel();
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview)
                ApplyBlendPreview(_toolContext?.Project);
        }

        private void OnPreview()
        {
            var project = _toolContext?.Project;
            if (project == null) return;
            if (!_cacheValid)
                BuildOriginalPositionCache(project);
            ApplyBlendPreview(project);
        }

        private void OnApplyClicked()
        {
            var project = _toolContext?.Project;
            if (project == null) return;
            ApplyBlend(project);
        }

        private void SyncSlidersFromWeights()
        {
            for (int i = 0; i < _weightSliders.Count && i < _weights.Count; i++)
                _weightSliders[i].SetValueWithoutNotify(_weights[i]);
        }

        private void UpdateTotalWeightLabel()
        {
            float totalWeight = _weights.Sum();
            _totalWeightLabel.text = $"{T("TotalWeight")}: {totalWeight:F3}";
        }

        // ================================================================
        // ブレンド処理
        // ================================================================

        private void ApplyBlend(ProjectContext project)
        {
            var currentModel = project.CurrentModel;
            if (currentModel == null) return;

            float[] normalizedWeights = NormalizeWeightArray(_weights);
            var beforeSnapshot = MultiMeshVertexSnapshot.Capture(currentModel);

            var targetDrawables = currentModel.DrawableMeshes;
            int drawableCount = targetDrawables.Count;

            for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
            {
                if (drawIdx < _meshEnabled.Count && !_meshEnabled[drawIdx]) continue;

                var targetEntry = targetDrawables[drawIdx];
                var targetMesh = targetEntry.Context?.MeshObject;
                if (targetMesh == null) continue;

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

                int minVertexCount = sourceMeshes.Min(m => m.VertexCount);
                minVertexCount = Mathf.Min(minVertexCount, targetMesh.VertexCount);

                BlendVertices(sourceMeshes.ToArray(), weights.ToArray(), targetMesh, minVertexCount);

                if (_recalculateNormals)
                    targetMesh.RecalculateSmoothNormals();
            }

            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();

            var undo = _toolContext?.UndoController;
            if (undo != null)
            {
                var afterSnapshot = MultiMeshVertexSnapshot.Capture(currentModel);
                var record = new MultiMeshVertexSnapshotRecord(beforeSnapshot, afterSnapshot, "Apply Model Blend");
                undo.MeshListStack.Record(record, "Apply Model Blend");
            }
        }

        private void ApplyBlendPreview(ProjectContext project)
        {
            var currentModel = project?.CurrentModel;
            if (currentModel == null) return;

            if (!_cacheValid)
                BuildOriginalPositionCache(project);

            float[] normalizedWeights = NormalizeWeightArray(_weights);

            var targetDrawables = currentModel.DrawableMeshes;
            int drawableCount = targetDrawables.Count;

            for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
            {
                if (drawIdx < _meshEnabled.Count && !_meshEnabled[drawIdx]) continue;

                var targetEntry = targetDrawables[drawIdx];
                var targetMesh = targetEntry.Context?.MeshObject;
                if (targetMesh == null) continue;

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

                int minVertexCount = sourcePositions.Min(p => p.Length);
                minVertexCount = Mathf.Min(minVertexCount, targetMesh.VertexCount);

                BlendVerticesFromCache(sourcePositions, weights.ToArray(), targetMesh, minVertexCount);
            }

            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
        }

        private void BlendVertices(MeshObject[] sources, float[] weights, MeshObject target, int vertexCount)
        {
            float totalWeight = weights.Sum();
            if (totalWeight <= 0f) return;

            float[] nw = weights.Select(w => w / totalWeight).ToArray();

            for (int vi = 0; vi < vertexCount; vi++)
            {
                Vector3 blendedPos = Vector3.zero;
                for (int si = 0; si < sources.Length; si++)
                {
                    if (vi < sources[si].VertexCount)
                        blendedPos += sources[si].Vertices[vi].Position * nw[si];
                }
                target.Vertices[vi].Position = blendedPos;
            }
        }

        private void BlendVerticesFromCache(List<Vector3[]> sourcePositions, float[] weights, MeshObject target, int vertexCount)
        {
            float totalWeight = weights.Sum();
            if (totalWeight <= 0f) return;

            float[] nw = weights.Select(w => w / totalWeight).ToArray();

            for (int vi = 0; vi < vertexCount; vi++)
            {
                Vector3 blendedPos = Vector3.zero;
                for (int si = 0; si < sourcePositions.Count; si++)
                {
                    if (vi < sourcePositions[si].Length)
                        blendedPos += sourcePositions[si][vi] * nw[si];
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
                            var positions = new Vector3[mesh.VertexCount];
                            for (int vi = 0; vi < mesh.VertexCount; vi++)
                                positions[vi] = mesh.Vertices[vi].Position;
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
            while (_weights.Count < modelCount)
                _weights.Add(1f / modelCount);
            while (_weights.Count > modelCount)
                _weights.RemoveAt(_weights.Count - 1);
        }

        private void NormalizeWeights()
        {
            float total = _weights.Sum();
            if (total <= 0f) { SetEqualWeights(); return; }
            for (int i = 0; i < _weights.Count; i++)
                _weights[i] /= total;
        }

        private void SetEqualWeights()
        {
            int count = _weights.Count;
            if (count == 0) return;
            float eq = 1f / count;
            for (int i = 0; i < count; i++)
                _weights[i] = eq;
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
            for (int i = 0; i < _weights.Count; i++)
                _weights[i] = (i == 0) ? 1f : 0f;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void RecalculateAllNormals(ModelContext model)
        {
            if (model == null) return;
            var drawables = model.DrawableMeshes;
            for (int i = 0; i < drawables.Count; i++)
            {
                if (i < _meshEnabled.Count && !_meshEnabled[i]) continue;
                var mesh = drawables[i].Context?.MeshObject;
                mesh?.RecalculateSmoothNormals();
            }
        }

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
                        minVertexCount = Mathf.Min(minVertexCount, srcMesh.VertexCount);
                }

                if (minVertexCount < baseVertexCount)
                    messages.Add(T("VertexCountMismatch", drawIdx, minVertexCount));
            }

            return messages;
        }
    }
}
