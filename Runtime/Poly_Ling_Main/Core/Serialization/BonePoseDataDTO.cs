// Assets/Editor/Poly_Ling/Serialization/BonePoseDataDTO.cs
// BonePoseDataのシリアライズ用データ構造
// Manualレイヤーのみ保存
// VMDは現状保存しない（VMD/IK/Physicsの「現在の状態」にリンクするトランジェント分）

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

        /// <summary>Manual有効フラグ</summary>
        public bool manualEnabled = true;
    }
}
