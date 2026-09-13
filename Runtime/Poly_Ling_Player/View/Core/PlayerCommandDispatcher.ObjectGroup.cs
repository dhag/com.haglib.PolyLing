// PlayerCommandDispatcher.ObjectGroup.cs
// コマンドディスパッチャ：オブジェクトグループ（作り直し・まとめる・解除・自動更新）とその実行部。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：オブジェクトグループ（作り直し・まとめる・解除・自動更新）。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchObjectGroup(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── オブジェクトグループ：まとめる（マクロを組む）
                //
                //   ソースの全ステップをターゲットの末尾へ移し、ソースのグループを消す。
                //   描画オブジェクトは 1 つも消さない。
                case MergeObjectGroupCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }

                    if (string.Equals(c.TargetGroupName, c.SourceGroupName, System.StringComparison.Ordinal))
                    { Fail("同じグループはまとめられません"); return true; }

                    var mgTarget = model.FindObjectGroupByName(c.TargetGroupName);
                    if (mgTarget == null) { Fail($"グループが見つかりません: {c.TargetGroupName}"); return true; }

                    var mgSource = model.FindObjectGroupByName(c.SourceGroupName);
                    if (mgSource == null) { Fail($"グループが見つかりません: {c.SourceGroupName}"); return true; }
                    if (mgSource.StepCount == 0)
                    { Fail($"足すステップがありません: {c.SourceGroupName}"); return true; }

                    int mgSourceIndex = model.ObjectGroups.IndexOf(mgSource);
                    var mgTargetBefore = mgTarget.Clone();
                    var mgSourceBefore = mgSource.Clone();

                    foreach (var mgStep in mgSource.Steps)
                        if (mgStep != null) mgTarget.AddStep(mgStep.Clone());

                    model.RemoveObjectGroup(mgSource);

                    // ターゲットの索引はソースを消したあとに取る。先に取ると、
                    // ソースがターゲットより前にあったときに 1 つずれる。
                    int mgTargetIndex = model.ObjectGroups.IndexOf(mgTarget);

                    mgTarget.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, mgTarget);

                    // 記録は「消す → 差し替える」の順。Undo は後ろから戻すので、
                    // 差し替え（消したあとの索引で有効）→ 挿し戻し の順に効く。
                    if (_undoController != null)
                    {
                        _undoController.MeshListStack.BeginGroup($"オブジェクトグループ結合: {mgTarget.Name}");

                        if (mgSourceIndex >= 0)
                        {
                            RecordObjectGroupUndo(
                                new ObjectGroupChangeRecord
                                {
                                    RemovedGroup = mgSourceBefore,
                                    RemovedIndex = mgSourceIndex,
                                },
                                $"オブジェクトグループ結合: {mgSource.Name} を畳む");
                        }

                        if (mgTargetIndex >= 0)
                        {
                            RecordObjectGroupUndo(
                                new ObjectGroupChangeRecord
                                {
                                    ReplacedIndex = mgTargetIndex,
                                    OldGroup      = mgTargetBefore,
                                    NewGroup      = mgTarget.Clone(),
                                },
                                $"オブジェクトグループ結合: {mgTarget.Name}");
                        }

                        _undoController.MeshListStack.EndGroup();
                    }

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── オブジェクトグループ：解除（描画オブジェクトは消さない）
                case DeleteObjectGroupCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var g = model.FindObjectGroupByName(c.GroupName);
                    if (g == null) { Fail($"グループが見つかりません: {c.GroupName}"); return true; }

                    int gIndex = model.ObjectGroups.IndexOf(g);
                    RecordObjectGroupUndo(
                        new ObjectGroupChangeRecord
                        {
                            RemovedGroup = g.Clone(),
                            RemovedIndex = gIndex,
                        },
                        $"オブジェクトグループ解除: {g.Name}");

                    model.RemoveObjectGroup(g);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── オブジェクトグループ：自動更新の切り替え
                case SetObjectGroupAutoUpdateCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var g = model.FindObjectGroupByName(c.GroupName);
                    if (g == null) { Fail($"グループが見つかりません: {c.GroupName}"); return true; }

                    int gIndex = model.ObjectGroups.IndexOf(g);
                    var before = g.Clone();
                    g.AutoUpdate = c.AutoUpdate;

                    if (gIndex >= 0)
                    {
                        RecordObjectGroupUndo(
                            new ObjectGroupChangeRecord
                            {
                                ReplacedIndex = gIndex,
                                OldGroup      = before,
                                NewGroup      = g.Clone(),
                            },
                            $"オブジェクトグループ自動更新: {g.Name}");
                    }

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── シュリンカー適用
                case ApplyShrinkCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var beforeCtx = model.GetMeshContext(c.BeforeMasterIndex);
                    if (beforeCtx?.MeshObject == null) { Fail("変形前のメッシュがありません"); return true; }

                    // 衝突計算に使うワールド座標をこの時点で1回だけ更新する。
                    _viewportManager.UpdateTransform();

                    var stops = ShrinkOperation.ComputeStopParams(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.ColliderMasterIndices,
                        c.SurfaceOffset, c.FrontFaceOnly, c.CollisionMode, c.MaxPasses,
                        mc => _viewportManager.TryGetMeshWorldPositions(model, mc, out var w) ? w : null,
                        out string shrinkError);

                    if (stops == null)
                    {
                        Debug.LogWarning($"[Shrink] 停止パラメータを算出できません: {shrinkError}");
                        return true;
                    }

                    // 上書きモードのみ、ビフォーの変更を Undo に記録する。
                    // 新規モードではビフォーを変更しないため、スナップショットは取らない。
                    if (!c.CreateNewObject && _undoController != null)
                    {
                        _undoController.SetMeshObject(beforeCtx.MeshObject, beforeCtx.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    var shrinkCtx = BuildSkinWeightToolCtx(model);

                    // パネル側はコマンド送信前にプレビューを破棄して元座標へ戻している。
                    // ここでは可視状態を変更しない（hideAfter: false）。
                    var shrinkPreview = new ShrinkPreviewState();
                    if (!shrinkPreview.Start(
                            model, c.BeforeMasterIndex, c.AfterMasterIndex, stops, hideAfter: false))
                        return true;

                    shrinkPreview.Apply(model, c.Slider, shrinkCtx);
                    ShrinkOperation.Apply(
                        model, shrinkPreview, c.ColliderMasterIndices,
                        c.CreateNewObject, c.RecalculateNormals, shrinkCtx);

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // オブジェクトグループ
        // ================================================================

        /// <summary>
        /// 今プロジェクトに居る全オブジェクトの ObjectId を控える。
        /// 生成の前後で比べて「増えたのはどれか」を出すために使う。
        /// 「維持する」が立っているときしか呼ばない（全モデル走査のため）。
        /// </summary>
        private HashSet<ulong> SnapshotObjectIds()
        {
            var set = new HashSet<ulong>();
            var project = _getProject();
            if (project == null) return set;

            for (int m = 0; m < project.ModelCount; m++)
            {
                var model = project.GetModel(m);
                if (model?.MeshContextList == null) continue;
                for (int i = 0; i < model.MeshContextList.Count; i++)
                {
                    var mc = model.MeshContextList[i];
                    if (mc != null && mc.ObjectId != 0UL) set.Add(mc.ObjectId);
                }
            }
            return set;
        }

        /// <summary>
        /// 「既存へ追加」のときの出力先索引。新規オブジェクトを作るモードでは -1。
        /// 追加先が -1（＝選択の先頭）のときも -1 を返し、
        /// 増えた ObjectId から探す側に任せる。
        /// </summary>
        private static int FallbackOutputIndex(CreatePrimitiveMeshCommand c)
            => (c.Placement.AddMode == Poly_Ling.Player.PrimitiveAddMode.AddToExisting)
                ? c.Placement.AddTargetIndex
                : -1;

        /// <summary>
        /// 実行し終えたコマンドからオブジェクトグループを 1 件作り、モデルへ足す。
        ///
        /// 出力先の決め方は 3 通り。
        ///   ・explicitOutputIds を渡されたらそれをそのまま使う。
        ///     はしごから作る鎖のように「増えたもの全部ではなく、その一部
        ///     （鎖の根元だけ）が出力」になるものはこれで渡す
        ///   ・渡されないときは、実行で増えた ObjectId のうち最も新しいもの 1 つ
        ///     （ObjectId は単調増加なので最大値が最後に作られたもの）
        ///   ・増えていなければ fallbackIndex のオブジェクト（既存へ追加・上書きブレンド）
        ///
        /// 出力先が決まらなくてもグループは作る。値を書くだけのコマンドのように
        /// 出力先を持たないステップがあるため。作り直しはそのステップで止まる。
        /// </summary>
        private void CaptureObjectGroup(
            PanelCommand cmd, HashSet<ulong> beforeIds, int fallbackIndex,
            IReadOnlyList<ulong> explicitOutputIds = null)
        {
            var project = _getProject();
            var model   = project?.CurrentModel;
            if (project == null || model == null) return;

            var outputIds = new List<ulong>();

            if (explicitOutputIds != null)
            {
                for (int i = 0; i < explicitOutputIds.Count; i++)
                    if (explicitOutputIds[i] != 0UL) outputIds.Add(explicitOutputIds[i]);
            }
            else if (beforeIds != null)
            {
                // 増えた ObjectId を拾う。ObjectId は単調増加なので最大値が最後に作られたもの。
                ulong newest = 0UL;

                for (int m = 0; m < project.ModelCount; m++)
                {
                    var mdl = project.GetModel(m);
                    if (mdl?.MeshContextList == null) continue;
                    for (int i = 0; i < mdl.MeshContextList.Count; i++)
                    {
                        var mc = mdl.MeshContextList[i];
                        if (mc == null || mc.ObjectId == 0UL) continue;
                        if (beforeIds.Contains(mc.ObjectId)) continue;
                        if (mc.ObjectId > newest) newest = mc.ObjectId;
                    }
                }

                if (newest != 0UL) outputIds.Add(newest);
            }

            if (outputIds.Count == 0 && fallbackIndex >= 0)
            {
                ulong fallbackId = model.GetMeshContext(fallbackIndex)?.ObjectId ?? 0UL;
                if (fallbackId != 0UL) outputIds.Add(fallbackId);
            }

            var outCtx = outputIds.Count > 0
                ? ObjectGroupOps.Resolve(project, outputIds[0])
                : null;
            string baseName = !string.IsNullOrEmpty(outCtx?.Name) ? outCtx.Name : "Group";

            var group = ObjectGroupOps.Capture(
                project, cmd.ModelIndex, cmd, outputIds,
                model.GenerateUniqueObjectGroupName(baseName));

            if (group == null) return;

            RecordObjectGroupUndo(
                new ObjectGroupChangeRecord
                {
                    AddedGroup = group.Clone(),
                    AddedIndex = model.ObjectGroupCount,
                },
                $"オブジェクトグループ追加: {group.Name}");

            model.AddObjectGroup(group);
        }

        /// <summary>入れ子で 2 周させないための印。</summary>
        private bool _autoUpdateRunning;

        /// <summary>
        /// 「ソースが変わったら自動で作り直す」が立っているグループを流す。
        ///
        /// 【いつ呼ぶか】
        ///   ソースの頂点を書き換える操作のあと。今はスキンド化の 2 経路
        ///   （SkinKindConverter.ToSkinned / MeshFilterToSkinnedConverter.Execute）から。
        ///   スキンド化は頂点をワールドへ焼き直すので、それを取り込むグループの
        ///   ダイジェストが変わる（ObjectGroupOps.ComputeSourceDigest は位置を混ぜる）。
        ///
        /// 【出力に含むグループは流さない】
        ///   ソースに含むものだけを対象にする。出力をスキンド化したあとに
        ///   作り直すと、生成コマンドは MeshFilter 前提の空間で作り直すので、
        ///   焼き直した頂点を壊す。
        ///
        /// 【ダイジェストでは絞らない】
        ///   スキンド化は「ソースが変わった」ではなく「塗る契機」。
        ///   WorldMatrix が単位のオブジェクトは焼いても位置が動かず、
        ///   ダイジェストが変わらないため、絞ると永久に流れない。
        ///
        /// 【Undo】
        ///   ここでは記録しない。呼び出し側が SuspendRecording の中で呼び、
        ///   外で 1 件だけ積む。
        /// </summary>
        /// <returns>最後まで流せたグループの数</returns>
        private int RunAutoUpdateGroups(
            ProjectContext project, ModelContext model, int modelIndex,
            IReadOnlyList<ulong> changedObjectIds)
        {
            if (project == null || model?.ObjectGroups == null) return 0;
            if (changedObjectIds == null || changedObjectIds.Count == 0) return 0;
            if (_autoUpdateRunning) return 0;

            // 対象は先に決める。走らせながら選ぶと、実行でモデルが変わって
            // 途中から条件が揺れる。
            var targets = new List<Poly_Ling.Data.ObjectGroup>();

            foreach (var g in model.ObjectGroups)
            {
                if (g == null || !g.AutoUpdate || g.StepCount == 0) continue;

                bool hit = false;
                for (int i = 0; i < changedObjectIds.Count; i++)
                    if (g.ContainsSource(changedObjectIds[i])) { hit = true; break; }
                if (!hit) continue;

                // ダイジェストでは絞らない。
                //
                // 【なぜ絞ってはいけないか】
                //   スキンド化は「ソースが変わった」ではなく「塗る契機」。
                //   変換は頂点をワールドへ焼くが、WorldMatrix が単位のオブジェクトでは
                //   位置が 1 つも動かないのでダイジェストは変わらない
                //   （MeshFilterToSkinnedConverter.cs:705-707）。
                //   IsStale で絞ると、そういうオブジェクトを取り込むグループが
                //   永久に流れず、ウェイトが塗られないまま残る。
                //
                //   流す相手は ContainsSource で既に「変換の対象を取り込むもの」だけに
                //   絞られている。冪等なので、要更新でなくても流して壊れない。

                targets.Add(g);
            }

            if (targets.Count == 0) return 0;

            int done = 0;
            _autoUpdateRunning = true;
            try
            {
                foreach (var g in targets)
                {
                    bool ok = true;

                    for (int si = 0; si < g.StepCount; si++)
                    {
                        // 退避は作らない。自動で流れるものが実行のたびに
                        // 複製を増やすと、モデルが静かに膨らむ。
                        if (!RunObjectGroupStep(
                                project, model, modelIndex, false, g, si, out string err))
                        {
                            Debug.LogWarning(
                                $"[ObjectGroup] 自動更新が止まりました: {g.Name} ステップ {si}: {err}");
                            ok = false;
                            break;
                        }
                    }

                    g.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, g);
                    if (ok) done++;
                }
            }
            finally
            {
                _autoUpdateRunning = false;
            }

            return done;
        }

        /// <summary>
        /// オブジェクトグループの 1 ステップを実行する。
        ///
        /// やること
        ///   1. そのステップの出力先を、控えの ObjectId から今の索引へ引き直す
        ///   2. ステップから生成コマンドを組み直す
        ///   3. 出力先があれば「そこへ書き戻す」形へ差し替える（PLParam.RebuildRole）
        ///   4. 実行する
        ///
        /// 【出力先を持たないステップ】
        ///   値を書くだけのコマンド（揺れ方の設定など）は出力先を持たない。
        ///   そのときは書き戻しをせず、組み直したコマンドをそのまま実行する。
        ///
        /// 【退避】
        ///   先頭ステップが単数の描画メッシュを出力するときだけ作る。
        ///   ステップごとに作ると、実行のたびに複製がステップ数ぶん増える。
        ///
        /// 【Undo】
        ///   ここでは記録しない。呼び出し側がマクロ全体で 1 件だけ積む。
        /// </summary>
        private bool RunObjectGroupStep(
            ProjectContext project, ModelContext model,
            int modelIndex, bool keepStash,
            Poly_Ling.Data.ObjectGroup g, int stepIndex,
            out string error)
        {
            error = null;

            var step = g.GetStep(stepIndex);
            if (step == null) { error = $"ステップ {stepIndex} がありません"; return false; }

            // 実行しない段（説明・指示・確認）は何もせず通す。
            // 失敗にすると、手本から起こしたマクロが説明 1 行で止まる。
            if (!step.IsExecutable) return true;

            System.Type stepType = PanelCommandFactory.ResolveType(step.Action);
            if (stepType == null) { error = $"未対応の action: {step.Action}"; return false; }

            var rbKeys = PanelCommandFactory.RebuildKeys(stepType);

            // 出力先を控えの ObjectId から今の索引へ引き直す。
            var outIndices = new List<int>();
            MeshContext outCtx = null;

            foreach (ulong outId in step.OutputObjectIds)
            {
                var mc = ObjectGroupOps.Resolve(project, outId);
                if (mc == null)
                { error = $"出力先の描画オブジェクトが見つかりません (ObjectId={outId})"; return false; }

                int idx = model.MeshContextList.IndexOf(mc);
                if (idx < 0) { error = "出力先が現在のモデルにありません"; return false; }

                outIndices.Add(idx);
                if (outCtx == null) outCtx = mc;
            }

            bool writeBack = outIndices.Count > 0;
            if (writeBack && !rbKeys.IsSupported)
            { error = "このステップは作り直しに対応していません"; return false; }

            // 出力先が単数の描画メッシュのときだけ、退避と頂点数の検査をする。
            // ボーンの MeshObject は頂点を持たないので、検査を通すと必ず落ちる。
            bool singleMesh = writeBack && !rbKeys.TargetIsArray;
            if (singleMesh && outCtx?.MeshObject == null)
            { error = "出力先の描画オブジェクトが見つかりません"; return false; }

            if (keepStash && stepIndex == 0 && singleMesh)
            {
                var prevStash = g.HasStash ? ObjectGroupOps.Resolve(project, g.StashObjectId) : null;

                string stashName = model.GenerateUniqueMeshName(outCtx.Name + "_stash");
                var stash = MeshContextCloneOps.Clone(
                    outCtx, MeshContextCloneKind.NewObject, stashName);
                if (stash != null)
                {
                    stash.IsVisible = false;
                    model.Add(stash);
                    g.StashObjectId = stash.ObjectId;

                    // 退避は最新の 1 件だけ持つ。前回のものは片づける。
                    if (prevStash != null && !ReferenceEquals(prevStash, stash))
                    {
                        int pi = model.MeshContextList.IndexOf(prevStash);
                        if (pi >= 0) model.RemoveAt(pi);
                    }

                    // 出力先の索引は退避の追加・削除でずれうる。引き直す。
                    outIndices[0] = model.MeshContextList.IndexOf(outCtx);
                    if (outIndices[0] < 0)
                    { error = "退避のあとに出力先を引けませんでした"; return false; }
                }
            }

            var rebuilt = ObjectGroupOps.BuildCommand(
                project, modelIndex, g, stepIndex, out string rbErr);
            if (rebuilt == null)
            { error = rbErr ?? "作り直すコマンドを組めませんでした"; return false; }

            // 出力先へ書き戻す形へ差し替える。
            //
            // どのキーへ何を書くかはコマンド側の印が持つ（PLParam.RebuildRole）。
            // ここでコマンド型を見て分岐すると、書き戻せるコマンドを足すたびに
            // 分岐が伸びる。キーの規則（先頭小文字・別名表・ドット区切り）も
            // 外で組み立てると必ずずれるので、PanelCommandFactory から引く。
            var rebuiltType = rebuilt.GetType();
            var rebuiltArgs = PanelCommandFactory.ToArgs(rebuilt);

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            if (writeBack)
            {
                if (rbKeys.TargetIsArray)
                {
                    var parts = new List<string>(outIndices.Count);
                    foreach (int oi in outIndices) parts.Add(oi.ToString(inv));
                    rebuiltArgs[rbKeys.TargetIndexKey] = string.Join(",", parts);
                }
                else
                {
                    rebuiltArgs[rbKeys.TargetIndexKey] = outIndices[0].ToString(inv);
                }

                if (rbKeys.HasMode) rebuiltArgs[rbKeys.ModeKey] = rbKeys.ModeArgValue;
            }

            var writeBackCmd = PanelCommandFactory.Create(
                PanelCommandFactory.ActionOf(rebuiltType), modelIndex,
                rebuiltArgs, out string wbErr);
            if (writeBackCmd == null)
            { error = wbErr ?? "書き戻しコマンドを組めませんでした"; return false; }

            int vertsBefore = singleMesh ? outCtx.MeshObject.VertexCount : -1;

            // Dispatch は _pendingResult を退避・復元するので、結果は
            // 戻り値で受け取ること（_pendingResult を見ても復元済みで分からない）。
            var stepResult = Dispatch(writeBackCmd);
            if (stepResult != null && !stepResult.Success)
            { error = stepResult.Reason ?? "作り直しに失敗しました"; return false; }

            if (singleMesh && outCtx.MeshObject.VertexCount == 0)
            { error = $"作り直しの結果が空になりました（{vertsBefore} → 0）"; return false; }

            return true;
        }

        /// <summary>グループ変更を MeshList スタックへ記録する。</summary>
        private void RecordObjectGroupUndo(ObjectGroupChangeRecord record, string description)
        {
            if (_undoController == null || record == null) return;
            _undoController.MeshListStack.Record(record, description);
        }

        private static List<MeshContext> CollectSelectedMeshContexts(ModelContext model)
        {
            var list = new List<MeshContext>();
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc != null) list.Add(mc);
            }
            if (list.Count == 0)
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) list.Add(mc);
            }
            return list;
        }
    }
}
