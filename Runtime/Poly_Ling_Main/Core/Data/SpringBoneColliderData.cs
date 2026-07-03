// Assets/Editor/Poly_Ling/Core/Data/SpringBoneColliderData.cs
// ============================================================
// スプリングボーン・コライダーデータ（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   VRMC_springBone(VRM SpringBone 1.0) の collider に相当する純データ。
//   コライダーは付帯先ボーン（Type == MeshType.Bone の MeshObject）に
//   List で複数付帯できる。形状は利用時にギズモとして生成する。
//   ※物理演算（RigidBodyData）とは別物。名称は SpringBone 接頭辞で統一する。
//
// 【所属グループ：index参照（コンテナ内部）】
//   所属する ColliderGroup は ModelContext.SpringBoneColliderGroupNames への
//   index で参照する（VRM 同様、1コライダーが複数グループに属せるため List）。
//   グループ名リストはモデルレベルに1つだけ持ち、index はその並び順。
//
// 【座標系】
//   Offset / Tail / Normal は付帯ボーンのローカル（working空間）の生値を保持する。
//   Unity 左手系のまま格納し、系変換は VRM 等 I/O 境界で行う（本POCOでは変換しない）。
//
// 【依存】
//   UnityEngine.Vector3 のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// スプリングボーン・コライダー形状（VRM springBone / extended collider 準拠）。
    /// </summary>
    public enum SpringBoneColliderShape
    {
        /// <summary>球（外側に押し出す）</summary>
        Sphere = 0,
        /// <summary>カプセル（外側に押し出す）</summary>
        Capsule = 1,
        /// <summary>内側球（球の内側に閉じ込める）</summary>
        InsideSphere = 2,
        /// <summary>内側カプセル（カプセルの内側に閉じ込める）</summary>
        InsideCapsule = 3,
        /// <summary>平面（法線側に押し出す）</summary>
        Plane = 4
    }

    /// <summary>
    /// スプリングボーン・コライダーデータ（純POCO）。
    /// MeshObject.SpringBoneColliders の要素として、付帯先ボーンに複数保持する。
    /// </summary>
    [Serializable]
    public class SpringBoneColliderData
    {
        /// <summary>形状（球/カプセル/内側球/内側カプセル/平面）。</summary>
        public SpringBoneColliderShape Shape { get; set; } = SpringBoneColliderShape.Sphere;

        /// <summary>中心オフセット（付帯ボーンのローカル）。</summary>
        public Vector3 Offset { get; set; } = Vector3.zero;

        /// <summary>半径。</summary>
        public float Radius { get; set; } = 0.05f;

        /// <summary>カプセル終点（付帯ボーンのローカル。Capsule/InsideCapsule のみ）。</summary>
        public Vector3 Tail { get; set; } = Vector3.zero;

        /// <summary>平面法線（付帯ボーンのローカル。Plane のみ）。</summary>
        public Vector3 Normal { get; set; } = Vector3.up;

        /// <summary>
        /// 所属 ColliderGroup の index（ModelContext.SpringBoneColliderGroupNames への index）。
        /// 1コライダーが複数グループに属せる（VRM 準拠）。空=どのグループにも属さない。
        /// </summary>
        public List<int> SpringBoneGroupIndices { get; set; } = new List<int>();

        /// <summary>ディープコピー。</summary>
        public SpringBoneColliderData Clone()
        {
            return new SpringBoneColliderData
            {
                Shape = this.Shape,
                Offset = this.Offset,
                Radius = this.Radius,
                Tail = this.Tail,
                Normal = this.Normal,
                SpringBoneGroupIndices = this.SpringBoneGroupIndices != null
                    ? new List<int>(this.SpringBoneGroupIndices)
                    : new List<int>()
            };
        }
    }
}
