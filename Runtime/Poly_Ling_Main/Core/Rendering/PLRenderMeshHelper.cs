// PLRenderMeshHelper.cs
// Graphics.DrawMesh → Graphics.RenderMesh 置き換え用の RenderParams 組み立てヘルパー。
//
// 【なぜ必要か】
//   Graphics.DrawMesh は影・ライトプローブ等の設定を暗黙の既定値で持つが、
//   Graphics.RenderMesh は RenderParams で明示的に渡す。既定値をそのまま使うと
//   DrawMesh と挙動が変わるため、ここで一致させる。
//
//   特に worldBounds は RenderMesh 固有の必須項目で、カリング判定に使われる。
//   指定を誤ると描画が消えるため、呼び出し側でメッシュ由来の値を必ず渡すこと。
//
// 【DrawMesh の既定値との対応】
//   castShadows    = true            → shadowCastingMode = On
//   receiveShadows = true            → receiveShadows    = true
//   useLightProbes = true            → lightProbeUsage   = BlendProbes
//   layer / camera / properties は呼び出し側の引数をそのまま渡す。

using UnityEngine;
using UnityEngine.Rendering;

namespace Poly_Ling.Core
{
    public static class PLRenderMeshHelper
    {
        /// <summary>
        /// Graphics.DrawMesh 相当の RenderParams を作る。
        /// </summary>
        /// <param name="mat">マテリアル</param>
        /// <param name="cam">描画対象カメラ（null なら全カメラ）</param>
        /// <param name="worldBounds">ワールド空間のバウンディングボックス。カリング判定に使われる。</param>
        /// <param name="mpb">MaterialPropertyBlock（不要なら null）</param>
        /// <param name="layer">レイヤー（既存呼び出しはすべて 0）</param>
        public static RenderParams Make(
            Material mat,
            Camera cam,
            Bounds worldBounds,
            MaterialPropertyBlock mpb = null,
            int layer = 0)
        {
            var rp = new RenderParams(mat);
            rp.camera            = cam;
            rp.layer             = layer;
            rp.worldBounds       = worldBounds;
            rp.matProps          = mpb;
            rp.shadowCastingMode = ShadowCastingMode.On;
            rp.receiveShadows    = true;
            rp.lightProbeUsage   = LightProbeUsage.BlendProbes;
            return rp;
        }

        /// <summary>
        /// メッシュのローカルバウンズを行列で変換してワールドバウンズを得る。
        /// 行列が identity のときは mesh.bounds をそのまま返す。
        /// </summary>
        public static Bounds WorldBoundsOf(Mesh mesh, Matrix4x4 m)
        {
            var b = mesh.bounds;
            if (m.isIdentity) return b;

            // 8 頂点を変換して包む。回転・スケールを含む行列でも正しい。
            Vector3 c = b.center;
            Vector3 e = b.extents;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = m.MultiplyPoint3x4(corner);
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.z < min.z) min.z = p.z;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
                if (p.z > max.z) max.z = p.z;
            }
            var wb = new Bounds();
            wb.SetMinMax(min, max);
            return wb;
        }
    }
}
