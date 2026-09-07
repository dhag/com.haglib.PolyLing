// PlayerOriginTestSubPanel.cs
// 原点CSV自動検証。ボタン 1 回で
//   MQO 読込 → 原点CSV適用 → Humanoid オートマップ → ミラー分岐の計画確認
//   → 記録の検査とレポート書出 → VRM 書出
// までを流す。段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるための検証か】
//   原点CSV は「各オブジェクトの原点（＝関節位置）をどこへ置くか」を並べた表。
//   適用すると、オブジェクトのローカル原点だけが動き、頂点のワールド位置は
//   変わらないのが正しい。ここが崩れると「読み込んだら形が飛んだ」になる。
//   この検証は ApplyObjectOrigins が通った分岐をオブジェクトごとに記録し、
//   「ワールド位置が動いていないか」を機械的に見る。
//
// 【この段階で右半身が無いのは不具合ではない】
//   スキンド変換前のモデルに右半身の関節ノードは存在しない。右半身は
//   MirrorBranchOps.BuildMirrorBranchPlan が持つ「ミラー枝に出す計画」として
//   表現され、実体化するのはスキンド変換（MeshFilterToSkinnedConverter）である。
//   したがって Right* が埋まることを合否条件にせず、
//   「左側が割り当たっているか」と「その相手がミラー枝に出る計画になっているか」を見る。
//
// 【VRM はここでも出せる】
//   スキンド変換前でもよい。PolyLingToVrmLibConverter.cs:200 が
//   skinned = ExportSkinning && IsSkinned && boneOrder.Count > 0 で分岐し、
//   SkinKind.MeshFilter のメッシュはノードに TRS を入れて配置する（同 :53）。
//   欠けている必須関節は SupplementHumanoid がダミーで補う
//   （Vrm10ExportTypes.cs:102-112）。スキニングが付かないだけで VRM は成立する。
//
// 【入力欄を置かない理由】
//   同じファイルを何度も使うため。MQO は直前の import が、CSV は直前の原点CSV読込が
//   RecentPaths に残しているので、それをそのまま使う。押すだけで走る。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;          // RecentPaths
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    /// <summary>原点CSV自動検証。人の操作は「実行」を押すだけ。</summary>
    public class PlayerOriginTestSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext> GetModel;
        public Func<int>          GetModelIndex;
        public Action<PanelCommand> SendCommand;

        /// <summary>MQO を読み込む。実際の import 経路（ImportMqoCommand）へ流す。</summary>
        public Action<string> ImportMqo;

        /// <summary>VRM を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, Poly_Ling.Vrm.Vrm10ExportSettings, Poly_Ling.Vrm.Vrm10ExportResult> ExportVrm;

        private int ModelIndex => GetModelIndex?.Invoke() ?? 0;

        // ================================================================
        // RecentPaths のキー（IO パネルと共有する）
        // ================================================================

        private const string MqoPathKey = "Import.MQO.Path";
        private const string CsvPathKey = "BoneEditor.OriginCsv.Path";
        private const string VrmPathKey = "Export.VRM.Path";

        /// <summary>ワールド位置が動いたと見なす閾値。</summary>
        private const float MoveEpsilon = 1e-4f;

        // ================================================================
        // UI
        // ================================================================

        private Label     _pathLabel;
        private Toggle    _doExport;
        private TextField _vrmPathField;

        // ================================================================
        // 実行状態
        // ================================================================

        private string _mqoPath = "";
        private string _csvPath = "";
        private string _reportPath = "";

        private HumanoidBoneMapping _mapping;
        private int                 _mapCandidates;
        private MirrorBranchPlan    _branchPlan;
        private readonly List<int>  _branchRoots = new List<int>();
        private int _meshCountBefore;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "原点CSV自動検証";

        protected override string NoteText =>
            "MQO 読込 → 原点CSV適用 → Humanoid オートマップ → ミラー分岐の計画確認\n"
          + "→ 記録の検査とレポート書出 → VRM 書出 を通しで流します。\n"
          + "対象は直前に使った MQO と原点CSV です。段ごとに手順と理由をログに出します。";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("入力（直前に使ったパスをそのまま使う）"));
            _pathLabel = new Label("");
            _pathLabel.style.fontSize   = 10;
            _pathLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_pathLabel);
            root.Add(Hint(
                "MQO はインポートパネルが、原点CSV はボーン編集パネルの「原点CSV読込」が"
              + "最後に使ったパスを残しています。どちらかが未設定なら先に一度手で通してください。"));

            root.Add(Sec("VRM 書出"));
            _doExport = new Toggle("最後に VRM を書き出す") { value = true };
            root.Add(_doExport);

            _vrmPathField = new TextField("VRM パス");
            _vrmPathField.style.fontSize = 10;
            _vrmPathField.value = SafeGet(VrmPathKey);
            root.Add(_vrmPathField);
            root.Add(Hint(
                "スキンド変換前なのでスキニングは付きません（メッシュがボーンに剛体で付く）。"
              + "欠けている必須関節はダミーで補って出力します。"));

            RefreshPathLabel();
        }

        public override void Refresh()
        {
            base.Refresh();
            if (!IsRunning) RefreshPathLabel();
        }

        private void RefreshPathLabel()
        {
            if (_pathLabel == null) return;
            string mqo = SafeGet(MqoPathKey);
            string csv = SafeGet(CsvPathKey);
            _pathLabel.text =
                "MQO: " + (string.IsNullOrEmpty(mqo) ? "（未設定）" : mqo) + "\n" +
                "CSV: " + (string.IsNullOrEmpty(csv) ? "（未設定）" : csv);
        }

        private static string SafeGet(string key)
        {
            try { return RecentPaths.Get(key); }
            catch { return ""; }
        }

        protected override bool CanRun()
        {
            _mqoPath = SafeGet(MqoPathKey);
            _csvPath = SafeGet(CsvPathKey);

            if (string.IsNullOrEmpty(_mqoPath) || !File.Exists(_mqoPath))
            { SetStatus("直前に読み込んだ MQO が見つかりません。一度 MQO を import してください。"); return false; }

            if (string.IsNullOrEmpty(_csvPath) || !File.Exists(_csvPath))
            { SetStatus("直前に使った原点CSVが見つかりません。一度「原点CSV読込」を実行してください。"); return false; }

            if (ImportMqo == null || SendCommand == null || GetModel == null)
            { SetStatus("配線が足りません（ImportMqo / SendCommand / GetModel）。"); return false; }

            if (_doExport.value)
            {
                if (ExportVrm == null) { SetStatus("配線が足りません（ExportVrm）。"); return false; }
                if (string.IsNullOrEmpty(_vrmPathField.value)) { SetStatus("VRM パスが空です。"); return false; }
            }
            return true;
        }

        protected override void ResetRunState()
        {
            _mapping = null;
            _mapCandidates = 0;
            _branchPlan = null;
            _branchRoots.Clear();
            _meshCountBefore = GetModel()?.MeshContextCount ?? 0;

            _reportPath = Path.Combine(
                Application.persistentDataPath, "PolyLing", "OriginTest",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"), "report.txt");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. MQO を読み込む",                     StageImportMqo));
            stages.Add(("2. 読み込みの完了を待つ",               StageWaitImport));
            stages.Add(("3. 原点CSVを適用する",                  StageApplyCsv));
            stages.Add(("4. Humanoid を自動割当し、ミラー分岐の計画を作る", StageAutoMapHumanoid));
            stages.Add(("5. 記録を検査してレポートを書く",       StageWriteReport));
            if (_doExport.value)
                stages.Add(("6. VRM を書き出す",                 StageExportVrm));
        }

        // ================================================================
        // 段
        // ================================================================

        private StageResult StageImportMqo()
        {
            ImportMqo(_mqoPath);
            return Ok(
                $"ImportMqoCommand と同じ経路で「{Path.GetFileName(_mqoPath)}」を読み込んだ",
                "インポートパネル → MQO → ファイルを選ぶ",
                "MQO は板（MeshFilter 系）の集まりで、この時点ではボーンもウェイトも無い。"
              + "関節の位置はこのあと原点CSVで入れる。");
        }

        private StageResult StageWaitImport()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;
            if (model.MeshContextCount == _meshCountBefore) return StageResult.Retry;

            return Ok(
                $"モデル「{model.Name}」に MeshContext が {model.MeshContextCount} 件できた",
                null,
                "読み込みはファイルの大きさで時間が変わる。件数が増えるまで待ち直している。"
              + "ここで止まるなら、そもそも import が走っていない。");
        }

        private StageResult StageApplyCsv()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            if (!ParseCsv(_csvPath, out var names, out var positions, out string err))
                return Ng($"CSV を読めなかった: {err}",
                    "ボーン編集パネル →「原点CSV読込」",
                    "CSV は 1 行が「名前, x, y, z」。# で始まる行と name, で始まる見出し行は飛ばす。"
                  + "有効な行が 0 なら区切り文字か列数が違う。");

            // 分岐の記録を有効にしてから送る。適用が終わるまで立てておく。
            ObjectOriginDiag.Enabled = true;
            SendCommand(new ApplyObjectOriginsCommand(ModelIndex, names, positions));

            return Ok(
                $"CSV {names.Length} 行を ApplyObjectOriginsCommand で適用した（診断の記録を有効化）",
                "ボーン編集パネル →「原点CSV読込」でファイルを選ぶ",
                "原点CSV はオブジェクトのローカル原点だけを動かす。"
              + "頂点は原点の移動ぶんだけ逆に動かして、ワールド位置が変わらないようにする（再局所化）。"
              + "ここが抜けると形が飛ぶので、次の段で「動いていないか」を機械的に見る。");
        }

        /// <summary>
        /// Humanoid オートマップと、ミラー分岐の計画確認。
        ///
        /// 候補はボーンだけでなく描画オブジェクトも含める。MQO を読んだ直後は
        /// ボーンが1本も無く、ボーンだけを候補にすると割当が 0 件になるため。
        /// HumanoidBoneMapping は「索引 = MeshContextList の索引」を前提にしているので、
        /// MeshContextCount の長さで各名前を自分の索引位置に置いたリストを渡す。
        /// </summary>
        private StageResult StageAutoMapHumanoid()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが無い", null, null);

            var names = new List<string>();
            _mapCandidates = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                string nm = (mc != null && !string.IsNullOrEmpty(mc.Name)) ? mc.Name : "";
                names.Add(nm);
                if (!string.IsNullOrEmpty(nm)) _mapCandidates++;
            }

            _mapping = new HumanoidBoneMapping();
            int mapped = _mapping.AutoMapFromEmbeddedCSV(names);

            if (mapped > 0)
            {
                ApplyHumanoidMappingCommand.SplitMapping(_mapping, out var hmNames, out var hmIdx);
                SendCommand(new ApplyHumanoidMappingCommand(ModelIndex, hmNames, hmIdx));
            }

            _branchRoots.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
                if (model.GetMeshContext(i)?.IsMirrorBranchRoot == true) _branchRoots.Add(i);

            _branchPlan = MirrorBranchOps.BuildMirrorBranchPlan(
                model, null, MirrorBranchTolerance.Tolerant);

            int emits = _branchPlan.CollectGeneratedMirrors().Count;

            if (mapped == 0)
                return Ng($"候補 {_mapCandidates} 件に対して割当 0 件",
                    "Humanoid 割当パネル →「自動割当」",
                    "自動割当は名前の表（埋め込みCSV）と突き合わせる。0 件なら"
                  + "オブジェクト名が表のどれにも当たっていない。");

            return Ok(
                $"候補 {_mapCandidates} 件 → 割当 {mapped} 件。"
              + $"ミラー分岐ルート {_branchRoots.Count} 件 / ミラー枝に出る計画 {emits} 件",
                "Humanoid 割当パネル →「自動割当」→ ミラー設定パネルで分岐ルートを確認",
                "候補にはボーンだけでなく描画オブジェクトも入れる。MQO 直後はボーンが 1 本も無く、"
              + "ボーンだけを候補にすると割当が 0 件になるため。"
              + "右半身はまだ実体が無く、ミラー枝に出す「計画」として持つ。"
              + "実体化するのはスキンド変換のとき。");
        }

        private StageResult StageWriteReport()
        {
            var model = GetModel();
            ObjectOriginDiag.Enabled = false;

            var entries = ObjectOriginDiag.Entries;
            if (entries.Count == 0)
                return Ng("診断の記録が空。ApplyObjectOrigins が走っていない", null,
                    "記録は段 3 で有効にしてからコマンドを送る。空なら適用そのものが動いていない。");

            var moved      = new List<ObjectOriginDiag.Entry>();
            var notApplied = new List<ObjectOriginDiag.Entry>();
            var skipped    = new List<ObjectOriginDiag.Entry>();

            foreach (var e in entries)
            {
                if (e.VertexCount > 0 && e.MaxWorldDelta > MoveEpsilon) moved.Add(e);
                if (e.IsTarget && !e.UseLocalAfter) notApplied.Add(e);
                if (e.IsTarget && e.SkippedByMatrixCompare) skipped.Add(e);
            }

            var missing  = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            var byPlan   = new List<string>();
            var unsolved = new List<string>();
            SplitMissingByBranchPlan(model, missing, byPlan, unsolved);

            var sb = new StringBuilder();
            WriteReport(sb, model, entries, moved, notApplied, skipped, missing, byPlan, unsolved);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                return Ng("レポートを書けなかった: " + e.Message, null, null);
            }

            string detail =
                $"記録 {entries.Count} 件 / ワールド位置が動いた {moved.Count} 件 / "
              + $"CSV 行があるのに姿勢が入っていない {notApplied.Count} 件 / "
              + $"行列比較でスキップ {skipped.Count} 件。"
              + $"必須の未割当 {missing.Count} 件（ミラー枝で解決見込み {byPlan.Count} / "
              + $"見込みなし {unsolved.Count}）。レポート: {_reportPath}";

            if (moved.Count > 0 || notApplied.Count > 0 || unsolved.Count > 0)
            {
                var head = new StringBuilder();
                int show = Mathf.Min(5, moved.Count);
                for (int i = 0; i < show; i++)
                    head.Append($" [{moved[i].Index}]{moved[i].Name} ずれ={moved[i].MaxWorldDelta:F6}");
                if (moved.Count > show) head.Append($" … 他 {moved.Count - show} 件");

                return Ng(detail + head,
                    null,
                    "ワールド位置が動いた＝再局所化が抜けている。"
                  + "姿勢が入っていない＝CSV の名前がオブジェクト名と一致していない。"
                  + "見込みなしの未割当＝左右どちらも名前が当たっていない。"
                  + "どのオブジェクトで起きたかはレポートに全件出ている。");
            }

            return Ok(detail,
                "ボーン編集パネルで原点CSVを読んだあと、形が飛んでいないかを目で見るのと同じ",
                "合格の条件は 3 つ。ワールド位置が動いていない／CSV 行のあるものに姿勢が入っている／"
              + "必須の未割当が全てミラー枝で解決される見込み。"
              + "右半身がまだ無いのは正常なので、Right* の未割当は不合格にしない。");
        }

        private StageResult StageExportVrm()
        {
            string path = _vrmPathField.value;

            var settings = Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            // 右半身などの欠損関節をダミーで補う。補わないと VRM 1.0 の必須ボーンが
            // 欠けたままになり、ビューアが読み込みを拒否する。
            settings.SupplementHumanoid = true;

            var result = ExportVrm(path, settings);
            if (result == null || !result.Success)
                return Ng("VRM 書出に失敗: " + (result?.ErrorMessage ?? "戻り値がありません"),
                    "エクスポートパネル → VRM",
                    "Humanoid の割当が 0 件だと VRM にできない。段 4 の割当件数を見る。");

            if (result.HumanoidBoneCount == 0)
                return Ng("Humanoid ボーンが 0 件で出力された", null,
                    "VRM 1.0 は humanoid が必須。0 件のファイルはビューアが読めない。");

            try { RecentPaths.Set(VrmPathKey, path); } catch { }

            return Ok(
                $"「{Path.GetFileName(path)}」へ書き出した。"
              + $"ノード {result.NodeCount} / メッシュ {result.MeshCount} / 頂点 {result.VertexCount} / "
              + $"Humanoid ボーン {result.HumanoidBoneCount}（うちダミー補完 {result.SupplementedJointCount}）"
              + (string.IsNullOrEmpty(result.Warning) ? "" : $" / 警告: {result.Warning}"),
                "エクスポートパネル → VRM → 保存先を選ぶ",
                "スキンド変換前なのでスキニングは付かず、メッシュはボーンに剛体で付く"
              + "（PolyLingToVrmLibConverter.cs:200 の分岐）。"
              + "ダミー補完の数は、右半身などまだ実体が無い関節の数とおおむね対応する。"
              + "段 4 の「ミラー枝に出る計画」の件数と見比べると、"
              + "どこまでが計画どおりかが分かる。");
        }

        protected override void OnFinished(bool aborted)
        {
            ObjectOriginDiag.Enabled = false;
        }

        // ================================================================
        // 検査の補助
        // ================================================================

        /// <summary>
        /// 必須の未割当を「ミラー枝で解決される見込み」と「見込みなし」に分ける。
        /// スキンド変換前に右半身の関節ノードは存在しないので、
        /// Right* が埋まっていないこと自体は不具合ではない。
        /// </summary>
        private void SplitMissingByBranchPlan(
            ModelContext model, List<string> missing,
            List<string> byPlan, List<string> unsolved)
        {
            byPlan.Clear();
            unsolved.Clear();
            if (missing == null) return;

            foreach (string humanName in missing)
            {
                string peer = MirrorNameOps.SwapHumanoidLeftRight(humanName);
                bool solved = false;

                if (!string.IsNullOrEmpty(peer) && _mapping != null &&
                    _mapping.BoneIndexMap.TryGetValue(peer, out int peerIndex) &&
                    _branchPlan != null && _branchPlan.EmitsMirror(peerIndex))
                {
                    string peerName = model?.GetMeshContext(peerIndex)?.Name ?? "?";
                    byPlan.Add($"{humanName}（{peer} = [{peerIndex}] {peerName} のミラー枝）");
                    solved = true;
                }

                if (!solved) unsolved.Add(humanName);
            }
        }

        // ================================================================
        // レポート本文
        // ================================================================

        private void WriteReport(
            StringBuilder sb, ModelContext model,
            List<ObjectOriginDiag.Entry> entries,
            List<ObjectOriginDiag.Entry> moved,
            List<ObjectOriginDiag.Entry> notApplied,
            List<ObjectOriginDiag.Entry> skipped,
            List<string> missing, List<string> byPlan, List<string> unsolved)
        {
            sb.AppendLine("# PolyLing 原点CSV自動検証レポート");
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("MQO: " + _mqoPath);
            sb.AppendLine("CSV: " + _csvPath);
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine("MeshContext 数: " + (model?.MeshContextCount ?? 0));
            sb.AppendLine("記録件数: " + entries.Count);
            sb.AppendLine();

            sb.AppendLine("## 判定");
            sb.AppendLine($"ワールド位置が動いた: {moved.Count} 件（閾値 {MoveEpsilon}）");
            sb.AppendLine($"CSV 行があるのに姿勢が入っていない: {notApplied.Count} 件");
            sb.AppendLine($"行列比較でスキップした適用先: {skipped.Count} 件");
            sb.AppendLine($"ミラー分岐ルート: {_branchRoots.Count} 件");
            sb.AppendLine($"Humanoid 割当: {_mapping?.Count ?? 0} 件 / 必須の未割当: {missing.Count} 件");
            sb.AppendLine($"  うちミラー枝で解決見込み: {byPlan.Count} 件");
            sb.AppendLine($"  解決見込みなし: {unsolved.Count} 件");
            sb.AppendLine();

            sb.AppendLine("## 動いたオブジェクト");
            if (moved.Count == 0) sb.AppendLine("なし");
            foreach (var e in moved)
            {
                sb.AppendLine($"- [{e.Index}] {e.Name}");
                sb.AppendLine($"    ずれ: {e.MaxWorldDelta:F6} {FormatV(e.WorldDeltaOfMax)}");
                sb.AppendLine($"    CSV 行: {(e.InCsv ? "有" : "無")} / 再局所化: {(e.Relocalized ? "実行" : "未")}");
                sb.AppendLine($"    祖先: {FormatAncestors(model, e)}");
            }
            sb.AppendLine();

            sb.AppendLine("## CSV 行があるのに姿勢が入っていないもの");
            if (notApplied.Count == 0) sb.AppendLine("なし");
            foreach (var e in notApplied) sb.AppendLine($"- [{e.Index}] {e.Name}");
            sb.AppendLine();

            sb.AppendLine("## 行列比較でスキップした適用先");
            if (skipped.Count == 0) sb.AppendLine("なし");
            foreach (var e in skipped) sb.AppendLine($"- [{e.Index}] {e.Name}");
            sb.AppendLine();

            sb.AppendLine("## 必須の未割当（ミラー枝で解決見込み）");
            if (byPlan.Count == 0) sb.AppendLine("なし");
            foreach (var s in byPlan) sb.AppendLine("- " + s);
            sb.AppendLine();

            sb.AppendLine("## 必須の未割当（解決見込みなし）");
            if (unsolved.Count == 0) sb.AppendLine("なし");
            foreach (var s in unsolved) sb.AppendLine("- " + s);
            sb.AppendLine();

            sb.AppendLine("## 実行ログ");
            foreach (var line in PlainLog) sb.Append(line);
        }

        private static string FormatV(Vector3 v)
            => $"({v.x:F6}, {v.y:F6}, {v.z:F6})";

        private static string FormatAncestors(ModelContext model, ObjectOriginDiag.Entry e)
        {
            if (e.Ancestors == null || e.Ancestors.Count == 0) return "（ルート）";

            var sb = new StringBuilder();
            for (int i = 0; i < e.Ancestors.Count; i++)
            {
                if (i > 0) sb.Append(" < ");
                int idx = e.Ancestors[i];
                sb.Append('[').Append(idx).Append(']');
                sb.Append(model?.GetMeshContext(idx)?.Name ?? "?");
            }
            return sb.ToString();
        }

        // ================================================================
        // CSV
        // ================================================================

        private static bool ParseCsv(
            string path, out string[] names, out Vector3[] positions, out string error)
        {
            names = null; positions = null; error = "";

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { error = e.Message; return false; }

            var ns = new List<string>();
            var ps = new List<Vector3>();

            foreach (string raw in lines)
            {
                string line = raw?.Trim('\uFEFF', ' ', '\t');
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("name,")) continue;

                var cols = line.Split(',');
                if (cols.Length < 4) continue;
                if (!float.TryParse(cols[1], out float x)) continue;
                if (!float.TryParse(cols[2], out float y)) continue;
                if (!float.TryParse(cols[3], out float z)) continue;

                ns.Add(cols[0]);
                ps.Add(new Vector3(x, y, z));
            }

            if (ns.Count == 0) { error = "有効な行がない"; return false; }

            names = ns.ToArray();
            positions = ps.ToArray();
            return true;
        }
    }
}
