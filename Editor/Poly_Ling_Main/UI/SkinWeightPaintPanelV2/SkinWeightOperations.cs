// SkinWeightOperations.cs
// Flood/Normalize/Prune の実処理
// UnityEditor非依存 → Runtime/に移行可能

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class SkinWeightOperations
    {
        public static void ExecuteFlood(
            ModelContext model, ToolContext toolCtx,
            int targetBoneMasterIndex, SkinWeightPaintMode paintMode,
            float weightValue, float brushStrength,
            System.Action<string> onError = null)
        {
            if (model == null || targetBoneMasterIndex < 0) return;
            var meshCtx = model.FirstDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            { onError?.Invoke("頂点が選択されていません。"); return; }

            var undo   = toolCtx?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();
            var mo     = meshCtx.MeshObject;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                BoneWeight bw = vertex.BoneWeight ?? default;
                switch (paintMode)
                {
                    case SkinWeightPaintMode.Replace: bw = SkinWeightOps.SetBoneWeight(bw, targetBoneMasterIndex, weightValue); break;
                    case SkinWeightPaintMode.Add:     bw = SkinWeightOps.AddBoneWeight(bw, targetBoneMasterIndex, weightValue * brushStrength); break;
                    case SkinWeightPaintMode.Scale:   bw = SkinWeightOps.ScaleBoneWeight(bw, targetBoneMasterIndex, weightValue); break;
                    case SkinWeightPaintMode.Smooth:  continue;
                }
                vertex.BoneWeight = SkinWeightOps.NormalizeBoneWeight(bw);
            }

            RecordAndSync(undo, before, toolCtx, "Flood Skin Weight");
        }

        public static void ExecuteNormalize(
            ModelContext model, ToolContext toolCtx,
            System.Action<string> onError = null)
        {
            if (model == null) return;
            var meshCtx = model.FirstDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            { onError?.Invoke("頂点が選択されていません。"); return; }

            var undo   = toolCtx?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();
            var mo     = meshCtx.MeshObject;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;
                vertex.BoneWeight = SkinWeightOps.NormalizeBoneWeight(vertex.BoneWeight.Value);
            }

            RecordAndSync(undo, before, toolCtx, "Normalize Skin Weights");
        }

        public static int ExecutePrune(
            ModelContext model, ToolContext toolCtx,
            float pruneThreshold,
            System.Action<string> onError = null)
        {
            if (model == null) return 0;
            var meshCtx = model.FirstDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return 0;

            var selectedVerts = meshCtx.SelectedVertices;
            if (selectedVerts == null || selectedVerts.Count == 0)
            { onError?.Invoke("頂点が選択されていません。"); return 0; }

            var undo   = toolCtx?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();
            var mo     = meshCtx.MeshObject;
            int prunedCount = 0;

            foreach (int vi in selectedVerts)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;
                var bw = vertex.BoneWeight.Value;
                bool changed = false;
                if (bw.weight0 > 0f && bw.weight0 < pruneThreshold) { bw.weight0 = 0f; bw.boneIndex0 = 0; changed = true; }
                if (bw.weight1 > 0f && bw.weight1 < pruneThreshold) { bw.weight1 = 0f; bw.boneIndex1 = 0; changed = true; }
                if (bw.weight2 > 0f && bw.weight2 < pruneThreshold) { bw.weight2 = 0f; bw.boneIndex2 = 0; changed = true; }
                if (bw.weight3 > 0f && bw.weight3 < pruneThreshold) { bw.weight3 = 0f; bw.boneIndex3 = 0; changed = true; }
                if (changed)
                {
                    bw = SkinWeightOps.NormalizeBoneWeight(bw);
                    bw = SkinWeightOps.SortBoneWeight(bw);
                    vertex.BoneWeight = bw;
                    prunedCount++;
                }
            }

            RecordAndSync(undo, before, toolCtx, "Prune Skin Weights");
            return prunedCount;
        }

        private static void RecordAndSync(MeshUndoController undo, MeshObjectSnapshot before,
            ToolContext toolCtx, string description)
        {
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                toolCtx?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(undo, before, after, description));
            }
            toolCtx?.SyncMesh?.Invoke();
            toolCtx?.Repaint?.Invoke();
        }
    }
}
