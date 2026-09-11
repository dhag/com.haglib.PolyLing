// MeshTypes.cs
// 描画オブジェクトの種類・一人称での見え方・種別の列挙（MeshObject.cs から分離）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（MeshObject.cs と同じ名前空間。MeshObject.cs から分割）

using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{
    // ============================================================
    // MeshType 定義（MeshContext.MeshTypeと統一）
    // ============================================================

    /// <summary>
    /// メッシュの種類
    /// </summary>
    public enum MeshType
    {
        /// <summary>通常のメッシュ</summary>
        Mesh = 0,
        /// <summary>ボーン</summary>
        Bone = 1,
        /// <summaryモーフオブジェクト</summary>
        Morph = 2,
        /// <summary>剛体オブジェクト</summary>
        RigidBody = 3,
        /// <summary>剛体ジョイントオブジェクト</summary>
        RigidBodyJoint = 4,
        /// <summary>ヘルパーオブジェクト</summary>
        Helper = 5,
        /// <summary>グループ</summary>
        Group = 6,
        /// <summary>ベイクされたミラーメッシュ</summary>
        BakedMirror = 7,
        /// <summary>MirrorPairのミラー側（サーフェス描画のみ、頂点・辺・ヒットテスト対象外）</summary>
        MirrorSide = 8
    }

    // ============================================================
    // 一人称カメラでの見え方（VRM FirstPerson）
    // ============================================================

    /// <summary>
    /// VRM の firstPerson.meshAnnotations に対応する、描画オブジェクトごとの
    /// 一人称カメラでの扱い。
    ///
    /// 【値の並び】
    ///   UniGLTF.Extensions.VRMC_vrm.FirstPersonType と同じ並びにしてある
    ///   （auto / both / thirdPersonOnly / firstPersonOnly）。
    ///   PolyLing.Vrm10 側で (int) キャストせず switch で写すが、
    ///   並べ替えると読み手が混乱するので順序は変えないこと。
    ///
    /// 【既定は Auto】
    ///   VRM 仕様の既定と同じ。Auto のノードは出力に書かない
    ///   （書かないことが「auto として扱ってよい」の意味になる）。
    /// </summary>
    public enum VrmFirstPersonType
    {
        /// <summary>頭の子孫かどうかで自動判定させる（VRM 既定）。</summary>
        Auto = 0,
        /// <summary>一人称・三人称の両方で描く。</summary>
        Both = 1,
        /// <summary>三人称でだけ描く（自分の視界からは消える）。</summary>
        ThirdPersonOnly = 2,
        /// <summary>一人称でだけ描く。</summary>
        FirstPersonOnly = 3,
    }

    // ============================================================
    // 描画オブジェクトの種別（MeshFilter 系 / SkinnedMesh 系）
    // ============================================================

    /// <summary>
    /// 描画オブジェクトの種別。MeshObject が持つ明示状態であり、
    /// 頂点のボーンウェイトから毎回導出してはならない。
    ///
    /// 【なぜ明示状態か】
    ///   従来は Vertices.Any(v =&gt; v.HasBoneWeight) で毎回判定していた。
    ///   この判定はカメラ操作・ドラッグ・行列アップロードのたびに
    ///   全 MeshContext × 全頂点を走査する。種別は編集操作でしか変わらないため、
    ///   状態として持ち、変わる契機でだけ再計算する。
    ///
    /// 【頂点ウェイトの有無とは別物】
    ///   ウェイト付き頂点が 0 個になっても種別は自動で戻らない。
    ///   MeshFilter へ戻すのは明示操作（ClearAllBoneWeights など）だけ。
    ///   実データの検査が要る箇所は AnyVertexHasBoneWeight() を使うこと。
    /// </summary>
    public enum SkinKind
    {
        /// <summary>MeshFilter 系。頂点はローカル空間、描画は WorldMatrix を直接使う。</summary>
        MeshFilter = 0,

        /// <summary>SkinnedMesh 系。頂点はワールド（バインド）空間、描画は SkinningMatrix を通す。</summary>
        Skinned = 1
    }
}
