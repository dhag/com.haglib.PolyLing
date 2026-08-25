// Runtime/Poly_Ling_Main/Core/Data/SkinKindOps.cs
// 描画オブジェクトの種別（MeshObject.SkinKind）の一括確定。
//
// 【何のためにあるか】
//   SkinKind は明示状態であり、頂点のボーンウェイトから毎回導出しない。
//   一方でインポータ・シリアライザは頂点を組み立ててから MeshObject に詰めるため、
//   組み上がった時点で一度だけ種別を確定させる必要がある。その入口をここに集める。
//
// 【一方向であること】
//   ウェイトを持つ頂点があれば Skinned にする。0 個でも MeshFilter へは戻さない。
//   MeshFilter へ戻すのは明示操作（MeshObject.SetSkinKind / ClearAllBoneWeights）だけ。
//   途中経過で種別が勝手に切り替わると描画行列が入れ替わり、形状が飛ぶ。

using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 描画オブジェクトの種別（SkinKind）の一括確定ヘルパ。
    /// インポート完了時・モデル読込完了時に 1 回だけ呼ぶ。毎フレーム呼んではならない。
    /// </summary>
    public static class SkinKindOps
    {
        /// <summary>
        /// MeshContext 列の SkinKind を実頂点のウェイトから確定させる。
        /// </summary>
        /// <returns>種別が変化したメッシュの数。</returns>
        public static int RecomputeAll(IEnumerable<MeshContext> contexts)
        {
            if (contexts == null) return 0;

            int changed = 0;
            foreach (var mc in contexts)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (mo.RecomputeSkinKind()) changed++;
            }
            return changed;
        }

        /// <summary>
        /// MeshObject 列の SkinKind を実頂点のウェイトから確定させる。
        /// </summary>
        /// <returns>種別が変化したメッシュの数。</returns>
        public static int RecomputeAll(IEnumerable<MeshObject> meshObjects)
        {
            if (meshObjects == null) return 0;

            int changed = 0;
            foreach (var mo in meshObjects)
            {
                if (mo == null) continue;
                if (mo.RecomputeSkinKind()) changed++;
            }
            return changed;
        }
    }
}
