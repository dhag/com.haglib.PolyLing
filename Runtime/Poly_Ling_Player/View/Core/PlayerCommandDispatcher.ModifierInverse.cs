// PlayerCommandDispatcher.ModifierInverse.cs
// コマンドディスパッチャ：逆方向（出力の編集 → 入力）の照会・確定・破棄（作り直し整備計画 段 6・段 14）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【流れ】（2.5「逆方向で入力を書き直す → 正方向で出力を作り直す → 下流を評価」を 1 つの処理で流す）
//   1. 今の入力から正方向の見本 G を作る（ビューアの BuildModifierTemplate。ModifierFrameTrace を立てた中）
//   2. 枠ごとに G と今の出力（編集後）E から相似変換を求める（ModifierInverse.Fit）
//   3. 方法ごとに新しい入力を作る（ModifierInverse.Invert*）
//        梯子を取り込んでいる項目 … 梯子の頂点（BeltAcquire.AcquireStrips で段 → 頂点）
//        点列を持っている項目      … 項目の梯子の引数
//        ネジ配置・円周配置・デッキ … 項目の params の引数
//        頂点へ藤壺                 … 選択辞書の頂点
//        パイプの梯子化・帯・貼り付け … 入力の頂点（生成の控えの Source の枠。段 14）
//   4. 照会は、確定と同じ手順（入力・引数を書いて BuildCommand で組み直し、書き戻しと同じ見本の口で作る）で
//      試しに作った出力と E のずれを返す。書いた入力・引数はその場で戻す（TrialRebuild。段 G）
//   5. 確定は、記録を止めて入力を書き、その項目を作り直す（RunObjectGroupStep）。
//      下流はコマンドの終わりに依存の順で流れる（段 4）。Undo は一覧のスナップショットと
//      グループの差し替えを 1 件（段 3 と同じ形）
//
// 【上流へ連ねる】（段 14）
//   3 で書く入力が別の項目の出力なら、その出力に書く値を入れた複製を「その項目の出力の編集」とみなして、
//   その項目の逆方向を同じ手順で組む（再帰）。逆方向を持たない項目に当たったら理由付きで断る。
//   確定は、一番上流の入力と各段の引数を書いてから、上流から順に作り直す。Undo は全体で 1 件。
//   照会は連ねた経路（chain）と各段の保つもの・失うもの・その段のずれを返す。各段のずれはその段だけの
//   試算なので、確定の応答は作り直した実際の出力と編集した出力のずれ（actualMaxDeviation）も返す。
//
// 【未確定】出力の署名が「今の入力に対する正方向の結果」と違う項目（ModifierEvaluator.IsOutputEditPending）。
//   未確定の項目と下流は評価器が流さない。未確定は出力ごとに保存される（段 F）。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>出力の格納空間での正方向の結果を作る口（ビューアが配線する）。戻り値は失敗理由。</summary>
        public delegate string ModifierTemplateBuilder(
            PanelCommand cmd, MeshContext target, ModifierSpace.WriteBack spaceWb, out MeshObject mo);

        public ModifierTemplateBuilder OnBuildModifierTemplate;

        /// <summary>逆方向の試算・確定の中身。</summary>
        private sealed class InversePlan
        {
            public string Error;
            public ObjectGroup Group;
            public ObjectGroupStep Step;
            public int StepIndex;
            public Type CmdType;
            public ModifierInverse.Info Info;
            public MeshContext Output;
            /// <summary>編集後の出力 E（出力そのもの、または下流から渡された複製）。</summary>
            public MeshObject Edited;
            public bool Pending;

            /// <summary>書き直す項目の引数（null なら書かない）。</summary>
            public Dictionary<string, string> NewArgs;
            /// <summary>書き直す頂点の位置（描画オブジェクト → 頂点番号 → 位置）。上流の出力は含まない（Upstream へ渡す）。</summary>
            public readonly Dictionary<MeshContext, Dictionary<int, Vector3>> VertexWrites
                = new Dictionary<MeshContext, Dictionary<int, Vector3>>();
            /// <summary>書く入力のうち別の項目の出力だったもの → その項目の逆方向（段 14）。</summary>
            public readonly List<InversePlan> Upstream = new List<InversePlan>();
            /// <summary>連ねた経路（上流から順、最後がこの項目）。一番下流の計画だけが持つ。</summary>
            public List<InversePlan> Chain;

            public string Writes = "";
            public int Frames;
            public int FailedFrames;
            public string FailedText = "";
            public bool HasDeviation;
            /// <summary>試算できなかった理由（段 G）。試算できたときは null。</summary>
            public string TrialError;
            public float MaxDeviation;
            public float MeanDeviation;

            public string Label => $"{Group?.Name}#{StepIndex}";
        }

        // ================================================================
        // 試算
        // ================================================================

        /// <summary>名前で引いた項目の逆方向を、上流へ連ねて組む。</summary>
        private InversePlan BuildInversePlan(ProjectContext project, ModelContext model, string groupName, int stepIndex)
        {
            var g = model?.FindObjectGroupByName(groupName);
            if (model != null && g == null)
                return new InversePlan { StepIndex = stepIndex, Error = $"グループが見つかりません: {groupName}" };

            var p = BuildInversePlanCore(project, model, g, stepIndex, null);
            if (p.Error != null) return p;

            var chain = new List<InversePlan>();
            var visited = new HashSet<ObjectGroupStep>();
            string err = ResolveInverseChain(project, model, p, visited, chain, 0);
            if (err != null) { p.Error = err; return p; }
            p.Chain = chain;
            return p;
        }

        /// <summary>連ねる深さの上限（グラフは循環しないが、念のため）。</summary>
        private const int InverseChainMaxDepth = 32;

        /// <summary>
        /// p が書く入力のうち別の項目の出力であるものを、その項目の逆方向へ渡す（再帰）。
        /// chain へ上流から順に入れ、最後に p を入れる。断るときは理由を返す。
        /// </summary>
        private string ResolveInverseChain(
            ProjectContext project, ModelContext model, InversePlan p,
            HashSet<ObjectGroupStep> visited, List<InversePlan> chain, int depth)
        {
            if (depth > InverseChainMaxDepth) return "上流の逆方向を辿る段が多すぎます";
            if (!visited.Add(p.Step)) return $"項目 {p.Label} へ 2 つの経路から書き直すことになるので断ります";

            foreach (var mc in new List<MeshContext>(p.VertexWrites.Keys))
            {
                if (!ModifierEvaluator.TryFindOutputOwner(model, mc.ObjectId, out var ow)) continue;

                string upAction = ow.Step?.Action ?? "";
                if (ModifierInverse.Of(PanelCommandFactory.ResolveType(upAction)) == null)
                    return $"「{mc.Name}」は別の項目（{ow.Group?.Name}#{ow.StepIndex} {upAction}）の出力で、"
                         + "その項目は逆方向を持たないので書き直せません（上流の逆方向を連ねられない）";

                var edited = mc.MeshObject.Clone();
                foreach (var w in p.VertexWrites[mc])
                    if (w.Key >= 0 && w.Key < edited.VertexCount) edited.Vertices[w.Key].Position = w.Value;
                edited.InvalidatePositionCache();

                var up = BuildInversePlanCore(project, model, ow.Group, ow.StepIndex, edited);
                if (up.Error != null) return $"上流の項目 {up.Label}（{upAction}）の逆方向: {up.Error}";

                p.VertexWrites.Remove(mc);
                p.Upstream.Add(up);

                string err = ResolveInverseChain(project, model, up, visited, chain, depth + 1);
                if (err != null) return err;
            }
            chain.Add(p);
            return null;
        }

        /// <summary>
        /// 項目 1 つの逆方向を組む（上流へは連ねない）。edited を渡すと、それを出力の編集後の形とみなす
        /// （下流の逆方向から渡された上流の出力。段 14）。null なら今の出力。
        /// </summary>
        private InversePlan BuildInversePlanCore(
            ProjectContext project, ModelContext model, ObjectGroup group, int stepIndex, MeshObject edited)
        {
            var p = new InversePlan { StepIndex = stepIndex, Group = group };
            InversePlan Err(string why) { p.Error = why; return p; }

            if (project == null || model == null) return Err("モデルがありません");
            if (!ReferenceEquals(project.CurrentModel, model)) return Err("逆方向は今のモデルでだけ扱えます");
            int modelIndex = project.CurrentModelIndex;

            if (p.Group == null) return Err("グループがありません");
            p.Step = p.Group.GetStep(stepIndex);
            if (p.Step == null) return Err($"項目 {stepIndex} がありません（項目数 {p.Group.StepCount}）");
            if (!p.Step.IsExecutable) return Err($"項目 {stepIndex} は実行する項目ではありません");

            p.CmdType = PanelCommandFactory.ResolveType(p.Step.Action);
            p.Info = ModifierInverse.Of(p.CmdType);
            if (p.Info == null) return Err($"{p.Step.Action} は逆方向を持ちません");

            if (p.Step.OutputObjectIds == null || p.Step.OutputObjectIds.Count != 1)
                return Err("出力が 1 つの項目だけ逆方向を扱えます");
            p.Output = ObjectGroupOps.Resolve(project, p.Step.OutputObjectIds[0]);
            if (p.Output?.MeshObject == null || model.IndexOf(p.Output) < 0)
                return Err("出力の描画オブジェクトが見つかりません");
            p.Edited = edited ?? p.Output.MeshObject;

            if (OnBuildModifierTemplate == null) return Err("見本を作る口が配線されていません");

            foreach (var e in ModifierEvaluator.GetGraph(model))
                if (ReferenceEquals(e.Step, p.Step)) { p.Pending = ModifierEvaluator.IsOutputEditPending(model, e); break; }

            var rebuilt = ObjectGroupOps.BuildCommand(project, modelIndex, p.Group, stepIndex, out string rbErr);
            if (rebuilt == null) return Err(rbErr ?? "作り直すコマンドを組めませんでした");

            if (!ComputeStepSpace(model, p.Step, p.Output, true, out var spaceWb, out string spaceErr))
                return Err(spaceErr);

            // 1. 見本と枠
            var trace = new ModifierFrameTrace();
            MeshObject template;
            using (ModifierFrameTrace.Begin(trace))
            {
                string why = OnBuildModifierTemplate(rebuilt, p.Output, spaceWb, out template);
                if (why != null) return Err(why);
            }
            var frames = trace.FramesOf(template);

            // 2. 枠ごとの相似変換
            var fits = ModifierInverse.Fit(template, p.Edited, frames, trace.ToOutput, out string fitErr);
            if (fitErr != null) return Err(fitErr);
            p.Frames = fits.Count;

            // 3. 方法ごとの戻し方
            string action = p.Step.Action;
            var inv = CultureInfo.InvariantCulture;

            switch (p.Info.Method)
            {
                case ModifierInverseMethod.BeltFrames:
                {
                    if (!(rebuilt is CreateBeltPrimitiveCommand bc)) return Err("梯子を持つ項目ではありません");
                    var br = ModifierInverse.InvertBelts(bc.Belts, trace, fits);
                    p.FailedFrames = br.FailedFrames;
                    p.FailedText   = br.FailedText;
                    int apex = 0;
                    foreach (bool b in br.StartMoved) if (b) apex++;
                    foreach (bool b in br.EndMoved)   if (b) apex++;
                    if (br.MovedRungs == 0 && apex == 0)
                        return Err("書き直せる段がありません" + (br.FailedText.Length > 0 ? $"（{br.FailedText}）" : ""));

                    string src = BeltSourceWrites(project, model, p, bc, br);
                    if (src == null && p.Error != null) return p;
                    if (src != null)
                    {
                        p.Writes = src;
                    }
                    else
                    {
                        var na = new Dictionary<string, string>(p.Step.Args, StringComparer.Ordinal);
                        ObjectGroupOps.WriteBeltArgs(p.CmdType, na, br.Belts);
                        p.NewArgs = na;
                        p.Writes = $"項目の梯子の点列（段 {br.MovedRungs} 個・先端 {apex} 個。書き直さない段 {br.UntouchedRungs} 個）";
                    }
                    break;
                }

                case ModifierInverseMethod.LushFrames:
                {
                    // まつげ（段 7）：元の梯子の段を最小二乗で当てはめ、回転の始点・終点も書き直す。
                    if (!(rebuilt is CreateLushCommand lc)) return Err("まつげの項目ではありません");
                    var lr = ModifierInverse.InvertLush(lc, trace, fits);
                    var br = lr.Belt;
                    p.FailedFrames = br.FailedFrames;
                    p.FailedText   = br.FailedText;
                    bool beltMoved = br.MovedRungs > 0
                        || Array.IndexOf(br.StartMoved, true) >= 0 || Array.IndexOf(br.EndMoved, true) >= 0;
                    if (!beltMoved && lr.RotationFrames == 0)
                        return Err("書き直せる段も回転もありません" + (br.FailedText.Length > 0 ? $"（{br.FailedText}）" : ""));

                    string kStart = "params.rotationStart", kEnd = "params.rotationEnd";
                    var targs = PanelCommandFactory.ToArgs(rebuilt);
                    if (!targs.ContainsKey(kStart) || !targs.ContainsKey(kEnd))
                        return Err($"引数 {kStart}・{kEnd} が {action} にありません");
                    string V3(Vector3 v) => string.Join(",",
                        v.x.ToString("R", inv), v.y.ToString("R", inv), v.z.ToString("R", inv));
                    string sStart = V3(lr.RotationStart), sEnd = V3(lr.RotationEnd);

                    var na = new Dictionary<string, string>(p.Step.Args, StringComparer.Ordinal);
                    na[kStart] = sStart; na[kEnd] = sEnd;
                    string rotText = $"回転の始点 ({sStart})・終点 ({sEnd})、当てはめた枠 {lr.RotationFrames} 個";

                    string src = beltMoved ? BeltSourceWrites(project, model, p, lc, br) : null;
                    if (src == null && p.Error != null) return p;
                    if (src != null)
                        p.Writes = src + "、" + rotText;
                    else
                    {
                        if (beltMoved) ObjectGroupOps.WriteBeltArgs(p.CmdType, na, br.Belts);
                        p.Writes = (beltMoved ? $"項目の梯子の点列（段 {br.MovedRungs} 個）、" : "") + rotText;
                    }
                    p.NewArgs = na;
                    break;
                }

                case ModifierInverseMethod.BoltPattern:
                case ModifierInverseMethod.RadialPlace:
                case ModifierInverseMethod.TowerDeck:
                {
                    var placed = ModifierInverse.PlacedInstances(fits, out int fails, out string failText);
                    p.FailedFrames = fails; p.FailedText = failText;
                    if (placed.Count == 0) return Err("姿勢を決められた部品がありません" + (failText.Length > 0 ? $"（{failText}）" : ""));

                    var values = new Dictionary<string, float>(StringComparer.Ordinal);
                    switch (rebuilt)
                    {
                        case CreateBoltPatternCommand bp:
                        {
                            var np = ModifierInverse.InvertBolt(bp.Params, placed);
                            if (np.Layout == BoltPatternLayout.Rectangle) { values["width"] = np.Width; values["height"] = np.Height; }
                            else { values["pcdRadius"] = np.PcdRadius; values["startAngle"] = np.StartAngle; }
                            values["scale"] = np.Scale;
                            break;
                        }
                        case CreateRadialPlaceCommand rp:
                        {
                            var np = ModifierInverse.InvertRadial(rp.Params, placed);
                            values["radius"] = np.Radius; values["startAngle"] = np.StartAngle; values["scale"] = np.Scale;
                            break;
                        }
                        case CreateTowerDeckCommand dc:
                        {
                            var np = ModifierInverse.InvertDeck(dc.Params, placed);
                            values["height"] = np.Height; values["rotationY"] = np.RotationY; values["scale"] = np.Scale;
                            break;
                        }
                        default:
                            return Err($"{action} の引数の戻し方がありません");
                    }

                    var targs = PanelCommandFactory.ToArgs(rebuilt);
                    var na = new Dictionary<string, string>(p.Step.Args, StringComparer.Ordinal);
                    var desc = new List<string>();
                    foreach (var kv in values)
                    {
                        string key = "params." + kv.Key;
                        if (!targs.ContainsKey(key)) return Err($"引数 {key} が {action} にありません");
                        string val = kv.Value.ToString("R", inv);
                        na[key] = val;
                        desc.Add($"{key}={kv.Value.ToString("0.####", inv)}");
                    }
                    p.NewArgs = na;
                    p.Writes = "項目の引数（" + string.Join(", ", desc) + "）";
                    break;
                }

                case ModifierInverseMethod.VertexFrames:
                {
                    if (!(rebuilt is CreateVertexBillboardPlaceCommand vb)) return Err("頂点へ藤壺の項目ではありません");
                    if (vb.Target != Poly_Ling.PlaceObject.BillboardPlaceTarget.Vertices)
                        return Err("ボーン・原点へ置いた頂点へ藤壺は逆方向を持ちません（ボーン・原点は動かさない）");

                    var placed = ModifierInverse.PlacedInstances(fits, out int fails, out string failText);
                    p.FailedFrames = fails; p.FailedText = failText;
                    var targets = VertexBillboardPlaceToolHandler.CollectVertexTargets(
                        model, vb.MasterIndices ?? Array.Empty<int>(), vb.VertexSetName);

                    int moved = 0;
                    foreach (var pl in placed)
                    {
                        if (pl.Key < 0 || pl.Key >= targets.Count) continue;
                        var (mc, vi) = targets[pl.Key];
                        // 置いた位置はバインド姿勢のワールド（生成器と同じ。段 E）。格納値へは
                        // BindWorldToVertexMatrix で戻す（スキンドは単位）。
                        // 別の項目の出力なら、上流の逆方向へ渡す（ResolveInverseChain。段 14）。
                        Vector3 local = mc.BindWorldToVertexMatrix.MultiplyPoint3x4(pl.Center);
                        if (!p.VertexWrites.TryGetValue(mc, out var d)) { d = new Dictionary<int, Vector3>(); p.VertexWrites[mc] = d; }
                        d[vi] = local;
                        moved++;
                    }
                    if (moved == 0) return Err("書き直せる頂点がありません" + (failText.Length > 0 ? $"（{failText}）" : ""));
                    p.Writes = $"選択辞書「{vb.VertexSetName}」の頂点 {moved} 個の位置";
                    break;
                }

                case ModifierInverseMethod.CopiedVertices:
                case ModifierInverseMethod.RibbonCenters:
                case ModifierInverseMethod.SurfacePaste:
                {
                    string why = SourceVertexWrites(model, p, rebuilt, template, frames, trace.ToOutput);
                    if (why != null) return Err(why);
                    break;
                }

                default:
                    return Err($"逆方向の方法 {p.Info.Method} の戻し方がありません");
            }

            // 4. 試しの作り直しと今の出力のずれ（確定と同じ手順。段 G）
            TrialRebuild(project, modelIndex, p, spaceWb);
            return p;
        }

        /// <summary>
        /// 試算（段 G）：確定（ExecuteModifierOutputEdit）と同じく、入力の頂点と項目の引数を書いてから
        /// 項目を組み直し（ObjectGroupOps.BuildCommand。梯子は記録した取り込み方で取り直す）、
        /// 書き戻しと同じ見本の口で出力の形を作って、編集後の出力とのずれを測る。書いたものは必ず戻す。
        /// 上流の項目の出力へ書く分（ResolveInverseChain で上流へ渡す前の VertexWrites）もここでは書くので、
        /// 上流の出力を「渡す編集後の形」にした、その段だけのずれになる。
        /// モデルへは何も足さず、Undo も積まない（評価器も流さない）。
        /// </summary>
        private void TrialRebuild(ProjectContext project, int modelIndex, InversePlan p, ModifierSpace.WriteBack spaceWb)
        {
            var restore = new List<(MeshObject mo, int v, Vector3 pos)>();
            var touched = new List<MeshObject>();
            Dictionary<string, string> oldArgs = null;
            try
            {
                foreach (var kv in p.VertexWrites)
                {
                    var mo = kv.Key?.MeshObject;
                    if (mo == null) continue;
                    foreach (var w in kv.Value)
                    {
                        if (w.Key < 0 || w.Key >= mo.VertexCount) continue;
                        restore.Add((mo, w.Key, mo.Vertices[w.Key].Position));
                        mo.Vertices[w.Key].Position = w.Value;
                    }
                    mo.InvalidatePositionCache();
                    touched.Add(mo);
                }
                if (p.NewArgs != null)
                {
                    oldArgs = new Dictionary<string, string>(p.Step.Args, StringComparer.Ordinal);
                    p.Step.Args.Clear();
                    foreach (var kv in p.NewArgs) p.Step.Args[kv.Key] = kv.Value;
                }

                var cmd = ObjectGroupOps.BuildCommand(project, modelIndex, p.Group, p.StepIndex, out string rbErr);
                if (cmd == null) { p.TrialError = rbErr ?? "作り直すコマンドを組めませんでした"; return; }

                using (ModifierFrameTrace.Begin(new ModifierFrameTrace()))
                {
                    string why = OnBuildModifierTemplate(cmd, p.Output, spaceWb, out var tmo);
                    if (why != null) { p.TrialError = why; return; }
                    if (!ModifierInverse.Deviation(tmo, p.Edited, out p.MaxDeviation, out p.MeanDeviation))
                    {
                        p.TrialError = $"作り直した出力と編集後の出力の頂点数が違う（{tmo?.VertexCount ?? 0} / {p.Edited?.VertexCount ?? 0}）";
                        return;
                    }
                    p.HasDeviation = true;
                }
            }
            finally
            {
                foreach (var r in restore) r.mo.Vertices[r.v].Position = r.pos;
                foreach (var mo in touched) mo.InvalidatePositionCache();
                if (oldArgs != null)
                {
                    p.Step.Args.Clear();
                    foreach (var kv in oldArgs) p.Step.Args[kv.Key] = kv.Value;
                }
            }
        }

        /// <summary>MeshObject を持つモデル内の描画オブジェクト。無ければ null。</summary>
        private static MeshContext FindMeshContextOf(ModelContext model, MeshObject mo)
        {
            if (model == null || mo == null) return null;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && ReferenceEquals(mc.MeshObject, mo)) return mc;
            }
            return null;
        }

        /// <summary>
        /// パイプの梯子化・帯・貼り付け（段 14）：入力の頂点から作った頂点の移動を入力の頂点へ戻し、
        /// p.VertexWrites と説明を入れる（ずれの試算は TrialRebuild。段 G）。断るときは理由を返す。
        ///   copiedVertices … 写した頂点の新しい位置そのもの（生成器の空間＝元のパイプの格納空間）
        ///   ribbonCenters  … 左右の中点の移動（生成器の空間＝バインド姿勢のワールド）を BindWorldToVertexMatrix で格納空間へ（段 E）
        ///   surfacePaste   … SurfacePasteOps.InvertPoint（生成器の空間＝入力の格納空間）
        /// </summary>
        private static string SourceVertexWrites(
            ModelContext model, InversePlan p, PanelCommand rebuilt,
            MeshObject template, List<ModifierFrameTrace.Frame> frames, Matrix4x4 toOutput)
        {
            string method = p.Info.Method;
            var moves = ModifierInverse.InvertSources(template, p.Edited, frames, toOutput);
            if (moves.Count == 0) return "生成の控えに入力の頂点との対応がありません";

            SurfacePasteInput pin = null;
            SurfacePasteParams pparams = default;
            List<(Vector3 a, Vector3 b, Vector3 c)> tris = null;
            if (method == ModifierInverseMethod.SurfacePaste)
            {
                if (!(rebuilt is CreateSurfacePasteCommand spc)) return "貼り付けの項目ではありません";
                pin = SurfacePasteOps.ResolveInput(model, spc.SourceIndex, spc.ReferenceIndex);
                if (pin == null) return "貼り付けの入力が見つかりません";
                pparams = spc.Params;
                tris = pin.ReferenceInModel != null ? SurfacePasteOps.Triangles(pin.ReferenceInModel) : null;
            }

            // 入力の頂点 → 新しい格納値。
            var counts = new Dictionary<MeshContext, int>();
            foreach (var m in moves)
            {
                if (!m.Moved) continue;
                var mc = FindMeshContextOf(model, m.SourceMesh);
                if (mc == null)
                    return "入力がスキンド化済みで、元のローカルへ戻した複製から作っているため、入力の頂点を書き直せません";
                var mo = mc.MeshObject;
                if (m.SourceVertex >= mo.VertexCount) return $"「{mc.Name}」の頂点番号 {m.SourceVertex} が範囲外です";

                Vector3 stored;
                switch (method)
                {
                    case ModifierInverseMethod.RibbonCenters:
                        // 帯の位置はバインド姿勢のワールド（生成器と同じ。段 E）。格納値へは
                        // BindWorldToVertexMatrix で戻す（スキンドは単位）。
                        stored = mo.Vertices[m.SourceVertex].Position
                               + mc.BindWorldToVertexMatrix.MultiplyVector(m.New - m.Old);
                        break;
                    case ModifierInverseMethod.SurfacePaste:
                        stored = SurfacePasteOps.InvertPoint(pin, pparams, tris, mo.Vertices[m.SourceVertex].Position, m.New);
                        break;
                    default:
                        stored = m.New;
                        break;
                }

                if (!p.VertexWrites.TryGetValue(mc, out var d)) { d = new Dictionary<int, Vector3>(); p.VertexWrites[mc] = d; }
                d[m.SourceVertex] = stored;
                counts[mc] = (counts.TryGetValue(mc, out int c) ? c : 0) + 1;
            }
            if (counts.Count == 0) return "入力へ戻す頂点の移動がありません（入力から作った頂点が動いていない）";

            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"「{kv.Key.Name}」の頂点 {kv.Value} 個");
            p.Writes = string.Join("、", parts)
                     + (method == ModifierInverseMethod.RibbonCenters ? "（帯の左右の中点の移動）" : "");
            // 試算は確定と同じ手順で作り直して測る（TrialRebuild。段 G）。
            return null;
        }

        /// <summary>
        /// 梯子を取り込んでいる項目なら、書き直す梯子の頂点を p.VertexWrites へ入れて説明を返す。
        /// 点列を持っている項目（取り込み方の記録が無い・取り直せない）なら null。拒否するときは p.Error。
        /// 梯子が別の項目の出力なら、その項目の逆方向へ渡す（ResolveInverseChain。段 14）。
        /// </summary>
        private static string BeltSourceWrites(
            ProjectContext project, ModelContext model, InversePlan p, CreateBeltPrimitiveCommand bc, ModifierInverse.BeltResult br)
        {
            if (bc.AcquireMethod == BeltAcquireMethod.Baked) return null;
            var src = bc.BeltSourceIndex >= 0 ? model.GetMeshContext(bc.BeltSourceIndex) : null;
            if (src?.MeshObject == null) return null;

            var got = BeltAcquire.AcquireStrips(src, bc.AcquireMethod, bc.AcquireCrossRows, bc.AcquireSetName);
            if (!got.Ok) return null;

            int vc = src.MeshObject.VertexCount;
            var strips = new List<BeltAutoStrip>();
            foreach (var st in got.Strips)
            {
                if (st == null || st.RungCount < 2) continue;
                bool okIdx = true;
                for (int i = 0; i < st.RungCount; i++)
                    if (st.Left[i] < 0 || st.Left[i] >= vc || st.Right[i] < 0 || st.Right[i] >= vc) { okIdx = false; break; }
                if (!okIdx) { p.Error = "取り込み元の梯子の頂点番号が範囲外です"; return null; }
                strips.Add(st);
            }
            if (strips.Count != br.Belts.Length)
            { p.Error = $"取り込み元の梯子の本数（{strips.Count}）と項目の梯子の本数（{br.Belts.Length}）が合わない"; return null; }

            var sum = new Dictionary<int, Vector3>();
            var cnt = new Dictionary<int, int>();
            void Add(int v, Vector3 pos)
            {
                if (v < 0 || v >= vc) return;
                sum[v] = (sum.TryGetValue(v, out var s) ? s : Vector3.zero) + pos;
                cnt[v] = (cnt.TryGetValue(v, out var c) ? c : 0) + 1;
            }

            int rungs = 0, apex = 0;
            for (int b = 0; b < strips.Count; b++)
            {
                var st = strips[b];
                var nb = br.Belts[b];
                for (int i = 0; i < st.RungCount && i < br.Moved[b].Length; i++)
                {
                    if (!br.Moved[b][i]) continue;
                    Add(st.Left[i],  nb.Left[i]);
                    Add(st.Right[i], nb.Right[i]);
                    rungs++;
                }
                if (br.StartMoved[b] && st.StartPoint >= 0 && nb.StartPoint.HasValue) { Add(st.StartPoint, nb.StartPoint.Value); apex++; }
                if (br.EndMoved[b]   && st.EndPoint   >= 0 && nb.EndPoint.HasValue)   { Add(st.EndPoint,   nb.EndPoint.Value);   apex++; }
            }

            var d = new Dictionary<int, Vector3>();
            foreach (var kv in sum) d[kv.Key] = kv.Value / cnt[kv.Key];
            p.VertexWrites[src] = d;
            return $"梯子「{src.Name}」の頂点 {d.Count} 個（段 {rungs} 個・先端 {apex} 個。書き直さない段 {br.UntouchedRungs} 個）";
        }

        // ================================================================
        // 照会・確定・破棄
        // ================================================================

        /// <summary>連ねた経路の 1 段の説明。</summary>
        private static string ChainLine(InversePlan q)
        {
            var inv = CultureInfo.InvariantCulture;
            string writes = q.Upstream.Count > 0 && q.VertexWrites.Count == 0 && q.NewArgs == null
                ? $"{q.Writes} → 上流 {string.Join("・", q.Upstream.ConvertAll(u => u.Label))} の出力の編集として渡す"
                : q.Upstream.Count > 0
                    ? $"{q.Writes}（うち上流 {string.Join("・", q.Upstream.ConvertAll(u => u.Label))} の出力の分は、その出力の編集として渡す）"
                    : q.Writes;
            string dev = q.HasDeviation
                ? $"その段のずれ 最大 {q.MaxDeviation.ToString("0.######", inv)}・平均 {q.MeanDeviation.ToString("0.######", inv)}"
                : $"その段のずれ（試算できない: {q.TrialError ?? "理由なし"}）";
            return $"{q.Label} {q.Step?.Action}（{q.Info?.Method}）／書く: {writes}／保つもの: {q.Info?.Keeps}／失うもの: {q.Info?.Loses}／{dev}";
        }

        private string BuildModifierInverseData(ProjectContext project, ModelContext model, QueryModifierInverseCommand c)
        {
            var g = model?.FindObjectGroupByName(c.GroupName);
            var step = g?.GetStep(c.StepIndex);
            var info = ModifierInverse.Of(PanelCommandFactory.ResolveType(step?.Action));

            var p = BuildInversePlan(project, model, c.GroupName, c.StepIndex);

            var lines = new List<string>();
            if (p.Chain != null) foreach (var q in p.Chain) lines.Add(ChainLine(q));
            else lines.Add($"{c.GroupName}#{c.StepIndex} {step?.Action ?? ""}（{info?.Method ?? ""}）／確定できない: {p.Error ?? ""}");

            return CommandDataJson.New()
                .Text("action",       step?.Action ?? "")
                .Text("method",       info?.Method ?? "")
                .Text("keeps",        info?.Keeps ?? "")
                .Text("loses",        info?.Loses ?? "")
                .Int ("pending",      p.Pending ? 1 : 0)
                .Int ("canCommit",    p.Error == null ? 1 : 0)
                .Text("reason",       p.Error ?? (p.FailedText.Length > 0 ? "姿勢を決められなかった枠: " + p.FailedText : ""))
                .Text("writes",       p.Chain != null ? ChainWrites(p.Chain) : (p.Writes ?? ""))
                .Int ("frames",       p.Frames)
                .Int ("failedFrames", p.FailedFrames)
                .Num ("maxDeviation",  p.HasDeviation ? p.MaxDeviation : -1f)
                .Num ("meanDeviation", p.HasDeviation ? p.MeanDeviation : -1f)
                .Int ("chainCount",   p.Chain?.Count ?? 0)
                .Texts("chain",       lines)
                .Build();
        }

        /// <summary>経路全体で実際に書く入力（上流へ渡した分を除く）の説明。</summary>
        private static string ChainWrites(List<InversePlan> chain)
        {
            var parts = new List<string>();
            foreach (var q in chain)
            {
                if (q.VertexWrites.Count == 0 && q.NewArgs == null) continue;
                parts.Add(q.Upstream.Count == 0 ? $"{q.Label}: {q.Writes}" : $"{q.Label}: {q.Writes}（上流の出力の分を除く）");
            }
            return string.Join(" ／ ", parts);
        }

        /// <summary>確定（commit=true）・破棄（false）。戻り値は失敗理由。</summary>
        private string ExecuteModifierOutputEdit(ProjectContext project, ModelContext model, string groupName, int stepIndex, bool commit)
        {
            InversePlan root;
            List<InversePlan> chain;
            if (commit)
            {
                root = BuildInversePlan(project, model, groupName, stepIndex);
                if (root.Error != null) return root.Error;
                chain = root.Chain;
            }
            else
            {
                root = new InversePlan { StepIndex = stepIndex, Group = model?.FindObjectGroupByName(groupName) };
                if (root.Group == null) return $"グループが見つかりません: {groupName}";
                root.Step = root.Group.GetStep(stepIndex);
                if (root.Step == null) return $"項目 {stepIndex} がありません";
                if (ModifierInverse.Of(PanelCommandFactory.ResolveType(root.Step.Action)) == null)
                    return $"{root.Step.Action} は逆方向を持ちません";
                chain = new List<InversePlan> { root };
            }

            // 引数を書き換えるグループの控え（グループごとに 1 つ）。
            var oldGroups = new Dictionary<ObjectGroup, (int index, ObjectGroup old)>();
            var oldArgs = new List<Dictionary<string, string>>();
            foreach (var q in chain)
            {
                if (!oldGroups.ContainsKey(q.Group))
                    oldGroups[q.Group] = (model.ObjectGroups.IndexOf(q.Group), q.Group.Clone());
                oldArgs.Add(new Dictionary<string, string>(q.Step.Args, StringComparer.Ordinal));
            }

            // 実際の丸めを測るための、編集した出力の控え。
            MeshObject editedBefore = commit ? root.Edited?.Clone() : null;

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);

            // 失敗したときに戻す頂点の位置。
            var restore = new List<(MeshContext mc, int v, Vector3 pos)>();

            string fail = null;
            _undoController?.SuspendRecording();
            try
            {
                foreach (var q in chain)
                {
                    foreach (var kv in q.VertexWrites)
                    {
                        var mo = kv.Key.MeshObject;
                        foreach (var w in kv.Value)
                        {
                            if (w.Key < 0 || w.Key >= mo.VertexCount) continue;
                            restore.Add((kv.Key, w.Key, mo.Vertices[w.Key].Position));
                            mo.Vertices[w.Key].Position = w.Value;
                        }
                        // GPU の位置は、作り直しが GPU のワールド位置を読む直前に合わせる
                        // （ExecuteEdgeRibbonFace などの SettleWorldPositions。段 B）。
                    }
                    if (q.NewArgs != null)
                    {
                        q.Step.Args.Clear();
                        foreach (var kv in q.NewArgs) q.Step.Args[kv.Key] = kv.Value;
                    }
                }

                // 上流から順に作り直す。各項目の控えを取り直してから次へ進む（下流は作り直した上流を読む）。
                foreach (var q in chain)
                {
                    if (!RunObjectGroupStep(project, model, project.CurrentModelIndex, false, q.Group, q.StepIndex, out string err))
                    { fail = $"{q.Label}: " + (err ?? "作り直しに失敗しました"); break; }
                    ModifierEvaluator.MarkRebuilt(model, q.Step);
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            string verb = commit ? "出力の編集の確定" : "出力の編集の破棄";
            if (fail != null)
            {
                // GPU の位置はコマンドの終わりに合わせる（SettleWorldPositions。段 B）。
                foreach (var r in restore) r.mc.MeshObject.Vertices[r.v].Position = r.pos;
                for (int i = 0; i < chain.Count; i++)
                {
                    chain[i].Step.Args.Clear();
                    foreach (var kv in oldArgs[i]) chain[i].Step.Args[kv.Key] = kv.Value;
                }
                // 出力は作り直しの途中で書き換わっている場合があるので、一覧ごと戻す。
                RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount, null,
                    $"{verb}（失敗）: {root.Group.Name} 項目 {stepIndex}");
                FinishGroupEdit(project, model);
                return $"作り直しに失敗したので入力を戻しました: {fail}";
            }

            // 書き直した入力と作り直した出力を控え、下流へ伝える（下流はこのコマンドの終わりに流れる）。
            var groupChanges = new List<ObjectGroupChangeRecord>();
            foreach (var kv in oldGroups)
            {
                var g = kv.Key;
                g.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, g);
                bool argsChanged = false;
                foreach (var q in chain) if (ReferenceEquals(q.Group, g) && q.NewArgs != null) argsChanged = true;
                if (argsChanged && kv.Value.index >= 0)
                    groupChanges.Add(new ObjectGroupChangeRecord
                        { ReplacedIndex = kv.Value.index, OldGroup = kv.Value.old, NewGroup = g.Clone() });
            }
            RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount,
                groupChanges.Count > 0 ? groupChanges : null,
                $"{verb}: {root.Group.Name} 項目 {stepIndex}" + (chain.Count > 1 ? $"（上流 {chain.Count - 1} 段を含む）" : ""));

            FinishGroupEdit(project, model);

            // 確定後の実際の出力と編集した出力のずれ（連ねた鎖では各段の試算より大きくなる）。
            bool hasActual = false;
            float actualMax = 0f, actualMean = 0f;
            if (commit && editedBefore != null && root.Step.OutputObjectIds?.Count == 1)
            {
                var outNow = ObjectGroupOps.Resolve(project, root.Step.OutputObjectIds[0]);
                if (outNow?.MeshObject != null)
                    hasActual = ModifierInverse.Deviation(outNow.MeshObject, editedBefore, out actualMax, out actualMean);
            }

            var lines = new List<string>();
            if (commit) foreach (var q in chain) lines.Add(ChainLine(q));
            else lines.Add($"{root.Label} {root.Step.Action}／破棄：今の入力から作り直した");

            ReportData(CommandDataJson.New()
                .Text("writes",        commit ? ChainWrites(chain) : "（破棄：今の入力から作り直した）")
                .Int ("frames",        root.Frames)
                .Int ("failedFrames",  root.FailedFrames)
                .Num ("maxDeviation",  root.HasDeviation ? root.MaxDeviation : -1f)
                .Num ("meanDeviation", root.HasDeviation ? root.MeanDeviation : -1f)
                .Num ("actualMaxDeviation",  hasActual ? actualMax : -1f)
                .Num ("actualMeanDeviation", hasActual ? actualMean : -1f)
                .Int ("chainCount",    chain.Count)
                .Texts("chain",        lines)
                .Build());
            return null;
        }
    }
}
