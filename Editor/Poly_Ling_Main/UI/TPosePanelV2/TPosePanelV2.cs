// TPosePanelV2.cs
// Tポーズ変換パネル V2（コード構築 UIToolkit）
// PanelContext（通知）+ ToolContext（実処理）ハイブリッド

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Model;
using Poly_Ling.Records;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class TPosePanelV2 : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書（V1 と同一）
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]     = new() { ["en"] = "T-Pose Converter",                      ["ja"] = "Tポーズ変換" },
            ["NoModel"]         = new() { ["en"] = "No model loaded",                        ["ja"] = "モデルが読み込まれていません" },
            ["NoContext"]       = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["NoMapping"]       = new() { ["en"] = "Humanoid bone mapping is not set.\nPlease configure it in the Humanoid Mapping panel first.",
                                          ["ja"] = "Humanoidボーンマッピングが未設定です。\n先にHumanoid Mappingパネルで設定してください。" },
            ["ApplyTPose"]      = new() { ["en"] = "Apply T-Pose",                           ["ja"] = "Tポーズに変換" },
            ["RestoreOriginal"] = new() { ["en"] = "Restore Original Pose",                  ["ja"] = "元の姿勢に戻す" },
            ["BakeOriginal"]    = new() { ["en"] = "Bake to original pose (discard backup, cannot restore)",
                                          ["ja"] = "元の姿勢にベイク（バックアップを破棄、復元不可）" },
            ["TPoseApplied"]    = new() { ["en"] = "T-Pose has been applied. Backup saved.",  ["ja"] = "Tポーズを適用しました。バックアップを保存しました。" },
            ["PoseRestored"]    = new() { ["en"] = "Original pose restored.",                 ["ja"] = "元の姿勢に戻しました。" },
            ["BackupDiscarded"] = new() { ["en"] = "Backup discarded. Current pose is now the base pose.", ["ja"] = "バックアップを破棄しました。現在の姿勢がベース姿勢になります。" },
            ["NoBackup"]        = new() { ["en"] = "No backup available",                     ["ja"] = "バックアップがありません" },
            ["HasBackup"]       = new() { ["en"] = "✓ Original pose backup exists (can restore)", ["ja"] = "✓ 元の姿勢のバックアップあり（復元可能）" },
            ["MappedBones"]     = new() { ["en"] = "Mapped: {0} bones",                      ["ja"] = "マッピング済: {0}ボーン" },
            ["ConfirmBake"]     = new() { ["en"] = "Discard the original pose backup?\nThis cannot be undone.",
                                          ["ja"] = "元の姿勢のバックアップを破棄しますか？\nこの操作は元に戻せません。" },
        };

        private static string T(string key)                       => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model => _toolCtx?.Model;

        // ================================================================
        // UI 要素
        // ================================================================

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
        private VisualElement _statusSection;
        private Label         _statusLabel;

        // ================================================================
        // Open
        // ================================================================

        public static TPosePanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<TPosePanelV2>();
            w.titleContent = new GUIContent(T("WindowTitle"));
            w.minSize = new Vector2(350, 250);
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

            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;

            ClearStatus();
            Refresh();
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
        }

        private void OnDisable()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            Refresh();
        }

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.ModelSwitch || kind == ChangeKind.Attributes)
            {
                ClearStatus();
                Refresh();
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 8;
            root.style.paddingRight  = 8;
            root.style.paddingTop    = 8;
            root.style.paddingBottom = 8;

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // メインコンテンツ
            _mainContent = new VisualElement();
            _mainContent.style.display = DisplayStyle.None;
            root.Add(_mainContent);

            // マッピング情報
            _mappingInfoLabel = new Label();
            _mappingInfoLabel.style.color       = new StyleColor(Color.gray);
            _mappingInfoLabel.style.marginBottom = 8;
            _mainContent.Add(_mappingInfoLabel);

            // Apply T-Pose ボタン
            _btnApplyTPose = new Button(OnApplyTPose) { text = T("ApplyTPose") };
            _btnApplyTPose.style.height       = 28;
            _btnApplyTPose.style.marginBottom = 8;
            _mainContent.Add(_btnApplyTPose);

            _mainContent.Add(MakeSep());

            // バックアップあり セクション
            _backupSection = new VisualElement();
            _backupSection.style.display = DisplayStyle.None;
            _mainContent.Add(_backupSection);

            _backupStatusLabel = new Label();
            _backupStatusLabel.style.color       = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            _backupStatusLabel.style.marginBottom = 6;
            _backupSection.Add(_backupStatusLabel);

            _btnRestore = new Button(OnRestoreOriginal) { text = T("RestoreOriginal") };
            _btnRestore.style.marginBottom = 4;
            _backupSection.Add(_btnRestore);

            _toggleBake = new Toggle(T("BakeOriginal"));
            _toggleBake.style.marginBottom = 2;
            _toggleBake.RegisterValueChangedCallback(e =>
                _btnBake.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None);
            _backupSection.Add(_toggleBake);

            _btnBake = new Button(OnBake) { text = "Bake" };
            _btnBake.style.display     = DisplayStyle.None;
            _btnBake.style.marginBottom = 4;
            _backupSection.Add(_btnBake);

            // バックアップなし ラベル
            _noBackupLabel = new Label();
            _noBackupLabel.style.color       = new StyleColor(Color.gray);
            _noBackupLabel.style.marginBottom = 6;
            _mainContent.Add(_noBackupLabel);

            _mainContent.Add(MakeSep());

            // ステータス
            _statusSection = new VisualElement();
            _statusSection.style.display    = DisplayStyle.None;
            _statusSection.style.marginTop  = 6;
            _mainContent.Add(_statusSection);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusSection.Add(_statusLabel);
        }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep.style.marginTop       = 4;
            sep.style.marginBottom    = 6;
            return sep;
        }

        // ================================================================
        // リフレッシュ（V1 と同一ロジック）
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolCtx == null)
            {
                ShowWarning(T("NoContext")); return;
            }
            if (Model == null)
            {
                ShowWarning(T("NoModel")); return;
            }

            var mapping = Model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                ShowWarning(T("NoMapping")); return;
            }

            _warningLabel.style.display  = DisplayStyle.None;
            _mainContent.style.display   = DisplayStyle.Flex;

            _btnApplyTPose.text = T("ApplyTPose");
            _btnRestore.text    = T("RestoreOriginal");
            _toggleBake.label   = T("BakeOriginal");

            _mappingInfoLabel.text = T("MappedBones", mapping.Count);

            RefreshBackupSection();
        }

        private void ShowWarning(string message)
        {
            if (_warningLabel == null) return;
            _warningLabel.text           = message;
            _warningLabel.style.display  = DisplayStyle.Flex;
            if (_mainContent != null) _mainContent.style.display = DisplayStyle.None;
        }

        private void RefreshBackupSection()
        {
            if (Model?.TPoseBackup != null)
            {
                _backupSection.style.display  = DisplayStyle.Flex;
                _backupStatusLabel.text       = T("HasBackup");
                _noBackupLabel.style.display  = DisplayStyle.None;
                _toggleBake.value             = false;
                _btnBake.style.display        = DisplayStyle.None;
            }
            else
            {
                _backupSection.style.display  = DisplayStyle.None;
                _noBackupLabel.text           = T("NoBackup");
                _noBackupLabel.style.display  = DisplayStyle.Flex;
            }
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void ShowStatus(string message)
        {
            if (_statusSection == null) return;
            _statusSection.style.display = DisplayStyle.Flex;
            _statusLabel.text            = message;
        }

        private void ClearStatus()
        {
            if (_statusSection != null)
                _statusSection.style.display = DisplayStyle.None;
        }

        // ================================================================
        // 操作（V1 と同一ロジック）
        // ================================================================

        private void OnApplyTPose()
        {
            var mapping = Model?.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;

            var beforeState  = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, beforeState);
            var oldTPoseBackup = Model.TPoseBackup;

            var backup = new TPoseBackup();
            TPoseConverter.ConvertToTPose(Model.MeshContextList, mapping, backup);
            Model.TPoseBackup = backup;

            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, afterState);

            var undo = _toolCtx?.UndoController;
            if (undo != null)
            {
                var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                undo.MeshListStack.Record(record, "Apply T-Pose");
            }

            ShowStatus(T("TPoseApplied"));
            Model.IsDirty = true;
            _toolCtx?.NotifyTopologyChanged?.Invoke();
            _toolCtx?.Repaint?.Invoke();
            Refresh();
        }

        private void OnRestoreOriginal()
        {
            if (Model?.TPoseBackup == null) return;

            var beforeState    = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, beforeState);
            var oldTPoseBackup = Model.TPoseBackup;

            TPoseConverter.RestoreFromBackup(Model.MeshContextList, Model.TPoseBackup);

            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, afterState);

            Model.TPoseBackup = null;

            var undo = _toolCtx?.UndoController;
            if (undo != null)
            {
                var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, null, "Restore Original Pose");
                undo.MeshListStack.Record(record, "Restore Original Pose");
            }

            ShowStatus(T("PoseRestored"));
            Model.IsDirty = true;
            _toolCtx?.NotifyTopologyChanged?.Invoke();
            _toolCtx?.Repaint?.Invoke();
            Refresh();
        }

        private void OnBake()
        {
            if (Model?.TPoseBackup == null) return;

            if (EditorUtility.DisplayDialog(T("WindowTitle"), T("ConfirmBake"), "OK", "Cancel"))
            {
                Model.TPoseBackup = null;
                ShowStatus(T("BackupDiscarded"));
                Refresh();
            }
        }
    }
}
