// PlayerCommandSchemaSubPanel.cs
// コマンド定義の検査と、MCP 道具一覧（JSON Schema）の書き出し。
//
// 【なぜ Player 側に置くか】
//   検査の対象に実行時の状態が混じる。
//     ・ParameterLimits の値（PLParam.LimitKey から上下限を引く）
//     ・PLSandbox の作業フォルダ（書き出し先の判定）
//   Editor のメニューから走らせると Player build と結果が食い違う。
//
// 【何を見るか】
//   PanelCommandFactoryAudit.RunAll() が 3 つをまとめて回す。
//     1. PLParamAudit.Run       PLParam の付け忘れ
//     2. RunStructure           action 衝突・引数の対応なし・未対応の型
//     3. スキーマ生成           道具として出せた数と出せなかった数・理由
//   コマンドを 1 本足したあとはこれを 1 回押す。
//
// 【書き出し】
//   PLSandbox.TryResolveWrite を通す。作業フォルダが未設定なら拒否理由が
//   そのまま出るので、関門の動作確認も兼ねる。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>コマンド定義の検査とスキーマ書き出し。</summary>
    public class PlayerCommandSchemaSubPanel
    {
        /// <summary>保存ダイアログのファイル名欄の初期値。</summary>
        private const string DefaultFileName = "tools.json";

        private VisualElement     _root;
        private Label             _summaryLabel;
        private Label             _statusLabel;
        private PlayerSaveDestRow _saveDest;
        private ScrollView        _resultView;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            var header = new Label("コマンド定義の検査");
            header.style.marginTop    = 4;
            header.style.marginBottom = 3;
            _root.Add(header);

            var desc = new Label(
                "PLParam の付け忘れ、action 名の衝突、スキーマに出せない型を調べます。\n" +
                "コマンドを追加したあとに 1 回押してください。");
            desc.style.fontSize     = 10;
            desc.style.whiteSpace   = WhiteSpace.Normal;
            desc.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            desc.style.marginBottom = 4;
            _root.Add(desc);

            _summaryLabel = new Label("未実行");
            _summaryLabel.style.fontSize     = 11;
            _summaryLabel.style.whiteSpace   = WhiteSpace.Normal;
            _summaryLabel.style.marginBottom = 4;
            _root.Add(_summaryLabel);

            var runBtn = new Button(OnRun) { text = "検査する" };
            runBtn.style.height       = 26;
            runBtn.style.marginBottom = 6;
            _root.Add(runBtn);

            // ── 書き出し ────────────────────────────────────────────
            var exportHeader = new Label("道具一覧の書き出し");
            exportHeader.style.marginTop    = 4;
            exportHeader.style.marginBottom = 3;
            _root.Add(exportHeader);

            var exportDesc = new Label(
                "[...] は書き込み先フォルダを決めるだけです。"
              + "ファイル名は「名前を付けて保存」のダイアログで決めます。");
            exportDesc.style.fontSize     = 10;
            exportDesc.style.whiteSpace   = WhiteSpace.Normal;
            exportDesc.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            exportDesc.style.marginBottom = 3;
            _root.Add(exportDesc);

            _saveDest = new PlayerSaveDestRow(
                "書き込み先フォルダ", SaveDest.Keys.Schema, "道具一覧の保存先", "json",
                () => DefaultFileName);
            _root.Add(_saveDest.Root);

            var exportBtn = new Button(OnExport) { text = "名前を付けて保存" };
            exportBtn.style.height       = 26;
            exportBtn.style.marginBottom = 4;
            _root.Add(exportBtn);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize     = 10;
            _statusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _statusLabel.style.color        = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.marginBottom = 4;
            _root.Add(_statusLabel);

            _resultView = new ScrollView();
            _resultView.style.flexGrow  = 1;
            _resultView.style.minHeight = 200;
            _root.Add(_resultView);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        /// <summary>開くたびに呼ばれる。検査は自動で走らせない（重いため）。</summary>
        public void Refresh()
        {
            _saveDest?.Refresh();
        }

        // ================================================================
        // 検査
        // ================================================================

        private void OnRun()
        {
            SetStatus("");
            _resultView.Clear();

            string report;
            try
            {
                report = PanelCommandFactoryAudit.RunAll();
            }
            catch (Exception ex)
            {
                SetStatus($"検査に失敗しました: {ex.Message}");
                return;
            }

            PanelCommandFactory.CountTools(out int usable, out int skipped);
            _summaryLabel.text = $"道具として出せる {usable} / 出せない {skipped}";
            _summaryLabel.style.color = new StyleColor(
                skipped == 0 ? Color.white : new Color(1f, 0.8f, 0.4f));

            AppendResult(report);
        }

        // ================================================================
        // 書き出し
        // ================================================================

        private void OnExport()
        {
            SetStatus("");

            // 保存先は必ずダイアログで確定する（他のパネルと同じ規約）。
            // ダイアログで選んだパスは PLSandbox が 1 回だけ通すので、
            // 作業フォルダの外でも書ける。
            string full = _saveDest?.AskSavePath() ?? "";
            if (string.IsNullOrEmpty(full)) return;   // キャンセル

            try
            {
                string json = PanelCommandFactory.BuildToolsListJson();

                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(full, json, System.Text.Encoding.UTF8);

                PanelCommandFactory.CountTools(out int usable, out int skipped);
                SetStatus($"書き出しました（道具 {usable} 本 / 出せなかった {skipped} 本）: {full}");
            }
            catch (Exception ex)
            {
                SetStatus($"書き出しに失敗しました: {ex.Message}");
            }
        }

        // ================================================================
        // 表示
        // ================================================================

        /// <summary>
        /// 検査結果を行ごとの Label として並べる。
        /// 1 つの Label に全文を入れると、行数が多いときに折り返しの計算が重くなる。
        /// </summary>
        private void AppendResult(string report)
        {
            if (_resultView == null || report == null) return;

            foreach (string line in report.Split('\n'))
            {
                var l = new Label(line);
                l.style.fontSize   = 10;
                l.style.whiteSpace = WhiteSpace.Normal;
                if (line.StartsWith("  "))
                    l.style.color = new StyleColor(new Color(1f, 0.8f, 0.4f));
                _resultView.Add(l);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }
    }
}
