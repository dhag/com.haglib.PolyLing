// Canvas2DHandle.cs
// 2Dキャンバス（UV/回転体/Profile2D）共通の回転/拡大縮小ハンドル。
// 3D の RotateRingGizmo（回転リング）＋ AxisGizmo（X/Y/中心ハンドル）の2D版。
// アンカー（Canvas2DAnchor.Value）をピボットとして、リングドラッグ＝回転、
// 軸ハンドルドラッグ＝X/Y スケール、角ハンドルドラッグ＝一様スケールを表す。
// 描画・ヒットテスト・駆動値算出のみを担い、実変換は各キャンバスの既存数式を使う。
// 座標はすべてキャンバスpx。データ座標との変換は各キャンバスのマッパを使う。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>回転/拡大縮小ハンドル（アンカー基準、キャンバスpx）。</summary>
    public sealed class Canvas2DHandle
    {
        public enum HandleType { None, Rotate, ScaleX, ScaleY, ScaleUniform }

        public float RingRadius = 55f;   // リング/軸ハンドルまでの半径(px)
        public float HandleHit  = 10f;   // 軸/角ハンドルのヒット半径(px)
        public float RingHit    = 7f;    // リングのヒット許容(px)

        public HandleType Hovered = HandleType.None;
        public HandleType Active  = HandleType.None;

        // 3D と揃えた配色（X=赤/Y=緑）＋回転リング/一様
        private static readonly Color ColX     = new Color(0.90f, 0.35f, 0.35f);
        private static readonly Color ColY     = new Color(0.40f, 0.85f, 0.45f);
        private static readonly Color ColRing  = new Color(0.55f, 0.70f, 1.00f, 0.85f);
        private static readonly Color ColUni   = new Color(1.00f, 0.85f, 0.30f);
        private static readonly Color ColHi    = Color.white;

        /// <summary>角ハンドル方向（右上）。</summary>
        private Vector2 UniPos(Vector2 c) => new Vector2(c.x + RingRadius * 0.7071f, c.y - RingRadius * 0.7071f);
        private Vector2 XPos  (Vector2 c) => new Vector2(c.x + RingRadius, c.y);
        private Vector2 YPos  (Vector2 c) => new Vector2(c.x, c.y - RingRadius);

        /// <summary>アンカーのキャンバス座標 c を中心に描画する。</summary>
        public void Draw(Painter2D p, Vector2 c)
        {
            // 回転リング
            p.strokeColor = (Active == HandleType.Rotate || Hovered == HandleType.Rotate) ? ColHi : ColRing;
            p.lineWidth   = (Active == HandleType.Rotate || Hovered == HandleType.Rotate) ? 2.5f : 1.5f;
            p.BeginPath(); p.Arc(c, RingRadius, 0f, 360f); p.Stroke();

            // X 軸ハンドル
            var xe = XPos(c);
            bool xh = (Active == HandleType.ScaleX || Hovered == HandleType.ScaleX);
            p.strokeColor = xh ? ColHi : ColX; p.lineWidth = xh ? 2.5f : 1.75f;
            p.BeginPath(); p.MoveTo(c); p.LineTo(xe); p.Stroke();
            FillSquare(p, xe, xh ? 6f : 4.5f, xh ? ColHi : ColX);

            // Y 軸ハンドル
            var ye = YPos(c);
            bool yh = (Active == HandleType.ScaleY || Hovered == HandleType.ScaleY);
            p.strokeColor = yh ? ColHi : ColY; p.lineWidth = yh ? 2.5f : 1.75f;
            p.BeginPath(); p.MoveTo(c); p.LineTo(ye); p.Stroke();
            FillSquare(p, ye, yh ? 6f : 4.5f, yh ? ColHi : ColY);

            // 一様スケール角ハンドル
            var ue = UniPos(c);
            bool uh = (Active == HandleType.ScaleUniform || Hovered == HandleType.ScaleUniform);
            FillSquare(p, ue, uh ? 7f : 5f, uh ? ColHi : ColUni);
        }

        private static void FillSquare(Painter2D p, Vector2 c, float r, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x - r, c.y - r));
            p.LineTo(new Vector2(c.x + r, c.y - r));
            p.LineTo(new Vector2(c.x + r, c.y + r));
            p.LineTo(new Vector2(c.x - r, c.y + r));
            p.ClosePath();
            p.Fill();
        }

        /// <summary>カーソル（キャンバス座標）がどのハンドル上かを返す。優先: 一様→X→Y→リング。</summary>
        public HandleType HitTest(Vector2 cursor, Vector2 c)
        {
            if (Vector2.Distance(cursor, UniPos(c)) <= HandleHit) return HandleType.ScaleUniform;
            if (Vector2.Distance(cursor, XPos(c))   <= HandleHit) return HandleType.ScaleX;
            if (Vector2.Distance(cursor, YPos(c))   <= HandleHit) return HandleType.ScaleY;
            if (Mathf.Abs(Vector2.Distance(cursor, c) - RingRadius) <= RingHit) return HandleType.Rotate;
            return HandleType.None;
        }

        /// <summary>カーソルのアンカー基準角度(度)。回転の累積に使う。</summary>
        public static float AngleDeg(Vector2 anchorC, Vector2 cursorC)
            => Mathf.Atan2(cursorC.y - anchorC.y, cursorC.x - anchorC.x) * Mathf.Rad2Deg;

        /// <summary>
        /// スケール係数を返す。基準距離＝RingRadius（ハンドル静止位置で係数1）。
        /// キャンバスYは下向きのため、Y軸は上方向を +data.Y として扱う。
        /// </summary>
        public void ScaleFactors(HandleType type, Vector2 anchorC, Vector2 cursorC, out float sx, out float sy)
        {
            sx = 1f; sy = 1f;
            float r = Mathf.Max(0.001f, RingRadius);
            switch (type)
            {
                case HandleType.ScaleX:       sx = (cursorC.x - anchorC.x) / r; break;
                case HandleType.ScaleY:       sy = (anchorC.y - cursorC.y) / r; break;
                case HandleType.ScaleUniform: sx = sy = Vector2.Distance(cursorC, anchorC) / r; break;
            }
        }
    }
}
