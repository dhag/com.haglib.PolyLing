// PlayerCommandDispatcher.cs
// PanelCommand を受け取り ProjectContext に適用するクラス。
// PolyLingPlayerViewer の DispatchPanelCommand を分離したもの。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerCommandDispatcher
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly Func<ProjectContext>   _getProject;
        private readonly MeshSceneRenderer      _renderer;
        private readonly PlayerViewportManager  _viewportManager;
        private readonly PlayerSelectionOps     _selectionOps;
        private readonly Action<ChangeKind>     _notifyPanels;
        private readonly Action                 _rebuildModelList;
        private readonly MeshUndoController     _undoController;

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerCommandDispatcher(
            Func<ProjectContext>  getProject,
            MeshSceneRenderer     renderer,
            PlayerViewportManager viewportManager,
            PlayerSelectionOps    selectionOps,
            Action<ChangeKind>    notifyPanels,
            Action                rebuildModelList,
            MeshUndoController    undoController = null)
        {
            _getProject       = getProject       ?? throw new ArgumentNullException(nameof(getProject));
            _renderer         = renderer         ?? throw new ArgumentNullException(nameof(renderer));
            _viewportManager  = viewportManager  ?? throw new ArgumentNullException(nameof(viewportManager));
            _selectionOps     = selectionOps;
            _notifyPanels     = notifyPanels     ?? throw new ArgumentNullException(nameof(notifyPanels));
            _rebuildModelList = rebuildModelList ?? throw new ArgumentNullException(nameof(rebuildModelList));
            _undoController   = undoController;
        }

        // ================================================================
        // ディスパッチ
        // ================================================================

        public void Dispatch(PanelCommand cmd)
        {
            var project = _getProject();
            if (project == null) return;
            var model   = project.CurrentModel;

            switch (cmd)
            {
                // ── モデル選択
                case SwitchModelCommand c:
                    project.SelectModel(c.TargetModelIndex);
                    {
                        var switchedModel = project.CurrentModel;
                        if (switchedModel != null)
                        {
                            _renderer?.ClearScene();
                            _viewportManager.RebuildAdapter(0, switchedModel);
                            var firstMc = switchedModel.FirstSelectedDrawableMesh;
                            if (firstMc != null)
                            {
                                _selectionOps?.SetSelectionState(firstMc.Selection);
                                _renderer?.SetSelectionState(firstMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, switchedModel);
                            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                        }
                    }
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;

                // ── モデル名前変更
                case RenameModelCommand c:
                    var renameTarget = project.GetModel(c.ModelIndex);
                    if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                        renameTarget.Name = c.NewName;
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── モデル削除
                case DeleteModelCommand c:
                    project.RemoveModelAt(c.ModelIndex);
                    _rebuildModelList();
                    return;

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) return;
                    switch (sel.Category)
                    {
                        case MeshCategory.Drawable:
                            model.ClearMeshSelection();
                            foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                            if (sel.Indices.Length > 0)
                                model.SelectDrawableMesh(sel.Indices[0]);
                            var selMc = model.FirstSelectedDrawableMesh;
                            if (selMc != null)
                            {
                                _selectionOps?.SetSelectionState(selMc.Selection);
                                _renderer?.SetSelectionState(selMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, model);
                            break;
                        case MeshCategory.Bone:
                            model.ClearBoneSelection();
                            foreach (int idx in sel.Indices) model.AddToBoneSelection(idx);
                            break;
                        case MeshCategory.Morph:
                            model.ClearMorphSelection();
                            foreach (int idx in sel.Indices) model.AddToMorphSelection(idx);
                            break;
                    }
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── 可視性トグル
                case ToggleVisibilityCommand c:
                    if (model == null) return;
                    var visCtx = model.GetMeshContext(c.MasterIndex);
                    if (visCtx != null) visCtx.IsVisible = !visCtx.IsVisible;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── 一括可視性
                case SetBatchVisibilityCommand c:
                    if (model == null) return;
                    foreach (int mi in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(mi);
                        if (ctx != null) ctx.IsVisible = c.Visible;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ロックトグル
                case ToggleLockCommand c:
                    if (model == null) return;
                    var lckCtx = model.GetMeshContext(c.MasterIndex);
                    if (lckCtx != null) lckCtx.IsLocked = !lckCtx.IsLocked;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── IgnorePoseInArmature 設定
                case SetIgnorePoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        ctx.IgnorePoseInArmature = c.Value;
                        if (c.Value && ctx.BoneTransform != null)
                            ctx.BoneTransform.Rotation = Vector3.zero;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ミラータイプ
                case CycleMirrorTypeCommand c:
                    if (model == null) return;
                    var mirCtx = model.GetMeshContext(c.MasterIndex);
                    if (mirCtx != null)
                        mirCtx.MirrorType = (mirCtx.MirrorType + 1) % 4;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── メッシュ名前変更
                case RenameMeshCommand c:
                    if (model == null) return;
                    var renCtx = model.GetMeshContext(c.MasterIndex);
                    if (renCtx != null && !string.IsNullOrEmpty(c.NewName))
                        renCtx.Name = c.NewName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                case DeleteMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                        model.RemoveAt(idx);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── メッシュ複製
                case DuplicateMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var srcCtx = model.GetMeshContext(idx);
                        if (srcCtx == null) continue;
                        var dup = new MeshContext
                        {
                            Name       = srcCtx.Name + "_copy",
                            MeshObject = srcCtx.MeshObject?.Clone(),
                            IsVisible  = srcCtx.IsVisible,
                            IsLocked   = srcCtx.IsLocked,
                            Depth      = srcCtx.Depth,
                        };
                        model.Add(dup);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── BonePose 初期化
                case InitBonePoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        if (ctx.BonePoseData == null)
                        {
                            ctx.BonePoseData          = new BonePoseData();
                            ctx.BonePoseData.IsActive = true;
                        }
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose Active
                case SetBonePoseActiveCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        // BonePoseData未初期化の場合、Active=trueで初期化する
                        if (ctx.BonePoseData == null && c.Active)
                            ctx.BonePoseData = new BonePoseData();
                        if (ctx.BonePoseData != null) ctx.BonePoseData.IsActive = c.Active;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose レイヤーリセット
                case ResetBonePoseLayersCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                        model.GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose → BindPose ベイク
                case BakePoseToBindPoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData == null) continue;
                        ctx.BindPose = ctx.WorldMatrix.inverse;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── モーフ全選択 / 全解除
                case SelectAllMorphsCommand c:
                    if (model == null) return;
                    model.ClearMorphSelection();
                    foreach (int idx in c.AllMorphIndices) model.AddToMorphSelection(idx);
                    _notifyPanels(ChangeKind.Selection);
                    return;

                case DeselectAllMorphsCommand _:
                    model?.ClearMorphSelection();
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── モーフ変換・プレビュー・セット作成（PolyLingCore が必要、Player では未実装）
                case ConvertMeshToMorphCommand _:
                case ConvertMorphToMeshCommand _:
                case CreateMorphSetCommand _:
                case StartMorphPreviewCommand _:
                case ApplyMorphPreviewCommand _:
                case EndMorphPreviewCommand _:
                    Debug.LogWarning($"[PlayerCommandDispatcher] {cmd.GetType().Name} requires PolyLingCore (not implemented in Player).");
                    return;

                // ── BoneTransform 値設定
                case SetBoneTransformValueCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BoneTransform == null) continue;
                        ctx.BoneTransform.UseLocalTransform = true;
                        switch (c.TargetField)
                        {
                            case SetBoneTransformValueCommand.Field.PositionX: ctx.BoneTransform.Position = new Vector3(c.Value, ctx.BoneTransform.Position.y, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionY: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, c.Value, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionZ: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, ctx.BoneTransform.Position.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.RotationX: ctx.BoneTransform.Rotation = new Vector3(c.Value, ctx.BoneTransform.Rotation.y, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationY: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, c.Value, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationZ: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, ctx.BoneTransform.Rotation.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.ScaleX:    ctx.BoneTransform.Scale    = new Vector3(c.Value, ctx.BoneTransform.Scale.y, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleY:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, c.Value, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleZ:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, ctx.BoneTransform.Scale.y, c.Value); break;
                        }
                    }
                    model.ComputeWorldMatrices();
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── UV展開
                case ApplyUvUnwrapCommand c:
                {
                    if (model == null) return;
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleApplyUvUnwrap(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── UV→XYZ展開メッシュ生成
                case UvToXyzCommand c:
                {
                    if (model == null) return;
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleUvToXyz(
                        model, _undoController, BuildMinimalToolCtx(model),
                        mc =>
                        {
                            mc.UnityMesh = mc.MeshObject?.ToUnityMesh();
                            model.Add(mc);
                        },
                        () => { }, c);
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── XYZ→UV書き戻し
                case XyzToUvCommand c:
                {
                    if (model == null) return;
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleXyzToUv(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── BoneTransform スライダー開始／終了（通知不要）
                case BeginBoneTransformSliderDragCommand _:
                case EndBoneTransformSliderDragCommand _:
                    return;

                // ── モデルブレンド: クローン作成
                case CreateBlendCloneCommand c:
                {
                    var src = project.GetModel(c.ModelIndex);
                    if (src == null) return;
                    string uniqueName = project.GenerateUniqueModelName(
                        string.IsNullOrEmpty(c.CloneNameBase) ? src.Name + "_blend" : c.CloneNameBase);
                    var clone = DeepCloneModelContext(src, uniqueName);
                    if (clone == null) return;
                    project.AddModel(clone);
                    // スキニング再計算（BoneTransform → WorldMatrix → BindPose）
                    clone.ComputeWorldAndBindPoses();
                    clone.ComputeMeshFilterBindPoses();
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── モデルブレンド: プレビュー（Undo なし）
                case PreviewModelBlendCommand c:
                {
                    var cloneModelPrev = project.GetModel(c.CloneModelIndex);
                    if (cloneModelPrev == null) return;
                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, recalcNormals: false, blendBones: c.BlendBones,
                        onSyncMesh: null);
                    _viewportManager.RebuildAdapter(0, cloneModelPrev);
                    var firstMcPrev = cloneModelPrev.FirstSelectedDrawableMesh;
                    if (firstMcPrev != null)
                    {
                        _selectionOps?.SetSelectionState(firstMcPrev.Selection);
                        _renderer?.SetSelectionState(firstMcPrev.Selection);
                    }
                    _renderer?.UpdateSelectedDrawableMesh(0, cloneModelPrev);
                    _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                    return;
                }

                // ── モデルブレンド: 適用
                case ApplyModelBlendCommand c:
                {
                    var cloneModelApply = project.GetModel(c.CloneModelIndex);
                    if (cloneModelApply == null) return;
                    ExecuteBlend(project, c.ModelIndex, c.CloneModelIndex,
                        c.Weights, c.MeshEnabled, c.RecalcNormals, c.BlendBones,
                        onSyncMesh: null);
                    _viewportManager.RebuildAdapter(0, cloneModelApply);
                    var firstMcApply = cloneModelApply.FirstSelectedDrawableMesh;
                    if (firstMcApply != null)
                    {
                        _selectionOps?.SetSelectionState(firstMcApply.Selection);
                        _renderer?.SetSelectionState(firstMcApply.Selection);
                    }
                    _renderer?.UpdateSelectedDrawableMesh(0, cloneModelApply);
                    _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── その他（モーフ変換・プレビュー等）は Player では未実装
                default:
                    Debug.LogWarning($"[PlayerCommandDispatcher] Unhandled PanelCommand: {cmd.GetType().Name}");
                    return;
            }
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
                bool targetIsExp = targetMesh.IsExpanded;

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
                    bool srcIsExp = srcMesh.IsExpanded;

                    if (targetIsExp)
                    {
                        var srcInvMap = srcIsExp ? null : srcMesh.BuildInverseExpansionMap();
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsExp)
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
                        var srcExpMap = srcIsExp ? targetMesh.BuildExpansionMap() : null;
                        for (int vi = 0; vi < rawCount; vi++)
                        {
                            if (!nonIsolated.Contains(vi)) continue;
                            Vector3 srcPos;
                            if (srcIsExp)
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

        private Poly_Ling.Tools.ToolContext BuildMinimalToolCtx(ModelContext model)
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.SyncMeshContextPositionsOnly = mc =>
            {
                _viewportManager.SyncMeshPositionsAndTransform(mc, model);
                _viewportManager.UpdateTransform();
                _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
            };
            ctx.NotifyTopologyChanged = () =>
            {
                _viewportManager.RebuildAdapter(0, model);
                _notifyPanels(ChangeKind.ListStructure);
            };
            return ctx;
        }
    }
}
