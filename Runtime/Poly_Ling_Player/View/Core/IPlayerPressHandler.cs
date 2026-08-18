// IPlayerPressHandler.cs
// 押下フェーズ（ボタンダウン〜ドラッグしきい値超え）を扱う任意実装インターフェース。
// IPlayerToolHandler の拡張として別インターフェースにしてあるのは、
// 実装済みのツールハンドラが 30 個以上あり、そのすべてに空実装を足すのを避けるため。
// PlayerVertexInteractor は as キャストで判定し、実装しているハンドラにだけ転送する。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 押下時点から追従を開始するツール用のインターフェース。
    ///
    /// <para>
    /// 従来はドラッグしきい値を越えてから移動を開始し、押下位置から現在位置までの
    /// 差分をその瞬間に一括適用していた。そのためしきい値ぶん（4px 超）の飛びが出た。
    /// このインターフェースを実装すると、押下時に原点と対象を確定し、
    /// しきい値未満の移動から 1:1 で追従できる。しきい値を越えずに離した場合は
    /// <see cref="OnLeftPressCancel"/> で巻き戻してからクリック処理へ移る。
    /// </para>
    /// </summary>
    public interface IPlayerPressHandler
    {
        /// <summary>
        /// 左ボタン押下時。押下位置を移動の原点として確定し、
        /// 直前の PointerMove で確定済みのホバー要素を掴む対象として記録する。
        /// </summary>
        /// <param name="hit">押下時点のヒットテスト結果。</param>
        /// <param name="screenPos">押下位置のスクリーン座標（Y=0 下）。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftButtonDown(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods);

        /// <summary>
        /// 押下中で、まだドラッグしきい値を越えていない間の移動。
        /// </summary>
        /// <param name="screenPos">現在のスクリーン座標。</param>
        /// <param name="delta">前回からの差分（絶対量方式では未使用）。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftPressMove(Vector2 screenPos, Vector2 delta, ModifierKeys mods);

        /// <summary>
        /// しきい値を越えずにボタンが離された。押下時に開始した操作を巻き戻す。
        /// この直後に IPlayerToolHandler.OnLeftClick が呼ばれる。
        /// </summary>
        /// <param name="screenPos">ボタンアップ時のスクリーン座標。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftPressCancel(Vector2 screenPos, ModifierKeys mods);
    }
}
