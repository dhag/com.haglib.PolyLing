// Assets/Editor/Poly_Ling/PolyLing/PolyLing_BoneInput.cs
// ボーン入力ハンドラ — PolyLingCoreへの委譲ラッパー
// ロジックは Runtime/Poly_Ling_Main/Core/MainCore/PolyLingCore_BoneInput.cs に移管済み

using UnityEngine;
using Poly_Ling.Tools;

public partial class PolyLing
{
    // ================================================================
    // BoneInput 委譲
    // ================================================================

    private bool HandleBoneInput(Event e, Vector2 mousePos, Rect previewRect,
                                  Vector3 camPos, Vector3 lookAt)
    {
        if (_core == null) return false;
        return _core.HandleBoneInput(
            e.type, e.button, mousePos,
            e.shift, e.control || e.command, e.alt,
            previewRect, camPos, lookAt);
    }

    private void DrawBoneGizmo(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        if (_model == null || !_model.HasBoneSelection) return;
        var ctx = _toolManager?.toolContext;
        if (ctx == null) return;

        _core.UpdateBoneGizmoCenter();

        if (_core.CurrentBoneDragState == Poly_Ling.Core.PolyLingCore.BoneDragState.Idle)
            _core.UpdateBoneGizmoHover(Event.current.mousePosition);

        _core.BoneAxisGizmo.Draw(ctx);
    }
}
