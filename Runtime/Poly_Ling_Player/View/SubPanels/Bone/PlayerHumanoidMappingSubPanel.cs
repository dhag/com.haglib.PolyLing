// PlayerHumanoidMappingSubPanel.cs
// HumanoidMappingPanelV2 の Player 版サブパネル。
// DnD 除去。EditorUtility.OpenFilePanel → PLEditorBridge.I.OpenFilePanel に置換。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerHumanoidMappingSubPanel
    {
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Action<PanelCommand> SendCommand;
        public Func<int>            GetModelIndex;

        private Label       _warningLabel;
        private TextField   _csvPathField;
        private Label       _csvHintLabel;
        private Button      _btnAutoMap, _btnLoadCsv, _btnApply, _btnClear;
        private Label       _mappedCountLabel;
        private VisualElement _previewContent;
        private Label       _previewEmptyLabel;
        private VisualElement _mappingDetailContainer;
        private Label       _statusLabel;

        private string             _csvFilePath   = "";
        private HumanoidBoneMapping _previewMapping = null;

        private ModelContext Model => GetModel?.Invoke();

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Humanoidボーンマッピング"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_warningLabel);

            // CSV ファイル行
            root.Add(SecLabel("CSV ファイル"));
            var fileRow = new VisualElement(); fileRow.style.flexDirection = FlexDirection.Row; fileRow.style.marginBottom = 4;
            _csvPathField = new TextField(); _csvPathField.style.flexGrow = 1; _csvPathField.isReadOnly = true;
            var btnBrowse = new Button(OnBrowseCSV) { text = "..." }; btnBrowse.style.width = 28;
            fileRow.Add(_csvPathField); fileRow.Add(btnBrowse);
            root.Add(fileRow);

            _csvHintLabel = new Label("CSVを[...]で選択してください。");
            _csvHintLabel.style.fontSize    = 10;
            _csvHintLabel.style.color       = new StyleColor(Color.white);
            _csvHintLabel.style.marginBottom = 4;
            root.Add(_csvHintLabel);

            // AutoMap / Load CSV ボタン
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 6;
            _btnAutoMap = new Button(OnAutoMap)  { text = "Auto Map (PMX)" }; _btnAutoMap.style.flexGrow = 1; _btnAutoMap.style.marginRight = 4;
            _btnLoadCsv = new Button(OnLoadCSV)  { text = "CSVから読み込み" }; _btnLoadCsv.style.flexGrow = 1;
            btnRow.Add(_btnAutoMap); btnRow.Add(_btnLoadCsv);
            root.Add(btnRow);

            root.Add(MakeSep());

            // プレビュー
            root.Add(SecLabel("プレビュー"));
            _previewEmptyLabel = new Label("マッピング未読込み");
            _previewEmptyLabel.style.color = new StyleColor(Color.white);
            root.Add(_previewEmptyLabel);

            _previewContent = new VisualElement();
            _previewContent.style.display = DisplayStyle.None;
            _mappedCountLabel = new Label(); _mappedCountLabel.style.marginBottom = 4;
            _previewContent.Add(_mappedCountLabel);
            _mappingDetailContainer = new VisualElement();
            _previewContent.Add(_mappingDetailContainer);
            root.Add(_previewContent);

            // Apply / Clear ボタン
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginTop = 6; applyRow.style.marginBottom = 4;
            _btnApply = new Button(OnApply) { text = "Apply" }; _btnApply.style.flexGrow = 1; _btnApply.style.marginRight = 4;
            _btnClear = new Button(OnClear) { text = "Clear" }; _btnClear.style.flexGrow = 1;
            applyRow.Add(_btnApply); applyRow.Add(_btnClear);
            root.Add(applyRow);

            _statusLabel = new Label(); _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new StyleColor(Color.white);
            root.Add(_statusLabel);

            UpdatePreviewUI();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            // hint text
            if (_csvHintLabel != null)
            {
                bool hasFile = !string.IsNullOrEmpty(_csvFilePath);
                _csvHintLabel.text          = hasFile ? "" : "CSVを[...]で選択してください。";
                _csvHintLabel.style.display = hasFile ? DisplayStyle.None : DisplayStyle.Flex;
            }
            var model = Model;
            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;
            UpdatePreviewUI();
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnBrowseCSV()
        {
            string dir  = string.IsNullOrEmpty(_csvFilePath) ? UnityEngine.Application.dataPath : Path.GetDirectoryName(_csvFilePath);
            string path = PLEditorBridge.I.OpenFilePanel("Select Bone Mapping CSV", dir, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                _csvFilePath = path;
                _csvPathField?.SetValueWithoutNotify(_csvFilePath);
                if (_csvHintLabel != null)
                {
                    _csvHintLabel.text          = "";
                    _csvHintLabel.style.display = DisplayStyle.None;
                }
                LoadCSVMapping();
            }
        }

        private void OnAutoMap()
        {
            var boneNames = GetBoneNames();
            _previewMapping = new HumanoidBoneMapping();
            int count = _previewMapping.AutoMapFromEmbeddedCSV(boneNames);
            UnityEngine.Debug.Log($"[PlayerHumanoidMappingSubPanel] Auto-mapped {count} bones");
            UpdatePreviewUI();
        }

        private void OnLoadCSV() => LoadCSVMapping();

        private void OnApply()
        {
            if (_previewMapping == null || Model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new ApplyHumanoidMappingCommand(modelIdx, _previewMapping.Clone()));
                SetStatus($"適用しました ({_previewMapping.Count} ボーン)");
                Refresh();
                return;
            }
            // フォールバック
            var tc     = GetToolContext?.Invoke();
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.CopyFrom(_previewMapping);
            var after  = Model.HumanoidMapping.Clone();
            var undo   = tc?.UndoController;
            if (undo != null)
                undo.MeshListStack.Record(new HumanoidMappingChangedRecord(before, after, "Apply Humanoid Mapping"), "Apply Humanoid Mapping");
            Model.IsDirty = true;
            SetStatus($"適用しました ({_previewMapping.Count} ボーン)");
            Refresh();
        }

        private void OnClear()
        {
            if (Model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new ClearHumanoidMappingCommand(modelIdx));
                _previewMapping = null;
                SetStatus("マッピングをクリアしました");
                UpdatePreviewUI();
                return;
            }
            // フォールバック
            var tc     = GetToolContext?.Invoke();
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.ClearAll();
            var after  = Model.HumanoidMapping.Clone();
            var undo   = tc?.UndoController;
            if (undo != null)
                undo.MeshListStack.Record(new HumanoidMappingChangedRecord(before, after, "Clear Humanoid Mapping"), "Clear Humanoid Mapping");
            Model.IsDirty   = true;
            _previewMapping = null;
            SetStatus("マッピングをクリアしました");
            UpdatePreviewUI();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void LoadCSVMapping()
        {
            if (string.IsNullOrEmpty(_csvFilePath) || !File.Exists(_csvFilePath))
            {
                SetStatus("CSVファイルが見つかりません"); return;
            }
            try
            {
                var csvLines = new List<string>(File.ReadAllLines(_csvFilePath, Encoding.UTF8));
                _previewMapping = new HumanoidBoneMapping();
                int count = _previewMapping.LoadFromCSV(csvLines, GetBoneNames());
                SetStatus($"CSV 読込み: {count} ボーン");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PlayerHumanoidMappingSubPanel] CSV load failed: {ex.Message}");
                _previewMapping = null;
                SetStatus("CSV 読込みに失敗しました");
            }
            UpdatePreviewUI();
        }

        private List<string> GetBoneNames()
        {
            var names = new List<string>();
            var model = Model;
            if (model == null) return names;
            foreach (var entry in model.Bones)
            {
                var mc = model.GetMeshContext(entry.MasterIndex);
                if (mc != null && !string.IsNullOrEmpty(mc.Name)) names.Add(mc.Name);
            }
            return names;
        }

        private void UpdatePreviewUI()
        {
            if (_previewContent == null) return;
            if (_previewMapping == null || _previewMapping.Count == 0)
            {
                _previewContent.style.display  = DisplayStyle.None;
                if (_previewEmptyLabel != null) _previewEmptyLabel.style.display = DisplayStyle.Flex;
                return;
            }
            if (_previewEmptyLabel != null) _previewEmptyLabel.style.display = DisplayStyle.None;
            _previewContent.style.display = DisplayStyle.Flex;
            if (_mappedCountLabel != null) _mappedCountLabel.text = $"マッピング済: {_previewMapping.Count} ボーン";

            // 詳細（最大15件）
            _mappingDetailContainer?.Clear();
            int shown = 0;
            foreach (var kvp in _previewMapping.BoneIndexMap)
            {
                if (shown++ >= 15) { var more = new Label("  ...他"); more.style.fontSize = 9; more.style.color = new StyleColor(Color.white); _mappingDetailContainer?.Add(more); break; }
                var lbl = new Label($"  {kvp.Key}: [{kvp.Value}]");
                _mappingDetailContainer?.Add(lbl);
            }
            PlayerLayoutRoot.ApplyDarkTheme(_mappingDetailContainer);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static VisualElement MakeSep() { var s = new VisualElement(); s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white); s.style.marginTop = 4; s.style.marginBottom = 6; return s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
