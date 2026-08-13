// PlaceObjectParams.cs
// オブジェクト配置の生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;

namespace Poly_Ling.PlaceObject
{
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

        /// <summary>
        /// 配置物の倍率。rung 長による等倍（PlaceObjectMeshGenerator の scale）へさらに掛ける。
        /// X/Y/Z 連動の1値。1 で従来どおり。
        /// </summary>
        public float Scale;

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
            Scale           = 1f,
            IncludeChildren = true,
            RollSteps       = 0,
        };

        public bool Equals(PlaceObjectParams o)
            => MeshName == o.MeshName
            && Mode     == o.Mode
            && RandomSeed == o.RandomSeed
            && Scale      == o.Scale
            && IncludeChildren == o.IncludeChildren
            && RollSteps       == o.RollSteps;

        public override bool Equals(object obj) => obj is PlaceObjectParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
