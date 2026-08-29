using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MeshBridge
{
    // ================================================================
    // MeshBridgeDefault
    // ----------------------------------------------------------------
    // IMeshBridge の既定実装。
    // 「MeshObject <-> UnityEngine.Mesh」変換の “唯一の実装場所”。
    //
    // ここに含まれる2種類のもの:
    //   (1) プラットフォーム非依存の変換アルゴリズム
    //       - 頂点展開（1論理頂点が複数UV/法線を持つ場合、Unity頂点へ分裂させる）
    //       - 孤立頂点（どの面からも参照されない頂点）の除外
    //       - Nゴン面の三角形分割（Face.Triangulate に委譲）
    //       - マテリアルインデックス -> サブメッシュ への割当
    //       - 読み込み時の同一位置頂点マージ
    //       これらは Python/JS へ移植可能。移植時はこのファイルを仕様書として使える。
    //   (2) Unity 固有の生API呼び出し
    //       - new Mesh() / SetVertices / SetUVs / SetNormals / SetTriangles
    //       - mesh.boneWeights / mesh.subMeshCount / RecalculateBounds / RecalculateNormals
    //       - 読み込み時の mesh.vertices / mesh.uv / mesh.normals / GetTriangles
    //       他言語へ移植するのは (1) のみで、(2) は各環境の同等APIに置き換える。
    //
    // 頂点展開の基本原則（FPX仕様）:
    //   論理頂点(MeshObject.Vertex)は複数のUV/法線スロットを持てる（シーム・ハードエッジ用）。
    //   GPUメッシュは「1頂点 = 1組の(位置,UV,法線)」しか表現できないため、
    //   面が参照する (頂点, UVサブindex[, 法線サブindex]) の組ごとに Unity頂点を1つ作る。
    //   同じ組を参照する面は同じ Unity頂点を共有する（vertexMapping で名寄せ）。
    // ================================================================
    public class MeshBridgeDefault : IMeshBridge
    {
        // ============================================================
        // MeshObject -> Unity Mesh （非変換・頂点順展開）
        // ============================================================
        // 展開順序: 「頂点順 -> UV順」。
        //   外側ループ = 論理頂点インデックス昇順、内側ループ = その頂点のUVスロット順。
        //   この順序は AppendExpandedVertices と一致させる必要がある（外部連携の頂点対応）。
        // 名寄せキー: (頂点index, UVサブindex)。法線は同じサブindexのスロットを使う
        // （UVs.Count == Normals.Count / UVIndices[j] == NormalIndices[j] の不変条件）。
        public Mesh ToUnityMesh(MeshObject source, int materialCount = -1)
        {
            var mesh = new Mesh();
            Poly_Ling.Diagnostics.PLResStat.LiveMesh++;
            mesh.name = source.Name;

            if (source.Vertices.Count == 0)
                return mesh;

            // サブメッシュ数（materialCount 指定時はそれを優先、無指定なら自動算出）
            int subMeshCount = materialCount > 0 ? materialCount : source.SubMeshCount;

            // (頂点index, UVサブindex) -> Unity頂点index
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = source.IsSkinnedKind;

            // 孤立頂点除外。規則は MeshExpansion に一本化してある
            // （3頂点以上の面から参照されない頂点は除外／face.IsHidden は見ない）。
            // ここに規則を書き直すと GPU 展開バッファ
            // （UnifiedBufferManager_Build.BuildExpandedVertexMapping）や書き戻し
            // （UnifiedSystemAdapter.WritebackTransformedVertices）と食い違う。
            var nonIsolatedVerts = MeshExpansion.BuildNonIsolatedSet(source);

            // パス1: 頂点順 -> UV順 で Unity頂点を作成（孤立頂点はスキップ）。
            for (int vIdx = 0; vIdx < source.Vertices.Count; vIdx++)
            {
                if (!nonIsolatedVerts.Contains(vIdx)) continue;
                var vertex = source.Vertices[vIdx];
                int uvCount = MeshExpansion.SlotCount(vertex);

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int unityIdx = unityVerts.Count;
                    vertexMapping[(vIdx, uvIdx)] = unityIdx;

                    // 位置
                    unityVerts.Add(vertex.Position);

                    // UV（スロットが無ければ (0,0)）
                    if (uvIdx < vertex.UVs.Count)
                        unityUVs.Add(vertex.UVs[uvIdx]);
                    else
                        unityUVs.Add(Vector2.zero);

                    // 法線（UVサブindexと同じスロット。無ければ先頭、それも無ければ上向き）
                    if (uvIdx < vertex.Normals.Count)
                        unityNormals.Add(vertex.Normals[uvIdx]);
                    else if (vertex.Normals.Count > 0)
                        unityNormals.Add(vertex.Normals[0]);
                    else
                        unityNormals.Add(Vector3.up);

                    // BoneWeight
                    if (hasBoneWeights)
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                }
            }

            // パス2: 面を三角形に分解し、サブメッシュ別の三角形indexを構築。
            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in source.Faces)
            {
                // 補助線(2頂点)や非表示面はスキップ
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= source.Vertices.Count)
                            continue;

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        var vertex = source.Vertices[vIdx];
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                        var key = (vIdx, uvSubIdx);
                        if (vertexMapping.TryGetValue(key, out int unityIdx))
                            subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // --- ここから生 Unity Mesh API ---
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
                mesh.boneWeights = unityBoneWeights.ToArray();

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);

            mesh.RecalculateBounds();

            // 法線が無い/全て既定値なら自動計算（PreserveNormals 時は行わない）
            if (!source.PreserveNormals &&
                (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up)))
                mesh.RecalculateNormals();

            return mesh;
        }

        // ============================================================
        // MeshObject -> Unity Mesh （transform適用 / 頂点共有）
        // ============================================================
        // 名寄せキーに法線サブindexも含め、(頂点index, UVサブindex, 法線サブindex) が
        // 一致する場合のみ Unity頂点を共有する。位置は transform で、法線は
        // 逆転置行列(transform.inverse.transpose)で変換する（非一様スケール対応）。
        public Mesh ToUnityMesh(MeshObject source, Matrix4x4 transform, int materialCount = -1)
        {
            var mesh = new Mesh();
            Poly_Ling.Diagnostics.PLResStat.LiveMesh++;
            mesh.name = source.Name;

            if (source.Vertices.Count == 0)
                return mesh;

            int subMeshCount = materialCount > 0 ? materialCount : source.SubMeshCount;

            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = source.IsSkinnedKind;

            // 法線変換用（逆転置行列）
            Matrix4x4 normalMatrix = transform.inverse.transpose;

            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            // 面駆動で展開（参照される (頂点,UV,法線) の組だけ Unity頂点を作る）。
            foreach (var face in source.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= source.Vertices.Count)
                            continue;

                        var vertex = source.Vertices[vIdx];

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                        int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                        if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                            normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                        var key = (vIdx, uvSubIdx, normalSubIdx);

                        if (!vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            unityIdx = unityVerts.Count;
                            vertexMapping[key] = unityIdx;

                            // 位置を変換
                            Vector3 transformedPos = transform.MultiplyPoint3x4(vertex.Position);
                            unityVerts.Add(transformedPos);

                            // UV
                            if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                                unityUVs.Add(vertex.UVs[uvSubIdx]);
                            else if (vertex.UVs.Count > 0)
                                unityUVs.Add(vertex.UVs[0]);
                            else
                                unityUVs.Add(Vector2.zero);

                            // 法線を変換
                            Vector3 normal;
                            if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                                normal = vertex.Normals[normalSubIdx];
                            else if (vertex.Normals.Count > 0)
                                normal = vertex.Normals[0];
                            else
                                normal = Vector3.up;

                            Vector3 transformedNormal = normalMatrix.MultiplyVector(normal).normalized;
                            unityNormals.Add(transformedNormal);

                            // BoneWeight
                            if (hasBoneWeights)
                                unityBoneWeights.Add(vertex.BoneWeight ?? default);
                        }

                        subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // --- 生 Unity Mesh API ---
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
                mesh.boneWeights = unityBoneWeights.ToArray();

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);

            mesh.RecalculateBounds();

            // PreserveNormals のメッシュは MeshObject の法線をそのまま使う
            if (!source.PreserveNormals &&
                (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up)))
                mesh.RecalculateNormals();

            return mesh;
        }

        // ============================================================
        // MeshObject -> Unity Mesh （頂点共有版・非変換）
        // ============================================================
        // ToUnityMesh(int) と同じ「頂点順->UV順」の展開だが、パス2でも
        // (頂点index, UVサブindex) キーの名寄せ結果を再利用して三角形を張る。
        // MQO読み込み時の CreateFaceAndModifyVertex 方式に対応した版。
        public Mesh ToUnityMeshShared(MeshObject source, int materialCount = -1)
        {
            var mesh = new Mesh();
            Poly_Ling.Diagnostics.PLResStat.LiveMesh++;
            mesh.name = source.Name;

            if (source.Vertices.Count == 0)
                return mesh;

            int subMeshCount = materialCount > 0 ? materialCount : source.SubMeshCount;

            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = source.IsSkinnedKind;

            // 孤立頂点除外（規則は MeshExpansion に一本化。ToUnityMesh と同一）
            var nonIsolatedVerts = MeshExpansion.BuildNonIsolatedSet(source);

            // パス1: 頂点順 -> UV順 で Unity頂点を作成
            for (int vIdx = 0; vIdx < source.Vertices.Count; vIdx++)
            {
                if (!nonIsolatedVerts.Contains(vIdx)) continue;
                var vertex = source.Vertices[vIdx];
                int uvCount = MeshExpansion.SlotCount(vertex);

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int unityIdx = unityVerts.Count;
                    vertexMapping[(vIdx, uvIdx)] = unityIdx;

                    unityVerts.Add(vertex.Position);

                    if (uvIdx < vertex.UVs.Count)
                        unityUVs.Add(vertex.UVs[uvIdx]);
                    else
                        unityUVs.Add(Vector2.zero);

                    // 法線はUVサブindexと同じスロットを使う
                    if (uvIdx < vertex.Normals.Count)
                        unityNormals.Add(vertex.Normals[uvIdx]);
                    else if (vertex.Normals.Count > 0)
                        unityNormals.Add(vertex.Normals[0]);
                    else
                        unityNormals.Add(Vector3.up);

                    if (hasBoneWeights)
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                }
            }

            // パス2: 面の三角形indexを構築
            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in source.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= source.Vertices.Count)
                            continue;

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        var vertex = source.Vertices[vIdx];
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                        var key = (vIdx, uvSubIdx);
                        if (vertexMapping.TryGetValue(key, out int unityIdx))
                            subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // --- 生 Unity Mesh API ---
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
                mesh.boneWeights = unityBoneWeights.ToArray();

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);

            mesh.RecalculateBounds();

            // PreserveNormals のメッシュは MeshObject の法線をそのまま使う
            if (!source.PreserveNormals &&
                (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up)))
                mesh.RecalculateNormals();

            return mesh;
        }

        // ============================================================
        // MeshObject -> Unity Mesh （頂点共有版・transform適用）
        // ============================================================
        public Mesh ToUnityMeshShared(MeshObject source, Matrix4x4 transform, int materialCount = -1)
        {
            var mesh = new Mesh();
            Poly_Ling.Diagnostics.PLResStat.LiveMesh++;
            mesh.name = source.Name;

            if (source.Vertices.Count == 0)
                return mesh;

            int subMeshCount = materialCount > 0 ? materialCount : source.SubMeshCount;

            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = source.IsSkinnedKind;

            Matrix4x4 normalMatrix = transform.inverse.transpose;

            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in source.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= source.Vertices.Count)
                            continue;

                        var vertex = source.Vertices[vIdx];

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                        int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                        if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                            normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                        var key = (vIdx, uvSubIdx, normalSubIdx);

                        if (!vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            unityIdx = unityVerts.Count;
                            vertexMapping[key] = unityIdx;

                            Vector3 transformedPos = transform.MultiplyPoint3x4(vertex.Position);
                            unityVerts.Add(transformedPos);

                            if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                                unityUVs.Add(vertex.UVs[uvSubIdx]);
                            else if (vertex.UVs.Count > 0)
                                unityUVs.Add(vertex.UVs[0]);
                            else
                                unityUVs.Add(Vector2.zero);

                            Vector3 normal;
                            if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                                normal = vertex.Normals[normalSubIdx];
                            else if (vertex.Normals.Count > 0)
                                normal = vertex.Normals[0];
                            else
                                normal = Vector3.up;

                            Vector3 transformedNormal = normalMatrix.MultiplyVector(normal).normalized;
                            unityNormals.Add(transformedNormal);

                            if (hasBoneWeights)
                                unityBoneWeights.Add(vertex.BoneWeight ?? default);
                        }

                        subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // --- 生 Unity Mesh API ---
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
                mesh.boneWeights = unityBoneWeights.ToArray();

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);

            mesh.RecalculateBounds();

            // PreserveNormals のメッシュは MeshObject の法線をそのまま使う
            if (!source.PreserveNormals &&
                (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up)))
                mesh.RecalculateNormals();

            return mesh;
        }

        // ============================================================
        // Unity Mesh -> MeshObject
        // ============================================================
        // mergeVertices=true:
        //   同一位置(Vector3Comparer による完全一致)の Unity頂点を1つの論理頂点に統合し、
        //   位置違いのUV/法線は同じ論理頂点の追加スロットとして保持する。
        //   ただしスキンドメッシュ(includeBoneWeights=true)では、同一位置でも
        //   BoneWeight が異なれば別頂点として扱う（統合するとウェイトが壊れるため）。
        // mergeVertices=false:
        //   Unity頂点をそのまま1対1で論理頂点に写す（GPUメッシュの忠実再現）。
        public void FromUnityMesh(MeshObject target, Mesh mesh, bool mergeVertices, bool includeBoneWeights)
        {
            target.Clear();
            if (mesh == null) return;

            // --- 生 Unity Mesh API（読み取り）---
            var srcVerts = mesh.vertices;
            var srcUVs = mesh.uv;
            var srcNormals = mesh.normals;

            BoneWeight[] srcBoneWeights = null;
            if (includeBoneWeights)
                srcBoneWeights = mesh.boneWeights;

            if (mergeVertices)
            {
                // 同一位置の頂点を統合
                var positionToIndex = new Dictionary<Vector3, int>(new Vector3Comparer());
                var oldToNewIndex = new int[srcVerts.Length];

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    Vector3 pos = srcVerts[i];

                    bool shouldMerge = false;
                    int existingIdx = -1;

                    if (positionToIndex.TryGetValue(pos, out int foundIdx))
                    {
                        existingIdx = foundIdx;

                        if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                        {
                            // BoneWeight が完全一致するときだけ統合
                            var existingBw = target.Vertices[existingIdx].BoneWeight;
                            var newBw = srcBoneWeights[i];
                            if (existingBw.HasValue &&
                                existingBw.Value.boneIndex0 == newBw.boneIndex0 &&
                                existingBw.Value.boneIndex1 == newBw.boneIndex1 &&
                                existingBw.Value.boneIndex2 == newBw.boneIndex2 &&
                                existingBw.Value.boneIndex3 == newBw.boneIndex3 &&
                                Mathf.Approximately(existingBw.Value.weight0, newBw.weight0) &&
                                Mathf.Approximately(existingBw.Value.weight1, newBw.weight1) &&
                                Mathf.Approximately(existingBw.Value.weight2, newBw.weight2) &&
                                Mathf.Approximately(existingBw.Value.weight3, newBw.weight3))
                            {
                                shouldMerge = true;
                            }
                        }
                        else
                        {
                            // 通常メッシュ: 位置が同じなら統合
                            shouldMerge = true;
                        }
                    }

                    if (shouldMerge && existingIdx >= 0)
                    {
                        // 既存の論理頂点を参照し、UV/法線を（重複なしで）追加
                        oldToNewIndex[i] = existingIdx;

                        var vertex = target.Vertices[existingIdx];
                        Vector2 addUV  = (srcUVs     != null && i < srcUVs.Length)     ? srcUVs[i]     : Vector2.zero;
                        Vector3 addNrm = (srcNormals != null && i < srcNormals.Length) ? srcNormals[i] : Vector3.up;
                        vertex.GetOrAddUVNormal(addUV, addNrm);
                    }
                    else
                    {
                        // 新規論理頂点（UV/法線は必ず1スロットずつ対で持たせる）
                        var vertex = new Vertex(pos);
                        vertex.UVs.Add((srcUVs != null && i < srcUVs.Length) ? srcUVs[i] : Vector2.zero);
                        vertex.Normals.Add((srcNormals != null && i < srcNormals.Length) ? srcNormals[i] : Vector3.up);

                        if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                            vertex.BoneWeight = srcBoneWeights[i];

                        int newIdx = target.Vertices.Count;
                        target.Vertices.Add(vertex);
                        positionToIndex[pos] = newIdx;
                        oldToNewIndex[i] = newIdx;
                    }
                }

                // サブメッシュごとに三角形面を生成（旧index -> 新index へ付け替え、
                // 各頂点のUV/法線サブindexは元値から逆引きする）
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    var srcTriangles = mesh.GetTriangles(subMesh);

                    for (int i = 0; i < srcTriangles.Length; i += 3)
                    {
                        int oldV0 = srcTriangles[i];
                        int oldV1 = srcTriangles[i + 1];
                        int oldV2 = srcTriangles[i + 2];

                        int v0 = oldToNewIndex[oldV0];
                        int v1 = oldToNewIndex[oldV1];
                        int v2 = oldToNewIndex[oldV2];

                        // UVと法線は同一スロットを指す（不変条件）。
                        int s0 = FindUVNormalIndex(target.Vertices[v0],
                            srcUVs     != null && oldV0 < srcUVs.Length     ? srcUVs[oldV0]     : Vector2.zero,
                            srcNormals != null && oldV0 < srcNormals.Length ? srcNormals[oldV0] : Vector3.up);
                        int s1 = FindUVNormalIndex(target.Vertices[v1],
                            srcUVs     != null && oldV1 < srcUVs.Length     ? srcUVs[oldV1]     : Vector2.zero,
                            srcNormals != null && oldV1 < srcNormals.Length ? srcNormals[oldV1] : Vector3.up);
                        int s2 = FindUVNormalIndex(target.Vertices[v2],
                            srcUVs     != null && oldV2 < srcUVs.Length     ? srcUVs[oldV2]     : Vector2.zero,
                            srcNormals != null && oldV2 < srcNormals.Length ? srcNormals[oldV2] : Vector3.up);

                        target.Faces.Add(Face.CreateTriangle(v0, v1, v2, s0, s1, s2, s0, s1, s2, subMesh));
                    }
                }
            }
            else
            {
                // 統合しない（Unity Mesh をそのまま再現）
                for (int i = 0; i < srcVerts.Length; i++)
                {
                    var vertex = new Vertex(srcVerts[i]);

                    vertex.UVs.Add((srcUVs != null && i < srcUVs.Length) ? srcUVs[i] : Vector2.zero);
                    vertex.Normals.Add((srcNormals != null && i < srcNormals.Length) ? srcNormals[i] : Vector3.up);

                    if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                        vertex.BoneWeight = srcBoneWeights[i];

                    target.Vertices.Add(vertex);
                }

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    var srcTriangles = mesh.GetTriangles(subMesh);

                    for (int i = 0; i < srcTriangles.Length; i += 3)
                    {
                        int v0 = srcTriangles[i];
                        int v1 = srcTriangles[i + 1];
                        int v2 = srcTriangles[i + 2];

                        target.Faces.Add(Face.CreateTriangle(v0, v1, v2, 0, 0, 0, 0, 0, 0, subMesh));
                    }
                }
            }

            // Unity Mesh から取り込んだ結果、ウェイトが入ったなら
            // 描画オブジェクトの種別を SkinnedMesh 系へ確定させる。
            // 種別を更新しないと、頂点はワールド（バインド）空間なのに
            // WorldMatrix 経路で描画され、位置が二重に掛かる。
            target.RecomputeSkinKind();
        }

        // ============================================================
        // 既存 Unity Mesh への上書き
        // ============================================================
        // 既存 Mesh インスタンスを保持したまま中身を差し替える（参照を壊さない）。
        // 注意: ここで `target.triangles = src.triangles` を使うため、サブメッシュ構造は
        //       1つに畳まれる（既存挙動を踏襲）。サブメッシュ維持が必要な経路では
        //       ToUnityMesh* で生成した Mesh をそのまま使うこと。
        public void RebuildMeshInPlace(Mesh target, MeshObject source)
        {
            if (target == null || source == null) return;

            var src = ToUnityMeshShared(source);

            target.Clear();
            target.vertices = src.vertices;
            target.triangles = src.triangles;
            target.uv = src.uv;
            target.normals = src.normals;
            target.RecalculateBounds();

            UnityEngine.Object.DestroyImmediate(src);
        }

        // 頂点位置のみ差し替える高速パス（三角形/UVは触らない）。
        public void ApplyVertexPositionsInPlace(Mesh target, MeshObject source)
        {
            if (target == null || source == null) return;

            var src = ToUnityMeshShared(source);
            target.vertices = src.vertices;
            if (source.PreserveNormals)
            {
                target.normals = src.normals;   // MeshObject 側の法線を維持
            }
            else
            {
                target.RecalculateNormals();

                // 法線再計算 除外セットの分だけ MeshObject 側の法線へ戻す
                var recalculated = target.normals;
                if (Poly_Ling.Ops.NormalHelper.RestoreExcludedNormals(
                        recalculated, src.normals, source))
                {
                    target.normals = recalculated;
                }
            }
            target.RecalculateBounds();

            UnityEngine.Object.DestroyImmediate(src);
        }

        // 三角形のみ差し替える高速パス（位置/UV/法線は触らない）。
        // 面の表示・非表示（Face.IsHidden）を切り替えたときに使う。
        // Mesh インスタンスを作り直さないのは、MeshUndoController.SetMeshObject が
        // 掴んでいる TargetMesh 参照を切らないため。
        // 展開頂点数が想定と違う場合は何もせず false を返す（呼び出し側で作り直すこと）。
        public bool ApplyTrianglesInPlace(Mesh target, MeshObject source, int materialCount = -1)
        {
            if (target == null || source == null) return false;
            if (source.Vertices.Count == 0) return false;

            int subMeshCount = materialCount > 0 ? materialCount : source.SubMeshCount;

            // ToUnityMeshShared と同一の名寄せ（頂点順 -> UV順）。頂点は作らず対応表だけ作る。
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            // 孤立頂点除外（規則は MeshExpansion に一本化。ToUnityMesh と同一）
            int expandedCount = 0;
            MeshExpansion.Enumerate(source, (vIdx, uvIdx, expIdx) =>
            {
                vertexMapping[(vIdx, uvIdx)] = expIdx;
                expandedCount = expIdx + 1;
            });

            // 頂点構成が変わっている場合はここでは扱えない
            if (expandedCount != target.vertexCount) return false;

            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in source.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= source.Vertices.Count)
                            continue;

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        var vertex = source.Vertices[vIdx];
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = 0;

                        if (vertexMapping.TryGetValue((vIdx, uvSubIdx), out int unityIdx))
                            subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            target.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                target.SetTriangles(subMeshTriangles[i], i);

            target.RecalculateBounds();
            return true;
        }

        // 法線のみ差し替える高速パス（位置/三角形/UVは触らない）。
        // 展開頂点数が変わっているときは Unity Mesh 側を作り直さないと
        // 対応が取れないため、何もせず false を返す。
        public bool ApplyNormalsInPlace(Mesh target, MeshObject source)
        {
            if (target == null || source == null) return false;

            var src = ToUnityMeshShared(source);
            bool applied = false;

            if (src.vertexCount == target.vertexCount)
            {
                target.normals = src.normals;
                applied = true;
            }

            UnityEngine.Object.DestroyImmediate(src);
            return applied;
        }

        // ============================================================
        // 内部ヘルパー（プラットフォーム非依存）
        // ============================================================
        // 指定UVが頂点のどのUVスロットに一致するかを返す（完全一致、無ければ0）。
        // 指定の (UV, 法線) の組が頂点のどのスロットに一致するかを返す（無ければ0）。
        // UVs.Count == Normals.Count の不変条件下では両者は同一スロットを指す。
        private int FindUVNormalIndex(Vertex vertex, Vector2 uv, Vector3 normal, float tolerance = 0.0001f)
        {
            int count = Mathf.Min(vertex.UVs.Count, vertex.Normals.Count);
            for (int i = 0; i < count; i++)
            {
                if (Vector2.Distance(vertex.UVs[i], uv) < tolerance &&
                    Vector3.Distance(vertex.Normals[i], normal) < tolerance)
                    return i;
            }
            return 0;
        }
    }
}
