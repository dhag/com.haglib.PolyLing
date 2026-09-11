// UiAutomationHighlight.cs
// UI 自動操作の強調枠。対象の周りに赤枠を重ねる。1 か所だけ。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【対象に手を入れない】
//   対象自身の border は変えない。root 直下に絶対配置の枠を 1 つ置いて重ねる。
//   枠は PickingMode.Ignore なので、枠の下の項目は通常どおり操作できる。
//
// 【位置の追従】
//   毎フレームは見ない。次の 2 つの通知で置き直す。
//     ・対象の GeometryChangedEvent … レイアウトが確定して対象の矩形が変わったとき
//       （パネルを開いた直後・折り畳みを開いた直後・ウインドウ寸法の変更）
//     ・祖先 ScrollView のスクロールバーの valueChanged … スクロールしたとき
//       （スクロールは transform で動かすため GeometryChangedEvent が出ない）
//   前例: PlayerLayoutRoot.cs の GeometryChangedEvent、PlayerLogSubPanel.cs の valueChanged。
//
// 【見えない範囲】
//   対象の矩形を祖先 ScrollView の表示領域で切り、残りが無ければ枠を隠す。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public sealed class UiAutomationHighlight
    {
        private const float LineWidth = 3f;
        private const float Margin    = 3f;
        private const float Radius    = 2f;
        private static readonly Color LineColor = new Color(1f, 0.15f, 0.15f, 1f);

        private readonly VisualElement _root;
        private readonly VisualElement _frame;

        private VisualElement          _target;
        private readonly List<ScrollView> _scrolls = new List<ScrollView>();

        /// <summary>今強調している項目。無ければ null。</summary>
        public VisualElement Target => _target;

        public UiAutomationHighlight(VisualElement root)
        {
            _root  = root;
            _frame = new VisualElement { name = "UiAutomationHighlight", pickingMode = PickingMode.Ignore };

            var s = _frame.style;
            s.position = Position.Absolute;
            s.borderLeftWidth   = LineWidth;
            s.borderRightWidth  = LineWidth;
            s.borderTopWidth    = LineWidth;
            s.borderBottomWidth = LineWidth;
            s.borderLeftColor   = LineColor;
            s.borderRightColor  = LineColor;
            s.borderTopColor    = LineColor;
            s.borderBottomColor = LineColor;
            s.borderTopLeftRadius     = Radius;
            s.borderTopRightRadius    = Radius;
            s.borderBottomLeftRadius  = Radius;
            s.borderBottomRightRadius = Radius;
            s.display = DisplayStyle.None;

            _root?.Add(_frame);
        }

        /// <summary>target を強調する。前の強調は消す。</summary>
        public void Show(VisualElement target)
        {
            Clear();
            if (target == null || _root == null) return;

            _target = target;
            _target.RegisterCallback<GeometryChangedEvent>(OnTargetGeometryChanged);

            for (var p = target.parent; p != null; p = p.parent)
            {
                if (!(p is ScrollView sv)) continue;
                sv.verticalScroller.valueChanged   += OnScrolled;
                sv.horizontalScroller.valueChanged += OnScrolled;
                _scrolls.Add(sv);
            }

            _frame.style.display = DisplayStyle.Flex;
            _frame.BringToFront();
            Place();
        }

        /// <summary>強調を消し、登録した通知を外す。</summary>
        public void Clear()
        {
            if (_target != null)
                _target.UnregisterCallback<GeometryChangedEvent>(OnTargetGeometryChanged);
            _target = null;

            foreach (var sv in _scrolls)
            {
                if (sv == null) continue;
                sv.verticalScroller.valueChanged   -= OnScrolled;
                sv.horizontalScroller.valueChanged -= OnScrolled;
            }
            _scrolls.Clear();

            _frame.style.display = DisplayStyle.None;
        }

        /// <summary>枠を root から外す。破棄時に呼ぶ。</summary>
        public void Dispose()
        {
            Clear();
            _frame.RemoveFromHierarchy();
        }

        private void OnTargetGeometryChanged(GeometryChangedEvent evt) => Place();

        private void OnScrolled(float _) => Place();

        /// <summary>対象の今の矩形に合わせて枠を置く。</summary>
        private void Place()
        {
            if (_target == null || _root == null) return;

            Rect r = _target.worldBound;
            if (!IsUsable(r))
            {
                _frame.style.visibility = Visibility.Hidden;
                return;
            }

            // 祖先 ScrollView の表示領域で切る。
            foreach (var sv in _scrolls)
            {
                Rect view = sv.contentViewport.worldBound;
                if (!IsUsable(view)) continue;
                r = Intersect(r, view);
                if (r.width <= 0f || r.height <= 0f)
                {
                    _frame.style.visibility = Visibility.Hidden;
                    return;
                }
            }

            const float pad = Margin + LineWidth;
            Vector2 tl = _root.WorldToLocal(new Vector2(r.xMin, r.yMin));

            _frame.style.left   = tl.x - pad;
            _frame.style.top    = tl.y - pad;
            _frame.style.width  = r.width  + pad * 2f;
            _frame.style.height = r.height + pad * 2f;
            _frame.style.visibility = Visibility.Visible;
        }

        /// <summary>レイアウト済みの矩形か（NaN でなく、幅と高さが正）。</summary>
        internal static bool IsUsable(Rect r)
            => !float.IsNaN(r.x) && !float.IsNaN(r.y)
            && !float.IsNaN(r.width) && !float.IsNaN(r.height)
            && r.width > 0f && r.height > 0f;

        private static Rect Intersect(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin);
            float y0 = Mathf.Max(a.yMin, b.yMin);
            float x1 = Mathf.Min(a.xMax, b.xMax);
            float y1 = Mathf.Min(a.yMax, b.yMax);
            return Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }
    }
}
