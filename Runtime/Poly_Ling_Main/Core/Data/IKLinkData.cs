// Assets/Editor/Poly_Ling/Core/Data/IKLinkData.cs
// ============================================================
// IKリンクデータ（per-bone 純POCOデータ契約）
// ============================================================
//
// 【格納規約】格納・参照・永続化の規約は
//   MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//
// 【役割】
//   IKチェーンを構成する各リンクボーン（Type == MeshType.Bone の MeshObject）に
//   1つ付帯する。非null ⇔ そのボーンはIKリンク（役割はフラグ＝存在で表す。
//   SpringBoneJointData と同型）。所属チェーン・順序は明示リストを持たず、
//   IKルートの EffectorBoneName を起点にボーン階層（HierarchyParentIndex）を
//   辿って導出する（IKChainResolver）。
//
//   ※前提: 1ボーンは高々1つのIKチェーンのリンクに属する
//     （複数経路に属する場合は per-bone フラグでは一意に表せない）。
//
// 【座標系】
//   LimitMin / LimitMax は角度制限（ラジアン）。生値を保持し、系変換は
//   I/O 境界で行う（本POCOでは変換しない）。
//
// 【依存】
//   UnityEngine.Vector3 のみ。#if UNITY_EDITOR を含まない。
//
// 【段階メモ（#4a）】
//   本POCOは追加のみ。現段階では IKData.Links（IKルート集約）が源泉であり、
//   本 per-bone 形式とは IKChainResolver で相互同期する（併存・非破壊）。
//
// ============================================================

using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// IKリンクの per-bone データ（純POCO）。
    /// MeshObject.IKLink として付帯し、非nullがIKリンクであることを表す。
    /// </summary>
    [Serializable]
    public class IKLinkData
    {
        /// <summary>角度制限あり。</summary>
        public bool HasLimit { get; set; } = false;

        /// <summary>角度制限下限（ラジアン）。</summary>
        public Vector3 LimitMin { get; set; } = Vector3.zero;

        /// <summary>角度制限上限（ラジアン）。</summary>
        public Vector3 LimitMax { get; set; } = Vector3.zero;

        /// <summary>ディープコピー。</summary>
        public IKLinkData Clone()
        {
            return new IKLinkData
            {
                HasLimit = this.HasLimit,
                LimitMin = this.LimitMin,
                LimitMax = this.LimitMax
            };
        }
    }
}
