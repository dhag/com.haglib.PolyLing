// Assets/Editor/Poly_Ling/Tools/Settings/SculptSettings.cs
// SculptTool用設定クラス（IToolSettings対応）

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// SculptTool用設定
    /// </summary>
    public class SculptSettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>スカルプトモード</summary>
        public SculptMode Mode = SculptMode.Draw;

        /// <summary>ブラシ半径</summary>
        public float BrushRadius = 0.5f;

        /// <summary>強度</summary>
        public float Strength = 0.1f;

        /// <summary>凹凸反転</summary>
        public bool Invert = false;

        // ================================================================
        // 定数　強度やブラシサイズなどの制限値
        // ================================================================

        public const float MIN_BRUSH_RADIUS = 0.05f;
        public const float MAX_BRUSH_RADIUS = 1.00f;
        public const float MIN_STRENGTH = 0.01f;
        public const float MAX_STRENGTH = 0.05f;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new SculptSettings
            {
                Mode = this.Mode,
                BrushRadius = this.BrushRadius,
                Strength = this.Strength,
                Invert = this.Invert
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is SculptSettings src)
            {
                Mode = src.Mode;
                BrushRadius = src.BrushRadius;
                Strength = src.Strength;
                Invert = src.Invert;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SculptSettings src)
            {
                return Mode != src.Mode ||
                       !Mathf.Approximately(BrushRadius, src.BrushRadius) ||
                       !Mathf.Approximately(Strength, src.Strength) ||
                       Invert != src.Invert;
            }
            return true;
        }
    }
}
