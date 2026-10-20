// MediaPipeCoords.cs
// MediaPipe の点の座標変換の唯一の置き場。
// Runtime/Poly_Ling_Main/MediaPipe/ に配置
//
// ■ MediaPipe の座標
//   landmarks … 画像を基準にした正規化座標。x 右（0～1）・y 下（0～1）・z は奥が正（x とほぼ同じ尺度）。
//   world     … メートル。姿勢の原点は腰の中心（MediaPipePoseSolver の冒頭）。軸の向きは landmarks と同じ（x 右・y 下・z 奥）。
//   どちらも右手系で、人はカメラ（-z 側）を向く。
//
// ■ Unity の座標（PolyLing の正準骨格・メッシュ）
//   左手系・y 上。人は +Z を向き、本人の左手は -X 側。
//   変換は (x, y, z) → (-x, -y, -z)。y を上向きにし、Y 軸まわりに 180° 回して人を +Z 向きにする
//   （行列式 -1。右手系 → 左手系なので鏡像にならない）。
//   姿勢の解決（MediaPipePoseSolver）と能面の生成（NohMaskMeshGenerator）が同じ変換を使う。
//
// ■ 画素
//   正規化座標に画像の幅・高さを掛ける（x·w, y·h, z·w）。向きは画像と同じ（x 右・y 下）。
//   表情転写（FaceExpressionTransfer）は画素で計算する。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    public static class MediaPipeCoords
    {
        /// <summary>MediaPipe の座標（landmarks・world のどちらも）を Unity の向きへ直す。</summary>
        public static Vector3 ToUnity(Vector3 p) => new Vector3(-p.x, -p.y, -p.z);

        /// <summary>正規化座標を画素単位（x·w, y·h, z·w）にする。</summary>
        public static Vector3 ToPixels(Vector3 normalized, int width, int height)
            => new Vector3(normalized.x * width, normalized.y * height, normalized.z * width);

        /// <summary>先頭 count 点（足りなければあるだけ）を画素単位にする。</summary>
        public static Vector3[] ToPixels(IReadOnlyList<Vector3> normalized, int width, int height, int count)
        {
            int n = normalized == null ? 0 : Mathf.Min(count, normalized.Count);
            var r = new Vector3[n];
            for (int i = 0; i < n; i++) r[i] = ToPixels(normalized[i], width, height);
            return r;
        }

        /// <summary>全点を Unity の向きへ直す。</summary>
        public static Vector3[] ToUnity(IReadOnlyList<Vector3> points)
        {
            if (points == null) return null;
            var r = new Vector3[points.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ToUnity(points[i]);
            return r;
        }
    }
}
