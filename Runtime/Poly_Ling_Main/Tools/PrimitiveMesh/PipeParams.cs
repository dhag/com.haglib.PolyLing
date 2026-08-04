// PipeParams.cs
// パイプ生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;

namespace Poly_Ling.Pipe
{
    /// <summary>
    /// パイプ生成パラメータ。断面プロファイルはパネル側が保持する。
    /// </summary>
    [Serializable]
    public struct PipeParams : IEquatable<PipeParams>
    {
        public string MeshName;

        /// <summary>開いた梯子のとき、両端に蓋を張るか。</summary>
        public bool CapEnds;

        public static PipeParams Default => new PipeParams
        {
            MeshName = "Pipe",
            CapEnds  = true,
        };

        public bool Equals(PipeParams o) => MeshName == o.MeshName && CapEnds == o.CapEnds;

        public override bool Equals(object obj) => obj is PipeParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
