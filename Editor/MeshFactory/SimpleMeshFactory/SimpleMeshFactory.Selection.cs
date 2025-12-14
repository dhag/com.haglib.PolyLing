// Assets/Editor/SimpleMeshFactory.Selection.cs
// 選択ヘルパー（SelectAll, Invert, Delete, Merge, キーボードショートカット）

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

public partial class SimpleMeshFactory
{
    // ================================================================
    // 選択ヘルパー
    // ================================================================
    private void SelectAllVertices()
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.Data == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        _selectedVertices.Clear();
        for (int i = 0; i < meshContext.Data.VertexCount; i++)
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
        if (meshContext?.Data == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        var newSelection = new HashSet<int>();
        for (int i = 0; i < meshContext.Data.VertexCount; i++)
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
        if (meshContext?.Data == null) return;

        // スナップショット取得（操作前）
        var before = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // 削除処理
        ExecuteDeleteVertices(meshContext.Data, new HashSet<int>(_selectedVertices));

        // 選択クリア
        _selectedVertices.Clear();
        _undoController.MeshContext.SelectedVertices.Clear();

        // スナップショット取得（操作後）
        var after = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // Undo記録
        _undoController.RecordDeleteVertices(before, after);

        // メッシュ更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// 頂点削除の実行
    /// </summary>
    private void ExecuteDeleteVertices(MeshData meshData, HashSet<int> verticesToDelete)
    {
        int originalCount = meshData.VertexCount;
        if (originalCount == 0) return;

        // 1. 新しいインデックスへのマッピングを作成
        // oldIndex -> newIndex (-1 if deleted)
        var indexMap = new int[originalCount];
        int newIndex = 0;
        for (int i = 0; i < originalCount; i++)
        {
            if (verticesToDelete.Contains(i))
            {
                indexMap[i] = -1; // 削除される
            }
            else
            {
                indexMap[i] = newIndex++;
            }
        }

        // 2. 面を処理（インデックス更新＆無効な面の削除）
        for (int f = meshData.FaceCount - 1; f >= 0; f--)
        {
            var face = meshData.Faces[f];
            var newVertexIndices = new List<int>();
            var newUVIndices = new List<int>();
            var newNormalIndices = new List<int>();

            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                int oldIdx = face.VertexIndices[i];
                if (oldIdx >= 0 && oldIdx < originalCount)
                {
                    int mappedIdx = indexMap[oldIdx];
                    if (mappedIdx >= 0)
                    {
                        newVertexIndices.Add(mappedIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }
            }

            if (newVertexIndices.Count < 3)
            {
                // 頂点数が3未満なら面を削除
                meshData.Faces.RemoveAt(f);
            }
            else
            {
                // 面を更新
                face.VertexIndices = newVertexIndices;
                face.UVIndices = newUVIndices;
                face.NormalIndices = newNormalIndices;
            }
        }

        // 3. 頂点を削除（降順で）
        var sortedIndices = verticesToDelete.OrderByDescending(i => i).ToList();
        foreach (var idx in sortedIndices)
        {
            if (idx >= 0 && idx < meshData.VertexCount)
            {
                meshData.Vertices.RemoveAt(idx);
            }
        }
    }

    /// <summary>
    /// 選択中の頂点を1つにマージ
    /// </summary>
    private void MergeSelectedVertices()
    {
        if (_selectedVertices.Count < 2) return;
        var meshContext = _model.CurrentMeshContext;
        if (meshContext?.Data == null) return;

        // スナップショット取得（操作前）
        var before = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // マージ処理
        int mergedVertex = ExecuteMergeVertices(meshContext.Data, new HashSet<int>(_selectedVertices));

        // 選択を更新（マージ後の1頂点のみ選択）
        _selectedVertices.Clear();
        if (mergedVertex >= 0)
        {
            _selectedVertices.Add(mergedVertex);
        }
        _undoController.MeshContext.SelectedVertices = new HashSet<int>(_selectedVertices);

        // スナップショット取得（操作後）
        var after = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // Undo記録
        _undoController.RecordTopologyChange(before, after, "Merge Vertices");

        // メッシュ更新
        SyncMeshFromData(meshContext);
        Repaint();
    }

    /// <summary>
    /// 頂点マージの実行
    /// </summary>
    /// <returns>マージ後の頂点インデックス</returns>
    private int ExecuteMergeVertices(MeshData meshData, HashSet<int> verticesToMerge)
    {
        if (verticesToMerge.Count < 2) return -1;
        int originalCount = meshData.VertexCount;
        if (originalCount == 0) return -1;

        // 1. マージ先の頂点を決定（重心を計算）
        Vector3 centroid = Vector3.zero;
        foreach (int idx in verticesToMerge)
        {
            if (idx >= 0 && idx < originalCount)
            {
                centroid += meshData.Vertices[idx].Position;
            }
        }
        centroid /= verticesToMerge.Count;

        // 最小インデックスの頂点をマージ先とし、位置を重心に更新
        int targetVertex = verticesToMerge.Min();
        meshData.Vertices[targetVertex].Position = centroid;

        // 2. インデックスマッピングを作成
        // targetVertexは残す、他のマージ対象は削除
        var indexMap = new int[originalCount];
        int newIndex = 0;

        for (int i = 0; i < originalCount; i++)
        {
            if (verticesToMerge.Contains(i) && i != targetVertex)
            {
                // マージ対象（targetVertex以外）は削除される
                indexMap[i] = -2; // 後でtargetの新インデックスに更新
            }
            else
            {
                // targetVertexを含む、残る頂点
                indexMap[i] = newIndex++;
            }
        }

        // targetVertexの新インデックスを取得
        int targetNewIndex = indexMap[targetVertex];

        // マージ対象のインデックスをtargetNewIndexに更新
        for (int i = 0; i < originalCount; i++)
        {
            if (indexMap[i] == -2)
            {
                indexMap[i] = targetNewIndex;
            }
        }

        // 3. 面を処理
        for (int f = meshData.FaceCount - 1; f >= 0; f--)
        {
            var face = meshData.Faces[f];
            var newVertexIndices = new List<int>();
            var newUVIndices = new List<int>();
            var newNormalIndices = new List<int>();

            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                int oldIdx = face.VertexIndices[i];
                if (oldIdx >= 0 && oldIdx < originalCount)
                {
                    int mappedIdx = indexMap[oldIdx];

                    // 連続する同じ頂点を避ける（縮退した辺を防ぐ）
                    if (newVertexIndices.Count == 0 || newVertexIndices[newVertexIndices.Count - 1] != mappedIdx)
                    {
                        newVertexIndices.Add(mappedIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }
            }

            // 最初と最後が同じなら最後を除去
            if (newVertexIndices.Count > 1 && newVertexIndices[0] == newVertexIndices[newVertexIndices.Count - 1])
            {
                newVertexIndices.RemoveAt(newVertexIndices.Count - 1);
                if (newUVIndices.Count > 0) newUVIndices.RemoveAt(newUVIndices.Count - 1);
                if (newNormalIndices.Count > 0) newNormalIndices.RemoveAt(newNormalIndices.Count - 1);
            }

            if (newVertexIndices.Count < 3)
            {
                // 頂点数が3未満なら面を削除
                meshData.Faces.RemoveAt(f);
            }
            else
            {
                // 面を更新
                face.VertexIndices = newVertexIndices;
                face.UVIndices = newUVIndices;
                face.NormalIndices = newNormalIndices;
            }
        }

        // 4. 頂点を削除（マージ先以外、降順で）
        var verticesToRemove = verticesToMerge.Where(i => i != targetVertex).OrderByDescending(i => i).ToList();
        foreach (var idx in verticesToRemove)
        {
            if (idx >= 0 && idx < meshData.VertexCount)
            {
                meshData.Vertices.RemoveAt(idx);
            }
        }

        return targetNewIndex;
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
                if (meshContext.Data != null)
                {
                    if (_selectedVertices.Count == meshContext.Data.VertexCount)
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
    /// スクリーン位置から頂点を検索（MeshDataベース）
    /// </summary>
    private int FindVertexAtScreenPos(Vector2 screenPos, MeshData meshData, Rect previewRect, Vector3 camPos, Vector3 lookAt, float radius)
    {
        if (meshData == null)
            return -1;

        int closestVertex = -1;
        float closestDist = radius;

        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector2 vertScreenPos = WorldToPreviewPos(meshData.Vertices[i].Position, previewRect, camPos, lookAt);
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