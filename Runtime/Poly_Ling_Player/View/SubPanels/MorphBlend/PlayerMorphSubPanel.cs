// PlayerMorphSubPanel.cs
// 表情（モーフエクスプレッション）の編集パネル。Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【PolyLing_モーフ整理計画.md 3.11・段 5】表情を「形の名前 × 重み」で編集する。
//   ・一覧の上の欄と「作る」でグループの表情を作る（AddExpressionCommand）。
//   ・選んだ表情の行：
//       形の行      … グループの子のうち形を指すもの。重みの欄で変え、「外す」で外す。
//                     「形を足す」で形を足す（SetExpressionShapesCommand）。
//       形（固定）  … 頂点の表情で、同じ名前の形があるもの（表情はその形そのもの。重みは 1）。
//   ・名前・英語名・パネルは SetExpressionAttributesCommand、削除は DeleteExpressionCommand。
//   ・選んだ表情は試し表示する（ツールの窓口 "morphExpression"。始めるときに基本の形へ切り替わる）。
//
// 【操作経路統一計画.md E】
//   読み取りは窓口（IProjectView の MorphExpressions・Shapes）、書き込みはコマンド、
//   試し表示と重みを動かしている間のプレビューはツールの窓口で行う。パネルはモデルのデータにも頂点にも触れない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerMorphSubPanel
    {
        /// <summary>プロジェクトの窓口。表情と形の写しを読む。</summary>
        public Func<IProjectView>   GetView;
        /// <summary>ツールの窓口（"morphExpression"）。試し表示と重みを動かしている間のプレビュー。</summary>
        public IToolSurface         Surface;
        /// <summary>書き込みのコマンドの送り先。</summary>
        public Action<PanelCommand> SendCommand;
        private const string Tool = "morphExpression";

        private IModelView Model      => GetView?.Invoke()?.CurrentModel;
        private int        ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>選んだ表情の行 1 つ。</summary>
        private struct Row
        {
            public RowKind Kind;
            public string  Label;
            public string  ShapeName;   // 形の行・形（固定）
            public float   Weight;
        }
        private enum RowKind { Shape, FixedShape }

        // データ
        private readonly List<(int index, string name, string info)> _setListData = new List<(int, string, string)>();
        private readonly List<Row> _rows = new List<Row>();
        private int  _selectedSetIndex = -1;
        private int  _selectedRow      = -1;
        private bool _weightDragging;

        // UI
        // UI 自動操作の ID は "morph.<下の Id>"（UiControlAttribute.cs）。
        // 表情の詳細は表情を選んだときだけ表示される（sets で行を選ぶ）。
        // 名前欄はフォーカスが外れたときに確定する作りなので、外からは確定まで行う Setter を通す。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label         _warningLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        [UiControl("newName", Description = "作るグループの表情の名前")]
        private TextField     _newName;
        [UiControl("add", Safety = UiSafety.SafeWrite, Description = "グループの表情を作る（形はあとで足す）")]
        private Button        _btnAdd;
        [UiControl("sets", Description = "表情の一覧（一覧の行番号）")]
        private ListView      _setListView;
        [UiControl(Ignore = true)]
        private VisualElement _setDetail;
        [UiControl("set.name", Setter = nameof(SetNameByAutomation), Description = "選んだ表情の名前（JP）")]
        private TextField     _setName;
        [UiControl("set.nameEn", Setter = nameof(SetNameEnByAutomation), Description = "選んだ表情の名前（EN）")]
        private TextField     _setNameEn;
        [UiControl("set.panel", Description = "選んだ表情のパネル（眉 / 目 / 口 / その他）")]
        private DropdownField _panelPopup;
        [UiControl("set.type", Safety = UiSafety.ReadOnly, Description = "選んだ表情の種類")]
        private Label         _setTypeLabel;
        [UiControl("set.entries", Description = "選んだ表情の行（形 × 重み。一覧の行番号）")]
        private ListView      _entryListView;
        [UiControl("entry.weight", Description = "選んだ行の重み（形の行）")]
        private Slider        _entryWeight;
        [UiControl("entry.remove", Safety = UiSafety.SafeWrite, Description = "選んだ形の行を外す")]
        private Button        _btnEntryRemove;
        [UiControl("entry.addShape", Description = "足す形（グループの表情だけ）")]
        private DropdownField _addShape;
        [UiControl("entry.add", Safety = UiSafety.SafeWrite, Description = "選んだ形を重み 1 で足す（グループの表情だけ）")]
        private Button        _btnEntryAdd;
        [UiControl(Ignore = true)]
        private VisualElement _addRow;
        [UiControl(Ignore = true)]
        private VisualElement _previewSection;
        [UiControl("preview.info", Safety = UiSafety.ReadOnly, Description = "プレビューの情報（プレビュー中だけ表示）")]
        private Label         _previewInfo;
        [UiControl("preview.weight", Description = "プレビューのウェイト（プレビュー中だけ表示）")]
        private Slider        _previewWeight;
        [UiControl("preview.end", Safety = UiSafety.SafeWrite, Description = "プレビューを終える")]
        private Button        _btnEndPreview;
        [UiControl("importCsv", Safety = UiSafety.UserOnly, Description = "CSV を読み込む（ファイル選択ダイアログを開く）")]
        private Button        _btnCsvImport;
        [UiControl("exportCsv", Safety = UiSafety.UserOnly, Description = "CSV に保存する（保存ダイアログを開く）")]
        private Button        _btnCsvExport;
        [UiControl("set.delete", Safety = UiSafety.UserOnly, Description = "選んだ表情を削除する（確認ダイアログを開く）")]
        private Button        _btnDeleteSet;

        /// <summary>UI 自動操作から名前（JP）を設定する。フォーカスが外れたときと同じく確定まで行う。</summary>
        private string SetNameByAutomation(string value)
        {
            if (_setName == null) return "名前欄がありません";
            _setName.value = value;
            OnSetDetailChanged();
            return null;
        }

        /// <summary>UI 自動操作から名前（EN）を設定する。フォーカスが外れたときと同じく確定まで行う。</summary>
        private string SetNameEnByAutomation(string value)
        {
            if (_setNameEn == null) return "名前欄がありません";
            _setNameEn.value = value;
            OnSetDetailChanged();
            return null;
        }

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("表情（形 × 重み）"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // 作る
            var addRow = new VisualElement(); addRow.style.flexDirection = FlexDirection.Row; addRow.style.marginBottom = 4;
            _newName = new TextField("新しい表情"); _newName.style.flexGrow = 1;
            _btnAdd = new Button(OnAddExpression) { text = "作る" };
            addRow.Add(_newName); addRow.Add(_btnAdd);
            root.Add(addRow);

            // 一覧
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
            _btnCsvImport = btnImport;
            _btnCsvExport = btnExport;

            // 詳細
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

            _panelPopup = new DropdownField("パネル",
                new List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }, 3);
            _panelPopup.style.color = new StyleColor(Color.white);
            _panelPopup.style.marginBottom = 3;
            _panelPopup.RegisterValueChangedCallback(_ => OnSetDetailChanged());
            parent.Add(_panelPopup);

            var typeRow = new VisualElement(); typeRow.style.flexDirection = FlexDirection.Row; typeRow.style.marginBottom = 2;
            typeRow.Add(new Label("種類: ") { style = { width = 60 } });
            _setTypeLabel = new Label(); _setTypeLabel.style.color = new StyleColor(Color.white);
            typeRow.Add(_setTypeLabel);
            parent.Add(typeRow);

            var btnDelete = new Button(OnDeleteSet) { text = "この表情を削除" };
            btnDelete.style.marginBottom = 6;
            parent.Add(btnDelete);
            _btnDeleteSet = btnDelete;

            _previewSection = new VisualElement();
            _previewSection.style.display = DisplayStyle.None;
            parent.Add(_previewSection);
            BuildPreviewSectionUI(_previewSection);

            parent.Add(SecLabel("形 × 重み"));

            _entryListView = new ListView(_rows, 20, EntryMakeItem, EntryBindItem);
            _entryListView.style.height      = 120;
            _entryListView.style.flexShrink  = 0;
            _entryListView.style.marginBottom = 4;
            _entryListView.selectionChanged += _ => OnRowSelected(_entryListView.selectedIndex);
            parent.Add(_entryListView);

            _entryWeight = new Slider("重み", 0f, 1f) { value = 0f };
            _entryWeight.style.color = new StyleColor(Color.white);
            _entryWeight.RegisterCallback<PointerDownEvent>(_ => _weightDragging = true, TrickleDown.TrickleDown);
            _entryWeight.RegisterCallback<PointerUpEvent>(_ => OnEntryWeightEnd(), TrickleDown.TrickleDown);
            _entryWeight.RegisterCallback<PointerCaptureOutEvent>(_ => OnEntryWeightEnd());
            _entryWeight.RegisterValueChangedCallback(evt => OnEntryWeightChanged(evt.newValue));
            parent.Add(_entryWeight);

            _btnEntryRemove = new Button(OnRemoveRow) { text = "選んだ形を外す" };
            _btnEntryRemove.style.marginBottom = 4;
            parent.Add(_btnEntryRemove);

            _addRow = new VisualElement(); _addRow.style.flexDirection = FlexDirection.Row; _addRow.style.marginBottom = 6;
            _addShape = new DropdownField("足す形", new List<string>(), 0); _addShape.style.flexGrow = 1;
            _addShape.style.color = new StyleColor(Color.white);
            _btnEntryAdd = new Button(OnAddShape) { text = "足す" };
            _addRow.Add(_addShape); _addRow.Add(_btnEntryAdd);
            parent.Add(_addRow);
        }

        private void BuildPreviewSectionUI(VisualElement parent)
        {
            parent.Add(SecLabel("試し表示"));
            _previewInfo = new Label(); _previewInfo.style.fontSize = 10; _previewInfo.style.color = new StyleColor(Color.white); _previewInfo.style.marginBottom = 2;
            parent.Add(_previewInfo);
            _previewWeight = new Slider("ウェイト", 0f, 1f) { value = 0f };
            _previewWeight.style.color = new StyleColor(Color.white);
            _previewWeight.style.marginBottom = 4;
            _previewWeight.RegisterValueChangedCallback(OnPreviewWeightChanged);
            parent.Add(_previewWeight);
            var btnEnd = new Button(OnEndPreview) { text = "試し表示を終える" };
            btnEnd.style.marginBottom = 4;
            parent.Add(btnEnd);
            _btnEndPreview = btnEnd;
        }

        // ── ListView helpers ─────────────────────────────────────────────
        private VisualElement SetMakeItem()
        {
            var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.color = new StyleColor(Color.white);
            return l;
        }
        private void SetBindItem(VisualElement e, int i)
        {
            if (e is Label l && i < _setListData.Count) l.text = $"[{_setListData[i].index}] {_setListData[i].name}  {_setListData[i].info}";
        }

        private VisualElement EntryMakeItem()
        {
            var l = new Label(); l.style.paddingLeft = 4; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.color = new StyleColor(Color.white);
            return l;
        }
        private void EntryBindItem(VisualElement e, int i)
        {
            if (e is Label l && i < _rows.Count) l.text = $"{_rows[i].Label}  ×{_rows[i].Weight:0.00}";
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
            var sets = Model?.MorphExpressions; if (sets == null) return;
            _setListData.Clear();
            for (int i = 0; i < sets.Count; i++)
                _setListData.Add((i, sets[i].Name ?? "", $"({TypeLabel(sets[i])}, {CountRows(sets[i])}件)"));
            _setListView?.RefreshItems();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setListData.Count - 1);
            if (_selectedSetIndex >= 0) { _setListView?.SetSelectionWithoutNotify(new[] { _selectedSetIndex }); RefreshSetDetail(_selectedSetIndex); }
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
        }

        private static string TypeLabel(MorphExpressionView v)
            => v.TypeName == nameof(MorphType.Group) ? "グループ" : v.TypeName == nameof(MorphType.Vertex) ? "頂点" : v.TypeName;

        private static int CountRows(MorphExpressionView v)
        {
            var rows = new List<Row>();
            BuildRows(v, rows);
            return rows.Count;
        }

        /// <summary>表情の写しから行を作る（冒頭の規則）。</summary>
        private static void BuildRows(MorphExpressionView v, List<Row> rows)
        {
            rows.Clear();
            if (v == null) return;
            bool isGroup = v.TypeName == nameof(MorphType.Group);
            if (!isGroup && v.HasSameNameShape)
            {
                rows.Add(new Row { Kind = RowKind.FixedShape, Label = $"形: {v.Name}（この表情は形そのもの）", ShapeName = v.Name, Weight = 1f });
                return;
            }
            if (isGroup)
                foreach (var c in v.Children)
                    if (c.IsShape) rows.Add(new Row { Kind = RowKind.Shape, Label = $"形: {c.Name}", ShapeName = c.Name, Weight = c.Weight });
        }

        private void RefreshSetDetail(int setIndex)
        {
            var sets = Model?.MorphExpressions;
            if (_setDetail == null || sets == null || setIndex < 0 || setIndex >= sets.Count)
            { if (_setDetail != null) _setDetail.style.display = DisplayStyle.None; return; }
            _setDetail.style.display = DisplayStyle.Flex;
            var set = sets[setIndex];
            _setName?.SetValueWithoutNotify(set.Name);
            _setNameEn?.SetValueWithoutNotify(set.NameEnglish);
            if (_setTypeLabel != null) _setTypeLabel.text = TypeLabel(set);
            _panelPopup?.SetValueWithoutNotify(new[] { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }[Math.Clamp(set.Panel, 0, 3)]);

            BuildRows(set, _rows);
            _entryListView?.RefreshItems();
            _selectedRow = Mathf.Clamp(_selectedRow, -1, _rows.Count - 1);
            if (_selectedRow >= 0) _entryListView?.SetSelectionWithoutNotify(new[] { _selectedRow });
            else _entryListView?.ClearSelection();
            RefreshRowControls();

            // 足す形（グループだけ）
            bool isGroup = set.TypeName == nameof(MorphType.Group);
            if (_addRow != null) _addRow.style.display = isGroup ? DisplayStyle.Flex : DisplayStyle.None;
            if (_addShape != null)
            {
                var choices = new List<string>();
                var shapes = Model?.Shapes;
                if (shapes != null) foreach (var s in shapes) if (s != null && !string.IsNullOrEmpty(s.Name)) choices.Add(s.Name);
                string keep = _addShape.value;
                _addShape.choices = choices;
                _addShape.SetValueWithoutNotify(choices.Contains(keep) ? keep : (choices.Count > 0 ? choices[0] : ""));
                _btnEntryAdd?.SetEnabled(choices.Count > 0);
            }

            // 試し表示の欄（行が 1 つも無い表情では出さない）。
            // 試し表示を始めるのは人の操作（表情を選ぶ・ウェイトを動かす・形を足す/外す）のときだけ。
            // 画面の更新（Undo・Redo の後など）で始めると、始めるときの基本の形への切り替えが Undo と食い違う。
            if (_previewSection != null) _previewSection.style.display = _rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshPreviewInfo();
        }

        private void RefreshPreviewInfo()
        {
            if (_previewInfo == null) return;
            bool active = Surface != null && Surface.GetInt(Tool, "previewSetIndex") == _selectedSetIndex && _selectedSetIndex >= 0;
            int pairCount = active ? Surface.GetInt(Tool, "previewPairCount") : 0;
            _previewInfo.text = active
                ? $"試し表示中: {pairCount} 組（基本の形で表示）"
                : "ウェイトを動かすと試し表示を始める（基本の形へ切り替わる）";
            if (!active) _previewWeight?.SetValueWithoutNotify(0f);
        }

        /// <summary>選んだ表情の試し表示を始める（始まっていれば何もしない）。人の操作からだけ呼ぶ。</summary>
        private void EnsurePreview()
        {
            if (Surface == null || _selectedSetIndex < 0) return;
            if (Surface.GetInt(Tool, "previewSetIndex") != _selectedSetIndex)
                Surface.Invoke(Tool, "startPreview", ("setIndex", _selectedSetIndex));
        }

        private void RefreshRowControls()
        {
            bool has = _selectedRow >= 0 && _selectedRow < _rows.Count;
            var kind = has ? _rows[_selectedRow].Kind : RowKind.FixedShape;
            _entryWeight?.SetEnabled(has && kind != RowKind.FixedShape);
            _entryWeight?.SetValueWithoutNotify(has ? _rows[_selectedRow].Weight : 0f);
            _btnEntryRemove?.SetEnabled(has && kind == RowKind.Shape);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            int idx = _setListView?.selectedIndex ?? -1;
            if (idx == _selectedSetIndex) return;
            EndPreview(); _previewWeight?.SetValueWithoutNotify(0f);
            _selectedSetIndex = idx;
            _selectedRow = -1;
            var sets = Model?.MorphExpressions;
            if (sets != null && idx >= 0 && idx < sets.Count) RefreshSetDetail(idx);
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
        }

        private void OnRowSelected(int row)
        {
            _selectedRow = row;
            RefreshRowControls();
        }

        private void OnAddExpression()
        {
            string name = _newName?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { StatusLog("表情の名前を入れてください"); return; }
            int before = Model?.MorphExpressions?.Count ?? 0;
            SendCommand?.Invoke(new AddExpressionCommand(ModelIndex, name));
            var sets = Model?.MorphExpressions;
            if (sets != null && sets.Count > before)
            {
                EndPreview();
                _selectedSetIndex = sets.Count - 1;
                _selectedRow = -1;
                StatusLog($"表情 '{name}' を作りました（形を足してください）");
            }
            RefreshAll();
        }

        /// <summary>選んだ表情の形の行の並び（名前・重み）。</summary>
        private void CurrentShapeRows(out List<string> names, out List<float> weights)
        {
            names = new List<string>(); weights = new List<float>();
            foreach (var r in _rows)
                if (r.Kind == RowKind.Shape) { names.Add(r.ShapeName); weights.Add(r.Weight); }
        }

        private void OnAddShape()
        {
            if (_selectedSetIndex < 0) return;
            string shape = _addShape?.value;
            if (string.IsNullOrEmpty(shape)) return;
            CurrentShapeRows(out var names, out var weights);
            names.Add(shape); weights.Add(1f);
            SendCommand?.Invoke(new SetExpressionShapesCommand(ModelIndex, _selectedSetIndex, names.ToArray(), weights.ToArray()));
            _selectedRow = -1;
            RestartPreview();
        }

        private void OnRemoveRow()
        {
            if (_selectedSetIndex < 0 || _selectedRow < 0 || _selectedRow >= _rows.Count) return;
            if (_rows[_selectedRow].Kind != RowKind.Shape) return;
            var names = new List<string>(); var weights = new List<float>();
            for (int i = 0; i < _rows.Count; i++)
                if (i != _selectedRow && _rows[i].Kind == RowKind.Shape) { names.Add(_rows[i].ShapeName); weights.Add(_rows[i].Weight); }
            SendCommand?.Invoke(new SetExpressionShapesCommand(ModelIndex, _selectedSetIndex, names.ToArray(), weights.ToArray()));
            _selectedRow = -1;
            RestartPreview();
        }

        /// <summary>表情の中身を変えたあと、試し表示を作り直す（今のウェイトを保つ）。</summary>
        private void RestartPreview()
        {
            float w = _previewWeight?.value ?? 0f;
            EndPreview();
            RefreshAll();
            if (w != 0f && Surface != null)
            {
                EnsurePreview();
                if (Surface.GetInt(Tool, "previewSetIndex") == _selectedSetIndex)
                {
                    _previewWeight?.SetValueWithoutNotify(w);
                    Surface.Invoke(Tool, "applyPreview", ("weight", w));
                }
                RefreshPreviewInfo();
            }
        }

        private void OnSetDetailChanged()
        {
            var sets = Model?.MorphExpressions;
            if (sets == null || _selectedSetIndex < 0 || _selectedSetIndex >= sets.Count) return;
            var set = sets[_selectedSetIndex];
            string newName   = _setName?.value?.Trim()   ?? set.Name;
            string newNameEn = _setNameEn?.value?.Trim() ?? set.NameEnglish;
            int    newPanel  = Math.Clamp(_panelPopup?.index ?? set.Panel, 0, 3);
            if (newName == set.Name && newNameEn == set.NameEnglish && newPanel == set.Panel) return;
            SendCommand?.Invoke(new SetExpressionAttributesCommand(ModelIndex, _selectedSetIndex, newName, newNameEn, newPanel));
            RefreshAll();
        }

        private void OnDeleteSet()
        {
            var sets = Model?.MorphExpressions;
            if (sets == null || _selectedSetIndex < 0 || _selectedSetIndex >= sets.Count) return;
            string name = sets[_selectedSetIndex].Name;
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"表情 '{name}' を削除しますか？（形は消えません）", "削除", "キャンセル");
            if (!ok) return;
            EndPreview();
            SendCommand?.Invoke(new DeleteExpressionCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1;
            StatusLog($"表情 '{name}' を削除");
            RefreshAll();
        }

        private void OnCsvImport()
        {
            if (Model == null) return;
            string path = MorphCsvIO.AskImportPath();
            if (string.IsNullOrEmpty(path)) return;
            int before = Model?.MorphExpressions.Count ?? 0;
            SendCommand?.Invoke(new ImportMorphCsvCommand(ModelIndex, path));
            StatusLog($"CSV読込: {before} → {Model?.MorphExpressions.Count ?? 0} 件");
            RefreshAll();
        }

        private void OnCsvExport()
        {
            if (Model == null) return;
            string path = MorphCsvIO.AskExportPath();
            if (string.IsNullOrEmpty(path)) return;
            SendCommand?.Invoke(new ExportMorphCsvCommand(ModelIndex, path));
            StatusLog($"CSV保存: {System.IO.Path.GetFileName(path)}");
        }

        // ── Preview（ツールの窓口 "morphExpression"）──────────────────────
        private void OnPreviewWeightChanged(ChangeEvent<float> evt)
        {
            EnsurePreview();
            Surface?.Invoke(Tool, "applyPreview", ("weight", evt.newValue));
            if (_previewInfo != null && Surface != null && Surface.GetInt(Tool, "previewSetIndex") == _selectedSetIndex)
                _previewInfo.text = $"試し表示中: {Surface.GetInt(Tool, "previewPairCount")} 組（基本の形で表示）";
        }

        private void OnEndPreview() { EndPreview(); _previewWeight?.SetValueWithoutNotify(0f); RefreshPreviewInfo(); }

        private void EndPreview() => Surface?.Invoke(Tool, "endPreview");

        /// <summary>プレビュー中なら終えて見た目を元へ戻す（Undo・Redo の前に Viewer が呼ぶ）。</summary>
        public void CancelIfActive()
        {
            if (Surface == null || Surface.GetInt(Tool, "previewSetIndex") < 0) return;
            OnEndPreview();
        }

        // ── 行の重み ────────────────────────────────────────────────────
        // 動かしている間は previewShapeWeight で見た目だけ変え、
        // 離したときに commitEntryWeights で確定する（Undo 1 回ぶん）。キー操作・UI 自動操作はすぐ確定する。
        private void OnEntryWeightChanged(float newWeight)
        {
            if (_selectedSetIndex < 0 || _selectedRow < 0 || _selectedRow >= _rows.Count) return;
            var r = _rows[_selectedRow];
            if (r.Kind == RowKind.FixedShape) return;
            r.Weight = newWeight;
            _rows[_selectedRow] = r;
            _entryListView?.RefreshItem(_selectedRow);

            Surface?.Invoke(Tool, "previewShapeWeight",
                ("setIndex", _selectedSetIndex), ("shapeName", r.ShapeName), ("weight", newWeight));

            if (!_weightDragging) Commit();
        }

        private void OnEntryWeightEnd()
        {
            if (!_weightDragging) return;
            _weightDragging = false;
            Commit();
        }

        private void Commit()
        {
            Surface?.Invoke(Tool, "commitEntryWeights");
            RefreshAll();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void StatusLog(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }
        private static VisualElement MakeSep() { var s = new VisualElement(); s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white); s.style.marginTop = 2; s.style.marginBottom = 6; return s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
