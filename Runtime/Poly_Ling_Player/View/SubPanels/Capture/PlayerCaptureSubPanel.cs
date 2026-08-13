// PlayerCaptureSubPanel.cs
// 画面キャプチャパネル（UIToolkit・右ペイン）。
// ファイル名・保存フォルダ・対象を設定し、ボタンまたはショートカットで
// PNG を保存する。設定は RecentPaths に write-through するため、
// パネルを開いていなくてもショートカットから同じ設定で撮れる。
// Runtime/Poly_Ling_Player/View/SubPanels/Capture/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerCaptureSubPanel
    {
        // ================================================================
        // 設定の保存キー（RecentPaths）
        // ================================================================

        public const string KeyFileName = "Capture.FileName";
        public const string KeyFolder   = "Capture.Folder";
        public const string KeyTarget   = "Capture.Target";

        /// <summary>保存済みのファイル名（未設定なら既定）。</summary>
        public static string GetFileName()
            => RecentPaths.Get(KeyFileName, PlayerScreenCapture.DefaultFileName);

        /// <summary>保存済みの保存フォルダ（未設定なら既定）。</summary>
        public static string GetFolder()
            => RecentPaths.Get(KeyFolder, PlayerScreenCapture.DefaultFolder);

        /// <summary>保存済みの対象（未設定・不正なら MainView）。</summary>
        public static CaptureTarget GetTarget()
        {
            string s = RecentPaths.Get(KeyTarget, "");
            if (!string.IsNullOrEmpty(s) &&
                Enum.TryParse(s, out CaptureTarget t) &&
                Enum.IsDefined(typeof(CaptureTarget), t))
                return t;
            return CaptureTarget.MainView;
        }

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>キャプチャ実行要求。実処理は ViewerCore 側が行う。</summary>
        public Action<CaptureTarget> OnCapture;

        // ================================================================
        // UI
        // ================================================================

        private TextField     _nameField;
        private TextField     _folderField;
        private DropdownField _targetDropdown;
        private Label         _statusLabel;

        private static readonly List<string> TargetNames = new List<string>
        {
            "メイン3D画面", "3面図を含む", "ウインドウ全体",
        };

        private static CaptureTarget TargetOf(string label)
        {
            int i = TargetNames.IndexOf(label);
            return i < 0 ? CaptureTarget.MainView : (CaptureTarget)i;
        }

        private static string LabelOf(CaptureTarget t)
        {
            int i = (int)t;
            return TargetNames[Mathf.Clamp(i, 0, TargetNames.Count - 1)];
        }

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();

            parent.Add(PlayerIoUiKit.Title("キャプチャ"));

            var note = new Label("ショートカット: K M = メイン3D / K T = 3面図 / K W = ウインドウ全体");
            note.style.fontSize     = 10;
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            parent.Add(note);

            // ── ファイル名 ─────────────────────────────────────────
            parent.Add(PlayerIoUiKit.SectionLabel("ファイル名（連番と .png が付きます）"));

            _nameField = new TextField { value = GetFileName() };
            _nameField.style.marginBottom = 2;
            _nameField.RegisterValueChangedCallback(e => RecentPaths.Set(KeyFileName, e.newValue));
            parent.Add(_nameField);

            // ── 保存フォルダ ───────────────────────────────────────
            parent.Add(PlayerIoUiKit.SectionLabel("保存フォルダ"));

            _folderField = new TextField { value = GetFolder() };
            _folderField.style.marginBottom = 2;
            _folderField.RegisterValueChangedCallback(e => RecentPaths.Set(KeyFolder, e.newValue));
            parent.Add(_folderField);

            // ── 対象 ───────────────────────────────────────────────
            parent.Add(PlayerIoUiKit.SectionLabel("対象"));

            _targetDropdown = new DropdownField("", TargetNames, LabelOf(GetTarget()));
            _targetDropdown.style.marginBottom = 4;
            _targetDropdown.RegisterValueChangedCallback(
                e => RecentPaths.Set(KeyTarget, TargetOf(e.newValue).ToString()));
            parent.Add(_targetDropdown);

            // ── 実行 ───────────────────────────────────────────────
            var runBtn = new Button(() => OnCapture?.Invoke(TargetOf(_targetDropdown.value)))
            {
                text = "キャプチャ"
            };
            runBtn.style.height    = 28;
            runBtn.style.marginTop = 2;
            runBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(runBtn);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop  = 4;
            parent.Add(_statusLabel);
        }

        // ================================================================
        // 同期・状態表示
        // ================================================================

        /// <summary>保存済み設定をフィールドへ反映する。</summary>
        public void Refresh()
        {
            if (_nameField == null) return;
            _nameField     .SetValueWithoutNotify(GetFileName());
            _folderField   .SetValueWithoutNotify(GetFolder());
            _targetDropdown.SetValueWithoutNotify(LabelOf(GetTarget()));
        }

        /// <summary>直近の保存先・失敗理由を表示する。</summary>
        public void SetStatus(string text)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text ?? "";
        }
    }
}
