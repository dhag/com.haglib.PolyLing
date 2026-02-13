// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Selection.cs
// 選択変更操作のUndo記録
// Phase 4: SelectionSnapshot に統一（V/E/F/L 全モード対応）

using System.Collections.Generic;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using UnityEngine;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 選択状態変更記録（全モード対応: V/E/F/L）
    /// SelectionSnapshot で完全な選択状態を保存・復元
    /// </summary>
    public class SelectionChangeRecord : MeshUndoRecord
    {
        public SelectionSnapshot OldSnapshot;
        public SelectionSnapshot NewSnapshot;

        // WorkPlane連動（AutoUpdate有効時のみ使用）
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        /// <summary>
        /// 選択変更はLevel 3（選択フラグのみ更新）で済む
        /// </summary>
        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Selection;

        /// <summary>
        /// SelectionSnapshot ベースのコンストラクタ
        /// </summary>
        public SelectionChangeRecord(
            SelectionSnapshot oldSnapshot,
            SelectionSnapshot newSnapshot,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            OldSnapshot = oldSnapshot?.Clone();
            NewSnapshot = newSnapshot?.Clone();
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        /// <summary>
        /// 後方互換: HashSet&lt;int&gt; ベースのコンストラクタ
        /// Vertex/Face のみの旧コードから呼ばれる場合用
        /// </summary>
        public SelectionChangeRecord(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            OldSnapshot = new SelectionSnapshot
            {
                Mode = MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line,
                Vertices = new HashSet<int>(oldVertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(),
                Faces = new HashSet<int>(oldFaces ?? new HashSet<int>()),
                Lines = new HashSet<int>()
            };
            NewSnapshot = new SelectionSnapshot
            {
                Mode = MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line,
                Vertices = new HashSet<int>(newVertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(),
                Faces = new HashSet<int>(newFaces ?? new HashSet<int>()),
                Lines = new HashSet<int>()
            };
            OldWorkPlaneSnapshot = null;
            NewWorkPlaneSnapshot = null;
        }

        /// <summary>
        /// 後方互換: WorkPlane連動付きHashSet&lt;int&gt;コンストラクタ
        /// </summary>
        public SelectionChangeRecord(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
            : this(oldVertices, newVertices, oldFaces, newFaces)
        {
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            // レガシーフィールドも更新（後方互換）
            ctx.SelectedVertices = new HashSet<int>(OldSnapshot?.Vertices ?? new HashSet<int>());

            // SelectionSnapshot を設定して OnUndoRedoPerformed で反映
            ctx.CurrentSelectionSnapshot = OldSnapshot?.Clone();

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public override void Redo(MeshUndoContext ctx)
        {
            ctx.SelectedVertices = new HashSet<int>(NewSnapshot?.Vertices ?? new HashSet<int>());

            ctx.CurrentSelectionSnapshot = NewSnapshot?.Clone();

            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }
}
