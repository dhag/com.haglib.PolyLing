// SummaryTreeRoot.cs
// IMeshView配列からツリーを構築し、D&D完了時にReorderCommandを送信
// TypedTreeRootの代替。ModelContextへの依存なし。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.MeshListV2
{
    public class SummaryTreeRoot : ITreeRoot<SummaryTreeAdapter>
    {
        private List<SummaryTreeAdapter> _rootItems = new List<SummaryTreeAdapter>();
        private Dictionary<int, SummaryTreeAdapter> _adapterByMasterIndex = new Dictionary<int, SummaryTreeAdapter>();
        /// <summary>一覧から外した行。親の索引 → 外した行の索引。</summary>
        private readonly Dictionary<int, List<int>> _hiddenChildren = new Dictionary<int, List<int>>();
        private int _nextId;

        // 外部コールバック
        public Action OnChanged { get; set; }
        public Action<PanelCommand> SendCommand { get; set; }
        public MeshCategory Category { get; private set; }
        public int ModelIndex { get; set; }

        // ITreeRoot実装
        public List<SummaryTreeAdapter> RootItems => _rootItems;
        public int TotalCount => _adapterByMasterIndex.Count;

        public void OnTreeChanged()
        {
            // D&D完了：新しい順序をコマンドとして送信
            var flatList = TreeViewHelper.Flatten(_rootItems);
            var entryList = new List<ReorderMeshesCommand.ReorderEntry>(flatList.Count);
            foreach (var a in flatList)
            {
                int parentMaster = a.Parent != null ? a.Parent.MasterIndex : -1;
                int depth = a.GetDepth();
                entryList.Add(new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex = a.MasterIndex,
                    NewDepth = depth,
                    NewParentMasterIndex = parentMaster
                });

                // 一覧から外した結果の行は元の直後へ置く。親子はリストの順と深さで決まるので、
                // 元だけが動くと結果が別のオブジェクトの子になる（MeshHierarchyOps.BuildParentIndicesFromDepth）。
                AddHiddenChildren(entryList, a.MasterIndex, depth);
            }
            var entries = entryList.ToArray();
            PLDiag.Cmd($"ReorderMeshes category={Category} modelIndex={ModelIndex} count={entries.Length}");

            SendCommand?.Invoke(new ReorderMeshesCommand(
                ModelIndex, Category, ReorderMeshesCommand.ToEntryValues(entries)));
            OnChanged?.Invoke();
        }

        /// <summary>外した行を親の直後へ足す。多段（外した行の子も外した行）は深さをたどる。</summary>
        private void AddHiddenChildren(List<ReorderMeshesCommand.ReorderEntry> entries, int parent, int parentDepth)
        {
            if (!_hiddenChildren.TryGetValue(parent, out var hidden)) return;
            foreach (int child in hidden)
            {
                entries.Add(new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex = child,
                    NewDepth = parentDepth + 1,
                    NewParentMasterIndex = parent
                });
                AddHiddenChildren(entries, child, parentDepth + 1);
            }
        }

        // ================================================================
        // ツリー構築
        // ================================================================

        /// <param name="meshViews">カテゴリ別IMeshViewリスト</param>
        /// <param name="category">カテゴリ</param>
        /// <param name="excludeMirrorSide">trueならIsMirrorSide=trueを除外</param>
        /// <param name="filterText">名前フィルタ（null/空=フィルタなし）</param>
        /// <param name="hiddenRows">一覧から外す行（モディファイアの結果とそのミラー側）。null なら外さない</param>
        public void Build(IReadOnlyList<IMeshView> meshViews, MeshCategory category,
            bool excludeMirrorSide = true, string filterText = null,
            IReadOnlyCollection<int> hiddenRows = null,
            IReadOnlyDictionary<int, int> resultParents = null)
        {
            _rootItems.Clear();
            _adapterByMasterIndex.Clear();
            _hiddenChildren.Clear();
            _nextId = 0;
            Category = category;

            if (meshViews == null) return;

            var hiddenSet = hiddenRows != null ? new HashSet<int>(hiddenRows) : null;
            var allAdapters = new List<SummaryTreeAdapter>();
            for (int i = 0; i < meshViews.Count; i++)
            {
                var v = meshViews[i];
                if (hiddenSet != null && hiddenSet.Contains(v.MasterIndex))
                {
                    if (!_hiddenChildren.TryGetValue(v.HierarchyParentIndex, out var hiddenList))
                        _hiddenChildren[v.HierarchyParentIndex] = hiddenList = new List<int>();
                    hiddenList.Add(v.MasterIndex);
                    continue;
                }
                if (excludeMirrorSide && v.IsMirrorSide) continue;
                if (!string.IsNullOrEmpty(filterText) &&
                    v.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var adapter = new SummaryTreeAdapter(v, _nextId++);
                allAdapters.Add(adapter);
                _adapterByMasterIndex[v.MasterIndex] = adapter;
            }

            if (category == MeshCategory.Drawable || category == MeshCategory.Morph)
                BuildHierarchyFromDepth(allAdapters, resultParents);
            else
                BuildHierarchyFromParentIndex(allAdapters);
        }

        private void BuildHierarchyFromDepth(List<SummaryTreeAdapter> allAdapters,
            IReadOnlyDictionary<int, int> resultParents)
        {
            var parentStack = new Stack<(SummaryTreeAdapter adapter, int depth)>();
            foreach (var adapter in allAdapters)
            {
                // モディファイアの結果は並びによらず元の下に置く（スタックは変えない）。
                if (resultParents != null && resultParents.TryGetValue(adapter.MasterIndex, out int source)
                    && _adapterByMasterIndex.TryGetValue(source, out var sourceAdapter))
                {
                    adapter.Parent = sourceAdapter;
                    sourceAdapter.Children.Add(adapter);
                    continue;
                }

                int depth = adapter.Depth;
                if (depth == 0)
                {
                    _rootItems.Add(adapter);
                    parentStack.Clear();
                    parentStack.Push((adapter, depth));
                }
                else
                {
                    while (parentStack.Count > 0 && parentStack.Peek().depth >= depth)
                        parentStack.Pop();
                    if (parentStack.Count > 0)
                    {
                        var parent = parentStack.Peek().adapter;
                        adapter.Parent = parent;
                        parent.Children.Add(adapter);
                    }
                    else
                    {
                        _rootItems.Add(adapter);
                    }
                    parentStack.Push((adapter, depth));
                }
            }
        }

        private void BuildHierarchyFromParentIndex(List<SummaryTreeAdapter> allAdapters)
        {
            foreach (var adapter in allAdapters)
            {
                int parentMasterIndex = adapter.HierarchyParentIndex;
                if (parentMasterIndex >= 0 && _adapterByMasterIndex.TryGetValue(parentMasterIndex, out var parent))
                {
                    adapter.Parent = parent;
                    parent.Children.Add(adapter);
                }
                else
                {
                    _rootItems.Add(adapter);
                }
            }
        }

        // ================================================================
        // 検索
        // ================================================================

        public SummaryTreeAdapter GetAdapterByMasterIndex(int masterIndex)
        {
            _adapterByMasterIndex.TryGetValue(masterIndex, out var a);
            return a;
        }

        public SummaryTreeAdapter FindById(int id)
        {
            return TreeViewHelper.FindById(_rootItems, id);
        }

        public List<SummaryTreeAdapter> GetAllAdapters()
        {
            return TreeViewHelper.Flatten(_rootItems);
        }
    }
}
