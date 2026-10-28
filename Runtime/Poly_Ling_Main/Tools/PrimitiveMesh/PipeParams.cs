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
        /// <summary>
        /// 取り込み元のはしごへ塗ってあるボーンウェイトを、生成した頂点へ引き継ぐ。
        ///
        /// はしごの点は取り込み元の頂点の位置そのものなので、生成の直前に
        /// 位置で突き合わせて引く（BeltWeightBinder）。頂点は線分の両端を
        /// 断面 x で混ぜたウェイトを持つ。引けない点は何も持たない。
        /// </summary>
        [PLParam(TextKey = "BeltInheritWeights",
                 Description = "取り込み元のはしごのボーンウェイトを生成した頂点へ引き継ぐ")]
        public bool InheritBeltWeights;

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

        // ── 旧ツール互換（NCSHAGLIB の髪パイプ生成 HairMakerHairA / CreatePipe_） ──
        //   一括（LegacyCompat）を入れると下の 4 つが全部入る。個別に入れることもできる。

        /// <summary>旧ツール互換の 4 項目（座標系・断面の縮尺・頂点の並び・UV）をまとめて入れる。</summary>
        [PLParam(TextKey = "PipeLegacyCompat",
                 Description = "旧ツール（NCSHAGLIB の髪パイプ生成）互換。座標系・断面の縮尺・頂点の並び・UV の 4 つをまとめて旧方式にする")]
        public bool LegacyCompat;

        /// <summary>
        /// 断面の上方向を旧方式にする。段の両端と次の段の中点（最後の段は終点）が作る三角形の法線を上にする。
        /// 切ると、隣り合う帯の面法線の平均を上にする（現行）。
        /// </summary>
        [PLParam(TextKey = "PipeLegacyFrame",
                 Description = "断面の上方向を旧方式にする（段の両端と次の段の中点が作る三角形の法線）。一括が入っていれば入る")]
        public bool LegacyFrame;

        /// <summary>
        /// 断面を正規化せず、生の座標を段の中点に置く。各軸に「段の長さ × 断面スケール」を掛ける（旧方式）。
        /// 切ると、長辺が 1 になるよう正規化した断面を左レールから置く（現行）。
        /// 入っているときは、断面を取り込むときにも正規化しない。
        /// </summary>
        [PLParam(TextKey = "PipeLegacyRawProfile",
                 Description = "断面を正規化せず生の座標で使い、段の中点に置いて段の長さ×断面スケールを掛ける（旧方式）。一括が入っていれば入る")]
        public bool LegacyRawProfile;

        /// <summary>頂点を「始点・断面×段・終点」の順に並べる（旧方式）。切ると始点・終点は最後に足す。</summary>
        [PLParam(TextKey = "PipeLegacyVertexOrder",
                 Description = "頂点を「始点・断面×段・終点」の順に並べる（旧方式）。一括が入っていれば入る")]
        public bool LegacyVertexOrder;

        /// <summary>
        /// UV を旧方式にする（断面の周りが u、長さ方向が v。PMX/MQO へ書き出したとき始点 (0.5,0)・終点 (0.5,1)）。
        /// PolyLing の中では Unity の向き（下が v=0）で持つので、v は 1 − 段/(段数+1)。
        /// </summary>
        [PLParam(TextKey = "PipeLegacyUV",
                 Description = "UV を旧方式にする（断面の周りが u、長さ方向が v。PMX/MQO へ書き出したとき始点 (0.5,0)・終点 (0.5,1)）。一括が入っていれば入る")]
        public bool LegacyUV;

        /// <summary>
        /// 旧方式の断面スケール。各成分が 0 以上なら段の長さに対する倍率、負なら絶対の大きさ。
        /// 旧ツールの既定は (1,1,0)（断面の z を捨てる）。断面を正規化しないときだけ使う。
        /// </summary>
        [PLParam(TextKey = "PipeLegacyCutEndScale",
                 Description = "旧方式の断面スケール。0 以上は段の長さに対する倍率、負は絶対の大きさ。断面を正規化しないときだけ使う")]
        public Vector3 LegacyCutEndScale;

        /// <summary>終点を最後の輪の頂点の平均位置へ置く（旧ツールの「終端ぱっつん」）。</summary>
        [PLParam(TextKey = "PipePattsunEnd",
                 Description = "毛先の扇の要を最後の輪の平均位置へ置く（終端ぱっつん）")]
        public bool PattsunEnd;

        public bool UseLegacyFrame       => LegacyCompat || LegacyFrame;
        public bool UseLegacyRawProfile  => LegacyCompat || LegacyRawProfile;
        public bool UseLegacyVertexOrder => LegacyCompat || LegacyVertexOrder;
        public bool UseLegacyUV          => LegacyCompat || LegacyUV;

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
            LegacyCutEndScale = new Vector3(1f, 1f, 0f),
        };

        public bool Equals(PipeParams o)
            => MeshName == o.MeshName
            && CapEnds  == o.CapEnds
            && LegacyCompat      == o.LegacyCompat
            && LegacyFrame       == o.LegacyFrame
            && LegacyRawProfile  == o.LegacyRawProfile
            && LegacyVertexOrder == o.LegacyVertexOrder
            && LegacyUV          == o.LegacyUV
            && LegacyCutEndScale == o.LegacyCutEndScale
            && PattsunEnd        == o.PattsunEnd
            && InheritBeltWeights == o.InheritBeltWeights
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
