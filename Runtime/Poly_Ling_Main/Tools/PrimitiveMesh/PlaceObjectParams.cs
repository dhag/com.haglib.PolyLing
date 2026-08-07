// PlaceObjectParams.cs
// オブジェクト接地の生成パラメータ（Runtime / Editor 共有）
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
    /// オブジェクト接地パラメータ。配置元オブジェクトはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PlaceObjectParams : IEquatable<PlaceObjectParams>
    {
        public string MeshName;

        /// <summary>配置元が複数のときの割り当て方式。</summary>
        public PlaceSourceMode Mode;

        /// <summary>Random 時の乱数シード。同じシード・同じ入力なら同一結果になる。</summary>
        public int RandomSeed;

        public static PlaceObjectParams Default => new PlaceObjectParams
        {
            MeshName   = "PlaceObject",
            Mode       = PlaceSourceMode.Combine,
            RandomSeed = 0,
        };

        public bool Equals(PlaceObjectParams o)
            => MeshName == o.MeshName
            && Mode     == o.Mode
            && RandomSeed == o.RandomSeed;

        public override bool Equals(object obj) => obj is PlaceObjectParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
