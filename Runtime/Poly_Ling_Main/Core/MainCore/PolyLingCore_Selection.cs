// PolyLingCore_Selection.cs
// 選択状態の保存・復元・Undo記録
// PolyLing_Selection.cs / PolyLing_Input.cs から移植

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

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

            // ViewportへのSetSelectionState通知はイベント経由
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
            // ViewportへのRequestNormalはEditorイベント経由
            OnRepaintRequired?.Invoke();
        }

        // Viewportに新しいSelectionStateを通知するイベント
        public event System.Action<SelectionState> OnSelectionStateChanged;

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
    }
}
