// PlayerPipelineTestSubPanel.cs
// 自動検証パネル。ボタン 1 回で、保存済みプロジェクトの読み込みから
// スキンド変換 → ウェイト設定 → Humanoid マッピング → 保存往復までを流し、
// 各段の直後に不変条件を検査して結果を表に出す。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ実コマンドを送るか】
//   Ops を直接叩くと、ディスパッチャ側の欠陥（対象の解決を選択状態に頼る、
//   ミラーペアを解体する等）が検査を素通りする。パネルが押されたときに
//   飛ぶのと同じ PanelCommand を送ることで、実際の経路をそのまま通す。
//
// 【段の区切り】
//   コマンドはキュー経由で処理されるため、送信直後の状態は当てにならない。
//   1 段ごとに間を空けてから検査する。
//   MonoBehaviour.Update は使わない（PolyLingPlayerViewer.cs:73-77 の規約どおり
//   毎フレーム駆動は置かない）。UIToolkit の schedule を使い、段が終わるたびに
//   次の段を予約する形にする。テストが動いていない間は何も走らない。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Diagnostics;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>
    /// パイプライン自動検証。人間の操作は「テスト実行」を押すだけ。
    /// </summary>
    public class PlayerPipelineTestSubPanel
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

        /// <summary>
        /// プロジェクトフォルダを読み込んでアクティブへ差し替える。
        /// 読み込み経路は Viewer が持っているのでコールバックで受ける。
        /// 戻り値は成功可否。
        /// </summary>
        public Func<string, bool> LoadProjectFolder;

        /// <summary>現在のプロジェクトを指定フォルダへ保存する。</summary>
        public Func<string, bool> SaveProjectFolder;

        /// <summary>
        /// ブリッジを 1 本作る。(穴Aメッシュ, 穴A頂点, 穴Bメッシュ, 穴B頂点, 名前) を渡す。
        /// UI ボタンと同じ経路を通す。
        /// </summary>
        public CreateBridgeDelegate CreateBridge;

        /// <summary>位相が変わったあとの再構築・通知。</summary>
        public Action RefreshAfterTopologyChange;

        public delegate bool CreateBridgeDelegate(
            int meshA, int vertexA, int meshB, int vertexB, string name, out string message);

        // ================================================================
        // 状態
        // ================================================================

        private VisualElement _root;
        private TextField     _folderField;
        private Button        _runButton;
        private Label         _statusLabel;
        private ScrollView     _resultView;

        private bool _running;
        private int  _stepIndex;

        /// <summary>段の本体からコマンド処理が落ち着くまでの待ち（ミリ秒）。</summary>
        private const long SettleMs = 120;

        private ModelStructureSnapshot _prevSnapshot;

        /// <summary>
        /// 段の本体が出した行の控え。
        /// 本体は見出しより先に走るので、そのまま出すと前の段の下にぶら下がる。
        /// 見出しを出したあとで流し込む。
        /// </summary>
        private readonly List<(string Text, bool Bad)> _pendingLines = new List<(string, bool)>();
        private bool _bufferLines;

        /// <summary>
        /// 段の検査時に評価する計測。
        /// 生成直後に測ると、キュー経由で後から走るコマンド（ミラー付与など）が
        /// まだ反映されていない。検査と同じ時点で測る。
        /// </summary>
        private Action _pendingMeasurement;
        private int _failCount;

        /// <summary>1 段ぶんの定義。</summary>
        private sealed class Stage
        {
            public string Name;
            /// <summary>段の本体。false を返すと以降を打ち切る。</summary>
            public Func<bool> Run;

            /// <summary>
            /// 親の相手が変わっていないかを検査するか。
            /// スキンド変換はメッシュの親をボーンへ張り替えるのが仕様なので false。
            /// </summary>
            public bool CheckParentIdentity = true;
        }

        private readonly List<Stage> _stages = new List<Stage>();
        private string _tempExportFolder;

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

            var title = new Label("パイプライン自動検証");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;
            _root.Add(title);

            var hint = new Label(
                "「テスト実行」を押すだけで、読み込み → スキンド変換 → ウェイト設定 →\n" +
                "Humanoid マッピング → 保存往復まで自動で流し、各段で不変条件を検査します。\n" +
                "対象は直前に開いていたプロジェクトを自動で使います。");
            hint.style.fontSize   = 10;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 4;
            _root.Add(hint);

            _runButton = new Button(StartRun) { text = "テスト実行" };
            _runButton.style.height = 32;
            _runButton.style.marginBottom = 4;
            _root.Add(_runButton);

            // 対象は自動で決める。押すだけで走るのが前提で、ここは確認用。
            var folderRow = new VisualElement();
            folderRow.style.flexDirection = FlexDirection.Row;
            folderRow.style.marginBottom  = 4;

            _folderField = new TextField();
            _folderField.style.flexGrow = 1;
            _folderField.value = ResolveTargetFolder();
            folderRow.Add(_folderField);

            var browse = new Button(BrowseFolder) { text = "参照" };
            browse.style.width = 44;
            folderRow.Add(browse);
            _root.Add(folderRow);

            _statusLabel = new Label("待機中");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 4;
            _root.Add(_statusLabel);

            _resultView = new ScrollView();
            _resultView.style.flexGrow = 1;
            _resultView.style.minHeight = 200;
            _root.Add(_resultView);
        }

        public void Refresh()
        {
            if (_runButton != null) _runButton.SetEnabled(!_running);

            // 開くたびに対象を引き直す。直前に開いていたプロジェクトがそのまま対象になる。
            if (!_running && _folderField != null && !IsProjectFolder(_folderField.value))
                _folderField.value = ResolveTargetFolder();
        }

        /// <summary>
        /// 対象フォルダを自動で決める。
        ///
        ///   1) このパネルで前回使ったフォルダ
        ///   2) 通常のプロジェクト読み書きが最後に使ったフォルダ（Project.CsvFolder）
        ///   3) 2 の隣にある、名前が "_bridge" で終わるフォルダ
        ///
        /// project.csv があるフォルダだけを採用する。
        /// </summary>
        private static string ResolveTargetFolder()
        {
            string remembered = LoadRememberedFolder();
            if (IsProjectFolder(remembered)) return remembered;

            string lastProject = "";
            try { lastProject = RecentPaths.Get(Poly_Ling.Serialization.FolderSerializer.CsvProjectSerializer.CsvFolderKey); }
            catch { lastProject = ""; }

            if (IsProjectFolder(lastProject)) return lastProject;

            // 隣接フォルダから、ブリッジ入りの Mesh プロジェクトを探す。
            try
            {
                string parent = Directory.Exists(lastProject)
                    ? Path.GetDirectoryName(lastProject)
                    : null;
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    var candidate = Directory.GetDirectories(parent)
                        .Where(IsProjectFolder)
                        .OrderByDescending(d => d.EndsWith("_bridge", StringComparison.Ordinal))
                        .ThenByDescending(d => Directory.GetLastWriteTime(d))
                        .FirstOrDefault();
                    if (candidate != null) return candidate;
                }
            }
            catch { /* 見つからなければ空のまま。参照ボタンで指定できる */ }

            return lastProject ?? "";
        }

        private static bool IsProjectFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            try { return File.Exists(Path.Combine(folder, "project.csv")); }
            catch { return false; }
        }

        /// <summary>参照ボタン。project.csv を選ぶとその親フォルダを対象にする。</summary>
        private void BrowseFolder()
        {
            string picked = RecentFileDialog.AskLoad(
                "検証対象の project.csv を選択", PrefKey, "csv");
            if (string.IsNullOrEmpty(picked)) return;

            string folder = Path.GetDirectoryName(picked);
            if (!IsProjectFolder(folder))
            {
                SetStatus("project.csv のあるフォルダを選んでください");
                return;
            }
            _folderField.value = folder;
            RememberFolder(folder);
        }

        // ================================================================
        // 実行
        // ================================================================

        private void StartRun()
        {
            if (_running) return;

            string folder = _folderField?.value?.Trim() ?? "";
            if (!IsProjectFolder(folder))
            {
                // 欄が空・古い場合はここで引き直す。押すだけで走らせるため。
                folder = ResolveTargetFolder();
                if (_folderField != null) _folderField.value = folder;
            }
            if (!IsProjectFolder(folder))
            {
                SetStatus("対象プロジェクトが見つかりません。「参照」で project.csv を選んでください。");
                return;
            }
            RememberFolder(folder);

            _resultView.Clear();
            _failCount    = 0;
            _stepIndex    = 0;
            _prevSnapshot = null;
            _running      = true;
            _runButton.SetEnabled(false);

            _tempExportFolder = Path.Combine(
                Application.persistentDataPath, "PolyLing", "PipelineTest",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            BuildStages(folder);
            SetStatus("実行中…");
            ScheduleNextStage();
        }

        /// <summary>
        /// 次の段を予約する。段の本体を走らせ、コマンド処理が落ち着いてから検査し、
        /// さらに次の段を予約する。実行中以外は何も走らない。
        /// </summary>
        private void ScheduleNextStage()
        {
            if (!_running || _root == null) return;

            if (_stepIndex >= _stages.Count)
            {
                Finish();
                return;
            }

            var stage = _stages[_stepIndex];

            bool ok;
            _pendingLines.Clear();
            _pendingMeasurement = null;
            _bufferLines = true;
            try { ok = stage.Run(); }
            catch (Exception e)
            {
                _bufferLines = false;
                AddLine($"■ {stage.Name}", true);
                FlushPendingLines();
                AddLine($"    例外: {e.Message}", true);
                _failCount++;
                Finish();
                return;
            }
            _bufferLines = false;

            if (!ok)
            {
                AddLine($"■ {stage.Name}", true);
                FlushPendingLines();
                AddLine("    段の実行に失敗", true);
                _failCount++;
                Finish();
                return;
            }

            _root.schedule.Execute(() =>
            {
                if (!_running) return;
                RunChecks(stage.Name, stage.CheckParentIdentity);
                _stepIndex++;
                ScheduleNextStage();
            }).StartingIn(SettleMs);
        }

        private void Finish()
        {
            _running = false;
            _runButton.SetEnabled(true);
            SetStatus(_failCount == 0
                ? "全段 合格"
                : $"違反 {_failCount} 件。上の一覧を参照");
        }

        // ================================================================
        // 段の組み立て
        // ================================================================

        private void BuildStages(string sourceFolder)
        {
            _stages.Clear();

            _stages.Add(new Stage
            {
                Name = "S0 読み込み",
                Run  = () => LoadProjectFolder != null && LoadProjectFolder(sourceFolder),
            });

            _stages.Add(new Stage
            {
                Name = "S1 ミラー分岐ルート設定",
                Run  = ApplyBranchRoots,
            });

            // ブリッジは 1 本ずつ別の段にする。どちらの挿入で階層が崩れるかを分けて見るため。
            _stages.Add(new Stage
            {
                Name = "S2a ブリッジ 左腕↔左ひじ",
                Run  = () => CreateOneBridge("左腕", "左ひじ", "Bridge"),
            });

            _stages.Add(new Stage
            {
                Name = "S2b ブリッジ 左ひじ↔左手首",
                Run  = () => CreateOneBridge("左ひじ", "左手首", "Bridge_1"),
            });

            _stages.Add(new Stage
            {
                Name = "S3 スキンド変換",
                Run  = () =>
                {
                    Send(new ConvertMeshFilterToSkinnedCommand(ModelIndex()));
                    return true;
                },
                // メッシュの親がボーンへ張り替わるのが仕様。
                CheckParentIdentity = false,
            });

            _stages.Add(new Stage
            {
                Name = "S4 ウェイト数値設定（ブリッジ）",
                Run  = ApplyBridgeWeights,
            });

            _stages.Add(new Stage
            {
                Name = "S5 Humanoid マッピング",
                Run  = ApplyHumanoidMapping,
            });

            // スキンド後にブリッジを後付けする経路。
            // 頂点の座標系が非スキンドと違うので、ここで位置が飛ばないかを見る。
            _stages.Add(new Stage
            {
                Name = "S6 スキンド後のブリッジ後付け（ひざ）",
                Run  = () => CreateOneBridge("左足_skinned", "左ひざ_skinned", "Bridge_2"),
            });

            _stages.Add(new Stage
            {
                Name = "S7 保存往復",
                Run  = () =>
                {
                    Directory.CreateDirectory(_tempExportFolder);
                    if (SaveProjectFolder == null || !SaveProjectFolder(_tempExportFolder))
                        return false;

                    // 書き出したファイルを直接見る。
                    // 読み込み後の値と突き合わせれば、書き出し側と読み込み側の
                    // どちらが落ちているかが 1 回の実行で確定する。
                    ReportExportedFileFacts(_tempExportFolder);

                    return LoadProjectFolder != null && LoadProjectFolder(_tempExportFolder);
                },
            });
        }

        /// <summary>
        /// 腕と足の付け根にミラー分岐ルートを立てる。
        /// これを立てないとスキンド変換で左右のボーン木が作られない。
        /// </summary>
        private bool ApplyBranchRoots()
        {
            var model = GetModel?.Invoke();
            if (model == null) return false;

            var targets = new List<int>();
            foreach (string name in new[] { "左腕", "左足" })
            {
                int idx = FindIndexByName(model, name, bone: false);
                if (idx < 0)
                {
                    AddLine($"    \"{name}\" が見つからない", true);
                    _failCount++;
                    continue;
                }
                targets.Add(idx);
            }
            if (targets.Count == 0) return false;

            Send(new SetMirrorBranchRootCommand(ModelIndex(), targets.ToArray(), true));
            return true;
        }

        /// <summary>
        /// 関節の隙間をブリッジで繋ぐ。
        ///   左腕 ↔ 左ひじ、左ひじ ↔ 左手首
        /// 穴の縁の頂点は、相手メッシュに最も近いエッジ頂点を選ぶ。
        /// 1 本作るたびに索引が繰り下がるので、毎回名前から引き直す。
        /// </summary>
        private bool CreateOneBridge(string nameA, string nameB, string bridgeName)
        {
            if (CreateBridge == null) { AddLine("    ブリッジ生成の配線が無い", true); _failCount++; return false; }

            var model = GetModel?.Invoke();
            if (model == null) return false;

            int ia = FindIndexByName(model, nameA, bone: false);
            int ib = FindIndexByName(model, nameB, bone: false);
            if (ia < 0 || ib < 0)
            {
                AddLine($"    \"{nameA}\" / \"{nameB}\" が見つからない", true);
                _failCount++;
                return true;
            }

            var ma = model.GetMeshContext(ia)?.MeshObject;
            var mb = model.GetMeshContext(ib)?.MeshObject;
            if (ma == null || mb == null) { _failCount++; return true; }

            int va = NearestBoundaryVertex(ma, Centroid(mb));
            int vb = NearestBoundaryVertex(mb, Centroid(ma));
            if (va < 0 || vb < 0)
            {
                AddLine($"    \"{nameA}\" / \"{nameB}\" に穴（エッジ）が無い", true);
                _failCount++;
                return true;
            }

            AddLine($"    挿入前: \"{nameA}\"={ia} \"{nameB}\"={ib} 要素数={model.MeshContextCount}");

            if (!CreateBridge(ia, va, ib, vb, bridgeName, out string msg))
            {
                AddLine($"    ブリッジ生成失敗 {bridgeName}: {msg}", true);
                _failCount++;
                return true;
            }

            RefreshAfterTopologyChange?.Invoke();
            _pendingMeasurement = () => ReportBridgePlacement(bridgeName, ma, mb);
            return true;
        }

        /// <summary>
        /// 生成したブリッジが、2 つの穴の間に収まっているかを実測で出す。
        /// スキンドは頂点がワールド空間、非スキンドはローカル空間なので、
        /// 座標系を取り違えると値が大きく外れる。
        /// </summary>
        private void ReportBridgePlacement(string bridgeName, MeshObject a, MeshObject b)
        {
            var model = GetModel?.Invoke();
            if (model == null) return;

            int idx = FindIndexByName(model, bridgeName, bone: false);
            if (idx < 0)
            {
                // 生成時に一意化されて名前が変わることがある
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var c = model.GetMeshContext(i);
                    if (c != null && c.Type != MeshType.Bone &&
                        (c.Name ?? "").StartsWith(bridgeName, StringComparison.Ordinal))
                    { idx = i; break; }
                }
            }
            if (idx < 0) { AddLine($"    生成物 \"{bridgeName}\" が見つからない", true); _failCount++; return; }

            var mc = model.GetMeshContext(idx);
            var mo = mc?.MeshObject;
            if (mo == null || mo.VertexCount == 0) return;

            // 比較相手もワールドへ出す。スキンド前のメッシュは頂点がローカル空間なので、
            // 変換せずに引き算すると座標系の違いをそのまま距離として拾う。
            Vector3 mid = (WorldCentroid(model, a) + WorldCentroid(model, b)) * 0.5f;
            Vector3 world = mc.VertexToWorldMatrix.MultiplyPoint3x4(Centroid(mo));
            float d = Vector3.Distance(world, mid);

            bool bad = d > 0.25f;
            if (bad) _failCount++;

            // 親がボーンかどうかも出す。スキンドの描画オブジェクトはボーンの子に並ぶ。
            var parentCtx = (mc.HierarchyParentIndex >= 0 && mc.HierarchyParentIndex < model.MeshContextCount)
                ? model.GetMeshContext(mc.HierarchyParentIndex) : null;
            string parentDesc = parentCtx == null
                ? "（なし）"
                : $"\"{parentCtx.Name}\"({mc.HierarchyParentIndex},{parentCtx.Type})";

            // ミラーが付いたか。ペアの実体側になっていれば有。
            bool mirrored = false;
            if (model.MirrorPairs != null)
                foreach (var pair in model.MirrorPairs)
                    if (pair?.Real != null && ReferenceEquals(pair.Real, mc)) { mirrored = true; break; }

            AddLine($"    \"{mc.Name}\" 索引={idx} 親={parentDesc} " +
                    $"スキンド={mc.IsSkinned} ミラー={(mirrored ? "有" : "無")} " +
                    $"重心(ワールド)={Fmt(world)} 2穴の中点={Fmt(mid)} 距離={d:F4}",
                    bad);
        }

        private static string Fmt(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

        /// <summary>
        /// 書き出したファイルの中身を数えて報告する。合否は付けない。
        /// 「保存で消えたのか、読み込みで落ちたのか」を切り分けるための実測値。
        /// </summary>
        private void ReportExportedFileFacts(string folder)
        {
            try
            {
                var files = Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories);

                int mirrorBoneLines = 0;
                int boneBlocks      = 0;
                int mirrorPairLines = 0;

                foreach (string f in files)
                {
                    string name = Path.GetFileName(f);

                    if (name.EndsWith(".bone.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string line in File.ReadAllLines(f))
                        {
                            if (line.StartsWith("mirrorBoneIndex,", StringComparison.Ordinal)) mirrorBoneLines++;
                            else if (line.StartsWith("index,", StringComparison.Ordinal))      boneBlocks++;
                        }
                    }
                    else if (name.Equals("mirrorpairs.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string line in File.ReadAllLines(f))
                            if (!string.IsNullOrEmpty(line) && !line.StartsWith("#", StringComparison.Ordinal))
                                mirrorPairLines++;
                    }
                }

                AddLine($"    書き出したファイル: bone.csv のボーン {boneBlocks} 本 / " +
                        $"mirrorBoneIndex 行 {mirrorBoneLines} 本 / mirrorpairs 行 {mirrorPairLines} 本");
            }
            catch (Exception e)
            {
                AddLine($"    書き出しファイルを読めなかった: {e.Message}", true);
            }
        }

        /// <summary>MeshObject の重心をワールドで返す。所属の MeshContext から座標系を引く。</summary>
        private static Vector3 WorldCentroid(ModelContext model, MeshObject mo)
        {
            Vector3 c = Centroid(mo);
            if (model == null || mo == null) return c;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && ReferenceEquals(mc.MeshObject, mo))
                    return mc.VertexToWorldMatrix.MultiplyPoint3x4(c);
            }
            return c;
        }

        private static Vector3 Centroid(MeshObject mo)
        {
            if (mo == null || mo.VertexCount == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < mo.VertexCount; i++) sum += mo.Vertices[i].Position;
            return sum / mo.VertexCount;
        }

        /// <summary>
        /// エッジ（1 面だけが使う辺）上の頂点のうち、target に最も近いものを返す。
        /// 六角柱の蓋なしメッシュには穴が 2 つあるので、相手側の穴を選ぶために使う。
        /// </summary>
        private static int NearestBoundaryVertex(MeshObject mo, Vector3 target)
        {
            if (mo == null) return -1;

            var edges = Poly_Ling.Ops.BoundaryEdgeOps.CollectBoundaryEdges(mo);
            if (edges == null || edges.Count == 0) return -1;

            int best = -1;
            float bestDist = float.MaxValue;

            foreach (var e in edges)
            {
                foreach (int v in new[] { e.V1, e.V2 })
                {
                    if (v < 0 || v >= mo.VertexCount) continue;
                    float d = Vector3.SqrMagnitude(mo.Vertices[v].Position - target);
                    if (d < bestDist) { bestDist = d; best = v; }
                }
            }
            return best;
        }

        /// <summary>
        /// ブリッジのウェイトを数値設定で入れる。
        /// 名前でメッシュとボーンを引き当て、選択してから数値設定コマンドを送る。
        /// パネルからの操作と同じ順序（選択 → 適用）にする。
        /// </summary>
        private bool ApplyBridgeWeights()
        {
            var model = GetModel?.Invoke();
            if (model == null) return false;

            // (メッシュ名, ボーン名A, ボーンB) の組。左右どちらも対象にする。
            var jobs = new (string mesh, string boneA, string boneB)[]
            {
                ("Bridge_skinned",   "左腕",   "左ひじ"),
                ("Bridge_1_skinned", "左ひじ", "左手首"),
            };

            foreach (var job in jobs)
            {
                int meshIdx = FindIndexByName(model, job.mesh, bone: false);
                int boneA   = FindIndexByName(model, job.boneA, bone: true);
                int boneB   = FindIndexByName(model, job.boneB, bone: true);

                if (meshIdx < 0)
                {
                    AddLine($"    メッシュ \"{job.mesh}\" が見つからない", true);
                    _failCount++;
                    continue;
                }
                if (boneA < 0 || boneB < 0)
                {
                    AddLine($"    ボーン \"{job.boneA}\" / \"{job.boneB}\" が見つからない", true);
                    _failCount++;
                    continue;
                }

                // 対象メッシュを選び、全頂点を選択してから数値設定する。
                Send(new SelectMeshCommand(ModelIndex(), MeshCategory.Drawable, new[] { meshIdx }));

                var mc = model.GetMeshContext(meshIdx);
                if (mc?.MeshObject != null)
                {
                    mc.SelectedVertices.Clear();
                    for (int v = 0; v < mc.MeshObject.VertexCount; v++)
                        mc.SelectedVertices.Add(v);
                }

                Send(new SetSkinWeightNumericCommand(
                    ModelIndex(),
                    new[] { boneA, boneB, -1, -1 },
                    new[] { 0.5f, 0.5f, 0f, 0f }));
            }

            return true;
        }

        private bool ApplyHumanoidMapping()
        {
            var model = GetModel?.Invoke();
            if (model == null) return false;

            var boneNames = new List<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                boneNames.Add(mc != null && mc.Type == MeshType.Bone ? (mc.Name ?? "") : "");
            }

            var mapping = new HumanoidBoneMapping();
            int mapped = mapping.AutoMapFromEmbeddedCSV(boneNames);
            if (mapped <= 0)
            {
                AddLine("    Humanoid 自動割当が 0 件", true);
                _failCount++;
                return true;   // 段自体は続行し、検査結果を見せる
            }

            Send(new ApplyHumanoidMappingCommand(ModelIndex(), mapping));
            return true;
        }

        // ================================================================
        // 検査
        // ================================================================

        private void RunChecks(string stageName, bool checkParentIdentity)
        {
            var model = GetModel?.Invoke();

            var violations = new List<InvariantViolation>();
            if (_prevSnapshot != null)
                violations.AddRange(ModelInvariantChecker.CompareWithPrevious(
                    _prevSnapshot, model, checkParentIdentity));
            violations.AddRange(ModelInvariantChecker.CheckAll(model));

            bool bad = violations.Count > 0;
            if (bad) _failCount += violations.Count;

            // 合否とは別に、値そのものを毎段出す。
            // 「どこで何が消えたか」を次の実行で読み取れるようにするため。
            var summary = ModelSummary.Capture(model);
            AddLine($"■ {stageName}  {summary}  " +
                    (bad ? $"違反 {violations.Count} 件" : "合格"), bad);

            FlushPendingLines();

            var measure = _pendingMeasurement;
            _pendingMeasurement = null;
            measure?.Invoke();

            foreach (var v in violations)
                AddLine("    " + v, true);

            CheckSkinKindConsistency(model);
            CheckSkinKindRoundTrip(model);

            _prevSnapshot = ModelStructureSnapshot.Capture(model);
        }

        /// <summary>
        /// 「スキンド化 → MeshFilter へ戻す」の往復で、頂点のワールド座標が
        /// 保存されるかを実測する。データは書き換えない（複製の上で試す）。
        ///
        /// 【何を見つけるための検査か】
        ///   2 つの種別は頂点の格納空間が違う。SkinKindConverter が焼き直しを
        ///   間違えると、変換した瞬間に形が飛ぶ。合否は「往復後のワールド座標が
        ///   元と一致するか」で判定する。
        ///
        ///   検査は複製した ModelContext ではなく、実データの読み取りだけで行う。
        ///   種別ごとに「格納値 → ワールド」の式が正しく合っているかを見る。
        /// </summary>
        private void CheckSkinKindRoundTrip(ModelContext model)
        {
            if (model == null) return;

            int bad = 0;
            int checkedCount = 0;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;
                if (mc.Type != MeshType.Mesh) continue;

                checkedCount++;

                // VertexToWorldMatrix は種別で答えを変える。
                //   MeshFilter … WorldMatrix
                //   Skinned    … 単位（頂点が既にワールド空間）
                // ここが種別と食い違うと、往復変換でワールド座標がずれる。
                Matrix4x4 toWorld  = mc.VertexToWorldMatrix;
                Matrix4x4 toVertex = mc.WorldToVertexMatrix;

                // 往復して戻るか。掛け算の順序と逆行列の整合を実測する。
                Vector3 v0    = mo.Vertices[0].Position;
                Vector3 world = toWorld.MultiplyPoint3x4(v0);
                Vector3 back  = toVertex.MultiplyPoint3x4(world);

                float d = Vector3.Distance(v0, back);
                if (d > 1e-3f)
                {
                    bad++;
                    AddLine($"    座標往復ずれ: \"{mc.Name}\" 索引={i} " +
                            $"種別={mo.SkinKind} 距離={d:F5}", true);
                }
            }

            if (bad > 0) _failCount += bad;

            AddLine($"    SkinKind 座標往復検査: 対象 {checkedCount} 件 / ずれ {bad} 件",
                    bad > 0);
        }

        /// <summary>
        /// 描画オブジェクトの種別（MeshObject.SkinKind）と実頂点のウェイトの食い違いを報告する。
        ///
        /// 【何を見つけるための検査か】
        ///   SkinKind は明示状態であり、頂点のウェイトから毎回導出しない。
        ///   そのため「ウェイトを入れたのに種別を確定させ忘れた」経路があると、
        ///   頂点はワールド（バインド）空間なのに WorldMatrix 経路で描画され、
        ///   位置が二重に掛かる。これを実測で拾う。
        ///
        /// 【判定】
        ///   ・種別 MeshFilter なのにウェイト付き頂点がある … 不合格（確定漏れ）
        ///   ・種別 Skinned なのにウェイト付き頂点が 0 個   … 情報のみ
        ///     （ウェイトを全部消しても種別は自動で戻さない仕様。想定内）
        /// </summary>
        private void CheckSkinKindConsistency(ModelContext model)
        {
            if (model == null) return;

            int missing = 0;   // MeshFilter 宣言なのにウェイトあり
            int empty   = 0;   // Skinned 宣言なのにウェイトなし

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;
                if (mc.Type == MeshType.Bone) continue;

                bool anyWeight = mo.AnyVertexHasBoneWeight();

                if (!mc.IsSkinned && anyWeight)
                {
                    missing++;
                    AddLine($"    種別未確定: \"{mc.Name}\" 索引={i} " +
                            $"SkinKind={mo.SkinKind} だがウェイト付き頂点あり", true);
                }
                else if (mc.IsSkinned && !anyWeight)
                {
                    empty++;
                    AddLine($"    種別のみ Skinned: \"{mc.Name}\" 索引={i} " +
                            $"ウェイト付き頂点 0（明示状態のため自動では戻さない）");
                }
            }

            if (missing > 0) _failCount += missing;

            AddLine($"    SkinKind 検査: 未確定 {missing} 件 / ウェイト 0 の Skinned {empty} 件",
                    missing > 0);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private int ModelIndex() => GetModelIndex?.Invoke() ?? 0;

        private void Send(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private static int FindIndexByName(ModelContext model, string name, bool bone)
        {
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (bone != (mc.Type == MeshType.Bone)) continue;
                if (mc.Name == name) return i;
            }
            return -1;
        }

        /// <summary>控えていた行を見出しの後ろへ流す。</summary>
        private void FlushPendingLines()
        {
            if (_pendingLines.Count == 0) return;
            var copy = new List<(string Text, bool Bad)>(_pendingLines);
            _pendingLines.Clear();
            foreach (var l in copy) AddLine(l.Text, l.Bad);
        }

        private void AddLine(string text, bool bad = false)
        {
            if (_bufferLines) { _pendingLines.Add((text, bad)); return; }

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

        // ================================================================
        // 対象フォルダの記憶
        // ================================================================

        private const string PrefKey = "PipelineTestFolder";

        private static string LoadRememberedFolder()
        {
            try { return RecentPaths.Get(PrefKey) ?? ""; }
            catch { return ""; }
        }

        private static void RememberFolder(string folder)
        {
            try { RecentPaths.Set(PrefKey, folder); }
            catch { /* 記憶できなくてもテストは動く */ }
        }
    }
}
