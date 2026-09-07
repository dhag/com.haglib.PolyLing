// VrmAnimationExporterNull.cs
// VRM 実装が登録されていない場合のスタブ。
// 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
//
// VRM パッケージが無い環境では PolyLing.Vrm10 アセンブリごとコンパイルされないため、
// 登録が行われず本クラスが使われる。UI は IsAvailable == false を見て VRMA 出力を出さない。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Vrm
{
    public class VrmAnimationExporterNull : IVrmAnimationExporter
    {
        private const string Prefix = "[PolyLing] VRM アニメーション エクスポータが利用できません";

        public bool IsAvailable => false;

        public VrmAnimationExportResult Export(
            GameObject skeletonRoot,
            IReadOnlyDictionary<HumanBodyBones, Transform> humanBones,
            Transform hipsPositionParent,
            IReadOnlyList<float> timesSec,
            Action<int> poseFrame,
            string outputPath)
        {
            Debug.LogError(
                $"{Prefix}: VRM パッケージ (com.vrmc.vrm) が導入されていないか、" +
                $"実装が登録されていません ({outputPath})");

            return VrmAnimationExportResult.Failed("VRM アニメーション エクスポータが利用できません");
        }
    }
}
