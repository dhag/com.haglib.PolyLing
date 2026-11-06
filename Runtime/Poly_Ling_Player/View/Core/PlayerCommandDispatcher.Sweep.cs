// PlayerCommandDispatcher.Sweep.cs
// コマンドディスパッチャ：選択からカードとパイプを作る（CreateSweepMeshCommand。Maya の Sweep Mesh）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 5・6 章の決定
//   入力の決め方（選択中のオブジェクトごと。入力が 1 つも無ければ直線のカーブを新しく作る）：
//     面の選択                … 複製して帯（一覧に出さない）。followSource なら元の面を辞書で読む
//     辺の選択                … 連なりごとに複製してカーブ。followSource なら元の辺を辞書で読む
//     線分だけのオブジェクト  … そのまま参照（カーブ）。一部の線分だけ選んでいれば辺の選択と同じ
//     帯（梯子の目印あり）    … そのまま参照
//   分岐と不連続は連なりごとに別のカーブ・別のパイプにする（3ds Max と同じ）。閉じた連なりは閉じたパイプにする。
//   パイプの作り方は createEdgePipe と同じ（RunEdgePipe）。履歴は帯＋パイプの 1 本。
//
// 【Undo はまとめて 1 件】RunEdgePipe の呼び出し側と同じ形（RecordEdgeOperationUndo）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Ops;
using Poly_Ling.Pipe;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.SpringBoneRig;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>鎖を置くコマンドの中でバッファを作り直さない（呼び出し側が最後に 1 回で作り直す）。</summary>
        private bool _deferSpringChainBufferRebuild;

        private const string CurveBaseName = "Curve";
        private const string SweepSetBaseName = "SweepEdges";

        private enum SweepInputKind { Edges, Faces, Curve, Belt }

        private sealed class SweepInput
        {
            public SweepInputKind Kind;
            public MeshContext Mesh;
            public List<VertexPair> Edges;
            public List<int> Faces;
        }

        private static bool IsCurveObject(MeshObject mo)
        {
            if (mo == null || mo.FaceCount == 0) return false;
            foreach (var f in mo.Faces)
                if (f?.VertexIndices == null || f.VertexIndices.Count != 2) return false;
            return true;
        }

        private static List<VertexPair> LinePairs(MeshObject mo)
        {
            var list = new List<VertexPair>();
            foreach (var f in mo.Faces)
                if (f?.VertexIndices != null && f.VertexIndices.Count == 2)
                    list.Add(new VertexPair(f.VertexIndices[0], f.VertexIndices[1]));
            return list;
        }

        /// <summary>選択から入力を決める（モデルを書き換えない）。</summary>
        private static List<SweepInput> CollectSweepInputs(ModelContext model)
        {
            var inputs = new List<SweepInput>();
            var indices = new List<int>(model.SelectedDrawableMeshIndices);
            indices.Sort();
            foreach (int idx in indices)
            {
                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null || mc.Type != MeshType.Mesh) continue;
                var sel = mc.Selection;

                bool curve = IsCurveObject(mo);
                if (sel?.Faces != null && sel.Faces.Count > 0 && !curve)
                {
                    inputs.Add(new SweepInput { Kind = SweepInputKind.Faces, Mesh = mc, Faces = new List<int>(sel.Faces) });
                    continue;
                }
                if (sel?.Edges != null && sel.Edges.Count > 0)
                {
                    if (curve && sel.Edges.Count >= LinePairs(mo).Count)
                        inputs.Add(new SweepInput { Kind = SweepInputKind.Curve, Mesh = mc });
                    else
                        inputs.Add(new SweepInput { Kind = SweepInputKind.Edges, Mesh = mc, Edges = new List<VertexPair>(sel.Edges) });
                    continue;
                }
                if (curve) { inputs.Add(new SweepInput { Kind = SweepInputKind.Curve, Mesh = mc }); continue; }

                var strips = BeltStackDetector.Detect(mo, false, out _);
                if (strips != null && strips.Count > 0)
                    inputs.Add(new SweepInput { Kind = SweepInputKind.Belt, Mesh = mc });
            }
            return inputs;
        }

        /// <summary>カーブのオブジェクトを新しく作る。点はワールド座標で、オブジェクトのローカルと同じ。</summary>
        private static MeshContext AddCurveObject(ModelContext model, string baseName, IList<Vector3> points, bool closed, out string reason)
        {
            string name = model.GenerateUniqueMeshName(baseName);
            var mo = new MeshObject(name);
            int gi = LineGroupEditOps.Create(mo, new List<Vector3>(points), closed, name, null, out reason);
            if (gi < 0) return null;
            mo.AssignMissingIds();
            mo.InvalidatePositionCache();

            var mesh = mo.ToUnityMesh(model.MaterialCount);
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            var mc = new MeshContext { MeshObject = mo, Name = name, UnityMesh = mesh };
            mc.ParentModelContext = model;
            model.Add(mc);
            return mc;
        }

        /// <summary>辺の集合をパーツ選択辞書へ保存する。</summary>
        private static string AddSweepEdgeSet(MeshContext mc, IEnumerable<VertexPair> edges, bool wholeCurve)
        {
            string baseName = wholeCurve ? EdgeRibbonFaceTool.CurveEdgeSetPrefix : SweepSetBaseName;
            string name = baseName;
            for (int n = 2; mc.FindSelectionSetByName(name) != null; n++) name = $"{baseName}_{n}";
            var set = PartsSelectionSet.FromCurrentSelection(name, null, new HashSet<VertexPair>(edges), null, null, MeshSelectMode.Edge);
            set.CaptureIds(mc.MeshObject);
            mc.PartsSelectionSetList.Add(set);
            return name;
        }

        private static PipeParams SweepPipeParams(ModelContext model, CreateSweepMeshCommand c)
        {
            var p = PipeParams.Default;
            p.MeshName     = model.GenerateUniqueMeshName("Pipe");
            p.FlatCaps     = true;
            p.SectionKind  = c.SectionKind;
            p.SectionSides = c.Sides;
            return p;
        }

        private static PrimitivePlacement SweepPlacement()
        {
            var pl = PrimitivePlacement.Default;
            pl.KeepAsModifierStack = true;
            return pl;
        }

        /// <summary>カーブ（または元の辺）の連なり 1 本からパイプを作る。</summary>
        private string SweepChain(ProjectContext project, ModelContext model, CreateSweepMeshCommand c,
                                  MeshContext curve, string setName, List<int> pipes, List<string> stacks)
        {
            var p = SweepPipeParams(model, c);
            var section = SweepSectionOps.Build(p, out bool closedSection);
            var cmd = new CreateEdgePipeCommand(
                c.ModelIndex, new[] { model.MeshContextList.IndexOf(curve) }, c.Width,
                true, false, setName, SweepPlacement(), p,
                section != null ? section.ToArray() : null, section != null && closedSection,
                default, default);
            string why = RunEdgePipe(project, model, cmd, out string group, out _, out _, out int pipeIndex);
            if (why != null) return why;
            if (pipeIndex >= 0) pipes.Add(pipeIndex);
            if (!string.IsNullOrEmpty(group)) stacks.Add(group);
            return null;
        }

        /// <summary>帯（面の列）からパイプを作る。履歴はパイプの 1 項目。</summary>
        private string SweepBelt(ProjectContext project, ModelContext model, CreateSweepMeshCommand c, MeshContext band,
                                 BeltAcquireMethod method, string setName, List<int> pipes, List<string> stacks)
        {
            var got = BeltAcquire.Acquire(band, method, false, setName ?? "");
            if (!got.Ok) return $"面の帯として読めません: {got.Message}";
            CreateBeltPrimitiveCommand.SplitBelts(got.Belts.ToArray(),
                out var left, out var right, out var starts, out var closed, out var flip, out var height);

            var p = SweepPipeParams(model, c);
            var section = SweepSectionOps.Build(p, out bool closedSection);
            int before = model.ModifierStackCount;
            var r = Dispatch(new CreatePipeCommand(
                c.ModelIndex, p, section != null ? section.ToArray() : null, section != null && closedSection,
                left, right, starts, closed, flip, height,
                default, default, SweepPlacement(),
                model.MeshContextList.IndexOf(band), method, false, setName ?? ""));
            if (r != null && !r.Success) return $"パイプを作れませんでした: {r.Reason}";
            if (r?.MasterIndices != null && r.MasterIndices.Length > 0) pipes.Add(r.MasterIndices[0]);
            if (model.ModifierStackCount > before) stacks.Add(model.ModifierStacks[model.ModifierStackCount - 1].Name);
            return null;
        }

        private string RunSweep(ProjectContext project, ModelContext model, CreateSweepMeshCommand c,
                                List<SweepInput> inputs, List<int> curves, List<int> pipes, List<string> stacks)
        {
            string why;

            // 入力が無いときは直線のカーブ（Maya の Sweep Mesh と同じ）。
            if (inputs.Count == 0)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= 4; i++) pts.Add(new Vector3(0f, 1f - i * 0.25f, 0f));
                var line = AddCurveObject(model, CurveBaseName, pts, false, out why);
                if (line == null) return why;
                curves.Add(model.MeshContextList.IndexOf(line));
                if (c.CurveOnly) return null;
                return SweepChain(project, model, c, line, AddSweepEdgeSet(line, LinePairs(line.MeshObject), true), pipes, stacks);
            }

            foreach (var input in inputs)
            {
                var mc = input.Mesh;
                var mo = mc.MeshObject;
                switch (input.Kind)
                {
                    case SweepInputKind.Curve:
                    {
                        if (c.CurveOnly) { curves.Add(model.MeshContextList.IndexOf(mc)); break; }
                        mo.AssignMissingIds();
                        foreach (var chain in LineGroupEditOps.SplitChains(LinePairs(mo)))
                        {
                            why = SweepChain(project, model, c, mc, AddSweepEdgeSet(mc, ChainEdges(chain.Order, chain.Closed), true), pipes, stacks);
                            if (why != null) return why;
                        }
                        break;
                    }

                    case SweepInputKind.Edges:
                    {
                        if (c.FollowSource && !c.CurveOnly) mo.AssignMissingIds();
                        var toWorld = mc.VertexToBindWorldMatrix;
                        foreach (var chain in LineGroupEditOps.SplitChains(input.Edges))
                        {
                            if (c.FollowSource && !c.CurveOnly)
                            {
                                why = SweepChain(project, model, c, mc, AddSweepEdgeSet(mc, ChainEdges(chain.Order, chain.Closed), false), pipes, stacks);
                                if (why != null) return why;
                                continue;
                            }
                            var pts = new List<Vector3>(chain.Order.Count);
                            foreach (int v in chain.Order) pts.Add(toWorld.MultiplyPoint3x4(mo.Vertices[v].Position));
                            var curve = AddCurveObject(model, mc.Name + "_curve", pts, chain.Closed, out why);
                            if (curve == null) return why;
                            curves.Add(model.MeshContextList.IndexOf(curve));
                            if (c.CurveOnly) continue;
                            why = SweepChain(project, model, c, curve, AddSweepEdgeSet(curve, LinePairs(curve.MeshObject), true), pipes, stacks);
                            if (why != null) return why;
                        }
                        break;
                    }

                    case SweepInputKind.Faces:
                    {
                        if (c.CurveOnly) return "面の選択からカーブは作れません。辺を選んでください";
                        MeshContext band = mc;
                        var faces = input.Faces;
                        if (!c.FollowSource)
                        {
                            band = SelectionDuplicateOps.Duplicate(mc, false, model.GenerateUniqueMeshName(mc.Name + "_band"), out var dup);
                            if (band == null) return dup.Reason;
                            band.IsVisible = false;
                            model.Add(band);
                            faces = new List<int>();
                            for (int i = 0; i < band.MeshObject.FaceCount; i++) faces.Add(i);
                        }
                        string set = "SweepFaces";
                        for (int n = 2; band.FindSelectionSetByName(set) != null; n++) set = $"SweepFaces_{n}";
                        var fs = PartsSelectionSet.FromCurrentSelection(set, null, null, new HashSet<int>(faces), null, MeshSelectMode.Face);
                        fs.CaptureIds(band.MeshObject);
                        band.PartsSelectionSetList.Add(fs);
                        why = SweepBelt(project, model, c, band, BeltAcquireMethod.SelectionSet, set, pipes, stacks);
                        if (why != null) return why;
                        break;
                    }

                    case SweepInputKind.Belt:
                    {
                        if (c.CurveOnly) return "帯からカーブは作れません。辺を選んでください";
                        why = SweepBelt(project, model, c, mc, BeltAcquireMethod.AutoLadder, "", pipes, stacks);
                        if (why != null) return why;
                        break;
                    }
                }
            }
            return null;
        }

        private static List<VertexPair> ChainEdges(List<int> order, bool closed)
        {
            var list = new List<VertexPair>(order.Count);
            for (int i = 0; i + 1 < order.Count; i++) list.Add(new VertexPair(order[i], order[i + 1]));
            if (closed && order.Count >= 3) list.Add(new VertexPair(order[order.Count - 1], order[0]));
            return list;
        }

        /// <summary>
        /// 履歴の帯から揺れ鎖を作る（操作統合計画 段 7）。帯は履歴の生成物が読むものと同じなので、
        /// カーブを直すと帯 → 生成物とボーンが並列に追随する。
        /// 帯と生成物がスキンド化していなければ鎖の先頭ボーンへスキンド化し（カーブはそのまま）、
        /// 揺れ鎖が帯へ塗ったウェイトを生成物へ引き継ぐ。最後に揺れ方と鎖の先頭を入れる
        /// （これが無いと揺れない）。値はあとで揺れもののパネルから変えられる。
        /// Undo は一覧の写し・足したスタック・引数を書いたスタックの 3 つを 1 件にまとめる。
        /// </summary>
        private string ExecuteAddSpringChainFromHistory(ProjectContext project, ModelContext model, AddSpringChainFromHistoryCommand c)
        {
            var g = model.FindModifierStackByName(c.StackName);
            if (g == null) return $"履歴が見つかりません: {c.StackName}";
            if (g.IsChain) return $"{g.Name} はモディファイアです。帯を読む履歴を指定してください";

            // 帯を読む項目を探す。作り直しと同じコマンドで取り込み元と取り込み方を読む。
            CreateBeltPrimitiveCommand belt = null;
            var beltSteps = new List<int>();
            for (int si = 0; si < g.Steps.Count; si++)
            {
                var t = PanelCommandFactory.ResolveType(g.Steps[si]?.Action);
                if (t == null || !typeof(CreateBeltPrimitiveCommand).IsAssignableFrom(t)) continue;
                beltSteps.Add(si);
                if (belt == null) belt = ModifierStackOps.BuildCommand(project, c.ModelIndex, g, si, out _) as CreateBeltPrimitiveCommand;
            }
            if (belt == null) return $"{g.Name} に帯を読む項目がありません";
            if (belt.AcquireMethod == BeltAcquireMethod.Baked) return "帯の取り込み方の記録が無いので、揺れ鎖を作れません";

            var band = model.GetMeshContext(belt.BeltSourceIndex);
            if (band?.MeshObject == null) return "帯のオブジェクトが見つかりません";

            foreach (var e in ModifierEvaluator.GetGraph(model))
                if (e.Step?.Action == ModifierSpace.SpringLadderAction && e.HasInput(band))
                    return "この帯にはもう揺れ鎖があります";

            bool skinned = band.IsSkinned;
            string prefix = c.NamePrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                var last = g.Steps[beltSteps[beltSteps.Count - 1]];
                var output = last.OutputObjectIds.Count > 0 ? ModifierStackOps.Resolve(project, last.OutputObjectIds[0]) : null;
                prefix = (output?.Name ?? g.Name) + "_Spring";
            }

            _undoController?.SetModelContext(model);
            var listBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ModifierStackCount;
            int gIndex       = model.ModifierStacks.IndexOf(g);
            var oldSnapshot  = g.Clone();

            // 同じ帯を読む他の履歴の生成物も同じ鎖に乗せる。乗せないと同じ帯のパイプやフリルが片方だけ動かないまま残り、
            // 帯に鎖が 1 本しか作れないので後から揺らす手段も無い。
            var beltOwners = new List<(ModifierStack Stack, int Step)>();
            foreach (int si in beltSteps) beltOwners.Add((g, si));
            var otherSnapshots = new List<(int Index, ModifierStack Old)>();
            int bandIndex = model.MeshContextList.IndexOf(band);
            for (int gi = 0; gi < model.ModifierStackCount; gi++)
            {
                var other = model.ModifierStacks[gi];
                if (other == null || ReferenceEquals(other, g) || other.IsChain || other.Steps == null) continue;
                bool touched = false;
                for (int si = 0; si < other.Steps.Count; si++)
                {
                    var t = PanelCommandFactory.ResolveType(other.Steps[si]?.Action);
                    if (t == null || !typeof(CreateBeltPrimitiveCommand).IsAssignableFrom(t)) continue;
                    if (!(ModifierStackOps.BuildCommand(project, c.ModelIndex, other, si, out _) is CreateBeltPrimitiveCommand ob)) continue;
                    if (ob.BeltSourceIndex != bandIndex) continue;
                    beltOwners.Add((other, si));
                    touched = true;
                }
                if (touched) otherSnapshots.Add((gi, other.Clone()));
            }

            var roots        = new List<int>();
            string fail      = null;

            _undoController?.SuspendRecording();
            try
            {
                CommandResult r;
                _deferSpringChainBufferRebuild = true;
                try
                {
                    r = Dispatch(new PlaceSpringBoneLadderChainsCommand(
                        c.ModelIndex, model.MeshContextList.IndexOf(band), belt.AcquireMethod, SpringBoneLadderMode.Strand,
                        -1, prefix, belt.AcquireSetName, paintWeights: true, keepAsModifierStack: true));
                }
                finally { _deferSpringChainBufferRebuild = false; }
                if (r != null && !r.Success) fail = $"揺れ鎖を作れませんでした: {r.Reason}";
                if (fail == null)
                    foreach (var ch in Poly_Ling.Tools.SpringBoneRig.SpringBoneChainNaming.CollectChainsByName(model, prefix))
                        if (ch.Count > 0) roots.Add(ch[0]);
                if (fail == null && roots.Count == 0) fail = "揺れ鎖のボーンができませんでした";

                // 鎖の根元を最寄りの体のボーンへ付ける。足首のカーブなら足首に付き、体を動かすと一緒に動く。
                if (fail == null)
                    SpringBoneLadderPlacer.ReattachRootsToNearestBody(
                        model, SpringBoneChainNaming.CollectChainsByName(model, prefix));

                // 塗ったウェイトを生成物へ引き継ぐ。
                var outputs = new List<MeshContext>();
                if (fail == null)
                    foreach (var owner in beltOwners)
                    {
                        var step = owner.Stack.Steps[owner.Step];
                        step.SetArg("params.inheritBeltWeights", "true");
                        foreach (ulong id in step.OutputObjectIds)
                        {
                            var o = ModifierStackOps.Resolve(project, id);
                            if (o != null && !outputs.Contains(o)) outputs.Add(o);
                        }
                    }

                if (fail == null && skinned)
                {
                    foreach (var owner in beltOwners)
                    {
                        if (!RunModifierStackStep(project, model, c.ModelIndex, false, owner.Stack, owner.Step, out string err))
                        { fail = err ?? "作り直しに失敗しました"; break; }
                        ModifierEvaluator.MarkRebuilt(model, owner.Stack.Steps[owner.Step]);
                    }
                }
                else if (fail == null)
                {
                    // 帯と生成物をスキンド化し、評価器に流させる（揺れ鎖が帯へ塗る → 生成物が引き継ぐ）。
                    // カーブはスキンド化しない。帯はカーブをバインド姿勢のワールドで読む。
                    var targets = new List<int> { model.MeshContextList.IndexOf(band) };
                    foreach (var o in outputs)
                    {
                        int oi = model.MeshContextList.IndexOf(o);
                        if (oi >= 0 && !targets.Contains(oi) && !o.IsSkinned) targets.Add(oi);
                    }
                    var ids = new List<ulong>(targets.Count);
                    foreach (int ti in targets) ids.Add(model.GetMeshContext(ti).ObjectId);
                    SkinKindConverter.ToSkinned(model, targets, roots[0]);
                    ReevaluateAfterSkinning(model, ids);
                    if (!band.IsSkinned) fail = "帯をスキンド化できませんでした";
                }

                // 生成物の全頂点へウェイトが付いたことを確かめる。付いていないものは揺れない。
                if (fail == null)
                    foreach (var o in outputs)
                    {
                        int missing = 0;
                        foreach (var v in o.MeshObject.Vertices)
                            if (v.BoneWeight == null) missing++;
                        if (missing > 0) { fail = $"「{o.Name}」の {missing} 頂点にウェイトが付きませんでした"; break; }
                    }

                // 揺れ方と鎖の先頭。値は roboF のアホ毛と同じ。揺れもののパネルから変えられる。
                if (fail == null)
                {
                    var sr = Dispatch(new ApplySpringBoneByPrefixCommand(c.ModelIndex, prefix,
                        hitRadius: 0.02f, stiffness: 0.5f, gravityPower: 0f, dragForce: 0.4f));
                    if (sr != null && !sr.Success) fail = $"揺れ方を入れられませんでした: {sr.Reason}";
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            if (_undoController != null)
            {
                _undoController.MeshListStack.BeginGroup("揺れ鎖を作る");
                RecordMeshListSnapshot(listBefore, model, "揺れ鎖を作る");
                for (int gi = groupsBefore; gi < model.ModifierStackCount; gi++)
                {
                    var added = model.ModifierStacks[gi];
                    if (added == null) continue;
                    RecordModifierStackUndo(new ModifierStackChangeRecord { AddedGroup = added.Clone(), AddedIndex = gi },
                                            $"モディファイアスタック追加: {added.Name}");
                }
                if (gIndex >= 0)
                    RecordModifierStackUndo(new ModifierStackChangeRecord { ReplacedIndex = gIndex, OldGroup = oldSnapshot, NewGroup = g.Clone() },
                                            $"スタックの引数: {g.Name}");
                foreach (var snap in otherSnapshots)
                    RecordModifierStackUndo(new ModifierStackChangeRecord { ReplacedIndex = snap.Index, OldGroup = snap.Old, NewGroup = model.ModifierStacks[snap.Index].Clone() },
                                            $"スタックの引数: {snap.Old.Name}");
                _undoController.MeshListStack.EndGroup();
            }

            model.IsDirty = true;
            if (skinned)
            {
                _viewportManager.EnterTopologyChanged(project);
                _notifyPanels(ChangeKind.ListStructure);
            }
            else
            {
                // 階層と頂点の格納空間が変わったので、スキンド化の経路と同じく GPU バッファを作り直す。
                _viewportManager.EnterSceneReset(project, clearScene: true);
                _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                _notifyPanels(ChangeKind.ModelSwitch);
            }

            if (fail != null) return fail;
            ReportData(CommandDataJson.New().Ints("chainRootIndices", roots).Flag("weightsInherited", true).Build(), roots.ToArray());
            return null;
        }

        private string ExecuteCreateSweep(ProjectContext project, ModelContext model, CreateSweepMeshCommand c)
        {
            var inputs = CollectSweepInputs(model);

            _undoController?.SetModelContext(model);
            var listBefore  = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ModifierStackCount;
            var curves = new List<int>();
            var pipes  = new List<int>();
            var stacks = new List<string>();
            string fail;

            _undoController?.SuspendRecording();
            try
            {
                fail = RunSweep(project, model, c, inputs, curves, pipes, stacks);
                // 作ったものを選ぶ（モディファイアパネルに履歴のカードが出る）。
                var show = pipes.Count > 0 ? pipes : curves;
                if (fail == null && show.Count > 0)
                    Dispatch(new SelectMeshCommand(c.ModelIndex, MeshCategory.Drawable, show.ToArray()));
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            RecordEdgeOperationUndo(model, listBefore, groupsBefore, c.CurveOnly ? "カーブ" : "パイプ");

            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            if (fail != null) return fail;
            ReportData(CommandDataJson.New()
                .Ints ("curveIndices", curves)
                .Ints ("pipeIndices",  pipes)
                .Texts("stackNames",   stacks)
                .Build(),
                pipes.Count > 0 ? pipes.ToArray() : curves.ToArray());
            return null;
        }
    }
}
