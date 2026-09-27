// MQOBackImageMapping.cs
// MQO の下絵（BackImage）とモデルの下絵（UnderlayData）の対応。読み込み・書き出しの両方が使う。
// Runtime/Poly_Ling_Main/MQO/Common/ に配置
//
// 【対応】
//   MQO のパート名    → 方向         2 つの数値 (x, y) を置く MQO の軸
//   front / back     → Front / Back   (X, Y)
//   top / bottom     → Top / Bottom   (X, Z)
//   left / right     → Left / Right   (Z, Y)
//   視線方向の成分は 0 にする。座標変換（倍率・軸反転）は頂点と同じものを通す。
//
// 【要確認】
//   4 つの数値の意味と、上下・左右での軸の向きは公式仕様に記載が無い。
//   上の表は手元の MQO（480×480 画像で -240 -240 240 240）から「2 隅の座標」と読み、
//   軸はそのパートで見える 2 軸を正の向きのまま当てたもの。
//   メタセコイアで見比べて違っていたら、この表（ToMqo3D / FromMqo3D）だけを直す。
//
// 【Persp / Ortho】
//   MQO に対応するものが無いので、読み込みでも書き出しでも扱わない。

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MQO
{
    /// <summary>MQO の下絵とモデルの下絵の対応。</summary>
    public static class MQOBackImageMapping
    {
        /// <summary>パート名から方向を引く。対応が無ければ false。</summary>
        public static bool TryDirectionOf(string part, out UnderlayDirection dir)
        {
            switch ((part ?? "").Trim().ToLowerInvariant())
            {
                case "front":  dir = UnderlayDirection.Front;  return true;
                case "back":   dir = UnderlayDirection.Back;   return true;
                case "top":    dir = UnderlayDirection.Top;    return true;
                case "bottom": dir = UnderlayDirection.Bottom; return true;
                case "left":   dir = UnderlayDirection.Left;   return true;
                case "right":  dir = UnderlayDirection.Right;  return true;
                default:       dir = UnderlayDirection.Front;  return false;
            }
        }

        /// <summary>方向からパート名を引く。MQO に無い方向は null。</summary>
        public static string PartOf(UnderlayDirection dir)
        {
            switch (dir)
            {
                case UnderlayDirection.Front:  return "front";
                case UnderlayDirection.Back:   return "back";
                case UnderlayDirection.Top:    return "top";
                case UnderlayDirection.Bottom: return "bottom";
                case UnderlayDirection.Left:   return "left";
                case UnderlayDirection.Right:  return "right";
                default:                       return null;
            }
        }

        /// <summary>そのパートの 2 つの数値を MQO の 3 次元座標へ置く。</summary>
        public static Vector3 ToMqo3D(UnderlayDirection dir, float x, float y)
        {
            switch (dir)
            {
                case UnderlayDirection.Top:
                case UnderlayDirection.Bottom: return new Vector3(x, 0f, y);
                case UnderlayDirection.Left:
                case UnderlayDirection.Right:  return new Vector3(0f, y, x);
                default:                       return new Vector3(x, y, 0f);
            }
        }

        /// <summary>MQO の 3 次元座標からそのパートの 2 つの数値を取り出す（ToMqo3D の逆）。</summary>
        public static Vector2 FromMqo3D(UnderlayDirection dir, Vector3 p)
        {
            switch (dir)
            {
                case UnderlayDirection.Top:
                case UnderlayDirection.Bottom: return new Vector2(p.x, p.z);
                case UnderlayDirection.Left:
                case UnderlayDirection.Right:  return new Vector2(p.z, p.y);
                default:                       return new Vector2(p.x, p.y);
            }
        }

        /// <summary>
        /// MQO の画像パスを絶対パスにする。相対なら MQO のあるフォルダを基準にする。
        /// </summary>
        public static string ResolvePath(string path, string baseDir)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(baseDir))
                    path = Path.GetFullPath(Path.Combine(baseDir, path));
            }
            catch (Exception)
            {
                // パスとして解釈できないものはそのまま残す（表示時に読めなければ出ないだけ）。
            }
            return path;
        }
    }
}
