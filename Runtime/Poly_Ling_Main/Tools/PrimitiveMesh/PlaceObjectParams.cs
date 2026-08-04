// PlaceObjectParams.cs
// オブジェクト接地の生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;

namespace Poly_Ling.PlaceObject
{
    /// <summary>
    /// オブジェクト接地パラメータ。配置元オブジェクトはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PlaceObjectParams : IEquatable<PlaceObjectParams>
    {
        public string MeshName;

        public static PlaceObjectParams Default => new PlaceObjectParams
        {
            MeshName = "PlaceObject",
        };

        public bool Equals(PlaceObjectParams o) => MeshName == o.MeshName;

        public override bool Equals(object obj) => obj is PlaceObjectParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
