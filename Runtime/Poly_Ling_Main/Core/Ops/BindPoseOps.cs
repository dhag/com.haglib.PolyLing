// BindPoseOps.cs
// BindPose（スキニング基準行列）の撮り直し。
//
// 【なぜ 2 つに分けるか】
//   どちらも中身は「ワールド行列の逆」だが、含めるものが違う。
//     RebindToBind        … BindWorldMatrix の逆（ポーズを含めない）
//     BakeCurrentPoseToBind … WorldMatrix の逆（ポーズを含める）
//   コード上はどちらも `BindPose = 何かの inverse` で見分けがつかず、
//   取り違えるとポーズ中の姿勢がレストとして残る。名前で区別する。
//
//   規約の全文は PolyLing_姿勢の規約.md を参照。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>BindPose の撮り直し。</summary>
    public static class BindPoseOps
    {
        /// <summary>
        /// バインド姿勢で撮り直す（BindPose = BindWorldMatrix の逆）。
        ///
        /// ポーズを含めない。取込・配置・再構築のあと「今の形をバインドとする」
        /// ときはこちら。ポーズが 1 つも入っていない場面では
        /// BakeCurrentPoseToBind と同じ値になる。
        /// 呼ぶ前に ModelContext.ComputeWorldMatrices を通しておくこと。
        /// </summary>
        public static void RebindToBind(MeshContext mc)
        {
            if (mc == null) return;
            mc.BindPose = mc.BindWorldMatrix.inverse;
        }

        /// <summary>
        /// 今のポーズ込みの姿勢をバインドとして焼き込む（BindPose = WorldMatrix の逆）。
        /// BakePoseToBindPoseCommand の意味。
        /// </summary>
        public static void BakeCurrentPoseToBind(MeshContext mc)
        {
            if (mc == null) return;
            mc.BindPose = mc.WorldMatrix.inverse;
        }
    }
}
