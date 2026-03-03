// SimpleBlendPanel.cs
// 簡易ブレンドパネル (UIToolkit)
// 選択メッシュ（複数可）をソースメッシュに向けてブレンド
// 決定時にバックアップメッシュを作成

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Symmetry;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    /// <summary>
    /// 簡易ブレンドパネル（UIToolkit版）
    /// </summary>
    public class SimpleBlendPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SimpleBlendPanel/SimpleBlendPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SimpleBlendPanel/SimpleBlendPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SimpleBlendPanel/SimpleBlendPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SimpleBlendPanel/SimpleBlendPanel.uss";

        // ================================================================
        // ローカライズ
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Simple Blend", ["ja"] = "簡易ブレンド" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュが未選択です" },
            ["TargetMeshes"] = new() { ["en"] = "Target Meshes", ["ja"] = "ターゲットメッシュ" },
            ["Source"] = new() { ["en"] = "Source Mesh", ["ja"] = "ソースメッシュ" },
            ["NoCandidate"] = new() { ["en"] = "No matching mesh found", ["ja"] = "一致するメッシュがありません" },
            ["BlendWeight"] = new() { ["en"] = "Blend Weight", ["ja"] = "ブレンドウェイト" },
            ["RecalcNormals"] = new() { ["en"] = "Recalculate normals", ["ja"] = "法線を再計算" },
            ["SelectedVerticesOnly"] = new() { ["en"] = "Selected vertices only", ["ja"] = "選択頂点のみ" },
            ["MatchByVertexId"] = new() { ["en"] = "Match by vertex ID", ["ja"] = "頂点IDで照合" },
            ["Apply"] = new() { ["en"] = "Apply (Create Backup)", ["ja"] = "決定（バックアップ作成）" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル" },
            ["Previewing"] = new() { ["en"] = "Previewing...", ["ja"] = "プレビュー中..." },
            ["ApplyDone"] = new() { ["en"] = "Blend applied. {0} backup(s) created.", ["ja"] = "ブレンド適用。バックアップ {0} 個作成。" },
            ["VertexMismatch"] = new() { ["en"] = "Vertex count mismatch: {0} ({1}) ≠ source ({2})", ["ja"] = "頂点数不一致: {0} ({1}) ≠ ソース ({2})" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;
        private bool HasValidSelection => _toolContext?.HasValidMeshSelection ?? false;

        // ================================================================
        // Settings
        // ================================================================

        private int _sourceIndex = -1;
        private float _blendWeight = 0f;
        private bool _recalculateNormals = true;
        private bool _selectedVerticesOnly = false;
        private bool _matchByVertexId = false;

        // ================================================================
        // プレビュー状態
        // ================================================================

        private Dictionary<int, Vector3[]> _previewBackups = new Dictionary<int, Vector3[]>();
        private Dictionary<int, bool> _savedVisibility = new Dictionary<int, bool>();
        private bool _isPreviewActive = false;
        private bool _isDragging = false;

        // ソース候補
        private List<(int index, string name, int vertexCount)> _candidates = new List<(int, string, int)>();
        private int _selectedCandidateListIndex = -1;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private VisualElement _mainContent;
        private Label _targetHeader;
        private VisualElement _targetListContainer;
        private Toggle _toggleRecalcNormals, _toggleSelectedOnly, _toggleMatchById;
        private Label _sourceHeader, _noCandidateLabel;
        private ScrollView _candidateScroll;
        private VisualElement _mismatchContainer;
        private Label _previewingLabel;
        private VisualElement _blendSection;
        private Label _blendHeader;
        private Slider _sliderBlend;
        private Button _btnApply, _btnCancel;

        // ================================================================
        // Open
        // ================================================================

        public static SimpleBlendPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<SimpleBlendPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(320, 350);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => EndPreview();
        private void OnDestroy() => EndPreview();

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            EndPreview();
            _toolContext = ctx;
            _sourceIndex = -1;
            _blendWeight = 0f;
            _selectedCandidateListIndex = -1;
            _candidates.Clear();
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
            _mainContent = root.Q<VisualElement>("main-content");
            _targetHeader = root.Q<Label>("target-header");
            _targetListContainer = root.Q<VisualElement>("target-list-container");
            _toggleRecalcNormals = root.Q<Toggle>("toggle-recalc-normals");
            _toggleSelectedOnly = root.Q<Toggle>("toggle-selected-only");
            _toggleMatchById = root.Q<Toggle>("toggle-match-by-id");
            _sourceHeader = root.Q<Label>("source-header");
            _noCandidateLabel = root.Q<Label>("no-candidate-label");
            _candidateScroll = root.Q<ScrollView>("candidate-scroll");
            _mismatchContainer = root.Q<VisualElement>("mismatch-container");
            _previewingLabel = root.Q<Label>("previewing-label");
            _blendSection = root.Q<VisualElement>("blend-section");
            _blendHeader = root.Q<Label>("blend-header");
            _sliderBlend = root.Q<Slider>("slider-blend");
            _btnApply = root.Q<Button>("btn-apply");
            _btnCancel = root.Q<Button>("btn-cancel");

            _toggleRecalcNormals.value = _recalculateNormals;
            _toggleRecalcNormals.RegisterValueChangedCallback(e => _recalculateNormals = e.newValue);
            _toggleSelectedOnly.value = _selectedVerticesOnly;
            _toggleSelectedOnly.RegisterValueChangedCallback(e => _selectedVerticesOnly = e.newValue);
            _toggleMatchById.value = _matchByVertexId;
            _toggleMatchById.RegisterValueChangedCallback(e => _matchByVertexId = e.newValue);

            _sliderBlend.RegisterValueChangedCallback(e => OnBlendSliderChanged(e.newValue));
            _sliderBlend.RegisterCallback<PointerUpEvent>(e => OnSliderDragEnd(), TrickleDown.TrickleDown);

            _btnApply.clicked += OnApplyClicked;
            _btnCancel.clicked += OnCancelClicked;
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

            var model = Model;
            if (model == null)
            {
                ShowWarning(T("ModelNotAvailable"));
                return;
            }

            if (!HasValidSelection)
            {
                EndPreview();
                ShowWarning(T("NoMeshSelected"));
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            _targetHeader.text = T("TargetMeshes");
            _toggleRecalcNormals.label = T("RecalcNormals");
            _toggleSelectedOnly.label = T("SelectedVerticesOnly");
            _toggleMatchById.label = T("MatchByVertexId");
            _sourceHeader.text = T("Source");
            _blendHeader.text = T("BlendWeight");
            _btnApply.text = T("Apply");
            _btnCancel.text = T("Cancel");

            RefreshTargetList(model);

            var targetIndices = model.SelectedMeshIndices;
            BuildCandidates(model, targetIndices);
            RefreshCandidateList();
            RefreshBlendSection(model, targetIndices);
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // ターゲットリスト
        // ================================================================

        private void RefreshTargetList(ModelContext model)
        {
            _targetListContainer.Clear();
            foreach (int idx in model.SelectedMeshIndices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var entry = new Label($"{ctx.Name}  [V:{ctx.MeshObject.VertexCount}]");
                entry.AddToClassList("sb-target-entry");
                _targetListContainer.Add(entry);
            }
        }

        // ================================================================
        // 候補リスト
        // ================================================================

        private void BuildCandidates(ModelContext model, List<int> targetIndices)
        {
            _candidates.Clear();
            var targetSet = new HashSet<int>(targetIndices);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (targetSet.Contains(i)) continue;
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null) continue;
                if (ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh && ctx.Type != MeshType.BakedMirror && ctx.Type != MeshType.MirrorSide)
                    continue;
                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            if (_selectedCandidateListIndex >= 0)
            {
                if (_selectedCandidateListIndex >= _candidates.Count ||
                    _candidates[_selectedCandidateListIndex].index != _sourceIndex)
                {
                    _selectedCandidateListIndex = _candidates.FindIndex(c => c.index == _sourceIndex);
                    if (_selectedCandidateListIndex < 0)
                        _sourceIndex = -1;
                }
            }
        }

        private void RefreshCandidateList()
        {
            _candidateScroll.Clear();

            if (_candidates.Count == 0)
            {
                _noCandidateLabel.text = T("NoCandidate");
                _noCandidateLabel.style.display = DisplayStyle.Flex;
                _candidateScroll.style.display = DisplayStyle.None;
                return;
            }

            _noCandidateLabel.style.display = DisplayStyle.None;
            _candidateScroll.style.display = DisplayStyle.Flex;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                int capturedIndex = i;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.AddToClassList("sb-candidate-row");
                if (i == _selectedCandidateListIndex)
                    row.AddToClassList("sb-candidate-row--selected");

                row.RegisterCallback<ClickEvent>(e =>
                {
                    if (_selectedCandidateListIndex != capturedIndex)
                    {
                        _selectedCandidateListIndex = capturedIndex;
                        _sourceIndex = _candidates[capturedIndex].index;
                        OnSourceChanged();
                        RefreshCandidateList();
                        RefreshBlendSection(Model, Model?.SelectedMeshIndices);
                    }
                });

                _candidateScroll.Add(row);
            }
        }

        // ================================================================
        // ブレンドセクション
        // ================================================================

        private void RefreshBlendSection(ModelContext model, List<int> targetIndices)
        {
            _mismatchContainer.Clear();
            if (_sourceIndex >= 0 && targetIndices != null)
            {
                var sourceCtx = model.GetMeshContext(_sourceIndex);
                int sourceVertexCount = sourceCtx?.MeshObject?.VertexCount ?? 0;
                foreach (int idx in targetIndices)
                {
                    var tctx = model.GetMeshContext(idx);
                    if (tctx?.MeshObject == null) continue;
                    if (tctx.MeshObject.VertexCount != sourceVertexCount)
                    {
                        var warn = new Label(T("VertexMismatch", tctx.Name, tctx.MeshObject.VertexCount, sourceVertexCount));
                        warn.AddToClassList("sb-mismatch-warning");
                        _mismatchContainer.Add(warn);
                    }
                }
            }

            _previewingLabel.text = T("Previewing");
            _previewingLabel.style.display = _isPreviewActive ? DisplayStyle.Flex : DisplayStyle.None;

            if (_sourceIndex < 0 || _selectedCandidateListIndex < 0)
            {
                _blendSection.style.display = DisplayStyle.None;
                return;
            }

            _blendSection.style.display = DisplayStyle.Flex;
            _sliderBlend.SetValueWithoutNotify(_blendWeight);
            _btnApply.SetEnabled(_isPreviewActive);
        }

        // ================================================================
        // ソース変更
        // ================================================================

        private void OnSourceChanged()
        {
            if (_isPreviewActive)
            {
                var model = Model;
                if (model != null)
                    ApplyPreview(model, model.SelectedMeshIndices);
            }
        }

        // ================================================================
        // スライダー操作
        // ================================================================

        private void OnBlendSliderChanged(float newValue)
        {
            var model = Model;
            if (model == null) return;

            var targetIndices = model.SelectedMeshIndices;

            if (!_isDragging)
            {
                _isDragging = true;
                StartPreview(model, targetIndices);
            }

            _blendWeight = newValue;
            ApplyPreview(model, targetIndices);
            _btnApply?.SetEnabled(_isPreviewActive);
            _previewingLabel.style.display = _isPreviewActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnSliderDragEnd()
        {
            if (!_isDragging) return;
            _isDragging = false;
        }

        // ================================================================
        // ボタンハンドラ
        // ================================================================

        private void OnApplyClicked()
        {
            var model = Model;
            if (model == null) return;
            ApplyAndCreateBackups(model, model.SelectedMeshIndices);
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _blendWeight = 0f;
            _sliderBlend?.SetValueWithoutNotify(0f);
            Refresh();
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void StartPreview(ModelContext model, List<int> targetIndices)
        {
            if (_isPreviewActive) return;

            _previewBackups.Clear();
            _savedVisibility.Clear();

            foreach (int idx in targetIndices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                var backup = new Vector3[mo.VertexCount];
                for (int i = 0; i < mo.VertexCount; i++)
                    backup[i] = mo.Vertices[i].Position;
                _previewBackups[idx] = backup;

                _savedVisibility[idx] = ctx.IsVisible;
                ctx.IsVisible = true;
            }

            if (_sourceIndex >= 0)
            {
                var srcCtx = model.GetMeshContext(_sourceIndex);
                if (srcCtx != null)
                {
                    _savedVisibility[_sourceIndex] = srcCtx.IsVisible;
                    srcCtx.IsVisible = false;
                }
            }

            _isPreviewActive = true;
        }

        private void ApplyPreview(ModelContext model, List<int> targetIndices)
        {
            if (!_isPreviewActive) return;

            var srcCtx = model.GetMeshContext(_sourceIndex);
            if (srcCtx?.MeshObject == null) return;
            var srcMo = srcCtx.MeshObject;

            float w = _blendWeight;
            var selectedVerts = _selectedVerticesOnly ? _toolContext?.SelectedVertices : null;

            Dictionary<int, int> srcIdMap = null;
            if (_matchByVertexId)
                srcIdMap = BuildVertexIdMap(srcMo);

            foreach (int idx in targetIndices)
            {
                if (!_previewBackups.TryGetValue(idx, out var backup)) continue;

                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo = ctx.MeshObject;

                var nonIsolated = BuildNonIsolatedSet(mo);

                if (_matchByVertexId && srcIdMap != null)
                {
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        int vertId = mo.Vertices[i].Id;
                        if (srcIdMap.TryGetValue(vertId, out int srcIdx))
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[srcIdx].Position, w);
                        else
                            mo.Vertices[i].Position = backup[i];
                    }
                }
                else
                {
                    int count = Mathf.Min(mo.VertexCount, srcMo.VertexCount);
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        if (i < count)
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[i].Position, w);
                        else
                            mo.Vertices[i].Position = backup[i];
                    }
                }

                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                SyncMirrorSide(model, ctx);
            }

            _toolContext?.Repaint?.Invoke();
        }

        private void EndPreview()
        {
            if (!_isPreviewActive) return;

            var model = Model;
            if (model != null)
            {
                foreach (var (idx, backup) in _previewBackups)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx?.MeshObject == null) continue;
                    var mo = ctx.MeshObject;
                    int count = Mathf.Min(backup.Length, mo.VertexCount);
                    for (int i = 0; i < count; i++)
                        mo.Vertices[i].Position = backup[i];

                    _toolContext?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                    SyncMirrorSide(model, ctx);
                }

                foreach (var (idx, visible) in _savedVisibility)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx != null)
                        ctx.IsVisible = visible;
                }
            }

            _previewBackups.Clear();
            _savedVisibility.Clear();
            _isPreviewActive = false;

            _toolContext?.Repaint?.Invoke();
        }

        // ================================================================
        // 決定（バックアップ作成＋差し替え）
        // ================================================================

        private void ApplyAndCreateBackups(ModelContext model, List<int> targetIndices)
        {
            if (!_isPreviewActive) return;

            var srcCtx = model.GetMeshContext(_sourceIndex);
            if (srcCtx?.MeshObject == null) return;
            var srcMo = srcCtx.MeshObject;

            float w = _blendWeight;
            int backupCount = 0;
            var selectedVerts = _selectedVerticesOnly ? _toolContext?.SelectedVertices : null;

            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) existingNames.Add(mc.Name);
            }

            Dictionary<int, int> srcIdMap = null;
            if (_matchByVertexId)
                srcIdMap = BuildVertexIdMap(srcMo);

            var undo = _toolContext?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            foreach (int idx in targetIndices)
            {
                if (!_previewBackups.TryGetValue(idx, out var backup)) continue;

                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo = ctx.MeshObject;

                // バックアップメッシュ作成
                var backupMo = mo.Clone();
                for (int i = 0; i < backup.Length && i < backupMo.VertexCount; i++)
                    backupMo.Vertices[i].Position = backup[i];

                string backupName = GenerateUniqueName(ctx.Name + "_backup", existingNames);
                backupMo.Name = backupName;
                backupMo.Type = ctx.MeshObject.Type;

                var backupCtx = new MeshContext
                {
                    MeshObject = backupMo,
                    Name = backupName,
                    Type = ctx.Type,
                    IsVisible = false
                };
                backupCtx.UnityMesh = backupMo.ToUnityMeshShared();
                if (backupCtx.UnityMesh != null)
                    backupCtx.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

                model.Add(backupCtx);
                existingNames.Add(backupName);
                backupCount++;

                // ブレンド結果確定
                var nonIsolated = BuildNonIsolatedSet(mo);

                if (_matchByVertexId && srcIdMap != null)
                {
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        int vertId = mo.Vertices[i].Id;
                        if (srcIdMap.TryGetValue(vertId, out int srcIdx))
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[srcIdx].Position, w);
                    }
                }
                else
                {
                    int count = Mathf.Min(mo.VertexCount, srcMo.VertexCount);
                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        if (!nonIsolated.Contains(i)) continue;
                        if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                        if (i < count)
                            mo.Vertices[i].Position = Vector3.Lerp(backup[i], srcMo.Vertices[i].Position, w);
                    }
                }

                if (_recalculateNormals)
                    mo.RecalculateSmoothNormals();

                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                SyncMirrorSide(model, ctx);
            }

            // 可視状態復元
            foreach (var (idx, visible) in _savedVisibility)
            {
                if (targetIndices.Contains(idx)) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx != null)
                    ctx.IsVisible = visible;
            }

            _previewBackups.Clear();
            _savedVisibility.Clear();
            _isPreviewActive = false;

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _toolContext?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Simple Blend"));
            }

            _toolContext?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();

            _blendWeight = 0f;
            _sourceIndex = -1;
            _selectedCandidateListIndex = -1;

            Debug.Log(T("ApplyDone", backupCount));
            Refresh();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static Dictionary<int, int> BuildVertexIdMap(MeshObject mo)
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (!map.ContainsKey(id))
                    map[id] = i;
            }
            return map;
        }

        private static HashSet<int> BuildNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
            {
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            }
            return set;
        }

        private static string GenerateUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName))
                return baseName;

            for (int n = 1; n < 10000; n++)
            {
                string name = $"{baseName}_{n}";
                if (!existingNames.Contains(name))
                    return name;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ================================================================
        // ミラー側同期
        // ================================================================

        private void SyncMirrorSide(ModelContext model, MeshContext ctx)
        {
            if (model == null || ctx?.MeshObject == null) return;

            string mirrorName = ctx.Name + "+";
            var axis = ctx.GetMirrorSymmetryAxis();
            var mo = ctx.MeshObject;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                if (mc.Name != mirrorName) continue;
                if (mc.MeshObject == null || mc.MeshObject.VertexCount != mo.VertexCount) continue;

                var mirrorMo = mc.MeshObject;
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var pos = mo.Vertices[v].Position;
                    switch (axis)
                    {
                        case SymmetryAxis.X: mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z); break;
                        case SymmetryAxis.Y: mirrorMo.Vertices[v].Position = new Vector3(pos.x, -pos.y, pos.z); break;
                        case SymmetryAxis.Z: mirrorMo.Vertices[v].Position = new Vector3(pos.x, pos.y, -pos.z); break;
                        default: mirrorMo.Vertices[v].Position = new Vector3(-pos.x, pos.y, pos.z); break;
                    }
                }
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(mc);
                break;
            }
        }
    }
}
