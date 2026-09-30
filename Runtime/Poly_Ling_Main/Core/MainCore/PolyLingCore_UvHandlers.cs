// PolyLingCore_UvHandlers.cs
// UV操作CommandHandlers
// UvUnwrapOps (internal/Poly_Ling.Data) と同一名前空間に配置することで参照可能にする
// OnRepaintRequired は Action デリゲートとして受け取る

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Commands;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Core
{
    public static class PolyLingCoreUvHandlers
    {
        public static void HandleApplyUvUnwrap(
            ModelContext model,
            MeshUndoController undoController,
            Poly_Ling.Tools.ToolContext toolContext,
            System.Action repaint,
            ApplyUvUnwrapCommand cmd)
        {
            if (model == null) return;
            ApplyToMeshes(model, undoController, toolContext, repaint, cmd.MasterIndices,
                (_, mo) => UvUnwrapOps.UnwrapMesh(mo, cmd.Projection, cmd.Scale, cmd.OffsetU, cmd.OffsetV),
                $"UV Unwrap ({cmd.Projection})");
        }

        /// <summary>
        /// 頂点ごとの UV（masterIndex → uvs[頂点番号]）を書く。ビューからの投影で使う
        /// （UV は Player 側が画面への投影から求める）。
        /// </summary>
        public static void HandleApplyVertexUvs(
            ModelContext model,
            MeshUndoController undoController,
            Poly_Ling.Tools.ToolContext toolContext,
            System.Action repaint,
            IReadOnlyDictionary<int, Vector2[]> uvsOf,
            string description)
        {
            if (model == null || uvsOf == null) return;
            var indices = new List<int>(uvsOf.Keys);
            ApplyToMeshes(model, undoController, toolContext, repaint, indices,
                (idx, mo) => UvUnwrapOps.ApplyVertexUVs(mo, uvsOf[idx]),
                description);
        }

        /// <summary>
        /// 対象の全メッシュへ write を掛け、全メッシュぶんを 1 つの Undo に記録する。
        /// 以前は先頭メッシュしか記録していなかった（MeshObjectSnapshot は 1 メッシュ分）。
        /// </summary>
        private static void ApplyToMeshes(
            ModelContext model,
            MeshUndoController undoController,
            Poly_Ling.Tools.ToolContext toolContext,
            System.Action repaint,
            IEnumerable<int> masterIndices,
            System.Action<int, MeshObject> write,
            string description)
        {
            var targets = new List<int>();
            foreach (int masterIdx in masterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                if (ctx?.MeshObject == null || targets.Contains(masterIdx)) continue;
                targets.Add(masterIdx);
            }
            if (targets.Count == 0) return;

            var before = new MultiMeshTopologySnapshot();
            foreach (int idx in targets) before.CaptureMesh(model, idx);

            foreach (int idx in targets) write(idx, model.GetMeshContext(idx).MeshObject);

            toolContext?.SyncMesh?.Invoke();

            if (undoController != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in targets) after.CaptureMesh(model, idx);
                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undoController.SetModelContext(model);
                var record = new MultiMeshTopologySnapshotRecord(before, after, description);
                undoController.MeshListStack.Record(record, description);
                undoController.FocusMeshList();
            }

            repaint?.Invoke();
        }

        public static void HandleUvToXyz(
            ModelContext model,
            MeshUndoController undoController,
            Poly_Ling.Tools.ToolContext toolContext,
            System.Action<MeshContext> addMeshContext,
            System.Action repaint,
            UvToXyzCommand cmd)
        {
            if (model == null) return;

            var srcCtx = model.GetMeshContext(cmd.MasterIndex);
            var srcMeshObj = srcCtx?.MeshObject;
            if (srcMeshObj == null || srcMeshObj.VertexCount == 0) return;

            var newMeshObj = UvUnwrapOps.BuildUvzMesh(
                srcMeshObj, cmd.UvScale, cmd.DepthScale, cmd.CameraPosition, cmd.CameraForward);

            var newCtx = new MeshContext
            {
                MeshObject        = newMeshObj,
                UnityMesh         = newMeshObj.ToUnityMesh(),
                OriginalPositions = newMeshObj.Positions.Clone() as UnityEngine.Vector3[],
            };

            addMeshContext?.Invoke(newCtx);
            toolContext?.SyncMesh?.Invoke();
            repaint?.Invoke();
        }

        public static void HandleXyzToUv(
            ModelContext model,
            MeshUndoController undoController,
            Poly_Ling.Tools.ToolContext toolContext,
            System.Action repaint,
            XyzToUvCommand cmd)
        {
            if (model == null) return;

            var srcCtx    = model.GetMeshContext(cmd.SourceMasterIndex);
            var targetCtx = model.GetMeshContext(cmd.TargetMasterIndex);
            var srcMeshObj    = srcCtx?.MeshObject;
            var targetMeshObj = targetCtx?.MeshObject;
            if (srcMeshObj == null || targetMeshObj == null) return;

            var before = undoController?.CaptureMeshObjectSnapshotOf(targetCtx);

            UvUnwrapOps.WritebackXyzToUv(srcMeshObj, targetMeshObj, cmd.UvScale);

            if (targetCtx.UnityMesh != null)
            {
                var rebuilt = targetMeshObj.ToUnityMesh();
                targetCtx.UnityMesh.Clear();
                targetCtx.UnityMesh.vertices     = rebuilt.vertices;
                targetCtx.UnityMesh.normals      = rebuilt.normals;
                targetCtx.UnityMesh.uv           = rebuilt.uv;
                targetCtx.UnityMesh.subMeshCount = rebuilt.subMeshCount;
                for (int s = 0; s < rebuilt.subMeshCount; s++)
                    targetCtx.UnityMesh.SetTriangles(rebuilt.GetTriangles(s), s);
                targetCtx.UnityMesh.RecalculateBounds();
            }

            if (undoController != null && before != null)
            {
                var after = undoController.CaptureMeshObjectSnapshotOf(targetCtx);
                undoController.RecordTopologyChange(before, after, "XYZ→UV書き戻し");
            }

            toolContext?.SyncMesh?.Invoke();
            repaint?.Invoke();
        }
    }
}
