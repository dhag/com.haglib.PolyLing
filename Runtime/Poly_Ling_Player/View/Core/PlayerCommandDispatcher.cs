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
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.UI;

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
        private readonly CommandQueue           _commandQueue;

        // BoneTransformスライダーのUndo用スナップショット（Begin～End間で保持）
        private readonly Dictionary<int, BoneTransformSnapshot> _boneTransformBeforeSnapshots
            = new Dictionary<int, BoneTransformSnapshot>();

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
            MeshUndoController    undoController = null,
            CommandQueue          commandQueue   = null)
        {
            _getProject       = getProject       ?? throw new ArgumentNullException(nameof(getProject));
            _renderer         = renderer         ?? throw new ArgumentNullException(nameof(renderer));
            _viewportManager  = viewportManager  ?? throw new ArgumentNullException(nameof(viewportManager));
            _selectionOps     = selectionOps;
            _notifyPanels     = notifyPanels     ?? throw new ArgumentNullException(nameof(notifyPanels));
            _rebuildModelList = rebuildModelList ?? throw new ArgumentNullException(nameof(rebuildModelList));
            _undoController   = undoController;
            _commandQueue     = commandQueue;
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
                            var firstMc = switchedModel.FirstDrawableMeshContext;
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

                // ── メッシュ追加（空メッシュ）
                case AddMeshCommand _:
                {
                    if (model == null) return;
                    var addBefore = MeshFilterToSkinnedRecord.CaptureList(model);
                    var newMc = new MeshContext
                    {
                        MeshObject        = new MeshObject("New Mesh"),
                        UnityMesh         = new Mesh(),
                        OriginalPositions = new Vector3[0],
                    };
                    newMc.ParentModelContext = model;
                    model.Add(newMc);
                    model.OnListChanged?.Invoke();
                    if (_undoController != null)
                    {
                        var addAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var addRecord = new MeshFilterToSkinnedRecord { BeforeList = addBefore, AfterList = addAfter };
                        _undoController.MeshListStack.Record(addRecord, "Add Mesh");
                        _undoController.FocusMeshList();
                    }
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) return;
                    switch (sel.Category)
                    {
                        case MeshCategory.Drawable:
                            model.ClearMeshSelection();
                            foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                            if (sel.Indices.Length > 0)
                                model.SelectMesh(sel.Indices[0]);
                            var selMc = model.FirstDrawableMeshContext;
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

                // ── 頂点・辺・面の選択
                case SelectElementsCommand c:
                {
                    if (model == null) return;
                    var targetMc = model.GetMeshContext(c.MasterIndex);
                    if (targetMc?.Selection == null) return;
                    var sel2 = targetMc.Selection;
                    if (!c.Additive)
                        sel2.ClearAll();
                    if (c.VertexIndices != null)
                        foreach (int vi in c.VertexIndices)
                            sel2.SelectVertex(vi, additive: true);
                    if (c.EdgePairs != null)
                        for (int i = 0; i + 1 < c.EdgePairs.Length; i += 2)
                            sel2.SelectEdge(c.EdgePairs[i], c.EdgePairs[i + 1], additive: true);
                    if (c.FaceIndices != null)
                        foreach (int fi in c.FaceIndices)
                            sel2.SelectFace(fi, additive: true);
                    _selectionOps?.SetSelectionState(sel2);
                    _renderer?.SetSelectionState(sel2);
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

                // ── 選択頂点の移動
                case MoveSelectedVerticesCommand c:
                {
                    if (model == null) return;
                    var moveMc = model.GetMeshContext(c.MasterIndex);
                    if (moveMc?.MeshObject == null || moveMc.Selection == null) return;

                    // Delta をローカル空間に変換
                    var localDelta = c.Space == MoveSelectedVerticesCommand.CoordSpace.World
                        ? moveMc.WorldMatrixInverse.MultiplyVector(c.Delta)
                        : c.Delta;

                    var mo              = moveMc.MeshObject;
                    var selectedVerts   = new List<int>(moveMc.Selection.Vertices);
                    if (selectedVerts.Count == 0) return;

                    // 移動前位置を記録
                    var oldPositions = new Vector3[selectedVerts.Count];
                    var newPositions = new Vector3[selectedVerts.Count];
                    for (int i = 0; i < selectedVerts.Count; i++)
                    {
                        int vi = selectedVerts[i];
                        oldPositions[i] = mo.Vertices[vi].Position;
                        newPositions[i] = mo.Vertices[vi].Position + localDelta;
                        mo.Vertices[vi].Position = newPositions[i];
                    }
                    mo.InvalidatePositionCache();

                    if (c.RecalcNormals)
                        mo.RecalculateSmoothNormals();

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var entry = new MeshMoveEntry
                        {
                            MeshContextIndex = c.MasterIndex,
                            Indices          = selectedVerts.ToArray(),
                            OldPositions     = oldPositions,
                            NewPositions     = newPositions,
                        };
                        var record = new MultiMeshVertexMoveRecord(new[] { entry });
                        _undoController.FocusVertexEdit();
                        _undoController.VertexEditStack.Record(record, $"Move {selectedVerts.Count} Vertices");
                    }

                    // GPU 反映
                    _viewportManager.SyncMeshPositionsAndTransform(moveMc, model);
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── ピボット移動
                case MovePivotCommand c:
                {
                    if (model == null) return;
                    var pivotMc = model.GetMeshContext(c.MasterIndex);
                    if (pivotMc?.MeshObject == null) return;

                    var mo = pivotMc.MeshObject;

                    // worldDelta / localDelta を確定
                    Vector3 worldDelta, localDelta;
                    if (c.Space == MoveSelectedVerticesCommand.CoordSpace.World)
                    {
                        worldDelta = c.Delta;
                        localDelta = pivotMc.WorldMatrixInverse.MultiplyVector(c.Delta);
                    }
                    else
                    {
                        localDelta = c.Delta;
                        worldDelta = pivotMc.WorldMatrix.MultiplyVector(c.Delta);
                    }

                    // 孤立頂点を除いた全頂点インデックスを収集
                    var nonIsolated = BuildBlendNonIsolatedSet(mo);
                    var indices     = new List<int>(nonIsolated);

                    // 移動前後の位置を記録しながら頂点に -localDelta を適用
                    var oldPositions = new Vector3[indices.Count];
                    var newPositions = new Vector3[indices.Count];
                    for (int i = 0; i < indices.Count; i++)
                    {
                        int vi = indices[i];
                        oldPositions[i]          = mo.Vertices[vi].Position;
                        newPositions[i]          = mo.Vertices[vi].Position - localDelta;
                        mo.Vertices[vi].Position = newPositions[i];
                    }
                    mo.InvalidatePositionCache();
                    if (pivotMc.OriginalPositions != null && pivotMc.OriginalPositions.Length == mo.VertexCount)
                        for (int i = 0; i < indices.Count; i++)
                            pivotMc.OriginalPositions[indices[i]] = newPositions[i];

                    // BoneTransform.Position を +worldDelta
                    BoneTransformSnapshot oldBoneSnap = default, newBoneSnap = default;
                    if (pivotMc.BoneTransform != null)
                    {
                        oldBoneSnap = pivotMc.BoneTransform.CreateSnapshot();
                        pivotMc.BoneTransform.UseLocalTransform = true;
                        pivotMc.BoneTransform.Position         += worldDelta;
                        newBoneSnap = pivotMc.BoneTransform.CreateSnapshot();
                    }

                    // Undo 記録（PivotMoveRecord を MeshListStack へ）
                    if (_undoController != null)
                    {
                        var record = new PivotMoveRecord
                        {
                            MasterIndex       = c.MasterIndex,
                            VertexIndices     = indices.ToArray(),
                            OldVertexPositions = oldPositions,
                            NewVertexPositions = newPositions,
                            OldBoneTransform  = oldBoneSnap,
                            NewBoneTransform  = newBoneSnap,
                        };
                        _undoController.MeshListStack.Record(record, "Pivot Move");
                        _undoController.FocusMeshList();
                    }

                    // GPU 反映
                    model.ComputeWorldMatrices();
                    _viewportManager.SyncMeshPositionsAndTransform(pivotMc, model);
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スカルプトストローク
                case SculptStrokeCommand c:
                {
                    if (model == null) return;
                    var sculptMc = model.GetMeshContext(c.MasterIndex);
                    if (sculptMc?.MeshObject == null) return;

                    var mo = sculptMc.MeshObject;

                    // 開始時の全頂点位置を保存
                    var beforePositions = new Vector3[mo.VertexCount];
                    for (int i = 0; i < mo.VertexCount; i++)
                        beforePositions[i] = mo.Vertices[i].Position;

                    // キャッシュ構築（ストローク開始時に1回）
                    var adjacency    = SculptBuildAdjacency(mo);
                    var vertNormals  = SculptBuildVertexNormals(mo);

                    // 各ブラシ中心でブラシを適用
                    foreach (var center in c.BrushCenters)
                    {
                        var affected = SculptGetAffected(mo, center, c.BrushRadius, c.Falloff);
                        if (affected.Count == 0) continue;

                        switch (c.Mode)
                        {
                            case SculptMode.Draw:
                                SculptApplyDraw(mo, affected, c.Strength, c.Invert, vertNormals);
                                break;
                            case SculptMode.Smooth:
                                SculptApplySmooth(mo, affected, c.Strength, adjacency);
                                break;
                            case SculptMode.Inflate:
                                SculptApplyInflate(mo, affected, c.Strength, c.Invert, vertNormals);
                                break;
                            case SculptMode.Flatten:
                                SculptApplyFlatten(mo, affected, c.Strength, vertNormals);
                                break;
                        }
                    }

                    mo.InvalidatePositionCache();

                    if (c.RecalcNormals)
                        mo.RecalculateSmoothNormals();

                    // 移動した頂点のみUndo記録に含める
                    if (_undoController != null)
                    {
                        var movedIdx  = new List<int>();
                        var oldPos    = new List<Vector3>();
                        var newPos    = new List<Vector3>();
                        for (int i = 0; i < mo.VertexCount; i++)
                        {
                            var cur = mo.Vertices[i].Position;
                            if (cur != beforePositions[i])
                            {
                                movedIdx.Add(i);
                                oldPos.Add(beforePositions[i]);
                                newPos.Add(cur);
                            }
                        }
                        if (movedIdx.Count > 0)
                        {
                            var entry = new MeshMoveEntry
                            {
                                MeshContextIndex = c.MasterIndex,
                                Indices          = movedIdx.ToArray(),
                                OldPositions     = oldPos.ToArray(),
                                NewPositions     = newPos.ToArray(),
                            };
                            var record = new MultiMeshVertexMoveRecord(new[] { entry });
                            _undoController.FocusVertexEdit();
                            _undoController.VertexEditStack.Record(record,
                                $"Sculpt ({c.Mode}) {movedIdx.Count} Vertices");
                        }
                    }

                    // GPU 反映
                    _viewportManager.SyncMeshPositionsAndTransform(sculptMc, model);
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 詳細選択
                case AdvancedSelectCommand c:
                {
                    if (model == null) return;
                    var advMc = model.GetMeshContext(c.MasterIndex);
                    if (advMc?.MeshObject == null || advMc.Selection == null) return;
                    var mo  = advMc.MeshObject;
                    var sel = advMc.Selection;

                    if (!c.Additive) sel.ClearAll();

                    switch (c.Mode)
                    {
                        case AdvancedSelectMode.Connected:
                        {
                            if (c.SeedVertexIndex >= 0)
                            {
                                var verts = AdvConnectedFromVertex(mo, c.SeedVertexIndex);
                                if (c.SelectVertices) foreach (int v in verts) sel.SelectVertex(v, additive: true);
                                if (c.SelectEdges)    foreach (var e in AdvEdgesFromVertices(mo, verts)) sel.SelectEdge(e, additive: true);
                                if (c.SelectFaces)    foreach (int f in AdvFacesFromVertices(mo, verts)) sel.SelectFace(f, additive: true);
                            }
                            else if (c.SeedEdgeV1 >= 0 && c.SeedEdgeV2 >= 0)
                            {
                                var edges = AdvConnectedFromEdge(mo, new VertexPair(c.SeedEdgeV1, c.SeedEdgeV2));
                                var verts = new HashSet<int>();
                                foreach (var e in edges) { verts.Add(e.V1); verts.Add(e.V2); }
                                if (c.SelectVertices) foreach (int v in verts) sel.SelectVertex(v, additive: true);
                                if (c.SelectEdges)    foreach (var e in edges) sel.SelectEdge(e, additive: true);
                                if (c.SelectFaces)    foreach (int f in AdvFacesFromVertices(mo, verts)) sel.SelectFace(f, additive: true);
                            }
                            else if (c.SeedFaceIndex >= 0)
                            {
                                var faces = AdvConnectedFromFace(mo, c.SeedFaceIndex);
                                var verts = new HashSet<int>();
                                foreach (int f in faces) foreach (int v in mo.Faces[f].VertexIndices) verts.Add(v);
                                if (c.SelectVertices) foreach (int v in verts) sel.SelectVertex(v, additive: true);
                                if (c.SelectEdges)    foreach (var e in AdvEdgesFromFaces(mo, faces)) sel.SelectEdge(e, additive: true);
                                if (c.SelectFaces)    foreach (int f in faces) sel.SelectFace(f, additive: true);
                            }
                            break;
                        }
                        case AdvancedSelectMode.Belt:
                        {
                            if (c.SeedEdgeV1 < 0 || c.SeedEdgeV2 < 0) break;
                            var (bVerts, bEdges, bFaces) = AdvBelt(mo, new VertexPair(c.SeedEdgeV1, c.SeedEdgeV2));
                            if (c.SelectVertices) foreach (int v in bVerts)  sel.SelectVertex(v, additive: true);
                            if (c.SelectEdges)    foreach (var e in bEdges)  sel.SelectEdge(e, additive: true);
                            if (c.SelectFaces)    foreach (int f in bFaces)  sel.SelectFace(f, additive: true);
                            break;
                        }
                        case AdvancedSelectMode.EdgeLoop:
                        {
                            if (c.SeedEdgeV1 < 0 || c.SeedEdgeV2 < 0) break;
                            var loopEdges = AdvEdgeLoop(mo, new VertexPair(c.SeedEdgeV1, c.SeedEdgeV2), c.EdgeLoopThreshold);
                            var loopVerts = new HashSet<int>();
                            foreach (var e in loopEdges) { loopVerts.Add(e.V1); loopVerts.Add(e.V2); }
                            if (c.SelectVertices) foreach (int v in loopVerts)  sel.SelectVertex(v, additive: true);
                            if (c.SelectEdges)    foreach (var e in loopEdges)  sel.SelectEdge(e, additive: true);
                            if (c.SelectFaces)    foreach (int f in AdvFacesFromEdges(mo, loopEdges)) sel.SelectFace(f, additive: true);
                            break;
                        }
                        case AdvancedSelectMode.ShortestPath:
                        {
                            if (c.SeedVertexIndex < 0 || c.EndVertexIndex < 0) break;
                            var path = AdvShortestPath(mo, c.SeedVertexIndex, c.EndVertexIndex);
                            if (c.SelectVertices) foreach (int v in path)            sel.SelectVertex(v, additive: true);
                            if (c.SelectEdges)    foreach (var e in AdvEdgesFromPath(path)) sel.SelectEdge(e, additive: true);
                            if (c.SelectFaces)    foreach (int f in AdvFacesFromEdges(mo, AdvEdgesFromPath(path))) sel.SelectFace(f, additive: true);
                            break;
                        }
                    }

                    _selectionOps?.SetSelectionState(sel);
                    _renderer?.SetSelectionState(sel);
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

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
                    // 先頭ターゲットを UndoController に設定（CaptureMeshObjectSnapshot に必要）
                    if (c.MasterIndices.Length > 0)
                    {
                        var uvMc = model.GetMeshContext(c.MasterIndices[0]);
                        if (uvMc?.MeshObject != null && _undoController != null)
                        {
                            _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleApplyUvUnwrap(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── マテリアルスロット追加
                case AddMaterialSlotCommand _:
                {
                    if (model == null) return;
                    var addMc = model.FirstDrawableMeshContext;
                    if (addMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(addMc.MeshObject, addMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var addBefore = _undoController?.CaptureMeshObjectSnapshot();
                    model.AddMaterial(null);
                    model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && addBefore != null)
                    {
                        var addAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(addBefore, addAfter, "Add Material Slot");
                    }
                    if (model.AutoSetDefaultMaterials)
                    {
                        model.DefaultMaterials            = new System.Collections.Generic.List<Material>(model.Materials);
                        model.DefaultCurrentMaterialIndex = model.CurrentMaterialIndex;
                    }
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── マテリアルスロット削除
                case RemoveMaterialSlotCommand c:
                {
                    if (model == null || model.MaterialCount <= 1) return;
                    var remMc = model.FirstDrawableMeshContext;
                    if (remMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(remMc.MeshObject, remMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var remBefore = _undoController?.CaptureMeshObjectSnapshot();
                    if (remMc?.MeshObject != null)
                        foreach (var face in remMc.MeshObject.Faces)
                        {
                            if (face.MaterialIndex == c.SlotIndex)       face.MaterialIndex = 0;
                            else if (face.MaterialIndex > c.SlotIndex)   face.MaterialIndex--;
                        }
                    model.RemoveMaterialAt(c.SlotIndex);
                    if (model.CurrentMaterialIndex >= model.MaterialCount)
                        model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && remBefore != null)
                    {
                        var remAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(remBefore, remAfter, $"Remove Material Slot [{c.SlotIndex}]");
                    }
                    if (remMc?.UnityMesh != null && remMc.MeshObject != null)
                        remMc.UnityMesh = remMc.MeshObject.ToUnityMesh();
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── 選択面にマテリアル適用
                case ApplyMaterialToFacesCommand c:
                {
                    if (model == null) return;
                    var matMc = model.GetMeshContext(c.MasterIndex);
                    if (matMc?.MeshObject == null) return;
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(matMc.MeshObject, matMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var matBefore = _undoController?.CaptureMeshObjectSnapshot();
                    foreach (int fi in c.FaceIndices)
                        if (fi >= 0 && fi < matMc.MeshObject.FaceCount)
                            matMc.MeshObject.Faces[fi].MaterialIndex = c.MaterialSlot;
                    if (_undoController != null && matBefore != null)
                    {
                        var matAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(matBefore, matAfter, $"Apply Material [{c.MaterialSlot}]");
                    }
                    _viewportManager.SyncMeshPositionsAndTransform(matMc, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── LSCM UV 展開
                case ApplyLscmUnwrapCommand c:
                {
                    if (model == null) return;
                    var lscmMc = model.GetMeshContext(c.MasterIndex);
                    if (lscmMc?.MeshObject == null) return;

                    // UndoController に対象メッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(lscmMc.MeshObject, lscmMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    var before = _undoController?.CaptureMeshObjectSnapshot();

                    // Seam エッジは実行時点の SelectedEdges から取得
                    var seamEdges = lscmMc.SelectedEdges
                        ?? new HashSet<VertexPair>();
                    var result = Poly_Ling.UI.Lscm.LscmUnwrapOperation.Execute(
                        lscmMc.MeshObject, seamEdges,
                        c.IncludeBoundaryAsSeam,
                        Mathf.Clamp(c.MaxIterations, 100, 50000));

                    if (result.Success)
                    {
                        if (_undoController != null && before != null)
                        {
                            var after = _undoController.CaptureMeshObjectSnapshot();
                            _undoController.RecordTopologyChange(before, after, "LSCM UV展開");
                        }
                        lscmMc.UnityMesh = lscmMc.MeshObject.ToUnityMesh();
                        _viewportManager.RebuildAdapter(0, model);
                        _renderer?.UpdateSelectedDrawableMesh(0, model);
                        _notifyPanels(ChangeKind.Attributes);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[LSCM] {result.StatusMessage}");
                    }
                    return;
                }

                // ── UV→XYZ展開メッシュ生成
                case UvToXyzCommand c:
                {
                    if (model == null) return;

                    // 追加前のリストをスナップショット（MeshListStack Undo 用）
                    var uvzBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleUvToXyz(
                        model, _undoController, BuildMinimalToolCtx(model),
                        mc =>
                        {
                            mc.UnityMesh = mc.MeshObject?.ToUnityMesh();
                            model.Add(mc);
                        },
                        () => { }, c);

                    // 追加後のリストをスナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var uvzAfter = MeshFilterToSkinnedRecord.CaptureList(model);
                        var uvzRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = uvzBefore,
                            AfterList  = uvzAfter,
                        };
                        _undoController.MeshListStack.Record(uvzRecord, "UV→XYZ メッシュ生成");
                        _undoController.FocusMeshList();
                    }

                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── XYZ→UV書き戻し
                case XyzToUvCommand c:
                {
                    if (model == null) return;
                    // ターゲットメッシュに SetMeshObject（RecordTopologyChange に必要）
                    var xyzTargetMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (xyzTargetMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(xyzTargetMc.MeshObject, xyzTargetMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleXyzToUv(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── BoneTransform スライダー開始：スナップショット保存
                case BeginBoneTransformSliderDragCommand c:
                {
                    if (model == null) return;
                    _boneTransformBeforeSnapshots.Clear();
                    foreach (int idx in c.MasterIndices)
                    {
                        var mc = model.GetMeshContext(idx);
                        if (mc?.BoneTransform != null)
                            _boneTransformBeforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
                    }
                    return;
                }

                // ── BoneTransform スライダー終了：Undo記録
                case EndBoneTransformSliderDragCommand c:
                {
                    if (model == null || _undoController == null) return;
                    if (_boneTransformBeforeSnapshots.Count == 0) return;
                    var record = new MultiBoneTransformChangeRecord();
                    foreach (var kv in _boneTransformBeforeSnapshots)
                    {
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc?.BoneTransform == null) continue;
                        var after = mc.BoneTransform.CreateSnapshot();
                        if (after.IsDifferentFrom(kv.Value))
                            record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                            {
                                MasterIndex  = kv.Key,
                                OldSnapshot  = kv.Value,
                                NewSnapshot  = after,
                            });
                    }
                    if (record.Entries.Count > 0)
                    {
                        _undoController.MeshListStack.Record(record, c.Description ?? "BoneTransform変更");
                        _undoController.FocusMeshList();
                    }
                    _boneTransformBeforeSnapshots.Clear();
                    return;
                }

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
                    var firstMcPrev = cloneModelPrev.FirstDrawableMeshContext;
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
                        _undoController.MeshListStack.Record(record, "モデルブレンド適用");
                        _undoController.FocusMeshList();
                    }

                    _viewportManager.RebuildAdapter(0, cloneModelApply);
                    var firstMcApply = cloneModelApply.FirstDrawableMeshContext;
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

                // ── メッシュブレンド適用
                case ApplyBlendCommand c:
                {
                    if (model == null) return;
                    var srcCtx = model.GetMeshContext(c.SourceMasterIndex);
                    if (srcCtx?.MeshObject == null) return;

                    // ターゲットインデックスをモデルのSelectedDrawableMeshIndicesに一時設定
                    var savedSelected = new System.Collections.Generic.List<int>(
                        model.SelectedDrawableMeshIndices);
                    model.SelectedDrawableMeshIndices.Clear();
                    foreach (int idx in c.TargetMasterIndices)
                        model.SelectedDrawableMeshIndices.Add(idx);

                    // UndoController に先頭ターゲットを設定（CaptureMeshObjectSnapshot が参照するため先に呼ぶ）
                    var firstTarget = c.TargetMasterIndices.Length > 0
                        ? model.GetMeshContext(c.TargetMasterIndices[0]) : null;
                    if (firstTarget?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(firstTarget.MeshObject, firstTarget.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    // ToolContext 構築（UndoController・CommandQueue 接続済み）
                    var blendCtx = BuildSkinWeightToolCtx(model);

                    // プレビュー → 確定
                    var preview = new BlendPreviewState();
                    preview.Start(model, model.SelectedDrawableMeshIndices, c.SourceMasterIndex);
                    preview.Apply(model, c.SourceMasterIndex, c.BlendWeight,
                        c.SelectedVerticesOnly, null, c.MatchByVertexId, blendCtx);

                    BlendOperation.ApplyAndCreateBackups(
                        model, preview, model.SelectedDrawableMeshIndices, c.SourceMasterIndex,
                        c.BlendWeight, c.RecalculateNormals,
                        c.SelectedVerticesOnly, null, c.MatchByVertexId, blendCtx);

                    // 選択状態を復元
                    model.SelectedDrawableMeshIndices.Clear();
                    foreach (int idx in savedSelected)
                        model.SelectedDrawableMeshIndices.Add(idx);

                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── UV 変更（移動・一括変換）
                case ApplyUVChangesCommand c:
                {
                    if (model == null) return;
                    var uvMc = model.GetMeshContext(c.MasterIndex);
                    if (uvMc?.MeshObject == null) return;

                    // UndoController にターゲットメッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    // before スナップショット（AfterUVs を MeshObject に書き込む前に取得）
                    var before = _undoController?.CaptureMeshObjectSnapshot();

                    // AfterUVs を MeshObject に適用
                    var mo = uvMc.MeshObject;
                    for (int i = 0; i < c.VertexIndices.Length; i++)
                    {
                        int vi = c.VertexIndices[i];
                        int ui = c.UVIndices[i];
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vx = mo.Vertices[vi];
                        if (ui >= 0 && ui < vx.UVs.Count)
                            vx.UVs[ui] = c.AfterUVs[i];
                    }

                    // after スナップショット → VertexEditStack に記録
                    if (_undoController != null && before != null)
                    {
                        var after = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(
                            new RecordTopologyChangeCommand(
                                _undoController, before, after, c.OperationName));
                    }

                    // UnityMesh + GPU 更新
                    _viewportManager.SyncMeshPositionsAndTransform(uvMc, model);
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スキンウェイト Flood
                case FloodSkinWeightCommand c:
                {
                    if (model == null) return;
                    var swCtx = BuildSkinWeightToolCtx(model);
                    SkinWeightOperations.ExecuteFlood(
                        model, swCtx,
                        c.TargetBoneMaster, c.PaintMode,
                        c.WeightValue, c.Strength,
                        err => Debug.LogWarning($"[Flood] {err}"));
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スキンウェイト Normalize
                case NormalizeSkinWeightCommand _:
                {
                    if (model == null) return;
                    var swCtx = BuildSkinWeightToolCtx(model);
                    SkinWeightOperations.ExecuteNormalize(model, swCtx,
                        err => Debug.LogWarning($"[Normalize] {err}"));
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── スキンウェイト Prune
                case PruneSkinWeightCommand c:
                {
                    if (model == null) return;
                    var swCtx = BuildSkinWeightToolCtx(model);
                    SkinWeightOperations.ExecutePrune(model, swCtx, c.Threshold,
                        err => Debug.LogWarning($"[Prune] {err}"));
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── MeshFilter → Skinned 変換
                case ConvertMeshFilterToSkinnedCommand c:
                {
                    if (model == null) return;

                    var entries = MeshFilterToSkinnedConverter.CollectMeshEntries(model);
                    if (entries.Count == 0) return;

                    // 変換前スナップショット
                    var beforeList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // 変換実行
                    MeshFilterToSkinnedConverter.Execute(
                        model, entries, c.SwapAxisForRotated, c.SetAxisForIdentity);

                    // 変換後スナップショット
                    var afterList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var record = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = beforeList,
                            AfterList  = afterList,
                        };
                        _undoController.MeshListStack.Record(record, "MeshFilter → Skinned 変換");
                        _undoController.FocusMeshList();
                    }

                    // GPU 再構築
                    _renderer?.ClearScene();
                    _viewportManager.RebuildAdapter(0, model);
                    var firstMc = model.FirstDrawableMeshContext;
                    if (firstMc != null)
                    {
                        _selectionOps?.SetSelectionState(firstMc.Selection);
                        _renderer?.SetSelectionState(firstMc.Selection);
                    }
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;
                }

                // ── MediaPipe フェイス変形
                case MediaPipeFaceDeformCommand c:
                {
                    if (model == null) return;
                    var mpSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    var srcMesh = mpSrcMc?.MeshObject;
                    if (srcMesh == null) return;

                    try
                    {
                        var mpBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                        var beforeLM  = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(c.BeforePath);
                        var afterLM   = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(c.AfterPath);
                        var triangles = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.ParseTrianglesJson(
                            System.IO.File.ReadAllText(c.TrianglesPath));

                        int vertexCount = srcMesh.VertexCount;
                        var positions   = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++) positions[i] = srcMesh.Vertices[i].Position;

                        var deformer = new Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer();
                        deformer.SetBaseMesh(beforeLM, triangles);
                        deformer.Bind(positions);
                        deformer.Apply(afterLM, positions);

                        var cloned = srcMesh.Clone();
                        cloned.Name = srcMesh.Name + "_MP";
                        for (int i = 0; i < vertexCount; i++) cloned.Vertices[i].Position = positions[i];

                        var mpNewMc = new MeshContext
                        {
                            MeshObject = cloned,
                            Materials  = new System.Collections.Generic.List<Material>(
                                mpSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        };
                        mpNewMc.UnityMesh           = cloned.ToUnityMesh();
                        mpNewMc.UnityMesh.name      = cloned.Name;
                        mpNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                        mpNewMc.ParentModelContext  = model;
                        model.Add(mpNewMc);
                        model.OnListChanged?.Invoke();

                        if (_undoController != null)
                        {
                            var mpAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                            var mpRecord = new MeshFilterToSkinnedRecord { BeforeList = mpBefore, AfterList = mpAfter };
                            _undoController.MeshListStack.Record(mpRecord, "MediaPipe変形");
                            _undoController.FocusMeshList();
                        }
                        _viewportManager.RebuildAdapter(0, model);
                        _renderer?.UpdateSelectedDrawableMesh(0, model);
                        _notifyPanels(ChangeKind.ListStructure);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"[MediaPipeFaceDeformCommand] {ex.Message}");
                    }
                    return;
                }

                // ── Quad減面
                case QuadDecimateCommand c:
                {
                    if (model == null) return;
                    var qdSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (qdSrcMc?.MeshObject == null) return;

                    var qdBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var prms = new Poly_Ling.UI.QuadDecimator.DecimatorParams
                    {
                        TargetRatio     = c.TargetRatio,
                        MaxPasses       = c.MaxPasses,
                        NormalAngleDeg  = c.NormalAngleDeg,
                        HardAngleDeg    = c.HardAngleDeg,
                        UvSeamThreshold = c.UvSeamThreshold,
                    };
                    var result = Poly_Ling.Tools.Panels.QuadDecimator.QuadPreservingDecimator.Decimate(
                        qdSrcMc.MeshObject, prms, out MeshObject resultMesh);
                    if (resultMesh == null) return;

                    resultMesh.Name = qdSrcMc.MeshObject.Name + "_decimated";
                    var qdNewMc = new MeshContext
                    {
                        Name       = resultMesh.Name,
                        MeshObject = resultMesh,
                        Materials  = new System.Collections.Generic.List<Material>(
                            qdSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                    };
                    qdNewMc.UnityMesh           = resultMesh.ToUnityMesh();
                    qdNewMc.UnityMesh.name      = resultMesh.Name;
                    qdNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    qdNewMc.ParentModelContext  = model;
                    model.Add(qdNewMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var qdAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var qdRecord = new MeshFilterToSkinnedRecord { BeforeList = qdBefore, AfterList = qdAfter };
                        _undoController.MeshListStack.Record(qdRecord, "Quad減面");
                        _undoController.FocusMeshList();
                    }
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Mirror Bake
                case BakeMirrorCommand c:
                {
                    if (model == null) return;
                    var srcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (srcMc?.MeshObject == null) return;

                    var bakeBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var (bakedMesh, bakeResult) = MirrorBaker.BakeMirror(
                        srcMc.MeshObject, c.MirrorAxis, 0f, c.Threshold, c.FlipU);
                    if (bakedMesh == null || bakeResult == null) return;

                    var newMc = new MeshContext
                    {
                        Name       = bakedMesh.Name,
                        MeshObject = bakedMesh,
                        Materials  = new System.Collections.Generic.List<Material>(
                            srcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                    };
                    newMc.UnityMesh           = bakedMesh.ToUnityMesh();
                    newMc.UnityMesh.name      = bakedMesh.Name;
                    newMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    newMc.ParentModelContext  = model;
                    model.Add(newMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var bakeAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var bakeRecord = new MeshFilterToSkinnedRecord { BeforeList = bakeBefore, AfterList = bakeAfter };
                        _undoController.MeshListStack.Record(bakeRecord, "Bake Mirror");
                        _undoController.FocusMeshList();
                    }
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Mirror WriteBack
                case WriteBackMirrorCommand c:
                {
                    if (model == null) return;
                    var wbEditedMc   = model.GetMeshContext(c.EditedMasterIndex);
                    var wbOriginalMc = model.GetMeshContext(c.OriginalMasterIndex);
                    if (wbEditedMc?.MeshObject == null || wbOriginalMc?.MeshObject == null) return;
                    if (c.BakeResult == null) return;

                    var wbBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var resultMesh = MirrorBaker.WriteBack(
                        wbEditedMc.MeshObject, wbOriginalMc.MeshObject, c.BakeResult, c.WriteBackMode);
                    if (resultMesh == null) return;

                    string wbName    = wbOriginalMc.Name + "_WriteBack";
                    resultMesh.Name  = wbName;
                    var wbNewMc = new MeshContext
                    {
                        Name           = wbName,
                        MeshObject     = resultMesh,
                        Materials      = new System.Collections.Generic.List<Material>(
                            wbOriginalMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        MirrorType     = wbOriginalMc.MirrorType,
                        MirrorAxis     = wbOriginalMc.MirrorAxis,
                        MirrorDistance = wbOriginalMc.MirrorDistance,
                    };
                    wbNewMc.UnityMesh           = resultMesh.ToUnityMesh();
                    wbNewMc.UnityMesh.name      = wbName;
                    wbNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    wbNewMc.ParentModelContext  = model;
                    model.Add(wbNewMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var wbAfter   = MeshFilterToSkinnedRecord.CaptureList(model);
                        var wbRecord  = new MeshFilterToSkinnedRecord { BeforeList = wbBefore, AfterList = wbAfter };
                        _undoController.MeshListStack.Record(wbRecord, "Mirror WriteBack");
                        _undoController.FocusMeshList();
                    }
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Mirror Blend
                case BlendMirrorCommand c:
                {
                    if (model == null) return;
                    var blSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    var blWbMc  = model.GetMeshContext(c.WriteBackMasterIndex);
                    if (blSrcMc?.MeshObject == null || blWbMc?.MeshObject == null) return;
                    var srcMesh = blSrcMc.MeshObject;
                    var wbMesh  = blWbMc.MeshObject;
                    if (srcMesh.VertexCount != wbMesh.VertexCount) return;

                    var blBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var blended   = srcMesh.Clone();
                    string blName = $"{blSrcMc.Name}_Blend{Mathf.RoundToInt(c.BlendWeight * 100)}";
                    blended.Name  = blName;
                    for (int i = 0; i < blended.VertexCount; i++)
                        blended.Vertices[i].Position = Vector3.Lerp(
                            srcMesh.Vertices[i].Position, wbMesh.Vertices[i].Position, c.BlendWeight);
                    blended.RecalculateSmoothNormals();

                    var blNewMc = new MeshContext
                    {
                        Name           = blName,
                        MeshObject     = blended,
                        Materials      = new System.Collections.Generic.List<Material>(
                            blSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        MirrorType     = blSrcMc.MirrorType,
                        MirrorAxis     = blSrcMc.MirrorAxis,
                        MirrorDistance = blSrcMc.MirrorDistance,
                    };
                    blNewMc.UnityMesh           = blended.ToUnityMesh();
                    blNewMc.UnityMesh.name      = blName;
                    blNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    blNewMc.ParentModelContext  = model;
                    model.Add(blNewMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var blAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var blRecord = new MeshFilterToSkinnedRecord { BeforeList = blBefore, AfterList = blAfter };
                        _undoController.MeshListStack.Record(blRecord, "Mirror Blend");
                        _undoController.FocusMeshList();
                    }
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── Humanoidマッピング適用
                case ApplyHumanoidMappingCommand c:
                {
                    if (model == null || c.Mapping == null) return;
                    _undoController?.SetModelContext(model);
                    var hmBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.CopyFrom(c.Mapping);
                    var hmAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmBefore, hmAfter, "Apply Humanoid Mapping");
                        _undoController.MeshListStack.Record(record, "Apply Humanoid Mapping");
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Humanoidマッピングクリア
                case ClearHumanoidMappingCommand _:
                {
                    if (model == null) return;
                    _undoController?.SetModelContext(model);
                    var hmcBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.ClearAll();
                    var hmcAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmcBefore, hmcAfter, "Clear Humanoid Mapping");
                        _undoController.MeshListStack.Record(record, "Clear Humanoid Mapping");
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Tポーズ変換
                case ApplyTPoseCommand _:
                {
                    if (model == null) return;
                    var mapping = model.HumanoidMapping;
                    if (mapping == null || mapping.IsEmpty) return;

                    // SetModelContext（MeshListStack の context を現在のモデルに設定）
                    _undoController?.SetModelContext(model);

                    var beforeState    = new Poly_Ling.Ops.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
                    var oldTPoseBackup = model.TPoseBackup;

                    var backup = new Poly_Ling.Ops.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.ConvertToTPose(model.MeshContextList, mapping, backup);
                    model.TPoseBackup = backup;

                    var afterState = new Poly_Ling.Ops.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, afterState);

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                        _undoController.MeshListStack.Record(record, "Apply T-Pose");
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Tポーズ復元
                case RestoreTPoseCommand _:
                {
                    if (model?.TPoseBackup == null) return;
                    _undoController?.SetModelContext(model);

                    var restoreBefore = new Poly_Ling.Ops.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreBefore);
                    var oldTPoseBackup = model.TPoseBackup;

                    Poly_Ling.Ops.TPoseConverter.RestoreFromBackup(model.MeshContextList, model.TPoseBackup);

                    var restoreAfter = new Poly_Ling.Ops.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreAfter);
                    model.TPoseBackup = null;

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(restoreBefore, restoreAfter, oldTPoseBackup, null, "Restore Original Pose");
                        _undoController.MeshListStack.Record(record, "Restore Original Pose");
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── Tポーズ Bake（Undo不可・バックアップ破棄のみ）
                case BakeTPoseCommand _:
                {
                    if (model == null) return;
                    model.TPoseBackup = null;
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                // ── メッシュマージ
                case MergeMeshesCommand c:
                {
                    if (model == null) return;
                    if (c.MasterIndices == null || c.MasterIndices.Length < 2) return;

                    // 対象 MeshContext を収集
                    var mergeTargets = new System.Collections.Generic.List<MeshContext>();
                    foreach (int mi in c.MasterIndices)
                    {
                        var mctx = model.GetMeshContext(mi);
                        if (mctx?.MeshObject != null) mergeTargets.Add(mctx);
                    }
                    if (mergeTargets.Count < 2) return;

                    var baseCtx = model.GetMeshContext(c.BaseMasterIndex);
                    if (baseCtx?.MeshObject == null) return;

                    // 変更前スナップショット（MeshListStack Undo 用）
                    var mergeBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    Matrix4x4 baseWorldInv = baseCtx.WorldMatrixInverse;

                    // マージ先 MeshContext の準備
                    MeshContext destCtx;
                    if (c.CreateNewMesh)
                    {
                        destCtx = new MeshContext
                        {
                            Name             = baseCtx.MeshObject.Name + "_merged",
                            MeshObject       = new MeshObject(baseCtx.MeshObject.Name + "_merged"),
                            OriginalPositions = new Vector3[0],
                        };
                        var bt = new BoneTransform();
                        bt.CopyFrom(baseCtx.BoneTransform);
                        destCtx.BoneTransform      = bt;
                        destCtx.WorldMatrix        = baseCtx.WorldMatrix;
                        destCtx.WorldMatrixInverse = baseCtx.WorldMatrixInverse;
                        destCtx.BindPose           = baseCtx.BindPose;
                    }
                    else
                    {
                        destCtx = baseCtx;
                    }

                    MeshObject destMesh = destCtx.MeshObject;

                    // 各ソースメッシュを destMesh に追記
                    foreach (var srcCtx in mergeTargets)
                    {
                        bool isBase = ReferenceEquals(srcCtx, baseCtx);
                        if (!c.CreateNewMesh && isBase) continue;

                        var srcMesh = srcCtx.MeshObject;
                        if (srcMesh == null || srcMesh.VertexCount == 0) continue;

                        Matrix4x4 xform  = baseWorldInv * srcCtx.WorldMatrix;
                        int vertexOffset = destMesh.VertexCount;

                        foreach (var v in srcMesh.Vertices)
                        {
                            var newV      = v.Clone();
                            newV.Id       = destMesh.GenerateVertexId();
                            newV.Position = xform.MultiplyPoint3x4(v.Position);
                            if (v.Normals != null)
                                newV.Normals = v.Normals.Select(n => xform.MultiplyVector(n).normalized).ToList();
                            destMesh.Vertices.Add(newV);
                            destMesh.RegisterVertexId(newV.Id);
                        }

                        foreach (var f in srcMesh.Faces)
                        {
                            var newF          = f.Clone();
                            newF.Id           = destMesh.GenerateFaceId();
                            newF.VertexIndices = f.VertexIndices.Select(i => i + vertexOffset).ToList();
                            destMesh.Faces.Add(newF);
                            destMesh.RegisterFaceId(newF.Id);
                        }
                    }

                    // UnityMesh 再生成
                    var mergedUnityMesh       = destMesh.ToUnityMesh();
                    mergedUnityMesh.name      = destMesh.Name;
                    mergedUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    destCtx.UnityMesh         = mergedUnityMesh;
                    destCtx.OriginalPositions = (Vector3[])destMesh.Positions.Clone();

                    // モデルへの追加・削除
                    if (c.CreateNewMesh)
                    {
                        destCtx.ParentModelContext = model;
                        model.Add(destCtx);
                    }
                    else
                    {
                        var nonBaseTargets    = mergeTargets.Where(t => !ReferenceEquals(t, baseCtx)).ToList();
                        var indicesToRemove   = nonBaseTargets
                            .Select(t => model.IndexOf(t))
                            .Where(i => i >= 0)
                            .OrderByDescending(i => i)
                            .ToList();
                        foreach (int idx in indicesToRemove)
                            model.RemoveAt(idx);
                    }

                    model.OnListChanged?.Invoke();

                    // 変更後スナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var mergeAfter = MeshFilterToSkinnedRecord.CaptureList(model);
                        var mergeRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = mergeBefore,
                            AfterList  = mergeAfter,
                        };
                        _undoController.MeshListStack.Record(mergeRecord, "メッシュマージ");
                        _undoController.FocusMeshList();
                    }

                    _viewportManager.RebuildAdapter(0, model);
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── 差分からのモーフ生成
                case CreateMorphFromDiffCommand c:
                {
                    var morphProject = _getProject();
                    if (morphProject == null) return;
                    var baseModel  = morphProject.GetModel(c.BaseModelIndex);
                    var morphModel = morphProject.GetModel(c.MorphModelIndex);
                    if (baseModel == null || morphModel == null) return;
                    if (c.BaseModelIndex == c.MorphModelIndex) return;
                    if (baseModel.Count != morphModel.Count) return;

                    // 変更前スナップショット
                    var morphBefore     = MeshFilterToSkinnedRecord.CaptureList(baseModel);
                    var morphExprBefore = baseModel.MorphExpressions
                        .Select(e => e.Clone()).ToList();

                    var expression   = new MorphExpression(c.MorphName, MorphType.Vertex) { Panel = c.Panel };
                    int morphCreated = 0;
                    const float DiffThresholdSq = 0.0001f * 0.0001f;

                    for (int mi = 0; mi < baseModel.Count; mi++)
                    {
                        var baseCtx  = baseModel.GetMeshContext(mi);
                        var morphCtx = morphModel.GetMeshContext(mi);
                        if (baseCtx == null || morphCtx == null) continue;
                        if (baseCtx.MeshObject == null || morphCtx.MeshObject == null) continue;
                        if (baseCtx.Type  != MeshType.Mesh && baseCtx.Type  != MeshType.BakedMirror) continue;
                        if (baseCtx.MeshObject.VertexCount != morphCtx.MeshObject.VertexCount) continue;

                        // 差分チェック
                        bool hasDiff = false;
                        int  checkCount = Mathf.Min(baseCtx.MeshObject.VertexCount, morphCtx.MeshObject.VertexCount);
                        for (int vi = 0; vi < checkCount; vi++)
                        {
                            var d = morphCtx.MeshObject.Vertices[vi].Position
                                  - baseCtx.MeshObject.Vertices[vi].Position;
                            if (d.sqrMagnitude > DiffThresholdSq) { hasDiff = true; break; }
                        }
                        if (!hasDiff) continue;

                        // Mirror 側はスキップ（Real 側から生成）
                        if (baseModel.IsMirrorSide(baseCtx)) continue;

                        // Real 側モーフ生成
                        int newIdx = CreateMorphMeshContextInDispatcher(
                            baseModel, baseCtx, mi, morphCtx.MeshObject,
                            c.MorphName, c.Panel, expression);
                        morphCreated++;

                        // Mirror 側モーフ生成
                        var pair = baseModel.GetMirrorPair(baseCtx);
                        if (pair != null && pair.Real == baseCtx && pair.Mirror != null)
                        {
                            int mirrorParentIdx = baseModel.MeshContextList.IndexOf(pair.Mirror);
                            if (mirrorParentIdx >= 0)
                                CreateMirrorMorphMeshContextInDispatcher(
                                    baseModel, pair, mirrorParentIdx,
                                    baseCtx.MeshObject, morphCtx.MeshObject,
                                    c.MorphName, c.Panel, expression);
                        }
                    }

                    if (morphCreated == 0) return;

                    baseModel.MorphExpressions.Add(expression);
                    baseModel.OnListChanged?.Invoke();

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var morphAfter     = MeshFilterToSkinnedRecord.CaptureList(baseModel);
                        var morphExprAfter = baseModel.MorphExpressions.Select(e => e.Clone()).ToList();
                        var record = new MorphCreateRecord
                        {
                            BeforeList        = morphBefore,
                            AfterList         = morphAfter,
                            BeforeExpressions = morphExprBefore,
                            AfterExpressions  = morphExprAfter,
                        };
                        _undoController.MeshListStack.Record(record, $"モーフ作成: {c.MorphName}");
                        _undoController.FocusMeshList();
                    }

                    _viewportManager.RebuildAdapter(0, baseModel);
                    _renderer?.UpdateSelectedDrawableMesh(0, baseModel);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;
                }

                // ── パーツ選択辞書 ─────────────────────────────────────────────
                case SavePartsSetCommand c:
                {
                    if (model == null) return;
                    var psMc = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
                    if (psMc == null) return;
                    var psSel = psMc.Selection;
                    if (psSel == null || !psSel.HasAnySelection) return;
                    string psName = string.IsNullOrEmpty(c.SetName)
                        ? psMc.GenerateUniqueSelectionSetName("Selection")
                        : c.SetName;
                    if (psMc.FindSelectionSetByName(psName) != null)
                        psName = psMc.GenerateUniqueSelectionSetName(psName);
                    var psSnap = psSel.CreateSnapshot();
                    var psSet  = Poly_Ling.Selection.PartsSelectionSet.FromCurrentSelection(
                        psName, psSnap.Vertices, psSnap.Edges, psSnap.Faces, psSnap.Lines, psSnap.Mode);
                    psMc.PartsSelectionSetList.Add(psSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case LoadPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: false);
                    return;

                case AddPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: true, subtract: false);
                    return;

                case SubtractPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: true);
                    return;

                case DeletePartsSetCommand c:
                {
                    if (model == null) return;
                    var delMc = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
                    var delSets = delMc?.PartsSelectionSetList;
                    if (delSets == null || c.SetIndex < 0 || c.SetIndex >= delSets.Count) return;
                    delSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case RenamePartsSetCommand c:
                {
                    if (model == null) return;
                    var rnMc   = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
                    var rnSets = rnMc?.PartsSelectionSetList;
                    if (rnSets == null || c.SetIndex < 0 || c.SetIndex >= rnSets.Count) return;
                    string rnName = c.NewName;
                    if (rnMc.FindSelectionSetByName(rnName) != null && rnName != rnSets[c.SetIndex].Name)
                        rnName = rnMc.GenerateUniqueSelectionSetName(rnName);
                    rnSets[c.SetIndex].Name = rnName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case ExportPartsSetsCsvCommand _:
                case ImportPartsSetCsvCommand _:
                    // Player では CSV I/O 未対応
                    return;

                // ── メッシュ選択辞書 ───────────────────────────────────────────
                case SaveSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var sdCategory = c.Category switch
                    {
                        MeshCategory.Bone  => ModelContext.SelectionCategory.Bone,
                        MeshCategory.Morph => ModelContext.SelectionCategory.Morph,
                        _                  => ModelContext.SelectionCategory.Mesh,
                    };
                    string sdName = string.IsNullOrEmpty(c.SetName)
                        ? model.GenerateUniqueMeshSelectionSetName("MeshSet")
                        : c.SetName;
                    if (model.FindMeshSelectionSetByName(sdName) != null)
                        sdName = model.GenerateUniqueMeshSelectionSetName(sdName);
                    var sdSet = new MeshSelectionSet(sdName) { Category = sdCategory };
                    foreach (var n in c.MeshNames)
                        if (!string.IsNullOrEmpty(n) && !sdSet.MeshNames.Contains(n))
                            sdSet.MeshNames.Add(n);
                    model.MeshSelectionSets.Add(sdSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case ApplySelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var sdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= sdSets.Count) return;

                    // Undo 用：適用前のメッシュ選択を記録
                    var sdOldSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (c.AddToExisting)
                        sdSets[c.SetIndex].AddTo(model);
                    else
                        sdSets[c.SetIndex].ApplyTo(model);
                    var sdNewSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (_undoController != null)
                    {
                        var sdRecord = new MeshSelectionChangeRecord(sdOldSel, sdNewSel);
                        _undoController.MeshListStack.Record(sdRecord, "メッシュ選択辞書適用");
                        _undoController.FocusMeshList();
                    }
                    _renderer?.UpdateSelectedDrawableMesh(0, model);
                    _notifyPanels(ChangeKind.Selection);
                    return;
                }

                case DeleteSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var dsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= dsdSets.Count) return;
                    dsdSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return;
                }

                case RenameSelectionDictionaryCommand c:
                {
                    if (model == null) return;
                    var rsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= rsdSets.Count) return;
                    string rsdName = c.NewName;
                    if (model.FindMeshSelectionSetByName(rsdName) != null && rsdName != rsdSets[c.SetIndex].Name)
                        rsdName = model.GenerateUniqueMeshSelectionSetName(rsdName);
                    rsdSets[c.SetIndex].Name = rsdName;
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

        private ToolContext BuildMinimalToolCtx(ModelContext model)
        {
            var ctx = new ToolContext();
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
                _viewportManager.RebuildAdapter(0, model);
                _renderer?.UpdateSelectedDrawableMesh(0, model);
            };
            ctx.Repaint        = () => { };
            return ctx;
        }

        // ================================================================
        // スカルプト ブラシ ヘルパー（SculptStrokeCommand 用）
        // ================================================================

        private static List<(int index, float weight)> SculptGetAffected(
            MeshObject mo, Vector3 center, float radius, FalloffType falloff)
        {
            var result = new List<(int, float)>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                float dist = Vector3.Distance(mo.Vertices[i].Position, center);
                if (dist <= radius)
                {
                    float t      = radius > 0f ? dist / radius : 0f;
                    float weight = FalloffHelper.Calculate(t, falloff);
                    result.Add((i, weight));
                }
            }
            return result;
        }

        private static Dictionary<int, HashSet<int>> SculptBuildAdjacency(MeshObject mo)
        {
            var cache = new Dictionary<int, HashSet<int>>();
            foreach (var face in mo.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    if (!cache.ContainsKey(v1)) cache[v1] = new HashSet<int>();
                    if (!cache.ContainsKey(v2)) cache[v2] = new HashSet<int>();
                    cache[v1].Add(v2);
                    cache[v2].Add(v1);
                }
            }
            return cache;
        }

        private static Dictionary<int, Vector3> SculptBuildVertexNormals(MeshObject mo)
        {
            var faceNormals = new Dictionary<int, List<Vector3>>();
            foreach (var face in mo.Faces)
            {
                if (face.VertexIndices.Count < 3) continue;
                var v0 = mo.Vertices[face.VertexIndices[0]].Position;
                var v1 = mo.Vertices[face.VertexIndices[1]].Position;
                var v2 = mo.Vertices[face.VertexIndices[2]].Position;
                var fn = NormalHelper.CalculateFaceNormal(v0, v1, v2);
                foreach (int vi in face.VertexIndices)
                {
                    if (!faceNormals.ContainsKey(vi)) faceNormals[vi] = new List<Vector3>();
                    faceNormals[vi].Add(fn);
                }
            }
            var result = new Dictionary<int, Vector3>();
            foreach (var kv in faceNormals)
            {
                var avg = Vector3.zero;
                foreach (var n in kv.Value) avg += n;
                result[kv.Key] = avg.normalized;
            }
            return result;
        }

        private static void SculptApplyDraw(
            MeshObject mo,
            List<(int index, float weight)> verts,
            float strength, bool invert,
            Dictionary<int, Vector3> normals)
        {
            if (normals == null) return;
            var avgN = Vector3.zero;
            foreach (var (idx, w) in verts)
                if (normals.TryGetValue(idx, out var n)) avgN += n * w;
            avgN = avgN.normalized;
            float dir = invert ? -1f : 1f;
            foreach (var (idx, w) in verts)
                mo.Vertices[idx].Position += avgN * strength * w * dir;
        }

        private static void SculptApplySmooth(
            MeshObject mo,
            List<(int index, float weight)> verts,
            float strength,
            Dictionary<int, HashSet<int>> adjacency)
        {
            if (adjacency == null) return;
            var newPos = new Dictionary<int, Vector3>();
            foreach (var (idx, w) in verts)
            {
                if (!adjacency.TryGetValue(idx, out var neighbors) || neighbors.Count == 0) continue;
                var avg = Vector3.zero;
                foreach (int nb in neighbors) avg += mo.Vertices[nb].Position;
                avg /= neighbors.Count;
                newPos[idx] = Vector3.Lerp(mo.Vertices[idx].Position, avg, strength * w);
            }
            foreach (var kv in newPos) mo.Vertices[kv.Key].Position = kv.Value;
        }

        private static void SculptApplyInflate(
            MeshObject mo,
            List<(int index, float weight)> verts,
            float strength, bool invert,
            Dictionary<int, Vector3> normals)
        {
            if (normals == null) return;
            float dir = invert ? -1f : 1f;
            foreach (var (idx, w) in verts)
                if (normals.TryGetValue(idx, out var n))
                    mo.Vertices[idx].Position += n * strength * w * dir;
        }

        private static void SculptApplyFlatten(
            MeshObject mo,
            List<(int index, float weight)> verts,
            float strength,
            Dictionary<int, Vector3> normals)
        {
            if (verts.Count == 0 || normals == null) return;
            var avgPos = Vector3.zero;
            var avgN   = Vector3.zero;
            float total = 0f;
            foreach (var (idx, w) in verts)
            {
                avgPos += mo.Vertices[idx].Position * w;
                if (normals.TryGetValue(idx, out var n)) avgN += n * w;
                total  += w;
            }
            if (total > 0f) avgPos /= total;
            avgN = avgN.normalized;

            foreach (var (idx, w) in verts)
            {
                var pos  = mo.Vertices[idx].Position;
                var proj = pos - avgN * Vector3.Dot(pos - avgPos, avgN);
                mo.Vertices[idx].Position = Vector3.Lerp(pos, proj, strength * w);
            }
        }

        // ================================================================
        // 詳細選択 トポロジー ヘルパー（AdvancedSelectCommand 用）
        // ================================================================

        // ── Connected ────────────────────────────────────────────────

        private static List<int> AdvConnectedFromVertex(MeshObject mo, int start)
        {
            var adj    = SelectionHelper.BuildVertexAdjacency(mo);
            var result = new HashSet<int>();
            var queue  = new Queue<int>();
            queue.Enqueue(start); result.Add(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var neighbors)) continue;
                foreach (int nb in neighbors)
                    if (result.Add(nb)) queue.Enqueue(nb);
            }
            return result.ToList();
        }

        private static List<VertexPair> AdvConnectedFromEdge(MeshObject mo, VertexPair start)
        {
            var adj    = SelectionHelperBuildEdgeAdj(mo);
            var result = new HashSet<VertexPair>();
            var queue  = new Queue<VertexPair>();
            queue.Enqueue(start); result.Add(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var neighbors)) continue;
                foreach (var nb in neighbors)
                    if (result.Add(nb)) queue.Enqueue(nb);
            }
            return result.ToList();
        }

        private static List<int> AdvConnectedFromFace(MeshObject mo, int start)
        {
            var adj    = SelectionHelper.BuildFaceAdjacency(mo);
            var result = new HashSet<int>();
            var queue  = new Queue<int>();
            queue.Enqueue(start); result.Add(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var neighbors)) continue;
                foreach (int nb in neighbors)
                    if (result.Add(nb)) queue.Enqueue(nb);
            }
            return result.ToList();
        }

        // ── Belt ─────────────────────────────────────────────────────

        private static (HashSet<int> verts, List<VertexPair> edges, List<int> faces)
            AdvBelt(MeshObject mo, VertexPair startEdge)
        {
            var verts        = new HashSet<int>();
            var ladderEdges  = new List<VertexPair>();
            var faces        = new List<int>();
            var edgeToFaces  = SelectionHelper.BuildEdgeToFacesMap(mo);
            var visited      = new HashSet<VertexPair>();

            AdvBeltTraverse(mo, startEdge, edgeToFaces, visited, verts, ladderEdges, faces, forward: true);
            AdvBeltTraverse(mo, startEdge, edgeToFaces, visited, verts, ladderEdges, faces, forward: false);

            return (verts, ladderEdges, faces);
        }

        private static void AdvBeltTraverse(
            MeshObject mo, VertexPair cur,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            HashSet<VertexPair> visited,
            HashSet<int> verts, List<VertexPair> edges, List<int> faces,
            bool forward)
        {
            while (true)
            {
                if (visited.Contains(cur)) break;
                visited.Add(cur);
                verts.Add(cur.V1); verts.Add(cur.V2);
                edges.Add(cur);

                if (!edgeToFaces.TryGetValue(cur, out var faceList)) break;

                VertexPair? next = null;
                foreach (int fi in faceList)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexIndices.Count != 4) continue;
                    if (!faces.Contains(fi)) faces.Add(fi);
                    var opp = AdvFindOppositeEdge(face, cur.V1, cur.V2);
                    if (opp.HasValue)
                    {
                        var oppPair = new VertexPair(opp.Value.Item1, opp.Value.Item2);
                        if (!visited.Contains(oppPair)) { next = oppPair; break; }
                    }
                }
                if (!next.HasValue) break;
                cur = next.Value;
            }
        }

        private static (int, int)? AdvFindOppositeEdge(Face face, int v1, int v2)
        {
            var vs = face.VertexIndices;
            int n  = vs.Count;
            if (n != 4) return null;
            for (int i = 0; i < n; i++)
            {
                if ((vs[i] == v1 && vs[(i + 1) % n] == v2) ||
                    (vs[i] == v2 && vs[(i + 1) % n] == v1))
                {
                    int s = (i + 2) % n;
                    int e = (i + 3) % n;
                    return (vs[s], vs[e]);
                }
            }
            return null;
        }

        // ── EdgeLoop ─────────────────────────────────────────────────

        private static List<VertexPair> AdvEdgeLoop(MeshObject mo, VertexPair startEdge, float threshold)
        {
            var adj     = SelectionHelper.BuildVertexAdjacency(mo);
            var result  = new HashSet<VertexPair>();
            var visited = new HashSet<VertexPair>();
            var dir = (mo.Vertices[startEdge.V2].Position - mo.Vertices[startEdge.V1].Position).normalized;

            AdvEdgeLoopTraverse(mo, startEdge.V1, startEdge.V2,  dir, adj, visited, result, threshold);
            AdvEdgeLoopTraverse(mo, startEdge.V2, startEdge.V1, -dir, adj, visited, result, threshold);

            return result.ToList();
        }

        private static void AdvEdgeLoopTraverse(
            MeshObject mo, int from, int to, Vector3 dir,
            Dictionary<int, HashSet<int>> adj,
            HashSet<VertexPair> visited, HashSet<VertexPair> result,
            float threshold)
        {
            int prev = from, cur = to;
            var curDir = dir;
            while (true)
            {
                var edge = new VertexPair(prev, cur);
                if (visited.Contains(edge)) break;
                visited.Add(edge); result.Add(edge);

                if (!adj.TryGetValue(cur, out var neighbors)) break;
                int best = -1; float bestDot = threshold;
                foreach (int nb in neighbors)
                {
                    if (nb == prev) continue;
                    var nd = (mo.Vertices[nb].Position - mo.Vertices[cur].Position).normalized;
                    float dot = Vector3.Dot(curDir, nd);
                    if (dot > bestDot) { bestDot = dot; best = nb; }
                }
                if (best < 0) break;
                curDir = (mo.Vertices[best].Position - mo.Vertices[cur].Position).normalized;
                prev = cur; cur = best;
            }
        }

        // ── ShortestPath (Dijkstra) ───────────────────────────────────

        private static List<int> AdvShortestPath(MeshObject mo, int start, int end)
        {
            var adj      = SelectionHelper.BuildVertexAdjacency(mo);
            var dist     = new Dictionary<int, float>();
            var prev     = new Dictionary<int, int>();
            var unvisited = new HashSet<int>();

            for (int i = 0; i < mo.VertexCount; i++) { dist[i] = float.MaxValue; unvisited.Add(i); }
            dist[start] = 0f;

            while (unvisited.Count > 0)
            {
                int cur = -1; float minD = float.MaxValue;
                foreach (int v in unvisited) if (dist[v] < minD) { minD = dist[v]; cur = v; }
                if (cur < 0 || cur == end) break;
                unvisited.Remove(cur);
                if (!adj.TryGetValue(cur, out var nbs)) continue;
                foreach (int nb in nbs)
                {
                    if (!unvisited.Contains(nb)) continue;
                    float alt = dist[cur] + Vector3.Distance(mo.Vertices[cur].Position, mo.Vertices[nb].Position);
                    if (alt < dist[nb]) { dist[nb] = alt; prev[nb] = cur; }
                }
            }

            var path = new List<int>();
            int node = end;
            while (prev.ContainsKey(node)) { path.Add(node); node = prev[node]; }
            path.Add(start);
            path.Reverse();
            return path;
        }

        // ── 共通ユーティリティ ────────────────────────────────────────

        private static List<VertexPair> AdvEdgesFromVertices(MeshObject mo, IEnumerable<int> verts)
        {
            var vset   = new HashSet<int>(verts);
            var result = new HashSet<VertexPair>();
            foreach (var face in mo.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    if (vset.Contains(v1) && vset.Contains(v2))
                        result.Add(new VertexPair(v1, v2));
                }
            }
            return result.ToList();
        }

        private static List<VertexPair> AdvEdgesFromFaces(MeshObject mo, IEnumerable<int> faceIndices)
        {
            var result = new HashSet<VertexPair>();
            foreach (int fi in faceIndices)
            {
                var verts = mo.Faces[fi].VertexIndices;
                int n = verts.Count;
                for (int i = 0; i < n; i++)
                    result.Add(new VertexPair(verts[i], verts[(i + 1) % n]));
            }
            return result.ToList();
        }

        private static List<int> AdvFacesFromVertices(MeshObject mo, IEnumerable<int> verts)
        {
            var vset   = new HashSet<int>(verts);
            var result = new List<int>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                bool all = true;
                foreach (int v in mo.Faces[fi].VertexIndices)
                    if (!vset.Contains(v)) { all = false; break; }
                if (all) result.Add(fi);
            }
            return result;
        }

        private static List<int> AdvFacesFromEdges(MeshObject mo, IEnumerable<VertexPair> edges)
        {
            var eset   = new HashSet<VertexPair>(edges);
            var result = new HashSet<int>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var vs = mo.Faces[fi].VertexIndices;
                int n  = vs.Count;
                for (int i = 0; i < n; i++)
                    if (eset.Contains(new VertexPair(vs[i], vs[(i + 1) % n])))
                        { result.Add(fi); break; }
            }
            return result.ToList();
        }

        private static List<VertexPair> AdvEdgesFromPath(List<int> path)
        {
            var result = new List<VertexPair>();
            for (int i = 0; i < path.Count - 1; i++)
                result.Add(new VertexPair(path[i], path[i + 1]));
            return result;
        }

        // SelectionHelper の BuildEdgeAdjacency は ToolContext を取るが
        // MeshObject のみから辺隣接を構築するオーバーロードを作成
        private static Dictionary<VertexPair, HashSet<VertexPair>> SelectionHelperBuildEdgeAdj(MeshObject mo)
        {
            // 辺→共有する面の辺隣接を構築（SelectionHelper.BuildEdgeAdjacency の MeshObject 版）
            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);
            var result      = new Dictionary<VertexPair, HashSet<VertexPair>>();

            foreach (var kv in edgeToFaces)
            {
                if (!result.ContainsKey(kv.Key)) result[kv.Key] = new HashSet<VertexPair>();
                foreach (int fi in kv.Value)
                {
                    var vs = mo.Faces[fi].VertexIndices;
                    int n  = vs.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var e = new VertexPair(vs[i], vs[(i + 1) % n]);
                        if (e != kv.Key)
                        {
                            result[kv.Key].Add(e);
                            if (!result.ContainsKey(e)) result[e] = new HashSet<VertexPair>();
                            result[e].Add(kv.Key);
                        }
                    }
                }
            }
            return result;
        }

        // ================================================================
        // 差分からのモーフ生成 ヘルパー
        // ================================================================

        private static int CreateMorphMeshContextInDispatcher(
            ModelContext baseModel, MeshContext baseCtx, int parentIdx,
            MeshObject morphMeshObj, string morphName, int panel,
            MorphExpression expression)
        {
            var morphObj      = baseCtx.MeshObject.Clone();
            morphObj.Type     = MeshType.Morph;
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
                morphObj.Vertices[vi].Position = morphMeshObj.Vertices[vi].Position;

            var newCtx = new MeshContext
            {
                Name       = morphName,
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, baseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = parentIdx;

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
            return newIdx;
        }

        private static void CreateMirrorMorphMeshContextInDispatcher(
            ModelContext baseModel, MirrorPair pair, int mirrorParentIdx,
            MeshObject realBaseObj, MeshObject realMorphObj,
            string morphName, int panel, MorphExpression expression)
        {
            var mirrorBaseCtx = pair.Mirror;
            if (mirrorBaseCtx?.MeshObject == null) return;

            var morphObj  = mirrorBaseCtx.MeshObject.Clone();
            morphObj.Type = MeshType.Morph;
            for (int vi = 0; vi < morphObj.VertexCount; vi++)
            {
                int ri = pair.VertexMap != null && vi < pair.VertexMap.Length
                    ? pair.VertexMap[vi] : vi;
                if (ri < 0 || ri >= realBaseObj.VertexCount) continue;
                var realDiff   = realMorphObj.Vertices[ri].Position - realBaseObj.Vertices[ri].Position;
                var mirrorDiff = pair.MirrorDirection(realDiff);
                morphObj.Vertices[vi].Position =
                    mirrorBaseCtx.MeshObject.Vertices[vi].Position + mirrorDiff;
            }

            var newCtx = new MeshContext
            {
                Name       = morphName,
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, mirrorBaseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = mirrorParentIdx;

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
        }

        // ================================================================
        // パーツ選択辞書ヘルパー
        // ================================================================

        private void PartsSetApply(ModelContext model, int setIndex, bool additive, bool subtract)
        {
            if (model == null) return;
            var mc   = model.FirstSelectedMeshContext ?? model.FirstDrawableMeshContext;
            var sets = mc?.PartsSelectionSetList;
            if (sets == null || setIndex < 0 || setIndex >= sets.Count) return;
            var sel = mc.Selection;
            if (sel == null) return;

            // Undo 用：適用前スナップショット
            SelectionSnapshot oldSnap = sel.CreateSnapshot();

            var set = sets[setIndex];
            SelectionSnapshot newSnap;
            if (additive)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.UnionWith(set.Vertices);
                snap.Edges.UnionWith(set.Edges);
                snap.Faces.UnionWith(set.Faces);
                snap.Lines.UnionWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else if (subtract)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.ExceptWith(set.Vertices);
                snap.Edges.ExceptWith(set.Edges);
                snap.Faces.ExceptWith(set.Faces);
                snap.Lines.ExceptWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else
            {
                newSnap = new SelectionSnapshot
                {
                    Mode     = set.Mode,
                    Vertices = new HashSet<int>(set.Vertices),
                    Edges    = new HashSet<VertexPair>(set.Edges),
                    Faces    = new HashSet<int>(set.Faces),
                    Lines    = new HashSet<int>(set.Lines),
                };
                sel.RestoreFromSnapshot(newSnap);
            }

            // Undo 記録（VertexEditStack の SelectionChangeRecord）
            if (_undoController != null)
            {
                var record = new SelectionChangeRecord(oldSnap, newSnap);
                _undoController.VertexEditStack.Record(record, "パーツ選択辞書 適用");
                _undoController.FocusVertexEdit();
            }

            _selectionOps?.SetSelectionState(sel);
            _renderer?.SetSelectionState(sel);
            _notifyPanels(ChangeKind.Selection);
        }
    }
}
