// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Selection.cs
// 選択変更操作のUndo記録

using System.Collections.Generic;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using UnityEngine;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // 基本選択変更記録
    // ============================================================

    /// <summary>
    /// 選択状態変更記録（WorkPlane原点連動対応）
    /// </summary>
    public class SelectionChangeRecord : MeshUndoRecord
    {
        public HashSet<int> OldSelectedVertices;
        public HashSet<int> NewSelectedVertices;
        public HashSet<int> OldSelectedFaces;
        public HashSet<int> NewSelectedFaces;

        // WorkPlane連動（AutoUpdate有効時のみ使用）
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        /// <summary>
        /// 選択変更はLevel 3（選択フラグのみ更新）で済む
        /// </summary>
        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Selection;

        public SelectionChangeRecord(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            OldSelectedVertices = new HashSet<int>(oldVertices ?? new HashSet<int>());
            NewSelectedVertices = new HashSet<int>(newVertices ?? new HashSet<int>());
            OldSelectedFaces = new HashSet<int>(oldFaces ?? new HashSet<int>());
            NewSelectedFaces = new HashSet<int>(newFaces ?? new HashSet<int>());
            OldWorkPlaneSnapshot = null;
            NewWorkPlaneSnapshot = null;
        }

        /// <summary>
        /// WorkPlane連動付きコンストラクタ
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
            Debug.Log($"[SelectionChangeRecord.Undo] START. OldSelectedVertices.Count={OldSelectedVertices?.Count ?? -1}");
            ctx.SelectedVertices = new HashSet<int>(OldSelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(OldSelectedFaces);

            // SelectionSnapshot を設定して OnUndoRedoPerformed で反映されるようにする
            ctx.CurrentSelectionSnapshot = new SelectionSnapshot
            {
                Mode = MeshSelectMode.Vertex,  // 基本モードとしてVertex
                Vertices = new HashSet<int>(OldSelectedVertices),
                Edges = new HashSet<VertexPair>(),
                Faces = new HashSet<int>(OldSelectedFaces),
                Lines = new HashSet<int>()
            };

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
            Debug.Log($"[SelectionChangeRecord.Undo] END. ctx.SelectedVertices.Count={ctx.SelectedVertices?.Count ?? -1}");
        }

        public override void Redo(MeshUndoContext ctx)
        {
            ctx.SelectedVertices = new HashSet<int>(NewSelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(NewSelectedFaces);

            // SelectionSnapshot を設定して OnUndoRedoPerformed で反映されるようにする
            ctx.CurrentSelectionSnapshot = new SelectionSnapshot
            {
                Mode = MeshSelectMode.Vertex,  // 基本モードとしてVertex
                Vertices = new HashSet<int>(NewSelectedVertices),
                Edges = new HashSet<VertexPair>(),
                Faces = new HashSet<int>(NewSelectedFaces),
                Lines = new HashSet<int>()
            };

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }

    // ============================================================
    // 拡張選択変更記録（Edge/Line対応）
    // ============================================================

    /// <summary>
    /// 拡張選択変更記録（Edge/Face/Line全モード対応）
    /// SelectionSnapshotを使用して全選択状態を保存
    /// </summary>
    public class ExtendedSelectionChangeRecord : MeshUndoRecord
    {
        // 新選択システムのスナップショット
        public SelectionSnapshot OldSnapshot;
        public SelectionSnapshot NewSnapshot;

        // レガシー互換用（_selectedVertices との同期用）
        public HashSet<int> OldLegacyVertices;
        public HashSet<int> NewLegacyVertices;

        // WorkPlane連動
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        /// <summary>
        /// 選択変更はLevel 3（選択フラグのみ更新）で済む
        /// </summary>
        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Selection;

        public ExtendedSelectionChangeRecord(
            SelectionSnapshot oldSnapshot,
            SelectionSnapshot newSnapshot,
            HashSet<int> oldLegacyVertices = null,
            HashSet<int> newLegacyVertices = null,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            OldSnapshot = oldSnapshot?.Clone();
            NewSnapshot = newSnapshot?.Clone();
            OldLegacyVertices = oldLegacyVertices != null ? new HashSet<int>(oldLegacyVertices) : new HashSet<int>();
            NewLegacyVertices = newLegacyVertices != null ? new HashSet<int>(newLegacyVertices) : new HashSet<int>();
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            // レガシー選択を復元（MeshEditContext用）
            ctx.SelectedVertices = new HashSet<int>(OldLegacyVertices);

            // 拡張選択スナップショットを設定
            ctx.CurrentSelectionSnapshot = OldSnapshot?.Clone();

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public override void Redo(MeshUndoContext ctx)
        {
            // レガシー選択を復元（MeshEditContext用）
            ctx.SelectedVertices = new HashSet<int>(NewLegacyVertices);

            // 拡張選択スナップショットを設定
            ctx.CurrentSelectionSnapshot = NewSnapshot?.Clone();

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }
}
