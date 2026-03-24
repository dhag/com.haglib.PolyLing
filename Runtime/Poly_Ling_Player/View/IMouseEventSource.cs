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
        event Action<int, Vector2, ModifierKeys> OnDragBegin;
        event Action<int, Vector2, Vector2, ModifierKeys> OnDrag;
        event Action<int, Vector2, ModifierKeys> OnDragEnd;
        event Action<float, ModifierKeys> OnScroll;
        bool IsAnyDragging { get; }
        bool IsDragging(int btn);
    }
}
