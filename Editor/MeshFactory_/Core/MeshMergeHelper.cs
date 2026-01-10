// Assets/Editor/MeshFactory/Utilities/MeshMergeHelper.cs
// 頂点結合・削除ロジックのヘルパークラス
// MergeVerticesTool, MeshCreatorWindowBase, SimpleMeshFactory_Selection から使用

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Utilities
{
    /// <summary>
    /// 頂点マージ結果
    /// </summary>
    public struct MergeResult
    {
        public bool Success;
        public int RemovedVertexCount;
        public string Message;
    }

    /// <summary>
    /// 頂点結合・削除のヘルパークラス
    /// </summary>
    public static class MeshMergeHelper
    {
        // ================================================================
        // 頂点マージ（しきい値ベース）
        // ================================================================

        /// <summary>
        /// 指定された頂点のうち、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="targetVertices">マージ対象の頂点インデックス</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeVerticesAtSamePosition(MeshObject meshObject, HashSet<int> targetVertices, float threshold = 0.001f)
        {
            var result = new MergeResult { Success = false };

            if (meshObject == null || targetVertices == null || targetVertices.Count < 2)
            {
                result.Message = "Not enough vertices selected";
                return result;
            }

            var validSelected = targetVertices
                .Where(v => v >= 0 && v < meshObject.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
            {
                result.Message = "Not enough valid vertices";
                return result;
            }

            // Union-Find
            var parent = new int[meshObject.VertexCount];
            var rank = new int[meshObject.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Unite(int x, int y)
            {
                int rx = Find(x), ry = Find(y);
                if (rx == ry) return;
                if (rank[rx] < rank[ry]) parent[rx] = ry;
                else if (rank[rx] > rank[ry]) parent[ry] = rx;
                else { parent[ry] = rx; rank[rx]++; }
            }

            // 距離計算
            for (int i = 0; i < validSelected.Count; i++)
            {
                for (int j = i + 1; j < validSelected.Count; j++)
                {
                    float dist = Vector3.Distance(
                        meshObject.Vertices[validSelected[i]].Position,
                        meshObject.Vertices[validSelected[j]].Position);

                    if (dist <= threshold)
                        Unite(validSelected[i], validSelected[j]);
                }
            }

            // グループ収集
            var groups = new Dictionary<int, List<int>>();
            foreach (int v in validSelected)
            {
                int root = Find(v);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(v);
            }

            var mergeGroups = groups.Values.Where(g => g.Count >= 2).ToList();

            if (mergeGroups.Count == 0)
            {
                result.Message = "No vertices within threshold";
                result.Success = true;  // エラーではない
                result.RemovedVertexCount = 0;
                return result;
            }

            // 頂点リマップを構築
            var vertexRemap = new Dictionary<int, int>();
            var verticesToRemove = new HashSet<int>();

            foreach (var group in mergeGroups)
            {
                int representative = group.Min();

                // 重心を計算
                Vector3 centroid = Vector3.zero;
                foreach (int v in group)
                    centroid += meshObject.Vertices[v].Position;
                centroid /= group.Count;

                meshObject.Vertices[representative].Position = centroid;

                foreach (int v in group)
                {
                    if (v != representative)
                    {
                        vertexRemap[v] = representative;
                        verticesToRemove.Add(v);
                    }
                }
            }

            // Faceの頂点インデックスを更新
            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (vertexRemap.TryGetValue(face.VertexIndices[i], out int newIdx))
                        face.VertexIndices[i] = newIdx;
                }
            }

            // 縮退面を削除
            RemoveDegenerateFaces(meshObject);

            // 不要頂点を削除
            if (verticesToRemove.Count > 0)
                RemoveVertices(meshObject, verticesToRemove);

            result.Success = true;
            result.RemovedVertexCount = verticesToRemove.Count;
            result.Message = $"Merged {verticesToRemove.Count} vertices into {mergeGroups.Count} groups";

            return result;
        }

        /// <summary>
        /// メッシュ内の全頂点を対象に、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeAllVerticesAtSamePosition(MeshObject meshObject, float threshold = 0.001f)
        {
            if (meshObject == null || meshObject.VertexCount < 2)
                return new MergeResult { Success = false, Message = "Not enough vertices" };

            var allVertices = new HashSet<int>(Enumerable.Range(0, meshObject.VertexCount));
            return MergeVerticesAtSamePosition(meshObject, allVertices, threshold);
        }

        // ================================================================
        // 頂点マージ（重心に統合）
        // ================================================================

        /// <summary>
        /// 選択された頂点を重心位置に統合する
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="verticesToMerge">マージ対象の頂点インデックス</param>
        /// <returns>マージ後の頂点インデックス（失敗時は-1）</returns>
        public static int MergeVerticesToCentroid(MeshObject meshObject, HashSet<int> verticesToMerge)
        {
            if (verticesToMerge == null || verticesToMerge.Count < 2) return -1;
            int originalCount = meshObject.VertexCount;
            if (originalCount == 0) return -1;

            // 1. マージ先の頂点を決定（重心を計算）
            Vector3 centroid = Vector3.zero;
            int validCount = 0;
            foreach (int idx in verticesToMerge)
            {
                if (idx >= 0 && idx < originalCount)
                {
                    centroid += meshObject.Vertices[idx].Position;
                    validCount++;
                }
            }
            if (validCount < 2) return -1;
            centroid /= validCount;

            // 最小インデックスの頂点をマージ先とし、位置を重心に更新
            int targetVertex = verticesToMerge.Where(i => i >= 0 && i < originalCount).Min();
            meshObject.Vertices[targetVertex].Position = centroid;

            // 2. インデックスマッピングを作成
            var indexMap = new int[originalCount];
            int newIndex = 0;

            for (int i = 0; i < originalCount; i++)
            {
                if (verticesToMerge.Contains(i) && i != targetVertex)
                {
                    indexMap[i] = -2; // 後でtargetの新インデックスに更新
                }
                else
                {
                    indexMap[i] = newIndex++;
                }
            }

            // targetVertexの新インデックスを取得
            int targetNewIndex = indexMap[targetVertex];

            // マージ対象のインデックスをtargetNewIndexに更新
            for (int i = 0; i < originalCount; i++)
            {
                if (indexMap[i] == -2)
                {
                    indexMap[i] = targetNewIndex;
                }
            }

            // 3. 面を処理
            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (oldIdx >= 0 && oldIdx < originalCount)
                    {
                        int mappedIdx = indexMap[oldIdx];

                        // 連続する同じ頂点を避ける（縮退した辺を防ぐ）
                        if (newVertexIndices.Count == 0 || newVertexIndices[newVertexIndices.Count - 1] != mappedIdx)
                        {
                            newVertexIndices.Add(mappedIdx);
                            if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                            if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                        }
                    }
                }

                // 最初と最後が同じなら最後を除去
                if (newVertexIndices.Count > 1 && newVertexIndices[0] == newVertexIndices[newVertexIndices.Count - 1])
                {
                    newVertexIndices.RemoveAt(newVertexIndices.Count - 1);
                    if (newUVIndices.Count > 0) newUVIndices.RemoveAt(newUVIndices.Count - 1);
                    if (newNormalIndices.Count > 0) newNormalIndices.RemoveAt(newNormalIndices.Count - 1);
                }

                if (newVertexIndices.Count < 3)
                {
                    // 頂点数が3未満なら面を削除
                    meshObject.Faces.RemoveAt(f);
                }
                else
                {
                    // 面を更新
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }

            // 4. 頂点を削除（マージ先以外、降順で）
            var verticesToRemove = verticesToMerge
                .Where(i => i != targetVertex && i >= 0 && i < meshObject.VertexCount)
                .OrderByDescending(i => i)
                .ToList();

            foreach (var idx in verticesToRemove)
            {
                if (idx >= 0 && idx < meshObject.VertexCount)
                {
                    meshObject.Vertices.RemoveAt(idx);
                }
            }

            return targetNewIndex;
        }

        // ================================================================
        // 頂点削除
        // ================================================================

        /// <summary>
        /// 指定された頂点を削除し、面のインデックスを更新する
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="verticesToDelete">削除する頂点インデックス</param>
        public static void DeleteVertices(MeshObject meshObject, HashSet<int> verticesToDelete)
        {
            if (meshObject == null || verticesToDelete == null || verticesToDelete.Count == 0)
                return;

            int originalCount = meshObject.VertexCount;
            if (originalCount == 0) return;

            // 1. 新しいインデックスへのマッピングを作成
            var indexMap = new int[originalCount];
            int newIndex = 0;
            for (int i = 0; i < originalCount; i++)
            {
                if (verticesToDelete.Contains(i))
                {
                    indexMap[i] = -1; // 削除される
                }
                else
                {
                    indexMap[i] = newIndex++;
                }
            }

            // 2. 面を処理（インデックス更新＆無効な面の削除）
            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (oldIdx >= 0 && oldIdx < originalCount)
                    {
                        int mappedIdx = indexMap[oldIdx];
                        if (mappedIdx >= 0)
                        {
                            newVertexIndices.Add(mappedIdx);
                            if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                            if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                        }
                    }
                }

                if (newVertexIndices.Count < 3)
                {
                    // 頂点数が3未満なら面を削除
                    meshObject.Faces.RemoveAt(f);
                }
                else
                {
                    // 面を更新
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }

            // 3. 頂点を削除（降順で）
            var sortedIndices = verticesToDelete.OrderByDescending(i => i).ToList();
            foreach (var idx in sortedIndices)
            {
                if (idx >= 0 && idx < meshObject.VertexCount)
                {
                    meshObject.Vertices.RemoveAt(idx);
                }
            }
        }

        /// <summary>
        /// 指定された頂点を削除する（面インデックス更新済みの場合）
        /// MergeVerticesAtSamePosition内部で使用
        /// </summary>
        public static void RemoveVertices(MeshObject meshObject, HashSet<int> verticesToRemove)
        {
            if (meshObject == null || verticesToRemove == null || verticesToRemove.Count == 0)
                return;

            var indexRemap = new Dictionary<int, int>();
            int newIndex = 0;

            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                {
                    indexRemap[i] = newIndex;
                    newIndex++;
                }
            }

            var newVertices = new List<Vertex>();
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                    newVertices.Add(meshObject.Vertices[i]);
            }
            meshObject.Vertices.Clear();
            meshObject.Vertices.AddRange(newVertices);

            foreach (var face in meshObject.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (indexRemap.TryGetValue(face.VertexIndices[i], out int newIdx))
                        face.VertexIndices[i] = newIdx;
                }
            }
        }

        // ================================================================
        // 縮退面削除
        // ================================================================

        /// <summary>
        /// 縮退した面（同じ頂点を複数参照する面）を削除
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <returns>削除した面の数</returns>
        public static int RemoveDegenerateFaces(MeshObject meshObject)
        {
            if (meshObject == null) return 0;

            var toRemove = new List<int>();

            for (int i = 0; i < meshObject.FaceCount; i++)
            {
                var face = meshObject.Faces[i];
                var unique = new HashSet<int>(face.VertexIndices);

                if (unique.Count < 2 || (face.VertexCount >= 3 && unique.Count < 3))
                    toRemove.Add(i);
            }

            for (int i = toRemove.Count - 1; i >= 0; i--)
                meshObject.Faces.RemoveAt(toRemove[i]);

            if (toRemove.Count > 0)
                Debug.Log($"[MeshMergeHelper] Removed {toRemove.Count} degenerate faces");

            return toRemove.Count;
        }

        // ================================================================
        // インデックスリマップ（ユーティリティ）
        // ================================================================

        /// <summary>
        /// 頂点削除用のインデックスリマップを構築
        /// </summary>
        public static Dictionary<int, int> BuildIndexRemap(int vertexCount, HashSet<int> verticesToRemove)
        {
            var remap = new Dictionary<int, int>();
            int newIndex = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                if (!verticesToRemove.Contains(i))
                {
                    remap[i] = newIndex++;
                }
            }

            return remap;
        }

        /// <summary>
        /// 面のインデックスにリマップを適用
        /// </summary>
        public static void ApplyIndexRemapToFaces(MeshObject meshObject, Dictionary<int, int> remap)
        {
            if (meshObject == null || remap == null) return;

            for (int f = meshObject.FaceCount - 1; f >= 0; f--)
            {
                var face = meshObject.Faces[f];
                var newVertexIndices = new List<int>();
                var newUVIndices = new List<int>();
                var newNormalIndices = new List<int>();

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int oldIdx = face.VertexIndices[i];
                    if (remap.TryGetValue(oldIdx, out int newIdx))
                    {
                        newVertexIndices.Add(newIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }

                if (newVertexIndices.Count < 3)
                {
                    meshObject.Faces.RemoveAt(f);
                }
                else
                {
                    face.VertexIndices = newVertexIndices;
                    face.UVIndices = newUVIndices;
                    face.NormalIndices = newNormalIndices;
                }
            }
        }
    }
}
