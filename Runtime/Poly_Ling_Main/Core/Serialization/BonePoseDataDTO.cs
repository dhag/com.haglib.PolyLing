// Assets/Editor/Poly_Ling/Serialization/BonePoseDataDTO.cs
// BonePoseDataのシリアライズ用データ構造
// Manualレイヤーのみ保存
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
        /// <summary>有効フラグ</summary>
        public bool isActive = true;

        /// <summary>Manual差分位置 [x,y,z] (null=なし)</summary>
        public float[] manualDeltaPosition;

        /// <summary>Manual差分回転 [x,y,z,w] (null=なし)</summary>
        public float[] manualDeltaRotation;

        /// <summary>Manualウェイト</summary>
        public float manualWeight = 1f;
    }
}
