// CameraSpeedModifier.cs
// 視点操作（回転・パン・ズーム）の粗微動倍率。
// - Shift 押下      → 粗動（Camera.SpeedCoarse 倍）
// - Ctrl  押下      → 微動（Camera.SpeedFine 倍）
// - 両押し / 無押し → 等倍（1.0）
// 倍率のプリセット値は DisplaySettings.csv の
// Camera.SpeedCoarse / Camera.SpeedFine に保存され、テキスト編集で変更できる。
// DisplaySettings.GetF はファイル読込が初回1回だけで、以降は辞書引きのみ。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置

using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public static class CameraSpeedModifier
    {
        /// <summary>粗動倍率（Shift）。CSV プリセット値。</summary>
        public static float Coarse =>
            Mathf.Max(0.0001f, DisplaySettings.GetF(DisplaySettings.KeyCameraSpeedCoarse));

        /// <summary>微動倍率（Ctrl）。CSV プリセット値。</summary>
        public static float Fine =>
            Mathf.Max(0.0001f, DisplaySettings.GetF(DisplaySettings.KeyCameraSpeedFine));

        /// <summary>
        /// 修飾キーから視点操作の速度倍率を返す。
        /// Shift と Ctrl の同時押しは打ち消し合いとして等倍にする。
        /// </summary>
        public static float Factor(ModifierKeys mods)
        {
            if (mods.Shift == mods.Ctrl) return 1f;
            return mods.Shift ? Coarse : Fine;
        }
    }
}
