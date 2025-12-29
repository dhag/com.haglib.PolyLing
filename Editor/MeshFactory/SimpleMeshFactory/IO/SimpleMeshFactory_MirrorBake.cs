// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_MirrorBake.cs
// ミラーベイク・SkinnedMeshRenderer関連機能
// - ミラーベイク（対称メッシュを実体化）
// - SkinnedMeshRenderer セットアップ
// - デフォルトマテリアル取得

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Symmetry;

public partial class SimpleMeshFactory
{
    // ================================================================
    // ミラーベイク
    // ================================================================

    /// <summary>
    /// ミラー（対称）をベイクしたUnity Meshを生成（頂点共有版）
    /// 頂点数・面数が2倍になり、サブメッシュは左右ペアで並ぶ
    /// 例: 元が[mat0, mat1]なら、結果は[左mat0, 右mat0, 左mat1, 右mat1]
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

        // 頂点データ
        var unityVerts = new List<Vector3>();
        var unityUVs = new List<Vector2>();
        var unityNormals = new List<Vector3>();

        // 頂点共有用マッピング（左側・右側で別々）
        // キー: (頂点インデックス, UVサブインデックス, 法線サブインデックス)
        var leftVertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();
        var rightVertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();

        // マテリアルインデックス → サブメッシュインデックス（左）
        var matToLeftSubMesh = new Dictionary<int, int>();
        var subMeshTriangles = new List<List<int>>();

        // ============================================
        // パス1: 左側（頂点共有版）
        // ============================================
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int matIdx = face.MaterialIndex;

            // 新しいマテリアルなら左右のサブメッシュを追加
            if (!matToLeftSubMesh.ContainsKey(matIdx))
            {
                matToLeftSubMesh[matIdx] = subMeshTriangles.Count;
                subMeshTriangles.Add(new List<int>()); // 左
                subMeshTriangles.Add(new List<int>()); // 右
                usedMaterialIndices.Add(matIdx);
            }

            int leftSubMesh = matToLeftSubMesh[matIdx];

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                for (int i = 0; i < 3; i++)
                {
                    int vIdx = tri.VertexIndices[i];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        vIdx = 0;

                    var vertex = meshObject.Vertices[vIdx];

                    // UVサブインデックスを取得
                    int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                    // 法線サブインデックスを取得
                    int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                    if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                        normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                    var key = (vIdx, uvSubIdx, normalSubIdx);

                    // 既存の頂点があるか確認
                    if (!leftVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        // 新しいUnity頂点を作成
                        unityIdx = unityVerts.Count;
                        leftVertexMapping[key] = unityIdx;

                        // 位置
                        unityVerts.Add(vertex.Position);

                        // UV
                        if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                            unityUVs.Add(vertex.UVs[uvSubIdx]);
                        else if (vertex.UVs.Count > 0)
                            unityUVs.Add(vertex.UVs[0]);
                        else
                            unityUVs.Add(Vector2.zero);

                        // 法線
                        if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                            unityNormals.Add(vertex.Normals[normalSubIdx]);
                        else if (vertex.Normals.Count > 0)
                            unityNormals.Add(vertex.Normals[0]);
                        else
                            unityNormals.Add(Vector3.up);
                    }

                    subMeshTriangles[leftSubMesh].Add(unityIdx);
                }
            }
        }

        int leftVertexCount = unityVerts.Count;
        Debug.Log($"[BakeMirror] Pass1 done: leftVertexCount={leftVertexCount}");

        // ============================================
        // パス2: 右側（頂点共有版、ミラー変換）
        // ============================================
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int rightSubMesh = matToLeftSubMesh[face.MaterialIndex] + 1;

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                // 右側は頂点順序を逆にして面を反転
                int[] triIndices = new int[3];

                for (int i = 0; i < 3; i++)
                {
                    // 逆順で処理（2, 1, 0）
                    int srcI = 2 - i;

                    int vIdx = tri.VertexIndices[srcI];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        vIdx = 0;

                    var vertex = meshObject.Vertices[vIdx];

                    // UVサブインデックスを取得
                    int uvSubIdx = srcI < tri.UVIndices.Count ? tri.UVIndices[srcI] : 0;
                    if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                        uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                    // 法線サブインデックスを取得
                    int normalSubIdx = srcI < tri.NormalIndices.Count ? tri.NormalIndices[srcI] : 0;
                    if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                        normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                    var key = (vIdx, uvSubIdx, normalSubIdx);

                    // 既存の頂点があるか確認
                    if (!rightVertexMapping.TryGetValue(key, out int unityIdx))
                    {
                        // 新しいUnity頂点を作成（ミラー変換）
                        unityIdx = unityVerts.Count;
                        rightVertexMapping[key] = unityIdx;

                        // 位置（ミラー）
                        unityVerts.Add(MirrorPosition(vertex.Position, axis));

                        // UV（flipU対応）
                        Vector2 uv;
                        if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                            uv = vertex.UVs[uvSubIdx];
                        else if (vertex.UVs.Count > 0)
                            uv = vertex.UVs[0];
                        else
                            uv = Vector2.zero;

                        if (flipU)
                            uv.x = 1f - uv.x;
                        unityUVs.Add(uv);

                        // 法線（ミラー）
                        Vector3 normal;
                        if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                            normal = vertex.Normals[normalSubIdx];
                        else if (vertex.Normals.Count > 0)
                            normal = vertex.Normals[0];
                        else
                            normal = Vector3.up;
                        unityNormals.Add(MirrorNormal(normal, axis));
                    }

                    triIndices[i] = unityIdx;
                }

                subMeshTriangles[rightSubMesh].Add(triIndices[0]);
                subMeshTriangles[rightSubMesh].Add(triIndices[1]);
                subMeshTriangles[rightSubMesh].Add(triIndices[2]);
            }
        }

        // 頂点数が多い場合は32ビットインデックスを使用
        //if (unityVerts.Count > 65535)
        if (true)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        // Meshに設定
        mesh.SetVertices(unityVerts);
        mesh.SetUVs(0, unityUVs);
        mesh.SetNormals(unityNormals);

        mesh.subMeshCount = subMeshTriangles.Count;
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            mesh.SetTriangles(subMeshTriangles[i], i);
        }

        mesh.RecalculateBounds();

        // デバッグ: 各サブメッシュのインデックス範囲を出力
        Debug.Log($"[BakeMirror] {meshObject.Name}: totalVerts={unityVerts.Count}, leftVertexCount={leftVertexCount}, rightStart={leftVertexCount}");
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            var tris = subMeshTriangles[i];
            if (tris.Count > 0)
            {
                int minIdx = tris.Min();
                int maxIdx = tris.Max();
                string side = (i % 2 == 0) ? "LEFT" : "RIGHT";
                string expected = (i % 2 == 0) ? $"should be < {leftVertexCount}" : $"should be >= {leftVertexCount}";
                Debug.Log($"[BakeMirror] SubMesh[{i}] ({side}): {tris.Count} indices, range [{minIdx} - {maxIdx}] {expected}");
            }
        }

        return mesh;
    }

    /// <summary>
    /// ベイクミラー用のマテリアル配列を構築
    /// </summary>
    private Material[] GetMaterialsForBakedMirror(List<int> usedMaterialIndices, Material[] baseMaterials)
    {
        var result = new Material[usedMaterialIndices.Count * 2];
        for (int i = 0; i < usedMaterialIndices.Count; i++)
        {
            int matIndex = usedMaterialIndices[i];
            Material mat = (matIndex >= 0 && matIndex < baseMaterials.Length)
                ? baseMaterials[matIndex]
                : GetOrCreateDefaultMaterial();
            result[i * 2] = mat;       // 左
            result[i * 2 + 1] = mat;   // 右
        }
        return result;
    }

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
