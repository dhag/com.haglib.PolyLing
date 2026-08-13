// PlayerLayoutRoot.SplitMode.cs
// 中央4画面の分割モード（4画面／横2画面／縦2画面／1画面）を
// 「一時的なサイズ変更」だけで切り替える。PlayerPrefs には保存しない。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 中央ビューポート領域の分割モード。
    ///
    /// 中央は入れ子 TwoPaneSplitView で以下の 2×2 構成になっている。
    ///   左列 = Perspective(上) / Side(下)
    ///   右列 = Top(上)         / Front(下)
    ///
    /// 分割モードは「列選択 {左のみ / 両方 / 右のみ}」×
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
        // 分割モード 状態
        // ================================================================

        private ViewportSplitMode _splitMode = ViewportSplitMode.Four;

        /// <summary>現在の分割モード。既定は 4画面。</summary>
        public ViewportSplitMode CurrentSplitMode => _splitMode;

        /// <summary>4画面から離れる直前の右列幅（復帰用）。未取得は -1。</summary>
        private float _baseRightW = -1f;
        /// <summary>4画面から離れる直前の上段高さ（復帰用）。未取得は -1。</summary>
        private float _baseTopH   = -1f;

        private VisualElement _perspSideDragline;
        private VisualElement _topFrontDragline;

        private Button[]            _splitModeButtons;
        private ViewportSplitMode[] _splitModeOrder;

        private bool _splitModeReapplyScheduled;

        // 分割モードボタンの選択表示色
        private static readonly Color SplitModeSelBg   = new Color(0.30f, 0.52f, 0.78f, 1f);
        private static readonly Color SplitModeSelText = new Color(1f, 1f, 1f, 1f);

        // ================================================================
        // 初期化（Build の末尾から呼ぶ。_crossDragRegion 生成後であること）
        // ================================================================

        private void SetupViewportSplitMode()
        {
            // これらの split は子に split を持たないため、自身の anchor が取れる。
            _perspSideDragline = _splitPerspSide?.Q(className: "unity-two-pane-split-view__dragline-anchor");
            _topFrontDragline  = _splitTopFront ?.Q(className: "unity-two-pane-split-view__dragline-anchor");

            // ウィンドウ／ペイン幅変更に追従してモードを再適用する。
            // TwoPaneSplitView は自身のサイズ変化時に固定ペイン寸法を内部値へ戻すため、
            // GeometryChanged 後に再適用しないとモードが崩れる。
            _splitCenter   ?.RegisterCallback<GeometryChangedEvent>(OnSplitModeGeometryChanged);
            _splitPerspSide?.RegisterCallback<GeometryChangedEvent>(OnSplitModeGeometryChanged);
            _splitTopFront ?.RegisterCallback<GeometryChangedEvent>(OnSplitModeGeometryChanged);

            UpdateSplitModeChrome();
            UpdateSplitModeButtons();
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// 分割モードを切り替える。サイズの一時変更のみで実現し、
        /// PlayerPrefs（永続レイアウト）には一切書き込まない。
        /// </summary>
        public void SetViewportSplitMode(ViewportSplitMode mode)
        {
            if (_splitCenter == null || _splitPerspSide == null || _splitTopFront == null) return;
            if (_splitMode == mode) return;

            // 4画面から離れる瞬間に、現在の実寸を復帰用として退避する。
            if (_splitMode == ViewportSplitMode.Four)
                CaptureSplitModeBase();

            _splitMode = mode;

            if (mode == ViewportSplitMode.Four)
            {
                // 横 → 縦の順で適用する。
                // TwoPaneSplitView は横幅変更時に縦の固定ペイン高を内部リセットするため、
                // 縦を後に適用することで上書きが有効になる。
                ApplyHorizontalSplitWidth(BaseRightW());
                ApplyVerticalSplitHeight(BaseTopH());
            }
            else
            {
                ApplySplitModeSizes();
            }

            UpdateSplitModeChrome();
            UpdateSplitModeButtons();
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

        private float BaseRightW()
        {
            if (_baseRightW > 0f) return _baseRightW;
            return Mathf.Max(50f, LoadPref(PrefCenterRight, DefCenterW));
        }

        private float BaseTopH()
        {
            if (_baseTopH > 0f) return _baseTopH;
            float saved = LoadPref(PrefCenterH, -1f);
            if (saved > 0f) return Mathf.Max(30f, saved);
            float ch = _splitCenter != null ? _splitCenter.resolvedStyle.height : 0f;
            if (!float.IsNaN(ch) && ch > 0f) return Mathf.Max(30f, ch * 0.5f);
            return 300f;
        }

        private void CaptureSplitModeBase()
        {
            float w = _splitTopFront != null ? _splitTopFront.resolvedStyle.width : float.NaN;
            if (!float.IsNaN(w) && w > 0f) _baseRightW = w;

            float h = _perspPane != null ? _perspPane.resolvedStyle.height : float.NaN;
            if (!float.IsNaN(h) && h > 0f) _baseTopH = h;
        }

        /// <summary>
        /// 現在のモードに対応するサイズを適用する（4画面以外専用）。
        /// 横は右列（_splitTopFront）の幅、縦は上段（_perspPane / _topPane）の高さで表現する。
        /// </summary>
        private void ApplySplitModeSizes()
        {
            if (_splitMode == ViewportSplitMode.Four) return;
            if (_splitCenter == null) return;

            float cw = _splitCenter.resolvedStyle.width;
            float ch = _splitCenter.resolvedStyle.height;
            if (float.IsNaN(cw) || float.IsNaN(ch) || cw <= 0f || ch <= 0f) return;   // レイアウト未確定

            int col = SplitModeCol(_splitMode);
            int row = SplitModeRow(_splitMode);

            float rightW = (col == 0) ? 0f
                         : (col == 2) ? cw
                         :              Mathf.Min(BaseRightW(), cw);

            float topH   = (row == 0) ? ch
                         : (row == 2) ? 0f
                         :              Mathf.Min(BaseTopH(), ch);

            // 横 → 縦の順（横幅変更が縦の固定ペイン高を内部リセットするため）。
            ApplySplitModeHorizontal(rightW);
            ApplyVerticalSplitHeight(topH);
        }

        /// <summary>
        /// 右列幅を 0 まで許容して設定する。
        /// ApplyHorizontalSplitWidth は Mathf.Max(50f, …) で下限を持つため、
        /// 列を完全に潰すモード用に下限なし版を用意する。
        /// </summary>
        private void ApplySplitModeHorizontal(float rightW)
        {
            if (rightW < 0f) rightW = 0f;
            _currentRightW = rightW;
            _splitTopFront.style.width = rightW;
            // _currentRightW <= 0 のとき ReapplyHorizontalDragline は早期 return するため、
            // 0 のときは dragline を右端へ直接寄せる（非表示なので見た目には影響しない）。
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

        private void OnSplitModeGeometryChanged(GeometryChangedEvent evt)
        {
            if (_splitMode == ViewportSplitMode.Four) return;
            if (_splitModeReapplyScheduled) return;
            if (_splitCenter == null) return;

            // 再適用がさらに GeometryChanged を誘発するため、
            // 次フレームに1回だけまとめて実行する。
            _splitModeReapplyScheduled = true;
            _splitCenter.schedule.Execute(() =>
            {
                _splitModeReapplyScheduled = false;
                ApplySplitModeSizes();
            });
        }

        // ================================================================
        // 付随UI（dragline / クロスドラッグ領域）の表示制御
        // ================================================================

        private void UpdateSplitModeChrome()
        {
            int col = SplitModeCol(_splitMode);
            int row = SplitModeRow(_splitMode);

            // 潰した軸の区切り線はドラッグでモードを壊すため隠す。
            SetElementVisible(_centerDraglineAnchor, col == 1);
            SetElementVisible(_perspSideDragline,    row == 1);
            SetElementVisible(_topFrontDragline,     row == 1);
            // クロスドラッグ（4分割交差点）は4画面のときだけ。
            SetElementVisible(_crossDragRegion, _splitMode == ViewportSplitMode.Four);
        }

        private static void SetElementVisible(VisualElement ve, bool visible)
        {
            if (ve == null) return;
            ve.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // 左ペインの 3×3 モードグリッド
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
                "4画面",
                "縦2画面：右列（Top ／ Front）",
                "1画面：Side",
                "横2画面：下段（Side ｜ Front）",
                "1画面：Front",
            };

            _splitModeOrder   = modes;
            _splitModeButtons = new Button[9];

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
                    b.clicked += () => SetViewportSplitMode(mode);

                    _splitModeButtons[idx] = b;
                    rowEl.Add(b);
                }
                wrap.Add(rowEl);
            }

            return wrap;
        }

        private void UpdateSplitModeButtons()
        {
            if (_splitModeButtons == null || _splitModeOrder == null) return;
            for (int i = 0; i < _splitModeButtons.Length; i++)
            {
                var b = _splitModeButtons[i];
                if (b == null) continue;
                bool on = (_splitModeOrder[i] == _splitMode);
                if (on)
                {
                    b.style.backgroundColor = new StyleColor(SplitModeSelBg);
                    b.style.color           = new StyleColor(SplitModeSelText);
                }
                else
                {
                    b.style.backgroundColor = StyleKeyword.Null;
                    b.style.color           = StyleKeyword.Null;
                }
            }
        }
    }
}
