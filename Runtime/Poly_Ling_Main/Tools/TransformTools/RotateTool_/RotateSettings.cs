// Tools/RotateSettings.cs
// 回転ツールの設定
// IToolSettings対応版
// ピボットモード追加

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ピボットモード（Rotate/Scaleツール共通）
    /// </summary>
    public enum PivotMode
    {
        SelectionCenter,  // 選択頂点の重心
        Origin            // 原点
    }

    /// <summary>
    /// 回転ツールの設定
    /// </summary>
    public class RotateSettings : ToolSettingsBase
    {
        // 回転角度
        public float RotationX = 0f;
        public float RotationY = 0f;
        public float RotationZ = 0f;

        // スナップ設定
        public bool UseSnap = false;
        public float SnapAngle = 15f;

        // ピボットモード
        public PivotMode PivotMode = PivotMode.SelectionCenter;

        /// <summary>
        /// 回転をリセット
        /// </summary>
        public void ResetRotation()
        {
            RotationX = 0f;
            RotationY = 0f;
            RotationZ = 0f;
        }

        /// <summary>
        /// 回転値をVector3で取得
        /// </summary>
        public Vector3 GetRotation() => new Vector3(RotationX, RotationY, RotationZ);

        /// <summary>
        /// 回転値をVector3で設定
        /// </summary>
        public void SetRotation(Vector3 rotation)
        {
            RotationX = rotation.x;
            RotationY = rotation.y;
            RotationZ = rotation.z;
        }

        /// <summary>
        /// スナップを適用
        /// </summary>
        public float ApplySnap(float angle)
        {
            if (!UseSnap || SnapAngle <= 0f) return angle;
            return Mathf.Round(angle / SnapAngle) * SnapAngle;
        }

        // === IToolSettings実装 ===

        public override IToolSettings Clone()
        {
            return new RotateSettings
            {
                RotationX = this.RotationX,
                RotationY = this.RotationY,
                RotationZ = this.RotationZ,
                UseSnap = this.UseSnap,
                SnapAngle = this.SnapAngle,
                PivotMode = this.PivotMode
            };
        }

        public override bool IsDifferentFrom(IToolSettings other)
        {
            if (!IsSameType<RotateSettings>(other, out var o)) return true;

            return RotationX != o.RotationX ||
                   RotationY != o.RotationY ||
                   RotationZ != o.RotationZ ||
                   UseSnap != o.UseSnap ||
                   SnapAngle != o.SnapAngle ||
                   PivotMode != o.PivotMode;
        }

        public override void CopyFrom(IToolSettings other)
        {
            if (!IsSameType<RotateSettings>(other, out var o)) return;

            RotationX = o.RotationX;
            RotationY = o.RotationY;
            RotationZ = o.RotationZ;
            UseSnap = o.UseSnap;
            SnapAngle = o.SnapAngle;
            PivotMode = o.PivotMode;
        }
    }
}
