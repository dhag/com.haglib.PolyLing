// Assets/Editor/Poly_Ling/Symmetry/SymmetryMeshCache.cs
// 対称表示用メッシュキャッシュ
// 面の頂点順序を反転し、正しい法線方向でミラー描画を実現
//
// サブメッシュ構成:
// - B方式（デフォルト）: [mat0, mat1, ...] ※ミラー位置・面反転済み
// - C方式（_MaterialPaired）: 未実装（表示では不要）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Symmetry
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

        /// <summary>
        /// スキニング済みミラー座標で頂点位置を更新
        /// GPU側でTransformMirrorVertices実行後に呼び出す
        /// </summary>
        /// <param name="skinnedMirrorPositions">スキニング済みミラー座標配列（頂点インデックス順）</param>
        /// <param name="meshObject">元のメッシュデータ（面構造取得用）</param>
        /// <param name="vertexOffset">このメッシュの開始頂点オフセット</param>
        /// <param name="settings">対称設定（キャッシュ無効時のフォールバック用）</param>
        public void UpdateWithSkinnedPositions(Vector3[] skinnedMirrorPositions, MeshObject meshObject, int vertexOffset, SymmetrySettings settings)
        {
            // デバッグ：無条件出力
            UnityEngine.Debug.Log($"[MirrorCache] ENTER: positions={skinnedMirrorPositions?.Length ?? -1}, mesh={meshObject?.VertexCount ?? -1}, offset={vertexOffset}, mirrorMesh={_mirrorMesh != null}");

            if (skinnedMirrorPositions == null || meshObject == null)
            {
                UnityEngine.Debug.Log($"[MirrorCache] NULL_INPUT: positions={skinnedMirrorPositions != null}, mesh={meshObject != null}");
                return;
            }

            // mesh=0の場合は早期リターン
            if (meshObject.VertexCount == 0)
            {
                UnityEngine.Debug.Log($"[MirrorCache] SKIP: mesh has 0 vertices");
                return;
            }

            if (_mirrorMesh == null)
            {
                if (UnityEngine.Time.frameCount % 60 == 0)
                    UnityEngine.Debug.Log("[MirrorCache] UpdateWithSkinnedPositions: _mirrorMesh is null, calling Update()");
                // キャッシュが未初期化の場合はフルリビルド
                Update(meshObject, settings);
                return;
            }

            // このメッシュの頂点座標を抽出
            int vertexCount = meshObject.VertexCount;
            Vector3[] localMirrorPositions = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                int globalIdx = vertexOffset + i;
                if (globalIdx < skinnedMirrorPositions.Length)
                {
                    localMirrorPositions[i] = skinnedMirrorPositions[globalIdx];
                }
            }

            // Unity Meshに展開（面ごとに頂点を持つ形式、面を反転）
            Vector3[] expandedPositions = ExpandPositions(localMirrorPositions, meshObject);

            if (UnityEngine.Time.frameCount % 60 == 0)
            {
                UnityEngine.Debug.Log($"[MirrorCache] UpdateWithSkinnedPositions: expandedLen={expandedPositions.Length}, mirrorMeshVerts={_mirrorMesh.vertexCount}");
                if (vertexCount > 0 && localMirrorPositions.Length > 0)
                {
                    UnityEngine.Debug.Log($"[MirrorCache] Sample pos[0]: {localMirrorPositions[0]}");
                }
            }

            if (expandedPositions.Length == _mirrorMesh.vertexCount)
            {
                _mirrorMesh.vertices = expandedPositions;
                _mirrorMesh.RecalculateNormals();
                _mirrorMesh.RecalculateBounds();

                // デバッグ：無条件出力
                UnityEngine.Debug.Log($"[MirrorCache] SUCCESS: expanded={expandedPositions.Length}, sample[0]={expandedPositions[0]}");
            }
            else
            {
                // デバッグ：無条件出力
                UnityEngine.Debug.Log($"[MirrorCache] MISMATCH: expanded={expandedPositions.Length}, mirrorVerts={_mirrorMesh.vertexCount}");
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
        /// <summary>
        /// ミラーメッシュを再構築（頂点共有方式）
        /// BakeMirrorToUnityMeshと同じ構造で、将来のワールド変換対応を考慮
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
            SymmetryAxis axis = settings.Axis;

            // === 頂点共有方式（BakeMirrorToUnityMeshと同じ構造）===
            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();

            // キー: (頂点インデックス, UVサブインデックス) → Unity頂点インデックス
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            // 頂点を展開（頂点順→UV順）
            for (int vIdx = 0; vIdx < meshObject.VertexCount; vIdx++)
            {
                var vertex = meshObject.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int unityIdx = unityVerts.Count;
                    vertexMapping[(vIdx, uvIdx)] = unityIdx;

                    // 位置（ミラー変換）
                    unityVerts.Add(mirrorMatrix.MultiplyPoint3x4(vertex.Position));

                    // UV
                    if (uvIdx < vertex.UVs.Count)
                        unityUVs.Add(vertex.UVs[uvIdx]);
                    else
                        unityUVs.Add(Vector2.zero);

                    // 法線（ミラー変換）
                    Vector3 normal = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
                    unityNormals.Add(MirrorNormal(normal, axis));
                }
            }

            // 面データを収集（3頂点以上の面のみ）
            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                int matIdx = face.MaterialIndex;
                if (!facesByMaterial.ContainsKey(matIdx))
                    facesByMaterial[matIdx] = new List<Face>();
                facesByMaterial[matIdx].Add(face);
            }

            if (facesByMaterial.Count == 0)
                return;

            // 三角形インデックスを構築（面を反転）
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
                    int[] reversedUVIndices = CreateReversedFace(face.UVIndices);

                    // 三角形分割（ファン方式）
                    for (int i = 1; i < reversedIndices.Length - 1; i++)
                    {
                        // 頂点0
                        int vIdx0 = reversedIndices[0];
                        int uvIdx0 = reversedUVIndices.Length > 0 ? reversedUVIndices[0] : 0;
                        trianglesBySubmesh[matIdx].Add(GetVertexIndex(vertexMapping, vIdx0, uvIdx0));

                        // 頂点i
                        int vIdxI = reversedIndices[i];
                        int uvIdxI = i < reversedUVIndices.Length ? reversedUVIndices[i] : 0;
                        trianglesBySubmesh[matIdx].Add(GetVertexIndex(vertexMapping, vIdxI, uvIdxI));

                        // 頂点i+1
                        int vIdxI1 = reversedIndices[i + 1];
                        int uvIdxI1 = (i + 1) < reversedUVIndices.Length ? reversedUVIndices[i + 1] : 0;
                        trianglesBySubmesh[matIdx].Add(GetVertexIndex(vertexMapping, vIdxI1, uvIdxI1));
                    }
                }
            }

            // メッシュに設定
            _mirrorMesh.vertices = unityVerts.ToArray();
            _mirrorMesh.uv = unityUVs.ToArray();
            _mirrorMesh.normals = unityNormals.ToArray();

            // サブメッシュ設定
            int subMeshCount = 0;
            foreach (var matIdx in trianglesBySubmesh.Keys)
            {
                subMeshCount = Mathf.Max(subMeshCount, matIdx + 1);
            }
            _mirrorMesh.subMeshCount = subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
            {
                if (trianglesBySubmesh.TryGetValue(i, out var triangles))
                    _mirrorMesh.SetTriangles(triangles, i);
                else
                    _mirrorMesh.SetTriangles(new int[0], i);
            }

            _mirrorMesh.RecalculateBounds();
        }

        /// <summary>
        /// 頂点マッピングから Unity 頂点インデックスを取得
        /// </summary>
        private int GetVertexIndex(Dictionary<(int vertexIdx, int uvIdx), int> mapping, int vIdx, int uvIdx)
        {
            if (mapping.TryGetValue((vIdx, uvIdx), out int result))
                return result;
            // フォールバック: uvIdx=0で試行
            if (mapping.TryGetValue((vIdx, 0), out result))
                return result;
            return 0;
        }

        /// <summary>
        /// 法線をミラー変換
        /// </summary>
        private Vector3 MirrorNormal(Vector3 normal, SymmetryAxis axis)
        {
            switch (axis)
            {
                case SymmetryAxis.X:
                    return new Vector3(-normal.x, normal.y, normal.z);
                case SymmetryAxis.Y:
                    return new Vector3(normal.x, -normal.y, normal.z);
                case SymmetryAxis.Z:
                    return new Vector3(normal.x, normal.y, -normal.z);
                default:
                    return normal;
            }
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
        /// 頂点位置を頂点共有方式で展開（RebuildMirrorMeshと同じ構造）
        /// </summary>
        private Vector3[] ExpandPositions(Vector3[] positions, MeshObject meshObject)
        {
            var expanded = new List<Vector3>();

            // RebuildMirrorMeshと同じ頂点展開ロジック（頂点順→UV順）
            for (int vIdx = 0; vIdx < meshObject.VertexCount; vIdx++)
            {
                var vertex = meshObject.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                // GPU座標を取得
                Vector3 pos = (vIdx < positions.Length) ? positions[vIdx] : Vector3.zero;

                // UVの数だけ同じ座標を追加
                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    expanded.Add(pos);
                }
            }


            UnityEngine.Debug.Log($"[MirrorCache] ExpandPositions: inputVerts={meshObject.VertexCount}, outputVerts={expanded.Count}");

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