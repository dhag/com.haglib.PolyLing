// Vrm10ImporterNull.cs
// VRM インポータ実装が登録されていない場合のスタブ。
// 規約は IVrm10Importer.cs 冒頭（さらに IVrm10Exporter.cs 冒頭）のコメントを正典とする。
//
// VRM パッケージが無い環境では PolyLing.Vrm10 アセンブリごとコンパイルされないため、
// 登録が行われず本クラスが使われる。UI は IsAvailable == false を見て VRM 読み込みを出さない。

using UnityEngine;

namespace Poly_Ling.Vrm
{
    public class Vrm10ImporterNull : IVrm10Importer
    {
        private const string Prefix = "[PolyLing] VRM 1.0 インポータが利用できません";

        public bool IsAvailable => false;

        public Vrm10ImportResult Import(string filePath, Vrm10ImportSettings settings)
        {
            Debug.LogError(
                $"{Prefix}: VRM パッケージ (com.vrmc.vrm) が導入されていないか、" +
                $"実装が登録されていません ({filePath})");

            return Vrm10ImportResult.Failed("VRM 1.0 インポータが利用できません");
        }
    }
}
