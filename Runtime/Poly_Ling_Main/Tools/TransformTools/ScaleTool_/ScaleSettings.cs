// Tools/ScaleSettings.cs
// スケールツールの設定
// IToolSettings対応版
// ピボットモード追加

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// スケールツールの設定
    /// </summary>
    public class ScaleSettings : ToolSettingsBase
    {
        // スケール値
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float ScaleZ = 1f;

        public float MIN_SCALE_XYZ = 0.01f;
        public float MAX_SCALE_XYZ = 5.00f;
        public float MIN_SCALE_X = 0.01f;
        public float MAX_SCALE_X = 5.00f;
        public float MIN_SCALE_Y = 0.01f;
        public float MAX_SCALE_Y = 5.00f;
        public float MIN_SCALE_Z = 0.01f;
        public float MAX_SCALE_Z = 5.00f;

        // 連動モード
        public bool UniformScale = true;

        // ピボットモード
        public PivotMode PivotMode = PivotMode.SelectionCenter;

        /// <summary>
        /// スケールをリセット（1.0に戻す）
        /// </summary>
        public void ResetScale()
        {
            ScaleX = 1f;
            ScaleY = 1f;
            ScaleZ = 1f;
        }

        /// <summary>
        /// スケール値をVector3で取得
        /// </summary>
        public Vector3 GetScale() => new Vector3(ScaleX, ScaleY, ScaleZ);

        /// <summary>
        /// スケール値をVector3で設定
        /// </summary>
        public void SetScale(Vector3 scale)
        {
            ScaleX = scale.x;
            ScaleY = scale.y;
            ScaleZ = scale.z;
        }

        /// <summary>
        /// 全軸を同じ値に設定（Uniformモード用）
        /// </summary>
        public void SetUniformScale(float value)
        {
            ScaleX = value;
            ScaleY = value;
            ScaleZ = value;
        }

        /// <summary>
        /// デフォルト値（1,1,1）かどうか
        /// </summary>
        public bool IsDefault()
        {
            return Mathf.Approximately(ScaleX, 1f) &&
                   Mathf.Approximately(ScaleY, 1f) &&
                   Mathf.Approximately(ScaleZ, 1f);
        }

        // === IToolSettings実装 ===

        public override IToolSettings Clone()
        {
            return new ScaleSettings
            {
                ScaleX = this.ScaleX,
                ScaleY = this.ScaleY,
                ScaleZ = this.ScaleZ,
                UniformScale = this.UniformScale,
                PivotMode = this.PivotMode
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<ScaleSettings>(other, out var o)) return true;

            return ScaleX != o.ScaleX ||
                   ScaleY != o.ScaleY ||
                   ScaleZ != o.ScaleZ ||
                   UniformScale != o.UniformScale ||
                   PivotMode != o.PivotMode;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<ScaleSettings>(other, out var o)) return;

            ScaleX = o.ScaleX;
            ScaleY = o.ScaleY;
            ScaleZ = o.ScaleZ;
            UniformScale = o.UniformScale;
            PivotMode = o.PivotMode;
        }
    }
}
