// NormalHelper.cs
// 面法線計算の共通ヘルパー
// 縮退三角形（面積ゼロ・同一直線上の頂点）でゼロ法線を返さないよう保護

using UnityEngine;

namespace Poly_Ling.Data
{
    public static class NormalHelper
    {
        /// <summary>
        /// 3頂点から面法線を計算。縮退三角形の場合はVector3.upを返す。
        /// </summary>
        public static Vector3 CalculateFaceNormal(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 cross = Vector3.Cross((p1 - p0).normalized, (p2 - p0).normalized);
            if (cross.sqrMagnitude < 1e-6f)
                return Vector3.up;
            return cross.normalized;
        }
    }
}
