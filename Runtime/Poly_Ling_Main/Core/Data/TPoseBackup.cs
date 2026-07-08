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
    }
}
