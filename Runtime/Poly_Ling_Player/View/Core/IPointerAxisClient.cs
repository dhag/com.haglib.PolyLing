// IPointerAxisClient.cs
// 縦横制限（PointerAxisConstraint）を使う操作を持つハンドラの任意実装インターフェース。
// ツール名ではなく、問い合わせ時点の操作状態で使い方と基準点を返す。
// 実装していないハンドラでは制限しない。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    public interface IPointerAxisClient
    {
        /// <summary>
        /// 現在の操作で使える縦横制限の種類と基準点（Y=0 下のビューポート座標）を返す。
        /// 縦横の判定はここで行わない（PointerAxisConstraint が行う）。
        /// </summary>
        AxisConstraintUse QueryAxisConstraint(out Vector2 anchor);
    }
}
