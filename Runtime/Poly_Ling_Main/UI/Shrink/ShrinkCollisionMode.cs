// ShrinkCollisionMode.cs
// シュリンカーの衝突判定方式
// UnityEditor非依存

namespace Poly_Ling.UI
{
    /// <summary>
    /// シュリンカーが「どこでぶつかったか」を判定する単位。
    /// </summary>
    public enum ShrinkCollisionMode
    {
        /// <summary>
        /// 頂点方式。頂点のビフォー→アフター線分と、コライダー三角形の交差を見る。
        /// ShrinkCollisionSolver が担当。
        /// </summary>
        VertexSegment = 0,

        /// <summary>
        /// 面方式。ビフォー面を三角形に割り、移動する三角形とコライダー三角形の
        /// 接触時刻を保守的前進法で求める。ShrinkFaceCollisionSolver が担当。
        /// </summary>
        FacePair = 1,
    }
}
