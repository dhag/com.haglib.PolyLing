// PlayerSkinTestSubPanel.cs
// スキン生成自動検証パネル。ボタン1つで MQO 読込 → 原点CSV適用 →
// メッシュからボーンとスキンの生成 → アバター用 Humanoid オートマップ →
// 検査 → レポート書出まで流す。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【原点CSV を先に通す理由】
//   MeshFilterToSkinnedConverter はボーンのローカル位置を
//   BoneTransform.Position から取る（BonePlan）。MQO はオブジェクトの
//   translation を持たないことがあり、その場合 Position はゼロのままなので、
//   原点CSV を通さずに変換すると全ボーンが原点に重なる。
//   関節位置を入れるのは原点CSV なので、実際の作業手順と同じ順で流す。
//
// 【原点CSV自動検証との違い】
//   あちらは原点適用そのものを検査する。こちらは適用済みの状態から
//   スキンド変換を掛け、ボーン生成とアバター割当までを検査する。
//
// 【入力欄を置かない理由】
//   同じファイルを何度も使うため。MQO は直前の import が RecentPaths に残しているので
//   それをそのまま使う。押すだけで走る。
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

namespace Poly_Ling.Player
{
    /// <summary>スキン生成自動検証。人の操作は「テスト実行」を押すだけ。</summary>
    public class PlayerSkinTestSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;
        public Action<string>       ImportMqo;

        // ================================================================
        // 定数
        // ================================================================

        /// <summary>MQO import が最後に使ったパスのキー（PlayerImportSubPanel と同じ規則）。</summary>
        private const string MqoPathKey = "Import.MQO.Path";

        /// <summary>原点CSVが最後に使ったパスのキー（PlayerBoneEditorSubPanel と同じ）。</summary>
        private const string CsvPathKey = "BoneEditor.OriginCsv.Path";

        /// <summary>スキンド変換がメッシュ名へ付ける接尾辞（MeshFilterToSkinnedConverter と同じ）。</summary>
        private const string MeshNameSuffix = "_skinned";

        /// <summary>段の本体からコマンド処理が落ち着くまでの待ち（ミリ秒）。</summary>
        private const long SettleMs = 600;

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
        private int    _csvRows;

        private readonly List<Func<bool>> _stages = new List<Func<bool>>();

        /// <summary>変換前の記録。名前をキーにする（索引は変換で動くため）。</summary>
        private sealed class Before
        {
            public string    Name = "";
            public string    Type = "";
            public int       VertexCount;
            public Vector3[] World;
        }

        private readonly List<Before> _before = new List<Before>();
        private int _beforeCount;

        private HumanoidBoneMapping _mapping;
        private int _mapCandidates;

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

            var title = new Label("スキン生成自動検証");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;
            _root.Add(title);

            var hint = new Label(
                "「テスト実行」を押すだけで、MQO 読込 → 原点CSV適用 →\n" +
                "ボーンとスキンの生成 → Humanoid オートマップ → レポート書出まで流します。\n" +
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
                SetStatus("直前に使った原点CSVが見つかりません。関節位置が入らないため実行しません。");
                return;
            }
            if (ImportMqo == null || SendCommand == null || GetModel == null)
            {
                SetStatus("配線が足りません（ImportMqo / SendCommand / GetModel）。");
                return;
            }

            _resultView.Clear();
            _before.Clear();
            _mapping   = null;
            _stepIndex = 0;
            _running   = true;
            _runButton.SetEnabled(false);

            _reportPath = Path.Combine(
                Application.persistentDataPath, "PolyLing", "SkinTest",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"), "report.txt");

            _stages.Clear();
            _stages.Add(StageImportMqo);
            _stages.Add(StageApplyOriginCsv);
            _stages.Add(StageCaptureBefore);
            _stages.Add(StageConvertSkinned);
            _stages.Add(StageAutoMapHumanoid);
            _stages.Add(StageWriteReport);

            SetStatus("実行中…");
            ScheduleNextStage();
        }

        private void ScheduleNextStage()
        {
            if (!_running || _root == null) return;
            if (_stepIndex >= _stages.Count) { Finish(); return; }

            var stage = _stages[_stepIndex];

            Debug.Log($"[SkinTest] 段 {_stepIndex + 1}/{_stages.Count} 開始");

            bool ok;
            try { ok = stage(); }
            catch (Exception e)
            {
                AddLine($"例外: {e.Message}", true);
                AddLine(e.StackTrace ?? "", true);
                WriteAbortReport($"段 {_stepIndex + 1} で例外: {e.Message}");
                Finish();
                return;
            }

            if (!ok)
            {
                WriteAbortReport($"段 {_stepIndex + 1} が中断を返した");
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
            Debug.Log("[SkinTest] 終了");
        }

        /// <summary>
        /// 途中で止まったときに、そこまでの内容と中断理由を書き出す。
        /// 「レポートが出ない」という状態を作らないための保険。
        /// </summary>
        private void WriteAbortReport(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PolyLing スキン生成自動検証レポート（中断）");
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("MQO: " + _mqoPath);
            sb.AppendLine("中断理由: " + reason);
            sb.AppendLine($"到達した段: {_stepIndex + 1} / {_stages.Count}");
            sb.AppendLine();

            var model = GetModel?.Invoke();
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine($"MeshContext 数: 変換前の記録 {_beforeCount} / 現在 {model?.MeshContextCount ?? 0}");
            sb.AppendLine($"頂点を控えたもの: {_before.Count} 件");
            sb.AppendLine($"Humanoid 割当: {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine();

            if (model != null)
            {
                sb.AppendLine("## 現在の全オブジェクト");
                sb.AppendLine("索引\t名前\t種別\t頂点\t親\tIsSkinned");
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null) continue;
                    sb.AppendLine($"{i}\t{mc.Name}\t{mc.Type}\t{mc.MeshObject?.VertexCount ?? 0}\t" +
                                  $"{mc.HierarchyParentIndex}\t{mc.IsSkinned}");
                }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
                AddLine("■ レポート（中断）", true);
                AddLine("    " + _reportPath, true);
                SetStatus("中断しました。レポートを確認してください。");
            }
            catch (Exception e)
            {
                AddLine("    中断レポートも書けなかった: " + e.Message, true);
                SetStatus("中断しました。レポートの書き出しにも失敗しました。");
            }
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

        /// <summary>
        /// 段2: 原点CSVを適用する。UI ボタンと同じコマンド経路へ流す。
        /// 関節位置（BoneTransform.Position）はこれで入る。
        /// </summary>
        private bool StageApplyOriginCsv()
        {
            var model = GetModel();
            if (model == null) { AddLine("    モデルが読み込まれていない", true); return false; }

            AddLine("■ 原点CSV適用");
            AddLine("    " + _csvPath);

            if (!ParseCsv(_csvPath, out var names, out var positions, out string err))
            {
                AddLine("    CSV を読めなかった: " + err, true);
                return false;
            }

            _csvRows = names.Length;
            AddLine($"    CSV {_csvRows} 行");

            SendCommand(new ApplyObjectOriginsCommand(
                GetModelIndex?.Invoke() ?? 0, names, positions, null));

            return true;
        }

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

        /// <summary>
        /// 段3: 変換前の状態を控える。
        /// 索引はスキンド変換でボーンが挿入されて動くので、名前をキーにする。
        /// </summary>
        private bool StageCaptureBefore()
        {
            var model = GetModel();
            if (model == null) { AddLine("    モデルが読み込まれていない", true); return false; }

            model.ComputeWorldMatrices();

            _beforeCount = model.MeshContextCount;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var wm = mc.WorldMatrix;
                var world = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++)
                    world[v] = wm.MultiplyPoint3x4(mo.Vertices[v].Position);

                _before.Add(new Before
                {
                    Name        = mc.Name ?? "",
                    Type        = mc.Type.ToString(),
                    VertexCount = mo.Vertices.Count,
                    World       = world,
                });
            }

            AddLine("■ 変換前の記録");
            AddLine($"    MeshContext {_beforeCount} 件 / 頂点を控えたもの {_before.Count} 件");
            return true;
        }

        /// <summary>段3: メッシュからボーンとスキンを生成する（実経路）。</summary>
        private bool StageConvertSkinned()
        {
            AddLine("■ ボーンとスキンの生成");
            SendCommand(new ConvertMeshFilterToSkinnedCommand(GetModelIndex?.Invoke() ?? 0));
            return true;
        }

        /// <summary>
        /// 段4: アバター用 Humanoid オートマップ。
        /// 変換でボーンが生成されるが、ボーン以外も候補に含めておく
        /// （変換が期待どおりに走らなかった場合でも割当の様子が判るようにするため）。
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

            return true;
        }

        /// <summary>段5: 検査してレポートを書く。</summary>
        private bool StageWriteReport()
        {
            var model = GetModel();
            if (model == null) { AddLine("    モデルが無い", true); return false; }

            model.ComputeWorldMatrices();

            // 名前 → 変換後の索引（重複名は先着）。
            //
            // スキンド変換の命名は次のとおり（MeshFilterToSkinnedConverter）。
            //   ボーン … 元のオブジェクト名をそのまま継ぐ
            //   メッシュ … 元の名前 + "_skinned"
            // ボーンを除いたうえで、変換前の名前に接尾辞を付けて引く。
            var afterByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (!afterByName.ContainsKey(mc.Name)) afterByName[mc.Name] = i;
            }

            var moved      = new List<(Before b, int idx, float delta, Vector3 d)>();
            var lost       = new List<Before>();
            var countDiff  = new List<Before>();

            foreach (var b in _before)
            {
                int idx;
                if (string.IsNullOrEmpty(b.Name) ||
                    !(afterByName.TryGetValue(b.Name + MeshNameSuffix, out idx) ||
                      afterByName.TryGetValue(b.Name, out idx)))
                { lost.Add(b); continue; }

                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) { lost.Add(b); continue; }

                if (mo.Vertices.Count != b.VertexCount) { countDiff.Add(b); continue; }

                var wm = mc.WorldMatrix;
                float max = 0f; Vector3 dmax = Vector3.zero;
                for (int v = 0; v < b.VertexCount; v++)
                {
                    Vector3 now = wm.MultiplyPoint3x4(mo.Vertices[v].Position);
                    Vector3 d   = now - b.World[v];
                    float   m   = d.magnitude;
                    if (m > max) { max = m; dmax = d; }
                }
                if (max > MoveEpsilon) moved.Add((b, idx, max, dmax));
            }

            // 種別内訳とウェイトの整合
            var typeCount   = new Dictionary<string, int>();
            var bones       = new List<int>();
            var weightOnly  = new List<int>();   // 種別が Skinned でないのにウェイト有り
            var kindOnly    = new List<int>();   // 種別 Skinned なのにウェイト無し

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                string t = mc.Type.ToString();
                typeCount[t] = typeCount.TryGetValue(t, out int c) ? c + 1 : 1;
                if (mc.Type == MeshType.Bone) bones.Add(i);

                var mo = mc.MeshObject;
                if (mo == null || mo.Vertices == null) continue;

                bool anyWeight = false;
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    if (mo.Vertices[v] != null && mo.Vertices[v].HasBoneWeight) { anyWeight = true; break; }
                }

                if (!mc.IsSkinned && anyWeight) weightOnly.Add(i);
                else if (mc.IsSkinned && !anyWeight && mo.Vertices.Count > 0) kindOnly.Add(i);
            }

            // ボーンのローカル位置がゼロのものを数える。
            // 全ボーンがゼロだと関節が原点に重なり、Unity のアバターは成立しない。
            // 原点CSV を通していれば関節位置が入っているはずなので、ここが多いと
            // 「原点が入っていない状態で変換した」ことになる。
            var zeroBones = new List<int>();
            foreach (int i in bones)
            {
                var bt = model.GetMeshContext(i)?.BoneTransform;
                Vector3 p = bt?.Position ?? Vector3.zero;
                if (p.sqrMagnitude <= 1e-12f) zeroBones.Add(i);
            }

            var sb = new StringBuilder();
            WriteReport(sb, model, moved, lost, countDiff, typeCount, bones, weightOnly, kindOnly, zeroBones);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { AddLine("    レポートを書けなかった: " + e.Message, true); return false; }

            var missing = _mapping?.GetMissingRequiredBones() ?? new List<string>();

            AddLine("■ 検査");
            AddLine($"    MeshContext {_beforeCount} → {model.MeshContextCount} 件");
            AddLine($"    生成されたボーン: {bones.Count} 本", bones.Count == 0);
            AddLine($"    ワールド位置が動いた: {moved.Count} 件", moved.Count > 0);
            AddLine($"    変換後に見つからない: {lost.Count} 件", lost.Count > 0);
            AddLine($"    頂点数が変わった: {countDiff.Count} 件", countDiff.Count > 0);
            AddLine($"    種別とウェイトの食い違い: {weightOnly.Count + kindOnly.Count} 件",
                    weightOnly.Count + kindOnly.Count > 0);
            AddLine($"    ローカル位置がゼロのボーン: {zeroBones.Count} / {bones.Count} 本",
                    bones.Count > 0 && zeroBones.Count == bones.Count);

            int show = Mathf.Min(10, moved.Count);
            for (int i = 0; i < show; i++)
            {
                var m = moved[i];
                AddLine($"      {m.b.Name} 索引={m.idx} ずれ={m.delta:F6} " +
                        $"({m.d.x:F6}, {m.d.y:F6}, {m.d.z:F6})", true);
            }
            if (moved.Count > show) AddLine($"      … 他 {moved.Count - show} 件", true);

            AddLine("■ Humanoid");
            AddLine($"    割当 {_mapping?.Count ?? 0} 件 / 必須の未割当 {missing.Count} 件",
                    (_mapping?.Count ?? 0) == 0);
            AddLine($"    アバター生成可={(_mapping?.CanCreateAvatar ?? false)}",
                    !(_mapping?.CanCreateAvatar ?? false));

            AddLine("■ レポート");
            AddLine("    " + _reportPath);

            bool ok = moved.Count == 0 && lost.Count == 0 && countDiff.Count == 0
                   && bones.Count > 0 && zeroBones.Count < bones.Count
                   && (_mapping?.CanCreateAvatar ?? false);
            SetStatus(ok
                ? "合格。位置が保たれ、ボーン生成とアバター割当がそろっています。"
                : "不合格。レポートを確認してください。");
            return true;
        }

        // ================================================================
        // レポート本文
        // ================================================================

        private void WriteReport(
            StringBuilder sb, ModelContext model,
            List<(Before b, int idx, float delta, Vector3 d)> moved,
            List<Before> lost, List<Before> countDiff,
            Dictionary<string, int> typeCount, List<int> bones,
            List<int> weightOnly, List<int> kindOnly, List<int> zeroBones)
        {
            sb.AppendLine("# PolyLing スキン生成自動検証レポート");
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("MQO: " + _mqoPath);
            sb.AppendLine("CSV: " + _csvPath + $"（{_csvRows} 行）");
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine($"MeshContext 数: 変換前 {_beforeCount} → 変換後 {model?.MeshContextCount ?? 0}");
            sb.AppendLine();

            sb.AppendLine("## 判定");
            sb.AppendLine($"生成されたボーン: {bones.Count} 本");
            sb.AppendLine($"ワールド位置が動いた: {moved.Count} 件（閾値 {MoveEpsilon}）");
            sb.AppendLine($"変換後に見つからない: {lost.Count} 件");
            sb.AppendLine($"頂点数が変わった: {countDiff.Count} 件");
            sb.AppendLine($"種別が Skinned でないのにウェイト有り: {weightOnly.Count} 件");
            sb.AppendLine($"種別 Skinned なのにウェイト無し: {kindOnly.Count} 件");
            sb.AppendLine($"ローカル位置がゼロのボーン: {zeroBones.Count} / {bones.Count} 本");
            sb.AppendLine($"Humanoid 割当: {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine($"アバター生成可: {(_mapping?.CanCreateAvatar ?? false)}");
            sb.AppendLine();

            sb.AppendLine("## 種別内訳（変換後）");
            foreach (var kv in typeCount) sb.AppendLine($"{kv.Key}\t{kv.Value}");
            sb.AppendLine();

            sb.AppendLine("## 動いたオブジェクト");
            if (moved.Count == 0) sb.AppendLine("なし");
            foreach (var m in moved)
            {
                var mc = model.GetMeshContext(m.idx);
                sb.AppendLine($"- {m.b.Name} 索引={m.idx}");
                sb.AppendLine($"    種別 前={m.b.Type} / 後={(mc != null ? mc.Type.ToString() : "-")}");
                sb.AppendLine($"    親={mc?.HierarchyParentIndex ?? -1} " +
                              $"（{ParentName(model, mc)}）");
                sb.AppendLine($"    最大ずれ={m.delta:F6} ({m.d.x:F6}, {m.d.y:F6}, {m.d.z:F6})");
            }
            sb.AppendLine();

            sb.AppendLine("## 変換後に見つからない");
            if (lost.Count == 0) sb.AppendLine("なし");
            foreach (var b in lost) sb.AppendLine($"- {b.Name} 種別={b.Type} 頂点={b.VertexCount}");
            sb.AppendLine();

            sb.AppendLine("## 頂点数が変わった");
            if (countDiff.Count == 0) sb.AppendLine("なし");
            foreach (var b in countDiff)
            {
                // 上の afterByName と同じ規則で引く（ボーンを除く／接尾辞つき優先）。
                int idx = -1;
                if (!string.IsNullOrEmpty(b.Name))
                {
                    string want = b.Name + MeshNameSuffix;
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var c = model.GetMeshContext(i);
                        if (c == null || c.Type == MeshType.Bone) continue;
                        if (c.Name == want || c.Name == b.Name) { idx = i; break; }
                    }
                }
                int after = (idx >= 0) ? (model.GetMeshContext(idx)?.MeshObject?.VertexCount ?? 0) : 0;
                sb.AppendLine($"- {b.Name} 前={b.VertexCount} 後={after}");
            }
            sb.AppendLine();

            sb.AppendLine("## 種別とウェイトの食い違い");
            if (weightOnly.Count == 0 && kindOnly.Count == 0) sb.AppendLine("なし");
            foreach (int i in weightOnly)
                sb.AppendLine($"- ウェイト有りだが種別が Skinned でない: [{i}] {model.GetMeshContext(i)?.Name}");
            foreach (int i in kindOnly)
                sb.AppendLine($"- 種別 Skinned だがウェイト無し: [{i}] {model.GetMeshContext(i)?.Name}");
            sb.AppendLine();

            sb.AppendLine("## ローカル位置がゼロのボーン");
            sb.AppendLine("（全ボーンがゼロなら関節が原点に重なる。原点CSV が入っていない状態で");
            sb.AppendLine("  変換したときに起きる。Unity のアバターはこの状態では成立しない）");
            if (zeroBones.Count == 0) sb.AppendLine("なし");
            else if (zeroBones.Count == bones.Count) sb.AppendLine("全ボーンがゼロ");
            else
                foreach (int i in zeroBones)
                    sb.AppendLine($"- [{i}] {model.GetMeshContext(i)?.Name}");
            sb.AppendLine();

            sb.AppendLine("## 生成されたボーン");
            if (bones.Count == 0) sb.AppendLine("なし");
            else
            {
                sb.AppendLine("索引\t名前\t親\t親の名前\tローカル位置");
                foreach (int i in bones)
                {
                    var mc = model.GetMeshContext(i);
                    Vector3 p = mc?.BoneTransform?.Position ?? Vector3.zero;
                    sb.AppendLine($"{i}\t{mc?.Name}\t{mc?.HierarchyParentIndex ?? -1}\t" +
                                  $"{ParentName(model, mc)}\t({p.x:F6}, {p.y:F6}, {p.z:F6})");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Humanoid オートマップ");
            sb.AppendLine($"候補 {_mapCandidates} 件（ボーン以外も含める）");
            sb.AppendLine($"割当 {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine();

            sb.AppendLine("### 割当");
            if (_mapping == null || _mapping.Count == 0) sb.AppendLine("なし");
            else
            {
                sb.AppendLine("Humanoid名\t索引\t名前\t種別");
                foreach (var kv in _mapping.BoneIndexMap)
                {
                    var mc = (kv.Value >= 0 && kv.Value < model.MeshContextCount)
                        ? model.GetMeshContext(kv.Value) : null;
                    sb.AppendLine($"{kv.Key}\t{kv.Value}\t{mc?.Name ?? "<範囲外>"}\t" +
                                  $"{(mc != null ? mc.Type.ToString() : "-")}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("### 必須ボーンの未割当");
            var miss = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            if (miss.Count == 0) sb.AppendLine("なし");
            foreach (var m in miss) sb.AppendLine("- " + m);
            sb.AppendLine();

            sb.AppendLine("## 全オブジェクト（変換後）");
            sb.AppendLine("索引\t名前\t種別\t頂点\t親\tIsSkinned");
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                sb.AppendLine($"{i}\t{mc.Name}\t{mc.Type}\t{mc.MeshObject?.VertexCount ?? 0}\t" +
                              $"{mc.HierarchyParentIndex}\t{mc.IsSkinned}");
            }
        }

        private static string ParentName(ModelContext model, MeshContext mc)
        {
            int p = mc?.HierarchyParentIndex ?? -1;
            if (p < 0 || p >= (model?.MeshContextCount ?? 0)) return "ルート";
            return model.GetMeshContext(p)?.Name ?? "?";
        }

        // ================================================================
        // 出力
        // ================================================================

        private void AddLine(string text, bool bad = false)
        {
            // パネルを閉じていても追えるように Console へも出す。
            // どの段で止まったかが分からない、という状態を作らないため。
            if (bad) Debug.LogWarning("[SkinTest] " + text);
            else     Debug.Log("[SkinTest] " + text);

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
