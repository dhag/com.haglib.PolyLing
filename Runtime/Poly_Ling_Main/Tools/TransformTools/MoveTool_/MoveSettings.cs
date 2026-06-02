// Assets/Editor/Poly_Ling/Tools/MoveSettings.cs
// MoveTool用の設定クラス
// IToolSettingsを実装し、Undo対応のための機能を提供

using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Core;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// MoveTool用の設定クラス
    /// </summary>
    public class MoveSettings : ToolSettingsBase
    {
        // ================================================================
        // 設定項目
        // ================================================================

        /// <summary>マグネット機能を使用するか</summary>
        public bool UseMagnet = false;

        /// <summary>マグネット半径</summary>
        public float MagnetRadius = 0.5f;

        /// <summary>マグネット減衰タイプ</summary>
        public FalloffType MagnetFalloff = FalloffType.Smooth;

        /// <summary>マグネット距離モード（直線 / リンク距離）</summary>
        public DistanceMode MagnetDistanceMode = DistanceMode.Euclidean;



        public  float MIN_SCREEN_OFFSET_X = ParameterLimits.GetF("Move.ScreenOffsetX.Min");
        public  float MAX_SCREEN_OFFSET_X = ParameterLimits.GetF("Move.ScreenOffsetX.Max");
        public  float MIN_SCREEN_OFFSET_Y = ParameterLimits.GetF("Move.ScreenOffsetY.Min");
        public  float MAX_SCREEN_OFFSET_Y = ParameterLimits.GetF("Move.ScreenOffsetY.Max");
        public  float MIN_MAGNET_RADIUS = ParameterLimits.GetF("Move.MagnetRadius.Min");
        public  float MAX_MAGNET_RADIUS = ParameterLimits.GetF("Move.MagnetRadius.Max");


        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public override IToolSettings Clone()
        {
            return new MoveSettings
            {
                UseMagnet = this.UseMagnet,
                MagnetRadius = this.MagnetRadius,
                MagnetFalloff = this.MagnetFalloff,
                MagnetDistanceMode = this.MagnetDistanceMode
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<MoveSettings>(other, out var m))
                return true;

            return UseMagnet != m.UseMagnet ||
                   !Mathf.Approximately(MagnetRadius, m.MagnetRadius) ||
                   MagnetFalloff != m.MagnetFalloff ||
                   MagnetDistanceMode != m.MagnetDistanceMode;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<MoveSettings>(other, out var m))
                return;

            UseMagnet = m.UseMagnet;
            MagnetRadius = m.MagnetRadius;
            MagnetFalloff = m.MagnetFalloff;
            MagnetDistanceMode = m.MagnetDistanceMode;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// デフォルト設定にリセット
        /// </summary>
        public void Reset()
        {
            UseMagnet = false;
            MagnetRadius = 0.5f;
            MagnetFalloff = FalloffType.Smooth;
            MagnetDistanceMode = DistanceMode.Euclidean;
        }
    }
}
