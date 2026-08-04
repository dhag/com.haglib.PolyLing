// PrimitiveMeshTransform.cs
// 図形生成物への TRS 焼き込み（頂点座標・法線を直接書き換える）。Runtime / Editor 共有。
// 既存の図形生成パネルと、将来のサブツールの双方から呼ぶ共有実装。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>
    /// 生成済み <see cref="MeshObject"/> に平行移動 / 回転 / スケールを焼き込む。
    /// <para>
    /// 法線は「逆転置行列」で変換する。<see cref="MeshObject.RecalculateNormals"/> を
    /// 使うと各ジェネレータが頂点単位で作り込んだ法線（角丸コーナー・球・カプセル等の
    /// スムーズ法線）が面法線で潰れるため、ここでは再計算しない。
    /// </para>
    /// <para>
    /// スケール成分の負の個数が奇数（＝鏡映）のときは巻き順が反転するため、
    /// 全ての面を <see cref="Face.Flip"/> で反転して表裏を維持する。
    /// </para>
    /// </summary>
    public static class PrimitiveMeshTransform
    {
        /// <summary>スケール成分として許容する絶対値の下限。0 除算と逆行列の破綻を防ぐ。</summary>
        public const float MinScaleAbs = 1e-4f;

        // ================================================================
        // 判定 / 正規化
        // ================================================================

        /// <summary>スケール成分の絶対値を <see cref="MinScaleAbs"/> 以上に丸める（符号は保持）。</summary>
        public static Vector3 SanitizeScale(Vector3 scale)
        {
            return new Vector3(
                SanitizeScaleComponent(scale.x),
                SanitizeScaleComponent(scale.y),
                SanitizeScaleComponent(scale.z));
        }

        private static float SanitizeScaleComponent(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 1f;
            if (Mathf.Abs(v) >= MinScaleAbs) return v;
            return (v < 0f) ? -MinScaleAbs : MinScaleAbs;
        }

        /// <summary>回転 0 かつスケール 1 なら true（＝焼き込む必要なし）。</summary>
        public static bool IsIdentityRotationScale(Vector3 eulerDeg, Vector3 scale)
        {
            var s = SanitizeScale(scale);
            return Mathf.Approximately(eulerDeg.x, 0f)
                && Mathf.Approximately(eulerDeg.y, 0f)
                && Mathf.Approximately(eulerDeg.z, 0f)
                && Mathf.Approximately(s.x, 1f)
                && Mathf.Approximately(s.y, 1f)
                && Mathf.Approximately(s.z, 1f);
        }

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>
        /// 回転とスケールのみを焼き込む（平行移動なし）。
        /// 生成位置は呼出し側（MeshContext.BoneTransform / 頂点加算）が従来どおり扱う。
        /// </summary>
        public static void ApplyRotationScale(MeshObject mo, Vector3 eulerDeg, Vector3 scale)
        {
            Apply(mo, Vector3.zero, eulerDeg, scale);
        }

        /// <summary>
        /// 平行移動 / 回転 / スケールを頂点へ焼き込む。
        /// </summary>
        /// <param name="mo">対象メッシュ。null 可（何もしない）。</param>
        /// <param name="translation">平行移動量（ローカル空間）。</param>
        /// <param name="eulerDeg">オイラー角（度）。Unity の Z→X→Y 順。</param>
        /// <param name="scale">スケール。負値は鏡映として扱う。</param>
        public static void Apply(MeshObject mo, Vector3 translation, Vector3 eulerDeg, Vector3 scale)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0) return;

            Vector3 s = SanitizeScale(scale);
            bool noTranslation = translation == Vector3.zero;
            if (noTranslation && IsIdentityRotationScale(eulerDeg, s)) return;

            Matrix4x4 m = Matrix4x4.TRS(translation, Quaternion.Euler(eulerDeg), s);

            // 法線用行列（逆転置）。非一様スケールでも法線の向きが正しく保たれる。
            Matrix4x4 nm = m.inverse.transpose;

            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                v.Position = m.MultiplyPoint3x4(v.Position);

                if (v.Normals == null) continue;
                for (int k = 0; k < v.Normals.Count; k++)
                {
                    Vector3 n = nm.MultiplyVector(v.Normals[k]);
                    if (n.sqrMagnitude > 1e-12f) v.Normals[k] = n.normalized;
                }
            }

            // 鏡映（負スケールが奇数個）は巻き順が反転するので面を反転して表裏を維持する。
            if (s.x * s.y * s.z < 0f && mo.Faces != null)
            {
                for (int i = 0; i < mo.Faces.Count; i++)
                    mo.Faces[i]?.Flip();
            }
        }
    }
}
