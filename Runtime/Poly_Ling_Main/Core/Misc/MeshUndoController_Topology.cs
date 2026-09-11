// MeshUndoController_Topology.cs
// Undo コントローラ：スナップショット・トポロジー変更・面／頂点の追加削除・ビュー変更の記録。
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
        // === スナップショット（トポロジー変更用） ===

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（新形式・後方互換）
        /// 
        /// 【注意】この版ではEdge/Line選択は保存されない
        /// Edge/Line選択も保存したい場合はselectionState付きの版を使用
        /// </summary>
        public MeshObjectSnapshot CaptureMeshObjectSnapshot()
        {
            return MeshObjectSnapshot.Capture(_meshContext);
        }

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（拡張選択対応版オーバーロード）
        /// 
        /// </summary>
        /// <param name="selectionState">
        /// 拡張選択状態（Edge/Line含む）。
        /// 
        /// 【重要】トポロジー変更ツールは必ずこれを渡すこと！
        /// これにより、ベベルや押し出しのUndoで元の選択も復元される。
        /// </param>
        /// <returns>スナップショット</returns>
        public MeshObjectSnapshot CaptureMeshObjectSnapshot(SelectionState selectionState)
        {
            return MeshObjectSnapshot.Capture(_meshContext, selectionState);
        }

        /// <summary>
        /// トポロジー変更を記録（新形式・後方互換）
        /// 
        /// 【注意】この版ではEdge/Line選択はUndo時に復元されない
        /// Edge/Line選択も復元したい場合はselectionState付きの版を使用
        /// </summary>
        public void RecordTopologyChange(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            string description = "Topology Change")
        {
            RecordTopologyChangeInternal(before, after, description);
        }

        /// <summary>
        /// トポロジー変更を記録（内部用）
        /// </summary>
        internal void RecordTopologyChangeInternal(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }


        /// <summary>
        /// トポロジー変更を記録（拡張選択対応版）
        /// 
        /// 【フェーズ1追加】
        /// </summary>
        /// <param name="before">変更前スナップショット（Capture()で取得）</param>
        /// <param name="after">変更後スナップショット（Capture()で取得）</param>
        /// <param name="selectionState">
        /// 拡張選択状態への参照。
        /// 
        /// 【重要】
        /// Edge/Line選択のUndo/Redoに必要。
        /// トポロジー変更ツール（ベベル、押し出し等）は必ずこれを渡すこと！
        /// 
        /// これにより：
        /// - ベベルUndo時 → メッシュが戻り、元のEdge選択も復元
        /// - 押し出しUndo時 → メッシュが戻り、元のFace選択も復元
        /// 
        /// nullの場合は従来動作（Edge/Line選択は復元されない）
        /// </param>
        /// <param name="description">Undo履歴に表示される説明</param>
        public void RecordTopologyChange(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState,
            string description = "Topology Change")
        {
            RecordTopologyChangeInternal(before, after, selectionState, description);
        }

        /// <summary>
        /// トポロジー変更を記録（拡張選択対応版・内部用）
        /// </summary>
        internal void RecordTopologyChangeInternal(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState,
            string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            // selectionStateを渡すことでEdge/Line選択もUndo対象に
            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after, selectionState);
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }
 



        /// <summary>
        /// トポロジー変更を記録（後方互換）
        /// </summary>
        public void RecordTopologyChange(MeshSnapshot before, MeshSnapshot after, string description = "Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            // 旧形式のスナップショットを新形式に変換して記録
            MeshObjectSnapshot beforeData = new MeshObjectSnapshot();
            before.ApplyTo(_meshContext);
            beforeData = MeshObjectSnapshot.Capture(_meshContext);

            after.ApplyTo(_meshContext);
            MeshObjectSnapshot afterData = MeshObjectSnapshot.Capture(_meshContext);

            MeshSnapshotRecord record = new MeshSnapshotRecord(beforeData, afterData);
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        // === 面操作の記録（新機能） ===

        /// <summary>
        /// 面追加を記録
        /// </summary>
        public void RecordFaceAdd(Face face, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new FaceAddRecord(face, index);
            {
                string __dbgDesc = "Add Face";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 面削除を記録
        /// </summary>
        public void RecordFaceDelete(Face face, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new FaceDeleteRecord(face, index);
            {
                string __dbgDesc = "Delete Face";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点追加を記録
        /// </summary>
        public void RecordVertexAdd(Vertex vertex, int index)
        {
            _vertexEditStack.EndGroup();  // グループをリセットして独立した操作に
            var record = new VertexAddRecord(vertex, index);
            {
                string __dbgDesc = "Add Vertex";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 面追加操作を記録（頂点と面をまとめて1つの操作として）
        /// </summary>
        public void RecordAddFaceOperation(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            
            var record = new AddFaceOperationRecord(face, faceIndex, addedVertices);
            string desc;
            if (face != null)
            {
                desc = addedVertices.Count > 0 
                    ? $"Add Face (+{addedVertices.Count} vertices)" 
                    : "Add Face";
            }
            else
            {
                desc = $"Add {addedVertices.Count} Vertices";
            }
            {
                string __dbgDesc = desc;
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// ナイフ切断操作を記録
        /// </summary>
        public void RecordKnifeCut(
            int originalFaceIndex,
            Face originalFace,
            Face newFace1,
            int newFace2Index,
            Face newFace2,
            List<(int Index, Vertex Vertex)> addedVertices)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録
            
            var record = new KnifeCutOperationRecord(
                originalFaceIndex,
                originalFace,
                newFace1,
                newFace2Index,
                newFace2,
                addedVertices
            );
            {
                string __dbgDesc = "Knife Cut";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点削除操作を記録（スナップショット方式）
        /// </summary>
        public void RecordDeleteVertices(MeshObjectSnapshot before, MeshObjectSnapshot after)
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            {
                string __dbgDesc = "Delete Vertices";
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }

        /// <summary>
        /// メッシュトポロジー変更を記録（スナップショット方式）
        /// ナイフツールの複数面切断、面マージなど汎用的に使用
        /// </summary>
        public void RecordMeshTopologyChange(MeshObjectSnapshot before, MeshObjectSnapshot after, string description = "UnityMesh Topology Change")
        {
            _vertexEditStack.EndGroup();  // 独立した操作として記録

            MeshSnapshotRecord record = new MeshSnapshotRecord(before, after);
            {
                string __dbgDesc = description;
                PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                _vertexEditStack.Record(record, __dbgDesc);
            }
            FocusVertexEdit();
        }
        /// <summary>
        /// トポロジー変更前にスナップショットを取得（後方互換）
        /// 現在のMeshObjectのスナップショットを取得
        /// </summary>
        public MeshSnapshot CaptureSnapshot()
        {
            return MeshSnapshot.Capture(_meshContext);
        }

        // === 表示操作の記録 ===

        /// <summary>
        /// カメラ変更を記録
        /// </summary>
        public void RecordViewChange(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget)
        {
            RecordViewChangeInternal(oldRotX, oldRotY, oldDist, oldTarget,
                newRotX, newRotY, newDist, newTarget);
        }

        /// <summary>
        /// カメラ変更を記録（内部用・キューから呼ばれる）
        /// Capture()を使用して完全なスナップショットを取得し、カメラ値のみ上書き
        /// </summary>
        internal void RecordViewChangeInternal(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget)
        {
            // カメラUndoが無効なら記録スキップ
            if (!_editorStateContext.UndoCameraChanges) return;
            //Debug.Log($"[RecordViewChangeInternal] Recording to EditorStateStack. Before Focus={_mainGroup.FocusedChildId}");
            
            // 完全なスナップショットを取得してカメラ値のみ上書き
            EditorStateSnapshot before = _editorStateContext.Capture();
            before.RotationX = oldRotX;
            before.RotationY = oldRotY;
            before.CameraDistance = oldDist;
            before.CameraTarget = oldTarget;

            EditorStateSnapshot after = _editorStateContext.Capture();
            after.RotationX = newRotX;
            after.RotationY = newRotY;
            after.CameraDistance = newDist;
            after.CameraTarget = newTarget;

            var record = new EditorStateChangeRecord(before, after);
            {
                string __dbgDesc = "Change View";
                PLDiag.UndoRecord("EditorState", __dbgDesc, record);
                _editorStateStack.Record(record, __dbgDesc);
            }
            FocusEditorState();
            
            //Debug.Log($"[RecordViewChangeInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }

        /// <summary>
        /// カメラ変更を記録（WorkPlane連動）
        /// </summary>
        public void RecordViewChangeWithWorkPlane(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
        {
            RecordViewChangeWithWorkPlaneInternal(
                oldRotX, oldRotY, oldDist, oldTarget,
                newRotX, newRotY, newDist, newTarget,
                oldWorkPlane, newWorkPlane);
        }

        /// <summary>
        /// カメラ変更を記録（WorkPlane連動・内部用）
        /// CameraParallelモードでカメラ姿勢に連動してWorkPlane軸もUndo/Redoされる
        /// Capture()を使用して完全なスナップショットを取得し、カメラ値のみ上書き
        /// </summary>
        internal void RecordViewChangeWithWorkPlaneInternal(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
        {
            // カメラUndoが無効なら記録スキップ
            if (!_editorStateContext.UndoCameraChanges) return;
            //Debug.Log($"[RecordViewChangeWithWorkPlaneInternal] Recording to EditorStateStack. Before Focus={_mainGroup.FocusedChildId}");
            
            // 完全なスナップショットを取得してカメラ値のみ上書き
            EditorStateSnapshot before = _editorStateContext.Capture();
            before.RotationX = oldRotX;
            before.RotationY = oldRotY;
            before.CameraDistance = oldDist;
            before.CameraTarget = oldTarget;

            EditorStateSnapshot after = _editorStateContext.Capture();
            after.RotationX = newRotX;
            after.RotationY = newRotY;
            after.CameraDistance = newDist;
            after.CameraTarget = newTarget;

            var record = new EditorStateChangeRecord(before, after, oldWorkPlane, newWorkPlane);
            {
                string __dbgDesc = "Change View";
                PLDiag.UndoRecord("EditorState", __dbgDesc, record);
                _editorStateStack.Record(record, __dbgDesc);
            }
            FocusEditorState();
            
            //Debug.Log($"[RecordViewChangeWithWorkPlaneInternal] After FocusEditorState(). Focus={_mainGroup.FocusedChildId}");
        }
    }
}
