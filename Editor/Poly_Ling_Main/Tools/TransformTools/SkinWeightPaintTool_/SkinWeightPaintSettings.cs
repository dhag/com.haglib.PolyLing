// Assets/Editor/Poly_Ling_Main/Tools/TransformTools/SkinWeightPaintTool_/SkinWeightPaintSettings.cs
// SkinWeightPaintTool用設定クラス（IToolSettings対応）

using UnityEngine;
using Poly_Ling.UI;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// SkinWeightPaintTool用設定
    /// </summary>
    public class SkinWeightPaintSettings : IToolSettings
    {
        // ================================================================
        // 設定値
        // ================================================================

        /// <summary>ペイントモード</summary>
        public SkinWeightPaintMode PaintMode = SkinWeightPaintMode.Replace;

        /// <summary>ブラシ半径（ワールド単位）</summary>
        public float BrushRadius = 0.05f;

        /// <summary>ブラシ強度</summary>
        public float Strength = 1.0f;

        /// <summary>フォールオフ</summary>
        public BrushFalloff Falloff = BrushFalloff.Smooth;

        /// <summary>ペイントウェイト値</summary>
        public float WeightValue = 1.0f;

        /// <summary>ターゲットボーンのマスターインデックス</summary>
        public int TargetBoneMasterIndex = -1;

        // ================================================================
        // 定数
        // ================================================================

        public const float MIN_BRUSH_RADIUS = 0.001f;
        public const float MAX_BRUSH_RADIUS = 1.0f;
        public const float MIN_STRENGTH = 0.0f;
        public const float MAX_STRENGTH = 1.0f;

        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public IToolSettings Clone()
        {
            return new SkinWeightPaintSettings
            {
                PaintMode = this.PaintMode,
                BrushRadius = this.BrushRadius,
                Strength = this.Strength,
                Falloff = this.Falloff,
                WeightValue = this.WeightValue,
                TargetBoneMasterIndex = this.TargetBoneMasterIndex,
            };
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is SkinWeightPaintSettings src)
            {
                PaintMode = src.PaintMode;
                BrushRadius = src.BrushRadius;
                Strength = src.Strength;
                Falloff = src.Falloff;
                WeightValue = src.WeightValue;
                TargetBoneMasterIndex = src.TargetBoneMasterIndex;
            }
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is SkinWeightPaintSettings src)
            {
                return PaintMode != src.PaintMode ||
                       !Mathf.Approximately(BrushRadius, src.BrushRadius) ||
                       !Mathf.Approximately(Strength, src.Strength) ||
                       Falloff != src.Falloff ||
                       !Mathf.Approximately(WeightValue, src.WeightValue) ||
                       TargetBoneMasterIndex != src.TargetBoneMasterIndex;
            }
            return true;
        }
    }
}
