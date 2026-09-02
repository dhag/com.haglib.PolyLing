// FrillParams.cs
// フリル生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;   // PrimitiveMeshPostProcess.PivotMin / PivotMax

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
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>高さ倍率の下限・上限</summary>
        public const float HeightScaleMin = 0f;
        public const float HeightScaleMax = 5f;

        /// <summary>厚みの下限・上限</summary>
        public const float ThicknessMin = 0f;
        public const float ThicknessMax = 0.5f;

        /// <summary>エッジ分割数の下限・上限</summary>
        public const int EdgeSegmentsMin = 0;
        public const int EdgeSegmentsMax = 16;

        /// <summary>エッジサイズの下限・上限</summary>
        public const float EdgeSizeMin = 0.001f;
        public const float EdgeSizeMax = 0.25f;

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        // ── 高さ倍率 ──
        /// <summary>
        /// フリルの高さ（断面プロファイルの Y ＝ 基準ベルト面の法線方向）の倍率。
        /// 1 で従来どおり。0 にすると基準ベルトと同じ平坦なリボンになる。
        /// 梯子ごとの倍率とは掛け算で合成する。
        /// </summary>
        [PLParam(TextKey = "FrillHeightScale", Description = "断面プロファイルの Y（法線方向）に掛ける倍率", Min = HeightScaleMin,
                 Max = HeightScaleMax)]
        public float HeightScale;

        // ── 共有レールの接続 ──
        /// <summary>
        /// 同一のレール線分（縦置きなら左右、横置きなら上下）を共有する梯子どうしを
        /// 1枚のメッシュとして溶接する。false なら梯子ごとに独立生成する。
        /// </summary>
        [PLParam(TextKey = "FrillConnectShared", Description = "同一レールを共有する梯子どうしを 1 枚に溶接する")]
        public bool ConnectShared;

        // ── rung 境界 ──
        /// <summary>
        /// ステップ s の終端（プロファイル index m-1）と、ステップ s+1 の始端（index 0）の扱い。
        /// プロファイル両端の y が違う／梯子が曲がっている／rung 間隔が不均一、のいずれかで両者はずれる。
        /// </summary>
        [PLParam(TextKey = "FrillRungSeam", Description = "rung 境界の扱い（分ける / まとめる）")]
        public FrillRungSeam RungSeam;

        // ── 厚み付け（0 で厚み付けなし。ベベル規約は FaceGroupSolidifier と同じ） ──
        /// <summary>総厚み。各シェルは ±Thickness/2 移動する</summary>
        [PLParam(TextKey = "Thickness", Description = "総厚み。0 で厚み付けなし", Min = ThicknessMin, Max = ThicknessMax)]
        public float Thickness;
        /// <summary>表側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        [PLParam(TextKey = "FrontSegments", Description = "表側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsFront;
        /// <summary>裏側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        [PLParam(TextKey = "BackSegments", Description = "裏側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                 Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
        public int SegmentsBack;
        /// <summary>表側エッジサイズ（面内インセット量＝法線方向の深さ）</summary>
        [PLParam(TextKey = "EdgeSize", Description = "表側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeFront;
        /// <summary>裏側エッジサイズ</summary>
        [PLParam(TextKey = "EdgeSize", Description = "裏側エッジのサイズ", Min = EdgeSizeMin, Max = EdgeSizeMax)]
        public float EdgeSizeBack;
        /// <summary>ラウンドの曲率方向を入れ替える</summary>
        [PLParam(TextKey = "EdgeInward", Description = "ラウンドの曲率方向を入れ替える")]
        public bool EdgeInward;

        // ── 面の向き ──
        /// <summary>生成後にメッシュ全体の面を反転する</summary>
        [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
        public bool FlipFaces;

        // ── 断面プロファイル（拡張モード） ──
        /// <summary>
        /// 断面プロファイルを A / B の2本にする。
        /// 段グループの t=0 側レールが A、t=1 側レールが B、中間の段は線形補間になる。
        /// 段が1本しかない場合は Left レールが A、Right レールが B。
        /// 点数が違うときは点数の少ない側を両方に使う。
        /// </summary>
        [PLParam(TextKey = "FrillTwoProfiles", Description = "断面プロファイルを A / B の 2 本にする")]
        public bool TwoProfiles;

        /// <summary>
        /// A / B の割り当てを上下反転する（t → 1-t）。
        /// 梯子の上下は幾何的に定義できないため、目視で合わせるための切替。
        /// </summary>
        [PLParam(TextKey = "FrillProfileFlip", Description = "A / B の割り当てを上下反転する")]
        public bool ProfileFlip;

        // ── ピボット ──
        /// <summary>AABB サイズ基準のピボット。生成後に -Pivot * サイズ だけ平行移動する</summary>
        [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                 Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
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
            TwoProfiles   = false,
            ProfileFlip   = false,
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
            && TwoProfiles == o.TwoProfiles
            && ProfileFlip == o.ProfileFlip
            && Pivot      == o.Pivot;

        public override bool Equals(object obj) => obj is FrillParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
