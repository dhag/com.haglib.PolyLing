// Assets/Editor/Poly_Ling/Core/Data/TPoseBackup.cs
// ============================================================
// Tポーズ変換前バックアップ（純データ）
// ============================================================
//
// 【格納規約】格納・参照・永続化の規約は
//   MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   ※本バックアップは MeshContext index をキーとする（実体が index キー）。
//     name主化（規約2）は別タスク。
//
// 【移設メモ】
//   従来 Core/Ops/TPoseConverter.cs 内に定義されていたが、データ実体を
//   Data フォルダへ集約するため本ファイルへ移設（namespace は
//   Poly_Ling.Ops → Poly_Ling.Data へ変更）。型・フィールド・振る舞いは不変。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// Tポーズ変換前の姿勢バックアップ
    /// </summary>
    public class TPoseBackup
    {
        /// <summary>
        /// ボーン別のローカル回転バックアップ（MeshContextインデックス→Euler角）
        /// </summary>
        public Dictionary<int, Vector3> BoneRotations = new();

        /// <summary>
        /// ボーン別のWorldMatrixバックアップ
        /// </summary>
        public Dictionary<int, Matrix4x4> WorldMatrices = new();

        /// <summary>
        /// ボーン別のBindPoseバックアップ
        /// </summary>
        public Dictionary<int, Matrix4x4> BindPoses = new();

        /// <summary>
        /// メッシュ別の頂点座標バックアップ（MeshContextインデックス→頂点Position配列）
        /// </summary>
        public Dictionary<int, Vector3[]> VertexPositions = new();

        /// <summary>
        /// ボーン別のローカル位置バックアップ（MeshContextインデックス→Position）
        /// </summary>
        public Dictionary<int, Vector3> BonePositions = new();

        /// <summary>
        /// ボーン別の UseLocalTransform バックアップ
        /// </summary>
        public Dictionary<int, bool> BoneUseLocal = new();

        /// <summary>
        /// ボーン別の BonePoseData バックアップ（Clone。ポーズ層の復元用）
        /// </summary>
        public Dictionary<int, BonePoseData> BonePoses = new();

        /// <summary>
        /// MeshContextList の並びが変わったとき、索引キーを付け替える。
        /// remap が -1 を返したエントリは対象が消えたものとして捨てる。
        /// ModelContext.Insert / RemoveAt / Move から呼ばれる。
        /// </summary>
        public void RemapIndices(System.Func<int, int> remap)
        {
            if (remap == null) return;

            BoneRotations   = Remap(BoneRotations,   remap);
            WorldMatrices   = Remap(WorldMatrices,   remap);
            BindPoses       = Remap(BindPoses,       remap);
            VertexPositions = Remap(VertexPositions, remap);
            BonePositions   = Remap(BonePositions,   remap);
            BoneUseLocal    = Remap(BoneUseLocal,    remap);
            BonePoses       = Remap(BonePoses,       remap);
        }

        private static Dictionary<int, T> Remap<T>(
            Dictionary<int, T> src, System.Func<int, int> remap)
        {
            if (src == null || src.Count == 0) return src;

            var dst = new Dictionary<int, T>(src.Count);
            foreach (var kv in src)
            {
                int ni = remap(kv.Key);
                if (ni >= 0) dst[ni] = kv.Value;
            }
            return dst;
        }
    }
}
