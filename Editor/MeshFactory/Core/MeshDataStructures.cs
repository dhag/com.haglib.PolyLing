// Assets/Editor/MeshData/MeshDataStructures.cs
// 頂点ベースのメッシュデータ構造
// - Vertex: 位置 + 複数UV + 複数法線
// - Face: N角形対応（三角形、四角形、Nゴン）+ マテリアルインデックス
// - MeshData: Unity Mesh との相互変換（サブメッシュ対応）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MeshFactory.Data
{
    // ============================================================
    // Vertex クラス
    // ============================================================

    /// <summary>
    /// 頂点データ
    /// 位置と、複数のUV/法線を保持（シーム・ハードエッジ対応）
    /// </summary>
    [Serializable]
    public class Vertex
    {
        /// <summary>頂点位置</summary>
        public Vector3 Position;

        /// <summary>UV座標リスト（面から UVIndices で参照）</summary>
        public List<Vector2> UVs = new List<Vector2>();

        /// <summary>法線リスト（面から NormalIndices で参照）</summary>
        public List<Vector3> Normals = new List<Vector3>();

        // === コンストラクタ ===

        public Vertex()
        {
            Position = Vector3.zero;
        }

        public Vertex(Vector3 position)
        {
            Position = position;
        }

        public Vertex(Vector3 position, Vector2 uv)
        {
            Position = position;
            UVs.Add(uv);
        }

        public Vertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Position = position;
            UVs.Add(uv);
            Normals.Add(normal);
        }

        // === ユーティリティ ===

        /// <summary>
        /// UVを追加し、インデックスを返す
        /// </summary>
        public int AddUV(Vector2 uv)
        {
            UVs.Add(uv);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線を追加し、インデックスを返す
        /// </summary>
        public int AddNormal(Vector3 normal)
        {
            Normals.Add(normal);
            return Normals.Count - 1;
        }

        /// <summary>
        /// 同一UVが既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddUV(Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < UVs.Count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance)
                    return i;
            }
            return AddUV(uv);
        }

        /// <summary>
        /// 同一法線が既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddNormal(Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < Normals.Count; i++)
            {
                if (Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }
            return AddNormal(normal);
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public Vertex Clone()
        {
            return new Vertex
            {
                Position = Position,
                UVs = new List<Vector2>(UVs),
                Normals = new List<Vector3>(Normals)
            };
        }
    }

    // ============================================================
    // Face クラス
    // ============================================================

    /// <summary>
    /// 面データ（N角形対応）
    /// 頂点インデックスと、各頂点のUV/法線サブインデックス、マテリアルインデックスを保持
    /// </summary>
    [Serializable]
    public class Face
    {
        /// <summary>頂点インデックスリスト（Vertex配列への参照）</summary>
        public List<int> VertexIndices = new List<int>();

        /// <summary>各頂点のUVサブインデックス（Vertex.UVs[n]への参照）</summary>
        public List<int> UVIndices = new List<int>();

        /// <summary>各頂点の法線サブインデックス（Vertex.Normals[n]への参照）</summary>
        public List<int> NormalIndices = new List<int>();

        /// <summary>マテリアルインデックス（MeshEntry.Materialsへの参照）</summary>
        public int MaterialIndex = 0;

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => VertexIndices.Count;

        /// <summary>三角形か</summary>
        public bool IsTriangle => VertexCount == 3;

        /// <summary>四角形か</summary>
        public bool IsQuad => VertexCount == 4;

        /// <summary>有効な面か（3頂点以上）</summary>
        public bool IsValid => VertexCount >= 3;

        // === コンストラクタ ===

        public Face() { }

        /// <summary>
        /// 三角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2 });
            UVIndices.AddRange(new[] { 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 四角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int v3, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2, v3 });
            UVIndices.AddRange(new[] { 0, 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 完全指定で三角形を作成
        /// </summary>
        public static Face CreateTriangle(
            int v0, int v1, int v2,
            int uv0, int uv1, int uv2,
            int n0, int n1, int n2,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2 },
                UVIndices = new List<int> { uv0, uv1, uv2 },
                NormalIndices = new List<int> { n0, n1, n2 },
                MaterialIndex = materialIndex
            };
        }

        /// <summary>
        /// 完全指定で四角形を作成
        /// </summary>
        public static Face CreateQuad(
            int v0, int v1, int v2, int v3,
            int uv0, int uv1, int uv2, int uv3,
            int n0, int n1, int n2, int n3,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2, v3 },
                UVIndices = new List<int> { uv0, uv1, uv2, uv3 },
                NormalIndices = new List<int> { n0, n1, n2, n3 },
                MaterialIndex = materialIndex
            };
        }

        // === 三角形分解 ===

        /// <summary>
        /// 三角形インデックスに分解（扇形分割）
        /// </summary>
        /// <returns>三角形数 × 3 のインデックス配列</returns>
        public int[] ToTriangleIndices()
        {
            if (VertexCount < 3)
                return Array.Empty<int>();

            if (IsTriangle)
                return VertexIndices.ToArray();

            // 扇形分割: v0 を中心に (v0, v1, v2), (v0, v2, v3), ... 
            var result = new List<int>();
            for (int i = 1; i < VertexCount - 1; i++)
            {
                result.Add(VertexIndices[0]);
                result.Add(VertexIndices[i]);
                result.Add(VertexIndices[i + 1]);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 三角形に分解してFaceリストを返す（MaterialIndex引き継ぎ）
        /// </summary>
        public List<Face> Triangulate()
        {
            var result = new List<Face>();

            if (VertexCount < 3)
                return result;

            if (IsTriangle)
            {
                result.Add(Clone());
                return result;
            }

            // 扇形分割（MaterialIndexを引き継ぐ）
            for (int i = 1; i < VertexCount - 1; i++)
            {
                var tri = Face.CreateTriangle(
                    VertexIndices[0], VertexIndices[i], VertexIndices[i + 1],
                    UVIndices[0], UVIndices[i], UVIndices[i + 1],
                    NormalIndices[0], NormalIndices[i], NormalIndices[i + 1],
                    MaterialIndex  // マテリアルインデックスを引き継ぐ
                );
                result.Add(tri);
            }

            return result;
        }

        /// <summary>
        /// 三角形数を取得
        /// </summary>
        public int TriangleCount => VertexCount >= 3 ? VertexCount - 2 : 0;

        // === ユーティリティ ===

        /// <summary>
        /// 面を反転（頂点順序を逆に）
        /// </summary>
        public void Flip()
        {
            VertexIndices.Reverse();
            UVIndices.Reverse();
            NormalIndices.Reverse();
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public Face Clone()
        {
            return new Face
            {
                VertexIndices = new List<int>(VertexIndices),
                UVIndices = new List<int>(UVIndices),
                NormalIndices = new List<int>(NormalIndices),
                MaterialIndex = MaterialIndex
            };
        }
    }

    // ============================================================
    // MeshData クラス
    // ============================================================

    /// <summary>
    /// メッシュデータ（頂点 + 面）
    /// Unity Mesh との相互変換機能付き（サブメッシュ対応）
    /// </summary>
    [Serializable]
    public class MeshData
    {
        /// <summary>頂点リスト</summary>
        public List<Vertex> Vertices = new List<Vertex>();

        /// <summary>面リスト</summary>
        public List<Face> Faces = new List<Face>();

        /// <summary>メッシュ名</summary>
        public string Name = "Mesh";

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => Vertices.Count;

        /// <summary>面数</summary>
        public int FaceCount => Faces.Count;

        /// <summary>三角形数（全ての面を三角形化した場合）</summary>
        public int TriangleCount => Faces.Sum(f => f.TriangleCount);

        /// <summary>使用されているマテリアルインデックスの最大値+1（サブメッシュ数）</summary>
        public int SubMeshCount
        {
            get
            {
                if (Faces.Count == 0) return 1;
                return Faces.Max(f => f.MaterialIndex) + 1;
            }
        }

        // === コンストラクタ ===

        public MeshData() { }

        public MeshData(string name)
        {
            Name = name;
        }

        // === 頂点操作 ===

        /// <summary>
        /// 頂点を追加
        /// </summary>
        public int AddVertex(Vector3 position)
        {
            Vertices.Add(new Vertex(position));
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV付き）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv)
        {
            Vertices.Add(new Vertex(position, uv));
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV + 法線付き）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Vertices.Add(new Vertex(position, uv, normal));
            return Vertices.Count - 1;
        }

        // === 面操作 ===

        /// <summary>
        /// 三角形を追加
        /// </summary>
        public void AddTriangle(int v0, int v1, int v2, int materialIndex = 0)
        {
            Faces.Add(new Face(v0, v1, v2, materialIndex));
        }

        /// <summary>
        /// 四角形を追加
        /// </summary>
        public void AddQuad(int v0, int v1, int v2, int v3, int materialIndex = 0)
        {
            Faces.Add(new Face(v0, v1, v2, v3, materialIndex));
        }

        /// <summary>
        /// 面を追加
        /// </summary>
        public void AddFace(Face face)
        {
            Faces.Add(face);
        }

        // === Unity Mesh 変換 ===

        /// <summary>
        /// Unity Mesh に変換（サブメッシュ対応）
        /// 全ての面を三角形に展開し、頂点を複製してUnity形式に変換
        /// MaterialIndexごとにサブメッシュを生成
        /// </summary>
        /// <summary>
        /// Unity Mesh に変換（サブメッシュ対応）
        /// Face.MaterialIndex に基づいてサブメッシュを分割
        /// </summary>
        public Mesh ToUnityMesh()
        {
            var mesh = new Mesh { name = Name };

            // Unity Mesh 用のリスト（展開後）
            var unityVertices = new List<Vector3>();
            var unityNormals = new List<Vector3>();
            var unityUVs = new List<Vector2>();

            // サブメッシュごとの三角形インデックス
            var subMeshTriangles = new Dictionary<int, List<int>>();

            foreach (var face in Faces)
            {
                if (!face.IsValid)
                    continue;

                int matIndex = face.MaterialIndex;

                // サブメッシュリストを確保
                if (!subMeshTriangles.ContainsKey(matIndex))
                {
                    subMeshTriangles[matIndex] = new List<int>();
                }

                // 三角形に分解
                var triangles = face.Triangulate();

                foreach (var tri in triangles)
                {
                    int startIdx = unityVertices.Count;

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        int uvIdx = tri.UVIndices[i];
                        int nIdx = tri.NormalIndices[i];

                        var vertex = Vertices[vIdx];

                        unityVertices.Add(vertex.Position);

                        // UV取得（範囲チェック）
                        if (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                            unityUVs.Add(vertex.UVs[uvIdx]);
                        else if (vertex.UVs.Count > 0)
                            unityUVs.Add(vertex.UVs[0]);
                        else
                            unityUVs.Add(Vector2.zero);

                        // 法線取得（範囲チェック）
                        if (nIdx >= 0 && nIdx < vertex.Normals.Count)
                            unityNormals.Add(vertex.Normals[nIdx]);
                        else if (vertex.Normals.Count > 0)
                            unityNormals.Add(vertex.Normals[0]);
                        else
                            unityNormals.Add(Vector3.zero);

                        subMeshTriangles[matIndex].Add(startIdx + i);
                    }
                }
            }

            mesh.vertices = unityVertices.ToArray();
            mesh.uv = unityUVs.ToArray();

            // 法線: 有効なデータがあれば設定、なければ自動計算
            if (unityNormals.Any(n => n != Vector3.zero))
                mesh.normals = unityNormals.ToArray();

            // サブメッシュ数を決定（使用されている最大MaterialIndex + 1）
            int subMeshCount = subMeshTriangles.Count > 0
                ? subMeshTriangles.Keys.Max() + 1
                : 1;

            mesh.subMeshCount = subMeshCount;

            // 各サブメッシュに三角形を設定
            for (int i = 0; i < subMeshCount; i++)
            {
                if (subMeshTriangles.TryGetValue(i, out var tris))
                {
                    mesh.SetTriangles(tris, i);
                }
                else
                {
                    // 空のサブメッシュ
                    mesh.SetTriangles(new int[0], i);
                }
            }

            // 法線が無効だった場合は再計算
            if (!unityNormals.Any(n => n != Vector3.zero))
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            return mesh;
        }
        /// <summary>
        /// Unity Mesh に変換（単一マテリアル、後方互換用）
        /// </summary>
        public Mesh ToUnityMeshSingleMaterial()
        {
            var mesh = new Mesh { name = Name };

            var unityVertices = new List<Vector3>();
            var unityNormals = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityTriangles = new List<int>();

            foreach (var face in Faces)
            {
                if (!face.IsValid)
                    continue;

                var triangles = face.Triangulate();

                foreach (var tri in triangles)
                {
                    int startIdx = unityVertices.Count;

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        int uvIdx = tri.UVIndices[i];
                        int nIdx = tri.NormalIndices[i];

                        var vertex = Vertices[vIdx];

                        unityVertices.Add(vertex.Position);

                        if (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                            unityUVs.Add(vertex.UVs[uvIdx]);
                        else if (vertex.UVs.Count > 0)
                            unityUVs.Add(vertex.UVs[0]);
                        else
                            unityUVs.Add(Vector2.zero);

                        if (nIdx >= 0 && nIdx < vertex.Normals.Count)
                            unityNormals.Add(vertex.Normals[nIdx]);
                        else if (vertex.Normals.Count > 0)
                            unityNormals.Add(vertex.Normals[0]);
                        else
                            unityNormals.Add(Vector3.zero);

                        unityTriangles.Add(startIdx + i);
                    }
                }
            }

            mesh.vertices = unityVertices.ToArray();
            mesh.uv = unityUVs.ToArray();
            mesh.triangles = unityTriangles.ToArray();

            bool hasValidNormals = unityNormals.Any(n => n.sqrMagnitude > 0.001f);
            if (hasValidNormals)
            {
                mesh.normals = unityNormals.ToArray();
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();

            return mesh;
        }

        // === Unity Mesh からインポート ===

        /// <summary>
        /// Unity Mesh からインポート
        /// </summary>
        /// <param name="mesh">ソースメッシュ</param>
        /// <param name="mergeVertices">同一位置の頂点をマージするか</param>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices = true)
        {
            Clear();

            if (mesh == null)
                return;

            Name = mesh.name;

            var srcVertices = mesh.vertices;
            var srcNormals = mesh.normals;
            var srcUVs = mesh.uv;

            if (mergeVertices)
            {
                FromUnityMeshMerged(srcVertices, srcUVs, srcNormals, mesh);
            }
            else
            {
                FromUnityMeshDirect(srcVertices, srcUVs, srcNormals, mesh);
            }
        }

        /// <summary>
        /// 同一位置の頂点をマージしてインポート（サブメッシュ対応）
        /// </summary>
        private void FromUnityMeshMerged(
            Vector3[] srcVertices, Vector2[] srcUVs, Vector3[] srcNormals, Mesh mesh)
        {
            // 位置→ローカル頂点インデックスのマップ
            var positionToIndex = new Dictionary<Vector3, int>(new Vector3Comparer());
            var unityToLocalIndex = new int[srcVertices.Length];

            // 頂点をマージ
            for (int i = 0; i < srcVertices.Length; i++)
            {
                Vector3 pos = srcVertices[i];

                if (!positionToIndex.TryGetValue(pos, out int localIdx))
                {
                    localIdx = Vertices.Count;
                    positionToIndex[pos] = localIdx;

                    var vertex = new Vertex(pos);
                    Vertices.Add(vertex);
                }

                unityToLocalIndex[i] = localIdx;

                // UV/法線を追加（頂点側に保存）
                var localVertex = Vertices[localIdx];

                if (srcUVs != null && i < srcUVs.Length)
                {
                    localVertex.GetOrAddUV(srcUVs[i]);
                }

                if (srcNormals != null && i < srcNormals.Length)
                {
                    localVertex.GetOrAddNormal(srcNormals[i]);
                }
            }

            // サブメッシュごとに三角形を面に変換
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var srcTriangles = mesh.GetTriangles(subMesh);

                for (int i = 0; i < srcTriangles.Length; i += 3)
                {
                    int ui0 = srcTriangles[i];
                    int ui1 = srcTriangles[i + 1];
                    int ui2 = srcTriangles[i + 2];

                    int v0 = unityToLocalIndex[ui0];
                    int v1 = unityToLocalIndex[ui1];
                    int v2 = unityToLocalIndex[ui2];

                    // UV/法線インデックスを検索
                    Vector2 uv0 = (srcUVs != null && ui0 < srcUVs.Length) ? srcUVs[ui0] : Vector2.zero;
                    Vector2 uv1 = (srcUVs != null && ui1 < srcUVs.Length) ? srcUVs[ui1] : Vector2.zero;
                    Vector2 uv2 = (srcUVs != null && ui2 < srcUVs.Length) ? srcUVs[ui2] : Vector2.zero;

                    int uvIdx0 = FindUVIndex(Vertices[v0], uv0);
                    int uvIdx1 = FindUVIndex(Vertices[v1], uv1);
                    int uvIdx2 = FindUVIndex(Vertices[v2], uv2);

                    Vector3 n0 = (srcNormals != null && ui0 < srcNormals.Length) ? srcNormals[ui0] : Vector3.zero;
                    Vector3 n1 = (srcNormals != null && ui1 < srcNormals.Length) ? srcNormals[ui1] : Vector3.zero;
                    Vector3 n2 = (srcNormals != null && ui2 < srcNormals.Length) ? srcNormals[ui2] : Vector3.zero;

                    int nIdx0 = FindNormalIndex(Vertices[v0], n0);
                    int nIdx1 = FindNormalIndex(Vertices[v1], n1);
                    int nIdx2 = FindNormalIndex(Vertices[v2], n2);

                    // サブメッシュインデックスをMaterialIndexとして設定
                    Faces.Add(Face.CreateTriangle(v0, v1, v2, uvIdx0, uvIdx1, uvIdx2, nIdx0, nIdx1, nIdx2, subMesh));
                }
            }
        }

        /// <summary>
        /// そのままインポート（頂点マージなし、サブメッシュ対応）
        /// </summary>
        private void FromUnityMeshDirect(
            Vector3[] srcVertices, Vector2[] srcUVs, Vector3[] srcNormals, Mesh mesh)
        {
            for (int i = 0; i < srcVertices.Length; i++)
            {
                var vertex = new Vertex(srcVertices[i]);

                if (srcUVs != null && i < srcUVs.Length)
                    vertex.UVs.Add(srcUVs[i]);
                else
                    vertex.UVs.Add(Vector2.zero);

                if (srcNormals != null && i < srcNormals.Length)
                    vertex.Normals.Add(srcNormals[i]);

                Vertices.Add(vertex);
            }

            // サブメッシュごとに処理
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var srcTriangles = mesh.GetTriangles(subMesh);

                for (int i = 0; i < srcTriangles.Length; i += 3)
                {
                    int v0 = srcTriangles[i];
                    int v1 = srcTriangles[i + 1];
                    int v2 = srcTriangles[i + 2];

                    Faces.Add(Face.CreateTriangle(v0, v1, v2, 0, 0, 0, 0, 0, 0, subMesh));
                }
            }
        }

        private int FindUVIndex(Vertex vertex, Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                if (Vector2.Distance(vertex.UVs[i], uv) < tolerance)
                    return i;
            }
            return 0;
        }

        private int FindNormalIndex(Vertex vertex, Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.Normals.Count; i++)
            {
                if (Vector3.Distance(vertex.Normals[i], normal) < tolerance)
                    return i;
            }
            return 0;
        }

        // === ユーティリティ ===

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Faces.Clear();
        }

        /// <summary>
        /// 全ての面の法線を自動計算
        /// </summary>
        public void RecalculateNormals()
        {
            // 各頂点の法線をクリア
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            // 各面ごとに法線を計算
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                // 面法線を計算
                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // 各頂点に法線を追加
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    var vertex = Vertices[face.VertexIndices[i]];
                    int nIdx = vertex.GetOrAddNormal(faceNormal);
                    face.NormalIndices.Add(nIdx);
                }
            }
        }

        /// <summary>
        /// スムーズ法線を計算（同一頂点の法線を平均化）
        /// </summary>
        public void RecalculateSmoothNormals()
        {
            // まず面法線を計算
            var faceNormals = new Dictionary<int, List<Vector3>>();

            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                foreach (int vIdx in face.VertexIndices)
                {
                    if (!faceNormals.ContainsKey(vIdx))
                        faceNormals[vIdx] = new List<Vector3>();
                    faceNormals[vIdx].Add(faceNormal);
                }
            }

            // 各頂点に平均法線を設定
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            foreach (var kvp in faceNormals)
            {
                var vertex = Vertices[kvp.Key];
                Vector3 avgNormal = Vector3.zero;
                foreach (var n in kvp.Value)
                    avgNormal += n;
                avgNormal = avgNormal.normalized;
                vertex.AddNormal(avgNormal);
            }

            // 面の法線インデックスを更新
            foreach (var face in Faces)
            {
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    face.NormalIndices.Add(0);
                }
            }
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public MeshData Clone()
        {
            var copy = new MeshData(Name);
            copy.Vertices = Vertices.Select(v => v.Clone()).ToList();
            copy.Faces = Faces.Select(f => f.Clone()).ToList();
            return copy;
        }

        /// <summary>
        /// バウンディングボックスを計算
        /// </summary>
        public Bounds CalculateBounds()
        {
            if (Vertices.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 min = Vertices[0].Position;
            Vector3 max = Vertices[0].Position;

            foreach (var vertex in Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>
        /// マテリアル使用状況を取得
        /// </summary>
        /// <returns>Key: MaterialIndex, Value: 使用面数</returns>
        public Dictionary<int, int> GetMaterialUsage()
        {
            var usage = new Dictionary<int, int>();
            foreach (var face in Faces)
            {
                if (!usage.ContainsKey(face.MaterialIndex))
                    usage[face.MaterialIndex] = 0;
                usage[face.MaterialIndex]++;
            }
            return usage;
        }

        /// <summary>
        /// 指定マテリアルインデックスの面を取得
        /// </summary>
        public IEnumerable<int> GetFacesByMaterial(int materialIndex)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].MaterialIndex == materialIndex)
                    yield return i;
            }
        }

        /// <summary>
        /// 選択した面のマテリアルインデックスを変更
        /// </summary>
        public void SetFacesMaterial(IEnumerable<int> faceIndices, int materialIndex)
        {
            foreach (int idx in faceIndices)
            {
                if (idx >= 0 && idx < Faces.Count)
                {
                    Faces[idx].MaterialIndex = materialIndex;
                }
            }
        }

        /// <summary>
        /// デバッグ情報
        /// </summary>
        public string GetDebugInfo()
        {
            int triCount = Faces.Where(f => f.IsTriangle).Count();
            int quadCount = Faces.Where(f => f.IsQuad).Count();
            int nGonCount = Faces.Count - triCount - quadCount;
            int subMeshCount = SubMeshCount;

            return $"[{Name}] Vertices: {VertexCount}, Faces: {FaceCount} " +
                   $"(Tri: {triCount}, Quad: {quadCount}, NGon: {nGonCount}), SubMeshes: {subMeshCount}";
        }
    }

    // ============================================================
    // ヘルパークラス
    // ============================================================

    /// <summary>
    /// Vector3 比較用（Dictionary キー用）
    /// </summary>
    internal class Vector3Comparer : IEqualityComparer<Vector3>
    {
        private const float Tolerance = 0.00001f;

        public bool Equals(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b) < Tolerance;
        }

        public int GetHashCode(Vector3 v)
        {
            // 精度を落としてハッシュ化（近い値が同じハッシュになるように）
            int x = Mathf.RoundToInt(v.x * 10000);
            int y = Mathf.RoundToInt(v.y * 10000);
            int z = Mathf.RoundToInt(v.z * 10000);
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }
    }
}
