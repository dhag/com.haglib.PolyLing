// MeshAttributeChangeRecord.cs
// メッシュ選択・順序変更のUndo/Redo記録用クラス
// Note: 属性変更はMeshAttributesBatchChangeRecord（MeshListRecords.cs）に統合

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 複数メッシュの選択変更を記録するレコード
    /// </summary>
    [Serializable]
    public class MeshMultiSelectionChangeRecord : IUndoRecord<ModelContext>
    {
        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>変更前の選択インデックスセット</summary>
        public int[] OldSelectedIndices { get; set; }

        /// <summary>変更後の選択インデックスセット</summary>
        public int[] NewSelectedIndices { get; set; }

        public MeshMultiSelectionChangeRecord() { }

        public MeshMultiSelectionChangeRecord(int[] oldIndices, int[] newIndices)
        {
            OldSelectedIndices = oldIndices ?? Array.Empty<int>();
            NewSelectedIndices = newIndices ?? Array.Empty<int>();
        }

        /// <summary>
        /// Undo実行：以前の選択状態に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            if (context == null) return;

            context.SelectedMeshContextIndices.Clear();
            foreach (var index in OldSelectedIndices)
            {
                if (index >= 0 && index < context.MeshContextCount)
                {
                    context.SelectedMeshContextIndices.Add(index);
                }
            }
        }

        /// <summary>
        /// Redo実行：新しい選択状態を再適用
        /// </summary>
        public void Redo(ModelContext context)
        {
            if (context == null) return;

            context.SelectedMeshContextIndices.Clear();
            foreach (var index in NewSelectedIndices)
            {
                if (index >= 0 && index < context.MeshContextCount)
                {
                    context.SelectedMeshContextIndices.Add(index);
                }
            }
        }

        public override string ToString()
        {
            return $"MeshMultiSelectionChange: [{string.Join(",", OldSelectedIndices)}] → [{string.Join(",", NewSelectedIndices)}]";
        }
    }

    /// <summary>
    /// メッシュの順序・階層変更を記録するレコード（D&D用）
    /// </summary>
    [Serializable]
    public class MeshReorderChangeRecord : IUndoRecord<ModelContext>
    {
        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>変更前のMeshContext順序（オブジェクト参照）</summary>
        public List<MeshContext> OldOrderedList { get; set; }

        /// <summary>変更後のMeshContext順序（オブジェクト参照）</summary>
        public List<MeshContext> NewOrderedList { get; set; }

        /// <summary>変更前の親マップ（MeshContext → 親MeshContext, null=ルート）</summary>
        public Dictionary<MeshContext, MeshContext> OldParentMap { get; set; }

        /// <summary>変更後の親マップ</summary>
        public Dictionary<MeshContext, MeshContext> NewParentMap { get; set; }

        /// <summary>変更前の選択MeshContext</summary>
        public MeshContext OldSelectedMeshContext { get; set; }

        /// <summary>変更後の選択MeshContext</summary>
        public MeshContext NewSelectedMeshContext { get; set; }

        public MeshReorderChangeRecord()
        {
            OldOrderedList = new List<MeshContext>();
            NewOrderedList = new List<MeshContext>();
            OldParentMap = new Dictionary<MeshContext, MeshContext>();
            NewParentMap = new Dictionary<MeshContext, MeshContext>();
        }

        /// <summary>
        /// Undo実行：以前の状態に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            ApplyState(context, OldOrderedList, OldParentMap, OldSelectedMeshContext);
        }

        /// <summary>
        /// Redo実行：新しい状態を再適用
        /// </summary>
        public void Redo(ModelContext context)
        {
            ApplyState(context, NewOrderedList, NewParentMap, NewSelectedMeshContext);
        }

        /// <summary>
        /// 状態を適用
        /// </summary>
        private void ApplyState(ModelContext context, List<MeshContext> orderedList, 
                               Dictionary<MeshContext, MeshContext> parentMap,
                               MeshContext selectedMeshContext)
        {
            if (context == null || orderedList == null || orderedList.Count == 0) return;

            // 1. リストを並べ替え（同じオブジェクト参照を使用）
            context.MeshContextList.Clear();
            context.MeshContextList.AddRange(orderedList);

            // 2. 親子関係を復元
            if (parentMap != null)
            {
                for (int i = 0; i < context.MeshContextCount; i++)
                {
                    var mc = context.GetMeshContext(i);
                    if (mc != null && parentMap.TryGetValue(mc, out var parent))
                    {
                        // 親のインデックスを取得（nullの場合は-1）
                        int parentIndex = parent != null ? context.MeshContextList.IndexOf(parent) : -1;
                        mc.HierarchyParentIndex = parentIndex;
                    }
                }
            }

            // 3. 選択を復元（オブジェクト参照から新しいインデックスを取得）
            if (selectedMeshContext != null)
            {
                int newIndex = context.MeshContextList.IndexOf(selectedMeshContext);
                if (newIndex >= 0)
                {
                    context.SelectedMeshContextIndices = new HashSet<int> { newIndex };
                }
            }
            context.ValidateSelection();

            // 4. フォーカス切り替え
            context.OnFocusMeshListRequested?.Invoke();

            // 5. 変更通知
            context.OnListChanged?.Invoke();

            // 6. CG再構築（順序変更はトポロジー変更）
            context.OnReorderCompleted?.Invoke();

            // 7. VertexEditStackクリア（古い頂点記録は無効）
            context.OnVertexEditStackClearRequested?.Invoke();
        }

        public override string ToString()
        {
            return $"MeshReorderChange: {OldOrderedList?.Count ?? 0} items";
        }
    }
}
