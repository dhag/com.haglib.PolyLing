// PipeParams.cs
// パイプ生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Pipe
{
    /// <summary>
    /// パイプ生成パラメータ。断面プロファイルはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PipeParams : IEquatable<PipeParams>
    {
        public string MeshName;

        /// <summary>開いた梯子のとき、両端に蓋を張るか。</summary>
        public bool CapEnds;

        // ── 厚み付け（0 で厚み付けなし。ベベル規約は FaceGroupSolidifier と同じ） ──
        /// <summary>総厚み。各シェルは ±Thickness/2 移動する</summary>
        public float Thickness;
        /// <summary>表側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int   SegmentsFront;
        /// <summary>裏側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int   SegmentsBack;
        /// <summary>表側エッジサイズ（面内インセット量＝法線方向の深さ）</summary>
        public float EdgeSizeFront;
        /// <summary>裏側エッジサイズ</summary>
        public float EdgeSizeBack;
        /// <summary>ラウンドの曲率方向を入れ替える</summary>
        public bool  EdgeInward;

        public static PipeParams Default => new PipeParams
        {
            MeshName      = "Pipe",
            CapEnds       = true,
            Thickness     = 0f,
            SegmentsFront = 0,
            SegmentsBack  = 0,
            EdgeSizeFront = 0.02f,
            EdgeSizeBack  = 0.02f,
            EdgeInward    = false,
        };

        public bool Equals(PipeParams o)
            => MeshName == o.MeshName
            && CapEnds  == o.CapEnds
            && Mathf.Approximately(Thickness, o.Thickness)
            && SegmentsFront == o.SegmentsFront
            && SegmentsBack  == o.SegmentsBack
            && Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)
            && Mathf.Approximately(EdgeSizeBack,  o.EdgeSizeBack)
            && EdgeInward == o.EdgeInward;

        public override bool Equals(object obj) => obj is PipeParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
