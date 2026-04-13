// PlayerTPoseSubPanel.cs
// TPosePanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerTPoseSubPanel
    {
        public Func<ModelContext>    GetModel;
        public Func<ToolContext>     GetToolContext;
        /// <summary>PanelCommand を送信するコールバック。</summary>
        public Action<PanelCommand> SendCommand;
        /// <summary>モデルインデックスを返すデリゲート。</summary>
        public Func<int>             GetModelIndex;

        private Label         _warningLabel;
        private VisualElement _mainContent;
        private Label         _mappingInfoLabel;
        private Button        _btnApplyTPose;
        private VisualElement _backupSection;
        private Label         _backupStatusLabel;
        private Button        _btnRestore;
        private Toggle        _toggleBake;
        private Button        _btnBake;
        private Label         _noBackupLabel;
        private Label         _statusLabel;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Tポーズ変換"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _mainContent.style.display = DisplayStyle.None;
            root.Add(_mainContent);

            _mappingInfoLabel = new Label();
            _mappingInfoLabel.style.color       = new StyleColor(Color.white);
            _mappingInfoLabel.style.marginBottom = 8;
            _mainContent.Add(_mappingInfoLabel);

            _btnApplyTPose = new Button(OnApplyTPose) { text = "Tポーズに変換" };
            _btnApplyTPose.style.height       = 28;
            _btnApplyTPose.style.marginBottom = 8;
            _mainContent.Add(_btnApplyTPose);

            _mainContent.Add(MakeSep());

            // バックアップあり
            _backupSection = new VisualElement();
            _backupSection.style.display = DisplayStyle.None;
            _backupStatusLabel = new Label();
            _backupStatusLabel.style.color       = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            _backupStatusLabel.style.marginBottom = 6;
            _backupSection.Add(_backupStatusLabel);
            _btnRestore = new Button(OnRestoreOriginal) { text = "元の姿勢に戻す" };
            _btnRestore.style.marginBottom = 4;
            _backupSection.Add(_btnRestore);
            _toggleBake = new Toggle("元の姿勢にベイク（バックアップを破棄）");
            _toggleBake.style.color = new StyleColor(Color.white);
            _toggleBake.style.marginBottom = 2;
            _toggleBake.RegisterValueChangedCallback(e =>
                { if (_btnBake != null) _btnBake.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None; });
            _backupSection.Add(_toggleBake);
            _btnBake = new Button(OnBake) { text = "Bake" };
            _btnBake.style.display     = DisplayStyle.None;
            _btnBake.style.marginBottom = 4;
            _backupSection.Add(_btnBake);
            _mainContent.Add(_backupSection);

            // バックアップなし
            _noBackupLabel = new Label();
            _noBackupLabel.style.color       = new StyleColor(Color.white);
            _noBackupLabel.style.marginBottom = 6;
            _mainContent.Add(_noBackupLabel);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.color      = new StyleColor(Color.white);
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _mainContent.Add(_statusLabel);
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var model = GetModel?.Invoke();
            if (model == null) { ShowWarning("モデルがありません"); return; }
            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            { ShowWarning("Humanoidボーンマッピングが未設定です。\nHumanoid Mappingパネルで設定してください。"); return; }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;
            _mappingInfoLabel.text      = $"マッピング済: {mapping.Count} ボーン";
            RefreshBackupSection(model);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnApplyTPose()
        {
            var model = GetModel?.Invoke(); if (model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new ApplyTPoseCommand(modelIdx));
                SetStatus("Tポーズを適用しました。");
                Refresh();
                return;
            }
            // フォールバック
            var tc      = GetToolContext?.Invoke();
            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;
            var beforeState    = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
            var oldTPoseBackup = model.TPoseBackup;
            var backup = new TPoseBackup();
            TPoseConverter.ConvertToTPose(model.MeshContextList, mapping, backup);
            model.TPoseBackup = backup;
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, afterState);
            var undo = tc?.UndoController;
            if (undo != null)
            {
                undo.MeshListStack.Record(
                    new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose"),
                    "Apply T-Pose");
            }
            model.IsDirty = true;
            tc?.NotifyTopologyChanged?.Invoke();
            tc?.Repaint?.Invoke();
            SetStatus("Tポーズを適用しました。バックアップを保存しました。");
            Refresh();
        }

        private void OnRestoreOriginal()
        {
            var model = GetModel?.Invoke(); if (model?.TPoseBackup == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new RestoreTPoseCommand(modelIdx));
                SetStatus("元の姿勢に戻しました。");
                Refresh();
                return;
            }
            // フォールバック
            var tc = GetToolContext?.Invoke();
            var beforeState    = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
            var oldTPoseBackup = model.TPoseBackup;
            TPoseConverter.RestoreFromBackup(model.MeshContextList, model.TPoseBackup);
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, afterState);
            model.TPoseBackup = null;
            var undo = tc?.UndoController;
            if (undo != null)
            {
                undo.MeshListStack.Record(
                    new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, null, "Restore Original Pose"),
                    "Restore Original Pose");
            }
            model.IsDirty = true;
            tc?.NotifyTopologyChanged?.Invoke();
            tc?.Repaint?.Invoke();
            SetStatus("元の姿勢に戻しました。");
            Refresh();
        }

        private void OnBake()
        {
            var model = GetModel?.Invoke(); if (model?.TPoseBackup == null) return;
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("Tポーズ変換", "元の姿勢のバックアップを破棄しますか？\nこの操作は元に戻せません。", "OK", "Cancel");
            if (!ok) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
                SendCommand.Invoke(new BakeTPoseCommand(modelIdx));
            else
                model.TPoseBackup = null;
            SetStatus("バックアップを破棄しました。現在の姿勢がベース姿勢になります。");
            Refresh();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            if (_mainContent != null) _mainContent.style.display = DisplayStyle.None;
        }

        private void RefreshBackupSection(ModelContext model)
        {
            if (model.TPoseBackup != null)
            {
                _backupSection.style.display = DisplayStyle.Flex;
                _backupStatusLabel.text      = "✓ 元の姿勢のバックアップあり（復元可能）";
                _noBackupLabel.style.display = DisplayStyle.None;
                if (_toggleBake != null) { _toggleBake.value = false; }
                if (_btnBake    != null) _btnBake.style.display = DisplayStyle.None;
            }
            else
            {
                _backupSection.style.display = DisplayStyle.None;
                _noBackupLabel.text          = "バックアップがありません";
                _noBackupLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(Color.white);
            sep.style.marginTop       = 4; sep.style.marginBottom = 6;
            return sep;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 3; return l;
        }
    }
}
