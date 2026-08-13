// CoordinateConverter.cs
// PMX/VMD ⇔ Unity 座標変換ユーティリティ

using UnityEngine;
using Poly_Ling.Ops;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// PMX/VMD座標系とUnity座標系の変換
    ///
    /// PMX/VMD: 左手系・キャラクタ正面 -Z
    /// Unity:   左手系・キャラクタ正面 +Z
    ///
    /// 変換: X と Z の両方を反転（Y軸180°回転。純粋な回転で手系は変わらない）。
    /// 規則は AxisFlipOps に集約しており、PMX インポータ/エクスポータと同一。
    /// 明示的に AxisFlip を渡す版も用意している。
    /// </summary>
    public static class CoordinateConverter
    {
        /// <summary>既定の軸反転（PMX ⇔ Unity）。</summary>
        public static AxisFlip DefaultFlip => AxisFlip.PmxToUnity;

        // ================================================================
        // Position (位置)
        // ================================================================

        /// <summary>
        /// PMX/VMD位置 → Unity位置
        /// Z軸を反転
        /// </summary>
        public static Vector3 ToUnityPosition(Vector3 pmxPosition)
            => ToUnityPosition(pmxPosition, DefaultFlip);

        public static Vector3 ToUnityPosition(Vector3 pmxPosition, AxisFlip flip)
            => AxisFlipOps.Position(flip, pmxPosition);

        /// <summary>
        /// Unity位置 → PMX/VMD位置
        /// Z軸を反転
        /// </summary>
        public static Vector3 ToPMXPosition(Vector3 unityPosition)
            => ToPMXPosition(unityPosition, DefaultFlip);

        public static Vector3 ToPMXPosition(Vector3 unityPosition, AxisFlip flip)
            => AxisFlipOps.Position(flip, unityPosition);

        // ================================================================
        // Rotation (回転)
        // ================================================================

        /// <summary>
        /// PMX/VMD回転 → Unity回転
        /// 手系は不変（両者とも左手系）。X,Z 成分を反転（Y軸180°回転）
        /// </summary>
        public static Quaternion ToUnityRotation(Quaternion pmxRotation)
            => ToUnityRotation(pmxRotation, DefaultFlip);

        public static Quaternion ToUnityRotation(Quaternion pmxRotation, AxisFlip flip)
            => AxisFlipOps.Rotation(flip, pmxRotation);

        /// <summary>
        /// Unity回転 → PMX/VMD回転
        /// </summary>
        public static Quaternion ToPMXRotation(Quaternion unityRotation)
            => ToPMXRotation(unityRotation, DefaultFlip);

        public static Quaternion ToPMXRotation(Quaternion unityRotation, AxisFlip flip)
            => AxisFlipOps.Rotation(flip, unityRotation);

        /// <summary>
        /// PMX/VMDオイラー角 → Unityオイラー角
        /// </summary>
        public static Vector3 ToUnityEuler(Vector3 pmxEuler)
            => ToUnityEuler(pmxEuler, DefaultFlip);

        public static Vector3 ToUnityEuler(Vector3 pmxEuler, AxisFlip flip)
            => AxisFlipOps.EulerDeg(flip, pmxEuler);

        /// <summary>
        /// Unityオイラー角 → PMX/VMDオイラー角
        /// </summary>
        public static Vector3 ToPMXEuler(Vector3 unityEuler)
            => ToPMXEuler(unityEuler, DefaultFlip);

        public static Vector3 ToPMXEuler(Vector3 unityEuler, AxisFlip flip)
            => AxisFlipOps.EulerDeg(flip, unityEuler);

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
            => ToUnityMatrix(pmxMatrix, DefaultFlip);

        public static Matrix4x4 ToUnityMatrix(Matrix4x4 pmxMatrix, AxisFlip flip)
        {
            // S * M * S（S は符号ベクトルの対角行列。自己逆元）
            Matrix4x4 s = Matrix4x4.Scale(new Vector3(flip.Sx, 1f, flip.Sz));
            return s * pmxMatrix * s;
        }

        /// <summary>
        /// Unity行列 → PMX/VMD行列
        /// </summary>
        public static Matrix4x4 ToPMXMatrix(Matrix4x4 unityMatrix)
            => ToPMXMatrix(unityMatrix, DefaultFlip);

        public static Matrix4x4 ToPMXMatrix(Matrix4x4 unityMatrix, AxisFlip flip)
        {
            Matrix4x4 s = Matrix4x4.Scale(new Vector3(flip.Sx, 1f, flip.Sz));
            return s * unityMatrix * s;
        }

        // ================================================================
        // Normal (法線)
        // ================================================================

        /// <summary>
        /// PMX/VMD法線 → Unity法線
        /// </summary>
        public static Vector3 ToUnityNormal(Vector3 pmxNormal)
            => ToUnityNormal(pmxNormal, DefaultFlip);

        public static Vector3 ToUnityNormal(Vector3 pmxNormal, AxisFlip flip)
            => AxisFlipOps.Normal(flip, pmxNormal);

        /// <summary>
        /// Unity法線 → PMX/VMD法線
        /// </summary>
        public static Vector3 ToPMXNormal(Vector3 unityNormal)
            => ToPMXNormal(unityNormal, DefaultFlip);

        public static Vector3 ToPMXNormal(Vector3 unityNormal, AxisFlip flip)
            => AxisFlipOps.Normal(flip, unityNormal);

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
        /// 面インデックスの巻き方向を反転する。
        /// 既定の変換（X・Z 両反転）は純粋な回転なので巻き順を変える必要はない。
        /// 反転軸が奇数個のときだけ呼ぶこと（AxisFlipOps.ReverseWinding で判定できる）。
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