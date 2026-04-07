// IPlayerToolHandler.cs
// プレイヤービルド用ツールハンドラーインターフェース。
// PlayerVertexInteractor が左ボタンイベントをこのインターフェースに委譲する。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 頂点インタラクションのモード別処理を担うインターフェース。
    /// <para>
    /// 選択共通ロジック（クリック選択・矩形選択）は <see cref="PlayerSelectionOps"/> として
    /// 独立しているため、各ハンドラーは必要に応じてそれを呼び出す。
    /// </para>
    /// </summary>
    public interface IPlayerToolHandler
    {
        /// <summary>
        /// 左クリック確定時。
        /// </summary>
        /// <param name="hit">スクリーン座標でのヒットテスト結果。</param>
        /// <param name="screenPos">クリックのスクリーン座標。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods);

        /// <summary>
        /// 左ドラッグ開始。MouseDown 位置でのヒットテスト結果を渡す。
        /// </summary>
        /// <param name="hit">ドラッグ開始位置でのヒットテスト結果。</param>
        /// <param name="screenPos">ドラッグ開始スクリーン座標（MouseDown 位置）。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods);

        /// <summary>
        /// 左ドラッグ中（毎フレーム）。
        /// </summary>
        /// <param name="screenPos">現在のスクリーン座標。</param>
        /// <param name="delta">前フレームからのスクリーン座標差分。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods);

        /// <summary>
        /// 左ドラッグ終了（ボタンアップ）。
        /// </summary>
        /// <param name="screenPos">ボタンアップ時のスクリーン座標。</param>
        /// <param name="mods">修飾キー状態。</param>
        void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods);
    }
}
