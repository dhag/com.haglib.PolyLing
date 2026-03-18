// MorphPanelV2.cs
// モーフエクスプレッション管理・プレビューパネル V2（コード構築 UIToolkit）
// PanelContext（モデル切り替え通知）+ ToolContext（実処理）ハイブリッド

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.MQO;

namespace Poly_Ling.UI
{
    public class MorphPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model => _toolCtx?.Model;

        // ================================================================
        // データ
        // ================================================================

        private readonly List<(int index, string name, string info)>                               _setListData = new();
        private readonly List<(int entryIdx, int meshIndex, string meshName, float weight)>        _entryData   = new();
        private int              _selectedSetIndex = -1;
        private MorphExpression  _entryEditSnapshot;

        // ================================================================
        // プレビュー状態
        // ================================================================

        private bool                                               _isPreviewActive            = false;
        private readonly Dictionary<int, Vector3[]>               _previewBackups             = new();
        private readonly List<(int morphIndex, int baseIndex, float entryWeight)> _previewPairs = new();
        private int                                                _previewMorphExpressionIndex = -1;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private Label         _statusLabel;
        private ListView      _setListView;
        private VisualElement _setDetail;
        private TextField     _setName;
        private TextField     _setNameEn;
        private Label         _setTypeLabel;
        private VisualElement _setPanelContainer;
        private PopupField<int> _setPanelPopup;
        private ListView      _entryListView;
        private VisualElement _previewSection;
        private Label         _previewInfo;
        private Slider        _previewWeight;

        // ================================================================
        // Open
        // ================================================================

        public static MorphPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<MorphPanelV2>();
            w.titleContent = new GUIContent("モーフエディタ");
            w.minSize = new Vector2(300, 420);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
            UnregisterUndoCallback();

            EndPreview();
            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;
            _selectedSetIndex = -1;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;
            RegisterUndoCallback();

            RefreshAll();
        }

        // ================================================================
        // Undo コールバック
        // ================================================================

        private void RegisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        private void UnregisterUndoCallback()
        {
            var undo = _toolCtx?.UndoController;
            if (undo != null) undo.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnUndoRedoPerformed()
        {
            EndPreview();
            _previewWeight?.SetValueWithoutNotify(0f);
            RefreshAll();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
            RegisterUndoCallback();
        }

        private void OnDisable()
        {
            EndPreview();
            UnregisterUndoCallback();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            EndPreview();
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
            if (kind == ChangeKind.ModelSwitch)
            {
                EndPreview();
                _selectedSetIndex = -1;
                RefreshAll();
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // 警告ラベル
            _warningLabel = new Label();
            _warningLabel.style.display  = DisplayStyle.None;
            _warningLabel.style.color    = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // セットリスト
            root.Add(MakeSectionLabel("モーフエクスプレッション"));

            _setListView = new ListView(_setListData, 20, SetMakeItem, SetBindItem);
            _setListView.style.height       = 120;
            _setListView.style.marginBottom = 4;
            _setListView.selectionChanged  += OnSetSelectionChanged;
            root.Add(_setListView);

            // CSVボタン行
            var csvRow = new VisualElement();
            csvRow.style.flexDirection  = FlexDirection.Row;
            csvRow.style.marginBottom   = 6;
            var btnImport = new Button(OnCsvImport) { text = "CSV読込" };
            btnImport.style.flexGrow = 1;
            btnImport.style.marginRight = 4;
            var btnExport = new Button(OnCsvExport) { text = "CSV保存" };
            btnExport.style.flexGrow = 1;
            csvRow.Add(btnImport);
            csvRow.Add(btnExport);
            root.Add(csvRow);

            // セット詳細
            _setDetail = new VisualElement();
            _setDetail.style.display = DisplayStyle.None;
            root.Add(_setDetail);

            BuildSetDetailUI(_setDetail);

            // ステータスラベル
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.gray);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        private void BuildSetDetailUI(VisualElement parent)
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep.style.marginTop       = 2;
            sep.style.marginBottom    = 6;
            parent.Add(sep);

            // 名前フィールド
            _setName = new TextField("名前 (JP)");
            _setName.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());
            parent.Add(_setName);

            _setNameEn = new TextField("名前 (EN)");
            _setNameEn.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());
            parent.Add(_setNameEn);

            // タイプ
            var typeRow = new VisualElement();
            typeRow.style.flexDirection = FlexDirection.Row;
            typeRow.style.marginTop     = 2;
            typeRow.style.marginBottom  = 2;
            typeRow.Add(new Label("タイプ: ") { style = { width = 60 } });
            _setTypeLabel = new Label();
            _setTypeLabel.style.color = new StyleColor(Color.gray);
            typeRow.Add(_setTypeLabel);
            parent.Add(typeRow);

            // パネル
            var panelRow = new VisualElement();
            panelRow.style.flexDirection = FlexDirection.Row;
            panelRow.style.marginBottom  = 4;
            panelRow.Add(new Label("パネル: ") { style = { width = 60 } });
            _setPanelContainer = new VisualElement();
            _setPanelContainer.style.flexGrow = 1;
            panelRow.Add(_setPanelContainer);
            parent.Add(panelRow);

            // 削除ボタン
            var btnDelete = new Button(OnDeleteSet) { text = "このセットを削除" };
            btnDelete.style.marginBottom = 6;
            parent.Add(btnDelete);

            // エントリリスト
            parent.Add(MakeSectionLabel("エントリ (モーフメッシュ / ウェイト)"));
            _entryListView = new ListView(_entryData, 22, EntryMakeItem, EntryBindItem);
            _entryListView.style.height       = 100;
            _entryListView.style.marginBottom = 6;
            parent.Add(_entryListView);

            // プレビューセクション
            _previewSection = new VisualElement();
            _previewSection.style.display = DisplayStyle.None;
            parent.Add(_previewSection);

            BuildPreviewSectionUI(_previewSection);
        }

        private void BuildPreviewSectionUI(VisualElement parent)
        {
            parent.Add(MakeSectionLabel("プレビュー"));

            _previewInfo = new Label();
            _previewInfo.style.fontSize    = 10;
            _previewInfo.style.color       = new StyleColor(Color.gray);
            _previewInfo.style.marginBottom = 2;
            parent.Add(_previewInfo);

            _previewWeight = new Slider("ウェイト", 0f, 1f) { value = 0f };
            _previewWeight.style.marginBottom = 4;
            _previewWeight.RegisterValueChangedCallback(OnPreviewWeightChanged);
            parent.Add(_previewWeight);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            var btnReset = new Button(OnResetPreview)   { text = "リセット" };
            btnReset.style.flexGrow  = 1;
            btnReset.style.marginRight = 4;
            var btnEnd   = new Button(OnEndPreview)     { text = "プレビュー終了" };
            btnEnd.style.flexGrow = 1;
            btnRow.Add(btnReset);
            btnRow.Add(btnEnd);
            parent.Add(btnRow);
        }

        private static Label MakeSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            return l;
        }

        // ================================================================
        // ListView: セット
        // ================================================================

        private VisualElement SetMakeItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft   = 4;
            var name = new Label { name = "name" };
            name.style.flexGrow = 1;
            row.Add(name);
            var info = new Label { name = "info" };
            info.style.color    = new StyleColor(Color.gray);
            info.style.fontSize = 10;
            row.Add(info);
            return row;
        }

        private void SetBindItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _setListData.Count) return;
            var d = _setListData[index];
            el.Q<Label>("name").text = d.name;
            el.Q<Label>("info").text = d.info;
        }

        // ================================================================
        // ListView: エントリ
        // ================================================================

        private VisualElement EntryMakeItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft   = 4;
            var name = new Label { name = "name" };
            name.style.flexGrow = 1;
            row.Add(name);
            var slider = new Slider(0f, 1f) { name = "slider" };
            slider.style.width = 80;
            row.Add(slider);
            var wl = new Label { name = "wl" };
            wl.style.width   = 30;
            wl.style.fontSize = 10;
            row.Add(wl);
            return row;
        }

        private void EntryBindItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _entryData.Count) return;
            var d      = _entryData[index];
            var name   = el.Q<Label>("name");
            var slider = el.Q<Slider>("slider");
            var wl     = el.Q<Label>("wl");

            if (name   != null) name.text = d.meshName;
            if (wl     != null) wl.text   = d.weight.ToString("F2");

            if (slider != null)
            {
                int eIdx = d.entryIdx;
                slider.UnregisterValueChangedCallback<float>(null);
                slider.SetValueWithoutNotify(d.weight);
                slider.RegisterValueChangedCallback(evt =>
                {
                    OnEntryWeightChanged(eIdx, evt.newValue);
                    if (wl != null) wl.text = evt.newValue.ToString("F2");
                });
                slider.RegisterCallback<PointerDownEvent>(_ => OnEntryWeightStart());
                slider.RegisterCallback<PointerUpEvent>(_  => OnEntryWeightEnd());
            }
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return;

            if (_toolCtx == null)
            {
                ShowWarning("ToolContext 未設定。Poly_Ling ウィンドウから開いてください。");
                return;
            }

            var model = Model;
            if (model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            if (!model.HasMorphExpressions)
            {
                ShowWarning("モーフエクスプレッションがありません");
                _setDetail.style.display = DisplayStyle.None;
                _previewSection.style.display = DisplayStyle.None;
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            RefreshSetList();
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text          = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _setDetail.style.display = DisplayStyle.None;
        }

        private void RefreshSetList()
        {
            _setListData.Clear();
            var model = Model;
            if (model != null)
                for (int i = 0; i < model.MorphExpressionCount; i++)
                {
                    var s = model.MorphExpressions[i];
                    _setListData.Add((i, s.Name, $"({s.Type}, {s.MeshCount}件)"));
                }

            _setListView?.RefreshItems();

            if (_selectedSetIndex >= 0 && _selectedSetIndex < _setListData.Count)
            {
                _setListView?.SetSelection(_selectedSetIndex);
                RefreshSetDetail(_selectedSetIndex);
            }
            else
            {
                _selectedSetIndex = -1;
                _setDetail.style.display = DisplayStyle.None;
            }
        }

        // ================================================================
        // セット選択
        // ================================================================

        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            int idx = _setListView?.selectedIndex ?? -1;
            if (idx == _selectedSetIndex) return;

            EndPreview();
            _previewWeight?.SetValueWithoutNotify(0f);

            _selectedSetIndex = idx;

            if (idx >= 0 && idx < (Model?.MorphExpressionCount ?? 0))
                RefreshSetDetail(idx);
            else
            {
                _setDetail.style.display = DisplayStyle.None;
            }
        }

        // ================================================================
        // セット詳細
        // ================================================================

        private void RefreshSetDetail(int setIndex)
        {
            var model = Model;
            if (_setDetail == null || model == null) return;
            if (setIndex < 0 || setIndex >= model.MorphExpressionCount)
            {
                _setDetail.style.display = DisplayStyle.None;
                return;
            }

            _setDetail.style.display = DisplayStyle.Flex;
            var set = model.MorphExpressions[setIndex];

            _setName?.SetValueWithoutNotify(set.Name);
            _setNameEn?.SetValueWithoutNotify(set.NameEnglish);
            if (_setTypeLabel != null) _setTypeLabel.text = set.Type.ToString();

            RebuildPanelPopup(set.Panel);

            _entryData.Clear();
            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry    = set.MeshEntries[i];
                string mname = $"[{entry.MeshIndex}]";
                if (entry.MeshIndex >= 0 && entry.MeshIndex < model.MeshContextCount)
                {
                    var mc = model.GetMeshContext(entry.MeshIndex);
                    if (mc != null) mname = $"[{entry.MeshIndex}] {mc.Name}";
                }
                _entryData.Add((i, entry.MeshIndex, mname, entry.Weight));
            }
            _entryListView?.RefreshItems();

            RefreshPreviewSection(model, set, setIndex);
        }

        private void RebuildPanelPopup(int currentPanel)
        {
            if (_setPanelContainer == null) return;
            _setPanelContainer.Clear();

            var indices = new List<int> { 0, 1, 2, 3 };
            var names   = new Dictionary<int, string> { [0]="眉", [1]="目", [2]="口", [3]="その他" };

            _setPanelPopup = new PopupField<int>(indices, currentPanel,
                v => names.TryGetValue(v, out var s) ? s : v.ToString(),
                v => names.TryGetValue(v, out var s) ? s : v.ToString());
            _setPanelPopup.style.flexGrow = 1;
            _setPanelPopup.RegisterValueChangedCallback(_ => OnSetDetailChanged());
            _setPanelContainer.Add(_setPanelPopup);
        }

        private void OnSetDetailChanged()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];

            string newName   = _setName?.value?.Trim()   ?? set.Name;
            string newNameEn = _setNameEn?.value?.Trim() ?? set.NameEnglish;
            int    newPanel  = _setPanelPopup?.value     ?? set.Panel;

            if (newName == set.Name && newNameEn == set.NameEnglish && newPanel == set.Panel) return;

            var record = new MorphExpressionEditRecord
            {
                SetIndex    = _selectedSetIndex,
                OldSnapshot = set.Clone(),
            };

            set.Name        = newName;
            set.NameEnglish = newNameEn;
            set.Panel       = newPanel;
            record.NewSnapshot = set.Clone();

            RecordUndo(record, $"モーフエクスプレッション属性変更: {newName}");

            if (_selectedSetIndex < _setListData.Count)
                _setListData[_selectedSetIndex] = (_selectedSetIndex, set.Name, $"({set.Type}, {set.MeshCount}件)");
            _setListView?.RefreshItems();
        }

        // ================================================================
        // エントリウェイト
        // ================================================================

        private void OnEntryWeightStart()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            _entryEditSnapshot = model.MorphExpressions[_selectedSetIndex].Clone();
        }

        private void OnEntryWeightChanged(int entryIdx, float newWeight)
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            if (entryIdx < 0 || entryIdx >= set.MeshEntries.Count) return;

            var entry = set.MeshEntries[entryIdx];
            entry.Weight = newWeight;
            set.MeshEntries[entryIdx] = entry;

            if (entryIdx < _entryData.Count)
            {
                var d = _entryData[entryIdx];
                _entryData[entryIdx] = (d.entryIdx, d.meshIndex, d.meshName, newWeight);
            }

            if (_isPreviewActive)
                ApplyBatchPreview(model, _previewWeight?.value ?? 0f);
        }

        private void OnEntryWeightEnd()
        {
            if (_entryEditSnapshot == null) return;
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount)
            { _entryEditSnapshot = null; return; }

            var set     = model.MorphExpressions[_selectedSetIndex];
            bool changed = _entryEditSnapshot.MeshEntries.Count != set.MeshEntries.Count;
            if (!changed)
                for (int i = 0; i < set.MeshEntries.Count; i++)
                    if (Mathf.Abs(_entryEditSnapshot.MeshEntries[i].Weight - set.MeshEntries[i].Weight) > 0.0001f)
                    { changed = true; break; }

            if (changed)
                RecordUndo(new MorphExpressionEditRecord
                {
                    SetIndex    = _selectedSetIndex,
                    OldSnapshot = _entryEditSnapshot,
                    NewSnapshot = set.Clone(),
                }, $"モーフエクスプレッションウェイト変更: {set.Name}");

            _entryEditSnapshot = null;
        }

        // ================================================================
        // セット削除
        // ================================================================

        private void OnDeleteSet()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            if (!EditorUtility.DisplayDialog("削除確認", $"モーフエクスプレッション '{set.Name}' を削除しますか？", "削除", "キャンセル")) return;

            EndPreview();
            RecordUndo(new MorphExpressionChangeRecord
            {
                RemovedExpression = set.Clone(),
                RemovedIndex      = _selectedSetIndex,
            }, $"モーフエクスプレッション削除: {set.Name}");

            model.MorphExpressions.RemoveAt(_selectedSetIndex);
            _selectedSetIndex = -1;
            StatusLog($"モーフエクスプレッション '{set.Name}' を削除");
            RefreshAll();
        }

        // ================================================================
        // プレビューセクション
        // ================================================================

        private void RefreshPreviewSection(ModelContext model, MorphExpression set, int setIndex)
        {
            if (_previewSection == null) return;
            var pairs = BuildMorphBasePairs(model, set);
            if (pairs.Count == 0) { _previewSection.style.display = DisplayStyle.None; return; }

            _previewSection.style.display = DisplayStyle.Flex;
            if (_previewInfo != null) _previewInfo.text = $"対象: {pairs.Count}ペア";

            if (!_isPreviewActive || _previewMorphExpressionIndex != setIndex)
                StartBatchPreview(model, pairs, setIndex);
        }

        private void OnPreviewWeightChanged(ChangeEvent<float> evt)
        {
            var model = Model;
            if (model != null) ApplyBatchPreview(model, evt.newValue);
        }

        private void OnResetPreview()
        {
            _previewWeight?.SetValueWithoutNotify(0f);
            var model = Model;
            if (model != null) ApplyBatchPreview(model, 0f);
        }

        private void OnEndPreview()
        {
            EndPreview();
            _previewWeight?.SetValueWithoutNotify(0f);
            _selectedSetIndex = -1;
            RefreshAll();
        }

        // ================================================================
        // バッチプレビュー
        // ================================================================

        private void StartBatchPreview(ModelContext model,
            List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)> pairs,
            int setIndex)
        {
            EndPreview();
            _previewBackups.Clear();
            _previewPairs.Clear();

            foreach (var (morphIndex, baseIndex, morphCtx, baseCtx, weight) in pairs)
            {
                if (baseCtx?.MeshObject == null) continue;
                if (!_previewBackups.ContainsKey(baseIndex))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup   = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _previewBackups[baseIndex] = backup;
                }
                _previewPairs.Add((morphIndex, baseIndex, weight));
            }

            _previewMorphExpressionIndex = setIndex;
            _isPreviewActive             = true;
        }

        private void ApplyBatchPreview(ModelContext model, float weight)
        {
            if (!_isPreviewActive || _previewBackups.Count == 0) return;

            foreach (var (baseIndex, backup) in _previewBackups)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count    = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            foreach (var (morphIndex, baseIndex, entryWeight) in _previewPairs)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                var baseCtx  = model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * (entryWeight * weight);
            }

            foreach (var baseIndex in _previewBackups.Keys)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx != null) _toolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
            }
            _toolCtx?.Repaint?.Invoke();
        }

        private void EndPreview()
        {
            if (!_isPreviewActive)
            {
                _previewBackups.Clear();
                _previewPairs.Clear();
                _previewMorphExpressionIndex = -1;
                return;
            }

            var model = Model;
            if (model != null)
            {
                foreach (var (baseIndex, backup) in _previewBackups)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count    = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    _toolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }

            _previewBackups.Clear();
            _previewPairs.Clear();
            _previewMorphExpressionIndex = -1;
            _isPreviewActive             = false;
            _toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // モーフ-ベースペア構築
        // ================================================================

        private List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)>
            BuildMorphBasePairs(ModelContext model, MorphExpression set)
        {
            var pairs = new List<(int, int, MeshContext, MeshContext, float)>();
            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry    = set.MeshEntries[i];
                var morphCtx = model.GetMeshContext(entry.MeshIndex);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIndex = FindBaseMeshIndex(model, morphCtx);
                var baseCtx   = baseIndex >= 0 ? model.GetMeshContext(baseIndex) : null;
                if (baseCtx?.MeshObject != null)
                    pairs.Add((entry.MeshIndex, baseIndex, morphCtx, baseCtx, entry.Weight));
            }
            return pairs;
        }

        private static int FindBaseMeshIndex(ModelContext model, MeshContext morphCtx)
        {
            if (morphCtx == null) return -1;
            if (morphCtx.MorphParentIndex >= 0) return morphCtx.MorphParentIndex;

            string morphName = morphCtx.MorphName;
            string meshName  = morphCtx.Name;
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var ctx = model.GetMeshContext(i);
                    if (ctx != null &&
                        (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror || ctx.Type == MeshType.MirrorSide) &&
                        ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }

        // ================================================================
        // CSV
        // ================================================================

        private void OnCsvImport()
        {
            var model = Model;
            if (model == null) return;

            string path = EditorUtility.OpenFilePanel("BlendShapeSync CSV読込", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var rows = CSVHelper.ParseFile(path);
            if (rows.Count == 0) { StatusLog("CSVが空です"); return; }

            var meshNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    meshNameToIndex[mc.Name] = i;
            }

            var importedSets  = new Dictionary<string, MorphExpression>();
            int unmatchedCount = 0;

            foreach (var row in rows)
            {
                if (CSVHelper.IsCommentLine(row.OriginalLine)) continue;
                if (row.FieldCount < 4) continue;

                string expressionName = row[0];
                if (string.IsNullOrEmpty(expressionName)) continue;

                var set = new MorphExpression { Name = expressionName, NameEnglish = "", Panel = 3, Type = MorphType.Vertex };

                for (int i = 1; i + 2 < row.FieldCount; i += 3)
                {
                    string meshName  = row[i];
                    string shapeName = row[i + 1];
                    string weightStr = row[i + 2];
                    if (string.IsNullOrEmpty(meshName) || string.IsNullOrEmpty(shapeName)) continue;
                    if (!float.TryParse(weightStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float w)) continue;

                    string morphMeshName = $"{meshName}_{shapeName}";
                    if (meshNameToIndex.TryGetValue(morphMeshName, out int meshIndex))
                        set.AddMesh(meshIndex, w);
                    else
                        unmatchedCount++;
                }

                if (set.MeshCount > 0) importedSets[expressionName] = set;
            }

            if (importedSets.Count == 0)
            { StatusLog($"マッチするモーフメッシュが見つかりません (未マッチ: {unmatchedCount})"); return; }

            var overwriteNames = importedSets.Keys.Where(n => model.FindMorphExpressionByName(n) != null).ToList();
            if (overwriteNames.Count > 0)
            {
                string msg = $"以下のセットは既に存在します。上書きしますか？\n{string.Join(", ", overwriteNames)}";
                if (!EditorUtility.DisplayDialog("上書き確認", msg, "上書き", "キャンセル")) return;
            }

            var oldSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            foreach (var imported in importedSets.Values)
            {
                int existIdx = model.MorphExpressions.FindIndex(s => s.Name == imported.Name);
                if (existIdx >= 0) model.MorphExpressions[existIdx] = imported;
                else               model.MorphExpressions.Add(imported);
            }

            var newSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            RecordUndo(new MorphExpressionListReplaceRecord { OldSets = oldSets, NewSets = newSets },
                $"CSVインポート: {importedSets.Count}セット");

            string unmatchMsg = unmatchedCount > 0 ? $" (未マッチ: {unmatchedCount})" : "";
            StatusLog($"CSV読込完了: {importedSets.Count}セット ({overwriteNames.Count}件上書き){unmatchMsg}");
            RefreshAll();
        }

        private void OnCsvExport()
        {
            var model = Model;
            if (model == null || model.MorphExpressionCount == 0)
            { StatusLog("保存するモーフエクスプレッションがありません"); return; }

            string path = EditorUtility.SaveFilePanel("BlendShapeSync CSV保存", "", "blendshape_sync.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var writer = new CSVWriter(MQOExportSettings.DefaultDecimalPrecision);
                writer.AddComment(" ExpressionName,MeshName,BlendShapeName,Weight,...");

                foreach (var set in model.MorphExpressions)
                {
                    if (set.Type != MorphType.Vertex || !set.IsValid) continue;
                    var parts = new List<object> { set.Name };

                    foreach (var entry in set.MeshEntries)
                    {
                        if (entry.MeshIndex < 0 || entry.MeshIndex >= model.MeshContextCount) continue;
                        var morphCtx = model.GetMeshContext(entry.MeshIndex);
                        if (morphCtx == null || !morphCtx.IsMorph) continue;

                        int lastUnderscore = morphCtx.Name.LastIndexOf('_');
                        if (lastUnderscore <= 0) continue;

                        parts.Add(morphCtx.Name.Substring(0, lastUnderscore));
                        parts.Add(set.Name);
                        parts.Add(entry.Weight);
                    }

                    if (parts.Count > 1) writer.AddRow(parts.ToArray());
                }

                writer.WriteToFile(path);
                StatusLog($"CSV保存完了: {model.MorphExpressionCount}セット → {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { StatusLog($"CSV保存失敗: {ex.Message}"); }

        }

        // ================================================================
        // Undo
        // ================================================================

        private void RecordUndo(MeshListUndoRecord record, string description)
        {
            var undoController = _toolCtx?.UndoController;
            if (undoController == null) return;
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void StatusLog(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            Debug.Log($"[MorphPanelV2] {msg}");
        }
    }

}
