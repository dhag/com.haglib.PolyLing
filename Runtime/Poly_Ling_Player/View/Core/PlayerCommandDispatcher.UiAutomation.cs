// PlayerCommandDispatcher.UiAutomation.cs
// UI 自動操作コマンド（PanelCommand.UiAutomation.cs）の振り分け。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【プロジェクトの null 門より前で捌く】
//   UI 操作はモデルにもプロジェクトにも触れない。DispatchCore の _getProject() より前で
//   DispatchUiAutomation を呼ぶ（QueryCommandAuditCommand と同じ扱い）。
//   何も読み込んでいない状態でもパネル表示・値の変更・キャプチャができる。
//
// 【受け口の型】
//   他の受け口は失敗理由の文字列だけを返すが、UI 操作は値・一覧・受付番号を返す。
//   ReportData はディスパッチャの中からしか呼べないので、受け口は CommandResult を
//   そのまま返し、ここで結果として採用する。

using System;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        // ================================================================
        // UI 自動操作の受け口（PolyLingPlayerViewerCore.UiAutomation.cs が配線する）
        // ================================================================

        /// <summary>登録済みパネル・項目の一覧。</summary>
        public Func<UiDescribeCommand, CommandResult>      OnUiDescribe;

        /// <summary>パネル表示。</summary>
        public Func<UiShowPanelCommand, CommandResult>     OnUiShowPanel;

        /// <summary>項目を見える状態にする。</summary>
        public Func<UiRevealCommand, CommandResult>        OnUiReveal;

        /// <summary>項目の値の読み取り。</summary>
        public Func<UiGetValueCommand, CommandResult>      OnUiGetValue;

        /// <summary>項目の値の変更。</summary>
        public Func<UiSetValueCommand, CommandResult>      OnUiSetValue;

        /// <summary>項目の強調表示。</summary>
        public Func<UiHighlightCommand, CommandResult>     OnUiHighlight;

        /// <summary>画面キャプチャの開始。</summary>
        public Func<UiCaptureCommand, CommandResult>       OnUiCapture;

        /// <summary>画面キャプチャの状態。</summary>
        public Func<UiCaptureStatusCommand, CommandResult> OnUiCaptureStatus;

        /// <summary>ボタンの押下。</summary>
        public Func<UiClickCommand, CommandResult>         OnUiClick;

        /// <summary>登録状況の検査。</summary>
        public Func<QueryUiAutomationAuditCommand, CommandResult> OnQueryUiAutomationAudit;

        /// <summary>UI 自動操作コマンドなら処理して true を返す。</summary>
        private bool DispatchUiAutomation(PanelCommand cmd)
        {
            switch (cmd)
            {
                case UiDescribeCommand c:      RunUiAutomation(OnUiDescribe,      c, "uiDescribe");      return true;
                case UiShowPanelCommand c:     RunUiAutomation(OnUiShowPanel,     c, "uiShowPanel");     return true;
                case UiRevealCommand c:        RunUiAutomation(OnUiReveal,        c, "uiReveal");        return true;
                case UiGetValueCommand c:      RunUiAutomation(OnUiGetValue,      c, "uiGetValue");      return true;
                case UiSetValueCommand c:      RunUiAutomation(OnUiSetValue,      c, "uiSetValue");      return true;
                case UiHighlightCommand c:     RunUiAutomation(OnUiHighlight,     c, "uiHighlight");     return true;
                case UiCaptureCommand c:       RunUiAutomation(OnUiCapture,       c, "uiCapture");       return true;
                case UiCaptureStatusCommand c: RunUiAutomation(OnUiCaptureStatus, c, "uiCaptureStatus"); return true;
                case UiClickCommand c:         RunUiAutomation(OnUiClick,         c, "uiClick");         return true;
                case QueryUiAutomationAuditCommand c:
                    RunUiAutomation(OnQueryUiAutomationAudit, c, "queryUiAutomationAudit");
                    return true;
                default: return false;
            }
        }

        private void RunUiAutomation<T>(Func<T, CommandResult> hook, T cmd, string name)
            where T : PanelCommand
        {
            if (hook == null) { Fail($"{name} handler not wired"); return; }
            var result = hook.Invoke(cmd);
            _pendingResult = result ?? CommandResult.Fail($"{name} handler returned no result");
        }
    }
}
