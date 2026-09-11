// MeshUndoController_Vertex.cs
// Undo コントローラ：フォーカス切替・エディタ状態・作業平面・頂点ドラッグ・選択変更の記録。
// Runtime/Poly_Ling_Main/Core/Misc/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using static Poly_Ling.UndoSystem.KnifeCutOperationRecord;
using Poly_Ling.Selection;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.UndoSystem
{
    public partial class MeshUndoController
    {
        // === フォーカス管理 ===

        /// <summary>
        /// 頂点編集モードにフォーカス
        /// </summary>
        public void FocusVertexEdit()
        {
            _mainGroup.FocusedChildId = _vertexEditStack.Id;
            _undoManager.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// エディタ状態（カメラ/表示/モード）にフォーカス
        /// </summary>
        public void FocusEditorState()
        {
            _mainGroup.FocusedChildId = _editorStateStack.Id;
            _undoManager.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// 表示モードにフォーカス（後方互換）
        /// </summary>
        public void FocusView()
        {
            FocusEditorState();
        }

        // === エディタ状態の記録（シンプル化） ===

        /// <summary>
        /// エディタ状態ドラッグ開始
        /// </summary>
        public void BeginEditorStateDrag()
        {
            if (_isEditorStateDragging) return;
            _isEditorStateDragging = true;
            _editorStateStartSnapshot = _editorStateContext.Capture();
        }

        /// <summary>
        /// エディタ状態ドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndEditorStateDrag(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging) return;
            _isEditorStateDragging = false;

            EditorStateSnapshot currentSnapshot = _editorStateContext.Capture();
            if (currentSnapshot.IsDifferentFrom(_editorStateStartSnapshot))
            {
                RecordEditorStateChangeInternal(_editorStateStartSnapshot, currentSnapshot, description);
            }
        }

        /// <summary>
        /// エディタ状態変更を記録（内部用・キューから呼ばれる）
        /// </summary>
        internal void RecordEditorStateChangeInternal(
            EditorStateSnapshot before,
            EditorStateSnapshot after,
            string description)
        {
            // パネル設定Undoが無効なら記録スキップ
            if (!_editorStateContext.UndoPanelSettings) return;
            // Debug.Log($"[RecordEditorStateChangeInternal] Recording to EditorStateStack: {description}. Before Focus={_mainGroup.FocusedChildId}");
            
            _editorStateStack.EndGroup();  // 独立した操作として記録
            EditorStateChangeRecord record = new EditorStateChangeRecord(before, after);
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("EditorState", __dbgDesc, record);
                _editorStateStack.Record(record, __dbgDesc);
            }
            FocusEditorState();
            
            // Debug.Log($"[RecordEditorStateChangeInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// エディタ状態を即座に記録（チェックボックス等）
        /// </summary>
        public void RecordEditorStateChange(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging)
            {
                // ドラッグ中でなければ、前回の状態から記録
                BeginEditorStateDrag();
            }
            EndEditorStateDrag(description);
        }

        // === WorkPlaneの記録 ===

        /// <summary>
        /// WorkPlaneにフォーカス
        /// </summary>
        public void FocusWorkPlane()
        {
            _mainGroup.FocusedChildId = _workPlaneStack.Id;
            _undoManager.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// WorkPlaneドラッグ開始
        /// </summary>
        public void BeginWorkPlaneDrag()
        {
            if (_isWorkPlaneDragging) return;
            _isWorkPlaneDragging = true;
            _workPlaneStartSnapshot = _workPlane.CreateSnapshot();
        }

        /// <summary>
        /// WorkPlaneドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndWorkPlaneDrag(string description = "Change WorkPlaneContext")
        {
            if (!_isWorkPlaneDragging) return;
            _isWorkPlaneDragging = false;

            WorkPlaneSnapshot currentSnapshot = _workPlane.CreateSnapshot();
            if (currentSnapshot.IsDifferentFrom(_workPlaneStartSnapshot))
            {
                WorkPlaneChangeRecord record = new WorkPlaneChangeRecord(_workPlaneStartSnapshot, currentSnapshot, description);
                {
                    string __dbgDesc = description;
                    PLDiag.UndoRecord("WorkPlane", __dbgDesc, record);
                    _workPlaneStack.Record(record, __dbgDesc);
                }
                FocusWorkPlane();
            }
        }

        /// <summary>
        /// WorkPlane変更を即座に記録
        /// </summary>
        public void RecordWorkPlaneChange(string description = "Change WorkPlaneContext")
        {
            if (!_isWorkPlaneDragging)
            {
                BeginWorkPlaneDrag();
            }
            EndWorkPlaneDrag(description);
        }

        /// <summary>
        /// WorkPlane変更を記録（スナップショット指定）
        /// </summary>
        public void RecordWorkPlaneChange(WorkPlaneSnapshot before, WorkPlaneSnapshot after, string description = null)
        {
            if (!before.IsDifferentFrom(after)) return;

            var record = new WorkPlaneChangeRecord(before, after, description);
            {
                string __dbgDesc = record.Description;
                PLDiag.UndoRecord("WorkPlane", __dbgDesc, record);
                _workPlaneStack.Record(record, __dbgDesc);
            }
            FocusWorkPlane();
        }

        // === 頂点編集操作の記録 ===

        /// <summary>
        /// 頂点ドラッグ開始
        /// </summary>
        public void BeginVertexDrag(Vector3[] currentPositions)
        {
            BeginVertexDragInternal(currentPositions);
        }

        /// <summary>
        /// 頂点ドラッグ開始（内部用）
        /// </summary>
        internal void BeginVertexDragInternal(Vector3[] currentPositions)
        {
            if (_isDragging) return;

            // Debug.Log($"[BeginVertexDragInternal] Starting vertex drag. Focus={_mainGroup.FocusedChildId}");

            _isDragging = true;
            _lastVertexPositions = (Vector3[])currentPositions.Clone();
            _dragStartGroupId = _vertexEditStack.BeginGroup("Move Vertices");
            FocusVertexEdit();
            
            // Debug.Log($"[BeginVertexDragInternal] After FocusVertexEdit(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// 頂点ドラッグ開始（MeshObjectから自動取得）
        /// </summary>
        public void BeginVertexDrag()
        {
            BeginVertexDragInternal();
        }

        /// <summary>
        /// 頂点ドラッグ開始（MeshObjectから自動取得・内部用）
        /// </summary>
        internal void BeginVertexDragInternal()
        {
            if (_isDragging) return;
            if (_meshContext?.MeshObject == null) return;

            BeginVertexDragInternal(_meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点ドラッグ終了（キュー経由）
        /// </summary>
        public void EndVertexDrag(int[] movedIndices, Vector3[] newPositions)
        {
            EndVertexDragInternal(movedIndices, newPositions);
        }

        /// <summary>
        /// 頂点ドラッグ終了（内部用）
        /// </summary>
        internal void EndVertexDragInternal(int[] movedIndices, Vector3[] newPositions)
        {
            if (!_isDragging) return;

            // Debug.Log($"[EndVertexDragInternal] Ending vertex drag. movedCount={movedIndices?.Length ?? 0}. Focus={_mainGroup.FocusedChildId}");

            _isDragging = false;

            if (movedIndices != null && movedIndices.Length > 0)
            {
                // 移動した頂点の記録を作成
                var oldPositions = new Vector3[movedIndices.Length];
                var newPos = new Vector3[movedIndices.Length];

                for (int i = 0; i < movedIndices.Length; i++)
                {
                    int idx = movedIndices[i];
                    oldPositions[i] = _lastVertexPositions[idx];
                    newPos[i] = newPositions[idx];
                }

                var record = new VertexMoveRecord(movedIndices, oldPositions, newPos);
                {
                    string __dbgDesc = "Move Vertices";
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    _vertexEditStack.Record(record, __dbgDesc);
                }
                FocusVertexEdit();
                
                // Debug.Log($"[EndVertexDragInternal] Recorded vertex move. Focus={_mainGroup.FocusedChildId}");
            }

            _vertexEditStack.EndGroup();
            _lastVertexPositions = null;
        }

        /// <summary>
        /// 頂点ドラッグ終了（MeshObjectから自動取得）
        /// </summary>
        public void EndVertexDrag(int[] movedIndices)
        {
            EndVertexDragInternal(movedIndices);
        }

        /// <summary>
        /// 頂点ドラッグ終了（MeshObjectから自動取得・内部用）
        /// </summary>
        internal void EndVertexDragInternal(int[] movedIndices)
        {
            if (_meshContext?.MeshObject == null) return;
            EndVertexDragInternal(movedIndices, _meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点グループ移動を記録
        /// </summary>
        public void RecordVertexGroupMove(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            RecordVertexGroupMoveInternal(groups, oldOffsets, newOffsets, originalVertices);
        }

        /// <summary>
        /// 頂点グループ移動を記録（内部用）
        /// </summary>
        internal void RecordVertexGroupMoveInternal(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            var record = new VertexGroupMoveRecord(
                groups,
                (Vector3[])oldOffsets.Clone(),
                (Vector3[])newOffsets.Clone(),
                (Vector3[])originalVertices.Clone()
            );
            {
                string __dbgDesc = "Move Vertex Group";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: Vertex/Face HashSet版）
        /// </summary>
        public void RecordSelectionChange(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            RecordSelectionChangeInternal(oldVertices, newVertices, oldFaces, newFaces);
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: 内部用）
        /// </summary>
        internal void RecordSelectionChangeInternal(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(oldVertices, newVertices, oldFaces, newFaces);
            {
                string __dbgDesc = "Change Selection";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: WorkPlane連動）
        /// </summary>
        public void RecordSelectionChangeWithWorkPlane(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            RecordSelectionChangeWithWorkPlaneInternal(
                oldVertices, newVertices, oldWorkPlane, newWorkPlane, oldFaces, newFaces);
        }

        /// <summary>
        /// 選択状態変更を記録（後方互換: WorkPlane連動・内部用）
        /// </summary>
        internal void RecordSelectionChangeWithWorkPlaneInternal(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(
                oldVertices, newVertices,
                oldWorkPlane, newWorkPlane,
                oldFaces, newFaces);
            {
                string __dbgDesc = "Change Selection";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録（SelectionSnapshot版 — 推奨）
        /// </summary>
        public void RecordSelectionChange(
            Poly_Ling.Selection.SelectionSnapshot oldSnapshot,
            Poly_Ling.Selection.SelectionSnapshot newSnapshot,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            RecordSelectionChangeInternal(oldSnapshot, newSnapshot, oldWorkPlane, newWorkPlane);
        }

        /// <summary>
        /// 選択状態変更を記録（SelectionSnapshot版・内部用）
        /// </summary>
        internal void RecordSelectionChangeInternal(
            Poly_Ling.Selection.SelectionSnapshot oldSnapshot,
            Poly_Ling.Selection.SelectionSnapshot newSnapshot,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            _vertexEditStack.EndGroup();
            var record = new SelectionChangeRecord(oldSnapshot, newSnapshot, oldWorkPlane, newWorkPlane);
            string desc = newSnapshot?.Mode.ToString() ?? "Selection";
            {
                string __dbgDesc = $"Change {desc} Selection";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }
    }
}
