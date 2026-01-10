// Assets/Editor/MeshFactory/Tools/MoveSettings.cs
// MoveTool用の設定クラス
// IToolSettingsを実装し、Undo対応のための機能を提供

using UnityEngine;
using MeshFactory.Transforms;

namespace MeshFactory.Tools
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



        public  float MIN_SCREEN_OFFSET_X = -100f;
        public  float MAX_SCREEN_OFFSET_X = 100f;
        public  float MIN_SCREEN_OFFSET_Y = -100f;
        public  float MAX_SCREEN_OFFSET_Y = 100f;
        public  float MIN_MAGNET_RADIUS = 0.01f;
        public  float MAX_MAGNET_RADIUS = 1.00f;


        // ================================================================
        // IToolSettings 実装
        // ================================================================

        public override IToolSettings Clone()
        {
            return new MoveSettings
            {
                UseMagnet = this.UseMagnet,
                MagnetRadius = this.MagnetRadius,
                MagnetFalloff = this.MagnetFalloff
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<MoveSettings>(other, out var m))
                return true;

            return UseMagnet != m.UseMagnet ||
                   !Mathf.Approximately(MagnetRadius, m.MagnetRadius) ||
                   MagnetFalloff != m.MagnetFalloff;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<MoveSettings>(other, out var m))
                return;

            UseMagnet = m.UseMagnet;
            MagnetRadius = m.MagnetRadius;
            MagnetFalloff = m.MagnetFalloff;
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
        }
    }
}
