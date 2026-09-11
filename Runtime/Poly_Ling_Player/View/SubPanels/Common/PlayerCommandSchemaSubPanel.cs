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

        // UI 自動操作の ID は "commandSchema.<下の Id>"（UiControlAttribute.cs）。
        // 検査の結果は行を並べて作り直すので、結果の入れ物はデータ行（Rows）にする。
        [UiControl(Ignore = true)]
        private VisualElement     _root;
        [UiControl("summary", Safety = UiSafety.ReadOnly, Description = "検査の要約")]
        private Label             _summaryLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label             _statusLabel;
        [UiNested("saveDest")]
        private PlayerSaveDestRow _saveDest;
        [UiControl(Ignore = true, Rows = true)]
        private ScrollView        _resultView;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "コマンドの宣言を検査する")]
        private Button            _runBtn;
        [UiControl("export", Safety = UiSafety.UserOnly, Description = "検査の結果を名前を付けて保存する（保存ダイアログを開く）")]
        private Button            _exportBtn;
        [UiControl(Ignore = true)]
        private VisualElement     _uiAuditBox;
        [UiControl("uiAudit.result", Safety = UiSafety.ReadOnly, Description = "UI 自動操作の登録で直すべきものの件数（無いときは非表示）")]
        private Label             _uiAuditLabel;
        [UiControl("uiAudit.run", Safety = UiSafety.SafeWrite, Description = "UI 自動操作の登録状況を検査し直す")]
        private Button            _uiAuditBtn;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            BuildUiAuditSection(_root);

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
            _runBtn = runBtn;
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
            _exportBtn = exportBtn;
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

        // ================================================================
        // UI 自動操作の登録状況
        // ================================================================

        /// <summary>
        /// UI 自動操作の登録状況。再生開始時の検査で問題があったときだけ出す。
        ///
        /// 【なぜここに出すか】
        ///   属性の付け忘れや未登録の部品はコンパイルを通ってしまうので、
        ///   人が queryUiAutomationAudit を呼ぶまで気づけない。ログだけだと流れるため、
        ///   コマンド定義の検査と同じ場所に、直すべきものが残っている間ずっと出しておく。
        /// </summary>
        private void BuildUiAuditSection(VisualElement root)
        {
            _uiAuditBox = new VisualElement();
            _uiAuditBox.style.marginBottom    = 6;
            _uiAuditBox.style.paddingTop      = 4;
            _uiAuditBox.style.paddingBottom   = 4;
            _uiAuditBox.style.paddingLeft     = 6;
            _uiAuditBox.style.paddingRight    = 6;
            _uiAuditBox.style.backgroundColor = new StyleColor(new Color(0.35f, 0.22f, 0.05f));
            _uiAuditBox.style.display         = DisplayStyle.None;

            var head = new Label("UI 自動操作の登録に直すべきものがあります");
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color      = new StyleColor(new Color(1f, 0.82f, 0.45f));
            head.style.whiteSpace = WhiteSpace.Normal;
            _uiAuditBox.Add(head);

            _uiAuditLabel = new Label("");
            _uiAuditLabel.style.fontSize   = 10;
            _uiAuditLabel.style.whiteSpace = WhiteSpace.Normal;
            _uiAuditLabel.style.marginTop  = 2;
            _uiAuditBox.Add(_uiAuditLabel);

            root.Add(_uiAuditBox);

            var btn = new Button(OnRunUiAudit) { text = "UI 登録を検査" };
            btn.style.height       = 24;
            btn.style.marginBottom = 6;
            root.Add(btn);
            _uiAuditBtn = btn;
        }

        private void OnRunUiAudit() => ShowUiAuditResult(RunUiAutomationAudit?.Invoke());

        /// <summary>
        /// 検査の結果を表示する。問題が無ければ枠ごと隠す。
        /// 再生開始時の結果を ViewerCore から渡すときにも使う。
        /// </summary>
        public void ShowUiAuditResult(UiAutomationAudit.Result r)
        {
            if (_uiAuditBox == null) return;

            if (r == null || !r.HasProblems)
            {
                _uiAuditBox.style.display = DisplayStyle.None;
                if (_uiAuditLabel != null) _uiAuditLabel.text = "";
                return;
            }

            _uiAuditBox.style.display = DisplayStyle.Flex;
            _uiAuditLabel.text = r.ProblemSummary()
                               + "\n直し方は queryUiAutomationAudit の結果を見てください。";
        }

        /// <summary>今の登録状況を検査し直す。ViewerCore が配線する。</summary>
        public Func<UiAutomationAudit.Result> RunUiAutomationAudit;

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
