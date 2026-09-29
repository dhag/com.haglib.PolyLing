// PlayerCommandDispatcher.Boolean2D.cs
// コマンドディスパッチャ：2D ブーリアン（PanelCommand.Boolean2D.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 実処理は Boolean2DOps.PerformMeshes。追加・削除・差し替えが混ざるので、
// 3D の booleanMesh と同じくリスト全体を 1 件で Undo に記録する。

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>DispatchCore の分担：2D ブーリアン。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchBoolean2D(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            if (!(cmd is BooleanMesh2DCommand c)) return false;

            if (model == null) { Fail("no current model"); return true; }

            var ctxA = model.GetMeshContext(c.AMasterIndex);
            var ctxB = model.GetMeshContext(c.BMasterIndex);
            if (ctxA?.MeshObject == null || ctxB?.MeshObject == null) { Fail("対象メッシュが 2 つ揃っていません"); return true; }
            if (ReferenceEquals(ctxA, ctxB)) { Fail("同じオブジェクト同士では計算できません"); return true; }

            // 演算は A のローカル空間。B の点は B → ワールド → A で写す。
            Matrix4x4 bToA = ctxA.WorldMatrixInverse * ctxB.WorldMatrix;
            var r = Boolean2DOps.PerformMeshes(c.Op, c.Source, ctxA.MeshObject, ctxB.MeshObject, bToA,
                                               c.PlaneTolerance, resultName: null);
            if (!r.Success || r.Mesh == null) { Fail("2D ブーリアン失敗: " + r.Message); return true; }

            var before = MeshFilterToSkinnedRecord.CaptureList(model);

            MeshObject mesh = r.Mesh;
            MeshContext outCtx;
            if (c.CreateNewMesh)
            {
                var dest = new MeshContext
                {
                    Name              = mesh.Name,
                    MeshObject        = mesh,
                    OriginalPositions = new Vector3[0],
                };
                var bt = new BoneTransform();
                bt.CopyFrom(ctxA.BoneTransform);
                dest.BoneTransform      = bt;
                dest.WorldMatrix        = ctxA.WorldMatrix;
                dest.WorldMatrixInverse = ctxA.WorldMatrixInverse;
                dest.BindPose           = ctxA.BindPose;

                var um = mesh.ToUnityMesh();
                um.name      = mesh.Name;
                um.hideFlags = HideFlags.HideAndDontSave;
                dest.ReplaceUnityMesh(um);
                dest.OriginalPositions = (Vector3[])mesh.Positions.Clone();

                dest.ParentModelContext = model;
                model.Add(dest);
                outCtx = dest;
            }
            else
            {
                // A の中身を置き換える。名前は A のものを保つ。
                mesh.Name = ctxA.MeshObject.Name;
                ctxA.MeshObject = mesh;
                ctxA.ClearSelection();
                outCtx = ctxA;

                var um = mesh.ToUnityMesh();
                um.name      = mesh.Name;
                um.hideFlags = HideFlags.HideAndDontSave;
                ctxA.ReplaceUnityMesh(um);
                ctxA.OriginalPositions = (Vector3[])mesh.Positions.Clone();
            }

            if (c.DeleteSourceB)
            {
                int idxB = model.IndexOf(ctxB);
                if (idxB >= 0) model.RemoveAt(idxB);
            }

            model.OnListChanged?.Invoke();

            if (_undoController != null)
            {
                var rec = new MeshFilterToSkinnedRecord
                {
                    BeforeList = before,
                    AfterList  = MeshFilterToSkinnedRecord.CaptureList(model),
                };
                string desc = "2D ブーリアン " + Boolean2DOps.DisplayName(c.Op);
                PLDiag.UndoRecord("MeshList", desc, rec);
                _undoController.MeshListStack.Record(rec, desc);
                _undoController.FocusMeshList();
            }

            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            // B を消すと索引がずれるので、ここで引き直す。
            int outIdx = model.IndexOf(outCtx);
            ReportData(CommandDataJson.New()
                .Int("vertices", mesh.VertexCount)
                .Int("faces",    mesh.FaceCount)
                .Int("loops",    r.LoopCount)
                .Int("holes",    r.HoleCount)
                .Build(),
                new[] { outIdx }, new[] { outCtx.ObjectId });
            return true;
        }
    }
}
