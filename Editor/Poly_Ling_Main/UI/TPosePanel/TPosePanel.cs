// TPosePanel.cs
// Tポーズ変換パネル (UIToolkit)
// アバターマッピング後にTポーズに変換し、元に戻すことも可能

using System;
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
    /// <summary>
    /// Tポーズ変換パネル（UIToolkit版）
    /// </summary>
    public class TPosePanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TPosePanel/TPosePanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TPosePanel/TPosePanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TPosePanel/TPosePanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TPosePanel/TPosePanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "T-Pose Converter", ["ja"] = "Tポーズ変換" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルが読み込まれていません" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["NoMapping"] = new()
            {
                ["en"] = "Humanoid bone mapping is not set.\nPlease configure it in the Humanoid Mapping panel first.",
                ["ja"] = "Humanoidボーンマッピングが未設定です。\n先にHumanoid Mappingパネルで設定してください。"
            },
            ["ApplyTPose"] = new() { ["en"] = "Apply T-Pose", ["ja"] = "Tポーズに変換" },
            ["RestoreOriginal"] = new() { ["en"] = "Restore Original Pose", ["ja"] = "元の姿勢に戻す" },
            ["BakeOriginal"] = new()
            {
                ["en"] = "Bake to original pose (discard backup, cannot restore)",
                ["ja"] = "元の姿勢にベイク（バックアップを破棄、復元不可）"
            },
            ["TPoseApplied"] = new() { ["en"] = "T-Pose has been applied. Backup saved.", ["ja"] = "Tポーズを適用しました。バックアップを保存しました。" },
            ["PoseRestored"] = new() { ["en"] = "Original pose restored.", ["ja"] = "元の姿勢に戻しました。" },
            ["BackupDiscarded"] = new() { ["en"] = "Backup discarded. Current pose is now the base pose.", ["ja"] = "バックアップを破棄しました。現在の姿勢がベース姿勢になります。" },
            ["NoBackup"] = new() { ["en"] = "No backup available", ["ja"] = "バックアップがありません" },
            ["HasBackup"] = new() { ["en"] = "✓ Original pose backup exists (can restore)", ["ja"] = "✓ 元の姿勢のバックアップあり（復元可能）" },
            ["MappedBones"] = new() { ["en"] = "Mapped: {0} bones", ["ja"] = "マッピング済: {0}ボーン" },
            ["ConfirmBake"] = new()
            {
                ["en"] = "Discard the original pose backup?\nThis cannot be undone.",
                ["ja"] = "元の姿勢のバックアップを破棄しますか？\nこの操作は元に戻せません。"
            },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private VisualElement _mainContent;
        private Label _mappingInfoLabel;
        private Button _btnApplyTPose;
        private VisualElement _backupSection;
        private Label _backupStatusLabel;
        private Button _btnRestore;
        private VisualElement _bakeSection;
        private Toggle _toggleBake;
        private Button _btnBake;
        private Label _noBackupLabel;
        private VisualElement _statusSection;
        private Label _statusLabel;

        // ================================================================
        // Open
        // ================================================================

        public static TPosePanel Open(ToolContext ctx)
        {
            var window = GetWindow<TPosePanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(350, 250);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
        private void Cleanup() { }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            _toolContext = ctx;
            ClearStatus();
            Refresh();
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
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<VisualElement>("main-content");
            _mappingInfoLabel = root.Q<Label>("mapping-info-label");
            _btnApplyTPose = root.Q<Button>("btn-apply-tpose");
            _backupSection = root.Q<VisualElement>("backup-section");
            _backupStatusLabel = root.Q<Label>("backup-status-label");
            _btnRestore = root.Q<Button>("btn-restore");
            _bakeSection = root.Q<VisualElement>("bake-section");
            _toggleBake = root.Q<Toggle>("toggle-bake");
            _btnBake = root.Q<Button>("btn-bake");
            _noBackupLabel = root.Q<Label>("no-backup-label");
            _statusSection = root.Q<VisualElement>("status-section");
            _statusLabel = root.Q<Label>("status-label");

            // ボタンイベント
            _btnApplyTPose.clicked += OnApplyTPose;
            _btnRestore.clicked += OnRestoreOriginal;
            _btnBake.clicked += OnBake;

            // Bakeトグル変更
            _toggleBake.RegisterValueChangedCallback(e =>
            {
                _btnBake.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return; // CreateGUI未実行

            // コンテキストチェック
            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            if (Model == null)
            {
                ShowWarning(T("NoModel"));
                return;
            }

            // HumanoidBoneMappingチェック
            var mapping = Model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                ShowWarning(T("NoMapping"));
                return;
            }

            // メインコンテンツ表示
            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            // ローカライズ更新
            _btnApplyTPose.text = T("ApplyTPose");
            _btnRestore.text = T("RestoreOriginal");
            _toggleBake.label = T("BakeOriginal");

            // マッピング情報
            _mappingInfoLabel.text = T("MappedBones", mapping.Count);

            // バックアップ状態
            RefreshBackupSection();
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        private void RefreshBackupSection()
        {
            if (Model?.TPoseBackup != null)
            {
                _backupSection.style.display = DisplayStyle.Flex;
                _backupStatusLabel.text = T("HasBackup");
                _noBackupLabel.style.display = DisplayStyle.None;

                // トグル状態をリセット
                _toggleBake.value = false;
                _btnBake.style.display = DisplayStyle.None;
            }
            else
            {
                _backupSection.style.display = DisplayStyle.None;
                _noBackupLabel.text = T("NoBackup");
                _noBackupLabel.style.display = DisplayStyle.Flex;
            }
        }

        // ================================================================
        // ステータス表示
        // ================================================================

        private void ShowStatus(string message, bool isWarning = false)
        {
            if (_statusSection == null) return;

            _statusSection.style.display = DisplayStyle.Flex;
            _statusLabel.text = message;

            _statusLabel.RemoveFromClassList("tp-status--info");
            _statusLabel.RemoveFromClassList("tp-status--warning");
            _statusLabel.AddToClassList(isWarning ? "tp-status--warning" : "tp-status--info");
        }

        private void ClearStatus()
        {
            if (_statusSection != null)
                _statusSection.style.display = DisplayStyle.None;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnApplyTPose()
        {
            var mapping = Model?.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
                return;

            // before状態をキャプチャ
            var beforeState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, beforeState);
            var oldTPoseBackup = Model.TPoseBackup;

            // Tポーズ適用
            var backup = new TPoseBackup();
            TPoseConverter.ConvertToTPose(Model.MeshContextList, mapping, backup);
            Model.TPoseBackup = backup;

            // after状態をキャプチャ
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, afterState);

            // Undo記録
            var undo = _toolContext?.UndoController;
            if (undo != null)
            {
                var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                undo.MeshListStack.Record(record, "Apply T-Pose");
            }

            ShowStatus(T("TPoseApplied"));

            Model.IsDirty = true;
            _toolContext?.NotifyTopologyChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            Refresh();
        }

        private void OnRestoreOriginal()
        {
            if (Model?.TPoseBackup == null)
                return;

            // before状態をキャプチャ
            var beforeState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, beforeState);
            var oldTPoseBackup = Model.TPoseBackup;

            // 復元
            TPoseConverter.RestoreFromBackup(Model.MeshContextList, Model.TPoseBackup);

            // after状態をキャプチャ
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(Model.MeshContextList, afterState);

            Model.TPoseBackup = null;

            // Undo記録
            var undo = _toolContext?.UndoController;
            if (undo != null)
            {
                var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, null, "Restore Original Pose");
                undo.MeshListStack.Record(record, "Restore Original Pose");
            }

            ShowStatus(T("PoseRestored"));

            Model.IsDirty = true;
            _toolContext?.NotifyTopologyChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            Refresh();
        }

        private void OnBake()
        {
            if (Model?.TPoseBackup == null)
                return;

            if (EditorUtility.DisplayDialog(
                T("WindowTitle"),
                T("ConfirmBake"),
                "OK", "Cancel"))
            {
                Model.TPoseBackup = null;
                ShowStatus(T("BackupDiscarded"));

                Refresh();
            }
        }
    }
}
