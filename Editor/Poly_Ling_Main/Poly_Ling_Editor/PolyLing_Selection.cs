// Assets/Editor/PolyLing.Selection.cs
// 選択ヘルパー — PolyLingCoreへの委譲ラッパー
// ロジックは Runtime/Poly_Ling_Main/Core/MainCore/PolyLingCore_Selection.cs に移管済み

using UnityEngine;
using Poly_Ling.Data;

public partial class PolyLing
{
    private void ClearSelectionWithMeshContext()
    {
        _selectionState?.ClearAll();
    }

    private void SelectAllVertices()        => _core.SelectAllVertices();
    private void InvertSelection()          => _core.InvertSelection();
    private void ClearSelection()           => _core.ClearSelection();
    private void DeleteSelectedVertices()   => _core.DeleteSelectedVertices();
    private void MergeSelectedVertices()    => _core.MergeSelectedVertices();

    private void HandleKeyboardShortcuts(Event e, MeshContext meshContext)
    {
        switch (e.keyCode)
        {
            case KeyCode.A:
                if (meshContext.MeshObject != null)
                {
                    if (_selectionState.Vertices.Count == meshContext.MeshObject.VertexCount)
                        ClearSelection();
                    else
                        SelectAllVertices();
                    e.Use();
                }
                break;

            case KeyCode.Escape:
                ClearSelection();
                ResetEditState();
                e.Use();
                break;

            case KeyCode.Delete:
            case KeyCode.Backspace:
                if (_selectionState.Vertices.Count > 0)
                {
                    DeleteSelectedVertices();
                    e.Use();
                }
                break;
        }
    }

    private void HandleCameraRotation(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 1)
            BeginCameraDrag();

        if (e.type == EventType.MouseDrag && e.button == 1)
        {
            if (e.control)
            {
                _rotationZ += _mouseSettings.GetRotationDelta(e.delta.x);
            }
            else
            {
                float zRad = -_rotationZ * Mathf.Deg2Rad;
                float cos  = Mathf.Cos(zRad);
                float sin  = Mathf.Sin(zRad);
                float adjustedDeltaX = e.delta.x * cos - e.delta.y * sin;
                float adjustedDeltaY = e.delta.x * sin + e.delta.y * cos;

                _rotationY += _mouseSettings.GetRotationDelta(adjustedDeltaX, e);
                _rotationX += _mouseSettings.GetRotationDelta(adjustedDeltaY, e);
                _rotationX  = Mathf.Clamp(_rotationX, -89f, 89f);
            }
            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseUp && e.button == 1)
            EndCameraDrag();
    }

    private int FindVertexAtScreenPos(Vector2 screenPos, MeshObject meshObject,
                                       Rect previewRect, Vector3 camPos, Vector3 lookAt, float radius)
    {
        if (meshObject == null) return -1;

        int   closestVertex = -1;
        float closestDist   = radius;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector2 vertScreenPos = WorldToPreviewPos(meshObject.Vertices[i].Position,
                                                      previewRect, camPos, lookAt);
            float dist = Vector2.Distance(screenPos, vertScreenPos);
            if (dist < closestDist)
            {
                closestDist   = dist;
                closestVertex = i;
            }
        }
        return closestVertex;
    }

    private Vector3 ScreenDeltaToWorldDelta(Vector2 screenDelta, Vector3 camPos, Vector3 lookAt,
                                             float camDist, Rect previewRect)
    {
        Vector3    forward = (lookAt - camPos).normalized;
        Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        Quaternion camRot  = lookRot * rollRot;

        Vector3 right = camRot * Vector3.right;
        Vector3 up    = camRot * Vector3.up;

        float fovRad           = CameraFOV * Mathf.Deg2Rad;
        float worldHeightAtDist = 2f * camDist * Mathf.Tan(fovRad / 2f);
        float pixelToWorld     = worldHeightAtDist / previewRect.height;

        return right * screenDelta.x * pixelToWorld
             - up    * screenDelta.y * pixelToWorld;
    }
}
