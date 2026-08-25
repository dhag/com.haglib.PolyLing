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

        /// <summary>
        /// 境界とみなす頂点インデックス（0..N-1）。
        /// null のときは Threshold による距離判定を使う。
        /// </summary>
        public int[] BoundaryVertices;

        /// <summary>境界頂点をミラー平面へ射影したか</summary>
        public bool ProjectBoundaryToPlane;

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

        /// <summary>元の面数（in-place 実体化時、面 0..OriginalFaceCount-1 が元の面）</summary>
        public int OriginalFaceCount;

        /// <summary>実体化前の MirrorType（解除時の復元判断用）</summary>
        public int SavedMirrorType;

        /// <summary>実体化前の MirrorAxis（1:X, 2:Y, 4:Z）</summary>
        public int SavedMirrorAxis;

        /// <summary>実体化前の MirrorDistance</summary>
        public float SavedMirrorDistance;

        /// <summary>実体化前の MirrorMaterialOffset</summary>
        public int SavedMirrorMaterialOffset;

        /// <summary>実体化に使った軸（0:X, 1:Y, 2:Z）</summary>
        public int BakeAxis;

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
                BoundaryVertices = BoundaryVertices != null ? (int[])BoundaryVertices.Clone() : null,
                ProjectBoundaryToPlane = ProjectBoundaryToPlane,
                FlipU = FlipU,
                OldToNew = OldToNew != null ? (int[])OldToNew.Clone() : null,
                NewToOldRepresentative = NewToOldRepresentative != null ? (int[])NewToOldRepresentative.Clone() : null,
                NewVertexOrigin = NewVertexOrigin != null ? (VertexOrigin[])NewVertexOrigin.Clone() : null,
                NewToOriginalIndex = NewToOriginalIndex != null ? (int[])NewToOriginalIndex.Clone() : null,
                OriginalFaceCount = OriginalFaceCount,
                SavedMirrorType = SavedMirrorType,
                SavedMirrorAxis = SavedMirrorAxis,
                SavedMirrorDistance = SavedMirrorDistance,
                SavedMirrorMaterialOffset = SavedMirrorMaterialOffset,
                BakeAxis = BakeAxis,
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
        /// <param name="boundaryVertices">
        /// 境界とみなす頂点インデックス。null のときは threshold による距離判定を使う。
        /// </param>
        /// <param name="projectBoundaryToPlane">
        /// 境界頂点をミラー平面へ射影するか。距離判定のときは true（平面上に居るので実質無変化）、
        /// 選択頂点指定のときは false にしないと平面から離れた頂点が寄せられて形が変わる。
        /// </param>
        /// <returns>ベイク結果（メッシュとメタデータ）</returns>
        public static (MeshObject bakedMesh, MirrorBakeResult result) BakeMirror(
            MeshObject source,
            int axis = 0,
            float planeOffset = 0f,
            float threshold = 0.0001f,
            bool flipU = false,
            IReadOnlyCollection<int> boundaryVertices = null,
            bool projectBoundaryToPlane = true)
        {
            // 軸から平面を定義
            Vector3 planeNormal = GetAxisNormal(axis);
            float planeDistance = -planeOffset; // n·x + d = 0 形式

            return BakeMirror(source, planeNormal, planeDistance, threshold, flipU,
                              boundaryVertices, projectBoundaryToPlane);
        }

        /// <summary>
        /// ミラーをベイクして新しいMeshObjectを生成（平面指定版）
        /// </summary>
        public static (MeshObject bakedMesh, MirrorBakeResult result) BakeMirror(
            MeshObject source,
            Vector3 planeNormal,
            float planeDistance,
            float threshold,
            bool flipU,
            IReadOnlyCollection<int> boundaryVertices = null,
            bool projectBoundaryToPlane = true)
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

            // 2-A: 元頂点 i とそのミラー i' を統合するか判定する。
            //   boundaryVertices が渡されていればその集合を境界とみなす（選択頂点モード）。
            //   渡されていなければ従来どおり平面からの距離が threshold 未満かで判定する。
            HashSet<int> boundarySet = null;
            if (boundaryVertices != null && boundaryVertices.Count > 0)
            {
                boundarySet = boundaryVertices as HashSet<int> ?? new HashSet<int>(boundaryVertices);
            }

            for (int i = 0; i < N; i++)
            {
                bool isBoundary;
                if (boundarySet != null)
                {
                    isBoundary = boundarySet.Contains(i);
                }
                else
                {
                    float dist = PlaneDist(positions[i], planeNormal, planeDistance);
                    isBoundary = Mathf.Abs(dist) < threshold;
                }

                if (isBoundary)
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

            // 各新頂点に「元側／ミラー側のどちらが含まれるか」を1パスで集計する。
            // （以前は root ごとに 2N 全体を走査していたため O(N^2) だった。結果は同一。）
            var hasOriginalOf = new bool[newVertexCount];
            var hasMirroredOf = new bool[newVertexCount];

            for (int v = 0; v < 2 * N; v++)
            {
                int newV = oldToNew[v];
                if (isMirrored[v]) hasMirroredOf[newV] = true;
                else               hasOriginalOf[newV] = true;
            }

            foreach (var kvp in rootToNew)
            {
                int root = kvp.Key;
                int newV = kvp.Value;

                newToOldRep[newV] = root;

                // 出自を判定
                if (hasOriginalOf[newV] && hasMirroredOf[newV])
                    newOrigin[newV] = VertexOrigin.Merged;
                else if (hasMirroredOf[newV])
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
                if (newOrigin[newV] == VertexOrigin.Merged && projectBoundaryToPlane)
                {
                    // 境界頂点：境界面上に配置（両側の平均的な位置）
                    pos = ProjectToPlane(positions[oldV], planeNormal, planeDistance);
                }
                else
                {
                    // 射影しない場合は代表元の位置をそのまま使う。
                    // 選択頂点を境界にしたとき、平面から離れた頂点を寄せて形を変えないため。
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

                // 法線をコピー（後で再計算する場合もある）。
                // 【不変条件】UVs.Count == Normals.Count を保つため、UVスロット数へ合わせる。
                // 以前は Normals[0] の1本しか追加していなかった。
                int normalSlots = newVertex.UVs.Count > 0 ? newVertex.UVs.Count : srcVertex.Normals.Count;
                for (int nIdx = 0; nIdx < normalSlots; nIdx++)
                {
                    Vector3 normal = nIdx < srcVertex.Normals.Count
                        ? srcVertex.Normals[nIdx]
                        : (srcVertex.Normals.Count > 0 ? srcVertex.Normals[0] : Vector3.up);

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

            // 元の面。
            // 【重要】in-place 実体化では元の面インデックスを保存する必要があるため、
            // ここで面を間引いてはならない。統合は i と i+N の間でしか起こらないので、
            // 元の面が統合で縮退することはない。
            foreach (var face in source.Faces)
            {
                bakedMesh.Faces.Add(CreateRemappedFace(face, oldToNew, 0, source));
            }

            int originalFaceCount = bakedMesh.Faces.Count;

            // ミラー面（頂点順序を反転して法線を逆に）
            foreach (var face in source.Faces)
            {
                bakedMesh.Faces.Add(CreateRemappedFaceFlipped(face, oldToNew, N, source));
            }

            // UV/法線サブindex がベイク後頂点のスロット範囲に収まっているか点検する。
            // 【不変条件】UVIndices[j] == NormalIndices[j] / スロット範囲内。
            foreach (var face in bakedMesh.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    while (face.UVIndices.Count <= i)     face.UVIndices.Add(0);
                    while (face.NormalIndices.Count <= i) face.NormalIndices.Add(0);

                    int vi = face.VertexIndices[i];
                    int slots = (vi >= 0 && vi < bakedMesh.Vertices.Count)
                        ? bakedMesh.Vertices[vi].UVs.Count : 0;

                    int idx = face.UVIndices[i];
                    if (idx < 0 || idx >= slots) idx = 0;

                    face.UVIndices[i]     = idx;
                    face.NormalIndices[i] = idx;
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
                BoundaryVertices = boundarySet != null ? boundarySet.ToArray() : null,
                ProjectBoundaryToPlane = projectBoundaryToPlane,
                FlipU = flipU,
                OriginalFaceCount = originalFaceCount,
                OldToNew = oldToNew,
                NewToOldRepresentative = newToOldRep,
                NewVertexOrigin = newOrigin,
                NewToOriginalIndex = newToOriginal,
                CreatedAt = DateTime.Now
            };

            // ベイク結果は元メッシュのウェイトを引き継ぐ。種別も合わせる。
            // 元が SkinnedMesh 系なら、頂点が 0 件でも種別は引き継ぐ必要がある
            // （RecomputeSkinKind は無 → 有の一方向なので、明示コピーで揃える）。
            bakedMesh.SetSkinKind(source.SkinKind);
            bakedMesh.RecomputeSkinKind();

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

            // 元コーナーの UV サブindex を保持したままリマップする。
            // ベイク後の頂点は元頂点と同じ順序で UV スロットを持つので添字はそのまま使える。
            // （以前は 0 固定にしていたため UV シームが全滅していた。）
            for (int i = 0; i < src.VertexIndices.Count; i++)
            {
                int oldIdx = src.VertexIndices[i] + offset;
                if (oldIdx < 0 || oldIdx >= oldToNew.Length) continue;

                face.VertexIndices.Add(oldToNew[oldIdx]);

                int uvSubIdx = i < src.UVIndices.Count ? src.UVIndices[i] : 0;
                face.UVIndices.Add(uvSubIdx);
                face.NormalIndices.Add(uvSubIdx);
            }

            return face;
        }

        /// <summary>面の頂点インデックスをリマップ（反転版）</summary>
        private static Face CreateRemappedFaceFlipped(Face src, int[] oldToNew, int offset, MeshObject srcMesh)
        {
            var face = new Face { MaterialIndex = src.MaterialIndex };

            // 逆順で追加（法線反転）。UV サブindex も同じコーナーのものを持ち越す。
            for (int i = src.VertexIndices.Count - 1; i >= 0; i--)
            {
                int oldIdx = src.VertexIndices[i] + offset;
                if (oldIdx < 0 || oldIdx >= oldToNew.Length) continue;

                face.VertexIndices.Add(oldToNew[oldIdx]);

                int uvSubIdx = i < src.UVIndices.Count ? src.UVIndices[i] : 0;
                face.UVIndices.Add(uvSubIdx);
                face.NormalIndices.Add(uvSubIdx);
            }

            return face;
        }


        /// <summary>有効な面か（同一頂点が複数ないか、3頂点以上か）</summary>
        // ================================================================
        // in-place 実体化 / 解除
        // ================================================================

        /// <summary>
        /// 選択メッシュそのものへミラーの実体を生やす（in-place）。
        /// 対称面をまたぐ処理（法線スムージング等）を正しく効かせるための作業用機能で、
        /// 別オブジェクトは作らない。
        ///
        /// 元の頂点インデックス 0..N-1 と元の面インデックス 0..F-1 は保存される。
        /// （統合は i と i+N の間でしか起こらないため。選択や各種頂点参照がそのまま生きる）
        /// </summary>
        /// <returns>解除に必要な情報。失敗時 null。</returns>
        public static MirrorBakeResult BakeInPlace(
            MeshObject target,
            int axis,
            float planeOffset,
            float threshold,
            bool flipU,
            IReadOnlyCollection<int> boundaryVertices,
            bool projectBoundaryToPlane)
        {
            if (target == null || target.VertexCount == 0) return null;

            var (baked, result) = BakeMirror(
                target, axis, planeOffset, threshold, flipU,
                boundaryVertices, projectBoundaryToPlane);

            if (baked == null || result == null) return null;

            result.BakeAxis = axis;

            // 中身だけ差し替える（MeshObject インスタンスは維持）
            target.Vertices = baked.Vertices;
            target.Faces    = baked.Faces;

            // Vertices を丸ごと差し替えたので種別を確認し直す。
            // 差し替え前が MeshFilter でも、ベイク結果がウェイトを持てば Skinned。
            target.RecomputeSkinKind();

            return result;
        }

        /// <summary>
        /// in-place 実体化を解除して半身へ戻す。
        /// 元頂点 0..N-1 と元面 0..OriginalFaceCount-1 だけを残し、
        /// mode に従ってどちら側の編集結果を採用するかを決める。
        /// </summary>
        /// <returns>成功したら true</returns>
        public static bool UnbakeInPlace(
            MeshObject target,
            MirrorBakeResult result,
            WriteBackMode mode)
        {
            if (target == null || result == null || !result.IsValid) return false;

            int N = result.OriginalVertexCount;
            if (N <= 0 || target.VertexCount < N) return false;

            var planeNormal = result.PlaneNormal.normalized;
            float planeDistance = result.PlaneDistance;

            // 採用する座標を決める
            var finalPos = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                int newOrig = (i < result.OldToNew.Length) ? result.OldToNew[i] : -1;
                int newMir  = (i + N < result.OldToNew.Length) ? result.OldToNew[i + N] : -1;

                bool hasOrig = newOrig >= 0 && newOrig < target.VertexCount;
                bool hasMir  = newMir  >= 0 && newMir  < target.VertexCount;

                // 統合された境界頂点は元とミラーが同一頂点。
                // ミラー逆変換や平均を掛けると平面の反対側へ飛ぶので、そのまま採用する。
                if (hasOrig && newOrig == newMir)
                {
                    finalPos[i] = target.Vertices[newOrig].Position;
                    continue;
                }

                Vector3 posOrig = hasOrig ? target.Vertices[newOrig].Position : Vector3.zero;
                Vector3 posMir  = hasMir
                    ? Mirror(target.Vertices[newMir].Position, planeNormal, planeDistance)
                    : Vector3.zero;

                switch (mode)
                {
                    case WriteBackMode.MirroredSideOnly:
                        finalPos[i] = hasMir ? posMir : posOrig;
                        break;

                    case WriteBackMode.Average:
                        if (hasOrig && hasMir)      finalPos[i] = (posOrig + posMir) * 0.5f;
                        else if (hasMir)            finalPos[i] = posMir;
                        else                        finalPos[i] = posOrig;
                        break;

                    default: // OriginalSideOnly
                        finalPos[i] = hasOrig ? posOrig : posMir;
                        break;
                }
            }

            // 面を元の分だけ残す（ミラー面は破棄）
            int keepFaces = Mathf.Clamp(result.OriginalFaceCount, 0, target.Faces.Count);
            if (target.Faces.Count > keepFaces)
                target.Faces.RemoveRange(keepFaces, target.Faces.Count - keepFaces);

            // 頂点を元の分だけ残す
            if (target.Vertices.Count > N)
                target.Vertices.RemoveRange(N, target.Vertices.Count - N);

            for (int i = 0; i < N; i++)
                target.Vertices[i].Position = finalPos[i];

            return true;
        }

        private static bool IsValidFace(Face face)
        {
            if (face.VertexIndices.Count < 3)
                return false;

            var unique = new HashSet<int>(face.VertexIndices);
            return unique.Count >= 3;
        }
    }
}
