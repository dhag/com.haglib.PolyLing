// FrillParams.cs
// フリル生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Frill
{
    /// <summary>rung 境界（ステップ s の終端とステップ s+1 の始端）の扱い。</summary>
    public enum FrillRungSeam
    {
        /// <summary>両者の生成位置を平均して1頂点にまとめ、段差を消す。</summary>
        Merge = 0,
        /// <summary>別頂点のまま残し、段差をそのまま出す。</summary>
        Split = 1,
    }

    /// <summary>
    /// フリル生成パラメータ。
    /// 第1段階では名前のみ。断面プロファイル関連は後続で追加する。
    /// </summary>
    [Serializable]
    public struct FrillParams : IEquatable<FrillParams>
    {
        public string MeshName;

        // ── 高さ倍率 ──
        /// <summary>
        /// フリルの高さ（断面プロファイルの Y ＝ 基準ベルト面の法線方向）の倍率。
        /// 1 で従来どおり。0 にすると基準ベルトと同じ平坦なリボンになる。
        /// 梯子ごとの倍率とは掛け算で合成する。
        /// </summary>
        public float HeightScale;

        // ── 共有レールの接続 ──
        /// <summary>
        /// 同一のレール線分（縦置きなら左右、横置きなら上下）を共有する梯子どうしを
        /// 1枚のメッシュとして溶接する。false なら梯子ごとに独立生成する。
        /// </summary>
        public bool ConnectShared;

        // ── rung 境界 ──
        /// <summary>
        /// ステップ s の終端（プロファイル index m-1）と、ステップ s+1 の始端（index 0）の扱い。
        /// プロファイル両端の y が違う／梯子が曲がっている／rung 間隔が不均一、のいずれかで両者はずれる。
        /// </summary>
        public FrillRungSeam RungSeam;

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

        // ── 面の向き ──
        /// <summary>生成後にメッシュ全体の面を反転する</summary>
        public bool  FlipFaces;

        // ── ピボット ──
        /// <summary>AABB サイズ基準のピボット。生成後に -Pivot * サイズ だけ平行移動する</summary>
        public Vector3 Pivot;

        public static FrillParams Default => new FrillParams
        {
            MeshName      = "Frill",
            HeightScale   = 1f,
            ConnectShared = true,
            RungSeam      = FrillRungSeam.Merge,
            Thickness     = 0f,
            SegmentsFront = 0,
            SegmentsBack  = 0,
            EdgeSizeFront = 0.02f,
            EdgeSizeBack  = 0.02f,
            EdgeInward    = false,
            FlipFaces     = false,
            Pivot         = Vector3.zero,
        };

        public bool Equals(FrillParams o)
            => MeshName == o.MeshName
            && Mathf.Approximately(HeightScale, o.HeightScale)
            && ConnectShared == o.ConnectShared
            && RungSeam == o.RungSeam
            && Mathf.Approximately(Thickness, o.Thickness)
            && SegmentsFront == o.SegmentsFront
            && SegmentsBack  == o.SegmentsBack
            && Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)
            && Mathf.Approximately(EdgeSizeBack,  o.EdgeSizeBack)
            && EdgeInward == o.EdgeInward
            && FlipFaces  == o.FlipFaces
            && Pivot      == o.Pivot;

        public override bool Equals(object obj) => obj is FrillParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
