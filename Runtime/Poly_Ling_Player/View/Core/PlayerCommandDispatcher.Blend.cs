// PlayerCommandDispatcher.Blend.cs
// コマンドディスパッチャ：モデルブレンド・メッシュブレンドと、その静的ヘルパ。
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
        /// DispatchCore の分担：モデルブレンド・メッシュブレンド。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchBlend(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── モデルブレンド: プレビュー（Undo なし）
                case PreviewModelBlendCommand c:
                {
                    // 設計 A: クローンは CreateBlendCloneCommand で既に CurrentModel。
                    // ExecuteBlend は project.GetModel(c.CloneModelIndex) を書き換えるが、
                    // CurrentModel と同じであれば GPU は EnterTopologyChanged で正規更新される。
                    if (project.CurrentModelIndex != c.CloneModelIndex)
                    {
                        Debug.LogWarning(
                            $"[PlayerCommandDispatcher] PreviewModelBlend: CurrentModel " +
                            $"({project.CurrentModelIndex}) != CloneModelIndex ({c.CloneModelIndex})。" +
                            $"設計 A 規約違反。CreateBlendCloneCommand 後の Select が行われていない可能性。");
                        return true;
                    }
                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, recalcNormals: false, blendBones: c.BlendBones,
                        onSyncMesh: null);
                    // Phase 2a-2g-1: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
                    // EnterTopologyChanged に集約。CurrentModel = clone なので正規入口で対応可能。
                    _viewportManager.EnterTopologyChanged(project);
                    return true;
                }

                // ── モデルブレンド: 適用
                case ApplyModelBlendCommand c:
                {
                    // 設計 A: クローンは CreateBlendCloneCommand で既に CurrentModel。
                    if (project.CurrentModelIndex != c.CloneModelIndex)
                    {
                        Debug.LogWarning(
                            $"[PlayerCommandDispatcher] ApplyModelBlend: CurrentModel " +
                            $"({project.CurrentModelIndex}) != CloneModelIndex ({c.CloneModelIndex})。" +
                            $"設計 A 規約違反。");
                        return true;
                    }
                    var cloneModelApply = project.CurrentModel;

                    // クローンモデルをUndoControllerのMeshListStackコンテキストに設定
                    _undoController?.SetModelContext(cloneModelApply);

                    // 適用前スナップショット
                    var beforePos = ModelBlendRecord.CapturePositions(cloneModelApply);

                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, c.RecalcNormals, c.BlendBones,
                        onSyncMesh: null);

                    // 適用後スナップショット
                    var afterPos = ModelBlendRecord.CapturePositions(cloneModelApply);

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var record = new ModelBlendRecord
                        {
                            BeforePositions = beforePos,
                            AfterPositions  = afterPos,
                        };
                        {
                            string __dbgDesc = "モデルブレンド適用";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + SetSelectionState + UpdateSelectedDrawableMesh を
                    // EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── メッシュブレンド適用
                case ApplyBlendCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }

                    var destCtx = model.GetMeshContext(c.DestMasterIndex);
                    if (destCtx?.MeshObject == null) { Fail("書き込み先のメッシュがありません"); return true; }

                    // ソースは別モデルを指せる。MasterIndex は必ずその
                    // BlendSourceSpec.ModelIndex のモデル内で引くこと。
                    // 宛先モデルの索引で引くと無関係なメッシュを混ぜる。
                    var sources    = new System.Collections.Generic.List<BlendSourceEntry>();
                    var hideIndices = new System.Collections.Generic.List<int>();
                    foreach (var spec in c.Sources)
                    {
                        if (spec.Weight <= 0f) continue;
                        var srcModel = project.GetModel(spec.ModelIndex);
                        var srcCtx   = srcModel?.GetMeshContext(spec.MasterIndex);
                        if (srcCtx?.MeshObject == null) continue;
                        sources.Add(new BlendSourceEntry(srcCtx, spec.Weight));

                        // 同一モデル内のソースだけプレビュー中に隠す。
                        // 別モデルの索引を混ぜると索引空間が違うため別物を隠す。
                        if (spec.ModelIndex == c.ModelIndex && spec.MasterIndex != c.DestMasterIndex)
                            hideIndices.Add(spec.MasterIndex);
                    }
                    if (sources.Count == 0) { Fail("ブレンド元がありません"); return true; }

                    // ToolContext 構築（UndoController・CommandQueue 接続済み）。
                    // Undo の対象メッシュ指定は BlendOperation が SetMeshObjectFor で行う。
                    var blendCtx = BuildSkinWeightToolCtx(model);
                    if (_undoController != null)
                        _undoController.MeshUndoContext.ParentModelContext = model;

                    // バックアップ位置を取ってから確定する。
                    // ApplyBlend が同じ backup を基準に混ぜるので、
                    // ここで preview.Apply を挟むと同じ計算を 2 回走らせるだけになる。
                    var preview = new BlendPreviewState();
                    preview.Start(model, c.DestMasterIndex, hideIndices);

                    var __blendBeforeIds = c.KeepAsGroup ? SnapshotObjectIds() : null;

                    BlendOperation.ApplyBlend(
                        model, preview, sources,
                        c.RecalculateNormals, c.SelectedVerticesOnly,
                        c.MatchMode, c.CreateNewObject, blendCtx);

                    if (c.KeepAsGroup)
                    {
                        // 新規オブジェクトを作らない設定では宛先そのものが出力先になる。
                        // 作る設定では新しく増えた ObjectId が出力先。
                        CaptureObjectGroup(c, __blendBeforeIds,
                            c.CreateNewObject ? -1 : c.DestMasterIndex);
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── オブジェクトグループ：作り直し
                //
                // 【出力先は作り直さず、中身だけ入れ替える】
                //   新しいオブジェクトを作ると ObjectId が変わり、名前・階層・姿勢・
                //   材質割当も引き継げない。出力先を指している参照が毎回切れる。
                //   AddMode を ReplaceExisting にして、既存の出力先へ書き戻す。
                //
                // 【頂点IDは残らない】
                //   中身の総入れ替えなので、出力先へ手で振った頂点IDは失われる。
                //   出力先にIDを振るなら、先にグループを解除すること。
                case RebuildObjectGroupCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }

                    var g = model.FindObjectGroupByName(c.GroupName);
                    if (g == null) { Fail($"グループが見つかりません: {c.GroupName}"); return true; }
                    if (g.StepCount == 0) { Fail($"グループにステップがありません: {c.GroupName}"); return true; }

                    int gIndex = model.ObjectGroups.IndexOf(g);
                    var oldSnapshot = g.Clone();

                    // 【Undo はマクロ全体で 1 件】
                    //   内側の記録は止め、終わってから MeshList へまとめて積む。
                    //   MeshFilterToSkinnedRecord は MeshContextList を丸ごと控えるので、
                    //   ボーンの追加・位置・はしごのウェイトまで 1 件で戻せる。
                    //
                    //   CollapseToGroup では畳めない。UndoGroup._undoLog は積んだぶんが
                    //   残るので、押下回数と巻き戻る量がずれる（UndoGroup.cs:272-292）。
                    _undoController?.SetModelContext(model);
                    var rbListBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    string rbFail = null;
                    int    rbDone = 0;

                    _undoController?.SuspendRecording();
                    try
                    {
                        for (int si = 0; si < g.StepCount; si++)
                        {
                            if (!RunObjectGroupStep(
                                    project, model, c.ModelIndex, c.KeepStash, g, si, out string stepErr))
                            { rbFail = stepErr; break; }
                            rbDone++;
                        }
                    }
                    finally
                    {
                        _undoController?.ResumeRecording();
                    }

                    g.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, g);

                    // 途中で落ちても記録は積む。そこまでの変更を Undo で戻せるようにする。
                    // 同じスタックへ続けて積むので Undo のログは 1 件に集約される。
                    if (_undoController != null)
                    {
                        _undoController.MeshListStack.BeginGroup($"オブジェクトグループ実行: {g.Name}");

                        RecordMeshListSnapshot(
                            rbListBefore, model, $"オブジェクトグループ実行: {g.Name}");

                        if (gIndex >= 0)
                        {
                            RecordObjectGroupUndo(
                                new ObjectGroupChangeRecord
                                {
                                    ReplacedIndex = gIndex,
                                    OldGroup      = oldSnapshot,
                                    NewGroup      = g.Clone(),
                                },
                                $"オブジェクトグループ実行: {g.Name}");
                        }

                        _undoController.MeshListStack.EndGroup();
                    }

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);

                    if (rbFail != null)
                    { Fail($"ステップ {rbDone} で止まりました: {rbFail}"); return true; }

                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // モデルブレンド静的ヘルパー
        // PolyLingCore_Commands.cs から移植（private→internalに昇格）
        // ================================================================

        private static void ExecuteBlend(
            ProjectContext project,
            int sourceModelIndex,
            int cloneModelIndex,
            float[] weights,
            bool[] meshEnabled,
            bool recalcNormals,
            bool blendBones,
            Action<MeshContext> onSyncMesh)
        {
            var cloneModel = project.GetModel(cloneModelIndex);
            if (cloneModel == null) return;

            // ウェイト正規化
            float total = 0f;
            foreach (var w in weights) total += w;
            float[] nw = new float[weights.Length];
            if (total > 0f)
                for (int i = 0; i < weights.Length; i++) nw[i] = weights[i] / total;
            else
            {
                float eq = weights.Length > 0 ? 1f / weights.Length : 0f;
                for (int i = 0; i < weights.Length; i++) nw[i] = eq;
            }

            var cloneDrawables = cloneModel.DrawableMeshes;
            var targetEntries  = new System.Collections.Generic.List<(int drawableIdx, TypedMeshEntry entry)>();
            for (int di = 0; di < cloneDrawables.Count; di++)
            {
                var e = cloneDrawables[di];
                if (e.Type == MeshType.MirrorSide || e.Type == MeshType.BakedMirror) continue;
                if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                targetEntries.Add((di, e));
            }

            var targetVertCountRaw      = targetEntries.Select(t => t.entry.MeshObject.VertexCount).ToArray();
            var targetVertCountExpanded = targetEntries.Select(t =>
                t.entry.Context.UnityMesh != null
                    ? t.entry.Context.UnityMesh.vertexCount
                    : t.entry.MeshObject.VertexCount).ToArray();

            var srcFilteredMap  = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<TypedMeshEntry>>();
            var srcExpCountsMap = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
            for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                var m = project.GetModel(modelIdx);
                if (m == null) continue;
                var srcDrawables = m.DrawableMeshes;
                var filtered  = new System.Collections.Generic.List<TypedMeshEntry>();
                var expCounts = new System.Collections.Generic.List<int>();
                for (int di = 0; di < srcDrawables.Count; di++)
                {
                    var e = srcDrawables[di];
                    if (e.Type == MeshType.MirrorSide || e.Type == MeshType.BakedMirror) continue;
                    if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                    filtered.Add(e);
                    int ec = e.Context.UnityMesh != null
                        ? e.Context.UnityMesh.vertexCount
                        : e.MeshObject.VertexCount;
                    expCounts.Add(ec);
                }
                srcFilteredMap[modelIdx]  = filtered;
                srcExpCountsMap[modelIdx] = expCounts;
            }

            var srcCursors = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var key in srcFilteredMap.Keys) srcCursors[key] = 0;

            for (int k = 0; k < targetEntries.Count; k++)
            {
                int drawableIdx = targetEntries[k].drawableIdx;
                if (drawableIdx < meshEnabled.Length && !meshEnabled[drawableIdx]) continue;

                var targetEntry = targetEntries[k].entry;
                var targetMesh  = targetEntry.MeshObject;
                int rawCount    = targetVertCountRaw[k];
                int expCount    = targetVertCountExpanded[k];

                var nonIsolated  = BuildBlendNonIsolatedSet(targetMesh);
                var blended      = new Vector3[rawCount];
                bool targetIsTriangulated = targetMesh.IsTriangulated;

                foreach (var kv in srcFilteredMap)
                {
                    float w = nw[kv.Key];
                    var srcList      = kv.Value;
                    var srcExpCounts = srcExpCountsMap[kv.Key];
                    int cursor       = srcCursors[kv.Key];
                    int matchSi      = -1;
                    for (int si = cursor; si < srcExpCounts.Count; si++)
                    {
                        if (srcExpCounts[si] == expCount) { matchSi = si; break; }
                    }
                    if (matchSi < 0) continue;
                    srcCursors[kv.Key] = matchSi + 1;
                    var srcMesh = srcList[matchSi].MeshObject;
                    bool srcIsTriangulated = srcMesh.IsTriangulated;

                    if (targetIsTriangulated)
                    {
                        var srcInvMap = srcIsTriangulated ? null : srcMesh.BuildInverseExpansionMap();
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsTriangulated)
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            else
                            {
                                if (!srcInvMap.TryGetValue(vi, out var r)) continue;
                                srcPos = srcMesh.Vertices[r.vIdx].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                    else
                    {
                        var srcExpMap = srcIsTriangulated ? targetMesh.BuildExpansionMap() : null;
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsTriangulated)
                            {
                                if (!srcExpMap.TryGetValue((vi, 0), out int srcEi)) continue;
                                if (srcEi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[srcEi].Position;
                            }
                            else
                            {
                                if (vi >= srcMesh.Vertices.Count) continue;
                                srcPos = srcMesh.Vertices[vi].Position;
                            }
                            blended[vi] += srcPos * w;
                        }
                    }
                }

                for (int vi = 0; vi < rawCount; vi++)
                {
                    if (!nonIsolated.Contains(vi)) continue;
                    targetMesh.Vertices[vi].Position = blended[vi];
                }

                if (recalcNormals)
                    targetMesh.RecalculateSmoothNormals();

                // UnityMesh 更新
                var ctx = targetEntry.Context;
                if (ctx.UnityMesh != null && ctx.MeshObject != null)
                {
                    var wm = ctx.WorldMatrix;
                    if (ctx.MeshObject.VertexCount == ctx.UnityMesh.vertexCount)
                    {
                        var verts = new Vector3[ctx.MeshObject.VertexCount];
                        for (int vi = 0; vi < verts.Length; vi++)
                            verts[vi] = wm.MultiplyPoint3x4(ctx.MeshObject.Vertices[vi].Position);
                        ctx.UnityMesh.vertices = verts;
                        ctx.UnityMesh.RecalculateBounds();
                    }
                }
                onSyncMesh?.Invoke(ctx);
            }

            // ミラー同期
            var syncedReal = new System.Collections.Generic.HashSet<MeshContext>();
            foreach (var pair in cloneModel.MirrorPairs)
            {
                if (!pair.IsValid) continue;
                pair.SyncPositions();
                if (recalcNormals) pair.SyncNormals();
                onSyncMesh?.Invoke(pair.Real);
                onSyncMesh?.Invoke(pair.Mirror);
                syncedReal.Add(pair.Real);
            }
            foreach (var (_, targetEntry) in targetEntries)
            {
                var realCtx = targetEntry.Context;
                if (syncedReal.Contains(realCtx)) continue;
                string mirrorName = realCtx.Name + "+";
                var axis   = realCtx.GetMirrorSymmetryAxis();
                var realMo = realCtx.MeshObject;
                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.MirrorSide || mc.Name != mirrorName) continue;
                    if (mc.MeshObject == null || mc.MeshObject.VertexCount != realMo.VertexCount) continue;
                    for (int vi = 0; vi < realMo.VertexCount; vi++)
                    {
                        var p = realMo.Vertices[vi].Position;
                        mc.MeshObject.Vertices[vi].Position = axis switch
                        {
                            Poly_Ling.Symmetry.SymmetryAxis.X => new Vector3(-p.x, p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Y => new Vector3(p.x, -p.y, p.z),
                            Poly_Ling.Symmetry.SymmetryAxis.Z => new Vector3(p.x, p.y, -p.z),
                            _ => new Vector3(-p.x, p.y, p.z),
                        };
                    }
                    onSyncMesh?.Invoke(mc);
                    break;
                }
            }

            // ボーンブレンド
            if (blendBones && cloneModel.BoneCount > 0)
            {
                var cloneBoneByName = new System.Collections.Generic.Dictionary<string, MeshContext>();
                for (int i = 0; i < cloneModel.MeshContextCount; i++)
                {
                    var mc = cloneModel.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    if (!string.IsNullOrEmpty(mc.Name)) cloneBoneByName[mc.Name] = mc;
                }

                var srcBoneMaps = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<string, Vector3>>();
                for (int modelIdx = 0; modelIdx < project.ModelCount; modelIdx++)
                {
                    if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                    var srcM = project.GetModel(modelIdx);
                    if (srcM == null || srcM.BoneCount == 0) continue;
                    var bmap = new System.Collections.Generic.Dictionary<string, Vector3>();
                    for (int i = 0; i < srcM.MeshContextCount; i++)
                    {
                        var mc = srcM.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        if (!string.IsNullOrEmpty(mc.Name) && mc.BoneTransform != null)
                            bmap[mc.Name] = mc.BoneTransform.Position;
                    }
                    if (bmap.Count > 0) srcBoneMaps[modelIdx] = bmap;
                }

                foreach (var kv in cloneBoneByName)
                {
                    if (kv.Value.BoneTransform == null) continue;
                    Vector3 blendedPos = Vector3.zero;
                    float totalW = 0f;
                    foreach (var srcKv in srcBoneMaps)
                    {
                        if (!srcKv.Value.TryGetValue(kv.Key, out Vector3 srcPos)) continue;
                        float w = nw[srcKv.Key];
                        blendedPos += srcPos * w;
                        totalW     += w;
                    }
                    if (totalW > 0f)
                        kv.Value.BoneTransform.Position = blendedPos / totalW;
                }
                cloneModel.ComputeWorldAndBindPoses();
            }
        }

        private static HashSet<int> BuildBlendNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            return set;
        }

        internal static ModelContext DeepCloneModelContext(ModelContext src, string newName)
        {
            var dst = new ModelContext { Name = newName };

            for (int i = 0; i < src.MeshContextCount; i++)
            {
                var s = src.GetMeshContext(i);
                if (s == null) continue;
                var meshObj = s.MeshObject?.Clone();
                if (meshObj == null) continue;

                var d = new MeshContext
                {
                    Name                   = s.Name,
                    MeshObject             = meshObj,
                    UnityMesh              = meshObj.ToUnityMesh(),
                    OriginalPositions      = (Vector3[])meshObj.Positions.Clone(),
                    BoneTransform          = CloneBoneTransform(s.BoneTransform),
                    ParentIndex            = s.ParentIndex,
                    Depth                  = s.Depth,
                    HierarchyParentIndex   = s.HierarchyParentIndex,
                    IsVisible              = s.IsVisible,
                    IsLocked               = s.IsLocked,
                    IsFolding              = s.IsFolding,
                    MirrorType             = s.MirrorType,
                    MirrorAxis             = s.MirrorAxis,
                    MirrorDistance         = s.MirrorDistance,
                    MirrorMaterialOffset   = s.MirrorMaterialOffset,
                    BakedMirrorSourceIndex = s.BakedMirrorSourceIndex,
                    HasBakedMirrorChild    = s.HasBakedMirrorChild,
                    MirrorGeometryDerived  = s.MirrorGeometryDerived,
                    MorphParentIndex       = s.MorphParentIndex,
                    BindPose               = s.BindPose,
                    BonePoseData           = s.BonePoseData?.Clone(),
                    MorphBaseData          = s.MorphBaseData?.Clone(),
                };
                dst.Add(d);
            }

            if (src.MaterialReferences != null)
                foreach (var m in src.MaterialReferences)
                    dst.MaterialReferences.Add(m);
            dst.CurrentMaterialIndex = src.CurrentMaterialIndex;

            if (src.DefaultMaterialReferences != null)
                foreach (var m in src.DefaultMaterialReferences)
                    dst.DefaultMaterialReferences.Add(m);
            dst.DefaultCurrentMaterialIndex = src.DefaultCurrentMaterialIndex;
            dst.AutoSetDefaultMaterials     = src.AutoSetDefaultMaterials;

            if (src.MirrorPairs != null)
            {
                foreach (var sp in src.MirrorPairs)
                {
                    int ri = src.IndexOf(sp.Real);
                    int mi = src.IndexOf(sp.Mirror);
                    if (ri < 0 || mi < 0 || ri >= dst.Count || mi >= dst.Count) continue;
                    var pair = new MirrorPair
                    {
                        Real   = dst.GetMeshContext(ri),
                        Mirror = dst.GetMeshContext(mi),
                        Axis   = sp.Axis,
                    };
                    if (pair.Build()) dst.MirrorPairs.Add(pair);
                }
            }
            return dst;
        }

        private static BoneTransform CloneBoneTransform(BoneTransform src)
        {
            if (src == null) return new BoneTransform();
            var dst = new BoneTransform();
            dst.CopyFrom(src);
            return dst;
        }

        private ToolContext BuildMinimalToolCtx(ModelContext model)
        {
            var ctx = new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.SyncMeshContextPositionsOnly = mc =>
            {
                // Phase 2a-2g-1: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。
                // project は Dispatch ローカルでクロージャ不可のため、毎回 _getProject() で取得する。
                var proj = _getProject();
                _viewportManager.EnterVerticesMoved(proj, VerticesMovedPhase.Dragging, mc);
                _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            };
            ctx.NotifyTopologyChanged = () =>
            {
                // Phase 2a-2g-1: RebuildAdapter を EnterTopologyChanged に集約。
                _viewportManager.EnterTopologyChanged(_getProject());
                _notifyPanels(ChangeKind.ListStructure);
            };
            return ctx;
        }

        /// <summary>
        /// SkinWeight 一括操作（Flood/Normalize/Prune）用の ToolContext を構築する。
        /// UndoController・CommandQueue・SyncMesh を設定済み。
        /// </summary>
        private ToolContext BuildSkinWeightToolCtx(ModelContext model)
        {
            var ctx            = BuildMinimalToolCtx(model);
            ctx.CommandQueue   = _commandQueue;
            ctx.SyncMesh       = () =>
            {
                // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                _viewportManager.EnterTopologyChanged(_getProject());
            };
            ctx.Repaint        = () => { };
            return ctx;
        }

        /// <summary>
        /// スキンウェイトの一括操作を、選択中の描画オブジェクト全件へ適用する。
        ///
        /// Flood / Normalize / Prune / 数値設定 / 全頂点正規化 の共通経路。
        /// 対象の列挙は SkinWeightOperations.CollectTargetMeshContexts に一本化してあり、
        /// ウェイト可視化（MeshSceneRenderer.CollectWeightVisTargets）と同じ集合になる。
        ///
        /// Undo はメッシュごとに取る。UndoController は一度に 1 メッシュしか保持できないため、
        /// SetMeshObject → before → 適用 → after → 記録 をメッシュごとに繰り返す
        /// （SetFaceHiddenCommand と同型）。
        ///
        /// 頂点数・面構成は変わらないので、同期は RebuildAdapter を伴う
        /// EnterTopologyChanged ではなくウェイトの部分転送のみを行う
        /// EnterVertexAttributesChanged を通す。
        /// </summary>
        /// <param name="apply">1 メッシュへ適用し、書き換えた頂点数を返す関数</param>
        private void ApplySkinWeightPerMesh(
            ProjectContext project, ModelContext model, string undoLabel,
            System.Func<MeshContext, int> apply)
        {
            if (model == null || apply == null) return;

            var targets = SkinWeightOperations.CollectTargetMeshContexts(model);
            if (targets.Count == 0) return;

            var changedMeshes = new List<MeshContext>();
            var mirrorMeshes  = new List<MeshContext>();

            foreach (var mc in targets)
            {
                if (mc?.MeshObject == null) continue;

                if (_undoController != null)
                {
                    // SetMeshObjectFor を使うこと。SetMeshObject(MeshObject,…) は
                    // 書き込み先が MeshUndoContext.ResolvedMeshContext（既定で先頭の
                    // 選択メッシュ）になるため、このループの2件目以降で先頭メッシュの
                    // MeshObject が今の対象のもので上書きされる。
                    _undoController.MeshUndoContext.ParentModelContext = model;
                    _undoController.SetMeshObjectFor(mc, mc.UnityMesh);
                }
                var before = _undoController?.CaptureMeshObjectSnapshot();

                int changed = apply(mc);
                if (changed <= 0) continue;

                changedMeshes.Add(mc);

                // ミラー側へ写す。ミラー側メッシュはファイル実体を持つ独立メッシュで
                // 自分の BoneWeight を保存するため、実体側を塗っただけでは更新されない。
                // SyncBoneWeights は実体側頂点の MirrorBoneWeight（GPU 描画用）も
                // 張り直すので、実体側の after を取る前に済ませる。後に回すと
                // Redo で MirrorBoneWeight が古い値に戻る。
                SyncSkinWeightToMirrors(model, mc, mirrorMeshes, undoLabel);

                if (_undoController != null && before != null)
                {
                    // ミラー側の記録で対象が移っているので戻す。
                    _undoController.SetMeshObjectFor(mc, mc.UnityMesh);
                    var after = _undoController.CaptureMeshObjectSnapshot();
                    _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _undoController, before, after, undoLabel));
                }
            }
            _undoController?.ClearTargetMeshContext();

            if (changedMeshes.Count == 0) return;

            foreach (var mc in mirrorMeshes)
                changedMeshes.Add(mc);

            foreach (var mc in changedMeshes)
                _viewportManager.EnterVertexAttributesChanged(
                    project, mc, weights: true, uvs: false);

            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>
        /// 書き換えたメッシュ 1 件のウェイトを、ペアの相方へ写す。
        /// 実体側を塗ったらミラー側へ、ミラー側を塗ったら実体側へ写す。
        /// 写した相方を collected へ足し、相方のぶんの Undo も積む。
        /// 呼び出し側は、戻った直後に元のメッシュへ SetMeshObjectFor し直すこと。
        /// </summary>
        private void SyncSkinWeightToMirrors(
            ModelContext model, MeshContext changedCtx,
            List<MeshContext> collected, string undoLabel)
        {
            if (model?.MirrorPairs == null || changedCtx == null || collected == null) return;

            foreach (var pair in model.MirrorPairs)
            {
                if (pair?.Real == null || pair.Mirror == null) continue;
                if (pair.Real.MeshObject == null || pair.Mirror.MeshObject == null) continue;

                // 塗ったのが実体側なら相方はミラー側、塗ったのがミラー側なら相方は実体側。
                // ミラー側も選択して直接塗れるので、両方向を扱う。
                bool fromReal   = ReferenceEquals(pair.Real, changedCtx);
                bool fromMirror = ReferenceEquals(pair.Mirror, changedCtx);
                if (!fromReal && !fromMirror) continue;

                var peer = fromReal ? pair.Mirror : pair.Real;
                if (collected.Contains(peer)) continue;

                MeshObjectSnapshot before = null;
                if (_undoController != null)
                {
                    _undoController.MeshUndoContext.ParentModelContext = model;
                    _undoController.SetMeshObjectFor(peer, peer.UnityMesh);
                    before = _undoController.CaptureMeshObjectSnapshot();
                }

                if (fromReal) pair.SyncBoneWeights();
                else          pair.SyncBoneWeightsFromMirror();

                collected.Add(peer);

                if (_undoController != null && before != null)
                {
                    var after = _undoController.CaptureMeshObjectSnapshot();
                    _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _undoController, before, after, undoLabel + " (mirror)"));
                }
            }
        }
    }
}
