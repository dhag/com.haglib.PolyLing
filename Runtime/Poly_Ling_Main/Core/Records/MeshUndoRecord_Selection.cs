// Assets/Editor/UndoSystem/MeshEditor/Records/MeshUndoRecord_Selection.cs
// 選択変更操作のUndo記録
// Phase 4: SelectionSnapshot に統一（V/E/F/L 全モード対応）

using System.Collections.Generic;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 選択状態変更記録（全モード対応: V/E/F/L）
    /// SelectionSnapshot で完全な選択状態を保存・復元
    /// </summary>
    ///
    /// <remarks>
    /// 【適用範囲 — 前提条件】
    ///
    /// 本 Record が扱えるのは「メッシュ 1 個ぶんの選択」だけである。
    /// SelectionSnapshot の Vertices / Faces / Lines はメッシュ内ローカル番号のため、
    /// 複数メッシュぶんを 1 つの Snapshot に入れることはできない。
    ///
    /// 復元先はアプリケーション層で ActiveMeshContext.Selection に固定されている
    /// （PolyLingPlayerViewerCore の OnUndoRedoPerformed）。
    /// したがって本 Record を使ってよいのは、記録側も ActiveMeshContext だけを
    /// 変更する操作に限る。現状の該当箇所は次の 2 つで、いずれも条件を満たす。
    ///   - AdvancedSelectToolHandler（ToolContext.SelectionState / TopologyCache が
    ///     どちらも ActiveMeshContext 由来）
    ///   - PlayerCommandDispatcher.PartsSetApply（ActiveMeshContext.Selection のみ操作）
    ///
    /// 複数メッシュにまたがって選択を変更する処理は
    /// MultiMeshSelectionChangeRecord を使うこと。
    /// 記録側だけ複数メッシュ化して本 Record を使い続けると、
    /// Undo が先頭メッシュしか戻さず選択状態が食い違う。
    /// </remarks>
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

    /// <summary>
    /// 1 メッシュぶんの選択状態変更（複数メッシュ選択変更記録の要素）。
    /// MeshContextIndex は MeshContextList のインデックス。
    /// </summary>
    public struct MeshSelectionEntry
    {
        public int MeshContextIndex;
        public SelectionSnapshot Old;
        public SelectionSnapshot New;
    }

    /// <summary>
    /// 複数メッシュ対応の選択状態変更記録。
    ///
    /// SelectionSnapshot はメッシュ内ローカル番号を持つため 1 メッシュしか表せない。
    /// そこで MeshContextIndex 付きのエントリ配列として保持し、
    /// Record 内ではメッシュ解決を行わず ctx.PendingSelectionEntries に積むだけにする。
    /// 実際の書き戻しは OnUndoRedoPerformed（アプリケーション層）が行う。
    /// MultiMeshVertexMoveRecord と同じ方式。
    ///
    /// 【前提条件】
    /// この方式を崩して Record 内で ctx.MeshObject 等を触ると、
    /// MeshUndoContext.ResolvedMeshContext が先頭メッシュしか返さないため
    /// 2 つ目以降のメッシュが復元されなくなる。
    /// </summary>
    public class MultiMeshSelectionChangeRecord : MeshUndoRecord
    {
        public MeshSelectionEntry[] Entries;

        /// <summary>選択変更はLevel 3（選択フラグのみ更新）で済む</summary>
        public override MeshUpdateLevel RequiredUpdateLevel => MeshUpdateLevel.Selection;

        public MultiMeshSelectionChangeRecord(MeshSelectionEntry[] entries)
        {
            Entries = entries;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            Apply(ctx, isUndo: true);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            Apply(ctx, isUndo: false);
        }

        private void Apply(MeshUndoContext ctx, bool isUndo)
        {
            if (Entries == null || Entries.Length == 0)
                return;

            var applied = new MeshSelectionEntry[Entries.Length];
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                // Undo は Old、Redo は New を New 側に載せて渡す。
                // 受け側は New だけを見て復元すればよい。
                applied[i] = new MeshSelectionEntry
                {
                    MeshContextIndex = e.MeshContextIndex,
                    Old              = isUndo ? e.New : e.Old,
                    New              = isUndo ? e.Old : e.New,
                };
            }

            ctx.PendingSelectionEntries = applied;

            ctx.DirtyMeshIndices.Clear();
            for (int i = 0; i < Entries.Length; i++)
                ctx.DirtyMeshIndices.Add(Entries[i].MeshContextIndex);
        }
    }
}
