// UiAutomationHighlight.cs
// UI 自動操作の強調枠。対象の周りに赤枠を重ねる。複数の対象を同時に囲める
// （折り畳みの見出しとその中のボタンなど）。
// Runtime/Poly_Ling_Player/View/UiAutomation/ に配置
//
// 【対象に手を入れない】
//   対象自身の border は変えない。root 直下に絶対配置の枠を対象ごとに 1 つ置いて重ねる。
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

        /// <summary>対象 1 つぶんの枠と、置き直しの通知の登録。</summary>
        private sealed class Entry
        {
            public VisualElement    Target;
            public VisualElement    Frame;
            public readonly List<ScrollView> Scrolls = new List<ScrollView>();
            public EventCallback<GeometryChangedEvent> OnGeometry;
            public System.Action<float>                OnScroll;
        }

        private readonly VisualElement _root;
        private readonly List<Entry>   _entries = new List<Entry>();

        /// <summary>最後に強調した項目。無ければ null。</summary>
        public VisualElement Target => _entries.Count > 0 ? _entries[_entries.Count - 1].Target : null;

        /// <summary>今強調している項目の数。</summary>
        public int Count => _entries.Count;

        public UiAutomationHighlight(VisualElement root)
        {
            _root = root;
        }

        /// <summary>target だけを強調する。前の強調は全部消す。</summary>
        public void Show(VisualElement target)
        {
            Clear();
            Add(target);
        }

        /// <summary>target を強調に足す。前の強調は残す。同じ対象がすでにあれば置き直すだけ。</summary>
        public void Add(VisualElement target)
        {
            if (target == null || _root == null) return;

            foreach (var e in _entries)
                if (e.Target == target) { Place(e); return; }

            var entry = new Entry { Target = target, Frame = MakeFrame() };
            entry.OnGeometry = _ => Place(entry);
            entry.OnScroll   = _ => Place(entry);

            target.RegisterCallback(entry.OnGeometry);
            for (var p = target.parent; p != null; p = p.parent)
            {
                if (!(p is ScrollView sv)) continue;
                sv.verticalScroller.valueChanged   += entry.OnScroll;
                sv.horizontalScroller.valueChanged += entry.OnScroll;
                entry.Scrolls.Add(sv);
            }

            _root.Add(entry.Frame);
            entry.Frame.BringToFront();
            _entries.Add(entry);
            Place(entry);
        }

        /// <summary>target の強調を消す。強調していなければ false。</summary>
        public bool Remove(VisualElement target)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Target != target) continue;
                Detach(_entries[i]);
                _entries.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>強調を全部消し、登録した通知を外す。</summary>
        public void Clear()
        {
            foreach (var e in _entries) Detach(e);
            _entries.Clear();
        }

        /// <summary>破棄時に呼ぶ。</summary>
        public void Dispose() => Clear();

        private static void Detach(Entry e)
        {
            if (e.Target != null) e.Target.UnregisterCallback(e.OnGeometry);
            foreach (var sv in e.Scrolls)
            {
                if (sv == null) continue;
                sv.verticalScroller.valueChanged   -= e.OnScroll;
                sv.horizontalScroller.valueChanged -= e.OnScroll;
            }
            e.Scrolls.Clear();
            e.Frame?.RemoveFromHierarchy();
        }

        private static VisualElement MakeFrame()
        {
            var f = new VisualElement { name = "UiAutomationHighlight", pickingMode = PickingMode.Ignore };
            var s = f.style;
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
            return f;
        }

        /// <summary>対象の今の矩形に合わせて枠を置く。</summary>
        private void Place(Entry e)
        {
            if (e?.Target == null || _root == null) return;

            Rect r = e.Target.worldBound;
            if (!IsUsable(r))
            {
                e.Frame.style.visibility = Visibility.Hidden;
                return;
            }

            // 祖先 ScrollView の表示領域で切る。
            foreach (var sv in e.Scrolls)
            {
                Rect view = sv.contentViewport.worldBound;
                if (!IsUsable(view)) continue;
                r = Intersect(r, view);
                if (r.width <= 0f || r.height <= 0f)
                {
                    e.Frame.style.visibility = Visibility.Hidden;
                    return;
                }
            }

            const float pad = Margin + LineWidth;
            Vector2 tl = _root.WorldToLocal(new Vector2(r.xMin, r.yMin));

            e.Frame.style.left   = tl.x - pad;
            e.Frame.style.top    = tl.y - pad;
            e.Frame.style.width  = r.width  + pad * 2f;
            e.Frame.style.height = r.height + pad * 2f;
            e.Frame.style.visibility = Visibility.Visible;
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
