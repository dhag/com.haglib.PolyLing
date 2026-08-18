// IMouseEventSource.cs
// PlayerMouseDispatcher と PlayerViewportPanel の共通インターフェース。
// OrbitCameraController / PlayerVertexInteractor はこのインターフェース経由で接続する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Player
{
    public interface IMouseEventSource
    {
        event Action<int, Vector2, ModifierKeys> OnButtonDown;
        event Action<int, Vector2, ModifierKeys> OnButtonUp;
        event Action<int, Vector2, ModifierKeys> OnClick;
        /// <summary>
        /// 押下中で、まだドラッグしきい値を越えていない間の移動。
        /// (btn, 現在位置, 前回からの差分, 修飾キー)
        ///
        /// しきい値を越えると OnDragBegin / OnDrag へ移る。移動ツールは
        /// 押下時点を原点として、このイベントから即座に追従を開始する。
        /// </summary>
        event Action<int, Vector2, Vector2, ModifierKeys> OnPressMove;

        event Action<int, Vector2, ModifierKeys> OnDragBegin;
        event Action<int, Vector2, Vector2, ModifierKeys> OnDrag;
        event Action<int, Vector2, ModifierKeys> OnDragEnd;
        event Action<float, ModifierKeys> OnScroll;
        bool IsAnyDragging { get; }
        bool IsDragging(int btn);
    }
}
