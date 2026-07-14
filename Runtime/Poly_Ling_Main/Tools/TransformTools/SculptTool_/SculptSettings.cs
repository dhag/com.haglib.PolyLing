// Assets/Editor/Poly_Ling/Tools/Settings/SculptSettings.cs
// SculptTool用設定クラス（IToolSettings対応）

using UnityEngine;
using Poly_Ling.Core;

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
        public float BrushRadius = 0.1f;

        /// <summary>強度</summary>
        public float Strength = 0.02f;

        /// <summary>凹凸反転</summary>
        public bool Invert = false;

        /// <summary>フォールオフ（減衰）タイプ</summary>
        public FalloffType Falloff = FalloffType.Gaussian;

        /// <summary>距離モード（直線 / リンク距離）</summary>
        public DistanceMode DistanceMode = DistanceMode.Euclidean;

        // ================================================================
        // 半径範囲（スライダーの最小値・最大値）
        // ================================================================

        // レンジ（上下限）はグローバル共有。CSV(ParameterLimits)を唯一の真実の源とし、
        // ここではキャッシュを持たず get/set とも CSV へ委譲する。
        // これにより UI 変更が即 CSV へ反映され、CSV とUIが乖離しない。
        private const string KeyRadiusMin   = "Sculpt.BrushRadius.Min";
        private const string KeyRadiusMax   = "Sculpt.BrushRadius.Max";

        public float MinBrushRadius
        {
            get => ParameterLimits.GetF(KeyRadiusMin);
            set => ParameterLimits.SetF(KeyRadiusMin, value);
        }
        public float MaxBrushRadius
        {
            get => ParameterLimits.GetF(KeyRadiusMax);
            set => ParameterLimits.SetF(KeyRadiusMax, value);
        }

        // ================================================================
        // 強度範囲（スライダーの最小値・最大値）
        // ================================================================

        private const string KeyStrengthMin = "Sculpt.Strength.Min";
        private const string KeyStrengthMax = "Sculpt.Strength.Max";

        public float MinStrength
        {
            get => ParameterLimits.GetF(KeyStrengthMin);
            set => ParameterLimits.SetF(KeyStrengthMin, value);
        }
        public float MaxStrength
        {
            get => ParameterLimits.GetF(KeyStrengthMax);
            set => ParameterLimits.SetF(KeyStrengthMax, value);
        }

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
                DistanceMode   = this.DistanceMode,
                // レンジ(Min/Max)はグローバル共有(CSV)なので複製しない
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
                DistanceMode   = src.DistanceMode;
                // レンジ(Min/Max)はグローバル共有(CSV)なので複製しない
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
                       DistanceMode != src.DistanceMode;
                       // レンジ(Min/Max)はグローバル共有(CSV)なので差分対象外
            }
            return true;
        }
    }
}
