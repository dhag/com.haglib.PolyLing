// FrillParams.cs
// フリル生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;

namespace Poly_Ling.Frill
{
    /// <summary>
    /// フリル生成パラメータ。
    /// 第1段階では名前のみ。断面プロファイル関連は後続で追加する。
    /// </summary>
    [Serializable]
    public struct FrillParams : IEquatable<FrillParams>
    {
        public string MeshName;

        public static FrillParams Default => new FrillParams
        {
            MeshName = "Frill",
        };

        public bool Equals(FrillParams o) => MeshName == o.MeshName;

        public override bool Equals(object obj) => obj is FrillParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
