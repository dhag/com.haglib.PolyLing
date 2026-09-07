// Runtime/Poly_Ling_Main/Core/Data/VrmLookAtData.cs
// ============================================================
// VRM 1.0 視線設定（VRMC_vrm.lookAt）の純POCOデータ契約
// ============================================================
//
// 【役割】
//   目の追従の基準点と、頭の向き→目の動き の対応づけをモデルレベルで持つ。
//   UniVRM の VRM10ObjectLookAt と同じ項目で、写しは PolyLing.Vrm10 が行う。
//
// 【4本の対応づけ】
//   UniVRM の CurveMapper は曲線ではなく 2 値しか持たない
//   （CurveXRangeDegree / CurveYRangeDegree）。よってここも 2 値で持つ。
//     InputMaxDegrees … 頭をどこまで回したところで振り切るか[度]
//     OutputScale     … 振り切ったときの目の量。
//                       LookAtType が Bone なら目ボーンの角度[度]、
//                       Expression なら表情の重み(0-1)。
//
// 【既定値】
//   UniVRM の VRM10ObjectLookAt / CurveMapper の初期値に合わせてある
//   （OffsetFromHead = (0, 0.06, 0)、4本とも 90 → 10）。
//   ModelContext.VrmLookAt が null のときは何も書かないので、
//   その場合も出力は UniVRM の既定と同じになる。
//
// 【座標系】
//   OffsetFromHead は Unity 左手系のまま持つ。VRM（右手系）への反転は
//   UniVRM 側の ExportLookAt が ReverseX で行う（Vrm10Exporter.cs:743）。
//   ここでは変換しない。
//
// 【依存】
//   UnityEngine.Vector3 のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>視線を何で表すか。VRMC_vrm の lookAt.type。</summary>
    public enum VrmLookAtType
    {
        /// <summary>目ボーンを回す。</summary>
        Bone = 0,
        /// <summary>表情（lookUp/lookDown/lookLeft/lookRight）で表す。</summary>
        Expression = 1,
    }

    /// <summary>
    /// 頭の向き→目の動き の対応づけ 1 本ぶん。
    /// UniVRM の CurveMapper に対応する。
    /// </summary>
    [Serializable]
    public class VrmLookAtRangeMap
    {
        /// <summary>振り切る頭の角度[度]。0 は UniVRM 側で 90 に直される。</summary>
        public float InputMaxDegrees { get; set; } = 90f;

        /// <summary>振り切ったときの出力量（目の角度[度] または 表情の重み）。</summary>
        public float OutputScale { get; set; } = 10f;

        public VrmLookAtRangeMap() { }

        public VrmLookAtRangeMap(float inputMaxDegrees, float outputScale)
        {
            InputMaxDegrees = inputMaxDegrees;
            OutputScale     = outputScale;
        }

        /// <summary>ディープコピー。</summary>
        public VrmLookAtRangeMap Clone()
            => new VrmLookAtRangeMap(InputMaxDegrees, OutputScale);
    }

    /// <summary>
    /// VRM 1.0 の視線設定（純POCO）。ModelContext.VrmLookAt として1つ保持する。
    /// null＝未設定で、出力は UniVRM の既定のままになる。
    /// </summary>
    [Serializable]
    public class VrmLookAtData
    {
        /// <summary>目の基準点。頭ボーンから見た位置[m]。</summary>
        public Vector3 OffsetFromHead { get; set; } = new Vector3(0f, 0.06f, 0f);

        /// <summary>視線を目ボーンで表すか、表情で表すか。</summary>
        public VrmLookAtType LookAtType { get; set; } = VrmLookAtType.Bone;

        /// <summary>左右のうち、鼻側（内側）へ向くとき。</summary>
        public VrmLookAtRangeMap HorizontalInner { get; set; } = new VrmLookAtRangeMap(90f, 10f);

        /// <summary>左右のうち、こめかみ側（外側）へ向くとき。</summary>
        public VrmLookAtRangeMap HorizontalOuter { get; set; } = new VrmLookAtRangeMap(90f, 10f);

        /// <summary>下を向くとき。</summary>
        public VrmLookAtRangeMap VerticalDown { get; set; } = new VrmLookAtRangeMap(90f, 10f);

        /// <summary>上を向くとき。</summary>
        public VrmLookAtRangeMap VerticalUp { get; set; } = new VrmLookAtRangeMap(90f, 10f);

        /// <summary>ディープコピー。</summary>
        public VrmLookAtData Clone()
        {
            return new VrmLookAtData
            {
                OffsetFromHead  = this.OffsetFromHead,
                LookAtType      = this.LookAtType,
                HorizontalInner = this.HorizontalInner?.Clone() ?? new VrmLookAtRangeMap(),
                HorizontalOuter = this.HorizontalOuter?.Clone() ?? new VrmLookAtRangeMap(),
                VerticalDown    = this.VerticalDown?.Clone()    ?? new VrmLookAtRangeMap(),
                VerticalUp      = this.VerticalUp?.Clone()      ?? new VrmLookAtRangeMap(),
            };
        }
    }
}
