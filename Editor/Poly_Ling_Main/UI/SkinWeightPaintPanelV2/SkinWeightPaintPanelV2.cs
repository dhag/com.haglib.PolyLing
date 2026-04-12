// SkinWeightPaintPanelV2.cs
// スキンウェイトペイントパネル V2（コード構築 UIToolkit）
// PanelContext（選択変更通知）+ ToolContext（実処理）ハイブリッド

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public class SkinWeightPaintPanelV2 : EditorWindow, ISkinWeightPaintPanel
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext                _panelCtx;
        private ToolContext                 _toolCtx;
        private Poly_Ling.Tools.ToolManager _toolManager;

        private ModelContext       Model          => _toolCtx?.Model;
        private MeshUndoController UndoController => _toolCtx?.UndoController;

        // ================================================================
        // 設定
        // ================================================================

        private SkinWeightPaintMode _paintMode     = SkinWeightPaintMode.Replace;
        private float               _brushRadius   = 0.05f;
        private float               _brushStrength = 1.0f;
        private BrushFalloff        _brushFalloff  = BrushFalloff.Smooth;
        private float               _weightValue   = 1.0f;
        private float               _pruneThreshold = 0.01f;
        private int                 _targetBoneMasterIndex = -1;
        private string              _boneFilterText = "";

        // ================================================================
        // ISkinWeightPaintPanel
        // ================================================================

        public SkinWeightPaintMode CurrentPaintMode   => _paintMode;
        public float               CurrentBrushRadius => _brushRadius;
        public float               CurrentStrength    => _brushStrength;
        public BrushFalloff        CurrentFalloff     => _brushFalloff;
        public float               CurrentWeightValue => _weightValue;
        public int                 CurrentTargetBone  => _targetBoneMasterIndex;

        public void NotifyWeightChanged()
        {
            UpdateVertexInfluences();
            UpdateStatus();
        }

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private Label         _statusLabel;
        private VisualElement _mainSection;

        // ステップガイド
        private Label _step1Status, _step2Status, _step3Status;
        private VisualElement _step1, _step2, _step3;

        // モード
        private Button _btnReplace, _btnAdd, _btnScale, _btnSmooth;

        // ブラシ
        private Slider     _sliderRadius,   _sliderStrength;
        private FloatField _fieldRadius,    _fieldStrength;
        private EnumField  _fieldFalloff;

        // ウェイト値
        private Slider     _sliderWeightValue;
        private FloatField _fieldWeightValue;

        // ボーン
        private Label         _boneCountLabel;
        private Label         _targetBoneLabel;
        private TextField     _fieldBoneFilter;
        private VisualElement _boneListContainer;

        // 影響ボーン
        private Label         _vertexInfoLabel;
        private VisualElement _influenceListContainer;

        // Prune
        private FloatField _fieldPruneThreshold;

        // ================================================================
        // Open
        // ================================================================

        public static SkinWeightPaintPanelV2 Open(
            PanelContext panelCtx, ToolContext toolCtx,
            Poly_Ling.Tools.ToolManager toolManager = null)
        {
            var w = GetWindow<SkinWeightPaintPanelV2>();
            w.titleContent = new GUIContent("Skin Weight Paint");
            w.minSize = new Vector2(300, 500);
            w.SetContexts(panelCtx, toolCtx, toolManager);
            w.Show();
            SkinWeightPaintTool.ActivePanel = w;
            toolManager?.SetTool("SkinWeightPaint");
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx,
            Poly_Ling.Tools.ToolManager toolManager)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
            UnregisterUndoCallback();

            _panelCtx    = panelCtx;
            _toolCtx     = toolCtx;
            _toolManager = toolManager;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;
            RegisterUndoCallback();

            SyncTargetBoneFromModelSelection();
            RefreshAll();
        }

        // ================================================================
        // Undo コールバック
        // ================================================================

        private void RegisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed += RefreshAll;
        }

        private void UnregisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed -= RefreshAll;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            SkinWeightPaintTool.ActivePanel = this;
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
            RegisterUndoCallback();
        }

        private void OnDisable()
        {
            if ((object)SkinWeightPaintTool.ActivePanel == this)
                SkinWeightPaintTool.ActivePanel = null;
            UnregisterUndoCallback();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if ((object)SkinWeightPaintTool.ActivePanel == this)
                SkinWeightPaintTool.ActivePanel = null;
            UnregisterUndoCallback();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            RefreshAll();
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch ||
                kind == ChangeKind.ListStructure)
            {
                SyncTargetBoneFromModelSelection();
                RefreshAll();
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 4;
            root.style.paddingRight  = 4;
            root.style.paddingTop    = 4;
            root.style.paddingBottom = 4;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            var sv = scroll.contentContainer;

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display     = DisplayStyle.None;
            _warningLabel.style.color       = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            sv.Add(_warningLabel);

            // ステップガイド
            BuildWorkflowGuide(sv);

            // メインセクション
            _mainSection = new VisualElement();
            _mainSection.style.display = DisplayStyle.None;
            sv.Add(_mainSection);

            BuildPaintModeSection(_mainSection);
            sv.Add(MakeSep());
            BuildBrushSection(_mainSection);
            sv.Add(MakeSep());
            BuildWeightValueSection(_mainSection);
            sv.Add(MakeSep());
            BuildBoneSection(_mainSection);
            sv.Add(MakeSep());
            BuildInfluenceSection(_mainSection);
            sv.Add(MakeSep());
            BuildOperationSection(_mainSection);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.gray);
            _statusLabel.style.marginTop = 4;
            sv.Add(_statusLabel);
        }

        private void BuildWorkflowGuide(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("ワークフロー"));
            (_step1, _step1Status) = MakeStepRow("1. メッシュを選択");
            (_step2, _step2Status) = MakeStepRow("2. ペイント対象ボーンを選択");
            (_step3, _step3Status) = MakeStepRow("3. ペイント");
            parent.Add(_step1);
            parent.Add(_step2);
            parent.Add(_step3);
        }

        private static (VisualElement row, Label status) MakeStepRow(string text)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var status = new Label("  ");
            status.style.width = 16;
            var lbl = new Label(text);
            lbl.style.flexGrow = 1;
            row.Add(status);
            row.Add(lbl);
            return (row, status);
        }

        private void BuildPaintModeSection(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("ペイントモード"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;
            _btnReplace = MakeModeButton("Replace", () => SetPaintMode(SkinWeightPaintMode.Replace));
            _btnAdd     = MakeModeButton("Add",     () => SetPaintMode(SkinWeightPaintMode.Add));
            _btnScale   = MakeModeButton("Scale",   () => SetPaintMode(SkinWeightPaintMode.Scale));
            _btnSmooth  = MakeModeButton("Smooth",  () => SetPaintMode(SkinWeightPaintMode.Smooth));
            row.Add(_btnReplace); row.Add(_btnAdd); row.Add(_btnScale); row.Add(_btnSmooth);
            parent.Add(row);
        }

        private static Button MakeModeButton(string label, Action onClick)
        {
            var btn = new Button(onClick) { text = label };
            btn.style.flexGrow = 1;
            return btn;
        }

        private void BuildBrushSection(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("ブラシ"));
            AddSliderField(parent, "半径",    0.001f, 1.0f, ref _sliderRadius,   ref _fieldRadius,
                v => _brushRadius   = v, () => _brushRadius);
            AddSliderField(parent, "強度",    0.0f,   1.0f, ref _sliderStrength, ref _fieldStrength,
                v => _brushStrength = v, () => _brushStrength);

            var falloffRow = new VisualElement();
            falloffRow.style.flexDirection = FlexDirection.Row;
            falloffRow.style.marginBottom  = 2;
            falloffRow.Add(new Label("フォールオフ: ") { style = { width = 80, color = new StyleColor(Color.gray) } });
            _fieldFalloff = new EnumField(_brushFalloff);
            _fieldFalloff.style.flexGrow = 1;
            _fieldFalloff.RegisterValueChangedCallback(evt => { if (evt.newValue is BrushFalloff f) _brushFalloff = f; });
            falloffRow.Add(_fieldFalloff);
            parent.Add(falloffRow);
        }

        private void BuildWeightValueSection(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("ウェイト値"));
            AddSliderField(parent, "値", 0.0f, 1.0f, ref _sliderWeightValue, ref _fieldWeightValue,
                v => _weightValue = v, () => _weightValue);

            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            foreach (var (label, val) in new[] { ("0", 0f), ("0.25", 0.25f), ("0.5", 0.5f), ("0.75", 0.75f), ("1", 1f) })
            {
                float v = val;
                var btn = new Button(() => SetWeightValue(v)) { text = label };
                btn.style.flexGrow = 1;
                presetRow.Add(btn);
            }
            parent.Add(presetRow);
        }

        private void BuildBoneSection(VisualElement parent)
        {
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.Add(MakeSectionLabel("ペイント対象ボーン"));
            _boneCountLabel = new Label();
            _boneCountLabel.style.color = new StyleColor(Color.gray);
            _boneCountLabel.style.marginLeft = 4;
            headerRow.Add(_boneCountLabel);
            parent.Add(headerRow);

            var targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.marginBottom  = 2;
            targetRow.Add(new Label("選択中: ") { style = { width = 50, color = new StyleColor(Color.gray) } });
            _targetBoneLabel = new Label("(未選択)");
            _targetBoneLabel.style.flexGrow = 1;
            targetRow.Add(_targetBoneLabel);
            parent.Add(targetRow);

            _fieldBoneFilter = new TextField();
            _fieldBoneFilter.style.marginBottom = 2;
            _fieldBoneFilter.RegisterValueChangedCallback(evt =>
            {
                _boneFilterText = evt.newValue ?? "";
                RebuildBoneList();
            });
            parent.Add(_fieldBoneFilter);

            _boneListContainer = new VisualElement();
            _boneListContainer.style.maxHeight  = 120;
            _boneListContainer.style.marginBottom = 4;
            parent.Add(_boneListContainer);
        }

        private void BuildInfluenceSection(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("選択頂点の影響ボーン"));
            _vertexInfoLabel = new Label();
            _vertexInfoLabel.style.fontSize    = 10;
            _vertexInfoLabel.style.color       = new StyleColor(Color.gray);
            _vertexInfoLabel.style.marginBottom = 2;
            parent.Add(_vertexInfoLabel);
            _influenceListContainer = new VisualElement();
            _influenceListContainer.style.marginBottom = 4;
            parent.Add(_influenceListContainer);
        }

        private void BuildOperationSection(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("操作"));

            var floodRow = new VisualElement();
            floodRow.style.flexDirection = FlexDirection.Row;
            floodRow.style.marginBottom  = 4;
            var btnFlood = new Button(ExecuteFlood) { text = "Flood" };
            btnFlood.style.flexGrow = 1;
            floodRow.Add(btnFlood);
            parent.Add(floodRow);

            var normPruneRow = new VisualElement();
            normPruneRow.style.flexDirection = FlexDirection.Row;
            normPruneRow.style.marginBottom  = 4;
            var btnNorm  = new Button(ExecuteNormalize) { text = "Normalize" };
            btnNorm.style.flexGrow  = 1;
            btnNorm.style.marginRight = 4;
            var btnPrune = new Button(ExecutePrune)     { text = "Prune" };
            btnPrune.style.flexGrow = 1;
            normPruneRow.Add(btnNorm);
            normPruneRow.Add(btnPrune);
            parent.Add(normPruneRow);

            var pruneRow = new VisualElement();
            pruneRow.style.flexDirection = FlexDirection.Row;
            pruneRow.style.marginBottom  = 4;
            pruneRow.Add(new Label("Pruneしきい値: ") { style = { width = 90, color = new StyleColor(Color.gray) } });
            _fieldPruneThreshold = new FloatField { value = _pruneThreshold };
            _fieldPruneThreshold.style.flexGrow = 1;
            _fieldPruneThreshold.RegisterValueChangedCallback(evt =>
                _pruneThreshold = Mathf.Clamp(evt.newValue, 0.0001f, 0.5f));
            pruneRow.Add(_fieldPruneThreshold);
            parent.Add(pruneRow);
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void AddSliderField(VisualElement parent, string labelText,
            float min, float max,
            ref Slider slider, ref FloatField field,
            Action<float> setter, Func<float> getter)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            row.Add(new Label(labelText + ": ") { style = { width = 50, color = new StyleColor(Color.gray) } });

            var s = new Slider(min, max) { value = getter() };
            s.style.flexGrow = 1;
            var f = new FloatField { value = getter() };
            f.style.width = 50;

            s.RegisterValueChangedCallback(evt =>
            {
                float v = Mathf.Clamp(evt.newValue, min, max);
                setter(v);
                f.SetValueWithoutNotify(v);
            });
            f.RegisterValueChangedCallback(evt =>
            {
                float v = Mathf.Clamp(evt.newValue, min, max);
                setter(v);
                s.SetValueWithoutNotify(v);
            });

            row.Add(s);
            row.Add(f);
            parent.Add(row);
            slider = s;
            field  = f;
        }

        private static Label MakeSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep.style.marginTop       = 4;
            sep.style.marginBottom    = 4;
            return sep;
        }

        // ================================================================
        // ペイントモード
        // ================================================================

        private void SetPaintMode(SkinWeightPaintMode mode)
        {
            _paintMode = mode;
            UpdateModeButtons();
        }

        private void UpdateModeButtons()
        {
            SetActive(_btnReplace, _paintMode == SkinWeightPaintMode.Replace);
            SetActive(_btnAdd,     _paintMode == SkinWeightPaintMode.Add);
            SetActive(_btnScale,   _paintMode == SkinWeightPaintMode.Scale);
            SetActive(_btnSmooth,  _paintMode == SkinWeightPaintMode.Smooth);
        }

        private static void SetActive(Button btn, bool active)
        {
            if (btn == null) return;
            btn.style.backgroundColor = active
                ? new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.7f))
                : StyleKeyword.Null;
        }

        // ================================================================
        // ウェイト値プリセット
        // ================================================================

        private void SetWeightValue(float value)
        {
            _weightValue = value;
            _sliderWeightValue?.SetValueWithoutNotify(value);
            _fieldWeightValue?.SetValueWithoutNotify(value);
        }

        // ================================================================
        // ボーン選択同期
        // ================================================================

        private void SyncTargetBoneFromModelSelection()
        {
            if (Model == null || !Model.HasBoneSelection) return;
            _targetBoneMasterIndex = Model.FirstBoneIndex;
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return;

            UpdateWorkflowGuide();

            if (_toolCtx == null || Model == null)
            {
                SetWarning("ToolContext が未設定です。PolyLing ウィンドウから開いてください。");
                _mainSection.style.display = DisplayStyle.None;
                return;
            }

            if (!Model.HasBones)
            {
                SetWarning("モデルにボーンがありません。");
                _mainSection.style.display = DisplayStyle.None;
                return;
            }

            if (!Model.HasMeshSelection)
            {
                SetWarning("メッシュが選択されていません。");
                _mainSection.style.display = DisplayStyle.None;
                return;
            }

            var firstMesh = Model.FirstDrawableMeshContext;
            if (firstMesh?.MeshObject == null)
            {
                SetWarning("選択メッシュが無効です。");
                _mainSection.style.display = DisplayStyle.None;
                return;
            }

            SetWarning("");
            _mainSection.style.display = DisplayStyle.Flex;

            UpdateModeButtons();
            RebuildBoneList();
            UpdateTargetBoneLabel();
            UpdateVertexInfluences();
            UpdateStatus();
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ================================================================
        // ワークフローガイド
        // ================================================================

        private void UpdateWorkflowGuide()
        {
            bool hasMesh  = Model != null && Model.HasMeshSelection &&
                            Model.FirstDrawableMeshContext?.MeshObject != null;
            bool hasBone  = _targetBoneMasterIndex >= 0;
            bool canPaint = hasMesh && hasBone;

            SetStep(_step1, _step1Status, hasMesh,         !hasMesh);
            SetStep(_step2, _step2Status, hasBone,          hasMesh && !hasBone);
            SetStep(_step3, _step3Status, false,            canPaint);
        }

        private static void SetStep(VisualElement step, Label status, bool done, bool current)
        {
            if (step == null) return;
            if (done)
            {
                step.style.opacity = 0.6f;
                if (status != null) status.text = "✓";
            }
            else if (current)
            {
                step.style.opacity = 1f;
                if (status != null) status.text = "◀";
            }
            else
            {
                step.style.opacity = 0.4f;
                if (status != null) status.text = "  ";
            }
        }

        // ================================================================
        // ボーンリスト
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
                var entry     = bones[i];
                string name   = entry.Context?.Name ?? $"Bone_{i}";
                int masterIdx = entry.MasterIndex;

                if (!string.IsNullOrEmpty(filter) && !name.ToLowerInvariant().Contains(filter)) continue;

                var item = new VisualElement();
                item.style.flexDirection  = FlexDirection.Row;
                item.style.paddingLeft    = 4;
                item.style.paddingTop     = 2;
                item.style.paddingBottom  = 2;

                if (masterIdx == _targetBoneMasterIndex)
                    item.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.4f));

                var idxLbl  = new Label(i.ToString()) { style = { width = 28, color = new StyleColor(Color.gray) } };
                var nameLbl = new Label(name) { style = { flexGrow = 1 } };
                item.Add(idxLbl);
                item.Add(nameLbl);

                int captured = masterIdx;
                item.RegisterCallback<ClickEvent>(_ => OnBoneItemClicked(captured));
                _boneListContainer.Add(item);
            }
        }

        private void OnBoneItemClicked(int masterIndex)
        {
            _targetBoneMasterIndex = masterIndex;
            _toolCtx?.Repaint?.Invoke();
            UpdateWorkflowGuide();
            RebuildBoneList();
            UpdateTargetBoneLabel();
        }

        private void UpdateTargetBoneLabel()
        {
            if (_targetBoneLabel == null) return;
            if (_targetBoneMasterIndex < 0 || Model == null)
            { _targetBoneLabel.text = "(未選択)"; return; }
            var ctx = Model.GetMeshContext(_targetBoneMasterIndex);
            _targetBoneLabel.text = ctx?.Name ?? $"Bone [{_targetBoneMasterIndex}]";
        }

        // ================================================================
        // 影響ボーン表示
        // ================================================================

        private void UpdateVertexInfluences()
        {
            if (_influenceListContainer == null || _vertexInfoLabel == null) return;
            _influenceListContainer.Clear();

            var meshCtx = Model?.FirstDrawableMeshContext;
            if (meshCtx?.MeshObject == null) { _vertexInfoLabel.text = "メッシュ未選択"; return; }

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            { _vertexInfoLabel.text = "選択頂点なし"; return; }

            _vertexInfoLabel.text = $"{selectedVerts.Count} 頂点選択中";

            var influences = new Dictionary<int, (float total, int count, string name)>();
            var mo = meshCtx.MeshObject;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;
                var bw = vertex.BoneWeight.Value;
                Accumulate(influences, bw.boneIndex0, bw.weight0);
                Accumulate(influences, bw.boneIndex1, bw.weight1);
                Accumulate(influences, bw.boneIndex2, bw.weight2);
                Accumulate(influences, bw.boneIndex3, bw.weight3);
            }

            foreach (var kv in influences
                .Where(kv => kv.Value.total > 0.0001f)
                .OrderByDescending(kv => kv.Value.total / kv.Value.count))
            {
                float avg  = kv.Value.total / kv.Value.count;
                var row    = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop    = 1;
                row.style.marginBottom  = 1;

                var nameLbl = new Label(kv.Value.name) { style = { flexGrow = 1 } };
                var barBg   = new VisualElement();
                barBg.style.width           = 60;
                barBg.style.height          = 10;
                barBg.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
                barBg.style.alignSelf       = Align.Center;
                var bar = new VisualElement();
                bar.style.height          = 10;
                bar.style.width           = new StyleLength(new Length(Mathf.Clamp01(avg) * 100f, LengthUnit.Percent));
                bar.style.backgroundColor = new StyleColor(new Color(0.3f, 0.7f, 0.3f));
                barBg.Add(bar);
                var valLbl = new Label(avg.ToString("F3"));
                valLbl.style.width   = 36;
                valLbl.style.fontSize = 10;

                row.Add(nameLbl);
                row.Add(barBg);
                row.Add(valLbl);

                int captured = kv.Key;
                row.RegisterCallback<ClickEvent>(_ => OnBoneItemClicked(captured));
                _influenceListContainer.Add(row);
            }
        }

        private void Accumulate(Dictionary<int, (float total, int count, string name)> dict,
            int boneIndex, float weight)
        {
            if (weight <= 0f) return;
            string name = "?";
            if (Model != null && boneIndex >= 0 && boneIndex < Model.MeshContextCount)
                name = Model.GetMeshContext(boneIndex)?.Name ?? $"[{boneIndex}]";
            if (dict.TryGetValue(boneIndex, out var e))
                dict[boneIndex] = (e.total + weight, e.count + 1, e.name);
            else
                dict[boneIndex] = (weight, 1, name);
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            var meshCtx   = Model?.FirstDrawableMeshContext;
            int vertCount = meshCtx?.MeshObject?.VertexCount ?? 0;
            int boneCount = Model?.BoneCount ?? 0;
            int selVerts  = meshCtx?.SelectedVertices?.Count ?? 0;
            _statusLabel.text = $"Mesh: {meshCtx?.Name ?? "-"}  Verts: {vertCount}  Bones: {boneCount}  Sel: {selVerts}";
        }

        // ================================================================
        // Flood
        // ================================================================

        private void ExecuteFlood()
        {
            SkinWeightOperations.ExecuteFlood(Model, _toolCtx,
                _targetBoneMasterIndex, _paintMode, _weightValue, _brushStrength,
                msg => EditorUtility.DisplayDialog("Flood", msg, "OK"));
            RefreshAll();
        }

        // ================================================================
        // Normalize
        // ================================================================

        private void ExecuteNormalize()
        {
            SkinWeightOperations.ExecuteNormalize(Model, _toolCtx,
                msg => EditorUtility.DisplayDialog("Normalize", msg, "OK"));
            RefreshAll();
        }

        // ================================================================
        // Prune
        // ================================================================

        private void ExecutePrune()
        {
            int prunedCount = SkinWeightOperations.ExecutePrune(Model, _toolCtx, _pruneThreshold,
                msg => EditorUtility.DisplayDialog("Prune", msg, "OK"));
            if (_statusLabel != null)
                _statusLabel.text = $"Prune完了: {prunedCount} 頂点 (threshold={_pruneThreshold:F4})";
            RefreshAll();
        }




    }
}