// Assets/Editor/SimpleMeshFactory.Selection.cs
// 選択ヘルパー（SelectAll, Invert, Delete, Merge, キーボードショートカット）
// Phase 4: MeshMergeHelper使用に変更

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Utilities;

public partial class SimpleMeshFactory
{
    // ================================================================
    // 選択ヘルパー
    // ================================================================
    private void SelectAllVertices()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        _selectedVertices.Clear();
        for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
        {
            _selectedVertices.Add(i);
        }

        if (!oldSelection.SetEquals(_selectedVertices))
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
        Repaint();
    }

    private void InvertSelection()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        var newSelection = new HashSet<int>();
        for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
        {
            if (!_selectedVertices.Contains(i))
            {
                newSelection.Add(i);
            }
        }
        _selectedVertices = newSelection;

        if (!oldSelection.SetEquals(_selectedVertices))
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
        Repaint();
    }

    private void ClearSelection()
    {
        if (_selectedVertices.Count == 0)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);
        _selectedVertices.Clear();
        RecordSelectionChange(oldSelection, _selectedVertices);
        Repaint();
    }

    /// <summary>
    /// 選択中の頂点を削除
    /// </summary>
    private void DeleteSelectedVertices()
    {
        if (_selectedVertices.Count == 0) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null) return;

        // スナップショット取得（操作前）
        var before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // MeshMergeHelper使用
        MeshMergeHelper.DeleteVertices(meshContext.MeshObject, new HashSet<int>(_selectedVertices));

        // 選択クリア
        _selectedVertices.Clear();
        _undoController.MeshUndoContext.SelectedVertices.Clear();

        // スナップショット取得（操作後）
        var after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // Undo記録
        _undoController.RecordDeleteVertices(before, after);

        // メッシュ更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// 選択中の頂点を1つにマージ
    /// </summary>
    private void MergeSelectedVertices()
    {
        if (_selectedVertices.Count < 2) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null) return;

        // スナップショット取得（操作前）
        var before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // MeshMergeHelper使用
        int mergedVertex = MeshMergeHelper.MergeVerticesToCentroid(meshContext.MeshObject, new HashSet<int>(_selectedVertices));

        // 選択を更新（マージ後の1頂点のみ選択）
        _selectedVertices.Clear();
        if (mergedVertex >= 0)
        {
            _selectedVertices.Add(mergedVertex);
        }
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectedVertices);

        // スナップショット取得（操作後）
        var after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // Undo記録
        _undoController.RecordTopologyChange(before, after, "Merge Vertices");

        // メッシュ更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// キーボードショートカット処理
    /// </summary>
    private void HandleKeyboardShortcuts(Event e, MeshContext meshContext)
    {
        if (HandleHitTestDebugShortcuts(e, e.mousePosition))
        {
            return;
        }


        switch (e.keyCode)
        {
            case KeyCode.A:
                // A: 全選択トグル
                if (meshContext.MeshObject != null)
                {
                    if (_selectedVertices.Count == meshContext.MeshObject.VertexCount)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        SelectAllVertices();
                    }
                    e.Use();
                }
                break;

            case KeyCode.Escape:
                // Escape: 選択解除
                ClearSelection();
                ResetEditState();
                e.Use();
                break;

            case KeyCode.Delete:
            case KeyCode.Backspace:
                // Delete/Backspace: 選択頂点を削除
                if (_selectedVertices.Count > 0)
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
        {
            BeginCameraDrag();
        }

        if (e.type == EventType.MouseDrag && e.button == 1)
        {
            if (e.control)
            {
                // Ctrl+右ドラッグ: Z軸回転（ロール）
                _rotationZ += e.delta.x * 0.5f;
            }
            else
            {
                // 通常の右ドラッグ: X/Y軸回転
                // Z回転分だけマウスデルタを逆回転して、画面上のドラッグ方向と一致させる
                float zRad = -_rotationZ * Mathf.Deg2Rad;
                float cos = Mathf.Cos(zRad);
                float sin = Mathf.Sin(zRad);
                float adjustedDeltaX = e.delta.x * cos - e.delta.y * sin;
                float adjustedDeltaY = e.delta.x * sin + e.delta.y * cos;

                _rotationY += adjustedDeltaX * 0.5f;
                _rotationX += adjustedDeltaY * 0.5f;
                _rotationX = Mathf.Clamp(_rotationX, -89f, 89f);
            }
            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseUp && e.button == 1)
        {
            EndCameraDrag();
        }
    }

    /// <summary>
    /// スクリーン位置から頂点を検索（MMeshObjectベース）
    /// </summary>
    private int FindVertexAtScreenPos(Vector2 screenPos, MeshObject meshObject, Rect previewRect, Vector3 camPos, Vector3 lookAt, float radius)
    {
        if (meshObject == null)
            return -1;

        int closestVertex = -1;
        float closestDist = radius;

        for (int i = 0; i < meshObject.VertexCount; i++)
        {
            Vector2 vertScreenPos = WorldToPreviewPos(meshObject.Vertices[i].Position, previewRect, camPos, lookAt);
            float dist = Vector2.Distance(screenPos, vertScreenPos);

            if (dist < closestDist)
            {
                closestDist = dist;
                closestVertex = i;
            }
        }

        return closestVertex;
    }

    private Vector3 ScreenDeltaToWorldDelta(Vector2 screenDelta, Vector3 camPos, Vector3 lookAt, float camDist, Rect previewRect)
    {
        // カメラの向きを計算（Z軸ロール対応）
        Vector3 forward = (lookAt - camPos).normalized;
        Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion rollRot = Quaternion.AngleAxis(_rotationZ, Vector3.forward);
        Quaternion camRot = lookRot * rollRot;

        Vector3 right = camRot * Vector3.right;
        Vector3 up = camRot * Vector3.up;

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad;
        float worldHeightAtDist = 2f * camDist * Mathf.Tan(fovRad / 2f);
        float pixelToWorld = worldHeightAtDist / previewRect.height;

        Vector3 worldDelta = right * screenDelta.x * pixelToWorld
                           - up * screenDelta.y * pixelToWorld;

        return worldDelta;
    }

}
