// PlayerBeltGroupTestSubPanelBase.cs
// 「基準ベルト＋断面プロファイル＋出力先」をオブジェクトグループとして残す一連の手順を
// ボタン 1 回で流す検証パネルの共通部。フリル版・パイプ版が派生する。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ要るか】
//   オブジェクトグループは「入力を直すと出力先も作り直せる」枠組みだが、
//   使うには 図形生成パネル の梯子取り込み・断面編集・「グループとして残す」
//   の 3 つを順に触る必要があり、初見では手順が分からない。
//   同じことをコマンドだけで通す経路を置いて、手順そのものをログにする。
//
// 【実コマンドを送る理由と段の区切り】
//   PlayerStagedTestSubPanelBase の冒頭を参照。段ランナーと 3 行ログも
//   そちらが持つ。ここが持つのは帯まわりの共通段だけ。
//
// 【派生が決めること】
//   ・ソース形状の作り方（円筒 / 四分球）とその検査
//   ・断面プロファイルの点列と、閉ループか否か
//   ・生成コマンドの組み立て（CreateFrillCommand / CreatePipeCommand）
//   ・ソースの編集内容（作り直しの検証用）
//   共通部が持つのは、段ランナー・ログ・プロファイルのオブジェクト化と取り込み・
//   グループの検査・要更新の検査・作り直しとその検査。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 帯グループ検証の共通部。人間の操作は「実行」を押すだけ。
    /// 段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
    /// </summary>
    public abstract class PlayerBeltGroupTestSubPanelBase : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>プロジェクト。グループの参照解決がモデルをまたぐため要る。</summary>
        public Func<ProjectContext> GetProject;

        /// <summary>現在のモデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>現在のモデル索引。</summary>
        public Func<int> GetModelIndex;

        /// <summary>コマンド送信。パネルが押されたときと同じ経路へ流す。</summary>
        public Action<PanelCommand> SendCommand;

        protected int ModelIndex => GetModelIndex?.Invoke() ?? 0;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        /// <summary>ソース（梯子の元）となる描画オブジェクトの名前。</summary>
        protected abstract string SourceObjectName { get; }

        /// <summary>
        /// 断面プロファイルの描画オブジェクトの名前。
        /// 断面を使わない図形（藤壺）の派生では読まれない。
        /// </summary>
        protected abstract string ProfileObjectName { get; }

        /// <summary>断面が閉ループか。閉ループは ExtractLoops、開いた折れ線は ExtractPolyline で読む。</summary>
        protected abstract bool ProfileIsClosed { get; }

        /// <summary>検査で照合する生成コマンドの action 名（"createFrill" / "createPipe"）。</summary>
        protected abstract string ExpectedAction { get; }

        /// <summary>断面プロファイルの点列（正規化前）。</summary>
        protected abstract List<Vector2> BuildProfilePoints();

        // 段を並べるのは派生。共通段は Stage〜 のメソッドをそのまま足せばよい。
        // 断面プロファイルを使わない図形（藤壺）は StageCreateProfileObject /
        // StageWaitProfile / StageImportProfile を足さない。
        // その場合 ProfileObjectName / ProfileIsClosed / BuildProfilePoints は読まれない。

        /// <summary>作り直しの検証用にソースを編集する。</summary>
        /// <summary>作り直しの検証用にソースを編集する。</summary>
        protected abstract StageResult StageEditSource();

        // ================================================================
        // 段をまたいで持ち回る値（派生からも読む）
        // ================================================================

        /// <summary>ソースオブジェクトの masterIndex。</summary>
        protected int SourceIndex = -1;

        /// <summary>プロファイルオブジェクトの masterIndex。</summary>
        protected int ProfileIndex = -1;

        /// <summary>取り込んで正規化した断面。</summary>
        protected List<Vector2> ProfilePoints;

        /// <summary>作り直す前の出力先 ObjectId。作り直しても変わらないことを検査する。</summary>
        protected ulong OutputIdBefore;

        /// <summary>作り直す前の出力先の頂点数。空になっていないかの検査に使う。</summary>
        protected int OutputVertsBefore;

        /// <summary>出来たグループの名前。</summary>
        protected string GroupName;

        /// <summary>直前に控えたオブジェクト数。完了待ちの判定に使う。</summary>
        protected int MeshCountBefore;

        // ================================================================
        // UI
        // ================================================================

        /// <summary>作り直しのときに退避を残すか。派生の設定 UI の下に置く。</summary>
        /// <remarks>UI 自動操作の属性はフィールドに付けるので、明示のフィールドで持つ。</remarks>
        [UiControl("keepStash", Description = "作り直す前の出力先を退避として残す")]
        private Toggle _keepStashToggle;

        protected Toggle KeepStashToggle => _keepStashToggle;

        /// <summary>
        /// 退避トグルを足す。派生の BuildOptionsUI の末尾で呼ぶこと。
        /// </summary>
        protected void AddKeepStashToggle(VisualElement root)
        {
            _keepStashToggle = new Toggle("作り直す前の出力先を退避として残す") { value = true };
            root.Add(_keepStashToggle);
        }

        protected override void ResetRunState()
        {
            SourceIndex       = -1;
            ProfileIndex      = -1;
            ProfilePoints     = null;
            OutputIdBefore    = 0UL;
            OutputVertsBefore = 0;
            GroupName         = null;
            MeshCountBefore   = 0;
        }

        // ================================================================
        // 共通段: 断面プロファイル
        // ================================================================

        /// <summary>
        /// 断面プロファイルを「2頂点ラインだけの描画オブジェクト」として置く。
        ///
        /// 断面は Vector2[] でコマンドに載るが、目で見て直せるようにするため
        /// いったん描画オブジェクトにする。図形生成パネルの断面編集にある
        /// 「反映(→メッシュ)」と同じ経路（LineProfileExtractor.PolylineToLineMesh）。
        /// </summary>
        protected StageResult StageCreateProfileObject()
        {
            var pts = BuildProfilePoints();
            if (pts == null || pts.Count < 2)
                return Ng("断面の点列を作れなかった", null, null);

            var mo = LineProfileExtractor.PolylineToLineMesh(pts, ProfileObjectName, ProfileIsClosed);
            if (mo == null || mo.FaceCount == 0)
                return Ng("線メッシュを作れなかった", null, null);

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = false;

            SendCommand(new AddGeneratedMeshCommand(
                ModelIndex, mo, ProfileObjectName, pl, poseAlreadyBaked: true));

            return Ok(
                $"「{ProfileObjectName}」を 2 頂点ライン {mo.FaceCount} 本で置いた"
              + $"（{(ProfileIsClosed ? "閉ループ" : "開いた折れ線")} / {FormatPoints(pts)}）",
                "図形生成パネル → 断面プロファイル欄で点を打つ → 「反映(→メッシュ)」",
                "断面はコマンド上は Vector2 の点列だが、そのままでは目で確かめられない。"
              + "2 頂点だけの面（＝補助線）を持つ描画オブジェクトにしておくと、"
              + "ビューポートで形を見ながら頂点を動かして直せる。"
              + "x が rung 方向、y が基準ベルト面の法線方向。");
        }

        protected StageResult StageWaitProfile()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;

            ProfileIndex = FindByName(model, ProfileObjectName);
            if (ProfileIndex < 0) return StageResult.Retry;

            var mo = model.GetMeshContext(ProfileIndex)?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            MeshCountBefore = model.MeshContextCount;

            return Ok($"「{ProfileObjectName}」が出来た（索引 {ProfileIndex} / 頂点 {mo.VertexCount}）",
                      null, null);
        }

        /// <summary>
        /// 置いた描画オブジェクトから断面を読み戻す。
        /// 図形生成パネルの「取り込み(メッシュ→断面)」と同じ経路。
        /// </summary>
        protected StageResult StageImportProfile()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(ProfileIndex)?.MeshObject;
            if (mo == null) return Ng("プロファイルオブジェクトが見つからない", null, null);

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mo);
            if (lineFaces.Count == 0)
                return Ng("2 頂点ラインが 1 本も無い", null,
                    "取り込みは「頂点 2 個だけの面」を線とみなして拾う。"
                  + "三角形や四角形の面しか無いオブジェクトからは断面を作れない。");

            List<Vector2> raw = null;
            if (ProfileIsClosed)
            {
                // 閉ループ断面。複数ループがあれば点数が最多のものを採る。
                var loops = LineProfileExtractor.ExtractLoops(mo, lineFaces);
                if (loops != null)
                {
                    foreach (var lp in loops)
                    {
                        if (lp?.Points == null || lp.Points.Count < 3) continue;
                        if (raw == null || lp.Points.Count > raw.Count) raw = lp.Points;
                    }
                }
            }
            else
            {
                raw = LineProfileExtractor.ExtractPolyline(mo, lineFaces);
            }

            if (raw == null || raw.Count < 2)
                return Ng("線をつなげて断面にできなかった", null,
                    ProfileIsClosed
                        ? "閉ループとして読むので、線が輪になっている必要がある。"
                        : "開いた折れ線として読むので、線が一本につながっている必要がある。");

            ProfilePoints = LineProfileExtractor.NormalizeToUnitSpan(raw);
            if (ProfilePoints == null)
                return Ng("断面がつぶれている（全点が同じ位置）", null, null);

            return Ok(
                $"線 {lineFaces.Count} 本 → {(ProfileIsClosed ? "ループ" : "折れ線")} {raw.Count} 点 "
              + $"→ 正規化 {FormatPoints(ProfilePoints)}",
                "図形生成パネル → 断面プロファイル欄 → 「取り込み(メッシュ→断面)」"
              + "（取り込み元はオブジェクト一覧で選択中のもの）",
                "断面座標は rung 長で正規化された系にある。描画オブジェクトから読んだ点列は"
              + "モデルのローカル座標そのままなので、そのまま使うと寸法が合わない。"
              + "長辺が 1 になるよう等方スケールし、AABB の最小角を原点へ寄せている"
              + "（LineProfileExtractor.NormalizeToUnitSpan）。等方なので縦横比は保たれる。"
              + "Z は捨てて XY だけ使う。");
        }

        // ================================================================
        // 共通段: グループの検査 → ソース編集 → 作り直し
        // ================================================================

        protected StageResult StageVerifyGroup()
        {
            var project = GetProject();
            var model   = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.ObjectGroupCount == 0) return StageResult.Retry;

            ObjectGroup g = null;
            foreach (var cand in model.ObjectGroups)
                if (cand != null && cand.Action == ExpectedAction) g = cand;
            if (g == null) return StageResult.Retry;

            var outCtx = ObjectGroupOps.Resolve(project, g.OutputObjectId);
            if (outCtx == null) return StageResult.Retry;

            GroupName      = g.Name;
            OutputIdBefore = g.OutputObjectId;

            if (ObjectGroupOps.IsStale(project, g))
                return Ng("作った直後なのに『要更新』になっている", null,
                    "ダイジェストは記録のときに取る。直後に食い違うなら、"
                  + "取ったあとにソースを触る経路があるということ。");

            var srcIds = g.GetMeshRefIds("beltSourceIndex");

            return Ok(
                $"グループ「{g.Name}」が出来た。出力先「{outCtx.Name}」（ObjectId={g.OutputObjectId} / "
              + $"頂点 {outCtx.MeshObject?.VertexCount ?? 0}）、入力 {g.SourceCount} 件、"
              + $"パラメータ {g.Args.Count} 件、beltSourceIndex の控え {srcIds.Count} 件、要更新=いいえ",
                "右ペイン → 「オブジェクトグループ」ボタンで同じ内容が見られる",
                "グループが持つのは 3 つ。(1) 生成コマンドの action と全パラメータ（文字列）、"
              + "(2) 索引で指していたオブジェクトの ObjectId、(3) 出力先の ObjectId。"
              + "索引ではなく ObjectId で控えるので、並べ替えや追加削除をしても"
              + "同じオブジェクトを指し続ける。複製したものには新しい ID が振られるので、"
              + "複製物がグループのメンバーになることもない。");
        }

        protected StageResult StageVerifyStale()
        {
            var project = GetProject();
            var model   = GetModel();
            var g       = model?.FindObjectGroupByName(GroupName);
            if (g == null) return Ng("グループが見つからない", null, null);

            if (!ObjectGroupOps.IsStale(project, g))
                return Ng("ソースを動かしたのに『要更新』にならない", null,
                    "ダイジェストは頂点ID と位置（1e-5 丸め）から作る。"
                  + "動かした量が丸めより小さいと検出できない。");

            return Ok(
                "『要更新』になった",
                "右ペイン → 「オブジェクトグループ」を開くと、その行が橙色で [要更新] と出る",
                "判定はソースの現在の中身から取り直したダイジェストと、"
              + "前回ビルド時に控えたダイジェストの比較。"
              + "全頂点を走査するので、毎フレームではなくパネルを開いたときと"
              + "作り直しを要求したときにだけ行う。編集のたびに印を立てるフックは置いていない。");
        }

        protected StageResult StageRebuild()
        {
            var project = GetProject();
            var model   = GetModel();
            var gBefore = model?.FindObjectGroupByName(GroupName);
            var outBefore = gBefore != null ? ObjectGroupOps.Resolve(project, gBefore.OutputObjectId) : null;

            OutputIdBefore    = gBefore?.OutputObjectId ?? 0UL;
            OutputVertsBefore = outBefore?.MeshObject?.VertexCount ?? 0;

            bool keepStash = KeepStashToggle != null && KeepStashToggle.value;
            SendCommand(new RebuildObjectGroupCommand(ModelIndex, GroupName, keepStash));

            return Ok(
                $"RebuildObjectGroupCommand を送った（退避={(keepStash ? "する" : "しない")} / "
              + $"作り直す前の出力先の頂点数 {OutputVertsBefore}）。"
              + $"梯子の扱い: {ObjectGroupOps.LastBeltMessage ?? "(記録なし)"}",
                "右ペイン → 「オブジェクトグループ」→ 行を選んで「作り直す」",
                "作り直しは 4 段階。(1) 控えた Args を複製、(2) ObjectId から今の索引へ引き直す、"
              + "(3) 記録した取り込み方をソースへ掛け直して梯子の点列を差し替える、"
              + "(4) 組み直したコマンドを実行。"
              + "(2) が (3) より先でないと、梯子の取り込み元索引が古いままになり"
              + "別のオブジェクトから取り込む。"
              + "出力先は新しく作らず、既存のオブジェクトの中身だけ入れ替える"
              + "（ObjectId・名前・階層・姿勢・材質割当がそのまま残る）。"
              + "退避は入れ替える前の複製。出力先へ手で振った頂点IDは総入れ替えで消えるので、"
              + "出力先にIDを振るなら先にグループを解除すること。");
        }

        protected StageResult StageVerifyRebuild()
        {
            var project = GetProject();
            var model   = GetModel();
            var g       = model?.FindObjectGroupByName(GroupName);
            if (g == null) return Ng("グループが見つからない", null, null);

            var outCtx = ObjectGroupOps.Resolve(project, g.OutputObjectId);
            if (outCtx?.MeshObject == null)
                return Ng("出力先を引けない", null,
                    "作り直しは出力先の中身だけを入れ替えるので、ObjectId は変わらないはず。"
                  + "引けないなら別の経路で作り直されている。");

            if (g.OutputObjectId != OutputIdBefore)
                return Ng($"出力先の ObjectId が変わった（{OutputIdBefore} → {g.OutputObjectId}）", null,
                    "作り直しは既存オブジェクトへの書き戻し（ReplaceExisting）で行う。"
                  + "ID が変わったなら新規作成の経路を通っている。");

            int verts = outCtx.MeshObject.VertexCount;
            if (verts == 0)
                return Ng("作り直した出力先が空になった", null,
                    "梯子の取り直しが失敗しているか、断面が壊れている。"
                  + "この段の 1 つ前のログに梯子の扱いが出ている。");

            if (ObjectGroupOps.IsStale(project, g))
                return Ng("作り直したのに『要更新』のまま", null,
                    "作り直しの最後にダイジェストを取り直している。残るなら"
                  + "取り直しの前にソースがまた変わったということ。");

            string stashText = "なし";
            if (g.HasStash)
            {
                var stash = ObjectGroupOps.Resolve(project, g.StashObjectId);
                stashText = stash != null
                    ? $"「{stash.Name}」（頂点 {stash.MeshObject?.VertexCount ?? 0}）"
                    : "参照切れ";
            }

            return Ok(
                $"出力先「{outCtx.Name}」の中身が入れ替わった（頂点 {OutputVertsBefore} → {verts} / "
              + $"ObjectId={g.OutputObjectId} は不変）。退避 {stashText}、要更新=いいえ",
                null,
                "出力先を作り直さないので、名前・階層・姿勢・材質割当と、"
              + "出力先を指している参照がそのまま残る。"
              + "退避は入れ替える前の複製で、頂点ID・パーツID・サブIDを保つ。"
              + "出力先へ手でIDを振るなら、先にグループを解除すること"
              + "（グループがある間は作り直しで総入れ替えされる）。");
        }

        // ================================================================
        // 共通の小物
        // ================================================================

        /// <summary>名前で描画オブジェクトの masterIndex を引く。無ければ -1。</summary>
        protected static int FindByName(ModelContext model, string name)
        {
            if (model == null) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.Name == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// 位置から頂点番号を引く表を作る。
        /// 面や頂点を足したあとは番号が動きうるので、番号を跨いで持ち回らず
        /// 使う直前にここで引き直す。
        /// </summary>
        protected static Dictionary<Vector3Int, int> BuildPositionIndex(MeshObject mo, float unit = 1e-4f)
        {
            var map = new Dictionary<Vector3Int, int>();
            if (mo == null) return map;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var key = Quantize(mo.Vertices[i].Position, unit);
                if (!map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        protected static Vector3Int Quantize(Vector3 p, float unit = 1e-4f)
            => new Vector3Int(
                Mathf.RoundToInt(p.x / unit),
                Mathf.RoundToInt(p.y / unit),
                Mathf.RoundToInt(p.z / unit));

        protected static string FormatPoints(IReadOnlyList<Vector2> pts)
        {
            if (pts == null || pts.Count == 0) return "(なし)";
            var sb = new StringBuilder();
            int n = Mathf.Min(pts.Count, 10);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append('(')
                  .Append(pts[i].x.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                  .Append(pts[i].y.ToString("0.###", CultureInfo.InvariantCulture)).Append(')');
            }
            if (pts.Count > n) sb.Append(" …計").Append(pts.Count).Append('点');
            return sb.ToString();
        }

    }
}
