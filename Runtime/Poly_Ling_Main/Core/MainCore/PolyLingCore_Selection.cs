// PolyLingCore_Selection.cs
// 選択状態の保存・復元・Undo記録
// 頂点選択操作（SelectAll/Invert/Clear/Delete/Merge）
// 選択セット操作（LoadByName/GetNames）

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore
    {
        // ================================================================
        // 選択のSave/Load（メッシュ切り替え時）
        // ================================================================

        partial void SaveSelectionToCurrentMesh()
        {
            if (_selectionState != null)
                _selectionState.OnSelectionChanged -= OnSelectionChanged;
        }

        partial void LoadSelectionFromCurrentMesh()
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext == null)
            {
                _selectionState = new SelectionState();
            }
            else
            {
                _selectionState = meshContext.Selection;
            }

            _selectionState.OnSelectionChanged += OnSelectionChanged;
            _selectionOps?.SetState(_selectionState);

            OnSelectionStateChanged?.Invoke(_selectionState);

            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.SelectedVertices =
                    new HashSet<int>(_selectionState.Vertices);
        }

        // ================================================================
        // SetSelectedIndex
        // ================================================================

        partial void SetSelectedIndex(int index)
        {
            if (_model == null) return;
            if (index < 0)
                _model.ClearAllCategorySelection();
            else
                _model.Select(index);
            OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // 選択Undo記録
        // ================================================================

        partial void RecordSelectionChange(HashSet<int> oldSel, HashSet<int> newSel)
        {
            if (_undoController == null) return;
            _undoController.RecordSelectionChange(oldSel, newSel);
        }

        // ================================================================
        // LoadMeshContextToUndoController
        // ================================================================

        internal void LoadMeshContextToUndoController(MeshContext meshContext)
        {
            if (_undoController == null || meshContext == null) return;
            _undoController.MeshUndoContext.SelectedVertices =
                new HashSet<int>(_selectionState.Vertices);
        }

        // ================================================================
        // 頂点選択操作
        // ================================================================

        public void SelectAllVertices()
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext?.MeshObject == null) return;

            var oldSelection = new HashSet<int>(_selectionState.Vertices);
            _selectionState.Vertices.Clear();
            for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
                _selectionState.Vertices.Add(i);

            if (!oldSelection.SetEquals(_selectionState.Vertices))
                RecordSelectionChange(oldSelection, _selectionState.Vertices);

            OnRepaintRequired?.Invoke();
        }

        public void InvertSelection()
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext?.MeshObject == null) return;

            var oldSelection = new HashSet<int>(_selectionState.Vertices);
            var newSelection = new HashSet<int>();
            for (int i = 0; i < meshContext.MeshObject.VertexCount; i++)
                if (!_selectionState.Vertices.Contains(i))
                    newSelection.Add(i);

            _selectionState.Vertices.Clear();
            _selectionState.Vertices.UnionWith(newSelection);

            if (!oldSelection.SetEquals(_selectionState.Vertices))
                RecordSelectionChange(oldSelection, _selectionState.Vertices);

            OnRepaintRequired?.Invoke();
        }

        public void ClearSelection()
        {
            if (_selectionState.Vertices.Count == 0) return;

            var oldSelection = new HashSet<int>(_selectionState.Vertices);
            _selectionState.Vertices.Clear();
            RecordSelectionChange(oldSelection, _selectionState.Vertices);

            OnRepaintRequired?.Invoke();
        }

        public void DeleteSelectedVertices()
        {
            if (_selectionState.Vertices.Count == 0) return;
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext?.MeshObject == null) return;

            MeshObjectSnapshot before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

            MeshMergeHelper.DeleteVertices(meshContext.MeshObject,
                new HashSet<int>(_selectionState.Vertices));

            _selectionState.Vertices.Clear();
            _undoController.MeshUndoContext.SelectedVertices.Clear();

            MeshObjectSnapshot after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);
            _undoController.RecordDeleteVertices(before, after);

            OnSyncMeshRequired?.Invoke(meshContext);
            OnRepaintRequired?.Invoke();
        }

        public void MergeSelectedVertices()
        {
            if (_selectionState.Vertices.Count < 2) return;
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext?.MeshObject == null) return;

            MeshObjectSnapshot before = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

            int mergedVertex = MeshMergeHelper.MergeVerticesToCentroid(
                meshContext.MeshObject, new HashSet<int>(_selectionState.Vertices));

            _selectionState.Vertices.Clear();
            if (mergedVertex >= 0)
                _selectionState.Vertices.Add(mergedVertex);
            _undoController.MeshUndoContext.SelectedVertices =
                new HashSet<int>(_selectionState.Vertices);

            MeshObjectSnapshot after = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);
            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                _undoController, before, after, "Merge Vertices"));

            OnSyncMeshRequired?.Invoke(meshContext);
            OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // 選択セット操作
        // ================================================================

        public bool LoadSelectionSetByName(string setName)
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext == null)
            {
                Debug.LogWarning("[SelectionSets] No current mesh context.");
                return false;
            }

            var set = meshContext.FindSelectionSetByName(setName);
            if (set == null)
            {
                Debug.LogWarning($"[SelectionSets] Set not found: {setName}");
                return false;
            }

            var snapshot = new SelectionSnapshot
            {
                Mode = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges = new HashSet<VertexPair>(set.Edges),
                Faces = new HashSet<int>(set.Faces),
                Lines = new HashSet<int>(set.Lines)
            };
            _selectionState.RestoreFromSnapshot(snapshot);

            Debug.Log($"[SelectionSets] Loaded by name: {setName}");
            OnRepaintRequired?.Invoke();
            return true;
        }

        public List<string> GetSelectionSetNames()
        {
            var meshContext = _model?.FirstSelectedMeshContext;
            if (meshContext == null) return new List<string>();
            return meshContext.PartsSelectionSetList.Select(s => s.Name).ToList();
        }
    }
}
