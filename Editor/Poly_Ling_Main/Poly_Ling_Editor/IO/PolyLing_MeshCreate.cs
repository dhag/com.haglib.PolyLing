// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_MeshCreate.cs
// メッシュ作成・削除機能
// - メッシュ作成コールバック
// - 空メッシュ作成
// - メッシュ削除
// - 頂点オフセット初期化

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Utilities;

public partial class PolyLing
{
    // ================================================================
    // メッシュ作成コールバック
    // ================================================================

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（MeshObject版 - 四角形を保持）
    /// </summary>
    private void OnMeshObjectCreated(MeshObject meshObject, string name)
    {
        // 追加モードかつ有効なメッシュが選択されている場合
        if (_addToCurrentMesh && _model.HasValidMeshContextSelection)
        {
            AddMeshObjectToCurrent(meshObject, name);
        }
        else
        {
            CreateNewMeshContext(meshObject, name);
        }
    }

    /// <summary>
    /// 新しいメッシュコンテキストを作成
    /// </summary>
    private void CreateNewMeshContext(MeshObject meshObject, string name)
    {
        var meshContext = new MeshContext
        {
            Name = name,
            MeshObject = meshObject.Clone(),
            OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            _model.SetMaterials(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.MaterialCount - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    face.MaterialIndex = _model.CurrentMaterialIndex;
                }
            }
        }

        // 自動マージ（全頂点対象）- MeshMergeHelper使用
        if (_autoMergeOnCreate && meshContext.MeshObject.VertexCount >= 2)
        {
            var result = MeshMergeHelper.MergeAllVerticesAtSamePosition(meshContext.MeshObject, _autoMergeThreshold);
            if (result.RemovedVertexCount > 0)
            {
                Debug.Log($"[CreateNewMeshContext] Auto-merged {result.RemovedVertexCount} vertices");
            }
            // OriginalPositionsを更新
            meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();
        }

        // MeshObjectから表示用Unity Meshを生成（MaterialIndex適用後）
        Mesh mesh = meshContext.MeshObject.ToUnityMesh();
        mesh.name = name;
        mesh.hideFlags = HideFlags.HideAndDontSave;
        meshContext.UnityMesh = mesh;

        Debug.Log($"[CreateNewMeshContext] name={name}, vertices={meshContext.MeshObject.VertexCount}, faces={meshContext.MeshObject.FaceCount}");

        // Undo記録用に変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        // リストに追加
        _meshContextList.Add(meshContext);
        meshContext.MaterialOwner = _model;  // Phase 1: Materials 委譲用
        SetSelectedIndex(_meshContextList.Count - 1);
        InitVertexOffsets();

        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        // MeshContextに必要な情報だけを設定
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();

            // Undo記録（メッシュコンテキスト追加）
            _undoController.MeshListContext.Select(_selectedIndex);
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndex, _selectedIndex);
        }

        // 統合システムにトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();

        Repaint();
    }

    /// <summary>
    /// 現在選択中のメッシュにMeshObjectを追加
    /// </summary>
    private void AddMeshObjectToCurrent(MeshObject meshObject, string name)
    {
        var meshContext = _meshContextList[_selectedIndex];
        if (meshContext.MeshObject == null)
        {
            meshContext.MeshObject = new MeshObject(meshContext.Name);
        }

        // ================================================================
        // Undo: 開始時スナップショット（ツール標準方式）
        // ================================================================
        MeshObjectSnapshot snapshotBefore = null;
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            snapshotBefore = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);
        }

        // 追加前の頂点数を記録
        int baseVertexIndex = meshContext.MeshObject.VertexCount;

        // 頂点を追加
        foreach (var vertex in meshObject.Vertices)
        {
            meshContext.MeshObject.Vertices.Add(new Vertex(vertex.Position));
        }

        // 面を追加（頂点インデックスをオフセット）
        int materialIndex = _model.CurrentMaterialIndex;
        foreach (var face in meshObject.Faces)
        {
            var newFace = new Face();
            newFace.VertexIndices = face.VertexIndices.Select(i => i + baseVertexIndex).ToList();
            newFace.UVIndices = new List<int>(face.UVIndices);
            newFace.NormalIndices = new List<int>(face.NormalIndices);
            newFace.MaterialIndex = materialIndex;  // 現在のマテリアルを適用
            meshContext.MeshObject.Faces.Add(newFace);
        }

        // 自動マージ（追加した頂点と既存頂点の境界をマージ）- MeshMergeHelper使用
        if (_autoMergeOnCreate && meshContext.MeshObject.VertexCount >= 2)
        {
            var allVertices = new HashSet<int>(Enumerable.Range(0, meshContext.MeshObject.VertexCount));
            var result = MeshMergeHelper.MergeVerticesAtSamePosition(meshContext.MeshObject, allVertices, _autoMergeThreshold);

            if (result.RemovedVertexCount > 0)
            {
                Debug.Log($"[AddMeshObjectToCurrent] Auto-merged {result.RemovedVertexCount} vertices at boundaries");
            }
        }

        // OriginalPositionsを更新
        meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();

        // メッシュ更新
        SyncMeshFromData(meshContext);

        // ================================================================
        // Undo: 終了時スナップショット + 記録（ツール標準方式）
        // ================================================================
        if (_undoController != null && snapshotBefore != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            MeshObjectSnapshot snapshotAfter = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

            // 直接VertexEditStackに記録（RecordTopologyChangeのEndGroup副作用を回避）
            MeshSnapshotRecord record = new MeshSnapshotRecord(snapshotBefore, snapshotAfter);
            _undoController.VertexEditStack.Record(record, $"Merge: {name}");
            _undoController.FocusVertexEdit();
        }

        // 選択更新（カメラは変更しない）
        InitVertexOffsets(updateCamera: false);

        // 注意: LoadMeshContextToUndoControllerは呼ばない
        // SetMeshObject内で_vertexEditStack.Clear()が呼ばれるため、Undo記録が消えてしまう
        // MeshContextは既に上で設定済み、追加で必要な設定のみ行う
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectedVertices);
        }

        Debug.Log($"[AddMeshObjectToCurrent] Added {name} to {meshContext.Name}, total vertices={meshContext.MeshObject.VertexCount}, faces={meshContext.MeshObject.FaceCount}");

        // 統合システムにトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();

        Repaint();
    }

    // ================================================================
    // 空メッシュ作成
    // ================================================================

    /// <summary>
    /// 空のメッシュを作成
    /// </summary>
    private void CreateEmptyMesh()
    {
        // 空メッシュは常に新規作成（追加モードでも）
        bool wasAddMode = _addToCurrentMesh;
        _addToCurrentMesh = false;

        var meshObject = new MeshObject("Empty");
        OnMeshObjectCreated(meshObject, "Empty");

        _addToCurrentMesh = wasAddMode;
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（従来版）
    /// </summary>
    private void OnMeshCreated(Mesh mesh, string name)
    {
        // Unity MeshからMeshObjectに変換
        var meshObject = new MeshObject(name);
        meshObject.FromUnityMesh(mesh, true);

        // エディタ専用の一時メッシュとしてマーク
        mesh.hideFlags = HideFlags.HideAndDontSave;

        // 元のMeshはそのまま表示用に使用
        var meshContext = new MeshContext
        {
            Name = name,
            UnityMesh = mesh,
            MeshObject = meshObject,
            OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            _model.SetMaterials(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.MaterialCount - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    face.MaterialIndex = _model.CurrentMaterialIndex;
                }
                // Meshを再生成してサブメッシュを反映
                var newMesh = meshContext.MeshObject.ToUnityMesh();
                newMesh.name = name;
                newMesh.hideFlags = HideFlags.HideAndDontSave;
                if (meshContext.UnityMesh != null) DestroyImmediate(meshContext.UnityMesh);
                meshContext.UnityMesh = newMesh;
            }
        }

        // Undo記録用に変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        _meshContextList.Add(meshContext);
        meshContext.MaterialOwner = _model;  // Phase 1: Materials 委譲用
        SetSelectedIndex(_meshContextList.Count - 1);
        InitVertexOffsets();

        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        // MeshContextに必要な情報だけを設定
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();

            // Undo記録（メッシュリスト追加）
            _undoController.MeshListContext.Select(_selectedIndex);
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndex, _selectedIndex);
        }

        // 統合システムにトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();

        Repaint();
    }

    // ================================================================
    // メッシュ削除
    // ================================================================

    private void RemoveMesh(int index)
    {
        if (index < 0 || index >= _meshContextList.Count)
            return;

        var meshContext = _meshContextList[index];

        // Undo記録用にスナップショットを削除前に保存
        int oldSelectedIndex = _selectedIndex;
        MeshContextSnapshot snapshot = null;
        if (_undoController != null)
        {
            snapshot = MeshContextSnapshot.Capture(meshContext);
        }

        // Meshの破棄
        if (meshContext.UnityMesh != null)
        {
            DestroyImmediate(meshContext.UnityMesh);
        }

        _meshContextList.RemoveAt(index);

        // 頂点選択と編集状態をリセット
        _selectedVertices.Clear();
        ResetEditState();

        if (_selectedIndex >= _meshContextList.Count)
        {
            SetSelectedIndex(_meshContextList.Count - 1);
        }

        if (_selectedIndex >= 0)
        {
            InitVertexOffsets();
            var newMeshContext = _meshContextList[_selectedIndex];

            // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
            // MeshContextに必要な情報だけを設定
            if (_undoController != null)
            {
                _undoController.MeshUndoContext.MeshObject = newMeshContext.MeshObject;
                _undoController.MeshUndoContext.TargetMesh = newMeshContext.UnityMesh;
                _undoController.MeshUndoContext.OriginalPositions = newMeshContext.OriginalPositions;
                _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();
                // Materials は ModelContext に集約済み
            }
        }
        else
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            // メッシュがなくなったときだけClear
            _undoController?.VertexEditStack.Clear();
        }

        // Undo記録（メッシュリスト削除）
        if (_undoController != null && snapshot != null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = new List<(int, MeshContextSnapshot)> { (index, snapshot) },
                OldSelectedIndex = oldSelectedIndex,
                NewSelectedIndex = _selectedIndex
            };
            _undoController.MeshListStack.Record(record, $"Remove UnityMesh: {meshContext.Name}");
            _undoController.FocusMeshList();
            _undoController.MeshListContext.Select(_selectedIndex);
        }

        Repaint();
    }

    // ================================================================
    // 頂点オフセット初期化
    // ================================================================

    /// <summary>
    /// 頂点オフセット初期化（MeshObjectベース）
    /// </summary>
    /// <param name="updateCamera">trueの場合、AutoZoomが有効ならカメラをメッシュに合わせて調整する</param>
    private void InitVertexOffsets(bool updateCamera = true)
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        var meshObject = meshContext.MeshObject;

        if (meshObject == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        // MeshObjectのVertex数でオフセット配列を作成
        int vertexCount = meshObject.VertexCount;
        _vertexOffsets = new Vector3[vertexCount];
        _groupOffsets = new Vector3[vertexCount];  // Vertexと1:1

        // カメラ設定（オプション、頂点がある場合のみ、AutoZoomが有効な場合のみ）
        bool autoZoomEnabled = _undoController?.EditorState?.AutoZoomEnabled ?? false;
        if (updateCamera && autoZoomEnabled && vertexCount > 0)
        {
            var bounds = meshObject.CalculateBounds();
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            _cameraDistance = radius * 3.5f;
            _cameraTarget = bounds.center;
        }
    }
}
