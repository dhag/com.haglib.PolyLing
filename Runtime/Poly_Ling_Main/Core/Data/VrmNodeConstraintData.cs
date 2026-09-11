// Runtime/Poly_Ling_Main/Core/Data/VrmNodeConstraintData.cs
// ============================================================
// VRM 1.0 ノード制約（VRMC_node_constraint）の純POCOデータ契約
// ============================================================
//
// 【役割】
//   あるノードの回転を、別のノード（Source）の動きに連動させる設定。
//   glTF ノード 1 つにつき制約は 1 つ（Roll / Aim / Rotation のいずれか）
//   （Format.g.cs:88-120）。MeshObject.VrmConstraint として付帯し、
//   null＝制約なし。
//
// 【PolyLing 側での評価】
//   しない。保持と書き戻しのみ。
//
// 【参照は名前】
//   Source は MeshObject.Name で持つ（MeshObject.cs「ボーン付帯データ格納規約」2）。
//
// 【座標系】
//   AimAxis は Unity 左手系の値で持つ。VRM（右手系）との変換は I/O 境界で
//   X 軸の正負を入れ替える（Vrm10Importer.cs:731 / Vrm10Exporter.cs:643）。
//   RollAxis は変換しない（同 :723 / :652）。
//
// 【なぜ PolyLing 側で enum を定義するか】
//   VrmMetaData.cs と同じ理由（分離規約は IVrm10Exporter.cs 冒頭を正典とする）。
//   境界では switch で写す。(int) キャストで写さないこと。
//
// 【依存】
//   UnityEngine の型を使わない。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;

namespace Poly_Ling.Data
{
    /// <summary>制約の種類。</summary>
    public enum VrmConstraintKind
    {
        /// <summary>Source の回転のうち、指定軸まわりのひねり成分だけを移す。</summary>
        Roll = 0,
        /// <summary>自ノードの指定軸を Source の位置へ向ける。</summary>
        Aim = 1,
        /// <summary>Source の回転の変化分をそのまま移す。</summary>
        Rotation = 2,
    }

    /// <summary>Roll 制約の軸。VRMC_node_constraint の RollAxis。</summary>
    public enum VrmRollAxis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    /// <summary>Aim 制約の軸。VRMC_node_constraint の AimAxis（値は Unity 側）。</summary>
    public enum VrmAimAxis
    {
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5,
    }

    /// <summary>
    /// VRM 1.0 ノード制約（純POCO）。MeshObject.VrmConstraint として付帯する。
    /// </summary>
    [Serializable]
    public class VrmNodeConstraintData
    {
        /// <summary>制約の種類。</summary>
        public VrmConstraintKind Kind { get; set; } = VrmConstraintKind.Rotation;

        /// <summary>制約元ノードの名前（MeshObject.Name）。</summary>
        public string SourceName { get; set; } = "";

        /// <summary>効かせる強さ（0-1）。</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>Roll のときの軸。</summary>
        public VrmRollAxis RollAxis { get; set; } = VrmRollAxis.X;

        /// <summary>Aim のときの軸（Unity 側の値）。</summary>
        public VrmAimAxis AimAxis { get; set; } = VrmAimAxis.PositiveX;

        /// <summary>ディープコピー。</summary>
        public VrmNodeConstraintData Clone()
        {
            return new VrmNodeConstraintData
            {
                Kind       = this.Kind,
                SourceName = this.SourceName,
                Weight     = this.Weight,
                RollAxis   = this.RollAxis,
                AimAxis    = this.AimAxis,
            };
        }
    }
}
