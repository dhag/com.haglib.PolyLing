// PlayerViewportPanel.cs
// UIToolkit VisualElement として RenderTexture を表示し、
// UIToolkit ポインターイベントを IMouseEventSource として公開する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// RenderTexture を背景に描画する UIToolkit VisualElement。
    /// UIToolkit のポインターイベントを受け取り、<see cref="IMouseEventSource"/> として公開する。
    /// <see cref="PlayerViewport"/> を所有する。
    /// </summary>
    public class PlayerViewportPanel : VisualElement, IMouseEventSource
    {
        // ================================================================
        // IMouseEventSource 実装
        // ================================================================

        public event Action<int, Vector2, ModifierKeys> OnButtonDown;
        public event Action<int, Vector2, ModifierKeys> OnButtonUp;
        public event Action<int, Vector2, ModifierKeys> OnClick;
        public event Action<int, Vector2, ModifierKeys> OnDragBegin;
        public event Action<int, Vector2, Vector2, ModifierKeys> OnDrag;
        public event Action<int, Vector2, ModifierKeys> OnDragEnd;
        public event Action<float, ModifierKeys> OnScroll;

        public bool IsAnyDragging => _state[0] == BtnState.Dragging
                                  || _state[1] == BtnState.Dragging
                                  || _state[2] == BtnState.Dragging;
        public bool IsDragging(int btn) => btn >= 0 && btn < 3 && _state[btn] == BtnState.Dragging;

        // ================================================================
        // 設定
        // ================================================================

        public float DragThreshold = 4f;

        // ================================================================
        // プロパティ
        // ================================================================

        public PlayerViewport Viewport { get; private set; }

        // ================================================================
        // 内部状態
        // ================================================================

        private enum BtnState { Idle, Pressed, Dragging }

        private readonly BtnState[] _state       = new BtnState[3];
        private readonly Vector2[]  _downPos      = new Vector2[3];
        private readonly Vector2[]  _prevDragPos  = new Vector2[3];

        // ================================================================
        // コンストラクタ
        // ================================================================

        public PlayerViewportPanel()
        {
            style.flexGrow        = 1;
            style.overflow        = Overflow.Hidden;
            style.backgroundSize  = new StyleBackgroundSize(
                new BackgroundSize(BackgroundSizeType.Cover));

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        // ================================================================
        // Viewport 接続
        // ================================================================

        public void SetViewport(PlayerViewport viewport)
        {
            // 旧 Viewport の接続を解除
            Viewport?.DisconnectSource(this);

            Viewport = viewport;

            if (viewport != null)
            {
                // RenderTexture を背景に設定
                RefreshBackground();
                // コントローラーをこのパネルのイベントに接続
                viewport.ConnectSource(this);
            }
        }

        // ================================================================
        // RenderTexture 背景更新
        // ================================================================

        private void RefreshBackground()
        {
            if (Viewport?.RT != null)
                style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(Viewport.RT));
        }

        // ================================================================
        // ジオメトリ変更（パネルリサイズ時）
        // ================================================================

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (Viewport == null) return;
            int w = Mathf.Max(1, Mathf.RoundToInt(resolvedStyle.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(resolvedStyle.height));
            Viewport.Resize(w, h);
            RefreshBackground();
        }

        // ================================================================
        // ポインターイベント処理
        // ================================================================

        private void OnPointerDown(PointerDownEvent evt)
        {
            int btn = evt.button;
            if (btn < 0 || btn >= 3) return;

            this.CapturePointer(evt.pointerId);

            Vector2 pos  = ToViewportCoord(evt.localPosition);
            var     mods = GetMods(evt);

            _state[btn]   = BtnState.Pressed;
            _downPos[btn] = pos;
            OnButtonDown?.Invoke(btn, pos, mods);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pos  = ToViewportCoord(evt.localPosition);
            var     mods = GetMods(evt);

            for (int btn = 0; btn < 3; btn++)
            {
                if (_state[btn] == BtnState.Idle) continue;

                if (_state[btn] == BtnState.Pressed)
                {
                    float moved = Vector2.Distance(pos, _downPos[btn]);
                    if (moved > DragThreshold)
                    {
                        _state[btn]       = BtnState.Dragging;
                        _prevDragPos[btn] = _downPos[btn];
                        OnDragBegin?.Invoke(btn, _downPos[btn], mods);
                    }
                }

                if (_state[btn] == BtnState.Dragging)
                {
                    Vector2 delta     = pos - _prevDragPos[btn];
                    _prevDragPos[btn] = pos;
                    if (delta.sqrMagnitude > 0f)
                        OnDrag?.Invoke(btn, pos, delta, mods);
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            int btn = evt.button;
            if (btn < 0 || btn >= 3) return;

            this.ReleasePointer(evt.pointerId);

            Vector2 pos       = ToViewportCoord(evt.localPosition);
            var     mods      = GetMods(evt);
            var     prevState = _state[btn];
            _state[btn] = BtnState.Idle;

            OnButtonUp?.Invoke(btn, pos, mods);

            if (prevState == BtnState.Pressed)
                OnClick?.Invoke(btn, pos, mods);
            else if (prevState == BtnState.Dragging)
                OnDragEnd?.Invoke(btn, pos, mods);
        }

        private void OnWheel(WheelEvent evt)
        {
            // UIToolkit WheelEvent.delta.y: 下スクロール=正
            // PlayerMouseDispatcher に合わせ: 手前スクロール（上）=正
            float scroll = -evt.delta.y * 0.1f;
            var   mods   = new ModifierKeys
            {
                Shift = evt.shiftKey,
                Ctrl  = evt.ctrlKey,
                Alt   = evt.altKey,
            };
            OnScroll?.Invoke(scroll, mods);
            evt.StopPropagation();
        }

        // ================================================================
        // 座標変換：panel-local（Y=0 が上）→ viewport-screen（Y=0 が下）
        // ================================================================

        /// <summary>
        /// UIToolkit local座標（Y=0が上）をビューポートスクリーン座標（Y=0が下）に変換する。
        /// Camera.WorldToScreenPoint の出力と同じ空間になる。
        /// </summary>
        private Vector2 ToViewportCoord(Vector2 local)
        {
            float h = resolvedStyle.height;
            return new Vector2(local.x, h - local.y);
        }

        private static ModifierKeys GetMods(PointerEventBase<PointerDownEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };

        private static ModifierKeys GetMods(PointerEventBase<PointerMoveEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };

        private static ModifierKeys GetMods(PointerEventBase<PointerUpEvent> evt)
            => new ModifierKeys { Shift = evt.shiftKey, Ctrl = evt.ctrlKey, Alt = evt.altKey };
    }
}
