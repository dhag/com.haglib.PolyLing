// PlayerMorphSubPanel.cs
// MorphPanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.UI;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerMorphSubPanel
    {
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;

        private ModelContext Model => GetModel?.Invoke();

        // データ
        private readonly List<(int index, string name, string info)> _setListData = new List<(int, string, string)>();
        private readonly List<(int entryIdx, int meshIndex, string meshName, float weight)> _entryData = new List<(int, int, string, float)>();
        private int             _selectedSetIndex  = -1;
        private MorphExpression _entryEditSnapshot;
        private readonly MorphPreviewState _previewState = new MorphPreviewState();

        // UI
        private Label         _warningLabel;
        private Label         _statusLabel;
        private ListView      _setListView;
        private VisualElement _setDetail;
        private TextField     _setName, _setNameEn;
        private DropdownField _panelPopup;
        private Label         _setTypeLabel;
        private ListView      _entryListView;
        private VisualElement _previewSection;
        private Label         _previewInfo;
        private Slider        _previewWeight;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("モーフエクスプレッション"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // セットリスト
            _setListView = new ListView(_setListData, 20, SetMakeItem, SetBindItem);
            _setListView.style.height       = 120;
            _setListView.style.marginBottom = 4;
            _setListView.selectionChanged  += OnSetSelectionChanged;
            root.Add(_setListView);

            // CSV ボタン
            var csvRow = new VisualElement(); csvRow.style.flexDirection = FlexDirection.Row; csvRow.style.marginBottom = 6;
            var btnImport = new Button(OnCsvImport) { text = "CSV読込" }; btnImport.style.flexGrow = 1; btnImport.style.marginRight = 4;
            var btnExport = new Button(OnCsvExport) { text = "CSV保存" }; btnExport.style.flexGrow = 1;
            csvRow.Add(btnImport); csvRow.Add(btnExport);
            root.Add(csvRow);

            // セット詳細
            _setDetail = new VisualElement();
            _setDetail.style.display = DisplayStyle.None;
            BuildSetDetailUI(_setDetail);
            root.Add(_setDetail);

            _statusLabel = new Label(); _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        private void BuildSetDetailUI(VisualElement parent)
        {
            parent.Add(MakeSep());

            _setName   = new TextField("名前 (JP)");
            _setName.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());
            parent.Add(_setName);

            _setNameEn = new TextField("名前 (EN)");
            _setNameEn.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());
            parent.Add(_setNameEn);

            // Panel (眉/目/口/その他)
            _panelPopup = new DropdownField("パネル",
                new System.Collections.Generic.List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }, 3);
            _panelPopup.style.marginBottom = 3;
            _panelPopup.RegisterValueChangedCallback(_ => OnSetDetailChanged());
            parent.Add(_panelPopup);

            var typeRow = new VisualElement(); typeRow.style.flexDirection = FlexDirection.Row; typeRow.style.marginBottom = 2;
            typeRow.Add(new Label("タイプ: ") { style = { width = 60 } });
            _setTypeLabel = new Label(); _setTypeLabel.style.color = new StyleColor(Color.white);
            typeRow.Add(_setTypeLabel);
            parent.Add(typeRow);

            var btnDelete = new Button(OnDeleteSet) { text = "このセットを削除" };
            btnDelete.style.marginBottom = 6;
            parent.Add(btnDelete);

            parent.Add(SecLabel("エントリ (モーフメッシュ / ウェイト)"));

            _previewSection = new VisualElement();
            _previewSection.style.display = DisplayStyle.None;
            parent.Add(_previewSection);
            BuildPreviewSectionUI(_previewSection);

            _entryListView = new ListView(_entryData, 22, EntryMakeItem, EntryBindItem);
            _entryListView.style.height      = 140;
            _entryListView.style.flexShrink  = 0;
            _entryListView.style.marginBottom = 6;
            parent.Add(_entryListView);
        }

        private void BuildPreviewSectionUI(VisualElement parent)
        {
            parent.Add(SecLabel("プレビュー"));
            _previewInfo = new Label(); _previewInfo.style.fontSize = 10; _previewInfo.style.color = new StyleColor(Color.white); _previewInfo.style.marginBottom = 2;
            parent.Add(_previewInfo);
            _previewWeight = new Slider("ウェイト", 0f, 1f) { value = 0f };
            _previewWeight.style.marginBottom = 4;
            _previewWeight.RegisterValueChangedCallback(OnPreviewWeightChanged);
            parent.Add(_previewWeight);
            var btnEnd = new Button(OnEndPreview) { text = "プレビュー終了" };
            btnEnd.style.marginBottom = 4;
            parent.Add(btnEnd);
        }

        // ── ListView helpers ─────────────────────────────────────────────
        private VisualElement SetMakeItem()
        {
            var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }
        private void SetBindItem(VisualElement e, int i)
        {
            if (e is Label l && i < _setListData.Count) l.text = $"[{_setListData[i].index}] {_setListData[i].name}  {_setListData[i].info}";
        }

        private VisualElement EntryMakeItem()
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label(); lbl.style.width = 120; lbl.name = "lbl";
            var sl  = new Slider(0f, 1f) { name = "slider" }; sl.style.flexGrow = 1;
            row.Add(lbl); row.Add(sl);
            return row;
        }
        private void EntryBindItem(VisualElement e, int i)
        {
            if (i >= _entryData.Count) return;
            var (entryIdx, _, meshName, weight) = _entryData[i];
            if (e.Q<Label>("lbl") is Label l) l.text = meshName;
            if (e.Q<Slider>("slider") is Slider sl)
            {
                sl.SetValueWithoutNotify(weight);
                int ci = entryIdx;
                sl.RegisterCallback<PointerDownEvent>(_ => OnEntryWeightStart());
                sl.RegisterCallback<PointerUpEvent>(_  => OnEntryWeightEnd());
                sl.RegisterValueChangedCallback(evt => OnEntryWeightChanged(ci, evt.newValue));
            }
        }

        // ── Refresh ───────────────────────────────────────────────────────
        public void Refresh()
        {
            if (_warningLabel == null) return;
            var model = Model;
            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;
            RefreshAll();
        }

        private void RefreshAll()
        {
            var model = Model; if (model == null) return;
            _setListData.Clear();
            for (int i = 0; i < model.MorphExpressionCount; i++)
            {
                var s = model.MorphExpressions[i];
                _setListData.Add((i, s.Name ?? "", $"({s.Type}, {s.MeshCount}件)"));
            }
            _setListView?.RefreshItems();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setListData.Count - 1);
            if (_selectedSetIndex >= 0) { _setListView?.SetSelection(_selectedSetIndex); RefreshSetDetail(_selectedSetIndex); }
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
        }

        private void RefreshSetDetail(int setIndex)
        {
            var model = Model;
            if (_setDetail == null || model == null || setIndex < 0 || setIndex >= model.MorphExpressionCount)
            { if (_setDetail != null) _setDetail.style.display = DisplayStyle.None; return; }
            _setDetail.style.display = DisplayStyle.Flex;
            var set = model.MorphExpressions[setIndex];
            _setName?.SetValueWithoutNotify(set.Name);
            _setNameEn?.SetValueWithoutNotify(set.NameEnglish);
            if (_setTypeLabel != null) _setTypeLabel.text = set.Type.ToString();
            _panelPopup?.SetValueWithoutNotify(new[] { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }[System.Math.Clamp(set.Panel, 0, 3)]);
            _entryData.Clear();
            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry = set.MeshEntries[i];
                string mname = $"[{entry.MeshIndex}]";
                if (entry.MeshIndex >= 0 && entry.MeshIndex < model.MeshContextCount)
                { var mc = model.GetMeshContext(entry.MeshIndex); if (mc != null) mname = $"[{entry.MeshIndex}] {mc.Name}"; }
                _entryData.Add((i, entry.MeshIndex, mname, entry.Weight));
            }
            _entryListView?.RefreshItems();
            // プレビュー
            var pairs = BuildMorphBasePairs(model, set);
            if (_previewSection != null) _previewSection.style.display = pairs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (pairs.Count > 0 && _previewInfo != null) _previewInfo.text = $"対象: {pairs.Count}ペア";
            if (pairs.Count > 0 && (!_previewState.IsActive || _previewState.ActiveSetIndex != setIndex))
                StartBatchPreview(model, pairs, setIndex);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            int idx = _setListView?.selectedIndex ?? -1;
            if (idx == _selectedSetIndex) return;
            EndPreview(); _previewWeight?.SetValueWithoutNotify(0f);
            _selectedSetIndex = idx;
            if (idx >= 0 && idx < (Model?.MorphExpressionCount ?? 0)) RefreshSetDetail(idx);
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
        }

        private void OnSetDetailChanged()
        {
            var model = Model; if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            string newName   = _setName?.value?.Trim()   ?? set.Name;
            string newNameEn = _setNameEn?.value?.Trim() ?? set.NameEnglish;
            int    panelIdx  = _panelPopup?.index ?? set.Panel;
            int    newPanel  = System.Math.Clamp(panelIdx, 0, 3);
            if (newName == set.Name && newNameEn == set.NameEnglish && newPanel == set.Panel) return;
            var record = new MorphExpressionEditRecord { SetIndex = _selectedSetIndex, OldSnapshot = set.Clone() };
            set.Name = newName; set.NameEnglish = newNameEn; set.Panel = newPanel;
            record.NewSnapshot = set.Clone();
            RecordUndo(record, $"モーフエクスプレッション属性変更: {newName}");
            if (_selectedSetIndex < _setListData.Count) _setListData[_selectedSetIndex] = (_selectedSetIndex, set.Name, $"({set.Type}, {set.MeshCount}件)");
            _setListView?.RefreshItems();
        }

        private void OnDeleteSet()
        {
            var model = Model; if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"モーフエクスプレッション '{set.Name}' を削除しますか？", "削除", "キャンセル");
            if (!ok) return;
            EndPreview();
            RecordUndo(new MorphExpressionChangeRecord { RemovedExpression = set.Clone(), RemovedIndex = _selectedSetIndex }, $"モーフエクスプレッション削除: {set.Name}");
            model.MorphExpressions.RemoveAt(_selectedSetIndex);
            _selectedSetIndex = -1;
            StatusLog($"モーフエクスプレッション '{set.Name}' を削除");
            RefreshAll();
        }

        private void OnCsvImport()
        {
            var model = Model; if (model == null) return;
            var oldSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            var (imported, overwritten, unmatched) = MorphCsvIO.Import(model, StatusLog);
            if (imported == 0) return;
            var newSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            RecordUndo(new MorphExpressionListReplaceRecord { OldSets = oldSets, NewSets = newSets }, $"CSVインポート: {imported}セット");
            RefreshAll();
        }

        private void OnCsvExport() => MorphCsvIO.Export(Model, StatusLog);

        // ── Preview ──────────────────────────────────────────────────────
        private void OnPreviewWeightChanged(ChangeEvent<float> evt)
        {
            var model = Model; if (model != null) ApplyBatchPreview(model, evt.newValue);
        }
        private void OnResetPreview() { _previewWeight?.SetValueWithoutNotify(0f); var model = Model; if (model != null) ApplyBatchPreview(model, 0f); }
        private void OnEndPreview()   { EndPreview(); _previewWeight?.SetValueWithoutNotify(0f); }

        private void StartBatchPreview(ModelContext model,
            List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)> pairs,
            int setIndex)
        {
            _previewState.Start(model, pairs, setIndex);
        }

        private void ApplyBatchPreview(ModelContext model, float weight)
        {
            var tc = GetToolContext?.Invoke();
            _previewState.Apply(model, weight, tc);
        }

        private void EndPreview()
        {
            var model = Model; var tc = GetToolContext?.Invoke();
            if (model != null) _previewState.End(model, tc);
        }

        private List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)>
            BuildMorphBasePairs(ModelContext model, MorphExpression set)
            => MorphPreviewState.BuildMorphBasePairs(model, set);

        // ── Undo ─────────────────────────────────────────────────────────
        private void RecordUndo(MeshListUndoRecord record, string description)
        {
            var tc = GetToolContext?.Invoke();
            var undo = tc?.UndoController; if (undo == null) return;
            undo.MeshListStack.Record(record, description);
            undo.FocusMeshList();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void StatusLog(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }
        private static VisualElement MakeSep() { var s = new VisualElement(); s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white); s.style.marginTop = 2; s.style.marginBottom = 6; return s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }

        private void OnEntryWeightStart()
        {
            var model = Model; if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            _entryEditSnapshot = model.MorphExpressions[_selectedSetIndex].Clone();
        }

        private void OnEntryWeightChanged(int entryIdx, float newWeight)
        {
            var model = Model; if (model == null || _selectedSetIndex < 0) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            if (entryIdx < 0 || entryIdx >= set.MeshEntries.Count) return;
            var entryVal = set.MeshEntries[entryIdx];
            entryVal.Weight = newWeight;
            set.MeshEntries[entryIdx] = entryVal;
            if (_entryData.Count > entryIdx)
                _entryData[entryIdx] = (_entryData[entryIdx].entryIdx, _entryData[entryIdx].meshIndex, _entryData[entryIdx].meshName, newWeight);
            if (_previewState.IsActive) ApplyBatchPreview(model, _previewWeight?.value ?? 1f);
        }

        private void OnEntryWeightEnd()
        {
            if (_entryEditSnapshot == null) return;
            var model = Model; if (model == null || _selectedSetIndex < 0) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            bool changed = false;
            for (int i = 0; i < set.MeshEntries.Count && i < _entryEditSnapshot.MeshEntries.Count; i++)
                if (Mathf.Abs(_entryEditSnapshot.MeshEntries[i].Weight - set.MeshEntries[i].Weight) > 0.0001f) { changed = true; break; }
            if (changed)
                RecordUndo(new MorphExpressionEditRecord { SetIndex = _selectedSetIndex, OldSnapshot = _entryEditSnapshot, NewSnapshot = set.Clone() }, $"モーフウェイト変更: {set.Name}");
            _entryEditSnapshot = null;
        }
    }
}
