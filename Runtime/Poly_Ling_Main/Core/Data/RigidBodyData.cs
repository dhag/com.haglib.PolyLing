// Assets/Editor/Poly_Ling/Core/Data/RigidBodyData.cs
// ============================================================
// 剛体データ（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   PMX互換の剛体（RigidBody）情報を MeshObject に持たせるための純データ。
//   剛体は Type == MeshType.RigidBody の MeshObject に付帯し、
//   頂点/面は持たない（形状は利用時にギズモとして生成する）。
//
// 【参照系：name主・index従】
//   関連ボーンは RelatedBoneName を一次キーとする。
//   BoneIndex は実行時キャッシュであり、ヒエラルキー並べ替えや
//   Unityヒエラルキーエクスポートで容易に無効化されるため、
//   永続化・再構築の基準には用いない（解決はインポート/エクスポート段で行う）。
//
// 【座標系】
//   Position / Rotation はモデル空間（PMX左手・モデルは-Z向き）の値を保持する。
//   Unity との相互変換は既存 CoordinateConverter 経由で行い、本POCOでは変換しない。
//   Rotation はラジアン（PMX準拠）。
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
    /// 剛体形状種別（PMX準拠）。
    /// </summary>
    public enum RigidBodyShape
    {
        /// <summary>球</summary>
        Sphere = 0,
        /// <summary>箱</summary>
        Box = 1,
        /// <summary>カプセル</summary>
        Capsule = 2
    }

    /// <summary>
    /// 剛体の物理演算モード（PMX準拠）。
    /// </summary>
    public enum RigidBodyPhysicsMode
    {
        /// <summary>ボーン追従（物理演算なし）</summary>
        FollowBone = 0,
        /// <summary>物理演算</summary>
        Physics = 1,
        /// <summary>物理演算 + ボーン位置合わせ</summary>
        PhysicsAndBone = 2
    }

    /// <summary>
    /// 剛体データ（純POCO）。MeshObject.RigidBodyData として保持する。
    /// 剛体名は付帯先 MeshObject.Name を用いる（本POCOでは重複保持しない）。
    /// </summary>
    [Serializable]
    public class RigidBodyData
    {
        /// <summary>剛体名（英語）。日本語名は MeshObject.Name を使用。</summary>
        public string NameEnglish { get; set; } = "";

        // --- 関連ボーン（name主・index従） ---

        /// <summary>関連ボーン名（一次キー）。未関連は空文字。</summary>
        public string RelatedBoneName { get; set; } = "";

        /// <summary>関連ボーンのMeshContextListインデックス（実行時キャッシュ。-1=未解決）。</summary>
        public int BoneIndex { get; set; } = -1;

        // --- 衝突グループ ---

        /// <summary>所属グループ（0..15）。</summary>
        public int Group { get; set; } = 0;

        /// <summary>非衝突グループマスク（ビット立ち=非衝突）。</summary>
        public ushort CollisionMask { get; set; } = 0;

        // --- 形状 ---

        /// <summary>形状（球/箱/カプセル）。</summary>
        public RigidBodyShape Shape { get; set; } = RigidBodyShape.Sphere;

        /// <summary>サイズ（形状により意味が異なる：球=x半径, 箱=xyz, カプセル=x半径・y高さ）。</summary>
        public Vector3 Size { get; set; } = Vector3.one;

        /// <summary>位置（モデル空間）。</summary>
        public Vector3 Position { get; set; } = Vector3.zero;

        /// <summary>回転（モデル空間・ラジアン）。</summary>
        public Vector3 Rotation { get; set; } = Vector3.zero;

        // --- 物理パラメータ ---

        /// <summary>質量。</summary>
        public float Mass { get; set; } = 1f;

        /// <summary>移動減衰。</summary>
        public float LinearDamping { get; set; } = 0f;

        /// <summary>回転減衰。</summary>
        public float AngularDamping { get; set; } = 0f;

        /// <summary>反発力。</summary>
        public float Restitution { get; set; } = 0f;

        /// <summary>摩擦力。</summary>
        public float Friction { get; set; } = 0f;

        /// <summary>物理演算モード。</summary>
        public RigidBodyPhysicsMode PhysicsMode { get; set; } = RigidBodyPhysicsMode.FollowBone;

        /// <summary>ディープコピー（値型のみのため浅いコピーで足りる）。</summary>
        public RigidBodyData Clone()
        {
            return new RigidBodyData
            {
                NameEnglish = this.NameEnglish,
                RelatedBoneName = this.RelatedBoneName,
                BoneIndex = this.BoneIndex,
                Group = this.Group,
                CollisionMask = this.CollisionMask,
                Shape = this.Shape,
                Size = this.Size,
                Position = this.Position,
                Rotation = this.Rotation,
                Mass = this.Mass,
                LinearDamping = this.LinearDamping,
                AngularDamping = this.AngularDamping,
                Restitution = this.Restitution,
                Friction = this.Friction,
                PhysicsMode = this.PhysicsMode
            };
        }
    }
}
