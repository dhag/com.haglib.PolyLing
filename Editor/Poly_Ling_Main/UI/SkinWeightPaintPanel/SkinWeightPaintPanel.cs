// Assets/Editor/Poly_Ling_Main/UI/SkinWeightPaintPanel/SkinWeightPaintPanel.cs
// スキンウェイトペイントツールパネル
// MAYAのPaint Skin Weightsを参考にした設計
// Phase 1: UIパネル + Flood/Normalize/Prune操作

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.UI
{
    // ================================================================
    // ペイントモード列挙
    // ================================================================

    public enum SkinWeightPaintMode
    {
        Replace,
        Add,
        Scale,
        Smooth,
    }

    public enum BrushFalloff
    {
        Constant,
        Linear,
        Smooth,
    }

    // ================================================================
    // パネル本体
    // ================================================================

    public class SkinWeightPaintPanel : EditorWindow
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SkinWeightPaintPanel/SkinWeightPaintPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/SkinWeightPaintPanel/SkinWeightPaintPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SkinWeightPaintPanel/SkinWeightPaintPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/SkinWeightPaintPanel/SkinWeightPaintPanel.uss";

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;

        /// <summary>ToolManager参照（ツール切替用）</summary>
        [NonSerialized] private Poly_Ling.Tools.ToolManager _toolManager;

        // ================================================================
        // 設定
        // ================================================================

        private SkinWeightPaintMode _paintMode = SkinWeightPaintMode.Replace;
        private float _brushRadius = 0.05f;
        private float _brushStrength = 1.0f;
        private BrushFalloff _brushFalloff = BrushFalloff.Smooth;
        private float _weightValue = 1.0f;
        private float _pruneThreshold = 0.01f;

        /// <summary>ペイント対象ボーンのマスターインデックス（-1=未選択）</summary>
        private int _targetBoneMasterIndex = -1;

        /// <summary>ボーンフィルタ文字列</summary>
        private string _boneFilterText = "";

        // ================================================================
        // ツール連携用プロパティ（SkinWeightPaintToolから参照される）
        // ================================================================

        public SkinWeightPaintMode CurrentPaintMode => _paintMode;
        public float CurrentBrushRadius => _brushRadius;
        public float CurrentStrength => _brushStrength;
        public BrushFalloff CurrentFalloff => _brushFalloff;
        public float CurrentWeightValue => _weightValue;
        public int CurrentTargetBone => _targetBoneMasterIndex;

        /// <summary>ツール側からのウェイト変更通知（UI更新用）</summary>
        public void NotifyWeightChanged()
        {
            UpdateVertexInfluences();
            UpdateStatus();
        }

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _statusLabel;
        private VisualElement _mainSection;

        // ワークフローガイド
        private VisualElement _step1, _step2, _step3;
        private Label _step1Status, _step2Status, _step3Status;

        // ペイントモード
        private Button _btnReplace, _btnAdd, _btnScale, _btnSmooth;

        // ブラシ
        private Slider _sliderRadius, _sliderStrength;
        private FloatField _fieldRadius, _fieldStrength;
        private EnumField _fieldFalloff;

        // ウェイト値
        private Slider _sliderWeightValue;
        private FloatField _fieldWeightValue;

        // ボーン
        private Label _boneCountLabel, _targetBoneLabel;
        private TextField _fieldBoneFilter;
        private VisualElement _boneListContainer;

        // 影響ボーン
        private Label _vertexInfoLabel;
        private VisualElement _influenceListContainer;

        // 操作
        private FloatField _fieldPruneThreshold;

        // ================================================================
        // Open
        // ================================================================

        public static SkinWeightPaintPanel Open(ToolContext ctx, Poly_Ling.Tools.ToolManager toolManager = null)
        {
            var window = GetWindow<SkinWeightPaintPanel>();
            window.titleContent = new GUIContent("Skin Weight Paint");
            window.minSize = new Vector2(300, 500);
            window._toolManager = toolManager;
            window.SetContext(ctx);
            window.Show();
            Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel = window;

            // ツールを自動切替
            toolManager?.SetTool("SkinWeightPaint");

            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            // ツール連携解除
            if (Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel == this)
                Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel = null;

            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnEnable()
        {
            // ツール連携設定
            Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel = this;
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
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _statusLabel = root.Q<Label>("status-label");
            _mainSection = root.Q<VisualElement>("main-section");

            // ワークフローガイド
            _step1 = root.Q<VisualElement>("step-1");
            _step2 = root.Q<VisualElement>("step-2");
            _step3 = root.Q<VisualElement>("step-3");
            _step1Status = root.Q<Label>("step-1-status");
            _step2Status = root.Q<Label>("step-2-status");
            _step3Status = root.Q<Label>("step-3-status");

            // === ペイントモード ===
            _btnReplace = root.Q<Button>("btn-mode-replace");
            _btnAdd = root.Q<Button>("btn-mode-add");
            _btnScale = root.Q<Button>("btn-mode-scale");
            _btnSmooth = root.Q<Button>("btn-mode-smooth");

            _btnReplace?.RegisterCallback<ClickEvent>(_ => SetPaintMode(SkinWeightPaintMode.Replace));
            _btnAdd?.RegisterCallback<ClickEvent>(_ => SetPaintMode(SkinWeightPaintMode.Add));
            _btnScale?.RegisterCallback<ClickEvent>(_ => SetPaintMode(SkinWeightPaintMode.Scale));
            _btnSmooth?.RegisterCallback<ClickEvent>(_ => SetPaintMode(SkinWeightPaintMode.Smooth));

            // === ブラシ設定 ===
            _sliderRadius = root.Q<Slider>("slider-radius");
            _fieldRadius = root.Q<FloatField>("field-radius");
            _sliderStrength = root.Q<Slider>("slider-strength");
            _fieldStrength = root.Q<FloatField>("field-strength");
            _fieldFalloff = root.Q<EnumField>("field-falloff");

            if (_fieldFalloff != null)
                _fieldFalloff.Init(_brushFalloff);

            BindSliderAndField(_sliderRadius, _fieldRadius, 0.001f, 1.0f,
                v => _brushRadius = v, () => _brushRadius);
            BindSliderAndField(_sliderStrength, _fieldStrength, 0.0f, 1.0f,
                v => _brushStrength = v, () => _brushStrength);

            if (_fieldFalloff != null)
            {
                _fieldFalloff.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is BrushFalloff f)
                        _brushFalloff = f;
                });
            }

            // === ウェイト値 ===
            _sliderWeightValue = root.Q<Slider>("slider-weight-value");
            _fieldWeightValue = root.Q<FloatField>("field-weight-value");

            BindSliderAndField(_sliderWeightValue, _fieldWeightValue, 0.0f, 1.0f,
                v => _weightValue = v, () => _weightValue);

            // プリセットボタン
            root.Q<Button>("btn-val-0")?.RegisterCallback<ClickEvent>(_ => SetWeightValue(0f));
            root.Q<Button>("btn-val-025")?.RegisterCallback<ClickEvent>(_ => SetWeightValue(0.25f));
            root.Q<Button>("btn-val-05")?.RegisterCallback<ClickEvent>(_ => SetWeightValue(0.5f));
            root.Q<Button>("btn-val-075")?.RegisterCallback<ClickEvent>(_ => SetWeightValue(0.75f));
            root.Q<Button>("btn-val-1")?.RegisterCallback<ClickEvent>(_ => SetWeightValue(1.0f));

            // === ボーン ===
            _boneCountLabel = root.Q<Label>("bone-count-label");
            _targetBoneLabel = root.Q<Label>("target-bone-label");
            _fieldBoneFilter = root.Q<TextField>("field-bone-filter");
            _boneListContainer = root.Q<VisualElement>("bone-list-container");

            if (_fieldBoneFilter != null)
            {
                _fieldBoneFilter.RegisterValueChangedCallback(evt =>
                {
                    _boneFilterText = evt.newValue ?? "";
                    RebuildBoneList();
                });
            }

            // === 影響ボーン ===
            _vertexInfoLabel = root.Q<Label>("vertex-info-label");
            _influenceListContainer = root.Q<VisualElement>("influence-list-container");

            // === 操作ボタン ===
            _fieldPruneThreshold = root.Q<FloatField>("field-prune-threshold");
            if (_fieldPruneThreshold != null)
            {
                _fieldPruneThreshold.value = _pruneThreshold;
                _fieldPruneThreshold.RegisterValueChangedCallback(evt =>
                {
                    _pruneThreshold = Mathf.Clamp(evt.newValue, 0.0001f, 0.5f);
                });
            }

            root.Q<Button>("btn-flood")?.RegisterCallback<ClickEvent>(_ => ExecuteFlood());
            root.Q<Button>("btn-normalize")?.RegisterCallback<ClickEvent>(_ => ExecuteNormalize());
            root.Q<Button>("btn-prune")?.RegisterCallback<ClickEvent>(_ => ExecutePrune());

            RefreshAll();
        }

        // ================================================================
        // Slider ↔ FloatField 双方向バインド
        // ================================================================

        private void BindSliderAndField(Slider slider, FloatField field,
            float min, float max, Action<float> setter, Func<float> getter)
        {
            if (slider != null)
            {
                slider.lowValue = min;
                slider.highValue = max;
                slider.value = getter();
                slider.RegisterValueChangedCallback(evt =>
                {
                    float v = Mathf.Clamp(evt.newValue, min, max);
                    setter(v);
                    if (field != null) field.SetValueWithoutNotify(v);
                });
            }

            if (field != null)
            {
                field.value = getter();
                field.RegisterValueChangedCallback(evt =>
                {
                    float v = Mathf.Clamp(evt.newValue, min, max);
                    setter(v);
                    if (slider != null) slider.SetValueWithoutNotify(v);
                });
            }
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;

            _toolContext = ctx;

            if (_toolContext?.Model != null)
            {
                SubscribeToModel();
                if (_toolContext.UndoController != null)
                    _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
            }

            // ボーン選択同期: モデルのボーン選択をターゲットに反映
            SyncTargetBoneFromModelSelection();

            RefreshAll();
        }

        private void SubscribeToModel()
        {
            if (Model != null)
                Model.OnListChanged += OnModelListChanged;
        }

        private void UnsubscribeFromModel()
        {
            if (_toolContext?.Model != null)
                _toolContext.Model.OnListChanged -= OnModelListChanged;
        }

        private void OnModelListChanged()
        {
            SyncTargetBoneFromModelSelection();
            RefreshAll();
        }

        private void OnUndoRedoPerformed()
        {
            RefreshAll();
        }

        // ================================================================
        // ペイントモード切替
        // ================================================================

        private void SetPaintMode(SkinWeightPaintMode mode)
        {
            _paintMode = mode;
            UpdateModeButtons();
        }

        private void UpdateModeButtons()
        {
            SetModeButtonActive(_btnReplace, _paintMode == SkinWeightPaintMode.Replace);
            SetModeButtonActive(_btnAdd, _paintMode == SkinWeightPaintMode.Add);
            SetModeButtonActive(_btnScale, _paintMode == SkinWeightPaintMode.Scale);
            SetModeButtonActive(_btnSmooth, _paintMode == SkinWeightPaintMode.Smooth);
        }

        private static void SetModeButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active)
                btn.AddToClassList("swp-mode-active");
            else
                btn.RemoveFromClassList("swp-mode-active");
        }

        // ================================================================
        // ウェイト値プリセット
        // ================================================================

        private void SetWeightValue(float value)
        {
            _weightValue = value;
            if (_sliderWeightValue != null) _sliderWeightValue.SetValueWithoutNotify(value);
            if (_fieldWeightValue != null) _fieldWeightValue.SetValueWithoutNotify(value);
        }

        // ================================================================
        // ボーン選択同期
        // ================================================================

        /// <summary>
        /// ModelContextのボーン選択をターゲットボーンに同期
        /// </summary>
        private void SyncTargetBoneFromModelSelection()
        {
            if (Model == null || !Model.HasBoneSelection)
                return;

            _targetBoneMasterIndex = Model.FirstBoneIndex;
        }

        // ================================================================
        // 表示更新
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return;

            // ワークフローガイドは常に更新
            UpdateWorkflowGuide();

            if (_toolContext == null || Model == null)
            {
                SetWarning("ToolContextが未設定です。PolyLingウィンドウから開いてください。");
                SetMainVisible(false);
                return;
            }

            if (!Model.HasBones)
            {
                SetWarning("モデルにボーンがありません。");
                SetMainVisible(false);
                return;
            }

            // メッシュ選択チェック（ウェイトペイントはメッシュ選択が必要）
            if (!Model.HasMeshSelection)
            {
                SetWarning("メッシュが選択されていません。ウェイトペイント対象のメッシュを選択してください。");
                SetMainVisible(false);
                return;
            }

            var firstMesh = Model.FirstSelectedDrawableMeshContext;
            if (firstMesh?.MeshObject == null)
            {
                SetWarning("選択メッシュが無効です。");
                SetMainVisible(false);
                return;
            }

            SetWarning("");
            SetMainVisible(true);

            UpdateModeButtons();
            RebuildBoneList();
            UpdateTargetBoneLabel();
            UpdateVertexInfluences();
            UpdateStatus();
        }

        // ================================================================
        // ワークフローガイド更新
        // ================================================================

        private void UpdateWorkflowGuide()
        {
            bool hasMesh = Model != null && Model.HasMeshSelection && Model.FirstSelectedDrawableMeshContext?.MeshObject != null;
            bool hasBone = _targetBoneMasterIndex >= 0;
            bool canPaint = hasMesh && hasBone;

            SetStepState(_step1, _step1Status, hasMesh, !hasMesh);
            SetStepState(_step2, _step2Status, hasBone, hasMesh && !hasBone);
            SetStepState(_step3, _step3Status, false, canPaint);
        }

        private void SetStepState(VisualElement step, Label status, bool done, bool current)
        {
            if (step == null) return;

            step.RemoveFromClassList("swp-step-done");
            step.RemoveFromClassList("swp-step-current");
            step.RemoveFromClassList("swp-step-pending");

            if (done)
            {
                step.AddToClassList("swp-step-done");
                if (status != null) status.text = "✓";
            }
            else if (current)
            {
                step.AddToClassList("swp-step-current");
                if (status != null) status.text = "◀";
            }
            else
            {
                step.AddToClassList("swp-step-pending");
                if (status != null) status.text = "";
            }
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetMainVisible(bool visible)
        {
            if (_mainSection != null)
                _mainSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // ボーンリスト構築
        // ================================================================

        private void RebuildBoneList()
        {
            if (_boneListContainer == null || Model == null) return;

            _boneListContainer.Clear();

            var bones = Model.Bones;
            if (bones == null || bones.Count == 0)
            {
                if (_boneCountLabel != null) _boneCountLabel.text = "(0)";
                return;
            }

            if (_boneCountLabel != null) _boneCountLabel.text = $"({bones.Count})";

            string filter = _boneFilterText.ToLowerInvariant().Trim();

            for (int i = 0; i < bones.Count; i++)
            {
                var entry = bones[i];
                string boneName = entry.Context?.Name ?? $"Bone_{i}";
                int masterIdx = entry.MasterIndex;

                // フィルタ
                if (!string.IsNullOrEmpty(filter) && !boneName.ToLowerInvariant().Contains(filter))
                    continue;

                var item = new VisualElement();
                item.AddToClassList("swp-bone-item");

                if (masterIdx == _targetBoneMasterIndex)
                    item.AddToClassList("swp-bone-item-selected");

                var idxLabel = new Label(i.ToString());
                idxLabel.AddToClassList("swp-bone-item-idx");

                var nameLabel = new Label(boneName);
                nameLabel.AddToClassList("swp-bone-item-name");

                item.Add(idxLabel);
                item.Add(nameLabel);

                int capturedMasterIdx = masterIdx;
                item.RegisterCallback<ClickEvent>(_ => OnBoneItemClicked(capturedMasterIdx));

                _boneListContainer.Add(item);
            }
        }

        private void OnBoneItemClicked(int masterIndex)
        {
            _targetBoneMasterIndex = masterIndex;

            // ※ Model.SelectBone()は呼ばない
            // SelectBoneはActiveCategoryをBoneに変え、FirstSelectedMeshContextが
            // 描画メッシュではなくボーンを返すようになり、ペイントが効かなくなる

            _toolContext?.Repaint?.Invoke();

            UpdateWorkflowGuide();
            RebuildBoneList();
            UpdateTargetBoneLabel();
        }

        private void UpdateTargetBoneLabel()
        {
            if (_targetBoneLabel == null) return;

            if (_targetBoneMasterIndex < 0 || Model == null)
            {
                _targetBoneLabel.text = "(未選択)";
                return;
            }

            var ctx = Model.GetMeshContext(_targetBoneMasterIndex);
            _targetBoneLabel.text = ctx?.Name ?? $"Bone [{_targetBoneMasterIndex}]";
        }

        // ================================================================
        // 選択頂点の影響ボーン表示
        // ================================================================

        private void UpdateVertexInfluences()
        {
            if (_influenceListContainer == null || _vertexInfoLabel == null) return;

            _influenceListContainer.Clear();

            var meshCtx = Model?.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null)
            {
                _vertexInfoLabel.text = "メッシュ未選択";
                return;
            }

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            {
                _vertexInfoLabel.text = "選択頂点なし";
                return;
            }

            _vertexInfoLabel.text = $"{selectedVerts.Count} 頂点選択中";

            // 影響ボーンを集計（ボーンマスターインデックス → 平均ウェイト）
            var influences = new Dictionary<int, (float totalWeight, int count, string name)>();
            var mo = meshCtx.MeshObject;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                var bw = vertex.BoneWeight.Value;
                AccumulateInfluence(influences, bw.boneIndex0, bw.weight0);
                AccumulateInfluence(influences, bw.boneIndex1, bw.weight1);
                AccumulateInfluence(influences, bw.boneIndex2, bw.weight2);
                AccumulateInfluence(influences, bw.boneIndex3, bw.weight3);
            }

            // ウェイト降順でソート
            var sorted = influences
                .Where(kv => kv.Value.totalWeight > 0.0001f)
                .OrderByDescending(kv => kv.Value.totalWeight / kv.Value.count)
                .ToList();

            foreach (var kv in sorted)
            {
                float avgWeight = kv.Value.totalWeight / kv.Value.count;
                string name = kv.Value.name;

                var row = new VisualElement();
                row.AddToClassList("swp-influence-row");

                var nameLabel = new Label(name);
                nameLabel.AddToClassList("swp-influence-name");

                // ウェイトバー
                var barBg = new VisualElement();
                barBg.AddToClassList("swp-influence-bar-bg");
                var bar = new VisualElement();
                bar.AddToClassList("swp-influence-bar");
                bar.style.width = new StyleLength(new Length(Mathf.Clamp01(avgWeight) * 100f, LengthUnit.Percent));
                barBg.Add(bar);

                var valLabel = new Label(avgWeight.ToString("F3"));
                valLabel.AddToClassList("swp-influence-value");

                row.Add(nameLabel);
                row.Add(barBg);
                row.Add(valLabel);

                // クリックでターゲットボーンに設定
                int capturedIdx = kv.Key;
                row.RegisterCallback<ClickEvent>(_ => OnBoneItemClicked(capturedIdx));

                _influenceListContainer.Add(row);
            }
        }

        private void AccumulateInfluence(Dictionary<int, (float totalWeight, int count, string name)> dict,
            int boneIndex, float weight)
        {
            if (weight <= 0f) return;

            string boneName = "?";
            if (Model != null && boneIndex >= 0 && boneIndex < Model.MeshContextCount)
            {
                var ctx = Model.GetMeshContext(boneIndex);
                boneName = ctx?.Name ?? $"[{boneIndex}]";
            }

            if (dict.TryGetValue(boneIndex, out var existing))
                dict[boneIndex] = (existing.totalWeight + weight, existing.count + 1, existing.name);
            else
                dict[boneIndex] = (weight, 1, boneName);
        }

        // ================================================================
        // ステータス更新
        // ================================================================

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;

            var meshCtx = Model?.FirstSelectedDrawableMeshContext;
            int vertCount = meshCtx?.MeshObject?.VertexCount ?? 0;
            int boneCount = Model?.BoneCount ?? 0;
            int selVerts = meshCtx?.SelectedVertices?.Count ?? 0;

            _statusLabel.text = $"Mesh: {meshCtx?.Name ?? "-"}  Verts: {vertCount}  Bones: {boneCount}  Selected: {selVerts}";
        }

        // ================================================================
        // Flood: 選択頂点にウェイト一括設定
        // ================================================================

        private void ExecuteFlood()
        {
            if (Model == null || _targetBoneMasterIndex < 0) return;

            var meshCtx = Model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            {
                EditorUtility.DisplayDialog("Flood", "頂点が選択されていません。", "OK");
                return;
            }

            var undo = _toolContext?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            var mo = meshCtx.MeshObject;
            int targetBone = _targetBoneMasterIndex;
            float value = _weightValue;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];

                BoneWeight bw = vertex.BoneWeight ?? default;

                switch (_paintMode)
                {
                    case SkinWeightPaintMode.Replace:
                        bw = SetBoneWeight(bw, targetBone, value);
                        break;
                    case SkinWeightPaintMode.Add:
                        bw = AddBoneWeight(bw, targetBone, value * _brushStrength);
                        break;
                    case SkinWeightPaintMode.Scale:
                        bw = ScaleBoneWeight(bw, targetBone, value);
                        break;
                    case SkinWeightPaintMode.Smooth:
                        // Smooth は近傍頂点が必要なので Flood では Skip
                        continue;
                }

                bw = NormalizeBoneWeight(bw);
                vertex.BoneWeight = bw;
            }

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _toolContext?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Flood Skin Weight"));
            }

            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            RefreshAll();
        }

        // ================================================================
        // Normalize: 選択頂点のウェイト正規化
        // ================================================================

        private void ExecuteNormalize()
        {
            if (Model == null) return;

            var meshCtx = Model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            {
                EditorUtility.DisplayDialog("Normalize", "頂点が選択されていません。", "OK");
                return;
            }

            var undo = _toolContext?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            var mo = meshCtx.MeshObject;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                vertex.BoneWeight = NormalizeBoneWeight(vertex.BoneWeight.Value);
            }

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _toolContext?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Normalize Skin Weights"));
            }

            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            RefreshAll();
        }

        // ================================================================
        // Prune: 微小ウェイト除去
        // ================================================================

        private void ExecutePrune()
        {
            if (Model == null) return;

            var meshCtx = Model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            {
                EditorUtility.DisplayDialog("Prune", "頂点が選択されていません。", "OK");
                return;
            }

            var undo = _toolContext?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            var mo = meshCtx.MeshObject;
            float threshold = _pruneThreshold;
            int prunedCount = 0;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                var bw = vertex.BoneWeight.Value;
                bool changed = false;

                if (bw.weight0 > 0f && bw.weight0 < threshold) { bw.weight0 = 0f; bw.boneIndex0 = 0; changed = true; }
                if (bw.weight1 > 0f && bw.weight1 < threshold) { bw.weight1 = 0f; bw.boneIndex1 = 0; changed = true; }
                if (bw.weight2 > 0f && bw.weight2 < threshold) { bw.weight2 = 0f; bw.boneIndex2 = 0; changed = true; }
                if (bw.weight3 > 0f && bw.weight3 < threshold) { bw.weight3 = 0f; bw.boneIndex3 = 0; changed = true; }

                if (changed)
                {
                    bw = NormalizeBoneWeight(bw);
                    bw = SortBoneWeight(bw);
                    vertex.BoneWeight = bw;
                    prunedCount++;
                }
            }

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _toolContext?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Prune Skin Weights"));
            }

            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            RefreshAll();

            if (_statusLabel != null)
                _statusLabel.text = $"Prune完了: {prunedCount} 頂点を処理 (threshold={threshold:F4})";
        }

        // ================================================================
        // BoneWeight操作ユーティリティ
        // ================================================================

        /// <summary>
        /// 指定ボーンのウェイトを設定（Replace）
        /// 他のボーンのウェイトは残量を按分
        /// </summary>
        private static BoneWeight SetBoneWeight(BoneWeight bw, int boneIndex, float weight)
        {
            weight = Mathf.Clamp01(weight);

            // 4スロットを配列に展開
            var slots = ExtractSlots(bw);

            // 既存スロットから対象ボーンを探す
            int targetSlot = -1;
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex && slots[i].weight > 0f)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot < 0)
            {
                // 空きスロットまたは最小ウェイトのスロットを使う
                targetSlot = FindSlotForNewBone(slots);
            }

            // 他のスロットの合計
            float otherTotal = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i != targetSlot) otherTotal += slots[i].weight;
            }

            slots[targetSlot] = (boneIndex, weight);

            // 残りを按分
            float remaining = 1f - weight;
            if (otherTotal > 0.0001f)
            {
                float scale = remaining / otherTotal;
                for (int i = 0; i < 4; i++)
                {
                    if (i != targetSlot)
                        slots[i].weight *= scale;
                }
            }

            return PackSlots(slots);
        }

        /// <summary>
        /// 指定ボーンのウェイトを加算（Add）
        /// </summary>
        private static BoneWeight AddBoneWeight(BoneWeight bw, int boneIndex, float amount)
        {
            var slots = ExtractSlots(bw);

            int targetSlot = -1;
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex && slots[i].weight > 0f)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot < 0)
                targetSlot = FindSlotForNewBone(slots);

            slots[targetSlot] = (boneIndex, Mathf.Clamp01(slots[targetSlot].weight + amount));

            return PackSlots(slots);
        }

        /// <summary>
        /// 指定ボーンのウェイトをスケール（Scale）
        /// </summary>
        private static BoneWeight ScaleBoneWeight(BoneWeight bw, int boneIndex, float scale)
        {
            var slots = ExtractSlots(bw);

            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex)
                {
                    slots[i].weight = Mathf.Clamp01(slots[i].weight * scale);
                    break;
                }
            }

            return PackSlots(slots);
        }

        /// <summary>
        /// ウェイト正規化（合計1.0に）
        /// </summary>
        private static BoneWeight NormalizeBoneWeight(BoneWeight bw)
        {
            float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (total < 0.0001f)
                return bw;

            float inv = 1f / total;
            bw.weight0 *= inv;
            bw.weight1 *= inv;
            bw.weight2 *= inv;
            bw.weight3 *= inv;
            return bw;
        }

        /// <summary>
        /// ウェイト降順ソート
        /// </summary>
        private static BoneWeight SortBoneWeight(BoneWeight bw)
        {
            var slots = ExtractSlots(bw);
            Array.Sort(slots, (a, b) => b.weight.CompareTo(a.weight));
            return PackSlots(slots);
        }

        // ================================================================
        // スロット操作ヘルパー
        // ================================================================

        private static (int index, float weight)[] ExtractSlots(BoneWeight bw)
        {
            return new (int, float)[]
            {
                (bw.boneIndex0, bw.weight0),
                (bw.boneIndex1, bw.weight1),
                (bw.boneIndex2, bw.weight2),
                (bw.boneIndex3, bw.weight3),
            };
        }

        private static BoneWeight PackSlots((int index, float weight)[] slots)
        {
            return new BoneWeight
            {
                boneIndex0 = slots[0].index,  weight0 = slots[0].weight,
                boneIndex1 = slots[1].index,  weight1 = slots[1].weight,
                boneIndex2 = slots[2].index,  weight2 = slots[2].weight,
                boneIndex3 = slots[3].index,  weight3 = slots[3].weight,
            };
        }

        /// <summary>
        /// 新ボーン用スロットを探す（空きスロット優先、なければ最小ウェイト）
        /// </summary>
        private static int FindSlotForNewBone((int index, float weight)[] slots)
        {
            // ウェイト0のスロットを探す
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].weight <= 0f)
                    return i;
            }

            // 最小ウェイトのスロット
            int minSlot = 0;
            float minWeight = slots[0].weight;
            for (int i = 1; i < 4; i++)
            {
                if (slots[i].weight < minWeight)
                {
                    minWeight = slots[i].weight;
                    minSlot = i;
                }
            }
            return minSlot;
        }
    }
}
