// LscmUnwrapOperation.cs
// LSCM UV展開の実処理（SeamSplit → Solve → Apply）
// UnityEditor非依存 → Runtime/Core/Ops/に配置

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.UI.Lscm
{
    public static class LscmUnwrapOperation
    {
        public struct Result
        {
            public bool   Success;
            public string StatusMessage;
        }

        public static Result Execute(
            MeshObject meshObj,
            HashSet<VertexPair> seamEdges,
            bool includeBoundary,
            int maxIterations)
        {
            if (meshObj == null)
                return new Result { Success = false, StatusMessage = "メッシュデータがありません" };

            if (meshObj.FaceCount == 0 || meshObj.VertexCount < 3)
                return new Result { Success = false, StatusMessage = "メッシュが空または不十分です" };

            var sw = Stopwatch.StartNew();

            var split = SeamSplitter.Build(meshObj, seamEdges, includeBoundary);

            if (split.VertexCount == 0 || split.TriangleCount == 0)
                return new Result { Success = false, StatusMessage = "分割結果が空です" };

            var lscmResult = LscmSolver.Solve(split, maxIterations);

            if (!lscmResult.Success)
                return new Result { Success = false, StatusMessage = $"LSCM失敗: {lscmResult.Error}" };

            LscmUvWriter.Apply(meshObj, split, lscmResult);

            sw.Stop();
            return new Result
            {
                Success = true,
                StatusMessage = $"完了 ({sw.ElapsedMilliseconds}ms)  " +
                                $"UV頂点:{split.VertexCount} Tri:{split.TriangleCount} " +
                                $"島:{lscmResult.IslandCount}"
            };
        }
    }
}
