// Assets/Editor/Poly_Ling/Data/MorphBaseData.cs
// モーフ基準データ（モーフ適用前の位置・法線・UVを保持）
// 編集中のデータ紛失を防ぐため、メッシュ頂点にはモーフ後の位置を格納し、
// このクラスにモーフ前の位置を保持する

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// モーフ基準データ
    /// メッシュ頂点（モーフ後）と対になる基準位置（モーフ前）を保持
    /// </summary>
    [Serializable]
    public class MorphBaseData
    {
        // ================================================================
        // 基準データ
        // ================================================================

        /// <summary>基準位置（モーフ前の頂点位置）</summary>
        public Vector3[] BasePositions;

        /// <summary>基準法線（モーフ前の法線、オプション）- 頂点ごとに最初の法線のみ保持</summary>
        public Vector3[] BaseNormals;

        /// <summary>基準UV（モーフ前のUV、オプション）</summary>
        public Vector2[] BaseUVs;

        // ================================================================
        // メタデータ
        // ================================================================

        /// <summary>対応するモーフ名</summary>
        public string MorphName = "";

        /// <summary>モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int Panel = 3;

        /// <summary>作成日時</summary>
        public DateTime CreatedAt = DateTime.Now;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>有効なデータを持っているか</summary>
        public bool IsValid => BasePositions != null && BasePositions.Length > 0;

        /// <summary>頂点数</summary>
        public int VertexCount => BasePositions?.Length ?? 0;

        /// <summary>法線データを持っているか</summary>
        public bool HasNormals => BaseNormals != null && BaseNormals.Length > 0;

        /// <summary>UVデータを持っているか</summary>
        public bool HasUVs => BaseUVs != null && BaseUVs.Length > 0;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public MorphBaseData()
        {
        }

        public MorphBaseData(string morphName)
        {
            MorphName = morphName;
        }

        public MorphBaseData(int vertexCount, string morphName = "")
        {
            MorphName = morphName;
            BasePositions = new Vector3[vertexCount];
        }

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>
        /// MeshObjectから基準データを作成（現在の状態を基準として保存）
        /// </summary>
        public static MorphBaseData FromMeshObject(MeshObject meshObject, string morphName = "")
        {
            if (meshObject == null || meshObject.VertexCount == 0)
                return null;

            var data = new MorphBaseData(morphName);
            int count = meshObject.VertexCount;

            // 位置をコピー
            data.BasePositions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                data.BasePositions[i] = meshObject.Vertices[i].Position;
            }

            // 法線をコピー（存在する場合）
            // Vertex.Normals は List<Vector3> なので、最初の法線のみ保存
            bool hasNormals = false;
            for (int i = 0; i < count; i++)
            {
                if (meshObject.Vertices[i].Normals != null && meshObject.Vertices[i].Normals.Count > 0)
                {
                    hasNormals = true;
                    break;
                }
            }
            if (hasNormals)
            {
                data.BaseNormals = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    var normals = meshObject.Vertices[i].Normals;
                    data.BaseNormals[i] = (normals != null && normals.Count > 0) ? normals[0] : Vector3.zero;
                }
            }

            // UVをコピー（存在する場合）
            bool hasUVs = false;
            for (int i = 0; i < count; i++)
            {
                if (meshObject.Vertices[i].UVs.Count > 0)
                {
                    hasUVs = true;
                    break;
                }
            }
            if (hasUVs)
            {
                data.BaseUVs = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    var uvs = meshObject.Vertices[i].UVs;
                    data.BaseUVs[i] = uvs.Count > 0 ? uvs[0] : Vector2.zero;
                }
            }

            return data;
        }

        /// <summary>
        /// 位置配列から基準データを作成
        /// </summary>
        public static MorphBaseData FromPositions(Vector3[] positions, string morphName = "")
        {
            if (positions == null || positions.Length == 0)
                return null;

            var data = new MorphBaseData(morphName);
            data.BasePositions = (Vector3[])positions.Clone();
            return data;
        }

        // ================================================================
        // 差分計算（エクスポート用）
        // ================================================================

        /// <summary>
        /// モーフ後の位置との差分を計算（エクスポート用）
        /// </summary>
        /// <param name="morphedPositions">モーフ後の位置配列</param>
        /// <returns>差分配列（モーフ後 - モーフ前）</returns>
        public Vector3[] CalculatePositionOffsets(Vector3[] morphedPositions)
        {
            if (!IsValid || morphedPositions == null)
                return null;

            int count = Mathf.Min(BasePositions.Length, morphedPositions.Length);
            var offsets = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                offsets[i] = morphedPositions[i] - BasePositions[i];
            }

            return offsets;
        }

        /// <summary>
        /// MeshObjectとの差分を計算（エクスポート用）
        /// </summary>
        public Vector3[] CalculatePositionOffsets(MeshObject meshObject)
        {
            if (!IsValid || meshObject == null)
                return null;

            int count = Mathf.Min(BasePositions.Length, meshObject.VertexCount);
            var offsets = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                offsets[i] = meshObject.Vertices[i].Position - BasePositions[i];
            }

            return offsets;
        }

        /// <summary>
        /// 変化のある頂点インデックスと差分を取得（エクスポート用、スパース形式）
        /// </summary>
        /// <param name="meshObject">モーフ後のメッシュ</param>
        /// <param name="threshold">変化とみなす閾値</param>
        /// <returns>(頂点インデックス, 差分) のリスト</returns>
        public List<(int Index, Vector3 Offset)> GetSparseOffsets(MeshObject meshObject, float threshold = 0.0001f)
        {
            var result = new List<(int, Vector3)>();

            if (!IsValid || meshObject == null)
                return result;

            int count = Mathf.Min(BasePositions.Length, meshObject.VertexCount);
            float thresholdSq = threshold * threshold;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = meshObject.Vertices[i].Position - BasePositions[i];
                if (offset.sqrMagnitude > thresholdSq)
                {
                    result.Add((i, offset));
                }
            }

            return result;
        }

        /// <summary>
        /// 変化のあるUVインデックスと差分を取得（エクスポート用、スパース形式）
        /// </summary>
        /// <param name="meshObject">モーフ後のメッシュ</param>
        /// <param name="threshold">変化とみなす閾値</param>
        /// <returns>(頂点インデックス, 差分) のリスト</returns>
        public List<(int Index, Vector2 Offset)> GetSparseUVOffsets(MeshObject meshObject, float threshold = 0.0001f)
        {
            var result = new List<(int, Vector2)>();

            if (!HasUVs || meshObject == null)
                return result;

            int count = Mathf.Min(BaseUVs.Length, meshObject.VertexCount);
            float thresholdSq = threshold * threshold;

            for (int i = 0; i < count; i++)
            {
                var uvs = meshObject.Vertices[i].UVs;
                if (uvs == null || uvs.Count == 0)
                    continue;

                Vector2 offset = uvs[0] - BaseUVs[i];
                if (offset.sqrMagnitude > thresholdSq)
                {
                    result.Add((i, offset));
                }
            }

            return result;
        }

        // ================================================================
        // 基準位置の適用
        // ================================================================

        /// <summary>
        /// 基準位置をMeshObjectに適用（モーフをリセット）
        /// </summary>
        public void ApplyBaseToMeshObject(MeshObject meshObject)
        {
            if (!IsValid || meshObject == null)
                return;

            int count = Mathf.Min(BasePositions.Length, meshObject.VertexCount);

            for (int i = 0; i < count; i++)
            {
                meshObject.Vertices[i].Position = BasePositions[i];
            }

            // 法線も復元（Vertex.Normals は List<Vector3>）
            if (HasNormals)
            {
                count = Mathf.Min(BaseNormals.Length, meshObject.VertexCount);
                for (int i = 0; i < count; i++)
                {
                    var normals = meshObject.Vertices[i].Normals;
                    if (normals != null && normals.Count > 0)
                    {
                        normals[0] = BaseNormals[i];
                    }
                    else if (normals != null)
                    {
                        normals.Add(BaseNormals[i]);
                    }
                    else
                    {
                        meshObject.Vertices[i].Normals = new List<Vector3> { BaseNormals[i] };
                    }
                }
            }

            // UVも復元
            if (HasUVs)
            {
                count = Mathf.Min(BaseUVs.Length, meshObject.VertexCount);
                for (int i = 0; i < count; i++)
                {
                    if (meshObject.Vertices[i].UVs.Count > 0)
                    {
                        meshObject.Vertices[i].UVs[0] = BaseUVs[i];
                    }
                }
            }
        }

        // ================================================================
        // 頂点数変更への対応
        // ================================================================

        /// <summary>
        /// 頂点が追加された場合に基準データを拡張
        /// </summary>
        /// <param name="newVertexCount">新しい頂点数</param>
        /// <param name="newPositions">新しい頂点の位置（追加分）</param>
        public void ExtendVertices(int newVertexCount, Vector3[] newPositions = null)
        {
            if (!IsValid)
                return;

            int oldCount = BasePositions.Length;
            if (newVertexCount <= oldCount)
                return;

            // 位置を拡張
            var newBasePositions = new Vector3[newVertexCount];
            Array.Copy(BasePositions, newBasePositions, oldCount);

            // 新しい頂点の基準位置を設定
            if (newPositions != null)
            {
                int copyCount = Mathf.Min(newPositions.Length, newVertexCount - oldCount);
                for (int i = 0; i < copyCount; i++)
                {
                    newBasePositions[oldCount + i] = newPositions[i];
                }
            }

            BasePositions = newBasePositions;

            // 法線も拡張
            if (HasNormals)
            {
                var newBaseNormals = new Vector3[newVertexCount];
                Array.Copy(BaseNormals, newBaseNormals, Mathf.Min(BaseNormals.Length, oldCount));
                BaseNormals = newBaseNormals;
            }

            // UVも拡張
            if (HasUVs)
            {
                var newBaseUVs = new Vector2[newVertexCount];
                Array.Copy(BaseUVs, newBaseUVs, Mathf.Min(BaseUVs.Length, oldCount));
                BaseUVs = newBaseUVs;
            }
        }

        /// <summary>
        /// 頂点が削除された場合に基準データを更新
        /// </summary>
        /// <param name="removedIndices">削除された頂点インデックス</param>
        public void RemoveVertices(HashSet<int> removedIndices)
        {
            if (!IsValid || removedIndices == null || removedIndices.Count == 0)
                return;

            int oldCount = BasePositions.Length;
            int newCount = oldCount - removedIndices.Count;
            if (newCount <= 0)
            {
                BasePositions = null;
                BaseNormals = null;
                BaseUVs = null;
                return;
            }

            // 新しい配列を作成
            var newBasePositions = new Vector3[newCount];
            Vector3[] newBaseNormals = HasNormals ? new Vector3[newCount] : null;
            Vector2[] newBaseUVs = HasUVs ? new Vector2[newCount] : null;

            int newIndex = 0;
            for (int i = 0; i < oldCount; i++)
            {
                if (!removedIndices.Contains(i))
                {
                    newBasePositions[newIndex] = BasePositions[i];
                    if (newBaseNormals != null && i < BaseNormals.Length)
                        newBaseNormals[newIndex] = BaseNormals[i];
                    if (newBaseUVs != null && i < BaseUVs.Length)
                        newBaseUVs[newIndex] = BaseUVs[i];
                    newIndex++;
                }
            }

            BasePositions = newBasePositions;
            BaseNormals = newBaseNormals;
            BaseUVs = newBaseUVs;
        }

        // ================================================================
        // クローン
        // ================================================================

        /// <summary>
        /// ディープコピーを作成
        /// </summary>
        public MorphBaseData Clone()
        {
            var clone = new MorphBaseData
            {
                MorphName = this.MorphName,
                Panel = this.Panel,
                CreatedAt = this.CreatedAt
            };

            if (BasePositions != null)
                clone.BasePositions = (Vector3[])BasePositions.Clone();

            if (BaseNormals != null)
                clone.BaseNormals = (Vector3[])BaseNormals.Clone();

            if (BaseUVs != null)
                clone.BaseUVs = (Vector2[])BaseUVs.Clone();

            return clone;
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            return $"MorphBaseData[{MorphName}]: {VertexCount} vertices" +
                   (HasNormals ? ", normals" : "") +
                   (HasUVs ? ", uvs" : "");
        }
    }
}
