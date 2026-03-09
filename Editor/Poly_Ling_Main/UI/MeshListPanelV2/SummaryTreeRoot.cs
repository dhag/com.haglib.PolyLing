// SummaryTreeRoot.cs
// IMeshView配列からツリーを構築し、D&D完了時にReorderCommandを送信
// TypedTreeRootの代替。ModelContextへの依存なし。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.MeshListV2
{
    public class SummaryTreeRoot : ITreeRoot<SummaryTreeAdapter>
    {
        private List<SummaryTreeAdapter> _rootItems = new List<SummaryTreeAdapter>();
        private Dictionary<int, SummaryTreeAdapter> _adapterByMasterIndex = new Dictionary<int, SummaryTreeAdapter>();
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
            var entries = new ReorderMeshesCommand.ReorderEntry[flatList.Count];
            for (int i = 0; i < flatList.Count; i++)
            {
                var a = flatList[i];
                int parentMaster = a.Parent != null ? a.Parent.MasterIndex : -1;
                entries[i] = new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex = a.MasterIndex,
                    NewDepth = a.GetDepth(),
                    NewParentMasterIndex = parentMaster
                };
            }
            SendCommand?.Invoke(new ReorderMeshesCommand(ModelIndex, Category, entries));
            OnChanged?.Invoke();
        }

        // ================================================================
        // ツリー構築
        // ================================================================

        /// <param name="meshViews">カテゴリ別IMeshViewリスト</param>
        /// <param name="category">カテゴリ</param>
        /// <param name="excludeMirrorSide">trueならIsMirrorSide=trueを除外</param>
        /// <param name="filterText">名前フィルタ（null/空=フィルタなし）</param>
        public void Build(IReadOnlyList<IMeshView> meshViews, MeshCategory category,
            bool excludeMirrorSide = true, string filterText = null)
        {
            _rootItems.Clear();
            _adapterByMasterIndex.Clear();
            _nextId = 0;
            Category = category;

            if (meshViews == null) return;

            var allAdapters = new List<SummaryTreeAdapter>();
            for (int i = 0; i < meshViews.Count; i++)
            {
                var v = meshViews[i];
                if (excludeMirrorSide && v.IsMirrorSide) continue;
                if (!string.IsNullOrEmpty(filterText) &&
                    v.Name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var adapter = new SummaryTreeAdapter(v, _nextId++);
                allAdapters.Add(adapter);
                _adapterByMasterIndex[v.MasterIndex] = adapter;
            }

            if (category == MeshCategory.Drawable || category == MeshCategory.Morph)
                BuildHierarchyFromDepth(allAdapters);
            else
                BuildHierarchyFromParentIndex(allAdapters);
        }

        private void BuildHierarchyFromDepth(List<SummaryTreeAdapter> allAdapters)
        {
            var parentStack = new Stack<(SummaryTreeAdapter adapter, int depth)>();
            foreach (var adapter in allAdapters)
            {
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
