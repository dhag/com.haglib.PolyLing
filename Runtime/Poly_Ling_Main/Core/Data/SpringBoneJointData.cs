// Assets/Editor/Poly_Ling/Core/Data/SpringBoneJointData.cs
// ============================================================
// スプリングボーン・ジョイントデータ（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   VRMC_springBone(VRM SpringBone 1.0) の springs[*].joints[*] に相当する
//   パラメータの純データ。揺れチェーンを構成する各ボーン
//   （Type == MeshType.Bone の MeshObject）に1つ付帯する。
//   非null ⇔ そのボーンは揺れジョイント。
//   ※物理演算（JointData＝剛体ジョイント）とは別物。名称は SpringBone 接頭辞で統一する。
//
// 【チェーン構造：ボーン階層に由来】
//   ジョイントの集合・順序は明示リストを持たず、既存ボーン階層
//   （MeshObject.HierarchyParentIndex）＋ SpringBoneJoint の有無から導出する。
//   チェーンのルート（Name/参照グループ/center）は SpringBoneChainData が別途保持する。
//   末端(tail)ボーンも SpringBoneJoint を持つ（パラメータは未使用・VRM 準拠）。
//
// 【実行時状態は保持しない】
//   prevTail/currentTail/boneAxis/boneLength 等は実行時に算出する値であり、
//   永続化・データ契約には含めない（本POCOは設定値のみ）。
//
// 【座標系】
//   GravityDir は working空間の生値（Unity 左手系）を保持する。
//
// 【依存】
//   UnityEngine.Vector3 のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// スプリングボーン・ジョイントデータ（純POCO）。
    /// MeshObject.SpringBoneJoint として付帯し、非nullが揺れジョイントを表す。
    /// </summary>
    [Serializable]
    public class SpringBoneJointData
    {
        /// <summary>当たり判定の半径（VRM hitRadius）。</summary>
        public float HitRadius { get; set; } = 0.02f;

        /// <summary>剛性（初期姿勢方向へ戻ろうとする力。VRM stiffnessForce）。</summary>
        public float StiffnessForce { get; set; } = 1.0f;

        /// <summary>重力の強さ（VRM gravityPower。物理量ではなく見た目の調整値）。</summary>
        public float GravityPower { get; set; } = 0f;

        /// <summary>重力の方向（VRM gravityDir。working空間の生値）。</summary>
        public Vector3 GravityDir { get; set; } = new Vector3(0f, -1f, 0f);

        /// <summary>減衰（1.0=完全停止。VRM dragForce）。</summary>
        public float DragForce { get; set; } = 0.4f;

        /// <summary>ディープコピー。</summary>
        public SpringBoneJointData Clone()
        {
            return new SpringBoneJointData
            {
                HitRadius = this.HitRadius,
                StiffnessForce = this.StiffnessForce,
                GravityPower = this.GravityPower,
                GravityDir = this.GravityDir,
                DragForce = this.DragForce
            };
        }
    }
}
