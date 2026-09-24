// FaceExpressionTransfer.cs
// 表情転写（BEFORE＝ターゲットモデルを描いた画像の MediaPipe 顔、AFTER＝人の顔の MediaPipe 顔）の計算。
//
// ■ 流れ
//   1. BEFORE の顔点を画素単位にする（x·w, y·h, z·w。MediaPipe の z は x とほぼ同じ尺度）。
//   2. ターゲットの頂点を、BEFORE を撮ったときのカメラ（FaceCaptureCamera）で画像へ投影する。
//   3. 投影した頂点を BEFORE の三角形に重心座標で結び付ける（MediaPipeFaceDeformer.Bind。画素の x・y で比べる）。
//   4. AFTER の顔点を画素単位にし、BEFORE へ重なる相似変換（平行移動・回転・一様な拡大縮小、3 次元の最小二乗）で
//      揃える。対応点は顔の全点（先頭 468 点）。これで画面の端の小さい顔・頭の傾きも BEFORE の位置・大きさに揃う。
//   5. 揃えた AFTER の三角形で同じ重みの新しい画素位置を求め（MediaPipeFaceDeformer.Apply）、
//      頂点の元の奥行き（投影時の clip z・w）のまま逆投影してワールドへ戻す。
//      三角形に入らなかった頂点は動かさない。
//
// ■ 相似変換
//   Horn の四元数法（1987）。対応点の相互共分散から 4×4 対称行列を作り、最大固有値の固有ベクトルが回転の四元数。
//   拡大率は Horn の対称形 s = sqrt(Σ|b'|² / Σ|a'|²)。固有値は Jacobi 法で求める。
//
// ■ 画素の向き
//   x 右・y 下（MediaPipe の画像座標と同じ）。投影は Unity の projectionMatrix（OpenGL 流）と
//   worldToCameraMatrix を使い、y は画像の上を 0 にするため反転する。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>BEFORE を撮ったときのカメラ。</summary>
    public sealed class FaceCaptureCamera
    {
        public Matrix4x4 View;       // Camera.worldToCameraMatrix
        public Matrix4x4 Projection; // Camera.projectionMatrix
        public int       Width;      // 画像の幅 [画素]
        public int       Height;     // 画像の高さ [画素]

        /// <summary>ワールド点を画素へ投影する。clip は逆投影用に返す。カメラの後ろなら false。</summary>
        public bool ToPixel(Vector3 world, out Vector2 pixel, out Vector4 clip)
        {
            clip = Projection * (View * new Vector4(world.x, world.y, world.z, 1f));
            pixel = default;
            if (clip.w <= 1e-6f) return false;
            float nx = clip.x / clip.w, ny = clip.y / clip.w;
            pixel = new Vector2((nx * 0.5f + 0.5f) * Width, (1f - (ny * 0.5f + 0.5f)) * Height);
            return true;
        }

        /// <summary>画素を、投影時の clip の奥行き（z・w）のままワールドへ戻す。</summary>
        public Vector3 FromPixel(Vector2 pixel, Vector4 clip)
        {
            float nx = pixel.x / Width * 2f - 1f;
            float ny = (1f - pixel.y / Height) * 2f - 1f;
            var c = new Vector4(nx * clip.w, ny * clip.w, clip.z, clip.w);
            var w = (Projection * View).inverse * c;
            return new Vector3(w.x / w.w, w.y / w.w, w.z / w.w);
        }
    }

    public static class FaceExpressionTransfer
    {
        /// <summary>MediaPipe の顔メッシュの点数（残り 10 点は虹彩）。</summary>
        public const int FaceMeshPoints = 468;

        /// <summary>正規化座標の点（[x,y,z] 以上）を画素単位（x·w, y·h, z·w）にする。先頭 count 点。</summary>
        public static Vector3[] ToPixels(IReadOnlyList<float[]> normalized, int width, int height, int count)
        {
            int n = Mathf.Min(count, normalized.Count);
            var r = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var p = normalized[i];
                r[i] = new Vector3(p[0] * width, p[1] * height, (p.Length > 2 ? p[2] : 0f) * width);
            }
            return r;
        }

        /// <summary>
        /// src を dst へ重ねる相似変換を求め、src の全点にかけた結果を返す（点数は同じであること）。
        /// </summary>
        public static Vector3[] AlignSimilarity(Vector3[] src, Vector3[] dst)
        {
            int n = Mathf.Min(src.Length, dst.Length);
            if (n < 3) throw new InvalidOperationException("対応点が 3 点未満です");

            Vector3 ca = Vector3.zero, cb = Vector3.zero;
            for (int i = 0; i < n; i++) { ca += src[i]; cb += dst[i]; }
            ca /= n; cb /= n;

            double sxx = 0, sxy = 0, sxz = 0, syx = 0, syy = 0, syz = 0, szx = 0, szy = 0, szz = 0;
            double na = 0, nb = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = src[i] - ca, b = dst[i] - cb;
                sxx += a.x * b.x; sxy += a.x * b.y; sxz += a.x * b.z;
                syx += a.y * b.x; syy += a.y * b.y; syz += a.y * b.z;
                szx += a.z * b.x; szy += a.z * b.y; szz += a.z * b.z;
                na += a.sqrMagnitude; nb += b.sqrMagnitude;
            }
            if (na < 1e-12) throw new InvalidOperationException("AFTER の点が 1 点に潰れています");

            var N = new double[4, 4]
            {
                { sxx + syy + szz, syz - szy,        szx - sxz,        sxy - syx        },
                { syz - szy,       sxx - syy - szz,  sxy + syx,        szx + sxz        },
                { szx - sxz,       sxy + syx,       -sxx + syy - szz,  syz + szy        },
                { sxy - syx,       szx + sxz,        syz + szy,       -sxx - syy + szz  },
            };
            var q = MaxEigenVector4(N);                    // (w, x, y, z)
            double w = q[0], x = q[1], y = q[2], z = q[3];
            var R = new double[3, 3]
            {
                { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w)     },
                { 2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w)     },
                { 2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y) },
            };
            double s = Math.Sqrt(nb / na);

            var result = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                Vector3 a = src[i] - ca;
                result[i] = new Vector3(
                    (float)(s * (R[0, 0] * a.x + R[0, 1] * a.y + R[0, 2] * a.z)) + cb.x,
                    (float)(s * (R[1, 0] * a.x + R[1, 1] * a.y + R[1, 2] * a.z)) + cb.y,
                    (float)(s * (R[2, 0] * a.x + R[2, 1] * a.y + R[2, 2] * a.z)) + cb.z);
            }
            return result;
        }

        /// <summary>
        /// 表情転写の本体。worldVertices（ターゲットのワールド頂点）を変形したワールド位置を返す。
        /// moved[i] が false の頂点は元のまま（三角形に入らなかった・カメラの後ろ）。
        /// </summary>
        public static Vector3[] Transfer(
            Vector3[] worldVertices, FaceCaptureCamera cam,
            Vector3[] beforePixels, Vector3[] afterPixels, int[][] triangles,
            out bool[] moved, out int boundCount)
        {
            int n = worldVertices.Length;
            var projected = new Vector3[n];
            var clips     = new Vector4[n];
            var visible   = new bool[n];
            // カメラの後ろの頂点は三角形に入らない場所へ置いて、結び付けから外す
            var outside = new Vector3(float.MaxValue, float.MaxValue, 0f);
            for (int i = 0; i < n; i++)
            {
                visible[i] = cam.ToPixel(worldVertices[i], out var px, out clips[i]);
                projected[i] = visible[i] ? new Vector3(px.x, px.y, 0f) : outside;
            }

            var before2 = new Vector2[beforePixels.Length];
            for (int i = 0; i < before2.Length; i++) before2[i] = beforePixels[i];

            var aligned = AlignSimilarity(afterPixels, beforePixels);
            var after2 = new Vector2[aligned.Length];
            for (int i = 0; i < after2.Length; i++) after2[i] = aligned[i];

            var deformer = new MediaPipeFaceDeformer();
            deformer.SetBaseMesh(before2, triangles);
            boundCount = deformer.Bind(projected);

            var moving = (Vector3[])projected.Clone();
            deformer.Apply(after2, moving);

            var result = (Vector3[])worldVertices.Clone();
            moved = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (!visible[i]) continue;
                if (moving[i].x == projected[i].x && moving[i].y == projected[i].y) continue;
                result[i] = cam.FromPixel(new Vector2(moving[i].x, moving[i].y), clips[i]);
                moved[i] = true;
            }
            return result;
        }

        // 4×4 対称行列の最大固有値の固有ベクトル（巡回 Jacobi 法）。
        private static double[] MaxEigenVector4(double[,] m)
        {
            var a = (double[,])m.Clone();
            var v = new double[4, 4];
            for (int i = 0; i < 4; i++) v[i, i] = 1;

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = 0;
                for (int p = 0; p < 4; p++) for (int q = p + 1; q < 4; q++) off += a[p, q] * a[p, q];
                if (off < 1e-24) break;

                for (int p = 0; p < 4; p++)
                for (int q = p + 1; q < 4; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-30) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < 4; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }

            int best = 0;
            for (int i = 1; i < 4; i++) if (a[i, i] > a[best, best]) best = i;
            var r = new double[4];
            double len = 0;
            for (int i = 0; i < 4; i++) { r[i] = v[i, best]; len += r[i] * r[i]; }
            len = Math.Sqrt(len);
            for (int i = 0; i < 4; i++) r[i] /= len;
            return r;
        }
    }
}
