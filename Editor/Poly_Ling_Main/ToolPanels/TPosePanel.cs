// TPosePanel.cs
// Tポーズ変換パネル
// アバターマッピング後にTポーズに変換し、元に戻すことも可能

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Model;
using Poly_Ling.Tools;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// Tポーズ変換パネル
    /// </summary>
    public class TPosePanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "TPose";
        public override string Title => "T-Pose";
        public override IToolSettings Settings => null;

        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "T-Pose Converter", ["ja"] = "Tポーズ変換" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルが読み込まれていません" },
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
        // フィールド
        // ================================================================

        private bool _bakeOriginal;
        private string _statusMessage;
        private MessageType _statusType;

        // ================================================================
        // ウィンドウ表示
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var window = GetWindow<TPosePanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(350, 250);
            window.SetContext(ctx);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            // コンテキストチェック
            if (!DrawNoContextWarning())
                return;

            if (Model == null)
            {
                EditorGUILayout.HelpBox(T("NoModel"), MessageType.Warning);
                return;
            }

            // HumanoidBoneMappingチェック
            var mapping = Model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                EditorGUILayout.HelpBox(T("NoMapping"), MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(5);

            // マッピング情報
            EditorGUILayout.LabelField(T("MappedBones", mapping.Count), EditorStyles.boldLabel);

            EditorGUILayout.Space(10);

            // ================================================================
            // Tポーズ適用ボタン
            // ================================================================

            if (GUILayout.Button(T("ApplyTPose"), GUILayout.Height(30)))
            {
                ApplyTPose();
            }

            EditorGUILayout.Space(10);

            // ================================================================
            // バックアップ状態表示
            // ================================================================

            if (Model.TPoseBackup != null)
            {
                GUI.color = new Color(0.5f, 1f, 0.5f);
                EditorGUILayout.LabelField(T("HasBackup"));
                GUI.color = Color.white;

                EditorGUILayout.Space(5);

                // 元に戻すボタン
                if (GUILayout.Button(T("RestoreOriginal"), GUILayout.Height(28)))
                {
                    RestoreOriginal();
                }

                EditorGUILayout.Space(10);

                // ベイクチェックボックス + 確認
                _bakeOriginal = EditorGUILayout.ToggleLeft(T("BakeOriginal"), _bakeOriginal);

                if (_bakeOriginal)
                {
                    EditorGUILayout.Space(3);
                    GUI.color = new Color(1f, 0.7f, 0.5f);
                    if (GUILayout.Button("Bake", GUILayout.Height(24)))
                    {
                        if (EditorUtility.DisplayDialog(
                            T("WindowTitle"),
                            T("ConfirmBake"),
                            "OK", "Cancel"))
                        {
                            Model.TPoseBackup = null;
                            _bakeOriginal = false;
                            _statusMessage = T("BackupDiscarded");
                            _statusType = MessageType.Info;
                        }
                    }
                    GUI.color = Color.white;
                }
            }
            else
            {
                EditorGUILayout.LabelField(T("NoBackup"));
            }

            // ステータスメッセージ
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        // ================================================================
        // 操作
        // ================================================================

        private void ApplyTPose()
        {
            var mapping = Model?.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
                return;

            var backup = new TPoseBackup();
            TPoseConverter.ConvertToTPose(Model.MeshContextList, mapping, backup);
            Model.TPoseBackup = backup;

            _statusMessage = T("TPoseApplied");
            _statusType = MessageType.Info;

            Model.IsDirty = true;
            _context?.NotifyTopologyChanged?.Invoke();
            _context?.Repaint?.Invoke();
            Repaint();
        }

        private void RestoreOriginal()
        {
            if (Model?.TPoseBackup == null)
                return;

            TPoseConverter.RestoreFromBackup(Model.MeshContextList, Model.TPoseBackup);
            Model.TPoseBackup = null;
            _bakeOriginal = false;

            _statusMessage = T("PoseRestored");
            _statusType = MessageType.Info;

            Model.IsDirty = true;
            _context?.NotifyTopologyChanged?.Invoke();
            _context?.Repaint?.Invoke();
            Repaint();
        }

        // ================================================================
        // コンテキスト更新時
        // ================================================================

        protected override void OnContextSet()
        {
            _statusMessage = null;
            _bakeOriginal = false;
        }
    }
}
