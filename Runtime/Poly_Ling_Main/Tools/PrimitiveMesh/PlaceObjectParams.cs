// PlaceObjectParams.cs
// オブジェクト配置の生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;

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
        public string MeshName;

        /// <summary>配置元が複数のときの割り当て方式。</summary>
        public PlaceSourceMode Mode;

        /// <summary>Random 時の乱数シード。同じシード・同じ入力なら同一結果になる。</summary>
        public int RandomSeed;

        /// <summary>配置物の倍率の決め方。既定は rung 長に比例（従来どおり）。</summary>
        public PlaceScaleMode ScaleMode;

        /// <summary>
        /// 配置物の倍率。X/Y/Z 連動の1値。
        /// ScaleMode = RungLength では rung 長への掛け率（1 で rung 長と等倍）。
        /// ScaleMode = Uniform     では倍率そのもの（1 で配置元のローカル座標のまま）。
        /// </summary>
        public float Scale;

        /// <summary>
        /// rung の間引き間隔。1 で全 rung、2 でひとつ飛ばし。1 未満は 1 として扱う。
        /// </summary>
        public int RungStride;

        /// <summary>
        /// rung 間引きの開始位置。rung 番号 i が i % RungStride == RungOffset のときだけ配置する。
        /// RungStride で割った余りとして扱うため、範囲外の値でも安全。
        /// </summary>
        public int RungOffset;

        /// <summary>
        /// 段（横につながった梯子）の間引き間隔。1 で全段、2 でひとつ飛ばし。1 未満は 1 として扱う。
        /// 段番号は上下方向の探索でレール辺を跨いで得た BeltSnapshot.RowIndex を使う。
        /// </summary>
        public int RowStride;

        /// <summary>
        /// 段間引きの開始位置。段番号 r が r % RowStride == RowOffset の段だけ配置する。
        /// </summary>
        public int RowOffset;

        /// <summary>
        /// 配置元にチェックを入れたオブジェクトの子孫も一緒に配置する。
        /// 子孫の頂点は配置元（ルート）のローカル空間へ移してから結合する。
        /// </summary>
        public bool IncludeChildren;

        /// <summary>
        /// 配置フレームの Z 軸（rung 法線）まわりのロール。90°単位の段数（0〜3）。
        /// 0=0°, 1=90°, 2=180°, 3=270°。Z 軸は変えないので rung 法線の向きは保たれる。
        /// </summary>
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
