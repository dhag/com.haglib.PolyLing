// Assets/Editor/MeshFactory/Symmetry/SymmetryMeshCache.cs
// 対称表示用メッシュキャッシュ
// 面の頂点順序を反転し、正しい法線方向でミラー描画を実現
//
// サブメッシュ構成:
// - B方式（デフォルト）: [mat0, mat1, ...] ※ミラー位置・面反転済み
// - C方式（_MaterialPaired）: 未実装（表示では不要）

using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Symmetry
{
    /// <summary>
    /// 対称表示用メッシュキャッシュ
    /// ミラー描画時に面の向きを正しく反転するためのデータを保持
    /// </summary>
    public class SymmetryMeshCache
    {
        // ================================================================
        // キャッシュデータ
        // ================================================================

        /// <summary>ミラー用Unity Mesh（反転済み面を持つ）</summary>
        private Mesh _mirrorMesh;

        /// <summary>キャッシュが無効か</summary>
        private bool _isDirty = true;

        /// <summary>前回の対称設定ハッシュ（設定変更検出用）</summary>
        private int _lastSettingsHash;

        /// <summary>前回のメッシュデータハッシュ（トポロジー変更検出用）</summary>
        private int _lastMeshObjectHash;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>キャッシュが有効か</summary>
        public bool IsValid => !_isDirty && _mirrorMesh != null;

        /// <summary>ミラー用メッシュ</summary>
        public Mesh MirrorMesh => _mirrorMesh;

        // ================================================================
        // キャッシュ操作
        // ================================================================

        /// <summary>
        /// キャッシュを無効化（トポロジー変更時に呼び出す）
        /// </summary>
        public void Invalidate()
        {
            _isDirty = true;
        }

        /// <summary>
        /// キャッシュをクリア（リソース解放）
        /// </summary>
        public void Clear()
        {
            if (_mirrorMesh != null)
            {
                Object.DestroyImmediate(_mirrorMesh);
                _mirrorMesh = null;
            }
            _isDirty = true;
            _lastSettingsHash = 0;
            _lastMeshObjectHash = 0;
        }

        /// <summary>
        /// キャッシュを更新（必要な場合のみ再構築）
        /// </summary>
        /// <param name="meshObject">元のメッシュデータ</param>
        /// <param name="settings">対称設定</param>
        /// <returns>更新されたか</returns>
        public bool Update(MeshObject meshObject, SymmetrySettings settings)
        {
            if (meshObject == null || settings == null)
            {
                Clear();
                return false;
            }

            // 設定変更の検出
            int settingsHash = ComputeSettingsHash(settings);
            int meshObjectHash = ComputeMeshObjectHash(meshObject);

            bool settingsChanged = settingsHash != _lastSettingsHash;
            bool meshChanged = meshObjectHash != _lastMeshObjectHash;

            // 更新が不要な場合
            if (!_isDirty && !settingsChanged && !meshChanged && _mirrorMesh != null)
            {
                return false;
            }

            // キャッシュ再構築
            RebuildMirrorMesh(meshObject, settings);

            _lastSettingsHash = settingsHash;
            _lastMeshObjectHash = meshObjectHash;
            _isDirty = false;

            return true;
        }

        /// <summary>
        /// 頂点位置のみ更新（トポロジー変更なし、移動のみの場合）
        /// </summary>
        /// <param name="meshObject">元のメッシュデータ</param>
        /// <param name="settings">対称設定</param>
        public void UpdatePositionsOnly(MeshObject meshObject, SymmetrySettings settings)
        {
            if (_mirrorMesh == null || meshObject == null || settings == null)
            {
                // フルリビルドが必要
                Update(meshObject, settings);
                return;
            }

            // 頂点位置のみ更新
            Matrix4x4 mirrorMatrix = settings.GetMirrorMatrix();
            Vector3[] mirrorPositions = new Vector3[meshObject.VertexCount];

            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                mirrorPositions[i] = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            }

            // Unity Meshに展開（面ごとに頂点を持つ形式）
            Vector3[] expandedPositions = ExpandPositions(mirrorPositions, meshObject);
            
            if (expandedPositions.Length == _mirrorMesh.vertexCount)
            {
                _mirrorMesh.vertices = expandedPositions;
                _mirrorMesh.RecalculateNormals();
                _mirrorMesh.RecalculateBounds();
            }
            else
            {
                // 頂点数が変わった場合はフルリビルド
                Update(meshObject, settings);
            }
        }

        // ================================================================
        // 内部処理（B方式：材質ごとにサブメッシュ）
        // ================================================================

        /// <summary>
        /// ミラーメッシュを再構築（B方式）
        /// サブメッシュ構成: [mat0, mat1, mat2, ...]
        /// </summary>
        private void RebuildMirrorMesh(MeshObject meshObject, SymmetrySettings settings)
        {
            // 既存メッシュをクリア
            if (_mirrorMesh == null)
            {
                _mirrorMesh = new Mesh();
                _mirrorMesh.name = "SymmetryMirrorMesh";
                _mirrorMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                _mirrorMesh.Clear();
            }

            if (meshObject.VertexCount == 0 || meshObject.FaceCount == 0)
            {
                return;
            }

            Matrix4x4 mirrorMatrix = settings.GetMirrorMatrix();

            // ミラー済み頂点位置を計算
            Vector3[] mirrorPositions = new Vector3[meshObject.VertexCount];
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                mirrorPositions[i] = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
            }

            // 面データを収集（3頂点以上の面のみ、2頂点は補助線）
            var realFaces = new List<Face>();
            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount >= 3)
                {
                    realFaces.Add(face);
                }
            }

            if (realFaces.Count == 0)
            {
                return;
            }

            // サブメッシュ（マテリアル）ごとに面を分類
            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in realFaces)
            {
                int matIdx = face.MaterialIndex;
                if (!facesByMaterial.ContainsKey(matIdx))
                {
                    facesByMaterial[matIdx] = new List<Face>();
                }
                facesByMaterial[matIdx].Add(face);
            }

            // 頂点・三角形を構築（面ごとに頂点を展開）
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var trianglesBySubmesh = new Dictionary<int, List<int>>();

            foreach (var kvp in facesByMaterial)
            {
                int matIdx = kvp.Key;
                var faces = kvp.Value;
                trianglesBySubmesh[matIdx] = new List<int>();

                foreach (var face in faces)
                {
                    // 反転した面インデックスを取得
                    int[] reversedIndices = CreateReversedFace(face.VertexIndices);
                    
                    // 三角形分割（ファン方式）
                    int baseVertex = vertices.Count;

                    for (int i = 0; i < reversedIndices.Length; i++)
                    {
                        int origIdx = reversedIndices[i];
                        vertices.Add(mirrorPositions[origIdx]);

                        // UV（元の順序から取得）
                        // 注: UVも反転順序に合わせて取得
                        int origOrderIdx = face.VertexIndices[GetOriginalOrderIndex(i, face.VertexCount)];
                        var vertex = meshObject.Vertices[origOrderIdx];
                        if (vertex.UVs.Count > 0)
                        {
                            uvs.Add(vertex.UVs[0]);
                        }
                        else
                        {
                            uvs.Add(Vector2.zero);
                        }
                    }

                    // 三角形インデックス（ファン分割）
                    for (int i = 1; i < reversedIndices.Length - 1; i++)
                    {
                        trianglesBySubmesh[matIdx].Add(baseVertex);
                        trianglesBySubmesh[matIdx].Add(baseVertex + i);
                        trianglesBySubmesh[matIdx].Add(baseVertex + i + 1);
                    }
                }
            }

            // メッシュに設定
            _mirrorMesh.vertices = vertices.ToArray();
            if (uvs.Count == vertices.Count)
            {
                _mirrorMesh.uv = uvs.ToArray();
            }

            // サブメッシュ設定（B方式：材質ごと）
            int subMeshCount = 0;
            foreach (var matIdx in trianglesBySubmesh.Keys)
            {
                subMeshCount = Mathf.Max(subMeshCount, matIdx + 1);
            }
            _mirrorMesh.subMeshCount = subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
            {
                if (trianglesBySubmesh.TryGetValue(i, out var triangles))
                {
                    _mirrorMesh.SetTriangles(triangles, i);
                }
                else
                {
                    _mirrorMesh.SetTriangles(new int[0], i);
                }
            }

            _mirrorMesh.RecalculateNormals();
            _mirrorMesh.RecalculateBounds();
        }

        /// <summary>
        /// 面の頂点順序を反転（法線を反転するため）
        /// [0, 1, 2, 3] → [0, 3, 2, 1]
        /// </summary>
        private int[] CreateReversedFace(List<int> originalIndices)
        {
            int count = originalIndices.Count;
            int[] reversed = new int[count];

            // 最初の頂点は維持、残りを逆順
            reversed[0] = originalIndices[0];
            for (int i = 1; i < count; i++)
            {
                reversed[i] = originalIndices[count - i];
            }

            return reversed;
        }

        /// <summary>
        /// 反転後のインデックスから元の順序のインデックスを取得
        /// </summary>
        private int GetOriginalOrderIndex(int reversedIndex, int count)
        {
            if (reversedIndex == 0) return 0;
            return count - reversedIndex;
        }

        /// <summary>
        /// 頂点位置を面ごとに展開
        /// </summary>
        private Vector3[] ExpandPositions(Vector3[] positions, MeshObject meshObject)
        {
            var expanded = new List<Vector3>();

            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount < 3) continue;

                int[] reversedIndices = CreateReversedFace(face.VertexIndices);
                foreach (int idx in reversedIndices)
                {
                    if (idx >= 0 && idx < positions.Length)
                    {
                        expanded.Add(positions[idx]);
                    }
                }
            }

            return expanded.ToArray();
        }

        /// <summary>
        /// 対称設定のハッシュを計算
        /// </summary>
        private int ComputeSettingsHash(SymmetrySettings settings)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + settings.Axis.GetHashCode();
                hash = hash * 31 + settings.PlaneOffset.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// メッシュデータのハッシュを計算（トポロジー変更検出用）
        /// </summary>
        private int ComputeMeshObjectHash(MeshObject meshObject)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + meshObject.VertexCount;
                hash = hash * 31 + meshObject.FaceCount;

                // 各面の頂点数とマテリアルインデックス
                foreach (var face in meshObject.Faces)
                {
                    hash = hash * 31 + face.VertexCount;
                    hash = hash * 31 + face.MaterialIndex;
                }

                return hash;
            }
        }

        // ================================================================
        // デストラクタ
        // ================================================================

        ~SymmetryMeshCache()
        {
            // Unity Objectの破棄はメインスレッドで行う必要があるため
            // 明示的なClear()呼び出しを推奨
        }
    }
}
