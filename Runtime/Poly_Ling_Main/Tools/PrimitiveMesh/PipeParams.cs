// PipeParams.cs
// パイプ生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;   // PrimitiveMeshPostProcess.PivotMin / PivotMax

namespace Poly_Ling.Pipe
{
    /// <summary>
    /// パイプ生成パラメータ。断面プロファイルはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PipeParams : IEquatable<PipeParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

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

        /// <summary>開いた梯子のとき、両端に蓋を張るか。</summary>
        [PLParam(TextKey = "PipeCapEnds", Description = "開いた梯子のとき、両端に蓋を張る")]
        public bool CapEnds;

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

        // ── ピボット ──
        /// <summary>AABB サイズ基準のピボット。生成後に -Pivot * サイズ だけ平行移動する</summary>
        [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                 Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
        public Vector3 Pivot;

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
            FlipFaces     = false,
            Pivot         = Vector3.zero,
        };

        public bool Equals(PipeParams o)
            => MeshName == o.MeshName
            && CapEnds  == o.CapEnds
            && Mathf.Approximately(Thickness, o.Thickness)
            && SegmentsFront == o.SegmentsFront
            && SegmentsBack  == o.SegmentsBack
            && Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)
            && Mathf.Approximately(EdgeSizeBack,  o.EdgeSizeBack)
            && EdgeInward == o.EdgeInward
            && FlipFaces  == o.FlipFaces
            && Pivot      == o.Pivot;

        public override bool Equals(object obj) => obj is PipeParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
