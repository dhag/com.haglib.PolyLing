// Vrm10ExporterNull.cs
// VRM 実装が登録されていない場合のスタブ。
// 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//
// VRM パッケージが無い環境では PolyLing.Vrm10 アセンブリごとコンパイルされないため、
// 登録が行われず本クラスが使われる。UI は IsAvailable == false を見て VRM 出力を出さない。

using UnityEngine;

namespace Poly_Ling.Vrm
{
    public class Vrm10ExporterNull : IVrm10Exporter
    {
        private const string Prefix = "[PolyLing] VRM 1.0 エクスポータが利用できません";

        public bool IsAvailable => false;

        public Vrm10ExportResult Export(
            Poly_Ling.Context.ModelContext model, string outputPath, Vrm10ExportSettings settings)
        {
            Debug.LogError(
                $"{Prefix}: VRM パッケージ (com.vrmc.vrm) が導入されていないか、" +
                $"実装が登録されていません ({outputPath})");

            return Vrm10ExportResult.Failed("VRM 1.0 エクスポータが利用できません");
        }
    }
}
