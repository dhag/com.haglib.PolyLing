// ReferenceSymmetryOperation.cs
// 対称な参照メッシュの頂点対応を使い、別メッシュの正 X 側を負 X 側へ移植する。
// 臨時機能のため既存のミラー系状態には触れず、新規 MeshContext の追加だけを行う。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public sealed class ReferenceSymmetryResult
    {
        public bool Success;
        public string Message;
        public int NewMasterIndex = -1;
        public int TransferredVertexCount;

        public static ReferenceSymmetryResult Fail(string message)
            => new ReferenceSymmetryResult { Success = false, Message = message };
    }

    public static class ReferenceSymmetryOperation
    {
        /// <summary>
        /// reference の移植元側（sourcePositiveX=true なら正 X、false なら負 X）の頂点を X 反転した位置から、
        /// reference の反対側の頂点を探す。対応する target の移植元側の頂点を反転し、
        /// target のクローンの反対側の頂点へ書く。
        /// |X| が許容誤差以下の頂点（中心線）と移植元側はクローン元のまま変更しない。
        /// </summary>
        public static ReferenceSymmetryResult ApplyAsNewObject(
            ModelContext model,
            int referenceMasterIndex,
            int targetMasterIndex,
            float tolerance,
            bool recalculateNormals,
            ToolContext toolCtx,
            string newObjectName = null,
            bool sourcePositiveX = true)
        {
            if (model == null) return ReferenceSymmetryResult.Fail("モデルがありません");
            if (referenceMasterIndex == targetMasterIndex)
                return ReferenceSymmetryResult.Fail("REF と対象には別のオブジェクトを指定してください");

            var reference = model.GetMeshContext(referenceMasterIndex);
            var target    = model.GetMeshContext(targetMasterIndex);
            var refMo     = reference?.MeshObject;
            var targetMo  = target?.MeshObject;
            if (refMo == null)    return ReferenceSymmetryResult.Fail("REF メッシュが見つかりません");
            if (targetMo == null) return ReferenceSymmetryResult.Fail("対象メッシュが見つかりません");
            if (refMo.VertexCount != targetMo.VertexCount)
                return ReferenceSymmetryResult.Fail(
                    $"頂点数が一致しません（REF {refMo.VertexCount} / 対象 {targetMo.VertexCount}）");

            tolerance = Mathf.Max(1e-7f, tolerance);
            if (!TryBuildPairs(refMo, tolerance, sourcePositiveX, out var pairs, out string pairError))
                return ReferenceSymmetryResult.Fail(pairError);

            // 名前の指定が無ければ「対象名_対称」。既存と重複すれば末尾に番号を付ける。
            string baseName = string.IsNullOrWhiteSpace(newObjectName) ? target.Name + "_対称" : newObjectName.Trim();
            string newName = GenerateUniqueName(model, baseName);
            var clone = BlendOperation.CloneContext(target, newName);
            if (clone?.MeshObject == null)
                return ReferenceSymmetryResult.Fail("対象のクローンを作成できませんでした");

            // 検証がすべて済んでからクローンだけを書き換える。
            // sourceIndex は REF と対象で共通する頂点インデックス、destIndex は
            // REF の幾何形状から得た反対側の対応インデックスである。
            // 書き込み先は移植元の反対側なので、X の符号も反対側にそろえる。
            float destSign = sourcePositiveX ? -1f : 1f;
            foreach (var pair in pairs)
            {
                Vector3 source = targetMo.Vertices[pair.SourceIndex].Position;
                clone.MeshObject.Vertices[pair.DestIndex].Position =
                    new Vector3(destSign * Mathf.Abs(source.x), source.y, source.z);
            }

            if (recalculateNormals) clone.MeshObject.RecalculateSmoothNormals();
            clone.MirrorGeometryDerived = false;
            clone.IsVisible = true;

            var undo = toolCtx?.UndoController;
            var oldSelected = model.CaptureAllSelectedIndices();
            int newIndex = model.Add(clone);
            var newSelected = model.CaptureAllSelectedIndices();

            undo?.RecordMeshContextsAdd(
                new List<(int Index, MeshContext MeshContext)> { (newIndex, clone) },
                oldSelected, newSelected);

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(clone);
            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return new ReferenceSymmetryResult
            {
                Success = true,
                Message = $"{clone.Name} を作成しました（移植 {pairs.Count} 頂点）",
                NewMasterIndex = newIndex,
                TransferredVertexCount = pairs.Count,
            };
        }

        private readonly struct VertexPair
        {
            public readonly int SourceIndex;
            public readonly int DestIndex;
            public VertexPair(int sourceIndex, int destIndex)
            {
                SourceIndex = sourceIndex;
                DestIndex   = destIndex;
            }
        }

        private static bool TryBuildPairs(
            MeshObject reference, float tolerance, bool sourcePositiveX,
            out List<VertexPair> pairs, out string error)
        {
            pairs = new List<VertexPair>();
            error = null;
            float toleranceSq = tolerance * tolerance;
            var usedDest = new HashSet<int>();

            // 中心線（|X| が許容誤差以下）は移植元にも書き込み先にもしない（X=0 は接続頂点で操作不要）。
            // 厳密な X=0 だけを中心線とすると、数値誤差で X がわずかにずれた中心線の頂点が
            // 移植元に入り、その対称点（中心線の向こう側）が見つからず全体が失敗する。
            bool IsSource(float x) => sourcePositiveX ? x >  tolerance : x < -tolerance;
            bool IsDest(float x)   => sourcePositiveX ? x < 0f        : x > 0f;

            // 書き込み先側を許容誤差サイズのセルへ入れる。全頂点総当たりにすると
            // 頭部メッシュで O(n^2) になるため、反転位置の隣接 27 セルだけを見る。
            var destCells = new Dictionary<Vector3Int, List<int>>();
            for (int j = 0; j < reference.VertexCount; j++)
            {
                Vector3 candidate = reference.Vertices[j].Position;
                if (!IsDest(candidate.x)) continue;
                Vector3Int cell = CellOf(candidate, tolerance);
                if (!destCells.TryGetValue(cell, out var indices))
                {
                    indices = new List<int>();
                    destCells.Add(cell, indices);
                }
                indices.Add(j);
            }

            for (int i = 0; i < reference.VertexCount; i++)
            {
                Vector3 p = reference.Vertices[i].Position;
                if (!IsSource(p.x)) continue;

                Vector3 mirrored = new Vector3(-p.x, p.y, p.z);
                int bestIndex = -1;
                float bestSq = float.PositiveInfinity;
                Vector3Int center = CellOf(mirrored, tolerance);
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var cell = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (!destCells.TryGetValue(cell, out var candidates)) continue;
                    foreach (int j in candidates)
                    {
                        float sq = (reference.Vertices[j].Position - mirrored).sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            bestIndex = j;
                        }
                    }
                }

                if (bestIndex < 0 || bestSq > toleranceSq)
                {
                    error = $"REF 頂点 {i}（X={p.x:G6}）の対称点が許容誤差 {tolerance:G6} 内にありません";
                    return false;
                }
                if (!usedDest.Add(bestIndex))
                {
                    error = $"REF の複数頂点が同じ対称点（頂点 {bestIndex}）に対応しました";
                    return false;
                }
                pairs.Add(new VertexPair(i, bestIndex));
            }

            if (pairs.Count == 0)
            {
                error = sourcePositiveX ? "REF に正 X 側の頂点がありません" : "REF に負 X 側の頂点がありません";
                return false;
            }
            return true;
        }

        private static Vector3Int CellOf(Vector3 p, float cellSize)
            => new Vector3Int(
                Mathf.FloorToInt(p.x / cellSize),
                Mathf.FloorToInt(p.y / cellSize),
                Mathf.FloorToInt(p.z / cellSize));

        private static string GenerateUniqueName(ModelContext model, string baseName)
        {
            var names = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) names.Add(mc.Name);
            }
            if (!names.Contains(baseName)) return baseName;
            for (int n = 1; n < 10000; n++)
            {
                string candidate = $"{baseName}_{n}";
                if (!names.Contains(candidate)) return candidate;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
