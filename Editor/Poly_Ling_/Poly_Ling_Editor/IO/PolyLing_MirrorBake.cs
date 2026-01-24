// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_MirrorBake.cs
// ミラーベイク・SkinnedMeshRenderer関連機能
// - ミラーベイク（対称メッシュを実体化）
// - SkinnedMeshRenderer セットアップ
// - デフォルトマテリアル取得
//
// サブメッシュ構成:
// - B方式（デフォルト）: [左mat0, 左mat1, ...] → [右mat0, 右mat1, ...]
// - C方式（_MaterialPaired）: [左mat0, 右mat0, 左mat1, 右mat1, ...]

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;

public partial class PolyLing
{
    // ================================================================
    // ミラーベイク（B方式：左右で分離）
    // ================================================================

    /// <summary>
    /// ミラー（対称）をベイクしたUnity Meshを生成（B方式：左右分離版）
    /// 頂点数・面数が2倍になり、サブメッシュは左側全部→右側全部の順で並ぶ
    /// 例: 元が[mat0, mat1]なら、結果は[左mat0, 左mat1, 右mat0, 右mat1]
    /// (頂点インデックス, UVサブインデックス, 法線サブインデックス) の組み合わせで頂点を共有
    /// </summary>
    private Mesh BakeMirrorToUnityMesh(MeshContext meshContext, bool flipU, out List<int> usedMaterialIndices)
    {
        var meshObject = meshContext.MeshObject;
        var mesh = new Mesh();
        mesh.name = meshObject.Name;
        usedMaterialIndices = new List<int>();

        if (meshObject.Vertices.Count == 0)
            return mesh;

        SymmetryAxis axis = meshContext.GetMirrorSymmetryAxis();
        bool hasBoneWeights = meshObject.IsSkinned;

        // 頂点データ
        var unityVerts = new List<Vector3>();
        var unityUVs = new List<Vector2>();
        var unityNormals = new List<Vector3>();
        var unityBoneWeights = new List<BoneWeight>();

        // === FPX仕様: 頂点順 → UV順 で頂点を展開 ===
        // キー: (頂点インデックス, UVサブインデックス)
        var leftVertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();
        var rightVertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

        // パス1: 左側頂点を頂点順→UV順で作成
        for (int vIdx = 0; vIdx < meshObject.Vertices.Count; vIdx++)
        {
            var vertex = meshObject.Vertices[vIdx];
            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

            for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
            {
                int unityIdx = unityVerts.Count;
                leftVertexMapping[(vIdx, uvIdx)] = unityIdx;

                // 位置
                unityVerts.Add(vertex.Position);

                // UV
                if (uvIdx < vertex.UVs.Count)
                    unityUVs.Add(vertex.UVs[uvIdx]);
                else
                    unityUVs.Add(Vector2.zero);

                // 法線（最初の法線を使用）
                if (vertex.Normals.Count > 0)
                    unityNormals.Add(vertex.Normals[0]);
                else
                    unityNormals.Add(Vector3.up);

                // BoneWeight（左側は実体側ウェイト）
                if (hasBoneWeights)
                {
                    unityBoneWeights.Add(vertex.BoneWeight ?? default);
                }
            }
        }

        int leftVertexCount = unityVerts.Count;

        // パス2: 右側頂点を頂点順→UV順で作成（ミラー変換）
        for (int vIdx = 0; vIdx < meshObject.Vertices.Count; vIdx++)
        {
            var vertex = meshObject.Vertices[vIdx];
            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

            for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
            {
                int unityIdx = unityVerts.Count;
                rightVertexMapping[(vIdx, uvIdx)] = unityIdx;

                // 位置（ミラー）
                unityVerts.Add(MirrorPosition(vertex.Position, axis));

                // UV（flipU対応）
                Vector2 uv;
                if (uvIdx < vertex.UVs.Count)
                    uv = vertex.UVs[uvIdx];
                else
                    uv = Vector2.zero;

                if (flipU)
                    uv.x = 1f - uv.x;
                unityUVs.Add(uv);

                // 法線（ミラー）
                Vector3 normal;
                if (vertex.Normals.Count > 0)
                    normal = vertex.Normals[0];
                else
                    normal = Vector3.up;
                unityNormals.Add(MirrorNormal(normal, axis));

                // BoneWeight（右側はミラー側ウェイト、なければ実体側ウェイト）
                if (hasBoneWeights)
                {
                    if (vertex.HasMirrorBoneWeight)
                    {
                        unityBoneWeights.Add(vertex.MirrorBoneWeight.Value);
                    }
                    else
                    {
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                    }
                }
            }
        }

        Debug.Log($"[BakeMirror-B] Vertices: left={leftVertexCount}, right={unityVerts.Count - leftVertexCount}, total={unityVerts.Count}, hasBoneWeights={hasBoneWeights}");

        // パス3: 使用マテリアルを収集
        var matToSubMesh = new Dictionary<int, int>();
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int matIdx = face.MaterialIndex;
            if (!matToSubMesh.ContainsKey(matIdx))
            {
                matToSubMesh[matIdx] = usedMaterialIndices.Count;
                usedMaterialIndices.Add(matIdx);
            }
        }

        int materialCount = usedMaterialIndices.Count;

        // サブメッシュ用三角形リスト（左全部 → 右全部）
        var leftSubMeshTriangles = new List<List<int>>();
        var rightSubMeshTriangles = new List<List<int>>();
        for (int i = 0; i < materialCount; i++)
        {
            leftSubMeshTriangles.Add(new List<int>());
            rightSubMeshTriangles.Add(new List<int>());
        }

        // パス4: 面の三角形インデックスを構築
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int subMeshIdx = matToSubMesh[face.MaterialIndex];

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                // 左側
                for (int i = 0; i < 3; i++)
                {
                    int vIdx = tri.VertexIndices[i];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        continue;

                    int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                    var vertex = meshObject.Vertices[vIdx];
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                    var key = (vIdx, uvSubIdx);
                    if (leftVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        leftSubMeshTriangles[subMeshIdx].Add(unityIdx);
                    }
                }

                // 右側（頂点順序を逆にして面を反転）
                for (int i = 0; i < 3; i++)
                {
                    int srcI = 2 - i; // 逆順
                    int vIdx = tri.VertexIndices[srcI];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        continue;

                    int uvSubIdx = srcI < tri.UVIndices.Count ? tri.UVIndices[srcI] : 0;
                    var vertex = meshObject.Vertices[vIdx];
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                    var key = (vIdx, uvSubIdx);
                    if (rightVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        rightSubMeshTriangles[subMeshIdx].Add(unityIdx);
                    }
                }
            }
        }

        // 32ビットインデックスを使用
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Meshに設定
        mesh.SetVertices(unityVerts);
        mesh.SetUVs(0, unityUVs);
        mesh.SetNormals(unityNormals);

        // BoneWeight設定
        if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
        {
            mesh.boneWeights = unityBoneWeights.ToArray();
            Debug.Log($"[BakeMirror-B] BoneWeights set: {unityBoneWeights.Count} weights");
        }

        // サブメッシュ設定（B方式：左全部 → 右全部）
        mesh.subMeshCount = materialCount * 2;

        // 左側サブメッシュ
        for (int i = 0; i < materialCount; i++)
        {
            mesh.SetTriangles(leftSubMeshTriangles[i], i);
        }
        // 右側サブメッシュ
        for (int i = 0; i < materialCount; i++)
        {
            mesh.SetTriangles(rightSubMeshTriangles[i], materialCount + i);
        }

        mesh.RecalculateBounds();

        // デバッグ: 各サブメッシュのインデックス範囲を出力
        Debug.Log($"[BakeMirror-B] {meshObject.Name}: totalVerts={unityVerts.Count}, leftVertexCount={leftVertexCount}, rightStart={leftVertexCount}");
        for (int i = 0; i < materialCount; i++)
        {
            var leftTris = leftSubMeshTriangles[i];
            var rightTris = rightSubMeshTriangles[i];
            if (leftTris.Count > 0)
            {
                Debug.Log($"[BakeMirror-B] SubMesh[{i}] (LEFT mat{usedMaterialIndices[i]}): {leftTris.Count} indices, range [{leftTris.Min()} - {leftTris.Max()}]");
            }
            if (rightTris.Count > 0)
            {
                Debug.Log($"[BakeMirror-B] SubMesh[{materialCount + i}] (RIGHT mat{usedMaterialIndices[i]}): {rightTris.Count} indices, range [{rightTris.Min()} - {rightTris.Max()}]");
            }
        }

        return mesh;
    }

    /// <summary>
    /// ベイクミラー用のマテリアル配列を構築（B方式：左右分離版）
    /// [mat0, mat1, ..., mat0+offset, mat1+offset, ...]
    /// </summary>
    private Material[] GetMaterialsForBakedMirror(List<int> usedMaterialIndices, Material[] baseMaterials, int mirrorMaterialOffset = 0)
    {
        int materialCount = usedMaterialIndices.Count;
        var result = new Material[materialCount * 2];

        // 左側マテリアル（実体側）
        for (int i = 0; i < materialCount; i++)
        {
            int matIndex = usedMaterialIndices[i];
            Material mat = (matIndex >= 0 && matIndex < baseMaterials.Length)
                ? baseMaterials[matIndex]
                : GetOrCreateDefaultMaterial();
            result[i] = mat;
        }

        // 右側マテリアル（ミラー側：オフセット適用）
        for (int i = 0; i < materialCount; i++)
        {
            int matIndex = usedMaterialIndices[i] + mirrorMaterialOffset;
            Material mat = (matIndex >= 0 && matIndex < baseMaterials.Length)
                ? baseMaterials[matIndex]
                : GetOrCreateDefaultMaterial();
            result[materialCount + i] = mat;
        }

        return result;
    }

    // ================================================================
    // ミラーベイク（C方式：材質ごとに左右ペア）※後方互換用
    // ================================================================

    /// <summary>
    /// ミラー（対称）をベイクしたUnity Meshを生成（C方式：材質ペア版）
    /// 頂点数・面数が2倍になり、サブメッシュは左右ペアで並ぶ
    /// 例: 元が[mat0, mat1]なら、結果は[左mat0, 右mat0, 左mat1, 右mat1]
    /// FPX仕様: 頂点順 → UV順 で頂点を展開
    /// </summary>
    private Mesh BakeMirrorToUnityMesh_MaterialPaired(MeshContext meshContext, bool flipU, out List<int> usedMaterialIndices)
    {
        var meshObject = meshContext.MeshObject;
        var mesh = new Mesh();
        mesh.name = meshObject.Name;
        usedMaterialIndices = new List<int>();

        if (meshObject.Vertices.Count == 0)
            return mesh;

        SymmetryAxis axis = meshContext.GetMirrorSymmetryAxis();
        bool hasBoneWeights = meshObject.IsSkinned;

        // 頂点データ
        var unityVerts = new List<Vector3>();
        var unityUVs = new List<Vector2>();
        var unityNormals = new List<Vector3>();
        var unityBoneWeights = new List<BoneWeight>();

        // === FPX仕様: 頂点順 → UV順 で頂点を展開 ===
        var leftVertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();
        var rightVertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

        // パス1: 左側頂点を頂点順→UV順で作成
        for (int vIdx = 0; vIdx < meshObject.Vertices.Count; vIdx++)
        {
            var vertex = meshObject.Vertices[vIdx];
            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

            for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
            {
                int unityIdx = unityVerts.Count;
                leftVertexMapping[(vIdx, uvIdx)] = unityIdx;

                unityVerts.Add(vertex.Position);

                if (uvIdx < vertex.UVs.Count)
                    unityUVs.Add(vertex.UVs[uvIdx]);
                else
                    unityUVs.Add(Vector2.zero);

                if (vertex.Normals.Count > 0)
                    unityNormals.Add(vertex.Normals[0]);
                else
                    unityNormals.Add(Vector3.up);

                if (hasBoneWeights)
                {
                    unityBoneWeights.Add(vertex.BoneWeight ?? default);
                }
            }
        }

        int leftVertexCount = unityVerts.Count;

        // パス2: 右側頂点を頂点順→UV順で作成（ミラー変換）
        for (int vIdx = 0; vIdx < meshObject.Vertices.Count; vIdx++)
        {
            var vertex = meshObject.Vertices[vIdx];
            int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

            for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
            {
                int unityIdx = unityVerts.Count;
                rightVertexMapping[(vIdx, uvIdx)] = unityIdx;

                unityVerts.Add(MirrorPosition(vertex.Position, axis));

                Vector2 uv;
                if (uvIdx < vertex.UVs.Count)
                    uv = vertex.UVs[uvIdx];
                else
                    uv = Vector2.zero;

                if (flipU)
                    uv.x = 1f - uv.x;
                unityUVs.Add(uv);

                Vector3 normal;
                if (vertex.Normals.Count > 0)
                    normal = vertex.Normals[0];
                else
                    normal = Vector3.up;
                unityNormals.Add(MirrorNormal(normal, axis));

                if (hasBoneWeights)
                {
                    if (vertex.HasMirrorBoneWeight)
                        unityBoneWeights.Add(vertex.MirrorBoneWeight.Value);
                    else
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                }
            }
        }

        Debug.Log($"[BakeMirror-C] Vertices: left={leftVertexCount}, right={unityVerts.Count - leftVertexCount}, total={unityVerts.Count}, hasBoneWeights={hasBoneWeights}");

        // パス3: 使用マテリアルを収集
        var matToSubMesh = new Dictionary<int, int>();
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int matIdx = face.MaterialIndex;
            if (!matToSubMesh.ContainsKey(matIdx))
            {
                matToSubMesh[matIdx] = usedMaterialIndices.Count;
                usedMaterialIndices.Add(matIdx);
            }
        }

        // サブメッシュ用三角形リスト（C方式：左右ペア）
        var subMeshTriangles = new List<List<int>>();
        for (int i = 0; i < usedMaterialIndices.Count * 2; i++)
        {
            subMeshTriangles.Add(new List<int>());
        }

        // パス4: 面の三角形インデックスを構築
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int subMeshIdx = matToSubMesh[face.MaterialIndex];
            int leftSubMesh = subMeshIdx * 2;
            int rightSubMesh = subMeshIdx * 2 + 1;

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                // 左側
                for (int i = 0; i < 3; i++)
                {
                    int vIdx = tri.VertexIndices[i];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        continue;

                    int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                    var vertex = meshObject.Vertices[vIdx];
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                    var key = (vIdx, uvSubIdx);
                    if (leftVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        subMeshTriangles[leftSubMesh].Add(unityIdx);
                    }
                }

                // 右側（頂点順序を逆にして面を反転）
                for (int i = 0; i < 3; i++)
                {
                    int srcI = 2 - i;
                    int vIdx = tri.VertexIndices[srcI];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        continue;

                    int uvSubIdx = srcI < tri.UVIndices.Count ? tri.UVIndices[srcI] : 0;
                    var vertex = meshObject.Vertices[vIdx];
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                    var key = (vIdx, uvSubIdx);
                    if (rightVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        subMeshTriangles[rightSubMesh].Add(unityIdx);
                    }
                }
            }
        }

        // 32ビットインデックスを使用
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Meshに設定
        mesh.SetVertices(unityVerts);
        mesh.SetUVs(0, unityUVs);
        mesh.SetNormals(unityNormals);

        // BoneWeight設定
        if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
        {
            mesh.boneWeights = unityBoneWeights.ToArray();
            Debug.Log($"[BakeMirror-C] BoneWeights set: {unityBoneWeights.Count} weights");
        }

        mesh.subMeshCount = subMeshTriangles.Count;
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            mesh.SetTriangles(subMeshTriangles[i], i);
        }

        mesh.RecalculateBounds();

        // デバッグ
        Debug.Log($"[BakeMirror-C] {meshObject.Name}: totalVerts={unityVerts.Count}, leftVertexCount={leftVertexCount}, rightStart={leftVertexCount}");
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            var tris = subMeshTriangles[i];
            if (tris.Count > 0)
            {
                int minIdx = tris.Min();
                int maxIdx = tris.Max();
                string side = (i % 2 == 0) ? "LEFT" : "RIGHT";
                string expected = (i % 2 == 0) ? $"should be < {leftVertexCount}" : $"should be >= {leftVertexCount}";
                Debug.Log($"[BakeMirror-C] SubMesh[{i}] ({side}): {tris.Count} indices, range [{minIdx} - {maxIdx}] {expected}");
            }
        }

        return mesh;
    }

    /// <summary>
    /// ベイクミラー用のマテリアル配列を構築（C方式：材質ペア版）
    /// [mat0, mat0+offset, mat1, mat1+offset, ...]
    /// </summary>
    private Material[] GetMaterialsForBakedMirror_MaterialPaired(List<int> usedMaterialIndices, Material[] baseMaterials, int mirrorMaterialOffset = 0)
    {
        var result = new Material[usedMaterialIndices.Count * 2];
        for (int i = 0; i < usedMaterialIndices.Count; i++)
        {
            int matIndex = usedMaterialIndices[i];
            
            // 左側（実体側）
            Material leftMat = (matIndex >= 0 && matIndex < baseMaterials.Length)
                ? baseMaterials[matIndex]
                : GetOrCreateDefaultMaterial();
            result[i * 2] = leftMat;
            
            // 右側（ミラー側：オフセット適用）
            int mirrorMatIndex = matIndex + mirrorMaterialOffset;
            Material rightMat = (mirrorMatIndex >= 0 && mirrorMatIndex < baseMaterials.Length)
                ? baseMaterials[mirrorMatIndex]
                : GetOrCreateDefaultMaterial();
            result[i * 2 + 1] = rightMat;
        }
        return result;
    }

    // ================================================================
    // ヘルパーメソッド
    // ================================================================

    /// <summary>
    /// 位置をミラー
    /// </summary>
    private Vector3 MirrorPosition(Vector3 pos, SymmetryAxis axis)
    {
        switch (axis)
        {
            case SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
            case SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
            case SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
            default: return new Vector3(-pos.x, pos.y, pos.z);
        }
    }

    /// <summary>
    /// 法線をミラー
    /// </summary>
    private Vector3 MirrorNormal(Vector3 normal, SymmetryAxis axis)
    {
        switch (axis)
        {
            case SymmetryAxis.X: return new Vector3(-normal.x, normal.y, normal.z);
            case SymmetryAxis.Y: return new Vector3(normal.x, -normal.y, normal.z);
            case SymmetryAxis.Z: return new Vector3(normal.x, normal.y, -normal.z);
            default: return new Vector3(-normal.x, normal.y, normal.z);
        }
    }

    /// <summary>
    /// デフォルトマテリアルを取得または作成
    /// </summary>
    private Material GetOrCreateDefaultMaterial()
    {
        // 既存のデフォルトマテリアルを探す
        string[] guids = AssetDatabase.FindAssets("t:Material Default-Material");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
        }

        // URPのLitシェーダーでマテリアル作成
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            mat.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            return mat;
        }

        return null;
    }

    // ================================================================
    // SkinnedMeshRenderer 対応
    // ================================================================

    /// <summary>
    /// BoneWeight が未設定の頂点にデフォルト値（boneIndex0 に 100%）を設定
    /// </summary>
    /// <param name="meshObject">対象の MeshObject</param>
    /// <param name="defaultBoneIndex">デフォルトのボーンインデックス（通常は自身のメッシュインデックス）</param>
    private void EnsureBoneWeights(MeshObject meshObject, int defaultBoneIndex)
    {
        if (meshObject == null) return;

        foreach (var vertex in meshObject.Vertices)
        {
            if (!vertex.HasBoneWeight)
            {
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = defaultBoneIndex,
                    weight0 = 1f,
                    boneIndex1 = 0,
                    weight1 = 0f,
                    boneIndex2 = 0,
                    weight2 = 0f,
                    boneIndex3 = 0,
                    weight3 = 0f
                };
            }
        }
    }

    /// <summary>
    /// BoneWeight を設定し、マスターインデックスからボーンリストインデックスに変換
    /// </summary>
    /// <param name="meshObject">対象の MeshObject</param>
    /// <param name="defaultBoneIndex">デフォルトのボーンインデックス（ボーンリスト内）</param>
    /// <param name="typedIndices">インデックス変換用のTypedMeshIndices</param>
    private void EnsureBoneWeightsWithConversion(MeshObject meshObject, int defaultBoneIndex, TypedMeshIndices typedIndices)
    {
        if (meshObject == null) return;

        foreach (var vertex in meshObject.Vertices)
        {
            if (!vertex.HasBoneWeight)
            {
                // 未設定の場合はデフォルト値を設定
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = defaultBoneIndex,
                    weight0 = 1f,
                    boneIndex1 = 0,
                    weight1 = 0f,
                    boneIndex2 = 0,
                    weight2 = 0f,
                    boneIndex3 = 0,
                    weight3 = 0f
                };
            }
            else
            {
                // 既存のBoneWeightのインデックスを変換
                var bw = vertex.BoneWeight.Value;
                vertex.BoneWeight = typedIndices.ConvertBoneWeightToLocal(bw);
            }
        }
    }

    /// <summary>
    /// SkinnedMeshRenderer をセットアップ
    /// </summary>
    /// <param name="go">対象の GameObject</param>
    /// <param name="mesh">設定するメッシュ</param>
    /// <param name="bones">ボーン Transform 配列</param>
    /// <param name="bindPoses">バインドポーズ行列配列</param>
    /// <param name="materials">マテリアル配列</param>
    private void SetupSkinnedMeshRenderer(
        GameObject go,
        Mesh mesh,
        Transform[] bones,
        Matrix4x4[] bindPoses,
        Material[] materials)
    {
        if (go == null || mesh == null) return;

        // SkinnedMeshRenderer を追加
        var smr = go.AddComponent<SkinnedMeshRenderer>();

        // バインドポーズを設定
        mesh.bindposes = bindPoses;

        // メッシュとボーンを設定
        smr.sharedMesh = mesh;
        smr.bones = bones;

        // マテリアル設定
        if (materials != null && materials.Length > 0)
        {
            smr.sharedMaterials = materials;
        }

        // ルートボーンを設定（最初のボーン）
        if (bones != null && bones.Length > 0)
        {
            smr.rootBone = bones[0];
        }
    }
}
