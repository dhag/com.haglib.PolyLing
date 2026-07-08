// Assets/Editor/Poly_Ling/Core/Data/SpringBoneChainData.cs
// ============================================================
// スプリングボーン・チェーンデータ（純POCOデータ契約）
// ============================================================
//
// 【格納規約】格納・参照・永続化の規約は
//   MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//
// 【役割】
//   VRMC_springBone(VRM SpringBone 1.0) の springs[*] に相当するチェーン属性の
//   純データ。チェーンのルートボーン（Type == MeshType.Bone の MeshObject）に
//   1つだけ付帯する。非null ⇔ そのボーンは揺れチェーンのルート。
//   ※物理演算とは無関係。名称は SpringBone 接頭辞で統一する。
//
// 【チェーン本体はボーン階層に由来】
//   チェーンを構成するジョイント列（順序）は保持しない。ルートを起点に
//   ボーン階層（HierarchyParentIndex）を辿り、SpringBoneJoint を持つ子孫を
//   チェーンメンバーとして導出する。ボーンのツリー構造そのものがチェーン形状。
//   分岐（1ボーンから複数の揺れ子）は各枝を別チェーンとして扱う
//   （各枝ルートに本データが付く）。
//
// 【参照系】
//   CenterBoneName … 慣性評価基準ボーンの名前（name主。空=World空間評価）。
//   SpringBoneColliderGroupIndices … このチェーンが衝突する ColliderGroup の
//     index（ModelContext.SpringBoneColliderGroupNames への index）。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純データ。
//
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>
    /// スプリングボーン・チェーンデータ（純POCO）。
    /// MeshObject.SpringBoneChainRoot として付帯し、非nullがチェーンのルートを表す。
    /// </summary>
    [Serializable]
    public class SpringBoneChainData
    {
        /// <summary>チェーン名（VRM springs[*].name）。</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 衝突対象 ColliderGroup の index
        /// （ModelContext.SpringBoneColliderGroupNames への index）。
        /// </summary>
        public List<int> SpringBoneColliderGroupIndices { get; set; } = new List<int>();

        /// <summary>
        /// 慣性評価基準ボーン名（VRM center。name主。空=World空間評価）。
        /// </summary>
        public string CenterBoneName { get; set; } = "";

        /// <summary>ディープコピー。</summary>
        public SpringBoneChainData Clone()
        {
            return new SpringBoneChainData
            {
                Name = this.Name,
                SpringBoneColliderGroupIndices = this.SpringBoneColliderGroupIndices != null
                    ? new List<int>(this.SpringBoneColliderGroupIndices)
                    : new List<int>(),
                CenterBoneName = this.CenterBoneName
            };
        }
    }
}
