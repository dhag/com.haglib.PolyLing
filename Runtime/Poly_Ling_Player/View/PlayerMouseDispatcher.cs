// PlayerMouseDispatcher.cs
// Input.* の一元読み取りとマウスイベント発火。
// ボタンごとの状態機械: Idle → Pressed → Dragging
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Player
{
    // ================================================================
    // 修飾キー
    // ================================================================

    public struct ModifierKeys
    {
        public bool Shift;
        public bool Ctrl;
        public bool Alt;
    }

    // ================================================================
    // PlayerMouseDispatcher
    // ================================================================

    /// <summary>
    /// Input.* を毎フレーム一か所だけ読み取り、マウスイベントを発火する。
    /// Viewer.Update() から Update(isPointerOverUI) を呼ぶ。
    /// </summary>
    public class PlayerMouseDispatcher : IMouseEventSource
    {
        // ================================================================
        // 設定
        // ================================================================

        /// <summary>ドラッグ開始と判定するピクセル移動量。</summary>
        public float DragThreshold = 4f;

        // ================================================================
        // イベント
        // ================================================================

        /// <summary>マウスボタンが押された（UI上以外）。(btn, screenPos, mods)</summary>
        public event Action<int, Vector2, ModifierKeys> OnButtonDown;

        /// <summary>マウスボタンが離された。(btn, screenPos, mods)</summary>
        public event Action<int, Vector2, ModifierKeys> OnButtonUp;

        /// <summary>
        /// クリック確定。移動量が DragThreshold 以下でボタンアップした場合。
        /// (btn, screenPos, mods)
        /// </summary>
        public event Action<int, Vector2, ModifierKeys> OnClick;

        /// <summary>ドラッグ開始。移動量が DragThreshold を超えた最初のフレーム。(btn, downScreenPos, mods)</summary>
        public event Action<int, Vector2, ModifierKeys> OnDragBegin;

        /// <summary>ドラッグ中。(btn, currentScreenPos, delta, mods)</summary>
        public event Action<int, Vector2, Vector2, ModifierKeys> OnDrag;

        /// <summary>ドラッグ終了（ボタンアップ）。(btn, screenPos, mods)</summary>
        public event Action<int, Vector2, ModifierKeys> OnDragEnd;

        /// <summary>スクロール。(scrollDelta, mods) UI上では発火しない。</summary>
        public event Action<float, ModifierKeys> OnScroll;

        // ================================================================
        // 読み取り専用状態
        // ================================================================

        public Vector2      MousePosition   { get; private set; }
        public ModifierKeys Modifiers       { get; private set; }
        public bool         IsPointerOverUI { get; private set; }

        /// <summary>指定ボタンがドラッグ中か。</summary>
        public bool IsDragging(int btn) => btn >= 0 && btn < 3 && _state[btn] == BtnState.Dragging;

        /// <summary>いずれかのボタンがドラッグ中か。</summary>
        public bool IsAnyDragging => _state[0] == BtnState.Dragging
                                  || _state[1] == BtnState.Dragging
                                  || _state[2] == BtnState.Dragging;

        // ================================================================
        // 内部状態
        // ================================================================

        private enum BtnState { Idle, Pressed, Dragging }

        private readonly BtnState[] _state       = new BtnState[3];
        private readonly Vector2[]  _downPos     = new Vector2[3];
        private readonly Vector2[]  _prevDragPos = new Vector2[3];

        // ================================================================
        // Update
        // ================================================================

        /// <summary>
        /// 毎フレーム Viewer.Update() から呼ぶ。
        /// </summary>
        public void Update(bool isPointerOverUI)
        {
            IsPointerOverUI = isPointerOverUI;
            MousePosition   = Input.mousePosition;
            Modifiers       = new ModifierKeys
            {
                Shift = Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift),
                Ctrl  = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Alt   = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt),
            };

            for (int btn = 0; btn < 3; btn++)
                ProcessButton(btn, isPointerOverUI);

            // スクロール（UI上は無効）
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!isPointerOverUI && Mathf.Abs(scroll) > 0.001f)
                OnScroll?.Invoke(scroll, Modifiers);
        }

        // ================================================================
        // 内部処理
        // ================================================================

        private void ProcessButton(int btn, bool isPointerOverUI)
        {
            Vector2 pos  = MousePosition;
            var     mods = Modifiers;

            // --- ButtonDown ---
            if (Input.GetMouseButtonDown(btn))
            {
                // UI上ではインタラクション開始しない
                if (isPointerOverUI)
                {
                    _state[btn] = BtnState.Idle;
                    return;
                }
                _state[btn]   = BtnState.Pressed;
                _downPos[btn] = pos;
                OnButtonDown?.Invoke(btn, pos, mods);
                return;
            }

            // --- ButtonUp ---
            if (Input.GetMouseButtonUp(btn))
            {
                if (_state[btn] == BtnState.Idle) return;

                var prevState = _state[btn];
                _state[btn] = BtnState.Idle;

                OnButtonUp?.Invoke(btn, pos, mods);

                if (prevState == BtnState.Pressed)
                    OnClick?.Invoke(btn, pos, mods);
                else if (prevState == BtnState.Dragging)
                    OnDragEnd?.Invoke(btn, pos, mods);
                return;
            }

            // ボタン押しっぱなし中の処理
            if (!Input.GetMouseButton(btn)) return;

            // --- Pressed → Dragging 遷移 ---
            if (_state[btn] == BtnState.Pressed)
            {
                float moved = Vector2.Distance(pos, _downPos[btn]);
                if (moved > DragThreshold)
                {
                    _state[btn]       = BtnState.Dragging;
                    _prevDragPos[btn] = _downPos[btn];
                    OnDragBegin?.Invoke(btn, _downPos[btn], mods);
                }
                // DragThreshold未満は引き続きPressed
                return;
            }

            // --- Dragging フレーム更新 ---
            if (_state[btn] == BtnState.Dragging)
            {
                Vector2 delta     = pos - _prevDragPos[btn];
                _prevDragPos[btn] = pos;
                if (delta.sqrMagnitude > 0f)
                    OnDrag?.Invoke(btn, pos, delta, mods);
            }
        }
    }
}
