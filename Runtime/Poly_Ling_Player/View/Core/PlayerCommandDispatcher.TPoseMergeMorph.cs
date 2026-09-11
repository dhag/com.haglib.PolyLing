// PlayerCommandDispatcher.TPoseMergeMorph.cs
// コマンドディスパッチャ：Tポーズ・メッシュマージ・ブーリアン・差分からのモーフ生成。
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
        /// DispatchCore の分担：Tポーズ・メッシュマージ・ブーリアン・差分からのモーフ生成。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchTPoseMergeMorph(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── この姿勢で確定（焼き込み）：現在のポーズを頂点へ焼き込み、ベースへリセット
                case FreezeCurrentPoseCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    _undoController?.SetModelContext(model);

                    var beforeState = new TPoseBackup();
                    TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);

                    // 1. 現在のポーズ込みでワールド確定 → 頂点焼き込み
                    model.ComputeWorldMatrices();
                    TPoseConverter.BakeSkinnedVertices(model.MeshContextList);

                    // 2. ポーズ層を全クリア（ゼロポーズ＝ベース）
                    for (int i = 0; i < model.Count; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        mc.BonePoseData?.ClearAllLayers();
                    }

                    // 3. ベースのワールドで再計算 → リバインド（焼いた姿勢を新デフォルトに）
                    model.ComputeWorldMatrices();
                    for (int i = 0; i < model.Count; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        mc.BindPose = mc.WorldMatrix.inverse;
                    }

                    var afterState = new TPoseBackup();
                    TPoseConverter.CaptureBackup(model.MeshContextList, afterState);

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(beforeState, afterState,
                            model.TPoseBackup, model.TPoseBackup, "この姿勢で確定");
                        {
                            string __dbgDesc = "この姿勢で確定";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Tポーズ復元
                case RestoreTPoseCommand _:
                {
                    if (model?.TPoseBackup == null) { Fail("T ポーズの控えがありません"); return true; }
                    _undoController?.SetModelContext(model);

                    var restoreBefore = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreBefore);
                    var oldTPoseBackup = model.TPoseBackup;

                    Poly_Ling.Ops.TPoseConverter.RestoreFromBackup(model.MeshContextList, model.TPoseBackup);

                    var restoreAfter = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, restoreAfter);
                    model.TPoseBackup = null;

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(restoreBefore, restoreAfter, oldTPoseBackup, null, "Restore Original Pose");
                        {
                            string __dbgDesc = "Restore Original Pose";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Tポーズ Bake（Undo不可・バックアップ破棄のみ）
                case BakeTPoseCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    model.TPoseBackup = null;
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── メッシュマージ
                case MergeMeshesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length < 2) { Fail("結合には対象を 2 個以上指定してください"); return true; }

                    // 対象 MeshContext を収集
                    var mergeTargets = new System.Collections.Generic.List<MeshContext>();
                    foreach (int mi in c.MasterIndices)
                    {
                        var mctx = model.GetMeshContext(mi);
                        if (mctx?.MeshObject != null) mergeTargets.Add(mctx);
                    }
                    if (mergeTargets.Count < 2) { Fail("結合できる対象が 2 個ありません"); return true; }

                    var baseCtx = model.GetMeshContext(c.BaseMasterIndex);
                    if (baseCtx?.MeshObject == null) { Fail("基準オブジェクトのメッシュがありません"); return true; }

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

                    // 部品IDはマージで振り直す。1 つのメッシュにつき 1 つの部品として
                    // 0 から順に付け、サブIDは全部の追記が終わってから部品ごとに 0 から振る。
                    // CreateNewMesh=false のときは base の既存頂点が部品 0 になる。
                    int mergePartsId = 0;
                    if (!c.CreateNewMesh)
                    {
                        Poly_Ling.Ops.PartsIdOps.SetPartsId(destMesh, mergePartsId);
                        mergePartsId++;
                    }

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
                            newV.PartsId  = mergePartsId;
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

                        mergePartsId++;
                    }

                    // サブIDは部品ごとに 0 から。頂点を全部並べ終えてから振る。
                    Poly_Ling.Ops.PartsIdOps.AssignSubIdByPartsId(destMesh);

                    // UnityMesh 再生成
                    var mergedUnityMesh       = destMesh.ToUnityMesh();
                    mergedUnityMesh.name      = destMesh.Name;
                    mergedUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                    // CreateNewMesh=false のとき destCtx は baseCtx（既存 MeshContext）そのもの。
                    destCtx.ReplaceUnityMesh(mergedUnityMesh);
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
                        {
                            string __dbgDesc = "メッシュマージ";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, mergeRecord);
                            _undoController.MeshListStack.Record(mergeRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── ブーリアン
                case BooleanMeshCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var boolCtxA = model.GetMeshContext(c.AMasterIndex);
                    var boolCtxB = model.GetMeshContext(c.BMasterIndex);
                    if (boolCtxA?.MeshObject == null || boolCtxB?.MeshObject == null) { Fail("対象メッシュが 2 つ揃っていません"); return true; }
                    if (ReferenceEquals(boolCtxA, boolCtxB)) { Fail("同じオブジェクト同士では計算できません"); return true; }

                    // 演算そのものは BooleanOps に集約してある。
                    // 演算空間は A のローカル空間で、結果も A の姿勢を引き継ぐ。
                    var boolResult = BooleanOps.Perform(
                        c.Op,
                        boolCtxA.MeshObject, boolCtxA.WorldMatrix,
                        boolCtxB.MeshObject, boolCtxB.WorldMatrix,
                        boolCtxA.WorldMatrixInverse,
                        c.Epsilon,
                        c.MergeVertices,
                        c.MergeThreshold,
                        resultName: null);

                    if (!boolResult.Success || boolResult.Mesh == null)
                    {
                        Debug.LogWarning("[PolyLing] ブーリアン失敗: " + boolResult.Message);
                        return true;
                    }

                    // 変更前スナップショット（MeshListStack Undo 用）。
                    // 追加・削除・メッシュ差し替えが混ざるため、メッシュマージと同じく
                    // リスト全体を 1 件で記録する。
                    var boolBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    MeshObject boolMesh = boolResult.Mesh;

                    if (c.CreateNewMesh)
                    {
                        var destCtx = new MeshContext
                        {
                            Name              = boolMesh.Name,
                            MeshObject        = boolMesh,
                            OriginalPositions = new Vector3[0],
                        };
                        var boolBt = new BoneTransform();
                        boolBt.CopyFrom(boolCtxA.BoneTransform);
                        destCtx.BoneTransform      = boolBt;
                        destCtx.WorldMatrix        = boolCtxA.WorldMatrix;
                        destCtx.WorldMatrixInverse = boolCtxA.WorldMatrixInverse;
                        destCtx.BindPose           = boolCtxA.BindPose;

                        var destUnityMesh       = boolMesh.ToUnityMesh();
                        destUnityMesh.name      = boolMesh.Name;
                        destUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                        destCtx.ReplaceUnityMesh(destUnityMesh);
                        destCtx.OriginalPositions = (Vector3[])boolMesh.Positions.Clone();

                        destCtx.ParentModelContext = model;
                        model.Add(destCtx);
                    }
                    else
                    {
                        // A の中身を置き換える。名前は A のものを保つ。
                        boolMesh.Name = boolCtxA.MeshObject.Name;
                        boolCtxA.MeshObject = boolMesh;
                        boolCtxA.ClearSelection();

                        var aUnityMesh       = boolMesh.ToUnityMesh();
                        aUnityMesh.name      = boolMesh.Name;
                        aUnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                        boolCtxA.ReplaceUnityMesh(aUnityMesh);
                        boolCtxA.OriginalPositions = (Vector3[])boolMesh.Positions.Clone();
                    }

                    if (c.DeleteSourceB)
                    {
                        int boolIdxB = model.IndexOf(boolCtxB);
                        if (boolIdxB >= 0) model.RemoveAt(boolIdxB);
                    }

                    model.OnListChanged?.Invoke();

                    // 変更後スナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var boolAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var boolRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = boolBefore,
                            AfterList  = boolAfter,
                        };
                        {
                            string __dbgDesc = "ブーリアン " + BooleanOps.DisplayName(c.Op);
                            PLDiag.UndoRecord("MeshList", __dbgDesc, boolRecord);
                            _undoController.MeshListStack.Record(boolRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── 差分からのモーフ生成
                case CreateMorphFromDiffCommand c:
                {
                    var morphProject = _getProject();
                    if (morphProject == null) { Fail("no project"); return true; }
                    var baseModel  = morphProject.GetModel(c.BaseModelIndex);
                    var morphModel = morphProject.GetModel(c.MorphModelIndex);
                    if (baseModel == null || morphModel == null) { Fail("基準モデルかモーフ元モデルがありません"); return true; }
                    if (c.BaseModelIndex == c.MorphModelIndex) { Fail("基準モデルとモーフ元モデルが同じです"); return true; }
                    if (baseModel.Count != morphModel.Count) { Fail("2 つのモデルのオブジェクト数が違います"); return true; }

                    // Phase 2a-2g-1: 設計 A - baseModel を CurrentModel に切り替えてから処理。
                    // これ以降 GPU は project.CurrentModel = baseModel で EnterTopologyChanged 経由で更新可能。
                    if (morphProject.CurrentModelIndex != c.BaseModelIndex)
                        morphProject.SelectModel(c.BaseModelIndex);

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
                                    baseModel, pair, mirrorParentIdx, newIdx,
                                    baseCtx.MeshObject, morphCtx.MeshObject,
                                    c.MorphName, c.Panel, expression);
                        }
                    }

                    if (morphCreated == 0) { Fail("差分のあるオブジェクトがありません"); return true; }

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
                        {
                            string __dbgDesc = $"モーフ作成: {c.MorphName}";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: 設計 A - baseModel = CurrentModel なので EnterTopologyChanged で統一。
                    _viewportManager.EnterTopologyChanged(morphProject);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── パーツ選択辞書 ─────────────────────────────────────────────
                case SavePartsSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var psMc = model.ActiveMeshContext;
                    if (psMc == null) { Fail("編集対象メッシュがありません"); return true; }
                    var psSel = psMc.Selection;
                    if (psSel == null || !psSel.HasAnySelection) { Fail("選択がありません"); return true; }
                    string psName = string.IsNullOrEmpty(c.SetName)
                        ? psMc.GenerateUniqueSelectionSetName("Selection")
                        : c.SetName;
                    if (psMc.FindSelectionSetByName(psName) != null)
                        psName = psMc.GenerateUniqueSelectionSetName(psName);
                    var psSnap = psSel.CreateSnapshot();
                    var psSet  = Poly_Ling.Selection.PartsSelectionSet.FromCurrentSelection(
                        psName, psSnap.Vertices, psSnap.Edges, psSnap.Faces, psSnap.Lines, psSnap.Mode);

                    // 索引がずれたときに引き直せるよう、作った時点で識別子を控える。
                    psSet.CaptureVertexIds(psMc.MeshObject);

                    psMc.PartsSelectionSetList.Add(psSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
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
                // モーフ実体の名前はシステムが決める（PMXImporter と同じ「{親名}_{モーフ名}」）。
                // ユーザーが管理する名前は MorphExpression.Name ひとつだけにする。
                Name       = $"{baseCtx.Name}_{morphName}",
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, baseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = parentIdx;

            // モーフは親のミラー機構に乗る（規約は MorphMirrorPolicy.cs を正典とする）
            newCtx.InheritMirrorSettingsFrom(baseCtx);

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
            return newIdx;
        }

        private static void CreateMirrorMorphMeshContextInDispatcher(
            ModelContext baseModel, MirrorPair pair, int mirrorParentIdx, int realMorphIdx,
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
                // モーフ実体の名前はシステムが決める（「{親名}_{モーフ名}」）。
                // ミラー側は親名が違うので、Real 側モーフと自然に別名になる。
                Name       = $"{mirrorBaseCtx.Name}_{morphName}",
                MeshObject = morphObj,
                IsVisible  = false,
            };
            newCtx.SetAsMorph(morphName, mirrorBaseCtx.MeshObject);
            newCtx.MorphBaseData.Panel = panel;
            newCtx.MorphParentIndex   = mirrorParentIdx;

            // モーフは親のミラー機構に乗る（規約は MorphMirrorPolicy.cs を正典とする）
            newCtx.InheritMirrorSettingsFrom(mirrorBaseCtx);

            // 親が生成ミラー（実体側から作られた形状）なら、モーフ側にも同じ連結を張る。
            // これで既存の MirrorBranchOps.RebakeDerivedMirrorVertices が
            // Real 側モーフ → Mirror 側モーフ の追随を担当できる。
            if (mirrorBaseCtx.MirrorGeometryDerived && realMorphIdx >= 0)
            {
                newCtx.MirrorGeometryDerived  = true;
                newCtx.BakedMirrorSourceIndex = realMorphIdx;
            }

            int newIdx = baseModel.Add(newCtx);
            expression.AddMesh(newIdx);
        }
    }
}
