// Canvas2DAnchor.cs
// 2Dキャンバス（UV/回転体/Profile2D）共通の回転/拡大縮小アンカー。
// 値はデータ座標系（回転体=プロファイル(R,Y)、Profile2D=ワールド）で保持する。
// 数値UI・サブモード切替・マウス処理は各キャンバス側で配線し、
// プリセット計算と十字描画をこのクラスが担う。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>回転/拡大縮小の基準アンカー（データ座標系）。</summary>
    public sealed class Canvas2DAnchor
    {
        public Vector2 Value = Vector2.zero;
        public bool    Manual;   // true=手動固定（重心へ自動追従しない）
        public bool    Mode;     // アンカー設定サブモード

        public enum Preset { Centroid, Center, TopLeft, BottomLeft }

        /// <summary>点列（データ座標）からプリセット位置を設定する。Y は上向き（大きいほど上）。</summary>
        public void SetPreset(IReadOnlyList<Vector2> pts, Preset preset)
        {
            if (pts == null || pts.Count == 0) return;

            if (preset == Preset.Centroid)
            {
                Vector2 s = Vector2.zero;
                for (int i = 0; i < pts.Count; i++) s += pts[i];
                Value  = s / pts.Count;
                Manual = false;   // 重心＝自動追従に戻す
                return;
            }

            float minX = float.MaxValue, maxX = float.MinValue,
                  minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                var q = pts[i];
                minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
                minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
            }
            switch (preset)
            {
                case Preset.Center:     Value = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f); break;
                case Preset.TopLeft:    Value = new Vector2(minX, maxY); break;
                case Preset.BottomLeft: Value = new Vector2(minX, minY); break;
            }
            Manual = true;
        }

        /// <summary>キャンバス座標 c に十字＋円マーカーを描画する。</summary>
        public void Draw(Painter2D p, Vector2 c)
        {
            float s   = 9f;
            var   col = Mode ? new Color(1f, 0.35f, 0.85f)
                             : new Color(1f, 0.5f, 0.9f, 0.75f);
            p.strokeColor = col;
            p.lineWidth   = Mode ? 2f : 1.25f;

            p.BeginPath(); p.MoveTo(new Vector2(c.x - s, c.y)); p.LineTo(new Vector2(c.x + s, c.y)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c.x, c.y - s)); p.LineTo(new Vector2(c.x, c.y + s)); p.Stroke();
            p.BeginPath(); p.Arc(c, s * 0.6f, 0f, 360f); p.Stroke();
        }
    }
}
