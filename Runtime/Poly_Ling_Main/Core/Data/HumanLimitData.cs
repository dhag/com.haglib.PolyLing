// Assets/Editor/Poly_Ling/Core/Data/HumanLimitData.cs
// ============================================================
// Humanoid マッスル可動域（per-bone 純POCOデータ契約）
// ============================================================
//
// 【格納規約】格納・参照・永続化の規約は
//   MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//
// 【役割】
//   Unity Humanoid の HumanLimit（1ボーン最大3マッスル軸の可動域）に対応する
//   per-bone の純データ。Type == MeshType.Bone の MeshObject に1つ付帯する。
//   非null ⇔ そのボーンにカスタム可動域を持たせる。null＝Unity 既定を使う。
//   3マッスル軸(X/Y/Z)は Min/Max/Center の Vector3 成分で表現する（リスト不要）。
//
// 【単位・座標系】
//   Min / Max / Center は角度（ラジアン）。IK の角度制限（IKLinkData）と単位を
//   統一する。Unity HumanLimit は「度(degree)」基準のため、Avatar 生成や
//   マッスル適用の I/O 境界で 度⇄ラジアン 変換を行う（本POCOは生値=ラジアンを保持）。
//
// 【段階メモ（5d-1：格納のみ）】
//   本POCOは追加のみ。格納（POCO＋DTO＋CSV/JSON 永続化）までを提供し、
//   consumer 差し替え（UnityClipApplier の HumanTrait.GetMuscleDefaultMin/Max を
//   本値へ置換する等）は別段階（5d-2）とし、本段階では触れない（非破壊）。
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
    /// Humanoid マッスル可動域の per-bone データ（純POCO）。
    /// MeshObject.HumanLimit として付帯し、非nullがカスタム可動域保持を表す。
    /// </summary>
    [Serializable]
    public class HumanLimitData
    {
        /// <summary>3マッスル軸の下限（X/Y/Z・ラジアン）。</summary>
        public Vector3 Min { get; set; } = Vector3.zero;

        /// <summary>3マッスル軸の上限（X/Y/Z・ラジアン）。</summary>
        public Vector3 Max { get; set; } = Vector3.zero;

        /// <summary>3マッスル軸の中央（X/Y/Z・ラジアン）。</summary>
        public Vector3 Center { get; set; } = Vector3.zero;

        /// <summary>軸長（Unity HumanLimit.axisLength）。</summary>
        public float AxisLength { get; set; } = 0f;

        /// <summary>true=Unity 既定可動域を使う（Min/Max/Center は無視）。</summary>
        public bool UseDefaultValues { get; set; } = true;

        /// <summary>ディープコピー。</summary>
        public HumanLimitData Clone()
        {
            return new HumanLimitData
            {
                Min = this.Min,
                Max = this.Max,
                Center = this.Center,
                AxisLength = this.AxisLength,
                UseDefaultValues = this.UseDefaultValues
            };
        }
    }
}
