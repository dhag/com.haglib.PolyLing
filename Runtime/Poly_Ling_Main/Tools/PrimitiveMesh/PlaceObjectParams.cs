// PlaceObjectParams.cs
// オブジェクト配置の生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using Poly_Ling.Data;

namespace Poly_Ling.PlaceObject
{
    /// <summary>配置物の倍率の決め方。</summary>
    public enum PlaceScaleMode
    {
        /// <summary>rung 長に比例させる。倍率 = rung 長 × Scale。梯子の幅に合わせて大小が変わる。</summary>
        RungLength = 0,
        /// <summary>rung 長を使わない一律サイズ。倍率 = Scale。梯子の幅に関係なく同じ大きさになる。</summary>
        Uniform    = 1,
    }

    /// <summary>配置元が複数のときの割り当て方式。</summary>
    public enum PlaceSourceMode
    {
        /// <summary>選択した全オブジェクトを1つに結合し、全 rung へ同じものを配置する。</summary>
        Combine  = 0,
        /// <summary>rung ごとに選択リストを先頭から巡回して配置する。</summary>
        Sequence = 1,
        /// <summary>rung ごとにシード固定の乱数で選んで配置する。</summary>
        Random   = 2,
    }

    /// <summary>
    /// オブジェクト配置パラメータ。配置元オブジェクトはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PlaceObjectParams : IEquatable<PlaceObjectParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>間引き間隔の下限・上限</summary>
        public const int StrideMin = 1;
        public const int StrideMax = 10;

        /// <summary>間引き開始位置の下限・上限</summary>
        public const int OffsetMin = 0;
        public const int OffsetMax = 9;

        /// <summary>倍率の下限・上限</summary>
        public const float ScaleMin = 0.01f;
        public const float ScaleMax = 10f;

        /// <summary>ロールの段数の下限・上限（90°単位）</summary>
        public const int RollStepsMin = 0;
        public const int RollStepsMax = 3;

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        /// <summary>配置元が複数のときの割り当て方式。</summary>
        [PLParam(TextKey = "PlaceMode", Description = "配置元が複数のときの割り当て方式（結合 / 順番 / 抽選）")]
        public PlaceSourceMode Mode;

        /// <summary>Random 時の乱数シード。同じシード・同じ入力なら同一結果になる。</summary>
        [PLParam(TextKey = "PlaceRandomSeed", Description = "抽選の乱数シード。同じシード・同じ入力なら同一結果になる")]
        public int RandomSeed;

        /// <summary>配置物の倍率の決め方。既定は rung 長に比例（従来どおり）。</summary>
        [PLParam(TextKey = "PlaceScaleMode", Description = "倍率の決め方（rung 長に比例 / 一定）")]
        public PlaceScaleMode ScaleMode;

        /// <summary>
        /// 配置物の倍率。X/Y/Z 連動の1値。
        /// ScaleMode = RungLength では rung 長への掛け率（1 で rung 長と等倍）。
        /// ScaleMode = Uniform     では倍率そのもの（1 で配置元のローカル座標のまま）。
        /// </summary>
        [PLParam(TextKey = "Scale", Description = "配置物の倍率", Min = ScaleMin, Max = ScaleMax)]
        public float Scale;

        /// <summary>
        /// rung の間引き間隔。1 で全 rung、2 でひとつ飛ばし。1 未満は 1 として扱う。
        /// </summary>
        [PLParam(TextKey = "PlaceRungStride", Description = "rung の間引き間隔。1 で全 rung", Min = StrideMin,
                 Max = StrideMax, Step = 1)]
        public int RungStride;

        /// <summary>
        /// rung 間引きの開始位置。rung 番号 i が i % RungStride == RungOffset のときだけ配置する。
        /// RungStride で割った余りとして扱うため、範囲外の値でも安全。
        /// </summary>
        [PLParam(TextKey = "PlaceRungOffset", Description = "rung 間引きの開始位置", Min = OffsetMin, Max = OffsetMax,
                 Step = 1)]
        public int RungOffset;

        /// <summary>
        /// 段（横につながった梯子）の間引き間隔。1 で全段、2 でひとつ飛ばし。1 未満は 1 として扱う。
        /// 段番号は上下方向の探索でレール辺を跨いで得た BeltSnapshot.RowIndex を使う。
        /// </summary>
        [PLParam(TextKey = "PlaceRowStride", Description = "段の間引き間隔。1 で全段", Min = StrideMin, Max = StrideMax,
                 Step = 1)]
        public int RowStride;

        /// <summary>
        /// 段間引きの開始位置。段番号 r が r % RowStride == RowOffset の段だけ配置する。
        /// </summary>
        [PLParam(TextKey = "PlaceRowOffset", Description = "段間引きの開始位置", Min = OffsetMin, Max = OffsetMax,
                 Step = 1)]
        public int RowOffset;

        /// <summary>
        /// 配置元にチェックを入れたオブジェクトの子孫も一緒に配置する。
        /// 子孫の頂点は配置元（ルート）のローカル空間へ移してから結合する。
        /// </summary>
        [PLParam(TextKey = "PlaceIncludeChildren", Description = "配置元の子孫も一緒に配置する")]
        public bool IncludeChildren;

        /// <summary>
        /// 配置フレームの Z 軸（rung 法線）まわりのロール。90°単位の段数（0〜3）。
        /// 0=0°, 1=90°, 2=180°, 3=270°。Z 軸は変えないので rung 法線の向きは保たれる。
        /// </summary>
        [PLParam(TextKey = "PlaceRollSteps", Description = "rung 法線まわりのロール。90°単位の段数（0〜3）", Min = RollStepsMin,
                 Max = RollStepsMax, Step = 1)]
        public int RollSteps;

        public static PlaceObjectParams Default => new PlaceObjectParams
        {
            MeshName        = "PlaceObject",
            Mode            = PlaceSourceMode.Combine,
            RandomSeed      = 0,
            ScaleMode       = PlaceScaleMode.RungLength,
            Scale           = 1f,
            RungStride      = 1,
            RungOffset      = 0,
            RowStride       = 1,
            RowOffset       = 0,
            IncludeChildren = true,
            RollSteps       = 0,
        };

        public bool Equals(PlaceObjectParams o)
            => MeshName == o.MeshName
            && Mode     == o.Mode
            && RandomSeed == o.RandomSeed
            && ScaleMode  == o.ScaleMode
            && Scale      == o.Scale
            && RungStride == o.RungStride
            && RungOffset == o.RungOffset
            && RowStride  == o.RowStride
            && RowOffset  == o.RowOffset
            && IncludeChildren == o.IncludeChildren
            && RollSteps       == o.RollSteps;

        public override bool Equals(object obj) => obj is PlaceObjectParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
