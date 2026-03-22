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

        private readonly MorphPreviewState _previewState = new();

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

            if (_previewState.IsActive)
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

            if (!_previewState.IsActive || _previewState.ActiveSetIndex != setIndex)
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
            _previewState.Start(model, pairs, setIndex);
        }

        private void ApplyBatchPreview(ModelContext model, float weight)
        {
            _previewState.Apply(model, weight, _toolCtx);
        }

        private void EndPreview()
        {
            _previewState.End(Model, _toolCtx);
        }

        // ================================================================
        // モーフ-ベースペア構築
        // ================================================================

        private List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)>
            BuildMorphBasePairs(ModelContext model, MorphExpression set)
            => MorphPreviewState.BuildMorphBasePairs(model, set);


        // ================================================================
        // CSV
        // ================================================================

        private void OnCsvImport()
        {
            var model = Model;
            if (model == null) return;
            var oldSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            var (imported, overwritten, unmatched) = MorphCsvIO.Import(model, StatusLog);
            if (imported == 0) return;
            var newSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            RecordUndo(new MorphExpressionListReplaceRecord { OldSets = oldSets, NewSets = newSets },
                $"CSVインポート: {imported}セット");
            RefreshAll();
        }

        private void OnCsvExport()
        {
            MorphCsvIO.Export(Model, StatusLog);
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
