// QuadDecimatorOperation.cs
// Quad保持減数化の実処理と結果のMeshContext追加
// UnityEditor非依存 → Runtime/Core/Ops/に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;

using Poly_Ling.UI.QuadDecimator;

namespace Poly_Ling.Tools.Panels.QuadDecimator
{
    public static class QuadDecimatorOperation
    {
        public static DecimatorResult Execute(
            MeshContext sourceMeshContext,
            DecimatorParams prms,
            ToolContext toolCtx)
        {
            var sourceMeshObj = sourceMeshContext?.MeshObject;
            if (sourceMeshObj == null) return null;

            var result = QuadPreservingDecimator.Decimate(sourceMeshObj, prms, out MeshObject resultMesh);
            resultMesh.Name = sourceMeshObj.Name + "_decimated";

            var newMeshContext = new MeshContext
            {
                Name       = resultMesh.Name,
                MeshObject = resultMesh,
                Materials  = new List<UnityEngine.Material>(sourceMeshContext.Materials ?? new List<UnityEngine.Material>()),
            };

            newMeshContext.UnityMesh           = resultMesh.ToUnityMesh();
            newMeshContext.UnityMesh.name       = resultMesh.Name;
            newMeshContext.UnityMesh.hideFlags  = UnityEngine.HideFlags.HideAndDontSave;

            toolCtx?.AddMeshContext?.Invoke(newMeshContext);
            toolCtx?.Repaint?.Invoke();

            return result;
        }
    }
}
