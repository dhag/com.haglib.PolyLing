// Assets/Editor/PolyLing.Selection.cs
// 選択ヘルパー（SelectAll, Invert, Delete, Merge, キーボードショートカット）
// Phase 4: MeshMergeHelper使用に変更
// Phase 5: メッシュ切り替え時の選択保存/復元を追加

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Utilities;
public partial class PolyLing
{
    // ================================================================
    // メッシュ切り替え時の選択参照更新
    // _selectionState は meshContext.Selection と同一インスタンスのため、
    // データコピーは不要。参照の差し替えと依存オブジェクトの更新のみ。
    // ================================================================

    /// <summary>
    /// メッシュ切り替え前: 旧メッシュの選択イベントを解除
    /// </summary>
    private void SaveSelectionToCurrentMesh()
    {
        // _selectionState == meshContext.Selection なのでデータコピー不要
        // イベント解除のみ
        if (_selectionState != null)
        {
            _selectionState.OnSelectionChanged -= OnSelectionChanged;
        }
    }

    /// <summary>
    /// メッシュ切り替え後: 新メッシュの Selection に参照を差し替え
    /// </summary>
    private void LoadSelectionFromCurrentMesh()
    {
        var meshContext = _model?.CurrentMeshContext;
        if (meshContext == null)
        {
            // メッシュがない場合はfallback
            _selectionState = new Poly_Ling.Selection.SelectionState();
        }
        else
        {
            _selectionState = meshContext.Selection;
        }

        // 依存オブジェクトの参照を更新
        _selectionState.OnSelectionChanged += OnSelectionChanged;
        _selectionOps?.SetState(_selectionState);

        // レンダラにも通知
        _unifiedAdapter?.SetSelectionState(_selectionState);

        // UndoContextにも同期
        if (_undoController?.MeshUndoContext != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectionState.Vertices);
        }
    }

    /// <summary>
    /// カレント選択をクリアして現在のMeshContextにも反映
    /// </summary>
    private void ClearSelectionWithMeshContext()
    {
        // _selectionState == meshContext.Selection なので ClearAll で両方クリア
        _selectionState?.ClearAll();
    }

    // ================================================================
    // 選択ヘルパー
    // ================================================================
    private void SelectAllVertices()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null)
            return;

        var oldSelection = new HashSet<int>(_selectionState.Vertices);

        _selectionState.Vertices.Clear();
        for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
        {
            _selectionState.Vertices.Add(i);
        }

        if (!oldSelection.SetEquals(_selectionState.Vertices))
        {
            RecordSelectionChange(oldSelection, _selectionState.Vertices);
        }
        
        // 選択変更をワンショットパイプラインに通知（PrepareUnifiedDrawingで処理）
        _unifiedAdapter?.RequestNormal();
        
        Repaint();
    }

    private void InvertSelection()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null)
            return;

        var oldSelection = new HashSet<int>(_selectionState.Vertices);

        var newSelection = new HashSet<int>();
        for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
        {
            if (!_selectionState.Vertices.Contains(i))
            {
                newSelection.Add(i);
            }
        }
        _selectionState.Vertices.Clear();
        _selectionState.Vertices.UnionWith(newSelection);

        if (!oldSelection.SetEquals(_selectionState.Vertices))
        {
            RecordSelectionChange(oldSelection, _selectionState.Vertices);
        }
        
        // 選択変更をワンショットパイプラインに通知（PrepareUnifiedDrawingで処理）
        _unifiedAdapter?.RequestNormal();
        
        Repaint();
    }

    private void ClearSelection()
    {
        if (_selectionState.Vertices.Count == 0)
            return;

        var oldSelection = new HashSet<int>(_selectionState.Vertices);
        _selectionState.Vertices.Clear();
        RecordSelectionChange(oldSelection, _selectionState.Vertices);
        
        // 選択変更をワンショットパイプラインに通知（PrepareUnifiedDrawingで処理）
        _unifiedAdapter?.RequestNormal();
        
        Repaint();
    }

    /// <summary>
    /// 選択中の頂点を削除
    /// </summary>
    private void DeleteSelectedVertices()
    {
        if (_selectionState.Vertices.Count == 0) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null) return;

        // スナップショット取得（操作前）
        MeshObjectSnapshot before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // MeshMergeHelper使用
        MeshMergeHelper.DeleteVertices(meshContext.MeshObject, new HashSet<int>(_selectionState.Vertices));

        // 選択クリア
        _selectionState.Vertices.Clear();
        _undoController.MeshUndoContext.SelectedVertices.Clear();

        // スナップショット取得（操作後）
        MeshObjectSnapshot after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

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
        if (_selectionState.Vertices.Count < 2) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.MeshObject == null) return;

        // スナップショット取得（操作前）
        MeshObjectSnapshot before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // MeshMergeHelper使用
        int mergedVertex = MeshMergeHelper.MergeVerticesToCentroid(meshContext.MeshObject, new HashSet<int>(_selectionState.Vertices));

        // 選択を更新（マージ後の1頂点のみ選択）
        _selectionState.Vertices.Clear();
        if (mergedVertex >= 0)
        {
            _selectionState.Vertices.Add(mergedVertex);
        }
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectionState.Vertices);

        // スナップショット取得（操作後）
        MeshObjectSnapshot  after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

        // Undo記録（コマンドキュー経由）
        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
            _undoController, before, after, "Merge Vertices"));

        // メッシュ更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// キーボードショートカット処理
    /// </summary>
    private void HandleKeyboardShortcuts(Event e, MeshContext meshContext)
    {



        switch (e.keyCode)
        {
            case KeyCode.A:
                // A: 全選択トグル
                if (meshContext.MeshObject != null)
                {
                    if (_selectionState.Vertices.Count == meshContext.MeshObject.VertexCount)
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
        {
            BeginCameraDrag();
        }

        if (e.type == EventType.MouseDrag && e.button == 1)
        {
            if (e.control)
            {
                // Ctrl+右ドラッグ: Z軸回転（ロール）
                // Note: Ctrlはゆっくり移動なので、Z回転には修飾キー倍率を適用しない
                _rotationZ += _mouseSettings.GetRotationDelta(e.delta.x);
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

                _rotationY += _mouseSettings.GetRotationDelta(adjustedDeltaX, e);
                _rotationX += _mouseSettings.GetRotationDelta(adjustedDeltaY, e);
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

        // デバッグ出力
        //Debug.Log($"[ScreenDeltaToWorldDelta] screenDelta={screenDelta}, forward={forward}, right={right}, up={up}, " +
        //          $"pixelToWorld={pixelToWorld}, worldDelta={worldDelta}");

        return worldDelta;
    }

}
