// Assets/Editor/Poly_Ling/Serialization/BonePoseDataDTO.cs
// BonePoseDataのシリアライズ用データ構造
// RestPose + Manualレイヤーのみ保存
// VMD/IK/Physicsレイヤーはトランジェント（保存しない）

using System;
using UnityEngine;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// BonePoseDataのシリアライズ用データ
    /// </summary>
    [Serializable]
    public class BonePoseDataDTO
    {
        /// <summary>初期位置 [x,y,z]</summary>
        public float[] restPosition;

        /// <summary>初期回転 [x,y,z,w] (Quaternion)</summary>
        public float[] restRotation;

        /// <summary>初期スケール [x,y,z]</summary>
        public float[] restScale;

        /// <summary>有効フラグ</summary>
        public bool isActive = true;

        /// <summary>Manual差分位置 [x,y,z] (null=なし)</summary>
        public float[] manualDeltaPosition;

        /// <summary>Manual差分回転 [x,y,z,w] (null=なし)</summary>
        public float[] manualDeltaRotation;

        /// <summary>Manualウェイト</summary>
        public float manualWeight = 1f;

        // ================================================================
        // 変換ヘルパー
        // ================================================================

        public Vector3 GetRestPosition()
        {
            if (restPosition == null || restPosition.Length < 3) return Vector3.zero;
            return new Vector3(restPosition[0], restPosition[1], restPosition[2]);
        }

        public void SetRestPosition(Vector3 pos)
        {
            restPosition = new float[] { pos.x, pos.y, pos.z };
        }

        public Quaternion GetRestRotation()
        {
            if (restRotation == null || restRotation.Length < 4) return Quaternion.identity;
            return new Quaternion(restRotation[0], restRotation[1], restRotation[2], restRotation[3]);
        }

        public void SetRestRotation(Quaternion rot)
        {
            restRotation = new float[] { rot.x, rot.y, rot.z, rot.w };
        }

        public Vector3 GetRestScale()
        {
            if (restScale == null || restScale.Length < 3) return Vector3.one;
            return new Vector3(restScale[0], restScale[1], restScale[2]);
        }

        public void SetRestScale(Vector3 s)
        {
            restScale = new float[] { s.x, s.y, s.z };
        }
    }
}
