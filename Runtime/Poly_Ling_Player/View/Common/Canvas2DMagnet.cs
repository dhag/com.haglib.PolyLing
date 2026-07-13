// Canvas2DMagnet.cs
// 2Dキャンバス（UV/回転体/Profile2D）共通のマグネット（ソフト選択/比例編集）。
// 非選択点について「最近傍の選択点までの距離」を求め、半径内なら
// weight = falloff(dist/radius)（3Dの FalloffHelper と同じ曲線）で影響させる。
// 移動はドラッグ（delta×weight）、回転/拡大縮小は変換適用（w倍のパラメータ）で用いる。
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;   // FalloffType / FalloffHelper

namespace Poly_Ling.Player
{
    /// <summary>2Dマグネット（比例編集）。値はデータ座標系。</summary>
    public sealed class Canvas2DMagnet
    {
        public bool        Enabled;
        public float       Radius   = 0.3f;
        public FalloffType Falloff  = FalloffType.Smooth;

        /// <summary>点 p の重み（非選択点用）。範囲外・無効時は 0。</summary>
        public float WeightFor(Vector2 p, IReadOnlyList<Vector2> selectedPos)
        {
            if (!Enabled || Radius <= 0f || selectedPos == null || selectedPos.Count == 0) return 0f;
            float min = float.MaxValue;
            for (int i = 0; i < selectedPos.Count; i++)
            {
                float d = Vector2.Distance(p, selectedPos[i]);
                if (d < min) min = d;
            }
            if (min >= Radius) return 0f;
            float wgt = FalloffHelper.Calculate(min / Radius, Falloff);
            return wgt > 0.0001f ? wgt : 0f;
        }

        /// <summary>影響半径をキャンバス座標上の円で描画する（選択点ごと）。</summary>
        public void DrawRadius(Painter2D p, IReadOnlyList<Vector2> canvasCenters, float canvasRadius)
        {
            if (!Enabled || canvasCenters == null || canvasCenters.Count == 0 || canvasRadius <= 0.5f) return;
            p.strokeColor = new Color(0.3f, 0.85f, 1f, 0.55f);
            p.lineWidth   = 1f;
            for (int i = 0; i < canvasCenters.Count; i++)
            {
                p.BeginPath();
                p.Arc(canvasCenters[i], canvasRadius, 0f, 360f);
                p.Stroke();
            }
        }
    }
}
