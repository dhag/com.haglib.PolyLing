// PlayerLayoutRoot.SplitMode.cs
// 中央4画面の仕切り位置を「押下した瞬間に1回だけ」再配置する。
// モード状態は持たず、PlayerPrefs にも書き込まない。
// 押下後は人間が仕切りをドラッグした場合と同じ状態になる。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 中央ビューポート領域の仕切り再配置の種別。
    ///
    /// 中央は入れ子 TwoPaneSplitView で以下の 2×2 構成になっている。
    ///   左列 = Perspective(上) / Side(下)
    ///   右列 = Top(上)         / Front(下)
    ///
    /// 種別は「列選択 {左のみ / 両方 / 右のみ}」×
    /// 「行選択 {上のみ / 両方 / 下のみ}」の直積 9 通りで表現される。
    /// </summary>
    public enum ViewportSplitMode
    {
        /// <summary>4画面（列=両方・行=両方）</summary>
        Four,
        /// <summary>横2画面：上段のみ（Perspective ｜ Top）</summary>
        RowTop,
        /// <summary>横2画面：下段のみ（Side ｜ Front）</summary>
        RowBottom,
        /// <summary>縦2画面：左列のみ（Perspective ／ Side）</summary>
        ColLeft,
        /// <summary>縦2画面：右列のみ（Top ／ Front）</summary>
        ColRight,
        /// <summary>1画面：Perspective</summary>
        OnlyPersp,
        /// <summary>1画面：Top</summary>
        OnlyTop,
        /// <summary>1画面：Side</summary>
        OnlySide,
        /// <summary>1画面：Front</summary>
        OnlyFront,
    }

    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// 中央の縦横の仕切りを1回だけ再配置する。
        /// モードとして保持せず、以降は通常どおりドラッグ・ウィンドウ伸縮で動かせる。
        /// 潰す側は 0、残す側は全幅／全高、両方残す場合は中央（1/2）に置く。
        /// </summary>
        public void ApplyViewportSplit(ViewportSplitMode mode)
        {
            if (_splitCenter == null || _splitPerspSide == null || _splitTopFront == null) return;

            float cw = _splitCenter.resolvedStyle.width;
            float ch = _splitCenter.resolvedStyle.height;
            if (float.IsNaN(cw) || float.IsNaN(ch) || cw <= 0f || ch <= 0f) return;   // レイアウト未確定

            int col = SplitModeCol(mode);
            int row = SplitModeRow(mode);

            float rightW = (col == 0) ? 0f
                         : (col == 2) ? cw
                         :              cw * 0.5f;

            float topH   = (row == 0) ? ch
                         : (row == 2) ? 0f
                         :              ch * 0.5f;

            // 横 → 縦の順（横幅変更が縦の固定ペイン高を内部リセットするため）。
            ApplySplitWidthNoMin(rightW);
            ApplyVerticalSplitHeight(topH);
        }

        // ================================================================
        // サイズ適用
        // ================================================================

        /// <summary>列選択を返す。0=左列のみ / 1=両方 / 2=右列のみ。</summary>
        private static int SplitModeCol(ViewportSplitMode m)
        {
            switch (m)
            {
                case ViewportSplitMode.ColLeft:
                case ViewportSplitMode.OnlyPersp:
                case ViewportSplitMode.OnlySide:
                    return 0;
                case ViewportSplitMode.ColRight:
                case ViewportSplitMode.OnlyTop:
                case ViewportSplitMode.OnlyFront:
                    return 2;
                default:
                    return 1;
            }
        }

        /// <summary>行選択を返す。0=上段のみ / 1=両方 / 2=下段のみ。</summary>
        private static int SplitModeRow(ViewportSplitMode m)
        {
            switch (m)
            {
                case ViewportSplitMode.RowTop:
                case ViewportSplitMode.OnlyPersp:
                case ViewportSplitMode.OnlyTop:
                    return 0;
                case ViewportSplitMode.RowBottom:
                case ViewportSplitMode.OnlySide:
                case ViewportSplitMode.OnlyFront:
                    return 2;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 右列幅を 0 まで許容して設定する。
        /// ApplyHorizontalSplitWidth は Mathf.Max(50f, …) で下限を持つため、
        /// 列を完全に潰す再配置用に下限なし版を用意する。
        /// </summary>
        private void ApplySplitWidthNoMin(float rightW)
        {
            if (rightW < 0f) rightW = 0f;
            _currentRightW = rightW;
            _splitTopFront.style.width = rightW;
            // _currentRightW <= 0 のとき ReapplyHorizontalDragline は早期 return するため、
            // 0 のときは dragline を右端へ直接寄せる。
            if (rightW > 0f)
            {
                ReapplyHorizontalDragline();
            }
            else if (_centerDraglineAnchor != null)
            {
                float containerW = _splitCenter.resolvedStyle.width;
                if (!float.IsNaN(containerW) && containerW > 0f)
                    _centerDraglineAnchor.style.left = containerW;
            }
        }

        // ================================================================
        // 左ペインの 3×3 ボタングリッド
        //
        // ボタンの並びがそのまま画面配置と対応する。
        //   行0(上段のみ)：P      ｜ P｜T ｜ T
        //   行1(両方)    ：P／S   ｜ 4画面 ｜ T／F
        //   行2(下段のみ)：S      ｜ S｜F ｜ F
        // ================================================================

        private VisualElement BuildSplitModeGrid()
        {
            var modes = new ViewportSplitMode[9]
            {
                ViewportSplitMode.OnlyPersp, ViewportSplitMode.RowTop,    ViewportSplitMode.OnlyTop,
                ViewportSplitMode.ColLeft,   ViewportSplitMode.Four,      ViewportSplitMode.ColRight,
                ViewportSplitMode.OnlySide,  ViewportSplitMode.RowBottom, ViewportSplitMode.OnlyFront,
            };
            var labels = new string[9]
            {
                "P",   "P|T", "T",
                "P/S", "4",   "T/F",
                "S",   "S|F", "F",
            };
            var tips = new string[9]
            {
                "1画面：Perspective",
                "横2画面：上段（Perspective ｜ Top）",
                "1画面：Top",
                "縦2画面：左列（Perspective ／ Side）",
                "4画面（縦横の仕切りを中央へ）",
                "縦2画面：右列（Top ／ Front）",
                "1画面：Side",
                "横2画面：下段（Side ｜ Front）",
                "1画面：Front",
            };

            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Column;
            wrap.style.marginBottom  = 2;

            for (int r = 0; r < 3; r++)
            {
                var rowEl = new VisualElement();
                rowEl.style.flexDirection = FlexDirection.Row;
                rowEl.style.marginBottom  = 1;

                for (int c = 0; c < 3; c++)
                {
                    int idx = r * 3 + c;
                    var mode = modes[idx];

                    var b = new Button { text = labels[idx], tooltip = tips[idx] };
                    b.style.flexGrow      = 1;
                    b.style.flexBasis     = 0;
                    b.style.height        = 18;
                    b.style.fontSize      = 9;
                    b.style.marginTop     = 0;
                    b.style.marginBottom  = 0;
                    b.style.marginLeft    = (c == 0) ? 0 : 1;
                    b.style.marginRight   = 0;
                    b.style.paddingTop    = 0;
                    b.style.paddingBottom = 0;
                    b.style.paddingLeft   = 0;
                    b.style.paddingRight  = 0;
                    b.clicked += () => ApplyViewportSplit(mode);

                    rowEl.Add(b);
                }
                wrap.Add(rowEl);
            }

            return wrap;
        }
    }
}
