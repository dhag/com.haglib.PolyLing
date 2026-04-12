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

        /// <summary>フォールオフ（減衰）タイプ</summary>
        public FalloffType Falloff = FalloffType.Gaussian;

        // ================================================================
        // 半径範囲（スライダーの最小値・最大値）
        // ================================================================

        public float MinBrushRadius = 0.05f;
        public float MaxBrushRadius = 1.00f;

        // ================================================================
        // 強度範囲（スライダーの最小値・最大値）
        // ================================================================

        public float MinStrength = 0.01f;
        public float MaxStrength = 0.05f;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new SculptSettings
            {
                Mode           = this.Mode,
                BrushRadius    = this.BrushRadius,
                Strength       = this.Strength,
                Invert         = this.Invert,
                Falloff        = this.Falloff,
                MinBrushRadius = this.MinBrushRadius,
                MaxBrushRadius = this.MaxBrushRadius,
                MinStrength    = this.MinStrength,
                MaxStrength    = this.MaxStrength,
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is SculptSettings src)
            {
                Mode           = src.Mode;
                BrushRadius    = src.BrushRadius;
                Strength       = src.Strength;
                Invert         = src.Invert;
                Falloff        = src.Falloff;
                MinBrushRadius = src.MinBrushRadius;
                MaxBrushRadius = src.MaxBrushRadius;
                MinStrength    = src.MinStrength;
                MaxStrength    = src.MaxStrength;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SculptSettings src)
            {
                return Mode != src.Mode ||
                       !Mathf.Approximately(BrushRadius, src.BrushRadius) ||
                       !Mathf.Approximately(Strength, src.Strength) ||
                       Invert != src.Invert ||
                       Falloff != src.Falloff ||
                       !Mathf.Approximately(MinBrushRadius, src.MinBrushRadius) ||
                       !Mathf.Approximately(MaxBrushRadius, src.MaxBrushRadius) ||
                       !Mathf.Approximately(MinStrength, src.MinStrength) ||
                       !Mathf.Approximately(MaxStrength, src.MaxStrength);
            }
            return true;
        }
    }
}
