// PlayerMqoToPmxTestSubPanel.cs
// MQO位置UV→PMX保存 自動検証。ボタン 1 回で
//   PMX を読む → MQO をソースとして読む（可視オブジェクトのみ）→ 頂点数で対応付け
//   → 対応した分だけ頂点位置（と、指定があれば UV）を差し替える → 結果の検査
//   → PMX を別名で書き出す
//   → 書き出しの検査とレポート書出
// までを流す。段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるための検証か】
//   PMX を土台にして、その一部だけを MQO で作り直したものを戻す作業を通す。
//   対応付けは名前を見ず展開頂点数だけで組む（MQOPartialMatchHelper.cs:297-315）ので、
//   同じ頂点数のオブジェクトが複数あると別のものへ入れ替わる。
//   段 4 で対応表を、段 6 で「対応が付かなかったメッシュが動いていないか」を見る。
//
// 【面にも材質にも触らない】
//   MQO 頂点は非展開形で UV スロットを n 個持ち、モデル側は展開形で 1 頂点 1 UV。
//   MQO 頂点 v のスロット u を、モデルの展開頂点へ MeshExpansion の展開順で配れば
//   位置と UV を差し替えられる（MQOPartialImportOps.ExecuteVertexPositionImport）。
//   頂点数も索引も動かないので、面・材質・ボーンウェイト・頂点IDはそのまま残る。
//   UV を書き込むかどうかはチェックボックスで選ぶ。既定は書き込まない。
//
// 【MQO は生座標のまま読む】
//   MQOPartialMatchHelper.LoadMQO は Scale=1・反転なしで読み込み、座標変換は
//   転送側で掛ける（MQOPartialMatchHelper.cs:234-243）。したがって MQO の読み込みは
//   ImportMqoCommand ではなくこのヘルパーを直接呼ぶ。同期呼び出しなので、
//   読んでいる間は画面が止まるが、待ち直しは発生しない。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;          // RecentPaths
using Poly_Ling.Data;
using Poly_Ling.Diagnostics;   // PLDiag
using Poly_Ling.MQO;
using Poly_Ling.Ops;           // AxisFlip
using Poly_Ling.PMX;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>MQO の頂点位置と UV で PMX を差し替えて別名保存する自動検証。</summary>
    public class PlayerMqoToPmxTestSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext> GetModel;

        /// <summary>PMX を読み込む。実際の import 経路（ImportPmxCommand）へ流す。</summary>
        public Action<string> ImportPmx;

        /// <summary>Undo 記録用。部分インポートパネルと同じ MeshListStack へ積む。</summary>
        public Func<MeshUndoController> GetUndoController;

        /// <summary>PMX を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, PMXExportSettings, PMXExportResult> ExportPmx;

        /// <summary>頂点と面を書き換えたあとの再構築。部分インポート完了時と同じ処理。</summary>
        public Action RefreshAfterReplace;

        // ================================================================
        // RecentPaths のキー（IO パネルと共有する）
        // ================================================================

        private const string PmxPathKey = "Import.PMX.Path";
        private const string MqoPathKey = "Import.MQO.Path";

        /// <summary>保存ダイアログのファイル名欄に出す既定名の接尾辞。</summary>
        private const string OutSuffix = "_frommqo";

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

        private TextField  _pmxPathField, _mqoPathField;
        private FloatField _mqoScale, _smoothingAngle;
        private Toggle     _mqoFlipX, _mqoFlipZ, _mqoFlipUV_V;
        private Toggle     _skipNamedMirror, _recalcNormals, _importUV;
        private EnumField  _normalMode;

        // ================================================================
        // 実行状態
        // ================================================================

        private readonly MQOPartialMatchHelper _helper = new MQOPartialMatchHelper();
        private readonly MQOPartialImportOps   _ops    = new MQOPartialImportOps();

        private string _pmxPath = "";
        private string _mqoPath = "";
        private string _outPath = "";
        private string _reportPath = "";

        private ModelContext _modelBeforeImport;

        private List<PartialMeshEntry> _pairModel = new List<PartialMeshEntry>();
        private List<PartialMQOEntry>  _pairMqo   = new List<PartialMQOEntry>();

        /// <summary>
        /// 差し替え前の姿。対応が付かなかったメッシュが動いていないかを見るために、
        /// 段 5 の直前に全描画オブジェクトの位置と UV を控える。
        /// 検査のためだけに持つ控えで、Undo には使わない。
        /// </summary>
        private readonly Dictionary<MeshContext, Vector3[]> _beforePos = new Dictionary<MeshContext, Vector3[]>();
        private readonly Dictionary<MeshContext, Vector2[]> _beforeUv  = new Dictionary<MeshContext, Vector2[]>();

        private int _transferred;
        private int _changedVertices;
        private PMXExportResult _exportResult;

        // ================================================================
        // 基底が要求するもの
        // ================================================================

        protected override string TitleText => "MQO位置UV→PMX保存 自動検証";

        protected override string NoteText =>
            "PMX 読込 → MQO をソースとして読込（可視オブジェクトのみ）→ 頂点数で対応付け\n"
          + "→ 対応した分だけ頂点位置と UV を差し替え → 結果の検査 → PMX を別名で書き出し\n"
          + "→ 書き出しの検査とレポート書出 を通しで流します。\n"
          + "面・材質・ボーンウェイトには触りません。UV は既定では書き込みません。\n"
          + "段ごとに、手でやるならどこを押すかと、その段が要る理由をログに出します。";

        protected override void BuildOptionsUI(VisualElement root)
        {
            // ── 入出力 ────────────────────────────────────────────────
            root.Add(Sec("入出力"));

            _pmxPathField = new TextField("PMX パス（土台）");
            _pmxPathField.style.fontSize = 10;
            _pmxPathField.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _pmxPathField.RegisterValueChangedCallback(e => SafeSet(PmxPathKey, e.newValue));
            root.Add(_pmxPathField);

            _mqoPathField = new TextField("MQO パス（ソース）");
            _mqoPathField.style.fontSize = 10;
            _mqoPathField.SetValueWithoutNotify(SafeGet(MqoPathKey));
            _mqoPathField.RegisterValueChangedCallback(e => SafeSet(MqoPathKey, e.newValue));
            root.Add(_mqoPathField);

            root.Add(MakeOutDest("出力 PMX の保存先", "pmx", DefaultOutName));

            root.Add(Hint(
                "[...] は書き込み先フォルダを決めるだけです。ファイル名は「実行」を押したときの"
              + "保存ダイアログで決めます。ファイル名欄の初期値は「元の名前" + OutSuffix + ".pmx」です。"
              + "読み込んだ PMX と同じパスは受け付けません（別名保存の検証のため）。"));

            root.Add(Hint(
                "PMX の読み込みには時間が掛かります。待ち直しの上限は 300 秒です。"
              + "MQO の読み込みは同期なので、読んでいる間は画面が止まります。"));

            // ── MQO 読込の座標変換 ───────────────────────────────────
            root.Add(Sec("MQO 読込の座標変換"));
            _mqoScale    = F("Scale", 0.01f);
            _mqoFlipX    = new Toggle("Flip X")    { value = true  };
            _mqoFlipZ    = new Toggle("Flip Z")    { value = false };
            _mqoFlipUV_V = new Toggle("Flip UV V") { value = true  };
            root.Add(_mqoScale); root.Add(_mqoFlipX); root.Add(_mqoFlipZ); root.Add(_mqoFlipUV_V);
            root.Add(Hint(
                "MQO は右手系で、Unity との違いは X の反転だけです。Flip Z は入れません。"
              + "部分インポートパネルの MQO 側と同じ既定です。"));

            // ── 対応付けと差し替えの扱い ─────────────────────────────
            root.Add(Sec("対応付けと差し替え"));
            _skipNamedMirror = new Toggle("名前末尾が + のメッシュを対象から外す") { value = true };
            _importUV        = new Toggle("MQO の UV を書き込む")                 { value = false };
            _recalcNormals   = new Toggle("法線を再計算する")                     { value = true };
            _normalMode      = new EnumField("法線の作り方", NormalMode.Smooth);
            _smoothingAngle  = F("スムージング角度[度]", 60f);
            root.Add(_skipNamedMirror); root.Add(_importUV);
            root.Add(_recalcNormals); root.Add(_normalMode); root.Add(_smoothingAngle);
            root.Add(Hint(
                "対応付けは名前を見ず、展開頂点数の一致だけで組みます。"
              + "同じ頂点数のオブジェクトが複数あると別のものへ入れ替わるので、"
              + "段 4 の対応表を必ず確認してください。"));
            root.Add(Hint(
                "UV は既定では書き込まず、PMX 由来のものをそのまま残します。"
              + "オンにすると MQO 側の UV でモデルの UV を差し替えます。"));
        }

        public override void Refresh()
        {
            base.Refresh();
            if (IsRunning) return;

            _pmxPathField?.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _mqoPathField?.SetValueWithoutNotify(SafeGet(MqoPathKey));
        }

        /// <summary>保存ダイアログのファイル名欄の初期値。土台 PMX の名前から作る。</summary>
        private string DefaultOutName()
        {
            string stem = "";
            try { stem = Path.GetFileNameWithoutExtension(_pmxPathField?.value ?? ""); }
            catch (ArgumentException) { }

            return string.IsNullOrEmpty(stem) ? "" : stem + OutSuffix + ".pmx";
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
            if (ImportPmx == null || GetModel == null || ExportPmx == null)
            { SetStatus("配線が足りません（ImportPmx / GetModel / ExportPmx）。"); return false; }

            _pmxPath = _pmxPathField?.value ?? "";
            _mqoPath = _mqoPathField?.value ?? "";

            if (string.IsNullOrEmpty(_pmxPath) || !File.Exists(_pmxPath))
            { SetStatus("PMX パスが正しくありません。"); return false; }

            if (string.IsNullOrEmpty(_mqoPath) || !File.Exists(_mqoPath))
            { SetStatus("MQO パスが正しくありません。"); return false; }

            // 出力先は毎回ここで確定する（「名前を付けて保存」）。
            // 履歴のフルパスや土台 PMX の名前がそのまま書き込み先になる経路は残さない。
            _outPath = AskOutPath();
            if (string.IsNullOrEmpty(_outPath)) return false;   // キャンセル

            if (SamePath(_outPath, _pmxPath))
            { SetStatus("出力パスが読み込む PMX と同じです。別名にしてください。"); return false; }

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
            _modelBeforeImport = GetModel?.Invoke();

            _pairModel = new List<PartialMeshEntry>();
            _pairMqo   = new List<PartialMQOEntry>();

            _beforePos.Clear();
            _beforeUv.Clear();

            _transferred     = 0;
            _changedVertices = 0;
            _exportResult    = null;

            _reportPath = BuildReportPath("MqoToPmxTest");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. PMX を読み込む",                     StageImportPmx));
            stages.Add(("2. PMX の読み込み完了を待つ",           StageWaitPmxImport));
            stages.Add(("3. MQO をソースとして読み込む",         StageLoadMqoSource));
            stages.Add(("4. 頂点数で対応付ける",                 StageMatch));
            stages.Add(("5. 頂点位置を差し替える",               StageReplace));
            stages.Add(("6. 差し替えの結果を検査する",           StageVerifyReplace));
            stages.Add(("7. PMX を別名で書き出す",               StageExportPmx));
            stages.Add(("8. 書き出しを検査してレポートを書く",   StageVerifyExport));
        }

        protected override void OnFinished(bool aborted)
        {
            if (aborted) WriteReport("段の途中で停止した");
        }

        // ================================================================
        // 段 1-2: PMX を土台として読む
        // ================================================================

        private StageResult StageImportPmx()
        {
            _modelBeforeImport = GetModel?.Invoke();
            ImportPmx(_pmxPath);

            return Ok(
                $"ImportPmxCommand と同じ経路で「{Path.GetFileName(_pmxPath)}」の読み込みを要求した",
                "インポートパネル → PMX → ファイルを選ぶ",
                "こちらが土台。ボーン・ウェイト・材質・モーフは PMX のものを残し、"
              + "MQO と対応が付いたオブジェクトだけを作り直す。読み込みはキュー経由なので"
              + "この段では要求を出すだけで完了を待たない。");
        }

        private StageResult StageWaitPmxImport()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;
            if (ReferenceEquals(model, _modelBeforeImport)) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;

            return Ok(
                $"モデル「{model.Name}」に入れ替わった（MeshContext {model.MeshContextCount} 件）",
                null,
                "ImportPmxCommand は新しい ModelContext を作って差し替える。"
              + "件数ではなく実体が変わったかで見ているので、前回の実行結果が残っていても"
              + "読み込む前に完了したと誤認しない。ここで止まるなら import が走っていない。");
        }

        // ================================================================
        // 段 3: MQO をソースとして読む
        // ================================================================

        private StageResult StageLoadMqoSource()
        {
            // 可視オブジェクトだけを対象にする（SkipHiddenObjects = visibleOnly）。
            // 座標は生のまま保持され、変換は転送側で掛かる。
            bool ok = _helper.LoadMQO(_mqoPath, visibleOnly: true);

            if (!ok || _helper.MQODocument == null)
                return Ng($"MQO の読み込みに失敗した（{Path.GetFileName(_mqoPath)}）",
                    "部分インポートパネル → MQO → ファイルを選ぶ",
                    "パスとファイルの中身を疑う。ここで止まると以降の段は意味を持たない。");

            if (_helper.MQOObjects.Count == 0)
                return Ng("MQO から取り込めるオブジェクトが 0 件だった",
                    null,
                    "非表示オブジェクト・頂点 0 のオブジェクト・ベイク済みミラーは"
                  + "一覧から外れる（MQOPartialMatchHelper.cs:262-270）。"
                  + "MQO 側でオブジェクトを表示状態にしてから読み直す。");

            int verts = 0;
            foreach (var e in _helper.MQOObjects) verts += e.ExpandedVertexCountWithMirror;

            return Ok(
                $"MQO 側の一覧を作った（可視 {_helper.MQOObjects.Count} オブジェクト / 展開後 合計 {verts} 頂点）",
                "部分インポートパネル → MQO → ファイルを選ぶ",
                "MQO の頂点は面ごとに展開すると PMX と同じ数え方になる。"
              + "対応付けはこの展開後の数で行う。ミラー指定のオブジェクトは 2 倍で数える。");
        }

        // ================================================================
        // 段 4: 頂点数で対応付ける
        // ================================================================

        private StageResult StageMatch()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            _helper.BuildModelList(
                model,
                skipBakedMirror: true,
                skipNamedMirror: _skipNamedMirror.value,
                pairMirrors: true);

            if (_helper.ModelMeshes.Count == 0)
                return Ng("差し替え先の描画オブジェクトが 0 件だった",
                    null,
                    "頂点 0 のオブジェクトは一覧から外れる（MQOPartialMatchHelper.cs:86-95）。");

            _helper.AutoMatch();

            _pairModel = _helper.SelectedModelMeshes;
            _pairMqo   = _helper.SelectedMQOObjects;

            if (_pairModel.Count == 0 || _pairMqo.Count == 0)
                return Ng("頂点数の一致する組が 1 つも無かった",
                    "部分インポートパネル → MQO →「Auto」",
                    "対応付けは展開頂点数の一致だけで行う（MQOPartialMatchHelper.cs:297-315）。"
                  + "MQO 側で頂点を足し引きしていると、そのオブジェクトは対応しない。"
                  + "読み込みの Scale や反転は対応付けに影響しない（数しか見ないため）。");

            int pairs = Math.Min(_pairModel.Count, _pairMqo.Count);
            var sb = new StringBuilder();
            for (int p = 0; p < pairs; p++)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(_pairModel[p].Name).Append('←').Append(_pairMqo[p].Name)
                  .Append('(').Append(_pairModel[p].TotalExpandedVertexCount).Append(')');
            }

            int untouched = _helper.ModelMeshes.Count - _pairModel.Count;

            return Ok(
                $"{pairs} 組が一致した: {sb}"
              + (untouched > 0 ? $"（対応が付かなかったモデル側オブジェクト {untouched} 件はそのまま残す）" : ""),
                "部分インポートパネル → MQO →「Auto」で自動対応、リストで手直し",
                "名前を見ない対応付けなので、同じ頂点数のオブジェクトが複数あると"
              + "別のものへ入れ替わる。この表が唯一の確認手段になる。"
              + "対応が付かなかったオブジェクトは触らないのが正しい動き。次の段で機械的に見る。");
        }

        // ================================================================
        // 段 5: 頂点位置（と、指定があれば UV）を差し替える
        // ================================================================

        private StageResult StageReplace()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            CaptureBefore(model);

            var beforeSnapshot = MultiMeshVertexSnapshot.Capture(model);

            _transferred = _ops.ExecuteVertexPositionImport(
                _pairModel, _pairMqo,
                importScale: _mqoScale.value,
                flip:        new AxisFlip(_mqoFlipX.value, _mqoFlipZ.value),
                position:    true,
                uv:          _importUV.value,
                flipUV_V:    _mqoFlipUV_V.value);

            if (_transferred == 0)
                return Ng("差し替えが 1 頂点も走らなかった", null,
                    "対応が付いていても、片側の MeshObject が null なら組は飛ばされる。");

            // 位置を入れ替えたので、そのベースを持つモーフの基準データを追従させる。
            // 頂点数も索引も動かないので old→new のマップは要らない。
            // UV を書き込まなかった場合は UV の基準も動いていないので触らない。
            PMXPartialImportOps.RemapMorphBasesAfterVertexChange(
                _pairModel, model, remapPosition: true, remapUV: _importUV.value);

            if (_recalcNormals.value)
            {
                foreach (var e in _pairModel)
                {
                    var mo = e.Context?.MeshObject;
                    if (mo != null) _ops.RecalculateNormals(mo, (NormalMode)_normalMode.value, _smoothingAngle.value);

                    var peer = e.BakedMirrorPeer?.Context?.MeshObject;
                    if (peer != null) _ops.RecalculateNormals(peer, (NormalMode)_normalMode.value, _smoothingAngle.value);
                }
            }

            // Undo は部分インポートパネルと同じ MeshListStack へ積む。
            // 記録は頂点座標のみなので UV の差し替えは戻らない。
            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var after  = MultiMeshVertexSnapshot.Capture(model);
                var label  = "MQO Partial Import";
                var record = new MultiMeshVertexSnapshotRecord(beforeSnapshot, after, label);
                PLDiag.UndoRecord("MeshList", label, record);
                undo.MeshListStack.Record(record, label);
            }

            RefreshAfterReplace?.Invoke();

            return Ok(
                $"{_transferred} 頂点ぶん位置を差し替えた"
              + $"（UV={(_importUV.value ? "MQO で上書き" : "PMX 由来のまま")} / "
              + $"法線={(_recalcNormals.value ? _normalMode.value.ToString() : "そのまま")}）",
                "部分インポートパネル → MQO →「頂点位置」をオンにして Import",
                "MQO 頂点を、モデル側の展開頂点へ MeshExpansion の展開順で配る。"
              + "面・材質・ボーンウェイトには触らないので、PMX 側の情報はそのまま残る。"
              + "モーフの基準データは位置の差分で持っているため、ベースを動かしたら"
              + "一緒に付け替えないとモーフが元の形へ引き戻す。");
        }

        /// <summary>差し替え前の位置と UV を控える。検査専用で Undo には使わない。</summary>
        private void CaptureBefore(ModelContext model)
        {
            _beforePos.Clear();
            _beforeUv.Clear();

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                var mo  = ctx?.MeshObject;
                if (mo == null) continue;

                var pos = new Vector3[mo.VertexCount];
                var uv  = new Vector2[mo.VertexCount];
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    pos[v] = mo.Vertices[v].Position;
                    uv[v]  = (mo.Vertices[v].UVs.Count > 0) ? mo.Vertices[v].UVs[0] : Vector2.zero;
                }
                _beforePos[ctx] = pos;
                _beforeUv[ctx]  = uv;
            }
        }

        // ================================================================
        // 段 6: 差し替えの結果を検査
        // ================================================================

        private StageResult StageVerifyReplace()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            // 差し替えた側の集合。ミラーのペアも触られるのでここへ入れる。
            var touched = new HashSet<MeshContext>();
            foreach (var e in _pairModel)
            {
                if (e.Context != null) touched.Add(e.Context);
                var peer = e.BakedMirrorPeer?.Context;
                if (peer != null) touched.Add(peer);
            }

            // ── 対応が付かなかったメッシュが動いていないか ────────────
            var movedNames = new List<string>();
            var drawables  = model.DrawableMeshes;
            if (drawables != null)
            {
                for (int i = 0; i < drawables.Count; i++)
                {
                    var ctx = drawables[i].Context;
                    var mo  = ctx?.MeshObject;
                    if (mo == null || touched.Contains(ctx)) continue;
                    if (!_beforePos.TryGetValue(ctx, out var pos)) continue;
                    if (!_beforeUv.TryGetValue(ctx, out var uv))   continue;

                    if (mo.VertexCount != pos.Length) { movedNames.Add(ctx.Name); continue; }

                    for (int v = 0; v < mo.VertexCount; v++)
                    {
                        var p  = mo.Vertices[v].Position;
                        var t  = (mo.Vertices[v].UVs.Count > 0) ? mo.Vertices[v].UVs[0] : Vector2.zero;
                        if ((p - pos[v]).sqrMagnitude > 0f || (t - uv[v]).sqrMagnitude > 0f)
                        { movedNames.Add(ctx.Name); break; }
                    }
                }
            }

            if (movedNames.Count > 0)
                return Ng(
                    $"対応が付かなかったのに変わったオブジェクトがある: {string.Join(", ", movedNames)}",
                    null,
                    "頂点数だけで組むので、無関係なオブジェクトへ流し込むのがいちばん怖い失敗。"
                  + "段 4 の対応表と、MQO 側のオブジェクト名を見比べる。");

            // ── 差し替えた側がどれだけ変わったか ─────────────────────
            _changedVertices = 0;
            int checkedVerts = 0;
            var shape = new StringBuilder();

            foreach (var ctx in touched)
            {
                var mo = ctx?.MeshObject;
                if (mo == null) continue;

                if (shape.Length > 0) shape.Append(" / ");
                shape.Append(ctx.Name).Append('(')
                     .Append(mo.VertexCount).Append('v').Append(',')
                     .Append(mo.FaceCount).Append("f)");

                if (!_beforePos.TryGetValue(ctx, out var pos)) continue;
                if (!_beforeUv.TryGetValue(ctx, out var uv))   continue;

                int n = Math.Min(mo.VertexCount, pos.Length);
                for (int v = 0; v < n; v++)
                {
                    var p = mo.Vertices[v].Position;
                    var t = (mo.Vertices[v].UVs.Count > 0) ? mo.Vertices[v].UVs[0] : Vector2.zero;
                    if ((p - pos[v]).sqrMagnitude > 0f || (t - uv[v]).sqrMagnitude > 0f) _changedVertices++;
                }
                checkedVerts += n;
            }

            if (_changedVertices == 0)
                return Ng(
                    $"{checkedVerts} 頂点を比べて 1 つも変わっていなかった",
                    null,
                    "差し替えは走ったのに値が同じなら、読み込みの Scale と反転が"
                  + "元の PMX と揃っていて、MQO 側が実は変わっていない。"
                  + "MQO を編集したつもりのファイルが別物でないか確かめる。");

            return Ok(
                $"対応が付かなかったオブジェクトは 1 頂点も動いていない。"
              + $"差し替えた側は {checkedVerts} 頂点中 {_changedVertices} 頂点が変わった。"
              + $"差し替え後の形: {shape}",
                null,
                "頂点数が変わっていないなら、PMX 側のウェイト・材質割当は引き継がれている。"
              + "変わっている場合は、MQO のミラー指定と『ミラーをベイク』の食い違いを疑う。");
        }

        // ================================================================
        // 段 7-8: PMX を別名で書き出す
        // ================================================================

        private StageResult StageExportPmx()
        {
            var settings = PMXExportSettings.CreateFullExport();
            _exportResult = ExportPmx(_outPath, settings);

            if (_exportResult == null || !_exportResult.Success)
                return Ng($"書き出しに失敗した: {_exportResult?.ErrorMessage}",
                    "エクスポートパネル → PMX → 保存先を選ぶ",
                    "書き出し経路はエクスポートパネルと同じ。失敗するならモデル側ではなく"
                  + "出力先か設定を疑う。");

            return Ok(
                $"「{Path.GetFileName(_outPath)}」へ書き出した"
              + $"（頂点 {_exportResult.VertexCount} / 面 {_exportResult.FaceCount} / "
              + $"材質 {_exportResult.MaterialCount} / ボーン {_exportResult.BoneCount} / "
              + $"モーフ {_exportResult.MorphCount}）",
                "エクスポートパネル → PMX → 保存先を選ぶ",
                "読み込んだ PMX は上書きしない。元と見比べられるように別名で出す。"
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
              + "対応表と変化した頂点数が載っているので、あとから追える。");
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
                sb.AppendLine("MQO位置UV→PMX保存 自動検証");
                sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("結果: " + outcome);
                sb.AppendLine();
                sb.AppendLine("PMX (土台): " + _pmxPath);
                sb.AppendLine("MQO (ソース): " + _mqoPath);
                sb.AppendLine("PMX (出力): " + _outPath);
                sb.AppendLine();
                sb.AppendLine($"MQO 読込: Scale={Fmt(_mqoScale.value)} FlipX={_mqoFlipX.value} "
                            + $"FlipZ={_mqoFlipZ.value} FlipUV_V={_mqoFlipUV_V.value}");
                sb.AppendLine($"名前末尾+を除外={_skipNamedMirror.value} / "
                            + $"MQO の UV を書き込む={_importUV.value}");
                sb.AppendLine($"法線: {(_recalcNormals.value ? _normalMode.value.ToString() : "そのまま")} "
                            + $"角度={Fmt(_smoothingAngle.value)}");
                sb.AppendLine();
                sb.AppendLine($"対応した組: {Math.Min(_pairModel.Count, _pairMqo.Count)}");
                sb.AppendLine($"差し替えた頂点: {_transferred}");
                sb.AppendLine($"変わった頂点: {_changedVertices}");
                sb.AppendLine();
                sb.AppendLine("── 段ごとのログ ──");
                foreach (var line in PlainLog) sb.Append(line);

                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MqoToPmxTest] レポートを書けなかった: {e.Message}");
            }
        }
    }
}
