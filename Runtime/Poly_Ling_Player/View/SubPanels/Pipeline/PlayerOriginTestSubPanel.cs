// PlayerOriginTestSubPanel.cs
// 原点CSV自動検証パネル。ボタン1つで MQO 読込 → 原点CSV適用 →
// Humanoid オートマップ → ミラー分岐の計画確認 → 検査 → レポート書出まで流す。
//
// 【この段階でアバターは成立しない】
//   スキンド変換前のモデルに右半身の関節ノードは存在しない。右半身は
//   MirrorBranchOps.BuildMirrorBranchPlan が持つ「ミラー枝に出す計画」として
//   表現され、実体化するのはスキンド変換（MeshFilterToSkinnedConverter）である。
//   したがってここでは Right* が埋まることを合否条件にせず、
//   「左側が割り当たっているか」と「その相手がミラー枝に出る計画になっているか」を見る。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ作るか】
//   「読み込んだら飛んだ」という結果だけでは、どの分岐が誤ったのか決まらない。
//   ApplyObjectOrigins が通った分岐をオブジェクトごとに記録し、
//   結果と原因が1対1で対応する形で1ファイルに落とす。人の目視も往復も要らなくする。
//
// 【入力欄を置かない理由】
//   同じファイルを何度も使うため。MQO は直前の import が、CSV は直前の原点CSV読込が
//   RecentPaths に残しているので、それをそのまま使う。押すだけで走る。
//
// 【段の区切り】
//   コマンドはキュー経由で処理されるため、送信直後の状態は当てにならない。
//   PlayerPipelineTestSubPanel と同じく UIToolkit の schedule で 1 段ずつ間を空ける。
//   MonoBehaviour.Update は使わない。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Ops;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    /// <summary>原点CSV自動検証。人の操作は「テスト実行」を押すだけ。</summary>
    public class PlayerOriginTestSubPanel
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

        /// <summary>MQO を読み込む。実際の import 経路（ImportMqoCommand）へ流す。</summary>
        public Action<string> ImportMqo;

        // ================================================================
        // 定数
        // ================================================================

        /// <summary>MQO import が最後に使ったパスのキー（PlayerImportSubPanel と同じ規則）。</summary>
        private const string MqoPathKey = "Import.MQO.Path";

        /// <summary>原点CSVが最後に使ったパスのキー（PlayerBoneEditorSubPanel と同じ）。</summary>
        private const string CsvPathKey = "BoneEditor.OriginCsv.Path";

        /// <summary>段の本体からコマンド処理が落ち着くまでの待ち（ミリ秒）。</summary>
        private const long SettleMs = 400;

        /// <summary>ワールド位置が動いたと見なす閾値。</summary>
        private const float MoveEpsilon = 1e-4f;

        // ================================================================
        // 状態
        // ================================================================

        private VisualElement _root;
        private Button        _runButton;
        private Label         _statusLabel;
        private Label         _pathLabel;
        private ScrollView    _resultView;

        private bool   _running;
        private int    _stepIndex;
        private string _mqoPath = "";
        private string _csvPath = "";
        private string _reportPath = "";

        private readonly List<Func<bool>> _stages = new List<Func<bool>>();

        /// <summary>オートマップの結果（段3で埋め、段4以降で読む）。</summary>
        private HumanoidBoneMapping _mapping;
        private int                 _mapCandidates;

        /// <summary>ミラー分岐の計画（段3で作り、段4で読む）。</summary>
        private MirrorBranchPlan _branchPlan;
        private readonly List<int> _branchRoots = new List<int>();

        // ================================================================
        // UI
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            var title = new Label("原点CSV自動検証");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;
            _root.Add(title);

            var hint = new Label(
                "「テスト実行」を押すだけで、MQO 読込 → 原点CSV適用 →\n" +
                "Humanoid オートマップ → ミラー分岐の計画確認 → レポート書出まで流します。\n" +
                "対象は直前に使った MQO と原点CSV です。");
            hint.style.fontSize   = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            _runButton = new Button(StartRun) { text = "テスト実行" };
            _runButton.style.height = 32;
            _runButton.style.marginBottom = 4;
            _root.Add(_runButton);

            _pathLabel = new Label("");
            _pathLabel.style.fontSize = 10;
            _pathLabel.style.whiteSpace = WhiteSpace.Normal;
            _pathLabel.style.marginBottom = 2;
            _root.Add(_pathLabel);

            _statusLabel = new Label("待機中");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 4;
            _root.Add(_statusLabel);

            _resultView = new ScrollView();
            _resultView.style.flexGrow = 1;
            _resultView.style.minHeight = 200;
            _root.Add(_resultView);

            RefreshPathLabel();
        }

        public void Refresh()
        {
            if (_runButton != null) _runButton.SetEnabled(!_running);
            if (!_running) RefreshPathLabel();
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

        // ================================================================
        // 実行
        // ================================================================

        private void StartRun()
        {
            if (_running) return;

            _mqoPath = SafeGet(MqoPathKey);
            _csvPath = SafeGet(CsvPathKey);

            if (string.IsNullOrEmpty(_mqoPath) || !File.Exists(_mqoPath))
            {
                SetStatus("直前に読み込んだ MQO が見つかりません。一度 MQO を import してください。");
                return;
            }
            if (string.IsNullOrEmpty(_csvPath) || !File.Exists(_csvPath))
            {
                SetStatus("直前に使った原点CSVが見つかりません。一度「原点CSV読込」を実行してください。");
                return;
            }
            if (ImportMqo == null || SendCommand == null || GetModel == null)
            {
                SetStatus("配線が足りません（ImportMqo / SendCommand / GetModel）。");
                return;
            }

            _resultView.Clear();
            _stepIndex = 0;
            _running   = true;
            _runButton.SetEnabled(false);

            _reportPath = Path.Combine(
                Application.persistentDataPath, "PolyLing", "OriginTest",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"), "report.txt");

            BuildStages();
            SetStatus("実行中…");
            ScheduleNextStage();
        }

        private void BuildStages()
        {
            _stages.Clear();
            _stages.Add(StageImportMqo);
            _stages.Add(StageApplyCsv);
            _stages.Add(StageAutoMapHumanoid);
            _stages.Add(StageWriteReport);
        }

        private void ScheduleNextStage()
        {
            if (!_running || _root == null) return;

            if (_stepIndex >= _stages.Count) { Finish(); return; }

            var stage = _stages[_stepIndex];

            bool ok;
            try { ok = stage(); }
            catch (Exception e)
            {
                AddLine($"例外: {e.Message}", true);
                ObjectOriginDiag.Enabled = false;
                Finish();
                return;
            }

            if (!ok)
            {
                ObjectOriginDiag.Enabled = false;
                Finish();
                return;
            }

            _root.schedule.Execute(() =>
            {
                if (!_running) return;
                _stepIndex++;
                ScheduleNextStage();
            }).StartingIn(SettleMs);
        }

        private void Finish()
        {
            _running = false;
            if (_runButton != null) _runButton.SetEnabled(true);
        }

        // ================================================================
        // 段
        // ================================================================

        /// <summary>段1: MQO を実経路で読み込む。</summary>
        private bool StageImportMqo()
        {
            AddLine("■ MQO 読込");
            AddLine("    " + _mqoPath);
            ImportMqo(_mqoPath);
            return true;
        }

        /// <summary>段2: 原点CSV を読んで、UI ボタンと同じコマンドで適用する。</summary>
        private bool StageApplyCsv()
        {
            var model = GetModel();
            if (model == null) { AddLine("    モデルが読み込まれていない", true); return false; }

            AddLine("■ 原点CSV適用");
            AddLine($"    モデル \"{model.Name}\" / MeshContext {model.MeshContextCount} 件");

            if (!ParseCsv(_csvPath, out var names, out var positions, out string err))
            {
                AddLine("    CSV を読めなかった: " + err, true);
                return false;
            }
            AddLine($"    CSV {names.Length} 行");

            // 分岐の記録を有効にしてから送る。適用が終わるまで立てておく。
            ObjectOriginDiag.Enabled = true;

            SendCommand(new ApplyObjectOriginsCommand(
                GetModelIndex?.Invoke() ?? 0, names, positions, null));

            return true;
        }

        /// <summary>
        /// 段3: Humanoid オートマップと、ミラー分岐の計画確認。
        ///
        /// 候補はボーンだけでなく描画オブジェクトも含める。MQO を読んだ直後は
        /// ボーンが1本も無く、ボーンだけを候補にすると割当が 0 件になるため。
        /// HumanoidBoneMapping は「索引 = MeshContextList の索引」を前提にしているので、
        /// MeshContextCount の長さで各名前を自分の索引位置に置いたリストを渡す
        /// （PlayerHumanoidMappingSubPanel.GetBoneNames の「ボーン以外も含める」と同じ形）。
        ///
        /// 続けて MirrorBranchOps.BuildMirrorBranchPlan を作る。右半身は
        /// この計画として存在するので、割当済みノードがミラー枝に出る計画に
        /// なっているかを見る。ここでノードを増やしたり名前を作ったりはしない。
        /// </summary>
        private bool StageAutoMapHumanoid()
        {
            var model = GetModel();
            if (model == null) { AddLine("    モデルが無い", true); return false; }

            AddLine("■ Humanoid オートマップ");

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

            AddLine($"    候補 {_mapCandidates} 件 / 割当 {mapped} 件", mapped == 0);

            if (mapped > 0)
                SendCommand(new ApplyHumanoidMappingCommand(GetModelIndex?.Invoke() ?? 0, _mapping.Clone()));

            // ミラー分岐の計画。許容モードはスキンド変換の既定に合わせる。
            _branchRoots.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
                if (model.GetMeshContext(i)?.IsMirrorBranchRoot == true) _branchRoots.Add(i);

            _branchPlan = MirrorBranchOps.BuildMirrorBranchPlan(
                model, null, MirrorBranchTolerance.Tolerant);

            AddLine($"    ミラー分岐ルート {_branchRoots.Count} 件 / " +
                    $"ミラー枝に出る計画 {_branchPlan.CollectGeneratedMirrors().Count} 件",
                    _branchRoots.Count == 0);

            return true;
        }

        /// <summary>段4: 記録を検査してレポートを書く。</summary>
        private bool StageWriteReport()
        {
            var model = GetModel();
            ObjectOriginDiag.Enabled = false;

            var entries = ObjectOriginDiag.Entries;
            if (entries.Count == 0)
            {
                AddLine("    診断の記録が空。ApplyObjectOrigins が走っていない", true);
                return false;
            }

            var moved       = new List<ObjectOriginDiag.Entry>();
            var notApplied  = new List<ObjectOriginDiag.Entry>();
            var skipped     = new List<ObjectOriginDiag.Entry>();

            foreach (var e in entries)
            {
                if (e.VertexCount > 0 && e.MaxWorldDelta > MoveEpsilon) moved.Add(e);
                if (e.IsTarget && !e.UseLocalAfter) notApplied.Add(e);
                if (e.IsTarget && e.SkippedByMatrixCompare) skipped.Add(e);
            }

            var sb = new StringBuilder();
            WriteReport(sb, model, entries, moved, notApplied, skipped);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                AddLine("    レポートを書けなかった: " + e.Message, true);
                return false;
            }

            AddLine("■ 検査");
            AddLine($"    記録 {entries.Count} 件");
            AddLine($"    ワールド位置が動いた: {moved.Count} 件", moved.Count > 0);
            AddLine($"    CSV 行があるのに姿勢が入っていない: {notApplied.Count} 件", notApplied.Count > 0);
            AddLine($"    行列比較でスキップした適用先: {skipped.Count} 件", skipped.Count > 0);

            int show = Mathf.Min(10, moved.Count);
            for (int i = 0; i < show; i++)
            {
                var e = moved[i];
                AddLine($"      {e.Name} 索引={e.Index} ずれ={e.MaxWorldDelta:F6} " +
                        $"({e.WorldDeltaOfMax.x:F6}, {e.WorldDeltaOfMax.y:F6}, {e.WorldDeltaOfMax.z:F6}) " +
                        $"CSV={(e.InCsv ? "有" : "無")} 再局所化={(e.Relocalized ? "実行" : "未")}", true);
            }
            if (moved.Count > show) AddLine($"      … 他 {moved.Count - show} 件", true);

            // 未割当のうち、ミラー枝で解決される見込みのものを分ける。
            // スキンド変換前に右半身の関節ノードは存在しないので、
            // Right* が埋まっていないこと自体は不具合ではない。
            var missing  = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            var byPlan   = new List<string>();
            var unsolved = new List<string>();
            SplitMissingByBranchPlan(model, missing, byPlan, unsolved);

            AddLine("■ Humanoid");
            AddLine($"    割当 {_mapping?.Count ?? 0} 件 / 必須の未割当 {missing.Count} 件",
                    (_mapping?.Count ?? 0) == 0);
            AddLine($"    うちミラー枝で解決される見込み: {byPlan.Count} 件");
            AddLine($"    解決見込みが立たない: {unsolved.Count} 件", unsolved.Count > 0);

            AddLine("■ レポート");
            AddLine("    " + _reportPath);

            bool ok = moved.Count == 0 && notApplied.Count == 0 && unsolved.Count == 0;
            SetStatus(ok
                ? "合格。ワールド位置は保たれ、未割当はミラー枝で解決される見込みです。"
                : "不合格。レポートを確認してください。");
            return true;
        }

        // ================================================================
        // レポート本文
        // ================================================================

        private void WriteReport(
            StringBuilder sb, ModelContext model,
            List<ObjectOriginDiag.Entry> entries,
            List<ObjectOriginDiag.Entry> moved,
            List<ObjectOriginDiag.Entry> notApplied,
            List<ObjectOriginDiag.Entry> skipped)
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
            sb.AppendLine();

            sb.AppendLine("## 動いたオブジェクト");
            if (moved.Count == 0) sb.AppendLine("なし");
            foreach (var e in moved)
            {
                sb.AppendLine($"- [{e.Index}] {e.Name}");
                sb.AppendLine($"    種別={e.Type} 頂点={e.VertexCount} 親={e.HierarchyParentIndex}");
                sb.AppendLine($"    祖先={FormatAncestors(model, e)}");
                sb.AppendLine($"    CSV={(e.InCsv ? FormatV(e.CsvPosition) : "行なし")}" +
                              $" 適用先={e.IsTarget} 再局所化候補={e.InRelocalize}");
                sb.AppendLine($"    控えあり={e.HasStartWorld} 行列比較スキップ={e.SkippedByMatrixCompare}" +
                              $" 再局所化={e.Relocalized}");
                sb.AppendLine($"    姿勢 前={FormatV(e.PosBefore)} UseLocal={e.UseLocalBefore}" +
                              $" / 後={FormatV(e.PosAfter)} UseLocal={e.UseLocalAfter}");
                sb.AppendLine($"    ワールド前={ObjectOriginDiag.Format(e.WorldBefore)}");
                sb.AppendLine($"    ワールド後={ObjectOriginDiag.Format(e.WorldAfter)}");
                sb.AppendLine($"    最大ずれ={e.MaxWorldDelta:F6} {FormatV(e.WorldDeltaOfMax)}");
                sb.AppendLine($"    祖先原点の合計={FormatV(SumAncestorCsv(e, entries))}");
            }
            sb.AppendLine();

            sb.AppendLine("## 行列比較でスキップした適用先");
            sb.AppendLine("（原点が非ゼロなのにスキップされていれば、比較判定が誤っている）");
            if (skipped.Count == 0) sb.AppendLine("なし");
            foreach (var e in skipped)
            {
                sb.AppendLine($"- [{e.Index}] {e.Name}");
                sb.AppendLine($"    親={e.HierarchyParentIndex} 祖先={FormatAncestors(model, e)}");
                sb.AppendLine($"    CSV={FormatV(e.CsvPosition)}" +
                              $" 祖先原点の合計={FormatV(SumAncestorCsv(e, entries))}");
                sb.AppendLine($"    姿勢 前={FormatV(e.PosBefore)} UseLocal={e.UseLocalBefore}" +
                              $" / 後={FormatV(e.PosAfter)} UseLocal={e.UseLocalAfter}");
                sb.AppendLine($"    ワールド前={ObjectOriginDiag.Format(e.WorldBefore)}");
                sb.AppendLine($"    ワールド後={ObjectOriginDiag.Format(e.WorldAfter)}");
            }
            sb.AppendLine();

            sb.AppendLine("## CSV 行があるのに姿勢が入っていない");
            if (notApplied.Count == 0) sb.AppendLine("なし");
            foreach (var e in notApplied)
                sb.AppendLine($"- [{e.Index}] {e.Name} CSV={FormatV(e.CsvPosition)} 後={FormatV(e.PosAfter)}");
            sb.AppendLine();

            sb.AppendLine("## Humanoid オートマップ");
            sb.AppendLine($"候補 {_mapCandidates} 件（ボーン以外も含める）");
            sb.AppendLine($"割当 {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine($"アバター生成可 = {(_mapping?.CanCreateAvatar ?? false)}");
            sb.AppendLine();

            sb.AppendLine("### 割当");
            if (_mapping == null || _mapping.Count == 0) sb.AppendLine("なし");
            else
            {
                sb.AppendLine("Humanoid名\t索引\t名前\t種別\t頂点");
                foreach (var kv in _mapping.BoneIndexMap)
                {
                    var mc = (kv.Value >= 0 && kv.Value < (model?.MeshContextCount ?? 0))
                        ? model.GetMeshContext(kv.Value) : null;
                    sb.AppendLine(
                        $"{kv.Key}\t{kv.Value}\t{mc?.Name ?? "<範囲外>"}\t" +
                        $"{(mc != null ? mc.Type.ToString() : "-")}\t{mc?.MeshObject?.VertexCount ?? 0}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("### 必須ボーンの未割当");
            sb.AppendLine("（スキンド変換前は右半身の関節ノードが存在しない。");
            sb.AppendLine("  右半身は MirrorBranchOps.BuildMirrorBranchPlan の計画として持たれ、");
            sb.AppendLine("  MeshFilterToSkinnedConverter がボーンとして実体化する）");

            var miss2     = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            var byPlan2   = new List<string>();
            var unsolved2 = new List<string>();
            SplitMissingByBranchPlan(model, miss2, byPlan2, unsolved2);

            if (miss2.Count == 0) sb.AppendLine("なし");
            sb.AppendLine($"ミラー枝で解決される見込み: {byPlan2.Count} 件");
            foreach (var m in byPlan2) sb.AppendLine("- " + m);
            sb.AppendLine($"解決見込みが立たない: {unsolved2.Count} 件");
            foreach (var m in unsolved2) sb.AppendLine("- " + m);
            sb.AppendLine();

            sb.AppendLine("### ミラー分岐の計画");
            sb.AppendLine($"分岐ルート: {_branchRoots.Count} 件");
            foreach (int r in _branchRoots)
                sb.AppendLine($"- [{r}] {model?.GetMeshContext(r)?.Name}");

            if (_branchPlan != null)
            {
                var gen = _branchPlan.CollectGeneratedMirrors();
                sb.AppendLine($"ミラー枝に出す計画（形状を実体側から生成）: {gen.Count} 件");
                sb.AppendLine("索引\t名前\t頂点\t実体側に出す\tミラー枝に出す");
                for (int i = 0; i < (model?.MeshContextCount ?? 0); i++)
                {
                    if (!_branchPlan.TryGet(i, out var node)) continue;
                    if (!node.EmitMirror) continue;
                    var mc = model.GetMeshContext(i);
                    sb.AppendLine($"{i}\t{mc?.Name}\t{mc?.MeshObject?.VertexCount ?? 0}\t" +
                                  $"{node.EmitReal}\t{node.EmitMirror}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("### 割当先の重複・範囲外");
            {
                var used = new Dictionary<int, string>();
                bool bad = false;
                if (_mapping != null)
                {
                    foreach (var kv in _mapping.BoneIndexMap)
                    {
                        if (kv.Value < 0 || kv.Value >= (model?.MeshContextCount ?? 0))
                        {
                            sb.AppendLine($"- 範囲外: {kv.Key} → {kv.Value}");
                            bad = true;
                            continue;
                        }
                        if (used.TryGetValue(kv.Value, out string other))
                        {
                            sb.AppendLine($"- 重複: 索引 {kv.Value} に {other} と {kv.Key}");
                            bad = true;
                        }
                        else used[kv.Value] = kv.Key;
                    }
                }
                if (!bad) sb.AppendLine("なし");
            }
            sb.AppendLine();

            sb.AppendLine("## 全オブジェクト");
            sb.AppendLine("索引\t名前\t種別\t頂点\t親\tCSV\t適用先\t再局所化候補\tスキップ\t再局所化\t最大ずれ");
            foreach (var e in entries)
            {
                sb.AppendLine(
                    $"{e.Index}\t{e.Name}\t{e.Type}\t{e.VertexCount}\t{e.HierarchyParentIndex}\t" +
                    $"{(e.InCsv ? FormatV(e.CsvPosition) : "-")}\t{e.IsTarget}\t{e.InRelocalize}\t" +
                    $"{e.SkippedByMatrixCompare}\t{e.Relocalized}\t{e.MaxWorldDelta:F6}");
            }
        }

        /// <summary>
        /// 必須ボーンの未割当を「ミラー枝で解決される見込み」と「立たない」に分ける。
        ///
        /// 見込みが立つ条件は、左右を入れ替えた Humanoid 名が割り当たっていて、
        /// その割当先がミラー枝に出る計画（EmitMirror）になっていること。
        /// スキンド変換はこの計画に従ってミラー側ボーンを作り、
        /// SwapHumanoidLeftRight で左右を入れ替えた Humanoid 名を引き継ぐ
        /// （MeshFilterToSkinnedConverter の BonePlan）。
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

        /// <summary>祖先に入った CSV 原点の合計。ずれ量と突き合わせるために出す。</summary>
        private static Vector3 SumAncestorCsv(
            ObjectOriginDiag.Entry e, List<ObjectOriginDiag.Entry> all)
        {
            var byIndex = new Dictionary<int, ObjectOriginDiag.Entry>();
            foreach (var x in all) byIndex[x.Index] = x;

            Vector3 sum = Vector3.zero;
            if (e.Ancestors == null) return sum;

            foreach (int idx in e.Ancestors)
                if (byIndex.TryGetValue(idx, out var a) && a.InCsv) sum += a.CsvPosition;

            return sum;
        }

        // ================================================================
        // CSV
        // ================================================================

        /// <summary>
        /// 原点CSVを読む。規則は PlayerBoneEditorSubPanel.ImportObjectOriginsCsv と同じ。
        /// 回転列は読まない（この検証では位置だけを対象にする）。
        /// </summary>
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

        // ================================================================
        // 出力
        // ================================================================

        private void AddLine(string text, bool bad = false)
        {
            if (_resultView == null) return;

            var l = new Label(text);
            l.style.fontSize   = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.color = new StyleColor(bad
                ? new Color(1f, 0.45f, 0.45f)
                : new Color(0.75f, 1f, 0.75f));
            _resultView.Add(l);
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }
    }
}
