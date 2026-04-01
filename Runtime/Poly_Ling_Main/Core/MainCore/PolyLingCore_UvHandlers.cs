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

            bool firstDone = false;
            MeshObjectSnapshot before = null;

            foreach (int masterIdx in cmd.MasterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                var meshObj = ctx?.MeshObject;
                if (meshObj == null) continue;

                if (!firstDone)
                {
                    before = undoController?.CaptureMeshObjectSnapshot();
                    firstDone = true;
                }

                UvUnwrapOps.UnwrapMesh(meshObj, cmd.Projection, cmd.Scale, cmd.OffsetU, cmd.OffsetV);
            }

            toolContext?.SyncMesh?.Invoke();

            if (undoController != null && before != null)
            {
                var after = undoController.CaptureMeshObjectSnapshot();
                undoController.RecordTopologyChange(before, after,
                    $"UV Unwrap ({cmd.Projection})");
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

            var before = undoController?.CaptureMeshObjectSnapshot();

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
                var after = undoController.CaptureMeshObjectSnapshot();
                undoController.RecordTopologyChange(before, after, "XYZ→UV書き戻し");
            }

            toolContext?.SyncMesh?.Invoke();
            repaint?.Invoke();
        }
    }
}
