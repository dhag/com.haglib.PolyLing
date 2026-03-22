// BlendOperation.cs
// SimpleBlend 確定処理（バックアップ作成 + ブレンド適用 + Undo記録）
// UnityEditor非依存 → Runtime/移行可能

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class BlendOperation
    {
        public static int ApplyAndCreateBackups(
            ModelContext model,
            BlendPreviewState preview,
            List<int> targetIndices,
            int sourceIndex,
            float blendWeight,
            bool recalculateNormals,
            bool selectedVertsOnly,
            HashSet<int> selectedVerts,
            bool matchByVertexId,
            ToolContext toolCtx)
        {
            if (!preview.IsActive) return 0;

            var srcCtx = model.GetMeshContext(sourceIndex);
            if (srcCtx?.MeshObject == null) return 0;
            var srcMo = srcCtx.MeshObject;

            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) existingNames.Add(mc.Name);
            }

            Dictionary<int, int> srcIdMap = matchByVertexId ? BlendPreviewState.BuildVertexIdMap(srcMo) : null;
            var verts   = selectedVertsOnly ? selectedVerts : null;
            var undo    = toolCtx?.UndoController;
            var before  = undo?.CaptureMeshObjectSnapshot();
            int backupCount = 0;

            foreach (int idx in targetIndices)
            {
                if (!preview.Backups.TryGetValue(idx, out var backup)) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo = ctx.MeshObject;

                // バックアップメッシュ作成
                var backupMo   = mo.Clone();
                for (int i = 0; i < backup.Length && i < backupMo.VertexCount; i++)
                    backupMo.Vertices[i].Position = backup[i];

                string backupName = GenerateUniqueName(ctx.Name + "_backup", existingNames);
                backupMo.Name = backupName;

                var backupCtx = new MeshContext
                {
                    MeshObject = backupMo,
                    Name       = backupName,
                    Type       = ctx.Type,
                    IsVisible  = false,
                };
                backupCtx.UnityMesh = backupMo.ToUnityMeshShared();
                if (backupCtx.UnityMesh != null)
                    backupCtx.UnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

                model.Add(backupCtx);
                existingNames.Add(backupName);
                backupCount++;

                // ブレンド確定
                var nonIsolated = BlendPreviewState.BuildNonIsolatedSet(mo);
                BlendPreviewState.BlendVertices(mo, backup, srcMo, blendWeight, nonIsolated, verts, srcIdMap);

                if (recalculateNormals) mo.RecalculateSmoothNormals();

                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                SyncMirrorSide(model, ctx, toolCtx);
            }

            // 可視状態復元（ターゲット以外）
            foreach (var (idx, visible) in preview.SavedVisibility)
            {
                if (targetIndices.Contains(idx)) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx != null) ctx.IsVisible = visible;
            }

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                toolCtx?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Simple Blend"));
            }

            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return backupCount;
        }

        public static void SyncMirrorSide(ModelContext model, MeshContext ctx, ToolContext toolCtx)
        {
            if (ctx?.MeshObject == null) return;

            string mirrorName = ctx.Name + "+";
            var    axis       = ctx.GetMirrorSymmetryAxis();
            var    mo         = ctx.MeshObject;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                if (mc.Name != mirrorName) continue;
                if (mc.MeshObject == null || mc.MeshObject.VertexCount != mo.VertexCount) continue;

                var mirrorMo = mc.MeshObject;
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var pos = mo.Vertices[v].Position;
                    mirrorMo.Vertices[v].Position = axis switch
                    {
                        SymmetryAxis.Y => new Vector3( pos.x, -pos.y,  pos.z),
                        SymmetryAxis.Z => new Vector3( pos.x,  pos.y, -pos.z),
                        _              => new Vector3(-pos.x,  pos.y,  pos.z),
                    };
                }
                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(mc);
                break;
            }
        }

        private static string GenerateUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName)) return baseName;
            for (int n = 1; n < 10000; n++)
            {
                string name = $"{baseName}_{n}";
                if (!existingNames.Contains(name)) return name;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
