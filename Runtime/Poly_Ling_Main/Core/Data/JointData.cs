// Assets/Editor/Poly_Ling/Core/Data/JointData.cs
// ============================================================
// JOINTデータ（純POCOデータ契約）
// ============================================================
//
// 【役割】
//   PMX互換のジョイント（剛体ジョイント）情報を MeshObject に持たせる純データ。
//   JOINTは Type == MeshType.RigidBodyJoint の MeshObject に付帯し、
//   頂点/面は持たない（表示は利用時にギズモとして生成する）。
//
// 【参照系：name主・index従】
//   接続する2剛体は BodyAName / BodyBName を一次キーとする。
//   RigidBodyIndexA / RigidBodyIndexB は実行時キャッシュであり、
//   並べ替え・エクスポートで無効化されうるため永続化の基準には用いない。
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
    /// JOINTデータ（純POCO）。MeshObject.JointData として保持する。
    /// JOINT名は付帯先 MeshObject.Name を用いる（本POCOでは重複保持しない）。
    /// </summary>
    [Serializable]
    public class JointData
    {
        /// <summary>JOINT名（英語）。日本語名は MeshObject.Name を使用。</summary>
        public string NameEnglish { get; set; } = "";

        /// <summary>
        /// ジョイントタイプ（PMX準拠。2.0では 0:6DOF(ばね付き) のみ。
        /// 2.1拡張で 1:6DOF, 2:P2P, 3:ConeTwist, 4:Slider, 5:Hinge）。
        /// </summary>
        public int JointType { get; set; } = 0;

        // --- 接続剛体（name主・index従） ---

        /// <summary>剛体A名（一次キー）。</summary>
        public string BodyAName { get; set; } = "";

        /// <summary>剛体B名（一次キー）。</summary>
        public string BodyBName { get; set; } = "";

        /// <summary>剛体AのMeshContextListインデックス（実行時キャッシュ。-1=未解決）。</summary>
        public int RigidBodyIndexA { get; set; } = -1;

        /// <summary>剛体BのMeshContextListインデックス（実行時キャッシュ。-1=未解決）。</summary>
        public int RigidBodyIndexB { get; set; } = -1;

        // --- 配置 ---

        /// <summary>位置（モデル空間）。</summary>
        public Vector3 Position { get; set; } = Vector3.zero;

        /// <summary>回転（モデル空間・ラジアン）。</summary>
        public Vector3 Rotation { get; set; } = Vector3.zero;

        // --- 拘束（移動・回転の上下限） ---

        /// <summary>移動下限。</summary>
        public Vector3 TranslationMin { get; set; } = Vector3.zero;

        /// <summary>移動上限。</summary>
        public Vector3 TranslationMax { get; set; } = Vector3.zero;

        /// <summary>回転下限（ラジアン）。</summary>
        public Vector3 RotationMin { get; set; } = Vector3.zero;

        /// <summary>回転上限（ラジアン）。</summary>
        public Vector3 RotationMax { get; set; } = Vector3.zero;

        // --- ばね定数 ---

        /// <summary>ばね定数（移動）。</summary>
        public Vector3 SpringTranslation { get; set; } = Vector3.zero;

        /// <summary>ばね定数（回転）。</summary>
        public Vector3 SpringRotation { get; set; } = Vector3.zero;

        /// <summary>ディープコピー（値型のみのため浅いコピーで足りる）。</summary>
        public JointData Clone()
        {
            return new JointData
            {
                NameEnglish = this.NameEnglish,
                JointType = this.JointType,
                BodyAName = this.BodyAName,
                BodyBName = this.BodyBName,
                RigidBodyIndexA = this.RigidBodyIndexA,
                RigidBodyIndexB = this.RigidBodyIndexB,
                Position = this.Position,
                Rotation = this.Rotation,
                TranslationMin = this.TranslationMin,
                TranslationMax = this.TranslationMax,
                RotationMin = this.RotationMin,
                RotationMax = this.RotationMax,
                SpringTranslation = this.SpringTranslation,
                SpringRotation = this.SpringRotation
            };
        }
    }
}
