// MouseSettings.cs
// マウス操作に関する設定を一元管理するクラス
//
// 設計方針:
// - グループB（ScreenDeltaToWorldDelta）を基本とし、マウス移動と画面上の物体移動を一致させる
// - 修飾キー（Shift/Ctrl）による速度変更をサポート（PMXEditor互換）
// - カメラ回転・ズームなど物理的対応がないものは個別の感度パラメータを持つ

using UnityEngine;

namespace Poly_Ling.Input
{
    /// <summary>
    /// マウス操作に関する設定を一元管理するクラス
    /// </summary>
    public class MouseSettings
    {
        // ================================================================
        // 修飾キー倍率（PMXEditor互換）
        // ================================================================

        /// <summary>
        /// Shift押下時の速度倍率（高速移動）
        /// </summary>
        public float ShiftMultiplier { get; set; } = 5.0f;

        /// <summary>
        /// Ctrl押下時の速度倍率（低速移動・微調整）
        /// </summary>
        public float CtrlMultiplier { get; set; } = 0.2f;

        // ================================================================
        // カメラ回転感度
        // ================================================================

        /// <summary>
        /// カメラ回転感度（ピクセル → 度）
        /// 右ドラッグ時の回転速度
        /// </summary>
        public float CameraRotationSensitivity { get; set; } = 0.5f;

        // ================================================================
        // ズーム感度
        // ================================================================

        /// <summary>
        /// ズーム感度（ホイール → 倍率変化）
        /// 通常ホイール時のズーム速度
        /// </summary>
        public float ZoomSensitivity { get; set; } = 0.05f;

        // ================================================================
        // 注目点Z移動感度
        // ================================================================

        /// <summary>
        /// 注目点Z移動感度（Shift+ホイール時）
        /// カメラ距離に対する係数
        /// </summary>
        public float FocusPointZSensitivity { get; set; } = 0.1f;

        // ================================================================
        // ヒット判定閾値
        // ================================================================

        /// <summary>
        /// 頂点ホバー判定半径（ピクセル）
        /// </summary>
        public float HoverVertexRadius { get; set; } = 12f;

        /// <summary>
        /// 線分ホバー判定距離（ピクセル）
        /// </summary>
        public float HoverLineDistance { get; set; } = 18f;

        // ================================================================
        // ユーティリティメソッド
        // ================================================================

        /// <summary>
        /// 現在の修飾キー状態に応じた倍率を取得
        /// </summary>
        /// <param name="e">現在のイベント（nullの場合はEvent.currentを使用）</param>
        /// <returns>適用すべき倍率</returns>
        public float GetModifierMultiplier(Event e = null)
        {
            e ??= Event.current;
            if (e == null) return 1.0f;

            if (e.shift) return ShiftMultiplier;
            if (e.control) return CtrlMultiplier;
            return 1.0f;
        }

        /// <summary>
        /// カメラ回転のデルタ値を計算（修飾キー対応）
        /// </summary>
        /// <param name="mouseDelta">マウス移動量（ピクセル）</param>
        /// <param name="e">現在のイベント</param>
        /// <returns>回転量（度）</returns>
        public float GetRotationDelta(float mouseDelta, Event e = null)
        {
            return mouseDelta * CameraRotationSensitivity * GetModifierMultiplier(e);
        }

        /// <summary>
        /// ズームの倍率変化を計算（修飾キー対応）
        /// </summary>
        /// <param name="scrollDelta">スクロール量</param>
        /// <param name="e">現在のイベント</param>
        /// <returns>距離の乗算係数（例: 1.05 で5%ズームアウト）</returns>
        public float GetZoomMultiplier(float scrollDelta, Event e = null)
        {
            return 1f + scrollDelta * ZoomSensitivity * GetModifierMultiplier(e);
        }

        /// <summary>
        /// 注目点Z移動量を計算（修飾キー対応）
        /// </summary>
        /// <param name="scrollDelta">スクロール量</param>
        /// <param name="cameraDistance">現在のカメラ距離</param>
        /// <param name="e">現在のイベント</param>
        /// <returns>Z方向移動量</returns>
        public float GetFocusPointZDelta(float scrollDelta, float cameraDistance, Event e = null)
        {
            return scrollDelta * cameraDistance * FocusPointZSensitivity * GetModifierMultiplier(e);
        }

        // ================================================================
        // デフォルトインスタンス
        // ================================================================

        /// <summary>
        /// デフォルト設定のシングルトンインスタンス
        /// </summary>
        public static MouseSettings Default { get; } = new MouseSettings();
    }
}
