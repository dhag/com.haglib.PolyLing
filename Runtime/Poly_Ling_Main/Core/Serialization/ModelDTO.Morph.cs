// ModelDTO.Morph.cs
// DTO：モーフ基準データとミラーペア。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    // ================================================================
    // モーフ基準データ（Phase: Morph対応）
    // ================================================================

    /// <summary>
    /// モーフ基準データのシリアライズ用構造
    /// メッシュ頂点（モーフ後）と対になる基準位置（モーフ前）を保持
    /// </summary>
    [Serializable]
    public class MorphBaseDataDTO
    {
        /// <summary>モーフ名</summary>
        public string morphName = "";

        /// <summary>モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int panel = 3;

        /// <summary>作成日時（ISO 8601形式）</summary>
        public string createdAt;

        /// <summary>
        /// 基準位置（モーフ前の頂点位置）
        /// 各頂点の [x, y, z] を連続配列として格納
        /// 例: [x0,y0,z0, x1,y1,z1, x2,y2,z2, ...]
        /// </summary>
        public float[] basePositions;

        /// <summary>
        /// 基準法線（オプション）
        /// 各頂点の [x, y, z] を連続配列として格納
        /// null = 法線データなし
        /// </summary>
        public float[] baseNormals;

        /// <summary>
        /// 基準UV（オプション）
        /// 各頂点の [u, v] を連続配列として格納
        /// null = UVデータなし
        /// </summary>
        public float[] baseUVs;

        // ================================================================
        // 変換ヘルパー
        // ================================================================

        /// <summary>頂点数を取得</summary>
        public int GetVertexCount()
        {
            if (basePositions == null) return 0;
            return basePositions.Length / 3;
        }

        /// <summary>指定頂点の基準位置を取得</summary>
        public Vector3 GetBasePosition(int index)
        {
            if (basePositions == null) return Vector3.zero;
            int i = index * 3;
            if (i + 2 >= basePositions.Length) return Vector3.zero;
            return new Vector3(basePositions[i], basePositions[i + 1], basePositions[i + 2]);
        }

        /// <summary>基準位置配列を取得</summary>
        public Vector3[] GetBasePositions()
        {
            if (basePositions == null) return null;
            int count = GetVertexCount();
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 3;
                result[i] = new Vector3(basePositions[idx], basePositions[idx + 1], basePositions[idx + 2]);
            }
            return result;
        }

        /// <summary>基準位置を設定</summary>
        public void SetBasePositions(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
            {
                basePositions = null;
                return;
            }
            basePositions = new float[positions.Length * 3];
            for (int i = 0; i < positions.Length; i++)
            {
                int idx = i * 3;
                basePositions[idx] = positions[i].x;
                basePositions[idx + 1] = positions[i].y;
                basePositions[idx + 2] = positions[i].z;
            }
        }

        /// <summary>基準法線配列を取得</summary>
        public Vector3[] GetBaseNormals()
        {
            if (baseNormals == null) return null;
            int count = baseNormals.Length / 3;
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 3;
                result[i] = new Vector3(baseNormals[idx], baseNormals[idx + 1], baseNormals[idx + 2]);
            }
            return result;
        }

        /// <summary>基準法線を設定</summary>
        public void SetBaseNormals(Vector3[] normals)
        {
            if (normals == null || normals.Length == 0)
            {
                baseNormals = null;
                return;
            }
            baseNormals = new float[normals.Length * 3];
            for (int i = 0; i < normals.Length; i++)
            {
                int idx = i * 3;
                baseNormals[idx] = normals[i].x;
                baseNormals[idx + 1] = normals[i].y;
                baseNormals[idx + 2] = normals[i].z;
            }
        }

        /// <summary>基準UV配列を取得</summary>
        public Vector2[] GetBaseUVs()
        {
            if (baseUVs == null) return null;
            int count = baseUVs.Length / 2;
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int idx = i * 2;
                result[i] = new Vector2(baseUVs[idx], baseUVs[idx + 1]);
            }
            return result;
        }

        /// <summary>基準UVを設定</summary>
        public void SetBaseUVs(Vector2[] uvs)
        {
            if (uvs == null || uvs.Length == 0)
            {
                baseUVs = null;
                return;
            }
            baseUVs = new float[uvs.Length * 2];
            for (int i = 0; i < uvs.Length; i++)
            {
                int idx = i * 2;
                baseUVs[idx] = uvs[i].x;
                baseUVs[idx + 1] = uvs[i].y;
            }
        }

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>デフォルト作成</summary>
        public static MorphBaseDataDTO Create(string morphName = "")
        {
            return new MorphBaseDataDTO
            {
                morphName = morphName,
                panel = 3,
                createdAt = DateTime.Now.ToString("o")
            };
        }
    }

    // ================================================================
    // ミラーペアデータ
    // ================================================================

    /// <summary>
    /// ミラーペア情報のシリアライズ用構造
    /// Real側とMirror側のメッシュインデックスペアを保持
    /// </summary>
    [Serializable]
    public class MirrorPairDTO
    {
        /// <summary>Real側メッシュインデックス（MeshContextList内）</summary>
        public int realIndex;

        /// <summary>Mirror側メッシュインデックス（MeshContextList内）</summary>
        public int mirrorIndex;

        /// <summary>ミラー軸（0=X, 1=Y, 2=Z）</summary>
        public int axis = 0;
    }
}
