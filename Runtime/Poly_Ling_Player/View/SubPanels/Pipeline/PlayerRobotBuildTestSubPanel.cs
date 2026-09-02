// PlayerRobotBuildTestSubPanel.cs
// ロボ組み立て自動検証。基本図形の生成から VRM 書き出しまでを 5 系統ぶん流す。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【既存のパイプライン自動検証との違い】
//   PlayerPipelineTestSubPanel は保存済みプロジェクトの読み込みが起点で、
//   保存は末尾の往復 1 回だけ。こちらは figure を置くところから始め、
//   段が通るたびにフォルダへ保存する。途中経過をあとから追えるようにする。
//
// 【5 系統を別々に流す理由】
//   手作業のフォルダも「上半身のみ」「ブリッジなし Skin」が別枝に切られている。
//   1 本の鎖の途中を保存する形にすると、枝ごとに違う段（ブリッジの有無など）を
//   表現できない。系統ごとに段の並びを持ち、親フォルダを分ける。
//
// 【なぜ実コマンドを送るか】
//   PlayerPipelineTestSubPanel.cs 冒頭と同じ理由。Ops を直接叩くと
//   ディスパッチャ側の欠陥が検査を素通りする。パネルが押されたときに
//   飛ぶのと同じ PanelCommand を送る。
//
// 【段の区切り】
//   コマンドはディスパッチャへ直に流れるが、生成のたびに再構築が走るため
//   送信直後の状態は当てにならない。UIToolkit の schedule で 1 段ずつ間を空ける。
//   MonoBehaviour.Update は使わない（PolyLingPlayerViewer.cs の規約どおり
//   毎フレーム駆動は置かない）。テストが動いていない間は何も走らない。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    /// <summary>ロボ組み立て自動検証。人の操作は「実行」を押すだけ。</summary>
    public partial class PlayerRobotBuildTestSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>現在のモデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>現在のモデル索引。</summary>
        public Func<int> GetModelIndex;

        /// <summary>コマンド送信。パネルが押されたときと同じ経路へ流す。</summary>
        public Action<PanelCommand> SendCommand;

        /// <summary>現在のプロジェクトを指定フォルダへ保存する。</summary>
        public Func<string, bool> SaveProjectFolder;

        /// <summary>VRM を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, Poly_Ling.Vrm.Vrm10ExportSettings, Poly_Ling.Vrm.Vrm10ExportResult> ExportVrm;

        /// <summary>位相が変わったあとの再構築・通知。</summary>
        public Action RefreshAfterTopologyChange;

        // ================================================================
        // 出力先
        // ================================================================

        private const string OutputRootKey = "RobotBuildTest.OutputRoot";

        // ================================================================
        // 系統
        // ================================================================

        /// <summary>流す系統。親フォルダ名にもなる。</summary>
        private enum Variant
        {
            LeftUpperMeshFilter,
            BothUpperMeshFilter,
            BothFullMeshFilter,
            BothFullSkinnedNoBridge,
            BothFullSkinnedBridged,
        }

        private static string FolderOf(Variant v)
        {
            switch (v)
            {
                case Variant.LeftUpperMeshFilter:     return "01_片側上半身_MeshFilter";
                case Variant.BothUpperMeshFilter:     return "02_両側上半身_MeshFilter";
                case Variant.BothFullMeshFilter:      return "03_両側全身_MeshFilter";
                case Variant.BothFullSkinnedNoBridge: return "04_両側全身_Skinned_ブリッジなし";
                default:                              return "05_両側全身_Skinned_ブリッジ付き";
            }
        }

        /// <summary>その系統で置く部位。</summary>
        private static string[] PartsOf(Variant v)
        {
            switch (v)
            {
                case Variant.LeftUpperMeshFilter: return RobotBuildRecipe.LeftUpperBody;
                case Variant.BothUpperMeshFilter: return RobotBuildRecipe.BothUpperBody;
                default:                          return RobotBuildRecipe.BothFullBody;
            }
        }

        /// <summary>ミラーで右半身を出すか。片側だけの系統は出さない。</summary>
        private static bool UsesMirror(Variant v) => v != Variant.LeftUpperMeshFilter;

        /// <summary>スキンド変換まで進むか。</summary>
        private static bool UsesSkin(Variant v)
            => v == Variant.BothFullSkinnedNoBridge || v == Variant.BothFullSkinnedBridged;

        /// <summary>ブリッジを張るか。</summary>
        private static bool UsesBridge(Variant v) => v == Variant.BothFullSkinnedBridged;

        // ================================================================
        // 段
        // ================================================================

        /// <summary>段の結果。Retry は同じ段をもう一度呼ぶ。</summary>
        private enum StageResult { Ok, Fail, Retry }

        /// <summary>1 段ぶんの定義。</summary>
        private sealed class Stage
        {
            /// <summary>表示名。保存フォルダ名にも使う。</summary>
            public string Name;

            /// <summary>段の本体。</summary>
            public Func<StageResult> Run;

            /// <summary>この段の直後に保存するか。</summary>
            public bool Save = true;

            /// <summary>
            /// 見出しだけの段。実行も保存もせず、以降のフォルダ名の接頭辞を切り替える。
            /// 手作業の手順書と同じ区切り（S1 図形生成 / S2 階層 …）を出すために使う。
            /// </summary>
            public bool Group = false;

            /// <summary>
            /// この段の目的。UI しか知らない人が読んで、
            /// 同じことを手で再現できる説明を書く。
            /// </summary>
            public string Purpose = "";

            /// <summary>UI での操作手順。1 行 1 手順。</summary>
            public string[] HowTo;

            /// <summary>パラメータの決め方・注意点。</summary>
            public string Note = "";
        }

        private readonly List<Stage> _stages = new List<Stage>();
        private readonly List<(string name, bool ok, string detail)> _log =
            new List<(string, bool, string)>();

        private int     _stageIndex;
        private int     _retryCount;

        /// <summary>今いる見出し。フォルダ名の途中に入る（例 S4ブリッジ）。</summary>
        private string  _groupName = "";

        /// <summary>
        /// 系統の中の通し番号。
        /// 見出しごとに振り直すと番号が飛んで順序が読めなくなるので、
        /// 系統の頭から最後まで通しで振る。
        /// </summary>
        private int     _stepInGroup;

        /// <summary>系統の手順書に積む行。00_手順書.txt へ書き出す。</summary>
        private readonly List<string> _guide = new List<string>();

        /// <summary>この段で送ったコマンドの記録。段の保存時にファイルへ落とす。</summary>
        private readonly List<string> _commandLog = new List<string>();
        private bool    _running;
        private Variant _variant;
        private int     _variantIndex;
        private string  _outputRoot = "";
        private int     _savedCount;

        /// <summary>段の間に空けるミリ秒。生成後の再構築が捌けるのを待つ。</summary>
        private const long StageIntervalMs = 120;

        /// <summary>同じ段を待ち直す上限。120ms × 100 ＝ 12 秒で諦める。</summary>
        private const int MaxRetry = 100;

        // ================================================================
        // UI
        // ================================================================

        private VisualElement _root;
        private TextField     _outputField;
        private Toggle[]      _variantToggles;
        private Button        _runButton;
        private Label         _status;
        private ScrollView    _resultView;

        public void Build(VisualElement parent)
        {
            _root = parent;
            _root.Clear();

            _root.Add(PlayerIoUiKit.SectionLabel("ロボ組み立て自動検証"));

            var hint = new Label(
                "基本図形の生成 → 原点と回転 → 階層 → ブリッジ → スキン → VRM 書き出し を\n" +
                "系統ごとに流します。段が通るたびにフォルダへ保存します。");
            hint.style.fontSize   = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            _outputField = new TextField("出力ルート");
            _outputField.SetValueWithoutNotify(RecentPaths.Get(OutputRootKey));
            _outputField.RegisterValueChangedCallback(e => RecentPaths.Set(OutputRootKey, e.newValue));
            _root.Add(_outputField);

            _root.Add(PlayerIoUiKit.Divider());
            _root.Add(PlayerIoUiKit.SectionLabel("流す系統"));

            var variants = (Variant[])Enum.GetValues(typeof(Variant));
            _variantToggles = new Toggle[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                _variantToggles[i] = new Toggle(FolderOf(variants[i])) { value = true };
                _root.Add(_variantToggles[i]);
            }

            _runButton = new Button(Run) { text = "実行" };
            _runButton.style.height    = 30;
            _runButton.style.marginTop = 6;
            _root.Add(_runButton);

            _status = new Label("");
            _status.style.fontSize   = 10;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop  = 4;
            _root.Add(_status);

            _resultView = new ScrollView();
            _resultView.style.maxHeight = 260;
            _resultView.style.marginTop = 4;
            _root.Add(_resultView);
        }

        public void Refresh()
        {
            _outputField?.SetValueWithoutNotify(RecentPaths.Get(OutputRootKey));
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        // ================================================================
        // 実行
        // ================================================================

        private void Run()
        {
            if (_running) { SetStatus("実行中です。"); return; }

            _outputRoot = (_outputField?.value ?? "").Trim();
            if (string.IsNullOrEmpty(_outputRoot)) { SetStatus("出力ルートが空です。"); return; }
            if (SendCommand == null)      { SetStatus("配線が足りません（SendCommand）。"); return; }
            if (SaveProjectFolder == null) { SetStatus("配線が足りません（SaveProjectFolder）。"); return; }

            RecentPaths.Set(OutputRootKey, _outputRoot);

            _log.Clear();
            _resultView?.Clear();
            _savedCount   = 0;
            _variantIndex = -1;
            _running      = true;
            _runButton?.SetEnabled(false);

            NextVariant();
        }

        /// <summary>次に流す系統へ進む。全部終わったら締める。</summary>
        private void NextVariant()
        {
            var variants = (Variant[])Enum.GetValues(typeof(Variant));

            // 直前の系統の手順書を確定させる。
            if (_variantIndex >= 0 && _guide.Count > 0) WriteGuide();

            while (true)
            {
                _variantIndex++;
                if (_variantIndex >= variants.Length) { Finish(); return; }
                if (_variantToggles[_variantIndex].value) break;
            }

            _guide.Clear();
            _stepInGroup = 0;

            _variant = variants[_variantIndex];

            // 系統の切れ目を必ず出す。次へ進んだのか止まったのかが
            // 画面から分からないと、どこで詰まったか追えない。
            AddHeading($"{FolderOf(_variant)}  （{_variantIndex + 1}/{variants.Length}）");

            BuildStages(_variant);
            _stageIndex = 0;
            _retryCount = 0;
            ScheduleNextStage();
        }

        private void ScheduleNextStage()
        {
            _root?.schedule.Execute(RunNextStage).StartingIn(StageIntervalMs);
        }

        private void RunNextStage()
        {
            if (!_running) return;

            if (_stageIndex >= _stages.Count) { NextVariant(); return; }

            var stage = _stages[_stageIndex];

            // 見出しの段は実行も保存もしない。フォルダの親を切り替えるだけ。
            if (stage.Group)
            {
                // 番号は振り直さない。系統の頭から通しで振らないと、
                // フォルダを名前順に並べたときの順序が実行順と食い違う。
                _groupName = Sanitize(stage.Name);
                _guide.Add("");
                _guide.Add("■ " + stage.Name);
                AddHeading(stage.Name);
                _stageIndex++;
                ScheduleNextStage();
                return;
            }

            _commandLog.Clear();

            StageResult r;
            try
            {
                r = stage.Run != null ? stage.Run() : StageResult.Ok;
            }
            catch (Exception ex)
            {
                AddLine(stage.Name, false, "例外: " + ex.Message);
                Debug.LogException(ex);
                NextVariant();
                return;
            }

            if (r == StageResult.Retry)
            {
                if (++_retryCount > MaxRetry)
                {
                    AddLine(stage.Name, false, "待ち時間を超えました");
                    NextVariant();
                    return;
                }
                ScheduleNextStage();
                return;
            }

            _retryCount = 0;

            if (r == StageResult.Fail)
            {
                // 失敗した段こそ記録が要る。何を送って何が起きたのかが残らないと
                // 原因が追えない。フォルダ名に「_失敗」を付けて書き出す。
                _stepInGroup++;
                string failDir = Path.Combine(
                    _outputRoot, FolderOf(_variant), StageFolder(stage.Name) + "_失敗");

                // 保存できなくても記録は残す。プロジェクトがまだ無い段で失敗すると
                // 保存だけが落ちるので、フォルダが空になって原因が分からなくなる。
                string detailF = "";
                try { Directory.CreateDirectory(failDir); } catch (Exception) { }

                bool savedF = SaveOne(failDir);
                WriteCommandLog(failDir, stage);
                detailF = savedF ? "記録: " + Path.GetFileName(failDir)
                                 : "記録のみ: " + Path.GetFileName(failDir);

                AddLine(stage.Name, false, "失敗したので以降を打ち切ります　" + detailF);
                foreach (string line in _commandLog) AddCommandLine(line);

                NextVariant();
                return;
            }

            _stepInGroup++;

            string detail = "";
            bool ok = true;

            if (stage.Save)
            {
                string dir = Path.Combine(_outputRoot, FolderOf(_variant), StageFolder(stage.Name));

                if (SaveOne(dir))
                {
                    _savedCount++;
                    detail = "保存: " + Path.GetFileName(dir);
                    WriteCommandLog(dir, stage);
                }
                else
                {
                    // 保存できないまま先へ進めても、あとから経過を追えない。
                    // 段そのものは通っていても失敗として扱う。
                    detail = "保存に失敗: " + dir;
                    ok = false;
                }
            }

            // 送ったコマンドを画面にも出す。何をどのパラメータで実行したかが
            // 分からないと、結果を見ても手順を追えない。
            AddLine(stage.Name, ok, detail);
            foreach (string line in _commandLog) AddCommandLine(line);

            if (!ok) { NextVariant(); return; }

            _stageIndex++;
            ScheduleNextStage();
        }

        private bool SaveOne(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                return SaveProjectFolder(dir);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private void Finish()
        {
            _running = false;
            _runButton?.SetEnabled(true);
            AddHeading("すべての系統を終えました");
            SetStatus($"完了。保存 {_savedCount} 件。");
        }

        /// <summary>フォルダ名に使えない字を置き換える。</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "stage";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
            return sb.ToString();
        }

        // ================================================================
        // 結果表示
        // ================================================================

        private void AddHeading(string text)
        {
            if (_resultView == null) return;
            var l = new Label("■ " + text);
            l.style.marginTop = 6;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            _resultView.Add(l);
            SetStatus(text + " を実行中…");
        }

        private void AddLine(string name, bool ok, string detail)
        {
            _log.Add((name, ok, detail));
            if (_resultView == null) return;

            var l = new Label($"  {(ok ? "○" : "×")} {name}" +
                              (string.IsNullOrEmpty(detail) ? "" : "  / " + detail));
            l.style.fontSize   = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (!ok) l.style.color = new Color(1f, 0.5f, 0.5f);
            _resultView.Add(l);
        }

        /// <summary>送ったコマンドの内容を結果欄へ出す。</summary>
        private void AddCommandLine(string text)
        {
            if (_resultView == null || string.IsNullOrEmpty(text)) return;

            var l = new Label("      " + text.Replace("\n", "\n      "));
            l.style.fontSize   = 9;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.color      = new Color(0.65f, 0.75f, 0.85f);
            _resultView.Add(l);
        }

        private int ModelIndex() => GetModelIndex?.Invoke() ?? 0;

        // ================================================================
        // コマンドログ
        // ================================================================

        /// <summary>
        /// コマンドを送り、その内容を記録する。
        /// 何をどのパラメータで実行したかが残らないと、結果を見ても手順を追えない。
        /// 段の名前ではなくコマンドの中身（PLParam）をそのまま出す。
        /// </summary>
        private void SendLogged(PanelCommand cmd)
        {
            if (cmd == null) return;

            _commandLog.Add(PanelCommandDump.Describe(cmd));
            SendCommand?.Invoke(cmd);
        }

        /// <summary>
        /// 段のフォルダ名。「通し番号_見出し_段名」。
        ///
        /// 見出しを名前に含めるのは、フォルダを開かずに工程が分かるようにするため。
        /// 見出しごとの中間フォルダを作ると、上下に行き来しないと順序が読めない。
        /// </summary>
        private string StageFolder(string stageName)
            => $"{_stepInGroup:00}_{_groupName}_{Sanitize(stageName)}";

        /// <summary>
        /// 系統の手順書を書き出す。フォルダ一覧と各段の目的を 1 枚にまとめる。
        /// これを読めば、どの順で何をしたかが分かるようにする。
        /// </summary>
        private void WriteGuide()
        {
            try
            {
                string dir = Path.Combine(_outputRoot, FolderOf(_variant));
                Directory.CreateDirectory(dir);

                var sb = new System.Text.StringBuilder();
                sb.Append("=====================================================\n");
                sb.Append(" ").Append(FolderOf(_variant)).Append(" の作り方\n");
                sb.Append("=====================================================\n\n");
                sb.Append("下のフォルダを番号順に開くと、各工程の直後の状態が入っています。\n");
                sb.Append("フォルダの中の「手順.txt」に、その工程の目的と操作が書いてあります。\n");
                foreach (string line in _guide) sb.Append(line).Append('\n');

                File.WriteAllText(Path.Combine(dir, "00_手順書.txt"), sb.ToString());
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>
        /// 実行結果の覚え書きを 1 行足す。コマンドと同じ列に並べ、
        /// 画面にも command_log.txt にも出す。
        /// 送ったコマンドだけでは「効いたかどうか」が分からないので、
        /// 面数や穴の内訳をここへ残す。
        /// </summary>
        private void AddNote(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _commandLog.Add("[結果] " + text);
        }

        /// <summary>
        /// 段のフォルダへ「手順.txt」を書く。
        ///
        /// 読み手は UI しか知らない人。目的 → UI での操作 → 決め方 → 結果 →
        /// 送ったコマンド、の順に並べる。
        /// コマンド名や配列はそのまま残す。同じことを別の道具から再現するときの
        /// 手がかりになるし、読み飛ばせばよい。
        /// </summary>
        private void WriteCommandLog(string dir, Stage stage)
        {
            try
            {
                Directory.CreateDirectory(dir);

                var sb = new System.Text.StringBuilder();
                sb.Append("─────────────────────────────────────────\n");
                sb.Append(" ").Append(stage.Name).Append("\n");
                sb.Append("─────────────────────────────────────────\n");
                sb.Append(" ").Append(FolderOf(_variant)).Append(" / ").Append(_groupName).Append("\n\n");

                if (!string.IsNullOrEmpty(stage.Purpose))
                    sb.Append("【目的】\n").Append(Indent(stage.Purpose)).Append("\n\n");

                if (stage.HowTo != null && stage.HowTo.Length > 0)
                {
                    sb.Append("【UI での操作】\n");
                    for (int i = 0; i < stage.HowTo.Length; i++)
                        sb.Append("  ").Append(i + 1).Append(". ").Append(stage.HowTo[i]).Append('\n');
                    sb.Append('\n');
                }

                if (!string.IsNullOrEmpty(stage.Note))
                    sb.Append("【決め方・注意】\n").Append(Indent(stage.Note)).Append("\n\n");

                var results = new List<string>();
                var cmds    = new List<string>();
                foreach (string line in _commandLog)
                {
                    if (line.StartsWith("[結果] ")) results.Add(line.Substring(4).Trim());
                    else                            cmds.Add(line);
                }

                if (results.Count > 0)
                {
                    sb.Append("【結果】\n");
                    foreach (string r in results) sb.Append("  ").Append(r).Append('\n');
                    sb.Append('\n');
                }

                if (cmds.Count > 0)
                {
                    sb.Append("【送ったコマンド】\n");
                    sb.Append("  ※ 内部の命令名です。UI から同じ操作をすれば同じものが飛びます。\n\n");
                    for (int i = 0; i < cmds.Count; i++)
                        sb.Append("  (").Append(i + 1).Append(") ").Append(cmds[i]).Append("\n\n");
                }

                File.WriteAllText(Path.Combine(dir, "手順.txt"), sb.ToString());

                // 手順書の 1 行。フォルダ名と目的の 1 行目を並べる。
                string head = string.IsNullOrEmpty(stage.Purpose)
                    ? ""
                    : "  … " + stage.Purpose.Split('\n')[0];
                _guide.Add($"  {Path.GetFileName(dir)}{head}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>複数行を字下げして返す。</summary>
        private static string Indent(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split('\n');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append("  ").Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

    }
}
