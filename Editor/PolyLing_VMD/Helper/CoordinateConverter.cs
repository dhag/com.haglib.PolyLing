// CoordinateConverter.cs
// PMX/VMD (右手系) ⇔ Unity (左手系) 座標変換ユーティリティ

using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// PMX/VMD座標系とUnity座標系の変換
    /// 
    /// PMX/VMD: 右手系 (Y-up, Z-forward)
    /// Unity:   左手系 (Y-up, Z-forward)
    /// 
    /// 変換: Z軸を反転
    /// </summary>
    public static class CoordinateConverter
    {
        // ================================================================
        // Position (位置)
        // ================================================================

        /// <summary>
        /// PMX/VMD位置 → Unity位置
        /// Z軸を反転
        /// </summary>
        public static Vector3 ToUnityPosition(Vector3 pmxPosition)
        {
            return new Vector3(pmxPosition.x, pmxPosition.y, -pmxPosition.z);
        }

        /// <summary>
        /// Unity位置 → PMX/VMD位置
        /// Z軸を反転
        /// </summary>
        public static Vector3 ToPMXPosition(Vector3 unityPosition)
        {
            return new Vector3(unityPosition.x, unityPosition.y, -unityPosition.z);
        }

        // ================================================================
        // Rotation (回転)
        // ================================================================

        /// <summary>
        /// PMX/VMD回転 → Unity回転
        /// 左手系⇔右手系: X,Y成分を反転
        /// </summary>
        public static Quaternion ToUnityRotation(Quaternion pmxRotation)
        {
            // 右手系→左手系: X,Y成分を反転、Z,W はそのまま
            return new Quaternion(-pmxRotation.x, -pmxRotation.y, pmxRotation.z, pmxRotation.w);
        }

        /// <summary>
        /// Unity回転 → PMX/VMD回転
        /// </summary>
        public static Quaternion ToPMXRotation(Quaternion unityRotation)
        {
            return new Quaternion(-unityRotation.x, -unityRotation.y, unityRotation.z, unityRotation.w);
        }

        /// <summary>
        /// PMX/VMDオイラー角 → Unityオイラー角
        /// </summary>
        public static Vector3 ToUnityEuler(Vector3 pmxEuler)
        {
            // X回転とY回転の符号を反転
            return new Vector3(-pmxEuler.x, -pmxEuler.y, pmxEuler.z);
        }

        /// <summary>
        /// Unityオイラー角 → PMX/VMDオイラー角
        /// </summary>
        public static Vector3 ToPMXEuler(Vector3 unityEuler)
        {
            return new Vector3(-unityEuler.x, -unityEuler.y, unityEuler.z);
        }

        // ================================================================
        // Scale (スケール)
        // ================================================================

        /// <summary>
        /// スケールはそのまま（変換不要）
        /// </summary>
        public static Vector3 ToUnityScale(Vector3 pmxScale)
        {
            return pmxScale;
        }

        /// <summary>
        /// スケールはそのまま（変換不要）
        /// </summary>
        public static Vector3 ToPMXScale(Vector3 unityScale)
        {
            return unityScale;
        }

        // ================================================================
        // Matrix (行列)
        // ================================================================

        /// <summary>
        /// PMX/VMD行列 → Unity行列
        /// </summary>
        public static Matrix4x4 ToUnityMatrix(Matrix4x4 pmxMatrix)
        {
            // Z軸反転行列
            Matrix4x4 flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));

            // flipZ * pmxMatrix * flipZ
            return flipZ * pmxMatrix * flipZ;
        }

        /// <summary>
        /// Unity行列 → PMX/VMD行列
        /// </summary>
        public static Matrix4x4 ToPMXMatrix(Matrix4x4 unityMatrix)
        {
            Matrix4x4 flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));
            return flipZ * unityMatrix * flipZ;
        }

        // ================================================================
        // Normal (法線)
        // ================================================================

        /// <summary>
        /// PMX/VMD法線 → Unity法線
        /// </summary>
        public static Vector3 ToUnityNormal(Vector3 pmxNormal)
        {
            return new Vector3(pmxNormal.x, pmxNormal.y, -pmxNormal.z);
        }

        /// <summary>
        /// Unity法線 → PMX/VMD法線
        /// </summary>
        public static Vector3 ToPMXNormal(Vector3 unityNormal)
        {
            return new Vector3(unityNormal.x, unityNormal.y, -unityNormal.z);
        }

        // ================================================================
        // UV (テクスチャ座標)
        // ================================================================

        /// <summary>
        /// PMX UV → Unity UV
        /// V座標を反転（PMX: 左上原点 → Unity: 左下原点）
        /// </summary>
        public static Vector2 ToUnityUV(Vector2 pmxUV)
        {
            return new Vector2(pmxUV.x, 1f - pmxUV.y);
        }

        /// <summary>
        /// Unity UV → PMX UV
        /// </summary>
        public static Vector2 ToPMXUV(Vector2 unityUV)
        {
            return new Vector2(unityUV.x, 1f - unityUV.y);
        }

        // ================================================================
        // Bone Transform (ボーン変換)
        // ================================================================

        /// <summary>
        /// VMDボーンフレームの変換をUnity空間に変換
        /// </summary>
        /// <param name="translation">VMD Translation</param>
        /// <param name="rotation">VMD Rotation</param>
        /// <returns>Unity空間での (position, rotation)</returns>
        public static (Vector3 position, Quaternion rotation) ToUnityBoneTransform(
            Vector3 translation, Quaternion rotation)
        {
            return (ToUnityPosition(translation), ToUnityRotation(rotation));
        }

        /// <summary>
        /// UnityボーンのローカルトランスフォームをVMD形式に変換
        /// </summary>
        public static (Vector3 translation, Quaternion rotation) ToPMXBoneTransform(
            Vector3 position, Quaternion rotation)
        {
            return (ToPMXPosition(position), ToPMXRotation(rotation));
        }

        // ================================================================
        // Utility
        // ================================================================

        /// <summary>
        /// 右手系の面インデックスを左手系に変換（巻き方向反転）
        /// </summary>
        public static void FlipTriangleWinding(int[] indices)
        {
            for (int i = 0; i < indices.Length; i += 3)
            {
                // 1番目と2番目を入れ替え
                int temp = indices[i + 1];
                indices[i + 1] = indices[i + 2];
                indices[i + 2] = temp;
            }
        }

        /// <summary>
        /// ボーンウェイトインデックスの変換は必要ない
        /// （インデックスはモデル固有なので変換対象外）
        /// </summary>
    }
}