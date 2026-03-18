// PolyLingCore_CameraState.cs
// カメラ状態をCoreで保持する
// UndoOperationsでCameraSnapshotを作成するために必要

using UnityEngine;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore
    {
        // ================================================================
        // カメラ状態（UndoOperationsがスナップショットを取るために必要）
        // ================================================================

        public float CameraRotationX    { get; set; } = 20f;
        public float CameraRotationY    { get; set; } = 0f;
        public float CameraDistance     { get; set; } = 2f;
        public Vector3 CameraTarget     { get; set; } = Vector3.zero;

        internal CameraSnapshot CaptureCameraSnapshot() => new CameraSnapshot
        {
            RotationX      = CameraRotationX,
            RotationY      = CameraRotationY,
            CameraDistance = CameraDistance,
            CameraTarget   = CameraTarget,
        };
    }
}
