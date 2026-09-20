// PlayerMorphSubPanel.cs
// MorphPanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置
//
// 【操作経路統一計画.md E】
//   読み取りは窓口（IProjectView の MorphExpressions）、書き込みはコマンド
//   （SetMorphExpressionAttributes / DeleteMorphExpression / SetMorphEntryWeights / Import・ExportMorphCsv）、
//   試し表示とウェイト調整中のプレビューはツールの窓口 "morphExpression"（MorphExpressionHandler）で行う。
//   パネルはモデルのデータにも頂点にも触れない。

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
        /// <summary>プロジェクトの窓口。モーフエクスプレッションの写しを読む。</summary>
        public Func<IProjectView>   GetView;
        /// <summary>ツールの窓口（"morphExpression"）。試し表示とウェイト調整中のプレビュー。</summary>
        public IToolSurface         Surface;
        /// <summary>書き込みのコマンドの送り先。</summary>
        public Action<PanelCommand> SendCommand;
        private const string Tool = "morphExpression";

        private IModelView Model      => GetView?.Invoke()?.CurrentModel;
        private int        ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        // データ
        private readonly List<(int index, string name, string info)> _setListData = new List<(int, string, string)>();
        private readonly List<(int entryIdx, int meshIndex, string meshName, float weight)> _entryData = new List<(int, int, string, float)>();
        private int  _selectedSetIndex  = -1;
        private bool _entryEditing;

        // UI
        // UI 自動操作の ID は "morph.<下の Id>"（UiControlAttribute.cs）。
        // セット詳細はセットを選んだときだけ表示される（sets で行を選ぶ）。
        // 名前欄はフォーカスが外れたときに確定する作りなので、外からは確定まで行う Setter を通す。
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label         _warningLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        [UiControl("sets", Description = "モーフエクスプレッションのセット（一覧の行番号）")]
        private ListView      _setListView;
        [UiControl(Ignore = true)]
        private VisualElement _setDetail;
        [UiControl("set.name", Setter = nameof(SetNameByAutomation), Description = "選んだセットの名前（JP）")]
        private TextField     _setName;
        [UiControl("set.nameEn", Setter = nameof(SetNameEnByAutomation), Description = "選んだセットの名前（EN）")]
        private TextField     _setNameEn;
        [UiControl("set.panel", Description = "選んだセットのパネル（眉 / 目 / 口 / その他）")]
        private DropdownField _panelPopup;
        [UiControl("set.type", Safety = UiSafety.ReadOnly, Description = "選んだセットのタイプ")]
        private Label         _setTypeLabel;
        [UiControl("set.entries", Description = "選んだセットのエントリ（モーフメッシュ / ウェイト。一覧の行番号）")]
        private ListView      _entryListView;
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
        [UiControl("set.delete", Safety = UiSafety.UserOnly, Description = "選んだセットを削除する（確認ダイアログを開く）")]
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
            _btnCsvImport = btnImport;
            _btnCsvExport = btnExport;

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
                new List<string> { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }, 3);
            _panelPopup.style.color = new StyleColor(Color.white);
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
            _btnDeleteSet = btnDelete;

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
            _previewWeight.style.color = new StyleColor(Color.white);
            _previewWeight.style.marginBottom = 4;
            _previewWeight.RegisterValueChangedCallback(OnPreviewWeightChanged);
            parent.Add(_previewWeight);
            var btnEnd = new Button(OnEndPreview) { text = "プレビュー終了" };
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
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label(); lbl.style.width = 120; lbl.name = "lbl";
            lbl.style.color = new StyleColor(Color.white);
            var sl  = new Slider(0f, 1f) { name = "slider" }; sl.style.flexGrow = 1;
            sl.style.color = new StyleColor(Color.white);
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
            var sets = Model?.MorphExpressions; if (sets == null) return;
            _setListData.Clear();
            for (int i = 0; i < sets.Count; i++)
                _setListData.Add((i, sets[i].Name ?? "", $"({sets[i].TypeName}, {sets[i].MeshCount}件)"));
            _setListView?.RefreshItems();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setListData.Count - 1);
            if (_selectedSetIndex >= 0) { _setListView?.SetSelection(_selectedSetIndex); RefreshSetDetail(_selectedSetIndex); }
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
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
            if (_setTypeLabel != null) _setTypeLabel.text = set.TypeName;
            _panelPopup?.SetValueWithoutNotify(new[] { "眉 (0)", "目 (1)", "口 (2)", "その他 (3)" }[Math.Clamp(set.Panel, 0, 3)]);
            _entryData.Clear();
            for (int i = 0; i < set.Entries.Count; i++)
            {
                var entry = set.Entries[i];
                string mname = entry.MeshName != null ? $"[{entry.MeshIndex}] {entry.MeshName}" : $"[{entry.MeshIndex}]";
                _entryData.Add((i, entry.MeshIndex, mname, entry.Weight));
            }
            _entryListView?.RefreshItems();

            // プレビュー（組が 1 つも無いセットではプレビュー欄を出さない）
            if (Surface != null && Surface.GetInt(Tool, "previewSetIndex") != setIndex)
                Surface.Invoke(Tool, "startPreview", ("setIndex", setIndex));
            int pairCount = Surface?.GetInt(Tool, "previewPairCount") ?? 0;
            if (_previewSection != null) _previewSection.style.display = pairCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (pairCount > 0 && _previewInfo != null) _previewInfo.text = $"対象: {pairCount}ペア";
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            int idx = _setListView?.selectedIndex ?? -1;
            if (idx == _selectedSetIndex) return;
            EndPreview(); _previewWeight?.SetValueWithoutNotify(0f);
            _selectedSetIndex = idx;
            var sets = Model?.MorphExpressions;
            if (sets != null && idx >= 0 && idx < sets.Count) RefreshSetDetail(idx);
            else if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
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
            SendCommand?.Invoke(new SetMorphExpressionAttributesCommand(ModelIndex, _selectedSetIndex, newName, newNameEn, newPanel));
            RefreshAll();
        }

        private void OnDeleteSet()
        {
            var sets = Model?.MorphExpressions;
            if (sets == null || _selectedSetIndex < 0 || _selectedSetIndex >= sets.Count) return;
            string name = sets[_selectedSetIndex].Name;
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"モーフエクスプレッション '{name}' を削除しますか？", "削除", "キャンセル");
            if (!ok) return;
            EndPreview();
            SendCommand?.Invoke(new DeleteMorphExpressionCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1;
            StatusLog($"モーフエクスプレッション '{name}' を削除");
            RefreshAll();
        }

        private void OnCsvImport()
        {
            if (Model == null) return;
            string path = MorphCsvIO.AskImportPath();
            if (string.IsNullOrEmpty(path)) return;
            int before = Model?.MorphExpressions.Count ?? 0;
            SendCommand?.Invoke(new ImportMorphCsvCommand(ModelIndex, path));
            StatusLog($"CSV読込: {before} → {Model?.MorphExpressions.Count ?? 0} セット");
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
            => Surface?.Invoke(Tool, "applyPreview", ("weight", evt.newValue));

        private void OnEndPreview() { EndPreview(); _previewWeight?.SetValueWithoutNotify(0f); }

        private void EndPreview() => Surface?.Invoke(Tool, "endPreview");

        // ── エントリのウェイト ────────────────────────────────────────────
        // 操作中は previewEntryWeight で見た目だけ変え、離したときに commitEntryWeights で
        // SetMorphEntryWeightsCommand（Undo 1 回ぶん）として確定する。
        private void OnEntryWeightStart() => _entryEditing = true;

        private void OnEntryWeightChanged(int entryIdx, float newWeight)
        {
            if (_selectedSetIndex < 0) return;
            if (_entryData.Count > entryIdx)
                _entryData[entryIdx] = (_entryData[entryIdx].entryIdx, _entryData[entryIdx].meshIndex, _entryData[entryIdx].meshName, newWeight);
            Surface?.Invoke(Tool, "previewEntryWeight",
                ("setIndex", _selectedSetIndex), ("entryIndex", entryIdx), ("weight", newWeight));
            // ドラッグ以外（キー操作など）で変わったときはすぐ確定する。
            if (!_entryEditing) Surface?.Invoke(Tool, "commitEntryWeights");
        }

        private void OnEntryWeightEnd()
        {
            if (!_entryEditing) return;
            _entryEditing = false;
            Surface?.Invoke(Tool, "commitEntryWeights");
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void StatusLog(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }
        private static VisualElement MakeSep() { var s = new VisualElement(); s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white); s.style.marginTop = 2; s.style.marginBottom = 6; return s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
