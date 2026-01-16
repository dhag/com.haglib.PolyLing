// Assets/Editor/Poly_Ling/Tools/MirrorEdit/MirrorBaker.cs
// ミラーベイク処理（頂点統合対応）
// - 境界面付近の頂点を統合してベイク
// - 編集後メッシュから元形式への書き戻し

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    // ================================================================
    // データ構造
    // ================================================================

    /// <summary>
    /// 頂点の出自
    /// </summary>
    public enum VertexOrigin
    {
        /// <summary>元頂点（ミラー側ではない）</summary>
        Original,
        /// <summary>ミラー生成された頂点</summary>
        Mirrored,
        /// <summary>境界で統合（元とミラーが同一化）</summary>
        Merged
    }

    /// <summary>
    /// 書き戻しモード
    /// </summary>
    public enum WriteBackMode
    {
        /// <summary>元側（+X等）の頂点を採用</summary>
        OriginalSideOnly,
        /// <summary>ミラー側（-X等）の頂点を採用してミラー逆変換</summary>
        MirroredSideOnly,
        /// <summary>両側を平均化（対称性保証）</summary>
        Average
    }

    /// <summary>
    /// ミラーベイク処理の結果データ
    /// MeshContextに紐付けて保存
    /// </summary>
    [Serializable]
    public class MirrorBakeResult
    {
        /// <summary>元のMeshContext名</summary>
        public string SourceMeshName;

        /// <summary>元の頂点数 N</summary>
        public int OriginalVertexCount;

        /// <summary>ミラー平面（法線ベクトル、正規化済み）</summary>
        public Vector3 PlaneNormal;

        /// <summary>ミラー平面（距離 d: n·x + d = 0）</summary>
        public float PlaneDistance;

        /// <summary>境界判定閾値</summary>
        public float Threshold;

        /// <summary>UV反転フラグ</summary>
        public bool FlipU;

        /// <summary>
        /// 旧インデックス → 新インデックス マッピング
        /// oldToNew[v] で 0..2N-1 の旧インデックスから統合後インデックスを取得
        /// </summary>
        public int[] OldToNew;

        /// <summary>
        /// 新インデックス → 代表旧インデックス（逆引き用）
        /// </summary>
        public int[] NewToOldRepresentative;

        /// <summary>
        /// 各新頂点の「出自」情報
        /// </summary>
        public VertexOrigin[] NewVertexOrigin;

        /// <summary>
        /// 各新頂点に対応する「元メッシュでのインデックス」(0..N-1)
        /// WriteBack時にこれを使って元メッシュに書き戻す
        /// </summary>
        public int[] NewToOriginalIndex;

        /// <summary>作成日時</summary>
        public DateTime CreatedAt;

        /// <summary>統合後の頂点数</summary>
        public int NewVertexCount => NewToOldRepresentative?.Length ?? 0;

        /// <summary>有効なデータか</summary>
        public bool IsValid => OldToNew != null && OldToNew.Length > 0;

        /// <summary>クローン作成</summary>
        public MirrorBakeResult Clone()
        {
            return new MirrorBakeResult
            {
                SourceMeshName = SourceMeshName,
                OriginalVertexCount = OriginalVertexCount,
                PlaneNormal = PlaneNormal,
                PlaneDistance = PlaneDistance,
                Threshold = Threshold,
                FlipU = FlipU,
                OldToNew = OldToNew != null ? (int[])OldToNew.Clone() : null,
                NewToOldRepresentative = NewToOldRepresentative != null ? (int[])NewToOldRepresentative.Clone() : null,
                NewVertexOrigin = NewVertexOrigin != null ? (VertexOrigin[])NewVertexOrigin.Clone() : null,
                NewToOriginalIndex = NewToOriginalIndex != null ? (int[])NewToOriginalIndex.Clone() : null,
                CreatedAt = CreatedAt
            };
        }
    }

    // ================================================================
    // Union-Find
    // ================================================================

    /// <summary>
    /// Union-Find（素集合データ構造）
    /// </summary>
    public class UnionFind
    {
        private int[] _parent;
        private int[] _rank;

        public UnionFind(int size)
        {
            _parent = new int[size];
            _rank = new int[size];
            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        /// <summary>代表元を取得（経路圧縮付き）</summary>
        public int Find(int x)
        {
            if (_parent[x] != x)
                _parent[x] = Find(_parent[x]);
            return _parent[x];
        }

        /// <summary>統合</summary>
        public void Union(int x, int y)
        {
            int px = Find(x);
            int py = Find(y);
            if (px == py) return;

            // ランクによる統合
            if (_rank[px] < _rank[py])
            {
                _parent[px] = py;
            }
            else if (_rank[px] > _rank[py])
            {
                _parent[py] = px;
            }
            else
            {
                _parent[py] = px;
                _rank[px]++;
            }
        }

        /// <summary>同じ集合に属するか</summary>
        public bool Same(int x, int y)
        {
            return Find(x) == Find(y);
        }
    }

    // ================================================================
    // MirrorBaker
    // ================================================================

    /// <summary>
    /// ミラーベイク処理
    /// </summary>
    public static class MirrorBaker
    {
        // ================================================================
        // メインAPI
        // ================================================================

        /// <summary>
        /// ミラーをベイクして新しいMeshObjectを生成
        /// </summary>
        /// <param name="source">元のMeshObject</param>
        /// <param name="axis">ミラー軸（0:X, 1:Y, 2:Z）</param>
        /// <param name="planeOffset">平面オフセット（通常0）</param>
        /// <param name="threshold">境界判定閾値</param>
        /// <param name="flipU">UV U座標を反転するか</param>
        /// <returns>ベイク結果（メッシュとメタデータ）</returns>
        public static (MeshObject bakedMesh, MirrorBakeResult result) BakeMirror(
            MeshObject source,
            int axis = 0,
            float planeOffset = 0f,
            float threshold = 0.0001f,
            bool flipU = false)
        {
            // 軸から平面を定義
            Vector3 planeNormal = GetAxisNormal(axis);
            float planeDistance = -planeOffset; // n·x + d = 0 形式

            return BakeMirror(source, planeNormal, planeDistance, threshold, flipU);
        }

        /// <summary>
        /// ミラーをベイクして新しいMeshObjectを生成（平面指定版）
        /// </summary>
        public static (MeshObject bakedMesh, MirrorBakeResult result) BakeMirror(
            MeshObject source,
            Vector3 planeNormal,
            float planeDistance,
            float threshold,
            bool flipU)
        {
            if (source == null || source.VertexCount == 0)
            {
                return (null, null);
            }

            int N = source.VertexCount;
            planeNormal = planeNormal.normalized;

            // ================================================================
            // Step 1: 二重化 + 出自記録
            // ================================================================
            var positions = new Vector3[2 * N];
            var originOf = new int[2 * N];      // 元のインデックス（0..N-1）
            var isMirrored = new bool[2 * N];

            for (int i = 0; i < N; i++)
            {
                Vector3 pos = source.Vertices[i].Position;
                positions[i] = pos;
                positions[i + N] = Mirror(pos, planeNormal, planeDistance);

                originOf[i] = i;
                originOf[i + N] = i;

                isMirrored[i] = false;
                isMirrored[i + N] = true;
            }

            // ================================================================
            // Step 2: Union-Find で境界頂点を統合
            // ================================================================
            var uf = new UnionFind(2 * N);

            // 2-A: 元頂点 i とそのミラー i' が境界付近なら統合
            for (int i = 0; i < N; i++)
            {
                float dist = PlaneDist(positions[i], planeNormal, planeDistance);
                if (Mathf.Abs(dist) < threshold)
                {
                    uf.Union(i, i + N);
                }
            }

            // 2-B: (オプション) 異なる元頂点間でも近ければ統合
            // → 空間ハッシュで候補を絞る（性能が必要な場合に実装）
            // 現時点では同一頂点のペアのみ統合

            // ================================================================
            // Step 3: 新インデックスへのリマップ
            // ================================================================
            var rootToNew = new Dictionary<int, int>();
            var oldToNew = new int[2 * N];
            int newIndex = 0;

            for (int v = 0; v < 2 * N; v++)
            {
                int root = uf.Find(v);
                if (!rootToNew.TryGetValue(root, out int newV))
                {
                    newV = newIndex++;
                    rootToNew[root] = newV;
                }
                oldToNew[v] = newV;
            }

            int newVertexCount = newIndex;

            // ================================================================
            // Step 4: 新メッシュの頂点属性を決定
            // ================================================================
            var newToOldRep = new int[newVertexCount];
            var newOrigin = new VertexOrigin[newVertexCount];
            var newToOriginal = new int[newVertexCount];

            // 各root（代表元）の情報を収集
            foreach (var kvp in rootToNew)
            {
                int root = kvp.Key;
                int newV = kvp.Value;

                newToOldRep[newV] = root;

                // このrootに統合された頂点群を調べる
                bool hasOriginal = false;
                bool hasMirrored = false;
                int firstOriginalIdx = -1;

                for (int v = 0; v < 2 * N; v++)
                {
                    if (uf.Find(v) == root)
                    {
                        if (isMirrored[v])
                        {
                            hasMirrored = true;
                        }
                        else
                        {
                            hasOriginal = true;
                            if (firstOriginalIdx < 0)
                                firstOriginalIdx = v;
                        }
                    }
                }

                // 出自を判定
                if (hasOriginal && hasMirrored)
                    newOrigin[newV] = VertexOrigin.Merged;
                else if (hasMirrored)
                    newOrigin[newV] = VertexOrigin.Mirrored;
                else
                    newOrigin[newV] = VertexOrigin.Original;

                // 元メッシュでのインデックス（WriteBack用）
                newToOriginal[newV] = originOf[root];
            }

            // ================================================================
            // Step 5: MeshObjectを構築
            // ================================================================
            var bakedMesh = new MeshObject("Baked_" + source.Name);

            // 頂点を生成
            for (int newV = 0; newV < newVertexCount; newV++)
            {
                int oldV = newToOldRep[newV];
                int srcIdx = originOf[oldV];
                var srcVertex = source.Vertices[srcIdx];

                // 位置
                Vector3 pos;
                if (newOrigin[newV] == VertexOrigin.Merged)
                {
                    // 境界頂点：境界面上に配置（両側の平均的な位置）
                    pos = ProjectToPlane(positions[oldV], planeNormal, planeDistance);
                }
                else
                {
                    pos = positions[oldV];
                }

                // 新しい頂点を作成
                var newVertex = new Vertex(pos);

                // UVをコピー
                if (srcVertex.UVs.Count > 0)
                {
                    Vector2 uv = srcVertex.UVs[0];
                    if (flipU && isMirrored[oldV])
                    {
                        uv.x = 1f - uv.x;
                    }
                    newVertex.UVs.Add(uv);

                    // 追加UVもコピー
                    for (int uvIdx = 1; uvIdx < srcVertex.UVs.Count; uvIdx++)
                    {
                        Vector2 extraUv = srcVertex.UVs[uvIdx];
                        if (flipU && isMirrored[oldV])
                        {
                            extraUv.x = 1f - extraUv.x;
                        }
                        newVertex.UVs.Add(extraUv);
                    }
                }

                // 法線をコピー（後で再計算する場合もある）
                if (srcVertex.Normals.Count > 0)
                {
                    Vector3 normal = srcVertex.Normals[0];
                    if (isMirrored[oldV])
                    {
                        normal = MirrorNormal(normal, planeNormal);
                    }
                    newVertex.Normals.Add(normal);
                }

                // BoneWeightをコピー
                if (srcVertex.BoneWeight.HasValue)
                {
                    newVertex.BoneWeight = srcVertex.BoneWeight;
                }

                bakedMesh.Vertices.Add(newVertex);
            }

            // ================================================================
            // Step 6: 面を生成（インデックスをリマップ）
            // ================================================================

            // 元の面
            foreach (var face in source.Faces)
            {
                var newFace = CreateRemappedFace(face, oldToNew, 0, source);
                if (IsValidFace(newFace))
                {
                    bakedMesh.Faces.Add(newFace);
                }
            }

            // ミラー面（頂点順序を反転して法線を逆に）
            foreach (var face in source.Faces)
            {
                var newFace = CreateRemappedFaceFlipped(face, oldToNew, N, source);
                if (IsValidFace(newFace))
                {
                    bakedMesh.Faces.Add(newFace);
                }
            }

            // 法線を再計算
            bakedMesh.RecalculateSmoothNormals();

            // ================================================================
            // Step 7: 結果を構築
            // ================================================================
            var result = new MirrorBakeResult
            {
                SourceMeshName = source.Name,
                OriginalVertexCount = N,
                PlaneNormal = planeNormal,
                PlaneDistance = planeDistance,
                Threshold = threshold,
                FlipU = flipU,
                OldToNew = oldToNew,
                NewToOldRepresentative = newToOldRep,
                NewVertexOrigin = newOrigin,
                NewToOriginalIndex = newToOriginal,
                CreatedAt = DateTime.Now
            };

            Debug.Log($"[MirrorBaker] Baked '{source.Name}': " +
                      $"Original={N} verts, Baked={newVertexCount} verts, " +
                      $"Merged={newOrigin.Count(o => o == VertexOrigin.Merged)} boundary verts");

            return (bakedMesh, result);
        }

        // ================================================================
        // WriteBack
        // ================================================================

        /// <summary>
        /// 編集後メッシュから元メッシュ形式に書き戻す
        /// </summary>
        /// <param name="editedMesh">編集後のベイクメッシュ</param>
        /// <param name="originalMesh">元のハーフメッシュ</param>
        /// <param name="bakeResult">ベイク時のメタデータ</param>
        /// <param name="mode">書き戻しモード</param>
        /// <returns>書き戻し後の新しいMeshObject</returns>
        public static MeshObject WriteBack(
            MeshObject editedMesh,
            MeshObject originalMesh,
            MirrorBakeResult bakeResult,
            WriteBackMode mode)
        {
            if (editedMesh == null || originalMesh == null || bakeResult == null || !bakeResult.IsValid)
            {
                Debug.LogError("[MirrorBaker] WriteBack: Invalid parameters");
                return null;
            }

            int N = bakeResult.OriginalVertexCount;
            if (originalMesh.VertexCount != N)
            {
                Debug.LogWarning($"[MirrorBaker] WriteBack: Vertex count mismatch. " +
                                 $"Expected {N}, got {originalMesh.VertexCount}");
            }

            // 元メッシュをクローン
            var result = originalMesh.Clone();

            // 編集後の位置を収集（新インデックス → 位置）
            var editedPositions = new Vector3[editedMesh.VertexCount];
            for (int i = 0; i < editedMesh.VertexCount; i++)
            {
                editedPositions[i] = editedMesh.Vertices[i].Position;
            }

            // 各元頂点に対して、対応するベイク頂点の位置を適用
            var originalContribution = new Vector3[N];
            var mirroredContribution = new Vector3[N];
            var originalCount = new int[N];
            var mirroredCount = new int[N];

            for (int newV = 0; newV < bakeResult.NewVertexCount; newV++)
            {
                if (newV >= editedMesh.VertexCount) break;

                int origIdx = bakeResult.NewToOriginalIndex[newV];
                if (origIdx < 0 || origIdx >= N) continue;

                var origin = bakeResult.NewVertexOrigin[newV];
                Vector3 pos = editedPositions[newV];

                switch (origin)
                {
                    case VertexOrigin.Original:
                        originalContribution[origIdx] += pos;
                        originalCount[origIdx]++;
                        break;

                    case VertexOrigin.Mirrored:
                        // ミラー逆変換
                        Vector3 unmirrored = Mirror(pos, bakeResult.PlaneNormal, bakeResult.PlaneDistance);
                        mirroredContribution[origIdx] += unmirrored;
                        mirroredCount[origIdx]++;
                        break;

                    case VertexOrigin.Merged:
                        // 両方にカウント
                        originalContribution[origIdx] += pos;
                        originalCount[origIdx]++;
                        mirroredContribution[origIdx] += pos; // 境界なのでミラー変換不要
                        mirroredCount[origIdx]++;
                        break;
                }
            }

            // モードに応じて最終位置を決定
            for (int i = 0; i < N && i < result.VertexCount; i++)
            {
                Vector3 finalPos;

                switch (mode)
                {
                    case WriteBackMode.OriginalSideOnly:
                        if (originalCount[i] > 0)
                        {
                            finalPos = originalContribution[i] / originalCount[i];
                        }
                        else if (mirroredCount[i] > 0)
                        {
                            // フォールバック：ミラー側を使用
                            finalPos = mirroredContribution[i] / mirroredCount[i];
                        }
                        else
                        {
                            finalPos = result.Vertices[i].Position;
                        }
                        break;

                    case WriteBackMode.MirroredSideOnly:
                        if (mirroredCount[i] > 0)
                        {
                            finalPos = mirroredContribution[i] / mirroredCount[i];
                        }
                        else if (originalCount[i] > 0)
                        {
                            // フォールバック：元側を使用
                            finalPos = originalContribution[i] / originalCount[i];
                        }
                        else
                        {
                            finalPos = result.Vertices[i].Position;
                        }
                        break;

                    case WriteBackMode.Average:
                    default:
                        int totalCount = originalCount[i] + mirroredCount[i];
                        if (totalCount > 0)
                        {
                            Vector3 sum = originalContribution[i] + mirroredContribution[i];
                            finalPos = sum / totalCount;
                        }
                        else
                        {
                            finalPos = result.Vertices[i].Position;
                        }
                        break;
                }

                result.Vertices[i].Position = finalPos;
            }

            // 法線を再計算
            result.RecalculateSmoothNormals();

            Debug.Log($"[MirrorBaker] WriteBack complete: {N} vertices updated, mode={mode}");

            return result;
        }

        // ================================================================
        // ヘルパーメソッド
        // ================================================================

        /// <summary>軸番号から法線ベクトルを取得</summary>
        private static Vector3 GetAxisNormal(int axis)
        {
            return axis switch
            {
                0 => Vector3.right,   // X軸
                1 => Vector3.up,      // Y軸
                2 => Vector3.forward, // Z軸
                _ => Vector3.right
            };
        }

        /// <summary>鏡写し位置を計算: Mirror(x) = x - 2*(n·x + d)*n</summary>
        private static Vector3 Mirror(Vector3 x, Vector3 n, float d)
        {
            float dist = Vector3.Dot(n, x) + d;
            return x - 2f * dist * n;
        }

        /// <summary>平面からの符号付き距離: n·x + d</summary>
        private static float PlaneDist(Vector3 x, Vector3 n, float d)
        {
            return Vector3.Dot(n, x) + d;
        }

        /// <summary>点を平面上に投影</summary>
        private static Vector3 ProjectToPlane(Vector3 x, Vector3 n, float d)
        {
            float dist = PlaneDist(x, n, d);
            return x - dist * n;
        }

        /// <summary>法線をミラー</summary>
        private static Vector3 MirrorNormal(Vector3 normal, Vector3 planeNormal)
        {
            // 平面法線成分を反転
            float dot = Vector3.Dot(normal, planeNormal);
            return normal - 2f * dot * planeNormal;
        }

        /// <summary>面の頂点インデックスをリマップ</summary>
        private static Face CreateRemappedFace(Face src, int[] oldToNew, int offset, MeshObject srcMesh)
        {
            var face = new Face { MaterialIndex = src.MaterialIndex };

            foreach (int vi in src.VertexIndices)
            {
                int oldIdx = vi + offset;
                if (oldIdx >= 0 && oldIdx < oldToNew.Length)
                {
                    face.VertexIndices.Add(oldToNew[oldIdx]);
                }
            }

            // UVインデックス（頂点と同じ位置を参照）
            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            return face;
        }

        /// <summary>面の頂点インデックスをリマップ（反転版）</summary>
        private static Face CreateRemappedFaceFlipped(Face src, int[] oldToNew, int offset, MeshObject srcMesh)
        {
            var face = new Face { MaterialIndex = src.MaterialIndex };

            // 逆順で追加（法線反転）
            for (int i = src.VertexIndices.Count - 1; i >= 0; i--)
            {
                int vi = src.VertexIndices[i];
                int oldIdx = vi + offset;
                if (oldIdx >= 0 && oldIdx < oldToNew.Length)
                {
                    face.VertexIndices.Add(oldToNew[oldIdx]);
                }
            }

            // UVインデックス
            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            return face;
        }

        /// <summary>有効な面か（同一頂点が複数ないか、3頂点以上か）</summary>
        private static bool IsValidFace(Face face)
        {
            if (face.VertexIndices.Count < 3)
                return false;

            var unique = new HashSet<int>(face.VertexIndices);
            return unique.Count >= 3;
        }
    }
}
