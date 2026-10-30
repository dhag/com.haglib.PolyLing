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
                //   ソースの全項目をターゲットの末尾へ移し、ソースのグループを消す。
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
                    { Fail($"足す項目がありません: {c.SourceGroupName}"); return true; }

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
                case PurgeObjectGroupsCommand:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    // 従来パネルが直接呼んでいた処理をそのまま受け口へ移した（Undo 記録なしは従来どおり）。
                    int purged = ObjectGroupOps.PurgeMissing(project, model);
                    ReportData(CommandDataJson.New().Int("purged", purged).Build());
                    _notifyPanels?.Invoke(ChangeKind.ListStructure);
                    return true;
                }

                case DeleteObjectGroupCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var g = model.FindObjectGroupByName(c.GroupName);
                    if (g == null) { Fail($"グループが見つかりません: {c.GroupName}"); return true; }

                    int gIndex = model.ObjectGroups.IndexOf(g);
                    var removedRecord = new ObjectGroupChangeRecord
                    {
                        RemovedGroup = g.Clone(),
                        RemovedIndex = gIndex,
                    };
                    string releaseDesc = $"オブジェクトグループ解除: {g.Name}";

                    // 「適用」：出力を普通のメッシュにする。処理器のある項目（サブディビジョン）は
                    // 処理器が立てたロック・表示を戻すので、一覧のスナップショットごと Undo 1 件にする。
                    _undoController?.SetModelContext(model);
                    var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
                    bool released  = ModifierEvaluator.ReleaseGroup(model, g);

                    model.RemoveObjectGroup(g);

                    if (released)
                    {
                        RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount,
                            new List<ObjectGroupChangeRecord> { removedRecord }, releaseDesc);
                        model.IsDirty = true;
                        _viewportManager.EnterTopologyChanged(project);
                    }
                    else
                    {
                        RecordObjectGroupUndo(removedRecord, releaseDesc);
                    }

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

                // ── オブジェクトグループ：項目の引数を書き換えて、その項目だけ作り直す（段 3）
                case SetObjectGroupStepArgsCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSetGroupStepArgs(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                // ── オブジェクトグループ：出力の編集の確定・破棄（段 6）
                case CommitModifierOutputEditCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteModifierOutputEdit(project, model, c.GroupName, c.StepIndex, commit: true);
                    if (reason != null) Fail(reason);
                    return true;
                }
                case DiscardModifierOutputEditCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteModifierOutputEdit(project, model, c.GroupName, c.StepIndex, commit: false);
                    if (reason != null) Fail(reason);
                    return true;
                }

                // ── シュリンカー適用
                case ApplyShrinkCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var beforeCtx = model.GetMeshContext(c.BeforeMasterIndex);
                    if (beforeCtx?.MeshObject == null) { Fail("変形前のメッシュがありません"); return true; }

                    // 衝突計算に使うワールド座標をこの時点で1回だけ合わせる
                    // （CPU の位置を送ってから変換する。段 B の SettleWorldPositions）。
                    _viewportManager.SettleWorldPositions(project);

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
        /// 出力先を持たない項目があるため。作り直しはその項目で止まる。
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

            // 依存の循環になる登録は拒否する（作り直し整備計画 段 4）。
            // 既存へ追加・置き換えで、出力先が上流の項目の入力になっているときに起きる。
            // グループは足さない。生成物はそのまま残り、そのコマンドの Undo で消せる。
            string cycle = ModifierGraph.CycleReason(model, group, focusInModel: false);
            if (cycle != null)
            {
                Fail($"グループとして残すと依存が循環するので、グループに登録しませんでした: {cycle}");
                return;
            }

            RecordObjectGroupUndo(
                new ObjectGroupChangeRecord
                {
                    AddedGroup = group.Clone(),
                    AddedIndex = model.ObjectGroupCount,
                },
                $"オブジェクトグループ追加: {group.Name}");

            model.AddObjectGroup(group);

            // 作った直後の入力・出力を控える（作り直し整備計画 段 9）。画面の更新の前に
            // 入力・出力を編集しても、要評価・未確定として扱われるようにする。
            ModifierEvaluator.PrimeGroup(model, group);
        }

        /// <summary>
        /// 作り直しの書き戻し（RunObjectGroupStep の入れ子の Dispatch）の深さ。
        /// 0 より大きい間は出力の保護（ModifierOutputGuard）を掛けない。
        /// </summary>
        private int _modifierWriteBackDepth;

        // スキンド化の直後の塗り直し（旧 RunAutoUpdateGroups）は、評価器へ寄せた
        // （PlayerCommandDispatcher.ModifierEval.cs の ReevaluateAfterSkinning。作り直し整備計画 段 5）。

        /// <summary>
        /// オブジェクトグループの 1 項目を実行する。
        ///
        /// やること
        ///   1. その項目の出力先を、控えの ObjectId から今の索引へ引き直す
        ///   2. 項目から生成コマンドを組み直す
        ///   3. 出力先があれば「そこへ書き戻す」形へ差し替える（PLParam.RebuildRole）
        ///   4. 実行する
        ///
        /// 【出力先を持たない項目】
        ///   値を書くだけのコマンド（揺れ方の設定など）は出力先を持たない。
        ///   そのときは書き戻しをせず、組み直したコマンドをそのまま実行する。
        ///
        /// 【退避】
        ///   先頭項目が単数の描画メッシュを出力するときだけ作る。
        ///   項目ごとに作ると、実行のたびに複製が項目数ぶん増える。
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
            if (step == null) { error = $"項目 {stepIndex} がありません"; return false; }

            // 実行しない項目（説明・指示・確認）は何もせず通す。
            // 失敗にすると、シナリオから起こしたマクロが説明 1 行で止まる。
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
            { error = "この項目は作り直しに対応していません"; return false; }

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

                if (rbKeys.HasMode)
                {
                    StripPoseOwnedByOutput(rebuilt, rbKeys.ModeKey, rebuiltArgs);
                    rebuiltArgs[rbKeys.ModeKey] = rbKeys.ModeArgValue;
                }
            }

            var writeBackCmd = PanelCommandFactory.Create(
                PanelCommandFactory.ActionOf(rebuiltType), modelIndex,
                rebuiltArgs, out string wbErr);
            if (writeBackCmd == null)
            { error = wbErr ?? "書き戻しコマンドを組めませんでした"; return false; }

            int vertsBefore = singleMesh ? outCtx.MeshObject.VertexCount : -1;

            // スキンド化のあとの評価空間（段 5。ModifierSpace）。
            //   評価できない項目は流さない。梯子を読む項目は、生成物を梯子の格納空間から
            //   出力の格納空間へ移す行列を、書き戻し（PrimitiveMeshReplaceExisting）へ渡す。
            if (!ComputeStepSpace(model, step, outCtx, singleMesh, out var spaceWb, out string spaceErr))
            { error = spaceErr; return false; }

            // Dispatch は _pendingResult を退避・復元するので、結果は
            // 戻り値で受け取ること（_pendingResult を見ても復元済みで分からない）。
            // 書き戻し先はグループの出力なので、出力の保護（ModifierOutputGuard）を掛けない。
            CommandResult stepResult;
            _modifierWriteBackDepth++;
            var spaceScope = ModifierSpace.Begin(spaceWb);
            try     { stepResult = Dispatch(writeBackCmd); }
            finally { spaceScope.Dispose(); _modifierWriteBackDepth--; }
            if (stepResult != null && !stepResult.Success)
            { error = stepResult.Reason ?? "作り直しに失敗しました"; return false; }

            if (singleMesh && outCtx.MeshObject.VertexCount == 0)
            { error = $"作り直しの結果が空になりました（{vertsBefore} → 0）"; return false; }

            return true;
        }

        /// <summary>
        /// 作ったときの追加先モードが新規オブジェクト・新規モデルなら、置き方の位置と
        /// 頂点へ焼かなかった回転・拡大は出力の姿勢（BoneTransform）が持っている
        /// （PolyLingPlayerViewerCore.BuildPrimitiveMeshContext）。書き戻し（ReplaceExisting）は
        /// 渡った位置・回転・拡大を頂点へ焼くので、そのまま渡すと二重に掛かり、作り直すたびに
        /// 出力が位置の分だけずれていた（段 8 で確かめた）。書き戻しの引数ではそれらを単位にする。
        /// 既存へ追加で作ったもの（頂点へ焼いてある）は変えない。
        /// modeKey は書き換える前の追加先モードのキー（例 placement.addMode）。
        /// </summary>
        internal static void StripPoseOwnedByOutput(
            PanelCommand rebuilt, string modeKey, Dictionary<string, string> args)
        {
            if (rebuilt == null || string.IsNullOrEmpty(modeKey) || args == null) return;
            if (!args.TryGetValue(modeKey, out var modeText)) return;
            if (!Enum.TryParse(modeText, true, out PrimitiveAddMode mode)) return;
            if (mode != PrimitiveAddMode.NewObject && mode != PrimitiveAddMode.NewModel) return;

            int dot = modeKey.LastIndexOf('.');
            string prefix = dot >= 0 ? modeKey.Substring(0, dot + 1) : "";

            void Set(string leaf, string value)
            {
                string k = prefix + leaf;
                if (args.ContainsKey(k)) args[k] = value;
            }

            Set("worldPosition", "0,0,0");

            bool poseRot, poseScl;
            switch (rebuilt)
            {
                case CreatePrimitiveMeshCommand pc:
                    poseRot = pc.PoseRotation != Vector3.zero;
                    poseScl = pc.PoseScale    != Vector3.one;
                    break;
                case AddGeneratedMeshCommand ag:
                    poseRot = !ag.PoseAlreadyBaked;
                    poseScl = !ag.PoseAlreadyBaked;
                    break;
                default:
                    poseRot = poseScl = false;   // 姿勢へ回転・拡大を入れない生成（辺の帯・頂点へ藤壺など）
                    break;
            }
            if (poseRot) Set("placeRotation", "0,0,0");
            if (poseScl) Set("placeScale", "1,1,1");
        }

        /// <summary>
        /// 項目の評価空間（段 5。ModifierSpace）。評価できなければ false と理由。
        /// 梯子を読む項目で M が要るときは wb に行列と出力を入れる。作り直しと逆方向（段 6）が同じ判定を使う。
        /// </summary>
        private static bool ComputeStepSpace(
            ModelContext model, ObjectGroupStep step, MeshContext outCtx, bool singleMesh,
            out ModifierSpace.WriteBack wb, out string error)
        {
            wb = null; error = null;
            model.ComputeWorldMatrices();
            ModifierEvaluator.Entry spaceEntry = null;
            foreach (var ge in ModifierEvaluator.GetGraph(model))
                if (ReferenceEquals(ge.Step, step)) { spaceEntry = ge; break; }
            if (spaceEntry == null) return true;

            var kind = ModifierSpace.Classify(spaceEntry, out string spaceWhy);
            if (kind == ModifierSpaceKind.Unsupported)
            { error = $"スキンド化のあとは作り直せない: {spaceWhy}"; return false; }

            if (kind == ModifierSpaceKind.Transform && singleMesh)
            {
                string beltKey = ModifierSpace.SpaceSourceKey(spaceEntry.CommandType);
                var beltCtx = beltKey != null ? spaceEntry.Input(beltKey) : null;
                if (ModifierSpace.TryComputeMatrix(beltCtx, outCtx, IsPoseOwnedByOutput(spaceEntry.CommandType, step),
                                                   out var spaceM))
                    wb = new ModifierSpace.WriteBack { Matrix = spaceM, Output = outCtx };
            }
            return true;
        }

        /// <summary>判定は ModifierSpace.IsPoseOwnedByOutput に 1 か所（評価器の署名と共有。段 E）。</summary>
        private static bool IsPoseOwnedByOutput(System.Type t, ObjectGroupStep step)
            => ModifierSpace.IsPoseOwnedByOutput(t, step);

        /// <summary>
        /// 項目 1 つの引数を書き換え、その項目だけを作り直す（PolyLing_作り直し整備計画.md 段 3）。
        ///
        /// 検査（ObjectGroupOps.TryValidateStepArgs）で落ちたら何も書かない。
        /// 作り直しに失敗したら引数を元へ戻す（ExecuteReplaceGroupSource と同じ）。
        /// Undo はオブジェクト一覧のスナップショットとグループの差し替えを 1 件にまとめる。
        /// 戻り値は失敗理由。成功時は null。
        /// </summary>
        private string ExecuteSetGroupStepArgs(ProjectContext project, ModelContext model, SetObjectGroupStepArgsCommand c)
        {
            var g = model.FindObjectGroupByName(c.GroupName);
            if (g == null) return $"グループが見つかりません: {c.GroupName}";

            if (!ObjectGroupOps.TryValidateStepArgs(
                    project, c.ModelIndex, g, c.StepIndex, c.Keys, c.Values, c.AllowProfileKeys, out string vErr))
                return vErr;

            var step = g.GetStep(c.StepIndex);
            int gIndex = model.ObjectGroups.IndexOf(g);
            var oldSnapshot = g.Clone();

            var oldArgs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in step.Args) oldArgs[kv.Key] = kv.Value;

            for (int i = 0; i < c.Keys.Length; i++) step.SetArg(c.Keys[i], c.Values[i]);

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);

            string fail = null;
            _undoController?.SuspendRecording();
            try
            {
                if (!RunObjectGroupStep(project, model, c.ModelIndex, false, g, c.StepIndex, out string err))
                    fail = err ?? "作り直しに失敗しました";
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            // 作り直せなかったときは引数を元へ戻す（グループと出力の中身を食い違わせない）。
            if (fail != null)
            {
                step.Args.Clear();
                foreach (var kv in oldArgs) step.Args[kv.Key] = kv.Value;
            }
            else
            {
                // 下流へ伝える（段 4）。下流はこのコマンドの終わりに依存の順で流れる。
                ModifierEvaluator.MarkRebuilt(model, step);
            }

            g.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, g);

            RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount,
                gIndex >= 0
                    ? new List<ObjectGroupChangeRecord>
                      {
                          new ObjectGroupChangeRecord { ReplacedIndex = gIndex, OldGroup = oldSnapshot, NewGroup = g.Clone() },
                      }
                    : null,
                $"グループの引数: {g.Name} 項目 {c.StepIndex}");

            FinishGroupEdit(project, model);
            if (fail != null) return $"作り直しに失敗したので引数を戻しました: {fail}";

            var outIdx = new List<int>();
            var outIds = new List<ulong>();
            foreach (ulong id in step.OutputObjectIds)
            {
                var mc = ObjectGroupOps.Resolve(project, id);
                int idx = mc != null ? model.MeshContextList.IndexOf(mc) : -1;
                if (idx < 0) continue;
                outIdx.Add(idx);
                outIds.Add(id);
            }
            if (outIdx.Count > 0) ReportTargets(outIdx.ToArray(), outIds.ToArray());
            return null;
        }

        /// <summary>グループ変更を MeshList スタックへ記録する。</summary>
        private void RecordObjectGroupUndo(ObjectGroupChangeRecord record, string description)
        {
            if (_undoController == null || record == null) return;
            _undoController.MeshListStack.Record(record, description);
        }

        /// <summary>オブジェクト一覧の Undo スタックへ記録し、Undo の対象を一覧へ向ける（モーフエクスプレッション等）。</summary>
        private void RecordMeshListUndo(Poly_Ling.UndoSystem.MeshListUndoRecord record, string description)
        {
            if (_undoController == null || record == null) return;
            _undoController.MeshListStack.Record(record, description);
            _undoController.FocusMeshList();
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
