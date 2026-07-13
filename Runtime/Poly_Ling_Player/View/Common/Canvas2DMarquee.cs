// Canvas2DMarquee.cs
// 2Dキャンバス（UV/回転体/Profile2D）共通の矩形・投げ縄マーキー選択ヘルパ。
// キャンバスpx空間で動作する。各キャンバスは自前の座標変換で候補点をpx化し、
// Contains() で内包判定する。描画は Draw() を generateVisualContent から呼ぶ。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>矩形／投げ縄のマーキー選択（キャンバスpx空間）。</summary>
    public sealed class Canvas2DMarquee
    {
        private bool          _active;
        private bool          _lasso;
        private Vector2       _start;
        private Vector2       _cur;
        private readonly List<Vector2> _lassoPts = new List<Vector2>();

        public bool Active  => _active;
        public bool IsLasso => _lasso;

        /// <summary>マーキー開始。lasso=true で投げ縄、false で矩形。</summary>
        public void Begin(Vector2 p, bool lasso)
        {
            _active = true;
            _lasso  = lasso;
            _start  = _cur = p;
            _lassoPts.Clear();
            if (lasso) _lassoPts.Add(p);
        }

        /// <summary>ドラッグ中の更新。</summary>
        public void Update(Vector2 p)
        {
            if (!_active) return;
            _cur = p;
            if (_lasso) _lassoPts.Add(p);
        }

        /// <summary>マーキー終了（状態リセット）。</summary>
        public void End()
        {
            _active = false;
            _lassoPts.Clear();
        }

        /// <summary>矩形選択の正規化 Rect。</summary>
        public Rect Rect
        {
            get
            {
                float x = Mathf.Min(_start.x, _cur.x);
                float y = Mathf.Min(_start.y, _cur.y);
                float w = Mathf.Abs(_cur.x - _start.x);
                float h = Mathf.Abs(_cur.y - _start.y);
                return new Rect(x, y, w, h);
            }
        }

        /// <summary>指定キャンバス点がマーキー内側か。</summary>
        public bool Contains(Vector2 pt)
        {
            if (!_active) return false;
            return _lasso ? PointInPolygon(pt, _lassoPts) : Rect.Contains(pt);
        }

        /// <summary>マーキーを Painter2D で描画する。</summary>
        public void Draw(Painter2D p, Color color)
        {
            if (!_active) return;
            p.strokeColor = color;
            p.fillColor   = new Color(color.r, color.g, color.b, 0.08f);
            p.lineWidth   = 1f;

            if (_lasso)
            {
                if (_lassoPts.Count < 2) return;
                p.BeginPath();
                p.MoveTo(_lassoPts[0]);
                for (int i = 1; i < _lassoPts.Count; i++) p.LineTo(_lassoPts[i]);
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }
            else
            {
                var r = Rect;
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }
        }

        /// <summary>点が多角形内部か（偶奇レイキャスト）。</summary>
        public static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            int j = poly.Count - 1;
            for (int i = 0; i < poly.Count; i++)
            {
                if ((poly[i].y > pt.y) != (poly[j].y > pt.y) &&
                    pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) /
                           (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
