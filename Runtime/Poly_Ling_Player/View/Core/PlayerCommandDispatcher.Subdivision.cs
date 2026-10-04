// PlayerCommandDispatcher.Subdivision.cs
// コマンドディスパッチャ：サブディビジョン（作成・作り直し・解除）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【Undo】
//   作成・作り直し・解除のどれも、オブジェクト一覧のスナップショットと
//   グループの変更を 1 件にまとめて積む（辺から帯面と同じ形）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：サブディビジョン。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchSubdivision(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case SubdivideMeshCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSubdivide(project, model, c);
                    if (reason != null) { Fail(reason); return true; }
                    return true;
                }

                case ReleaseSubdivisionCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteReleaseSubdivision(project, model, c);
                    if (reason != null) { Fail(reason); return true; }
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // 作成・作り直し
        // ================================================================

        private string ExecuteSubdivide(ProjectContext project, ModelContext model, SubdivideMeshCommand c)
        {
            var cage = model.GetMeshContext(c.CageMasterIndex);
            if (cage?.MeshObject == null) return $"親のメッシュが見つかりません (masterIndex={c.CageMasterIndex})";
            if (cage.Type != MeshType.Mesh) return "親は通常のメッシュでなければなりません";
            if (cage.IsSkinned) return "スキンド化したメッシュは親にできません";
            if (SubdivisionSync.FindByChild(model, cage) != null)
                return "サブディビジョンの子は親にできません。親の側を指定してください";

            int level = Mathf.Clamp(c.Level, SubdivisionOps.LevelMin, SubdivisionOps.LevelMax);

            if (!SubdivisionOps.TryBuild(cage.MeshObject, level, out var result, out var stencil, out string err))
                return err;

            // 作り直しの出力先。指定が無くても、既にこの親の子があればそこへ書く
            // （同じ親に子を 2 つ作らない）。
            MeshContext child = c.TargetMasterIndex >= 0 ? model.GetMeshContext(c.TargetMasterIndex) : null;
            var existing = SubdivisionSync.FindByCage(model, cage);
            if (child == null && existing != null) child = existing.Child;
            if (c.TargetMasterIndex >= 0 && child?.MeshObject == null)
                return $"作り直す子が見つかりません (masterIndex={c.TargetMasterIndex})";

            _undoController?.SetModelContext(model);
            var listBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ObjectGroupCount;
            var groupChanges = new List<ObjectGroupChangeRecord>();

            int childIndex;
            _undoController?.SuspendRecording();
            try
            {
                if (child != null)
                {
                    // ── 作り直し：中身だけ入れ替える。名前・ObjectId・階層・ロックは保つ。
                    SubdivisionOps.ReplaceContents(child.MeshObject, result);
                    childIndex = model.MeshContextList.IndexOf(child);

                    // 回数が変わったらグループの項目へ書く（つながりの正本はグループ）。
                    var link = SubdivisionSync.FindByChild(model, child);
                    if (link != null && link.Level != level)
                    {
                        int gi = model.ObjectGroups.IndexOf(link.Group);
                        var before = link.Group.Clone();
                        link.Step.SetArg(SubdivisionSync.LevelKey,
                            level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        if (gi >= 0)
                            groupChanges.Add(new ObjectGroupChangeRecord
                            {
                                ReplacedIndex = gi, OldGroup = before, NewGroup = link.Group.Clone(),
                            });
                    }
                }
                else
                {
                    // ── 新しく作る：親の子に置き、ローカルは単位（親の姿勢をそのまま受ける）。
                    string name = model.GenerateUniqueMeshName(cage.Name + "_subdiv");
                    result.Name = name;

                    var unityMesh = result.ToUnityMesh(model.MaterialCount);
                    unityMesh.name      = name;
                    unityMesh.hideFlags = HideFlags.HideAndDontSave;

                    child = new MeshContext
                    {
                        MeshObject = result,
                        Name       = name,
                        UnityMesh  = unityMesh,
                        IsVisible  = true,
                        IsLocked   = true,
                    };
                    child.ParentModelContext   = model;
                    child.HierarchyParentIndex = c.CageMasterIndex;
                    child.Depth                = cage.Depth + 1;

                    childIndex = model.Add(child);

                    // つながりをグループとして残す。出力先は作った子だけ。
                    var recorded = new SubdivideMeshCommand(c.ModelIndex, c.CageMasterIndex, level, -1);
                    CaptureObjectGroup(recorded, null, -1, new[] { child.ObjectId });
                }

                // 直後の RebuildAdapter で作り直さないよう、計算結果を控える。
                SubdivisionSync.Prime(child, cage, level, stencil);
                cage.HideFaceShading = true;
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            RecordSubdivisionUndo(model, listBefore, groupsBefore, groupChanges, "サブディビジョン");

            model.ComputeWorldMatrices();
            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            childIndex = model.MeshContextList.IndexOf(child);
            ReportTargets(new[] { childIndex }, child.ObjectId != 0UL ? new[] { child.ObjectId } : null);
            return null;
        }

        // ================================================================
        // 解除
        // ================================================================

        private string ExecuteReleaseSubdivision(ProjectContext project, ModelContext model, ReleaseSubdivisionCommand c)
        {
            var target = model.GetMeshContext(c.MasterIndex);
            if (target == null) return $"描画オブジェクトが見つかりません (masterIndex={c.MasterIndex})";

            var link = SubdivisionSync.FindByCage(model, target) ?? SubdivisionSync.FindByChild(model, target);
            if (link == null) return "サブディビジョンの親でも子でもありません";

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var groupChanges = new List<ObjectGroupChangeRecord>();

            _undoController?.SuspendRecording();
            try
            {
                // つながりの項目を外す。項目が 1 つだけのグループはグループごと外す。
                int gi = model.ObjectGroups.IndexOf(link.Group);
                if (gi >= 0)
                {
                    if (link.Group.StepCount <= 1)
                    {
                        groupChanges.Add(new ObjectGroupChangeRecord
                        {
                            RemovedGroup = link.Group.Clone(), RemovedIndex = gi,
                        });
                        model.RemoveObjectGroup(link.Group);
                    }
                    else
                    {
                        var before = link.Group.Clone();
                        link.Group.Steps.Remove(link.Step);
                        groupChanges.Add(new ObjectGroupChangeRecord
                        {
                            ReplacedIndex = gi, OldGroup = before, NewGroup = link.Group.Clone(),
                        });
                    }
                }

                SubdivisionSync.Forget(link.Child);
                link.Cage.HideFaceShading = false;

                if (c.DeleteChild)
                {
                    int ci = model.MeshContextList.IndexOf(link.Child);
                    if (ci >= 0) model.RemoveAt(ci);
                }
                else
                {
                    link.Child.IsLocked = false;
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount, groupChanges, "サブディビジョン解除");

            model.ComputeWorldMatrices();
            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            int cageIndex = model.MeshContextList.IndexOf(link.Cage);
            ReportTargets(new[] { cageIndex }, link.Cage.ObjectId != 0UL ? new[] { link.Cage.ObjectId } : null);
            return null;
        }

        /// <summary>
        /// オブジェクト一覧のスナップショットと、グループの追加・差し替え・削除を Undo 1 件として積む。
        /// グループの記録は「差し替え・削除」→「追加」の順に並べる。
        /// </summary>
        private void RecordSubdivisionUndo(
            ModelContext model, List<MeshContext> listBefore, int groupsBefore,
            List<ObjectGroupChangeRecord> groupChanges, string description)
        {
            if (_undoController == null || model == null) return;

            _undoController.MeshListStack.BeginGroup(description);

            RecordMeshListSnapshot(listBefore, model, description);

            if (groupChanges != null)
                foreach (var rec in groupChanges)
                    RecordObjectGroupUndo(rec, description);

            for (int gi = groupsBefore; gi < model.ObjectGroupCount; gi++)
            {
                var added = model.ObjectGroups[gi];
                if (added == null) continue;
                RecordObjectGroupUndo(
                    new ObjectGroupChangeRecord { AddedGroup = added.Clone(), AddedIndex = gi },
                    $"オブジェクトグループ追加: {added.Name}");
            }

            _undoController.MeshListStack.EndGroup();
        }
    }
}
