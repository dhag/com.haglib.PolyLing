// RevolutionProfileEditCore.cs
// 回転体プロファイルの操作コア（UIフレームワーク非依存）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 座標系:
//   プロファイル空間: X 0..2（半径）、Y -1..2（高さ）
//   キャンバス空間:   (0,0) = 左上、Y 下向き（UIToolkit / IMGUI 矩形どちらにも対応）

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Revolution
{
    public static class RevolutionProfileEditCore
    {
        public const float RangeX = 2f;   // プロファイル X 範囲
        public const float RangeY = 3f;   // プロファイル Y 範囲 (-1 〜 2)

        /// <summary>
        /// 点の xy だけを差し替え、z は元の点のまま残す。
        /// 2D のキャンバスは xy だけを編集するので、点を書き戻すときは必ずこれを通す
        /// （Vector2 をそのまま代入すると暗黙の変換で z が 0 になる）。
        /// </summary>
        public static Vector3 WithXY(Vector3 orig, Vector2 xy) => new Vector3(xy.x, xy.y, orig.z);

        /// <summary>2 点の間の点。xy はキャンバス上の比率、z も同じ比率で補間する。</summary>
        public static Vector3 LerpPoint(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t);

        /// <summary>
        /// 3D の点列を (半径, y) の点列へ直す。半径 = x の符号 × √(x²+z²)。
        /// Y 軸まわりに回す用途（回転体・揺れボーンの回転配置）で使う。z=0 なら元の (x, y) と同じ。
        /// </summary>
        public static List<Vector2> ToRadial(IReadOnlyList<Vector3> src)
        {
            var dst = new List<Vector2>(src?.Count ?? 0);
            if (src == null) return dst;
            for (int i = 0; i < src.Count; i++)
            {
                Vector3 q = src[i];
                float r = Mathf.Sqrt(q.x * q.x + q.z * q.z);
                dst.Add(new Vector2(q.x < 0f ? -r : r, q.y));
            }
            return dst;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        /// <summary>
        /// プロファイル座標 → キャンバスローカル座標 (Y=0 上端)
        /// </summary>
        public static Vector2 ProfileToCanvas(Vector2 p, float w, float h,
            float zoom = 1f, Vector2 offset = default)
        {
            float scale = Mathf.Min(w / RangeX, h / RangeY) * zoom;
            float offX  = (w - RangeX * scale) * 0.5f;
            float offY  = (h - RangeY * scale) * 0.5f;
            float cx    = offX + p.x * scale + offset.x;
            float cy    = (h - offY) - (p.y + 1f) * scale + offset.y;
            return new Vector2(cx, cy);
        }

        /// <summary>
        /// キャンバスローカル座標 (Y=0 上端) → プロファイル座標
        /// </summary>
        public static Vector2 CanvasToProfile(Vector2 c, float w, float h,
            float zoom = 1f, Vector2 offset = default)
        {
            float scale = Mathf.Min(w / RangeX, h / RangeY) * zoom;
            float offX  = (w - RangeX * scale) * 0.5f;
            float offY  = (h - RangeY * scale) * 0.5f;
            float px    = (c.x - offX - offset.x) / scale;
            float py    = (h - offY - c.y + offset.y) / scale - 1f;
            return new Vector2(px, py);
        }

        /// <summary>
        /// キャンバス座標から最近傍プロファイル点のインデックスを返す。
        /// maxDist 以内に点がなければ -1。
        /// </summary>
        public static int FindClosest(List<Vector3> profile, Vector2 canvasPos,
            float w, float h, float maxDist, float zoom = 1f, Vector2 offset = default)
        {
            if (profile == null) return -1;
            int   best     = -1;
            float bestDist = maxDist;
            for (int i = 0; i < profile.Count; i++)
            {
                float d = Vector2.Distance(canvasPos, ProfileToCanvas(profile[i], w, h, zoom, offset));
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ================================================================
        // データ操作
        // ================================================================

        /// <summary>
        /// 選択点の直後（末尾なら末端）に点を追加し、選択インデックスを更新。
        /// 点は 3D。挿入点の z は両隣（末端なら延長線）から決める。
        /// </summary>
        public static void AddPoint(List<Vector3> profile, ref int selectedIndex)
        {
            if (profile == null) return;

            Vector3 newPoint;
            int     insertAt;

            if (selectedIndex >= 0 && selectedIndex < profile.Count - 1)
            {
                newPoint = (profile[selectedIndex] + profile[selectedIndex + 1]) * 0.5f;
                insertAt = selectedIndex + 1;
            }
            else if (profile.Count >= 2)
            {
                Vector3 dir = profile[profile.Count - 1] - profile[profile.Count - 2];
                newPoint = profile[profile.Count - 1] + dir.normalized * 0.2f;
                insertAt = profile.Count;
            }
            else
            {
                newPoint = new Vector3(0.5f, 0.5f, 0f);
                insertAt = profile.Count;
            }

            profile.Insert(insertAt, newPoint);
            selectedIndex = insertAt;
        }

        /// <summary>
        /// 選択点を削除。2 点未満になる場合は何もしない。選択インデックスを更新。
        /// </summary>
        public static void RemovePoint(List<Vector3> profile, ref int selectedIndex)
        {
            if (profile == null || profile.Count <= 2) return;
            if (selectedIndex < 0 || selectedIndex >= profile.Count) return;

            profile.RemoveAt(selectedIndex);
            selectedIndex = Mathf.Min(selectedIndex, profile.Count - 1);
        }

        /// <summary>
        /// プロファイルをデフォルトにリセット。
        /// </summary>
        public static void ResetProfile(List<Vector3> profile, ref int selectedIndex)
        {
            if (profile == null) return;
            profile.Clear();
            foreach (var p in RevolutionProfileGenerator.CreateDefault())
                profile.Add(p);
            selectedIndex = -1;
        }
    }
}
