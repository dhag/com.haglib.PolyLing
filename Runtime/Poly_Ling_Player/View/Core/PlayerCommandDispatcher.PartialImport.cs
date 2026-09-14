// PlayerCommandDispatcher.PartialImport.cs
// MQO を元にした部分差し替え（読み込み・対応付け・転送）の処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【毎回読み直す】
//   MQOPartialMatchHelper は使い捨てにする。同じファイル・同じ visibleOnly なら
//   MQOObjects の並びは何度読んでも同じなので、組を索引で受け渡せる。
//   実行の途中状態を持つ器を置かない。
//
// 【組は索引で受ける】
//   AutoMatch は両側に Selected を立てるだけで組を記録しない。
//   ExecuteVertexPositionImport が i 番目どうしを転送する約束なので、
//   ここでは受け取った modelIndices / mqoIndices の順にリストを組み直してから渡す。
//   組み替えはその並びを変えるだけで表せる。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.MQO;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>MQO 部分差し替えのコマンドなら処理して true を返す。</summary>
        private bool DispatchPartialImport(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case QueryMqoSourceObjectsCommand c:
                {
                    var helper = new MQOPartialMatchHelper();
                    if (!helper.LoadMQO(c.MqoPath, c.VisibleOnly) || helper.MQODocument == null)
                    { Fail($"MQO を読めません: {c.MqoPath}"); return true; }

                    var names = new List<string>();
                    var verts = new List<int>();
                    var mirr  = new List<int>();
                    foreach (var e in helper.MQOObjects)
                    {
                        names.Add(e.Name ?? "");
                        verts.Add(e.ExpandedVertexCountWithMirror);
                        mirr.Add(e.IsMirrored ? 1 : 0);
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("count",                helper.MQOObjects.Count)
                        .Texts("names",                names)
                        .Ints ("expandedVertexCounts", verts)
                        .Ints ("mirrored",             mirr)
                        .Build());
                    return true;
                }

                case MatchMqoSourceByVertexCountCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (!TryBuildMatchHelper(model, c.MqoPath, c.VisibleOnly,
                                             c.SkipNamedMirror, c.PairMirrors,
                                             out var helper, out string reason))
                    { Fail(reason); return true; }

                    helper.AutoMatch();

                    // AutoMatch は組を記録しない。両リストを頭から拾った順が組になる。
                    var pickedModel = new List<PartialMeshEntry>();
                    foreach (var e in helper.ModelMeshes) if (e.Selected) pickedModel.Add(e);

                    var pickedMqo = new List<PartialMQOEntry>();
                    foreach (var e in helper.MQOObjects) if (e.Selected) pickedMqo.Add(e);

                    int pairs = System.Math.Min(pickedModel.Count, pickedMqo.Count);

                    var mIdx   = new List<int>();
                    var qIdx   = new List<int>();
                    var mNames = new List<string>();
                    var qNames = new List<string>();
                    var counts = new List<int>();

                    for (int p = 0; p < pairs; p++)
                    {
                        mIdx.Add(pickedModel[p].Index);
                        qIdx.Add(helper.MQOObjects.IndexOf(pickedMqo[p]));
                        mNames.Add(pickedModel[p].Name ?? "");
                        qNames.Add(pickedMqo[p].Name ?? "");
                        counts.Add(pickedModel[p].TotalExpandedVertexCount);
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("pairs",        pairs)
                        .Int  ("unmatched",    helper.ModelMeshes.Count - pairs)
                        .Ints ("modelIndices", mIdx)
                        .Ints ("mqoIndices",   qIdx)
                        .Texts("modelNames",   mNames)
                        .Texts("mqoNames",     qNames)
                        .Ints ("vertexCounts", counts)
                        .Build());
                    return true;
                }

                case ImportMqoVertexPositionsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (c.ModelIndices.Length != c.MqoIndices.Length)
                    { Fail($"modelIndices が {c.ModelIndices.Length} 件、mqoIndices が {c.MqoIndices.Length} 件で長さが合いません"); return true; }

                    if (c.ModelIndices.Length == 0)
                    { Fail("組が 1 つもありません"); return true; }

                    if (!TryBuildMatchHelper(model, c.MqoPath, c.VisibleOnly,
                                             c.SkipNamedMirror, c.PairMirrors,
                                             out var helper, out string reason))
                    { Fail(reason); return true; }

                    // 受け取った順にリストを組み直す。並びがそのまま組になる。
                    var pairModel = new List<PartialMeshEntry>();
                    var pairMqo   = new List<PartialMQOEntry>();

                    for (int p = 0; p < c.ModelIndices.Length; p++)
                    {
                        var me = helper.ModelMeshes.Find(x => x.Index == c.ModelIndices[p]);
                        if (me == null)
                        { Fail($"差し替え先が見つかりません（描画オブジェクト索引 {c.ModelIndices[p]}）。対応付けと同じ条件で呼んでいるか確かめること"); return true; }

                        int qi = c.MqoIndices[p];
                        if (qi < 0 || qi >= helper.MQOObjects.Count)
                        { Fail($"MQO 側の番号が範囲外です（{qi} / 0..{helper.MQOObjects.Count - 1}）"); return true; }

                        pairModel.Add(me);
                        pairMqo.Add(helper.MQOObjects[qi]);
                    }

                    var ops = new MQOPartialImportOps();
                    int transferred = ops.ExecuteVertexPositionImport(
                        pairModel, pairMqo,
                        importScale: c.ImportScale,
                        flip:        new Poly_Ling.Ops.AxisFlip(c.FlipX, c.FlipZ),
                        position:    true,
                        uv:          c.ImportUV,
                        flipUV_V:    c.FlipUV_V);

                    if (transferred == 0)
                    { Fail("差し替えが 1 頂点も走りませんでした"); return true; }

                    // 位置を入れ替えたので、そのベースを持つモーフの基準を追従させる。
                    // 頂点数も索引も動かないので old→new のマップは要らない。
                    Poly_Ling.PMX.PMXPartialImportOps.RemapMorphBasesAfterVertexChange(
                        pairModel, model, remapPosition: true, remapUV: c.ImportUV);

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);

                    ReportData(CommandDataJson.New()
                        .Int("pairs",       pairModel.Count)
                        .Int("transferred", transferred)
                        .Build());
                    return true;
                }

                default: return false;
            }
        }

        /// <summary>PMX 部分差し替えのコマンドなら処理して true を返す。</summary>
        private bool DispatchPmxPartialImport(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case QueryPmxSourceObjectsCommand c:
                {
                    if (!TryLoadPmxOps(null, c.PmxPath, c.Scale, c.FlipX, c.FlipZ,
                                       out var ops, out string reason))
                    { Fail(reason); return true; }

                    var names = new List<string>();
                    var verts = new List<int>();
                    foreach (var e in ops.PMXMeshes)
                    {
                        names.Add(e.Name ?? "");
                        verts.Add(e.VertexCount);
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("count",        ops.PMXMeshes.Count)
                        .Texts("names",        names)
                        .Ints ("vertexCounts", verts)
                        .Build());
                    return true;
                }

                case MatchPmxSourceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (!TryLoadPmxOps(model, c.PmxPath, c.Scale, c.FlipX, c.FlipZ,
                                       out var ops, out string reason))
                    { Fail(reason); return true; }

                    ops.AutoMatch();

                    // AutoMatch は組を記録しない。両リストを頭から拾った順が組になる。
                    var pickedModel = new List<PartialMeshEntry>();
                    foreach (var e in ops.ModelMeshes) if (e.Selected) pickedModel.Add(e);

                    var pickedPmx = new List<Poly_Ling.PMX.PartialPMXEntry>();
                    foreach (var e in ops.PMXMeshes) if (e.Selected) pickedPmx.Add(e);

                    int pairs = System.Math.Min(pickedModel.Count, pickedPmx.Count);

                    var mIdx     = new List<int>();
                    var xIdx     = new List<int>();
                    var mNames   = new List<string>();
                    var xNames   = new List<string>();
                    var mCounts  = new List<int>();
                    var xCounts  = new List<int>();
                    int mismatch = 0;

                    for (int p = 0; p < pairs; p++)
                    {
                        mIdx.Add(pickedModel[p].Index);
                        xIdx.Add(pickedPmx[p].Index);
                        mNames.Add(pickedModel[p].Name ?? "");
                        xNames.Add(pickedPmx[p].Name ?? "");

                        // 比べるのはモデル側の展開後頂点数と PMX の実頂点数。
                        // PMX は 1 頂点に UV を 1 つしか持てないので、UV の切れ目で
                        // 頂点が分かれている。MeshObject は 1 頂点に複数の UV を
                        // 持てるため、生の頂点数どうしは一致しないのが正常。
                        // ExpandedVertexCount と VertexCount を取り違えないこと。
                        int me = pickedModel[p].ExpandedVertexCount;
                        int xe = pickedPmx[p].VertexCount;
                        mCounts.Add(me);
                        xCounts.Add(xe);
                        if (me != xe) mismatch++;
                    }

                    ReportData(CommandDataJson.New()
                        .Int  ("pairs",               pairs)
                        .Int  ("unmatched",           ops.ModelMeshes.Count - pairs)
                        .Int  ("mismatchedCounts",    mismatch)
                        .Ints ("modelIndices",        mIdx)
                        .Ints ("pmxIndices",          xIdx)
                        .Texts("modelNames",          mNames)
                        .Texts("pmxNames",            xNames)
                        .Ints ("modelExpandedCounts", mCounts)
                        .Ints ("pmxVertexCounts",     xCounts)
                        .Build());
                    return true;
                }

                case ImportPmxVertexAttributesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (c.ModelIndices.Length != c.PmxIndices.Length)
                    { Fail($"modelIndices が {c.ModelIndices.Length} 件、pmxIndices が {c.PmxIndices.Length} 件で長さが合いません"); return true; }

                    if (c.ModelIndices.Length == 0)
                    { Fail("組が 1 つもありません"); return true; }

                    if (!c.ImportPosition && !c.ImportUV && !c.ImportBoneWeight)
                    { Fail("importPosition / importUV / importBoneWeight のどれかを立てること"); return true; }

                    if (!TryLoadPmxOps(model, c.PmxPath, c.Scale, c.FlipX, c.FlipZ,
                                       out var ops, out string reason))
                    { Fail(reason); return true; }

                    var pairModel = new List<PartialMeshEntry>();
                    var pairPmx   = new List<Poly_Ling.PMX.PartialPMXEntry>();

                    for (int p = 0; p < c.ModelIndices.Length; p++)
                    {
                        var me = ops.ModelMeshes.Find(x => x.Index == c.ModelIndices[p]);
                        if (me == null)
                        { Fail($"差し替え先が見つかりません（描画オブジェクト索引 {c.ModelIndices[p]}）"); return true; }

                        var xe = ops.PMXMeshes.Find(x => x.Index == c.PmxIndices[p]);
                        if (xe == null)
                        { Fail($"PMX 側の番号が範囲外です（{c.PmxIndices[p]} / 0..{ops.PMXMeshes.Count - 1}）"); return true; }

                        pairModel.Add(me);
                        pairPmx.Add(xe);
                    }

                    int transferred = ops.ExecuteVertexAttributeImport(
                        pairModel, pairPmx,
                        position:   c.ImportPosition,
                        uv:         c.ImportUV,
                        boneWeight: c.ImportBoneWeight);

                    if (transferred == 0)
                    { Fail("差し替えが 1 頂点も走りませんでした"); return true; }

                    // 位置を入れ替えたので、そのベースを持つモーフの基準を追従させる。
                    Poly_Ling.PMX.PMXPartialImportOps.RemapMorphBasesAfterVertexChange(
                        pairModel, model,
                        remapPosition: c.ImportPosition,
                        remapUV:       c.ImportUV);

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);

                    ReportData(CommandDataJson.New()
                        .Int("pairs",       pairModel.Count)
                        .Int("transferred", transferred)
                        .Build());
                    return true;
                }

                default: return false;
            }
        }

        /// <summary>
        /// PMX を読み、モデル側の一覧も作った使い捨ての ops を返す。
        /// model が null なら PMX 側だけ作る（一覧の照会用）。
        ///
        /// 取り込み設定は部分インポートパネルの PMX 側と同じ
        /// （PlayerPmxToMqoTestSubPanel.cs:343-354）。材質は取り込まず、
        /// 位置だけ移すので法線の再計算もしない。
        /// </summary>
        private static bool TryLoadPmxOps(
            ModelContext model, string pmxPath, float scale, bool flipX, bool flipZ,
            out Poly_Ling.PMX.PMXPartialImportOps ops, out string reason)
        {
            reason = null;
            ops    = new Poly_Ling.PMX.PMXPartialImportOps();

            if (string.IsNullOrEmpty(pmxPath) || !System.IO.File.Exists(pmxPath))
            { reason = $"PMX が見つかりません: {pmxPath}"; return false; }

            var settings = new Poly_Ling.PMX.PMXImportSettings
            {
                ImportMode            = Poly_Ling.PMX.PMXImportMode.NewModel,
                ImportTarget          = Poly_Ling.PMX.PMXImportTarget.Mesh,
                ImportMaterials       = false,
                FlipX                 = flipX,
                FlipZ                 = flipZ,
                Scale                 = scale,
                RecalculateNormals    = false,
                DetectNamedMirror     = true,
                UseObjectNameGrouping = true,
            };

            var result = Poly_Ling.PMX.PMXImporter.ImportFile(pmxPath, settings);
            if (result == null || !result.Success)
            { reason = $"PMX を読めません: {result?.ErrorMessage ?? pmxPath}"; return false; }

            ops.LoadPMXResult(result);
            if (ops.PMXMeshes.Count == 0)
            { reason = "PMX から取り込めるオブジェクトが 0 件です"; return false; }

            if (model != null)
            {
                ops.BuildModelList(model);
                if (ops.ModelMeshes.Count == 0)
                { reason = "差し替え先の描画オブジェクトが 0 件です"; return false; }
            }

            return true;
        }

        /// <summary>
        /// MQO を読み、モデル側の一覧も作った使い捨てのヘルパを返す。
        /// 対応付けと転送で条件を揃えるため、引数は同じものを通す。
        /// </summary>
        private static bool TryBuildMatchHelper(
            ModelContext model, string mqoPath, bool visibleOnly,
            bool skipNamedMirror, bool pairMirrors,
            out MQOPartialMatchHelper helper, out string reason)
        {
            reason = null;
            helper = new MQOPartialMatchHelper();

            if (!helper.LoadMQO(mqoPath, visibleOnly) || helper.MQODocument == null)
            { reason = $"MQO を読めません: {mqoPath}"; return false; }

            if (helper.MQOObjects.Count == 0)
            { reason = "MQO から取り込めるオブジェクトが 0 件です"; return false; }

            helper.BuildModelList(model,
                skipBakedMirror: true,
                skipNamedMirror: skipNamedMirror,
                pairMirrors:     pairMirrors);

            if (helper.ModelMeshes.Count == 0)
            { reason = "差し替え先の描画オブジェクトが 0 件です"; return false; }

            return true;
        }
    }
}
