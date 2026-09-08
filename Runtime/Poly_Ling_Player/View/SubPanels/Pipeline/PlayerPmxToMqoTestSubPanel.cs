// PlayerPmxToMqoTestSubPanel.cs
// PMX位置→MQO保存 自動検証。ボタン 1 回で
//   PMX をソースとして読む → MQO を読む → 対応付け → MQO の頂点位置を PMX で差し替える
//   → 差し替え結果の検査 → MQO を別名で書き出す → 書き出しの検査とレポート書出
// までを流す。段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるための検証か】
//   「形は PMX で作り直したが、面構成・材質・頂点IDは MQO のものを使いたい」という
//   置き換え作業を、部分インポートパネルと同じ経路（PMXPartialImportOps）で通す。
//   崩れやすいのは対応付けで、名前も頂点数も合わないメッシュがあると
//   別のオブジェクトへ座標が流し込まれる。段 5 で対応表を、段 7 で全数比較を出す。
//
// 【PMX をモデルに載せない】
//   ImportPmxCommand / ImportMqoCommand はどちらも新しい ModelContext を作って
//   差し替える（ImportCommands.cs:78-113 / :180-205）。PMX をモデルに載せると
//   直後の MQO 読込で消えるため、PMX は onResult で受け取った PMXImportResult を
//   PMXPartialImportOps に渡すだけにして、_localLoader.LoadModel は呼ばない。
//   これは部分インポートパネルが PMX 側を持つのと同じ扱いである。
//
// 【展開頂点と非展開頂点】
//   モデル（MQO 由来）の頂点は非展開形で、1 頂点が UV スロットを n 個持つ。
//   PMX 側は展開済みで 1 頂点 1 UV。両者の対応は MeshExpansion の展開順で決まり、
//   生の頂点数どうしは一致しないのが正常である。
//   数え直しも突き合わせも MeshExpansion を通すこと。ここに手書きしてはならない。
//
// 【待ちの予算】
//   読み込みはコマンドキュー経由で、ファイルの大きさによっては何分も掛かる。
//   PMXImporter.ImportFile / MQOImporter.ImportFile はどちらも同期なので、
//   読んでいる間はメインスレッドが止まり、待ち直しの回数は消費されない。
//   ここでの上限は「キューが回り始めるまで」「クラウドストレージの実体化待ち」で
//   空回りする分に対する予算であり、1200ms × 250 ＝ 300 秒を確保している。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Commands;
using Poly_Ling.Context;
using Poly_Ling.Core;          // RecentPaths
using Poly_Ling.Diagnostics;   // PLDiag
using Poly_Ling.MeshBridge;
using Poly_Ling.MQO;
using Poly_Ling.PMX;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>PMX の頂点位置で MQO を差し替えて別名保存する自動検証。</summary>
    public class PlayerPmxToMqoTestSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext> GetModel;

        /// <summary>コマンドキューへ積む。読み込みは実経路（ImportPmxCommand）へ流す。</summary>
        public Action<ICommand> EnqueueCommand;

        /// <summary>MQO を読み込む。実際の import 経路（ImportMqoCommand）へ流す。</summary>
        public Action<string> ImportMqo;

        /// <summary>Undo 記録用。部分インポートパネルと同じ MeshListStack へ積む。</summary>
        public Func<MeshUndoController> GetUndoController;

        /// <summary>MQO を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, MQOExportSettings, MQOExportResult> ExportMqo;

        /// <summary>頂点を書き換えたあとの再構築。部分インポート完了時と同じ処理。</summary>
        public Action RefreshAfterReplace;

        // ================================================================
        // RecentPaths のキー（IO パネルと共有する）
        // ================================================================

        private const string PmxPathKey = "Import.PMX.Path";
        private const string MqoPathKey = "Import.MQO.Path";

        /// <summary>保存ダイアログのファイル名欄に出す既定名の接尾辞。</summary>
        private const string OutSuffix = "_pmxpos";

        // ================================================================
        // 段の刻み
        // ================================================================

        /// <summary>段の間隔。既定（120ms）では読み込みの完了を待ち切れない。</summary>
        protected override long StageIntervalMs => 1200;

        /// <summary>待ち直しの上限。1200ms × 250 ＝ 300 秒。</summary>
        protected override int MaxRetry => 250;

        // ================================================================
        // UI
        // ================================================================

        private TextField _pmxPathField, _mqoPathField;
        private FloatField _pmxScale;
        private Toggle     _pmxFlipX, _pmxFlipZ;
        private FloatField _mqoRatio;
        private Toggle     _mqoFlipX, _mqoFlipZ;

        // ================================================================
        // 実行状態
        // ================================================================

        private readonly PMXPartialImportOps _pmxOps = new PMXPartialImportOps();

        private string _pmxPath = "";
        private string _mqoPath = "";
        private string _outPath = "";
        private string _reportPath = "";

        // PMX 読込の受け取り（コマンドの onResult / onError から書かれる）
        private PMXImportResult _pmxResult;
        private string          _pmxError;
        private bool            _pmxDone;

        // MQO 読込の完了判定。ImportMqoCommand は必ず新しい ModelContext を作るので、
        // 件数ではなく実体が入れ替わったかで見る。件数比較だと、前回の実行結果が
        // 同じ件数で残っている場合に「読み込む前に完了した」と誤認する。
        private ModelContext _modelBeforeImport;

        private List<PartialMeshEntry> _pairModel = new List<PartialMeshEntry>();
        private List<PartialPMXEntry>  _pairPmx   = new List<PartialPMXEntry>();

        private int _transferred;
        private int _mismatchMeshes;
        private int _diffVertices;
        private MQOExportResult _exportResult;

        // ================================================================
        // 基底が要求するもの
        // ================================================================

        protected override string TitleText => "PMX位置→MQO保存 自動検証";

        protected override string NoteText =>
            "PMX をソースとして読込 → MQO を読込 → 対応付け → MQO の頂点位置を PMX で差し替え\n"
          + "→ 差し替えの検査 → MQO を別名で書き出し → 書き出しの検査とレポート書出 を通しで流します。\n"
          + "PMX はモデルに載せません（載せると次の MQO 読込で消えるため）。\n"
          + "段ごとに、手でやるならどこを押すかと、その段が要る理由をログに出します。";

        protected override void BuildOptionsUI(VisualElement root)
        {
            // ── 入出力 ────────────────────────────────────────────────
            root.Add(Sec("入出力"));

            _pmxPathField = new TextField("PMX パス（ソース）");
            _pmxPathField.style.fontSize = 10;
            _pmxPathField.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _pmxPathField.RegisterValueChangedCallback(e => SafeSet(PmxPathKey, e.newValue));
            root.Add(_pmxPathField);

            _mqoPathField = new TextField("MQO パス（差し替え先）");
            _mqoPathField.style.fontSize = 10;
            _mqoPathField.SetValueWithoutNotify(SafeGet(MqoPathKey));
            _mqoPathField.RegisterValueChangedCallback(e => SafeSet(MqoPathKey, e.newValue));
            root.Add(_mqoPathField);

            root.Add(MakeOutDest("出力 MQO の保存先", "mqo", DefaultOutName));

            root.Add(Hint(
                "[...] は書き込み先フォルダを決めるだけです。ファイル名は「実行」を押したときの"
              + "保存ダイアログで決めます。ファイル名欄の初期値は「元の名前" + OutSuffix + ".mqo」です。"
              + "読み込んだ MQO と同じパスは受け付けません（別名保存の検証のため）。"));

            root.Add(Hint(
                "読み込みには時間が掛かります。読んでいる間は画面が止まりますが、"
              + "待ち直しの上限は 300 秒あるので、そのまま待ってください。"));

            // ── PMX 読込の座標変換 ───────────────────────────────────
            root.Add(Sec("PMX 読込の座標変換"));
            _pmxScale = F("Scale", 0.1f);
            _pmxFlipX = new Toggle("Flip X") { value = true };
            _pmxFlipZ = new Toggle("Flip Z") { value = true };
            root.Add(_pmxScale); root.Add(_pmxFlipX); root.Add(_pmxFlipZ);
            root.Add(Hint(
                "PMX と Unity はどちらも左手系ですが、PMX のモデルは Z のマイナス方向を向いています。"
              + "そのため X と Z の両反転（＝Y軸180°回転）が既定です。"
              + "部分インポートパネルの PMX 側と同じ値です。"));

            // ── MQO 書き出しの座標変換 ───────────────────────────────
            root.Add(Sec("MQO 書き出しの座標変換"));
            _mqoRatio = F("MQO/Unity 比", 0.01f);
            _mqoFlipX = new Toggle("Flip X") { value = true };
            _mqoFlipZ = new Toggle("Flip Z") { value = false };
            root.Add(_mqoRatio); root.Add(_mqoFlipX); root.Add(_mqoFlipZ);
            root.Add(Hint(
                "MQO は右手系で、Unity との違いは X の反転だけです。Flip Z を入れると"
              + "読み込み側（既定 false）と食い違い、往復で Z 反転が残ります。"
              + "エクスポートパネルの MQO 既定と同じ値です。"));
        }

        public override void Refresh()
        {
            base.Refresh();
            if (IsRunning) return;

            _pmxPathField?.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _mqoPathField?.SetValueWithoutNotify(SafeGet(MqoPathKey));
        }

        /// <summary>保存ダイアログのファイル名欄の初期値。差し替え先 MQO の名前から作る。</summary>
        private string DefaultOutName()
        {
            string stem = "";
            try { stem = Path.GetFileNameWithoutExtension(_mqoPathField?.value ?? ""); }
            catch (ArgumentException) { }

            return string.IsNullOrEmpty(stem) ? "" : stem + OutSuffix + ".mqo";
        }

        private static string SafeGet(string key)
        {
            try { return RecentPaths.Get(key); }
            catch { return ""; }
        }

        private static void SafeSet(string key, string value)
        {
            try { RecentPaths.Set(key, value); }
            catch { }
        }

        // ================================================================
        // 実行前の検査
        // ================================================================

        protected override bool CanRun()
        {
            if (EnqueueCommand == null || ImportMqo == null || GetModel == null || ExportMqo == null)
            { SetStatus("配線が足りません（EnqueueCommand / ImportMqo / GetModel / ExportMqo）。"); return false; }

            _pmxPath = _pmxPathField?.value ?? "";
            _mqoPath = _mqoPathField?.value ?? "";

            if (string.IsNullOrEmpty(_pmxPath) || !File.Exists(_pmxPath))
            { SetStatus("PMX パスが正しくありません。"); return false; }

            if (string.IsNullOrEmpty(_mqoPath) || !File.Exists(_mqoPath))
            { SetStatus("MQO パスが正しくありません。"); return false; }

            // 出力先は毎回ここで確定する（「名前を付けて保存」）。
            // 履歴のフルパスや差し替え先 MQO の名前がそのまま書き込み先になる経路は残さない。
            _outPath = AskOutPath();
            if (string.IsNullOrEmpty(_outPath)) return false;   // キャンセル

            if (SamePath(_outPath, _mqoPath))
            { SetStatus("出力パスが読み込む MQO と同じです。別名にしてください。"); return false; }

            // フォルダを指したまま実行すると、書き出しが
            // 「Access to the path ... is denied」で落ちる。ここで弾く。
            if (Directory.Exists(_outPath) ||
                _outPath.EndsWith("\\", StringComparison.Ordinal) ||
                _outPath.EndsWith("/",  StringComparison.Ordinal))
            { SetStatus("出力パスがフォルダを指しています。ファイル名まで入れてください。"); return false; }

            if (!string.Equals(Path.GetExtension(_outPath), ".mqo", StringComparison.OrdinalIgnoreCase))
            { SetStatus("出力パスの拡張子が .mqo ではありません。"); return false; }

            string outDir = Path.GetDirectoryName(_outPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            { SetStatus($"出力先フォルダがありません: {outDir}"); return false; }

            return true;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(a), Path.GetFullPath(b),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        protected override void ResetRunState()
        {
            _pmxResult = null;
            _pmxError  = null;
            _pmxDone   = false;

            _modelBeforeImport = GetModel?.Invoke();

            _pairModel = new List<PartialMeshEntry>();
            _pairPmx   = new List<PartialPMXEntry>();

            _transferred    = 0;
            _mismatchMeshes = 0;
            _diffVertices   = 0;
            _exportResult   = null;

            _reportPath = BuildReportPath("PmxToMqoTest");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. PMX をソースとして読み込む",       StageLoadPmxSource));
            stages.Add(("2. PMX の読み込み完了を待つ",         StageWaitPmxSource));
            stages.Add(("3. MQO を読み込む",                   StageImportMqo));
            stages.Add(("4. MQO の読み込み完了を待つ",         StageWaitMqoImport));
            stages.Add(("5. 対応付けをする",                   StageMatch));
            stages.Add(("6. 頂点位置を差し替える",             StageReplacePositions));
            stages.Add(("7. 差し替えの結果を検査する",         StageVerifyReplace));
            stages.Add(("8. MQO を別名で書き出す",             StageExportMqo));
            stages.Add(("9. 書き出しを検査してレポートを書く", StageVerifyExport));
        }

        protected override void OnFinished(bool aborted)
        {
            if (aborted) WriteReport("段の途中で停止した");
        }

        // ================================================================
        // 段 1-2: PMX をソースとして読む
        // ================================================================

        private StageResult StageLoadPmxSource()
        {
            // 部分インポートパネルの PMX 側と同じ設定（PlayerPartialImportSubPanel.cs:202-213）。
            // 材質は取り込まないので ImportMaterials は false、
            // 位置だけ移すので法線の再計算も要らない。
            var settings = new PMXImportSettings
            {
                ImportMode            = PMXImportMode.NewModel,
                ImportTarget          = PMXImportTarget.Mesh,
                ImportMaterials       = false,
                FlipX                 = _pmxFlipX.value,
                FlipZ                 = _pmxFlipZ.value,
                Scale                 = _pmxScale.value,
                RecalculateNormals    = false,
                DetectNamedMirror     = true,
                UseObjectNameGrouping = true
            };

            EnqueueCommand(new ImportPmxCommand(
                _pmxPath, settings,
                onResult: (_, r)  => { _pmxResult = r;   _pmxDone = true; },
                onError:  message => { _pmxError  = message; _pmxDone = true; }));

            return Ok(
                $"ImportPmxCommand をキューへ積んだ（「{Path.GetFileName(_pmxPath)}」/ "
              + $"Scale={Fmt(_pmxScale.value)} / FlipX={_pmxFlipX.value} / FlipZ={_pmxFlipZ.value}）",
                "部分インポートパネル → PMX → ファイルを選ぶ",
                "PMX はソースにするだけで、モデルには載せない。載せると次の段の MQO 読込で"
              + "ModelContext ごと差し替わって消える。コマンドが返す PMXImportResult を"
              + "そのまま部分インポートのロジックへ渡す。");
        }

        private StageResult StageWaitPmxSource()
        {
            if (!_pmxDone) return StageResult.Retry;

            if (!string.IsNullOrEmpty(_pmxError))
                return Ng($"PMX の読み込みに失敗した: {_pmxError}",
                    "部分インポートパネル → PMX → ファイルを選ぶ",
                    "パスとファイルの中身を疑う。ここで止まると以降の段は意味を持たない。");

            if (_pmxResult == null || !_pmxResult.Success)
                return Ng($"PMX の読み込みに失敗した: {_pmxResult?.ErrorMessage}", null, null);

            _pmxOps.LoadPMXResult(_pmxResult);

            if (_pmxOps.PMXMeshes.Count == 0)
                return Ng("PMX から取り込めるメッシュが 0 件だった",
                    null,
                    "ボーンだけのメッシュと頂点 0 のメッシュは一覧から外れる"
                  + "（PMXPartialImportOps.cs:96-115）。材質の ObjectName が付いていないと"
                  + "分割の仕方も変わる。");

            int verts = 0;
            foreach (var e in _pmxOps.PMXMeshes) verts += e.VertexCount;

            return Ok(
                $"PMX 側の一覧を作った（{_pmxOps.PMXMeshes.Count} メッシュ / 合計 {verts} 頂点）",
                null,
                "PMX の頂点は面ごとに展開済みなので、MQO 側とは頂点数が食い違うのが普通。"
              + "対応付けは次の段で行う。");
        }

        // ================================================================
        // 段 3-4: MQO を読む
        // ================================================================

        private StageResult StageImportMqo()
        {
            _modelBeforeImport = GetModel?.Invoke();
            ImportMqo(_mqoPath);

            return Ok(
                $"ImportMqoCommand と同じ経路で「{Path.GetFileName(_mqoPath)}」の読み込みを要求した",
                "インポートパネル → MQO → ファイルを選ぶ",
                "差し替え先はこちら。面構成・材質・頂点IDは MQO のものを残し、"
              + "頂点の位置だけを PMX の値に置き換える。読み込みはキュー経由なので"
              + "この段では要求を出すだけで完了を待たない。");
        }

        private StageResult StageWaitMqoImport()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;
            if (ReferenceEquals(model, _modelBeforeImport)) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;

            return Ok(
                $"モデル「{model.Name}」に入れ替わった（MeshContext {model.MeshContextCount} 件）",
                null,
                "ImportMqoCommand は新しい ModelContext を作って差し替える。"
              + "件数ではなく実体が変わったかで見ているので、前回の実行結果が残っていても"
              + "読み込む前に完了したと誤認しない。ここで止まるなら import が走っていない。");
        }

        // ================================================================
        // 段 5: 対応付け
        // ================================================================

        private StageResult StageMatch()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            _pmxOps.BuildModelList(model);
            if (_pmxOps.ModelMeshes.Count == 0)
                return Ng("差し替え先の描画オブジェクトが 0 件だった",
                    null,
                    "頂点 0 のオブジェクトは一覧から外れる（PMXPartialImportOps.cs:55-78）。");

            _pmxOps.AutoMatch();

            _pairModel = _pmxOps.SelectedModelMeshes;
            _pairPmx   = _pmxOps.SelectedPMXMeshes;

            if (_pairModel.Count == 0 || _pairPmx.Count == 0)
                return Ng("対応が 1 組も付かなかった",
                    "部分インポートパネル → PMX →「Auto」",
                    "対応付けは名前の完全一致が先、次に頂点数の一致（PMXPartialImportOps.cs:120-152）。"
                  + "どちらも当たらないなら、MQO と PMX でオブジェクト名がずれている。");

            // 実際の転送は「両方の一覧の同じ位置どうし」を組にする。表示もその組み方に合わせる。
            // 比べるのはモデル側の展開後頂点数と PMX の頂点数。生の頂点数どうしは
            // 一致しないのが正常なので、そちらを出すと誤った判断を招く。
            // 展開数は BuildModelList が MeshExpansion から取った値をそのまま使う。
            int pairs = Math.Min(_pairModel.Count, _pairPmx.Count);
            var sb = new StringBuilder();
            _mismatchMeshes = 0;

            for (int p = 0; p < pairs; p++)
            {
                int m = _pairModel[p].ExpandedVertexCount;
                int x = _pairPmx[p].VertexCount;
                bool same = (m == x);
                if (!same) _mismatchMeshes++;

                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(_pairModel[p].Name).Append("←").Append(_pairPmx[p].Name)
                  .Append('(').Append(m).Append(same ? "=" : "≠").Append(x).Append(')');
            }

            string did =
                $"{pairs} 組を対応付けた（数字はモデルの展開後頂点数と PMX の頂点数）: {sb}"
              + (_mismatchMeshes > 0 ? $"（食い違い {_mismatchMeshes} 組）" : "");

            return Ok(
                did,
                "部分インポートパネル → PMX →「Auto」で自動対応、リストで手直し",
                "対応付けはモデルの展開後頂点数と PMX の頂点数で行う。"
              + "食い違う組は少ないほうの件数だけ転送して警告が出る。"
              + "組の並びがずれていると別のオブジェクトへ座標が流れ込むので、"
              + "この表を必ず確認すること。");
        }

        // ================================================================
        // 段 6: 頂点位置を差し替える
        // ================================================================

        private StageResult StageReplacePositions()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            var before = MultiMeshVertexSnapshot.Capture(model);

            _transferred = _pmxOps.ExecuteVertexAttributeImport(
                _pairModel, _pairPmx, position: true, uv: false, boneWeight: false);

            if (_transferred == 0)
                return Ng("転送された頂点が 0 だった", null,
                    "対応が付いていても、片側の MeshObject が null なら組は飛ばされる。");

            // 位置を動かしたので、そのベースを持つモーフの基準データを追従させる。
            // 頂点数も順序も変えていないので索引マップは要らない
            // （PMXPartialImportOps.cs:305-312）。
            PMXPartialImportOps.RemapMorphBasesAfterVertexChange(
                _pairModel, model, remapPosition: true, remapUV: false);

            // Undo は部分インポートパネルと同じ MeshListStack へ積む
            // （PlayerPartialImportSubPanel.cs:258-271）。
            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var after  = MultiMeshVertexSnapshot.Capture(model);
                var label  = "PMX Partial Import";
                var record = new MultiMeshVertexSnapshotRecord(before, after, label);
                PLDiag.UndoRecord("MeshList", label, record);
                undo.MeshListStack.Record(record, label);
            }

            RefreshAfterReplace?.Invoke();

            return Ok(
                $"頂点位置を {_transferred} 頂点ぶん差し替えた（UV とウェイトは触らない）"
              + (undo != null ? "。Undo を 1 件積んだ" : "。Undo コントローラが無いので記録は省いた"),
                "部分インポートパネル → PMX →「頂点位置」だけをオンにして Import",
                "位置だけを移すので、面構成・材質・頂点IDは MQO のものが残る。"
              + "モデル頂点と PMX 頂点の対応は MeshExpansion の展開順で決まる。"
              + "位置は展開時にスロット数ぶん複製される値なので、逆方向はスロット 0 の"
              + "1 個だけを取る。モーフの基準データは位置の差分で持っているため、"
              + "ベースを動かしたら一緒に付け替えないとモーフが元の形へ引き戻す。");
        }

        // ================================================================
        // 段 7: 差し替えの結果を検査
        // ================================================================

        private StageResult StageVerifyReplace()
        {
            // 突き合わせも転送と同じ MeshExpansion の展開順で行う。
            // ここで別の対応付けを書くと、転送が誤っていても検査が通ってしまう。
            int pairs = Math.Min(_pairModel.Count, _pairPmx.Count);
            int checkedVerts = 0;
            int untouched    = 0;
            _diffVertices = 0;

            var shortNames = new List<string>();

            for (int p = 0; p < pairs; p++)
            {
                var dst = _pairModel[p].Context?.MeshObject;
                var src = _pairPmx[p].MeshContext?.MeshObject;
                if (dst == null || src == null) continue;

                if (_pairModel[p].ExpandedVertexCount != _pairPmx[p].VertexCount)
                    shortNames.Add(_pairModel[p].Name);

                int localChecked = 0, localDiff = 0, localSkipped = 0;

                MeshExpansion.Enumerate(dst, (vIdx, uvIdx, expIdx) =>
                {
                    if (uvIdx != 0) return;                 // 位置はスロット 0 だけが転送先
                    if (expIdx >= src.VertexCount) { localSkipped++; return; }

                    var d = dst.Vertices[vIdx].Position;
                    var s = src.Vertices[expIdx].Position;
                    if ((d - s).sqrMagnitude > 0f) localDiff++;
                    localChecked++;
                });

                checkedVerts  += localChecked;
                _diffVertices += localDiff;
                untouched     += localSkipped;
            }

            if (_diffVertices > 0)
                return Ng(
                    $"{checkedVerts} 頂点を比べて {_diffVertices} 頂点が PMX と一致しなかった",
                    null,
                    "転送は代入なので、通った頂点は完全に一致するはず。ずれているなら"
                  + "差し替えのあとに別の処理が座標へ触っている。");

            string did = $"{checkedVerts} 頂点すべてが PMX の位置と一致した";
            if (untouched > 0)
                did += $"（PMX 側の頂点が足りず手つかずのモデル頂点が {untouched} 個残っている）";
            if (shortNames.Count > 0)
                did += $"。展開数が PMX と食い違う組: {string.Join(", ", shortNames)}";

            return Ok(
                did,
                null,
                "手つかずの頂点は MQO の元の位置のまま残る。ここが 0 でないなら、"
              + "書き出した MQO は PMX の形と部分的にしか一致しない。");
        }

        // ================================================================
        // 段 8-9: MQO を別名で書き出す
        // ================================================================

        private StageResult StageExportMqo()
        {
            var settings = MQOExportSettings.CreateFromCoordinate(
                _mqoRatio.value, flipZ: _mqoFlipZ.value, flipX: _mqoFlipX.value);

            _exportResult = ExportMqo(_outPath, settings);

            if (_exportResult == null || !_exportResult.Success)
                return Ng($"書き出しに失敗した: {_exportResult?.ErrorMessage}",
                    "エクスポートパネル → MQO → 保存先を選ぶ",
                    "書き出し経路はエクスポートパネルと同じ。失敗するならモデル側ではなく"
                  + "出力先か設定を疑う。");

            var st = _exportResult.Stats;
            return Ok(
                $"「{Path.GetFileName(_outPath)}」へ書き出した"
              + $"（オブジェクト {st.ObjectCount} / 頂点 {st.TotalVertices} / 面 {st.TotalFaces} / 材質 {st.MaterialCount}）",
                "エクスポートパネル → MQO → 保存先を選ぶ",
                "読み込んだ MQO は上書きしない。元と見比べられるように別名で出す。"
              + "補助データ（CSV）も隣の _backup フォルダへ一緒に出る。");
        }

        private StageResult StageVerifyExport()
        {
            if (!File.Exists(_outPath))
                return Ng("書き出したはずのファイルが無い", null,
                    "書き出しは成功を返したのにファイルが無いなら、出力先の権限か"
                  + "同期フォルダ側の遅延を疑う。");

            long size = 0;
            try { size = new FileInfo(_outPath).Length; } catch { }

            if (size <= 0)
                return Ng("書き出したファイルが空だった", null, null);

            WriteReport("完了");

            return Ok(
                $"ファイルを確認した（{size} バイト）。レポートを書いた: {_reportPath}",
                null,
                "段ごとの 3 行ログをそのまま平文で残している。"
              + "対応表と手つかず頂点数が載っているので、あとから differences を追える。");
        }

        // ================================================================
        // レポート
        // ================================================================

        private void WriteReport(string outcome)
        {
            try
            {
                string dir = Path.GetDirectoryName(_reportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("PMX位置→MQO保存 自動検証");
                sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("結果: " + outcome);
                sb.AppendLine();
                sb.AppendLine("PMX (ソース): " + _pmxPath);
                sb.AppendLine("MQO (差し替え先): " + _mqoPath);
                sb.AppendLine("MQO (出力): " + _outPath);
                sb.AppendLine();
                sb.AppendLine($"PMX 読込: Scale={Fmt(_pmxScale.value)} FlipX={_pmxFlipX.value} FlipZ={_pmxFlipZ.value}");
                sb.AppendLine($"MQO 書出: 比={Fmt(_mqoRatio.value)} FlipX={_mqoFlipX.value} FlipZ={_mqoFlipZ.value}");
                sb.AppendLine();
                sb.AppendLine($"対応した組: {Math.Min(_pairModel.Count, _pairPmx.Count)}");
                sb.AppendLine($"展開数が PMX と食い違った組: {_mismatchMeshes}");
                sb.AppendLine($"差し替えたモデル頂点: {_transferred}");
                sb.AppendLine($"一致しなかった頂点: {_diffVertices}");
                sb.AppendLine();
                sb.AppendLine("── 段ごとのログ ──");
                foreach (var line in PlainLog) sb.Append(line);

                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PmxToMqoTest] レポートを書けなかった: {e.Message}");
            }
        }
    }
}
